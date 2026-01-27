// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.PathToolSegment
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class PathToolSegment : ICloneable
{
  [Browsable(false)]
  public ChartAnchor EndAnchor { get; set; }

  [Browsable(false)]
  public string Name { get; set; }

  [Browsable(false)]
  public ChartAnchor StartAnchor { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public object AssemblyClone(Type t) => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual object Clone() => (object) null;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(PathToolSegment other)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public PathToolSegment()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public PathToolSegment(ChartAnchor startAnchor, ChartAnchor endAnchor, string name)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static PathToolSegment()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
