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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class SessionLevelsStrategy : Strategy
	{
		// Session 1: Asia
		private double asiaHigh = double.MinValue;
		private double asiaLow = double.MaxValue;
		private DateTime asiaSessionDate; // To track if we are in a new session
		
		// Session 2: Europe
		private double europeHigh = double.MinValue;
		private double europeLow = double.MaxValue;
		private DateTime europeSessionDate;

		// Session 3: USA
		private double usaHigh = double.MinValue;
		private double usaLow = double.MaxValue;
		private DateTime usaSessionDate;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Strategy to track Session Highs and Lows strictly during session hours.";
				Name										= "SessionLevelsStrategy";
				Calculate									= Calculate.OnEachTick;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for more information
				IsInstantiatedOnEachOptimizationIteration	= true;
				
				IsOverlay = true; // Draw on the price chart

				// Default Hours
				AsiaStartTime = "18:00";
				AsiaEndTime = "03:00";
				EuropeStartTime = "03:00";
				EuropeEndTime = "09:30";
				USAStartTime = "09:30";
				USAEndTime = "18:00";
			}
			else if (State == State.Configure)
			{
			}
		}

		// TimeZone Caching
		private TimeZoneInfo nyTimeZone;
		private TimeZoneInfo chartTimeZone;
		private bool timeZonesLoaded = false;

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 20) return;
			
			// Initialize TimeZones once
			if (!timeZonesLoaded)
			{
				try 
				{
					// "Eastern Standard Time" handles both EST and EDT automatically on Windows
					nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
					
					
					// Get the TimeZone of the current bars/chart
					// We use the Global Options TimeZone because that is what determines the visual 'Local' time on the chart
					// for most users who use "Default" time settings.
					if (NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo != null)
						chartTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
					else
						chartTimeZone = TimeZoneInfo.Local; // Fallback
						
					timeZonesLoaded = true;
				}
				catch (Exception ex)
				{
					Print("Error loading TimeZones: " + ex.Message);
					timeZonesLoaded = true; // Don't retry per tick
				}
			}

			// We pass the TimeZones to the check method
			CheckSession("Asia", AsiaStartTime, AsiaEndTime, ref asiaHigh, ref asiaLow, ref asiaSessionDate, Brushes.White);
			CheckSession("Europe", EuropeStartTime, EuropeEndTime, ref europeHigh, ref europeLow, ref europeSessionDate, Brushes.Yellow);
			CheckSession("USA", USAStartTime, USAEndTime, ref usaHigh, ref usaLow, ref usaSessionDate, Brushes.RoyalBlue);
		}
		
		private void CheckSession(string sessionName, string startStr, string endStr, ref double sessionHigh, ref double sessionLow, ref DateTime sessionDate, Brush color)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;

			// 1. Resolve NY Times for "Today"
			// Problem: "Today" in NY might be different than "Today" on Chart if offset is large.
			// However, we just need to find the "Current" or "Upcoming" session window in Chart Time.
			
			// Strategy: Get Current Chart Time. Convert to NY Time. 
			// Compare NY Time to NY Windows.
			// This is easier than converting Windows to Chart Time because of date boundaries.
			
			DateTime chartTime = Time[0];
			DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
			TimeSpan nyTimeOfDay = nyTime.TimeOfDay;
			
			// Parse inputs (Assumed to be NY Time)
			TimeSpan startTs = TimeSpan.Parse(startStr);
			TimeSpan endTs = TimeSpan.Parse(endStr);
			
			bool inSession = false;
			
			// Handle Midnight crossover (Start > End, e.g. 18:00 to 03:00)
			if (startTs > endTs)
			{
				if (nyTimeOfDay >= startTs || nyTimeOfDay < endTs) inSession = true;
			}
			else
			{
				if (nyTimeOfDay >= startTs && nyTimeOfDay < endTs) inSession = true;
			}
			
			if (inSession)
			{
				// Detect New Session Start Logic based on NY Date
				// If (nyNow >= startTs), the session started Today (NY).
				// If (nyNow < endTs) and is crossover, the session started Yesterday (NY).
				
				DateTime calculatedSessionStartNY = DateTime.MinValue;
				if (startTs > endTs)
				{
					// Crossover
					if (nyTimeOfDay >= startTs) calculatedSessionStartNY = nyTime.Date; // Started today
					else calculatedSessionStartNY = nyTime.Date.AddDays(-1); // Started yesterday
				}
				else
				{
					calculatedSessionStartNY = nyTime.Date;
				}
				
				// Full NY Start DateTime
				calculatedSessionStartNY = calculatedSessionStartNY.Add(startTs);
				
				// Identify this session instance uniquely
				if (calculatedSessionStartNY != sessionDate)
				{
					// New Session Detected
					sessionHigh = double.MinValue;
					sessionLow = double.MaxValue;
					sessionDate = calculatedSessionStartNY;
				}
				
				// Track High/Low
				if (High[0] > sessionHigh) sessionHigh = High[0];
				if (Low[0] < sessionLow) sessionLow = Low[0];
				
				// Visuals: We need to draw from the Session Start Time.
				// But 'sessionDate' is in NY Time. We need to convert it back to Chart Time for Draw.Line!
				
				DateTime chartStartTime = TimeZoneInfo.ConvertTime(sessionDate, nyTimeZone, chartTimeZone);

				// Draw logic
				if (sessionHigh > 0 && sessionLow < double.MaxValue)
				{
					string tagH = sessionName + "_High_" + sessionDate.Ticks;
					Draw.Line(this, tagH, false, chartStartTime, sessionHigh, Time[0], sessionHigh, color, DashStyleHelper.Solid, 2);
					
					string tagL = sessionName + "_Low_" + sessionDate.Ticks;
					Draw.Line(this, tagL, false, chartStartTime, sessionLow, Time[0], sessionLow, color, DashStyleHelper.Solid, 2);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Asia Start Time", Order=1, GroupName="1. Sessions")]
		public string AsiaStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Asia End Time", Order=2, GroupName="1. Sessions")]
		public string AsiaEndTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Europe Start Time", Order=3, GroupName="1. Sessions")]
		public string EuropeStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Europe End Time", Order=4, GroupName="1. Sessions")]
		public string EuropeEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="USA Start Time", Order=5, GroupName="1. Sessions")]
		public string USAStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="USA End Time", Order=6, GroupName="1. Sessions")]
		public string USAEndTime { get; set; }
		#endregion
	}
}
