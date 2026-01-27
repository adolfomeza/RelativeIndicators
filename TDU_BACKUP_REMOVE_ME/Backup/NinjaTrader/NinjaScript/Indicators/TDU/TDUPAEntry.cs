// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPAEntry
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPAEntry
{
  public List<TDUPatsLeg> Legs;

  public bool IsAtEMA { get; set; }

  public TDUPAEntryType Entry { get; set; }

  public int EntryBarIndex { get; set; }

  public int SignalBarIndex { get; set; }

  public DateTime EntryTime { get; set; }

  public double EntryPrice { get; set; }

  public bool IsActive { get; set; }

  public double ScalpStoplossPrice { get; set; }

  public int BarIndex0 { get; set; }

  public DateTime Time0 { get; set; }

  public DateTime EndTime { get; set; }

  public double Price0 { get; set; }

  public bool ProfitSet { get; set; }

  public bool Ignored { get; set; }

  public double Profit { get; set; }

  public double Exit { get; set; }

  public DateTime CloseTime { get; set; }

  public DateTime ScalpExitTime { get; set; }

  public bool ScalpHitTarget { get; set; }

  public int ScalpContracts { get; set; }

  public double SignalBarStrength { get; set; }

  public double ResetPrice { get; set; }

  public int Count { get; set; }

  public bool IsLong
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => false;
  }

  public bool IsShort
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => false;
  }

  public int TurnBar { get; set; }

  public double TurnPrice { get; set; }

  public bool TrapActive { get; set; }

  public double TrapPrice { get; set; }

  public int TrapBar { get; set; }

  public double PriceLow { get; set; }

  public double PriceHi { get; set; }

  public int BarLow { get; set; }

  public int BarHi { get; set; }

  public int RunnerContracts { get; set; }

  public double ScalpTargetPrice { get; set; }

  public double RunnerTargetPrice { get; set; }

  public double RunnerStoplossPrice { get; set; }

  public double SignalBarStoplossPrice { get; set; }

  public bool IsBreakEvenSet { get; set; }

  public bool RunnerTargetHit { get; set; }

  public List<double> StoplossTrail { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public TDUPAEntry()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPAEntry()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
