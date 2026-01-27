// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.Swing
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Custom;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators;

public class Swing : Indicator
{
  private int constant;
  private double currentSwingHigh;
  private double currentSwingLow;
  private ArrayList lastHighCache;
  private double lastSwingHighValue;
  private ArrayList lastLowCache;
  private double lastSwingLowValue;
  private int saveCurrentBar;
  private Series<double> swingHighSeries;
  private Series<double> swingHighSwings;
  private Series<double> swingLowSeries;
  private Series<double> swingLowSwings;

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void OnBarUpdate()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public int SwingLowBar(int barsAgo, int instance, int lookBackPeriod) => 0;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public int SwingHighBar(int barsAgo, int instance, int lookBackPeriod) => 0;

  [Display(ResourceType = typeof (Resource), Name = "Strength", GroupName = "NinjaScriptParameters", Order = 0)]
  [Range(1, 2147483647 /*0x7FFFFFFF*/)]
  [NinjaScriptProperty]
  public int Strength { get; set; }

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> SwingHigh
  {
    get
    {
      ((NinjaScriptBase) this).Update();
      return this.swingHighSeries;
    }
  }

  private Series<double> SwingHighPlot
  {
    get
    {
      ((NinjaScriptBase) this).Update();
      return ((NinjaScriptBase) this).Values[0];
    }
  }

  [XmlIgnore]
  [Browsable(false)]
  public Series<double> SwingLow
  {
    get
    {
      ((NinjaScriptBase) this).Update();
      return this.swingLowSeries;
    }
  }

  private Series<double> SwingLowPlot
  {
    get
    {
      ((NinjaScriptBase) this).Update();
      return ((NinjaScriptBase) this).Values[1];
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static Swing()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
