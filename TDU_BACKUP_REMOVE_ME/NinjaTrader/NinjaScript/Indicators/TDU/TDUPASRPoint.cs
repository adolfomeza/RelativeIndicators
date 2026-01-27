// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPASRPoint
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPASRPoint
{
  public double Price { get; set; }

  public DateTime StartTime { get; set; }

  public int StartBar { get; set; }

  public DateTime EndTime { get; set; }

  public int EndBar { get; set; }

  public int Touches { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPASRPoint()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
