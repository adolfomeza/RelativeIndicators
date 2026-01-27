// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.GannAngleContainer
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

public abstract class GannAngleContainer : DrawingTool
{
  [PropertyEditor("NinjaTrader.Gui.Tools.CollectionEditor")]
  [SkipOnCopyTo(true)]
  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsGannAngles", Prompt = "NinjaScriptDrawingToolsGannAnglesPrompt", GroupName = "NinjaScriptGeneral", Order = 99)]
  public List<GannAngle> GannAngles { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(NinjaTrader.NinjaScript.NinjaScript ninjaScript)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected GannAngleContainer()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static GannAngleContainer()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
