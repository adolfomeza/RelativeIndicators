// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.MarketAnalyzerColumns.MarketAnalyzerColumn
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns;

public class MarketAnalyzerColumn : MarketAnalyzerColumnBase
{
  private Indicator indicator;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public MarketAnalyzerColumn()
  {
  }

  [Browsable(false)]
  public bool IsDataSeriesRequired
  {
    get => ((NinjaScriptBase) this).IsDataSeriesRequired;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WoodiesCCI WoodiesCCI(
    int chopIndicatorWidth,
    int neutralBars,
    int period,
    int periodEma,
    int periodLinReg,
    int periodTurbo,
    int sideWinderLimit0,
    int sideWinderLimit1,
    int sideWinderWidth)
  {
    return (NinjaTrader.NinjaScript.Indicators.WoodiesCCI) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WoodiesCCI WoodiesCCI(
    ISeries<double> input,
    int chopIndicatorWidth,
    int neutralBars,
    int period,
    int periodEma,
    int periodLinReg,
    int periodTurbo,
    int sideWinderLimit0,
    int sideWinderLimit1,
    int sideWinderWidth)
  {
    return (NinjaTrader.NinjaScript.Indicators.WoodiesCCI) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WoodiesPivots WoodiesPivots(
    HLCCalculationModeWoodie priorDayHlc,
    int width)
  {
    return (NinjaTrader.NinjaScript.Indicators.WoodiesPivots) null;
  }

  public NinjaTrader.NinjaScript.Indicators.WoodiesPivots WoodiesPivots(
    ISeries<double> input,
    HLCCalculationModeWoodie priorDayHlc,
    int width)
  {
    return this.indicator.WoodiesPivots(input, priorDayHlc, width);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WisemanAlligator WisemanAlligator(
    int jawPeriod,
    int teethPeriod,
    int lipsPeriod,
    int jawOffset,
    int teethOffset,
    int lipsOffset)
  {
    return (NinjaTrader.NinjaScript.Indicators.WisemanAlligator) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WisemanAlligator WisemanAlligator(
    ISeries<double> input,
    int jawPeriod,
    int teethPeriod,
    int lipsPeriod,
    int jawOffset,
    int teethOffset,
    int lipsOffset)
  {
    return (NinjaTrader.NinjaScript.Indicators.WisemanAlligator) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator()
  {
    return (NinjaTrader.NinjaScript.Indicators.WisemanAwesomeOscillator) null;
  }

  public NinjaTrader.NinjaScript.Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator(
    ISeries<double> input)
  {
    return this.indicator.WisemanAwesomeOscillator(input);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.WisemanFractal WisemanFractal(
    int strength,
    int triangleOffset)
  {
    return (NinjaTrader.NinjaScript.Indicators.WisemanFractal) null;
  }

  public NinjaTrader.NinjaScript.Indicators.WisemanFractal WisemanFractal(
    ISeries<double> input,
    int strength,
    int triangleOffset)
  {
    return this.indicator.WisemanFractal(input, strength, triangleOffset);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(
    CumulativeDeltaType deltaType,
    CumulativeDeltaPeriod period,
    int sizeFilter)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(
    ISeries<double> input,
    CumulativeDeltaType deltaType,
    CumulativeDeltaPeriod period,
    int sizeFilter)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(
    BaseVolumeRange baseRange,
    int maxRange,
    int minRange,
    OpacityDistribution opacityDistribution,
    int depthMargin,
    bool extendLastKnown,
    bool showBidAskLine)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowMarketDepthMap) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(
    ISeries<double> input,
    BaseVolumeRange baseRange,
    int maxRange,
    int minRange,
    OpacityDistribution opacityDistribution,
    int depthMargin,
    bool extendLastKnown,
    bool showBidAskLine)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowMarketDepthMap) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowVWAP OrderFlowVWAP(
    VWAPResolution resolution,
    TradingHours tradingHoursInstance,
    VWAPStandardDeviations numStandardDeviations,
    double sD1Multiplier,
    double sD2Multiplier,
    double sD3Multiplier)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowVWAP) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowVWAP OrderFlowVWAP(
    ISeries<double> input,
    VWAPResolution resolution,
    TradingHours tradingHoursInstance,
    VWAPStandardDeviations numStandardDeviations,
    double sD1Multiplier,
    double sD2Multiplier,
    double sD3Multiplier)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowVWAP) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(
    TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn,
    int minimumVolumeForMarker,
    int maximumMarkerSize,
    TradeDetectorSizeBase baseMarkerSizeOn,
    bool hoverValues)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowTradeDetector) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(
    ISeries<double> input,
    TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn,
    int minimumVolumeForMarker,
    int maximumMarkerSize,
    TradeDetectorSizeBase baseMarkerSizeOn,
    bool hoverValues)
  {
    return (NinjaTrader.NinjaScript.Indicators.OrderFlowTradeDetector) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction TDUPriceAction(
    TDUPatsRules legCountMethod,
    bool resetCountAtDTDB,
    TDUPatsTradeManagement aTMType,
    double commissions,
    int trapOffsetTicks,
    int maxStopLossTicks,
    int stoplossTicksOffset,
    TDUPATSPositionSizing scalpPositionType,
    int scalpFixedContracts,
    int scalpFixedAmount,
    double scalpPercentCapital,
    int scalpMaxContracts,
    TDUPATSPositionSizingRunner runnerPositionType,
    int runnerFixedContracts,
    int runnerFixedAmount,
    double runnerPercentCapital,
    int runnerMaxContracts,
    string email,
    long contactId)
  {
    return (NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction TDUPriceAction(
    ISeries<double> input,
    TDUPatsRules legCountMethod,
    bool resetCountAtDTDB,
    TDUPatsTradeManagement aTMType,
    double commissions,
    int trapOffsetTicks,
    int maxStopLossTicks,
    int stoplossTicksOffset,
    TDUPATSPositionSizing scalpPositionType,
    int scalpFixedContracts,
    int scalpFixedAmount,
    double scalpPercentCapital,
    int scalpMaxContracts,
    TDUPATSPositionSizingRunner runnerPositionType,
    int runnerFixedContracts,
    int runnerFixedAmount,
    double runnerPercentCapital,
    int runnerMaxContracts,
    string email,
    long contactId)
  {
    return (NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.Swing Swing(int strength) => (NinjaTrader.NinjaScript.Indicators.Swing) null;

  public NinjaTrader.NinjaScript.Indicators.Swing Swing(ISeries<double> input, int strength)
  {
    return this.indicator.Swing(input, strength);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.EMA EMA(int period) => (NinjaTrader.NinjaScript.Indicators.EMA) null;

  public NinjaTrader.NinjaScript.Indicators.EMA EMA(ISeries<double> input, int period)
  {
    return this.indicator.EMA(input, period);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public NinjaTrader.NinjaScript.Indicators.ATR ATR(int period) => (NinjaTrader.NinjaScript.Indicators.ATR) null;

  public NinjaTrader.NinjaScript.Indicators.ATR ATR(ISeries<double> input, int period)
  {
    return this.indicator.ATR(input, period);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static MarketAnalyzerColumn()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
