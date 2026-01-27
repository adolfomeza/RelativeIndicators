// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPatsLeg
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPatsLeg
{
  public int LegLabelBar { get; set; }

  public double LegLabelPrice { get; set; }

  public int SignalBar { get; set; }

  public double Stoploss { get; set; }

  public double TurnPrice { get; set; }

  public int TurnBar { get; set; }

  public int BreakBar { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPatsLeg()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
