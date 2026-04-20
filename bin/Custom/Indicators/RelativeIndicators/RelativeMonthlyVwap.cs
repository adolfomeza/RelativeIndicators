//+----------------------------------------------------------------------------------------------+
//| Copyright © <2017>  <LizardIndicators.com - powered by AlderLab UG>
//
//| This program is free software: you can redistribute it and/or modify
//| it under the terms of the GNU General Public License as published by
//| the Free Software Foundation, either version 3 of the License, or
//| any later version.
//|
//| This program is distributed in the hope that it will be useful,
//| but WITHOUT ANY WARRANTY; without even the implied warranty of
//| MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//| GNU General Public License for more details.
//|
//| By installing this software you confirm acceptance of the GNU
//| General Public License terms. You may find a copy of the license
//| here; http://www.gnu.org/licenses/
//+----------------------------------------------------------------------------------------------+

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
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators;
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — this.RLog() + RelativeIndicatorRegistry
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	/// <summary>
	/// The Current Month VWAP is the volume weighted average price of the current month. 
	/// The indicator further displays three upper and lower volatility bands.
	/// </summary>
	/// 
	[Gui.CategoryOrder("Algorithmic Options", 0)]
	[Gui.CategoryOrder("Custom Hours", 5)]
	[Gui.CategoryOrder("Standard Deviation Bands", 10)]
	[Gui.CategoryOrder("Quarter Range Bands", 15)]
	[Gui.CategoryOrder("Data Series", 20)]
	[Gui.CategoryOrder("Set up", 30)]
	[Gui.CategoryOrder("Visual", 40)]
	[Gui.CategoryOrder("Plot Colors", 50)]
	[Gui.CategoryOrder("Plot Parameters", 60)]
	[Gui.CategoryOrder("Area Opacity", 70)]
	[Gui.CategoryOrder("Version", 80)]
	[TypeConverter("NinjaTrader.NinjaScript.Indicators.RelativeMonthlyVwapTypeConverter")]
	public class RelativeMonthlyVwap : Indicator
	{
		private DateTime						sessionDateTmp				= Globals.MinDate;
		private DateTime						cacheMonthlyEndDate			= Globals.MinDate;
		private TimeSpan						customSessionStart			= new TimeSpan(8,30,0);
		private TimeSpan						customSessionEnd			= new TimeSpan(15,15,0);
		private double							multiplierSD1				= 1.0;
		private double							multiplierSD2				= 2.0;
		private double							multiplierSD3				= 3.0;
		private double							multiplierQR1				= 1.0;
		private double							multiplierQR2				= 2.0;
		private double							multiplierQR3				= 3.0;
		private double							multiplier1					= 1.0;
		private double							multiplier2					= 2.0;
		private double							multiplier3					= 3.0;
		private double							open						= 0.0;
		private double							high						= 0.0;
		private double							low							= 0.0;
		private double							close						= 0.0;
		private double							mean						= 0.0;
		private double							mean1						= 0.0;
		private double							mean2						= 0.0;
		private	double							volSum						= 0.0;
		private	double							squareSum					= 0.0;
		private	double							priorSessionHigh			= 0.0;
		private	double							priorSessionLow				= 0.0;
		private double							priorVWAP					= 0.0;
		private double							sessionVWAP					= 0.0;
		private int								displacement				= 0;
		private int								count						= 0;
		private bool							showBands					= true;
		private bool							plotVWAP					= false;
		private bool							gap0						= true;
		private bool							gap1						= true;
		private bool							timeBased					= true;
		private bool							breakAtEOD					= true;
		private bool							calculateFromPriceData		= true;
		private bool							applyTradingHours			= false;
		private bool							anchorBar					= false;
		private bool							basicError					= false;
		private bool							errorMessage				= false;
		private bool							sundaySessionError			= false;
		private bool							startEndTimeError			= false;
		private amaSessionTypeVWAPM				sessionType					= amaSessionTypeVWAPM.Full_Session;
		private amaTimeZonesVWAPM				customTZSelector			= amaTimeZonesVWAPM.Exchange_Time;
		private amaBandTypeVWAPM				bandType					= amaBandTypeVWAPM.Standard_Deviation;
		private readonly List<int>				newSessionBarIdxArr			= new List<int>();
		private SessionIterator					sessionIterator				= null;
		private System.Windows.Media.Brush		upBrush						= Brushes.RoyalBlue;
		private System.Windows.Media.Brush  	downBrush					= Brushes.RoyalBlue;
		private System.Windows.Media.Brush		innerBandBrush				= Brushes.RoyalBlue;
		private System.Windows.Media.Brush  	middleBandBrush				= Brushes.RoyalBlue;
		private System.Windows.Media.Brush		outerBandBrush				= Brushes.RoyalBlue;
		private System.Windows.Media.Brush		innerAreaBrush 				= null;
		private System.Windows.Media.Brush		middleAreaBrush 			= null;
		private System.Windows.Media.Brush		outerAreaBrush 				= null;
		private System.Windows.Media.Brush		errorBrush					= null;
		private SimpleFont						errorFont					= null;
		private string							errorText1					= "The RelativeMonthlyVwap only works on price data.";
		private string							errorText2					= "The RelativeMonthlyVwap can only be displayed on intraday charts.";
		private string							errorText3					= "The RelativeMonthlyVwap cannot be used with a negative displacement.";
		private string							errorText4					= "The RelativeMonthlyVwap cannot be used with a displacement on non-equidistant chart bars.";
		private string							errorText5					= "The RelativeMonthlyVwap cannot be used when the Break EOD data series property is unselected.";
		private string							errorText6					= "RelativeMonthlyVwap: The VWAP may not be calculated from fractional Sunday sessions. Please change your trading hours template.";
		private string							errorText7					= "RelativeMonthlyVwap: Mismatch between trading hours selected for the VWAP and the session template selected for the chart bars!";
		private int								innerAreaOpacity			= 60;
		private int								middleAreaOpacity			= 0;
		private int								outerAreaOpacity			= 60;
		private int								plot0Width					= 3;
		private int								plot1Width					= 1;
		private PlotStyle						plot0Style					= PlotStyle.Line;
		private DashStyleHelper					dash0Style					= DashStyleHelper.DashDot;
		private PlotStyle						plot1Style					= PlotStyle.Line;
		private DashStyleHelper					dash1Style					= DashStyleHelper.Solid;
		private TimeZoneInfo					globalTimeZone				= Core.Globals.GeneralOptions.TimeZoneInfo;
		private TimeZoneInfo					customTimeZone;
		private string							versionString				= "v 2.0  -  August 11, 2017";
		private Series<DateTime>				tradingDate;
		private Series<DateTime>				tradingMonth;
		private Series<DateTime>				sessionBegin;
		private Series<DateTime>				anchorTime;
		private Series<DateTime>				cutoffTime;
		private Series<bool>					isFirstDayOfPeriod;
		private Series<bool>					calcOpen;
		private Series<bool>					initMonthlyPlot;
		private Series<int>						sessionBar;
		private Series<double>					firstBarOpen;
		private Series<double>					currentVolSum;
		private Series<double>					currentVWAP;
		private Series<double>					currentSquareSum;
		private Series<double>					sessionHigh;
		private Series<double>					sessionLow;
		private Series<double>					offset;
		private class SessionZone
		{
			public DateTime StartTime;
			public DateTime EndTime;
			public int CreationBar;
			public double UpperY;
			public double MidY;
			public double LowerY;
			public string Tag;
			public bool IsActive;
			public bool IsBreached;
			public DateTime BreachTime;
		}
		private List<SessionZone> activeZones = new List<SessionZone>();
		private List<SharpDX.RectangleF> zoneLabelObstacles = new List<SharpDX.RectangleF>();
		private bool showSessionZones = true;
		private int zoneCutoffPercentage = 50;
		private System.Windows.Media.Brush sessionZoneBrush = Brushes.Gray;
		private int sessionZoneOpacity = 40;
		private System.Windows.Media.Brush zoneLineBrush = Brushes.Gray;
		private int zoneLineWidth = 1;
		private System.Windows.Media.Brush zoneTextBrush = Brushes.White;
		private int zoneTextSize = 12;
		private string zoneLabelUpper = "pMDVAH";
		private string zoneLabelLower = "pMDVAL";
		private System.Windows.Media.Brush zoneTextBackgroundBrush = Brushes.Black;
		private int zoneTextBackgroundOpacity = 0;
		private bool publishGlobalZones = false;
		private bool showGlobalZoneBackground = true;
		
		// Current Month Labels
		private string currentMonthLabelUpper = "mDVAH";
		private string currentMonthLabelLower = "mDVAL";
		private System.Windows.Media.Brush currentMonthLabelColor = Brushes.White;
		private int currentMonthLabelSize = 12;
		private int currentMonthLabelOffset = 15;
		private bool showCurrentMonthLabelsGlobally = false;
		private int globalLabelPadding = 25;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "\r\nThe Current Month VWAP is the volume weighted average price of the current month. The indicator further displays three upper and lower volatility bands.";
				Name						= "RelativeMonthlyVwap";
				IsSuspendedWhileInactive	= true;
				IsOverlay					= true;
				Calculate					= Calculate.OnPriceChange;
				IsAutoScale					= false;
				ArePlotsConfigurable		= false;
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Session VWAP");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 3");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 2");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 1");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 1");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 2");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 3");	
			}
			else if (State == State.Configure)
			{
				displacement = Displacement;
				Plots[1].Brush = outerBandBrush.Clone();
				Plots[2].Brush = middleBandBrush.Clone();
				Plots[3].Brush = innerBandBrush.Clone();
				Plots[4].Brush = innerBandBrush.Clone();
				Plots[5].Brush = middleBandBrush.Clone();
				Plots[6].Brush = outerBandBrush.Clone();
				Plots[0].Width = plot0Width;
				Plots[0].PlotStyle = plot0Style;
				Plots[0].DashStyleHelper = dash0Style;			
				Plots[1].Width = plot1Width;
				Plots[1].PlotStyle = plot1Style;
				Plots[1].DashStyleHelper = dash1Style;
				Plots[2].Width = plot1Width;
				Plots[2].PlotStyle = plot1Style;
				Plots[2].DashStyleHelper = dash1Style;
				Plots[3].Width = plot1Width;
				Plots[3].PlotStyle = plot1Style;
				Plots[3].DashStyleHelper = dash1Style;
				Plots[4].Width = plot1Width;
				Plots[4].PlotStyle = plot1Style;
				Plots[4].DashStyleHelper = dash1Style;
				Plots[5].Width = plot1Width;
				Plots[5].PlotStyle = plot1Style;
				Plots[5].DashStyleHelper = dash1Style;
				Plots[6].Width = plot1Width;
				Plots[6].PlotStyle = plot1Style;
				Plots[6].DashStyleHelper = dash1Style;
				upBrush.Freeze();	
				downBrush.Freeze();
				innerAreaBrush	= innerBandBrush.Clone();
				innerAreaBrush.Opacity = (float) innerAreaOpacity/100.0;
				innerAreaBrush.Freeze();
				middleAreaBrush	= middleBandBrush.Clone();
				middleAreaBrush.Opacity = (float) middleAreaOpacity/100.0;
				middleAreaBrush.Freeze();
				outerAreaBrush	= outerBandBrush.Clone();
				outerAreaBrush.Opacity = (float) outerAreaOpacity/100.0;
				outerAreaBrush.Freeze();
				if (sessionZoneBrush != null) sessionZoneBrush.Freeze();
				if (zoneLineBrush != null) zoneLineBrush.Freeze();
				if (zoneTextBrush != null) zoneTextBrush.Freeze();
				if (zoneTextBackgroundBrush != null) zoneTextBackgroundBrush.Freeze();
			}
		  	else if (State == State.DataLoaded)
		 	{
				tradingDate = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				tradingMonth = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionBegin = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				anchorTime = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				cutoffTime = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				isFirstDayOfPeriod = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				calcOpen = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				initMonthlyPlot = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionBar = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				firstBarOpen = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentVolSum = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentVWAP = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentSquareSum = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionHigh = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionLow = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				offset = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				if (Bars.BarsType.IsTimeBased) 
					timeBased = true;
				else
					timeBased = false;
				if(Input is PriceSeries)
					calculateFromPriceData = true;
				else
					calculateFromPriceData = false;
		    	sessionIterator = new SessionIterator(Bars);
		  	}
			else if (State == State.Historical)
			{
				// v1.1.0: Limpiar objetos globales residuales del indicador original amaCurrentMonthVWAP
				CleanupLegacyGlobalObjects();

				if (sessionType == amaSessionTypeVWAPM.Full_Session)
					applyTradingHours = false;
				else if (sessionType == amaSessionTypeVWAPM.Custom_Hours) 
					applyTradingHours = true;
				if(bandType == amaBandTypeVWAPM.Standard_Deviation)
				{
					multiplier1 = multiplierSD1;
					multiplier2 = multiplierSD2;
					multiplier3 = multiplierSD3;
					showBands = true;
				}
				else if(bandType == amaBandTypeVWAPM.Quarter_Range)
				{
					multiplier1 = multiplierQR1;
					multiplier2 = multiplierQR2;
					multiplier3 = multiplierQR3;
					showBands = true;
				}
				else if(bandType == amaBandTypeVWAPM.None)
					showBands = false;
				switch (customTZSelector)
				{
					case amaTimeZonesVWAPM.Exchange_Time:	
						customTimeZone = Instrument.MasterInstrument.TradingHours.TimeZoneInfo;
						break;
					case amaTimeZonesVWAPM.Chart_Time:	
						customTimeZone = Core.Globals.GeneralOptions.TimeZoneInfo;
						break;
					case amaTimeZonesVWAPM.US_Eastern_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");	
						break;
					case amaTimeZonesVWAPM.US_Central_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");	
						break;
					case amaTimeZonesVWAPM.US_Mountain_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");	
						break;
					case amaTimeZonesVWAPM.US_Pacific_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");	
						break;
					case amaTimeZonesVWAPM.AUS_Eastern_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");	
						break;
					case amaTimeZonesVWAPM.Japan_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");	
						break;
					case amaTimeZonesVWAPM.China_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");	
						break;
					case amaTimeZonesVWAPM.India_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");	
						break;
					case amaTimeZonesVWAPM.Central_European_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");	
						break;
					case amaTimeZonesVWAPM.GMT_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");	
						break;
				}					
				gap0 = (plot0Style == PlotStyle.Line || plot0Style == PlotStyle.Square);
				gap1 = (plot1Style == PlotStyle.Line || plot1Style == PlotStyle.Square);
				if(ChartBars != null)
				{	
					breakAtEOD = ChartBars.Bars.IsResetOnNewTradingDay;
					errorBrush = ChartControl.Properties.AxisPen.Brush;
					errorBrush.Freeze();
					errorFont = new SimpleFont("Arial", 24);
				}
				this.ZOrder = -1; //SetZOrder(-1);
				basicError = false;
				errorMessage = false;
				if(!calculateFromPriceData)
				{
					Draw.TextFixed(this, "error text 1", errorText1, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}	
				else if (!Bars.BarsType.IsIntraday)
				{
					Draw.TextFixed(this, "error text 2", errorText2, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				else if(displacement < 0)
				{
					Draw.TextFixed(this, "error text 3", errorText3, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				else if (ChartBars != null && (ChartControl.BarSpacingType == BarSpacingType.TimeBased || ChartControl.BarSpacingType == BarSpacingType.EquidistantMulti) && displacement != 0)
				{
					Draw.TextFixed(this, "error text 4", errorText4, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}	
				else if(!breakAtEOD)
				{
					Draw.TextFixed(this, "error text 5", errorText5, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				sundaySessionError = false;
				startEndTimeError = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if(IsFirstTickOfBar)
			{	
				if(errorMessage)
				{	
					if(basicError)
						return;
					else if(sundaySessionError)
					{	
						Draw.TextFixed(this, "error text 6", errorText6, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);
						RemoveDrawObject("error text 7");
						return;
					}	
					else if(startEndTimeError)
					{	
						Draw.TextFixed(this, "error text 7", errorText7, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);
						return;
					}
				}	
			}
			
			if (CurrentBar == 0)
			{	
				if(IsFirstTickOfBar)
				{	
					tradingDate[0] = GetLastBarSessionDateD(Time[0]);
					tradingMonth[0] = GetLastBarSessionDateM(Time[0]);
					sessionBegin[0] = sessionIterator.ActualSessionBegin;
					if(applyTradingHours)
					{	
						anchorTime[0] = Globals.MinDate;
						cutoffTime[0] = Globals.MinDate;
					}	
					isFirstDayOfPeriod[0] = false;
					calcOpen[0] = false;
					initMonthlyPlot[0] = false;
					firstBarOpen[0] = Open[0];
					anchorBar = false;
					sessionBar.Reset();
					currentVolSum.Reset();
					currentVWAP.Reset();
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
					SessionVWAP.Reset();
					UpperBand3.Reset();
					UpperBand2.Reset();
					UpperBand1.Reset();
					LowerBand1.Reset();
					LowerBand2.Reset();
					LowerBand3.Reset();
					activeZones.Clear();
				}
				return;
			}
			if(IsFirstTickOfBar)
			{	
				if(Bars.IsFirstBarOfSession)
				{	
					tradingDate[0] = GetLastBarSessionDateD(Time[0]); // GetLastBarSessionDateD must be calculated prior to GetLastBarSessionDateM
					if(tradingDate[0].DayOfWeek == DayOfWeek.Sunday)
					{
						sundaySessionError = true; 
						errorMessage = true;
						return;
					}
					sessionBegin[0] = sessionIterator.ActualSessionBegin;
					tradingMonth[0] = GetLastBarSessionDateM(Time[0]);
					if(tradingMonth[0] != tradingMonth[1])
					{
						// Freeze breached zones: marcar inactivas con EndTime = barra anterior
						for (int i = activeZones.Count - 1; i >= 0; i--)
						{
							if (activeZones[i].IsBreached && activeZones[i].IsActive)
							{
								activeZones[i].IsActive = false;
								activeZones[i].EndTime = Time[1];
							}
						}

						if (showSessionZones && bandType == amaBandTypeVWAPM.Standard_Deviation)
						{
							double up1 = UpperBand1[1];
							double low1 = LowerBand1[1];
							double mid1 = Values[0].IsValidDataPointAt(CurrentBar - 1) ? SessionVWAP[1] : (up1 + low1) / 2.0;
							if (up1 > 0 && low1 > 0 && up1 > low1)
							{
								string tag = "Monthly_SessionZone_" + Time[1].Ticks;
								SessionZone zone = new SessionZone
								{
									StartTime = Time[1],
									UpperY = up1,
									MidY = mid1,
									LowerY = low1,
									Tag = tag,
									IsActive = true,
									IsBreached = false,
									CreationBar = CurrentBar
								};
								activeZones.Add(zone);
								// v1.1.0: Draw eliminado — OnRender dibuja con SharpDX
							}
						}

						isFirstDayOfPeriod[0] = true;
						calcOpen[0] = false;
						initMonthlyPlot[0] = false;
						firstBarOpen[0] = Open[0];
					}
					else if(tradingDate[0] != tradingDate[1])
					{
						isFirstDayOfPeriod[0] = false;
						if(applyTradingHours)
							calcOpen[0] = false;
						else
							calcOpen[0] = calcOpen[1];
						initMonthlyPlot[0] = initMonthlyPlot[1];
						firstBarOpen[0] = firstBarOpen[1];	
					}
					else
					{	
						isFirstDayOfPeriod[0] = isFirstDayOfPeriod[1];
						calcOpen[0] = calcOpen[1];
						initMonthlyPlot[0] = initMonthlyPlot[1];
						firstBarOpen[0] = firstBarOpen[1];	
					}	
					if(tradingDate[0] != tradingDate[1])
					{
						if(applyTradingHours)
						{	
							anchorTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionStart), customTimeZone, globalTimeZone);
							if(anchorTime[0] >= sessionBegin[0].AddHours(24))
								anchorTime[0] = anchorTime[0].AddHours(-24);
							else if(anchorTime[0] < sessionBegin[0])
								anchorTime[0] = anchorTime[0].AddHours(24);
							cutoffTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionEnd), customTimeZone, globalTimeZone);
							if(cutoffTime[0] > sessionBegin[0].AddHours(24))
								cutoffTime[0] = cutoffTime[0].AddHours(-24);
							else if(cutoffTime[0] <= sessionBegin[0])
								cutoffTime[0] = cutoffTime[0].AddHours(24);
							if(cutoffTime[0] <= anchorTime[0])
							{
								startEndTimeError = true;
								errorMessage = true;
								return;
							}
						}	
					}	
					else
					{	
						if(applyTradingHours)
						{	
							anchorTime[0] = anchorTime[1];
							cutoffTime[0] = cutoffTime[1];
						}	
					}	
				}					
				else
				{	
					tradingDate[0] = tradingDate[1];
					tradingMonth[0] = tradingMonth[1];
					sessionBegin[0] = sessionBegin[1];
					isFirstDayOfPeriod[0] = isFirstDayOfPeriod[1];
					calcOpen[0] = calcOpen[1];
					initMonthlyPlot[0] = initMonthlyPlot[1];
					firstBarOpen[0] = firstBarOpen[1];	
					if(applyTradingHours)
					{	
						anchorTime[0] = anchorTime[1];
						cutoffTime[0] = cutoffTime[1];
					}	
				}	
			}	
			if(applyTradingHours) 
			{
				if(timeBased && Time[0] > anchorTime[0] && Time[1] <= anchorTime[0])
					anchorBar = true;
				else if(!timeBased && Time[0] >= anchorTime[0] && Time[1] < anchorTime[0])
					anchorBar = true;
				else
					anchorBar = false;
				if(timeBased && Time[0] > cutoffTime[0] && Time[1] <= cutoffTime[0])
					calcOpen[0] = false;
				else if(!timeBased && Time[0] >= cutoffTime[0] && Time[1] < cutoffTime[0])
					calcOpen[0] = false;
			}
			
			if ((!applyTradingHours && tradingMonth[0] != tradingMonth[1]) || (applyTradingHours && isFirstDayOfPeriod[0] && anchorBar))
			{
				if(IsFirstTickOfBar || !calcOpen[0])
				{	
					initMonthlyPlot[0] 	= true;
					sessionBar[0]		= 1;
				}	
				open				= Open[0] - firstBarOpen[0];
				high				= High[0] - firstBarOpen[0];
				low 				= Low[0] - firstBarOpen[0];
				close				= Close[0] - firstBarOpen[0];
				mean1				= 0.5*(high + low);
				mean2				= 0.5*(open + close);
				mean				= 0.5*(mean1 + mean2);
				currentVolSum[0] 	= Volume[0];
				currentVWAP[0]		= mean;
				if(bandType == amaBandTypeVWAPM.Standard_Deviation)
				{	
					currentSquareSum[0] = Volume[0]*(open*open + high*high + low*low + close*close + 2*mean2*mean2 + 2*mean1*mean1)/8.0;
					offset[0]			= (currentVolSum[0] > 0.5) ? Math.Sqrt(currentSquareSum[0]/currentVolSum[0] - currentVWAP[0]*currentVWAP[0]) : 0;
				}
				else if(bandType == amaBandTypeVWAPM.Quarter_Range)
				{	
					sessionHigh[0]	= High[0];
					sessionLow[0]	= Low[0];
					offset[0]		= 0.25*(sessionHigh[0] - sessionLow[0]);
				}
				else
				{
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
				calcOpen[0] = true;
				plotVWAP = true;
			}
			else if (applyTradingHours && anchorBar)
			{
				if(!calcOpen[0])
				{	
					sessionBar[0] 	= sessionBar[1] + 1;
					volSum			= currentVolSum[1];
					priorVWAP		= currentVWAP[1];
				}	
				open				= Open[0] - firstBarOpen[0];
				high				= High[0] - firstBarOpen[0];
				low 				= Low[0] - firstBarOpen[0];
				close				= Close[0] - firstBarOpen[0];
				mean1				= 0.5*(high + low);
				mean2				= 0.5*(open + close);
				mean				= 0.5*(mean1 + mean2);
				currentVolSum[0]	= volSum + Volume[0];
				currentVWAP[0]		= (currentVolSum[0] > 0.5 ) ? (volSum*priorVWAP + Volume[0]*mean)/currentVolSum[0] : mean;
				if(bandType == amaBandTypeVWAPM.Standard_Deviation)
				{	
					if(!calcOpen[0])
						squareSum 		= currentSquareSum[1];
					currentSquareSum[0] = squareSum + Volume[0]*(open*open + high*high + low*low + close*close + 2*mean2*mean2 + 2*mean1*mean1)/8.0;
					offset[0]			= (currentVolSum[0] > 0.5) ? Math.Sqrt(currentSquareSum[0]/currentVolSum[0] - currentVWAP[0]*currentVWAP[0]) : 0;
				}	
				else if(bandType == amaBandTypeVWAPM.Quarter_Range)
				{
					if(!calcOpen[0])
					{
						priorSessionHigh = sessionHigh[1];
						priorSessionLow	= sessionLow[1];
					}
					sessionHigh[0]		= Math.Max(priorSessionHigh, High[0]);
					sessionLow[0]		= Math.Min(priorSessionLow, Low[0]);
					offset[0]			= 0.25*(sessionHigh[0] - sessionLow[0]);
				}
				else
				{
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
				calcOpen[0] = true;
			}
			else if (calcOpen[0])
			{
				if (IsFirstTickOfBar)
				{
					sessionBar[0] 	= sessionBar[1] + 1;
					volSum			= currentVolSum[1];
					priorVWAP		= currentVWAP[1];
				}
				open				= Open[0] - firstBarOpen[0];
				high				= High[0] - firstBarOpen[0];
				low 				= Low[0] - firstBarOpen[0];
				close				= Close[0] - firstBarOpen[0];
				mean1				= 0.5*(high + low);
				mean2				= 0.5*(open + close);
				mean				= 0.5*(mean1 + mean2);
				currentVolSum[0]	= volSum + Volume[0];
				currentVWAP[0]		= (currentVolSum[0] > 0.5 ) ? (volSum*priorVWAP + Volume[0]*mean)/currentVolSum[0] : mean;
				if(bandType == amaBandTypeVWAPM.Standard_Deviation)
				{	
					if(IsFirstTickOfBar)
						squareSum 		= currentSquareSum[1];
					currentSquareSum[0] = squareSum + Volume[0]*(open*open + high*high + low*low + close*close + 2*mean2*mean2 + 2*mean1*mean1)/8.0;
					offset[0]			= (currentVolSum[0] > 0.5) ? Math.Sqrt(currentSquareSum[0]/currentVolSum[0] - currentVWAP[0]*currentVWAP[0]) : 0;
				}	
				else if(bandType == amaBandTypeVWAPM.Quarter_Range)
				{
					if(IsFirstTickOfBar)
					{
						priorSessionHigh = sessionHigh[1];
						priorSessionLow	= sessionLow[1];
					}
					sessionHigh[0]		= Math.Max(priorSessionHigh, High[0]);
					sessionLow[0]		= Math.Min(priorSessionLow, Low[0]);
					offset[0]			= 0.25*(sessionHigh[0] - sessionLow[0]);
				}
				else
				{
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
			}
			else 
			{	
				if(initMonthlyPlot[0])
				{	
					if(IsFirstTickOfBar)
						sessionBar[0] = sessionBar[1] + 1;
					currentVolSum[0] = currentVolSum[1];
					currentVWAP[0] = currentVWAP[1];
					if(bandType == amaBandTypeVWAPM.Standard_Deviation)
					{	
						currentSquareSum[0] = currentSquareSum[1];
						offset[0] = offset[1];	
					}	
					else if(bandType == amaBandTypeVWAPM.Quarter_Range)	
					{	
						sessionHigh[0]	= sessionHigh[1];
						sessionLow[0]	= sessionLow[1];
						offset[0] = offset[1];
					}
					else
					{
						currentSquareSum.Reset();
						sessionHigh.Reset();
						sessionLow.Reset();
						offset.Reset();
					}	
				}	
				else if (IsFirstTickOfBar)
				{		
					sessionBar.Reset();
					currentVolSum.Reset();
					currentVWAP.Reset();
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
			}	

			if (plotVWAP && initMonthlyPlot[0])
			{
				sessionVWAP = currentVWAP[0] + firstBarOpen[0];
				SessionVWAP[0] = sessionVWAP;
				if (bandType == amaBandTypeVWAPM.None)
				{
					UpperBand3.Reset();
					UpperBand2.Reset();
					UpperBand1.Reset();
					LowerBand1.Reset();
					LowerBand2.Reset();
					LowerBand3.Reset();
				}	
				else
				{
					UpperBand3[0] = sessionVWAP + multiplier3 * offset[0];
					UpperBand2[0] = sessionVWAP + multiplier2 * offset[0];
					UpperBand1[0] = sessionVWAP + multiplier1 * offset[0];
					LowerBand1[0] = sessionVWAP - multiplier1 * offset[0];
					LowerBand2[0] = sessionVWAP - multiplier2 * offset[0];
					LowerBand3[0] = sessionVWAP - multiplier3 * offset[0];
				}
				
				if (sessionBar[0] == 1 && gap0)
					PlotBrushes[0][0] = Brushes.Transparent;
				else if (SessionVWAP[0] > SessionVWAP[1])
					PlotBrushes[0][0] = upBrush;
				else if (SessionVWAP[0] < SessionVWAP[1])
					PlotBrushes[0][0] = downBrush;
				else if(sessionBar[0] == 2 && gap0)
					PlotBrushes[0][0] = upBrush;
				else
					PlotBrushes[0][0] = PlotBrushes[0][1];
				if(sessionBar[0] == 1 && gap1)
				{
					for (int i = 1; i <= 6; i++)
						PlotBrushes[i][0] = Brushes.Transparent;
				}
			}
			else
			{
				SessionVWAP.Reset();
				UpperBand3.Reset();
				UpperBand2.Reset();
				UpperBand1.Reset();
				LowerBand1.Reset();
				LowerBand2.Reset();
				LowerBand3.Reset();
			}

			// Update and draw active zones
			if (showSessionZones && activeZones.Count > 0)
			{
				bool isLastBar = (CurrentBar == Bars.Count - 1);
				for (int i = activeZones.Count - 1; i >= 0; i--)
				{
					SessionZone zone = activeZones[i];
					if (zone.IsActive)
					{
						double range = zone.UpperY - zone.LowerY;
						double cutoffY = zone.LowerY + (range * (zoneCutoffPercentage / 100.0));

						// Breach: barra cruza (straddle) el cutoff line
						if (!zone.IsBreached && CurrentBar > zone.CreationBar
							&& High[0] >= cutoffY && Low[0] <= cutoffY)
						{
							zone.IsBreached = true;
							zone.BreachTime = Time[0];
						}

						// Safety: si la zona fue breached en un mes anterior y sigue activa, congelarla
						if (zone.IsBreached && zone.BreachTime != DateTime.MinValue)
						{
							DateTime breachMonth = new DateTime(zone.BreachTime.Year, zone.BreachTime.Month, 1);
							DateTime currentMonth = new DateTime(Time[0].Year, Time[0].Month, 1);
							if (currentMonth > breachMonth)
							{
								zone.IsActive = false;
								zone.EndTime = zone.BreachTime;
							}
						}
					}
				}
			}

			// v1.1.0: Global labels y DrawGlobalZone eliminados — todo via SharpDX en OnRender
			// Export niveles a archivo para indicador lector (Realtime + última barra histórica)
			if (State == State.Realtime || CurrentBar >= Bars.Count - 2)
				ExportLevels();

			// --- RelativeMCP observability ---
			// Publish siempre en Realtime (Registry refleja último tick).
			// RLog solo en IsFirstTickOfBar para no saturar el buffer.
			if (State == State.Realtime)
			{
				try
				{
					string indName = typeof(RelativeMonthlyVwap).Name;
					double vwap = Values[0][0];
					double uSD1 = Values[3][0], uSD3 = Values[1][0];
					double lSD1 = Values[4][0], lSD3 = Values[6][0];
					int zonesActive = 0;
					if (activeZones != null)
						foreach (var z in activeZones) if (z.IsActive) zonesActive++;

					RelativeIndicatorRegistry.Publish(
						string.Format("{0}:{1}:{2}{3}", indName, Instrument.FullName,
							BarsPeriod.Value, BarsPeriod.BarsPeriodType),
						new Dictionary<string, object>
						{
							["bar"] = CurrentBar,
							["bar_time"] = Time[0],
							["close"] = Close[0],
							["vwap"] = vwap,
							["dvah_sd1"] = uSD1, ["dvah_sd3"] = uSD3,
							["dval_sd1"] = lSD1, ["dval_sd3"] = lSD3,
							["active_zones"] = zonesActive,
						});

					if (IsFirstTickOfBar)
						this.RLog("bar={0} close={1:F2} vwap={2:F2} dvah={3:F2} dval={4:F2} zones={5}",
							CurrentBar, Close[0], vwap, uSD1, lSD1, zonesActive);
				}
				catch { }
			}
			// --- end RelativeMCP ---
		}

		// v1.1.0: Limpiar objetos Draw globales del indicador original
		private void CleanupLegacyGlobalObjects()
		{
			if (DrawObjects == null) return;
			var toRemove = new List<string>();
			foreach (var dObj in DrawObjects)
			{
				if (dObj.Tag != null && (dObj.Tag.Contains("_Global") || dObj.Tag == "CurrentMonthLabelUp_Global" || dObj.Tag == "CurrentMonthLabelLow_Global"))
					toRemove.Add(dObj.Tag);
			}
			foreach (var tag in toRemove)
				RemoveDrawObject(tag);
		}

		// v1.1.0: Export de niveles a archivo compartido
		private DateTime _lastExportTime = DateTime.MinValue;
		private void ExportLevels()
		{
			if ((DateTime.Now - _lastExportTime).TotalSeconds < 5) return;
			try
			{
				string dir = System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"NinjaTrader 8", "bin", "Custom", "VwapLevels");
				if (!System.IO.Directory.Exists(dir))
					System.IO.Directory.CreateDirectory(dir);
				string file = System.IO.Path.Combine(dir, "Monthly_" + Instrument.MasterInstrument.Name + ".txt");

				double dvah = (Values[3].Count > 0 && Values[3].IsValidDataPointAt(CurrentBar)) ? UpperBand1[0] : 0;
				double pva  = (Values[0].Count > 0 && Values[0].IsValidDataPointAt(CurrentBar)) ? SessionVWAP[0] : 0;
				double dval = (Values[4].Count > 0 && Values[4].IsValidDataPointAt(CurrentBar)) ? LowerBand1[0] : 0;

				var sb = new System.Text.StringBuilder();
				sb.AppendLine("INSTRUMENT=" + Instrument.MasterInstrument.Name);
				sb.AppendLine("TIMEFRAME=Monthly");
				sb.AppendLine("TIMESTAMP=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				sb.AppendLine("DVAH=" + dvah.ToString(System.Globalization.CultureInfo.InvariantCulture));
				sb.AppendLine("VWAP=" + pva.ToString(System.Globalization.CultureInfo.InvariantCulture));
				sb.AppendLine("DVAL=" + dval.ToString(System.Globalization.CultureInfo.InvariantCulture));
				var zoneSb = new System.Text.StringBuilder();
				int zIdx = 0;
				for (int i = 0; i < activeZones.Count; i++)
				{
					if (activeZones[i].IsBreached) continue;
					var z = activeZones[i];
					zoneSb.AppendLine("ZONE_" + zIdx + "="
						+ z.UpperY.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
						+ z.MidY.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
						+ z.LowerY.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
						+ z.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
					zIdx++;
				}
				sb.AppendLine("ZONE_COUNT=" + zIdx);
				sb.Append(zoneSb);
				System.IO.File.WriteAllText(file, sb.ToString());
				_lastExportTime = DateTime.Now;
			}
			catch (Exception ex)
			{
				Print("RelativeMonthlyVwap ExportLevels ERROR: " + ex.Message);
			}
		}
		
		#region Properties
		public Series<double> SessionVWAP
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand3
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand2
		{
			get { return Values[2]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand1
		{
			get { return Values[3]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand1
		{
			get { return Values[4]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand2
		{
			get { return Values[5]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand3
		{
			get { return Values[6]; }
		}
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Select session", Description = "Select session - ETH, Custom or Trading Month - for calculating the VWAP", GroupName = "Algorithmic Options", Order = 0)]
		[RefreshProperties(RefreshProperties.All)] 
		public amaSessionTypeVWAPM SessionType
		{	
            get { return sessionType; }
            set { sessionType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Band type", Description = "Select formula for calculating volatility bands", GroupName = "Algorithmic Options", Order = 1)]
 		[RefreshProperties(RefreshProperties.All)] 
		public amaBandTypeVWAPM BandType
		{	
            get { return bandType; }
            set { bandType = value; }
		}
			
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Select time zone", Description="Enter time zone for custom session", GroupName="Custom Hours", Order = 0)]
		public amaTimeZonesVWAPM CustomTZSelector
		{
			get
			{
				return customTZSelector;
			}
			set
			{
				customTZSelector = value;
			}
		}
			
		[Browsable(false)]
		[XmlIgnore]
		public TimeSpan CustomSessionStart
		{
			get { return customSessionStart;}
			set { customSessionStart = value;}
		}	
	
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Custom start time (+ h:min)", Description="Enter start time for VWAP calculation in time zone of exchange", GroupName="Custom Hours", Order = 1)]
		public string S_CustomSessionStart	
		{
			get 
			{ 
				return string.Format("{0:D2}:{1:D2}", customSessionStart.Hours, customSessionStart.Minutes);
			}
			set 
			{ 
				char[] delimiters = new char[] {':'};
				string[]values =((string)value).Split(delimiters, StringSplitOptions.None);
				customSessionStart = new TimeSpan(Convert.ToInt16(values[0]),Convert.ToInt16(values[1]),0);
			}
		}
	
		[Browsable(false)]
		[XmlIgnore]
		public TimeSpan CustomSessionEnd
		{
			get { return customSessionEnd;}
			set { customSessionEnd = value;}
		}	
	
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Custom end time (+ h:min)", Description="Enter end time for VWAP calculation in time zone of exchange", GroupName="Custom Hours", Order = 2)]
		public string S_CustomSessionEnd	
		{
			get 
			{ 
				return string.Format("{0:D2}:{1:D2}", customSessionEnd.Hours, customSessionEnd.Minutes);
			}
			set 
			{ 
				char[] delimiters = new char[] {':'};
				string[]values =((string)value).Split(delimiters, StringSplitOptions.None);
				customSessionEnd = new TimeSpan(Convert.ToInt16(values[0]),Convert.ToInt16(values[1]),0);
			}
		}
	
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 1", Description = "Select multiplier for inner standard deviation bands", GroupName = "Standard Deviation Bands", Order = 0)]
		public double MultiplierSD1 
		{
			get { return multiplierSD1; }
			set { multiplierSD1 = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 2", Description = "Select multiplier for central standard deviation bands", GroupName = "Standard Deviation Bands", Order = 1)]
		public double MultiplierSD2
		{
			get { return multiplierSD2; }
			set { multiplierSD2 = value; }
		}
		
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 3", Description = "Select multiplier for outer standard deviation bands", GroupName = "Standard Deviation Bands", Order = 2)]
		public double MultiplierSD3
		{
			get { return multiplierSD3; }
			set { multiplierSD3 = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 1", Description = "Select multiplier for inner quarter range bands", GroupName = "Quarter Range Bands", Order = 0)]
		public double MultiplierQR1
		{
			get { return multiplierQR1; }
			set { multiplierQR1 = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 2", Description = "Select multiplier for central quarter range bands", GroupName = "Quarter Range Bands", Order = 1)]
		public double MultiplierQR2
		{
			get { return multiplierQR2; }
			set { multiplierQR2 = value; }
		}
		
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 3", Description = "Select multiplier for outer quarter range bands", GroupName = "Quarter Range Bands", Order = 2)]
		public double MultiplierQR3
		{
			get { return multiplierQR3; }
			set { multiplierQR3 = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Rising VWAP", Description = "Sets the color for a bullish VWAP", GroupName = "Plot Colors", Order = 0)]
		public System.Windows.Media.Brush UpBrush
		{ 
			get {return upBrush;}
			set {upBrush = value;}
		}

		[Browsable(false)]
		public string UpBrushSerializable
		{
			get { return Serialize.BrushToString(upBrush); }
			set { upBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Falling VWAP", Description = "Sets the color for a bearish VWAP", GroupName = "Plot Colors", Order = 1)]
		public System.Windows.Media.Brush DownBrush
		{ 
			get {return downBrush;}
			set {downBrush = value;}
		}

		[Browsable(false)]
		public string DownBrushSerializable
		{
			get { return Serialize.BrushToString(downBrush); }
			set { downBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Inner bands", Description = "Sets the color for the inner bands", GroupName = "Plot Colors", Order = 2)]
		public System.Windows.Media.Brush InnerBandBrush
		{ 
			get {return innerBandBrush;}
			set {innerBandBrush = value;}
		}

		[Browsable(false)]
		public string InnerBandBrushSerializable
		{
			get { return Serialize.BrushToString(innerBandBrush); }
			set { innerBandBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Middle bands", Description = "Sets the color for the middle bands", GroupName = "Plot Colors", Order = 3)]
		public System.Windows.Media.Brush MiddleBandBrush
		{ 
			get {return middleBandBrush;}
			set {middleBandBrush = value;}
		}

		[Browsable(false)]
		public string MiddleBandBrushSerializable
		{
			get { return Serialize.BrushToString(middleBandBrush); }
			set { middleBandBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Outer bands", Description = "Sets the color for the outer bands", GroupName = "Plot Colors", Order = 4)]
		public System.Windows.Media.Brush OuterBandBrush
		{ 
			get {return outerBandBrush;}
			set {outerBandBrush = value;}
		}

		[Browsable(false)]
		public string OuterBandBrushSerializable
		{
			get { return Serialize.BrushToString(outerBandBrush); }
			set { outerBandBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style VWAP", Description = "Sets the plot style for the VWAP plot", GroupName = "Plot Parameters", Order = 0)]
		public PlotStyle Plot0Style
		{	
            get { return plot0Style; }
            set { plot0Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style VWAP", Description = "Sets the dash style for the VWAP plot", GroupName = "Plot Parameters", Order = 1)]
		public DashStyleHelper Dash0Style
		{
			get { return dash0Style; }
			set { dash0Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width VWAP", Description = "Sets the plot width for the VWAP plot", GroupName = "Plot Parameters", Order = 2)]
		public int Plot0Width
		{	
            get { return plot0Width; }
            set { plot0Width = value; }
		}
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style SD bands", Description = "Sets the plot style for the volatility bands", GroupName = "Plot Parameters", Order = 3)]
		public PlotStyle Plot1Style
		{	
            get { return plot1Style; }
            set { plot1Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style SD bands", Description = "Sets the dash style for the volatility bands", GroupName = "Plot Parameters", Order = 4)]
		public DashStyleHelper Dash1Style
		{
			get { return dash1Style; }
			set { dash1Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width SD bands", Description = "Sets the plot width for the volatility bands", GroupName = "Plot Parameters", Order = 5)]
		public int Plot1Width
		{	
            get { return plot1Width; }
            set { plot1Width = value; }
		}
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Inner bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "Area Opacity", Order = 0)]
        public int InnerAreaOpacity
        {
            get { return innerAreaOpacity; }
            set { innerAreaOpacity = value; }
        }
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Middle bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "Area Opacity", Order = 1)]
        public int MiddleAreaOpacity
        {
            get { return middleAreaOpacity; }
            set { middleAreaOpacity = value; }
        }
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Outer bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "Area Opacity", Order = 2)]
        public int OuterAreaOpacity
        {
            get { return outerAreaOpacity; }
            set { outerAreaOpacity = value; }
        }
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Release and date", Description = "Release and date", GroupName = "Version", Order = 0)]
		public string VersionString
		{	
            get { return versionString; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Session Zones", Description = "Show historical session zones", GroupName = "Visual", Order = 100)]
		public bool ShowSessionZones
		{
			get { return showSessionZones; }
			set { showSessionZones = value; }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Cutoff %", Description = "Percentage of zone penetration to cut the zone", GroupName = "Visual", Order = 101)]
		public int ZoneCutoffPercentage
		{
			get { return zoneCutoffPercentage; }
			set { zoneCutoffPercentage = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Color", Description = "Color of the session zone", GroupName = "Visual", Order = 102)]
		public System.Windows.Media.Brush SessionZoneBrush
		{
			get { return sessionZoneBrush; }
			set { sessionZoneBrush = value; }
		}

		[Browsable(false)]
		public string SessionZoneBrushSerializable
		{
			get { return Serialize.BrushToString(sessionZoneBrush); }
			set { sessionZoneBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Opacity", Description = "Opacity of the session zone", GroupName = "Visual", Order = 103)]
		public int SessionZoneOpacity
		{
			get { return sessionZoneOpacity; }
			set { sessionZoneOpacity = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Color", Description = "Color of the zone lines", GroupName = "Visual", Order = 104)]
		public System.Windows.Media.Brush ZoneLineBrush
		{
			get { return zoneLineBrush; }
			set { zoneLineBrush = value; }
		}

		[Browsable(false)]
		public string ZoneLineBrushSerializable
		{
			get { return Serialize.BrushToString(zoneLineBrush); }
			set { zoneLineBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Width", Description = "Width of the zone lines", GroupName = "Visual", Order = 105)]
		public int ZoneLineWidth
		{
			get { return zoneLineWidth; }
			set { zoneLineWidth = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Color", Description = "Color of the zone text", GroupName = "Visual", Order = 106)]
		public System.Windows.Media.Brush ZoneTextBrush
		{
			get { return zoneTextBrush; }
			set { zoneTextBrush = value; }
		}

		[Browsable(false)]
		public string ZoneTextBrushSerializable
		{
			get { return Serialize.BrushToString(zoneTextBrush); }
			set { zoneTextBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Size", Description = "Size of the zone text", GroupName = "Visual", Order = 107)]
		public int ZoneTextSize
		{
			get { return zoneTextSize; }
			set { zoneTextSize = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper Label", Description = "Text for the upper line", GroupName = "Visual", Order = 108)]
		public string ZoneLabelUpper
		{
			get { return zoneLabelUpper; }
			set { zoneLabelUpper = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower Label", Description = "Text for the lower line", GroupName = "Visual", Order = 109)]
		public string ZoneLabelLower
		{
			get { return zoneLabelLower; }
			set { zoneLabelLower = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Background", Description = "Color of the text background", GroupName = "Visual", Order = 110)]
		public System.Windows.Media.Brush ZoneTextBackgroundBrush
		{
			get { return zoneTextBackgroundBrush; }
			set { zoneTextBackgroundBrush = value; }
		}

		[Browsable(false)]
		public string ZoneTextBackgroundBrushSerializable
		{
			get { return Serialize.BrushToString(zoneTextBackgroundBrush); }
			set { zoneTextBackgroundBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Opacity", Description = "Opacity of the text background", GroupName = "Visual", Order = 111)]
		public int ZoneTextBackgroundOpacity
		{
			get { return zoneTextBackgroundOpacity; }
			set { zoneTextBackgroundOpacity = value; }
		}

		[Browsable(false)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Publish Global Zones", Description = "Obsoleto v1.1.0 — usar RelativeVwapLevels", GroupName = "Visual", Order = 112)]
		public bool PublishGlobalZones
		{
			get { return publishGlobalZones; }
			set { publishGlobalZones = value; }
		}

		[Browsable(false)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Global Zone Background", Description = "Obsoleto v1.1.0", GroupName = "Visual", Order = 113)]
		public bool ShowGlobalZoneBackground
		{
			get { return showGlobalZoneBackground; }
			set { showGlobalZoneBackground = value; }
		}



		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Current Month Upper Label", Description = "Text for the current month +SD1 band", GroupName = "Visual", Order = 114)]
		public string CurrentMonthLabelUpper
		{
			get { return currentMonthLabelUpper; }
			set { currentMonthLabelUpper = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Current Month Lower Label", Description = "Text for the current month -SD1 band", GroupName = "Visual", Order = 115)]
		public string CurrentMonthLabelLower
		{
			get { return currentMonthLabelLower; }
			set { currentMonthLabelLower = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Current Month Label Color", Description = "Color of the current month labels", GroupName = "Visual", Order = 116)]
		public System.Windows.Media.Brush CurrentMonthLabelColor
		{
			get { return currentMonthLabelColor; }
			set { currentMonthLabelColor = value; }
		}

		[Browsable(false)]
		public string CurrentMonthLabelColorSerializable
		{
			get { return Serialize.BrushToString(currentMonthLabelColor); }
			set { currentMonthLabelColor = Serialize.StringToBrush(value); }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Current Month Label Size", Description = "Size of the current month labels", GroupName = "Visual", Order = 117)]
		public int CurrentMonthLabelSize
		{
			get { return currentMonthLabelSize; }
			set { currentMonthLabelSize = value; }
		}

		[Range(0, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Current Month Label Offset", Description = "Horizontal offset for the current month labels", GroupName = "Visual", Order = 118)]
		public int CurrentMonthLabelOffset
		{
			get { return currentMonthLabelOffset; }
			set { currentMonthLabelOffset = value; }
		}

		[Browsable(false)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Current Month Labels Globally", Description = "Obsoleto v1.1.0", GroupName = "Visual", Order = 119)]
		public bool ShowCurrentMonthLabelsGlobally
		{
			get { return showCurrentMonthLabelsGlobally; }
			set { showCurrentMonthLabelsGlobally = value; }
		}

		[Browsable(false)]
		[Range(0, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Global Label Padding", Description = "Obsoleto v1.1.0", GroupName = "Visual", Order = 120)]
		public int GlobalLabelPadding
		{
			get { return globalLabelPadding; }
			set { globalLabelPadding = value; }
		}
		#endregion
	
		#region Miscellaneous
		
		public override string FormatPriceMarker(double price)
		{
			return Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price));
		}
		
		private DateTime RoundUpTimeToPeriodTime(DateTime time)
		{
				DateTime result = new DateTime(time.Year, time.Month, 1); 
				return result.AddMonths(1).AddDays(-1);
		}	
		
		private DateTime GetLastBarSessionDateD(DateTime time)
		{
			sessionIterator.CalculateTradingDay(time, timeBased);
			sessionDateTmp = sessionIterator.ActualTradingDayExchange;
			return sessionDateTmp;			
		}
		
		private DateTime GetLastBarSessionDateM(DateTime time)
		{
			sessionIterator.CalculateTradingDay(time, timeBased);
			sessionDateTmp = sessionIterator.ActualTradingDayExchange;
			DateTime monthlyEndDateTmpM = RoundUpTimeToPeriodTime(sessionDateTmp);				
			if(monthlyEndDateTmpM != cacheMonthlyEndDate) 
			{
				cacheMonthlyEndDate = monthlyEndDateTmpM;
				if (newSessionBarIdxArr.Count == 0 || (newSessionBarIdxArr.Count > 0 && CurrentBar > (int) newSessionBarIdxArr[newSessionBarIdxArr.Count - 1]))
					newSessionBarIdxArr.Add(CurrentBar);
			}
			return monthlyEndDateTmpM;
		}
		
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Bars == null || ChartControl == null || !IsVisible) return;

			SharpDX.Direct2D1.Brush innerAreaBrushDX = null;
			SharpDX.Direct2D1.Brush middleAreaBrushDX = null;
			SharpDX.Direct2D1.Brush outerAreaBrushDX = null;
			SharpDX.Direct2D1.AntialiasMode oldAntialiasMode = RenderTarget.AntialiasMode;
			try
			{
			innerAreaBrushDX = innerAreaBrush.ToDxBrush(RenderTarget);
			middleAreaBrushDX = middleAreaBrush.ToDxBrush(RenderTarget);
			outerAreaBrushDX = outerAreaBrush.ToDxBrush(RenderTarget);
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			bool nonEquidistant 			= (chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti);
			int	lastBarPainted 	 			= ChartBars.ToIndex;
			int lastBarCounted				= Inputs[0].Count - 1;
			int	lastBarOnUpdate				= lastBarCounted - (Calculate == Calculate.OnBarClose ? 1 : 0);
			int	lastBarIndex				= Math.Min(lastBarPainted, lastBarOnUpdate);
			int firstBarPainted	 			= ChartBars.FromIndex;
			int firstBarIndex  	 			= Math.Max(BarsRequiredToPlot, firstBarPainted);
			int firstBarIdxToPaint  		= 0;
			int lastPlotIndex				= 0;
			int firstPlotIndex				= 0;
			int	returnBar					= 0;
			double barWidth					= chartControl.GetBarPaintWidth(chartControl.BarsArray[0]);
			int x							= 0;
			int y							= 0;
			Vector2[] cloudArray 			= new Vector2[2 * (Math.Max(0, lastBarIndex - firstBarIndex + displacement) + 1)]; 
			
			if(displacement > 0 && nonEquidistant)
				return;
			if(lastBarIndex + displacement > firstBarIndex)
			{	
				if (displacement > 0 && lastBarIndex < lastBarOnUpdate)
					lastPlotIndex = lastBarIndex + 1;
				else if (displacement > 0 && lastBarIndex == lastBarOnUpdate)
				{	
					lastPlotIndex = lastBarIndex + displacement;
					for(int i = 0; i < displacement; i++)
					{
						x = ChartControl.GetXByBarIndex(ChartBars, lastPlotIndex);
						if(x > ChartPanel.X + ChartPanel.W + 1.5*barWidth - ChartControl.Properties.BarMarginRight)
							lastPlotIndex = lastPlotIndex - 1;
						else
							break;
					}	
				}	
				else
					lastPlotIndex = lastBarIndex;
			
				if(showBands)
				{	
					do
					{
						for (int i = newSessionBarIdxArr.Count - 1; i >= 0; i--)
						{
							int prevSessionBreakIdx = newSessionBarIdxArr[i];
							if (prevSessionBreakIdx + displacement <= lastPlotIndex)
							{
								firstBarIdxToPaint = prevSessionBreakIdx + displacement;
								break;
							}
						}
						firstPlotIndex = Math.Max(firstBarIndex, firstBarIdxToPaint);
						
						if(innerAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathI;
							SharpDX.Direct2D1.GeometrySink 	sinkI;
							pathI = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathI)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[3].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand1.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand1.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkI = pathI.Open();
								sinkI.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkI.AddLine(cloudArray[i]);
								sinkI.EndFigure(FigureEnd.Closed);
				        		sinkI.Close();
			 					RenderTarget.FillGeometry(pathI, innerAreaBrushDX);
							}
							pathI.Dispose();
							sinkI.Dispose();					
						}
						
						if(middleAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathMU;
							SharpDX.Direct2D1.GeometrySink 	sinkMU;
							pathMU = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathMU)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[2].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand2.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(UpperBand1.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkMU = pathMU.Open();
								sinkMU.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkMU.AddLine(cloudArray[i]);
								sinkMU.EndFigure(FigureEnd.Closed);
				        		sinkMU.Close();
			 					RenderTarget.FillGeometry(pathMU, middleAreaBrushDX);
							}
							pathMU.Dispose();
							sinkMU.Dispose();					
							SharpDX.Direct2D1.PathGeometry 	pathML;
							SharpDX.Direct2D1.GeometrySink 	sinkML;
							pathML = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathML)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[4].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(LowerBand1.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand2.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkML = pathML.Open();
								sinkML.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkML.AddLine(cloudArray[i]);
								sinkML.EndFigure(FigureEnd.Closed);
				        		sinkML.Close();
			 					RenderTarget.FillGeometry(pathML, middleAreaBrushDX);
							}
							pathML.Dispose();
							sinkML.Dispose();						
						}					
						
						if(outerAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathOU;
							SharpDX.Direct2D1.GeometrySink 	sinkOU;
							pathOU = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathOU)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[1].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand3.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(UpperBand2.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkOU = pathOU.Open();
								sinkOU.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkOU.AddLine(cloudArray[i]);
								sinkOU.EndFigure(FigureEnd.Closed);
				        		sinkOU.Close();
			 					RenderTarget.FillGeometry(pathOU, outerAreaBrushDX);
							}
							pathOU.Dispose();
							sinkOU.Dispose();					
							SharpDX.Direct2D1.PathGeometry 	pathOL;
							SharpDX.Direct2D1.GeometrySink 	sinkOL;
							pathOL = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathOL)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[5].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(LowerBand2.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand3.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkOL = pathOL.Open();
								sinkOL.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkOL.AddLine(cloudArray[i]);
								sinkOL.EndFigure(FigureEnd.Closed);
				        		sinkOL.Close();
			 					RenderTarget.FillGeometry(pathOL, outerAreaBrushDX);
							}
							pathOL.Dispose();
							sinkOL.Dispose();						
						}					
						if(lastPlotIndex < firstPlotIndex)
							lastPlotIndex = 0;
						else
							lastPlotIndex = firstPlotIndex - 1;
					}	
					while (lastPlotIndex > firstBarIndex);

					// v1.1.0: Session Zones — SharpDX rendering
					if (showSessionZones && activeZones.Count > 0)
					{
						var zoneBrushDX = sessionZoneBrush.ToDxBrush(RenderTarget);
						zoneBrushDX.Opacity = sessionZoneOpacity / 100f;
						var zoneLineBrushDX = zoneLineBrush.ToDxBrush(RenderTarget);
						var zoneTextBrushDX = zoneTextBrush.ToDxBrush(RenderTarget);
						var zoneTextBgBrushDX = zoneTextBackgroundBrush.ToDxBrush(RenderTarget);
						zoneTextBgBrushDX.Opacity = zoneTextBackgroundOpacity / 100f;
						var zoneTextFmt = new SimpleFont("Arial", zoneTextSize).ToDirectWriteTextFormat();
						zoneTextFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						zoneTextFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

						// Referencia temporal para calcular edad de zonas
						int refBarIdx = Math.Min(ChartBars.ToIndex, Bars.Count - 1);
						DateTime refTime = (refBarIdx >= 0) ? Bars.GetTime(refBarIdx) : DateTime.Now;
						float labelH = zoneTextSize * 2;

						// Obstáculos de etiquetas de zona (para anticolisión de DVA)
						zoneLabelObstacles.Clear();

						foreach (var zone in activeZones)
						{
							float zx1 = (float)chartControl.GetXByTime(zone.StartTime);
							float zx2 = zone.IsActive
								? (float)chartControl.GetXByBarIndex(ChartBars, lastBarIndex)
								: (float)chartControl.GetXByTime(zone.EndTime);
							zx1 = Math.Max(zx1, ChartPanel.X);
							zx2 = Math.Min(zx2, ChartPanel.X + ChartPanel.W);
							if (zx2 <= zx1) continue;
							float zyUp = (float)chartScale.GetYByValue(zone.UpperY);
							float zyLow = (float)chartScale.GetYByValue(zone.LowerY);

							// Rectángulo de fondo
							RenderTarget.FillRectangle(new SharpDX.RectangleF(zx1, zyUp, zx2 - zx1, zyLow - zyUp), zoneBrushDX);
							// Líneas upper/lower
							RenderTarget.DrawLine(new SharpDX.Vector2(zx1, zyUp), new SharpDX.Vector2(zx2, zyUp), zoneLineBrushDX, zoneLineWidth);
							RenderTarget.DrawLine(new SharpDX.Vector2(zx1, zyLow), new SharpDX.Vector2(zx2, zyLow), zoneLineBrushDX, zoneLineWidth);
							// Etiquetas fijas con edad
							int ageMonths = ((refTime.Year - zone.StartTime.Year) * 12) + refTime.Month - zone.StartTime.Month;
							string ageStr = ageMonths > 0 ? $" -{ageMonths}M" : "";
							float zlX = zx2 + 2;
							var rectUp = new SharpDX.RectangleF(zlX, zyUp - zoneTextSize, 120, labelH);
							RenderTarget.FillRectangle(rectUp, zoneTextBgBrushDX);
							RenderTarget.DrawText(zoneLabelUpper + ageStr, zoneTextFmt, rectUp, zoneTextBrushDX);
							zoneLabelObstacles.Add(rectUp);
							var rectLow = new SharpDX.RectangleF(zlX, zyLow - zoneTextSize, 120, labelH);
							RenderTarget.FillRectangle(rectLow, zoneTextBgBrushDX);
							RenderTarget.DrawText(zoneLabelLower + ageStr, zoneTextFmt, rectLow, zoneTextBrushDX);
							zoneLabelObstacles.Add(rectLow);
						}
						zoneBrushDX.Dispose();
						zoneLineBrushDX.Dispose();
						zoneTextBrushDX.Dispose();
						zoneTextBgBrushDX.Dispose();
						zoneTextFmt.Dispose();
					}

					// Draw Current Month Labels (mDVAH / mDVAL) — con anticolisión contra etiquetas de zona
					if (lastBarIndex >= firstBarIndex)
					{
						SharpDX.Direct2D1.Brush labelBrushDX = currentMonthLabelColor.ToDxBrush(RenderTarget);
						SimpleFont labelFont = new SimpleFont("Arial", currentMonthLabelSize);
						SharpDX.DirectWrite.TextFormat textFormat = labelFont.ToDirectWriteTextFormat();
						textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

						// Upper Band 1 Label
						if (Values[3].IsValidDataPointAt(lastBarIndex - displacement))
						{
							double yVal = UpperBand1.GetValueAt(lastBarIndex - displacement);
							float labelY = (float)chartScale.GetYByValue(yVal);
							float labelX = (float)chartControl.GetXByBarIndex(ChartBars, lastBarIndex) + currentMonthLabelOffset;

							SharpDX.RectangleF rect = new SharpDX.RectangleF(labelX, labelY - currentMonthLabelSize, 200, currentMonthLabelSize * 2);
							// Anticolisión: desplazar hacia la derecha si colisiona con etiquetas de zona
							for (int ac = 0; ac < 10; ac++)
							{
								bool hit = false;
								foreach (var obs in zoneLabelObstacles)
								{
									if (rect.Intersects(obs)) { rect.X = obs.Right + 5; hit = true; break; }
								}
								if (!hit) break;
							}
							RenderTarget.DrawText(currentMonthLabelUpper, textFormat, rect, labelBrushDX);
						}

						// Lower Band 1 Label
						if (Values[4].IsValidDataPointAt(lastBarIndex - displacement))
						{
							double yVal = LowerBand1.GetValueAt(lastBarIndex - displacement);
							float labelY = (float)chartScale.GetYByValue(yVal);
							float labelX = (float)chartControl.GetXByBarIndex(ChartBars, lastBarIndex) + currentMonthLabelOffset;

							SharpDX.RectangleF rect = new SharpDX.RectangleF(labelX, labelY - currentMonthLabelSize, 200, currentMonthLabelSize * 2);
							// Anticolisión: desplazar hacia la derecha si colisiona con etiquetas de zona
							for (int ac = 0; ac < 10; ac++)
							{
								bool hit = false;
								foreach (var obs in zoneLabelObstacles)
								{
									if (rect.Intersects(obs)) { rect.X = obs.Right + 5; hit = true; break; }
								}
								if (!hit) break;
							}
							RenderTarget.DrawText(currentMonthLabelLower, textFormat, rect, labelBrushDX);
						}

						labelBrushDX.Dispose();
						textFormat.Dispose();
					}
				}
			}	
			}
			catch (Exception ex)
			{
				Print("RelativeMonthlyVwap OnRender ERROR: " + ex.Message);
			}
			finally
			{
				if (innerAreaBrushDX != null) innerAreaBrushDX.Dispose();
				if (middleAreaBrushDX != null) middleAreaBrushDX.Dispose();
				if (outerAreaBrushDX != null) outerAreaBrushDX.Dispose();
			}
			RenderTarget.AntialiasMode = oldAntialiasMode;
			base.OnRender(chartControl, chartScale);
		}
		#endregion
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{		
	public class RelativeMonthlyVwapTypeConverter : NinjaTrader.NinjaScript.IndicatorBaseConverter
	{
		public override bool GetPropertiesSupported(ITypeDescriptorContext context) { return true; }

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);

			RelativeMonthlyVwap			thisVWAPInstance			= (RelativeMonthlyVwap) value;
			amaSessionTypeVWAPM			sessionTypeFromInstance		= thisVWAPInstance.SessionType;
			amaBandTypeVWAPM			bandTypeFromInstance		= thisVWAPInstance.BandType;
			
			PropertyDescriptorCollection adjusted = new PropertyDescriptorCollection(null);
			
			foreach (PropertyDescriptor thisDescriptor in propertyDescriptorCollection)
			{
				if ((sessionTypeFromInstance == amaSessionTypeVWAPM.Full_Session) && (thisDescriptor.Name == "CustomTZSelector" 
					|| thisDescriptor.Name == "S_CustomSessionStart" || thisDescriptor.Name == "S_CustomSessionEnd"))
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else if (bandTypeFromInstance == amaBandTypeVWAPM.None && (thisDescriptor.Name == "MultiplierSD1" || thisDescriptor.Name == "MultiplierSD2" || thisDescriptor.Name == "MultiplierSD3"	
					|| thisDescriptor.Name == "MultiplierQR1" || thisDescriptor.Name == "MultiplierQR2" || thisDescriptor.Name == "MultiplierQR3"
					|| thisDescriptor.Name == "InnerBandBrush" || thisDescriptor.Name == "MiddleBandBrush" || thisDescriptor.Name == "OuterBandBrush" 
					|| thisDescriptor.Name == "Plot1Style" || thisDescriptor.Name == "Dash1Style" || thisDescriptor.Name == "Plot1Width" 
					|| thisDescriptor.Name == "InnerAreaOpacity" || thisDescriptor.Name == "MiddleAreaOpacity" || thisDescriptor.Name == "OuterAreaOpacity"))
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else if (bandTypeFromInstance != amaBandTypeVWAPM.Standard_Deviation && (thisDescriptor.Name == "MultiplierSD1" || thisDescriptor.Name == "MultiplierSD2" || thisDescriptor.Name == "MultiplierSD3"))
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else if (bandTypeFromInstance != amaBandTypeVWAPM.Quarter_Range && (thisDescriptor.Name == "MultiplierQR1" || thisDescriptor.Name == "MultiplierQR2" || thisDescriptor.Name == "MultiplierQR3"))
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] {new BrowsableAttribute(false), }));
				else	
					adjusted.Add(thisDescriptor);
			}
			return adjusted;
		}
	}
}

// Enums amaSessionTypeVWAPM, amaBandTypeVWAPM, amaTimeZonesVWAPM
// se reutilizan desde amaCurrentMonthVWAP.cs (global namespace).
// No redefinir aquí o causa CS0101.

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeMonthlyVwap[] cacheRelativeMonthlyVwap;
		public RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			return RelativeMonthlyVwap(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, publishGlobalZones, showGlobalZoneBackground, currentMonthLabelUpper, currentMonthLabelLower, currentMonthLabelSize, currentMonthLabelOffset, showCurrentMonthLabelsGlobally, globalLabelPadding);
		}

		public RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(ISeries<double> input, amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			if (cacheRelativeMonthlyVwap != null)
				for (int idx = 0; idx < cacheRelativeMonthlyVwap.Length; idx++)
					if (cacheRelativeMonthlyVwap[idx] != null && cacheRelativeMonthlyVwap[idx].SessionType == sessionType && cacheRelativeMonthlyVwap[idx].BandType == bandType && cacheRelativeMonthlyVwap[idx].CustomTZSelector == customTZSelector && cacheRelativeMonthlyVwap[idx].S_CustomSessionStart == s_CustomSessionStart && cacheRelativeMonthlyVwap[idx].S_CustomSessionEnd == s_CustomSessionEnd && cacheRelativeMonthlyVwap[idx].MultiplierSD1 == multiplierSD1 && cacheRelativeMonthlyVwap[idx].MultiplierSD2 == multiplierSD2 && cacheRelativeMonthlyVwap[idx].MultiplierSD3 == multiplierSD3 && cacheRelativeMonthlyVwap[idx].MultiplierQR1 == multiplierQR1 && cacheRelativeMonthlyVwap[idx].MultiplierQR2 == multiplierQR2 && cacheRelativeMonthlyVwap[idx].MultiplierQR3 == multiplierQR3 && cacheRelativeMonthlyVwap[idx].ShowSessionZones == showSessionZones && cacheRelativeMonthlyVwap[idx].ZoneCutoffPercentage == zoneCutoffPercentage && cacheRelativeMonthlyVwap[idx].SessionZoneOpacity == sessionZoneOpacity && cacheRelativeMonthlyVwap[idx].ZoneLineWidth == zoneLineWidth && cacheRelativeMonthlyVwap[idx].ZoneTextSize == zoneTextSize && cacheRelativeMonthlyVwap[idx].ZoneLabelUpper == zoneLabelUpper && cacheRelativeMonthlyVwap[idx].ZoneLabelLower == zoneLabelLower && cacheRelativeMonthlyVwap[idx].ZoneTextBackgroundOpacity == zoneTextBackgroundOpacity && cacheRelativeMonthlyVwap[idx].PublishGlobalZones == publishGlobalZones && cacheRelativeMonthlyVwap[idx].ShowGlobalZoneBackground == showGlobalZoneBackground && cacheRelativeMonthlyVwap[idx].CurrentMonthLabelUpper == currentMonthLabelUpper && cacheRelativeMonthlyVwap[idx].CurrentMonthLabelLower == currentMonthLabelLower && cacheRelativeMonthlyVwap[idx].CurrentMonthLabelSize == currentMonthLabelSize && cacheRelativeMonthlyVwap[idx].CurrentMonthLabelOffset == currentMonthLabelOffset && cacheRelativeMonthlyVwap[idx].ShowCurrentMonthLabelsGlobally == showCurrentMonthLabelsGlobally && cacheRelativeMonthlyVwap[idx].GlobalLabelPadding == globalLabelPadding && cacheRelativeMonthlyVwap[idx].EqualsInput(input))
						return cacheRelativeMonthlyVwap[idx];
			return CacheIndicator<RelativeIndicators.RelativeMonthlyVwap>(new RelativeIndicators.RelativeMonthlyVwap(){ SessionType = sessionType, BandType = bandType, CustomTZSelector = customTZSelector, S_CustomSessionStart = s_CustomSessionStart, S_CustomSessionEnd = s_CustomSessionEnd, MultiplierSD1 = multiplierSD1, MultiplierSD2 = multiplierSD2, MultiplierSD3 = multiplierSD3, MultiplierQR1 = multiplierQR1, MultiplierQR2 = multiplierQR2, MultiplierQR3 = multiplierQR3, ShowSessionZones = showSessionZones, ZoneCutoffPercentage = zoneCutoffPercentage, SessionZoneOpacity = sessionZoneOpacity, ZoneLineWidth = zoneLineWidth, ZoneTextSize = zoneTextSize, ZoneLabelUpper = zoneLabelUpper, ZoneLabelLower = zoneLabelLower, ZoneTextBackgroundOpacity = zoneTextBackgroundOpacity, PublishGlobalZones = publishGlobalZones, ShowGlobalZoneBackground = showGlobalZoneBackground, CurrentMonthLabelUpper = currentMonthLabelUpper, CurrentMonthLabelLower = currentMonthLabelLower, CurrentMonthLabelSize = currentMonthLabelSize, CurrentMonthLabelOffset = currentMonthLabelOffset, ShowCurrentMonthLabelsGlobally = showCurrentMonthLabelsGlobally, GlobalLabelPadding = globalLabelPadding }, input, ref cacheRelativeMonthlyVwap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			return indicator.RelativeMonthlyVwap(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, publishGlobalZones, showGlobalZoneBackground, currentMonthLabelUpper, currentMonthLabelLower, currentMonthLabelSize, currentMonthLabelOffset, showCurrentMonthLabelsGlobally, globalLabelPadding);
		}

		public Indicators.RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(ISeries<double> input , amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			return indicator.RelativeMonthlyVwap(input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, publishGlobalZones, showGlobalZoneBackground, currentMonthLabelUpper, currentMonthLabelLower, currentMonthLabelSize, currentMonthLabelOffset, showCurrentMonthLabelsGlobally, globalLabelPadding);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			return indicator.RelativeMonthlyVwap(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, publishGlobalZones, showGlobalZoneBackground, currentMonthLabelUpper, currentMonthLabelLower, currentMonthLabelSize, currentMonthLabelOffset, showCurrentMonthLabelsGlobally, globalLabelPadding);
		}

		public Indicators.RelativeIndicators.RelativeMonthlyVwap RelativeMonthlyVwap(ISeries<double> input , amaSessionTypeVWAPM sessionType, amaBandTypeVWAPM bandType, amaTimeZonesVWAPM customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, bool publishGlobalZones, bool showGlobalZoneBackground, string currentMonthLabelUpper, string currentMonthLabelLower, int currentMonthLabelSize, int currentMonthLabelOffset, bool showCurrentMonthLabelsGlobally, int globalLabelPadding)
		{
			return indicator.RelativeMonthlyVwap(input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, publishGlobalZones, showGlobalZoneBackground, currentMonthLabelUpper, currentMonthLabelLower, currentMonthLabelSize, currentMonthLabelOffset, showCurrentMonthLabelsGlobally, globalLabelPadding);
		}
	}
}

#endregion
