// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.GannAngle
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using NinjaTrader.Gui;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Xml.Serialization;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.GannAngleTypeConverter")]
public class GannAngle : NotifyPropertyChangedBase, IStrokeProvider, ICloneable
{
  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsGannAngleRatioX", GroupName = "NinjaScriptGeneral")]
  public double RatioX { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsGannAngleRatioY", GroupName = "NinjaScriptGeneral")]
  public double RatioY { get; set; }

  [Browsable(false)]
  public string Name
  {
    [MethodImpl(MethodImplOptions.NoInlining)] get => (string) null;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public object AssemblyClone(Type t) => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual object Clone() => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(GannAngle other)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public GannAngle()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public GannAngle(double ratioX, double ratioY, Brush strokeBrush)
  {
  }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevelIsVisible", GroupName = "NinjaScriptGeneral")]
  public bool IsVisible { get; set; }

  [Browsable(false)]
  [XmlIgnore]
  public bool IsValueVisible { get; set; }

  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevelLineStroke", GroupName = "NinjaScriptGeneral")]
  public Stroke Stroke { get; set; }

  [XmlIgnore]
  [Browsable(false)]
  public object Tag { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static GannAngle()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
