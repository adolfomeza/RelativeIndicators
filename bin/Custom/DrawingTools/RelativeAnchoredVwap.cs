#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;

// Alias to resolve ambiguity
using MediaBrush = System.Windows.Media.Brush;
using MediaSolidBrush = System.Windows.Media.SolidColorBrush;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
    public class RelativeAnchoredVwap : DrawingTool
    {
        public enum ToolVwapPriceMethod
        {
            Close,
            Typical,
            OHLC4
        }
        
        // --- Properties ---
        [Display(Name = "Line Color", GroupName = "Visuals", Order = 1)]
        [XmlIgnore]
        public MediaBrush LineColor { get; set; }

        [Browsable(false)]
        public string LineColorSerializable
        {
            get { return Serialize.BrushToString(LineColor); }
            set { LineColor = Serialize.StringToBrush(value); }
        }

        [Range(1, 10)]
        [Display(Name = "Line Width", GroupName = "Visuals", Order = 2)]
        public float LineWidth { get; set; }

        [Display(Name = "Price Source", GroupName = "Calculation", Order = 4)]
        public ToolVwapPriceMethod PriceMethod { get; set; } = ToolVwapPriceMethod.Close;

        public override object Icon { get { return Gui.Tools.Icons.DrawPencil; } }

        // --- Anchor Management ---
        // --- Anchor Management ---
        [Display(Name="Start Anchor", GroupName="General", Order=10)]
        public ChartAnchor StartAnchor { get; set; }

        [Display(Name="End Anchor", GroupName="General", Order=11)]
        public ChartAnchor EndAnchor { get; set; }

        public override IEnumerable<ChartAnchor> Anchors 
        { 
            get { return new[] { StartAnchor, EndAnchor }; } 
        }

        // --- State Management ---
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                _clickCount = 0; // Reset counter
                Description = "Anchored VWAP starting from a specific bar and ending at another.";
                Name = "Relative Anchored VWAP";
                
                LineColor = Brushes.Silver;
                LineWidth = 2.0f;
                
                // Initialize StartAnchor
                StartAnchor = new ChartAnchor
                {
                    IsEditing = true,
                    DrawingTool = this,
                    DisplayName = "Start",
                    IsBrowsable = true
                };
                
                // Initialize EndAnchor
                // Standard Pattern: Enable editing for BOTH at start
                EndAnchor = new ChartAnchor
                {
                    IsEditing = true,
                    DrawingTool = this,
                    DisplayName = "End",
                    IsBrowsable = true
                };
                
                DrawingState = DrawingState.Building;
            }
            else if (State == State.Terminated)
            {
            }
        }

        // Internal counter to enforce 2-click placement
        private int _clickCount = 0;

        // --- Interaction ---
        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    
                    // Click 0: Place Start Anchor
                    if (_clickCount == 0)
                    {
                        dataPoint.CopyDataValues(StartAnchor);
                        
                        // Seed EndAnchor so it draws a visible line
                        dataPoint.CopyDataValues(EndAnchor);
                        
                        // Increment click count & wait for next click
                        _clickCount++;
                        
                        // Ensure EndAnchor is Editing for visual feedback in MouseMove
                        StartAnchor.IsEditing = false;
                        EndAnchor.IsEditing = true;
                        
                        return;
                    }
                    
                    // Click 1: Place End Anchor
                    if (_clickCount == 1)
                    {
                        dataPoint.CopyDataValues(EndAnchor);
                        
                        // Done
                        StartAnchor.IsEditing = false;
                        EndAnchor.IsEditing = false;
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                        _clickCount = 0; // Reset
                    }
                    break;
                    
                case DrawingState.Normal:
                    // Simple re-selection logic
                    Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                    ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, 10, point);
                    
                    if (closest != null)
                    {
                         closest.IsEditing = true;
                         DrawingState = DrawingState.Editing;
                    }
                    else
                    {
                        IsSelected = false;
                    }
                    break;
                 default:
                    base.OnMouseDown(chartControl, chartPanel, chartScale, dataPoint);
                    break;
            }
        }
        
        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building)
            {
                if (EndAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(EndAnchor);
                }
            }
            else if (DrawingState == DrawingState.Editing)
            {
                if (StartAnchor.IsEditing) StartAnchor.CopyDataValues(dataPoint);
                if (EndAnchor.IsEditing)   EndAnchor.CopyDataValues(dataPoint);
            }
        }
        
        // --- Rendering ---
        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Safety Checks
            if (StartAnchor == null) return;
            if (chartControl == null || chartControl.BarsArray.Count == 0) return;
            
            ChartBars chartBars = chartControl.BarsArray[0];
            if (chartBars == null) return;
            
            NinjaTrader.Data.Bars bars = chartBars.Bars;
            if (bars == null || bars.Count == 0) return;

            // Resolve Anchor Time
            DateTime anchorTime = StartAnchor.Time;
            
            // Find Start Index
            int startIdx = bars.GetBar(anchorTime);
            
            if (startIdx < 0) 
            {
               // If precise match fail, try to find approximate?
               // For now, fallback to 0 is causing "All History VWAP"
               // Try identifying if we can find a better index.
               // Search for index with time >= anchorTime
               startIdx = 0;
               for(int i=0; i<bars.Count; i++) {
                   if (bars.GetTime(i) >= anchorTime) {
                       startIdx = i;
                       break;
                   }
               }
            } 
            
            // Prepare DX Drawing
            SharpDX.Direct2D1.RenderTarget renderTarget = RenderTarget;
            if (renderTarget == null) return;

            // Prepare Brush
            // Ensure LineColor is not null
            if (LineColor == null) LineColor = Brushes.Silver;
            
            var mediaColor = ((MediaSolidBrush)LineColor).Color;
            var dxColor = new SharpDX.Color(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);

            using (var dxBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, dxColor))
            {
                double cumPV = 0;
                double cumVol = 0;

                
                SharpDX.Vector2? lastPoint = null;

                // Limit Loop
                int endIdx = bars.Count - 1;
                
                // If EndAnchor is valid, clamp calculation to it
                if (EndAnchor != null)
                {
                    int anchorEndIdx = bars.GetBar(EndAnchor.Time);
                    if (anchorEndIdx >= 0)
                    {
                        // Use the smaller of LastBar or EndAnchor
                        // But wait, GetBar returns index of time. 
                        // If EndAnchor is in future, GetBar might be strange.
                        // Assuming GetBar returns -1 if not found ? Or closet?
                        // If -1, we default to Count-1 (end of data)
                        endIdx = Math.Min(endIdx, anchorEndIdx);
                    }
                }

                // Optimize: Start Calculation
                for (int i = startIdx; i <= endIdx; i++)
                {
                    double vol = bars.GetVolume(i);
                    double price = bars.GetClose(i);

                    if (PriceMethod == ToolVwapPriceMethod.Typical)
                        price = (bars.GetHigh(i) + bars.GetLow(i) + bars.GetClose(i)) / 3.0;
                    else if (PriceMethod == ToolVwapPriceMethod.OHLC4)
                        price = (bars.GetOpen(i) + bars.GetHigh(i) + bars.GetLow(i) + bars.GetClose(i)) / 4.0;

                    cumPV += price * vol;
                    cumVol += vol;

                    // if (cumVol == 0) continue; // Allow render even if vol is 0? No, VWAP is undefined.
                    if (cumVol == 0) continue;
                    
                    double vwap = cumPV / cumVol;

                    // Draw if visible
                    if (i >= chartBars.FromIndex - 1 && i <= chartBars.ToIndex + 1)
                    {
                        // Coordinate Conversion
                        float x = chartControl.GetXByBarIndex(chartBars, i);
                        float y = (float)chartScale.GetYByValue(vwap);
                        
                        SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                        if (lastPoint.HasValue)
                        {
                             renderTarget.DrawLine(lastPoint.Value, currentPoint, dxBrush, LineWidth);
                        }

                        lastPoint = currentPoint;
                    }
                    else
                    {
                         // Maintain continuity for lines entering screen from left
                         float x = chartControl.GetXByBarIndex(chartBars, i);
                         float y = (float)chartScale.GetYByValue(vwap);
                         lastPoint = new SharpDX.Vector2(x, y);
                    }
                }
            }
        }
    }
}
