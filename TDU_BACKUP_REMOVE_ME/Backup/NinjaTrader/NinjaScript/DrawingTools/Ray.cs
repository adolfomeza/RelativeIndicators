// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.Ray
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using NinjaTrader.Gui.Tools;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

public class Ray : Line
{
  public override object Icon => (object) Icons.DrawRay;

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override void OnStateChange()
  {
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  static Ray()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
