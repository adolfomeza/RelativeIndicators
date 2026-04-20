#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class RelativeVolumeProfile
	{
		#region Composite / Split Data Structures

		private class CompositeInfo
		{
			public List<VolumeProfileSession> OriginalProfiles;
			public VolumeProfileSession MergedSession;
		}

		/// <summary>
		/// Recipe for a TPO split: identifies original profile by StartTime + splitPeriod.
		/// Applied before merges during restore.
		/// </summary>
		private class SplitRecipe
		{
			public DateTime OriginalStartTime;
			public int SplitPeriod;
		}

		#endregion

		#region Context Menu Fields

		private ChartControl _contextMenuChartControl;
		private Point _lastRightClickPoint;

		// Hit testing cache — populated during each OnRender
		private Dictionary<VolumeProfileSession, SharpDX.RectangleF> _profileBounds;

		// Composite / Split tracking
		private List<CompositeInfo> _composites;
		private List<SplitRecipe> _splitRecipes;
		private VolumeProfileSession _clickedProfile;
		private int _splitAtPeriodIndex = -1; // periodo TPO donde cortar al dividir

		// Persistence
		private bool _compositesRestored;
		private string _compositeFilePath;

		#endregion

		#region Setup / Cleanup

		private void SetupContextMenu()
		{
			if (ChartControl == null) return;

			// Clean up previous subscriptions if they exist
			if (_contextMenuChartControl != null)
				CleanupContextMenu();

			_contextMenuChartControl = ChartControl;

			// Unsub first to prevent duplicates (pattern from RelativeNewsFilter.cs)
			_contextMenuChartControl.PreviewMouseRightButtonDown -= OnChartPreviewRightClick;
			_contextMenuChartControl.ContextMenuOpening -= OnContextMenuOpening;
			_contextMenuChartControl.ContextMenuClosing -= OnContextMenuClosing;

			_contextMenuChartControl.PreviewMouseRightButtonDown += OnChartPreviewRightClick;
			_contextMenuChartControl.ContextMenuOpening += OnContextMenuOpening;
			_contextMenuChartControl.ContextMenuClosing += OnContextMenuClosing;

			if (_profileBounds == null)
				_profileBounds = new Dictionary<VolumeProfileSession, SharpDX.RectangleF>();
			if (_composites == null)
				_composites = new List<CompositeInfo>();
			if (_splitRecipes == null)
				_splitRecipes = new List<SplitRecipe>();

			if (ShowDebugLogs)
				Print("RelativeVolumeProfile: Context menu setup OK");
		}

		private void CleanupContextMenu()
		{
			if (_contextMenuChartControl != null)
			{
				// Remove any leftover menu items before unsubscribing
				RemoveAllCustomMenuItems();

				_contextMenuChartControl.PreviewMouseRightButtonDown -= OnChartPreviewRightClick;
				_contextMenuChartControl.ContextMenuOpening -= OnContextMenuOpening;
				_contextMenuChartControl.ContextMenuClosing -= OnContextMenuClosing;
				_contextMenuChartControl = null;
			}

			_profileBounds = null;
			_composites = null;
			_splitRecipes = null;
			_clickedProfile = null;
			_compositeFilePath = null;
		}

		#endregion

		#region Mouse / Menu Event Handlers

		private void OnChartPreviewRightClick(object sender, MouseButtonEventArgs e)
		{
			if (_contextMenuChartControl != null)
				_lastRightClickPoint = e.GetPosition(_contextMenuChartControl);

			_clickedProfile = null;
		}

		private void OnContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
		{
			if (_contextMenuChartControl?.ContextMenu == null) return;

			// Remove ALL our items first — guarantees zero duplicates
			RemoveAllCustomMenuItems();

			int insertIdx = 0;

			// TPO view toggle: always available when in TPO mode
			if (ProfileType == ProfileDataType.TPO)
			{
				string header = _tpoViewMode == TpoViewMode.Compact
					? "TPO Vista: Extendida"
					: "TPO Vista: Compacta";

				var mi = CreateTaggedMenuItem(header, OnTpoViewToggleClick);
				_contextMenuChartControl.ContextMenu.Items.Insert(insertIdx++, mi);
			}

			if (_profileBounds == null || _profileBounds.Count == 0) return;

			// Hit test against cached profile bounds
			double mouseX = _lastRightClickPoint.X;
			double mouseY = _lastRightClickPoint.Y;

			_clickedProfile = null;
			float tolerance = 10f;

			foreach (var kvp in _profileBounds)
			{
				SharpDX.RectangleF bounds = kvp.Value;
				if (mouseX >= bounds.Left - tolerance && mouseX <= bounds.Right + tolerance &&
					mouseY >= bounds.Top - tolerance && mouseY <= bounds.Bottom + tolerance)
				{
					_clickedProfile = kvp.Key;
					break;
				}
			}

			if (_clickedProfile == null) return;

			bool isComposite = IsComposite(_clickedProfile);
			VolumeProfileSession rightProfile = FindAdjacentProfile(_clickedProfile, +1);
			VolumeProfileSession leftProfile  = FindAdjacentProfile(_clickedProfile, -1);

			// Show merge-right option
			if (rightProfile != null)
			{
				var mi = CreateTaggedMenuItem("Fusionar con perfil derecho", OnMergeRightClick);
				_contextMenuChartControl.ContextMenu.Items.Insert(insertIdx++, mi);
			}

			// Show merge-left option
			if (leftProfile != null)
			{
				var mi = CreateTaggedMenuItem("Fusionar con perfil izquierdo", OnMergeLeftClick);
				_contextMenuChartControl.ContextMenu.Items.Insert(insertIdx++, mi);
			}

			// Show unmerge option: only for composites
			if (isComposite)
			{
				var mi = CreateTaggedMenuItem("Desfusionar", OnUnmergeClick);
				_contextMenuChartControl.ContextMenu.Items.Insert(insertIdx++, mi);
			}

			// Show split TPO option: only in TPO extended view, when profile has >=2 periods
			if (ProfileType == ProfileDataType.TPO && _tpoViewMode == TpoViewMode.Extended
				&& _clickedProfile != null && _clickedProfile.TpoPeriodBarMap != null
				&& _clickedProfile.TpoPeriodBarMap.Count >= 2)
			{
				int splitPeriod = FindTpoPeriodAtMouseX(mouseX, _clickedProfile);
				if (splitPeriod >= 0)
				{
					_splitAtPeriodIndex = splitPeriod;
					char splitLetter = splitPeriod < 26 ? (char)('A' + splitPeriod) : (char)('a' + (splitPeriod - 26));
					var mi = CreateTaggedMenuItem("Dividir TPO aquí (antes de " + splitLetter + ")", OnSplitTpoClick);
					_contextMenuChartControl.ContextMenu.Items.Insert(insertIdx, mi);
				}
			}
		}

		private void OnContextMenuClosing(object sender, System.Windows.Controls.ContextMenuEventArgs e)
		{
			RemoveAllCustomMenuItems();
		}

		private const string MENU_TAG = "RVP_ctx";

		/// <summary>
		/// Creates a fresh MenuItem tagged with MENU_TAG. Ephemeral — created on open, removed on close.
		/// </summary>
		private System.Windows.Controls.MenuItem CreateTaggedMenuItem(string header, RoutedEventHandler clickHandler)
		{
			var mi = new System.Windows.Controls.MenuItem
			{
				Header = header,
				Tag = MENU_TAG
			};
			mi.Click += clickHandler;
			return mi;
		}

		private void RemoveAllCustomMenuItems()
		{
			if (_contextMenuChartControl?.ContextMenu == null) return;
			var items = _contextMenuChartControl.ContextMenu.Items;

			for (int i = items.Count - 1; i >= 0; i--)
			{
				var mi = items[i] as System.Windows.Controls.MenuItem;
				if (mi != null && mi.Tag as string == MENU_TAG)
				{
					// Unhook click handler to avoid leaks, then remove
					mi.Click -= OnTpoViewToggleClick;
					mi.Click -= OnMergeRightClick;
					mi.Click -= OnMergeLeftClick;
					mi.Click -= OnUnmergeClick;
					mi.Click -= OnSplitTpoClick;
					items.RemoveAt(i);
				}
			}
		}

		#endregion

		#region Merge / Unmerge Logic

		private void OnMergeRightClick(object sender, RoutedEventArgs e)
		{
			if (_clickedProfile == null || _allProfiles == null) return;
			VolumeProfileSession neighbor = FindAdjacentProfile(_clickedProfile, +1);
			if (neighbor == null) return;
			ExecuteMerge(_clickedProfile, neighbor);
		}

		private void OnMergeLeftClick(object sender, RoutedEventArgs e)
		{
			if (_clickedProfile == null || _allProfiles == null) return;
			VolumeProfileSession neighbor = FindAdjacentProfile(_clickedProfile, -1);
			if (neighbor == null) return;
			// Left neighbor is the "anchor" — merge left into clicked
			ExecuteMerge(neighbor, _clickedProfile);
		}

		/// <summary>
		/// Merges leftProfile with rightProfile. leftProfile position is kept in _allProfiles.
		/// Supports chain merging when either profile is already a composite.
		/// </summary>
		private void ExecuteMerge(VolumeProfileSession leftProfile, VolumeProfileSession rightProfile)
		{

			// Collect originals from both sides (expand composites)
			List<VolumeProfileSession> originals = new List<VolumeProfileSession>();

			CompositeInfo leftComposite = _composites?.FirstOrDefault(c => c.MergedSession == leftProfile);
			if (leftComposite != null)
			{
				originals.AddRange(leftComposite.OriginalProfiles);
				_composites.Remove(leftComposite);
			}
			else
			{
				originals.Add(leftProfile);
			}

			CompositeInfo rightComposite = _composites?.FirstOrDefault(c => c.MergedSession == rightProfile);
			if (rightComposite != null)
			{
				originals.AddRange(rightComposite.OriginalProfiles);
				_composites.Remove(rightComposite);
			}
			else
			{
				originals.Add(rightProfile);
			}

			// Create merged session
			VolumeProfileSession merged = MergeMultipleProfiles(originals);
			if (merged == null) return;

			// If merging the active profile, stop it from receiving new data
			bool mergingActive = leftProfile.IsActive || rightProfile.IsActive;
			if (mergingActive)
				_activeProfile = null;

			// Replace in _allProfiles: keep leftProfile's position, remove rightProfile
			int leftIdx  = _allProfiles.IndexOf(leftProfile);
			int rightIdx = _allProfiles.IndexOf(rightProfile);

			if (leftIdx >= 0 && rightIdx >= 0)
			{
				_allProfiles.Remove(rightProfile);
				// After removal, leftIdx may have shifted if rightIdx < leftIdx
				leftIdx = _allProfiles.IndexOf(leftProfile);
				if (leftIdx >= 0)
					_allProfiles[leftIdx] = merged;
			}

			// Track composite
			if (_composites == null)
				_composites = new List<CompositeInfo>();

			_composites.Add(new CompositeInfo
			{
				OriginalProfiles = originals,
				MergedSession = merged
			});

			if (ShowDebugLogs)
				Print("RelativeVolumeProfile: Merged " + originals.Count + " profiles | POC: " + merged.POC
					+ " | VAH: " + merged.VAH + " | VAL: " + merged.VAL);

			SaveAllRecipes();
			ForceRefresh();
		}

		private void OnUnmergeClick(object sender, RoutedEventArgs e)
		{
			if (_clickedProfile == null || _allProfiles == null || _composites == null) return;

			CompositeInfo composite = _composites.FirstOrDefault(c => c.MergedSession == _clickedProfile);
			if (composite == null) return;

			int compositeIdx = _allProfiles.IndexOf(_clickedProfile);
			if (compositeIdx < 0) return;

			// Remove composite, insert originals back in order
			_allProfiles.RemoveAt(compositeIdx);
			for (int i = 0; i < composite.OriginalProfiles.Count; i++)
				_allProfiles.Insert(compositeIdx + i, composite.OriginalProfiles[i]);

			// Restore _activeProfile if one of the originals was active
			var activeOriginal = composite.OriginalProfiles.FirstOrDefault(p => p.IsActive);
			if (activeOriginal != null)
				_activeProfile = activeOriginal;

			_composites.Remove(composite);

			if (ShowDebugLogs)
				Print("RelativeVolumeProfile: Unmerged " + composite.OriginalProfiles.Count + " profiles");

			SaveAllRecipes();
			ForceRefresh();
		}

		private void OnTpoViewToggleClick(object sender, RoutedEventArgs e)
		{
			_tpoViewMode = _tpoViewMode == TpoViewMode.Compact
				? TpoViewMode.Extended
				: TpoViewMode.Compact;

			if (ShowDebugLogs)
				Print("RelativeVolumeProfile: TPO view → " + _tpoViewMode);

			ForceRefresh();
		}

		private void OnSplitTpoClick(object sender, RoutedEventArgs e)
		{
			if (_clickedProfile == null || _splitAtPeriodIndex < 0) return;
			SplitTPOAtPeriod(_clickedProfile, _splitAtPeriodIndex);
		}

		/// <summary>
		/// Given a mouse X coordinate and a TPO profile, finds which TPO period
		/// the user clicked on by comparing against TpoPeriodBarMap bar positions.
		/// Returns the periodIndex that should become the FIRST period of the RIGHT half.
		/// Returns -1 if no valid split point found.
		/// </summary>
		private int FindTpoPeriodAtMouseX(double mouseX, VolumeProfileSession profile)
		{
			if (profile.TpoPeriodBarMap == null || profile.TpoPeriodBarMap.Count < 2)
				return -1;
			if (ChartBars == null || _contextMenuChartControl == null) return -1;

			float barDist = ChartControl.Properties.BarDistance;

			// Convert mouse X to approximate bar index
			// Use the reference point of a known visible bar
			int refBar = Math.Max(ChartBars.FromIndex, Math.Min(ChartBars.ToIndex, ChartBars.FromIndex));
			float refX = ChartControl.GetXByBarIndex(ChartBars, refBar);
			int approxBar = refBar + (int)Math.Round((mouseX - refX) / barDist);

			// Find the closest period to this bar index
			var sortedPeriods = new List<int>(profile.TpoPeriodBarMap.Keys);
			sortedPeriods.Sort();

			int closestPeriod = -1;
			int closestDist = int.MaxValue;

			foreach (int pi in sortedPeriods)
			{
				int piBar = profile.TpoPeriodBarMap[pi];
				int dist = Math.Abs(piBar - approxBar);
				if (dist < closestDist)
				{
					closestDist = dist;
					closestPeriod = pi;
				}
			}

			if (closestPeriod < 0) return -1;

			// The split point: the clicked period becomes the first of the RIGHT half.
			// But if the user clicked on the very first period, there's nothing to split off on the left.
			int closestIdx = sortedPeriods.IndexOf(closestPeriod);
			if (closestIdx <= 0) return -1; // Can't split at the first period

			return closestPeriod;
		}

		/// <summary>
		/// User-initiated split: splits TPO profile and persists the recipe.
		/// </summary>
		private void SplitTPOAtPeriod(VolumeProfileSession profile, int splitPeriod)
		{
			if (profile == null || profile.TpoPeriodBarMap == null) return;

			DateTime originalStartTime = profile.StartTime;

			// If this was a composite, remove the composite entry first
			if (_composites != null)
			{
				var comp = _composites.FirstOrDefault(c => c.MergedSession == profile);
				if (comp != null)
					_composites.Remove(comp);
			}

			// Execute the split
			SplitTPOAtPeriodInternal(profile, splitPeriod);

			// Track split recipe for persistence
			if (_splitRecipes == null)
				_splitRecipes = new List<SplitRecipe>();
			_splitRecipes.Add(new SplitRecipe
			{
				OriginalStartTime = originalStartTime,
				SplitPeriod = splitPeriod
			});

			if (ShowDebugLogs)
			{
				char splitChar = splitPeriod < 26 ? (char)('A' + splitPeriod) : (char)('a' + (splitPeriod - 26));
				Print("RelativeVolumeProfile: Split TPO at period " + splitChar);
			}

			SaveAllRecipes();
			ForceRefresh();
		}

		private VolumeProfileSession MergeMultipleProfiles(List<VolumeProfileSession> profiles)
		{
			if (profiles == null || profiles.Count == 0) return null;

			bool isTPO = ProfileType == ProfileDataType.TPO;

			var merged = new VolumeProfileSession
			{
				StartTime        = profiles[0].StartTime,
				EndTime          = profiles[profiles.Count - 1].EndTime,
				StartBarIdx      = profiles.Min(p => p.StartBarIdx),
				EndBarIdx        = profiles.Max(p => p.EndBarIdx),
				LastVolumeBarIdx = profiles.Max(p => p.LastVolumeBarIdx),
				IsActive         = false,
				Levels           = new Dictionary<long, VolumeLevelData>(),
				TotalVolume      = 0,
				TickSize         = profiles[0].TickSize,
				TicksPerLevel    = profiles[0].TicksPerLevel,
				TpoPeriodBarMap  = isTPO ? new Dictionary<int, int>() : null
			};

			// For TPO merge: each profile's periods get offset so they don't collide
			// Profile 0 keeps periods 0..N, Profile 1 gets offset by maxPeriod+1 of profile 0, etc.
			int tpoPeriodOffset = 0;

			foreach (var profile in profiles)
			{
				// Find max period in this profile (for offset calculation)
				int maxPeriodInProfile = -1;

				foreach (var kvp in profile.Levels)
				{
					long key = kvp.Key;
					if (!merged.Levels.ContainsKey(key))
					{
						merged.Levels[key] = new VolumeLevelData
						{
							Price = kvp.Value.Price,
							TpoPeriods = isTPO ? new HashSet<int>() : null
						};
					}

					var mergedLevel = merged.Levels[key];

					if (isTPO && kvp.Value.TpoPeriods != null)
					{
						// TPO: merge period sets with offset
						if (mergedLevel.TpoPeriods == null)
							mergedLevel.TpoPeriods = new HashSet<int>();

						foreach (int pi in kvp.Value.TpoPeriods)
						{
							int offsetPi = pi + tpoPeriodOffset;
							if (mergedLevel.TpoPeriods.Add(offsetPi))
							{
								mergedLevel.Volume += 1;
								merged.TotalVolume += 1;
							}
							if (pi > maxPeriodInProfile)
								maxPeriodInProfile = pi;
						}
					}
					else
					{
						// Volume: simple sum
						mergedLevel.Volume += kvp.Value.Volume;
						merged.TotalVolume += kvp.Value.Volume;
					}
				}

				// Merge TpoPeriodBarMap with offset
				if (isTPO && profile.TpoPeriodBarMap != null && merged.TpoPeriodBarMap != null)
				{
					foreach (var mapKvp in profile.TpoPeriodBarMap)
					{
						int offsetPi = mapKvp.Key + tpoPeriodOffset;
						if (!merged.TpoPeriodBarMap.ContainsKey(offsetPi))
							merged.TpoPeriodBarMap[offsetPi] = mapKvp.Value;

						if (mapKvp.Key > maxPeriodInProfile)
							maxPeriodInProfile = mapKvp.Key;
					}
				}

				// Offset for next profile: starts after this profile's last period
				if (isTPO)
					tpoPeriodOffset += maxPeriodInProfile + 1;
			}

			RecalculateKeyLevels(merged);
			return merged;
		}

		#endregion

		#region Composite Persistence

		/// <summary>
		/// Returns the file path for storing composite recipes.
		/// Uses instrument name to keep composites per-instrument.
		/// </summary>
		private string GetCompositeFilePath()
		{
			if (_compositeFilePath != null) return _compositeFilePath;

			string dir = Path.Combine(
				NinjaTrader.Core.Globals.UserDataDir,
				"RelativeVolumeProfile");

			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			// Use instrument name + session mode for unique file per config
			string safeInstrument = Instrument != null
				? Instrument.FullName.Replace(" ", "_").Replace("/", "_")
				: "Unknown";

			_compositeFilePath = Path.Combine(dir,
				safeInstrument + "_" + SessionMode + "_composites.txt");

			return _compositeFilePath;
		}

		/// <summary>
		/// Saves all recipes (splits + merges) to disk.
		/// Format:
		///   S:StartTime|splitPeriod        (split recipe)
		///   M:StartTime1|StartTime2|...    (merge recipe)
		/// Splits are saved first, merges second (restore order).
		/// </summary>
		private void SaveAllRecipes()
		{
			try
			{
				string filePath = GetCompositeFilePath();

				bool hasSplits = _splitRecipes != null && _splitRecipes.Count > 0;
				bool hasMerges = _composites != null && _composites.Count > 0;

				if (!hasSplits && !hasMerges)
				{
					if (File.Exists(filePath))
						File.Delete(filePath);
					return;
				}

				var lines = new List<string>();

				// Save split recipes first (applied before merges on restore)
				if (hasSplits)
				{
					foreach (var sr in _splitRecipes)
						lines.Add("S:" + sr.OriginalStartTime.ToString("o") + "|" + sr.SplitPeriod);
				}

				// Save merge recipes: M:StartTime@StartBarIdx|StartTime@StartBarIdx|...
				if (hasMerges)
				{
					foreach (var composite in _composites)
					{
						if (composite.OriginalProfiles == null || composite.OriginalProfiles.Count == 0)
							continue;

						var entries = composite.OriginalProfiles
							.Select(p => p.StartTime.ToString("o") + "@" + p.StartBarIdx)
							.ToArray();

						lines.Add("M:" + string.Join("|", entries));
					}
				}

				File.WriteAllLines(filePath, lines);

				if (ShowDebugLogs)
					Print("RelativeVolumeProfile: Saved " + lines.Count + " recipes to " + filePath);
			}
			catch (Exception ex)
			{
				Print("RelativeVolumeProfile: Error saving recipes — " + ex.Message);
			}
		}

		/// <summary>
		/// Merge recipe entry: StartTime + optional StartBarIdx for disambiguation.
		/// </summary>
		private struct MergeRecipeEntry
		{
			public DateTime StartTime;
			public int StartBarIdx; // -1 if not available (legacy format)
		}

		/// <summary>
		/// Loads all recipes from disk. Returns split recipes and merge recipes separately.
		/// </summary>
		private void LoadAllRecipes(out List<SplitRecipe> splits, out List<List<MergeRecipeEntry>> merges)
		{
			splits = new List<SplitRecipe>();
			merges = new List<List<MergeRecipeEntry>>();

			try
			{
				string filePath = GetCompositeFilePath();
				if (!File.Exists(filePath)) return;

				string[] lines = File.ReadAllLines(filePath);
				foreach (string line in lines)
				{
					if (string.IsNullOrWhiteSpace(line)) continue;

					if (line.StartsWith("S:"))
					{
						// Split recipe: S:StartTime|splitPeriod
						string data = line.Substring(2);
						string[] parts = data.Split('|');
						if (parts.Length >= 2)
						{
							DateTime dt;
							int period;
							if (DateTime.TryParse(parts[0].Trim(), null,
								System.Globalization.DateTimeStyles.RoundtripKind, out dt)
								&& int.TryParse(parts[1].Trim(), out period))
							{
								splits.Add(new SplitRecipe { OriginalStartTime = dt, SplitPeriod = period });
							}
						}
					}
					else
					{
						// Merge recipe: M:Time@BarIdx|Time@BarIdx|... or legacy Time|Time|...
						string data = line.StartsWith("M:") ? line.Substring(2) : line;
						string[] parts = data.Split('|');
						var entries = new List<MergeRecipeEntry>();

						foreach (string part in parts)
						{
							string trimmed = part.Trim();
							int atIdx = trimmed.IndexOf('@');

							DateTime dt;
							int barIdx = -1;

							if (atIdx > 0)
							{
								// New format: Time@BarIdx
								string timePart = trimmed.Substring(0, atIdx);
								string barPart = trimmed.Substring(atIdx + 1);

								if (DateTime.TryParse(timePart, null,
									System.Globalization.DateTimeStyles.RoundtripKind, out dt))
								{
									int.TryParse(barPart, out barIdx);
									entries.Add(new MergeRecipeEntry { StartTime = dt, StartBarIdx = barIdx });
								}
							}
							else
							{
								// Legacy format: just Time
								if (DateTime.TryParse(trimmed, null,
									System.Globalization.DateTimeStyles.RoundtripKind, out dt))
								{
									entries.Add(new MergeRecipeEntry { StartTime = dt, StartBarIdx = -1 });
								}
							}
						}

						if (entries.Count >= 2)
							merges.Add(entries);
					}
				}

				if (ShowDebugLogs)
					Print("RelativeVolumeProfile: Loaded " + splits.Count + " split + " + merges.Count + " merge recipes");
			}
			catch (Exception ex)
			{
				Print("RelativeVolumeProfile: Error loading recipes — " + ex.Message);
			}
		}

		/// <summary>
		/// Attempts to restore splits and composites from saved recipes.
		/// Called once after all historical profiles have been built.
		/// Order: splits first (to create the split profiles), then merges.
		/// </summary>
		private void RestoreComposites()
		{
			if (_compositesRestored) return;
			_compositesRestored = true;

			if (_allProfiles == null || _allProfiles.Count == 0) return;

			List<SplitRecipe> splitRecipes;
			List<List<MergeRecipeEntry>> mergeRecipes;
			LoadAllRecipes(out splitRecipes, out mergeRecipes);

			if (splitRecipes.Count == 0 && mergeRecipes.Count == 0) return;

			if (_composites == null)
				_composites = new List<CompositeInfo>();
			if (_splitRecipes == null)
				_splitRecipes = new List<SplitRecipe>();

			int restoredSplits = 0;
			int restoredMerges = 0;

			// === Phase 1: Apply splits ===
			foreach (var sr in splitRecipes)
			{
				// Find the profile matching the original StartTime
				VolumeProfileSession target = _allProfiles
					.FirstOrDefault(p => Math.Abs((p.StartTime - sr.OriginalStartTime).TotalMinutes) < 2);

				if (target == null) continue;

				// Only split TPO profiles with the required period data
				if (target.TpoPeriodBarMap == null || target.TpoPeriodBarMap.Count < 2) continue;

				// Verify the split period exists in this profile
				bool hasPeriodsBefore = false;
				bool hasPeriodsAtOrAfter = false;
				foreach (int pi in target.TpoPeriodBarMap.Keys)
				{
					if (pi < sr.SplitPeriod) hasPeriodsBefore = true;
					if (pi >= sr.SplitPeriod) hasPeriodsAtOrAfter = true;
				}
				if (!hasPeriodsBefore || !hasPeriodsAtOrAfter) continue;

				// Execute the split (this modifies _allProfiles in-place and adds to _splitRecipes)
				// But we don't want to re-add to _splitRecipes since we're restoring, so do it manually
				SplitTPOAtPeriodInternal(target, sr.SplitPeriod);
				_splitRecipes.Add(sr);
				restoredSplits++;
			}

			// === Phase 2: Apply merges ===
			foreach (var recipe in mergeRecipes)
			{
				var matchedProfiles = new List<VolumeProfileSession>();

				foreach (var entry in recipe)
				{
					// After splits, there may be multiple profiles with the same StartTime.
					// Use StartBarIdx (if available) to disambiguate between split halves.
					VolumeProfileSession match;
					if (entry.StartBarIdx >= 0)
					{
						// Prefer matching by both StartTime and StartBarIdx (tolerance: 5 bars)
						match = _allProfiles
							.FirstOrDefault(p => Math.Abs((p.StartTime - entry.StartTime).TotalMinutes) < 2
								&& Math.Abs(p.StartBarIdx - entry.StartBarIdx) < 5
								&& !matchedProfiles.Contains(p));

						// Fallback to StartTime only if bar-level match fails
						if (match == null)
							match = _allProfiles
								.FirstOrDefault(p => Math.Abs((p.StartTime - entry.StartTime).TotalMinutes) < 2
									&& !matchedProfiles.Contains(p));
					}
					else
					{
						// Legacy format: match by StartTime only
						match = _allProfiles
							.FirstOrDefault(p => Math.Abs((p.StartTime - entry.StartTime).TotalMinutes) < 2
								&& !matchedProfiles.Contains(p));
					}

					if (match != null)
						matchedProfiles.Add(match);
				}

				if (matchedProfiles.Count < 2) continue;

				matchedProfiles.Sort((a, b) => a.StartBarIdx.CompareTo(b.StartBarIdx));

				VolumeProfileSession merged = MergeMultipleProfiles(matchedProfiles);
				if (merged == null) continue;

				bool mergingActive = matchedProfiles.Any(p => p.IsActive);
				if (mergingActive)
					_activeProfile = null;

				int firstIdx = _allProfiles.IndexOf(matchedProfiles[0]);
				if (firstIdx < 0) continue;

				for (int i = 1; i < matchedProfiles.Count; i++)
					_allProfiles.Remove(matchedProfiles[i]);

				firstIdx = _allProfiles.IndexOf(matchedProfiles[0]);
				if (firstIdx >= 0)
					_allProfiles[firstIdx] = merged;

				_composites.Add(new CompositeInfo
				{
					OriginalProfiles = matchedProfiles,
					MergedSession = merged
				});

				restoredMerges++;
			}

			if (restoredSplits > 0 || restoredMerges > 0)
			{
				if (ShowDebugLogs)
					Print("RelativeVolumeProfile: Restored " + restoredSplits + " splits + " + restoredMerges + " merges");

				ForceRefresh();
			}
		}

		/// <summary>
		/// Internal split that modifies _allProfiles but does NOT add to _splitRecipes or save.
		/// Used by both user-initiated splits and restore.
		/// </summary>
		private void SplitTPOAtPeriodInternal(VolumeProfileSession profile, int splitPeriod)
		{
			if (profile == null || profile.TpoPeriodBarMap == null) return;

			var leftPeriods = new HashSet<int>();
			var rightPeriods = new HashSet<int>();

			foreach (int pi in profile.TpoPeriodBarMap.Keys)
			{
				if (pi < splitPeriod)
					leftPeriods.Add(pi);
				else
					rightPeriods.Add(pi);
			}

			if (leftPeriods.Count == 0 || rightPeriods.Count == 0) return;

			int leftMinBar = int.MaxValue, leftMaxBar = int.MinValue;
			int rightMinBar = int.MaxValue, rightMaxBar = int.MinValue;

			foreach (int pi in leftPeriods)
			{
				int bar = profile.TpoPeriodBarMap[pi];
				if (bar < leftMinBar) leftMinBar = bar;
				if (bar > leftMaxBar) leftMaxBar = bar;
			}
			foreach (int pi in rightPeriods)
			{
				int bar = profile.TpoPeriodBarMap[pi];
				if (bar < rightMinBar) rightMinBar = bar;
				if (bar > rightMaxBar) rightMaxBar = bar;
			}

			var leftProfile = new VolumeProfileSession
			{
				StartTime = profile.StartTime, EndTime = profile.EndTime,
				StartBarIdx = leftMinBar, EndBarIdx = leftMaxBar,
				LastVolumeBarIdx = leftMaxBar, IsActive = false,
				Levels = new Dictionary<long, VolumeLevelData>(), TotalVolume = 0,
				TickSize = profile.TickSize, TicksPerLevel = profile.TicksPerLevel,
				TpoPeriodBarMap = new Dictionary<int, int>()
			};

			var rightProfile = new VolumeProfileSession
			{
				StartTime = profile.StartTime, EndTime = profile.EndTime,
				StartBarIdx = rightMinBar,
				EndBarIdx = profile.IsActive ? profile.EndBarIdx : rightMaxBar,
				LastVolumeBarIdx = rightMaxBar, IsActive = profile.IsActive,
				Levels = new Dictionary<long, VolumeLevelData>(), TotalVolume = 0,
				TickSize = profile.TickSize, TicksPerLevel = profile.TicksPerLevel,
				TpoPeriodBarMap = new Dictionary<int, int>()
			};

			foreach (var kvp in profile.Levels)
			{
				var srcLevel = kvp.Value;
				if (srcLevel.TpoPeriods == null) continue;

				var leftTpo = new HashSet<int>();
				var rightTpo = new HashSet<int>();
				foreach (int pi in srcLevel.TpoPeriods)
				{
					if (leftPeriods.Contains(pi)) leftTpo.Add(pi);
					if (rightPeriods.Contains(pi)) rightTpo.Add(pi);
				}

				if (leftTpo.Count > 0)
				{
					leftProfile.Levels[kvp.Key] = new VolumeLevelData
					{ Price = srcLevel.Price, Volume = leftTpo.Count, TpoPeriods = leftTpo };
					leftProfile.TotalVolume += leftTpo.Count;
				}
				if (rightTpo.Count > 0)
				{
					rightProfile.Levels[kvp.Key] = new VolumeLevelData
					{ Price = srcLevel.Price, Volume = rightTpo.Count, TpoPeriods = rightTpo };
					rightProfile.TotalVolume += rightTpo.Count;
				}
			}

			foreach (var mapKvp in profile.TpoPeriodBarMap)
			{
				if (leftPeriods.Contains(mapKvp.Key))
					leftProfile.TpoPeriodBarMap[mapKvp.Key] = mapKvp.Value;
				else if (rightPeriods.Contains(mapKvp.Key))
					rightProfile.TpoPeriodBarMap[mapKvp.Key] = mapKvp.Value;
			}

			RecalculateKeyLevels(leftProfile);
			RecalculateKeyLevels(rightProfile);

			int idx = _allProfiles.IndexOf(profile);
			if (idx < 0) return;

			if (profile.IsActive)
				_activeProfile = rightProfile;

			_allProfiles[idx] = leftProfile;
			_allProfiles.Insert(idx + 1, rightProfile);
		}

		#endregion

		#region Helper Methods

		/// <summary>
		/// Finds the adjacent profile. direction: +1 = right, -1 = left.
		/// Returns null only if there is no neighbor at all.
		/// </summary>
		private VolumeProfileSession FindAdjacentProfile(VolumeProfileSession profile, int direction)
		{
			if (_allProfiles == null) return null;

			int idx = _allProfiles.IndexOf(profile);
			int neighborIdx = idx + direction;

			if (idx < 0 || neighborIdx < 0 || neighborIdx >= _allProfiles.Count)
				return null;

			return _allProfiles[neighborIdx];
		}

		private bool IsComposite(VolumeProfileSession profile)
		{
			if (_composites == null) return false;
			return _composites.Any(c => c.MergedSession == profile);
		}

		#endregion
	}
}
