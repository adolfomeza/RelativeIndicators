#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.AddOns;
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

		// Cache del composite más reciente (evita doble LINQ por profile por frame).
		// Se invalida (= -1) cuando _composites cambia. Stamp incrementa con merge/unmerge.
		private VolumeProfileSession _cachedLatestCompositeSession;
		private int _compositesStampCached = -1;
		private int _compositesStamp = 0; // incrementar al modificar _composites

		// PERF: cache de TextFormat por tamaño de font para TPO. Evita crear/destruir
		// un TextFormat por cada profile renderizado por frame. Con 50 profiles
		// visibles se ahorran 50 allocs DirectWrite × 30fps = 1500 allocs/seg.
		private Dictionary<int, SharpDX.DirectWrite.TextFormat> _tpoFormatCache;

		private SharpDX.DirectWrite.TextFormat GetCachedTpoFormat(float fontSize)
		{
			if (_dwFactory == null) return null;
			int key = (int)Math.Round(fontSize);
			if (key < 5) key = 5;
			if (key > 24) key = 24;
			if (_tpoFormatCache == null)
				_tpoFormatCache = new Dictionary<int, SharpDX.DirectWrite.TextFormat>();
			SharpDX.DirectWrite.TextFormat fmt;
			if (_tpoFormatCache.TryGetValue(key, out fmt) && fmt != null && !fmt.IsDisposed)
				return fmt;
			fmt = new SharpDX.DirectWrite.TextFormat(_dwFactory, "Consolas",
				SharpDX.DirectWrite.FontWeight.Normal,
				SharpDX.DirectWrite.FontStyle.Normal, key);
			_tpoFormatCache[key] = fmt;
			return fmt;
		}

		private void DisposeTpoFormatCache()
		{
			if (_tpoFormatCache != null)
			{
				foreach (var kv in _tpoFormatCache)
					try { if (kv.Value != null && !kv.Value.IsDisposed) kv.Value.Dispose(); } catch { }
				_tpoFormatCache.Clear();
			}
		}

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
			{
				RestoreComposites();
				// Trigger auto-merge inmediato despues del restore (se gateaba con
				// _compositesRestored). Asi el primer render despues de carga ya muestra
				// las CVAs auto-merged sin esperar al siguiente session close.
				if (AutoMergeNadroEnabled)
				{
					ApplyNadroAutoMerge();
					int cc = 0;
					for (int k = 0; k < _allProfiles.Count; k++)
						if (!_allProfiles[k].IsActive) cc++;
					_lastNadroBuildClosedCount = cc;
				}
			}

			// Clear hit-test cache for this frame
			_profileBounds?.Clear();

			// Use ChartBars count as the limit for bar scanning (avoids empty future bars)
			int lastBarIdx = _lastRealBar > 0 ? _lastRealBar : (ChartBars.Bars.Count - 1);

			// Render TODOS los profiles visibles con detalle completo. La performance
			// se garantiza con TpoViewMode.Histogram auto-activado cuando las letras
			// serian ilegibles (<6pt) — eso ya elimina los allocs costosos de DrawText.
			for (int p = 0; p < _allProfiles.Count; p++)
			{
				var profile = _allProfiles[p];
				if (profile.Levels.Count == 0) continue;
				if (profile.POCVolume <= 0) continue;

				int profEnd = profile.IsActive ? profile.LastVolumeBarIdx : profile.EndBarIdx;

				if (profEnd < ChartBars.FromIndex)
				{
					if (ExtendLines && !profile.IsActive)
					{
						bool anyVisible = false;
						double[] levels = { profile.POC, profile.VAH, profile.VAL };
						foreach (double lvl in levels)
						{
							if (lvl <= 0) continue;
							int touch = FindFirstTouchBar(lvl, profEnd + 1, lastBarIdx);
							int extEnd = (touch >= 0) ? touch : lastBarIdx;
							if (extEnd >= ChartBars.FromIndex)
							{
								anyVisible = true;
								break;
							}
						}
						if (!anyVisible) continue;
					}
					else continue;
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
			// PERF: modo histograma para TPO (1 FillRect por nivel = ~13x mas rapido que letras).
			// Auto-activado cuando: el usuario lo elige explicitamente, O cuando el ancho de letra
			// seria <6pt (ilegible) — en ese caso las letras son ruido visual ademas de lentas.
			bool tpoHistogram = isTPO && _tpoViewMode == TpoViewMode.Histogram;

			// For TPO mode: calculate font/letter sizing
			float tpoFontSize = 0f;
			float tpoLetterW  = 0f;
			float tpoRowH     = 0f;
			float tpoStartX   = anchorX; // used in Compact mode
			float tpoBarDist  = chartControl.Properties.BarDistance;
			SharpDX.DirectWrite.TextFormat tpoFormat = null;

			// PERF: maxTpoCount también lo necesita el modo Histogram para escalar las barras.
			int tpoMaxCount = 1;
			if (isTPO)
			{
				tpoMaxCount = profile.CachedMaxTpoCount;
				if (tpoMaxCount < 0)
				{
					tpoMaxCount = 1;
					foreach (var kvp2 in profile.Levels)
					{
						if (kvp2.Value.TpoPeriods != null && kvp2.Value.TpoPeriods.Count > tpoMaxCount)
							tpoMaxCount = kvp2.Value.TpoPeriods.Count;
					}
					profile.CachedMaxTpoCount = tpoMaxCount;
				}
			}

			if (isTPO && !tpoHistogram && _dwFactory != null)
			{
				if (tpoExtended)
				{
					// Extended: font size based on the LARGER of bar distance or level height
					float sizeFromBar = tpoBarDist * 0.7f;
					float sizeFromLevel = levelPixels * 0.85f;
					tpoFontSize = Math.Max(7f, Math.Min(Math.Max(sizeFromBar, sizeFromLevel), 16f));
					tpoLetterW  = tpoFontSize * 0.65f;
				}
				else
				{
					// Compact: letters stack from profile start, width fills session
					int clampedStart = Math.Max(ChartBars.FromIndex, Math.Min(profile.StartBarIdx, ChartBars.ToIndex));
					tpoStartX = chartControl.GetXByBarIndex(ChartBars, clampedStart);
					if (profile.StartBarIdx < ChartBars.FromIndex)
						tpoStartX -= (ChartBars.FromIndex - profile.StartBarIdx) * tpoBarDist;

					tpoLetterW = Math.Max(3f, sessionWidthPx / tpoMaxCount);
					tpoLetterW = Math.Min(tpoLetterW, 14f);
					tpoFontSize = Math.Max(6f, Math.Min(tpoLetterW / 0.65f, 16f));
				}

				// AUTO-SWITCH a Histogram cuando la letra seria ilegible (<6pt).
				// Las letras chicas son ruido visual ademas de lentas de renderear.
				if (tpoLetterW < 6f)
				{
					tpoHistogram = true;
				}
				else
				{
					// Cap font size so letters never overlap vertically between levels
					float maxFontForLevel = Math.Max(5f, levelPixels - 1f);
					if (tpoFontSize > maxFontForLevel)
					{
						tpoFontSize = maxFontForLevel;
						tpoLetterW  = tpoFontSize * 0.65f;
					}
					tpoRowH = tpoFontSize + 1f;
					tpoFormat = GetCachedTpoFormat(tpoFontSize);
				}
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

				if (isTPO && tpoHistogram && kvp.Value.TpoPeriods != null)
				{
					// === TPO HISTOGRAM MODE: barras horizontales por count de TPO periods ===
					int tpoCount = kvp.Value.TpoPeriods.Count;
					if (tpoCount <= 0) continue;
					float widthRatio = (float)tpoCount / Math.Max(1, tpoMaxCount);
					float barWidth = widthRatio * maxHistPx;
					if (barWidth < 0.5f) continue;

					float barTopH = y - barHeight / 2;
					float barBotH = y + barHeight / 2;
					if (barTopH < boundsMinY) boundsMinY = barTopH;
					if (barBotH > boundsMaxY) boundsMaxY = barBotH;
					if (barWidth > boundsMaxWidth) boundsMaxWidth = barWidth;

					// Side Right: anchor=end de sesion, bars crecen a la IZQUIERDA (dentro del dia)
					// Side Left: anchor=start, bars crecen a la DERECHA (dentro del dia)
					float drawX = HistogramSideParam == HistogramSide.Right
						? anchorX - barWidth
						: anchorX;
					RenderTarget.FillRectangle(
						new SharpDX.RectangleF(drawX, y - barHeight / 2, barWidth, barHeight),
						brush);
				}
				else if (isTPO && tpoFormat != null && kvp.Value.TpoPeriods != null)
				{
					// === TPO MODE: Draw letters ===
					// PERF: cachear sortedPeriods en VolumeLevelData (se invalida solo
					// cuando se agrega un nuevo periodo). Antes: List+Sort por nivel por
					// frame. Ahora: 1 sort cuando entra letra nueva.
					var sortedPeriods = kvp.Value.SortedPeriodsCache;
					if (sortedPeriods == null)
					{
						sortedPeriods = new List<int>(kvp.Value.TpoPeriods);
						sortedPeriods.Sort();
						kvp.Value.SortedPeriodsCache = sortedPeriods;
					}

					float minXDrawn = float.MaxValue;
					float maxXDrawn = float.MinValue;
					int compactIdx = 0; // sequential index for compact stacking
					float rowHalf = tpoRowH / 2;
					float letterRectW = tpoLetterW + 4;
					float letterRectH = tpoRowH + 2;

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

						// PERF: DrawText directo en lugar de TextLayout+DrawTextLayout.
						// TextLayout aloca un objeto COM nativo + GC pressure cada letra.
						// DrawText es la API ligera que internamente computa layout sin alocar.
						// Impacto: 40-70% menos lag al ampliar días en TPO.
						var letterRect = new SharpDX.RectangleF(
							xPos, y - rowHalf, letterRectW, letterRectH);
						RenderTarget.DrawText(letter.ToString(), tpoFormat, letterRect, brush);
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

					float barTop = y - barHeight / 2;
					float barBot = y + barHeight / 2;
					if (barTop < boundsMinY) boundsMinY = barTop;
					if (barBot > boundsMaxY) boundsMaxY = barBot;
					if (barWidth > boundsMaxWidth) boundsMaxWidth = barWidth;

					// Side Right: anchor=end de sesion, bars crecen IZQUIERDA (dentro del dia).
					// Side Left: anchor=start, bars crecen DERECHA (dentro del dia).
					float drawX = HistogramSideParam == HistogramSide.Right
						? anchorX - barWidth
						: anchorX;
					RenderTarget.FillRectangle(
						new SharpDX.RectangleF(drawX, y - barHeight / 2, barWidth, barHeight),
						brush);
				}
			}
			}
			finally
			{
				// PERF: NO disponer — tpoFormat está cacheado y se reusa entre profiles/frames.
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

			// Calcular días de TRADING (no calendario) entre EndTime y hoy.
			// Sábados y domingos no cuentan (mercados cerrados).
			// Ejemplo: si EndTime=viernes y hoy=lunes → 1 día de trading (no 3 calendario).
			int ageDays = 0;
			string ageSuffix = "";
			if (!profile.IsActive && profile.EndTime > DateTime.MinValue)
			{
				ageDays = CountTradingDays(profile.EndTime.Date, DateTime.Now.Date);
				if (ageDays > 0)
					ageSuffix = " " + ageDays + "d";
			}

			// NADRO label convention (jerarquía):
			// - Composite MÁS RECIENTE → CVAH/CVAL/CPOC + edad
			// - Composite ANTERIOR (cualquier composite no-más-reciente) → oCVAH/oCVAL/oCPOC + edad
			// - Activo (developing) → VAH/VAL/POC (sin prefijo, sin edad)
			// - Histórico 1 día (ayer) → pVAH/pVAL/pPOC (Prior Value Area)
			// - Histórico ≥2 días → oVAH/oVAL/oPOC (Old Value Area)
			// PERF: cache del composite más reciente.
			// Antes: 2 LINQ traversals por profile por frame (FirstOrDefault + Where+OrderBy+FirstOrDefault).
			// Ahora: O(N) una sola vez cuando _compositesStamp cambia, después O(1) por profile.
			bool isComposite = false;
			bool isLatestComposite = false;
			if (_composites != null && _composites.Count > 0)
			{
				// Refrescar cache del latest si _composites cambió desde el último frame
				if (_compositesStampCached != _compositesStamp)
				{
					_cachedLatestCompositeSession = null;
					DateTime maxEnd = DateTime.MinValue;
					for (int ci = 0; ci < _composites.Count; ci++)
					{
						var c = _composites[ci];
						if (c.MergedSession == null) continue;
						if (c.OriginalProfiles == null || c.OriginalProfiles.Count < 2) continue;
						if (c.MergedSession.EndTime > maxEnd)
						{
							maxEnd = c.MergedSession.EndTime;
							_cachedLatestCompositeSession = c.MergedSession;
						}
					}
					_compositesStampCached = _compositesStamp;
				}

				// Lookup O(N) sobre _composites pero sin LINQ allocs.
				// Para muchos composites podríamos cachear un Dict, pero típicamente N<10.
				for (int ci = 0; ci < _composites.Count; ci++)
				{
					var c = _composites[ci];
					if (c.MergedSession == profile && c.OriginalProfiles != null
						&& c.OriginalProfiles.Count >= 2)
					{
						isComposite = true;
						isLatestComposite = (_cachedLatestCompositeSession == profile);
						break;
					}
				}
			}

			string prefix;
			if (isComposite && isLatestComposite)
				prefix = "C";                                      // Composite más reciente
			else if (isComposite)
				prefix = "oC";                                     // Old Composite (anterior al más reciente)
			else if (profile.IsActive)
				prefix = "";                                       // Active developing
			else if (ageDays == 1)
				prefix = "p";                                      // Prior (1 día / ayer)
			else if (ageDays >= 2)
				prefix = "o";                                      // Old (≥2 días)
			else
				prefix = "";                                       // Fallback

			string pocLabel = prefix + "POC";
			string vahLabel = prefix + "VAH";
			string valLabel = prefix + "VAL";

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
					DrawLevelExtension(profile.POC, profile, lastBarIdx, extStartX, barDist,
						chartControl, _dxPOCLineBrush, y, pocLabel + ageSuffix, LevelKind.POC);
				else if (!profileOffScreenLeft)
					RenderLabel(pocLabel + ageSuffix, labelX, y - 6, _dxPOCLineBrush);
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
					DrawLevelExtension(profile.VAH, profile, lastBarIdx, extStartX, barDist,
						chartControl, _dxVALineBrush, y, vahLabel + ageSuffix, LevelKind.VAH);
				else if (!profileOffScreenLeft)
					RenderLabel(vahLabel + ageSuffix, labelX, y - 6, _dxVALineBrush);
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
					DrawLevelExtension(profile.VAL, profile, lastBarIdx, extStartX, barDist,
						chartControl, _dxVALineBrush, y, valLabel + ageSuffix, LevelKind.VAL);
				else if (!profileOffScreenLeft)
					RenderLabel(valLabel + ageSuffix, labelX, y - 6, _dxVALineBrush);
			}
		}

		#endregion

		#region Level Extension Helpers

		/// <summary>
		/// Tipo de nivel para definir cómo detectar "touch":
		/// - VAH: acceptance arriba (close del día NADRO > nivel)
		/// - VAL: acceptance abajo (close del día NADRO < nivel)
		/// - POC: wick touch simple (no es borde, no aplica acceptance)
		/// </summary>
		private enum LevelKind { POC, VAH, VAL }

		/// <summary>
		/// Draws level extension from profile end:
		/// - SOLID line (level color) until level is "touched"
		/// - If virgin: SOLID line hasta current bar
		/// - If touched: solid hasta el bar de cierre del día NADRO con acceptance,
		///   después DASHED hasta end-of-session de ese día.
		///
		/// VAH/VAL usan ACCEPTANCE (close del día NADRO en el lado correcto del nivel),
		/// no wicks. POC usa wick touch simple porque está en el centro y no tiene "lado".
		/// </summary>
		private void DrawLevelExtension(double priceLevel, VolumeProfileSession profile, int lastBarIdx,
			float extStartX, float barDist, ChartControl chartControl,
			SharpDX.Direct2D1.SolidColorBrush levelBrush, float y, string label, LevelKind kind)
		{
			int searchStartBar = GetSafeTouchSearchStartBar(profile);

			// Touch detection según el tipo de nivel:
			// - VAH/VAL: acceptance al cierre del día NADRO (close del día > o < nivel)
			// - POC: wick touch simple (high/low cruza nivel)
			int touchBar;
			if (kind == LevelKind.VAH)
				touchBar = FindFirstAcceptanceBar(priceLevel, searchStartBar, lastBarIdx, isUpper: true);
			else if (kind == LevelKind.VAL)
				touchBar = FindFirstAcceptanceBar(priceLevel, searchStartBar, lastBarIdx, isUpper: false);
			else
				touchBar = FindFirstTouchBar(priceLevel, searchStartBar, lastBarIdx);

			bool isTouched = (touchBar >= 0);

			// Debug log: cuando es composite y se detecta touch, log para diagnóstico.
			if (ShowDebugLogs && _composites != null && _composites.Any(c => c.MergedSession == profile))
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				try
				{
					string startBarInfo = (searchStartBar < bars.Count)
						? "bar " + searchStartBar + " @ " + bars.GetTime(searchStartBar).ToString("yyyy-MM-dd HH:mm")
						: "bar " + searchStartBar + " (out of range)";
					string kindStr = kind.ToString();

					if (isTouched && touchBar < bars.Count)
					{
						double touchClose = bars.GetClose(touchBar);
						DateTime touchTime = bars.GetTime(touchBar);
						this.RLog("{0} [{1}] level={2} | EndTime={3:yyyy-MM-dd HH:mm} | search starts at {4} | ACCEPTED at bar {5} @ {6:yyyy-MM-dd HH:mm} (close={7})",
							label, kindStr, priceLevel, profile.EndTime, startBarInfo,
							touchBar, touchTime, touchClose);
					}
					else if (!isTouched)
					{
						this.RLog("{0} [{1}] level={2} | EndTime={3:yyyy-MM-dd HH:mm} | search starts at {4} | VIRGIN (no acceptance)",
							label, kindStr, priceLevel, profile.EndTime, startBarInfo);
					}
				}
				catch { }
			}

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
		/// Devuelve el bar index "seguro" desde el cual buscar toques de un perfil
		/// histórico/composite. Usa EndTime como fuente de verdad: busca el primer
		/// bar cuyo time es ESTRICTAMENTE > profile.EndTime. Esto garantiza que el
		/// touch detection NUNCA caiga dentro del composite mismo (donde el precio
		/// obviamente toca todos los niveles porque están dentro del rango operado).
		///
		/// Importante para composites fusionados: el EndBarIdx del composite es
		/// Max(p => p.EndBarIdx) de los perfiles originales, pero si esos perfiles
		/// fueron rotados o el chart cambió de timeframe, el EndBarIdx puede quedar
		/// obsoleto. EndTime es invariante.
		/// </summary>
		/// <summary>
		/// Cuenta días de TRADING entre fromDate (exclusive) y toDate (inclusive).
		/// Sábados y domingos NO cuentan. No considera holidays — para precisión total
		/// requeriría calendar CME, pero para uso operativo NADRO basta omitir fines de semana.
		///
		/// Ejemplos (asumiendo no holidays):
		///   from=Vie 24, to=Lun 27 → 1 día (lunes)
		///   from=Jue 23, to=Lun 27 → 2 días (vie + lun)
		///   from=Mar 21, to=Lun 27 → 4 días (mié + jue + vie + lun)
		/// </summary>
		private int CountTradingDays(DateTime fromDate, DateTime toDate)
		{
			if (toDate <= fromDate) return 0;
			int count = 0;
			DateTime d = fromDate.Date;
			while (d < toDate.Date)
			{
				d = d.AddDays(1);
				if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
					count++;
			}
			return count;
		}

		private int GetSafeTouchSearchStartBar(VolumeProfileSession profile)
		{
			// Active profiles: empezar desde el bar después del último volumen registrado
			if (profile.IsActive)
				return profile.LastVolumeBarIdx + 1;

			// Si no hay EndTime válido, fallback al EndBarIdx + 1
			if (profile.EndTime <= DateTime.MinValue)
				return profile.EndBarIdx + 1;

			try
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0)
					return profile.EndBarIdx + 1;

				// NADRO usa reset 18:00 ET CME (Guía 03 §5). Día NADRO va de 18:00
				// del día anterior a 18:00 del día actual. El composite cierra al
				// EndTime (RTH 16:00 ET típicamente), pero el "día NADRO" sigue hasta
				// las 18:00 ET. Los wicks 16:00-18:00 ET del MISMO día son afterhours
				// del día del cierre (no del siguiente día NADRO) y producen falsos
				// touches con rangos anormales del cambio de sesión.
				//
				// Solución: arrancar touch search en el SIGUIENTE reset 18:00 ET
				// posterior a EndTime. Eso garantiza que estamos buscando en el
				// "día NADRO siguiente" donde los toques son legítimos.
				DateTime touchSearchStart = ComputeNextResetAfter(profile.EndTime);

				// Búsqueda binaria: primer bar cuyo time > touchSearchStart
				int lo = 0;
				int hi = bars.Count - 1;
				int result = bars.Count;

				while (lo <= hi)
				{
					int mid = lo + (hi - lo) / 2;
					DateTime midTime = bars.GetTime(mid);
					if (midTime > touchSearchStart)
					{
						result = mid;
						hi = mid - 1;
					}
					else
					{
						lo = mid + 1;
					}
				}

				return result;
			}
			catch
			{
				return profile.EndBarIdx + 1;
			}
		}

		/// <summary>
		/// Calcula el siguiente reset NADRO 18:00 ET (CME) post-EndTime.
		/// Si EndTime es 21-abr 16:00 → reset = 21-abr 18:00.
		/// Si EndTime es 21-abr 18:30 → reset = 22-abr 18:00 (siguiente día).
		/// El usuario está en VET (UTC-4) que en EDT coincide con ET. Por eso
		/// usamos hora local del feed (que ya está en VET = ET en EDT).
		/// </summary>
		private DateTime ComputeNextResetAfter(DateTime endTime)
		{
			// Reset CME a las 18:00 ET (= 18:00 VET en horario EDT)
			TimeSpan resetTod = new TimeSpan(18, 0, 0);
			DateTime sameDayReset = endTime.Date + resetTod;

			// Si EndTime es antes de las 18:00 → el reset del MISMO día
			// Si EndTime es a las 18:00+ → el reset del SIGUIENTE día
			if (endTime < sameDayReset)
				return sameDayReset;
			else
				return sameDayReset.AddDays(1);
		}

		/// <summary>
		/// Detecta acceptance + touch (patrón BPB clásico NADRO):
		/// 1. ACCEPTANCE: primer día NADRO cuyo close confirma rotura
		///    - VAH (isUpper=true): close > nivel
		///    - VAL (isUpper=false): close < nivel
		/// 2. TOUCH: después del acceptance, primer bar que vuelve a tocar el
		///    nivel desde el lado nuevo (el pullback del BPB)
		///    - Para VAH: low <= nivel (precio baja a testear desde arriba)
		///    - Para VAL: high >= nivel (precio sube a testear desde abajo)
		///
		/// Returns el bar del touch POST-acceptance, o -1 si:
		/// - No hubo acceptance (puro ruido/wicks intra-día) → virgin
		/// - Hubo acceptance pero no pullback todavía → tampoco "touched" aún
		///
		/// Wicks intra-día que no resultan en close del día con acceptance NO
		/// cuentan ni como acceptance ni como touch.
		/// </summary>
		private int FindFirstAcceptanceBar(double priceLevel, int startBar, int endBar, bool isUpper)
		{
			try
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0) return -1;

				int from = Math.Max(0, startBar);
				int to   = Math.Min(endBar, bars.Count - 1);
				if (from > to) return -1;

				double tolerance = TickSize * 0.5;
				TimeSpan resetTod = new TimeSpan(18, 0, 0);

				// === FASE 1: encontrar primer día NADRO con ACCEPTANCE ===
				int acceptanceCloseBar = -1;
				int i = from;
				while (i <= to)
				{
					// Próximo reset 18:00 ET (fin del día NADRO actual)
					DateTime barTime = bars.GetTime(i);
					DateTime dayEnd;
					if (barTime.TimeOfDay < resetTod)
						dayEnd = barTime.Date + resetTod;
					else
						dayEnd = barTime.Date.AddDays(1) + resetTod;

					// Último bar antes de dayEnd = "close" del día NADRO
					int closeBar = i;
					int j = i;
					while (j <= to && bars.GetTime(j) < dayEnd)
					{
						closeBar = j;
						j++;
					}
					if (j == i) j = i + 1;

					// Evaluar acceptance del día
					double dayClose = bars.GetClose(closeBar);
					bool accepted = isUpper
						? (dayClose > priceLevel + tolerance)
						: (dayClose < priceLevel - tolerance);

					if (accepted)
					{
						acceptanceCloseBar = closeBar;
						break;
					}

					i = j;
				}

				// Sin acceptance → virgin (no hay BPB candidato)
				if (acceptanceCloseBar < 0) return -1;

				// === FASE 2: desde el día siguiente al acceptance, buscar TOUCH ===
				// El touch es el pullback que vuelve a testear el nivel desde el lado nuevo.
				int searchFrom = acceptanceCloseBar + 1;
				for (int k = searchFrom; k <= to; k++)
				{
					double high = bars.GetHigh(k);
					double low  = bars.GetLow(k);

					if (isUpper)
					{
						// VAH: pullback = precio baja a tocar el nivel desde arriba
						if (low <= priceLevel + tolerance)
							return k;
					}
					else
					{
						// VAL: pullback = precio sube a tocar el nivel desde abajo
						if (high >= priceLevel - tolerance)
							return k;
					}
				}

				// Hubo acceptance pero todavía no hubo pullback (BPB no completado)
				// → tratar como virgin todavía (línea sigue activa esperando el pullback)
				return -1;
			}
			catch
			{
				// Safe fallback
			}

			return -1;
		}

		/// <summary>
		/// Scans bars from startBar to endBar to find the first bar where
		/// price touched the level. Uses ChartBars.Bars (the actual chart series)
		/// to ensure correct OHLC data regardless of AddDataSeries configuration.
		/// Returns -1 if no touch found (level is virgin).
		///
		/// NOTA: usado SOLO para POC (que está en el centro del VA y no tiene "lado"
		/// claro de acceptance). VAH/VAL usan FindFirstAcceptanceBar.
		/// </summary>
		private int FindFirstTouchBar(double priceLevel, int startBar, int endBar)
		{
			try
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0) return -1;

				int from = Math.Max(0, startBar);
				int to   = Math.Min(endBar, bars.Count - 1);
				double tolerance = TickSize * 0.5;

				for (int i = from; i <= to; i++)
				{
					double high = bars.GetHigh(i);
					double low  = bars.GetLow(i);

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
			// PERF: liberar cache de TPO TextFormats antes que el _dwFactory.
			DisposeTpoFormatCache();
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
