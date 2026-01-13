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
using NinjaTrader.Core;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	public class RelativeDualConfluence : Indicator
	{
		#region Variables
		
		// Session 1 (Primary)
		private double 	primVolSum;
		private double 	primPvSum;
		private double 	primSumSquaredDiffs;
		private int		primCount;
		private bool	insidePrim;

		// Session 2 (Secondary)
		private double 	secVolSum;
		private double 	secPvSum;
		private double 	secSumSquaredDiffs;
		private int		secCount;
		private bool	insideSec;

		// Confluence Rendering
		private SharpDX.Direct2D1.Brush confluenceBrushDX;
		private Vector2[] cloudArray;
		
		// Config
		private string	primStartTime	= "09:30";
		private string	primEndTime		= "16:15";
		private string	secStartTime	= "18:00";
		private string	secEndTime		= "09:30";
		
		private TimeSpan tsPrimStart, tsPrimEnd;
		private TimeSpan tsSecStart, tsSecEnd;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Nueva versión optimizada para visualizar la confluencia (intersección) de dos sesiones.";
				Name						= "Relative Dual Confluence v1.0";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				DrawHorizontalGridLines		= true;
				DrawVerticalGridLines		= true;
				PaintPriceMarkers			= true;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= true;

				// Plots
				// Primary (0-3)
				AddPlot(new Stroke(Brushes.DodgerBlue, 1), PlotStyle.Line, "Prim Upper 3");
				AddPlot(new Stroke(Brushes.DodgerBlue, 1), PlotStyle.Line, "Prim Upper 2");
				AddPlot(new Stroke(Brushes.DodgerBlue, 1), PlotStyle.Line, "Prim Lower 2");
				AddPlot(new Stroke(Brushes.DodgerBlue, 1), PlotStyle.Line, "Prim Lower 3");

				// Secondary (4-7)
				AddPlot(new Stroke(Brushes.Orange, DashStyleHelper.Dash, 1), PlotStyle.Line, "Sec Upper 3");
				AddPlot(new Stroke(Brushes.Orange, DashStyleHelper.Dash, 1), PlotStyle.Line, "Sec Upper 2");
				AddPlot(new Stroke(Brushes.Orange, DashStyleHelper.Dash, 1), PlotStyle.Line, "Sec Lower 2");
				AddPlot(new Stroke(Brushes.Orange, DashStyleHelper.Dash, 1), PlotStyle.Line, "Sec Lower 3");

				ConfluenceColor 			= Brushes.Cyan;
				ConfluenceOpacity			= 30;
			}
			else if (State == State.Configure)
			{
				// Parse Times
				TimeSpan.TryParse(primStartTime, out tsPrimStart);
				TimeSpan.TryParse(primEndTime, out tsPrimEnd);
				TimeSpan.TryParse(secStartTime, out tsSecStart);
				TimeSpan.TryParse(secEndTime, out tsSecEnd);
			}
			else if (State == State.DataLoaded)
			{
				cloudArray = new Vector2[2000]; // Pre-allocate buffer
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1) return;

			// --- 1. Determine Session Status ---
			TimeSpan now = Time[0].TimeOfDay;
			TimeSpan prev = Time[1].TimeOfDay;

			// Check Primary
			bool wasInPrim = IsInSession(prev, tsPrimStart, tsPrimEnd);
			insidePrim = IsInSession(now, tsPrimStart, tsPrimEnd);
			
			bool isNewDate = Time[0].Date != Time[1].Date;
			
			// Detect Reset Primary
			bool primCrosses = tsPrimStart > tsPrimEnd;
			bool resetPrim = (insidePrim && !wasInPrim); // Normal transition
			
			// Gap/Date Reset Logic
			if (insidePrim && !resetPrim && isNewDate)
			{
				if (!primCrosses) resetPrim = true;
				else if (now >= tsPrimStart) resetPrim = true;
			}

			if (resetPrim) 
			{
				// Debug
				Print("Reset Primary at " + Time[0] + " CurrentBar: " + CurrentBar);
				ResetPrimary();
			}
			else if (!insidePrim) primCount = 0;

			// Check Secondary
			bool wasInSec = IsInSession(prev, tsSecStart, tsSecEnd);
			insideSec = IsInSession(now, tsSecStart, tsSecEnd);
			
			// Detect Reset Secondary
			bool secCrosses = tsSecStart > tsSecEnd;
			bool resetSec = (insideSec && !wasInSec);
			
			if (insideSec && !resetSec && isNewDate)
			{
				if (!secCrosses) resetSec = true;
				else if (now >= tsSecStart) resetSec = true;
			}
			
			if (resetSec) 
			{
				Print("Reset Secondary at " + Time[0]);
				ResetSecondary();
			}
			else if (!insideSec) secCount = 0;

			// --- 2. Calculate Primary ---
			if (insidePrim)
			{
				CalculateSession(ref primVolSum, ref primPvSum, ref primSumSquaredDiffs, ref primCount,
					out double vwap, out double sd);

				// Debug
				if (CurrentBar % 100 == 0) Print(Time[0] + " Prim Active. VWAP: " + vwap + " SD: " + sd + " Count: " + primCount);

				// Assign Values
				if (primCount > 0)
				{
					Values[0][0] = vwap + (3.0 * sd); // P+3
					Values[1][0] = vwap + (2.0 * sd); // P+2
					Values[2][0] = vwap - (2.0 * sd); // P-2
					Values[3][0] = vwap - (3.0 * sd); // P-3
				}
			}
			else
			{
				// Hold last values or NaN? Usually NaN to clear non-session data
				// But to see lines, maybe hold? Let's use NaN to be clean.
				Values[0][0] = double.NaN; Values[1][0] = double.NaN;
				Values[2][0] = double.NaN; Values[3][0] = double.NaN;
			}

			// --- 3. Calculate Secondary ---
			if (insideSec)
			{
				CalculateSession(ref secVolSum, ref secPvSum, ref secSumSquaredDiffs, ref secCount,
					out double vwap, out double sd);

				if (CurrentBar % 100 == 0) Print(Time[0] + " Sec Active. VWAP: " + vwap + " SD: " + sd);

				if (secCount > 0)
				{
					Values[4][0] = vwap + (3.0 * sd); // S+3
					Values[5][0] = vwap + (2.0 * sd); // S+2
					Values[6][0] = vwap - (2.0 * sd); // S-2
					Values[7][0] = vwap - (3.0 * sd); // S-3
				}
			}
			else
			{
				Values[4][0] = double.NaN; Values[5][0] = double.NaN;
				Values[6][0] = double.NaN; Values[7][0] = double.NaN;
			}
		}

		private bool IsInSession(TimeSpan t, TimeSpan start, TimeSpan end)
		{
			if (start < end) return (t >= start && t < end);
			return (t >= start || t < end); // Wraps midnight
		}

		private void ResetPrimary()
		{
			primVolSum = 0; 
			primPvSum = 0; 
			primSumSquaredDiffs = 0;
			primCount = 0;
		}

		private void ResetSecondary()
		{
			secVolSum = 0; 
			secPvSum = 0; 
			secSumSquaredDiffs = 0;
			secCount = 0;
		}

		private void CalculateSession(ref double vSum, ref double pvSum, ref double sqSum, ref int count, out double vwap, out double sd)
		{
			double vol = Volume[0];
			double price = (High[0] + Low[0] + Close[0]) / 3.0;

			// Reset logic handled by caller? No, accumulative.
			// New session start should zero these out.
			// Current accumulation:
			vSum += vol;
			pvSum += (vol * price);
			sqSum += (vol * price * price);
			count++;

			vwap = 0; sd = 0;
			if (vSum > 0)
			{
				vwap = pvSum / vSum;
				// Variance = E[X^2] - (E[X])^2
				// Mean of Squares = sqSum / vSum
				double meanOfSquares = sqSum / vSum;
				double variance = meanOfSquares - (vwap * vwap);
				if (variance > 0) sd = Math.Sqrt(variance);
			}
		}

		public override void OnRenderTargetChanged()
		{
			if (confluenceBrushDX != null) confluenceBrushDX.Dispose();

			if (RenderTarget != null)
			{
				try
				{
					// Convert WPF brush to DX brush with opacity
					confluenceBrushDX = ConfluenceColor.ToDxBrush(RenderTarget);
					// Force opacity override
					if (confluenceBrushDX is SharpDX.Direct2D1.SolidColorBrush scb)
						scb.Opacity = (float)(ConfluenceOpacity / 100.0);
				}
				catch {}
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Bars == null || ChartBars == null || confluenceBrushDX == null) return;
			
			// Render Overlap if enabled
			// Require both sessions to be potentially active? Or just data validity.
			
			int lastPlotIndex = ChartBars.FromIndex + ChartBars.Count - 1; // Correct visible range end
			int firstPlotIndex = ChartBars.FromIndex;
			int displacement = Displacement;

			// Ensure array size
			int pointsNeeded = (lastPlotIndex - firstPlotIndex + 1) * 2 + 10;
			if (cloudArray == null || cloudArray.Length < pointsNeeded)
				cloudArray = new Vector2[pointsNeeded + 100];

			SharpDX.Direct2D1.PathGeometry pathConf = null;
			SharpDX.Direct2D1.GeometrySink sinkConf = null;

			try 
			{
				pathConf = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
				sinkConf = pathConf.Open();
				
				// --- UPPER INTERSECTION (+2 to +3) ---
				// P3=Val[0], P2=Val[1]. S3=Val[4], S2=Val[5].
				// Intersection Top = Min(P3, S3).
				// Intersection Bot = Max(P2, S2).
				
				int count = -1;
				int returnBar = firstPlotIndex;
				
				// Pass 1: Top Edge (Right to Left)
				for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx--)
				{
					int iVal = idx - displacement;
					if (iVal < 0 || iVal >= BarsArray[0].Count) continue;

					double p3 = Values[0].IsValidDataPointAt(iVal) ? Values[0].GetValueAt(iVal) : 0;
					double s3 = Values[4].IsValidDataPointAt(iVal) ? Values[4].GetValueAt(iVal) : 0;
					double p2 = Values[1].IsValidDataPointAt(iVal) ? Values[1].GetValueAt(iVal) : 0;
					double s2 = Values[5].IsValidDataPointAt(iVal) ? Values[5].GetValueAt(iVal) : 0;

					bool valid = (p3 > 0.0001 && s3 > 0.0001 && p2 > 0.0001 && s2 > 0.0001 &&
								  p3 < 2000000 && s3 < 2000000); // Filter insanity

					double yVal = 0;
					if (valid)
					{
						double intTop = Math.Min(p3, s3);
						double intBot = Math.Max(p2, s2);
						double val = (intTop > intBot) ? intTop : intBot;
						yVal = chartScale.GetYByValue(val);
					}
					else 
					{ 
						// Break segment loop if data invalid
						// But for simplicity in one-pass, we just draw flat line or clamp?
						// Better: Break figure.
						// Simplest: Check if valid, if not, skip/break.
						// If we break, we need to restart figure. 
						// For this basic version, let's just break for now.
						if (count >= 0) break; // End current figure
						continue;
					}

					float x = ChartControl.GetXByBarIndex(ChartBars, idx);
					float y = (float)Math.Max(-5000, Math.Min(yVal, ChartControl.ActualHeight + 5000));
					
					returnBar = idx;
					count++;
					cloudArray[count] = new Vector2(x, y);
				}
				
				// Pass 2: Bottom Edge (Left to Right)
				if (count >= 0)
				{
					for (int idx = returnBar; idx <= lastPlotIndex; idx++)
					{
						int iVal = idx - displacement;
						double p3 = Values[0].GetValueAt(iVal);
						double s3 = Values[4].GetValueAt(iVal);
						double p2 = Values[1].GetValueAt(iVal);
						double s2 = Values[5].GetValueAt(iVal);

						double intTop = Math.Min(p3, s3);
						double intBot = Math.Max(p2, s2);
						double val = intBot;
						
						double yVal = chartScale.GetYByValue(val);
						float x = ChartControl.GetXByBarIndex(ChartBars, idx);
						float y = (float)Math.Max(-5000, Math.Min(yVal, ChartControl.ActualHeight + 5000));

						count++;
						cloudArray[count] = new Vector2(x, y);
					}

					sinkConf.BeginFigure(cloudArray[0], FigureBegin.Filled);
					for (int i=1; i<=count; i++) sinkConf.AddLine(cloudArray[i]);
					sinkConf.EndFigure(FigureEnd.Closed);
				}
				
				// --- LOWER INTERSECTION (-3 to -2) ---
				// P2_Low=Val[2](-2), P3_Low=Val[3](-3).
				// S2_Low=Val[6](-2), S3_Low=Val[7](-3).
				// Visually Top (-2) is Higher Value.
				// Intersection Top (-2) = Min(P2_Low, S2_Low).
				// Intersection Bot (-3) = Max(P3_Low, S3_Low).
				
				count = -1;
				// Pass 1: Top Edge (Right to Left) -> Min(-2)
				for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx--)
				{
					int iVal = idx - displacement;
					if (iVal < 0 || iVal >= BarsArray[0].Count) continue;

					double p2 = Values[2].IsValidDataPointAt(iVal) ? Values[2].GetValueAt(iVal) : 0;
					double s2 = Values[6].IsValidDataPointAt(iVal) ? Values[6].GetValueAt(iVal) : 0;
					double p3 = Values[3].IsValidDataPointAt(iVal) ? Values[3].GetValueAt(iVal) : 0;
					double s3 = Values[7].IsValidDataPointAt(iVal) ? Values[7].GetValueAt(iVal) : 0;

					bool valid = (p2 > 0.0001 && s2 > 0.0001 && p3 > 0.0001 && s3 > 0.0001 &&
								  p2 < 2000000 && s2 < 2000000);

					double yVal = 0;
					if (valid)
					{
						double intTop = Math.Min(p2, s2);
						double intBot = Math.Max(p3, s3);
						double val = (intTop > intBot) ? intTop : intBot;
						yVal = chartScale.GetYByValue(val);
					}
					else 
					{ 
						if (count >= 0) break;
						continue;
					}

					float x = ChartControl.GetXByBarIndex(ChartBars, idx);
					float y = (float)Math.Max(-5000, Math.Min(yVal, ChartControl.ActualHeight + 5000));
					
					returnBar = idx;
					count++;
					cloudArray[count] = new Vector2(x, y);
				}

				// Pass 2: Bottom Edge (Left to Right)
				if (count >= 0)
				{
					for (int idx = returnBar; idx <= lastPlotIndex; idx++)
					{
						int iVal = idx - displacement;
						double p2 = Values[2].GetValueAt(iVal);
						double s2 = Values[6].GetValueAt(iVal);
						double p3 = Values[3].GetValueAt(iVal);
						double s3 = Values[7].GetValueAt(iVal);

						double intTop = Math.Min(p2, s2);
						double intBot = Math.Max(p3, s3);
						double val = intBot;
						
						double yVal = chartScale.GetYByValue(val);
						float x = ChartControl.GetXByBarIndex(ChartBars, idx);
						float y = (float)Math.Max(-5000, Math.Min(yVal, ChartControl.ActualHeight + 5000));

						count++;
						cloudArray[count] = new Vector2(x, y);
					}

					sinkConf.BeginFigure(cloudArray[0], FigureBegin.Filled);
					for (int i=1; i<=count; i++) sinkConf.AddLine(cloudArray[i]);
					sinkConf.EndFigure(FigureEnd.Closed);
				}

				sinkConf.Close();
				RenderTarget.FillGeometry(pathConf, confluenceBrushDX);
			}
			catch (Exception e) {}
			finally
			{
				if (sinkConf != null) sinkConf.Dispose();
				if (pathConf != null) pathConf.Dispose();
			}
			
			// Restore default Plot rendering (Lines)
			base.OnRender(chartControl, chartScale);
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Primary Start Time", GroupName="Settings", Order=1)]
		public string PrimStartTime
		{ get { return primStartTime; } set { primStartTime = value; } }

		[NinjaScriptProperty]
		[Display(Name="Primary End Time", GroupName="Settings", Order=2)]
		public string PrimEndTime
		{ get { return primEndTime; } set { primEndTime = value; } }

		[NinjaScriptProperty]
		[Display(Name="Secondary Start Time", GroupName="Settings", Order=3)]
		public string SecStartTime
		{ get { return secStartTime; } set { secStartTime = value; } }

		[NinjaScriptProperty]
		[Display(Name="Secondary End Time", GroupName="Settings", Order=4)]
		public string SecEndTime
		{ get { return secEndTime; } set { secEndTime = value; } }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Confluence Color", GroupName="Visual", Order=5)]
		public Brush ConfluenceColor { get; set; }
		[Browsable(false)] public string ConfluenceColorSerializable
		{ get { return Serialize.BrushToString(ConfluenceColor); } set { ConfluenceColor = Serialize.StringToBrush(value); } }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="Confluence Opacity", GroupName="Visual", Order=6)]
		public int ConfluenceOpacity { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeDualConfluence[] cacheRelativeDualConfluence;
		public RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			return RelativeDualConfluence(Input, primStartTime, primEndTime, secStartTime, secEndTime, confluenceColor, confluenceOpacity);
		}

		public RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(ISeries<double> input, string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			if (cacheRelativeDualConfluence != null)
				for (int idx = 0; idx < cacheRelativeDualConfluence.Length; idx++)
					if (cacheRelativeDualConfluence[idx] != null && cacheRelativeDualConfluence[idx].PrimStartTime == primStartTime && cacheRelativeDualConfluence[idx].PrimEndTime == primEndTime && cacheRelativeDualConfluence[idx].SecStartTime == secStartTime && cacheRelativeDualConfluence[idx].SecEndTime == secEndTime && cacheRelativeDualConfluence[idx].ConfluenceColor == confluenceColor && cacheRelativeDualConfluence[idx].ConfluenceOpacity == confluenceOpacity && cacheRelativeDualConfluence[idx].EqualsInput(input))
						return cacheRelativeDualConfluence[idx];
			return CacheIndicator<RelativeIndicators.RelativeDualConfluence>(new RelativeIndicators.RelativeDualConfluence(){ PrimStartTime = primStartTime, PrimEndTime = primEndTime, SecStartTime = secStartTime, SecEndTime = secEndTime, ConfluenceColor = confluenceColor, ConfluenceOpacity = confluenceOpacity }, input, ref cacheRelativeDualConfluence);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			return indicator.RelativeDualConfluence(Input, primStartTime, primEndTime, secStartTime, secEndTime, confluenceColor, confluenceOpacity);
		}

		public Indicators.RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(ISeries<double> input , string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			return indicator.RelativeDualConfluence(input, primStartTime, primEndTime, secStartTime, secEndTime, confluenceColor, confluenceOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			return indicator.RelativeDualConfluence(Input, primStartTime, primEndTime, secStartTime, secEndTime, confluenceColor, confluenceOpacity);
		}

		public Indicators.RelativeIndicators.RelativeDualConfluence RelativeDualConfluence(ISeries<double> input , string primStartTime, string primEndTime, string secStartTime, string secEndTime, Brush confluenceColor, int confluenceOpacity)
		{
			return indicator.RelativeDualConfluence(input, primStartTime, primEndTime, secStartTime, secEndTime, confluenceColor, confluenceOpacity);
		}
	}
}

#endregion
