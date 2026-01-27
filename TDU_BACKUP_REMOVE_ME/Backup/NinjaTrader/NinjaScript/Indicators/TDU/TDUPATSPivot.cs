// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPATSPivot
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPATSPivot
{
  public bool IsHigh { get; set; }

  public bool IsLow => !this.IsHigh;

  public int Bar { get; set; }

  public DateTime Time { get; set; }

  public double Price { get; set; }

  public int BullCount { get; set; }

  public int BearCount { get; set; }

  public bool BearCountReset { get; set; }

  public bool BullCountReset { get; set; }

  public bool BearBroken { get; set; }

  public bool BullBroken { get; set; }

  public bool IsNewLowHigh { get; set; }

  public bool IsSecondEntry { get; set; }

  public int LabelBar { get; set; }

  public double LabelPrice { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPATSPivot()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
