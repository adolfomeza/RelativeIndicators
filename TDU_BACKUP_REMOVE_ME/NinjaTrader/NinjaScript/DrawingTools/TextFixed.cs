// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.TextFixed
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui.Chart;
using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class TextFixed : Text
{
  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolTextFixedTextPosition", GroupName = "NinjaScriptGeneral")]
  public TextPosition TextPosition { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override void OnCalculateMinMax()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private int PaddingMultiplier(ChartControl chartControl, ChartPanel panel, bool top) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override Point GetTextDrawingPosition(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale)
  {
    return new Point();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override Rect GetCurrentRect(Rect layoutRect, double outlinePadding) => new Rect();

  public override bool IsVisibleOnChart(
    ChartControl chartControl,
    ChartScale chartScale,
    DateTime firstTimeOnChart,
    DateTime lastTimeOnChart)
  {
    return true;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TextFixed()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
