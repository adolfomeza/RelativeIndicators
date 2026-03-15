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
    // v2.2.7: Enum for trading mode selection
    public enum TradingModeType
    {
        Auto,           // Detect trend automatically, block reversals when trend active
        TrendOnly,      // Only trend trades (ignore reversals even when no trend detected)
        ReversalOnly    // Only reversal trades (ignore trend signals)
    }

	public partial class RelativeVwap
	{
		// ===========================================
		// TRADING PROPERTIES
		// ===========================================
		
		[TypeConverter(typeof(NinjaTrader.NinjaScript.AccountNameConverter))]
		[Display(Name = "Cuenta", Description = "Selecciona la cuenta para operar (ej: Sim101)", GroupName = "11. Gestión de Riesgo", Order = 1)]
		public string SelectedAccount { get; set; }

		[Display(Name = "Usar Gestión de Riesgo", Description = "Si TRUE, calcula contratos basándose en % de riesgo del balance. Si FALSE, usa cantidad fija.", GroupName = "11. Gestión de Riesgo", Order = 2)]
		public bool UseRiskBasedSizing { get; set; } = true;

		[Range(0.001, 10.0)]
		[Display(Name = "Riesgo (%)", Description = "Porcentaje del balance a arriesgar por operación (ej: 1.0 = 1%)", GroupName = "11. Gestión de Riesgo", Order = 3)]
		public double RiskPercentage { get; set; } = 1.0;

        [Range(1000, 10000000)]
        [Display(Name = "Capital Simulado", Description = "Tamaño de cuenta para cálculos de riesgo histórico cuando no hay cuenta conectada", GroupName = "11. Gestión de Riesgo", Order = 4)]
        public double SimulatedBalance { get; set; } = 50000;

		[Range(1, 1000)]
		[Display(Name = "Cantidad Fija", Description = "Número de contratos (solo si Gestión de Riesgo = FALSE)", GroupName = "11. Gestión de Riesgo", Order = 5)]
		public int TradeQuantity { get; set; } = 1;

		[Range(0, 500)]
		[Display(Name = "Offset Stop Loss (Ticks)", Description = "Distancia extra desde el anclaje para el SL", GroupName = "11. Gestión de Riesgo", Order = 6)]
		public int StopAnchorOffsetTicks { get; set; } = 5;

		[Display(Name = "Trail SL a VWAP tras TP1", Description = "Si TRUE, después de alcanzar TP1 el SL se mueve al VWAP origen y hace trailing con él", GroupName = "11. Gestión de Riesgo", Order = 7)]
		public bool TrailSLToVwapAfterTP1 { get; set; } = false;

		// v2.2.7: Trend Mode Properties
		[Display(Name = "Modo de Trading", Description = "Auto: Detecta tendencia automáticamente. TrendOnly: Solo trades de tendencia. ReversalOnly: Solo reversals (ignora tendencias).", GroupName = "11. Gestión de Riesgo", Order = 10)]
		public TradingModeType TradingMode { get; set; } = TradingModeType.Auto;

		[Range(0, 10000)]
		[Display(Name = "Umbral Delta Tendencia", Description = "Valor mínimo de delta (global Y sesión) para activar modo tendencia. Ej: 500 = ambos deltas deben superar ±500", GroupName = "11. Gestión de Riesgo", Order = 11)]
		public double TrendDeltaThreshold { get; set; } = 500;

		[XmlIgnore]
		[Display(Name = "Color Trade Tendencia", Description = "Color para trades en modo tendencia (diferenciados de reversals)", GroupName = "11. Gestión de Riesgo", Order = 12)]
		public Brush TrendTradeColor { get; set; } = Brushes.Cyan;
		[Browsable(false)] public string TrendTradeColorSerializable { get { return Serialize.BrushToString(TrendTradeColor); } set { TrendTradeColor = Serialize.StringToBrush(value); } }

		[Display(Name = "Ventana de Trading", Description = "Si TRUE, solo genera señales dentro del horario configurado (Inicio → Fin Ventana)", GroupName = "11. Gestión de Riesgo", Order = 13)]
		public bool UseTradeWindow { get; set; } = true;

		[Display(Name = "Inicio Ventana (HH:mm)", Description = "Hora de inicio para tomar trades (formato 24h, hora del chart). Default: inicio sesión USA", GroupName = "11. Gestión de Riesgo", Order = 14)]
		public string TradeWindowStart { get; set; } = "09:30";

		[Display(Name = "Fin Ventana (HH:mm)", Description = "Hora de fin para tomar trades (formato 24h, hora del chart). Default: 12:30 PM", GroupName = "11. Gestión de Riesgo", Order = 15)]
		public string TradeWindowEnd { get; set; } = "12:30";

		[Display(Name = "Solo si Flat (Sin Posición)", Description = "Si TRUE, solo entra si la cuenta está flat (sin posiciones abiertas). CRÍTICO para evitar entradas múltiples.", GroupName = "11. Gestión de Riesgo", Order = 8)]
		public bool OnlyEnterWhenFlat { get; set; } = true;

		[Display(Name = "Mostrar Líneas Históricas", Description = "Si TRUE, las líneas de SL/TP permanecen visibles después de cerrar la operación para análisis histórico.", GroupName = "11. Gestión de Riesgo", Order = 9)]
		public bool ShowHistoricalTradeLines { get; set; } = true;


		// ===========================================
		// VISUALIZATION PROPERTIES
		// ===========================================
		[XmlIgnore]
		[Display(Name = "Color Ganador (Win)", Description = "Color para trades ganadores (TP)", GroupName = "12. Visualización de Trades", Order = 1)]
		public Brush WinTradeColor { get; set; } = Brushes.LimeGreen;
		[Browsable(false)] public string WinTradeColorSerializable { get { return Serialize.BrushToString(WinTradeColor); } set { WinTradeColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Perdedor (Loss)", Description = "Color para trades perdedores (SL)", GroupName = "12. Visualización de Trades", Order = 2)]
		public Brush LossTradeColor { get; set; } = Brushes.Red;
		[Browsable(false)] public string LossTradeColorSerializable { get { return Serialize.BrushToString(LossTradeColor); } set { LossTradeColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Línea Neutral", Description = "Color para la línea de ejecución (Entry -> Exit)", GroupName = "12. Visualización de Trades", Order = 3)]
		public Brush ExecutionLineColor { get; set; } = Brushes.Gray;
		[Browsable(false)] public string ExecutionLineColorSerializable { get { return Serialize.BrushToString(ExecutionLineColor); } set { ExecutionLineColor = Serialize.StringToBrush(value); } }

        [Display(Name = "Estilo de Línea", Description = "Estilo de la línea de ejecución", GroupName = "12. Visualización de Trades", Order = 4)]
        public DashStyleHelper ExecutionLineStyle { get; set; } = DashStyleHelper.Dash;

        [Range(1, 10)]
        [Display(Name = "Grosor de Línea", Description = "Grosor de la línea de ejecución", GroupName = "12. Visualización de Trades", Order = 5)]
        public int ExecutionLineWidth { get; set; } = 1;

        [Range(6, 30)]
        [Display(Name = "Tamaño Texto Trade", Description = "Tamaño de la fuente del resultado", GroupName = "12. Visualización de Trades", Order = 6)]
        public int TradeResultFontSize { get; set; } = 11;
        
        [Display(Name = "Texto en Negrita", Description = "Si TRUE, el texto del resultado se muestra en negrita", GroupName = "12. Visualización de Trades", Order = 7)]
        public bool TradeResultFontBold { get; set; } = true;

        [Range(0.1, 5.0)]
        [Display(Name = "Distancia Etiqueta (ATR)", Description = "Multiplicador de ATR para la distancia de las etiquetas TP/SL desde el precio", GroupName = "12. Visualización de Trades", Order = 8)]
        public double TradeLabelDistanceATR { get; set; } = 1.0;

		// ===========================================
		// INTERNAL TRADING STATE
		// ===========================================
		private Account 			_tradingAccount 		= null;
		private bool 				_isTradingEnabled 		= false;
		
		// Smart Entry State
		private bool 				_isEntryArmed 			= false;
		private bool				_waitingForConfirmation	= false;
        
        // Ghost Visual State (Calculated every tick when Armed)
        private double              _ghostSL                = 0;
        private double              _ghostTP                = 0;
        private double              _ghostEntry             = 0; // Current Close
		
		// Button UI
		private System.Windows.Controls.Button _armButton;
		private System.Windows.Controls.Grid   _chartGrid;

		// ===========================================
		// RISK MANAGEMENT
		// ===========================================

		/// <summary>
		/// Calcula la cantidad de contratos basándose en el % de riesgo del balance de la cuenta
		/// </summary>
		private int CalculatePositionSize(double entryPrice, double stopLossPrice)
		{
			if (_tradingAccount == null)
			{
				Print("[RISK] ERROR: No account connected. Using fallback quantity = 1");
				return 1;
			}

			if (!UseRiskBasedSizing)
			{
				if (ShowDebugLogs) Print(string.Format("[RISK] Using fixed quantity: {0}", TradeQuantity));
				return TradeQuantity;
			}

			try
			{
				// 1. Obtener Balance de la Cuenta
				double accountBalance = _tradingAccount.Get(AccountItem.CashValue, Currency.UsDollar);

				if (accountBalance <= 0)
				{
					Print("[RISK] ERROR: Invalid account balance: " + accountBalance + ". Using fallback quantity = 1");
					return 1;
				}

				// 2. Calcular Riesgo en Dólares
				double riskInDollars = accountBalance * (RiskPercentage / 100.0);

				// 3. Calcular Distancia al Stop Loss en Ticks
				double distanceInPrice = Math.Abs(entryPrice - stopLossPrice);
				double distanceInTicks = distanceInPrice / TickSize;

				// 4. Calcular Valor de la Distancia en Dólares
				// PointValue = $ por punto (ej: ES = $50, NQ = $20, CL = $10)
				// Valor por tick = PointValue * TickSize
				double pointValue = Instrument.MasterInstrument.PointValue;
				double dollarValuePerTick = pointValue * TickSize;
				double distanceInDollars = distanceInTicks * dollarValuePerTick;

				if (distanceInDollars <= 0)
				{
					Print("[RISK] ERROR: Invalid SL distance: " + distanceInDollars + ". Using fallback quantity = 1");
					return 1;
				}

				// 5. Calcular Cantidad de Contratos
				double calculatedQty = riskInDollars / distanceInDollars;
				int quantity = Math.Max(1, (int)Math.Floor(calculatedQty)); // Mínimo 1 contrato

				// Log completo
				if (ShowDebugLogs)
				{
					Print("=== RISK CALCULATION ===");
					Print(string.Format("[RISK] Balance: ${0:F2} | Risk%: {1}% | Risk$: ${2:F2}",
						accountBalance, RiskPercentage, riskInDollars));
					Print(string.Format("[RISK] Entry: {0} | SL: {1} | Distance: {2:F2} ticks ({3:F2} pts)",
						entryPrice, stopLossPrice, distanceInTicks, distanceInPrice));
					Print(string.Format("[RISK] PointValue: ${0} | TickSize: {1} | $PerTick: ${2:F4} | SL Distance$: ${3:F2}",
						pointValue, TickSize, dollarValuePerTick, distanceInDollars));
					Print(string.Format("[RISK] Calculated Qty: {0:F2} → Rounded: {1} contracts",
						calculatedQty, quantity));
				}

				return quantity;
			}
			catch (Exception ex)
			{
				Print("[RISK] ERROR calculating position size: " + ex.Message);
				return 1; // Fallback seguro
			}
		}

		// ===========================================
		// INITIALIZATION & UI
		// ===========================================

		private void InitializeTrading()
		{
			// Called from State.DataLoaded
			if (ShowDebugLogs) Print("[TRADING] InitializeTrading called");
			if (ShowDebugLogs) Print("[TRADING] SelectedAccount: " + (SelectedAccount ?? "NULL"));

			if (SelectedAccount != null)
			{
				lock (Account.All)
				{
					if (ShowDebugLogs) Print("[TRADING] Searching for account in Account.All...");
					if (ShowDebugLogs) Print("[TRADING] Available accounts count: " + Account.All.Count);

					if (ShowDebugLogs)
					{
						foreach (var acc in Account.All)
						{
							if (ShowDebugLogs) Print("[TRADING] Available account: " + acc.Name);
						}
					}

					_tradingAccount = Account.All.FirstOrDefault(a => a.Name == SelectedAccount);
				}

				if (_tradingAccount != null)
				{
					if (ShowDebugLogs) Print("[TRADING] Account found and connected: " + _tradingAccount.Name);
					// Subscribe to events
					_tradingAccount.OrderUpdate += OnAccountOrderUpdate;
					_tradingAccount.ExecutionUpdate += OnAccountExecutionUpdate; // Re-enabled for entry price tracking
				}
				else
				{
					Print("[TRADING] ERROR: Account '" + SelectedAccount + "' not found in Account.All!");
				}
			}
			else
			{
				Print("[TRADING] WARNING: SelectedAccount is NULL. Trading disabled.");
			}
		}

		private void TerminateTrading()
		{
			// Called from State.Terminated
			if (_tradingAccount != null)
			{
				_tradingAccount.OrderUpdate -= OnAccountOrderUpdate;
				_tradingAccount.ExecutionUpdate -= OnAccountExecutionUpdate;
				_tradingAccount = null;
			}

			// Clean up trade stats
			_activeTradeStats.Clear();
			_entryFills.Clear();

			RemoveWpfControls();
		}

		private void CreateWpfControls()
		{
			// Only in Realtime
			if (ShowDebugLogs) Print("[TRADING] CreateWpfControls called");
			ChartControl chartControl = ChartControl;
			if (chartControl == null)
			{
				Print("[TRADING] ERROR: ChartControl is NULL!");
				return;
			}
			if (ShowDebugLogs) Print("[TRADING] ChartControl OK, proceeding...");
            
			chartControl.Dispatcher.InvokeAsync(() =>
			{
				_chartGrid = chartControl.Parent as System.Windows.Controls.Grid;
				if (_chartGrid == null) return;
                
                // CLEANUP ZOMBIE BUTTONS (Fix for orphaned controls)
                List<System.Windows.UIElement> toRemove = new List<System.Windows.UIElement>();
                foreach (var child in _chartGrid.Children)
                {
                    if (child is System.Windows.Controls.Button b && (b.Content.ToString() == "ARMAR ENTRADA" || b.Content.ToString() == "ESPERANDO CIERRE..."))
                    {
                        toRemove.Add((System.Windows.UIElement)child);
                        if (ShowDebugLogs) Print("Removing Zombie Button found on Grid.");
                    }
                }
				// v3.1.2: ARMAR ENTRADA button disabled — Signal 2 manual entry superseded by Touch Study auto-trade
				// _armButton creation commented out — keeping cleanup code above for zombie removal
				/*
				_armButton = new System.Windows.Controls.Button();
				_armButton.Content = "ARMAR ENTRADA";
				_armButton.Background = Brushes.SlateGray;
				_armButton.Foreground = Brushes.White;
				_armButton.FontWeight = FontWeights.Bold;
				_armButton.HorizontalAlignment = HorizontalAlignment.Right;
				_armButton.VerticalAlignment = VerticalAlignment.Bottom;
                _armButton.Width = 150;
                _armButton.Height = 30;
				_armButton.Margin = new Thickness(0, 0, 70, 60);
				_armButton.Padding = new Thickness(10, 5, 10, 5);
				_armButton.Click += OnArmButtonClick;
                System.Windows.Controls.Panel.SetZIndex(_armButton, 99);
				_chartGrid.Children.Add(_armButton);
				*/
			if (ShowDebugLogs) Print("[TRADING] Button successfully added to ChartGrid!");
			});
		}

		private void RemoveWpfControls()
		{
			if (_armButton != null)
			{
				ChartControl chartControl = ChartControl;
				if (chartControl != null)
				{
					chartControl.Dispatcher.InvokeAsync(() =>
					{
						if (_chartGrid != null)
						{
							_chartGrid.Children.Remove(_armButton);
							_armButton = null;
						}
					});
				}
			}
		}

		private void OnArmButtonClick(object sender, RoutedEventArgs e)
		{
			if (ShowDebugLogs) Print("[TRADING] Button Clicked!");

			if (_tradingAccount == null)
			{
				Print("[TRADING] ERROR: No Account Selected. Account is NULL!");
				Print("[TRADING] Selected Account Name: " + (SelectedAccount ?? "NULL"));
				return;
			}

			_isEntryArmed = !_isEntryArmed;
			UpdateArmButtonState();

			// Visual Feedback
			if (_isEntryArmed)
			{
				if (ShowDebugLogs) Print("[TRADING] System ARMED. Waiting for Signal 2 Close...");
				if (ShowDebugLogs) Print("[TRADING] Account: " + _tradingAccount.Name);
			}
			else
			{
				if (ShowDebugLogs) Print("[TRADING] System DISARMED.");
			}

			ChartControl.InvalidateVisual();
		}

		private void UpdateArmButtonState()
		{
			if (_armButton == null) return;
			
			if (_isEntryArmed)
			{
				_armButton.Content = "ESPERANDO CIERRE...";
				_armButton.Background = Brushes.Goldenrod;
			}
			else
			{
				_armButton.Content = "ARMAR ENTRADA";
				_armButton.Background = Brushes.SlateGray;
			}
		}

		// ===========================================
		// TRADING LOGIC
		// ===========================================
		
			// Called from OnBarUpdate (Every Tick)
		private void CheckSmartEntryLogic()
		{
			if (_tradingAccount == null) return;

			// STICKY TP UPDATE (Time-based: max 1x per second for performance)
			UpdateStickyTP();

			if (!_isEntryArmed) return;
			
			// 1. AUTO-DISARM CHECK (Every Tick)
			// If price touches the Internal VWAP while armed, disarm immediately to prevent entering on a failed/invalidated setup.
			// Query the current Internal VWAP values
			// Note: We use the 'Values' series for visual sync
			
			if (hasInternalHighVWAP)
			{
			    double iHigh = Values[2][0];
				if (High[0] >= iHigh) 
				{
				    Disarm("Price touched Internal High VWAP");
					return;
				}
			}
			
			if (hasInternalLowVWAP)
			{
			    double iLow = Values[3][0];
				if (Low[0] <= iLow)
				{
				    Disarm("Price touched Internal Low VWAP");
					return;
				}
			}
			
            // ------------------------------------------
            // GHOST CALCULATIONS (For Visuals)
            // ------------------------------------------
            _ghostEntry = Close[0];
            double gVwap = (High[0] < Values[0][0]) ? Values[0][0] : Values[1][0]; // Simple heuristic: nearest or opposing?
            // Actually, we anticipate the signal.
            // If Short (Price > InternalHigh), TP is InternalLow (Values[3]). SL is InternalHigh anchor.
            // If Long (Price < InternalLow), TP is InternalHigh (Values[2]). SL is InternalLow anchor.
            
            if (hasInternalHighVWAP && hasInternalLowVWAP)
            {
               double iHigh = Values[2][0];
               double iLow = Values[3][0];
               
               if (Close[0] > iHigh) // Potential Short
               {
                   _ghostTP = iLow;
                   // SL requires anchor price. We can approximate or use current Session High if internal logic aligns.
                   // Since we don't have easy access to specific anchor price variables here without lookup,
                   // we will use the Internal High VWAP + Offset as a proxy or 0 for now.
                   // Better: Use SessionHigh/Low if they align with Internal logic.
                   _ghostSL = 0; // Placeholder until we lookup exact anchor
               }
               else if (Close[0] < iLow) // Potential Long
               {
                   _ghostTP = iHigh;
                   _ghostSL = 0;
               }
            }
            
			// 2. ENTRY TRIGGER CHECK (Only on Bar Close / First Tick of New Bar)
			if (!IsFirstTickOfBar) return;

			// Look at index 1 - the bar that just closed
			// Check BOTH Global (yellow Entry) AND Internal (orange Int) signals
			bool entrySignalShort = (highSignal2BarIdx == CurrentBar - 1) || (internalHighSignal2BarIdx == CurrentBar - 1);
			bool entrySignalLong  = (lowSignal2BarIdx == CurrentBar - 1) || (internalLowSignal2BarIdx == CurrentBar - 1);

			if (ShowDebugLogs)
			{
				Print(string.Format("[TRADING] IsFirstTickOfBar=true | CurrentBar={0}", CurrentBar));
				Print(string.Format("[TRADING] Global: highSignal2BarIdx={0} | lowSignal2BarIdx={1}", highSignal2BarIdx, lowSignal2BarIdx));
				Print(string.Format("[TRADING] Internal: internalHighSignal2BarIdx={0} | internalLowSignal2BarIdx={1}", internalHighSignal2BarIdx, internalLowSignal2BarIdx));
				Print(string.Format("[TRADING] entrySignalShort={0} | entrySignalLong={1}", entrySignalShort, entrySignalLong));
			}

			if (entrySignalShort)
			{
				if (ShowDebugLogs) Print("[TRADING] SHORT SIGNAL DETECTED! Submitting entry...");
				SubmitSmartEntry(false); // Short
			}
			else if (entrySignalLong)
			{
				if (ShowDebugLogs) Print("[TRADING] LONG SIGNAL DETECTED! Submitting entry...");
				SubmitSmartEntry(true); // Long
			}
			else if (ShowDebugLogs)
			{
				Print("[TRADING] No entry signal on this bar close.");
			}
		}

        private void Disarm(string reason)
        {
            if (!_isEntryArmed) return;
            
            _isEntryArmed = false;
            UpdateArmButtonState();
            if (ShowDebugLogs) Print("[TRADING] System DISARMED: " + reason);
            ChartControl.InvalidateVisual();
        }
		
		private void SubmitSmartEntry(bool isLong)
		{
		    if (_tradingAccount == null) return;

			// v1.0.50: CRITICAL FIX - Check if account is flat before entering
			// This prevents multiple instances of the indicator from entering simultaneously
			if (OnlyEnterWhenFlat)
			{
				// Get position for THIS instrument on this account
				Position position = _tradingAccount.Positions.FirstOrDefault(p => p.Instrument == Instrument);

				if (position != null && position.MarketPosition != MarketPosition.Flat)
				{
					if (ShowDebugLogs) Print(string.Format("[TRADING] ENTRY BLOCKED - Account already has position: {0} {1} contracts",
						position.MarketPosition, Math.Abs(position.Quantity)));
					return;
				}

				// Also check if there are any pending entry orders for this instrument
				bool hasPendingEntry = _tradingAccount.Orders.Any(o =>
					o.Instrument == Instrument &&
					o.Name == "SmartEntry" &&
					(o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted));

				if (hasPendingEntry)
				{
					if (ShowDebugLogs) Print("[TRADING] ENTRY BLOCKED - Entry order already pending for this instrument");
					return;
				}
			}

			// Detect if signal is Global (yellow) or Internal (orange)
			bool isGlobalSignal = isLong ? (lowSignal2BarIdx == CurrentBar - 1) : (highSignal2BarIdx == CurrentBar - 1);

			// 1. Calculate Bracket Prices SNAPSHOT (Frozen at Entry Moment)
			// SL: Anchor +/- Offset
			// TP: Opposite VWAP

			if (isLong)
			{
			    // Determine anchor based on signal type
				double anchorPrice;
				if (isGlobalSignal)
				{
					// Global Signal: Use session anchor
					anchorPrice = (sessionLowBarIdx >= 0) ? Low.GetValueAt(sessionLowBarIdx) : Low[0];

					// Target Global High VWAP (Values[0]) - Use previous bar since current hasn't calculated
					if (hasHighVWAP && CurrentBar > 0 && !double.IsNaN(Values[0][1]) && Values[0][1] > 0)
						_pendingTpPrice = Values[0][1]; // Use PREVIOUS bar's VWAP
					else if (hasHighVWAP && !double.IsNaN(Values[0][0]) && Values[0][0] > 0)
						_pendingTpPrice = Values[0][0]; // Fallback to current if available
					else
						_pendingTpPrice = anchorPrice + 100 * TickSize; // Fallback: +100 ticks

					if (ShowDebugLogs) Print("[TRADING] LONG Entry - GLOBAL signal | Anchor: sessionLow=" + anchorPrice + " | TP: " + _pendingTpPrice);
				}
				else
				{
					// Internal Signal: Use internal anchor
					anchorPrice = (internalLowBarIdx >= 0) ? Low.GetValueAt(internalLowBarIdx) : Low[0];

					// Target Internal High VWAP (Values[2]) or fallback to Global
					if (hasInternalHighVWAP && !double.IsNaN(Values[2][0]) && Values[2][0] > 0)
						_pendingTpPrice = Values[2][0];
					else if (hasHighVWAP && !double.IsNaN(Values[0][0]) && Values[0][0] > 0)
						_pendingTpPrice = Values[0][0];
					else
						_pendingTpPrice = anchorPrice + 50 * TickSize; // Fallback: +50 ticks

					if (ShowDebugLogs) Print("[TRADING] LONG Entry - INTERNAL signal | Anchor: internalLow=" + anchorPrice + " | TP: " + _pendingTpPrice);
				}

				_pendingSlPrice = anchorPrice - (StopAnchorOffsetTicks * TickSize);
			}
			else
			{
			    // Determine anchor based on signal type
				double anchorPrice;
				if (isGlobalSignal)
				{
					// Global Signal: Use session anchor
					anchorPrice = (sessionHighBarIdx >= 0) ? High.GetValueAt(sessionHighBarIdx) : High[0];

					// Target Global Low VWAP (Values[1])
					// Use previous bar's VWAP value since current bar (IsFirstTickOfBar) hasn't calculated yet
					if (ShowDebugLogs)
					{
						Print("[TRADING] DEBUG - hasLowVWAP: " + hasLowVWAP);
						Print("[TRADING] DEBUG - Values[1][0]: " + Values[1][0]);
						Print("[TRADING] DEBUG - Values[1][1]: " + (CurrentBar > 0 ? Values[1][1] : 0));
						Print("[TRADING] DEBUG - sessionLowBarIdx: " + sessionLowBarIdx);
					}

					if (hasLowVWAP && CurrentBar > 0 && !double.IsNaN(Values[1][1]) && Values[1][1] > 0)
					{
						_pendingTpPrice = Values[1][1]; // Use PREVIOUS bar's VWAP
						if (ShowDebugLogs) Print("[TRADING] SHORT Entry - GLOBAL signal | Using Low VWAP[1] as TP: " + _pendingTpPrice);
					}
					else if (hasLowVWAP && !double.IsNaN(Values[1][0]) && Values[1][0] > 0)
					{
						_pendingTpPrice = Values[1][0]; // Fallback to current if available
						if (ShowDebugLogs) Print("[TRADING] SHORT Entry - GLOBAL signal | Using Low VWAP[0] as TP: " + _pendingTpPrice);
					}
					else
					{
						_pendingTpPrice = anchorPrice - 100 * TickSize; // Ultimate fallback
						if (ShowDebugLogs) Print("[TRADING] SHORT Entry - GLOBAL signal | FALLBACK TP (Low VWAP not available)");
					}

					if (ShowDebugLogs) Print("[TRADING] SHORT Entry - GLOBAL signal | Anchor: sessionHigh=" + anchorPrice + " | TP: " + _pendingTpPrice);
				}
				else
				{
					// Internal Signal: Use internal anchor
					anchorPrice = (internalHighBarIdx >= 0) ? High.GetValueAt(internalHighBarIdx) : High[0];

					// Target Internal Low VWAP (Values[3]) or fallback to Global
					if (hasInternalLowVWAP && !double.IsNaN(Values[3][0]) && Values[3][0] > 0)
						_pendingTpPrice = Values[3][0];
					else if (hasLowVWAP && !double.IsNaN(Values[1][0]) && Values[1][0] > 0)
						_pendingTpPrice = Values[1][0];
					else
						_pendingTpPrice = anchorPrice - 50 * TickSize; // Fallback: -50 ticks

					if (ShowDebugLogs) Print("[TRADING] SHORT Entry - INTERNAL signal | Anchor: internalHigh=" + anchorPrice + " | TP: " + _pendingTpPrice);
				}

				_pendingSlPrice = anchorPrice + (StopAnchorOffsetTicks * TickSize);
			}
			
			// Rounding
			_pendingSlPrice = Instrument.MasterInstrument.RoundToTickSize(_pendingSlPrice);
			_pendingTpPrice = Instrument.MasterInstrument.RoundToTickSize(_pendingTpPrice);

			OrderAction action = isLong ? OrderAction.Buy : OrderAction.Sell;

			// Validate prices
			if (_pendingTpPrice <= 0 || double.IsNaN(_pendingTpPrice))
			{
				Print("[TRADING] ERROR: Invalid TP price: " + _pendingTpPrice + ". Cannot submit entry.");
				return;
			}

			try
			{
				// v1.0.50: Calculate position size based on risk management
				double estimatedEntryPrice = Close[0]; // Use current Close as entry estimate for Market orders
				int calculatedQuantity = CalculatePositionSize(estimatedEntryPrice, _pendingSlPrice);

				if (calculatedQuantity <= 0)
				{
					Print("[TRADING] ERROR: Calculated quantity is 0 or negative. Cannot submit entry.");
					return;
				}

			    if (ShowDebugLogs) Print(string.Format("[TRADING] SMART ENTRY TRIGGERED: {0} {1} @ Market | SL: {2} | TP: {3}", action, calculatedQuantity, _pendingSlPrice, _pendingTpPrice));

				Order entryOrder = _tradingAccount.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, calculatedQuantity, 0, 0, "", "SmartEntry", DateTime.MaxValue, null);
				_tradingAccount.Submit(new[] { entryOrder }); // Fixed CS1503

				// Disarm immediately (UI update must be on UI thread)
				_isEntryArmed = false;
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => UpdateArmButtonState());
				}
			}
			catch (Exception ex)
			{
			    Print("[TRADING] EXECUTION ERROR: " + ex.Message);
			    _isEntryArmed = false;
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() => UpdateArmButtonState());
				}
			}
		}

		// Trading State Tracking
		private double _pendingSlPrice;
		private double _pendingTpPrice;
		private string _lastEntryOcoId;
		private double _filledQtySoFar; // Track cumulative fills to handle partials correctly?
		// Actually, OrderEventArgs provides 'FilledQuantity' (total filled) and we can diff it?
		// Or simpler: Handle 'Filled' events individually?
		// Account.OrderUpdate fires on state change.
		// Best practice for Partial: Track 'total submitted backets' vs 'total entry filled'.

		private Dictionary<string, double> _entryFills = new Dictionary<string, double>(); // OrderId -> Qty Covered

		// Trade Statistics Tracking
		private class TradeStats
		{
			public double EntryPrice;
			public double SlPrice;
			public double TpPrice;
			public int Quantity;
			public bool IsLong;
			public DateTime EntryTime;
		}
		private Dictionary<string, TradeStats> _activeTradeStats = new Dictionary<string, TradeStats>(); // OCO ID -> Stats

		private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
		{
		    // v3.1.0: Handle AUTO-TRADE exit fills
			if ((e.Order.Name.StartsWith("AutoSL_") || e.Order.Name.StartsWith("AutoTP_")) && e.OrderState == OrderState.Filled)
			{
				string exitType = e.Order.Name.StartsWith("AutoSL_") ? "SL" : "TP";
				string cfg = e.Order.Name.Substring(e.Order.Name.IndexOf('_') + 1);
				if (ShowDebugLogs) Print(string.Format("[AUTO-TRADE] <<< SALIDA por {0} | Config {1} | Precio={2:F2}", exitType, cfg, e.Order.AverageFillPrice));
				_autoTradeOpen = false;
				_autoTradeConfig = "";
				_autoTradeOcoId = "";
				return;
			}

		    // Only handle Smart orders (manual entry system)
			if (e.Order.Name != "SmartEntry" && e.Order.Name != "SmartSL" && e.Order.Name != "SmartTP") return;

			// 1. HANDLE ENTRY FILLS (Submit Brackets)
			if (e.Order.Name == "SmartEntry" && (e.OrderState == OrderState.Filled || e.OrderState == OrderState.PartFilled))
			{
			    // Calculate how much *new* quantity was just filled
				double totalFilled = e.Order.Filled; // Fixed CS1061
				double alreadyCovered = 0;
				if (_entryFills.ContainsKey(e.Order.OrderId)) alreadyCovered = _entryFills[e.Order.OrderId];

				double newFillQty = totalFilled - alreadyCovered;

				if (newFillQty > 0)
				{
				    // Submit Brackets for this chunk
					SubmitBrackets(e.Order, (int)newFillQty);

					// Update tracking
					if (_entryFills.ContainsKey(e.Order.OrderId)) _entryFills[e.Order.OrderId] = totalFilled;
					else _entryFills.Add(e.Order.OrderId, totalFilled);
				}
			}

			// 2. HANDLE EXIT FILLS (SL or TP) - Show Trade Statistics
			if ((e.Order.Name == "SmartSL" || e.Order.Name == "SmartTP") && e.OrderState == OrderState.Filled)
			{
				string ocoId = e.Order.Oco;
				if (_activeTradeStats.ContainsKey(ocoId))
				{
					ShowTradeStatistics(e.Order, _activeTradeStats[ocoId]);

					// Clean up visual markers ONLY if historical display is disabled
					if (!ShowHistoricalTradeLines)
					{
						RemoveDrawObject("SL_Line_" + ocoId);
						RemoveDrawObject("SL_Text_" + ocoId);
						RemoveDrawObject("TP_Line_" + ocoId);
						RemoveDrawObject("TP_Text_" + ocoId);
					}

					_activeTradeStats.Remove(ocoId); // Clean up stats
				}
			}

			// 3. DETECT MANUAL INTERVENTION ON TP (Sticky Override)
			if (e.Order.Name == "SmartTP")
			{
			    TrackStickyTP(e.Order);

			    // Simple Manual Move Detection:
			    // If we receive an update where LimitPrice Changed, but WE didn't initiate it?
			    // Hard to distinguish.
			    // For now, we will assume if the user is using Chart Trader to drag lines,
			    // we might fight them if we don't have a reliable flag.
			    // IMPROVEMENT: Add a physical button "Lock TP" or similiar?
			    // User request: "possibility to move myself... stay where I put it".
			    // Simplest: If the price difference is significant > 2 ticks?
			    // Or just check if ChangeOrder was called.
			    // NOTE: For v1, we will enable Sticky by default. To override, user might need to toggle something?
			    // Actually, simply: If _isTpManuallyMoved = true, stop updating.
			    // How to set true? Maybe double click button?
			    // Or: Detect if Order Limit Price != Our Calculated Target in the OrderUpdate event?
			}
		}

		private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
		{
			// Only capture entry fill prices
			if (e.Execution.Order.Name != "SmartEntry") return;

			// Update the entry price for all pending trades that don't have one yet
			foreach (var kvp in _activeTradeStats)
			{
				if (kvp.Value.EntryPrice == 0) // Not yet set
				{
					kvp.Value.EntryPrice = e.Execution.Price;
					if (ShowDebugLogs)
						Print(string.Format("[STATS] Entry price captured: {0} for OCO: {1}", e.Execution.Price, kvp.Key));
					break; // Only update the first one (most recent)
				}
			}
		}

		private void ShowTradeStatistics(Order exitOrder, TradeStats stats)
		{
			try
			{
				bool hitSL = (exitOrder.Name == "SmartSL");
				double exitPrice = exitOrder.AverageFillPrice;

				// If entry price wasn't captured, use estimate
				if (stats.EntryPrice == 0)
				{
					Print("[STATS] WARNING: Entry price not captured, using exit approximation");
					stats.EntryPrice = exitPrice; // This won't be accurate but prevents division by zero
				}

				// Calculate distances in ticks and points
				double slDistanceInPrice = Math.Abs(stats.EntryPrice - stats.SlPrice);
				double tpDistanceInPrice = Math.Abs(stats.EntryPrice - stats.TpPrice);
				double slDistanceInTicks = slDistanceInPrice / TickSize;
				double tpDistanceInTicks = tpDistanceInPrice / TickSize;

				// Calculate dollar values
				double pointValue = Instrument.MasterInstrument.PointValue;
				double slRiskPerContract = slDistanceInPrice * pointValue;
				double tpRewardPerContract = tpDistanceInPrice * pointValue;
				double totalSlRisk = slRiskPerContract * stats.Quantity;
				double totalTpReward = tpRewardPerContract * stats.Quantity;

				// Calculate actual P&L
				double actualDistanceInPrice = Math.Abs(exitPrice - stats.EntryPrice);
				double actualPnL = actualDistanceInPrice * pointValue * stats.Quantity;
				if (!stats.IsLong) actualPnL *= -1; // Invert for shorts
				if (hitSL) actualPnL *= -1; // Loss

				// Calculate R:R
				double rr = (slRiskPerContract > 0) ? (tpRewardPerContract / slRiskPerContract) : 0;

				// Display comprehensive statistics
				if (ShowDebugLogs)
				{
					Print("════════════════════════════════════════════════════════");
					Print("           TRADE CLOSED - " + (hitSL ? "STOP LOSS HIT" : "TAKE PROFIT HIT"));
					Print("════════════════════════════════════════════════════════");
					Print(string.Format("Direction:      {0}", stats.IsLong ? "LONG" : "SHORT"));
					Print(string.Format("Contracts:      {0}", stats.Quantity));
					Print(string.Format("Entry Price:    {0}", stats.EntryPrice));
					Print(string.Format("Exit Price:     {0}", exitPrice));
					Print(string.Format("SL Price:       {0}", stats.SlPrice));
					Print(string.Format("TP Price:       {0}", stats.TpPrice));
					Print("────────────────────────────────────────────────────────");
					Print(string.Format("SL Distance:    {0:F2} ticks ({1:F2} pts)", slDistanceInTicks, slDistanceInPrice));
					Print(string.Format("TP Distance:    {0:F2} ticks ({1:F2} pts)", tpDistanceInTicks, tpDistanceInPrice));
					Print("────────────────────────────────────────────────────────");
					Print(string.Format("Risk per Cntr:  ${0:F2}", slRiskPerContract));
					Print(string.Format("Reward per Cntr: ${0:F2}", tpRewardPerContract));
					Print(string.Format("Total Risk:     ${0:F2}", totalSlRisk));
					Print(string.Format("Total Reward:   ${0:F2}", totalTpReward));
					Print(string.Format("Risk/Reward:    1:{0:F2}", rr));
					Print("────────────────────────────────────────────────────────");
					Print(string.Format("Actual P&L:     ${0:F2} {1}", Math.Abs(actualPnL), actualPnL >= 0 ? "PROFIT ✓" : "LOSS ✗"));
					Print(string.Format("Duration:       {0}", (DateTime.Now - stats.EntryTime).ToString(@"hh\:mm\:ss")));
					Print("════════════════════════════════════════════════════════");
				}
			}
			catch (Exception ex)
			{
				Print("[STATS] ERROR showing trade statistics: " + ex.Message);
			}
		}

        private void SubmitBrackets(Order entryOrder, int quantity)
        {
            if (_tradingAccount == null) return;
            
            bool isLong = entryOrder.OrderAction == OrderAction.Buy;
            string ocoId = Guid.NewGuid().ToString("N");
            
            // SL Price
            double slPrice = _pendingSlPrice;
            if (slPrice <= 0) slPrice = isLong ? Low[0] - 10 * TickSize : High[0] + 10 * TickSize; // Fallback
            
            // TP Price
            double tpPrice = _pendingTpPrice;
             if (tpPrice <= 0) tpPrice = isLong ? High[0] + 20 * TickSize : Low[0] - 20 * TickSize; // Fallback
            
            // Create Orders
            OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.Buy;
            
            // Stop Loss
            Order slOrder = _tradingAccount.CreateOrder(Instrument, exitAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day, quantity, 0, slPrice, ocoId, "SmartSL", DateTime.MaxValue, null);
            
            // Take Profit
            Order tpOrder = _tradingAccount.CreateOrder(Instrument, exitAction, OrderType.Limit, OrderEntry.Manual, TimeInForce.Day, quantity, tpPrice, 0, ocoId, "SmartTP", DateTime.MaxValue, null);
            
            _tradingAccount.Submit(new[] { slOrder }); // Fixed CS1503
            _tradingAccount.Submit(new[] { tpOrder }); // Fixed CS1503

            // v1.0.50: Store trade stats for performance tracking
            // Entry price will be updated when ExecutionUpdate fires
            _activeTradeStats[ocoId] = new TradeStats
            {
                EntryPrice = 0, // Will be updated in OnAccountExecutionUpdate
                SlPrice = slPrice,
                TpPrice = tpPrice,
                Quantity = quantity,
                IsLong = isLong,
                EntryTime = DateTime.Now
            };

            // v1.0.50: Draw visual SL and TP lines with risk/reward information
            DrawSlRiskVisualization(slPrice, quantity, isLong, ocoId);
            DrawTpRewardVisualization(tpPrice, quantity, isLong, ocoId);

            if (ShowDebugLogs) Print(string.Format("[TRADING] Submitted Brackets for {0} contracts. OCO: {1}", quantity, ocoId));
        }

        private void DrawSlRiskVisualization(double slPrice, int quantity, bool isLong, string ocoId)
        {
            try
            {
                // Calculate risk in dollars
                double entryEstimate = Close[0];
                double slDistanceInPrice = Math.Abs(entryEstimate - slPrice);
                double pointValue = Instrument.MasterInstrument.PointValue;
                double riskPerContract = slDistanceInPrice * pointValue;
                double totalRisk = riskPerContract * quantity;

                // Create unique tag for this SL line
                string lineTag = "SL_Line_" + ocoId;
                string textTag = "SL_Text_" + ocoId;

                // Draw horizontal ray at SL price extending forward
                Draw.Ray(this, lineTag, true,  // autoScale = true for historical visibility
                    0, slPrice,            // Start: current bar (0 bars ago)
                    1, slPrice,            // Direction: 1 bar forward at same price
                    Brushes.Red, DashStyleHelper.Solid, 2, false); // Ray extends forward infinitely

                // Text position: slightly below the line for longs, above for shorts
                double textOffset = isLong ? -3 * TickSize : 3 * TickSize;
                double textY = slPrice + textOffset;

                // Format text: "2 cntr | Risk: $100"
                string riskText = string.Format("{0} cntr | Risk: ${1:F0}", quantity, totalRisk);

                // Draw text at current bar
                Draw.Text(this, textTag, false, riskText,  // autoScale = false to prevent chart compression
                    0, textY,              // Position: current bar (0 bars ago)
                    0,                      // Vertical offset (0 = at price level)
                    Brushes.Red,            // Text color
                    new SimpleFont("Arial", 10),
                    TextAlignment.Left,
                    Brushes.Transparent,    // Background
                    Brushes.Transparent,    // Border
                    0);                     // Border opacity

                if (ShowDebugLogs)
                    Print(string.Format("[VISUAL] SL line drawn at {0} | {1}", slPrice, riskText));
            }
            catch (Exception ex)
            {
                Print("[VISUAL] ERROR drawing SL visualization: " + ex.Message);
            }
        }

        private void DrawTpRewardVisualization(double tpPrice, int quantity, bool isLong, string ocoId)
        {
            try
            {
                // Calculate reward in dollars
                double entryEstimate = Close[0];
                double tpDistanceInPrice = Math.Abs(entryEstimate - tpPrice);
                double pointValue = Instrument.MasterInstrument.PointValue;
                double rewardPerContract = tpDistanceInPrice * pointValue;
                double totalReward = rewardPerContract * quantity;

                // Create unique tag for this TP line
                string lineTag = "TP_Line_" + ocoId;
                string textTag = "TP_Text_" + ocoId;

                // Draw horizontal ray at TP price extending forward
                Draw.Ray(this, lineTag, true,  // autoScale = true for historical visibility
                    0, tpPrice,            // Start: current bar (0 bars ago)
                    1, tpPrice,            // Direction: 1 bar forward at same price
                    Brushes.Green, DashStyleHelper.Solid, 2, false); // Ray extends forward infinitely

                // Text position: slightly above the line for longs, below for shorts
                double textOffset = isLong ? 3 * TickSize : -3 * TickSize;
                double textY = tpPrice + textOffset;

                // Format text: "2 cntr | Reward: $200"
                string rewardText = string.Format("{0} cntr | Reward: ${1:F0}", quantity, totalReward);

                // Draw text at current bar
                Draw.Text(this, textTag, false, rewardText,  // autoScale = false to prevent chart compression
                    0, textY,              // Position: current bar (0 bars ago)
                    0,                      // Vertical offset (0 = at price level)
                    Brushes.Green,          // Text color
                    new SimpleFont("Arial", 10),
                    TextAlignment.Left,
                    Brushes.Transparent,    // Background
                    Brushes.Transparent,    // Border
                    0);                     // Border opacity

                if (ShowDebugLogs)
                    Print(string.Format("[VISUAL] TP line drawn at {0} | {1}", tpPrice, rewardText));
            }
            catch (Exception ex)
            {
                Print("[VISUAL] ERROR drawing TP visualization: " + ex.Message);
            }
        }
        // Sticky TP State
        private bool _isTpManuallyMoved = false;
        private List<Order> _activeTpOrders = new List<Order>();
        private bool _isUpdatingTpInternally = false; // Flag to prevent false "manual move" detection
        private DateTime _lastStickyTpUpdate = DateTime.MinValue; // Time-based throttle for performance

        private void UpdateStickyTP()
        {
            // Time-based throttle: Only check once per second to prevent chart slowdown
            if ((DateTime.Now - _lastStickyTpUpdate).TotalSeconds < 1.0) return;

            _lastStickyTpUpdate = DateTime.Now; // Update timestamp after passing the time check

            if (_activeTpOrders.Count == 0 || _isTpManuallyMoved) return;

            // Calculate Current Target (Opposite VWAP)
            Order firstTp = _activeTpOrders[0];
            bool isLongPos = (firstTp.OrderAction == OrderAction.Sell); // Closing a Long -> Sell

            double currentTarget = 0;
            if (isLongPos)
            {
                // Long: Target High VWAP (Values[0])
                 if (Values[0].IsValidDataPointAt(CurrentBar)) currentTarget = Values[0][0];
            }
            else
            {
                // Short: Target Low VWAP (Values[1])
                if (Values[1].IsValidDataPointAt(CurrentBar)) currentTarget = Values[1][0];
            }

            if (currentTarget == 0) return;

            double roundedTarget = Instrument.MasterInstrument.RoundToTickSize(currentTarget);

            // Update Orders ONLY if price changed by at least 1 tick
            // Create snapshot to avoid "Collection was modified" errors
            List<Order> orderSnapshot = new List<Order>(_activeTpOrders);

            foreach (var snapOrder in orderSnapshot)
            {
                if (snapOrder.OrderState != OrderState.Working) continue;

                double priceDiff = Math.Abs(snapOrder.LimitPrice - roundedTarget);

                // Only update if changed by at least 1 full tick
                if (priceDiff >= TickSize)
                {
                    // Find the ACTUAL order object in the live list (by OrderId)
                    Order liveOrder = _activeTpOrders.FirstOrDefault(o => o.OrderId == snapOrder.OrderId);
                    if (liveOrder == null) continue;

                    if (ShowDebugLogs)
                    {
                        Print(string.Format("[STICKY_TP] Updating TP: Current={0}, VWAP Target={1}, Diff={2} ticks",
                            liveOrder.LimitPrice, roundedTarget, priceDiff / TickSize));
                    }

                    _isUpdatingTpInternally = true; // Set flag BEFORE updating
                    _lastInternalTpPrice = roundedTarget; // Store new target

                    try
                    {
                        // For Indicators, Account.Change() only has: void Change(Order[] orders)
                        // We must modify the Order object properties first, then call Change()
                        double oldPrice = liveOrder.LimitPrice;
                        liveOrder.LimitPrice = roundedTarget;

                        _tradingAccount.Change(new[] { liveOrder });

                        if (ShowDebugLogs) Print(string.Format("[TRADING] Sticky TP Change submitted: {0} -> {1}",
                            oldPrice, roundedTarget));
                    }
                    catch (Exception ex)
                    {
                        Print("[TRADING] Error changing TP: " + ex.Message);
                        Print("[TRADING] Stack: " + ex.StackTrace);
                    }

                    _isUpdatingTpInternally = false; // Reset flag AFTER
                }
            }
        }

        private double _lastInternalTpPrice = 0;

        private void TrackStickyTP(Order ord)
        {
             // Update Logic helper
             if (ord.Name == "SmartTP")
             {
                 if (ord.OrderState == OrderState.Working || ord.OrderState == OrderState.Accepted)
                 {
                     // Find existing order by OrderId (not by reference!)
                     Order existingOrder = _activeTpOrders.FirstOrDefault(o => o.OrderId == ord.OrderId);

                     if (existingOrder == null)
                     {
                         // New order - add to tracking
                         _activeTpOrders.Add(ord);
                         _lastInternalTpPrice = ord.LimitPrice; // Init tracking
                         if (ShowDebugLogs) Print("[TRADING] TP Order Added to Sticky Tracking: " + ord.LimitPrice);
                     }
                     else
                     {
                         // Update existing order reference (NinjaTrader sends new object instances)
                         int index = _activeTpOrders.IndexOf(existingOrder);

                         // ALWAYS update the reference to latest object from NinjaTrader
                         _activeTpOrders[index] = ord;

                         if (ShowDebugLogs) Print(string.Format("[TRACK_TP] Order update received - LimitPrice={0}, _lastInternalTpPrice={1}, _isUpdatingTpInternally={2}",
                             ord.LimitPrice, _lastInternalTpPrice, _isUpdatingTpInternally));

                         // Detect Manual Change - BUT ignore if we're updating internally
                         double priceDiff = Math.Abs(ord.LimitPrice - _lastInternalTpPrice);

                         // Only mark as manual if:
                         // 1. Price changed by more than 1 tick
                         // 2. We're NOT in the middle of an internal update
                         if (!_isUpdatingTpInternally && priceDiff >= TickSize)
                         {
                             // Price changed externally (user moved it manually)
                             _isTpManuallyMoved = true;
                             if (ShowDebugLogs) Print(string.Format("[TRADING] Manual TP Move Detected! Price changed from {0} to {1}. Sticky Logic Disabled.",
                                 _lastInternalTpPrice, ord.LimitPrice));
                         }
                         else if (_isUpdatingTpInternally)
                         {
                             // Internal update in progress
                             if (Math.Abs(ord.LimitPrice - _lastInternalTpPrice) < TickSize / 2.0)
                             {
                                 // Price matches our target - change confirmed!
                                 if (ShowDebugLogs) Print(string.Format("[TRADING] TP Change CONFIRMED - Order now at {0}", ord.LimitPrice));
                             }
                             else
                             {
                                 if (ShowDebugLogs) Print(string.Format("[TRADING] TP Update in progress - Order event price={0}, target={1}", ord.LimitPrice, _lastInternalTpPrice));
                             }
                         }
                     }
                 }
                 else if (ord.OrderState == OrderState.Filled || ord.OrderState == OrderState.Cancelled || ord.OrderState == OrderState.Rejected)
                 {
                     // Remove by OrderId, not by reference
                     Order toRemove = _activeTpOrders.FirstOrDefault(o => o.OrderId == ord.OrderId);
                     if (toRemove != null)
                     {
                         if (ShowDebugLogs) Print(string.Format("[TRADING] TP Order {0}: {1}", ord.OrderState, ord.LimitPrice));
                         _activeTpOrders.Remove(toRemove);
                     }

                     if (_activeTpOrders.Count == 0)
                     {
                         _isTpManuallyMoved = false; // Reset flag when flat
                         _lastInternalTpPrice = 0;
                         if (ShowDebugLogs) Print("[TRADING] All TP orders closed. Sticky logic reset.");
                     }
                 }
             }
        }

        // ===========================================
        // v3.1.0: AUTO-TRADE — Touch Study Config Execution
        // ===========================================

        /// <summary>
        /// Submits a market entry with SL/TP bracket based on Touch Study config detection.
        /// Called from UpdateTouchStudyTracking when an episode-first touch is detected
        /// and the corresponding trade config checkbox is enabled.
        /// </summary>
        internal void SubmitAutoTrade(string config, double entryPrice, bool isShort)
        {
            if (_tradingAccount == null)
            {
                Print("[AUTO-TRADE] ERROR: No hay cuenta conectada. Selecciona una cuenta en propiedades.");
                return;
            }

            if (_autoTradeOpen)
            {
                if (ShowDebugLogs) Print(string.Format("[AUTO-TRADE] BLOQUEADO: Ya hay trade abierto (Config {0})", _autoTradeConfig));
                return;
            }

            // Check flat
            if (OnlyEnterWhenFlat)
            {
                Position position = _tradingAccount.Positions.FirstOrDefault(p => p.Instrument == Instrument);
                if (position != null && position.MarketPosition != MarketPosition.Flat)
                {
                    if (ShowDebugLogs) Print("[AUTO-TRADE] BLOQUEADO: Cuenta tiene posición abierta");
                    return;
                }

                bool hasPending = _tradingAccount.Orders.Any(o =>
                    o.Instrument == Instrument &&
                    (o.Name.StartsWith("AutoCfg_") || o.Name == "SmartEntry") &&
                    (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted));
                if (hasPending)
                {
                    if (ShowDebugLogs) Print("[AUTO-TRADE] BLOQUEADO: Orden pendiente para este instrumento");
                    return;
                }
            }

            // Calculate SL/TP from Touch Study parameters
            double slPrice, tpPrice;
            if (isShort)
            {
                slPrice = entryPrice + TouchStudySLTicks * TickSize;
                tpPrice = entryPrice - TouchStudyTPTicks * TickSize;
            }
            else
            {
                slPrice = entryPrice - TouchStudySLTicks * TickSize;
                tpPrice = entryPrice + TouchStudyTPTicks * TickSize;
            }

            slPrice = Instrument.MasterInstrument.RoundToTickSize(slPrice);
            tpPrice = Instrument.MasterInstrument.RoundToTickSize(tpPrice);

            if (tpPrice <= 0 || slPrice <= 0)
            {
                Print("[AUTO-TRADE] ERROR: Precios inválidos SL=" + slPrice + " TP=" + tpPrice);
                return;
            }

            // Position sizing
            int qty = CalculatePositionSize(entryPrice, slPrice);
            if (qty <= 0)
            {
                Print("[AUTO-TRADE] ERROR: Cantidad calculada = 0");
                return;
            }

            OrderAction action = isShort ? OrderAction.Sell : OrderAction.Buy;
            string entryName = "AutoCfg_" + config;
            _autoTradeOcoId = Guid.NewGuid().ToString("N");

            try
            {
                if (ShowDebugLogs) Print(string.Format("[AUTO-TRADE] >>> ENTRADA Config {0}: {1} {2} @ Market | SL={3} TP={4}",
                    config, action, qty, slPrice, tpPrice));

                // Submit market entry
                Order entryOrder = _tradingAccount.CreateOrder(
                    Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                    qty, 0, 0, "", entryName, DateTime.MaxValue, null);
                _tradingAccount.Submit(new[] { entryOrder });

                // Submit bracket immediately (SL + TP with OCO)
                OrderAction exitAction = isShort ? OrderAction.Buy : OrderAction.Sell;

                Order slOrder = _tradingAccount.CreateOrder(
                    Instrument, exitAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Day,
                    qty, 0, slPrice, _autoTradeOcoId, "AutoSL_" + config, DateTime.MaxValue, null);

                Order tpOrder = _tradingAccount.CreateOrder(
                    Instrument, exitAction, OrderType.Limit, OrderEntry.Manual, TimeInForce.Day,
                    qty, tpPrice, 0, _autoTradeOcoId, "AutoTP_" + config, DateTime.MaxValue, null);

                _tradingAccount.Submit(new[] { slOrder });
                _tradingAccount.Submit(new[] { tpOrder });

                // Mark trade as open
                _autoTradeOpen = true;
                _autoTradeConfig = config;

                Print(string.Format("[AUTO-TRADE] Bracket OCO enviado: SL={0} TP={1} OCO={2}", slPrice, tpPrice, _autoTradeOcoId));
            }
            catch (Exception ex)
            {
                Print("[AUTO-TRADE] EXECUTION ERROR: " + ex.Message);
                _autoTradeOpen = false;
                _autoTradeConfig = "";
            }
        }
	}
}
