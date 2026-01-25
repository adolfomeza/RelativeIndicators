
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
using System.Windows.Controls;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using NinjaTrader.Gui.Chart;

#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	public class RelativeDelta : Indicator
	{
		private double		buys 	= 1;
		private double 		sells 	= 1;
		private double		cdHigh 	= 1;
		private double 		cdLow 	= 1;
		private double		cdOpen 	= 1;
		private double 		cdClose	= 1;
		private int										barPaintWidth;
		private Dictionary<string, DXMediaMap>			dxmBrushes;
		private SharpDX.RectangleF						reuseRect;
		private SharpDX.Vector2							reuseVector1, reuseVector2;
		private double									tmpMax, tmpMin, tmpPlotVal;
		private int										x, y1, y2, y3, y4;
		private Series<Double> delta_open;
		private Series<Double> delta_close;
		private Series<Double> delta_high;
		private Series<Double> delta_low;		
		

		
		private bool	isReset;

		private int 	lastBar;
		private bool 	lastInTransition;
		
		private Brush	divergeCandleup   = Brushes.Purple;  // Color body for Divergence Candle
		private Brush	divergeCandledown   = Brushes.Pink;  // Color body for Divergence Candle
		
		private double usSessionAnchor = double.MinValue; // V_ZERO_LINE: Anchor Value
		private int    usSessionAnchorIdx = -1; // V_ZERO_LINE: Anchor Bar Index
		private bool   usSessionActive = false;
		private TimeSpan usStartTimeTs; // Cached TimeSpan
		
		// V_HIST: Historical Lines Logic
		private class HistoricalZeroLine
		{
		    public int StartIdx;
		    public int EndIdx;
		    public double Value;
		}
		private List<HistoricalZeroLine> historicalLines = new List<HistoricalZeroLine>();
		
// Redundant fields removed
		
		private NinjaTrader.NinjaScript.Indicators.Stochastics stoch;
// Duplicate stoch removed
		// Cache for DirectWrite
		private SharpDX.DirectWrite.Factory dwFactory;
		private SharpDX.DirectWrite.TextFormat dwTextFormat;
		
		// PERFORMANCE OPTIMIZATION: Resource Caching
		// Cache for Extra Lines Brushes
		private SharpDX.Direct2D1.Brush dxBrushLine2500, dxBrushLineN2500;
		private SharpDX.Direct2D1.Brush dxBrushLine5000, dxBrushLineN5000;
		private SharpDX.Direct2D1.Brush dxBrushLine10000, dxBrushLineN10000;
		private SharpDX.Direct2D1.Brush dxBrushUSZeroLine;
		private SharpDX.Direct2D1.StrokeStyle dxStrokeDash;
		
		// Helper to safely dispose
		private void SafeDispose(IDisposable obj)
		{
			if (obj != null) obj.Dispose();
		}
		
		
		
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Relative Delta";
				Name										= "Relative Delta";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= false;
	
				
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				
				dxmBrushes	= new Dictionary<string, DXMediaMap>();
				foreach (string brushName in new string[] { "barColorDown", "barColorUp", "shadowColor", "wickColor" })
					dxmBrushes.Add(brushName, new DXMediaMap());
				BarColorDown								= Brushes.Transparent;
				BarColorUp									= Brushes.White;
				ShadowColor									= Brushes.White;
				ShadowWidth									= 1;
				WickColor									= Brushes.White;
				WickWidth									= 1;
				int MinSize 								= 0;
				ShowDivs 									= false;
				
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaOpen");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaHigh");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaLow");
				AddPlot(new Stroke(Brushes.Orange),PlotStyle.PriceBox,"DeltaClose");
				
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
			    // Cache TimeSpan
			    TimeSpan.TryParse(USStartTime, out usStartTimeTs);
				delta_open = new Series<double>(this);
				delta_close = new Series<double>(this);
				delta_high = new Series<double>(this);
				delta_low = new Series<double>(this);
				
// Redundant init removed
				
				stoch = this.Stochastics(3, 14, 3);
				
				// Initialize D2D Factory once - NO LONGER NEEDED (Using RenderTarget.Factory)
			}
			else if (State == State.Terminated)
			{
			    DisposeD2DResources(); // Ensure cleanup
			}		
		}
		
		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < 5 || CurrentBars[1] < 5)
				return;
			
			// Performance optimization: Skip calculation for old bars
			// v2.0.1 Fix: In Playback, use the Last Bar Time as "Now" instead of DateTime.Now
			DateTime referenceDate = DateTime.Now;
			if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected && BarsArray[0].Count > 0)
			{
			    referenceDate = BarsArray[0].GetTime(BarsArray[0].Count - 1);
			}

			if (DaysToLoad > 0 && BarsArray[0].GetTime(CurrentBars[0]) < referenceDate.Date.AddDays(-DaysToLoad))
				return;

			if (BarsInProgress == 0)
			{
				
				int indexOffset = BarsArray[1].Count - 1 - CurrentBars[1];
				
				
				if (IsFirstTickOfBar && Calculate != Calculate.OnBarClose && (State == State.Realtime || BarsArray[0].IsTickReplay))
				{
					
					if (CurrentBars[0] > 0)
						SetValues(1);					
					
					if (BarsArray[0].IsTickReplay || State == State.Realtime && indexOffset == 0)
						ResetValues(false,cdClose);
				}
				
				
				SetValues(0);
				
			
				if (Calculate == Calculate.OnBarClose || (lastBar != CurrentBars[0] && (State == State.Historical || State == State.Realtime && indexOffset > 0)))
					ResetValues(false,cdClose);
				
				lastBar = CurrentBars[0];
					/* DISABLED COLOR OVERRIDE TO ALLOW CUSTOM PROPERTIES
					if (delta_close[0] > delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.LimeGreen;
					else if (delta_close[0] < delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.Red;
					else PlotBrushes[3][0] = (Brush) Brushes.Orange;
					*/
				
				
				if (IsFirstTickOfBar && ShowDivs)
				{
				if(delta_low[1] >= delta_low[2] && Low[1] <= Low[2] && Low[1] <= Low[3] && stoch.K[1] <= 20)	
				{
				
					Draw.TriangleUp(this,CurrentBar.ToString(), true, 1, Low[1] - 2*TickSize, divergeCandleup);
				}		
	
				if(delta_high[1] <= delta_high[2] && High[1] >= High[2] && High[1] >= High[3] && stoch.K[1] >= 80)
	
				{
				
					Draw.TriangleDown(this,CurrentBar.ToString(), true, 1, High[1] + 2*TickSize, divergeCandledown);
				}
				}
				
			}
			else if (BarsInProgress == 1)
			{
			
				if (BarsArray[1].IsFirstBarOfSession)
				{
// Debug removed
					ResetValues(true,cdClose);
					usSessionAnchor = double.MinValue; // Reset Anchor
					usSessionAnchorIdx = -1;
					usSessionActive = false;
				}
			
				CalculateValues(false);
				
				// V_ZERO_LINE: Capture Logic (Optimized Crossover)
				if (ShowUSZeroLine && !usSessionActive && usSessionAnchor == double.MinValue)
				{
					// Ensure we have at least 1 previous bar
					if (CurrentBar > 0)
					{
						TimeSpan currentTs = Time[0].TimeOfDay;
						TimeSpan previousTs = Time[1].TimeOfDay;
						
						// Handle Midnight Crossing Special Case
						// If previous was large (e.g. 23:59) and current is small (00:00), we don't want to trigger "Crossover" 
						// unless target is 00:00.
						// Standard day: 09:29 < 09:30 AND 09:30 >= 09:30 -> Trigger
						// Start at 18:00: 17:00 (prev) > 09:30 AND 18:00 (curr) > 09:30 -> No Trigger (Both True)
						
						bool isCrossover = (previousTs < usStartTimeTs && currentTs >= usStartTimeTs);
						
						if (isCrossover)
						{
							usSessionAnchor = cdClose;
							usSessionAnchorIdx = CurrentBars[0]; 
							usSessionActive = true;
							// Print(string.Format("DEBUG CAPTURE: Time={0} Anchor={1} Idx={2}", Time[0], usSessionAnchor, usSessionAnchorIdx));
						}
					}
				}
			}
		}
		
				
		private void CalculateValues(bool forceCurrentBar)
		{
			
			int 	indexOffset 	= BarsArray[1].Count - 1 - CurrentBars[1];
			bool 	inTransition 	= State == State.Realtime && indexOffset > 1;
			if (!inTransition && lastInTransition && !forceCurrentBar && Calculate == Calculate.OnBarClose)
				CalculateValues(true);
			
			bool 	useCurrentBar 	= State == State.Historical || inTransition || Calculate != Calculate.OnBarClose || forceCurrentBar;
			int 	whatBar 		= useCurrentBar ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);
		
			double 	volume 			= BarsArray[1].GetVolume(whatBar);
			double	price			= BarsArray[1].GetClose(whatBar);
			
			if (price >= BarsArray[1].GetAsk(whatBar) && volume>=MinSize)
				buys += volume;	
			else if (price <= BarsArray[1].GetBid(whatBar) && volume>=MinSize)
				sells += volume;
			
			cdClose = buys - sells;
	
			if (cdClose > cdHigh)
					cdHigh = cdClose;
	
			if (cdClose < cdLow)
					cdLow = cdClose;
	
			
			lastInTransition 	= inTransition;
		}
		
		private void SetValues(int barsAgo)
		{
		
		
			
			Values[0][barsAgo] = delta_open[barsAgo] = cdOpen;
			Values[1][barsAgo] = delta_high[barsAgo] = cdHigh;
			Values[2][barsAgo] = delta_low[barsAgo] = cdLow;
			Values[3][barsAgo] = delta_close[barsAgo] = cdClose;
			
	
		}
		
		private void ResetValues(bool isNewSession, double openlevel)
		{
		
		
			
			cdOpen = cdClose = cdHigh = cdLow = openlevel;
				
			if (isNewSession)
			{
				cdOpen = cdClose = cdHigh = cdLow = buys = sells = 0;
			}
			isReset = true;
		}
		
		public override string DisplayName
		{
		  get { return "Relative Delta"; }
		}
		
		#region Miscellaneous
	
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			barPaintWidth = Math.Max(1, (int)(ChartBars.Properties.ChartStyle.BarWidth * 2));
	

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx - Displacement < 0 || idx - Displacement >= BarsArray[0].Count || (idx - Displacement < BarsRequiredToPlot))
                    continue;

                x					= ChartControl.GetXByBarIndex(ChartBars, idx);
                y1					= chartScale.GetYByValue(delta_open.GetValueAt(idx));
                y2					= chartScale.GetYByValue(delta_high.GetValueAt(idx));
                y3					= chartScale.GetYByValue(delta_low.GetValueAt(idx));
                y4					= chartScale.GetYByValue(delta_close.GetValueAt(idx));

                // Calculate Top and Bottom of the body
                float bodyTop = Math.Min(y1, y4);
                float bodyBottom = Math.Max(y1, y4);

				if (y4 == y1) // Doji
				{
                    // Draw full wick
    				reuseVector1.X		= x;
    				reuseVector1.Y		= y2;
    				reuseVector2.X		= x;
    				reuseVector2.Y		= y3;
    				RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["wickColor"].DxBrush, WickWidth);

                    // Draw Doji Body Line
					reuseVector1.X	= (x - barPaintWidth / 2);
					reuseVector1.Y	= y1;
					reuseVector2.X	= (x + barPaintWidth / 2);
					reuseVector2.Y	= y1;

					RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["shadowColor"].DxBrush, ShadowWidth);
				}
				else
				{
                    // Draw Upper Wick (High to Body Top)
                    if (y2 < bodyTop)
                    {
                        reuseVector1.X = x;
                        reuseVector1.Y = y2;
                        reuseVector2.X = x;
                        reuseVector2.Y = bodyTop;
                        RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["wickColor"].DxBrush, WickWidth);
                    }

                    // Draw Lower Wick (Body Bottom to Low)
                    if (y3 > bodyBottom)
                    {
                        reuseVector1.X = x;
                        reuseVector1.Y = bodyBottom;
                        reuseVector2.X = x;
                        reuseVector2.Y = y3;
                        RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["wickColor"].DxBrush, WickWidth);
                    }

					if (y4 > y1) // Down Candle
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y1, barPaintWidth, (y4 - y1));
						RenderTarget.FillRectangle(reuseRect, dxmBrushes["barColorDown"].DxBrush);
					}
					else // Up Candle
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y4, barPaintWidth, (y1 - y4));
						RenderTarget.FillRectangle(reuseRect, dxmBrushes["barColorUp"].DxBrush);
					}

					UpdateRect(ref reuseRect, ((x - barPaintWidth / 2) + (ShadowWidth / 2)), Math.Min(y4, y1), (barPaintWidth - ShadowWidth + 2), Math.Abs(y4 - y1));
					RenderTarget.DrawRectangle(reuseRect, dxmBrushes["shadowColor"].DxBrush);
				}
            }

            // Dibuja la línea horizontal configurable
            if (HorizontalLineColor != null)
            {
                double yValue = chartScale.GetYByValue(HorizontalLineValue);
                byte alpha = (byte)(255 * HorizontalLineAlphaPercent / 100);
                var color = ((SolidColorBrush)HorizontalLineColor).Color;
                var colorWithAlpha = System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
                var brushWithAlpha = new SolidColorBrush(colorWithAlpha);
                var lineBrush = brushWithAlpha.ToDxBrush(RenderTarget);
                var start = new SharpDX.Vector2(0, (float)yValue);
                var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)yValue);
                RenderTarget.DrawLine(start, end, lineBrush, HorizontalLineWidth);
                lineBrush.Dispose();
                // Dibuja el valor en el margen derecho
                DrawLineLabel(HorizontalLineValue, yValue, chartControl, chartScale);
            }

            // Dibuja líneas extra individuales usando recursos cacheados
            DrawExtraLine(2500, ShowLine2500, dxBrushLine2500, Line2500Width, chartScale, chartControl);
            DrawExtraLine(-2500, ShowLineN2500, dxBrushLineN2500, LineN2500Width, chartScale, chartControl);
            DrawExtraLine(5000, ShowLine5000, dxBrushLine5000, Line5000Width, chartScale, chartControl);
            DrawExtraLine(-5000, ShowLineN5000, dxBrushLineN5000, LineN5000Width, chartScale, chartControl);
            DrawExtraLine(10000, ShowLine10000, dxBrushLine10000, Line10000Width, chartScale, chartControl);
            DrawExtraLine(-10000, ShowLineN10000, dxBrushLineN10000, LineN10000Width, chartScale, chartControl);
            
            // V_ZERO_LINE: Draw US Session Anchor Line (OPTIMIZED)
            if (ShowUSZeroLine && dxBrushUSZeroLine != null && dxStrokeDash != null)
            {
                 // 1. Draw Historical Lines
                 foreach (var line in historicalLines)
                 {
                     // Optimization: Check visibility
                     if (line.EndIdx < ChartBars.FromIndex || line.StartIdx > ChartBars.ToIndex) continue;
                     
                     // DaysToLoad Check (Visual approximation or strict?)
                     // Already handled by data loading, but if chart has more data:
                     // We can check time of the anchor. 
                     
                     double yVal = chartScale.GetYByValue(line.Value);
                     float x1 = chartControl.GetXByBarIndex(ChartBars, line.StartIdx);
                     float x2 = chartControl.GetXByBarIndex(ChartBars, line.EndIdx);
                     
                     var p1 = new SharpDX.Vector2(x1, (float)yVal);
                     var p2 = new SharpDX.Vector2(x2, (float)yVal);
                     RenderTarget.DrawLine(p1, p2, dxBrushUSZeroLine, USZeroLineWidth, dxStrokeDash);
                 }
            
                 // 2. Draw Current Active Line
                 if (usSessionAnchor != double.MinValue && usSessionAnchorIdx != -1)
                 {
                     double yValue = chartScale.GetYByValue(usSessionAnchor);
                     
                     // Calculate Start X based on captured index
                     float startX = chartControl.GetXByBarIndex(ChartBars, usSessionAnchorIdx);
                     
                     // Ensure we don't draw backwards if anchor is somehow ahead (unlikely)
                     
                     var start = new SharpDX.Vector2(startX, (float)yValue);
                     var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)yValue);
                     RenderTarget.DrawLine(start, end, dxBrushUSZeroLine, USZeroLineWidth, dxStrokeDash);
                 }
            }
		}
		
		public override void OnRenderTargetChanged()
		{		
			try
			{
				foreach (KeyValuePair<string, DXMediaMap> item in dxmBrushes)
				{
					if (item.Value.DxBrush != null)
						item.Value.DxBrush.Dispose();

					if (RenderTarget != null)
						item.Value.DxBrush = item.Value.MediaBrush.ToDxBrush(RenderTarget);					
				}
				
				CreateD2DResources(); // Re-create custom resources
			}
			catch (Exception exception)
			{
			}
		}
		
		private void DisposeD2DResources()
		{
		    SafeDispose(dxBrushLine2500); dxBrushLine2500 = null;
		    SafeDispose(dxBrushLineN2500); dxBrushLineN2500 = null;
		    SafeDispose(dxBrushLine5000); dxBrushLine5000 = null;
		    SafeDispose(dxBrushLineN5000); dxBrushLineN5000 = null;
		    SafeDispose(dxBrushLine10000); dxBrushLine10000 = null;
		    SafeDispose(dxBrushLineN10000); dxBrushLineN10000 = null;
		    SafeDispose(dxBrushUSZeroLine); dxBrushUSZeroLine = null;
		    SafeDispose(dxStrokeDash); dxStrokeDash = null;
		    SafeDispose(dwTextFormat); dwTextFormat = null;
		    SafeDispose(dwFactory); dwFactory = null;
		}
		
		private void CreateD2DResources()
		{
		    DisposeD2DResources(); // Clear old
		    
		    if (RenderTarget == null) return;
		    
		    // Helpers to create brush with alpha
		    Func<Brush, int, SharpDX.Direct2D1.Brush> createBrush = (brush, alpha) => 
		    {
		        if (brush == null) return null;
		        var color = ((SolidColorBrush)brush).Color;
		        var dxColor = new SharpDX.Color((int)color.R, (int)color.G, (int)color.B, (int)(255 * alpha / 100.0));
		        return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);
		    };
		    
		    dxBrushLine2500 = createBrush(Line2500Color, Line2500Alpha);
		    dxBrushLineN2500 = createBrush(LineN2500Color, LineN2500Alpha);
		    dxBrushLine5000 = createBrush(Line5000Color, Line5000Alpha);
		    dxBrushLineN5000 = createBrush(LineN5000Color, LineN5000Alpha);
		    dxBrushLine10000 = createBrush(Line10000Color, Line10000Alpha);
		    dxBrushLineN10000 = createBrush(LineN10000Color, LineN10000Alpha);
		    dxBrushUSZeroLine = createBrush(USZeroLineColor, USZeroLineAlpha);
		    
		    // Create Stroke Style (Dash) using RenderTarget's Factory (Crucial for performance/compat)
		    try 
		    {
		        var strokeStyleProps = new SharpDX.Direct2D1.StrokeStyleProperties();
                strokeStyleProps.DashStyle = (SharpDX.Direct2D1.DashStyle)USZeroLineDashStyle;
                dxStrokeDash = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, strokeStyleProps);
		    }
		    catch {}
		    
		    // Cache DirectWrite
		    try
		    {
		        dwFactory = new SharpDX.DirectWrite.Factory();
		        dwTextFormat = new SharpDX.DirectWrite.TextFormat(dwFactory, "Segoe UI", 12f);
		    }
		    catch {}
		}

		private void UpdateRect(ref SharpDX.RectangleF updateRectangle, float x, float y, float width, float height)
		{
			updateRectangle.X		= x;
			updateRectangle.Y		= y;
			updateRectangle.Width	= width;
			updateRectangle.Height	= height;
		}

		private void UpdateRect(ref SharpDX.RectangleF rectangle, int x, int y, int width, int height)
		{
			UpdateRect(ref rectangle, (float)x, (float)y, (float)width, (float)height);
		}
		#endregion
		
		#region Properties
		[Browsable(false)]
		public class DXMediaMap
		{
			public SharpDX.Direct2D1.Brush		DxBrush;
			public System.Windows.Media.Brush	MediaBrush;
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorDown", Order=4, GroupName= "Optics")]
		public Brush BarColorDown
		{
			get { return dxmBrushes["barColorDown"].MediaBrush; }
			set { dxmBrushes["barColorDown"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorDownSerializable
		{
			get { return Serialize.BrushToString(BarColorDown); }
			set { BarColorDown = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorUp", Order=5, GroupName= "Optics")]
		public Brush BarColorUp
		{
			get { return dxmBrushes["barColorUp"].MediaBrush; }
			set { dxmBrushes["barColorUp"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorUpSerializable
		{
			get { return Serialize.BrushToString(BarColorUp); }
			set { BarColorUp = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="ShadowColor", Order=6, GroupName="Optics")]
		public Brush ShadowColor
		{
			get { return dxmBrushes["shadowColor"].MediaBrush; }
			set { dxmBrushes["shadowColor"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string ShadowColorSerializable
		{
			get { return Serialize.BrushToString(ShadowColor); }
			set { ShadowColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ShadowWidth", Order=7, GroupName= "Optics")]
		public int ShadowWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="WickColor", Description="Color of the vertical wick (high-low line)", Order=8, GroupName="Optics")]
		public Brush WickColor
		{
			get { return dxmBrushes["wickColor"].MediaBrush; }
			set { dxmBrushes["wickColor"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string WickColorSerializable
		{
			get { return Serialize.BrushToString(WickColor); }
			set { WickColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="WickWidth", Description="Width of the vertical wick (high-low line)", Order=9, GroupName="Optics")]
		public int WickWidth
		{ get; set; } = 2;

		[Browsable(false)]
		[XmlIgnore]
		public int BarWidth
		{ get; set; } = 1;
		

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaOpen
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaHigh
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaLow
		{
			get { return Values[2]; }
		}
		
				
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaClose
		{
			get { return Values[3]; }
		}
	
		[Range(0, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name="Size Filter", Description="Size filtering", Order=1, GroupName="Parameters")]
		public int MinSize
		{ get; set; }

// Duplicate attributes removed
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Days To Load", Description="Number of days to load (0 = all)", Order=1, GroupName="Performance")]
		public int DaysToLoad
		{ get; set; } = 7;
		
		
		[NinjaScriptProperty]
		[Display(Name="Show Delta Divergences", Description="Enable to show cumulative delta divergences", Order=2, GroupName="Parameters")]
		public bool ShowDivs
		{ get; set; }
		
		[NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Línea Horizontal Color", Order=10, GroupName="Línea Horizontal")]
        public Brush HorizontalLineColor { get; set; } = Brushes.RoyalBlue;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Línea Horizontal Grosor", Order=11, GroupName="Línea Horizontal")]
        public int HorizontalLineWidth { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name="Línea Horizontal Valor", Order=12, GroupName="Línea Horizontal")]
        public double HorizontalLineValue { get; set; } = 0;
		
		[NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Línea Horizontal Transparencia (%)", Order=13, GroupName="Línea Horizontal")]
        public int HorizontalLineAlphaPercent { get; set; } = 100;
		
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasOpen
        {
            get { return delta_open; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasHigh
        {
            get { return delta_high; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasClose
        {
            get { return delta_close; }
        }	
		
		[NinjaScriptProperty]
        [Display(Name="Mostrar Líneas Extra Niveles", Order=14, GroupName="Línea Horizontal")]
        public bool ShowExtraLevels { get; set; } = true;
		
		// Propiedades para línea +2500
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +2500", Order=20, GroupName="Líneas Extra")]
        public bool ShowLine2500 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +2500", Order=21, GroupName="Líneas Extra")]
        public Brush Line2500Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line2500ColorSerializable
        {
            get { return Serialize.BrushToString(Line2500Color); }
            set { Line2500Color = Serialize.StringToBrush(value); }
        }

        [Range(1, 10)]
        [Display(Name="Grosor Línea +2500", Order=22, GroupName="Líneas Extra")]
        public int Line2500Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +2500 (%)", Order=23, GroupName="Líneas Extra")]
        public int Line2500Alpha { get; set; } = 100;

        // Propiedades para línea -2500
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -2500", Order=24, GroupName="Líneas Extra")]
        public bool ShowLineN2500 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -2500", Order=25, GroupName="Líneas Extra")]
        public Brush LineN2500Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN2500ColorSerializable
        {
            get { return Serialize.BrushToString(LineN2500Color); }
            set { LineN2500Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -2500", Order=26, GroupName="Líneas Extra")]
        public int LineN2500Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -2500 (%)", Order=27, GroupName="Líneas Extra")]
        public int LineN2500Alpha { get; set; } = 100;

        // Propiedades para línea +5000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +5000", Order=28, GroupName="Líneas Extra")]
        public bool ShowLine5000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +5000", Order=29, GroupName="Líneas Extra")]
        public Brush Line5000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line5000ColorSerializable
        {
            get { return Serialize.BrushToString(Line5000Color); }
            set { Line5000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea +5000", Order=30, GroupName="Líneas Extra")]
        public int Line5000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +5000 (%)", Order=31, GroupName="Líneas Extra")]
        public int Line5000Alpha { get; set; } = 100;

        // Propiedades para línea -5000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -5000", Order=32, GroupName="Líneas Extra")]
        public bool ShowLineN5000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -5000", Order=33, GroupName="Líneas Extra")]
        public Brush LineN5000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN5000ColorSerializable
        {
            get { return Serialize.BrushToString(LineN5000Color); }
            set { LineN5000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -5000", Order=34, GroupName="Líneas Extra")]
        public int LineN5000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -5000 (%)", Order=35, GroupName="Líneas Extra")]
        public int LineN5000Alpha { get; set; } = 100;

        // Propiedades para línea +10000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +10000", Order=36, GroupName="Líneas Extra")]
        public bool ShowLine10000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +10000", Order=37, GroupName="Líneas Extra")]
        public Brush Line10000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line10000ColorSerializable
        {
            get { return Serialize.BrushToString(Line10000Color); }
            set { Line10000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea +10000", Order=38, GroupName="Líneas Extra")]
        public int Line10000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +10000 (%)", Order=39, GroupName="Líneas Extra")]
        public int Line10000Alpha { get; set; } = 100;

        // Propiedades para línea -10000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -10000", Order=40, GroupName="Líneas Extra")]
        public bool ShowLineN10000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -10000", Order=41, GroupName="Líneas Extra")]
        public Brush LineN10000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN10000ColorSerializable
        {
            get { return Serialize.BrushToString(LineN10000Color); }
            set { LineN10000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -10000", Order=42, GroupName="Líneas Extra")]
        public int LineN10000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -10000 (%)", Order=43, GroupName="Líneas Extra")]
        public int LineN10000Alpha { get; set; } = 100;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Texto Líneas", Order=50, GroupName="Líneas Extra")]
        public Brush LineLabelColor { get; set; } = Brushes.White; // Default to White
        [Browsable(false)]
        public string LineLabelColorSerializable
        {
            get { return Serialize.BrushToString(LineLabelColor); }
            set { LineLabelColor = Serialize.StringToBrush(value); }
        }
		
		[NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Fondo Texto Líneas", Order=51, GroupName="Líneas Extra")]
        public Brush LineLabelBackground { get; set; } = Brushes.Transparent; // Default to Transparent
        [Browsable(false)]
        public string LineLabelBackgroundSerializable
        {
            get { return Serialize.BrushToString(LineLabelBackground); }
            set { LineLabelBackground = Serialize.StringToBrush(value); }
        }
        
        // V_ZERO_LINE: Properties
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea Cero USA", Order=60, GroupName="Línea Cero USA")]
        public bool ShowUSZeroLine { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Hora Inicio USA", Description="Hora formateada HH:mm (ej. 09:30)", Order=61, GroupName="Línea Cero USA")]
        public string USStartTime { get; set; } = "10:30";

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea Cero USA", Order=62, GroupName="Línea Cero USA")]
        public Brush USZeroLineColor { get; set; } = Brushes.Yellow;
        [Browsable(false)]
        public string USZeroLineColorSerializable
        {
            get { return Serialize.BrushToString(USZeroLineColor); }
            set { USZeroLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea Cero USA", Order=63, GroupName="Línea Cero USA")]
        public int USZeroLineWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea Cero USA (%)", Order=64, GroupName="Línea Cero USA")]
        public int USZeroLineAlpha { get; set; } = 100;

        [NinjaScriptProperty]
        [Display(Name="Estilo Dash Línea USA", Order=65, GroupName="Línea Cero USA")]
        public DashStyleHelper USZeroLineDashStyle { get; set; } = DashStyleHelper.Dash; 
		#endregion
		
		// Método auxiliar para dibujar líneas extra (Optimized)
        private void DrawExtraLine(double level, bool show, SharpDX.Direct2D1.Brush dxBrush, int width, ChartScale chartScale, ChartControl chartControl)
        {
            if (!show || dxBrush == null)
                return;
                
            // Use cached brush directly! No creation/dispose here.
            double y = chartScale.GetYByValue(level);
            var start = new SharpDX.Vector2(0, (float)y);
            var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)y);
            RenderTarget.DrawLine(start, end, dxBrush, width);
            
            // Dibuja el valor en el margen derecho
            DrawLineLabel(level, y, chartControl, chartScale);
        }

        // Método auxiliar para dibujar el valor de la línea en el margen derecho (Optimized)
        private void DrawLineLabel(double value, double y, ChartControl chartControl, ChartScale chartScale)
        {
            if (LineLabelColor == null || RenderTarget == null || dwFactory == null || dwTextFormat == null)
                return;
            
            var color = ((SolidColorBrush)LineLabelColor).Color;
            var dxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);
            var bgColor = ((SolidColorBrush)LineLabelBackground).Color;
            var dxBgColor = new SharpDX.Color(bgColor.R, bgColor.G, bgColor.B, bgColor.A);
            
            // Using logic is lightweight for brushes if they are small/solid, but ideally cached too.
            // For now, caching Factory and Format is the huge win.
            
            // For now, caching Factory and Format is the huge win.
            
            using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
            using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
            {
                string label = value.ToString();
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, label, dwTextFormat, 100, 20))
                {
                    float x = (float)chartControl.PanelWidth - layout.Metrics.Width - 4;
                    float yText = (float)y - layout.Metrics.Height / 2;
                    var rect = new SharpDX.RectangleF(x, yText, layout.Metrics.Width, layout.Metrics.Height);
                    // Dibuja el fondo antes del texto
                    RenderTarget.FillRectangle(rect, bgBrush);
                    RenderTarget.DrawText(label, dwTextFormat, rect, textBrush);
                }
            }
        }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeDelta[] cacheRelativeDelta;
		public RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			return RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle);
		}

		public RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input, Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			if (cacheRelativeDelta != null)
				for (int idx = 0; idx < cacheRelativeDelta.Length; idx++)
					if (cacheRelativeDelta[idx] != null && cacheRelativeDelta[idx].BarColorDown == barColorDown && cacheRelativeDelta[idx].BarColorUp == barColorUp && cacheRelativeDelta[idx].ShadowColor == shadowColor && cacheRelativeDelta[idx].ShadowWidth == shadowWidth && cacheRelativeDelta[idx].WickColor == wickColor && cacheRelativeDelta[idx].WickWidth == wickWidth && cacheRelativeDelta[idx].MinSize == minSize && cacheRelativeDelta[idx].DaysToLoad == daysToLoad && cacheRelativeDelta[idx].ShowDivs == showDivs && cacheRelativeDelta[idx].HorizontalLineColor == horizontalLineColor && cacheRelativeDelta[idx].HorizontalLineWidth == horizontalLineWidth && cacheRelativeDelta[idx].HorizontalLineValue == horizontalLineValue && cacheRelativeDelta[idx].HorizontalLineAlphaPercent == horizontalLineAlphaPercent && cacheRelativeDelta[idx].ShowExtraLevels == showExtraLevels && cacheRelativeDelta[idx].ShowLine2500 == showLine2500 && cacheRelativeDelta[idx].Line2500Color == line2500Color && cacheRelativeDelta[idx].Line2500Alpha == line2500Alpha && cacheRelativeDelta[idx].ShowLineN2500 == showLineN2500 && cacheRelativeDelta[idx].LineN2500Color == lineN2500Color && cacheRelativeDelta[idx].LineN2500Width == lineN2500Width && cacheRelativeDelta[idx].LineN2500Alpha == lineN2500Alpha && cacheRelativeDelta[idx].ShowLine5000 == showLine5000 && cacheRelativeDelta[idx].Line5000Color == line5000Color && cacheRelativeDelta[idx].Line5000Width == line5000Width && cacheRelativeDelta[idx].Line5000Alpha == line5000Alpha && cacheRelativeDelta[idx].ShowLineN5000 == showLineN5000 && cacheRelativeDelta[idx].LineN5000Color == lineN5000Color && cacheRelativeDelta[idx].LineN5000Width == lineN5000Width && cacheRelativeDelta[idx].LineN5000Alpha == lineN5000Alpha && cacheRelativeDelta[idx].ShowLine10000 == showLine10000 && cacheRelativeDelta[idx].Line10000Color == line10000Color && cacheRelativeDelta[idx].Line10000Width == line10000Width && cacheRelativeDelta[idx].Line10000Alpha == line10000Alpha && cacheRelativeDelta[idx].ShowLineN10000 == showLineN10000 && cacheRelativeDelta[idx].LineN10000Color == lineN10000Color && cacheRelativeDelta[idx].LineN10000Width == lineN10000Width && cacheRelativeDelta[idx].LineN10000Alpha == lineN10000Alpha && cacheRelativeDelta[idx].LineLabelColor == lineLabelColor && cacheRelativeDelta[idx].LineLabelBackground == lineLabelBackground && cacheRelativeDelta[idx].ShowUSZeroLine == showUSZeroLine && cacheRelativeDelta[idx].USStartTime == uSStartTime && cacheRelativeDelta[idx].USZeroLineColor == uSZeroLineColor && cacheRelativeDelta[idx].USZeroLineWidth == uSZeroLineWidth && cacheRelativeDelta[idx].USZeroLineAlpha == uSZeroLineAlpha && cacheRelativeDelta[idx].USZeroLineDashStyle == uSZeroLineDashStyle && cacheRelativeDelta[idx].EqualsInput(input))
						return cacheRelativeDelta[idx];
			return CacheIndicator<RelativeIndicators.RelativeDelta>(new RelativeIndicators.RelativeDelta(){ BarColorDown = barColorDown, BarColorUp = barColorUp, ShadowColor = shadowColor, ShadowWidth = shadowWidth, WickColor = wickColor, WickWidth = wickWidth, MinSize = minSize, DaysToLoad = daysToLoad, ShowDivs = showDivs, HorizontalLineColor = horizontalLineColor, HorizontalLineWidth = horizontalLineWidth, HorizontalLineValue = horizontalLineValue, HorizontalLineAlphaPercent = horizontalLineAlphaPercent, ShowExtraLevels = showExtraLevels, ShowLine2500 = showLine2500, Line2500Color = line2500Color, Line2500Alpha = line2500Alpha, ShowLineN2500 = showLineN2500, LineN2500Color = lineN2500Color, LineN2500Width = lineN2500Width, LineN2500Alpha = lineN2500Alpha, ShowLine5000 = showLine5000, Line5000Color = line5000Color, Line5000Width = line5000Width, Line5000Alpha = line5000Alpha, ShowLineN5000 = showLineN5000, LineN5000Color = lineN5000Color, LineN5000Width = lineN5000Width, LineN5000Alpha = lineN5000Alpha, ShowLine10000 = showLine10000, Line10000Color = line10000Color, Line10000Width = line10000Width, Line10000Alpha = line10000Alpha, ShowLineN10000 = showLineN10000, LineN10000Color = lineN10000Color, LineN10000Width = lineN10000Width, LineN10000Alpha = lineN10000Alpha, LineLabelColor = lineLabelColor, LineLabelBackground = lineLabelBackground, ShowUSZeroLine = showUSZeroLine, USStartTime = uSStartTime, USZeroLineColor = uSZeroLineColor, USZeroLineWidth = uSZeroLineWidth, USZeroLineAlpha = uSZeroLineAlpha, USZeroLineDashStyle = uSZeroLineDashStyle }, input, ref cacheRelativeDelta);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle);
		}
	}
}

#endregion
