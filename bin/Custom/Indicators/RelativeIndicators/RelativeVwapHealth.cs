#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    /// <summary>
    /// v3.2.2: Indicador INDEPENDIENTE de health score para VWAPs.
    /// Calcula su propio MFE/MAE y aplica suavizado EMA para visualización limpia.
    /// NO afecta la lógica de trading de RelativeVwap.
    /// </summary>
    public class RelativeVwapHealth : Indicator
    {
        // Raw health scores (accumulated MFE/MAE, same formula as RelativeVwap)
        private double _highRunningMFE = 0;
        private double _highRunningMAE = 0;
        private double _lowRunningMFE = 0;
        private double _lowRunningMAE = 0;

        // VWAP references from parent
        private double _currentHighVWAP = 0;
        private double _currentLowVWAP = 0;
        private bool _hasHighVWAP = false;
        private bool _hasLowVWAP = false;

        // Previous anchor tracking (reset on new VWAP)
        private double _lastHighAnchor = 0;
        private double _lastLowAnchor = 0;

        // EMA smoothing
        private double _emaHigh = 0;
        private double _emaLow = 0;
        private bool _emaHighInit = false;
        private bool _emaLowInit = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description     = "Health Score INDEPENDIENTE de los VWAPs — suavizado EMA";
                Name            = "RelativeVwapHealth";
                Calculate       = Calculate.OnBarClose;
                IsOverlay       = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                AddPlot(new Stroke(Brushes.RoyalBlue, 2), PlotStyle.Line, "Supply");
                AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "Demand");

                AddLine(new Stroke(Brushes.LimeGreen, DashStyleHelper.Dash, 1), 3.0, "Strong (3.0)");
                AddLine(new Stroke(Brushes.OrangeRed, DashStyleHelper.Dash, 1), 2.0, "Weak (2.0)");
                AddLine(new Stroke(Brushes.DimGray, DashStyleHelper.Dot, 1), 0.0, "Zero");

                HighHealthColor = Brushes.RoyalBlue;
                LowHealthColor  = Brushes.White;
                SmoothingPeriod = 20;
            }
            else if (State == State.Configure)
            {
                Plots[0].Brush = HighHealthColor;
                Plots[1].Brush = LowHealthColor;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
            {
                Values[0][0] = 0;
                Values[1][0] = 0;
                return;
            }

            // Read VWAP values from parent RelativeVwap static fields
            _currentHighVWAP = RelativeIndicators.RelativeVwap.SharedCurrentHighVWAP;
            _currentLowVWAP  = RelativeIndicators.RelativeVwap.SharedCurrentLowVWAP;
            _hasHighVWAP = _currentHighVWAP > 0;
            _hasLowVWAP  = _currentLowVWAP > 0;

            double tickSize = TickSize > 0 ? TickSize : 0.25;
            double high = High[0];
            double low  = Low[0];

            // Detect anchor changes → reset
            if (_hasHighVWAP && Math.Abs(_currentHighVWAP - _lastHighAnchor) > tickSize * 5)
            {
                _highRunningMFE = 0;
                _highRunningMAE = 0;
                _emaHigh = 0;
                _emaHighInit = false;
                _lastHighAnchor = _currentHighVWAP;
            }
            if (_hasLowVWAP && Math.Abs(_currentLowVWAP - _lastLowAnchor) > tickSize * 5)
            {
                _lowRunningMFE = 0;
                _lowRunningMAE = 0;
                _emaLow = 0;
                _emaLowInit = false;
                _lastLowAnchor = _currentLowVWAP;
            }

            // === HIGH VWAP (Supply) — accumulate MFE/MAE ===
            double rawHigh = 0;
            if (_hasHighVWAP)
            {
                double distBelow = (_currentHighVWAP - low) / tickSize;
                if (distBelow > 0) _highRunningMFE += distBelow;
                double distAbove = (high - _currentHighVWAP) / tickSize;
                if (distAbove > 0) _highRunningMAE += distAbove;
                rawHigh = _highRunningMFE / (_highRunningMAE + 1.0);
            }

            // === LOW VWAP (Demand) — accumulate MFE/MAE ===
            double rawLow = 0;
            if (_hasLowVWAP)
            {
                double distAbove = (high - _currentLowVWAP) / tickSize;
                if (distAbove > 0) _lowRunningMFE += distAbove;
                double distBelow = (_currentLowVWAP - low) / tickSize;
                if (distBelow > 0) _lowRunningMAE += distBelow;
                rawLow = _lowRunningMFE / (_lowRunningMAE + 1.0);
            }

            // === EMA smoothing ===
            double k = 2.0 / (SmoothingPeriod + 1);

            if (!_emaHighInit && rawHigh > 0)
            {
                _emaHigh = rawHigh;
                _emaHighInit = true;
            }
            else if (_emaHighInit)
            {
                _emaHigh = k * rawHigh + (1.0 - k) * _emaHigh;
            }

            if (!_emaLowInit && rawLow > 0)
            {
                _emaLow = rawLow;
                _emaLowInit = true;
            }
            else if (_emaLowInit)
            {
                _emaLow = k * rawLow + (1.0 - k) * _emaLow;
            }

            Values[0][0] = _emaHighInit ? _emaHigh : 0;
            Values[1][0] = _emaLowInit ? _emaLow : 0;
        }

        #region Rendering — Labels at right edge

        private SharpDX.DirectWrite.TextFormat _labelFmt;
        private SharpDX.Direct2D1.SolidColorBrush _supplyBrush;
        private SharpDX.Direct2D1.SolidColorBrush _demandBrush;

        public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();
            _labelFmt?.Dispose(); _labelFmt = null;
            _supplyBrush?.Dispose(); _supplyBrush = null;
            _demandBrush?.Dispose(); _demandBrush = null;

            if (RenderTarget != null)
            {
                _labelFmt = new SharpDX.DirectWrite.TextFormat(
                    NinjaTrader.Core.Globals.DirectWriteFactory, "Consolas",
                    SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 15f);

                var hc = ((SolidColorBrush)HighHealthColor).Color;
                _supplyBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color(hc.R, hc.G, hc.B, (byte)255));

                var lc = ((SolidColorBrush)LowHealthColor).Color;
                _demandBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color(lc.R, lc.G, lc.B, (byte)255));
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (RenderTarget == null || _labelFmt == null || ChartBars == null) return;
            if (CurrentBar < 2) return;

            float rightX = (float)ChartPanel.W - 100f;

            // Supply label
            double supVal = Values[0].IsValidDataPointAt(CurrentBar) ? Values[0].GetValueAt(CurrentBar) : 0;
            float supY = (float)chartScale.GetYByValue(supVal);
            string supText = string.Format("Supply {0:F1}", supVal);
            if (_supplyBrush != null)
                RenderTarget.DrawText(supText, _labelFmt, new SharpDX.RectangleF(rightX, supY - 10, 150, 22), _supplyBrush);

            // Demand label
            double demVal = Values[1].IsValidDataPointAt(CurrentBar) ? Values[1].GetValueAt(CurrentBar) : 0;
            float demY = (float)chartScale.GetYByValue(demVal);
            string demText = string.Format("Demand {0:F1}", demVal);
            if (_demandBrush != null)
                RenderTarget.DrawText(demText, _labelFmt, new SharpDX.RectangleF(rightX, demY - 10, 150, 22), _demandBrush);
        }

        #endregion

        #region Properties

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "High VWAP Color (Supply)", GroupName = "Colores", Order = 1)]
        public Brush HighHealthColor { get; set; }

        [Browsable(false)]
        public string HighHealthColorSerializable
        {
            get { return Serialize.BrushToString(HighHealthColor); }
            set { HighHealthColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Low VWAP Color (Demand)", GroupName = "Colores", Order = 2)]
        public Brush LowHealthColor { get; set; }

        [Browsable(false)]
        public string LowHealthColorSerializable
        {
            get { return Serialize.BrushToString(LowHealthColor); }
            set { LowHealthColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Periodo Suavizado EMA", Description = "Barras para suavizar el score (mayor = más suave)",
                 GroupName = "Parámetros", Order = 1)]
        public int SmoothingPeriod { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeVwapHealth[] cacheRelativeVwapHealth;
		public RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			return RelativeVwapHealth(Input, highHealthColor, lowHealthColor, smoothingPeriod);
		}

		public RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(ISeries<double> input, Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			if (cacheRelativeVwapHealth != null)
				for (int idx = 0; idx < cacheRelativeVwapHealth.Length; idx++)
					if (cacheRelativeVwapHealth[idx] != null && cacheRelativeVwapHealth[idx].HighHealthColor == highHealthColor && cacheRelativeVwapHealth[idx].LowHealthColor == lowHealthColor && cacheRelativeVwapHealth[idx].SmoothingPeriod == smoothingPeriod && cacheRelativeVwapHealth[idx].EqualsInput(input))
						return cacheRelativeVwapHealth[idx];
			return CacheIndicator<RelativeIndicators.RelativeVwapHealth>(new RelativeIndicators.RelativeVwapHealth(){ HighHealthColor = highHealthColor, LowHealthColor = lowHealthColor, SmoothingPeriod = smoothingPeriod }, input, ref cacheRelativeVwapHealth);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			return indicator.RelativeVwapHealth(Input, highHealthColor, lowHealthColor, smoothingPeriod);
		}

		public Indicators.RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(ISeries<double> input , Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			return indicator.RelativeVwapHealth(input, highHealthColor, lowHealthColor, smoothingPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			return indicator.RelativeVwapHealth(Input, highHealthColor, lowHealthColor, smoothingPeriod);
		}

		public Indicators.RelativeIndicators.RelativeVwapHealth RelativeVwapHealth(ISeries<double> input , Brush highHealthColor, Brush lowHealthColor, int smoothingPeriod)
		{
			return indicator.RelativeVwapHealth(input, highHealthColor, lowHealthColor, smoothingPeriod);
		}
	}
}

#endregion
