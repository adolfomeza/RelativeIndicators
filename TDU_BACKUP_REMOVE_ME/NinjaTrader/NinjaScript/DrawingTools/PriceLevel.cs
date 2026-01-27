// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.PriceLevel
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Xml.Serialization;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

[XmlInclude(typeof (GannAngle))]
[CategoryDefaultExpanded(true)]
[XmlInclude(typeof (TrendLevel))]
[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.PriceLevelTypeConverter")]
public class PriceLevel : NotifyPropertyChangedBase, IStrokeProvider, ICloneable
{
  private double value;
  private string name;

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevelIsVisible", GroupName = "NinjaScriptGeneral")]
  public bool IsVisible { get; set; }

  [XmlIgnore]
  [Browsable(false)]
  public bool IsValueVisible { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevelLineStroke", GroupName = "NinjaScriptGeneral")]
  public Stroke Stroke { get; set; }

  [XmlIgnore]
  [Browsable(false)]
  public object Tag { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevelValue", GroupName = "NinjaScriptGeneral")]
  public double Value
  {
    get => this.value;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  [Browsable(false)]
  [XmlIgnore]
  public Func<double, string> ValueFormatFunc { get; set; }

  [Browsable(false)]
  public string Name
  {
    get => this.name;
    [MethodImpl(MethodImplOptions.NoInlining)] set
    {
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual object Clone() => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public object AssemblyClone(Type t) => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(PriceLevel other)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public double GetPrice(double startPrice, double totalPriceRange, bool isInverted) => 0.0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public float GetY(
    ChartScale chartScale,
    double startPrice,
    double totalPriceRange,
    bool isInverted)
  {
    return 0.0f;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public PriceLevel()
  {
  }

  public PriceLevel(double value, Brush brush)
    : this(value, brush, 2f)
  {
  }

  public PriceLevel(double value, Brush brush, float strokeWidth)
    : this(value, brush, strokeWidth, (DashStyleHelper) 0, 100)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public PriceLevel(
    double value,
    Brush brush,
    float strokeWidth,
    DashStyleHelper dashStyle,
    int opacity)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static PriceLevel()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
