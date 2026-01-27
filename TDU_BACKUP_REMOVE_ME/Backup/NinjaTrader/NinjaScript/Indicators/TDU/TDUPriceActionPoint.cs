// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceActionPoint
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPriceActionPoint
{
  public double Price { get; set; }

  public DateTime Time { get; set; }

  public TDUPASwingType SwingType { get; set; }

  public int Bar { get; set; }

  public bool IsHigh
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => false;
  }

  public bool IsLow
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPriceActionPoint()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
