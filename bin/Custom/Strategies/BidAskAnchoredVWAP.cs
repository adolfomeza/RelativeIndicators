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
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class BidAskAnchoredVWAP : Indicator
	{
		// Accumulators for High Anchor
		private double highAnchorPrice;
		private double highBidVolSum, highAskVolSum;
		private double highBidPvSum, highAskPvSum;
		private int highAnchorBar;

		// Accumulators for Low Anchor
		private double lowAnchorPrice;
		private double lowBidVolSum, lowAskVolSum;
		private double lowBidPvSum, lowAskPvSum;
		private int lowAnchorBar;

		// Session State (Reset daily or per session)
		private bool inSession;
		private DateTime currentSessionDate;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Anchored VWAP that automatically resets on Session High/Low and splits Bid/Ask volume.";
				Name										= "BidAskAnchoredVWAP";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;

				// Default Parameters (ETH Defaults)
				StartTime 									= DateTime.Parse("18:00");
				EndTime 									= DateTime.Parse("17:00");
				
				// Plots
				// Plots (White Active, 2px Width)
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "High_AskVWAP");   // Resistance
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "High_BidVWAP");   // Resistance
				
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "Low_AskVWAP");    // Support
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "Low_BidVWAP");    // Support
			}
			else if (State == State.DataLoaded)
			{
				ResetSession();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1) return;

			// 1. Session Management
			DateTime now = Time[0];
			DateTime sessionStart = now.Date + StartTime.TimeOfDay;
			DateTime sessionEnd = now.Date + EndTime.TimeOfDay;

			// Handle Overnight Session (e.g., 18:00 to 17:00)
			if (EndTime.TimeOfDay < StartTime.TimeOfDay)
			{
				// If strictly overnight definition
				// If we are currently BEFORE the start time (e.g. 11:00 < 18:00), 
				// then the session actually started YESTERDAY.
				if (now.TimeOfDay < StartTime.TimeOfDay)
				{
					sessionStart = now.Date.AddDays(-1) + StartTime.TimeOfDay;
					sessionEnd = now.Date + EndTime.TimeOfDay;
				}
				else
				{
					// We are after start time (e.g. 20:00 > 18:00), session starts today.
					sessionEnd = sessionEnd.AddDays(1);
				}
			}
			else
			{
				// Intraday Session (e.g., 09:30 to 16:00)
				// Standard logic
			}

			bool isInsideSession = (now >= sessionStart && now <= sessionEnd);

			// New Session Detection
			// Critical: If sessionStart changes (new day), trigger reset.
			// Compare sessionStart reference, not just 'currentSessionDate' which was vague.
			if (sessionStart != currentSessionDate || (!inSession && isInsideSession))
			{
				ResetSession();
				currentSessionDate = sessionStart; // Track the HEAD of the session
			}
			inSession = isInsideSession;

			if (!inSession) return;

			// 2. High/Low Detection & Re-Anchoring
			double high = High[0];
			double low = Low[0];
			double close = Close[0];
			
			// Detect New Session High
			if (high > highAnchorPrice)
			{
				// MITIGATION HAPPENED: Recolor the failed resistance to Gray
				if (highAnchorBar > 0 && CurrentBar > highAnchorBar)
				{
					for (int i = 0; i < (CurrentBar - highAnchorBar); i++)
					{
						int barsBack = i; 
						// Careful: PlotBrushes uses [barsBack] (offset from current)
						// Wait, PlotBrushes[0][barsAgo] access is absolute or relative?
						// It works like Series. PlotBrushes[0][0] is current. PlotBrushes[0][1] is 1 ago.
						// We want to color FROM 1 bar ago TO (CurrentBar - lowAnchorBar) bars ago.
						
						PlotBrushes[0][i+1] = Brushes.DarkGray; // Recolor History Ask
						PlotBrushes[1][i+1] = Brushes.Gray;     // Recolor History Bid
					}
					// Cut the jump
					PlotBrushes[0][0] = Brushes.Transparent;
					PlotBrushes[1][0] = Brushes.Transparent;
				}

				// New High -> Reset High Anchors
				highAnchorPrice = high;
				highAnchorBar = CurrentBar;
				highBidVolSum = 0; highAskVolSum = 0;
				highBidPvSum = 0; highAskPvSum = 0;
			}

			// Detect New Session Low
			if (low < lowAnchorPrice)
			{
				// MITIGATION HAPPENED: Recolor the failed support to Gray
				if (lowAnchorBar > 0 && CurrentBar > lowAnchorBar)
				{
					for (int i = 0; i < (CurrentBar - lowAnchorBar); i++)
					{
						PlotBrushes[2][i+1] = Brushes.DarkGray; // Recolor History Ask
						PlotBrushes[3][i+1] = Brushes.Gray;     // Recolor History Bid
					}
					// Cut the jump
					PlotBrushes[2][0] = Brushes.Transparent;
					PlotBrushes[3][0] = Brushes.Transparent;
				}

				// New Low -> Reset Low Anchors
				lowAnchorPrice = low;
				lowAnchorBar = CurrentBar;
				lowBidVolSum = 0; lowAskVolSum = 0;
				lowBidPvSum = 0; lowAskPvSum = 0;
			}

			// 3. Volume Approximation (Candle Direction)
			// (True Bid/Ask requires Volumetric Bars or complex aggregation)
			double askVol = 0;
			double bidVol = 0;
			
			if (Close[0] > Open[0]) 
			{
				askVol = Volume[0]; 
				bidVol = 0;
			}
			else if (Close[0] < Open[0])
			{
				askVol = 0;
				bidVol = Volume[0];
			}
			else
			{
				// Neutral
				askVol = Volume[0] * 0.5;
				bidVol = Volume[0] * 0.5;
			}

			double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;

			// Update High Anchor Accumulators
			highAskVolSum += askVol;
			highBidVolSum += bidVol;
			highAskPvSum += (askVol * typicalPrice);
			highBidPvSum += (bidVol * typicalPrice);

			// Update Low Anchor Accumulators
			lowAskVolSum += askVol;
			lowBidVolSum += bidVol;
			lowAskPvSum += (askVol * typicalPrice);
			lowBidPvSum += (bidVol * typicalPrice);

			// 4. Calculate & Plot
			// 4. Calculate & Plot
			// HIGH ANCHOR
			if (highAskVolSum > 0) Values[0][0] = highAskPvSum / highAskVolSum; // High_AskVWAP
			else Values[0][0] = highAnchorPrice;

			if (highBidVolSum > 0) Values[1][0] = highBidPvSum / highBidVolSum; // High_BidVWAP
			else Values[1][0] = highAnchorPrice;

			// LOW ANCHOR
			if (lowAskVolSum > 0) Values[2][0] = lowAskPvSum / lowAskVolSum; // Low_AskVWAP
			else Values[2][0] = lowAnchorPrice;

			if (lowBidVolSum > 0) Values[3][0] = lowBidPvSum / lowBidVolSum; // Low_BidVWAP
			else Values[3][0] = lowAnchorPrice;

			// DEBUG VISUALS
			// Mark the Anchors
			if (CurrentBar == lowAnchorBar) Draw.Diamond(this, "LowAnchor" + currentSessionDate, true, 0, Low[0] - TickSize * 5, Brushes.LimeGreen);
			if (CurrentBar == highAnchorBar) Draw.Diamond(this, "HighAnchor" + currentSessionDate, true, 0, High[0] + TickSize * 5, Brushes.Red);
			
			// Show Active Config
			if (CurrentBar == Count - 2) 
				Draw.TextFixed(this, "Info", "BidAskVWAP v3 (ETH Fixed)\nSession: " + StartTime.ToShortTimeString() + " - " + EndTime.ToShortTimeString(), TextPosition.BottomRight);
		}

		private void ResetSession()
		{
			highAnchorPrice = double.MinValue;
			lowAnchorPrice = double.MaxValue;
			
			highBidVolSum = 0; highAskVolSum = 0;
			highBidPvSum = 0; highAskPvSum = 0;
			
			lowBidVolSum = 0; lowAskVolSum = 0;
			lowBidPvSum = 0; lowAskPvSum = 0;
			
			currentSessionDate = DateTime.MinValue;
			inSession = false;
		}

		#region Properties
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="Start Time", Description="Session Start Time", Order=1, GroupName="Parameters")]
		public DateTime StartTime
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="End Time", Description="Session End Time", Order=2, GroupName="Parameters")]
		public DateTime EndTime
		{ get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> High_AskVWAP
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> High_BidVWAP
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Low_AskVWAP
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Low_BidVWAP
		{
			get { return Values[3]; }
		}
		#endregion
	}
}
