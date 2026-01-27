// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.FibonacciExtensions
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class FibonacciExtensions : FibonacciRetracements
{
  private Point anchorExtensionPoint;

  [Display(Order = 3)]
  public ChartAnchor ExtensionAnchor { get; set; }

  public override IEnumerable<ChartAnchor> Anchors
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (IEnumerable<ChartAnchor>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected new Tuple<Point, Point> GetPriceLevelLinePoints(
    PriceLevel priceLevel,
    ChartControl chartControl,
    ChartScale chartScale,
    bool isInverted)
  {
    return (Tuple<Point, Point>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private new void DrawPriceLevelText(
    ChartPanel chartPanel,
    ChartScale chartScale,
    double minX,
    double maxX,
    double y,
    double price,
    PriceLevel priceLevel)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override Cursor GetCursor(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    Point point)
  {
    return (Cursor) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Point GetEndLineMidpoint(ChartControl chartControl, ChartScale chartScale) => new Point();

  [MethodImpl(MethodImplOptions.NoInlining)]
  public sealed override Point[] GetSelectionPoints(
    ChartControl chartControl,
    ChartScale chartScale)
  {
    return (Point[]) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private string GetPriceString(double price, PriceLevel priceLevel, ChartPanel chartPanel)
  {
    return (string) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Tuple<Point, Point> GetTranslatedExtensionYLine(
    ChartControl chartControl,
    ChartScale chartScale)
  {
    return (Tuple<Point, Point>) null;
  }

  public override object Icon => (object) Icons.DrawFbExtensions;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override bool IsAlertConditionTrue(
    AlertConditionItem conditionItem,
    Condition condition,
    ChartAlertValue[] values,
    ChartControl chartControl,
    ChartScale chartScale)
  {
    return false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override void OnMouseDown(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override void OnMouseMove(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override void OnRender(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static FibonacciExtensions()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
