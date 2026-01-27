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
		
		private TDU.TDUPriceAction[] cacheTDUPriceAction;

		
		public TDU.TDUPriceAction TDUPriceAction(TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			return TDUPriceAction(Input, legCountMethod, resetCountAtDTDB, aTMType, commissions, trapOffsetTicks, maxStopLossTicks, stoplossTicksOffset, scalpPositionType, scalpFixedContracts, scalpFixedAmount, scalpPercentCapital, scalpMaxContracts, runnerPositionType, runnerFixedContracts, runnerFixedAmount, runnerPercentCapital, runnerMaxContracts);
		}


		
		public TDU.TDUPriceAction TDUPriceAction(ISeries<double> input, TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			if (cacheTDUPriceAction != null)
				for (int idx = 0; idx < cacheTDUPriceAction.Length; idx++)
					if (cacheTDUPriceAction[idx].LegCountMethod == legCountMethod && cacheTDUPriceAction[idx].ResetCountAtDTDB == resetCountAtDTDB && cacheTDUPriceAction[idx].ATMType == aTMType && cacheTDUPriceAction[idx].Commissions == commissions && cacheTDUPriceAction[idx].TrapOffsetTicks == trapOffsetTicks && cacheTDUPriceAction[idx].MaxStopLossTicks == maxStopLossTicks && cacheTDUPriceAction[idx].StoplossTicksOffset == stoplossTicksOffset && cacheTDUPriceAction[idx].ScalpPositionType == scalpPositionType && cacheTDUPriceAction[idx].ScalpFixedContracts == scalpFixedContracts && cacheTDUPriceAction[idx].ScalpFixedAmount == scalpFixedAmount && cacheTDUPriceAction[idx].ScalpPercentCapital == scalpPercentCapital && cacheTDUPriceAction[idx].ScalpMaxContracts == scalpMaxContracts && cacheTDUPriceAction[idx].RunnerPositionType == runnerPositionType && cacheTDUPriceAction[idx].RunnerFixedContracts == runnerFixedContracts && cacheTDUPriceAction[idx].RunnerFixedAmount == runnerFixedAmount && cacheTDUPriceAction[idx].RunnerPercentCapital == runnerPercentCapital && cacheTDUPriceAction[idx].RunnerMaxContracts == runnerMaxContracts && cacheTDUPriceAction[idx].EqualsInput(input))
						return cacheTDUPriceAction[idx];
			return CacheIndicator<TDU.TDUPriceAction>(new TDU.TDUPriceAction(){ LegCountMethod = legCountMethod, ResetCountAtDTDB = resetCountAtDTDB, ATMType = aTMType, Commissions = commissions, TrapOffsetTicks = trapOffsetTicks, MaxStopLossTicks = maxStopLossTicks, StoplossTicksOffset = stoplossTicksOffset, ScalpPositionType = scalpPositionType, ScalpFixedContracts = scalpFixedContracts, ScalpFixedAmount = scalpFixedAmount, ScalpPercentCapital = scalpPercentCapital, ScalpMaxContracts = scalpMaxContracts, RunnerPositionType = runnerPositionType, RunnerFixedContracts = runnerFixedContracts, RunnerFixedAmount = runnerFixedAmount, RunnerPercentCapital = runnerPercentCapital, RunnerMaxContracts = runnerMaxContracts }, input, ref cacheTDUPriceAction);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.TDU.TDUPriceAction TDUPriceAction(TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			return indicator.TDUPriceAction(Input, legCountMethod, resetCountAtDTDB, aTMType, commissions, trapOffsetTicks, maxStopLossTicks, stoplossTicksOffset, scalpPositionType, scalpFixedContracts, scalpFixedAmount, scalpPercentCapital, scalpMaxContracts, runnerPositionType, runnerFixedContracts, runnerFixedAmount, runnerPercentCapital, runnerMaxContracts);
		}


		
		public Indicators.TDU.TDUPriceAction TDUPriceAction(ISeries<double> input , TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			return indicator.TDUPriceAction(input, legCountMethod, resetCountAtDTDB, aTMType, commissions, trapOffsetTicks, maxStopLossTicks, stoplossTicksOffset, scalpPositionType, scalpFixedContracts, scalpFixedAmount, scalpPercentCapital, scalpMaxContracts, runnerPositionType, runnerFixedContracts, runnerFixedAmount, runnerPercentCapital, runnerMaxContracts);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.TDU.TDUPriceAction TDUPriceAction(TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			return indicator.TDUPriceAction(Input, legCountMethod, resetCountAtDTDB, aTMType, commissions, trapOffsetTicks, maxStopLossTicks, stoplossTicksOffset, scalpPositionType, scalpFixedContracts, scalpFixedAmount, scalpPercentCapital, scalpMaxContracts, runnerPositionType, runnerFixedContracts, runnerFixedAmount, runnerPercentCapital, runnerMaxContracts);
		}


		
		public Indicators.TDU.TDUPriceAction TDUPriceAction(ISeries<double> input , TDUPatsRules legCountMethod, bool resetCountAtDTDB, TDUPatsTradeManagement aTMType, double commissions, int trapOffsetTicks, int maxStopLossTicks, int stoplossTicksOffset, TDUPATSPositionSizing scalpPositionType, int scalpFixedContracts, int scalpFixedAmount, double scalpPercentCapital, int scalpMaxContracts, TDUPATSPositionSizingRunner runnerPositionType, int runnerFixedContracts, int runnerFixedAmount, double runnerPercentCapital, int runnerMaxContracts)
		{
			return indicator.TDUPriceAction(input, legCountMethod, resetCountAtDTDB, aTMType, commissions, trapOffsetTicks, maxStopLossTicks, stoplossTicksOffset, scalpPositionType, scalpFixedContracts, scalpFixedAmount, scalpPercentCapital, scalpMaxContracts, runnerPositionType, runnerFixedContracts, runnerFixedAmount, runnerPercentCapital, runnerMaxContracts);
		}

	}
}

#endregion
