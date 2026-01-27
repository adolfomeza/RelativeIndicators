// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPATSWaveBox
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPATSWaveBox
{
  public double High { get; set; }

  public double Low { get; set; }

  public int StartBar { get; set; }

  public int EndBar { get; set; }

  public double PriceLow { get; set; }

  public double PriceHigh { get; set; }

  public DateTime StartDate { get; set; }

  public DateTime EndDate { get; set; }

  public bool Up { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPATSWaveBox()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
