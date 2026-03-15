#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Core;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class RelativeVwap
    {
        #region Rendering Methods

		// === INTERNAL VWAP STYLE ===
		private SharpDX.Color InternalVwapColor = new SharpDX.Color(255, 165, 0, 255);
		private float InternalVwapThickness = 2.0f;

		// === CACHED SHARPDX RESOURCES ===
		private SharpDX.Direct2D1.SolidColorBrush _cachedHighVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedLowVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedHistoricalBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedPreviousVwapBrush;  // v3.0.2
		private SharpDX.Direct2D1.SolidColorBrush _cachedInternalVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedLabelBgBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedGrayBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedGoldenrodBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedLimeGreenBrush;
		private SharpDX.Direct2D1.StrokeStyle _cachedDashStyle;
		private bool _diagPreviousVwapLogged; // v3.0.2: One-shot diagnostic flag
		// v3.0.4: Health score brushes (green/yellow/red gradient)
		private SharpDX.Direct2D1.SolidColorBrush _cachedHealthGreenBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedHealthYellowBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedHealthRedBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedTouchStudyBrush; // v3.0.5
		private SharpDX.Direct2D1.SolidColorBrush _cachedConfigBBrush;    // v3.0.6: Config B arrow (orange-red)
		private SharpDX.Direct2D1.SolidColorBrush _cachedConfigCBrush;    // v3.0.6: Config C arrow (magenta)
		// v3.1.1 perf: Cached resources for OnRender (avoid per-frame allocation)
		private SharpDX.DirectWrite.TextFormat _cachedCfgLabelFmt;        // Config label "A/B/C/D"
		private SharpDX.DirectWrite.TextFormat _cachedHealthLabelFmt;     // Health score label "S:3.2"
		private SharpDX.Direct2D1.SolidColorBrush _cachedBgBrush;        // Label background
		private SharpDX.Direct2D1.SolidColorBrush _cachedWhiteDashBrush;  // Trade diagonal line
		private SharpDX.Direct2D1.SolidColorBrush _cachedSlRefBrush;     // SL reference line
		private SharpDX.Direct2D1.SolidColorBrush _cachedTpRefBrush;     // TP reference line
		private SharpDX.Direct2D1.SolidColorBrush _cachedTpDiamondBrush; // TP diamond fill
		private SharpDX.Direct2D1.SolidColorBrush _cachedSlSquareBrush;  // SL square fill
		// v3.1.2 perf: Cached brushes for open-trade rendering (were per-frame per-trade)
		private SharpDX.Direct2D1.SolidColorBrush _cachedOpenTradeBrush;   // Gray dotted line
		private SharpDX.Direct2D1.SolidColorBrush _cachedOpenSlRefBrush;   // SL reference (faint red)
		private SharpDX.Direct2D1.SolidColorBrush _cachedOpenTpRefBrush;   // TP reference (faint green)
		// v3.2.0 perf: Cached resources for touch study detail labels (were per-touch per-frame!)
		private SharpDX.DirectWrite.TextFormat _cachedDetailLabelFmt;      // 10f Consolas for detail labels
		private SharpDX.Direct2D1.SolidColorBrush _cachedDetailBgBrush;    // Dark background for detail labels
		// v3.2.0: Auto mode badge
		private SharpDX.DirectWrite.TextFormat _cachedAutoModeFmt;         // 12f Consolas bold for auto mode badge
		private SharpDX.Direct2D1.SolidColorBrush _cachedAutoModeBrush;    // White text for badge

        public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();

            if (dwFactory != null) dwFactory.Dispose();
            if (textFormat != null) textFormat.Dispose();
            
            // Dispose previous brushes to prevent leaks on resize/rebuild
            DisposeCachedBrushes();

            if (RenderTarget != null)
            {
                dwFactory = new SharpDX.DirectWrite.Factory();
                textFormat = new SharpDX.DirectWrite.TextFormat(dwFactory, "Arial", 12)
                {
                    TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
                    ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
                };

                // Cache frequently used brushes
                _cachedHighVwapBrush = CreateBrushFromMedia(HighVWAPColor);
                _cachedLowVwapBrush = CreateBrushFromMedia(LowVWAPColor);
                _cachedHistoricalBrush = CreateBrushFromMedia(HistoricalVWAPColor);
                _cachedPreviousVwapBrush = CreateBrushFromMedia(PreviousVWAPColor ?? Brushes.White);  // v3.0.2
                _cachedInternalVwapBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color((byte)InternalVwapColor.R, (byte)InternalVwapColor.G, (byte)InternalVwapColor.B, (byte)255));
                _cachedLabelBgBrush = CreateBrushFromMedia(LabelBackgroundColor);
                _cachedGrayBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Gray);
                _cachedGoldenrodBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Goldenrod);
                _cachedLimeGreenBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.LimeGreen);
                // v3.0.4: Health score color brushes
                _cachedHealthGreenBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(46, 204, 113, 220));  // #2ecc71
                _cachedHealthYellowBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(241, 196, 15, 220)); // #f1c40f
                _cachedHealthRedBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(231, 76, 60, 220));     // #e74c3c

                // v3.0.5: Touch study brush from property color
                try
                {
                    var scb = TouchStudyColor as System.Windows.Media.SolidColorBrush;
                    if (scb != null)
                    {
                        var mc = scb.Color;
                        _cachedTouchStudyBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(mc.R, mc.G, mc.B, (byte)220));
                    }
                    else
                    {
                        _cachedTouchStudyBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 255, 255, 220));
                    }
                }
                catch { _cachedTouchStudyBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 255, 255, 220)); }

                // v3.0.6: Config B/C arrow brushes
                _cachedConfigBBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(255, 87, 34, 240));   // Orange-red for SHORT breakout
                _cachedConfigCBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(186, 85, 211, 240));  // MediumOrchid for SHORT reversal

                // v3.1.1 perf: Cache TextFormats and utility brushes (avoid per-frame allocation)
                _cachedCfgLabelFmt = new SharpDX.DirectWrite.TextFormat(dwFactory, "Consolas", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12f)
                { TextAlignment = SharpDX.DirectWrite.TextAlignment.Center, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
                _cachedHealthLabelFmt = new SharpDX.DirectWrite.TextFormat(dwFactory, "Consolas", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 15f);
                _cachedBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(10, 10, 26, 200));
                _cachedWhiteDashBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(255, 255, 255, 220));
                _cachedSlRefBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(231, 76, 60, 80));
                _cachedTpRefBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(46, 204, 113, 80));
                _cachedTpDiamondBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(46, 204, 113, 220));
                _cachedSlSquareBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(231, 76, 60, 220));
                // v3.1.2 perf: Open-trade brushes (avoid per-frame per-trade allocation)
                _cachedOpenTradeBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(200, 200, 200, 120));
                _cachedOpenSlRefBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(231, 76, 60, 60));
                _cachedOpenTpRefBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(46, 204, 113, 60));
                // v3.2.0 perf: Detail label resources (were allocated per-touch per-frame!)
                _cachedDetailLabelFmt = new SharpDX.DirectWrite.TextFormat(dwFactory, "Consolas", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10f);
                _cachedDetailBgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(10, 10, 26, 190));
                // v3.2.0: Auto mode badge
                _cachedAutoModeFmt = new SharpDX.DirectWrite.TextFormat(dwFactory, "Consolas", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12f);
                _cachedAutoModeBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);

                // Cache dash stroke style
                var dashProps = new SharpDX.Direct2D1.StrokeStyleProperties()
                {
                    DashStyle = SharpDX.Direct2D1.DashStyle.Dash,
                    DashCap = SharpDX.Direct2D1.CapStyle.Flat,
                    StartCap = SharpDX.Direct2D1.CapStyle.Flat,
                    EndCap = SharpDX.Direct2D1.CapStyle.Flat
                };
                _cachedDashStyle = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, dashProps);
            }
        }

        private float DrawLabel(string text, float x, float y, Brush color, ChartControl chartControl, DateTime timestamp, bool alignRight = false)
        {
            if (dwFactory == null || textFormat == null) return 0;

            float textWidth = 0;
            using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, text, textFormat, 2000, 20))
            {
                textWidth = layout.Metrics.Width;
            }

            float drawX = alignRight ? (x - textWidth - 5) : (x + 5);

            if (labelQueue != null)
            {
                labelQueue.Add(new LabelData {
                    Text = text,
                    DrawX = drawX,
                    Y = y,
                    Width = textWidth,
                    Brush = color,
                    Time = timestamp
                });
            }

            return textWidth;
        }

        private void RenderQueuedLabels(ChartControl chartControl)
        {
            if (labelQueue == null || labelQueue.Count == 0 || RenderTarget == null || dwFactory == null || textFormat == null) return;

            // PHASE 2: Replace LINQ with manual deduplication (avoid GC pressure)
            Dictionary<string, LabelData> dedupMap = new Dictionary<string, LabelData>();
            foreach (var label in labelQueue)
            {
                if (!dedupMap.ContainsKey(label.Text) || label.Time > dedupMap[label.Text].Time)
                    dedupMap[label.Text] = label;
            }

            // Sort by time descending using List.Sort (in-place, no allocation)
            List<LabelData> sortedQueue = new List<LabelData>(dedupMap.Values);
            sortedQueue.Sort((a, b) => b.Time.CompareTo(a.Time));

            List<SharpDX.RectangleF> placedRects = new List<SharpDX.RectangleF>();

            foreach (var label in sortedQueue)
            {
                var solidColor = ((SolidColorBrush)label.Brush).Color;
                var dxColor = new SharpDX.Color((int)solidColor.R, (int)solidColor.G, (int)solidColor.B, 255);

                // PHASE 1.5: Create brush only when needed (can't cache dynamic colors easily)
                using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label.Text, textFormat, 2000, 20))
                {
                    float desiredX = label.DrawX;
                    float desiredY = label.Y - 10;

                    SharpDX.RectangleF candidate = new SharpDX.RectangleF(desiredX, desiredY, label.Width, 20);

                    // PHASE 3: Limit collision iterations based on actual count
                    int maxIterations = Math.Min(25, placedRects.Count + 5);
                    int safety = 0;
                    while (safety < maxIterations)
                    {
                        bool hit = false;
                        foreach (var rect in placedRects)
                        {
                            if (candidate.Intersects(rect))
                            {
                                candidate.X = rect.Right + 5;
                                hit = true;
                                break;
                            }
                        }
                        if (!hit) break;
                        safety++;
                    }

                    // PHASE 1.5: Use cached background brush
                    if (_cachedLabelBgBrush != null)
                    {
                        RenderTarget.FillRectangle(candidate, _cachedLabelBgBrush);
                    }

                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(candidate.X, candidate.Y), layout, brush);
                    placedRects.Add(candidate);
                }
            }
        }

        private void RenderSignalLabels(ChartControl chartControl, ChartScale chartScale)
        {
            if (signalLabels == null || signalLabels.Count == 0 || RenderTarget == null || dwFactory == null || textFormat == null) return;
            if (Bars == null || ChartBars == null) return;

            Dictionary<int, List<SharpDX.RectangleF>> occupiedSpace = new Dictionary<int, List<SharpDX.RectangleF>>();

            // PHASE 2: Replace LINQ with manual filtering and grouping
            Dictionary<int, List<SignalObj>> signalsByBarDict = new Dictionary<int, List<SignalObj>>();
            foreach (var sig in signalLabels.Values)
            {
                if (sig.BarIdx >= ChartBars.FromIndex && sig.BarIdx <= ChartBars.ToIndex)
                {
                    if (!signalsByBarDict.ContainsKey(sig.BarIdx))
                        signalsByBarDict[sig.BarIdx] = new List<SignalObj>();
                    signalsByBarDict[sig.BarIdx].Add(sig);
                }
            }

            foreach (var kvp in signalsByBarDict)
            {
                int idx = kvp.Key;
                var signals = kvp.Value;
                float barX = chartControl.GetXByBarIndex(ChartBars, idx);

                // Manual separation instead of LINQ Where
                List<SignalObj> highSignals = new List<SignalObj>();
                List<SignalObj> lowSignals = new List<SignalObj>();
                foreach (var sig in signals)
                {
                    if (sig.IsHigh) highSignals.Add(sig);
                    else lowSignals.Add(sig);
                }

                highSignals.Sort((a, b) => a.Price.CompareTo(b.Price));
                lowSignals.Sort((a, b) => b.Price.CompareTo(a.Price));

                Action<List<SignalObj>> processList = (list) =>
                {
                    foreach (var sig in list)
                    {
                        float y = (float)chartScale.GetYByValue(sig.Price);
                        float drawY = y;

                        using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, sig.Text, textFormat, 300f, 50f))
                        {
                            float w = layout.Metrics.Width;
                            float h = layout.Metrics.Height;
                            float drawX = barX - (w / 2);

                            if (sig.IsHigh) drawY -= h;

                            SharpDX.RectangleF currentRect = new SharpDX.RectangleF(drawX, drawY, w, h);

                            if (!occupiedSpace.ContainsKey(idx)) occupiedSpace[idx] = new List<SharpDX.RectangleF>();
                            List<SharpDX.RectangleF> barRects = occupiedSpace[idx];

                            // PHASE 3: Limit iterations
                            int maxIter = Math.Min(15, barRects.Count + 3);
                            int safety = 0;
                            while (safety < maxIter)
                            {
                                bool collision = false;
                                foreach (var obst in barRects)
                                {
                                    if (currentRect.Intersects(obst))
                                    {
                                        collision = true;
                                        float padding = 4f;

                                        if (sig.IsHigh) currentRect.Y = obst.Top - h - padding;
                                        else currentRect.Y = obst.Bottom + padding;

                                        break;
                                    }
                                }
                                if (!collision) break;
                                safety++;
                            }

                            barRects.Add(currentRect);

                            // PHASE 1.5: Use cached background brush (with alpha adjustment)
                            if (_cachedLabelBgBrush != null)
                            {
                                _cachedLabelBgBrush.Opacity = 0.7f;
                                RenderTarget.FillRectangle(new SharpDX.RectangleF(currentRect.X - 2, currentRect.Y - 1, currentRect.Width + 4, currentRect.Height + 2), _cachedLabelBgBrush);
                                _cachedLabelBgBrush.Opacity = 1.0f;
                            }

                            var sc = ((SolidColorBrush)sig.Brush).Color;
                            var dxColor = new SharpDX.Color((int)sc.R, (int)sc.G, (int)sc.B, 255);
                            using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                            {
                                RenderTarget.DrawTextLayout(new SharpDX.Vector2(currentRect.X, currentRect.Y), layout, brush);
                            }
                        }
                    }
                };

                processList(highSignals);
                processList(lowSignals);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (Bars == null || chartControl == null || chartScale == null) return;

            try { RenderTradeVisuals(chartControl, chartScale); } catch {}

            if (labelQueue != null) labelQueue.Clear();

            // v3.0.4: Render US First Hour Rectangle (background — drawn first so levels are on top)
            if (ShowUSFirstHour && Personality == PersonalityMode.Intraday)
                RenderUSFirstHourRects(chartControl, chartScale);

            // v3.0.0: Render Levels (Conditional on Personality Mode)
            if (Personality == PersonalityMode.Intraday)
            {
                // v3.1.2 perf: Calculate overnight flags once per frame (not per session per frame)
                bool asiaOvernight = GetTimeByZone(AsiaStartTime) > GetTimeByZone(AsiaEndTime);
                bool europeOvernight = GetTimeByZone(EuropeStartTime) > GetTimeByZone(EuropeEndTime);
                bool usOvernight = GetTimeByZone(USStartTime) > GetTimeByZone(USEndTime);

                // Intraday Mode: Render Session Levels
                if (ShowAsia && asiaSessions != null)
                    foreach(var s in asiaSessions) RenderSessionLevels(s, AsiaLineColor, AsiaLabelColor, ShowAsiaHigh, ShowAsiaLow, chartControl, chartScale, asiaOvernight);

                if (ShowEurope && europeSessions != null)
                    foreach(var s in europeSessions) RenderSessionLevels(s, EuropeLineColor, EuropeLabelColor, ShowEuropeHigh, ShowEuropeLow, chartControl, chartScale, europeOvernight);

                if (ShowUS && usSessions != null)
                    foreach(var s in usSessions) RenderSessionLevels(s, USLineColor, USLabelColor, ShowUSHigh, ShowUSLow, chartControl, chartScale, usOvernight);
            }
            else
            {
                // Period Mode: Render Period Levels
                if (periodSessions != null)
                    foreach(var s in periodSessions) RenderPeriodLevels(s, PeriodLineColor, PeriodLabelColor, ShowPeriodHigh, ShowPeriodLow, chartControl, chartScale);

            }

            // v3.0.2: Render Period Dividers ALWAYS (independent of personality - daily ETH in Intraday, period boundaries in other modes)
            RenderPeriodDividers(chartControl, chartScale);

            // Draw Anchored VWAPs (High/Low)
            if (hasHighVWAP)
            {
                // Series 0
                DrawAnchoredLine(sessionHighBarIdx, HighVWAPColor, HighVwapLabel, chartControl, chartScale, -1, -1, 2.0f, true, 0);
            }
            if (hasLowVWAP)
            {
                // Series 1
                DrawAnchoredLine(sessionLowBarIdx, LowVWAPColor, LowVwapLabel, chartControl, chartScale, -1, -1, 2.0f, true, 1);
            }

            // v3.0.4: Health score labels for active VWAPs
            if (ShowVwapHealth)
            {
                int rightEdgeBar = Math.Min(CurrentBar, ChartBars.ToIndex);
                int labelBar = Math.Max(ChartBars.FromIndex, rightEdgeBar + HealthLabelOffsetBars);
                if (hasHighVWAP && sessionHighBarIdx >= 0)
                {
                    float hx = chartControl.GetXByBarIndex(ChartBars, labelBar);
                    double hScore = GetVwapHealthScore(true);
                    RenderHealthLabel(hx, currentHighVWAP, hScore, _highVwapTouchCount, chartControl, chartScale, true);
                }
                if (hasLowVWAP && sessionLowBarIdx >= 0)
                {
                    float lx = chartControl.GetXByBarIndex(ChartBars, labelBar);
                    double lScore = GetVwapHealthScore(false);
                    RenderHealthLabel(lx, currentLowVWAP, lScore, _lowVwapTouchCount, chartControl, chartScale, false);
                }
            }

            // v3.2.0: Auto mode badge — top-left corner showing current ATR mode
            if (StudyTemplate == TouchStudyTemplate.Auto && _lastAutoMode.Length > 0
                && _cachedAutoModeFmt != null && _cachedAutoModeBrush != null && _cachedDetailBgBrush != null)
            {
                string atrVal = (atr != null && CurrentBar >= 14) ? string.Format("{0:F1}", atr[0]) : "?";
                string badgeText = string.Format("AUTO: {0}  ATR={1}  SL={2} TP={3}", _lastAutoMode, atrVal, TouchStudySLTicks, TouchStudyTPTicks);
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, badgeText, _cachedAutoModeFmt, 400, 20))
                {
                    float tw = layout.Metrics.Width;
                    float th = layout.Metrics.Height;
                    float chartW = (float)chartControl.ActualWidth;
                    float chartH = chartScale.GetYByValue(chartScale.MinValue);
                    float px = chartW - tw - 12f;
                    float py = chartH - th - 8f;
                    var bgRect = new SharpDX.RectangleF(px - 3, py - 2, tw + 6, th + 4);
                    RenderTarget.FillRectangle(bgRect, _cachedDetailBgBrush);

                    // Color by mode: green=BAJA_VOL, white=NORMAL, orange=ALTA_VOL
                    SharpDX.Color modeColor;
                    if (_lastAutoMode == "BAJA_VOL") modeColor = new SharpDX.Color(0, 200, 83);
                    else if (_lastAutoMode == "ALTA_VOL") modeColor = new SharpDX.Color(255, 152, 0);
                    else modeColor = SharpDX.Color.White;

                    using (var modeBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, modeColor))
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(px, py), layout, modeBrush);
                }
            }

            // v3.0.5: Touch study labels for active VWAPs
            // v3.2.0 perf: Only render active touches here. Completed ones are already in historicalHighs/Lows.
            if (ShowTouchStudy && _activeFirstTouches != null && _activeFirstTouches.Count > 0)
            {
                RenderTouchStudyLabels(_activeFirstTouches, null, chartControl, chartScale);
            }

            // Draw Historical HIGH VWAPs (segments from previous anchors)
            // v3.0.3: IsSessionEnd anchors use PreviousVWAPColor (white) — these are the final VWAP of each completed session
            if (historicalHighs != null && historicalHighs.Count > 0)
            {
                // v3.0.3 DIAG: One-shot log
                if (!_diagPreviousVwapLogged && ShowDebugLogs)
                {
                    _diagPreviousVwapLogged = true;
                    int sessionEndCount = 0;
                    for (int i = 0; i < historicalHighs.Count; i++)
                        if (historicalHighs[i].IsSessionEnd) sessionEndCount++;
                    Print(string.Format("[DIAG v3.0.3] PreviousVWAP: histHighs={0} sessionEnds={1} prevBrush={2}",
                        historicalHighs.Count, sessionEndCount,
                        (_cachedPreviousVwapBrush != null ? "OK" : "NULL")));
                }

                for (int h = 0; h < historicalHighs.Count; h++)
                {
                    var anchor = historicalHighs[h];
                    if (anchor.EndIdx >= ChartBars.FromIndex && anchor.StartIdx <= ChartBars.ToIndex && anchor.VwapValues != null)
                    {
                        if (anchor.IsSessionEnd && _cachedPreviousVwapBrush != null)
                        {
                            RenderHistoricalSegment(anchor, _cachedPreviousVwapBrush, HistoricalVWAPThickness, chartControl, chartScale);
                        }
                        else if (_cachedHistoricalBrush != null)
                        {
                            RenderHistoricalSegment(anchor, _cachedHistoricalBrush, 1.5f, chartControl, chartScale);
                        }
                        // v3.2.0: Historical health labels removed — companion indicator (bottom panel) is sufficient
                        // v3.0.5: Touch study labels for this historical High VWAP
                        if (ShowTouchStudy && anchor.FirstTouches != null && anchor.FirstTouches.Count > 0)
                            RenderTouchStudyLabels(anchor.FirstTouches, anchor.VwapValues, chartControl, chartScale);
                    }
                }
            }

            // Draw Historical LOW VWAPs (segments from previous anchors)
            if (historicalLows != null && historicalLows.Count > 0)
            {
                for (int h = 0; h < historicalLows.Count; h++)
                {
                    var anchor = historicalLows[h];
                    if (anchor.EndIdx >= ChartBars.FromIndex && anchor.StartIdx <= ChartBars.ToIndex && anchor.VwapValues != null)
                    {
                        if (anchor.IsSessionEnd && _cachedPreviousVwapBrush != null)
                        {
                            RenderHistoricalSegment(anchor, _cachedPreviousVwapBrush, HistoricalVWAPThickness, chartControl, chartScale);
                        }
                        else if (_cachedHistoricalBrush != null)
                        {
                            RenderHistoricalSegment(anchor, _cachedHistoricalBrush, 1.5f, chartControl, chartScale);
                        }
                        // v3.2.0: Historical health labels removed — companion indicator (bottom panel) is sufficient
                        // v3.0.5: Touch study labels for this historical Low VWAP
                        if (ShowTouchStudy && anchor.FirstTouches != null && anchor.FirstTouches.Count > 0)
                            RenderTouchStudyLabels(anchor.FirstTouches, anchor.VwapValues, chartControl, chartScale);
                    }
                }
            }

            // Draw Internal VWAPs (Active)
            if (EnableInternalLogic)
            {
                if (hasInternalHighVWAP && internalHighBarIdx >= 0)
                    DrawInternalVWAP(internalHighBarIdx, -1, InternalVwapColor, chartControl, chartScale, 2);

                if (hasInternalLowVWAP && internalLowBarIdx >= 0)
                    DrawInternalVWAP(internalLowBarIdx, -1, InternalVwapColor, chartControl, chartScale, 3);
            }

            // Draw Historical Internal HIGH VWAPs
            if (EnableInternalLogic && historicalInternalHighs != null)
            {
                foreach (var anchor in historicalInternalHighs)
                {
                    if (anchor.EndIdx >= ChartBars.FromIndex && anchor.StartIdx <= ChartBars.ToIndex && anchor.VwapValues != null)
                    {
                        DrawHistoricalVWAP(anchor.VwapValues, anchor.StartIdx, anchor.EndIdx, SharpDX.Color.LightGray, chartControl, chartScale, InternalVwapThickness, true);
                    }
                }
            }

            // Draw Historical Internal LOW VWAPs
            if (EnableInternalLogic && historicalInternalLows != null)
            {
                foreach (var anchor in historicalInternalLows)
                {
                    if (anchor.EndIdx >= ChartBars.FromIndex && anchor.StartIdx <= ChartBars.ToIndex && anchor.VwapValues != null)
                    {
                        DrawHistoricalVWAP(anchor.VwapValues, anchor.StartIdx, anchor.EndIdx, SharpDX.Color.LightGray, chartControl, chartScale, InternalVwapThickness, true);
                    }
                }
            }

            // Render Signal Labels
            RenderSignalLabels(chartControl, chartScale);

            // Flush Labels
            RenderQueuedLabels(chartControl);

            // Draw Countdown
            if (ShowLabels && ShowCountdown && !string.IsNullOrEmpty(_currentCountdownText))
            {
                int idx = Bars.Count - 1;
                float x = chartControl.GetXByBarIndex(ChartBars, idx) + CountdownOffsetX;
                double price = High.GetValueAt(idx) + (CountdownOffsetY * TickSize);
                float y = (float)chartScale.GetYByValue(price);

                using (var countdownFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, (float)CountdownFontSize))
                {
                    System.Windows.Media.Color sysColor = ((SolidColorBrush)CountdownTextColor).Color;
                    SharpDX.Color dxColor = new SharpDX.Color(sysColor.R, sysColor.G, sysColor.B, sysColor.A);

                    using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                    {
                        RenderTarget.DrawText(_currentCountdownText, countdownFormat, new SharpDX.RectangleF(x, y, 200, 50), brush);
                    }
                }
            }
        }

        private void RenderTradeVisuals(ChartControl chartControl, ChartScale chartScale)
        {
            if (Bars == null || RenderTarget == null) return;

            // 1. Draw Ghost Brackets if Armed
            if (_isEntryArmed)
            {
                float x = chartControl.GetXByBarIndex(ChartBars, Bars.Count - 1) + 40; // Forward projection
                float w = 60;

                // Entry Line (Current Price)
                float yEntry = (float)chartScale.GetYByValue(_ghostEntry);

                // PHASE 1.5: Use cached brushes and stroke style
                if (_cachedGoldenrodBrush != null && _cachedDashStyle != null)
                {
                    RenderTarget.DrawLine(new SharpDX.Vector2(x, yEntry), new SharpDX.Vector2(x + w, yEntry), _cachedGoldenrodBrush, 2, _cachedDashStyle);
                }

                // TP Line (Ghost)
                if (_ghostTP > 0)
                {
                    float yTP = (float)chartScale.GetYByValue(_ghostTP);
                    if (_cachedLimeGreenBrush != null && _cachedDashStyle != null)
                    {
                        RenderTarget.DrawLine(new SharpDX.Vector2(x, yTP), new SharpDX.Vector2(x + w, yTP), _cachedLimeGreenBrush, 2, _cachedDashStyle);
                    }
                    DrawLabel("TP (Est)", x + w + 5, yTP, Brushes.LimeGreen, chartControl, DateTime.Now, false);
                }

                // Draw "ARMED" Label
                DrawLabel("ARMED", x, yEntry - 20, Brushes.Goldenrod, chartControl, DateTime.Now, false);
            }
        }

        private void DrawDirectLine(double price, float x1, float x2, ChartScale chartScale, Brush brush, string label, SharpDX.DirectWrite.TextFormat fmt)
        {
            float y = (float)chartScale.GetYByValue(price);

            System.Windows.Media.Color mColor = ((SolidColorBrush)brush).Color;
            SharpDX.Color dxColor = new SharpDX.Color(mColor.R, mColor.G, mColor.B, mColor.A);

            var dxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);

            RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), dxBrush, 1.0f);

            var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label, fmt, 100f, 20f);
            float textW = layout.Metrics.Width;
            float textH = layout.Metrics.Height;

            System.Windows.Media.Color bgColor = ((SolidColorBrush)LabelBackgroundColor).Color;
            SharpDX.Color dxBgColor = new SharpDX.Color((byte)bgColor.R, (byte)bgColor.G, (byte)bgColor.B, (byte)128);

            using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
            {
                bgBrush.Opacity = 0.5f;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x2, y - textH/2, textW + 4, textH), bgBrush);
            }

            RenderTarget.DrawText(label, fmt, new SharpDX.RectangleF(x2 + 2, y - textH/2, textW, textH), dxBrush);

            dxBrush.Dispose();
            layout.Dispose();
        }

        private void DrawAnchoredLine(int startIdx, Brush color, string label, ChartControl chartControl, ChartScale chartScale, int limitIdx = -1, int visualStartIdx = -1, float thickness = 2.0f, bool showLabel = true, int seriesIdx = -1)
        {
            if (Bars == null || RenderTarget == null) return;

            int endIdx = (limitIdx == -1) ? Bars.Count - 1 : limitIdx;
            int safeStart = Math.Max(0, startIdx);
            int safeEnd = Math.Min(Bars.Count - 1, endIdx);

            int safeVisualStart = Math.Max(safeStart, (visualStartIdx == -1) ? safeStart : visualStartIdx);

            if (safeStart > safeEnd) return;
            if (safeEnd < ChartBars.FromIndex || safeStart > ChartBars.ToIndex) return;

            // OPTIMIZATION: Use pre-calculated Values if seriesIdx provided
            // This avoids O(N) recalculation per frame
            
            SharpDX.Vector2? lastPoint = null;
            SharpDX.Vector2? lastLabelPoint = null;
            var solidColor = ((SolidColorBrush)color).Color;
            var colorWithAlpha = new SharpDX.Color((int)solidColor.R, (int)solidColor.G, (int)solidColor.B, 255);

            using (var lineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, colorWithAlpha))
            {
                int rangeStart = Math.Max(safeVisualStart, ChartBars.FromIndex - 1); // Draw bit before for continuity
                int rangeEnd = Math.Min(safeEnd, ChartBars.ToIndex + 1);

                if (rangeStart > rangeEnd) return;

                // Fallback to calculation if seriesIdx not provided (should not happen with updated calls)
                if (seriesIdx == -1) 
                {
                     // Legacy Loop (Removed for brevity, assuming we always update calls)
                     // If needed, we can re-implement it, but for now we enforce seriesIdx or fail safely.
                     if (ShowDebugLogs && CurrentBar % 100 == 0) Print("DrawAnchoredLine: seriesIdx is -1! Performance degraded.");
                     return; 
                }

                // Initial Point (Connect from previous if valid)
                // We need to find the first valid point in or just before the view
                // For 'Values', we can just loop the viewable range.
                
                int lastValidIdx = -1; // Track last valid index to detect gaps

                for (int i = rangeStart; i <= rangeEnd; i++)
                {
                    if (i < 0 || i >= Values[seriesIdx].Count) continue;

                    // FIX: For historical segments, strictly enforce the range boundaries
                    // Don't draw points outside the segment's startIdx to endIdx range
                    if (i < safeStart || i > safeEnd) continue;

                    double vwap = Values[seriesIdx].GetValueAt(i);

                    // Logic check: Values might be NaN if not active
                    if (double.IsNaN(vwap))
                    {
                        // NaN means gap - reset lastPoint to prevent connecting across segments
                        lastPoint = null;
                        lastValidIdx = -1;
                        continue;
                    }

                    float x = chartControl.GetXByBarIndex(ChartBars, i);
                    float y = (float)chartScale.GetYByValue(vwap);

                    SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                    if (lastPoint.HasValue)
                    {
                         // FIX: Only draw line if previous point was adjacent (no gap)
                         // This prevents connecting across different VWAP segments
                         if (lastValidIdx == i - 1)
                         {
                             RenderTarget.DrawLine(lastPoint.Value, currentPoint, lineBrush, thickness);
                         }
                    }
                    // NOTE: Removed the "else if" block that tried to connect from i-1
                    // This was causing diagonal lines when the first visible bar of a new VWAP
                    // tried to connect to the last bar of the previous (different) VWAP

                    lastPoint = currentPoint;
                    lastLabelPoint = currentPoint;
                    lastValidIdx = i;
                }
            }

            if (showLabel && ShowLabels && !string.IsNullOrEmpty(label) && lastLabelPoint.HasValue && safeEnd >= ChartBars.FromIndex && safeEnd <= ChartBars.ToIndex)
            {
                DateTime time = (safeEnd < Bars.Count) ? Bars.GetTime(safeEnd) : DateTime.Now;
                DrawLabel(label, lastLabelPoint.Value.X, lastLabelPoint.Value.Y, color, chartControl, time, false);
            }
        }

        private void DrawInternalVWAP(int startIdx, int endIdx, SharpDX.Color color, ChartControl chartControl, ChartScale chartScale, int seriesIdx)
        {
            if (Bars == null || startIdx < 0 || RenderTarget == null) return;
            if (startIdx > ChartBars.ToIndex) return; // Starts after current view
            if (_cachedDashStyle == null) return; // Need cached stroke

            // Constants for Internal VWAP
            float iThickness = InternalVwapThickness;

            int limitLimit = Bars.Count - 1;
            // If endIdx is provided (not -1), use it as the limit
            if (endIdx != -1) limitLimit = endIdx;

            int viewStart = Math.Max(startIdx, ChartBars.FromIndex);
            int viewEnd = Math.Min(limitLimit, ChartBars.ToIndex);

            if (viewStart > viewEnd) return;

            // OPTIMIZATION: Use pre-calculated Values[seriesIdx]
            if (seriesIdx < 0 || seriesIdx >= 4) // Hardcoded known plot count to avoid CS0019 (LINQ conflict)
            {
                 if (ShowDebugLogs && CurrentBar % 100 == 0) Print("DrawInternalVWAP: Invalid seriesIdx!");
                 return;
            }

            // PHASE 1.6: Use cached stroke style, create brush only for non-standard colors
            bool useInternalCached = (color.R == InternalVwapColor.R && color.G == InternalVwapColor.G && color.B == InternalVwapColor.B);
            bool useGrayCached = (color.R == SharpDX.Color.LightGray.R && color.G == SharpDX.Color.LightGray.G && color.B == SharpDX.Color.LightGray.B);

            SharpDX.Direct2D1.SolidColorBrush brush = null;
            bool disposeBrush = false;

            if (useInternalCached && _cachedInternalVwapBrush != null)
                brush = _cachedInternalVwapBrush;
            else if (useGrayCached && _cachedGrayBrush != null)
                brush = _cachedGrayBrush;
            else
            {
                brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, color);
                disposeBrush = true;
            }

            try
            {
                SharpDX.Vector2? lastPoint = null;
                int lastValidIdx = -1; // Track last valid index to detect gaps

                for (int i = viewStart; i <= viewEnd; i++)
                {
                   // FIX: Strictly enforce segment boundaries
                   if (i < startIdx || (endIdx != -1 && i > endIdx)) continue;

                   double vwap = Values[seriesIdx].GetValueAt(i);

                   // NaN means gap - reset to prevent connecting across segments
                   if (double.IsNaN(vwap))
                   {
                       lastPoint = null;
                       lastValidIdx = -1;
                       continue;
                   }

                   float x = chartControl.GetXByBarIndex(ChartBars, i);
                   float y = (float)chartScale.GetYByValue(vwap);
                   SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                   if (lastPoint.HasValue)
                   {
                       // FIX: Only draw if previous point was adjacent (no gap)
                       if (lastValidIdx == i - 1)
                       {
                           RenderTarget.DrawLine(lastPoint.Value, currentPoint, brush, iThickness, _cachedDashStyle);
                       }
                   }
                   // NOTE: Removed the "else if" block that tried to connect from i-1
                   // This was causing diagonal lines between different VWAP segments

                   lastPoint = currentPoint;
                   lastValidIdx = i;
                }
            }
            finally
            {
                if (disposeBrush && brush != null)
                    brush.Dispose();
            }
        }

        /// <summary>
        /// v3.0.4: Render VWAP health score label with visual bar.
        /// Format: "3.2 ███░░░░" with color gradient (green > yellow > red).
        /// </summary>
        private void RenderHealthLabel(float x, double vwapPrice, double score, int touchCount, ChartControl chartControl, ChartScale chartScale, bool isHighVwap)
        {
            if (RenderTarget == null || dwFactory == null) return;

            // Build bar: 7 blocks, score capped at 10
            double cappedScore = Math.Min(score, 10.0);
            int filledBlocks = (int)Math.Round(cappedScore * 7.0 / 10.0);
            filledBlocks = Math.Max(0, Math.Min(7, filledBlocks));
            string bar = new string('\u2588', filledBlocks) + new string('\u2591', 7 - filledBlocks);
            string labelText = string.Format("{0:F1} {1}", score, bar);

            // Select color brush based on score
            SharpDX.Direct2D1.SolidColorBrush brush;
            // v3.2.0: Use configurable threshold for color
            if (score >= HealthStrongThreshold)
                brush = _cachedHealthGreenBrush;
            else if (score >= HealthWeakThreshold)
                brush = _cachedHealthYellowBrush;
            else
                brush = _cachedHealthRedBrush;

            if (brush == null) return;

            // v3.0.4: Apply tick offset — Supply label goes UP, Demand label goes DOWN
            double tickSize = TickSize > 0 ? TickSize : 0.25;
            double offsetPrice = HealthLabelOffsetTicks * tickSize;
            double labelPrice = isHighVwap ? vwapPrice + offsetPrice : vwapPrice - offsetPrice;
            float drawY = (float)chartScale.GetYByValue(labelPrice);

            var useFmt = _cachedHealthLabelFmt ?? textFormat;
            if (useFmt != null)
            {
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, labelText, useFmt, 300, 30))
                {
                    float textWidth = layout.Metrics.Width;
                    float textHeight = layout.Metrics.Height;
                    float drawX = x;

                    // Background rect with padding
                    var bgRect = new SharpDX.RectangleF(drawX - 3, drawY - 2, textWidth + 6, textHeight + 4);
                    if (_cachedBgBrush != null)
                        RenderTarget.FillRectangle(bgRect, _cachedBgBrush);

                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(drawX, drawY), layout, brush);
                }
            }
        }

        /// <summary>
        /// v3.0.7: Render touch study with full trade visualization.
        /// Episode-first touches: entry arrow + SL/TP horizontal lines + exit marker.
        /// Non-episode touches: small dot only (in All mode) or hidden (in filtered mode).
        /// </summary>
        private void RenderTouchStudyLabels(List<FirstTouchRecord> touches, Dictionary<int, double> vwapValues,
            ChartControl chartControl, ChartScale chartScale)
        {
            if (RenderTarget == null || dwFactory == null || _cachedTouchStudyBrush == null) return;
            if (touches == null || touches.Count == 0) return;

            // v3.1.2: Use chart date (last bar) instead of system date — fixes playback/replay
            DateTime chartDate = (Bars != null && CurrentBar >= 0 && CurrentBar < Bars.Count) ? Bars.GetTime(CurrentBar).Date : DateTime.Today;
            DateTime cutoffDate = chartDate.AddDays(-TouchStudyDays);
            double tickSize = TickSize > 0 ? TickSize : 0.25;
            bool filterActive = TouchStudyFilter != TouchStudyFilterMode.All;

            try
            {
            foreach (var t in touches)
            {
                if (t.BarIdx < ChartBars.FromIndex || t.BarIdx > ChartBars.ToIndex) continue;
                if (t.BarIdx < 0 || t.BarIdx > CurrentBar) continue;

                try { if (Bars.GetTime(t.BarIdx).Date < cutoffDate) continue; }
                catch { continue; }

                // --- Use pre-classified config from tracking (fallback to runtime calc for old data) ---
                string config = t.Config;
                if (string.IsNullOrEmpty(config))
                {
                    // v3.2.0: Use configurable thresholds
                    bool sf = t.HighHealthScore >= HealthStrongThreshold; bool dd = t.LowHealthScore < HealthWeakThreshold;
                    bool df = t.LowHealthScore >= HealthStrongThreshold; bool sd = t.HighHealthScore < HealthWeakThreshold;
                    if (!t.TouchedHighVwap && sf && dd) config = "B";
                    else if (t.TouchedHighVwap && sf && dd) config = "C";
                    else if (t.TouchedHighVwap && df && sd) config = "A";
                    else if (!t.TouchedHighVwap && df && sd) config = "D";
                    else config = "-";
                }

                // --- Apply config filter (property enum) ---
                if (filterActive)
                {
                    bool pass = false;
                    if (TouchStudyFilter == TouchStudyFilterMode.ConfigA && config == "A") pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigB && config == "B") pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigC && config == "C") pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigD && config == "D") pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigBC && (config == "B" || config == "C")) pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigCD && (config == "C" || config == "D")) pass = true;
                    else if (TouchStudyFilter == TouchStudyFilterMode.ConfigAD && (config == "A" || config == "D")) pass = true;
                    if (!pass) continue;
                }

                // --- v3.2.0: Apply ATR and Separation filters (from templates or manual) ---
                if (TouchStudyMaxATR > 0 && t.ATRValue > TouchStudyMaxATR) continue;
                if (TouchStudyMaxSeparation > 0 && t.Separation > TouchStudyMaxSeparation) continue;

                // --- Apply toolbar config toggles (v3.0.8) ---
                // If any toggle is off, user is actively filtering → hide unclassified too
                bool anyToggleOff = !_showCfgA || !_showCfgB || !_showCfgC || !_showCfgD;
                if (config == "A" && !_showCfgA) continue;
                if (config == "B" && !_showCfgB) continue;
                if (config == "C" && !_showCfgC) continue;
                if (config == "D" && !_showCfgD) continue;
                if (config == "-" && anyToggleOff) continue;

                // In filtered mode, only show episode-first touches (trade entries)
                if (filterActive && !t.IsEpisodeFirst) continue;

                double vwapPrice = t.VwapPrice;
                double vwapLookup;
                if (vwapValues != null && vwapValues.TryGetValue(t.BarIdx, out vwapLookup))
                    vwapPrice = vwapLookup;
                if (vwapPrice <= 0) continue;

                float entryX = chartControl.GetXByBarIndex(ChartBars, t.BarIdx);

                // --- Brush by config ---
                SharpDX.Direct2D1.SolidColorBrush cfgBrush;
                if (config == "B") cfgBrush = _cachedConfigBBrush;
                else if (config == "C") cfgBrush = _cachedConfigCBrush;
                else if (config == "A") cfgBrush = _cachedHealthGreenBrush;
                else if (config == "D") cfgBrush = _cachedHealthYellowBrush;
                else cfgBrush = _cachedTouchStudyBrush;
                if (cfgBrush == null) cfgBrush = _cachedTouchStudyBrush;

                // v3.1.2: Skip non-episode touches (dots over wicks confuse visualization)
                if (!t.IsEpisodeFirst)
                    continue;

                // v3.0.9: Skip stale open-trade viz from historical anchor copies
                // These are snapshots taken at re-anchor time and never updated.
                // The live copy in _activeFirstTouches handles rendering for open trades.
                if (vwapValues != null && t.IsEpisodeFirst && t.ExitType == 0)
                    continue;

                // ============================================================
                // EPISODE-FIRST TOUCH: Full trade visualization
                // ============================================================
                bool isShort = (config == "B" || config == "C");
                float entryY = (float)chartScale.GetYByValue(t.TouchPrice);

                // --- Entry arrow (triangle) ---
                float arrowH = 14f;
                float arrowW = 10f;
                if (isShort)
                {
                    // DOWN triangle above entry price
                    float triTopY = entryY - arrowH - 4;
                    var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                    var sink = geo.Open();
                    sink.BeginFigure(new SharpDX.Vector2(entryX, entryY - 4), SharpDX.Direct2D1.FigureBegin.Filled);
                    sink.AddLine(new SharpDX.Vector2(entryX - arrowW, triTopY));
                    sink.AddLine(new SharpDX.Vector2(entryX + arrowW, triTopY));
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    sink.Close();
                    RenderTarget.FillGeometry(geo, cfgBrush);
                    sink.Dispose(); geo.Dispose();
                }
                else
                {
                    // UP triangle below entry price
                    float triBottomY = entryY + arrowH + 4;
                    var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                    var sink = geo.Open();
                    sink.BeginFigure(new SharpDX.Vector2(entryX, entryY + 4), SharpDX.Direct2D1.FigureBegin.Filled);
                    sink.AddLine(new SharpDX.Vector2(entryX - arrowW, triBottomY));
                    sink.AddLine(new SharpDX.Vector2(entryX + arrowW, triBottomY));
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    sink.Close();
                    RenderTarget.FillGeometry(geo, cfgBrush);
                    sink.Dispose(); geo.Dispose();
                }

                // --- Config label (A/B/C/D/-) for colorblind accessibility ---
                {
                    string cfgLabel = string.IsNullOrEmpty(config) ? "-" : config;
                    float labelY;
                    // Place label beyond the triangle tip: short=above triangle, long=below triangle
                    if (isShort)
                        labelY = entryY - arrowH - 4f - 14f; // above the down-triangle tip
                    else
                        labelY = entryY + arrowH + 4f + 2f;  // below the up-triangle tip

                    if (_cachedCfgLabelFmt != null)
                    {
                        var labelRect = new SharpDX.RectangleF(entryX - 10f, labelY, 20f, 16f);
                        RenderTarget.DrawText(cfgLabel, _cachedCfgLabelFmt, labelRect, cfgBrush);
                    }
                }

                // --- SL and TP price levels ---
                double slPrice, tpPrice;
                if (isShort)
                {
                    slPrice = t.TouchPrice + TouchStudySLTicks * tickSize;
                    tpPrice = t.TouchPrice - TouchStudyTPTicks * tickSize;
                }
                else
                {
                    slPrice = t.TouchPrice - TouchStudySLTicks * tickSize;
                    tpPrice = t.TouchPrice + TouchStudyTPTicks * tickSize;
                }

                float slY = (float)chartScale.GetYByValue(slPrice);
                float tpY = (float)chartScale.GetYByValue(tpPrice);

                // Determine end bar (exit bar or last visible bar)
                int endBar = t.ExitBarIdx > 0 ? t.ExitBarIdx : Math.Min(CurrentBar, ChartBars.ToIndex);
                if (endBar < t.BarIdx) endBar = t.BarIdx + 1;
                float endX = chartControl.GetXByBarIndex(ChartBars, Math.Min(endBar, ChartBars.ToIndex));

                // --- Open trade: dotted line from entry to current price ---
                if (t.ExitType == 0)
                {
                    float curY = (float)chartScale.GetYByValue(Close.GetValueAt(Math.Min(CurrentBar, ChartBars.ToIndex)));
                    // v3.1.2 perf: Use cached brushes instead of per-frame allocation
                    if (_cachedOpenTradeBrush != null)
                    {
                        if (_cachedDashStyle != null)
                            RenderTarget.DrawLine(new SharpDX.Vector2(entryX, entryY), new SharpDX.Vector2(endX, curY), _cachedOpenTradeBrush, 1.0f, _cachedDashStyle);
                        else
                            RenderTarget.DrawLine(new SharpDX.Vector2(entryX, entryY), new SharpDX.Vector2(endX, curY), _cachedOpenTradeBrush, 0.75f);
                    }
                    // SL/TP reference lines for open trade
                    if (_cachedOpenSlRefBrush != null && _cachedDashStyle != null)
                        RenderTarget.DrawLine(new SharpDX.Vector2(entryX, slY), new SharpDX.Vector2(endX, slY), _cachedOpenSlRefBrush, 0.5f, _cachedDashStyle);
                    if (_cachedOpenTpRefBrush != null && _cachedDashStyle != null)
                        RenderTarget.DrawLine(new SharpDX.Vector2(entryX, tpY), new SharpDX.Vector2(endX, tpY), _cachedOpenTpRefBrush, 0.5f, _cachedDashStyle);
                }

                // --- Diagonal line from entry to exit (like Signal 2 trade viz) ---
                if (t.ExitBarIdx > 0 && t.ExitType > 0)
                {
                    float exitX = chartControl.GetXByBarIndex(ChartBars, Math.Min(t.ExitBarIdx, ChartBars.ToIndex));
                    float exitY = (float)chartScale.GetYByValue(t.ExitPrice);

                    // Main diagonal: entry price → exit price, white dashed, 2px thick
                    if (_cachedWhiteDashBrush != null)
                    {
                        if (_cachedDashStyle != null)
                            RenderTarget.DrawLine(new SharpDX.Vector2(entryX, entryY), new SharpDX.Vector2(exitX, exitY), _cachedWhiteDashBrush, 2.0f, _cachedDashStyle);
                        else
                            RenderTarget.DrawLine(new SharpDX.Vector2(entryX, entryY), new SharpDX.Vector2(exitX, exitY), _cachedWhiteDashBrush, 2.0f);
                    }

                    // Thin SL reference line (dashed, subtle)
                    if (_cachedSlRefBrush != null && _cachedDashStyle != null)
                        RenderTarget.DrawLine(new SharpDX.Vector2(entryX, slY), new SharpDX.Vector2(exitX, slY), _cachedSlRefBrush, 0.5f, _cachedDashStyle);

                    // Thin TP reference line (dashed, subtle)
                    if (_cachedTpRefBrush != null && _cachedDashStyle != null)
                        RenderTarget.DrawLine(new SharpDX.Vector2(entryX, tpY), new SharpDX.Vector2(exitX, tpY), _cachedTpRefBrush, 0.5f, _cachedDashStyle);

                    if (t.ExitType == 1 && _cachedTpDiamondBrush != null) // TP — diamond (green)
                    {
                        float ds = 6f;
                        var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                        var sink = geo.Open();
                        sink.BeginFigure(new SharpDX.Vector2(exitX, exitY - ds), SharpDX.Direct2D1.FigureBegin.Filled);
                        sink.AddLine(new SharpDX.Vector2(exitX + ds, exitY));
                        sink.AddLine(new SharpDX.Vector2(exitX, exitY + ds));
                        sink.AddLine(new SharpDX.Vector2(exitX - ds, exitY));
                        sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                        sink.Close();
                        RenderTarget.FillGeometry(geo, _cachedTpDiamondBrush);
                        sink.Dispose(); geo.Dispose();
                    }
                    else if (t.ExitType == 2 && _cachedSlSquareBrush != null) // SL — square (red)
                    {
                        float ss = 5f;
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(exitX - ss, exitY - ss, ss * 2, ss * 2), _cachedSlSquareBrush);
                    }
                    else if (t.ExitType == 3) // EOD — circle (gray)
                    {
                        RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(exitX, exitY), 5f, 5f), _cachedGrayBrush);
                    }
                }

                // --- Config label near entry arrow ---
                if (filterActive && _cachedDetailLabelFmt != null && _cachedDetailBgBrush != null)
                {
                    string label = string.Format("{0} H:{1:F1} L:{2:F1}", config, t.HighHealthScore, t.LowHealthScore);
                    // v3.2.0 perf: Use cached TextFormat + brush (were per-touch per-frame allocations!)
                    using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, label, _cachedDetailLabelFmt, 180, 20))
                    {
                        float tw = layout.Metrics.Width;
                        float th = layout.Metrics.Height;
                        float lx = entryX - tw / 2;
                        float ly = isShort ? entryY - arrowH - th - 8 : entryY + arrowH + 8;
                        var bgRect = new SharpDX.RectangleF(lx - 2, ly - 1, tw + 4, th + 2);
                        RenderTarget.FillRectangle(bgRect, _cachedDetailBgBrush);
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(lx, ly), layout, cfgBrush);
                    }
                }
            }
            }
            catch
            {
                // Don't let touch study errors kill the entire rendering pipeline
            }
        }

        /// <summary>
        /// v3.0.2: Renders a historical VWAP segment using a pre-cached brush directly.
        /// Used for PreviousVWAPColor (white) vs regular historical color (gray).
        /// </summary>
        private void RenderHistoricalSegment(HistoricalAnchor anchor, SharpDX.Direct2D1.SolidColorBrush brush, float thickness, ChartControl chartControl, ChartScale chartScale)
        {
            if (Bars == null || RenderTarget == null || brush == null) return;
            if (anchor.VwapValues == null || anchor.VwapValues.Count == 0) return;
            if (anchor.StartIdx < 0 || anchor.EndIdx < anchor.StartIdx) return;
            if (anchor.EndIdx < ChartBars.FromIndex || anchor.StartIdx > ChartBars.ToIndex) return;

            int viewStart = Math.Max(anchor.StartIdx, ChartBars.FromIndex);
            int viewEnd = Math.Min(anchor.EndIdx, ChartBars.ToIndex);
            if (viewStart > viewEnd) return;

            SharpDX.Vector2? lastPoint = null;
            int lastValidIdx = -1;

            for (int i = viewStart; i <= viewEnd; i++)
            {
                // v3.1.2 perf: TryGetValue instead of ContainsKey + indexer
                double vwap;
                if (!anchor.VwapValues.TryGetValue(i, out vwap) || double.IsNaN(vwap))
                {
                    lastPoint = null;
                    lastValidIdx = -1;
                    continue;
                }

                float x = chartControl.GetXByBarIndex(ChartBars, i);
                float y = (float)chartScale.GetYByValue(vwap);
                SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                if (lastPoint.HasValue && lastValidIdx == i - 1)
                {
                    RenderTarget.DrawLine(lastPoint.Value, currentPoint, brush, thickness);
                }

                lastPoint = currentPoint;
                lastValidIdx = i;
            }
        }

        /// <summary>
        /// Draws historical VWAP segments using pre-stored values from Dictionary.
        /// This prevents diagonal line artifacts caused by Values[] being overwritten.
        /// </summary>
        private void DrawHistoricalVWAP(Dictionary<int, double> vwapValues, int startIdx, int endIdx, SharpDX.Color color, ChartControl chartControl, ChartScale chartScale, float thickness = 1.5f, bool isDashed = false)
        {
            if (Bars == null || RenderTarget == null || vwapValues == null || vwapValues.Count == 0) return;
            if (startIdx < 0 || endIdx < startIdx) return;
            if (endIdx < ChartBars.FromIndex || startIdx > ChartBars.ToIndex) return;

            int viewStart = Math.Max(startIdx, ChartBars.FromIndex);
            int viewEnd = Math.Min(endIdx, ChartBars.ToIndex);

            if (viewStart > viewEnd) return;

            // PHASE 1: Use cached brushes when possible
            bool useGrayCached = (color.R == SharpDX.Color.LightGray.R && color.G == SharpDX.Color.LightGray.G && color.B == SharpDX.Color.LightGray.B);
            bool useHistoricalCached = (color.R == ((SolidColorBrush)HistoricalVWAPColor).Color.R &&
                                        color.G == ((SolidColorBrush)HistoricalVWAPColor).Color.G &&
                                        color.B == ((SolidColorBrush)HistoricalVWAPColor).Color.B);

            SharpDX.Direct2D1.SolidColorBrush brush = null;
            bool disposeBrush = false;

            if (useGrayCached && _cachedGrayBrush != null)
                brush = _cachedGrayBrush;
            else if (useHistoricalCached && _cachedHistoricalBrush != null)
                brush = _cachedHistoricalBrush;
            else
            {
                brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, color);
                disposeBrush = true;
            }

            var strokeStyle = isDashed ? _cachedDashStyle : null;

            try
            {
                SharpDX.Vector2? lastPoint = null;
                int lastValidIdx = -1;

                for (int i = viewStart; i <= viewEnd; i++)
                {
                    // v3.1.2 perf: TryGetValue instead of ContainsKey + indexer (single lookup)
                    double vwap;
                    if (!vwapValues.TryGetValue(i, out vwap))
                    {
                        // No value for this index - reset connection
                        lastPoint = null;
                        lastValidIdx = -1;
                        continue;
                    }

                    if (double.IsNaN(vwap))
                    {
                        lastPoint = null;
                        lastValidIdx = -1;
                        continue;
                    }

                    float x = chartControl.GetXByBarIndex(ChartBars, i);
                    float y = (float)chartScale.GetYByValue(vwap);
                    SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                    if (lastPoint.HasValue && lastValidIdx == i - 1)
                    {
                        RenderTarget.DrawLine(lastPoint.Value, currentPoint, brush, thickness, strokeStyle);
                    }

                    lastPoint = currentPoint;
                    lastValidIdx = i;
                }
            }
            finally
            {
                if (disposeBrush && brush != null)
                    brush.Dispose();
            }
        }

        private void RenderSessionLevels(SessionLevelInfo session, Brush lineColor, Brush labelColor, bool showHigh, bool showLow, ChartControl chartControl, ChartScale chartScale, bool isOvernight)
        {
            if (session.StartBarIdx < 0 || session.High == 0) return;
            if (session.StartBarIdx > ChartBars.ToIndex) return;

            int startIdx = Math.Max(0, session.StartBarIdx);
            int endIdx = Bars.Count - 1;

            int limitIdx;
            if (ExtendLinesUntilTouch)
            {
                limitIdx = Bars.Count - 1;
            }
            else
            {
                DateTime cutOff = session.SessionDate.AddDays(1).AddHours(16);
                limitIdx = Bars.GetBar(cutOff);
                if (limitIdx < 0) limitIdx = Bars.Count - 1;
            }

            if (limitIdx < startIdx) limitIdx = startIdx;

            // PERFORMANCE OPTIMIZATION: Early Exit if logic is entirely off-screen (Left)
            // If Extended, we check breaks. If Not Extended, limitIdx determines end.
            if (!ExtendLinesUntilTouch)
            {
                if (limitIdx < ChartBars.FromIndex) return;
            }
            else
            {
                // Extended: Check if both High and Low levels are terminated locally before the view
                // This is a heuristic. If unsure, we draw.
                // If High is broken before View, and Ghost ends before View (or not shown), High is invisible.
                // Same for Low.
                bool highVisible = showHigh;
                if (session.HighBrokenBarIdx != -1 && session.HighBrokenBarIdx < ChartBars.FromIndex)
                {
                   int ghostEnd = (session.HighGhostEndIdx == -1) ? Bars.Count - 1 : session.HighGhostEndIdx;
                   if (ghostEnd < ChartBars.FromIndex) highVisible = false;
                }

                bool lowVisible = showLow;
                if (session.LowBrokenBarIdx != -1 && session.LowBrokenBarIdx < ChartBars.FromIndex)
                {
                   int ghostEnd = (session.LowGhostEndIdx == -1) ? Bars.Count - 1 : session.LowGhostEndIdx;
                   if (ghostEnd < ChartBars.FromIndex) lowVisible = false;
                }
                
                if (!highVisible && !lowVisible) return;
            }

            string suffixText = "";
            bool isGraySuffix = false;

            int days = 0;
            if (ShowDaysAgo)
            {
                int refIdx = (ChartBars != null) ? ChartBars.ToIndex : (Bars.Count - 1);
                if (refIdx >= Bars.Count) refIdx = Bars.Count - 1;
                if (refIdx < 0) refIdx = 0;

                DateTime refDate = (Bars != null && refIdx < Bars.Count) ? Bars.GetTime(refIdx).Date : DateTime.MinValue;

                TimeSpan diff = (refDate != DateTime.MinValue)
                    ? (refDate - session.SessionDate.Date)
                    : TimeSpan.Zero;

                days = (int)diff.TotalDays;

                if (days == 1 && !session.IsActive)
                {
                    if (isOvernight)
                    {
                        days = 0;
                    }
                }

                if (days > 0 && !session.IsActive)
                {
                    suffixText = "  " + days + " days";
                    isGraySuffix = true;
                }
            }

            Action<string, double, int, int> drawLevel = (suffix, price, breakIdx, ghostEndIdx) => {
                int currentLimit = limitIdx;
                int seg1End = currentLimit;
                bool isBroken = (ExtendLinesUntilTouch && breakIdx != -1 && breakIdx < currentLimit);

                if (isBroken) seg1End = breakIdx;
                if (seg1End > Bars.Count-1) seg1End = Bars.Count-1;

                float x1 = chartControl.GetXByBarIndex(ChartBars, startIdx);
                float xEnd1 = chartControl.GetXByBarIndex(ChartBars, seg1End);
                float y = (float)chartScale.GetYByValue(price);

                using(var dxBrush = lineColor.ToDxBrush(RenderTarget))
                {
                    RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(xEnd1, y), dxBrush, SessionLevelThickness);
                }

                float finalLabelX = xEnd1;
                Brush finalLabelBrush = labelColor;
                bool alignRight = false;

                if (isBroken)
                {
                    int activeGhostEnd = (ghostEndIdx == -1) ? Bars.Count - 1 : ghostEndIdx;

                    if (activeGhostEnd > Bars.Count - 1) activeGhostEnd = Bars.Count - 1;
                    if (activeGhostEnd < breakIdx) activeGhostEnd = breakIdx;

                    float xEnd2 = chartControl.GetXByBarIndex(ChartBars, activeGhostEnd);

                    // PHASE 1.5: Ghost line (dashed) after level break
                    if (_cachedGrayBrush != null && _cachedDashStyle != null)
                    {
                        float ghostThickness = Math.Max(1.5f, SessionLevelThickness * 0.75f);
                        RenderTarget.DrawLine(new SharpDX.Vector2(xEnd1, y), new SharpDX.Vector2(xEnd2, y), _cachedGrayBrush, ghostThickness, _cachedDashStyle);
                    }
                    finalLabelX = xEnd2;
                    finalLabelBrush = Brushes.Gray;
                }

                if (ShowLabels)
                {
                    string mainLabel = session.Name + " " + suffix;
                    if (!string.IsNullOrEmpty(suffixText)) mainLabel += suffixText;

                    float currentX = finalLabelX;

                    float screenRight = ChartPanel.X + ChartPanel.W;
                    bool isClamped = false;

                    if (finalLabelX > screenRight)
                    {
                        if (x1 < screenRight)
                        {
                            currentX = screenRight - 5;
                            isClamped = true;
                        }
                        else
                        {
                            return;
                        }
                    }

                    float w1 = DrawLabel(mainLabel, currentX, y, finalLabelBrush, chartControl, session.SessionDate, isClamped);
                }
            };

            if (showHigh) drawLevel("High", session.High, session.HighBrokenBarIdx, session.HighGhostEndIdx);
            if (showLow) drawLevel("Low", session.Low, session.LowBrokenBarIdx, session.LowGhostEndIdx);
        }

        private double GetNonCollidingHighY(double proposedY, double spacing)
        {
            return proposedY;
        }

        private double GetNonCollidingLowY(double proposedY, double spacing)
        {
            return proposedY;
        }

        
        private SharpDX.Direct2D1.SolidColorBrush CreateBrushFromMedia(System.Windows.Media.Brush mediaBrush)
        {
            if (RenderTarget == null || mediaBrush == null) return null;
            // Handle different brush types if needed, but usually SolidColorBrush
            if (mediaBrush is SolidColorBrush solid) 
            {
               var c = solid.Color;
               var dxColor = new SharpDX.Color(c.R, c.G, c.B, c.A);
               return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);
            }
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Gray); // Fallback
        }

        // v3.0.4: Render US First Hour Opening Range rectangles
        private void RenderUSFirstHourRects(ChartControl chartControl, ChartScale chartScale)
        {
            if (RenderTarget == null || USFirstHourColor == null) return;

            var solidColor = ((System.Windows.Media.SolidColorBrush)USFirstHourColor).Color;
            byte alpha = (byte)(255 * USFirstHourOpacity / 100);
            var dxColor = new SharpDX.Color((byte)solidColor.R, (byte)solidColor.G, (byte)solidColor.B, alpha);

            // Draw historical first hours
            if (_historicalFirstHours != null)
            {
                for (int i = 0; i < _historicalFirstHours.Count; i++)
                {
                    var fh = _historicalFirstHours[i];
                    if (fh.EndBarIdx < ChartBars.FromIndex || fh.StartBarIdx > ChartBars.ToIndex) continue;

                    int drawStart = Math.Max(fh.StartBarIdx, ChartBars.FromIndex);
                    int drawEnd = Math.Min(fh.EndBarIdx, ChartBars.ToIndex);

                    float x1 = chartControl.GetXByBarIndex(ChartBars, drawStart);
                    float x2 = chartControl.GetXByBarIndex(ChartBars, drawEnd);
                    float yHigh = (float)chartScale.GetYByValue(fh.High);
                    float yLow = (float)chartScale.GetYByValue(fh.Low);

                    using (var fillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                    {
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, yHigh, x2 - x1, yLow - yHigh), fillBrush);
                    }
                }
            }

            // Draw current (in-progress or completed today) first hour
            if (_usFirstHourStartBarIdx >= 0 && _usFirstHourHigh > 0 && _usFirstHourLow > 0)
            {
                int drawStart = Math.Max(_usFirstHourStartBarIdx, ChartBars.FromIndex);
                int drawEnd = Math.Min(_usFirstHourEndBarIdx, ChartBars.ToIndex);

                if (drawStart <= ChartBars.ToIndex && drawEnd >= ChartBars.FromIndex)
                {
                    float x1 = chartControl.GetXByBarIndex(ChartBars, drawStart);
                    float x2 = chartControl.GetXByBarIndex(ChartBars, drawEnd);
                    float yHigh = (float)chartScale.GetYByValue(_usFirstHourHigh);
                    float yLow = (float)chartScale.GetYByValue(_usFirstHourLow);

                    using (var fillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                    {
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, yHigh, x2 - x1, yLow - yHigh), fillBrush);
                    }
                }
            }
        }

        private void DisposeCachedBrushes()
        {
            _cachedHighVwapBrush?.Dispose(); _cachedHighVwapBrush = null;
            _cachedLowVwapBrush?.Dispose(); _cachedLowVwapBrush = null;
            _cachedHistoricalBrush?.Dispose(); _cachedHistoricalBrush = null;
            _cachedPreviousVwapBrush?.Dispose(); _cachedPreviousVwapBrush = null;  // v3.0.2
            _cachedInternalVwapBrush?.Dispose(); _cachedInternalVwapBrush = null;
            _cachedLabelBgBrush?.Dispose(); _cachedLabelBgBrush = null;
            _cachedGrayBrush?.Dispose(); _cachedGrayBrush = null;
            _cachedGoldenrodBrush?.Dispose(); _cachedGoldenrodBrush = null;
            _cachedLimeGreenBrush?.Dispose(); _cachedLimeGreenBrush = null;
            _cachedDashStyle?.Dispose(); _cachedDashStyle = null;
            _cachedHealthGreenBrush?.Dispose(); _cachedHealthGreenBrush = null;
            _cachedHealthYellowBrush?.Dispose(); _cachedHealthYellowBrush = null;
            _cachedHealthRedBrush?.Dispose(); _cachedHealthRedBrush = null;
            _cachedTouchStudyBrush?.Dispose(); _cachedTouchStudyBrush = null;
            _cachedConfigBBrush?.Dispose(); _cachedConfigBBrush = null;
            _cachedConfigCBrush?.Dispose(); _cachedConfigCBrush = null;
            // v3.1.1 perf: Dispose cached TextFormats and utility brushes
            _cachedCfgLabelFmt?.Dispose(); _cachedCfgLabelFmt = null;
            _cachedHealthLabelFmt?.Dispose(); _cachedHealthLabelFmt = null;
            _cachedBgBrush?.Dispose(); _cachedBgBrush = null;
            _cachedWhiteDashBrush?.Dispose(); _cachedWhiteDashBrush = null;
            _cachedSlRefBrush?.Dispose(); _cachedSlRefBrush = null;
            _cachedTpRefBrush?.Dispose(); _cachedTpRefBrush = null;
            _cachedTpDiamondBrush?.Dispose(); _cachedTpDiamondBrush = null;
            _cachedSlSquareBrush?.Dispose(); _cachedSlSquareBrush = null;
            // v3.1.2 perf: Open-trade brushes
            _cachedOpenTradeBrush?.Dispose(); _cachedOpenTradeBrush = null;
            _cachedOpenSlRefBrush?.Dispose(); _cachedOpenSlRefBrush = null;
            _cachedOpenTpRefBrush?.Dispose(); _cachedOpenTpRefBrush = null;
            // v3.2.0 perf: Detail label resources
            _cachedDetailLabelFmt?.Dispose(); _cachedDetailLabelFmt = null;
            _cachedDetailBgBrush?.Dispose(); _cachedDetailBgBrush = null;
            // v3.2.0: Auto mode badge
            _cachedAutoModeFmt?.Dispose(); _cachedAutoModeFmt = null;
            _cachedAutoModeBrush?.Dispose(); _cachedAutoModeBrush = null;
        }

        // v3.0.0: Render Period Levels (Weekly, Monthly, Quarterly, Yearly)
        private void RenderPeriodLevels(SessionLevelInfo session, Brush lineColor, Brush labelColor, bool showHigh, bool showLow, ChartControl chartControl, ChartScale chartScale)
        {
            if (session == null || session.StartBarIdx < 0) return;
            if (session.High == 0 || session.Low == 0) return;
            if (session.StartBarIdx > ChartBars.ToIndex) return; // Off screen to the right

            // v3.0.1: Determine period end (when this period session ends)
            int periodEndIdx = Bars.Count - 1; // Default: current bar if active session
            
            // For inactive sessions, find the end by looking at when the next period starts
            if (!session.IsActive && periodSessions != null)
            {
                int sessionIdx = periodSessions.IndexOf(session);
                if (sessionIdx >= 0 && sessionIdx < periodSessions.Count - 1)
                {
                    // Period ends right before next period starts
                    var nextSession = periodSessions[sessionIdx + 1];
                    periodEndIdx = nextSession.StartBarIdx - 1;
                    
                    // v3.0.1: If mitigation happened AT the border (first bar of next period),
                    // extend periodEndIdx to include that bar for the dash line
                    if (session.HighBrokenBarIdx >= 0 && session.HighBrokenBarIdx == nextSession.StartBarIdx)
                    {
                        periodEndIdx = session.HighBrokenBarIdx;
                    }
                    if (session.LowBrokenBarIdx >= 0 && session.LowBrokenBarIdx == nextSession.StartBarIdx)
                    {
                        periodEndIdx = Math.Max(periodEndIdx, session.LowBrokenBarIdx);
                    }
                }
                else if (sessionIdx == periodSessions.Count - 1)
                {
                    // This is the last (inactive) period - extend to current bar
                    periodEndIdx = Bars.Count - 1;
                }
            }

            // v3.0.1: The periodEndIdx from nextSession.StartBarIdx-1 is the correct end
            // If mitigation happened at the exact boundary or after, we still draw the dash
            // line UP TO the period end, not extending beyond it.

            // DEBUG: Log to file
            if (CurrentBar == Bars.Count - 1 && (session.HighBrokenBarIdx != -1 || session.LowBrokenBarIdx != -1))
            {
                try
                {
                    string logPath = System.IO.Path.Combine(
                        NinjaTrader.Core.Globals.UserDataDir,
                        "trace",
                        "RelativeVwap",
                        "dash_debug.txt");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                    
                    string logLine = string.Format("[{0}] {1}: periodEndIdx={2}, HiBrokenIdx={3}, LoBrokenIdx={4}, HighBarIdx={5}, FromIdx={6}, ToIdx={7}\n",
                        DateTime.Now.ToString("HH:mm:ss"),
                        session.Name,
                        periodEndIdx,
                        session.HighBrokenBarIdx,
                        session.LowBrokenBarIdx,
                        session.HighBarIdx,
                        ChartBars.FromIndex,
                        ChartBars.ToIndex);
                    System.IO.File.AppendAllText(logPath, logLine);
                }
                catch { }
            }

            // Render High Line
            if (showHigh && session.HighBarIdx >= 0)
            {
                int highStartIdx = Math.Max(ChartBars.FromIndex, session.HighBarIdx);
                
                // v3.0.1: Determine end index based on mitigation status
                bool isMitigated = (session.HighBrokenBarIdx != -1);
                int mitigationIdx = session.HighBrokenBarIdx;
                
                int highEndIdx;
                if (isMitigated)
                {
                    // Mitigated: extend at least to mitigation point (solid) and period end (dash if applicable)
                    highEndIdx = Math.Max(mitigationIdx, periodEndIdx);
                }
                else
                {
                    // Not mitigated: extend to last bar of all data
                    highEndIdx = Bars.Count - 1;
                }
                
                // Only draw if within visible range or extends beyond it
                if (highEndIdx >= highStartIdx && highStartIdx <= ChartBars.ToIndex)
                {
                    float startX = chartControl.GetXByBarIndex(ChartBars, highStartIdx);
                    float endX = chartControl.GetXByBarIndex(ChartBars, Math.Min(highEndIdx, ChartBars.ToIndex));
                    float y = chartScale.GetYByValue(session.High);

                    if (isMitigated && mitigationIdx >= ChartBars.FromIndex)
                    {
                        // Draw solid line from start to mitigation point
                        int visibleMitigationIdx = Math.Min(mitigationIdx, ChartBars.ToIndex);
                        float mitigationX = chartControl.GetXByBarIndex(ChartBars, visibleMitigationIdx);

                        SharpDX.Direct2D1.Brush solidBrush = lineColor.ToDxBrush(RenderTarget);
                        RenderTarget.DrawLine(
                            new SharpDX.Vector2(startX, y),
                            new SharpDX.Vector2(mitigationX, y),
                            solidBrush, SessionLevelThickness);
                        solidBrush?.Dispose();

                        // Draw gray dashed line from mitigation to period end (only if period end is beyond mitigation)
                        if (periodEndIdx > mitigationIdx && _cachedGrayBrush != null && _cachedDashStyle != null)
                        {
                            int dashEndIdx = Math.Min(periodEndIdx, ChartBars.ToIndex);
                            if (dashEndIdx > visibleMitigationIdx)
                            {
                                float dashEndX = chartControl.GetXByBarIndex(ChartBars, dashEndIdx);
                                RenderTarget.DrawLine(
                                    new SharpDX.Vector2(mitigationX, y),
                                    new SharpDX.Vector2(dashEndX, y),
                                    _cachedGrayBrush, SessionLevelThickness * 0.5f, _cachedDashStyle);
                            }
                        }
                    }
                    else
                    {
                        // Not mitigated - draw solid line extending to end
                        SharpDX.Vector2 startPoint = new SharpDX.Vector2(startX, y);
                        SharpDX.Vector2 endPoint = new SharpDX.Vector2(endX, y);

                        SharpDX.Direct2D1.Brush dxBrush = lineColor.ToDxBrush(RenderTarget);
                        RenderTarget.DrawLine(startPoint, endPoint, dxBrush, SessionLevelThickness);
                        dxBrush?.Dispose();
                    }

                    // Add label at the right end
                    string labelText = GetPeriodLabelText(session.Name, true);
                    if (labelQueue != null)
                    {
                        labelQueue.Add(new LabelData {
                            Text = labelText,
                            DrawX = endX + 5,
                            Y = y,
                            Width = 0,
                            Brush = labelColor,
                            Time = DateTime.Now
                        });
                    }
                }
            }

            // Render Low Line
            if (showLow && session.LowBarIdx >= 0)
            {
                int lowStartIdx = Math.Max(ChartBars.FromIndex, session.LowBarIdx);
                
                // v3.0.1: Determine end index based on mitigation status
                bool isMitigated = (session.LowBrokenBarIdx != -1);
                int mitigationIdx = session.LowBrokenBarIdx;
                
                int lowEndIdx;
                if (isMitigated)
                {
                    // Mitigated: extend at least to mitigation point
                    lowEndIdx = Math.Max(mitigationIdx, periodEndIdx);
                }
                else
                {
                    // Not mitigated: extend to last bar of all data
                    lowEndIdx = Bars.Count - 1;
                }
                
                // Only draw if within visible range or extends beyond it
                if (lowEndIdx >= lowStartIdx && lowStartIdx <= ChartBars.ToIndex)
                {
                    float startX = chartControl.GetXByBarIndex(ChartBars, lowStartIdx);
                    float endX = chartControl.GetXByBarIndex(ChartBars, Math.Min(lowEndIdx, ChartBars.ToIndex));
                    float y = chartScale.GetYByValue(session.Low);

                    if (isMitigated && mitigationIdx >= ChartBars.FromIndex)
                    {
                        // Draw solid line from start to mitigation point
                        int visibleMitigationIdx = Math.Min(mitigationIdx, ChartBars.ToIndex);
                        float mitigationX = chartControl.GetXByBarIndex(ChartBars, visibleMitigationIdx);

                        SharpDX.Direct2D1.Brush solidBrush = lineColor.ToDxBrush(RenderTarget);
                        RenderTarget.DrawLine(
                            new SharpDX.Vector2(startX, y),
                            new SharpDX.Vector2(mitigationX, y),
                            solidBrush, SessionLevelThickness);
                        solidBrush?.Dispose();

                        // Draw gray dashed line from mitigation to period end (only if period end is beyond mitigation)
                        if (periodEndIdx > mitigationIdx && _cachedGrayBrush != null && _cachedDashStyle != null)
                        {
                            int dashEndIdx = Math.Min(periodEndIdx, ChartBars.ToIndex);
                            if (dashEndIdx > visibleMitigationIdx)
                            {
                                float dashEndX = chartControl.GetXByBarIndex(ChartBars, dashEndIdx);
                                RenderTarget.DrawLine(
                                    new SharpDX.Vector2(mitigationX, y),
                                    new SharpDX.Vector2(dashEndX, y),
                                    _cachedGrayBrush, SessionLevelThickness * 0.5f, _cachedDashStyle);
                            }
                        }
                    }
                    else
                    {
                        // Not mitigated - draw solid line extending to current bar
                        SharpDX.Vector2 startPoint = new SharpDX.Vector2(startX, y);
                        SharpDX.Vector2 endPoint = new SharpDX.Vector2(endX, y);

                        SharpDX.Direct2D1.Brush dxBrush = lineColor.ToDxBrush(RenderTarget);
                        RenderTarget.DrawLine(startPoint, endPoint, dxBrush, SessionLevelThickness);
                        dxBrush?.Dispose();
                    }

                    // Add label at the right end
                    string labelText = GetPeriodLabelText(session.Name, false);
                    if (labelQueue != null)
                    {
                        labelQueue.Add(new LabelData {
                            Text = labelText,
                            DrawX = endX + 5,
                            Y = y,
                            Width = 0,
                            Brush = labelColor,
                            Time = DateTime.Now
                        });
                    }
                }
            }
        }

        private string GetPeriodLabelText(string sessionName, bool isHigh)
        {
            // Compact label format
            // Weekly: "Wk 02/09"
            // Monthly: "Feb '26"
            // Quarterly: "Q1 '26"
            // Yearly: "2026"

            string suffix = isHigh ? "H" : "L";

            if (sessionName.StartsWith("Week "))
            {
                string date = sessionName.Substring(5); // "2026-02-09"
                if (DateTime.TryParse(date, out DateTime weekDate))
                {
                    return string.Format("Wk {0:MM/dd} {1}", weekDate, suffix);
                }
            }
            else if (sessionName.StartsWith("Month "))
            {
                string date = sessionName.Substring(6); // "2026-02"
                if (DateTime.TryParse(date + "-01", out DateTime monthDate))
                {
                    return string.Format("{0:MMM '\'yy} {1}", monthDate, suffix);
                }
            }
            else if (sessionName.StartsWith("Q"))
            {
                // "Q1 2026" -> "Q1 '26"
                string[] parts = sessionName.Split(' ');
                if (parts.Length == 2)
                {
                    string year = parts[1].Length == 4 ? "'" + parts[1].Substring(2) : parts[1];
                    return string.Format("{0} {1} {2}", parts[0], year, suffix);
                }
            }
            else if (sessionName.StartsWith("Year "))
            {
                return sessionName.Substring(5) + " " + suffix; // "2026 H" or "2026 L"
            }

            return sessionName + " " + suffix;
        }

        // v3.0.0: Render Period Dividers (Vertical Lines + Triangle Markers)
        private void RenderPeriodDividers(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowPeriodDividers || periodDividerBars == null || periodDividerBars.Count == 0) return;
            if (RenderTarget == null || ChartBars == null) return;

            // v3.1.2 perf: Create divider brush ONCE per frame (not per-divider)
            SharpDX.Direct2D1.Brush dividerBrush = PeriodDividerColor.ToDxBrush(RenderTarget);

            foreach (int barIdx in periodDividerBars)
            {
                // Skip if bar is outside visible range
                if (barIdx < ChartBars.FromIndex || barIdx > ChartBars.ToIndex) continue;

                // Get X coordinate for this bar
                float x = chartControl.GetXByBarIndex(ChartBars, barIdx);

                // Get Y coordinates for top and bottom of chart
                float yTop = chartScale.GetYByValue(chartScale.MaxValue);
                float yBottom = chartScale.GetYByValue(chartScale.MinValue);

                // Draw vertical line
                SharpDX.Vector2 topPoint = new SharpDX.Vector2(x, yTop);
                SharpDX.Vector2 bottomPoint = new SharpDX.Vector2(x, yBottom);

                // v3.1.2 perf: Reuse cached dash style and shared divider brush
                RenderTarget.DrawLine(topPoint, bottomPoint, dividerBrush, 1.5f, _cachedDashStyle);

                // Draw triangle marker at bottom
                if (ShowPeriodMarker)
                {
                    float triangleSize = 8f;
                    float triangleY = yBottom - 15f; // 15 pixels above chart bottom

                    // Triangle vertices (pointing up)
                    SharpDX.Vector2 triangleTop = new SharpDX.Vector2(x, triangleY - triangleSize);
                    SharpDX.Vector2 triangleLeft = new SharpDX.Vector2(x - triangleSize / 2, triangleY);
                    SharpDX.Vector2 triangleRight = new SharpDX.Vector2(x + triangleSize / 2, triangleY);

                    // Create path geometry for filled triangle
                    SharpDX.Direct2D1.PathGeometry pathGeometry = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                    SharpDX.Direct2D1.GeometrySink sink = pathGeometry.Open();

                    sink.BeginFigure(triangleTop, SharpDX.Direct2D1.FigureBegin.Filled);
                    sink.AddLine(triangleLeft);
                    sink.AddLine(triangleRight);
                    sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                    sink.Close();

                    // Fill triangle
                    RenderTarget.FillGeometry(pathGeometry, dividerBrush);

                    // Draw triangle outline
                    RenderTarget.DrawGeometry(pathGeometry, dividerBrush, 1f);

                    // Cleanup
                    sink.Dispose();
                    pathGeometry.Dispose();
                }
            }
            // v3.1.2 perf: Dispose shared divider brush once (after loop)
            dividerBrush?.Dispose();
        }

        #endregion
    }
}
