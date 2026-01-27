// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.EMA
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators;

public class EMA : Indicator
{
  private double constant1;
  private double constant2;

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnBarUpdate()
  {
  }

  [NinjaScriptProperty]
  [Range(1, 2147483647 /*0x7FFFFFFF*/)]
  [Display(ResourceType = typeof (Resource), Name = "Period", GroupName = "NinjaScriptParameters", Order = 0)]
  public int Period { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static EMA()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
