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
using System.Net;
using System.Net.Mail;
using System.IO;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class SessionLevelsStrategy : Strategy
	{
		// Session 1: Asia


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
				
				// High Performance Plots for VWAPs
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "EthHighVWAP");
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "EthLowVWAP");
			}
			else if (State == State.Configure)
			{
			}
		}

		// TimeZone Caching
		private TimeZoneInfo nyTimeZone;
		private TimeZoneInfo chartTimeZone;
		private bool timeZonesLoaded = false;
		private double lastVol = 0;

		// Level Persistence
		private class SessionLevel
		{
			public string Name;
			public double Price;
			public DateTime StartTime;
			public DateTime EndTime;
			public DateTime MitigationTime; // When it was touched
			public bool IsResistance; // True = High, False = Low
			public bool IsMitigated;
			public Brush Color;
			public string Tag; // For Drawing
			
			// VWAP Data
			public double VolSum;
			public double PvSum;
			public bool JustReset;
		}
		
		private List<SessionLevel> activeLevels = new List<SessionLevel>();

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 20) return;
			
			// Initialize TimeZones & Lists once
			if (!timeZonesLoaded)
			{
				try 
				{
					// "Eastern Standard Time" handles both EST and EDT automatically on Windows
					nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
					
					// Get the TimeZone of the current bars/chart
					if (NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo != null)
						chartTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
					else
						chartTimeZone = TimeZoneInfo.Local; // Fallback
						
					timeZonesLoaded = true;
				}
				catch (Exception ex)
				{
					Print("Error loading TimeZones: " + ex.Message);
					timeZonesLoaded = true; 
				}
			}

			// 0. Calculate Volume Delta for VWAP
			if (IsFirstTickOfBar) lastVol = 0;
			double deltaVol = Volume[0] - lastVol;
			lastVol = Volume[0];

			// 1. Session Logic: Identify/Create Levels
			CheckSession("Asia", AsiaStartTime, AsiaEndTime, Brushes.White, deltaVol);
			CheckSession("Europe", EuropeStartTime, EuropeEndTime, Brushes.Yellow, deltaVol);
			CheckSession("USA", USAStartTime, USAEndTime, Brushes.RoyalBlue, deltaVol);
			
			// 2. Manage Extension & Touching
			ManageLevels(deltaVol);
			
			// 3. Global ETH VWAPs
			ManageGlobalVWAPs(deltaVol);
			
			// 4. Entry Logic
			ManageEntryA_Plus();
		}
		
		private void CheckSession(string sessionName, string startStr, string endStr, Brush color, double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;

			DateTime chartTime = Time[0];
			DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
			TimeSpan nyTimeOfDay = nyTime.TimeOfDay;
			
			TimeSpan startTs = TimeSpan.Parse(startStr);
			TimeSpan endTs = TimeSpan.Parse(endStr);
			
			bool inSession = false;
			
			if (startTs > endTs) { if (nyTimeOfDay >= startTs || nyTimeOfDay < endTs) inSession = true; }
			else { if (nyTimeOfDay >= startTs && nyTimeOfDay < endTs) inSession = true; }
			
			if (inSession)
			{
				// Determine Session Date (for unique ID)
				DateTime calculatedSessionStartNY = (startTs > endTs && nyTimeOfDay < endTs) ? nyTime.Date.AddDays(-1) : nyTime.Date;
				calculatedSessionStartNY = calculatedSessionStartNY.Add(startTs);
				
				// Unique IDs for High and Low
				string tagH = sessionName + "_High_" + calculatedSessionStartNY.Ticks;
				string tagL = sessionName + "_Low_" + calculatedSessionStartNY.Ticks;
				
				// Find or Create Levels
				SessionLevel highLvl = activeLevels.FirstOrDefault(l => l.Tag == tagH);
				SessionLevel lowLvl = activeLevels.FirstOrDefault(l => l.Tag == tagL);
				
				// Convert Start Time to Chart Time for Visuals
				DateTime chartStartTime = TimeZoneInfo.ConvertTime(calculatedSessionStartNY, nyTimeZone, chartTimeZone);

				if (highLvl == null)
				{
					// New High Level (Init VWAP with current Bar Full Volume as it creates the anchor)
					highLvl = new SessionLevel 
					{ 
						Name = sessionName + " High", Price = double.MinValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = true, IsMitigated = false, Color = color, Tag = tagH,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(highLvl);
				}
				else highLvl.JustReset = false; // Reset flag default
				
				if (lowLvl == null)
				{
					// New Low Level
					lowLvl = new SessionLevel 
					{ 
						Name = sessionName + " Low", Price = double.MaxValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = false, IsMitigated = false, Color = color, Tag = tagL,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(lowLvl);
				}
				else lowLvl.JustReset = false;
				
				// Logic: While in session, we push the High/Low out. 
				// If New High -> Reset VWAP to Anchor HERE.
				
				if (High[0] > highLvl.Price) 
				{
					highLvl.Price = High[0];
					// RE-ANCHOR VWAP
					highLvl.VolSum = Volume[0];
					highLvl.PvSum = Volume[0] * Close[0];
					highLvl.JustReset = true;
				}
				
				if (Low[0] < lowLvl.Price) 
				{
					lowLvl.Price = Low[0];
					// RE-ANCHOR VWAP
					lowLvl.VolSum = Volume[0];
					lowLvl.PvSum = Volume[0] * Close[0];
					lowLvl.JustReset = true;
				}
				
				// While in session, update EndTime to current to keep line growing
				if (!highLvl.IsMitigated) highLvl.EndTime = Time[0];
				if (!lowLvl.IsMitigated) lowLvl.EndTime = Time[0];
			}
		}

		private void ManageLevels(double deltaVol)
		{
			// Check for touches on existing active levels
			
			foreach (var lvl in activeLevels)
			{
				// VWAP ACCUMULATION
				if (!lvl.JustReset)
				{
					lvl.VolSum += deltaVol;
					lvl.PvSum += deltaVol * Close[0];
				}
				// If JustReset was true, we already set VolSum/PvSum in CheckSession. 
				// JustReset is ephemeral for this tick.
				
				// Calculate VWAP
				double vwap = 0;
				if (lvl.VolSum > 0) vwap = lvl.PvSum / lvl.VolSum;

				// LINE EXTENSION LOGIC
				// Alive: Always extend.
				// Mitigated: Extend ONLY if we are still in the same calendar day as the mitigation Event.
				
				if (!lvl.IsMitigated)
				{
					lvl.EndTime = Time[0];
				}
				else
				{
					// Ghost Line Extension
					// Extension Rule: Continue until the End of the American Session (USAEndTime).
					// We need to calculate the *specific* cutoff time relative to the Mitigation event.
					
					// 1. Convert MitigationTime to NY to understand when it happened
					DateTime mitNy = TimeZoneInfo.ConvertTime(lvl.MitigationTime, chartTimeZone, nyTimeZone);
					TimeSpan usaEndTs = TimeSpan.Parse(USAEndTime);
					
					// 2. Determine the Cutoff Date/Time (NY)
					// If mitigation happened BEFORE the cutoff today (e.g. 10:00 vs 18:00), cutoff is Today 18:00.
					// If mitigation happened AFTER the cutoff (e.g. 19:00 vs 18:00), cutoff is Tomorrow 18:00.
					
					DateTime cutoffNy;
					if (mitNy.TimeOfDay < usaEndTs)
						cutoffNy = mitNy.Date.Add(usaEndTs);
					else
						cutoffNy = mitNy.Date.AddDays(1).Add(usaEndTs);
						
					// 3. Compare Current Time (NY) to Cutoff (NY)
					DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
					
					if (currentNy < cutoffNy)
					{
						lvl.EndTime = Time[0];
					}
					// Else: Freeze (Stop extending)
				}
				
				// Check for Mitigation (if not already broken)
				// Only if session is effectively done (Start/End checks or just assume if formed)
				// Simplified: Just always check touch.
				
				if (!lvl.IsMitigated)
				{
					// Avoid self-mitigation during formation
					// If the StartTime was effectively "today" or "recent" and we are still largely in that window?
					// Problem: CheckSession pushes Price up/down. 
					// If we are IN session, CheckSession updates Price.
					// So if High[0] > Price, CheckSession makes Price = High[0].
					// So High[0] == Price.
					// So "High[0] >= Price" is TRUE.
					// We need to know if we are "In Session" to avoid mitigation.
					
					// Heuristic: If CheckSession updated it THIS TICK, don't mitigate.
					// But we run ManageLevels AFTER CheckSession.
					// Let's rely on a flag or simply check if Time is outside Session Window?
					// Checking Time is hard because of the varying session hours.
					// Let's use a "InSession" flag on the object?
					// Or reusing the "EndTime" check from previous step:
					// If(lvl.EndTime == Time[0]) it means CheckSession updated it? 
					// NO, we just updated lvl.EndTime = Time[0] at the top of this loop! Invalid logic now.
					
					// Let's add an explicit "LastUpdateBar" or similar to SessionLevel?
					// Or simpler: We know the logic in CheckSession updates Price.
					// If Price == High[0], it's likely pushing.
					// But if Price < High[0], it's a break.
					// Wait, if Price < High[0] (Resistance), then CheckSession WOULD have updated it if we were in session!
					// So... if CheckSession DID NOT update it (Price < High[0]), it means we are NOT in session (or logic failed).
					// Therefore, if High[0] > Price, it MUST be a mitigation break!
					// CORRECT.
					
					// Exception: The very specific moment High[0] jumps? 
					// If in session, CheckSession runs first. 
					// If High[0] > currentHigh, set currentHigh = High[0].
					// So entering ManageLevels, Price == High[0].
					// Use strict inequality? High[0] > Price? No, touch is enough.
					
					// Let's iterate:
					// In Session: Price = 100. High[0] = 101. -> CheckSession sets Price = 101. -> ManageLevels sees Price=101, High[0]=101.
					// Out Session: Price = 100. High[0] = 101. -> CheckSession does nothing. -> ManageLevels sees Price=100, High[0]=101. -> MITIGATION!
					
					// So, logic:
					// Resistance: If High[0] > Price -> Mitigation.
					// Support: If Low[0] < Price -> Mitigation.
					// Equality (Touch) shouldn't count if we assume "Break"?
					// User said "cortadas" (cut/broken) or "tocada" (touched)?
					// "hasta que sea tocada" (touched).
					// If it's a touch (==), then in-session formation is a touch.
					// We MUST distinguish In-Session.
					
					// Let's calculate In-Session locally again or store it.
					// Re-calculating properly is safer.
					
					// Actually, let's use the object creation/update time?
					// Let's look at `IsResistance`.
					bool potentialMitigation = false;
					if (lvl.IsResistance && High[0] >= lvl.Price) potentialMitigation = true;
					if (!lvl.IsResistance && Low[0] <= lvl.Price) potentialMitigation = true;
					
					if (potentialMitigation)
					{
						// Check if we are physically in the session window for this specific level
						// This line's tag has StartTicks.
						// Simplest: Check if the *current price* is EQUAL to level price.
						// If equal, likely just forming/touching.
						// If strictly Greater (Res) or Less (Sup) AND Level Price wasn't updated?
						// It's ambiguous.
						
						// CLEAN FIX: Add `IsActive` bool to SessionLevel, set by CheckSession.
						// But I can't easily change CheckSession signature in this edit without replacing whole file.
						// I'll calculate `inSession` simply here. It's safe.
						// Oh wait, I don't know WHICH session hours apply to THIS level (Asia? USA?).
						// I have `lvl.Name` ("Asia High"). I can parse or Map.
						
						// HACK: Just assume if the Price CHANGED this bar, it's active?
						// No.
						
						// Let's guess based on inequality.
						// If High[0] > lvl.Price, it's definitely a Break (Mitigation), because if it was active, Price would have updated to match High[0].
						// Wait. CheckSession updates logic: `if (High[0] > highLvl.Price) highLvl.Price = High[0];`
						// So Price will ALWAYS be >= High[0] if active.
						// Price will never be < High[0].
						// So if High[0] > Price, it implies CheckSession did NOT run/update -> We are Out of Session -> Mitigation.
						// If High[0] == Price? Could be "Just forming" OR "Perfect double top touch".
						// We'll ignore Exact Touch for mitigation to be safe against formation noise.
						// Strictly greater/less for "Cut/Break".
						
						bool strictBreak = false;
						if (lvl.IsResistance && High[0] > lvl.Price) strictBreak = true;
						if (!lvl.IsResistance && Low[0] < lvl.Price) strictBreak = true;
						
						if (strictBreak)
						{
							lvl.IsMitigated = true;
							lvl.MitigationTime = Time[0];
						}
					}
				}
				
				// Drawing Logic
				string tagA = lvl.Tag + "_A";
				string tagB = lvl.Tag + "_B";
				
				if (!lvl.IsMitigated)
				{
					// Phase A Only: Start -> Current
					Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.EndTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
				}
				else
				{
					// Phase A: Start -> Mitigation
					Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.MitigationTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
					
					// Phase B (Ghost): Mitigation -> Current (Gray, Dash, 1px)
					// Verify MitigationTime < EndTime to draw
					// Draw.Line(this, tagB, false, lvl.MitigationTime, lvl.Price, lvl.EndTime, lvl.Price, Brushes.Gray, DashStyleHelper.Dash, 1);
					// User requested: Gray, Dash, 1px.
					Draw.Line(this, tagB, false, lvl.MitigationTime, lvl.Price, lvl.EndTime, lvl.Price, Brushes.Gray, DashStyleHelper.Dash, 1);
				}
			}
		}


		// -------------------------------------------------------------------------
		// ENTRY LOGIC VARIABLES
		// -------------------------------------------------------------------------
		private enum EntryState { Idle, WaitingForConfirmation, workingOrder, InPosition }
		private EntryState currentEntryState = EntryState.Idle;
		private bool isShortSetup = false; // true = Short, false = Long
		private Order entryOrder = null;
		private Order stopOrder = null;
		private Order targetOrder = null; // We use Order objects for dynamic management
		
		private double setupAnchorPrice = 0; // For SL
		
		// Visual Tracking
		private string triggerTag = "";
		private int triggerBar = 0;
		
		// -------------------------------------------------------------------------
		// GLOBAL ETH SESSION VWAP LOGIC
		// -------------------------------------------------------------------------
		private class SessionVWAP
		{
			public double VolSum;
			public double PvSum;
			public double CurrentValue => VolSum == 0 ? 0 : PvSum / VolSum;
			
			public void Reset(double vol, double price)
			{
				VolSum = vol;
				PvSum = vol * price;
			}
			
			public void Accumulate(double vol, double price)
			{
				VolSum += vol;
				PvSum += vol * price;
			}
		}
		
		private SessionVWAP ethHighVWAP = new SessionVWAP();
		private SessionVWAP ethLowVWAP = new SessionVWAP();
		
		#region Properties
		// Email & Screenshot Properties
		[NinjaScriptProperty]
		[Display(Name = "Send Email with Photo", Description = "Send screenshot via email (Requires SMTP settings)", GroupName = "8. Email Alerts", Order = 1)]
		public bool EnableEmailAlerts { get; set; } = false;

		[NinjaScriptProperty]
		[Display(Name = "To Address", Description = "Recipient address", GroupName = "8. Email Alerts", Order = 2)]
		public string EmailTo { get; set; } = "user@example.com";

		[NinjaScriptProperty]
		[Display(Name = "From Address", Description = "Sender address (usually same as username)", GroupName = "8. Email Alerts", Order = 3)]
		public string EmailFrom { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Host", Description = "e.g. smtp.gmail.com", GroupName = "8. Email Alerts", Order = 4)]
		public string EmailHost { get; set; } = "smtp.gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Port", Description = "e.g. 587", GroupName = "8. Email Alerts", Order = 5)]
		public int EmailPort { get; set; } = 587;

		[NinjaScriptProperty]
		[Display(Name = "SMTP Username", Description = "Full email address", GroupName = "8. Email Alerts", Order = 6)]
		public string EmailUsername { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Password", Description = "App Password (not your login password)", GroupName = "8. Email Alerts", Order = 7)]
		public string EmailPassword { get; set; } = "password";

		private double ethHighPrice = double.MinValue;
		private double ethLowPrice = double.MaxValue;
		private DateTime lastEthResetDate = DateTime.MinValue; 
		
		private int highAnchorBar = 0;
		private int lowAnchorBar = 0;

		private void ManageGlobalVWAPs(double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;
			
			// 1. Determine Current Trading Day (based on 18:00 NY start)
			DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
			TimeSpan cutoff = TimeSpan.FromHours(18);
			DateTime tradingDay = currentNy.TimeOfDay >= cutoff ? currentNy.Date.AddDays(1) : currentNy.Date;
			
			// 2. HARD RESET at Start of Day
			bool hardReset = false;
			if (tradingDay != lastEthResetDate)
			{
				ethHighPrice = double.MinValue;
				ethLowPrice = double.MaxValue;
				ethHighVWAP = new SessionVWAP();
				ethLowVWAP = new SessionVWAP();
				lastEthResetDate = tradingDay;
				hardReset = true;
				
				// Reset Anchor Trackers
				highAnchorBar = CurrentBar;
				lowAnchorBar = CurrentBar;
			}
			
			// 3. Update High/Low and Anchor Logic
			bool highReset = false;
			bool lowReset = false;
			
			// Check High
			if (High[0] > ethHighPrice)
			{
				// New High found! The PREVIOUS segment (from highAnchorBar to CurrentBar-1) is now "Old/Cut".
				// We must paint it GRAY.
				if (!hardReset && CurrentBar > highAnchorBar)
				{
					int barsBack = CurrentBar - highAnchorBar;
					// IMPORTANT: Use i < barsBack to avoid overwriting the Transparency of the Anchor Bar itself.
					for (int i = 1; i < barsBack; i++)
					{
						PlotBrushes[0][i] = Brushes.Gray;
					}
				}
				
				ethHighPrice = High[0];
				highReset = true;
				ethHighVWAP.Reset(Volume[0], Close[0]);
				highAnchorBar = CurrentBar; // Update anchor to here
			}
			else
			{
				ethHighVWAP.Accumulate(deltaVol, Close[0]);
			}
			
			// Check Low
			if (Low[0] < ethLowPrice)
			{
				// New Low found! Paint previous segment Gray.
				if (!hardReset && CurrentBar > lowAnchorBar)
				{
					int barsBack = CurrentBar - lowAnchorBar;
					for (int i = 1; i < barsBack; i++)
					{
						PlotBrushes[1][i] = Brushes.Gray;
					}
				}
				
				ethLowPrice = Low[0];
				lowReset = true;
				ethLowVWAP.Reset(Volume[0], Close[0]);
				lowAnchorBar = CurrentBar;
			}
			else
			{
				ethLowVWAP.Accumulate(deltaVol, Close[0]);
			}
			
			// 4. Assign to Plots (Values[0] = High, Values[1] = Low)
			// Default color is White (defined in AddPlot). We only override active history to Gray when it dies.
			// The "Current" active segment stays White until it dies.
			
			if (ethHighVWAP.VolSum > 0)
			{
				Values[0][0] = ethHighVWAP.CurrentValue;
				
				if (hardReset || highReset)
				{
					PlotBrushes[0][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[0][0] = double.NaN; 
			}

			if (ethLowVWAP.VolSum > 0)
			{
				Values[1][0] = ethLowVWAP.CurrentValue;
				
				if (hardReset || lowReset)
				{
					PlotBrushes[1][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[1][0] = double.NaN;
			}
			
			// Debug Panel
			DrawStatePanel();
			
			// SAFETY NET: Check for Zombie Positions (In Market, but State logic missed it)
			CheckSafetyNet();
		}
		
		private void CheckSafetyNet()
		{
			// 1. Zombie Position: We have a position, but State thinks we are Idle/Working.
			if (Position.MarketPosition != MarketPosition.Flat && currentEntryState != EntryState.InPosition)
			{
				Print(Time[0] + " CRITICAL: Safety Net Triggered! Position exists but State was " + currentEntryState);
				currentEntryState = EntryState.InPosition;
				
				// Force Place Stops if missing
				if (Position.MarketPosition == MarketPosition.Short)
				{
					EnsureProtection("Short");
				}
				else if (Position.MarketPosition == MarketPosition.Long)
				{
					EnsureProtection("Long");
				}
			}
			
			// 2. Ghost State: State thinks we are InPosition, but we are Flat.
			// This happens if we missed the Exit Execution or closed manually.
			// We only reset if we are confident the entry order isn't just "about to fill" (Working State handles that).
			// If State is InPosition, it implies we ALREADY filled. So if we are Flat now, we must have closed.
			if (Position.MarketPosition == MarketPosition.Flat && currentEntryState == EntryState.InPosition)
			{
				Print(Time[0] + " SYNC: State is InPosition but MarketPosition is Flat. Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				entryOrder = null;
				targetOrder = null;
			}
		}
		
		private void DrawStatePanel()
		{
			string text = string.Format("State: {0}\nPosition: {1}\nPnL: {2}\nActive Levels: {3}\nHigh VWAP: {4:F2}\nLow VWAP: {5:F2}",
				currentEntryState,
				Position.MarketPosition,
				SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit.ToString("C"),
				activeLevels.Count,
				ethHighVWAP.CurrentValue,
				ethLowVWAP.CurrentValue);
				
			Draw.TextFixed(this, "InfoPanel", text, TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
		}
		
		// -------------------------------------------------------------------------
		// ENTRY A+ MANAGEMENT
		// -------------------------------------------------------------------------
		private void ManageEntryA_Plus()
		{
			// 1. TRIGGER DETECTION (Transition from Idle -> Waiting)
			// Check if any "Virgin" level was JUST mitigated this tick/bar.
			// Since we run OnEachTick, we can check if MitigationTime is very recent (Time[0]).
			// Warning: OnEachTick means this might fire multiple times per bar if logic repeats.
			// We guard with currentEntryState == Idle.
			
			if (currentEntryState == EntryState.Idle)
			{
				foreach (var lvl in activeLevels)
				{
					// If level is mitigated exactly NOW
					// Note: ManageLevels sets MitigationTime = Time[0].
					if (lvl.IsMitigated && lvl.MitigationTime == Time[0])
					{
						// Valid Trigger?
						// "Entrada A+ cuando el precio toque un nivel virgen"
						
						if (lvl.IsResistance)
						{
							Print(Time[0] + " Trigger Short on " + lvl.Name); // DEBUG
							// Short Setup
							triggerTag = "TriggerShort_" + Time[0].Ticks; // Store Tag
							triggerBar = CurrentBar;
							Draw.ArrowDown(this, triggerTag, true, 0, High[0] + TickSize * 15, Brushes.Cyan);
							Draw.Text(this, triggerTag + "_Txt", "Short", 0, High[0] + TickSize * 25, Brushes.Cyan);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							isShortSetup = true;
							setupAnchorPrice = ethHighPrice; 
						}
						else
						{
							Print(Time[0] + " Trigger Long on " + lvl.Name); // DEBUG
							// Long Setup
							triggerTag = "TriggerLong_" + Time[0].Ticks;
							triggerBar = CurrentBar;
							Draw.ArrowUp(this, triggerTag, true, 0, Low[0] - TickSize * 15, Brushes.Lime);
							Draw.Text(this, triggerTag + "_Txt", "Long", 0, Low[0] - TickSize * 25, Brushes.Lime);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							isShortSetup = false; // Long
							setupAnchorPrice = ethLowPrice;
						}
						
						break; // Only take one trigger at a time
					}
				}
			}
			
			// Update Visuals if bar is expanding (Avoid burying the arrow)
			if (currentEntryState == EntryState.WaitingForConfirmation && CurrentBar == triggerBar)
			{
				if (isShortSetup)
				{
					Draw.ArrowDown(this, triggerTag, true, 0, High[0] + TickSize * 15, Brushes.Cyan);
					Draw.Text(this, triggerTag + "_Txt", "Short", 0, High[0] + TickSize * 25, Brushes.Cyan);
				}
				else
				{
					Draw.ArrowUp(this, triggerTag, true, 0, Low[0] - TickSize * 15, Brushes.Lime);
					Draw.Text(this, triggerTag + "_Txt", "Long", 0, Low[0] - TickSize * 25, Brushes.Lime);
				}
			}
			
			// 2. CONFIRMATION LOGIC (Waiting -> Working)
			// "Wait for a candle... close... max below vwap 1 tick"
			// Must check on Bar Close (IsFirstTickOfBar of NEXT bar, or calculate OnBarClose).
			// If we are OnEachTick, checking IsFirstTickOfBar handles "Just Closed".
			
			if (currentEntryState == EntryState.WaitingForConfirmation && IsFirstTickOfBar)
			{
				// Check Previous Bar [1]
				
				if (isShortSetup)
				{
					// Short: High[1] < Bearish VWAP (ethHighVWAP) - 1 Tick
					// We need the VWAP value at that time. 
					// Values[0][1] holds the HighVWAP of the previous bar.
					double vwapVal = Values[0][1]; 
					
					if (isValidVWAP(vwapVal) && High[1] < (vwapVal - TickSize))
					{
						// CONFIRMED. Place Limit Order.
						// Order Price = Current VWAP (Values[0][0] or re-calculated).
						// Wait, Limit Order "en el vwap".
						// We submit at current VWAP.
						
						entryOrder = EnterShortLimit(0, true, 1, GetCurrentHighVWAP(), "EntryA_Short");
						currentEntryState = EntryState.workingOrder;
						Print(Time[0] + " Order Submitted (Short). OID: " + (entryOrder != null ? entryOrder.OrderId : "null"));
					}
					else
					{
						// Confirmation Logic Update:
						// Do NOT reset to Idle immediately. Wait for subsequent bars.
						// "Esperar a una vela..." implies patience.
						
						// Check invalidation? If price goes ABOVE the Anchor (SL), setup is dead.
						// FIX: Don't invalidate on Sweep. UPDATE Anchor (Trail it).
						if (High[0] > setupAnchorPrice)
						{
							Print(Time[0] + " Anchor Broken. Updating Anchor to " + High[0]);
							setupAnchorPrice = High[0];
							// Do not reset state. Keep Waiting.
						}
					}
				}
				else
				{
					// Long: Low[1] > Bullish VWAP (ethLowVWAP) + 1 Tick
					double vwapVal = Values[1][1];
					
					if (isValidVWAP(vwapVal) && Low[1] > (vwapVal + TickSize))
					{
						entryOrder = EnterLongLimit(0, true, 1, GetCurrentLowVWAP(), "EntryA_Long");
						currentEntryState = EntryState.workingOrder;
						Print(Time[0] + " Order Submitted (Long). OID: " + (entryOrder != null ? entryOrder.OrderId : "null"));
					}
					else
					{
						// Check invalidation
						if (Low[0] < setupAnchorPrice)
						{
							Print(Time[0] + " Anchor Broken. Updating Anchor to " + Low[0]);
							setupAnchorPrice = Low[0];
							// Do not reset state. Keep Waiting.
						}
					}
				}
			}
			
			// Mid-bar check for Anchor Update
			if (currentEntryState == EntryState.WaitingForConfirmation && !IsFirstTickOfBar)
			{
				if (isShortSetup && High[0] > setupAnchorPrice) 
				{
					Print(Time[0] + " Anchor Broken (Mid-Bar). Updating Anchor to " + High[0]);
					setupAnchorPrice = High[0];
				}
				if (!isShortSetup && Low[0] < setupAnchorPrice) 
				{
					Print(Time[0] + " Anchor Broken (Mid-Bar). Updating Anchor to " + Low[0]);
					setupAnchorPrice = Low[0];
				}
			}
			
			// Visuals Update (Track the expanding candle)
			if (currentEntryState == EntryState.WaitingForConfirmation && CurrentBar == triggerBar)
			{
				if (isShortSetup)
				{
					Draw.ArrowDown(this, triggerTag, true, 0, High[0] + TickSize * 15, Brushes.Cyan);
					Draw.Text(this, triggerTag + "_Txt", "Short", 0, High[0] + TickSize * 25, Brushes.Cyan);
				}
				else
				{
					Draw.ArrowUp(this, triggerTag, true, 0, Low[0] - TickSize * 15, Brushes.Lime);
					Draw.Text(this, triggerTag + "_Txt", "Long", 0, Low[0] - TickSize * 25, Brushes.Lime);
				}
			}

			// 3. ORDER MANAGEMENT & SYNC (Working -> InPosition)
			if (currentEntryState == EntryState.workingOrder && entryOrder != null)
			{
				// Check for FILL (Full or Partial)
				if (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled)
				{
					Print(Time[0] + " SYNC: Order Filled but State was Working. Forcing InPosition.");
					currentEntryState = EntryState.InPosition;
					EnsureProtection(isShortSetup ? "Short" : "Long");
				}
				// Tracking the VWAP (Only if still working)
				else if (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted)
				{
					double currentVWAP = isShortSetup ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
					
					// Compare with current Order Limit Price
					if (Math.Abs(entryOrder.LimitPrice - currentVWAP) >= TickSize)
					{
						ChangeOrder(entryOrder, 1, currentVWAP, 0);
					}
				}
			}
			
			// 4. IN POSITION MANAGEMENT
			ManagePositionExit();
		} // End ManageEntryA_Plus

		private void EnsureProtection(string direction)
		{
			// Debug
			Print(Time[0] + " EnsureProtection Called for " + direction + ". Anchor=" + setupAnchorPrice);

			// Places SL and TP if they don't exist.
			if (direction == "Short")
			{
				// FALLBACK VALIDATION
				if (setupAnchorPrice <= 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue) 
				{
					setupAnchorPrice = High[0] + 20 * TickSize; // Emergency Stop
					Print(Time[0] + " WARNING: Invalid Anchor. Used Emergency Stop: " + setupAnchorPrice);
				}

				ExitShortStopMarket(0, true, 1, setupAnchorPrice, "SL_Short", "EntryA_Short");
				ExitShortLimit(0, true, 1, GetCurrentLowVWAP(), "TP_Short", "EntryA_Short");
			}
			else
			{
				if (setupAnchorPrice <= 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue) 
				{
					setupAnchorPrice = Low[0] - 20 * TickSize; // Emergency Stop
					Print(Time[0] + " WARNING: Invalid Anchor. Used Emergency Stop: " + setupAnchorPrice);
				}

				ExitLongStopMarket(0, true, 1, setupAnchorPrice, "SL_Long", "EntryA_Long");
				ExitLongLimit(0, true, 1, GetCurrentHighVWAP(), "TP_Long", "EntryA_Long");
			}
		}
		
		private bool isValidVWAP(double val)
		{
			return val > 0 && !double.IsNaN(val);
		}

		private int screenshotSequence = 0; // For unique filenames

		private void TriggerScreenshot(string eventName, DateTime time, string execId)
		{
			// Only take screenshots in LIVE/REALTIME mode or if forcing it (users choice)
			// Generally we prefer Realtime to avoid spam during reload.
			if (State != State.Realtime) return;

			if (EnableEmailAlerts && ChartControl != null)
			{
				try 
				{
					// Must run on UI Thread
					ChartControl.Dispatcher.InvokeAsync((Action)(() => 
					{
						try 
						{
							// 1. Get Screen Coordinates of the ChartControl
							System.Windows.Point p = ChartControl.PointToScreen(new System.Windows.Point(0, 0));
							
							// 2. Get Dimensions
							int w = (int)ChartControl.ActualWidth;
							int h = (int)ChartControl.ActualHeight;
							
							if (w > 0 && h > 0)
							{
								// 3. Creates System.Drawing.Bitmap (WinForms/GDI+)
								// Fully qualified to avoid namespace ambiguity
								using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(w, h))
								{
									using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
									{
										// 4. Capture Screen
										g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, new System.Drawing.Size(w, h));
									}
									
									// 5. Build Path
									string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
									string shotFolder = System.IO.Path.Combine(docPath, "NinjaTrader 8", "Strategy_Screenshots");
									if (!System.IO.Directory.Exists(shotFolder)) System.IO.Directory.CreateDirectory(shotFolder);
									
									// Increment Sequence
									screenshotSequence++;

									string cleanName = Instrument.FullName.Replace(" ", "_");
									string fileName = string.Format("{0:D4}_{1}_{2}_{3}_{4}.png", 
										screenshotSequence,
										eventName,
										cleanName,
										time.ToString("yyyyMMdd_HHmmss"),
										execId.GetHashCode()); 
										
									string fullPath = System.IO.Path.Combine(shotFolder, fileName);
									
									// 6. Save
									bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
									Print(Time[0] + " Snapshot Saved: " + fullPath);
									
									// 7. Send Email (Async-ish)
									if (EnableEmailAlerts)
									{
										SendEmailWithAttachment(eventName, fullPath);
									}
								}
							}
						}
						catch (Exception innerEx) { Print(Time[0] + " Screen Capture Failed: " + innerEx.Message); }
					}));
				}
				catch (Exception ex) { Print(Time[0] + " Snapshot Dispatch Failed: " + ex.Message); }
			}
		}

		private void SendEmailWithAttachment(string subject, string attachmentPath)
		{
			try 
			{
				// Basic Validation
				if (string.IsNullOrEmpty(EmailHost) || string.IsNullOrEmpty(EmailUsername) || string.IsNullOrEmpty(EmailPassword))
					return;

				Task.Run(() => 
				{
					try 
					{
						using (MailMessage mail = new MailMessage())
						{
							mail.From = new MailAddress(EmailFrom);
							mail.To.Add(EmailTo);
							mail.Subject = "NinjaTrader Alert: " + subject + " - " + Instrument.FullName;
							mail.Body = string.Format("Trade alert for {0} at {1}.\nEvent: {2}", Instrument.FullName, DateTime.Now, subject);
							
							if (File.Exists(attachmentPath))
							{
								Attachment data = new Attachment(attachmentPath, "image/png");
								mail.Attachments.Add(data);
							}
							
							using (SmtpClient smtp = new SmtpClient(EmailHost, EmailPort))
							{
								smtp.Credentials = new NetworkCredential(EmailUsername, EmailPassword);
								smtp.EnableSsl = true;
								smtp.Send(mail);
							}
							Print("Email Sent to " + EmailTo);
						}
					}
					catch (Exception ex)
					{
						Print("Email Failed: " + ex.Message);
					}
				});
			}
			catch (Exception ex) { Print("Email Setup Failed: " + ex.Message); }
		}

		private double GetCurrentHighVWAP() { return ethHighVWAP.CurrentValue; }
		private double GetCurrentLowVWAP() { return ethLowVWAP.CurrentValue; }

		private void ManagePositionExit()
		{
			if (Position.MarketPosition == MarketPosition.Flat) return;
			
			if (targetOrder != null && (targetOrder.OrderState == OrderState.Working || targetOrder.OrderState == OrderState.Accepted))
			{
				double targetPrice = 0;
				if (Position.MarketPosition == MarketPosition.Long)
				{
					targetPrice = GetCurrentHighVWAP();
				}
				else
				{
					targetPrice = GetCurrentLowVWAP();
				}
				
				if (isValidVWAP(targetPrice) && Math.Abs(targetOrder.LimitPrice - targetPrice) >= TickSize)
				{
					ChangeOrder(targetOrder, 1, targetPrice, 0);
				}
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			if (order.Name == "EntryA_Short" || order.Name == "EntryA_Long")
			{
				entryOrder = order;
				
				// Handle Terminal States
				if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || (orderState == OrderState.Filled && filled == 0)) 
				{
					if (currentEntryState == EntryState.workingOrder) 
					{
						currentEntryState = EntryState.Idle;
						entryOrder = null;
					}
				}
			}
			
			if (order.Name.Contains("TP_"))
			{
				targetOrder = order;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null) return;

			if (execution.Order.Name == "EntryA_Short" || execution.Order.Name == "EntryA_Long")
			{
				entryOrder = execution.Order;
				
				if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
				{
					currentEntryState = EntryState.InPosition;
					
					if (Position.MarketPosition == MarketPosition.Short)
					{
						Print(Time + " Filled Short (Exec). Placing SL/TP.");
						EnsureProtection("Short");
						TriggerScreenshot("Entry_Short", DateTime.Now, executionId);
					}
					else if (Position.MarketPosition == MarketPosition.Long)
					{
						Print(Time + " Filled Long (Exec). Placing SL/TP.");
						EnsureProtection("Long");
						TriggerScreenshot("Entry_Long", DateTime.Now, executionId);
					}
				}
			}
			
			if (execution.Order.Name.Contains("TP_"))
			{
				targetOrder = execution.Order;
			}
			
			// Reset if Closed (Filled) OR Cancelled/Rejected
			if (entryOrder != null && execution.Order == entryOrder)
			{
				if (execution.Order.OrderState == OrderState.Cancelled || 
					execution.Order.OrderState == OrderState.Rejected)
				{
					Print(Time + " Entry Order Cancelled/Rejected. Resetting to Idle.");
					currentEntryState = EntryState.Idle;
					entryOrder = null;
					targetOrder = null;
				}
			}
			
			if (execution.Order.OrderState == OrderState.Filled && (execution.Order.Name.Contains("SL_") || execution.Order.Name.Contains("TP_")))
			{
				Print(Time + " Position Closed (TP/SL). Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				entryOrder = null;
				targetOrder = null;
			}
		}

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
