// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Cbi;
using NinjaTrader.Custom;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Serialization;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

[TypeConverter("NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceActionConverter")]
public class TDUPriceAction : Indicator
{
  private const int SeriesChart = 0;
  private const int SeriesSR = 1;
  private const string _productId = "PFJTQUtleVZhbHVlPjxNb2R1bHVzPndxZmJNWnlueUxEdW9Tb3RZbUE5cWRTRWEzbXN6VDJscU5tR0x5ckxVUkpLMEdwTG1DcXcvZWE4d3BGQWpmQlBjUW52RnR0OUFZRVo2bnUwNStSSkJNODRCNnc4SlZoazhVQW1tbzFQQzZrWTdzTDlpUFNsNldybGJEL2hTRzIzSGFQeFlyRTBYT2hqdG0xdmF3MzFBb2tYanloelF2WkVQY3Z6RXZmOEswNmRXQVNzM0pDY3YwNklMRUhPbUdDK2NROXNZWkw5Zk5lNzQ0b3ViWGRxUElocjZmOE9vSmw2MnZlbFJNSVVaMnNJRkRqZzFtaEZPNzJ4WEJ6ZnNWa3RtNWZDK2lGK2wxLzFrdW5JVHFKWmUyNWFQdk5oWEZ2cndGT2QxY3FObDFlczZReGhaZ0lxVktaKy9HYnJBd1o4YzVCbzZ3UXNoQjd6WUNjNzNCbVVwUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==";
  private TimeSpan _sessionStart;
  private TimeSpan _sessionEnd;
  private readonly List<TDUPriceActionPoint> _srSwings;
  private List<TDUPASRPoint> _srPoints;
  private double _rangeLow;
  private double _rangeHi;
  private int _rangeStartBar;
  private int _rangeRectangleIndex;
  private List<TDUPAWaveSwing> _swingHHLLs;
  private int _upCnt;
  private int _downCnt;
  private TDUPATSWaveBox _boxUp;
  private TDUPATSWaveBox _boxDn;
  private List<TDUPAWaveSwing> _swingHHLLLos;
  private List<TDUPAWaveSwing> _swingHHLHis;
  private TDUPAWaveSwing _extSwing;
  private TDUPAWaveSwing _reverseSwing;
  private int _prevNonInsideBar;
  private int _prevSwingBar;
  private double _previousSRSwingHi;
  private double _previousSRSwingLo;
  private int _prevSRBar;
  private double _maxDistance;
  private NinjaTrader.NinjaScript.Indicators.Swing _swingSR;
  private double _prevSRLo;
  private double _prevSRHi;
  private int _previousBar;
  private DateTime _now;
  private bool _isConnected;
  private bool _hasRealtimeData;
  private SessionIterator _sessionIterator;
  private DispatcherTimer _timer;
  private string _tickCount;
  private double _price;
  private List<TDUPATSCongestionBox> _boxes;
  private TDUPATSCongestionBox _currentBox;
  private int _boxNr;
  private int _minSwingSizeInTicks;
  private static readonly Action EmptyDelegate;
  private double _pointValue;
  private NinjaTrader.NinjaScript.Indicators.EMA _ema;
  private List<double> _breakEvenATR;
  private List<double> _trailingATR;
  private string toolbarname;
  private string uID;
  private bool isToolBarButtonAdded;
  private NinjaTrader.Gui.Chart.Chart chartWindow;
  private Grid indytoolbar;
  private Menu MenuControlContainer;
  private MenuItem MenuControl;
  private MenuItem miRecalculate1;
  private bool ShowAll;
  private Dictionary<string, NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction.TextBoxContext> _textBoxes;
  private int _prevCongBar;
  private List<int> _barsTraded;
  private TDUPAEntry _longEntry;
  private TDUPAEntry _shortEntry;
  private TDUPAEntry _calcEntry;
  private List<TDUPAEntry> _entries;
  private List<TDUPAEntry> _longEntries;
  private List<TDUPAEntry> _shortEntries;
  private double long_stoploss;
  private double short_stoploss;
  private int _long_state_count;
  private int _long_substate_count;
  private int _short_state_count;
  private int _prev_long_bar;
  private int _short_substate_count;
  private int _prev_short_bar;
  private bool _long_broken;
  private bool _short_broken;
  private DateTime _dateNow;
  private int _prevCurrentEntryBar;
  private double _prevAsk;
  private List<TDUPATSPivot> _pivots;
  private List<TDUPATSPivot> _pivotsHi;
  private List<TDUPATSPivot> _pivotsLow;
  private bool _resetBullCount;
  private bool _resetBearCount;
  private bool _resetBull2ndEntry;
  private bool _resetBear2ndEntry;
  private StackPanel _panel;
  private Button _btnShort;
  private Button _btnLong;
  private Button _btnCancel;
  private AtmStrategy _scalpShortATM;
  private AtmStrategy _scalpLongATM;
  private Order _scalpShortEntryOrder;
  private Order _runnerShortEntryOrder;
  private Order _scalpLongEntryOrder;
  private Order _runnerLongEntryOrder;
  private Order _scalpStoplossOrder;
  private Order _runnerStoplossOrder;
  private Order _scalpTakeProfitOrder;
  private Order _runnerTakeProfitOrder;
  private TDUPAEntry _tradeEntryShort;
  private TDUPAEntry _tradeEntryLong;
  private bool _initialized;
  private NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction.ResponseType _errorCode;
  private DateTime _licenseTimer;

  [ReadOnly(true)]
  [Display(ResourceType = typeof (Resource), Name = "Indicator Version", GroupName = "00. TradeDevils", Order = 0)]
  public string Version
  {
    get => "1.0.1.9";
    set
    {
    }
  }

  [ReadOnly(true)]
  [Display(ResourceType = typeof (Resource), Name = "Website", GroupName = "00. TradeDevils", Order = 1)]
  public string Website
  {
    get => "www.tradedevils-indicators.com";
    set
    {
    }
  }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = "Leg count method", GroupName = "01. Parameters", Order = 0)]
  public TDUPatsRules LegCountMethod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Place (0,1,2,3,4,...) labels", GroupName = "01. Parameters", Order = 1)]
  public TDUPatsLabelPositionsType TDUPatsLabelPositionsType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Ignore engulfing bars", GroupName = "01. Parameters", Order = 2)]
  public bool IgnoreEngulfingBars { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Reset count at DT/DB", GroupName = "01. Parameters", Order = 3)]
  [NinjaScriptProperty]
  public bool ResetCountAtDTDB { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show 0-1", GroupName = "01. Parameters", Order = 4)]
  public bool Show01 { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show 3-4-5 and higher", GroupName = "01. Parameters", Order = 5)]
  public bool ShowHigherEntries { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show 2LPB lines", GroupName = "01. Parameters", Order = 6)]
  public bool Show2LPBLines { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show SL/TP", GroupName = "01. Parameters", Order = 7)]
  public bool ShowSLTP { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show Traps", GroupName = "01. Parameters", Order = 8)]
  public bool ShowTraps { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show Stoploss (in ticks)", GroupName = "01. Parameters", Order = 9)]
  public bool ShowRisk { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show contracts", GroupName = "01. Parameters", Order = 10)]
  public bool ShowContracts { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show Statistics", GroupName = "01. Parameters", Order = 11)]
  public bool ShowStatistics { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show HH/LL", GroupName = "01. Parameters", Order = 12)]
  public bool ShowHHLL { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show Congestion", GroupName = "01. Parameters", Order = 13)]
  public bool ShowCongestion { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show EntryBar Timer", GroupName = "01. Parameters", Order = 14)]
  public bool ShowCounter { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Show EMA", GroupName = "01. Parameters", Order = 15)]
  public bool ShowEma { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> EMA Period", GroupName = "01. Parameters", Order = 16 /*0x10*/)]
  public int EMAPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "HH/LL Y offset", GroupName = "01. Parameters", Order = 27)]
  public int HHLLOffset { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Entry Y offset", GroupName = "01. Parameters", Order = 28)]
  public int EntryOffset { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Congestion Margin (ticks)", GroupName = "01. Parameters", Order = 29)]
  public int CongestionMargin { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(Name = "Trade management", Order = 1, GroupName = "02. Trade management")]
  [NinjaScriptProperty]
  public TDUPatsTradeManagement ATMType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Commissions ($) per contract", GroupName = "02. Trade management", Order = 2)]
  [NinjaScriptProperty]
  public double Commissions { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = "Trap # ticks above/below signal bar(ticks)", Description = "Number of ticks above/below signal bar", GroupName = "02. Trade management", Order = 3)]
  public int TrapOffsetTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Stoploss type", GroupName = "02. Trade management", Order = 10)]
  [RefreshProperties(RefreshProperties.All)]
  public TDUPATSStoplossType StoplossType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Ticks", GroupName = "02. Trade management", Order = 11)]
  public int StoplossTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Period", GroupName = "02. Trade management", Order = 12)]
  public int StoplossATRPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Multiplier", GroupName = "02. Trade management", Order = 13)]
  public double StoplossATRMultiplier { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = " --> Max. stoploss (ticks)", Description = "Max. stoploss (ticks)", GroupName = "02. Trade management", Order = 14)]
  public int MaxStopLossTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> # offset ticks above/below signal bar", Description = "Number of ticks above/below signal bar", GroupName = "02. Trade management", Order = 15)]
  [NinjaScriptProperty]
  public int StoplossTicksOffset { get; set; }

  [Display(Name = "Show Order panel", Order = 1, GroupName = "05. Order panel")]
  [RefreshProperties(RefreshProperties.All)]
  public bool ShowOrderPanel { get; set; }

  [Display(Name = " --> Docking", Order = 2, GroupName = "05. Order panel")]
  public TDUPATSToolbarDock Docking { get; set; }

  [Display(Name = " --> Direction", Order = 3, GroupName = "05. Order panel")]
  public FlowDirection Flow { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = "Scalp Position sizing type", GroupName = "03. Scalp", Order = 1)]
  public TDUPATSPositionSizing ScalpPositionType { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = " --> Fixed # contracts", Description = "The number of contract/trade when using Fixed Contracts Position sizing type", GroupName = "03. Scalp", Order = 2)]
  public int ScalpFixedContracts { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = " --> Fixed $ amount", Description = "The amount of dollars to risk per trade when using Fixed $ Position sizing type", GroupName = "03. Scalp", Order = 3)]
  public int ScalpFixedAmount { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> % of account", Description = "% of account to risk / trade when using PercentageOfCapital Position sizing type", GroupName = "03. Scalp", Order = 4)]
  [NinjaScriptProperty]
  public double ScalpPercentCapital { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Max # contracts", Description = "Max to trade", GroupName = "03. Scalp", Order = 5)]
  [NinjaScriptProperty]
  public int ScalpMaxContracts { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Scalp Target type", GroupName = "03. Scalp", Order = 21)]
  public TDUPATSSTargetType ScalpTargetType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Ticks", GroupName = "03. Scalp", Order = 22)]
  public int ScalpTargetTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Period", GroupName = "03. Scalp", Order = 23)]
  public int ScalpTargetATRPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Multiplier", GroupName = "03. Scalp", Order = 24)]
  public double ScalpTargetATRMultiplier { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Risk/Reward", GroupName = "03. Scalp", Order = 25)]
  public double ScalpTargetRiskReward { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Runner Position sizing type", GroupName = "04. Runner", Order = 1)]
  [NinjaScriptProperty]
  public TDUPATSPositionSizingRunner RunnerPositionType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Fixed # contracts", Description = "The number of contract/trade when using Fixed Contracts Position sizing type", GroupName = "04. Runner", Order = 2)]
  [NinjaScriptProperty]
  public int RunnerFixedContracts { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = " --> Fixed $ amount", Description = "The amount of dollars to risk per trade when using Fixed $ Position sizing type", GroupName = "04. Runner", Order = 3)]
  public int RunnerFixedAmount { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> % of account", Description = "% of account to risk / trade when using PercentageOfCapital Position sizing type", GroupName = "04. Runner", Order = 4)]
  [NinjaScriptProperty]
  public double RunnerPercentCapital { get; set; }

  [NinjaScriptProperty]
  [Display(ResourceType = typeof (Resource), Name = " --> Max # contracts", Description = "Max to trade", GroupName = "04. Runner", Order = 5)]
  public int RunnerMaxContracts { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Runner Move stop to break even when", GroupName = "04. Runner", Order = 20)]
  public TDUPATSBreakEvenType RunnerBreakEvenType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Ticks", GroupName = "04. Runner", Order = 21)]
  public int RunnerBreakEvenTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Period", GroupName = "04. Runner", Order = 22)]
  public int RunnerBreakEvenATRPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Multiplier", GroupName = "04. Runner", Order = 23)]
  public double RunnerBreakEvenATRMultiplier { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Risk/Reward", GroupName = "04. Runner", Order = 24)]
  public double RunnerBreakEvenRiskReward { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> #ticks offset", GroupName = "04. Runner", Order = 25)]
  public int RunnerBreakEvenTicksOffset { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Runner stoploss trailing type", GroupName = "04. Runner", Order = 40)]
  public TDUPATSTrailingType RunnerTrailingType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Period", GroupName = "04. Runner", Order = 42)]
  public int RunnerTrailingATRPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Multiplier", GroupName = "04. Runner", Order = 43)]
  public double RunnerTrailingATRMultiplier { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> #ticks offset", GroupName = "04. Runner", Order = 25)]
  public int RunnerTrailingTicksOffset { get; set; }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Runner Target type", GroupName = "04. Runner", Order = 50)]
  public TDUPATSSTargetType RunnerTargetType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Ticks", GroupName = "04. Runner", Order = 51)]
  public int RunnerTargetTicks { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Period", GroupName = "04. Runner", Order = 52)]
  public int RunnerTargetATRPeriod { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> ATR Multiplier", GroupName = "04. Runner", Order = 53)]
  public double RunnerTargetATRMultiplier { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Risk/Reward", GroupName = "04. Runner", Order = 54)]
  public double RunnerTargetRiskReward { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Enable signal bar filter", Description = "Enable the signal bar filter", GroupName = "06. Statistics Filters", Order = 1)]
  [RefreshProperties(RefreshProperties.All)]
  public bool EnableSignalBarStrengthFilter { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Min. Signal bar strength (%)", Description = "Min. Signal bar strength (%)", GroupName = "06. Statistics Filters", Order = 2)]
  public double MinSignalbarStrength { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Only show trades at key entry points", GroupName = "06. Statistics Filters", Order = 4)]
  public bool OnlyShowEntriesAtKeyEntryPoints { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Ignore counter trend trades", GroupName = "06. Statistics Filters", Order = 5)]
  public bool IgnoreCounterTrendTrades { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Use EMA as key entry point", GroupName = "06. Statistics Filters", Order = 6)]
  [RefreshProperties(RefreshProperties.All)]
  public bool UseEMAAsKeyEntryPoint { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Enable session filter", GroupName = "06. Statistics Filters", Order = 9)]
  [RefreshProperties(RefreshProperties.All)]
  public bool EnableSessionFilter { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Session start time (+ h:min)", Description = "Session start", GroupName = "06. Statistics Filters", Order = 10)]
  public string S_SessionStartTime
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (string) null;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  [Browsable(false)]
  [XmlIgnore]
  public TimeSpan SessionStartTime
  {
    get => this._sessionStart;
    set => this._sessionStart = value;
  }

  [Display(ResourceType = typeof (Resource), Name = " --> Session end time (+ h:min)", Description = "Session end time", GroupName = "06. Statistics Filters", Order = 11)]
  public string S_SessionEndTime
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (string) null;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  [RefreshProperties(RefreshProperties.All)]
  [Display(ResourceType = typeof (Resource), Name = "Show Support/Resistance", GroupName = "07. Support & Resistance", Order = 1)]
  public bool ShowSR { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> S&R Detail", GroupName = "07. Support & Resistance", Order = 2)]
  public TDUPASRDetail SRDetail { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> S&R Max Line width", GroupName = "07. Support & Resistance", Order = 3)]
  public int SRMaxLineWidth { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> S&R Strength", GroupName = "07. Support & Resistance", Order = 4)]
  public int Strength { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> Max S&R Lines to show", GroupName = "07. Support & Resistance", Order = 5)]
  public int SRLinesToShow { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> S&R Period Type", GroupName = "07. Support & Resistance", Order = 6)]
  public BarsPeriodType SrBarPeriodPeriodType { get; set; }

  [Display(ResourceType = typeof (Resource), Name = " --> S&R Period value", GroupName = "07. Support & Resistance", Order = 7)]
  public int SrBarPeriodPeriodValue { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2EL entry", GroupName = "08. Alerts", Order = 1)]
  public bool SecondEntryLongAlert { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2ES entry", GroupName = "08. Alerts", Order = 2)]
  public bool SecondEntryShortAlert { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2EL setting up", GroupName = "08. Alerts", Order = 3)]
  public bool SecondEntryLongSettingUpAlert { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2ES setting up", GroupName = "08. Alerts", Order = 4)]
  public bool SecondEntryShortSettingUpAlert { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Trap", GroupName = "08. Alerts", Order = 5)]
  public bool TrapAlertEnabled { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Long Entry alert Sound", GroupName = "08. Alerts", Order = 6)]
  [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
  public string LongEntryAlertSound { get; set; }

  [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
  [Display(ResourceType = typeof (Resource), Name = "Short Entry alert Sound", GroupName = "08. Alerts", Order = 7)]
  public string ShortEntryAlertSound { get; set; }

  [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
  [Display(ResourceType = typeof (Resource), Name = "Long Setting up alert Sound", GroupName = "08. Alerts", Order = 8)]
  public string LongSettingUpAlertSound { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Short Setting up alert Sound", GroupName = "08. Alerts", Order = 9)]
  [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
  public string ShortSettingUpAlertSound { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Trap alert Sound", GroupName = "08. Alerts", Order = 10)]
  [PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter = "WAV Files (*.wav)|*.wav")]
  public string TrapAlertSound { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2nd Entry Short Label", GroupName = "09. Labels", Order = 1)]
  public string Label2ES { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2nd Entry Long Label", GroupName = "09. Labels", Order = 2)]
  public string Label2EL { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Failed 2nd Entry Short Label", GroupName = "09. Labels", Order = 3)]
  public string LabelF2ES { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Failed 2nd Entry Long Label", GroupName = "09. Labels", Order = 4)]
  public string LabelF2EL { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Trap Label", GroupName = "09. Labels", Order = 4)]
  public string LabelTrap { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Text Font", GroupName = "10. Visual", Order = 1)]
  public SimpleFont Font { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Support", GroupName = "10. Visual", Order = 2)]
  public Stroke Support { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Resistance", GroupName = "10. Visual", Order = 3)]
  public Stroke Resistance { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Congestion Stroke", GroupName = "10. Visual", Order = 4)]
  public Stroke CongestionStroke { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Bullish candle", GroupName = "10. Visual", Order = 5)]
  public Stroke BullishCandle { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Bearish candle", GroupName = "10. Visual", Order = 6)]
  public Stroke BearishCandle { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Text Color", GroupName = "10. Visual", Order = 7)]
  public Stroke TextColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2EL", GroupName = "10. Visual", Order = 8)]
  public Stroke Entry2ELColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "2ES", GroupName = "10. Visual", Order = 9)]
  public Stroke Entry2ESColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "F2EL", GroupName = "10. Visual", Order = 10)]
  public Stroke Failed2ELColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "F2ES", GroupName = "10. Visual", Order = 11)]
  public Stroke Failed2ESColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Long color", GroupName = "10. Visual", Order = 12)]
  public Stroke LongColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Short color", GroupName = "10. Visual", Order = 13)]
  public Stroke ShortColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Temp color", GroupName = "10. Visual", Order = 14)]
  public Stroke TempColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Winner background", GroupName = "10. Visual", Order = 15)]
  public Stroke Winner { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Loser background", GroupName = "10. Visual", Order = 16 /*0x10*/)]
  public Stroke Loser { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "Trap background", GroupName = "10. Visual", Order = 17)]
  public Stroke Trap { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "EMA Rising", GroupName = "10. Visual", Order = 18)]
  public Stroke EmaRisingColor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "EMA Falling", GroupName = "10. Visual", Order = 19)]
  public Stroke EmaFallingColor { get; set; }

  [Browsable(false)]
  [XmlIgnore]
  public double CashValue { get; set; }

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> BarSizeInPoints => ((NinjaScriptBase) this).Values[0];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> BarSizeInTicks => ((NinjaScriptBase) this).Values[1];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> BarSizeInDollars => ((NinjaScriptBase) this).Values[2];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> SignalLong => ((NinjaScriptBase) this).Values[3];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> StopLossLong => ((NinjaScriptBase) this).Values[4];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> ContractsLong => ((NinjaScriptBase) this).Values[5];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> SignalBarStrengthLong => ((NinjaScriptBase) this).Values[6];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> SignalShort => ((NinjaScriptBase) this).Values[7];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> StopLossShort => ((NinjaScriptBase) this).Values[8];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> ContractsShort => ((NinjaScriptBase) this).Values[9];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> SignalBarStrengthShort => ((NinjaScriptBase) this).Values[10];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> TrapLong => ((NinjaScriptBase) this).Values[11];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> TrapShort => ((NinjaScriptBase) this).Values[12];

  [Browsable(false)]
  [XmlIgnore]
  public Series<double> Congestion => ((NinjaScriptBase) this).Values[13];

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> Ema => ((NinjaScriptBase) this).Values[14];

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void addToolBar()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void AddIntegerTextBox(
    string name,
    Grid grid,
    int initialValue,
    int min,
    int max,
    string title,
    bool needsReclaculation,
    Action<int> func)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void AddDoubleTextBox(
    string name,
    Grid grid,
    double initialValue,
    double min,
    double max,
    double step,
    string title,
    bool needsReclaculation,
    Action<double> func)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private MenuItem CreateGridItem(out Grid grid) => (MenuItem) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private T SelectNextEnumValue<T>(T enumType) => default (T);

  [MethodImpl(MethodImplOptions.NoInlining)]
  private MenuItem CreateToggleMenuItem(
    bool onOff,
    string title,
    bool needsRecalcuate,
    Func<bool> action)
  {
    return (MenuItem) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private MenuItem CreateMenuItemEnumSelection<T>(
    T enumType,
    string title,
    bool needsRecalcuate,
    Func<T> action)
  {
    return (MenuItem) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void menuTxtbox_KeyDownInteger(object sender, KeyEventArgs e)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void InformUserAboutRecalculation()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ResetRecalculationUI()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void TabSelectionChangedHandler(object sender, SelectionChangedEventArgs e)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnBarUpdate()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool DisplayTime() => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void OnTimerTick(object sender, EventArgs e)
  {
  }

  private SessionIterator SessionIterator
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (SessionIterator) null;
  }

  private DateTime Now
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => new DateTime();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected void DetectSRSwings()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void AddSRSwingLevel(int barsAgo, double price, bool isSwingLo)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void DrawSRLines()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private TDUPASRPoint GetTouches(TDUPriceActionPoint swing) => (TDUPASRPoint) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool DoesLevelExists(TDUPriceActionPoint swing) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void DrawCongestion()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void FindMostTouches(
    ref double boxLow,
    ref double boxHi,
    int barsAgo,
    double maxHighestBody = 1.7976931348623157E+308,
    double minLowestBody = -1.7976931348623157E+308,
    bool retry = false)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsGreen(int barsAgo) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsRed(int barsAgo) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetBarsInside(out double boxLow, out double boxHi, out int barsAgo) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CalcEntries()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void AddBrooksPivotHi(TDUPATSPivot pivot)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void AddBrooksPivotLow(TDUPATSPivot pivot)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Brooks_Check_EntrySettingUp()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Brooks_CountPivots(bool firstRun = true)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void On2ESLowMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void On2ESHighMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Update2ESOpenMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool On2ESResetCountMack() => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Count2ESMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void On2ELHighMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void On2ELLowMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Update2ELOpenMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool On2ELResetCountMack() => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void Count2ELMack()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CalculateFailedSecondEntries()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double GetSignalBarStrength(int bar, bool isLong) => 0.0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void SendTrapAlert()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CalculateGannSwings()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void UpdateRightEnd()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CalcUpSwings()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CalcDownSwings()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void UpdateDelta(TDUPAWaveSwing swing)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetTotalContracts(TDUPAEntry entry) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double GetTargetInTicks(
    TDUPATSSTargetType targetType,
    int atrPeriod,
    double atrMultiplier,
    int ticks,
    double riskReward,
    double entryPrice,
    double stoplossPrice)
  {
    return 0.0;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetStoplossInTicks(TDUPAEntry entry) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetScalpContracts(TDUPAEntry entry) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetRunnerContracts(TDUPAEntry entry) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetStoplossBaseOnSignalBarInticks(TDUPAEntry entry) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnRender(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RenderPivots(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RenderCongestionBox(
    ChartControl chartControl,
    ChartScale chartScale,
    TDUPATSCongestionBox box)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RenderStatistics(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void GetStats(
    ChartControl chartControl,
    ChartScale chartScale,
    TDUPAEntry entry,
    Dictionary<DateTime, double> profitPerDay,
    ref int totalContractsTraded,
    ref int tradeCount,
    ref double averageStoploss,
    ref double losingTrades,
    ref double totalLossAmount,
    ref double winningTrades,
    ref double totalProfitAmount)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RenderEntry(ChartControl chartControl, ChartScale chartScale, TDUPAEntry entry)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsRising(int barsAgo) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsFalling(int barsAgo) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double CalculateEntryPnL(
    ChartControl chartControl,
    ChartScale chartScale,
    TDUPAEntry entry)
  {
    return 0.0;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RenderSwing(
    ChartControl chartControl,
    ChartScale chartScale,
    TDUPAWaveSwing swing,
    List<TDUPAWaveSwing> highs,
    List<TDUPAWaveSwing> lows,
    bool confirmed)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void DrawTextLabel(
    ChartControl chartControl,
    ChartScale chartScale,
    string text,
    float xoffset,
    float yoffset,
    Brush fontBrush,
    SimpleFont font = null,
    TextAlignment align = 0)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private float GetTextWidth(SimpleFont font, string text) => 0.0f;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private float GetTextHeight(SimpleFont font, string text) => 0.0f;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double RenderArrow(
    ChartControl chartControl,
    ChartScale chartScale,
    float x,
    float y,
    Brush brush,
    bool isUp = true)
  {
    return 0.0;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double RenderDiamond(
    ChartControl chartControl,
    ChartScale chartScale,
    float x,
    float y,
    Brush brush,
    bool isUp = true)
  {
    return 0.0;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsAtEMA(TDUPAEntry entry) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool AppearsAtKeyEntryPoint(
    ChartControl chartControl,
    ChartScale chartScale,
    int barIdx,
    bool tradeIsLong)
  {
    return false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private double Cross(Vector a, Vector v) => 0.0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsZero(double d) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool DoLinesInterSect(Vector p, Vector p2, Vector q, Vector q2) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void CreateOrderPanel()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void DockOrderPanel()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Account GetSelectedAccount() => (Account) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private string GetSelectedAtm() => (string) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Instrument GetInstrument() => (Instrument) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int GetQuantity(bool isLong) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void BtnCancelOnClick(object sender, RoutedEventArgs args)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void BtnShortOnClick(object sender, RoutedEventArgs e)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void BtnLongOnClick(object sender, RoutedEventArgs e)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void EnableOrderPanelButtons()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ManageSellOrders()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ManageBuyOrders()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ManageOrders()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsOrderClosed(Order entryOrder) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool IsOrderClosed(Order entryOrder, Order stoplossOrder, Order targetOrder) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool CheckEntryPrice(bool isLong, double entryPrice) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool CheckStoplossPrice(bool isLong, double stoplossPrice) => false;

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void RemoveToolBar()
  {
  }

  [Display(Name = "Email", GroupName = "00. TradeDevils", Order = 2)]
  [NinjaScriptProperty]
  public string Email { get; set; }

  [NinjaScriptProperty]
  [Display(Name = "ContactId", GroupName = "00. TradeDevils", Order = 3)]
  public long ContactId { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ShowError()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Task<bool> OnInit() => (Task<bool>) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public TDUPriceAction()
  {
  }

  static TDUPriceAction()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
    NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction.EmptyDelegate = (Action) (() => { });
  }

  private class TextBoxContext
  {
    public Label Label { get; set; }

    public TextBox TextBox { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetVisible(bool onOff)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static TextBoxContext()
    {
      \u003CAgileDotNetRTPro\u003E.Initialize();
      \u003CAgileDotNetRTPro\u003E.PostInitialize();
    }
  }

  private class Request
  {
    public long CustomerId { get; set; }

    public string Email { get; set; }

    public string Product { get; set; }

    public string MachineId { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Request()
    {
      \u003CAgileDotNetRTPro\u003E.Initialize();
      \u003CAgileDotNetRTPro\u003E.PostInitialize();
    }
  }

  private enum ResponseType
  {
    Valid,
    UnknownCustomer,
    Expired,
    CantChangeIn5Days,
  }

  private class Response
  {
    public NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceAction.ResponseType ResponseCode { get; set; }

    public long CustomerId { get; set; }

    public string Email { get; set; }

    public string Product { get; set; }

    public string MachineId { get; set; }

    public DateTime ValidUntil { get; set; }

    public string Checksum { get; set; }

    public string Hash
    {
      [MethodImpl(MethodImplOptions.NoInlining)] get => (string) null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Response()
    {
      \u003CAgileDotNetRTPro\u003E.Initialize();
      \u003CAgileDotNetRTPro\u003E.PostInitialize();
    }
  }
}
