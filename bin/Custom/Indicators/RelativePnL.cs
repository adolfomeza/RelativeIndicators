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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class RelativePnL : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Helper indicator to display Strategy PnL on a separate panel.";
				Name										= "RelativePnL";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false; // Crucial: Put on separate panel
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Line, "RelativePnL");
				AddLine(Brushes.DarkGray, 0, "ZeroLine");
				
				// V_FIX: FORCE Panel 2 to prevent Price Compression

			}
			else if (State == State.Configure)
			{
			}
		}

		// Property to receive data from Strategy
		public double CurrentPnL { get; set; }

		protected override void OnBarUpdate()
		{
			// Render the PnL value received from Strategy
			Values[0][0] = CurrentPnL;
			
			// Color Logic
			PlotBrushes[0][0] = (CurrentPnL >= 0) ? Brushes.LimeGreen : Brushes.Red;
		}
		
		// Legacy method removed in favor of Property model
		public void UpdateValue(double pnl)
		{
			CurrentPnL = pnl; // Bridge for backward compat if needed
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativePnL[] cacheRelativePnL;
		public RelativePnL RelativePnL()
		{
			return RelativePnL(Input);
		}

		public RelativePnL RelativePnL(ISeries<double> input)
		{
			if (cacheRelativePnL != null)
				for (int idx = 0; idx < cacheRelativePnL.Length; idx++)
					if (cacheRelativePnL[idx] != null &&  cacheRelativePnL[idx].EqualsInput(input))
						return cacheRelativePnL[idx];
			return CacheIndicator<RelativePnL>(new RelativePnL(), input, ref cacheRelativePnL);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativePnL RelativePnL()
		{
			return indicator.RelativePnL(Input);
		}

		public Indicators.RelativePnL RelativePnL(ISeries<double> input )
		{
			return indicator.RelativePnL(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativePnL RelativePnL()
		{
			return indicator.RelativePnL(Input);
		}

		public Indicators.RelativePnL RelativePnL(ISeries<double> input )
		{
			return indicator.RelativePnL(input);
		}
	}
}

#endregion
