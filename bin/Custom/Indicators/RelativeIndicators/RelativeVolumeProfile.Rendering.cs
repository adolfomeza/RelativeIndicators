#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows.Media;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class RelativeVolumeProfile
	{
		#region Cached SharpDX Resources

		private SharpDX.Direct2D1.SolidColorBrush _dxPOCBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxVABrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxOutsideBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxPOCLineBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxVALineBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxTouchedBrush;
		private SharpDX.Direct2D1.StrokeStyle     _dxDashStyle;
		private SharpDX.DirectWrite.Factory        _dwFactory;
		private SharpDX.DirectWrite.TextFormat      _dwTextFormat;

		// TPO: brushes full-opacity for text rendering
		private SharpDX.Direct2D1.SolidColorBrush _dxTPO_POCBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxTPO_VABrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxTPO_OutBrush;

		// Anchored VWAP brushes
		private SharpDX.Direct2D1.SolidColorBrush _dxHighVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxLowVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _dxArchivedVwapBrush;

		#endregion

		#region OnRenderTargetChanged

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();

			DisposeCachedBrushes();

			if (RenderTarget != null)
			{
				byte alpha = (byte)(255 * HistogramOpacity / 100.0);

				_dxPOCBrush     = CreateBrushWithAlpha(POCColor, alpha);
				_dxVABrush      = CreateBrushWithAlpha(ValueAreaColor, alpha);
				_dxOutsideBrush = CreateBrushWithAlpha(OutsideVAColor, alpha);
				_dxPOCLineBrush = CreateBrushFromMedia(POCLineColor);
				_dxVALineBrush  = CreateBrushFromMedia(VALineColor);
				_dxTouchedBrush = CreateBrushFromMedia(TouchedLineColor);

				// TPO: full-opacity brushes for letter rendering
				_dxTPO_POCBrush = CreateBrushFromMedia(POCColor);
				_dxTPO_VABrush  = CreateBrushFromMedia(ValueAreaColor);
				_dxTPO_OutBrush = CreateBrushFromMedia(OutsideVAColor);

				// Anchored VWAP brushes
				_dxHighVwapBrush     = CreateBrushFromMedia(HighVwapColor);
				_dxLowVwapBrush      = CreateBrushFromMedia(LowVwapColor);
				_dxArchivedVwapBrush = CreateBrushWithAlpha(ArchivedVwapColor, 128);

				var dashProps = new SharpDX.Direct2D1.StrokeStyleProperties
				{
					DashStyle = SharpDX.Direct2D1.DashStyle.Dash,
					DashCap   = SharpDX.Direct2D1.CapStyle.Flat
				};
				_dxDashStyle = new SharpDX.Direct2D1.StrokeStyle(
					NinjaTrader.Core.Globals.D2DFactory, dashProps);

				_dwFactory = new SharpDX.DirectWrite.Factory();
				_dwTextFormat = new SharpDX.DirectWrite.TextFormat(
					_dwFactory, "Arial", 10);
			}
		}

		#endregion

		#region OnRender

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!_isLicensed)
			{
				RenderLicenseOverlay(chartControl, chartScale);
				return;
			}
			if (Bars == null || chartControl == null || chartScale == null) return;
			if (RenderTarget == null || ChartBars == null) return;
			if (_allProfiles == null || _allProfiles.Count == 0) return;

			// Restore composites from saved recipes (runs once after rebuild/F5)
			if (!_compositesRestored && _allProfiles.Count >= 2)
				RestoreComposites();

			// Clear hit-test cache for this frame
			_profileBounds?.Clear();

			// Use ChartBars count as the limit for bar scanning (avoids empty future bars)
			int lastBarIdx = _lastRealBar > 0 ? _lastRealBar : (ChartBars.Bars.Count - 1);

			for (int p = 0; p < _allProfiles.Count; p++)
			{
				var profile = _allProfiles[p];
				if (profile.Levels.Count == 0) continue;
				if (profile.POCVolume <= 0) continue;

				int profEnd = profile.IsActive ? profile.LastVolumeBarIdx : profile.EndBarIdx;

				// When ExtendLines is on, a historical profile's extension may reach
				// into the visible area even though the profile itself is off-screen left.
				if (profEnd < ChartBars.FromIndex)
				{
					if (ExtendLines && !profile.IsActive)
					{
						// Check if any level's extension reaches the visible area
						bool anyVisible = false;
						double[] levels = { profile.POC, profile.VAH, profile.VAL };
						foreach (double lvl in levels)
						{
							if (lvl <= 0) continue;
							int touch = FindFirstTouchBar(lvl, profEnd + 1, lastBarIdx);
							// Extension ends at touch (or lastBarIdx if virgin)
							int extEnd = (touch >= 0) ? touch : lastBarIdx;
							if (extEnd >= ChartBars.FromIndex)
							{
								anyVisible = true;
								break;
							}
						}
						if (!anyVisible) continue;
					}
					else
					{
						continue;
					}
				}

				if (profile.StartBarIdx > ChartBars.ToIndex) continue;

				if (!profile.IsActive && !ShowHistoricalProfiles) continue;

				RenderProfile(profile, chartControl, chartScale);
			}
		}

		#endregion

		#region RenderProfile

		private void RenderProfile(VolumeProfileSession profile, ChartControl chartControl, ChartScale chartScale)
		{
			// Use LastVolumeBarIdx for active profiles to avoid extending beyond the actual session data
			int profileEndBar = profile.IsActive ? profile.LastVolumeBarIdx : profile.EndBarIdx;
			if (profileEndBar <= 0) profileEndBar = profile.StartBarIdx;

			// Determine anchor X position
			int anchorBarIdx;
			if (HistogramSideParam == HistogramSide.Right)
			{
				anchorBarIdx = profileEndBar;
			}
			else
			{
				anchorBarIdx = profile.StartBarIdx;
			}

			// Clamp to valid range for GetXByBarIndex
			int clampedAnchor = Math.Max(ChartBars.FromIndex, Math.Min(anchorBarIdx, ChartBars.ToIndex));
			float anchorX = chartControl.GetXByBarIndex(ChartBars, clampedAnchor);

			// Adjust X if the real anchor is outside visible range
			if (anchorBarIdx < ChartBars.FromIndex)
			{
				float barDist = chartControl.Properties.BarDistance;
				anchorX -= (ChartBars.FromIndex - anchorBarIdx) * barDist;
			}
			else if (anchorBarIdx > ChartBars.ToIndex)
			{
				float barDist = chartControl.Properties.BarDistance;
				anchorX += (anchorBarIdx - ChartBars.ToIndex) * barDist;
			}

			// Calculate session width in pixels for percentage-based histogram
			int sessionBars = profileEndBar - profile.StartBarIdx;
			float sessionWidthPx = Math.Max(20f, sessionBars * chartControl.Properties.BarDistance);
			float maxHistPx = sessionWidthPx * HistogramMaxWidth / 100f;

			// Visible price range for culling
			double visibleMinPrice = chartScale.MinValue;
			double visibleMaxPrice = chartScale.MaxValue;

			double levelStep = TickSize * TicksPerLevel;

			// Calculate pixel height for one level with visible gap between rows
			float yTop = chartScale.GetYByValue(visibleMaxPrice);
			float yBot = chartScale.GetYByValue(visibleMaxPrice - levelStep);
			float levelPixels = Math.Abs(yBot - yTop);
			float gapPx = Math.Max(1.0f, levelPixels * 0.15f);   // 15% gap between rows
			float barHeight = Math.Max(1.0f, levelPixels - gapPx);

			// Draw histogram bars or TPO letters — track bounding box for hit testing
			float boundsMinY = float.MaxValue;
			float boundsMaxY = float.MinValue;
			float boundsMaxWidth = 0f;
			float boundsMinX = float.MaxValue;  // for extended TPO: track leftmost X
			float boundsMaxX = float.MinValue;  // for extended TPO: track rightmost X

			bool isTPO = ProfileType == ProfileDataType.TPO;
			bool tpoExtended = isTPO && _tpoViewMode == TpoViewMode.Extended;

			// For TPO mode: calculate font/letter sizing
			float tpoFontSize = 0f;
			float tpoLetterW  = 0f;
			float tpoRowH     = 0f;
			float tpoStartX   = anchorX; // used in Compact mode
			float tpoBarDist  = chartControl.Properties.BarDistance;
			SharpDX.DirectWrite.TextFormat tpoFormat = null;
			if (isTPO && _dwFactory != null)
			{
				if (tpoExtended)
				{
					// Extended: font size based on the LARGER of bar distance or level height
					// so letters stay readable even when bars are narrow but levels are tall
					float sizeFromBar = tpoBarDist * 0.7f;
					float sizeFromLevel = levelPixels * 0.85f;
					tpoFontSize = Math.Max(7f, Math.Min(Math.Max(sizeFromBar, sizeFromLevel), 16f));
					tpoLetterW  = tpoFontSize * 0.65f; // monospace char width ≈ 0.6×fontSize
				}
				else
				{
					// Compact: letters stack from profile start, width fills session
					int clampedStart = Math.Max(ChartBars.FromIndex, Math.Min(profile.StartBarIdx, ChartBars.ToIndex));
					tpoStartX = chartControl.GetXByBarIndex(ChartBars, clampedStart);
					if (profile.StartBarIdx < ChartBars.FromIndex)
						tpoStartX -= (ChartBars.FromIndex - profile.StartBarIdx) * tpoBarDist;

					int maxTpoCount = 1;
					foreach (var kvp2 in profile.Levels)
					{
						if (kvp2.Value.TpoPeriods != null && kvp2.Value.TpoPeriods.Count > maxTpoCount)
							maxTpoCount = kvp2.Value.TpoPeriods.Count;
					}

					tpoLetterW = Math.Max(3f, sessionWidthPx / maxTpoCount);
					tpoLetterW = Math.Min(tpoLetterW, 14f);
					tpoFontSize = Math.Max(6f, Math.Min(tpoLetterW / 0.65f, 16f));
				}

				// Cap font size so letters never overlap vertically between levels
				float maxFontForLevel = Math.Max(5f, levelPixels - 1f);
				if (tpoFontSize > maxFontForLevel)
				{
					tpoFontSize = maxFontForLevel;
					tpoLetterW  = tpoFontSize * 0.65f;
				}

				tpoRowH = tpoFontSize + 1f;
				tpoFormat = new SharpDX.DirectWrite.TextFormat(_dwFactory, "Consolas",
					SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, tpoFontSize);
			}

			try
			{
			foreach (var kvp in profile.Levels)
			{
				double price = kvp.Value.Price;

				if (price < visibleMinPrice - levelStep || price > visibleMaxPrice + levelStep)
					continue;

				long vol = kvp.Value.Volume;
				if (vol <= 0) continue;

				float y = chartScale.GetYByValue(price);

				// Select brush based on zone
				bool isPOC = Math.Abs(price - profile.POC) < levelStep * 0.5;
				bool insideVA = price >= profile.VAL && price <= profile.VAH;

				SharpDX.Direct2D1.SolidColorBrush brush;
				if (isTPO)
				{
					// TPO: full-opacity brushes for legible text
					if (isPOC)
						brush = _dxTPO_POCBrush;
					else if (insideVA)
						brush = _dxTPO_VABrush;
					else
						brush = _dxTPO_OutBrush;
				}
				else
				{
					// Volume: semi-transparent histogram brushes
					if (isPOC)
						brush = _dxPOCBrush;
					else if (insideVA)
						brush = _dxVABrush;
					else
						brush = _dxOutsideBrush;
				}

				if (brush == null) continue;

				if (isTPO && tpoFormat != null && kvp.Value.TpoPeriods != null)
				{
					// === TPO MODE: Draw letters ===
					var sortedPeriods = new List<int>(kvp.Value.TpoPeriods);
					sortedPeriods.Sort();

					float minXDrawn = float.MaxValue;
					float maxXDrawn = float.MinValue;
					int compactIdx = 0; // sequential index for compact stacking

					foreach (int pi in sortedPeriods)
					{
						char letter;
						if (pi < 26)
							letter = (char)('A' + pi);
						else
							letter = (char)('a' + (pi - 26));

						float xPos;
						if (tpoExtended && profile.TpoPeriodBarMap != null)
						{
							// Extended: position letter at actual chart bar
							int barIdx;
							if (!profile.TpoPeriodBarMap.TryGetValue(pi, out barIdx))
								continue;

							if (barIdx >= ChartBars.FromIndex && barIdx <= ChartBars.ToIndex)
								xPos = chartControl.GetXByBarIndex(ChartBars, barIdx);
							else if (barIdx < ChartBars.FromIndex)
								xPos = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex)
									 - (ChartBars.FromIndex - barIdx) * tpoBarDist;
							else
								xPos = chartControl.GetXByBarIndex(ChartBars, ChartBars.ToIndex)
									 + (barIdx - ChartBars.ToIndex) * tpoBarDist;
						}
						else
						{
							// Compact: stack letters tightly, no gaps
							xPos = tpoStartX + compactIdx * tpoLetterW;
							compactIdx++;
						}

						if (xPos < minXDrawn) minXDrawn = xPos;
						if (xPos > maxXDrawn) maxXDrawn = xPos;

						using (var layout = new SharpDX.DirectWrite.TextLayout(
							_dwFactory, letter.ToString(), tpoFormat, tpoLetterW + 4, tpoRowH + 2))
						{
							RenderTarget.DrawTextLayout(
								new SharpDX.Vector2(xPos, y - tpoRowH / 2),
								layout, brush);
						}
					}

					// Track bounding box
					float barTop = y - tpoRowH / 2;
					float barBot = y + tpoRowH / 2;
					if (barTop < boundsMinY) boundsMinY = barTop;
					if (barBot > boundsMaxY) boundsMaxY = barBot;
					float totalWidth = (maxXDrawn > minXDrawn) ? (maxXDrawn - minXDrawn + tpoLetterW) : tpoLetterW;
					if (totalWidth > boundsMaxWidth) boundsMaxWidth = totalWidth;
					// Track global X bounds for extended TPO hit testing
					if (minXDrawn < boundsMinX) boundsMinX = minXDrawn;
					if (maxXDrawn + tpoLetterW > boundsMaxX) boundsMaxX = maxXDrawn + tpoLetterW;
				}
				else
				{
					// === VOLUME MODE: Draw histogram bars ===
					float widthRatio = (float)vol / profile.POCVolume;
					float barWidth = widthRatio * maxHistPx;

					if (barWidth < 0.5f) continue;

					// Track bounding box
					float barTop = y - barHeight / 2;
					float barBot = y + barHeight / 2;
					if (barTop < boundsMinY) boundsMinY = barTop;
					if (barBot > boundsMaxY) boundsMaxY = barBot;
					if (barWidth > boundsMaxWidth) boundsMaxWidth = barWidth;

					SharpDX.RectangleF rect = new SharpDX.RectangleF(
						anchorX,
						y - barHeight / 2,
						barWidth,
						barHeight);

					RenderTarget.FillRectangle(rect, brush);
				}
			}
			}
			finally
			{
				if (tpoFormat != null)
					tpoFormat.Dispose();
			}

			// Cache bounding box for hit testing (context menu)
			if (_profileBounds != null && boundsMinY < boundsMaxY)
			{
				float bboxX, bboxW;
				if (tpoExtended && boundsMinX < boundsMaxX)
				{
					// Extended TPO: use actual drawn letter positions
					bboxX = boundsMinX;
					bboxW = boundsMaxX - boundsMinX;
				}
				else
				{
					bboxX = anchorX;
					bboxW = boundsMaxWidth;
				}

				_profileBounds[profile] = new SharpDX.RectangleF(
					bboxX,
					boundsMinY,
					bboxW,
					boundsMaxY - boundsMinY);
			}

			// Draw key level lines
			RenderKeyLevelLines(profile, chartControl, chartScale);

			// Draw anchored VWAPs
			if (ShowAnchoredVWAP)
				RenderProfileVwaps(profile, chartControl, chartScale);
		}

		#endregion

		#region RenderKeyLevelLines

		private void RenderKeyLevelLines(VolumeProfileSession profile, ChartControl chartControl, ChartScale chartScale)
		{
			int leftIdx  = profile.StartBarIdx;
			int profileEndBar = profile.IsActive ? profile.LastVolumeBarIdx : profile.EndBarIdx;
			if (profileEndBar <= 0) profileEndBar = leftIdx;
			int rightIdx = profileEndBar;

			// Extension: extend level lines, stopping at first price touch
			// Use _lastRealBar to avoid scanning empty/future bars beyond actual data
			int lastBarIdx = _lastRealBar > 0 ? _lastRealBar : (ChartBars.Bars.Count - 1);
			bool canExtend = ExtendLines && !profile.IsActive && lastBarIdx > rightIdx;

			// Check if profile is off-screen
			bool profileOffScreenLeft = rightIdx < ChartBars.FromIndex;
			bool profileOffScreenRight = leftIdx > ChartBars.ToIndex;

			// If profile is off-screen right, nothing to draw
			if (profileOffScreenRight) return;

			// If profile is off-screen left and we can't extend, nothing to draw
			if (profileOffScreenLeft && !canExtend) return;

			float barDist = chartControl.Properties.BarDistance;

			// Calculate session width for percentage-based histogram
			int sessionBars = rightIdx - leftIdx;
			float sessionWidthPx = Math.Max(20f, sessionBars * barDist);
			float maxHistPx = sessionWidthPx * HistogramMaxWidth / 100f;

			// Calculate leftX
			int clampedLeft = Math.Max(ChartBars.FromIndex, Math.Min(leftIdx, ChartBars.ToIndex));
			float leftX = chartControl.GetXByBarIndex(ChartBars, clampedLeft);
			if (leftIdx < ChartBars.FromIndex)
				leftX -= (ChartBars.FromIndex - leftIdx) * barDist;

			// Calculate rightX
			int clampedRight = Math.Max(ChartBars.FromIndex, Math.Min(rightIdx, ChartBars.ToIndex));
			float rightX = chartControl.GetXByBarIndex(ChartBars, clampedRight);
			if (rightIdx > ChartBars.ToIndex)
				rightX += (rightIdx - ChartBars.ToIndex) * barDist;

			// Lines span from profile start to profile end + histogram width
			float startX = leftX;
			float endX, labelX;
			if (HistogramSideParam == HistogramSide.Right)
			{
				endX   = rightX + maxHistPx + 20;
				labelX = endX + 5;
			}
			else
			{
				endX   = rightX;
				labelX = endX + 5;
			}

			// Extend active profile lines a bit to the right
			if (profile.IsActive)
			{
				endX   += 50;
				labelX = endX + 5;
			}

			// When extending, the extension starts right after the profile end (rightX),
			// not after endX (which includes histogram width and would overlap with the extension).
			float extStartX;
			if (profileOffScreenLeft)
				extStartX = chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);
			else
				extStartX = canExtend ? rightX : endX;

			// When extending, profile lines only go to rightX (profile end),
			// the extension takes over from there. Without extension, lines go to endX (includes histogram).
			float lineEndX = canExtend ? rightX : endX;

			// Calculate age suffix for historical profiles (e.g. " 3d")
			string ageSuffix = "";
			if (!profile.IsActive && profile.EndTime > DateTime.MinValue)
			{
				int days = (int)(DateTime.Now.Date - profile.EndTime.Date).TotalDays;
				if (days > 0)
					ageSuffix = " " + days + "d";
			}

			// POC line
			if (ShowPOCLine && profile.POC > 0 && _dxPOCLineBrush != null)
			{
				float y = chartScale.GetYByValue(profile.POC);

				// Only draw the profile line if profile is on-screen
				if (!profileOffScreenLeft)
				{
					RenderTarget.DrawLine(
						new SharpDX.Vector2(startX, y),
						new SharpDX.Vector2(lineEndX, y),
						_dxPOCLineBrush, 2.0f);
				}

				if (canExtend)
					DrawLevelExtension(profile.POC, rightIdx, lastBarIdx, extStartX, barDist,
						chartControl, _dxPOCLineBrush, y, "POC" + ageSuffix);
				else if (!profileOffScreenLeft)
					RenderLabel("POC" + ageSuffix, labelX, y - 6, _dxPOCLineBrush);
			}

			// VAH line
			if (ShowVALines && profile.VAH > 0 && _dxVALineBrush != null)
			{
				float y = chartScale.GetYByValue(profile.VAH);

				if (!profileOffScreenLeft)
				{
					RenderTarget.DrawLine(
						new SharpDX.Vector2(startX, y),
						new SharpDX.Vector2(lineEndX, y),
						_dxVALineBrush, 2.0f);
				}

				if (canExtend)
					DrawLevelExtension(profile.VAH, rightIdx, lastBarIdx, extStartX, barDist,
						chartControl, _dxVALineBrush, y, "VAH" + ageSuffix);
				else if (!profileOffScreenLeft)
					RenderLabel("VAH" + ageSuffix, labelX, y - 6, _dxVALineBrush);
			}

			// VAL line
			if (ShowVALines && profile.VAL > 0 && _dxVALineBrush != null)
			{
				float y = chartScale.GetYByValue(profile.VAL);

				if (!profileOffScreenLeft)
				{
					RenderTarget.DrawLine(
						new SharpDX.Vector2(startX, y),
						new SharpDX.Vector2(lineEndX, y),
						_dxVALineBrush, 2.0f);
				}

				if (canExtend)
					DrawLevelExtension(profile.VAL, rightIdx, lastBarIdx, extStartX, barDist,
						chartControl, _dxVALineBrush, y, "VAL" + ageSuffix);
				else if (!profileOffScreenLeft)
					RenderLabel("VAL" + ageSuffix, labelX, y - 6, _dxVALineBrush);
			}
		}

		#endregion

		#region Level Extension Helpers

		/// <summary>
		/// Draws level extension from profile end:
		/// - SOLID line (level color) until price touches the level
		/// - If virgin (never touched): SOLID line all the way to current bar
		/// - If touched: solid stops at touch, then DASHED line (touched color)
		///   extends to end of that day's session (ProfileEndTime)
		/// </summary>
		private void DrawLevelExtension(double priceLevel, int profileEndBarIdx, int lastBarIdx,
			float extStartX, float barDist, ChartControl chartControl,
			SharpDX.Direct2D1.SolidColorBrush levelBrush, float y, string label)
		{
			// Find first bar after profile end where price touched this level
			int touchBar = FindFirstTouchBar(priceLevel, profileEndBarIdx + 1, lastBarIdx);
			bool isTouched = (touchBar >= 0);

			// Solid line from profile end to touch (or current bar if virgin)
			int solidEndBar = isTouched ? touchBar : lastBarIdx;

			// Clamp to last visible bar — never draw beyond the current bar on chart
			int clampedEndBar = Math.Min(solidEndBar, ChartBars.ToIndex);

			float solidEndX = BarIdxToX(clampedEndBar, barDist, chartControl);
			float thickness = (float)ExtendLineThickness;

			// Draw solid extension if visible
			if (solidEndX > extStartX + 2)
			{
				RenderTarget.DrawLine(
					new SharpDX.Vector2(extStartX, y),
					new SharpDX.Vector2(solidEndX, y),
					levelBrush, thickness);
			}

			// Ghost/dashed segment after touch → extends to end of session day
			if (isTouched && _dxTouchedBrush != null && _dxDashStyle != null)
			{
				int ghostEndBar = FindSessionEndBar(touchBar);
				int clampedGhostEnd = Math.Min(ghostEndBar, ChartBars.ToIndex);
				float ghostEndX = BarIdxToX(clampedGhostEnd, barDist, chartControl);

				if (ghostEndX > solidEndX + 2)
				{
					RenderTarget.DrawLine(
						new SharpDX.Vector2(solidEndX, y),
						new SharpDX.Vector2(ghostEndX, y),
						_dxTouchedBrush, thickness, _dxDashStyle);
				}

				// Label at end of ghost line
				RenderLabel(label, ghostEndX + 5, y - 6, _dxTouchedBrush);
			}
			else
			{
				// Virgin level — label at solid end with level color
				var labelBrush = isTouched ? _dxTouchedBrush : levelBrush;
				if (labelBrush != null)
					RenderLabel(label, solidEndX + 5, y - 6, labelBrush);
			}
		}

		/// <summary>
		/// Convert a bar index to screen X coordinate, handling off-screen bars.
		/// </summary>
		private float BarIdxToX(int barIdx, float barDist, ChartControl chartControl)
		{
			int clamped = Math.Max(ChartBars.FromIndex, Math.Min(barIdx, ChartBars.ToIndex));
			float x = chartControl.GetXByBarIndex(ChartBars, clamped);
			if (barIdx > ChartBars.ToIndex)
				x += (barIdx - ChartBars.ToIndex) * barDist;
			else if (barIdx < ChartBars.FromIndex)
				x -= (ChartBars.FromIndex - barIdx) * barDist;
			return x;
		}

		/// <summary>
		/// Scans bars from startBar to endBar to find the first bar where
		/// price touched the level. Uses ChartBars.Bars (the actual chart series)
		/// to ensure correct OHLC data regardless of AddDataSeries configuration.
		/// Returns -1 if no touch found (level is virgin).
		/// </summary>
		private int FindFirstTouchBar(double priceLevel, int startBar, int endBar)
		{
			try
			{
				// Use ChartBars.Bars (chart series) instead of Bars (indicator input series)
				// which may differ when AddDataSeries is used
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0) return -1;

				int from = Math.Max(0, startBar);
				int to   = Math.Min(endBar, bars.Count - 1);
				double tolerance = TickSize * 0.5;

				for (int i = from; i <= to; i++)
				{
					double high = bars.GetHigh(i);
					double low  = bars.GetLow(i);

					// Bar touched the level: high reached up to level OR low reached down to level
					// With half-tick tolerance for floating-point precision
					if (high >= priceLevel - tolerance && low <= priceLevel + tolerance)
						return i;
				}
			}
			catch
			{
				// Safe fallback — treat as virgin if we can't read bars
			}

			return -1;
		}

		/// <summary>
		/// Given a touch bar index, finds the bar at the end of that day's session
		/// (ProfileEndTime). Used for ghost/dashed extension after a level is touched.
		/// </summary>
		private int FindSessionEndBar(int touchBarIdx)
		{
			try
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0) return touchBarIdx;

				int safeIdx = Math.Max(0, Math.Min(touchBarIdx, bars.Count - 1));
				DateTime touchTime = bars.GetTime(safeIdx);
				DateTime ghostEndDt = touchTime.Date + _profileEndTs;

				// If touch happened after session end time, push to next day
				if (touchTime.TimeOfDay > _profileEndTs)
					ghostEndDt = ghostEndDt.AddDays(1);

				int lastValid = _lastRealBar > 0 ? _lastRealBar : (bars.Count - 1);

				// Ghost end is in the future — cap to last available bar
				if (ghostEndDt > bars.GetTime(lastValid))
					return lastValid;

				int ghostEndIdx = bars.GetBar(ghostEndDt);
				if (ghostEndIdx < 0) return lastValid;
				if (ghostEndIdx < touchBarIdx) ghostEndIdx = touchBarIdx;
				if (ghostEndIdx > lastValid) ghostEndIdx = lastValid;

				return ghostEndIdx;
			}
			catch
			{
				return touchBarIdx;
			}
		}

		#endregion

		#region RenderLabel

		private void RenderLabel(string text, float x, float y, SharpDX.Direct2D1.SolidColorBrush brush)
		{
			if (_dwFactory == null || _dwTextFormat == null || brush == null) return;

			using (var textLayout = new SharpDX.DirectWrite.TextLayout(_dwFactory, text, _dwTextFormat, 80, 16))
			{
				RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), textLayout, brush);
			}
		}

		#endregion

		#region Brush Helpers

		private SharpDX.Direct2D1.SolidColorBrush CreateBrushFromMedia(System.Windows.Media.Brush mediaBrush)
		{
			if (RenderTarget == null || mediaBrush == null) return null;
			var solid = mediaBrush as System.Windows.Media.SolidColorBrush;
			if (solid != null)
			{
				var c = solid.Color;
				return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
					new SharpDX.Color((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A));
			}
			return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Gray);
		}

		private SharpDX.Direct2D1.SolidColorBrush CreateBrushWithAlpha(System.Windows.Media.Brush mediaBrush, byte alpha)
		{
			if (RenderTarget == null || mediaBrush == null) return null;
			var solid = mediaBrush as System.Windows.Media.SolidColorBrush;
			if (solid != null)
			{
				var c = solid.Color;
				return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
					new SharpDX.Color((byte)c.R, (byte)c.G, (byte)c.B, alpha));
			}
			return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color((byte)128, (byte)128, (byte)128, alpha));
		}

		#region Anchored VWAP Rendering

		private void RenderProfileVwaps(VolumeProfileSession profile, ChartControl chartControl, ChartScale chartScale)
		{
			if (profile.HighVwapValues == null && profile.LowVwapValues == null) return;

			float activeWidth   = (float)VwapLineThickness;
			float archivedWidth = Math.Max(1.0f, activeWidth * 0.6f);

			// Archived HIGH VWAP segments (gris, más fino)
			if (profile.ArchivedHighVwaps != null && _dxArchivedVwapBrush != null)
			{
				foreach (var seg in profile.ArchivedHighVwaps)
				{
					if (seg.EndIdx >= ChartBars.FromIndex && seg.StartIdx <= ChartBars.ToIndex)
						DrawVwapPolyline(seg.Values, seg.StartIdx, seg.EndIdx,
							_dxArchivedVwapBrush, archivedWidth, chartControl, chartScale);
				}
			}

			// Archived LOW VWAP segments
			if (profile.ArchivedLowVwaps != null && _dxArchivedVwapBrush != null)
			{
				foreach (var seg in profile.ArchivedLowVwaps)
				{
					if (seg.EndIdx >= ChartBars.FromIndex && seg.StartIdx <= ChartBars.ToIndex)
						DrawVwapPolyline(seg.Values, seg.StartIdx, seg.EndIdx,
							_dxArchivedVwapBrush, archivedWidth, chartControl, chartScale);
				}
			}

			// Active HIGH VWAP
			if (profile.HighVwapValues != null && profile.HighVwapValues.Count > 0 && _dxHighVwapBrush != null)
			{
				int endBar = profile.IsActive ? _lastRealBar : profile.EndBarIdx;
				DrawVwapPolyline(profile.HighVwapValues, profile.HighVwapAnchorBar, endBar,
					_dxHighVwapBrush, activeWidth, chartControl, chartScale);
			}

			// Active LOW VWAP
			if (profile.LowVwapValues != null && profile.LowVwapValues.Count > 0 && _dxLowVwapBrush != null)
			{
				int endBar = profile.IsActive ? _lastRealBar : profile.EndBarIdx;
				DrawVwapPolyline(profile.LowVwapValues, profile.LowVwapAnchorBar, endBar,
					_dxLowVwapBrush, activeWidth, chartControl, chartScale);
			}
		}

		/// <summary>
		/// Dibuja una polyline VWAP desde valores pre-calculados.
		/// </summary>
		private void DrawVwapPolyline(Dictionary<int, double> values, int startIdx, int endIdx,
			SharpDX.Direct2D1.SolidColorBrush brush, float thickness,
			ChartControl chartControl, ChartScale chartScale)
		{
			if (values == null || values.Count == 0 || brush == null) return;
			if (startIdx < 0 || endIdx < startIdx) return;
			if (endIdx < ChartBars.FromIndex || startIdx > ChartBars.ToIndex) return;

			int viewStart = Math.Max(startIdx, ChartBars.FromIndex);
			int viewEnd   = Math.Min(endIdx, ChartBars.ToIndex);
			if (viewStart > viewEnd) return;

			SharpDX.Vector2? lastPoint = null;
			int lastValidIdx = -1;

			for (int i = viewStart; i <= viewEnd; i++)
			{
				double vwap;
				if (!values.TryGetValue(i, out vwap) || double.IsNaN(vwap))
				{
					lastPoint = null;
					lastValidIdx = -1;
					continue;
				}

				float x = chartControl.GetXByBarIndex(ChartBars, i);
				float y = chartScale.GetYByValue(vwap);
				var currentPoint = new SharpDX.Vector2(x, y);

				if (lastPoint.HasValue && lastValidIdx == i - 1)
					RenderTarget.DrawLine(lastPoint.Value, currentPoint, brush, thickness);

				lastPoint = currentPoint;
				lastValidIdx = i;
			}
		}

		#endregion

		private void DisposeCachedBrushes()
		{
			if (_dxPOCBrush != null)     { _dxPOCBrush.Dispose();     _dxPOCBrush = null; }
			if (_dxVABrush != null)      { _dxVABrush.Dispose();      _dxVABrush = null; }
			if (_dxOutsideBrush != null) { _dxOutsideBrush.Dispose(); _dxOutsideBrush = null; }
			if (_dxPOCLineBrush != null)  { _dxPOCLineBrush.Dispose();  _dxPOCLineBrush = null; }
			if (_dxVALineBrush != null)   { _dxVALineBrush.Dispose();   _dxVALineBrush = null; }
			if (_dxTouchedBrush != null) { _dxTouchedBrush.Dispose(); _dxTouchedBrush = null; }
			if (_dxTPO_POCBrush != null) { _dxTPO_POCBrush.Dispose(); _dxTPO_POCBrush = null; }
			if (_dxTPO_VABrush != null)  { _dxTPO_VABrush.Dispose();  _dxTPO_VABrush = null; }
			if (_dxTPO_OutBrush != null) { _dxTPO_OutBrush.Dispose(); _dxTPO_OutBrush = null; }
			if (_dxHighVwapBrush != null)     { _dxHighVwapBrush.Dispose();     _dxHighVwapBrush = null; }
			if (_dxLowVwapBrush != null)      { _dxLowVwapBrush.Dispose();      _dxLowVwapBrush = null; }
			if (_dxArchivedVwapBrush != null) { _dxArchivedVwapBrush.Dispose(); _dxArchivedVwapBrush = null; }
			if (_dxDashStyle != null)     { _dxDashStyle.Dispose();     _dxDashStyle = null; }
			if (_dwTextFormat != null)    { _dwTextFormat.Dispose();    _dwTextFormat = null; }
			if (_dwFactory != null)      { _dwFactory.Dispose();      _dwFactory = null; }
		}

		#endregion

		#region License Overlay

		private void RenderLicenseOverlay(ChartControl chartControl, ChartScale chartScale)
		{
			if (RenderTarget == null || chartControl == null || chartScale == null) return;

			float chartW = (float)chartControl.ActualWidth;
			float chartH = (float)chartScale.Height;

			// Semi-transparent dark overlay
			using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0f, 0f, 0f, 0.6f)))
			{
				RenderTarget.FillRectangle(new SharpDX.RectangleF(0, 0, chartW, chartH), bgBrush);
			}

			// Box dimensions
			float boxW = Math.Min(520, chartW - 40);
			float boxH = 160;
			float boxX = (chartW - boxW) / 2f;
			float boxY = (chartH - boxH) / 2f;

			// Rounded box background
			var boxRect = new SharpDX.RectangleF(boxX, boxY, boxW, boxH);
			using (var boxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.12f, 0.12f, 0.18f, 0.95f)))
			using (var borderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.44f, 0.29f, 0.40f, 1f)))
			{
				var roundRect = new SharpDX.Direct2D1.RoundedRectangle { Rect = boxRect, RadiusX = 10, RadiusY = 10 };
				RenderTarget.FillRoundedRectangle(roundRect, boxBrush);
				RenderTarget.DrawRoundedRectangle(roundRect, borderBrush, 2f);
			}

			var factory = _dwFactory ?? new SharpDX.DirectWrite.Factory();
			bool ownFactory = (_dwFactory == null);

			try
			{
				// Title
				using (var titleFormat = new SharpDX.DirectWrite.TextFormat(factory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 18f))
				using (var titleBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.44f, 0.29f, 0.40f, 1f)))
				{
					titleFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					var titleRect = new SharpDX.RectangleF(boxX, boxY + 20, boxW, 30);
					RenderTarget.DrawText("RelativeVolumeProfile", titleFormat, titleRect, titleBrush);
				}

				// Icon (lock symbol)
				using (var iconFormat = new SharpDX.DirectWrite.TextFormat(factory, "Segoe UI Symbol", 24f))
				using (var iconBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.9f, 0.7f, 0.2f, 1f)))
				{
					iconFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					var iconRect = new SharpDX.RectangleF(boxX, boxY + 50, boxW, 35);
					RenderTarget.DrawText("\U0001F512", iconFormat, iconRect, iconBrush);
				}

				// Message
				string msg = _licenseMessage ?? "License Key requerida.";
				using (var msgFormat = new SharpDX.DirectWrite.TextFormat(factory, "Segoe UI", 13f))
				using (var msgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.85f, 0.85f, 0.85f, 1f)))
				{
					msgFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					msgFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.Wrap;
					var msgRect = new SharpDX.RectangleF(boxX + 20, boxY + 90, boxW - 40, 50);
					RenderTarget.DrawText(msg, msgFormat, msgRect, msgBrush);
				}
			}
			finally
			{
				if (ownFactory && factory != null) factory.Dispose();
			}
		}

		#endregion
	}
}
