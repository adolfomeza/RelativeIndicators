#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	public class RelativeBarAdvisor : Indicator
	{
		private double dailyAvgVolume;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Calculates the recommended Volume Bar setting to achieve a target number of bars per day.";
				Name										= "Relative Bar Advisor";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				TargetBarsPerDay = 1500;
				LookbackDays = 20;
				TextColor = Brushes.Orange;
				TextPosition = TextPosition.TopRight;
			}
			else if (State == State.Configure)
			{
				// Add Daily Data Series to calculate Average Volume
				AddDataSeries(BarsPeriodType.Day, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			// Logic runs on the Primary Bar Series (BarInProgress 0)
			// But we need data from Secondary Series (Daily, BarInProgress 1)
			
			if (CurrentBars[0] < 1 || CurrentBars[1] < LookbackDays) return;

			// Calculate SMA of Daily Volume
			// We access BarsArray[1] (Daily)
			if (BarsInProgress == 0)
			{
				double totalVol = 0;
				int count = 0;
				
				// Loop through last N daily bars
				// Note: CurrentDay is at index CurrentBars[1]
				for (int i = 0; i < LookbackDays; i++)
				{
					int idx = CurrentBars[1] - i;
					if (idx >= 0)
					{
						// Use Volumes[1] to access the Volume series of the secondary data series
						totalVol += Volumes[1].GetValueAt(idx);
						count++;
					}
				}
				
				if (count > 0)
				{
					dailyAvgVolume = totalVol / count;
					double recommendedSetting = dailyAvgVolume / TargetBarsPerDay;
					
					// Round to nearest decent number (optional, but keep raw for accuracy)
					int recInt = (int)Math.Round(recommendedSetting);
					
					// Draw Label
					string text = string.Format("Target Bars: {0}\nAvg Daily Vol ({1}d): {2:N0}\nRec. Volume Setting: {3}", 
						TargetBarsPerDay, count, dailyAvgVolume, recInt);
						
					Draw.TextFixed(this, "VolAdvisor", text, TextPosition, TextColor, 
						new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Black, 100);
				}
			}
		}

		#region Properties
		[Range(100, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name="Target Bars Per Day", Description="Desired number of bars per session", Order=1, GroupName="Parameters")]
		public int TargetBarsPerDay
		{ get; set; }

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(Name="Lookback Days", Description="Days to average volume", Order=2, GroupName="Parameters")]
		public int LookbackDays
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Text Color", GroupName="Visual", Order=3)]
		public Brush TextColor
		{ get; set; }

		[Browsable(false)]
		public string TextColorSerializable
		{
			get { return Serialize.BrushToString(TextColor); }
			set { TextColor = Serialize.StringToBrush(value); }
		}
		
		[Display(Name="Text Position", GroupName="Visual", Order=4)]
		public TextPosition TextPosition
		{ get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeBarAdvisor[] cacheRelativeBarAdvisor;
		public RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(int targetBarsPerDay, int lookbackDays)
		{
			return RelativeBarAdvisor(Input, targetBarsPerDay, lookbackDays);
		}

		public RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(ISeries<double> input, int targetBarsPerDay, int lookbackDays)
		{
			if (cacheRelativeBarAdvisor != null)
				for (int idx = 0; idx < cacheRelativeBarAdvisor.Length; idx++)
					if (cacheRelativeBarAdvisor[idx] != null && cacheRelativeBarAdvisor[idx].TargetBarsPerDay == targetBarsPerDay && cacheRelativeBarAdvisor[idx].LookbackDays == lookbackDays && cacheRelativeBarAdvisor[idx].EqualsInput(input))
						return cacheRelativeBarAdvisor[idx];
			return CacheIndicator<RelativeIndicators.RelativeBarAdvisor>(new RelativeIndicators.RelativeBarAdvisor(){ TargetBarsPerDay = targetBarsPerDay, LookbackDays = lookbackDays }, input, ref cacheRelativeBarAdvisor);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(int targetBarsPerDay, int lookbackDays)
		{
			return indicator.RelativeBarAdvisor(Input, targetBarsPerDay, lookbackDays);
		}

		public Indicators.RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(ISeries<double> input , int targetBarsPerDay, int lookbackDays)
		{
			return indicator.RelativeBarAdvisor(input, targetBarsPerDay, lookbackDays);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(int targetBarsPerDay, int lookbackDays)
		{
			return indicator.RelativeBarAdvisor(Input, targetBarsPerDay, lookbackDays);
		}

		public Indicators.RelativeIndicators.RelativeBarAdvisor RelativeBarAdvisor(ISeries<double> input , int targetBarsPerDay, int lookbackDays)
		{
			return indicator.RelativeBarAdvisor(input, targetBarsPerDay, lookbackDays);
		}
	}
}

#endregion
