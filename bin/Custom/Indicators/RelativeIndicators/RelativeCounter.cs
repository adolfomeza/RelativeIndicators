using System;
using System.Windows.Media;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.Cbi;
using System.Windows;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Gui;

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public class RelativeCounter : Indicator
    {
        private double volume;
        private bool isVolume, isVolumeBase, isTimeBased;
        private System.Timers.Timer updateTimer;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Volume Counter displayed above the current candle.";
                Name = "RelativeCounter";
                Calculate = Calculate.OnEachTick;
                CountDown = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                IsChartOnly = true;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                ShowPercent = true;
                VerticalOffset = 10.0;
                FontSize = 12;
                TextColor = Brushes.White;
            }
            else if (State == State.DataLoaded)
            {
                isVolume = BarsPeriod.BarsPeriodType == BarsPeriodType.Volume;
                isVolumeBase = (BarsPeriod.BarsPeriodType == BarsPeriodType.HeikenAshi || BarsPeriod.BarsPeriodType == BarsPeriodType.PriceOnVolume || BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric) && BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Volume;
                isTimeBased = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute || BarsPeriod.BarsPeriodType == BarsPeriodType.Second || BarsPeriod.BarsPeriodType == BarsPeriodType.Day
                    || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Day;
                
                if (isTimeBased)
                {
                    if (updateTimer == null)
                    {
                        updateTimer = new System.Timers.Timer(250); // Update 4 times per second
                        updateTimer.Elapsed += OnTimerTick;
                        updateTimer.AutoReset = true;
                        updateTimer.Enabled = true;
                    }
                }
            }
            else if (State == State.Terminated)
            {
                if (updateTimer != null)
                {
                    updateTimer.Enabled = false;
                    updateTimer.Elapsed -= OnTimerTick;
                    updateTimer.Dispose();
                    updateTimer = null;
                }
            }
        }

        private void OnTimerTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Update if we have a ChartControl, regardless of State (to handle "live" historical bars)
            if (ChartControl != null && Bars != null)
            {
                // Force update on UI thread
                ChartControl.Dispatcher.InvokeAsync(() => 
                {
                    // Only update if we are at the last bar
                    if (CurrentBar == Bars.Count - 1)
                    {
                        UpdateDisplay();
                    }
                });
            }
        }

        private void UpdateDisplay()
        {
            try
            {
                if (State == State.Terminated || Bars == null || Bars.Count == 0 || Instrument == null) return;
                
                // Use the last bar index explicitly since CurrentBar is not valid in Timer events
                int idx = Bars.Count - 1;
                if (idx < 0) return;

                volume = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency
                    ? Core.Globals.ToCryptocurrencyVolume((long)Bars.GetVolume(idx))
                    : Bars.GetVolume(idx);

                double volumeCount;

                if (ShowPercent)
                {
                    volumeCount = CountDown
                        ? (1 - Bars.PercentComplete) * 100
                        : Bars.PercentComplete * 100;
                }
                else
                {
                    if (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick)
                    {
                        // Note: Bars.TickCount might rely on CurrentBar, so this might be inaccurate in Timer
                        // But for TimeBased bars (our focus), we skip this.
                        volumeCount = CountDown
                            ? BarsPeriod.Value - Bars.TickCount
                            : Bars.TickCount;
                    }
                    else if (isVolume || isVolumeBase)
                    {
                        double totalVolume = isVolumeBase ? BarsPeriod.BaseBarsPeriodValue : BarsPeriod.Value;
                        volumeCount = CountDown
                            ? totalVolume - volume
                            : volume;
                    }
                    else if (isTimeBased)
                    {
                        double totalSeconds = 0;
                        if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second) totalSeconds = BarsPeriod.Value;
                        else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute) totalSeconds = BarsPeriod.Value * 60;
                        else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Day) totalSeconds = 86400;
                        
                        if (totalSeconds == 0 && (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute))
                        {
                            if (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second) totalSeconds = BarsPeriod.BaseBarsPeriodValue;
                            else if (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute) totalSeconds = BarsPeriod.BaseBarsPeriodValue * 60;
                        }

                        if (totalSeconds > 0)
                        {
                            // Use Bars.GetTime(idx) for smoother countdown
                            DateTime barTime = Bars.GetTime(idx);
                            
                            if (CountDown && barTime > DateTime.Now)
                            {
                                TimeSpan remaining = barTime.Subtract(DateTime.Now);
                                volumeCount = Math.Max(0, remaining.TotalSeconds);
                            }
                            else
                            {
                                volumeCount = CountDown
                                    ? totalSeconds * (1 - Bars.PercentComplete)
                                    : totalSeconds * Bars.PercentComplete;
                            }
                        }
                        else
                        {
                            volumeCount = 0; 
                        }
                    }
                    else
                    {
                        volumeCount = 0;
                    }
                }

                string volumeText;
                
                if (ShowPercent)
                {
                    volumeText = volumeCount.ToString("F0") + "%";
                }
                else if (isTimeBased)
                {
                    // Format as mm:ss if it's time-based and not percentage
                    TimeSpan t = TimeSpan.FromSeconds(volumeCount);
                    volumeText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                    // If hours are needed
                    if (t.TotalHours >= 1)
                        volumeText = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
                }
                else
                {
                    volumeText = volumeCount.ToString("F0");
                }

                // Debug: Print time calculation
                // if (State == State.Realtime)
                // {
                //    Print(string.Format("Counter Debug: Time={0}, Now={1}, Val={2}", Bars.GetTime(idx), DateTime.Now, volumeText));
                // }

                double adjustedVerticalOffset = Bars.GetHigh(idx) + (VerticalOffset * TickSize);

                // Draw the text and capture the object to modify properties
                // Use -HorizontalOffset to shift text to the right (future bars)
                NinjaTrader.NinjaScript.DrawingTools.Text myText = Draw.Text(this, "RelativeCounter", volumeText, -HorizontalOffset, adjustedVerticalOffset, TextColor);
                
                // Improve visibility
                myText.AreaBrush = Brushes.Black;
                myText.AreaOpacity = 40; 
                myText.ZOrderType = DrawingToolZOrder.Normal;
            }
            catch (Exception e)
            {
                Print("RelativeCounter CRASHED: " + e.ToString());
            }
        }

        protected override void OnBarUpdate()
        {
            // For time-based bars, let the Timer handle the update in Realtime
            if (isTimeBased && State == State.Realtime) return;
            
            UpdateDisplay();
        }

        [NinjaScriptProperty]
        [Display(Name = "CountDown", GroupName = "Parameters", Order = 0)]
        public bool CountDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ShowPercent", GroupName = "Parameters", Order = 1)]
        public bool ShowPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Offset", GroupName = "Parameters", Order = 2)]
        public double VerticalOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Horizontal Offset (Bars)", GroupName = "Parameters", Order = 3)]
        public int HorizontalOffset { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Font Size", GroupName = "Parameters", Order = 4)]
        public int FontSize { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Text Color", GroupName = "Parameters", Order = 5)]
        public Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeCounter[] cacheRelativeCounter;
		public RelativeIndicators.RelativeCounter RelativeCounter(bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			return RelativeCounter(Input, countDown, showPercent, verticalOffset, horizontalOffset, fontSize, textColor);
		}

		public RelativeIndicators.RelativeCounter RelativeCounter(ISeries<double> input, bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			if (cacheRelativeCounter != null)
				for (int idx = 0; idx < cacheRelativeCounter.Length; idx++)
					if (cacheRelativeCounter[idx] != null && cacheRelativeCounter[idx].CountDown == countDown && cacheRelativeCounter[idx].ShowPercent == showPercent && cacheRelativeCounter[idx].VerticalOffset == verticalOffset && cacheRelativeCounter[idx].HorizontalOffset == horizontalOffset && cacheRelativeCounter[idx].FontSize == fontSize && cacheRelativeCounter[idx].TextColor == textColor && cacheRelativeCounter[idx].EqualsInput(input))
						return cacheRelativeCounter[idx];
			return CacheIndicator<RelativeIndicators.RelativeCounter>(new RelativeIndicators.RelativeCounter(){ CountDown = countDown, ShowPercent = showPercent, VerticalOffset = verticalOffset, HorizontalOffset = horizontalOffset, FontSize = fontSize, TextColor = textColor }, input, ref cacheRelativeCounter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeCounter RelativeCounter(bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			return indicator.RelativeCounter(Input, countDown, showPercent, verticalOffset, horizontalOffset, fontSize, textColor);
		}

		public Indicators.RelativeIndicators.RelativeCounter RelativeCounter(ISeries<double> input , bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			return indicator.RelativeCounter(input, countDown, showPercent, verticalOffset, horizontalOffset, fontSize, textColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeCounter RelativeCounter(bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			return indicator.RelativeCounter(Input, countDown, showPercent, verticalOffset, horizontalOffset, fontSize, textColor);
		}

		public Indicators.RelativeIndicators.RelativeCounter RelativeCounter(ISeries<double> input , bool countDown, bool showPercent, double verticalOffset, int horizontalOffset, int fontSize, Brush textColor)
		{
			return indicator.RelativeCounter(input, countDown, showPercent, verticalOffset, horizontalOffset, fontSize, textColor);
		}
	}
}

#endregion
