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

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private ASCENDO.NewsEvents[] cacheNewsEvents;

		
		public ASCENDO.NewsEvents NewsEvents()
		{
			return NewsEvents(Input);
		}


		
		public ASCENDO.NewsEvents NewsEvents(ISeries<double> input)
		{
			if (cacheNewsEvents != null)
				for (int idx = 0; idx < cacheNewsEvents.Length; idx++)
					if ( cacheNewsEvents[idx].EqualsInput(input))
						return cacheNewsEvents[idx];
			return CacheIndicator<ASCENDO.NewsEvents>(new ASCENDO.NewsEvents(), input, ref cacheNewsEvents);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.ASCENDO.NewsEvents NewsEvents()
		{
			return indicator.NewsEvents(Input);
		}


		
		public Indicators.ASCENDO.NewsEvents NewsEvents(ISeries<double> input )
		{
			return indicator.NewsEvents(input);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.ASCENDO.NewsEvents NewsEvents()
		{
			return indicator.NewsEvents(Input);
		}


		
		public Indicators.ASCENDO.NewsEvents NewsEvents(ISeries<double> input )
		{
			return indicator.NewsEvents(input);
		}

	}
}

#endregion
