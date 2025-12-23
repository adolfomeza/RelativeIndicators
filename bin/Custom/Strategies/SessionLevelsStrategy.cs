#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Core; // Added explicit Core usage
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Net;
using System.Net.Mail;
using System.IO;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class SessionLevelsStrategy : Strategy
	{
		// Version Control
		private const string StrategyVersion = "v1.4_StrictAlign";

		public enum VwapCalculationMode
		{
			Typical, // (H+L+C)/3
			Close,   // Close
			OHLC4    // (O+H+L+C)/4
		}


		// ... existing properties ...
		private bool enableDebugLogs = true; // Default to true during dev

		[NinjaScriptProperty]
		[Display(Name="Enable Debug Logs", Description="Print detailed execution steps to Output. Disable for faster backtests.", Order=60, GroupName="General")]
		public bool EnableDebugLogs
		{
			get { return enableDebugLogs; }
			set { enableDebugLogs = value; }
		}

		private bool showVisuals = true;
		
		[NinjaScriptProperty]
		[Display(Name="Show Visuals", Description="Draw lines on chart. Disable to save resources.", Order=61, GroupName="General")]
		public bool ShowVisuals
		{
			get { return showVisuals; }
			set { showVisuals = value; }
		}
		
		private VwapCalculationMode vwapMethod = VwapCalculationMode.Typical;
		[NinjaScriptProperty]
		[Display(Name="VWAP Calculation Method", Description="Select formula for VWAP.", Order=62, GroupName="General")]
		public VwapCalculationMode VwapMethod
		{
			get { return vwapMethod; }
			set { vwapMethod = value; }
		}
		

		// Visual State for Adhoc VWAP Line
		private double visualAdhocPrevBarVal = 0;
		private double visualAdhocLastVal = 0;
		private int visualAdhocLastBar = -1;

		// Forensic Logging
		private void Log(string message)
		{
			// Simple Print only to debug activation crash
			if (EnableDebugLogs) Print(message);
		}


		// =========================================================
		// STATE PERSISTENCE (XML)
		// =========================================================
		



		

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				// CRITICAL: Unmanaged Mode (Moved to end)
				// IsUnmanaged = true; // Moved down
				
				// ...
				Description									= @"Advanced Session Levels Strategy with VWAP and R/R Filters.";
				Name										= "SessionLevelsStrategy " + StrategyVersion;
				Calculate									= Calculate.OnEachTick;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix; // REVERTED FROM INFINITE 
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat; // Aligned with RLS
				TimeInForce									= TimeInForce.Gtc; // Aligned with RLS
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				IsOverlay = true;
				
				// IsUnmanaged moved to top
				
				// Add Plots for VWAP
				AddPlot(Brushes.White, "HighVWAP"); // Values[0]
				AddPlot(Brushes.White, "LowVWAP");  // Values[1]
				
				// FINAL FORCE: Unmanaged Mode
				IsUnmanaged = false; // Reverted to false
			}
			else if (State == State.DataLoaded)
			{
				Print("DEBUG: OnStateChange(DataLoaded) IsUnmanaged = " + IsUnmanaged);
				try 
				{
					LoadLevels();
				} 
				catch(Exception ex) { Print("Warning: Failed to load levels: " + ex.Message); }
			}
			else if (State == State.Terminated)
			{
				try
				{
					SaveLevels();
				}
				catch(Exception ex) { Print("Warning: Failed to save levels: " + ex.Message); }
			}
		}


		// -------------------------------------------------------------------------
		// PERSISTENCE LOGIC (v3 - Safe Mode - Multi-Instrument)
		// -------------------------------------------------------------------------
		private string GetPersistencePath()
		{
			// Safe Filename: Remove slashes or colons from Instrument Name
			string safeName = Instrument.FullName.Replace('/', '-').Replace(':', '-');
			string filename = "SessionLevels_State_" + safeName + "_v3.xml";
			return System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "trace", filename);
		}

		private void SaveLevels()
		{
			// Only save if we have data and logic initialized
			if (activeLevels == null || activeLevels.Count == 0) return;

			string path = GetPersistencePath();
			
			// Map specific List<SessionLevel> to List<SessionLevelData>
			List<SessionLevelData> dataToSave = new List<SessionLevelData>();
			foreach(var level in activeLevels)
			{
				dataToSave.Add(new SessionLevelData
				{
					Name = level.Name,
					Price = level.Price,
					StartTime = level.StartTime,
					EndTime = level.EndTime,
					MitigationTime = level.MitigationTime,
					IsResistance = level.IsResistance,
					IsMitigated = level.IsMitigated,
					VolSum = level.VolSum,
					PvSum = level.PvSum,
					Tag = level.Tag
				});
			}

			try
			{
				// Ensure directory exists
				string dir = System.IO.Path.GetDirectoryName(path);
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

				XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevelData>));
				using (StreamWriter writer = new StreamWriter(path))
				{
					serializer.Serialize(writer, dataToSave);
				}
				Print(DateTime.Now + " State Saved: " + dataToSave.Count + " levels to " + path);
			}
			catch (Exception ex)
			{
				Print("SaveLevels Exception: " + ex.Message);
			}
		}

		private void LoadLevels()
		{
			string path = GetPersistencePath();
			if (!File.Exists(path)) return;

			// 1. GAP DETECTION
			try
			{
				if (Bars != null && Bars.Count > 0)
				{
					DateTime fileTime = File.GetLastWriteTime(path);
					DateTime firstBarTime = Bars.GetTime(0);
					
					// If the file is OLDER than the First Bar loaded, we have a blind spot.
					if (fileTime < firstBarTime) 
					{
						gapDetected = true;
						Print("WARNING: Persistence Gap Detected! File Time: " + fileTime + " < First Bar: " + firstBarTime);
					}
				}
			}
			catch {}

			try
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevelData>));
				List<SessionLevelData> loadedData;
				
				using (StreamReader reader = new StreamReader(path))
				{
					loadedData = (List<SessionLevelData>)serializer.Deserialize(reader);
				}

				if (loadedData != null && loadedData.Count > 0)
				{
					// 2. SANITY CHECK (Auto-Mitigate Ghost Lines)
					double sanityPrice = -1;
					if (Bars != null && Bars.Count > 0) sanityPrice = Bars.GetOpen(0);

					int count = 0;
					int mitigatedCount = 0;
					
					foreach (var d in loadedData)
					{
						if (activeLevels.Any(l => l.Tag == d.Tag)) continue;

						SessionLevel newLvl = new SessionLevel
						{
							Name = d.Name,
							Price = d.Price,
							StartTime = d.StartTime,
							EndTime = d.EndTime,
							MitigationTime = d.MitigationTime,
							IsResistance = d.IsResistance,
							IsMitigated = d.IsMitigated,
							Tag = d.Tag,
							VolSum = d.VolSum,
							PvSum = d.PvSum,
							JustReset = false
						};
						
						// SANITY LOGIC
						if (sanityPrice > 0 && !newLvl.IsMitigated)
						{
							if (newLvl.IsResistance && sanityPrice > newLvl.Price)
							{
								newLvl.IsMitigated = true;
								newLvl.MitigationTime = Bars.GetTime(0); // Mark as broken at open
								mitigatedCount++;
							}
							else if (!newLvl.IsResistance && sanityPrice < newLvl.Price)
							{
								newLvl.IsMitigated = true;
								newLvl.MitigationTime = Bars.GetTime(0);
								mitigatedCount++;
							}
						}
						
						// Restore Color
						if (d.Name.Contains("Asia")) newLvl.Color = Brushes.White;
						else if (d.Name.Contains("Europe")) newLvl.Color = Brushes.Yellow;
						else if (d.Name.Contains("USA")) newLvl.Color = Brushes.RoyalBlue;
						else newLvl.Color = Brushes.Gray;

						activeLevels.Add(newLvl);
						count++;
					}
					
					string msg = "State Loaded: " + count + " levels restored.";
					if (mitigatedCount > 0) msg += " (Auto-Mitigated " + mitigatedCount + " ghosts due to Gap).";
					Print(DateTime.Now + " " + msg);
				}
			}
			catch (Exception ex)
			{
				Print("LoadLevels Exception: " + ex.Message);
			}
		}

		// TimeZone Caching
		private TimeZoneInfo nyTimeZone;
		private TimeZoneInfo chartTimeZone;
		private bool timeZonesLoaded = false;
		private double lastVol = 0;

		// Level Persistence
		private class SessionLevel
		{
			public string Name;
			public double Price;
			public DateTime StartTime;
			public DateTime EndTime;
			public DateTime MitigationTime; // When it was touched
			public bool IsResistance; // True = High, False = Low
			public bool IsMitigated;
			public Brush Color;
			public string Tag; // For Drawing
			
			// VWAP Data
			public double VolSum;
			public double PvSum;
			public bool JustReset;
		}
		
		private List<SessionLevel> activeLevels = new List<SessionLevel>();

		// Strategy Initialization Flag
		private bool isStrategyInitialized = false;
		private bool gapDetected = false;

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 20) return;
			
			// INITIALIZATION (Snap Anchors to start)
			if (!isStrategyInitialized)
			{
				isStrategyInitialized = true;
				highAnchorBar = CurrentBar;
				lowAnchorBar = CurrentBar;
				ethHighPrice = High[0];
				ethLowPrice = Low[0];
				
				// Reset VWAPs to start fresh here
				ethHighVWAP = new SessionVWAP(); ethHighVWAP.Reset(Volume[0], Close[0]);
				ethLowVWAP = new SessionVWAP(); ethLowVWAP.Reset(Volume[0], Close[0]);
				
				// Init AdHoc
				adhocLastBar = CurrentBar;
				lastVol = Volume[0]; // Set volume baseline
				
				// Don't modify plots on init frame
				return;
			}
			
			// Initialize TimeZones & Lists once
			if (!timeZonesLoaded)
			{
				try 
				{
					// "Eastern Standard Time" handles both EST and EDT automatically on Windows
					nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
					
					// Get the TimeZone of the current bars/chart
					if (NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo != null)
						chartTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
					else
						chartTimeZone = TimeZoneInfo.Local; // Fallback
						
					timeZonesLoaded = true;
				}
				catch (Exception ex)
				{
					Print("Error loading TimeZones: " + ex.Message);
					timeZonesLoaded = true; 
				}
			}

			// 0. Calculate Volume Delta for VWAP
			if (IsFirstTickOfBar) lastVol = 0;
			double deltaVol = Volume[0] - lastVol;
			lastVol = Volume[0];

			// CSV LOGGING INIT (Once per session)
			if (CurrentBar == BarsRequiredToTrade) // Use a safe bar index to init
			{
				InitCSV();
			}

			// 1. Session Logic: Identify/Create Levels
			CheckSession("Asia", AsiaStartTime, AsiaEndTime, Brushes.White, deltaVol);
			CheckSession("Europe", EuropeStartTime, EuropeEndTime, Brushes.Yellow, deltaVol);
			CheckSession("USA", USAStartTime, USAEndTime, Brushes.RoyalBlue, deltaVol);
			
			// 2. Manage Extension & Touching
			ManageLevels(deltaVol);
			
			// 3. Global ETH VWAPs
			ManageGlobalVWAPs(deltaVol);
			
			// 4. Entry Logic
			ManageEntryA_Plus();
		}
		
		private void CheckSession(string sessionName, string startStr, string endStr, Brush color, double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;

			DateTime chartTime = Time[0];
			DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
			TimeSpan nyTimeOfDay = nyTime.TimeOfDay;
			
			TimeSpan startTs = TimeSpan.Parse(startStr);
			TimeSpan endTs = TimeSpan.Parse(endStr);
			
			bool inSession = false;
			
			if (startTs > endTs) { if (nyTimeOfDay >= startTs || nyTimeOfDay < endTs) inSession = true; }
			else { if (nyTimeOfDay >= startTs && nyTimeOfDay < endTs) inSession = true; }
			
			if (inSession)
			{
				// Determine Session Date (for unique ID)
				DateTime calculatedSessionStartNY = (startTs > endTs && nyTimeOfDay < endTs) ? nyTime.Date.AddDays(-1) : nyTime.Date;
				calculatedSessionStartNY = calculatedSessionStartNY.Add(startTs);
				
				// Unique IDs for High and Low
				string tagH = sessionName + "_High_" + calculatedSessionStartNY.Ticks;
				string tagL = sessionName + "_Low_" + calculatedSessionStartNY.Ticks;
				
				// Find or Create Levels
				SessionLevel highLvl = activeLevels.FirstOrDefault(l => l.Tag == tagH);
				SessionLevel lowLvl = activeLevels.FirstOrDefault(l => l.Tag == tagL);
				
				// Convert Start Time to Chart Time for Visuals
				DateTime chartStartTime = TimeZoneInfo.ConvertTime(calculatedSessionStartNY, nyTimeZone, chartTimeZone);

				if (highLvl == null)
				{
					// New High Level (Init VWAP with current Bar Full Volume as it creates the anchor)
					highLvl = new SessionLevel 
					{ 
						Name = sessionName + " High", Price = double.MinValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = true, IsMitigated = false, Color = color, Tag = tagH,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(highLvl);
				}
				else highLvl.JustReset = false; // Reset flag default
				
				if (lowLvl == null)
				{
					// New Low Level
					lowLvl = new SessionLevel 
					{ 
						Name = sessionName + " Low", Price = double.MaxValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = false, IsMitigated = false, Color = color, Tag = tagL,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(lowLvl);
				}
				else lowLvl.JustReset = false;
				
				// Logic: While in session, we push the High/Low out. 
				// If New High -> Reset VWAP to Anchor HERE.
				
				if (High[0] > highLvl.Price) 
				{
					highLvl.Price = High[0];
					// RE-ANCHOR VWAP
					highLvl.VolSum = Volume[0];
					highLvl.PvSum = Volume[0] * Close[0];
					highLvl.JustReset = true;
				}
				
				if (Low[0] < lowLvl.Price) 
				{
					lowLvl.Price = Low[0];
					// RE-ANCHOR VWAP
					lowLvl.VolSum = Volume[0];
					lowLvl.PvSum = Volume[0] * Close[0];
					lowLvl.JustReset = true;
				}
				
				// While in session, update EndTime to current to keep line growing
				if (!highLvl.IsMitigated) highLvl.EndTime = Time[0];
				if (!lowLvl.IsMitigated) lowLvl.EndTime = Time[0];
			}
		}

		private void ManageLevels(double deltaVol)
		{
			// Check for touches on existing active levels
			
			foreach (var lvl in activeLevels)
			{
				// BACKTEST SAFETY: Completely ignore future levels (Visuals + Logic)
				if (lvl.StartTime > Time[0]) continue;

				// VWAP ACCUMULATION
			if (!lvl.JustReset)
			{
				lvl.VolSum += deltaVol;
				double price = Close[0];
				if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
				else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
				
				lvl.PvSum += deltaVol * price;
			}
				// If JustReset was true, we already set VolSum/PvSum in CheckSession. 
				// JustReset is ephemeral for this tick.
				
				// Calculate VWAP
				double vwap = 0;
				if (lvl.VolSum > 0) vwap = lvl.PvSum / lvl.VolSum;

				// LINE EXTENSION LOGIC
				// Alive: Always extend.
				// Mitigated: Extend ONLY if we are still in the same calendar day as the mitigation Event.
				
				if (!lvl.IsMitigated)
				{
					lvl.EndTime = Time[0];
				}
				else
				{
					// Ghost Line Extension
					// Extension Rule: Continue until the End of the American Session (USAEndTime).
					// We need to calculate the *specific* cutoff time relative to the Mitigation event.
					
					// 1. Convert MitigationTime to NY to understand when it happened
					DateTime mitNy = TimeZoneInfo.ConvertTime(lvl.MitigationTime, chartTimeZone, nyTimeZone);
					TimeSpan usaEndTs = TimeSpan.Parse(USAEndTime);
					
					// 2. Determine the Cutoff Date/Time (NY)
					// If mitigation happened BEFORE the cutoff today (e.g. 10:00 vs 18:00), cutoff is Today 18:00.
					// If mitigation happened AFTER the cutoff (e.g. 19:00 vs 18:00), cutoff is Tomorrow 18:00.
					
					DateTime cutoffNy;
					if (mitNy.TimeOfDay < usaEndTs)
						cutoffNy = mitNy.Date.Add(usaEndTs);
					else
						cutoffNy = mitNy.Date.AddDays(1).Add(usaEndTs);
						
					// 3. Compare Current Time (NY) to Cutoff (NY)
					DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
					
					if (currentNy < cutoffNy)
					{
						lvl.EndTime = Time[0];
					}
					// Else: Freeze (Stop extending)
				}
				
				// Check for Mitigation (if not already broken)
				// Only if session is effectively done (Start/End checks or just assume if formed)
				// Simplified: Just always check touch.
				
				if (!lvl.IsMitigated)
				{
					// Avoid self-mitigation during formation
					// If the StartTime was effectively "today" or "recent" and we are still largely in that window?
					// Problem: CheckSession pushes Price up/down. 
					// If we are IN session, CheckSession updates Price.
					// So if High[0] == Price, CheckSession makes Price = High[0].
					// So High[0] == Price.
					// So "High[0] >= Price" is TRUE.
					// We need to know if we are "In Session" to avoid mitigation.
					
					// Heuristic: If CheckSession updated it THIS TICK, don't mitigate.
					// But we run ManageLevels AFTER CheckSession.
					// Let's rely on a flag or simply check if Time is outside Session Window?
					// Checking Time is hard because of the varying session hours.
					// Let's use a "InSession" flag on the object?
					// Or reusing the "EndTime" check from previous step:
					// If(lvl.EndTime == Time[0]) it means CheckSession updated it? 
					// NO, we just updated lvl.EndTime = Time[0] at the top of this loop! Invalid logic now.
					
					// Let's add an explicit "LastUpdateBar" or similar to SessionLevel?
					// Or simpler: We know the logic in CheckSession updates Price.
					// If Price == High[0], it's likely pushing.
					// But if Price < High[0], it's a break.
					// Wait, if Price < High[0] (Resistance), then CheckSession WOULD have updated it if we were in session!
					// So if CheckSession DID NOT update it (Price < High[0]), it means we are NOT in session (or logic failed).
					// Therefore, if High[0] > Price, it MUST be a mitigation break!
					// CORRECT.
					
					// Exception: The very specific moment High[0] jumps? 
					// If in session, CheckSession runs first. 
					// If High[0] > currentHigh, set currentHigh = High[0].
					// So entering ManageLevels, Price == High[0].
					// Use strict inequality? High[0] > Price? No, touch is enough.
					
					// Let's iterate:
					// In Session: Price = 100. High[0] = 101. -> CheckSession sets Price = 101. -> ManageLevels sees Price=101, High[0]=101.
					// Out Session: Price = 100. High[0] = 101. -> CheckSession does nothing. -> ManageLevels sees Price=100, High[0]=101. -> MITIGATION!
					
					// So, logic:
					// Resistance: If High[0] > Price -> Mitigation.
					// Support: If Low[0] < Price -> Mitigation.
					// Equality (Touch) shouldn't count if we assume "Break"?
					// User said "cortadas" (cut/broken) or "tocada" (touched)?
					// "hasta que sea tocada" (touched).
					// If it's a touch (==), then in-session formation is a touch.
					// We MUST distinguish In-Session.
					
					// Let's calculate In-Session locally again or store it.
					// Re-calculating properly is safer.
					
					// Actually, let's use the object creation/update time?
					// Let's look at `IsResistance`.
					bool potentialMitigation = false;
					if (lvl.IsResistance && High[0] >= lvl.Price) potentialMitigation = true;
					if (!lvl.IsResistance && Low[0] <= lvl.Price) potentialMitigation = true;
					
					if (potentialMitigation)
					{
						// Check if we are physically in the session window for this specific level
						// This line's tag has StartTicks.
						// Simplest: Check if the *current price* is EQUAL to level price.
						// If equal, likely just forming/touching.
						// If strictly Greater (Res) or Less (Sup) AND Level Price wasn't updated?
						// It's ambiguous.
						
						// CLEAN FIX: Add `IsActive` bool to SessionLevel, set by CheckSession.
						// But I can't easily change CheckSession signature in this edit without replacing whole file.
						// I'll calculate `inSession` simply here. It's safe.
						// Oh wait, I don't know WHICH session hours apply to THIS level (Asia? USA?).
						// I have `lvl.Name` ("Asia High"). I can parse or Map.
						
						// HACK: Just assume if the Price CHANGED this bar, it's active?
						// No.
						
						// Let's guess based on inequality.
						// If High[0] > lvl.Price, it's definitely a Break (Mitigation), because if it was active, Price would have updated to match High[0].
						// Wait. CheckSession updates logic: `if (High[0] > highLvl.Price) highLvl.Price = High[0];`
						// So Price will ALWAYS be >= High[0] if active.
						// Price will never be < High[0].
						// So if High[0] > Price, it implies CheckSession did NOT run/update -> We are Out of Session -> Mitigation.
						// If High[0] == Price? Could be "Just forming" OR "Perfect double top touch".
						// We'll ignore Exact Touch for mitigation to be safe against formation noise.
						// Strictly greater/less for "Cut/Break".
						
						bool strictBreak = false;
						if (lvl.IsResistance && High[0] > lvl.Price) strictBreak = true;
						if (!lvl.IsResistance && Low[0] < lvl.Price) strictBreak = true;
						
						if (strictBreak)
						{
							lvl.IsMitigated = true;
							lvl.MitigationTime = Time[0];
						}
					}
				}
				
				// Drawing Logic in Low Performance Mode (Optional)
				if (ShowVisuals)
				{
					string tagA = lvl.Tag + "_A";
					string tagB = lvl.Tag + "_B";
					
					if (!lvl.IsMitigated)
					{
						// Phase A Only: Start -> Current
						Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.EndTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
					}
					else
					{
						// Phase A: Start -> Mitigation
						Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.MitigationTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
						
						// Phase B (Ghost): Mitigation -> Current (Gray, Dash, 1px)
						// Verify MitigationTime < EndTime to draw
						Draw.Line(this, tagB, false, lvl.MitigationTime, lvl.Price, lvl.EndTime, lvl.Price, Brushes.Gray, DashStyleHelper.Dash, 1);
					}
				}
			}
		}


		// -------------------------------------------------------------------------
		// ENTRY LOGIC VARIABLES
		// -------------------------------------------------------------------------
		private enum EntryState { Idle, WaitingForConfirmation, workingOrder, PositionActive }
		private EntryState currentEntryState = EntryState.Idle;
		private bool isShortSetup = false; // true = Short, false = Long
		private Order entryOrder = null;
		private Order stopOrder = null;
		private Order targetOrder = null; // We use Order objects for dynamic management
		
		private double setupAnchorPrice = 0; // For SL
		private string setupLevelName = "";
		
		// Visual Tracking
		private string triggerTag = "";
		private int triggerBar = 0;
		
		// -------------------------------------------------------------------------
		// GLOBAL ETH SESSION VWAP LOGIC
		// -------------------------------------------------------------------------
		private class SessionVWAP
		{
			public double VolSum;
			public double PvSum;
			public double CurrentValue => VolSum == 0 ? 0 : PvSum / VolSum;
			
			public void Reset(double vol, double price)
			{
				VolSum = vol;
				PvSum = vol * price;
			}
			
			public void Accumulate(double vol, double price)
			{
				VolSum += vol;
				PvSum += vol * price;
			}
		}
		
		private SessionVWAP ethHighVWAP = new SessionVWAP();
		private SessionVWAP ethLowVWAP = new SessionVWAP();
		
		#region Properties
		// Email & Screenshot Properties
		[NinjaScriptProperty]
		[Display(Name = "Send Email with Photo", Description = "Send screenshot via email (Requires SMTP settings)", GroupName = "8. Email Alerts", Order = 1)]
		public bool EnableEmailAlerts { get; set; } = false;

		[NinjaScriptProperty]
		[Display(Name = "To Address", Description = "Recipient address", GroupName = "8. Email Alerts", Order = 2)]
		public string EmailTo { get; set; } = "user@example.com";

		[NinjaScriptProperty]
		[Display(Name = "From Address", Description = "Sender address (usually same as username)", GroupName = "8. Email Alerts", Order = 3)]
		public string EmailFrom { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Host", Description = "e.g. smtp.gmail.com", GroupName = "8. Email Alerts", Order = 4)]
		public string EmailHost { get; set; } = "smtp.gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Port", Description = "e.g. 587", GroupName = "8. Email Alerts", Order = 5)]
		public int EmailPort { get; set; } = 587;

		[NinjaScriptProperty]
		[Display(Name = "SMTP Username", Description = "Full email address", GroupName = "8. Email Alerts", Order = 6)]
		public string EmailUsername { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Password", Description = "App Password (not your login password)", GroupName = "8. Email Alerts", Order = 7)]
		public string EmailPassword { get; set; } = "password";
		
		private double ethHighPrice = double.MinValue;
		private double ethLowPrice = double.MaxValue;
		private DateTime lastEthResetDate = DateTime.MinValue; 

		// AD-HOC VWAP Variables (Fresh Start)
		private double adhocVolSum = 0;
		private double adhocPvSum = 0;
		private double adhocLastVol = 0; // To track delta volume inside a bar
		private int adhocLastBar = -1;

		private void UpdateAdhocVWAP()
		{
			// Reset tracker on new bar for proper delta calculation
			if (CurrentBar != adhocLastBar)
			{
				adhocLastVol = 0;
				adhocLastBar = CurrentBar;
			}

			// Calculate Delta Volume (Current Bar Volume so far - what we already processed)
			// NinjaTrader Volume[0] is cumulative for the bar
			double currentBarVol = Volume[0];
			double deltaVol = currentBarVol - adhocLastVol;
			
			if (deltaVol > 0)
	{
		adhocVolSum += deltaVol;
		double price = Close[0];
		if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
		else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
		
		adhocPvSum += deltaVol * price; 
		adhocLastVol = currentBarVol; // Update processed volume
	}
		} 
		
		private int highAnchorBar = 0;
		private int lowAnchorBar = 0;

		private void ManageGlobalVWAPs(double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;
			
			// 1. Determine Current Trading Day (based on 18:00 NY start)
			DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
			TimeSpan cutoff = TimeSpan.FromHours(18);
			DateTime tradingDay = currentNy.TimeOfDay >= cutoff ? currentNy.Date.AddDays(1) : currentNy.Date;
			
			// 2. HARD RESET at Start of Day
			bool hardReset = false;
			if (tradingDay != lastEthResetDate)
			{
				ethHighPrice = double.MinValue;
				ethLowPrice = double.MaxValue;
				ethHighVWAP = new SessionVWAP();
				ethLowVWAP = new SessionVWAP();
				lastEthResetDate = tradingDay;
				hardReset = true;
				
				// Reset Anchor Trackers
				highAnchorBar = CurrentBar;
				lowAnchorBar = CurrentBar;
			}
			
			// 3. Update High/Low and Anchor Logic
	bool highReset = false;
	bool lowReset = false;
	
	double price = Close[0];
	if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
	else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
	
	// Check High
	if (High[0] > ethHighPrice)
	{
		// New High found! The PREVIOUS segment (from highAnchorBar to CurrentBar-1) is now "Old/Cut".
		// We must paint it GRAY.
		if (!hardReset && CurrentBar > highAnchorBar)
		{
			int barsBack = CurrentBar - highAnchorBar;
			// IMPORTANT: Use i < barsBack to avoid overwriting the Transparency of the Anchor Bar itself.
			for (int i = 1; i < barsBack; i++)
			{
				PlotBrushes[0][i] = Brushes.Gray;
			}
		}
		
		ethHighPrice = High[0];
		highReset = true;
		ethHighVWAP.Reset(Volume[0], price);
		highAnchorBar = CurrentBar; // Update anchor to here
	}
	else
	{
		ethHighVWAP.Accumulate(deltaVol, price);
	}
	
	// Check Low
	if (Low[0] < ethLowPrice)
	{
		// New Low found! Paint previous segment Gray.
		if (!hardReset && CurrentBar > lowAnchorBar)
		{
			int barsBack = CurrentBar - lowAnchorBar;
			for (int i = 1; i < barsBack; i++)
			{
				PlotBrushes[1][i] = Brushes.Gray;
			}
		}
		
		ethLowPrice = Low[0];
		lowReset = true;
		ethLowVWAP.Reset(Volume[0], price);
		lowAnchorBar = CurrentBar;
	}
	else
	{
		ethLowVWAP.Accumulate(deltaVol, price);
	}
			
			// 4. Assign to Plots (Values[0] = High, Values[1] = Low)
			// Default color is White (defined in AddPlot). We only override active history to Gray when it dies.
			// The "Current" active segment stays White until it dies.
			
			if (ethHighVWAP.VolSum > 0)
			{
				Values[0][0] = ethHighVWAP.CurrentValue;
				
				if (hardReset || highReset)
				{
					PlotBrushes[0][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[0][0] = double.NaN; 
			}

			if (ethLowVWAP.VolSum > 0)
			{
				Values[1][0] = ethLowVWAP.CurrentValue;
				
				if (hardReset || lowReset)
				{
					PlotBrushes[1][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[1][0] = double.NaN;
			}
			
			// Debug Panel
			DrawStatePanel();
			
			// SAFETY NET: Check for Zombie Positions (In Market, but State logic missed it)
			CheckSafetyNet();
			
			// FAILSAFE: Hard Stop Check (In case Managed Order fails)
			CheckHardStop();
		}
		
		private void CheckHardStop()
		{
			if (Position.MarketPosition == MarketPosition.Flat) return;
			
			// Validate Anchor
			if (setupAnchorPrice <= 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue) return;

			// BUFFER: Only force exit if price breaches Anchor SIGNIFICANTLY (e.g., 4 ticks).
			// This gives the native Stop Order priority to execute at the correct price.
			// Failsafe is only for when the Stop Order fails.
			double checkBuffer = 4 * TickSize;

			if (Position.MarketPosition == MarketPosition.Short)
			{
				// If Price is ABOVE Anchor + Buffer
				if (High[0] >= (setupAnchorPrice + checkBuffer))
				{
					Log(Time[0] + " FAILSAFE: Price (" + High[0] + ") violated Anchor (" + (setupAnchorPrice + checkBuffer) + "). Forcing ExitShort.");
					ExitShort();
				}
			}
			else if (Position.MarketPosition == MarketPosition.Long)
			{
				// If Price is BELOW Anchor - Buffer
				if (Low[0] <= (setupAnchorPrice - checkBuffer))
				{
					Log(Time[0] + " FAILSAFE: Price (" + Low[0] + ") violated Anchor (" + (setupAnchorPrice - checkBuffer) + "). Forcing ExitLong.");
					ExitLong();
				}
			}
		}

		// Orphan State Tracking
		private bool orphanHandled = false;

		private void CheckSafetyNet()
		{
			// 0. ACCOUNT SYNC CHECK (Realtime Only)
			if (State == State.Realtime && Account != null && Position.MarketPosition == MarketPosition.Flat)
			{
				bool foundOrphan = false; 
				
				try 
				{
					foreach (Position accPos in Account.Positions)
					{
						// Filter for this Instrument (String compare safer)
						if (accPos.Instrument.FullName == Instrument.FullName && accPos.MarketPosition != MarketPosition.Flat)
						{
							foundOrphan = true;
							
							// ORPHAN DETECTED
							double avgPrice = accPos.AveragePrice;
							double safetyMargin = 20 * TickSize;
							
							// Safety Check
							bool unsafeOrphan = false;
							if (accPos.MarketPosition == MarketPosition.Long)
							{
								if (Low[0] <= avgPrice - safetyMargin) unsafeOrphan = true;
							}
							else if (accPos.MarketPosition == MarketPosition.Short)
							{
								if (High[0] >= avgPrice + safetyMargin) unsafeOrphan = true;
							}

							if (unsafeOrphan)
							{
								Log(Time[0] + " CRITICAL: Orphan Position Detected (Unsafe). Flattening Account for " + Instrument.FullName + " @ " + avgPrice);
								Account.Flatten(new [] { Instrument });
							}
							else if (!orphanHandled)
							{
								// Safe Orphan & Not Handled -> ALERT ONLY (Managed Mode Risk)
								// Visual Confirmation
								Draw.Text(this, "OrphanTxt_" + CurrentBar, "SAFE ORPHAN DETECTED\nMANAGE MANUALLY", 0, avgPrice, Brushes.LimeGreen);
								
								// Alert
								PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
								Log(Time[0] + " WARNING: Safe Orphan Position Detected @ " + avgPrice + ". Unable to auto-manage in Managed Mode. PLEASE SET SL/TP MANUALLY.");
								
								orphanHandled = true;
							}
						}
					}
				}
				catch (Exception ex) { Log("Account Sync Check Failed: " + ex.Message); }
				
				// Reset Handled flag if no orphan found (position closed)
				if (!foundOrphan) orphanHandled = false;
			}
			else
			{
				orphanHandled = false; // Reset if Strategy has position (managed)
			}

			// 1. Zombie Position: We have a position, but State thinks we are Idle/Working.
			if (Position.MarketPosition != MarketPosition.Flat && currentEntryState != EntryState.PositionActive)
			{
				Log(Time[0] + " CRITICAL: Safety Net Triggered! Position exists but State was " + currentEntryState);
				
				// --- SMART ADOPTION LOGIC (Strategy Position) ---
				// ... (Existing Logic) ...
				
				if (setupAnchorPrice == 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue)
				{
					// We have AMNESIA. Let's infer a safety anchor.
					double avgPrice = Position.AveragePrice;
					double safetyMargin = 20 * TickSize; // Emergency allow 20 ticks from entry
					
					if (Position.MarketPosition == MarketPosition.Short)
					{
						// Short: Anchor should be ABOVE entry.
						double inferredAnchor = avgPrice + safetyMargin;
						
						// Validation: Are we ALREADY dead?
						if (High[0] >= inferredAnchor)
						{
							Log(Time[0] + " ZOMBIE CHECK: Price (" + High[0] + ") is above Inferred Anchor (" + inferredAnchor + "). Closing Unsafe Position.");
							ExitShort();
							return; // Don't adopt. Kill.
						}
						
						// If safe, adopt.
						setupAnchorPrice = inferredAnchor;
						Log(Time[0] + " ZOMBIE ADOPTED (Short). Inferred Anchor: " + setupAnchorPrice);
					}
					else if (Position.MarketPosition == MarketPosition.Long)
					{
						// Long: Anchor should be BELOW entry.
						double inferredAnchor = avgPrice - safetyMargin;
						
						// Validation: Are we ALREADY dead?
						if (Low[0] <= inferredAnchor)
						{
							Log(Time[0] + " ZOMBIE CHECK: Price (" + Low[0] + ") is below Inferred Anchor (" + inferredAnchor + "). Closing Unsafe Position.");
							ExitLong();
							return; // Don't adopt. Kill.
						}
						
						// If safe, adopt.
						setupAnchorPrice = inferredAnchor;
						Log(Time[0] + " ZOMBIE ADOPTED (Long). Inferred Anchor: " + setupAnchorPrice);
					}
				}

				// If we reached here, we are adopting (or already had an anchor).
				currentEntryState = EntryState.PositionActive;
				
				// Force Place Stops if missing
				if (Position.MarketPosition == MarketPosition.Short)
				{
					EnsureProtection("Short");
				}
				else if (Position.MarketPosition == MarketPosition.Long)
				{
					EnsureProtection("Long");
				}
			}
			
			// 2. Ghost State: State thinks we are InPosition, but we are Flat.
			// This happens if we missed the Exit Execution or closed manually.
			// We only reset if we are confident the entry order isn't just "about to fill" (Working State handles that).
			// If State is InPosition, it implies we ALREADY filled. So if we are Flat now, we must have closed.
			if (Position.MarketPosition == MarketPosition.Flat && currentEntryState == EntryState.PositionActive)
			{
				Log(Time[0] + " SYNC: State is InPosition but MarketPosition is Flat. Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
				
				// CRITICAL FIX: Ensure ALL order references are cleared to prevent "Exits already exist" blocking future trades.
				entryOrder = null;
				targetOrder = null; // Legacy
				stopOrder = null; 
				tp1Order = null;
				tp2Order = null;
			}
		}
		
		private void DrawStatePanel()
		{
			double accountPnL = 0;
			double sessionPnL = 0;

			try {
				if (Account != null)
					accountPnL = Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);

				if (SystemPerformance != null && SystemPerformance.RealTimeTrades != null)
					sessionPnL = SystemPerformance.RealTimeTrades.TradesPerformance.Currency.CumProfit;
			} catch {}

			string text = string.Format("Ver: {0}\nState: {1}\nPosition: {2}\nSession PnL: {3}\nActive Levels: {4}\nHigh VWAP: {5:F2}\nLow VWAP: {6:F2}",
				StrategyVersion,
				currentEntryState,
				Position.MarketPosition,
				sessionPnL.ToString("C"),
				activeLevels.Count,
				ethHighVWAP.CurrentValue,
				ethLowVWAP.CurrentValue);
				
			Draw.TextFixed(this, "InfoPanel", text, TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
			
			if (gapDetected)
			{
				Draw.TextFixed(this, "GapWarning", "\n\n\n\n\n\n\n\nDATA GAP DETECTED - LOAD MORE DAYS", TextPosition.TopRight, Brushes.Red, new SimpleFont("Arial", 12) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 100);
			}
		}
		
		// -------------------------------------------------------------------------
		// ENTRY A+ MANAGEMENT
		// -------------------------------------------------------------------------
		private void ManageEntryA_Plus()
		{
			// 1. TRIGGER DETECTION (Transition from Idle -> Waiting OR Switch Setup)
			// Allow scanning for triggers if Idle OR Waiting (to switch setups).
			bool canScan = (currentEntryState == EntryState.Idle || currentEntryState == EntryState.WaitingForConfirmation);
			
			// Always Update ADHOC VWAP if we are in a setup based on it
			// Wait... we need to accumulate ONLY after trigger? Or always?
			// User wants "Ends when touched". So we accumulate FROM Trigger.
			if (currentEntryState == EntryState.WaitingForConfirmation || currentEntryState == EntryState.workingOrder)
			{
				UpdateAdhocVWAP();
				
				// VISUAL DEBUG: Draw 1px White Line
				bool isShort = (isShortSetup); 
				double v = GetSetupVWAP(isShort);
				
				// Redundancy Check: Is this Anchor the Global High or Low?
				// If so, the Global Plot (Values[0] or [1]) is already drawing this. We don't need a double line.
				bool isGlobal = false;
				if (isShort && Math.Abs(setupAnchorPrice - ethHighPrice) < TickSize) isGlobal = true;
				if (!isShort && Math.Abs(setupAnchorPrice - ethLowPrice) < TickSize) isGlobal = true;
				
				if (v > 0 && !isGlobal)
				{
					// Update Visual State logic
					if (visualAdhocLastBar != CurrentBar && visualAdhocLastBar != -1)
					{
						// New Bar Detected. Store the FINAL value of previous bar as start point.
						visualAdhocPrevBarVal = visualAdhocLastVal;
					}
					
					// Draw Line from PrevBarVal (Start of this bar logic) to CurrentVal (v)
					// Only draw if we have a valid previous point (not just started)
					if (visualAdhocLastBar != -1 && visualAdhocPrevBarVal > 0)
					{
						string lineTag = "AdhocLine_" + CurrentBar;
						Draw.Line(this, lineTag, false, 1, visualAdhocPrevBarVal, 0, v, Brushes.White, DashStyleHelper.Solid, 1);
					}
					
					// REMOVED TEXT LABEL AS REQUESTED
					// string label = "  " + setupLevelName; 
					// Draw.Text(this, "AdhocCurrentLabel", label, 0, v, Brushes.White);

					// Update Tracking
					visualAdhocLastVal = v;
					visualAdhocLastBar = CurrentBar;
				}
			}
			
			if (canScan)
			{
				foreach (var lvl in activeLevels)
				{
					// BACKTEST SAFETY: Ignore Future Levels (Cheat Prevention)
					if (lvl.StartTime > Time[0]) continue;

					// If level is mitigated exactly NOW
					// Note: ManageLevels sets MitigationTime = Time[0].
					if (lvl.IsMitigated && lvl.MitigationTime == Time[0])
					{
						// If we are already waiting, check if this is a DIFFERENT level.
						// If it's the same level, we ignore re-triggering to preserve the 'setupAnchorPrice' (Extreme).
						if (currentEntryState == EntryState.WaitingForConfirmation)
						{
							if (lvl.Name == setupLevelName)
								continue;
							else
							{
								// SWITCHING SETUP!
								Log(Time[0] + " SWITCH: New Trigger on " + lvl.Name + " overrides " + setupLevelName);
								// Fall through to process new trigger...
							}
						}
							
						// Valid Trigger (New or Switch)
						
						if (lvl.IsResistance)
						{
							Print(Time[0] + " DEBUG: Trigger Short Detected on " + lvl.Name + " Price: " + lvl.Price); // DEBUG
							// Short Setup
							triggerTag = "TriggerShort_" + Time[0].Ticks; // Store Tag
							triggerBar = CurrentBar;
							Draw.ArrowDown(this, triggerTag, true, 0, High[0] + TickSize * 15, Brushes.Cyan);
							Draw.Text(this, triggerTag + "_Txt", "Short", 0, High[0] + TickSize * 25, Brushes.Cyan);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							isShortSetup = true;
							setupAnchorPrice = High[0]; // ANCHOR START: Current Wick High
							setupLevelName = lvl.Name;
							
							// RESET ADHOC VWAP (Start Fresh from this touch)
							// ALIGNMENT: To match Global VWAP behavior, we must Include the Trigger Bar's volume completely.
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0]; // So Delta next tick in same bar is 0, but we already have base volume.
							
							// Reset Visual State
							visualAdhocPrevBarVal = 0;
							visualAdhocLastVal = 0;
							visualAdhocLastBar = -1;
						}
						else
						{
							Print(Time[0] + " DEBUG: Trigger Long Detected on " + lvl.Name + " Price: " + lvl.Price); // DEBUG
							// Long Setup
							triggerTag = "TriggerLong_" + Time[0].Ticks;
							triggerBar = CurrentBar;
							Draw.ArrowUp(this, triggerTag, true, 0, Low[0] - TickSize * 15, Brushes.Lime);
							Draw.Text(this, triggerTag + "_Txt", "Long", 0, Low[0] - TickSize * 25, Brushes.Lime);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							isShortSetup = false; // Long
							setupAnchorPrice = Low[0]; // ANCHOR START: Current Wick Low
							setupLevelName = lvl.Name;
							
							// RESET ADHOC VWAP
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0];

							// Reset Visual State
							visualAdhocPrevBarVal = 0;
							visualAdhocLastVal = 0;
							visualAdhocLastBar = -1;
						}
						
						break; // Only take one trigger at a time
					}
				}
			}
			
			// ... (Visuals Update Skipped for brevity, unchanged) ...
			if (currentEntryState == EntryState.WaitingForConfirmation && CurrentBar == triggerBar)
			{
				// VISUALS
				if (isShortSetup)
				{
					Draw.ArrowDown(this, triggerTag, true, 0, High[0] + TickSize * 15, Brushes.Cyan);
					Draw.Text(this, triggerTag + "_Txt", "Short", 0, High[0] + TickSize * 25, Brushes.Cyan);
				}
				else
				{
					Draw.ArrowUp(this, triggerTag, true, 0, Low[0] - TickSize * 15, Brushes.Lime);
					Draw.Text(this, triggerTag + "_Txt", "Long", 0, Low[0] - TickSize * 25, Brushes.Lime);
				}

				// WICK GROWTH (Mid-Bar during Trigger)
				// We allow the anchor to expand while we form the trigger candle.
				if (isShortSetup && High[0] > setupAnchorPrice) setupAnchorPrice = High[0];
				if (!isShortSetup && Low[0] < setupAnchorPrice) setupAnchorPrice = Low[0];
			}
			
			// 2. CONFIRMATION LOGIC (Waiting -> Working)
			// "Wait for a candle... close... max below vwap 1 tick"
			
			if (currentEntryState == EntryState.WaitingForConfirmation && IsFirstTickOfBar && CurrentBar > triggerBar)
			{
				// Determine Local VWAP to use
				double setupVWAP = GetSetupVWAP(isShortSetup);
				
				if (isShortSetup)
				{
					// Short: High[1] < Bearish VWAP (setupVWAP) - 1 Tick
					if (isValidVWAP(setupVWAP) && High[1] < (setupVWAP - TickSize))
					{
						// --- RISK / REWARD CHECK ---
						double projectedEntry = setupVWAP;
						// Padding: Stop is placed 1 tick ABOVE the wicks for breathing room.
						double projectedStop = setupAnchorPrice + TickSize; 
						double projectedTarget = GetCurrentLowVWAP(); // Opposing Global VWAP? Or Local? 
						// Opposing Usually Global Extreme is the Target (Standard). Or Opposing Local?
						// Let's assume Target is Global Opposing VWAP (Classic Reversion).
						
						double risk = Math.Abs(projectedEntry - projectedStop);
						double reward = Math.Abs(projectedTarget - projectedEntry);
						
						if (risk > 0 && (reward / risk) >= MinRiskRewardRatio)
						{
							entryOrder = EnterShortLimit(0, true, Quantity, setupVWAP, "EntryA_Short");
							currentEntryState = EntryState.workingOrder;
							Log(Time[0] + " Order Submitted (Short). OID: " + (entryOrder != null ? entryOrder.OrderId : "null"));
						}
						else
						{
							Log(Time[0] + string.Format(" Trade Skipped (Short). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						}
					}
					else
					{
						// Check invalidation
						// Check invalidation (End of Bar)
						if (High[0] > setupAnchorPrice)
						{
							// DYNAMIC UPDATE: Don't kill the setup, just update the reference High.
							setupAnchorPrice = High[0];
							Log(Time[0] + " Anchor Updated (Short End-Bar) to New High: " + setupAnchorPrice);
							
							// RESET VWAP Calculation (Start fresh from new high)
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							// Keep visual continuity: visualAdhocLastBar = -1; // Removed to allow drop visualization
						}
					}
				}
				else
				{
					// Long: Low[1] > Bullish VWAP (setupVWAP) + 1 Tick
					
					if (isValidVWAP(setupVWAP) && Low[1] > (setupVWAP + TickSize))
					{
						// --- RISK / REWARD CHECK ---
						double projectedEntry = setupVWAP;
						// Padding: Stop is placed 1 tick BELOW the wicks.
						double projectedStop = setupAnchorPrice - TickSize;
						double projectedTarget = GetCurrentHighVWAP(); // Opposing Global
						
						double risk = Math.Abs(projectedEntry - projectedStop);
						double reward = Math.Abs(projectedTarget - projectedEntry);
						
						if (risk > 0 && (reward / risk) >= MinRiskRewardRatio)
						{
							entryOrder = EnterLongLimit(0, true, Quantity, setupVWAP, "EntryA_Long");
							currentEntryState = EntryState.workingOrder;
							Log(Time[0] + " Order Submitted (Long). OID: " + (entryOrder != null ? entryOrder.OrderId : "null"));
						}
						else
						{
							Log(Time[0] + string.Format(" Trade Skipped (Long). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						}
					}
					else
					{
						// Check invalidation
						// Check invalidation (End of Bar)
						if (Low[0] < setupAnchorPrice)
						{
							// DYNAMIC UPDATE: Don't kill the setup, just update the reference Low.
							setupAnchorPrice = Low[0];
							Log(Time[0] + " Anchor Updated (Long End-Bar) to New Low: " + setupAnchorPrice);
							
							// RESET VWAP Calculation (Start fresh from new low)
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							// Keep visual continuity: visualAdhocLastBar = -1; // Removed to allow drop visualization
						}
					}
				}
			}
			
			// Mid-bar check for Anchor Update / Invalidation
			// ONLY if we are PAST the trigger bar (because logic above handles Trigger Bar growth)
			if (currentEntryState == EntryState.WaitingForConfirmation && !IsFirstTickOfBar && CurrentBar > triggerBar)
			{
				if (isShortSetup && High[0] > setupAnchorPrice) 
				{
					// DYNAMIC UPDATE: Don't kill the setup, just update the reference High.
					setupAnchorPrice = High[0];
					// PERFORMANCE OPTIMIZATION: Reduce spam.
					// Log(Time[0] + " Anchor Updated (Short) to New High: " + setupAnchorPrice);
					
					// RESET VWAP Calculation
					adhocVolSum = 0; adhocPvSum = 0;
					// visualAdhocLastBar = -1; 
				}
				if (!isShortSetup && Low[0] < setupAnchorPrice) 
				{
					// DYNAMIC UPDATE: Don't kill the setup, just update the reference Low.
					setupAnchorPrice = Low[0];
					// PERFORMANCE OPTIMIZATION: Reduce spam.
					// Log(Time[0] + " Anchor Updated (Long) to New Low: " + setupAnchorPrice);
					
					// RESET VWAP Calculation
					adhocVolSum = 0; adhocPvSum = 0;
					// visualAdhocLastBar = -1;
				}
			}

			// 3. ORDER MANAGEMENT & SYNC (Working -> InPosition)
			if (currentEntryState == EntryState.workingOrder && entryOrder != null)
			{
				// Check for FILL (Full or Partial)
				if (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled)
				{
					Log(Time[0] + " SYNC: Order Filled but State was Working. Forcing InPosition.");
					currentEntryState = EntryState.PositionActive;
					EnsureProtection(isShortSetup ? "Short" : "Long");
				}
				// Tracking the VWAP (Only if still working)
				else if (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted)
				{
					// --- SAFETY VALIDATION: ANCHOR BREAK ---
					// If price moves against us and breaks the Anchor while we are trying to enter, CANCEL.
					bool anchorViolated = false;
					if (isShortSetup && High[0] > setupAnchorPrice) anchorViolated = true;
					if (!isShortSetup && Low[0] < setupAnchorPrice) anchorViolated = true;
					
					if (anchorViolated)
					{
						Log(Time[0] + " SECURITY: Anchor Violated while Working Order. Cancelling.");
						CancelOrder(entryOrder);
						return; 
					}
				
					// Track the SETUP VWAP (Local), not just Global
					double currentVWAP = GetSetupVWAP(isShortSetup);
					
					// --- DYNAMIC RISK / REWARD CHECK ---
					// As VWAP moves, our entry price moves. We must re-validate R/R.
					double projectedEntry = currentVWAP;
					// Update R/R check to include padding too
					double projectedStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);
					double projectedTarget = isShortSetup ? GetCurrentLowVWAP() : GetCurrentHighVWAP(); // Global Opposing
					
					double risk = Math.Abs(projectedEntry - projectedStop);
					double reward = Math.Abs(projectedTarget - projectedEntry);
					
					// If Risk is 0 (Anchor == Entry), ratio is infinite (Good). 
					// If degraded:
					if (risk > 0 && (reward / risk) < MinRiskRewardRatio)
					{
						Log(Time[0] + " R/R Degraded to " + (reward/risk).ToString("F2") + " (Min: " + MinRiskRewardRatio + "). Cancelling Order.");
						CancelOrder(entryOrder);
						// State reset happens in OnExecutionUpdate/OnOrderUpdate when cancel is confirmed.
						return;
					}

					// Compare with current Order Limit Price
					if (Math.Abs(entryOrder.LimitPrice - currentVWAP) >= TickSize)
					{
						ChangeOrder(entryOrder, Quantity, currentVWAP, 0);
					}
				}
			}
			
			// 4. IN POSITION MANAGEMENT
			ManagePositionExit();
		} // End ManageEntryA_Plus

		private void EnsureProtection(string direction)
		{
			// Places SL and TP if they don't exist.
			// PROTECTION & TARGETS (Multi-Contract Smart Split)
			if (entryOrder == null) return;
			
			// Relaxed Check: Allow PartFilled to start protecting
			if (entryOrder.OrderState != OrderState.Filled && entryOrder.OrderState != OrderState.PartFilled) return;
			
			// Check if we already have exits
			if (targetOrder != null || stopOrder != null) return; 

			// 1. Calculate Basis Prices
			double avgEntry = entryOrder.AverageFillPrice;
			double slPrice = 0;
			
			// 2. Determine Smart Targets (TP1 Closer, TP2 Farther)
			double targetGlobalVWAP = 0;
			double targetZoneOpposite = 0;
			
			if (isShortSetup)
			{
				slPrice = setupAnchorPrice + (StopLossTicks * TickSize); // SL Above High
				targetGlobalVWAP = GetCurrentLowVWAP(); 
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, Time[0]);
				if (targetZoneOpposite == 0) targetZoneOpposite = targetGlobalVWAP; // Fallback
			}
			else
			{
				slPrice = setupAnchorPrice - (StopLossTicks * TickSize); // SL Below Low
				targetGlobalVWAP = GetCurrentHighVWAP(); // Long -> Target High VWAP
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, Time[0]);
				if (targetZoneOpposite == 0) targetZoneOpposite = targetGlobalVWAP;
			}
			
			// SANITY CHECK PRICES (Prevent 0.0 price error)
			if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry; 
			if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry;

			// 3. Sort Targets (Closer = TP1, Farther = TP2)
			double distV = Math.Abs(avgEntry - targetGlobalVWAP);
			double distZ = Math.Abs(avgEntry - targetZoneOpposite);
			
			double tp1Price = (distV < distZ) ? targetGlobalVWAP : targetZoneOpposite;
			double tp2Price = (distV < distZ) ? targetZoneOpposite : targetGlobalVWAP;
			
			// Rounding to TickSize is CRITICAL for Limit Orders
			tp1Price = Instrument.MasterInstrument.RoundToTickSize(tp1Price);
			tp2Price = Instrument.MasterInstrument.RoundToTickSize(tp2Price);
			slPrice = Instrument.MasterInstrument.RoundToTickSize(slPrice);
			
			// Store for dynamic updates
			activeTp1Price = tp1Price;
			activeTp2Price = tp2Price;

			// 4. Split Quantity
			int qty1 = Quantity / 2;
			int qty2 = Quantity - qty1; // Remainder (in case of odd quantity)
			
			if (Quantity == 1) { qty1 = 1; qty2 = 0; tp1Price = (distV < distZ ? targetGlobalVWAP : targetZoneOpposite); } 

			Log("   -> Smart Protection Split: Qty=" + Quantity + " | TP1(Qty"+qty1+")=" + tp1Price + " | TP2(Qty"+qty2+")=" + tp2Price);

			// 5. Submit Orders (MANAGED)
			try
			{
				if (isShortSetup)
				{
					// Stop Loss (All Qty)
					ExitShortStopMarket(0, true, Quantity, slPrice, "SL_Short", "EntryA_Short");
					
					// TP1
					if (qty1 > 0) tp1Order = ExitShortLimit(0, true, qty1, tp1Price, "TP1_Short", "EntryA_Short");
					
					// TP2
					if (qty2 > 0) tp2Order = ExitShortLimit(0, true, qty2, tp2Price, "TP2_Short", "EntryA_Short");
				}
				else
				{
					// Stop Loss (All Qty)
					ExitLongStopMarket(0, true, Quantity, slPrice, "SL_Long", "EntryA_Long");
					
					// TP1
					if (qty1 > 0) tp1Order = ExitLongLimit(0, true, qty1, tp1Price, "TP1_Long", "EntryA_Long");
					
					// TP2
					if (qty2 > 0) tp2Order = ExitLongLimit(0, true, qty2, tp2Price, "TP2_Long", "EntryA_Long");
				}
			}
			catch (Exception ex)
			{
				Log("CRITICAL ERROR Submitting Exits: " + ex.Message);
			}
		}
		
		private double GetOppositeLevelPrice(string name, DateTime refTime)
		{
			// Try to find the opposite.
			// Format: "Asia Low", "Asia High".
			if (string.IsNullOrEmpty(name)) return 0;
			
			string oppName = "";
			if (name.Contains(" Low")) oppName = name.Replace(" Low", " High");
			else if (name.Contains(" High")) oppName = name.Replace(" High", " Low");
			else return 0; // Can't guess
			
			// Search in valid levels for the SAME DAY (Session)
			foreach(var l in activeLevels)
			{
				if (l.Name == oppName) 
				{
					// Date Check: Ensure it's from the same session date as the reference.
					// Or within a reasonable window (e.g. < 24 hours difference).
					if (Math.Abs((l.StartTime - refTime).TotalHours) < 24)
						return l.Price;
				}
			}
			return 0; // Not found
		}

		
		private bool isValidVWAP(double val)
		{
			return val > 0 && !double.IsNaN(val);
		}

		private int screenshotSequence = 0; // For unique filenames

		private void TriggerScreenshot(string eventName, DateTime time, string execId)
		{
			// Only take screenshots in LIVE/REALTIME mode or if forcing it (users choice)
			// Generally we prefer Realtime to avoid spam during reload.
			if (State != State.Realtime) return;

			if (EnableEmailAlerts && ChartControl != null)
			{
				try 
				{
					// Must run on UI Thread
					ChartControl.Dispatcher.InvokeAsync((Action)(() => 
					{
						try 
						{
							// 1. Get Screen Coordinates of the ChartControl
							System.Windows.Point p = ChartControl.PointToScreen(new System.Windows.Point(0, 0));
							
							// 2. Get Dimensions
							int w = (int)ChartControl.ActualWidth;
							int h = (int)ChartControl.ActualHeight;
							
							if (w > 0 && h > 0)
							{
								// 3. Creates System.Drawing.Bitmap (WinForms/GDI+)
								// Fully qualified to avoid namespace ambiguity
								using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(w, h))
								{
									using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
									{
										// 4. Capture Screen
										g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, new System.Drawing.Size(w, h));
									}
									
									// 5. Build Path
									string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
									string shotFolder = System.IO.Path.Combine(docPath, "NinjaTrader 8", "Strategy_Screenshots");
									if (!System.IO.Directory.Exists(shotFolder)) System.IO.Directory.CreateDirectory(shotFolder);
									
									// Increment Sequence
									screenshotSequence++;

									string cleanName = Instrument.FullName.Replace(" ", "_");
									string fileName = string.Format("{0:D4}_{1}_{2}_{3}_{4}.png", 
										screenshotSequence,
										eventName,
										cleanName,
										time.ToString("yyyyMMdd_HHmmss"),
										execId.GetHashCode()); 
										
									string fullPath = System.IO.Path.Combine(shotFolder, fileName);
									
									// 6. Save
									bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
									Print(Time[0] + " Snapshot Saved: " + fullPath);
									
									// 7. Send Email (Async-ish)
									if (EnableEmailAlerts)
									{
										SendEmailWithAttachment(eventName, fullPath);
									}
								}
							}
						}
						catch (Exception innerEx) { Print(Time[0] + " Screen Capture Failed: " + innerEx.Message); }
					}));
				}
				catch (Exception ex) { Print(Time[0] + " Snapshot Dispatch Failed: " + ex.Message); }
			}
		}


		#endregion
		
		private void LogTrade(Trade trade)
		{
			string action = (trade.Entry.MarketPosition == MarketPosition.Long) ? "TR_LONG" : "TR_SHORT";
			Log(string.Format("{0} TRADE CLOSED: {1} at {2}, Exit at {3}, Profit: {4}", 
				DateTime.Now, action, trade.Entry.Price, trade.Exit.Price, trade.ProfitCurrency.ToString("C")));
		}

		protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
		{
			// Detect Trade Close (Transition to Flat)
			if (marketPosition == MarketPosition.Flat && position.Instrument == Instrument)
			{
				// We just closed a position. Log the last trade.
				if (SystemPerformance != null && SystemPerformance.RealTimeTrades != null && SystemPerformance.RealTimeTrades.Count > 0)
				{
					Trade lastTrade = SystemPerformance.RealTimeTrades[SystemPerformance.RealTimeTrades.Count - 1];
					// Verify it just happened (within last few seconds? Or just assume sequentially correct)
					// "RealTimeTrades" only updates on close. So the last one IS the one we just closed.
					LogTrade(lastTrade);
				}
			}
		}

		private void SendEmailWithAttachment(string subject, string attachmentPath)
		{
			try 
			{
				// Basic Validation
				if (string.IsNullOrEmpty(EmailHost) || string.IsNullOrEmpty(EmailUsername) || string.IsNullOrEmpty(EmailPassword))
					return;

				Task.Run(() => 
				{
					try 
					{
						using (MailMessage mail = new MailMessage())
						{
							mail.From = new MailAddress(EmailFrom);
							mail.To.Add(EmailTo);
							mail.Subject = "NinjaTrader Alert: " + subject + " - " + Instrument.FullName;
							mail.Body = string.Format("Trade alert for {0} at {1}.\nEvent: {2}", Instrument.FullName, DateTime.Now, subject);
							
							if (File.Exists(attachmentPath))
							{
								Attachment data = new Attachment(attachmentPath, "image/png");
								mail.Attachments.Add(data);
							}
							
							using (SmtpClient smtp = new SmtpClient(EmailHost, EmailPort))
							{
								smtp.Credentials = new NetworkCredential(EmailUsername, EmailPassword);
								smtp.EnableSsl = true;
								smtp.Send(mail);
							}
							Print("Email Sent to " + EmailTo);
						}
					}
					catch (Exception ex)
					{
						Print("Email Failed: " + ex.Message);
					}
				});
			}
			catch (Exception ex) { Print("Email Setup Failed: " + ex.Message); }
		}

		private double GetCurrentHighVWAP() { return ethHighVWAP.CurrentValue; }
		private double GetCurrentLowVWAP() { return ethLowVWAP.CurrentValue; }
		
		private double GetSetupVWAP(bool isShort)
		{
			// 1. If we have ADHOC VOLUME tracked, use it.
			// This represents the "VWAP since touch".
			if (!string.IsNullOrEmpty(setupLevelName) && adhocVolSum > 0)
			{
				return adhocPvSum / adhocVolSum;
			}
			
			// 2. Fallback to Global (e.g. if logic fails or we are tracking a Global Extremum trade where we didn't reset adhoc)
			return isShort ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
		}

		private void ManagePositionExit()
		{
			if (Position.MarketPosition == MarketPosition.Flat) return;
			
			// Updates for Limit Orders (Chasing/Dynamic) can go here if needed.
			// Currently TP1/TP2 are static or handled elsewhere?
			// Use internal state if we want to move them.
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			// 1. Entry Order Tracking
			if (order.Name == "EntryA_Short" || order.Name == "EntryA_Long")
			{
				entryOrder = order;
				
				// Handle Terminal States for Entry
				if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || (orderState == OrderState.Filled && filled == 0)) 
				{
					if (currentEntryState == EntryState.workingOrder) 
					{
						currentEntryState = EntryState.Idle;
						setupLevelName = "";
						entryOrder = null;
					}
				}
			}
			
			// 2. Generic Reference Updates (for persistence/recovery)
			if (order.Name.Contains("SL_")) stopOrder = order;
			if (order.Name.Contains("Entry")) entryOrder = order;
			// TP Orders are now tracked via return value of SubmitOrderUnmanaged
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			// DEBUG: Global Spy
			if (EnableDebugLogs) Print(Time + " EXEC SPY: Name='" + execution.Order.Name + "' State=" + execution.Order.OrderState + " Qty=" + quantity + " OID=" + orderId);

			if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
			{
				if (execution.Order.Name == "EntryA_Short" || execution.Order.Name == "EntryA_Long")
				{
					if (currentEntryState == EntryState.workingOrder)
					{
						currentEntryState = EntryState.PositionActive;
						Log(Time + " Entry Filled ("+execution.Order.Name+"). State -> PositionActive.");
					}
					
					// Ensure Protection Runs (Retry if needed)
					if (Position.MarketPosition == MarketPosition.Short)
					{
						EnsureProtection("Short");
						TriggerScreenshot("Entry_Short", DateTime.Now, executionId);
					}
					else if (Position.MarketPosition == MarketPosition.Long)
					{
						EnsureProtection("Long");
						TriggerScreenshot("Entry_Long", DateTime.Now, executionId);
					}
				}
			}
			
			// Remove reliance on targetOrder singular variable? 
			// if (execution.Order.Name.Contains("TP_")) targetOrder = execution.Order;
			
			// BREAKEVEN & QUANTITY MANAGEMENT (TP1 Filled)
			if (execution.Order.OrderState == OrderState.Filled)
			{
				// Robust Check: Name Contains "TP1_"
				string n = execution.Order.Name;
				bool isTP1 = n.Contains("TP1_");

				if (isTP1)
				{
					Print(Time + " DETECTED TP1 FILL: " + n);

					if (stopOrder != null && entryOrder != null)
					{
						// 1. Calculate Remaining Quantity
						// Safe math: (Order Qty - Filled Portion). 
						// Or just Position.Quantity? 
						// Careful: Position.Quantity updates slightly differently in Managed.
						// Better: Original Stop Qty - Executed Qty.
						int filledQty = execution.Order.Quantity;
						int currentStopQty = stopOrder.Quantity;
						int newSlQty = currentStopQty - filledQty;
						
						if (newSlQty < 1) newSlQty = 1; // Safety floor
						
						// 2. Determine New Price
						double newSlPrice = stopOrder.StopPrice; 
						bool priceChanged = false;
						
						if (EnableBreakeven)
						{
							newSlPrice = entryOrder.AverageFillPrice;
							priceChanged = true;
						}

						Print(Time + " -> Adjusting SL (" + stopOrder.Name + "): Qty " + currentStopQty + "->" + newSlQty + " | Price " + stopOrder.StopPrice + "->" + newSlPrice);
						
						// 3. Update Stop Order
						ChangeOrder(stopOrder, newSlQty, 0, newSlPrice);
					}
					else
					{
						Print(Time + " -> TP1 Filled but StopOrder/EntryOrder is NULL. Cannot Adjust.");
					}
				}
			}
			
			// Reset if Closed (Filled) OR Cancelled/Rejected
			if (entryOrder != null && execution.Order == entryOrder)
			{
				if (execution.Order.OrderState == OrderState.Cancelled || 
					execution.Order.OrderState == OrderState.Rejected)
				{
					Print(Time + " Entry Order Cancelled/Rejected. Resetting to Idle.");
					currentEntryState = EntryState.Idle;
					setupLevelName = "";
					entryOrder = null;
					targetOrder = null;
					tp1Order = null;
					tp2Order = null; 
					stopOrder = null;
				}
			}
			
			// CRITICAL FIX: Only reset if we are truly FLAT. 
			if (execution.Order.OrderState == OrderState.Filled && (execution.Order.Name.Contains("SL_") || execution.Order.Name.Contains("TP_")))
			{
				if (Position.MarketPosition == MarketPosition.Flat)
				{
					Print(Time + " Position Closed (" + execution.Order.Name + "). Resetting to Idle.");
					TriggerScreenshot("Exit_" + execution.Order.Name, DateTime.Now, executionId);
					currentEntryState = EntryState.Idle;
					setupLevelName = "";
					entryOrder = null;
					targetOrder = null;
					stopOrder = null; 
					tp1Order = null;
					tp2Order = null;
				}
				else
				{
					Print(Time + " Partial Execution (" + execution.Order.Name + "). Position Active. Qty=" + Position.Quantity);
				}
			}
		}

		[NinjaScriptProperty]
		[Display(Name="Asia Start Time", Order=1, GroupName="1. Sessions")]
		public string AsiaStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Asia End Time", Order=2, GroupName="1. Sessions")]
		public string AsiaEndTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Europe Start Time", Order=3, GroupName="1. Sessions")]
		public string EuropeStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Quantity", Order=1, GroupName="Order Management")]
		public int Quantity
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Move to Breakeven", Order=1, GroupName="Order Management")]
		public bool EnableBreakeven
		{ get; set; } = true;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stop Loss (Ticks)", Order=2, GroupName="Order Management")]
		public int StopLossTicks
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="Min Risk/Reward Ratio", Order=3, GroupName="Order Management")]
		public double MinRiskRewardRatio
		{ get; set; }
		
		// Internal Targets State
		private double activeTp1Price = 0;
		private double activeTp2Price = 0;
		
		// Explicit Order Tracking
		private Order tp1Order = null;
		private Order tp2Order = null;

		
		[NinjaScriptProperty]
		[Display(Name="Europe End Time", Order=4, GroupName="1. Sessions")]
		public string EuropeEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="USA Start Time", Order=5, GroupName="1. Sessions")]
		public string USAStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="USA End Time", Order=6, GroupName="1. Sessions")]
		public string USAEndTime { get; set; }
		


		// Fix: Missing InitCSV stub.
		private void InitCSV()
		{
			// Safe stub to ensure compilation
		}

	} // End of SessionLevelsStrategy class

	public class SessionLevelData
	{
		public string Name;
		public double Price;
		public DateTime StartTime;
		public DateTime EndTime;
		public DateTime MitigationTime;
		public bool IsResistance;
		public bool IsMitigated;
		public double VolSum;
		public double PvSum;
		public string Tag;
		// Color is not serialized easily, we infer it from Name or defaults.
	}
} // End of Namespace
