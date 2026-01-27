// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.DrawingTools.TextPosition
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System.ComponentModel;

#nullable disable
namespace NinjaTrader.NinjaScript.DrawingTools;

[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
public enum TextPosition
{
  BottomLeft,
  BottomRight,
  Center,
  TopLeft,
  TopRight,
}
