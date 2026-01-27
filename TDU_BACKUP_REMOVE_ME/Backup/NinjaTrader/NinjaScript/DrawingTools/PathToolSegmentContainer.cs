// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.PathToolSegmentContainer
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public abstract class PathToolSegmentContainer : DrawingTool
{
  [Browsable(false)]
  [SkipOnCopyTo(true)]
  public List<PathToolSegment> PathToolSegments { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(NinjaTrader.NinjaScript.NinjaScript ninjaScript)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected PathToolSegmentContainer()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static PathToolSegmentContainer()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
