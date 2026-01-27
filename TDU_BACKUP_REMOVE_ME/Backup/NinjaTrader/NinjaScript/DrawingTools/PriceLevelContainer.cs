// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.PriceLevelContainer
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

public abstract class PriceLevelContainer : DrawingTool
{
  [PropertyEditor("NinjaTrader.Gui.Tools.CollectionEditor")]
  [SkipOnCopyTo(true)]
  [Display(ResourceType = typeof (Resource), Name = "NinjaScriptDrawingToolsPriceLevels", Prompt = "NinjaScriptDrawingToolsPriceLevelsPrompt", GroupName = "NinjaScriptLines", Order = 99)]
  public List<PriceLevel> PriceLevels { get; set; }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual void CopyTo(NinjaTrader.NinjaScript.NinjaScript ninjaScript)
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected PriceLevelContainer()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  public void SetAllPriceLevelsRenderTarget()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static PriceLevelContainer()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
