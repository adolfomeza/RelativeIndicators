// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.FibonacciLevels
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public abstract class FibonacciLevels : PriceLevelContainer
{
  protected const int CursorSensitivity = 15;
  private int priceLevelOpacity;
  protected ChartAnchor editingAnchor;

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolFibonacciLevelsBaseAnchorLineStroke", GroupName = "NinjaScriptLines", Order = 1)]
  public Stroke AnchorLineStroke { get; set; }

  [Display(Order = 1)]
  public ChartAnchor StartAnchor { get; set; }

  [Display(Order = 2)]
  public ChartAnchor EndAnchor { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolPriceLevelsOpacity", GroupName = "NinjaScriptGeneral")]
  [Range(0, 100)]
  public int PriceLevelOpacity
  {
    get => this.priceLevelOpacity;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  public virtual IEnumerable<ChartAnchor> Anchors
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (IEnumerable<ChartAnchor>) null;
  }

  public virtual bool SupportsAlerts => true;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual IEnumerable<AlertConditionItem> GetAlertConditionItems()
  {
    return (IEnumerable<AlertConditionItem>) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static FibonacciLevels()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
