// Decompiled with JetBrains decompiler
// Type: NinjaTrader.NinjaScript.Indicators.TDU.TDUPriceActionConverter
// Assembly: TDUPriceAction, Version=1.0.0.6, Culture=neutral
// MVID: F406B208-2A90-48DE-B68A-47FB4C68C1DB
// Assembly location: C:\Dropbox\Adolfo\Trading Software\TDU NT8 New\bin\Custom\TDUPriceAction - copia.dll

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable disable
namespace NinjaTrader.NinjaScript.Indicators.TDU;

public class TDUPriceActionConverter : IndicatorBaseConverter
{
  [MethodImpl(MethodImplOptions.NoInlining)]
  public virtual PropertyDescriptorCollection GetProperties(
    ITypeDescriptorContext context,
    object component,
    Attribute[] attrs)
  {
    return (PropertyDescriptorCollection) null;
  }

  public virtual bool GetPropertiesSupported(ITypeDescriptorContext context) => true;

  [MethodImpl(MethodImplOptions.NoInlining)]
  static TDUPriceActionConverter()
  {
    \u003CAgileDotNetRTPro\u003E.Initialize();
    \u003CAgileDotNetRTPro\u003E.PostInitialize();
  }
}
