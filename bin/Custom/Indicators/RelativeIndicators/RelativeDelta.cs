
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
using System.Windows.Controls;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — RLog + Registry
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;

#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	public class RelativeDelta : Indicator
	{
		private double		buys 	= 1;
		private double 		sells 	= 1;
		private double		cdHigh 	= 1;
		private double 		cdLow 	= 1;
		private double		cdOpen 	= 1;
		private double 		cdClose	= 1;
		private int										barPaintWidth;
		private Dictionary<string, DXMediaMap>			dxmBrushes;
		private SharpDX.RectangleF						reuseRect;
		private SharpDX.Vector2							reuseVector1, reuseVector2;
		private double									tmpMax, tmpMin, tmpPlotVal;
		private int										x, y1, y2, y3, y4;
		private Series<Double> delta_open;
		private Series<Double> delta_close;
		private Series<Double> delta_high;
		private Series<Double> delta_low;		
		

		
		private bool	isReset;

		private int 	lastBar;
		private bool 	lastInTransition;

		// v1.16: Delta history export para backtest Apteros
		private string _deltaHistoryDir;
		private string _currentDeltaFile = "";
		private DateTime _lastDeltaExportDate = DateTime.MinValue;
		private System.Text.StringBuilder _deltaBuffer = new System.Text.StringBuilder();
		private DateTime _lastDeltaFlushTime = DateTime.MinValue;
		
		private Brush	divergeCandleup   = Brushes.Purple;  // Color body for Divergence Candle
		private Brush	divergeCandledown   = Brushes.Pink;  // Color body for Divergence Candle
		

		private double usSessionAnchor = double.MinValue; // V_ZERO_LINE: Anchor Value
		private int    usSessionAnchorIdx = -1; // V_ZERO_LINE: Anchor Bar Index
		private bool   usSessionActive = false;
		private TimeSpan usStartTimeTs; // Cached TimeSpan
		private TimeSpan usEndTimeTs;
        
        // ASIA Session
        private double asiaSessionAnchor = double.MinValue;
        private int    asiaSessionAnchorIdx = -1;
        private bool   asiaSessionActive = false;
        private TimeSpan asiaStartTimeTs;
        private TimeSpan asiaEndTimeTs;

        // EU Session
        private double euSessionAnchor = double.MinValue;
        private int    euSessionAnchorIdx = -1;
        private bool   euSessionActive = false;
        private TimeSpan euStartTimeTs;
        private TimeSpan euEndTimeTs;
        
        // GLOBAL Session
        private double globalSessionAnchor = double.MinValue;
        private int    globalSessionAnchorIdx = -1;
        private bool   globalSessionActive = false;
        private TimeSpan globalStartTimeTs;
        private TimeSpan globalEndTimeTs;
		
		// V_HIST: Historical Lines Logic
		private class HistoricalZeroLine
		{
		    public int StartIdx;
		    public int EndIdx;
		    public double Value;
            public int SessionType; // 0=US, 1=Asia, 2=EU, 3=Global
		}
		private List<HistoricalZeroLine> historicalLines = new List<HistoricalZeroLine>();
		
// Redundant fields removed
		
		private NinjaTrader.NinjaScript.Indicators.Stochastics stoch;
// Duplicate stoch removed
		// Cache for DirectWrite
		private SharpDX.DirectWrite.Factory dwFactory;
		private SharpDX.DirectWrite.TextFormat dwTextFormat;
		
		// PERFORMANCE OPTIMIZATION: Resource Caching
		// Cache for Extra Lines Brushes
		private SharpDX.Direct2D1.Brush dxBrushLine2500, dxBrushLineN2500;
		private SharpDX.Direct2D1.Brush dxBrushLine5000, dxBrushLineN5000;
		private SharpDX.Direct2D1.Brush dxBrushLine10000, dxBrushLineN10000;
		private SharpDX.Direct2D1.Brush dxBrushUSZeroLine;
        private SharpDX.Direct2D1.Brush dxBrushAsiaZeroLine;
        private SharpDX.Direct2D1.Brush dxBrushEUZeroLine;
        private SharpDX.Direct2D1.Brush dxBrushGlobalZeroLine;
        
		private SharpDX.Direct2D1.StrokeStyle dxStrokeDash;
        private SharpDX.Direct2D1.StrokeStyle dxStrokeDashAsia;
        private SharpDX.Direct2D1.StrokeStyle dxStrokeDashEU;
        private SharpDX.Direct2D1.StrokeStyle dxStrokeDashGlobal;
		

		
// V_STRUCTURE: Market Structure Variables REMOVED
		
		// V_ADAPTIVE: Volatility Tracking
		private Series<double> deltaRangeSeries;
		// private SMA volatilitySMA; // Removed to avoid dependency issues
		
		// V_STRUCTURE: Visual State REMOVED

		// Helper to safely dispose
		private void SafeDispose(IDisposable obj)
		{
			if (obj != null) obj.Dispose();
		}
		
		
		
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Relative Delta";
				Name										= "Relative Delta";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= false;
	
				
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				
				dxmBrushes	= new Dictionary<string, DXMediaMap>();
				foreach (string brushName in new string[] { "barColorDown", "barColorUp", "shadowColor", "wickColor" })
					dxmBrushes.Add(brushName, new DXMediaMap());
				BarColorDown								= Brushes.Transparent;
				BarColorUp									= Brushes.White;
				ShadowColor									= Brushes.White;
				ShadowWidth									= 1;
				WickColor									= Brushes.White;
				WickWidth									= 1;
				int MinSize 								= 0;
				ShowDivs 									= false;
				
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaOpen");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaHigh");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaLow");
				AddPlot(new Stroke(Brushes.Orange),PlotStyle.PriceBox,"DeltaClose");
				AddPlot(new Stroke(Brushes.Cyan),PlotStyle.PriceBox,"BarDelta"); // v1.15.63: New plot for per-bar delta verification
				
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
			    // Cache TimeSpan
			    TimeSpan.TryParse(USStartTime, out usStartTimeTs);
			    TimeSpan.TryParse(USEndTime, out usEndTimeTs);
			    
                TimeSpan.TryParse(AsiaStartTime, out asiaStartTimeTs);
                TimeSpan.TryParse(AsiaEndTime, out asiaEndTimeTs);
                
                TimeSpan.TryParse(EUStartTime, out euStartTimeTs);
                TimeSpan.TryParse(EUEndTime, out euEndTimeTs);
                
                TimeSpan.TryParse(GlobalStartTime, out globalStartTimeTs);
                TimeSpan.TryParse(GlobalEndTime, out globalEndTimeTs);
				delta_open = new Series<double>(this);
				delta_close = new Series<double>(this);
				delta_high = new Series<double>(this);
				delta_low = new Series<double>(this);
				
				// V_ADAPTIVE: Init Volatility Series
				deltaRangeSeries = new Series<double>(this);
				

				
				stoch = this.Stochastics(3, 14, 3);

				// v1.16: Delta history directory
				_deltaHistoryDir = System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"NinjaTrader 8", "bin", "Custom", "DeltaHistory");
				if (!System.IO.Directory.Exists(_deltaHistoryDir))
					System.IO.Directory.CreateDirectory(_deltaHistoryDir);

				// Initialize D2D Factory once - NO LONGER NEEDED (Using RenderTarget.Factory)
			}
			else if (State == State.Terminated)
			{
			    DisposeD2DResources(); // Ensure cleanup
			    FlushDeltaBuffer(); // v1.16: flush last buffered delta bars on shutdown
			}
		}
		
		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < 5 || CurrentBars[1] < 5)
				return;
			
			// Performance optimization: Skip calculation for old bars
			// v2.0.1 Fix: In Playback, use the Last Bar Time as "Now" instead of DateTime.Now
			DateTime referenceDate = DateTime.Now;
			if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected && BarsArray[0].Count > 0)
			{
			    referenceDate = BarsArray[0].GetTime(BarsArray[0].Count - 1);
			}

			if (DaysToLoad > 0 && BarsArray[0].GetTime(CurrentBars[0]) < referenceDate.Date.AddDays(-DaysToLoad))
				return;

			if (BarsInProgress == 0)
			{
				
				int indexOffset = BarsArray[1].Count - 1 - CurrentBars[1];
				
				
				if (IsFirstTickOfBar && Calculate != Calculate.OnBarClose && (State == State.Realtime || BarsArray[0].IsTickReplay))
				{
					
					if (CurrentBars[0] > 0)
						SetValues(1);					
					
					if (BarsArray[0].IsTickReplay || State == State.Realtime && indexOffset == 0)
						ResetValues(false,cdClose);
				}
				
				
				SetValues(0);
				
			
				if (Calculate == Calculate.OnBarClose || (lastBar != CurrentBars[0] && (State == State.Historical || State == State.Realtime && indexOffset > 0)))
					ResetValues(false,cdClose);
				
				lastBar = CurrentBars[0];
					/* DISABLED COLOR OVERRIDE TO ALLOW CUSTOM PROPERTIES
					if (delta_close[0] > delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.LimeGreen;
					else if (delta_close[0] < delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.Red;
					else PlotBrushes[3][0] = (Brush) Brushes.Orange;
					*/
				
				
				if (IsFirstTickOfBar && ShowDivs)
				{
				if(delta_low[1] >= delta_low[2] && Low[1] <= Low[2] && Low[1] <= Low[3] && stoch.K[1] <= 20)	
				{
				
					Draw.TriangleUp(this,CurrentBar.ToString(), true, 1, Low[1] - 2*TickSize, divergeCandleup);
				}		
	
				if(delta_high[1] <= delta_high[2] && High[1] >= High[2] && High[1] >= High[3] && stoch.K[1] >= 80)
	
				{
				
					Draw.TriangleDown(this,CurrentBar.ToString(), true, 1, High[1] + 2*TickSize, divergeCandledown);
				}
				}
				
			}
			else if (BarsInProgress == 1)
			{
			
				if (BarsArray[1].IsFirstBarOfSession)
				{
// Debug removed
					ResetValues(true,cdClose);
					
					// Force close potential open sessions at EOD (Safety)
					if (usSessionActive) { historicalLines.Add(new HistoricalZeroLine { StartIdx = usSessionAnchorIdx, EndIdx = CurrentBars[0], Value = usSessionAnchor, SessionType = 0 }); }
					if (asiaSessionActive) { historicalLines.Add(new HistoricalZeroLine { StartIdx = asiaSessionAnchorIdx, EndIdx = CurrentBars[0], Value = asiaSessionAnchor, SessionType = 1 }); }
					if (euSessionActive) { historicalLines.Add(new HistoricalZeroLine { StartIdx = euSessionAnchorIdx, EndIdx = CurrentBars[0], Value = euSessionAnchor, SessionType = 2 }); }
					// Global usually stays, but reset it here for new day calculation
					if (globalSessionActive) { historicalLines.Add(new HistoricalZeroLine { StartIdx = globalSessionAnchorIdx, EndIdx = CurrentBars[0], Value = globalSessionAnchor, SessionType = 3 }); }
					
					usSessionAnchor = double.MinValue; // Reset Anchor
					usSessionAnchorIdx = -1;
					usSessionActive = false;
                    
                    asiaSessionAnchor = double.MinValue;
                    asiaSessionAnchorIdx = -1;
                    asiaSessionActive = false;

                    euSessionAnchor = double.MinValue;
                    euSessionAnchorIdx = -1;
                    euSessionActive = false;
                    
                    globalSessionAnchor = double.MinValue;
                    globalSessionAnchorIdx = -1;
                    globalSessionActive = false;
				}
			
				CalculateValues(false);
				

				
				// V_ZERO_LINE: Capture Logic (Optimized Crossover)
				if (CurrentBar > 0)
				{
				    TimeSpan currentTs = Time[0].TimeOfDay;
				    TimeSpan previousTs = Time[1].TimeOfDay;

				    // US Session Capture
				    if (ShowUSZeroLine && !usSessionActive && usSessionAnchor == double.MinValue)
				    {
				        bool isCrossover = (previousTs < usStartTimeTs && currentTs >= usStartTimeTs);
				        // Handle wrap-around (midnight) if start time is early (unlikely for US open but possible)
                        // If start is 09:30, prev 09:29, curr 09:30 -> True.
                        
				        if (isCrossover)
				        {
				            usSessionAnchor = cdClose;
				            usSessionAnchorIdx = CurrentBars[0]; 
				            usSessionActive = true;
				        }
				    }

                    // ASIA Session Capture
                    if (DisplayAsiaZeroLine && !asiaSessionActive && asiaSessionAnchor == double.MinValue)
                    {
                        // Asia usually starts around 18:00 (Chicago) or 19:00.
                        // If 18:00: prev 17:59, curr 18:00
                        bool isCrossover = (previousTs < asiaStartTimeTs && currentTs >= asiaStartTimeTs);
                         // Handle midnight wrap if needed. 
                         // Logic: if start time is late (e.g. 18:00), simple compare works.
                         // If start time is 00:00, prev 23:59, curr 00:00 -> simple compare works (0 < 0 is false, wait, 23:59 < 0 is false... wait. TS is 0-24.)
                         // Wait: 23:59 is greater than 00:00.
                         // If start time is 18:00. 17:59 < 18:00 (T), 18:00 >= 18:00 (T).
                         
                         // If we are wrapping around midnight for "Custom" times:
                         // Simple check: Just check if we *crossed* the time.
                         // For accurate daily reset, we rely on IsFirstBarOfSession above to reset anchors.
                         // So we just need to detect T >= StartTime for the first time in the session.
                         
                         if (isCrossover)
                        {
                            asiaSessionAnchor = cdClose;
                            asiaSessionAnchorIdx = CurrentBars[0];
                            asiaSessionActive = true;
                        }
                    }

                    // EU Session Capture
                    if (ShowEUZeroLine && !euSessionActive && euSessionAnchor == double.MinValue)
                    {
                        bool isCrossover = (previousTs < euStartTimeTs && currentTs >= euStartTimeTs);
                        if (isCrossover)
                        {
                            euSessionAnchor = cdClose;
                            euSessionAnchorIdx = CurrentBars[0];
                            euSessionActive = true;
                        }
                    }
                    
                    // GLOBAL Session Capture
                    if (DisplayGlobalZeroLine && !globalSessionActive && globalSessionAnchor == double.MinValue)
                    {
                        bool isCrossover = (previousTs < globalStartTimeTs && currentTs >= globalStartTimeTs);
                        if (isCrossover)
                        {
                            globalSessionAnchor = cdClose;
                            globalSessionAnchorIdx = CurrentBars[0];
                            globalSessionActive = true;
                        }
                    }
				}
				
				// V_ZERO_LINE: End Time Logic (Truncate Lines)
				if (CurrentBar > 0)
				{
				    TimeSpan currentTs = Time[0].TimeOfDay;
				    TimeSpan previousTs = Time[1].TimeOfDay;
				    
				    // Helper to check time crossing
				    // A simple check "Current >= EndTime" isn't enough if EndTime < StartTime (overnight).
				    // But typically we just look for the transition.
				    
				    if (usSessionActive)
				    {
				        // Simple check: If we just passed the EndTime
				        bool isEnd = (previousTs < usEndTimeTs && currentTs >= usEndTimeTs);
				        // Optimization: If EndTime is 00:00, handle naturally via date change or explicit check if needed.
				        // For now assume standard times.
				        if (isEnd)
				        {
				            usSessionActive = false;
				            historicalLines.Add(new HistoricalZeroLine { StartIdx = usSessionAnchorIdx, EndIdx = CurrentBars[0], Value = usSessionAnchor, SessionType = 0 }); 
				        }
				    }
				    
				    if (asiaSessionActive)
				    {
				        bool isEnd = (previousTs < asiaEndTimeTs && currentTs >= asiaEndTimeTs);
				        // Asia often wraps (Start 18:00, End 03:00). 
				        // If End is 03:00. Prev 02:59, Curr 03:00 -> TRUE.
				        if (isEnd)
				        {
				            asiaSessionActive = false;
                            historicalLines.Add(new HistoricalZeroLine { StartIdx = asiaSessionAnchorIdx, EndIdx = CurrentBars[0], Value = asiaSessionAnchor, SessionType = 1 });
				        }
				    }
				    
				    if (euSessionActive)
				    {
				        bool isEnd = (previousTs < euEndTimeTs && currentTs >= euEndTimeTs);
				        if (isEnd)
				        {
				            euSessionActive = false;
                            historicalLines.Add(new HistoricalZeroLine { StartIdx = euSessionAnchorIdx, EndIdx = CurrentBars[0], Value = euSessionAnchor, SessionType = 2 });
				        }
				    }
				    
				    // Global typically runs full session, so we might skip automatic end time or set it to session close.
				    if (globalSessionActive && DisplayGlobalZeroLine)
				    {
				         // Optional: Use GlobalEndTime if configured to something other than start?
				         // For now, let it run until Session Close (handled by IsFirstBarOfSession reset).
                         bool isEnd = (previousTs < globalEndTimeTs && currentTs >= globalEndTimeTs);
                         if (isEnd)
                         {
                            globalSessionActive = false;
                            historicalLines.Add(new HistoricalZeroLine { StartIdx = globalSessionAnchorIdx, EndIdx = CurrentBars[0], Value = globalSessionAnchor, SessionType = 3 });
                         }
				    }
				}

				// --- RelativeMCP observability ---
				// Publica siempre en BP==0 (sin filtrar State) — delta acumulativo + anchors de sesión.
				if (CurrentBar >= 0)
				{
					try
					{
						RelativeIndicatorRegistry.Publish(
							string.Format("{0}:{1}:{2}{3}", typeof(RelativeDelta).Name,
								Instrument.FullName, BarsPeriod.Value, BarsPeriod.BarsPeriodType),
							new Dictionary<string, object>
							{
								["bar"] = CurrentBar,
								["bar_time"] = Time[0],
								["close"] = Close[0],
								["cumulative_delta"] = cdClose,
								["bar_delta"] = cdClose - cdOpen,
								["delta_open"] = cdOpen,
								["delta_high"] = cdHigh,
								["delta_low"] = cdLow,
								["us_anchor"] = usSessionAnchor == double.MinValue ? double.NaN : usSessionAnchor,
								["asia_anchor"] = asiaSessionAnchor == double.MinValue ? double.NaN : asiaSessionAnchor,
								["eu_anchor"] = euSessionAnchor == double.MinValue ? double.NaN : euSessionAnchor,
								["global_anchor"] = globalSessionAnchor == double.MinValue ? double.NaN : globalSessionAnchor,
								["us_active"] = usSessionActive,
								["asia_active"] = asiaSessionActive,
								["eu_active"] = euSessionActive,
								["global_active"] = globalSessionActive,
							});

						if (IsFirstTickOfBar && State == State.Realtime)
							this.RLog("bar={0} close={1:F2} cd={2:F0} barD={3:F0} | O={4:F0} H={5:F0} L={6:F0} | anchors US={7} EU={8} A={9} G={10}",
								CurrentBar, Close[0], cdClose, cdClose - cdOpen,
								cdOpen, cdHigh, cdLow,
								usSessionActive ? usSessionAnchor.ToString("F0") : "off",
								euSessionActive ? euSessionAnchor.ToString("F0") : "off",
								asiaSessionActive ? asiaSessionAnchor.ToString("F0") : "off",
								globalSessionActive ? globalSessionAnchor.ToString("F0") : "off");

						// v1.16: Delta history export — persistir bar cerrado para backtest Apteros
						if (IsFirstTickOfBar && CurrentBar >= 2)
							ExportDeltaBar();
					}
					catch { }
				}
				// --- end RelativeMCP ---
			}
		}


		// v1.16: Delta history export para backtest Apteros
		private void ExportDeltaBar()
		{
			try
			{
				DateTime barTime = Time[1]; // bar que acaba de cerrar
				DateTime barDate = barTime.Date;
				if (barDate != _lastDeltaExportDate)
				{
					FlushDeltaBuffer();
					string fn = string.Format("{0}_{1:yyyy-MM-dd}.jsonl",
						Instrument.MasterInstrument.Name, barDate);
					_currentDeltaFile = System.IO.Path.Combine(_deltaHistoryDir, fn);
					_lastDeltaExportDate = barDate;
				}

				double dO = delta_open[1], dH = delta_high[1], dL = delta_low[1], dC = delta_close[1];
				double barDelta = dC - dO;
				var ci = System.Globalization.CultureInfo.InvariantCulture;
				string usA = usSessionAnchor == double.MinValue ? "null" : usSessionAnchor.ToString("0.##", ci);
				string euA = euSessionAnchor == double.MinValue ? "null" : euSessionAnchor.ToString("0.##", ci);
				string asiaA = asiaSessionAnchor == double.MinValue ? "null" : asiaSessionAnchor.ToString("0.##", ci);
				string globalA = globalSessionAnchor == double.MinValue ? "null" : globalSessionAnchor.ToString("0.##", ci);

				_deltaBuffer.AppendFormat(ci,
					"{{\"t\":\"{0:yyyy-MM-dd HH:mm:ss.fff}\",\"p\":{1},\"cdO\":{2},\"cdH\":{3},\"cdL\":{4},\"cdC\":{5},\"bd\":{6},\"us\":{7},\"eu\":{8},\"asia\":{9},\"g\":{10}}}\n",
					barTime, Close[1], dO, dH, dL, dC, barDelta, usA, euA, asiaA, globalA);

				// Flush si buffer grande o cada 5s
				if (_deltaBuffer.Length > 8192 || (DateTime.Now - _lastDeltaFlushTime).TotalSeconds > 5)
					FlushDeltaBuffer();
			}
			catch (Exception ex)
			{
				Print("RelativeDelta ExportDeltaBar ERROR: " + ex.Message);
			}
		}

		private void FlushDeltaBuffer()
		{
			if (_deltaBuffer.Length == 0) return;
			if (string.IsNullOrEmpty(_currentDeltaFile)) return;
			try
			{
				System.IO.File.AppendAllText(_currentDeltaFile, _deltaBuffer.ToString());
				_deltaBuffer.Clear();
				_lastDeltaFlushTime = DateTime.Now;
			}
			catch (Exception ex)
			{
				Print("RelativeDelta FlushDeltaBuffer ERROR: " + ex.Message);
			}
		}

		private void CalculateValues(bool forceCurrentBar)
		{
			
			int 	indexOffset 	= BarsArray[1].Count - 1 - CurrentBars[1];
			bool 	inTransition 	= State == State.Realtime && indexOffset > 1;
			if (!inTransition && lastInTransition && !forceCurrentBar && Calculate == Calculate.OnBarClose)
				CalculateValues(true);
			
			bool 	useCurrentBar 	= State == State.Historical || inTransition || Calculate != Calculate.OnBarClose || forceCurrentBar;
			int 	whatBar 		= useCurrentBar ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);
		
			double 	volume 			= BarsArray[1].GetVolume(whatBar);
			double	price			= BarsArray[1].GetClose(whatBar);
			
			// v1.15.65: Robust Delta Calculation (UpTick/DownTick Fallback)
			// Check if Bid/Ask data is valid (Tick Replay Check)
			double ask = BarsArray[1].GetAsk(whatBar);
			double bid = BarsArray[1].GetBid(whatBar);
			
			if (ask > 0 && bid > 0)
			{
				// Accurate Volumetric Calculation (Requires Tick Replay)
				if (price >= ask && volume >= MinSize) buys += volume;
				else if (price <= bid && volume >= MinSize) sells += volume;
			}
			else
			{
				// Fallback: UpTick / DownTick (No Tick Replay)
				// Use previous price to determine direction
				double prevPrice = (whatBar > 0) ? BarsArray[1].GetClose(whatBar - 1) : price;
				
				if (price >= prevPrice && volume >= MinSize) buys += volume;
				else if (volume >= MinSize) sells += volume;
			}
			
			cdClose = buys - sells;
	
			if (cdClose > cdHigh)
					cdHigh = cdClose;
	
			if (cdClose < cdLow)
					cdLow = cdClose;
	
			
			lastInTransition 	= inTransition;
		}
		
		private void SetValues(int barsAgo)
		{
		
		
			
			Values[0][barsAgo] = delta_open[barsAgo] = cdOpen;
			Values[1][barsAgo] = delta_high[barsAgo] = cdHigh;
			Values[2][barsAgo] = delta_low[barsAgo] = cdLow;
			Values[3][barsAgo] = delta_close[barsAgo] = cdClose;
			Values[3][barsAgo] = delta_close[barsAgo] = cdClose;
			Values[4][barsAgo] = cdClose - cdOpen; // v1.15.63: Calculate Bar Delta (Close - Open)
			
			// V_ADAPTIVE: Update logic
			double dH = (cdHigh > cdClose) ? cdHigh : cdClose; // Safety if high !updated
			double dL = (cdLow < cdClose) ? cdLow : cdClose;
			deltaRangeSeries[barsAgo] = Math.Abs(cdHigh - cdLow);
			
	
		}
		
		private void ResetValues(bool isNewSession, double openlevel)
		{
		
		
			
			cdOpen = cdClose = cdHigh = cdLow = openlevel;
				
			if (isNewSession)
			{
				cdOpen = cdClose = cdHigh = cdLow = buys = sells = 0;
			}
			isReset = true;
		}
		
		public override string DisplayName
		{
		  get { return "Relative Delta"; }
		}
		
		#region Miscellaneous
	
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			barPaintWidth = Math.Max(1, (int)(ChartBars.Properties.ChartStyle.BarWidth * 2));

			// PERF: cachear referencias a brushes ANTES del loop. Antes: 7 string-key
			// hashtable lookups por bar × 1000 bars visibles = 7000 lookups/frame.
			// Ahora: 4 lookups una sola vez antes del loop.
			SharpDX.Direct2D1.Brush brushWick = null, brushShadow = null, brushUp = null, brushDown = null;
			try
			{
				if (dxmBrushes != null)
				{
					if (dxmBrushes.ContainsKey("wickColor"))   brushWick   = dxmBrushes["wickColor"].DxBrush;
					if (dxmBrushes.ContainsKey("shadowColor")) brushShadow = dxmBrushes["shadowColor"].DxBrush;
					if (dxmBrushes.ContainsKey("barColorUp"))  brushUp     = dxmBrushes["barColorUp"].DxBrush;
					if (dxmBrushes.ContainsKey("barColorDown"))brushDown   = dxmBrushes["barColorDown"].DxBrush;
				}
			}
			catch { }

			try
			{
				for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx - Displacement < 0 || idx - Displacement >= BarsArray[0].Count || (idx - Displacement < BarsRequiredToPlot))
                    continue;

                x					= ChartControl.GetXByBarIndex(ChartBars, idx);
                y1					= chartScale.GetYByValue(delta_open.GetValueAt(idx));
                y2					= chartScale.GetYByValue(delta_high.GetValueAt(idx));
                y3					= chartScale.GetYByValue(delta_low.GetValueAt(idx));
                y4					= chartScale.GetYByValue(delta_close.GetValueAt(idx));

                // Calculate Top and Bottom of the body
                float bodyTop = Math.Min(y1, y4);
                float bodyBottom = Math.Max(y1, y4);

				if (y4 == y1) // Doji
				{
                    // Draw full wick
    				reuseVector1.X		= x;
    				reuseVector1.Y		= y2;
    				reuseVector2.X		= x;
    				reuseVector2.Y		= y3;
    				if (brushWick != null) RenderTarget.DrawLine(reuseVector1, reuseVector2, brushWick, WickWidth);

                    // Draw Doji Body Line
					reuseVector1.X	= (x - barPaintWidth / 2);
					reuseVector1.Y	= y1;
					reuseVector2.X	= (x + barPaintWidth / 2);
					reuseVector2.Y	= y1;

					if (brushShadow != null) RenderTarget.DrawLine(reuseVector1, reuseVector2, brushShadow, ShadowWidth);
				}
				else
				{
                    // Draw Upper Wick (High to Body Top)
                    if (y2 < bodyTop)
                    {
                        reuseVector1.X = x;
                        reuseVector1.Y = y2;
                        reuseVector2.X = x;
                        reuseVector2.Y = bodyTop;
                        if (brushWick != null) RenderTarget.DrawLine(reuseVector1, reuseVector2, brushWick, WickWidth);
                    }

                    // Draw Lower Wick (Body Bottom to Low)
                    if (y3 > bodyBottom)
                    {
                        reuseVector1.X = x;
                        reuseVector1.Y = bodyBottom;
                        reuseVector2.X = x;
                        reuseVector2.Y = y3;
                        if (brushWick != null) RenderTarget.DrawLine(reuseVector1, reuseVector2, brushWick, WickWidth);
                    }

					// Select Brush based on Structure State
					SharpDX.Direct2D1.Brush activeBrush = brushDown;

					// Fallback to Up/Down standard logic if Structure logic disabled
					if (y4 > y1) activeBrush = brushDown;
					else activeBrush = brushUp;


					if (y4 > y1) // Down Candle
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y1, barPaintWidth, (y4 - y1));
						if (activeBrush != null) RenderTarget.FillRectangle(reuseRect, activeBrush);
					}
					else // Up Candle
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y4, barPaintWidth, (y1 - y4));
						if (activeBrush != null) RenderTarget.FillRectangle(reuseRect, activeBrush);
					}
				}

				UpdateRect(ref reuseRect, ((x - barPaintWidth / 2) + (ShadowWidth / 2)), Math.Min(y4, y1), (barPaintWidth - ShadowWidth + 2), Math.Abs(y4 - y1));
				if (brushShadow != null) RenderTarget.DrawRectangle(reuseRect, brushShadow);
				

            }
		}  // Close for loop
		catch (Exception ex)
            {
                Print("RelativeDelta Render Error: " + ex.Message);
            }

            // Dibuja la línea horizontal configurable
            if (HorizontalLineColor != null)
            {
                double yValue = chartScale.GetYByValue(HorizontalLineValue);
                byte alpha = (byte)(255 * HorizontalLineAlphaPercent / 100);
                var color = ((SolidColorBrush)HorizontalLineColor).Color;
                var colorWithAlpha = System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
                var brushWithAlpha = new SolidColorBrush(colorWithAlpha);
                var lineBrush = brushWithAlpha.ToDxBrush(RenderTarget);
                var start = new SharpDX.Vector2(0, (float)yValue);
                var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)yValue);
                RenderTarget.DrawLine(start, end, lineBrush, HorizontalLineWidth);
                lineBrush.Dispose();
                // Dibuja el valor en el margen derecho
                DrawLineLabel(HorizontalLineValue, yValue, chartControl, chartScale);
            }

            // Dibuja líneas extra individuales usando recursos cacheados
            DrawExtraLine(2500, ShowLine2500, dxBrushLine2500, Line2500Width, chartScale, chartControl);
            DrawExtraLine(-2500, ShowLineN2500, dxBrushLineN2500, LineN2500Width, chartScale, chartControl);
            DrawExtraLine(5000, ShowLine5000, dxBrushLine5000, Line5000Width, chartScale, chartControl);
            DrawExtraLine(-5000, ShowLineN5000, dxBrushLineN5000, LineN5000Width, chartScale, chartControl);
            DrawExtraLine(10000, ShowLine10000, dxBrushLine10000, Line10000Width, chartScale, chartControl);
            DrawExtraLine(-10000, ShowLineN10000, dxBrushLineN10000, LineN10000Width, chartScale, chartControl);
            
            
            
            // V_ZERO_LINE: Draw Historical Lines (Moved from loop to here for better management)
             foreach (var line in historicalLines)
             {
                 if (line.EndIdx < ChartBars.FromIndex || line.StartIdx > ChartBars.ToIndex) continue;
                 
                 SharpDX.Direct2D1.Brush brush = null;
                 SharpDX.Direct2D1.StrokeStyle stroke = null;
                 int width = 1;
                 
                 switch(line.SessionType)
                 {
                     case 0: // US
                        if(!ShowUSZeroLine) continue;
                        brush = dxBrushUSZeroLine; stroke = dxStrokeDash; width = USZeroLineWidth;
                        break;
                     case 1: // Asia
                        if(!DisplayAsiaZeroLine) continue;
                        brush = dxBrushAsiaZeroLine; stroke = dxStrokeDashAsia; width = AsiaZeroLineWidth;
                        break;
                     case 2: // EU
                        if(!ShowEUZeroLine) continue;
                        brush = dxBrushEUZeroLine; stroke = dxStrokeDashEU; width = EUZeroLineWidth;
                        break;
                     case 3: // Global
                        if(!DisplayGlobalZeroLine) continue;
                        brush = dxBrushGlobalZeroLine; stroke = dxStrokeDashGlobal; width = GlobalZeroLineWidth;
                        break;
                 }
                 
                 if (brush != null && stroke != null)
                 {
                     double yVal = chartScale.GetYByValue(line.Value);
                     float x1 = chartControl.GetXByBarIndex(ChartBars, line.StartIdx);
                     float x2 = chartControl.GetXByBarIndex(ChartBars, line.EndIdx);
                     var p1 = new SharpDX.Vector2(x1, (float)yVal);
                     var p2 = new SharpDX.Vector2(x2, (float)yVal);
                     RenderTarget.DrawLine(p1, p2, brush, width, stroke);
                 }
             }

            // V_ZERO_LINE: Draw Session Lines using Helper
            DrawSessionLine(usSessionAnchor, usSessionAnchorIdx, usSessionActive, ShowUSZeroLine, dxBrushUSZeroLine, USZeroLineWidth, dxStrokeDash, "Cero USA: ", chartControl, chartScale);
            DrawSessionLine(asiaSessionAnchor, asiaSessionAnchorIdx, asiaSessionActive, DisplayAsiaZeroLine, dxBrushAsiaZeroLine, AsiaZeroLineWidth, dxStrokeDashAsia, "Cero Asia: ", chartControl, chartScale);
            DrawSessionLine(euSessionAnchor, euSessionAnchorIdx, euSessionActive, ShowEUZeroLine, dxBrushEUZeroLine, EUZeroLineWidth, dxStrokeDashEU, "Cero EU: ", chartControl, chartScale);
            DrawSessionLine(globalSessionAnchor, globalSessionAnchorIdx, globalSessionActive, DisplayGlobalZeroLine, dxBrushGlobalZeroLine, GlobalZeroLineWidth, dxStrokeDashGlobal, "Cero Global: ", chartControl, chartScale);
            
		}
        
        // Helper for Drawing Session Lines with Labels
        private void DrawSessionLine(double anchor, int anchorIdx, bool isActive, bool show, SharpDX.Direct2D1.Brush brush, int width, SharpDX.Direct2D1.StrokeStyle stroke, string labelPrefix, ChartControl chartControl, ChartScale chartScale)
        {
             if (!show || !isActive || anchor == double.MinValue || anchorIdx == -1 || brush == null || stroke == null)
                 return;

             double yValue = chartScale.GetYByValue(anchor);
             float startX = chartControl.GetXByBarIndex(ChartBars, anchorIdx);
             var start = new SharpDX.Vector2(startX, (float)yValue);
             var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)yValue);
             
             RenderTarget.DrawLine(start, end, brush, width, stroke);
             
             // Draw Label
             if (LineLabelColor != null && dwFactory != null && dwTextFormat != null)
             {
                 DrawLineLabelText(labelPrefix + anchor.ToString("N2"), yValue, chartControl);
             }
        }
        
        private void DrawLineLabelText(string text, double y, ChartControl chartControl)
        {
            if (LineLabelColor == null || RenderTarget == null) return;
            
             var color = ((SolidColorBrush)LineLabelColor).Color;
             var dxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);
             var bgColor = ((SolidColorBrush)LineLabelBackground).Color;
             var dxBgColor = new SharpDX.Color(bgColor.R, bgColor.G, bgColor.B, bgColor.A);
             
             using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
             using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
             {
                 using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, text, dwTextFormat, 200, 20))
                 {
                     float x = (float)chartControl.PanelWidth - layout.Metrics.Width - 4;
                     float yText = (float)y - layout.Metrics.Height / 2;
                     var rect = new SharpDX.RectangleF(x, yText, layout.Metrics.Width, layout.Metrics.Height);
                     RenderTarget.FillRectangle(rect, bgBrush);
                     RenderTarget.DrawText(text, dwTextFormat, rect, textBrush);
                 }
             }
        }
		
		public override void OnRenderTargetChanged()
		{		
			try
			{
				foreach (KeyValuePair<string, DXMediaMap> item in dxmBrushes)
				{
					if (item.Value.DxBrush != null)
						item.Value.DxBrush.Dispose();

					if (RenderTarget != null)
						item.Value.DxBrush = item.Value.MediaBrush.ToDxBrush(RenderTarget);					
				}
				
				CreateD2DResources(); // Re-create custom resources
			}
			catch (Exception exception)
			{
			}
		}
		
		private void DisposeD2DResources()
		{
		    SafeDispose(dxBrushLine10000); dxBrushLine10000 = null;
		    SafeDispose(dxBrushLineN10000); dxBrushLineN10000 = null;
		    SafeDispose(dxBrushUSZeroLine); dxBrushUSZeroLine = null;
            SafeDispose(dxBrushAsiaZeroLine); dxBrushAsiaZeroLine = null;
            SafeDispose(dxBrushEUZeroLine); dxBrushEUZeroLine = null;
            SafeDispose(dxBrushGlobalZeroLine); dxBrushGlobalZeroLine = null;

		    SafeDispose(dxStrokeDash); dxStrokeDash = null;
            SafeDispose(dxStrokeDashAsia); dxStrokeDashAsia = null;
            SafeDispose(dxStrokeDashEU); dxStrokeDashEU = null;
            SafeDispose(dxStrokeDashGlobal); dxStrokeDashGlobal = null;
            
		    SafeDispose(dwTextFormat); dwTextFormat = null;
		    SafeDispose(dwFactory); dwFactory = null;
		}
		
		private void CreateD2DResources()
		{
		    DisposeD2DResources(); // Clear old
		    
		    if (RenderTarget == null) return;
		    
		    // Helpers to create brush with alpha
		    Func<Brush, int, SharpDX.Direct2D1.Brush> createBrush = (brush, alpha) => 
		    {
		        if (brush == null) return null;
		        var color = ((SolidColorBrush)brush).Color;
		        var dxColor = new SharpDX.Color((int)color.R, (int)color.G, (int)color.B, (int)(255 * alpha / 100.0));
		        return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);
		    };
		    
		    dxBrushLine2500 = createBrush(Line2500Color, Line2500Alpha);
		    dxBrushLineN2500 = createBrush(LineN2500Color, LineN2500Alpha);
		    dxBrushLine5000 = createBrush(Line5000Color, Line5000Alpha);
		    dxBrushLineN5000 = createBrush(LineN5000Color, LineN5000Alpha);
		    dxBrushLine10000 = createBrush(Line10000Color, Line10000Alpha);
		    dxBrushLineN10000 = createBrush(LineN10000Color, LineN10000Alpha);
		    dxBrushUSZeroLine = createBrush(USZeroLineColor, USZeroLineAlpha);
            dxBrushAsiaZeroLine = createBrush(AsiaZeroLineColor, AsiaZeroLineAlpha);
            dxBrushEUZeroLine = createBrush(EUZeroLineColor, EUZeroLineAlpha);
            dxBrushGlobalZeroLine = createBrush(GlobalZeroLineColor, GlobalZeroLineAlpha);
			

		    
		    // Create Stroke Style (Dash) using RenderTarget's Factory (Crucial for performance/compat)
		    try 
		    {
		        var strokeStyleProps = new SharpDX.Direct2D1.StrokeStyleProperties();
                strokeStyleProps.DashStyle = (SharpDX.Direct2D1.DashStyle)USZeroLineDashStyle;
                dxStrokeDash = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, strokeStyleProps);
                
                var strokeStylePropsAsia = new SharpDX.Direct2D1.StrokeStyleProperties();
                strokeStylePropsAsia.DashStyle = (SharpDX.Direct2D1.DashStyle)AsiaZeroLineDashStyle;
                dxStrokeDashAsia = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, strokeStylePropsAsia);

                var strokeStylePropsEU = new SharpDX.Direct2D1.StrokeStyleProperties();
                strokeStylePropsEU.DashStyle = (SharpDX.Direct2D1.DashStyle)EUZeroLineDashStyle;
                dxStrokeDashEU = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, strokeStylePropsEU);

                var strokeStylePropsGlobal = new SharpDX.Direct2D1.StrokeStyleProperties();
                strokeStylePropsGlobal.DashStyle = (SharpDX.Direct2D1.DashStyle)GlobalZeroLineDashStyle;
                dxStrokeDashGlobal = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, strokeStylePropsGlobal);
		    }
		    catch {}
		    
		    // Cache DirectWrite
		    try
		    {
		        dwFactory = new SharpDX.DirectWrite.Factory();
		        dwTextFormat = new SharpDX.DirectWrite.TextFormat(dwFactory, "Segoe UI", 12f);
		    }
		    catch {}
		}

		private void UpdateRect(ref SharpDX.RectangleF updateRectangle, float x, float y, float width, float height)
		{
			updateRectangle.X		= x;
			updateRectangle.Y		= y;
			updateRectangle.Width	= width;
			updateRectangle.Height	= height;
		}

		private void UpdateRect(ref SharpDX.RectangleF rectangle, int x, int y, int width, int height)
		{
			UpdateRect(ref rectangle, (float)x, (float)y, (float)width, (float)height);
		}
		#endregion
		
		#region Properties
		[Browsable(false)]
		public class DXMediaMap
		{
			public SharpDX.Direct2D1.Brush		DxBrush;
			public System.Windows.Media.Brush	MediaBrush;
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorDown", Order=4, GroupName= "Optics")]
		public Brush BarColorDown
		{
			get { return dxmBrushes["barColorDown"].MediaBrush; }
			set { dxmBrushes["barColorDown"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorDownSerializable
		{
			get { return Serialize.BrushToString(BarColorDown); }
			set { BarColorDown = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorUp", Order=5, GroupName= "Optics")]
		public Brush BarColorUp
		{
			get { return dxmBrushes["barColorUp"].MediaBrush; }
			set { dxmBrushes["barColorUp"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorUpSerializable
		{
			get { return Serialize.BrushToString(BarColorUp); }
			set { BarColorUp = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="ShadowColor", Order=6, GroupName="Optics")]
		public Brush ShadowColor
		{
			get { return dxmBrushes["shadowColor"].MediaBrush; }
			set { dxmBrushes["shadowColor"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string ShadowColorSerializable
		{
			get { return Serialize.BrushToString(ShadowColor); }
			set { ShadowColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ShadowWidth", Order=7, GroupName= "Optics")]
		public int ShadowWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="WickColor", Description="Color of the vertical wick (high-low line)", Order=8, GroupName="Optics")]
		public Brush WickColor
		{
			get { return dxmBrushes["wickColor"].MediaBrush; }
			set { dxmBrushes["wickColor"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string WickColorSerializable
		{
			get { return Serialize.BrushToString(WickColor); }
			set { WickColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="WickWidth", Description="Width of the vertical wick (high-low line)", Order=9, GroupName="Optics")]
		public int WickWidth
		{ get; set; } = 2;

		[Browsable(false)]
		[XmlIgnore]
		public int BarWidth
		{ get; set; } = 1;
		

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaOpen
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaHigh
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaLow
		{
			get { return Values[2]; }
		}
		
				
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaClose
		{
			get { return Values[3]; }
		}
	
		[Range(0, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name="Size Filter", Description="Size filtering", Order=1, GroupName="Parameters")]
		public int MinSize
		{ get; set; }

// Duplicate attributes removed
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Days To Load", Description="Number of days to load (0 = all)", Order=1, GroupName="Performance")]
		public int DaysToLoad
		{ get; set; } = 7;
		
		
		[NinjaScriptProperty]
		[Display(Name="Show Delta Divergences", Description="Enable to show cumulative delta divergences", Order=2, GroupName="Parameters")]
		public bool ShowDivs
		{ get; set; }
		
		[NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Línea Horizontal Color", Order=10, GroupName="Línea Horizontal")]
        public Brush HorizontalLineColor { get; set; } = Brushes.RoyalBlue;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Línea Horizontal Grosor", Order=11, GroupName="Línea Horizontal")]
        public int HorizontalLineWidth { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name="Línea Horizontal Valor", Order=12, GroupName="Línea Horizontal")]
        public double HorizontalLineValue { get; set; } = 0;
		
		[NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Línea Horizontal Transparencia (%)", Order=13, GroupName="Línea Horizontal")]
        public int HorizontalLineAlphaPercent { get; set; } = 100;
		
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasOpen
        {
            get { return delta_open; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasHigh
        {
            get { return delta_high; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasClose
        {
            get { return delta_close; }
        }	
		
		[NinjaScriptProperty]
        [Display(Name="Mostrar Líneas Extra Niveles", Order=14, GroupName="Línea Horizontal")]
        public bool ShowExtraLevels { get; set; } = true;
		
		// Propiedades para línea +2500
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +2500", Order=20, GroupName="Líneas Extra")]
        public bool ShowLine2500 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +2500", Order=21, GroupName="Líneas Extra")]
        public Brush Line2500Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line2500ColorSerializable
        {
            get { return Serialize.BrushToString(Line2500Color); }
            set { Line2500Color = Serialize.StringToBrush(value); }
        }

        [Range(1, 10)]
        [Display(Name="Grosor Línea +2500", Order=22, GroupName="Líneas Extra")]
        public int Line2500Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +2500 (%)", Order=23, GroupName="Líneas Extra")]
        public int Line2500Alpha { get; set; } = 100;

        // Propiedades para línea -2500
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -2500", Order=24, GroupName="Líneas Extra")]
        public bool ShowLineN2500 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -2500", Order=25, GroupName="Líneas Extra")]
        public Brush LineN2500Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN2500ColorSerializable
        {
            get { return Serialize.BrushToString(LineN2500Color); }
            set { LineN2500Color = Serialize.StringToBrush(value); }
        }
		

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -2500", Order=26, GroupName="Líneas Extra")]
        public int LineN2500Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -2500 (%)", Order=27, GroupName="Líneas Extra")]
        public int LineN2500Alpha { get; set; } = 100;

        // Propiedades para línea +5000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +5000", Order=28, GroupName="Líneas Extra")]
        public bool ShowLine5000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +5000", Order=29, GroupName="Líneas Extra")]
        public Brush Line5000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line5000ColorSerializable
        {
            get { return Serialize.BrushToString(Line5000Color); }
            set { Line5000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea +5000", Order=30, GroupName="Líneas Extra")]
        public int Line5000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +5000 (%)", Order=31, GroupName="Líneas Extra")]
        public int Line5000Alpha { get; set; } = 100;

        // Propiedades para línea -5000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -5000", Order=32, GroupName="Líneas Extra")]
        public bool ShowLineN5000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -5000", Order=33, GroupName="Líneas Extra")]
        public Brush LineN5000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN5000ColorSerializable
        {
            get { return Serialize.BrushToString(LineN5000Color); }
            set { LineN5000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -5000", Order=34, GroupName="Líneas Extra")]
        public int LineN5000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -5000 (%)", Order=35, GroupName="Líneas Extra")]
        public int LineN5000Alpha { get; set; } = 100;

        // Propiedades para línea +10000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea +10000", Order=36, GroupName="Líneas Extra")]
        public bool ShowLine10000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea +10000", Order=37, GroupName="Líneas Extra")]
        public Brush Line10000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string Line10000ColorSerializable
        {
            get { return Serialize.BrushToString(Line10000Color); }
            set { Line10000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea +10000", Order=38, GroupName="Líneas Extra")]
        public int Line10000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea +10000 (%)", Order=39, GroupName="Líneas Extra")]
        public int Line10000Alpha { get; set; } = 100;

        // Propiedades para línea -10000
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea -10000", Order=40, GroupName="Líneas Extra")]
        public bool ShowLineN10000 { get; set; } = true;
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea -10000", Order=41, GroupName="Líneas Extra")]
        public Brush LineN10000Color { get; set; } = Brushes.Gray;
        [Browsable(false)]
        public string LineN10000ColorSerializable
        {
            get { return Serialize.BrushToString(LineN10000Color); }
            set { LineN10000Color = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea -10000", Order=42, GroupName="Líneas Extra")]
        public int LineN10000Width { get; set; } = 1;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea -10000 (%)", Order=43, GroupName="Líneas Extra")]
        public int LineN10000Alpha { get; set; } = 100;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Texto Líneas", Order=50, GroupName="Líneas Extra")]
        public Brush LineLabelColor { get; set; } = Brushes.White; // Default to White
        [Browsable(false)]
        public string LineLabelColorSerializable
        {
            get { return Serialize.BrushToString(LineLabelColor); }
            set { LineLabelColor = Serialize.StringToBrush(value); }
        }
		
		[NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Fondo Texto Líneas", Order=51, GroupName="Líneas Extra")]
        public Brush LineLabelBackground { get; set; } = Brushes.Transparent; // Default to Transparent
        [Browsable(false)]
        public string LineLabelBackgroundSerializable
        {
            get { return Serialize.BrushToString(LineLabelBackground); }
            set { LineLabelBackground = Serialize.StringToBrush(value); }
        }
        
        // V_ZERO_LINE: Properties
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea Cero USA", Order=60, GroupName="Línea Cero USA")]
        public bool ShowUSZeroLine { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Hora Inicio USA", Description="Hora formateada HH:mm (ej. 10:30)", Order=61, GroupName="Línea Cero USA")]
        public string USStartTime { get; set; } = "10:30";
        [NinjaScriptProperty]
        [Display(Name="Hora Fin USA", Description="Hora finalización (ej. 17:00)", Order=61, GroupName="Línea Cero USA")]
        public string USEndTime { get; set; } = "17:00";

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea Cero USA", Order=62, GroupName="Línea Cero USA")]
        public Brush USZeroLineColor { get; set; } = Brushes.Yellow;
        [Browsable(false)]
        public string USZeroLineColorSerializable
        {
            get { return Serialize.BrushToString(USZeroLineColor); }
            set { USZeroLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea Cero USA", Order=63, GroupName="Línea Cero USA")]
        public int USZeroLineWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea Cero USA (%)", Order=64, GroupName="Línea Cero USA")]
        public int USZeroLineAlpha { get; set; } = 100;

        [NinjaScriptProperty]
        [Display(Name="Estilo Dash Línea USA", Order=65, GroupName="Línea Cero USA")]
        public DashStyleHelper USZeroLineDashStyle { get; set; } = DashStyleHelper.Dash; 

        // ASIA
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea Cero Asia", Order=70, GroupName="Línea Cero Asia")]
        public bool DisplayAsiaZeroLine { get; set; } = false;
        [NinjaScriptProperty]
        [Display(Name="Hora Inicio Asia", Description="Hora formateada HH:mm (ej. 18:00)", Order=71, GroupName="Línea Cero Asia")]
        public string AsiaStartTime { get; set; } = "18:00";
        [NinjaScriptProperty]
        [Display(Name="Hora Fin Asia", Description="Hora finalización (ej. 03:00)", Order=71, GroupName="Línea Cero Asia")]
        public string AsiaEndTime { get; set; } = "03:00";
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea Cero Asia", Order=72, GroupName="Línea Cero Asia")]
        public Brush AsiaZeroLineColor { get; set; } = Brushes.Red;
        [Browsable(false)]
        public string AsiaZeroLineColorSerializable
        {
            get { return Serialize.BrushToString(AsiaZeroLineColor); }
            set { AsiaZeroLineColor = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea Cero Asia", Order=73, GroupName="Línea Cero Asia")]
        public int AsiaZeroLineWidth { get; set; } = 2;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea Cero Asia (%)", Order=74, GroupName="Línea Cero Asia")]
        public int AsiaZeroLineAlpha { get; set; } = 100;
        [NinjaScriptProperty]
        [Display(Name="Estilo Dash Línea Asia", Order=75, GroupName="Línea Cero Asia")]
        public DashStyleHelper AsiaZeroLineDashStyle { get; set; } = DashStyleHelper.Dash;

        // EUROPE
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea Cero EU", Order=80, GroupName="Línea Cero EU")]
        public bool ShowEUZeroLine { get; set; } = true;
        [NinjaScriptProperty]
        [Display(Name="Hora Inicio EU", Description="Hora formateada HH:mm (ej. 03:00)", Order=81, GroupName="Línea Cero EU")]
        public string EUStartTime { get; set; } = "03:00";
        [NinjaScriptProperty]
        [Display(Name="Hora Fin EU", Description="Hora finalización (ej. 10:30)", Order=81, GroupName="Línea Cero EU")]
        public string EUEndTime { get; set; } = "10:30";
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea Cero EU", Order=82, GroupName="Línea Cero EU")]
        public Brush EUZeroLineColor { get; set; } = Brushes.Cyan;
        [Browsable(false)]
        public string EUZeroLineColorSerializable
        {
            get { return Serialize.BrushToString(EUZeroLineColor); }
            set { EUZeroLineColor = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea Cero EU", Order=83, GroupName="Línea Cero EU")]
        public int EUZeroLineWidth { get; set; } = 2;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea Cero EU (%)", Order=84, GroupName="Línea Cero EU")]
        public int EUZeroLineAlpha { get; set; } = 100;
        [NinjaScriptProperty]
        [Display(Name="Estilo Dash Línea EU", Order=85, GroupName="Línea Cero EU")]
        public DashStyleHelper EUZeroLineDashStyle { get; set; } = DashStyleHelper.Dash;

        // GLOBAL
        [NinjaScriptProperty]
        [Display(Name="Mostrar Línea Cero Global", Order=90, GroupName="Línea Cero Global")]
        public bool DisplayGlobalZeroLine { get; set; } = false;
        [NinjaScriptProperty]
        [Display(Name="Hora Inicio Global", Description="Hora formateada HH:mm (ej. 17:00)", Order=91, GroupName="Línea Cero Global")]
        public string GlobalStartTime { get; set; } = "17:00";
        [NinjaScriptProperty]
        [Display(Name="Hora Fin Global", Description="Hora finalización (ej. 16:59)", Order=91, GroupName="Línea Cero Global")]
        public string GlobalEndTime { get; set; } = "16:59";
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Color Línea Cero Global", Order=92, GroupName="Línea Cero Global")]
        public Brush GlobalZeroLineColor { get; set; } = Brushes.Gold;
        [Browsable(false)]
        public string GlobalZeroLineColorSerializable
        {
            get { return Serialize.BrushToString(GlobalZeroLineColor); }
            set { GlobalZeroLineColor = Serialize.StringToBrush(value); }
        }
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Grosor Línea Cero Global", Order=93, GroupName="Línea Cero Global")]
        public int GlobalZeroLineWidth { get; set; } = 2;
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Transparencia Línea Cero Global (%)", Order=94, GroupName="Línea Cero Global")]
        public int GlobalZeroLineAlpha { get; set; } = 100;
        [NinjaScriptProperty]
        [Display(Name="Estilo Dash Línea Global", Order=95, GroupName="Línea Cero Global")]
        public DashStyleHelper GlobalZeroLineDashStyle { get; set; } = DashStyleHelper.Solid;

		#endregion
		
		// Método auxiliar para dibujar líneas extra (Optimized)
        private void DrawExtraLine(double level, bool show, SharpDX.Direct2D1.Brush dxBrush, int width, ChartScale chartScale, ChartControl chartControl)
        {
            if (!show || dxBrush == null)
                return;
                
            // Use cached brush directly! No creation/dispose here.
            double y = chartScale.GetYByValue(level);
            var start = new SharpDX.Vector2(0, (float)y);
            var end = new SharpDX.Vector2((float)chartControl.PanelWidth, (float)y);
            RenderTarget.DrawLine(start, end, dxBrush, width);
            
            // Dibuja el valor en el margen derecho
            DrawLineLabel(level, y, chartControl, chartScale);
        }

        // Método auxiliar para dibujar el valor de la línea en el margen derecho (Optimized)
        private void DrawLineLabel(double value, double y, ChartControl chartControl, ChartScale chartScale)
        {
            if (LineLabelColor == null || RenderTarget == null || dwFactory == null || dwTextFormat == null)
                return;
            
            var color = ((SolidColorBrush)LineLabelColor).Color;
            var dxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);
            var bgColor = ((SolidColorBrush)LineLabelBackground).Color;
            var dxBgColor = new SharpDX.Color(bgColor.R, bgColor.G, bgColor.B, bgColor.A);
            
            // Using logic is lightweight for brushes if they are small/solid, but ideally cached too.
            // For now, caching Factory and Format is the huge win.
            
            // For now, caching Factory and Format is the huge win.
            
            using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
            using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
            {
                string label = value.ToString();
                using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, label, dwTextFormat, 100, 20))
                {
                    float x = (float)chartControl.PanelWidth - layout.Metrics.Width - 4;
                    float yText = (float)y - layout.Metrics.Height / 2;
                    var rect = new SharpDX.RectangleF(x, yText, layout.Metrics.Width, layout.Metrics.Height);
                    // Dibuja el fondo antes del texto
                    RenderTarget.FillRectangle(rect, bgBrush);
                    RenderTarget.DrawText(label, dwTextFormat, rect, textBrush);
                }
            }
        }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeDelta[] cacheRelativeDelta;
		public RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			return RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSEndTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle, displayAsiaZeroLine, asiaStartTime, asiaEndTime, asiaZeroLineColor, asiaZeroLineWidth, asiaZeroLineAlpha, asiaZeroLineDashStyle, showEUZeroLine, eUStartTime, eUEndTime, eUZeroLineColor, eUZeroLineWidth, eUZeroLineAlpha, eUZeroLineDashStyle, displayGlobalZeroLine, globalStartTime, globalEndTime, globalZeroLineColor, globalZeroLineWidth, globalZeroLineAlpha, globalZeroLineDashStyle);
		}

		public RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input, Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			if (cacheRelativeDelta != null)
				for (int idx = 0; idx < cacheRelativeDelta.Length; idx++)
					if (cacheRelativeDelta[idx] != null && cacheRelativeDelta[idx].BarColorDown == barColorDown && cacheRelativeDelta[idx].BarColorUp == barColorUp && cacheRelativeDelta[idx].ShadowColor == shadowColor && cacheRelativeDelta[idx].ShadowWidth == shadowWidth && cacheRelativeDelta[idx].WickColor == wickColor && cacheRelativeDelta[idx].WickWidth == wickWidth && cacheRelativeDelta[idx].MinSize == minSize && cacheRelativeDelta[idx].DaysToLoad == daysToLoad && cacheRelativeDelta[idx].ShowDivs == showDivs && cacheRelativeDelta[idx].HorizontalLineColor == horizontalLineColor && cacheRelativeDelta[idx].HorizontalLineWidth == horizontalLineWidth && cacheRelativeDelta[idx].HorizontalLineValue == horizontalLineValue && cacheRelativeDelta[idx].HorizontalLineAlphaPercent == horizontalLineAlphaPercent && cacheRelativeDelta[idx].ShowExtraLevels == showExtraLevels && cacheRelativeDelta[idx].ShowLine2500 == showLine2500 && cacheRelativeDelta[idx].Line2500Color == line2500Color && cacheRelativeDelta[idx].Line2500Alpha == line2500Alpha && cacheRelativeDelta[idx].ShowLineN2500 == showLineN2500 && cacheRelativeDelta[idx].LineN2500Color == lineN2500Color && cacheRelativeDelta[idx].LineN2500Width == lineN2500Width && cacheRelativeDelta[idx].LineN2500Alpha == lineN2500Alpha && cacheRelativeDelta[idx].ShowLine5000 == showLine5000 && cacheRelativeDelta[idx].Line5000Color == line5000Color && cacheRelativeDelta[idx].Line5000Width == line5000Width && cacheRelativeDelta[idx].Line5000Alpha == line5000Alpha && cacheRelativeDelta[idx].ShowLineN5000 == showLineN5000 && cacheRelativeDelta[idx].LineN5000Color == lineN5000Color && cacheRelativeDelta[idx].LineN5000Width == lineN5000Width && cacheRelativeDelta[idx].LineN5000Alpha == lineN5000Alpha && cacheRelativeDelta[idx].ShowLine10000 == showLine10000 && cacheRelativeDelta[idx].Line10000Color == line10000Color && cacheRelativeDelta[idx].Line10000Width == line10000Width && cacheRelativeDelta[idx].Line10000Alpha == line10000Alpha && cacheRelativeDelta[idx].ShowLineN10000 == showLineN10000 && cacheRelativeDelta[idx].LineN10000Color == lineN10000Color && cacheRelativeDelta[idx].LineN10000Width == lineN10000Width && cacheRelativeDelta[idx].LineN10000Alpha == lineN10000Alpha && cacheRelativeDelta[idx].LineLabelColor == lineLabelColor && cacheRelativeDelta[idx].LineLabelBackground == lineLabelBackground && cacheRelativeDelta[idx].ShowUSZeroLine == showUSZeroLine && cacheRelativeDelta[idx].USStartTime == uSStartTime && cacheRelativeDelta[idx].USEndTime == uSEndTime && cacheRelativeDelta[idx].USZeroLineColor == uSZeroLineColor && cacheRelativeDelta[idx].USZeroLineWidth == uSZeroLineWidth && cacheRelativeDelta[idx].USZeroLineAlpha == uSZeroLineAlpha && cacheRelativeDelta[idx].USZeroLineDashStyle == uSZeroLineDashStyle && cacheRelativeDelta[idx].DisplayAsiaZeroLine == displayAsiaZeroLine && cacheRelativeDelta[idx].AsiaStartTime == asiaStartTime && cacheRelativeDelta[idx].AsiaEndTime == asiaEndTime && cacheRelativeDelta[idx].AsiaZeroLineColor == asiaZeroLineColor && cacheRelativeDelta[idx].AsiaZeroLineWidth == asiaZeroLineWidth && cacheRelativeDelta[idx].AsiaZeroLineAlpha == asiaZeroLineAlpha && cacheRelativeDelta[idx].AsiaZeroLineDashStyle == asiaZeroLineDashStyle && cacheRelativeDelta[idx].ShowEUZeroLine == showEUZeroLine && cacheRelativeDelta[idx].EUStartTime == eUStartTime && cacheRelativeDelta[idx].EUEndTime == eUEndTime && cacheRelativeDelta[idx].EUZeroLineColor == eUZeroLineColor && cacheRelativeDelta[idx].EUZeroLineWidth == eUZeroLineWidth && cacheRelativeDelta[idx].EUZeroLineAlpha == eUZeroLineAlpha && cacheRelativeDelta[idx].EUZeroLineDashStyle == eUZeroLineDashStyle && cacheRelativeDelta[idx].DisplayGlobalZeroLine == displayGlobalZeroLine && cacheRelativeDelta[idx].GlobalStartTime == globalStartTime && cacheRelativeDelta[idx].GlobalEndTime == globalEndTime && cacheRelativeDelta[idx].GlobalZeroLineColor == globalZeroLineColor && cacheRelativeDelta[idx].GlobalZeroLineWidth == globalZeroLineWidth && cacheRelativeDelta[idx].GlobalZeroLineAlpha == globalZeroLineAlpha && cacheRelativeDelta[idx].GlobalZeroLineDashStyle == globalZeroLineDashStyle && cacheRelativeDelta[idx].EqualsInput(input))
						return cacheRelativeDelta[idx];
			return CacheIndicator<RelativeIndicators.RelativeDelta>(new RelativeIndicators.RelativeDelta(){ BarColorDown = barColorDown, BarColorUp = barColorUp, ShadowColor = shadowColor, ShadowWidth = shadowWidth, WickColor = wickColor, WickWidth = wickWidth, MinSize = minSize, DaysToLoad = daysToLoad, ShowDivs = showDivs, HorizontalLineColor = horizontalLineColor, HorizontalLineWidth = horizontalLineWidth, HorizontalLineValue = horizontalLineValue, HorizontalLineAlphaPercent = horizontalLineAlphaPercent, ShowExtraLevels = showExtraLevels, ShowLine2500 = showLine2500, Line2500Color = line2500Color, Line2500Alpha = line2500Alpha, ShowLineN2500 = showLineN2500, LineN2500Color = lineN2500Color, LineN2500Width = lineN2500Width, LineN2500Alpha = lineN2500Alpha, ShowLine5000 = showLine5000, Line5000Color = line5000Color, Line5000Width = line5000Width, Line5000Alpha = line5000Alpha, ShowLineN5000 = showLineN5000, LineN5000Color = lineN5000Color, LineN5000Width = lineN5000Width, LineN5000Alpha = lineN5000Alpha, ShowLine10000 = showLine10000, Line10000Color = line10000Color, Line10000Width = line10000Width, Line10000Alpha = line10000Alpha, ShowLineN10000 = showLineN10000, LineN10000Color = lineN10000Color, LineN10000Width = lineN10000Width, LineN10000Alpha = lineN10000Alpha, LineLabelColor = lineLabelColor, LineLabelBackground = lineLabelBackground, ShowUSZeroLine = showUSZeroLine, USStartTime = uSStartTime, USEndTime = uSEndTime, USZeroLineColor = uSZeroLineColor, USZeroLineWidth = uSZeroLineWidth, USZeroLineAlpha = uSZeroLineAlpha, USZeroLineDashStyle = uSZeroLineDashStyle, DisplayAsiaZeroLine = displayAsiaZeroLine, AsiaStartTime = asiaStartTime, AsiaEndTime = asiaEndTime, AsiaZeroLineColor = asiaZeroLineColor, AsiaZeroLineWidth = asiaZeroLineWidth, AsiaZeroLineAlpha = asiaZeroLineAlpha, AsiaZeroLineDashStyle = asiaZeroLineDashStyle, ShowEUZeroLine = showEUZeroLine, EUStartTime = eUStartTime, EUEndTime = eUEndTime, EUZeroLineColor = eUZeroLineColor, EUZeroLineWidth = eUZeroLineWidth, EUZeroLineAlpha = eUZeroLineAlpha, EUZeroLineDashStyle = eUZeroLineDashStyle, DisplayGlobalZeroLine = displayGlobalZeroLine, GlobalStartTime = globalStartTime, GlobalEndTime = globalEndTime, GlobalZeroLineColor = globalZeroLineColor, GlobalZeroLineWidth = globalZeroLineWidth, GlobalZeroLineAlpha = globalZeroLineAlpha, GlobalZeroLineDashStyle = globalZeroLineDashStyle }, input, ref cacheRelativeDelta);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSEndTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle, displayAsiaZeroLine, asiaStartTime, asiaEndTime, asiaZeroLineColor, asiaZeroLineWidth, asiaZeroLineAlpha, asiaZeroLineDashStyle, showEUZeroLine, eUStartTime, eUEndTime, eUZeroLineColor, eUZeroLineWidth, eUZeroLineAlpha, eUZeroLineDashStyle, displayGlobalZeroLine, globalStartTime, globalEndTime, globalZeroLineColor, globalZeroLineWidth, globalZeroLineAlpha, globalZeroLineDashStyle);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSEndTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle, displayAsiaZeroLine, asiaStartTime, asiaEndTime, asiaZeroLineColor, asiaZeroLineWidth, asiaZeroLineAlpha, asiaZeroLineDashStyle, showEUZeroLine, eUStartTime, eUEndTime, eUZeroLineColor, eUZeroLineWidth, eUZeroLineAlpha, eUZeroLineDashStyle, displayGlobalZeroLine, globalStartTime, globalEndTime, globalZeroLineColor, globalZeroLineWidth, globalZeroLineAlpha, globalZeroLineDashStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			return indicator.RelativeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSEndTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle, displayAsiaZeroLine, asiaStartTime, asiaEndTime, asiaZeroLineColor, asiaZeroLineWidth, asiaZeroLineAlpha, asiaZeroLineDashStyle, showEUZeroLine, eUStartTime, eUEndTime, eUZeroLineColor, eUZeroLineWidth, eUZeroLineAlpha, eUZeroLineDashStyle, displayGlobalZeroLine, globalStartTime, globalEndTime, globalZeroLineColor, globalZeroLineWidth, globalZeroLineAlpha, globalZeroLineDashStyle);
		}

		public Indicators.RelativeIndicators.RelativeDelta RelativeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, Brush wickColor, int wickWidth, int minSize, int daysToLoad, bool showDivs, Brush horizontalLineColor, int horizontalLineWidth, double horizontalLineValue, int horizontalLineAlphaPercent, bool showExtraLevels, bool showLine2500, Brush line2500Color, int line2500Alpha, bool showLineN2500, Brush lineN2500Color, int lineN2500Width, int lineN2500Alpha, bool showLine5000, Brush line5000Color, int line5000Width, int line5000Alpha, bool showLineN5000, Brush lineN5000Color, int lineN5000Width, int lineN5000Alpha, bool showLine10000, Brush line10000Color, int line10000Width, int line10000Alpha, bool showLineN10000, Brush lineN10000Color, int lineN10000Width, int lineN10000Alpha, Brush lineLabelColor, Brush lineLabelBackground, bool showUSZeroLine, string uSStartTime, string uSEndTime, Brush uSZeroLineColor, int uSZeroLineWidth, int uSZeroLineAlpha, DashStyleHelper uSZeroLineDashStyle, bool displayAsiaZeroLine, string asiaStartTime, string asiaEndTime, Brush asiaZeroLineColor, int asiaZeroLineWidth, int asiaZeroLineAlpha, DashStyleHelper asiaZeroLineDashStyle, bool showEUZeroLine, string eUStartTime, string eUEndTime, Brush eUZeroLineColor, int eUZeroLineWidth, int eUZeroLineAlpha, DashStyleHelper eUZeroLineDashStyle, bool displayGlobalZeroLine, string globalStartTime, string globalEndTime, Brush globalZeroLineColor, int globalZeroLineWidth, int globalZeroLineAlpha, DashStyleHelper globalZeroLineDashStyle)
		{
			return indicator.RelativeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, wickColor, wickWidth, minSize, daysToLoad, showDivs, horizontalLineColor, horizontalLineWidth, horizontalLineValue, horizontalLineAlphaPercent, showExtraLevels, showLine2500, line2500Color, line2500Alpha, showLineN2500, lineN2500Color, lineN2500Width, lineN2500Alpha, showLine5000, line5000Color, line5000Width, line5000Alpha, showLineN5000, lineN5000Color, lineN5000Width, lineN5000Alpha, showLine10000, line10000Color, line10000Width, line10000Alpha, showLineN10000, lineN10000Color, lineN10000Width, lineN10000Alpha, lineLabelColor, lineLabelBackground, showUSZeroLine, uSStartTime, uSEndTime, uSZeroLineColor, uSZeroLineWidth, uSZeroLineAlpha, uSZeroLineDashStyle, displayAsiaZeroLine, asiaStartTime, asiaEndTime, asiaZeroLineColor, asiaZeroLineWidth, asiaZeroLineAlpha, asiaZeroLineDashStyle, showEUZeroLine, eUStartTime, eUEndTime, eUZeroLineColor, eUZeroLineWidth, eUZeroLineAlpha, eUZeroLineDashStyle, displayGlobalZeroLine, globalStartTime, globalEndTime, globalZeroLineColor, globalZeroLineWidth, globalZeroLineAlpha, globalZeroLineDashStyle);
		}
	}
}

#endregion
