// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.GannFan
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class GannFan : GannAngleContainer
{
  public ChartAnchor Anchor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolGannFanFanDirection", GroupName = "NinjaScriptGeneral", Order = 3)]
  public GannFan.GannFanDirection FanDirection { get; set; }

  public virtual object Icon => (object) Icons.DrawGanFan;

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolGannFanDisplayText", GroupName = "NinjaScriptGeneral", Order = 2)]
  public bool IsTextDisplayed { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolGannFanPointsPerBar", GroupName = "NinjaScriptGeneral", Order = 4)]
  public double PointsPerBar { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolPriceLevelsOpacity", GroupName = "NinjaScriptGeneral")]
  public int PriceLevelOpacity { get; set; }

  public virtual IEnumerable<ChartAnchor> Anchors
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (IEnumerable<ChartAnchor>) null;
  }

  public virtual bool SupportsAlerts => true;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnCalculateMinMax()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public Point CalculateExtendedDataPoint(
    ChartPanel panel,
    ChartScale scale,
    int startX,
    double startPrice,
    Vector slope)
  {
    return new Point();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual Cursor GetCursor(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    Point point)
  {
    return (Cursor) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual IEnumerable<AlertConditionItem> GetAlertConditionItems()
  {
    return (IEnumerable<AlertConditionItem>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private IEnumerable<Point> GetGannEndPoints(ChartControl chartControl, ChartScale chartScale)
  {
    return (IEnumerable<Point>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Point GetGannStepPoint(
    ChartScale scale,
    double startX,
    double startPrice,
    double deltaX,
    double deltaPrice)
  {
    return new Point();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private Vector GetGannStepDataVector(double deltaX, double deltaPrice) => new Vector();

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
  {
    return (Point[]) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual IEnumerable<Condition> GetValidAlertConditions() => (IEnumerable<Condition>) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual bool IsAlertConditionTrue(
    AlertConditionItem conditionItem,
    Condition condition,
    ChartAlertValue[] values,
    ChartControl chartControl,
    ChartScale chartScale)
  {
    return false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual bool IsVisibleOnChart(
    ChartControl chartControl,
    ChartScale chartScale,
    DateTime firstTimeOnChart,
    DateTime lastTimeOnChart)
  {
    return false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnMouseDown(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnMouseMove(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
  }

  public virtual void OnMouseUp(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
    this.DrawingState = (DrawingState) 2;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnRender(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static GannFan()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }

  [TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
  public enum GannFanDirection
  {
    UpLeft,
    UpRight,
    DownLeft,
    DownRight,
  }
}
