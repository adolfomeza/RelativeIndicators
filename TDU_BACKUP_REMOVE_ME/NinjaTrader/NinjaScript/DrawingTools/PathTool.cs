// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.PathTool
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using SharpDX.Direct2D1;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class PathTool : PathToolSegmentContainer
{
  private const double cursorSensitivity = 15.0;
  private PathGeometry arrowPathGeometry;
  private DispatcherTimer doubleClickTimer;
  private ChartAnchor editingAnchor;
  private bool firstTime = true;

  [Browsable(false)]
  [ExcludeFromTemplate]
  [SkipOnCopyTo(true)]
  public List<ChartAnchor> ChartAnchors { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 0)]
  public Stroke OutlineStroke { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolPathBegin", GroupName = "NinjaScriptGeneral", Order = 1)]
  public PathTool.PathToolCapMode PathBegin { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolPathEnd", GroupName = "NinjaScriptGeneral", Order = 2)]
  public PathTool.PathToolCapMode PathEnd { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolPathShowCount", GroupName = "NinjaScriptGeneral", Order = 3)]
  public bool ShowCount { get; set; }

  [SkipOnCopyTo(true)]
  [ExcludeFromTemplate]
  [Display(Order = 0)]
  public ChartAnchor StartAnchor
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (ChartAnchor) null;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  public virtual IEnumerable<ChartAnchor> Anchors
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (IEnumerable<ChartAnchor>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public override void CopyTo(NinjaTrader.NinjaScript.NinjaScript ninjaScript)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private PathGeometry CreatePathGeometry(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    double pixelAdjust)
  {
    return (PathGeometry) null;
  }

  private void DoubleClickTimerTick(object sender, EventArgs e) => this.doubleClickTimer.Stop();

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual IEnumerable<AlertConditionItem> GetAlertConditionItems()
  {
    return (IEnumerable<AlertConditionItem>) null;
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
  private Point[] GetPathAnchorPoints(ChartControl chartControl, ChartScale chartScale)
  {
    return (Point[]) null;
  }

  public virtual Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
  {
    return this.GetPathAnchorPoints(chartControl, chartScale);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual IEnumerable<Condition> GetValidAlertConditions() => (IEnumerable<Condition>) null;

  public virtual object Icon => (object) Icons.DrawPath;

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
  public virtual void OnCalculateMinMax()
  {
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

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnMouseUp(
    ChartControl chartControl,
    ChartPanel chartPanel,
    ChartScale chartScale,
    ChartAnchor dataPoint)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void OnRender(ChartControl chartControl, ChartScale chartScale)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnStateChange()
  {
  }

  public virtual bool SupportsAlerts => true;

  [MethodImpl(MethodImplOptions.NoInlining)]
  static PathTool()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }

  [TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
  public enum PathToolCapMode
  {
    Arrow,
    Line,
  }
}
