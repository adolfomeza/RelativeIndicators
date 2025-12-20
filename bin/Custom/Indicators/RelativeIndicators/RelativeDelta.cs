
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
		
		private NinjaTrader.NinjaScript.Indicators.Stochastics stoch;
		
		
		
		
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
				foreach (string brushName in new string[] { "barColorDown", "barColorUp", "shadowColor" })
					dxmBrushes.Add(brushName, new DXMediaMap());
				BarColorDown								= Brushes.RoyalBlue;
				BarColorUp									= Brushes.White;
				ShadowColor									= Brushes.Silver;
				ShadowWidth									= 1;
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
				delta_open = new Series<double>(this);
				delta_close = new Series<double>(this);
				delta_high = new Series<double>(this);
				delta_low = new Series<double>(this);
				
				stoch = this.Stochastics(3, 14, 3);
			}		
		}
		
		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < 5 || CurrentBars[1] < 5)
				return;
			
			// Performance optimization: Skip calculation for old bars
			if (DaysToLoad > 0 && BarsArray[0].GetTime(CurrentBars[0]) < DateTime.Now.Date.AddDays(-DaysToLoad))
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
					if (delta_close[0] > delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.LimeGreen;
					else if (delta_close[0] < delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.Red;
					else PlotBrushes[3][0] = (Brush) Brushes.Orange;
				
				
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
					ResetValues(true,cdClose);
			
				CalculateValues(false);
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

			barPaintWidth = Math.Max(1, BarWidth);
	

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx - Displacement < 0 || idx - Displacement >= BarsArray[0].Count || (idx - Displacement < BarsRequiredToPlot))
                    continue;

                x					= ChartControl.GetXByBarIndex(ChartBars, idx);
                y1					= chartScale.GetYByValue(delta_open.GetValueAt(idx));
                y2					= chartScale.GetYByValue(delta_high.GetValueAt(idx));
                y3					= chartScale.GetYByValue(delta_low.GetValueAt(idx));
                y4					= chartScale.GetYByValue(delta_close.GetValueAt(idx));

				reuseVector1.X		= x;
				reuseVector1.Y		= y2;
				reuseVector2.X		= x;
				reuseVector2.Y		= y3;

				RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["shadowColor"].DxBrush);

				if (y4 == y1)
				{
					reuseVector1.X	= (x - barPaintWidth / 2);
					reuseVector1.Y	= y1;
					reuseVector2.X	= (x + barPaintWidth / 2);
					reuseVector2.Y	= y1;

					RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["shadowColor"].DxBrush);
				}
				else
				{
					if (y4 > y1)
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y1, barPaintWidth, (y4 - y1));
						RenderTarget.FillRectangle(reuseRect, dxmBrushes["barColorDown"].DxBrush);
					}
					else
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

            // Dibuja líneas extra individuales
            DrawExtraLine(2500, ShowLine2500, Line2500Color, Line2500Width, Line2500Alpha, chartScale, chartControl);
            DrawExtraLine(-2500, ShowLineN2500, LineN2500Color, LineN2500Width, LineN2500Alpha, chartScale, chartControl);
            DrawExtraLine(5000, ShowLine5000, Line5000Color, Line5000Width, Line5000Alpha, chartScale, chartControl);
            DrawExtraLine(-5000, ShowLineN5000, LineN5000Color, LineN5000Width, LineN5000Alpha, chartScale, chartControl);
            DrawExtraLine(10000, ShowLine10000, Line10000Color, Line10000Width, Line10000Alpha, chartScale, chartControl);
            DrawExtraLine(-10000, ShowLineN10000, LineN10000Color, LineN10000Width, LineN10000Alpha, chartScale, chartControl);
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
			}
			catch (Exception exception)
			{
			}
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
		[Range(1, int.MaxValue)]
		[Display(Name="BarWidth", Order=8, GroupName= "Optics")]
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

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Days To Load", Description="Number of days to load (0 = all)", Order=1, GroupName="Performance")]
		public int DaysToLoad
		{ get; set; } = 3;
		
		
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
        public int HorizontalLineWidth { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name="Línea Horizontal Valor", Order=12, GroupName="Línea Horizontal")]
        public double HorizontalLineValue { get; set; } = 0;
		
		[NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Línea Horizontal Transparencia (%)", Order=13, GroupName="Línea Horizontal")]
        public int HorizontalLineAlphaPercent { get; set; } = 50;
		
		
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
        public Brush LineLabelColor { get; set; } = Brushes.Gray;
		
		[NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Fondo Texto Líneas", Order=51, GroupName="Líneas Extra")]
        public Brush LineLabelBackground { get; set; } = Brushes.Black;
		#endregion
		
		// Método auxiliar para dibujar líneas extra
        private void DrawExtraLine(double level, bool show, Brush color, int width, int alphaPercent, ChartScale chartScale, ChartControl chartControl)
        {
            if (!show || color == null)
                return;
            byte alpha = (byte)(255 * alphaPercent / 100);
            var solidColor = ((SolidColorBrush)color).Color;
            var colorWithAlpha = System.Windows.Media.Color.FromArgb(alpha, solidColor.R, solidColor.G, solidColor.B);
            var brushWithAlpha = new SolidColorBrush(colorWithAlpha);
            var lineBrush = brushWithAlpha.ToDxBrush(RenderTarget);
            double y = chartScale.GetYByValue(level);
            var start = new SharpDX.Vector2(0, (float)y);
            var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)y);
            RenderTarget.DrawLine(start, end, lineBrush, width);
            lineBrush.Dispose();
            // Dibuja el valor en el margen derecho
            DrawLineLabel(level, y, chartControl, chartScale);
        }

        // Método auxiliar para dibujar el valor de la línea en el margen derecho
        private void DrawLineLabel(double value, double y, ChartControl chartControl, ChartScale chartScale)
        {
            if (LineLabelColor == null || RenderTarget == null)
                return;
            var color = ((SolidColorBrush)LineLabelColor).Color;
            var dxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);
            var bgColor = ((SolidColorBrush)LineLabelBackground).Color;
            var dxBgColor = new SharpDX.Color(bgColor.R, bgColor.G, bgColor.B, bgColor.A);
            using (var dwFactory = new SharpDX.DirectWrite.Factory())
            using (var textFormat = new SharpDX.DirectWrite.TextFormat(dwFactory, "Segoe UI", 12f))
            using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
            using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
            {
                string label = value.ToString();
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, label, textFormat, 100, 20))
                {
                    float x = (float)chartControl.PanelWidth - layout.Metrics.Width - 4;
                    float yText = (float)y - layout.Metrics.Height / 2;
                    var rect = new SharpDX.RectangleF(x, yText, layout.Metrics.Width, layout.Metrics.Height);
                    // Dibuja el fondo antes del texto
                    RenderTarget.FillRectangle(rect, bgBrush);
                    RenderTarget.DrawText(label, textFormat, rect, textBrush);
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
		public RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			return RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, barWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground);
		}

		public RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input, Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			if (cacheRelativeDelta != null)
				for (int idx = 0; idx < cacheRelativeDelta.Length; idx++)
					if (cacheRelativeDelta[idx] != null && cacheRelativeDelta[idx].BarColorDown == barColorDown && cacheRelativeDelta[idx].BarColorUp == barColorUp && cacheRelativeDelta[idx].ShadowColor == shadowColor && cacheRelativeDelta[idx].ShadowWidth == shadowWidth && cacheRelativeDelta[idx].BarWidth == barWidth && cacheRelativeDelta[idx].MinSize == minSize && cacheRelativeDelta[idx].DaysToLoad == daysToLoad && cacheRelativeDelta[idx].ShowDivs == showDivs && cacheRelativeDelta[idx].HorizontalLineColor == horizontalLineColor && cacheRelativeDelta[idx].HorizontalLineWidth == horizontalLineWidth && cacheRelativeDelta[idx].HorizontalLineValue == horizontalLineValue && cacheRelativeDelta[idx].HorizontalLineAlphaPercent == horizontalLineAlphaPercent && cacheRelativeDelta[idx].ShowExtraLevels == showExtraLevels && cacheRelativeDelta[idx].ShowLine2500 == showLine2500 && cacheRelativeDelta[idx].Line2500Color == line2500Color && cacheRelativeDelta[idx].Line2500Alpha == line2500Alpha && cacheRelativeDelta[idx].ShowLineN2500 == showLineN2500 && cacheRelativeDelta[idx].LineN2500Color == lineN2500Color && cacheRelativeDelta[idx].LineN2500Width == lineN2500Width && cacheRelativeDelta[idx].LineN2500Alpha == lineN2500Alpha && cacheRelativeDelta[idx].ShowLine5000 == showLine5000 && cacheRelativeDelta[idx].Line5000Color == line5000Color && cacheRelativeDelta[idx].Line5000Width == line5000Width && cacheRelativeDelta[idx].Line5000Alpha == line5000Alpha && cacheRelativeDelta[idx].ShowLineN5000 == showLineN5000 && cacheRelativeDelta[idx].LineN5000Color == lineN5000Color && cacheRelativeDelta[idx].LineN5000Width == lineN5000Width && cacheRelativeDelta[idx].LineN5000Alpha == lineN5000Alpha && cacheRelativeDelta[idx].ShowLine10000 == showLine10000 && cacheRelativeDelta[idx].Line10000Color == line10000Color && cacheRelativeDelta[idx].Line10000Width == line10000Width && cacheRelativeDelta[idx].Line10000Alpha == line10000Alpha && cacheRelativeDelta[idx].ShowLineN10000 == showLineN10000 && cacheRelativeDelta[idx].LineN10000Color == lineN10000Color && cacheRelativeDelta[idx].LineN10000Width == lineN10000Width && cacheRelativeDelta[idx].LineN10000Alpha == lineN10000Alpha && cacheRelativeDelta[idx].LineLabelColor == lineLabelColor && cacheRelativeDelta[idx].LineLabelBackground == lineLabelBackground && cacheRelativeDelta[idx].EqualsInput(input))
						return cacheRelativeDelta[idx];
			return CacheIndicator<RelativeIndicators.RelativeDelta>(new RelativeIndicators.RelativeDelta(){ BarColorDown = barColorDown, BarColorUp = barColorUp, ShadowColor = shadowColor, ShadowWidth = shadowWidth, BarWidth = barWidth, MinSize = minSize, DaysToLoad = daysToLoad, ShowDivs = showDivs, HorizontalLineColor = horizontalLineColor, HorizontalLineWidth = horizontalLineWidth, HorizontalLineValue = horizontalLineValue, HorizontalLineAlphaPercent = horizontalLineAlphaPercent, ShowExtraLevels = showExtraLevels, ShowLine2500 = showLine2500, Line2500Color = line2500Color, Line2500Alpha = line2500Alpha, ShowLineN2500 = showLineN2500, LineN2500Color = lineN2500Color, LineN2500Width = lineN2500Width, LineN2500Alpha = lineN2500Alpha, ShowLine5000 = showLine5000, Line5000Color = line5000Color, Line5000Width = line5000Width, Line5000Alpha = line5000Alpha, ShowLineN5000 = showLineN5000, LineN5000Color = lineN5000Color, LineN5000Width = lineN5000Width, LineN5000Alpha = lineN5000Alpha, ShowLine10000 = showLine10000, Line10000Color = line10000Color, Line10000Width = line10000Width, Line10000Alpha = line10000Alpha, ShowLineN10000 = showLineN10000, LineN10000Color = lineN10000Color, LineN10000Width = lineN10000Width, LineN10000Alpha = lineN10000Alpha, LineLabelColor = lineLabelColor, LineLabelBackground = lineLabelBackground }, input, ref cacheRelativeDelta);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, barWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, barWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, barWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, int barWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, barWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground);
		}
	}
}

#endregion
