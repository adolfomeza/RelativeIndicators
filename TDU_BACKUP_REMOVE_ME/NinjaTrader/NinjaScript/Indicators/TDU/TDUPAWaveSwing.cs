// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPAWaveSwing
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPAWaveSwing
{
  public int StartBar { get; set; }

  public int EndBar { get; set; }

  public double StartPrice { get; set; }

  public double EndPrice { get; set; }

  public bool IsTemp { get; set; }

  public DateTime StartDate { get; set; }

  public DateTime EndDate { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPAWaveSwing()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
