using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class StrategyHelpers
    {
        private SessionLevelsStrategy strategy;
        
        // UI Components
        private System.Windows.Controls.StackPanel buttonPanel;
        private System.Windows.Controls.Button btnPause;
        private System.Windows.Controls.Button btnClose;
        private bool buttonsInitialized = false;
        
        // Log State
        private string logFilePath;
        private object logFileLock = new object();

        public StrategyHelpers(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        // =========================================================
        // LOGGING
        // =========================================================
        public void Log(string message)
        {
            if (!strategy.EnableDebugLogs) return;
            
            string instrumentName = strategy.Instrument != null ? strategy.Instrument.MasterInstrument.Name : "UNKNOWN";
            string prefix = "[" + instrumentName + "] ";
            string fullMessage = prefix + message;
            
            // Print to Output window
            strategy.Print(fullMessage);
            
            // Write to file (buffered, low overhead)
            try
            {
                // Only calculate path once per instance
                if (logFilePath == null)
                {
                    // Use NinjaTrader's trace folder (always exists)
                    string ntDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string logsDir = System.IO.Path.Combine(ntDocsPath, "NinjaTrader 8", "trace", "SessionLevels");
                    if (!System.IO.Directory.Exists(logsDir))
                        System.IO.Directory.CreateDirectory(logsDir);
                    
                    // One file per instrument per day
                    string fileName = string.Format("{0}_{1:yyyyMMdd}.txt", instrumentName, DateTime.Now);
                    logFilePath = System.IO.Path.Combine(logsDir, fileName);
                }
                
                lock (logFileLock)
                {
                    System.IO.File.AppendAllText(logFilePath, 
                        string.Format("{0:HH:mm:ss.fff} {1}\r\n", DateTime.Now, message));
                }
            }
            catch { } // Silently ignore file errors
        }

        public void ClearLogFile()
        {
            try
            {
                if (strategy.Instrument == null) return;
                
                string instrumentName = strategy.Instrument.MasterInstrument.Name;
                string ntDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string logsDir = System.IO.Path.Combine(ntDocsPath, "NinjaTrader 8", "trace", "SessionLevels");
                
                if (!System.IO.Directory.Exists(logsDir))
                    System.IO.Directory.CreateDirectory(logsDir);
                
                string fileName = string.Format("{0}_{1:yyyyMMdd}.txt", instrumentName, DateTime.Now);
                logFilePath = System.IO.Path.Combine(logsDir, fileName);
                
                lock (logFileLock)
                {
                    System.IO.File.WriteAllText(logFilePath, 
                        string.Format("=== {0} Strategy Log - Started {1:yyyy-MM-dd HH:mm:ss} ===\r\n\r\n", 
                            instrumentName, DateTime.Now));
                }
            }
            catch { }
        }

        // =========================================================
        // UI: STATE PANEL
        // =========================================================
        public void DrawStatePanel()
        {
            double accountPnL = 0;
            double sessionPnL = 0;

            try {
                if (strategy.Account != null)
                    accountPnL = strategy.Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);

                if (strategy.SystemPerformance != null && strategy.SystemPerformance.RealTimeTrades != null)
                    sessionPnL = strategy.SystemPerformance.RealTimeTrades.TradesPerformance.Currency.CumProfit;
            } catch {}

            // Local vs Global Risk
            double localRiskDisplay = strategy.RiskPerTradeUSD;
            if (strategy.atr != null && strategy.atr[0] > 0)
            {
                double atrInUSD = strategy.atr[0] * strategy.Instrument.MasterInstrument.PointValue;
                double scaledRisk = atrInUSD * strategy.ATRRiskScaleFactor;
                localRiskDisplay = Math.Min(strategy.RiskPerTradeUSD, scaledRisk);
                if (localRiskDisplay < 5.0) localRiskDisplay = 5.0;
                
                strategy.WriteSharedRisk(localRiskDisplay); // Using public wrapper or access? WriteSharedRisk is public line 841
            }
            double globalRiskDisplay = strategy.ReadMaxSharedRisk(); // Public line 880
            
            // Minimum Risk Calculation
            double minTickValue = strategy.Instrument.MasterInstrument.PointValue * strategy.TickSize;
            double minRiskUSD = strategy.StopLossTicks * strategy.MinQuantity * minTickValue;

            // Level Info
            string levelInfo = "-";
            if (!string.IsNullOrEmpty(strategy.setupLevelName) && strategy.setupLevelTime != DateTime.MinValue)
            {
                int daysOld = (int)(strategy.Time[0].Date - strategy.setupLevelTime.Date).TotalDays;
                if (daysOld == 0) levelInfo = strategy.setupLevelName + " (Today)";
                else if (daysOld == 1) levelInfo = strategy.setupLevelName + " (1 Day)";
                else levelInfo = strategy.setupLevelName + " (" + daysOld + " Days)";
                
                // Retries Counter
                if (strategy.MaxRetriesPerLevel > 1)
                    levelInfo += " " + strategy.currentVwapNumber + "/" + strategy.MaxRetriesPerLevel;
            }

            // Order Info
            string orderInfo = "";
            bool hasActiveOrders = (strategy.currentEntryState == EntryState.workingOrder || strategy.currentEntryState == EntryState.PositionActive);
            
            if (hasActiveOrders)
            {
                double tickValue = strategy.Instrument.MasterInstrument.PointValue * strategy.TickSize;
                double avgEntry = 0;
                double slPrice = 0;
                
                double tp1Price = strategy.tradeOriginalTp1Price > 0 ? strategy.tradeOriginalTp1Price : strategy.activeTp1Price;
                double tp2Price = strategy.tradeOriginalTp2Price > 0 ? strategy.tradeOriginalTp2Price : strategy.activeTp2Price;
                int totalQty = 0;
                
                if (strategy.entryOrder != null && strategy.entryOrder.AverageFillPrice > 0)
                    avgEntry = strategy.entryOrder.AverageFillPrice;
                else if (strategy.entryOrder != null && strategy.entryOrder.LimitPrice > 0)
                    avgEntry = strategy.entryOrder.LimitPrice;
                else if (strategy.Position.MarketPosition != MarketPosition.Flat)
                    avgEntry = strategy.Position.AveragePrice;
                
                if (strategy.tradeOriginalQty > 0)
                    totalQty = strategy.tradeOriginalQty;
                else if (strategy.Position.MarketPosition != MarketPosition.Flat)
                    totalQty = Math.Abs(strategy.Position.Quantity);
                else if (strategy.entryOrder != null)
                    totalQty = strategy.entryOrder.Quantity;
                
                slPrice = strategy.isShortSetup ? (strategy.setupAnchorPrice + strategy.TickSize) : (strategy.setupAnchorPrice - strategy.TickSize);
                
                if (avgEntry > 0 && slPrice > 0 && totalQty > 0)
                {
                    double riskTicks = Math.Abs(avgEntry - slPrice) / strategy.TickSize;
                    double riskUSD = riskTicks * tickValue * totalQty;
                    
                    double tp1RewardTicks = 0;
                    double tp1RewardUSD = 0;
                    double tp1RR = 0;
                    if (tp1Price > 0)
                    {
                        tp1RewardTicks = Math.Abs(tp1Price - avgEntry) / strategy.TickSize;
                        tp1RewardUSD = tp1RewardTicks * tickValue * ((totalQty + 1) / 2);
                        tp1RR = riskTicks > 0 ? tp1RewardTicks / riskTicks : 0;
                    }
                    
                    double tp2RewardTicks = 0;
                    double tp2RewardUSD = 0;
                    double tp2RR = 0;
                    if (tp2Price > 0)
                    {
                        tp2RewardTicks = Math.Abs(tp2Price - avgEntry) / strategy.TickSize;
                        tp2RewardUSD = tp2RewardTicks * tickValue * (totalQty / 2);
                        tp2RR = riskTicks > 0 ? tp2RewardTicks / riskTicks : 0;
                    }
                    
                    orderInfo = string.Format("\n─────────────────\nSL: -${0:F0} ({1:F0}t)\nTP1: +${2:F0} R={3:F1}\nTP2: +${4:F0} R={5:F1}",
                        riskUSD, riskTicks, tp1RewardUSD, tp1RR, tp2RewardUSD, tp2RR);
                }
            }

            string stateDisplay = strategy.currentEntryState.ToString();
    
            if (!string.IsNullOrEmpty(strategy.lastFilterReason) && (DateTime.Now - strategy.lastFilterTime).TotalSeconds < 120) 
            {
                stateDisplay += "\n(" + strategy.lastFilterReason + ")";
            }
            
            // v1.14.54: Show retry counter when applicable
            string retryInfo = "";
            if (strategy.currentVwapNumber > 1 || strategy.waitingForVwapMitigation)
            {
                retryInfo = string.Format(" (Intento {0}/{1})", strategy.currentVwapNumber, strategy.MaxRetriesPerLevel);
            }

            string text = string.Format("Ver: {0}\nState: {1}\nLevel: {2}{3}\nPosition: {4}\nPnL: {5} | Risk: {6:C0} (Min: {7:C0}){8}",
                "v1.14.61", // Hardcoded version updated
                stateDisplay,
                levelInfo,
                retryInfo,
                strategy.Position.MarketPosition,
                sessionPnL.ToString("C"),
                globalRiskDisplay,
                minRiskUSD,
                orderInfo);
                
            Draw.TextFixed(strategy, "InfoPanel", text, TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Black, 50);
            
            if (strategy.gapDetected || strategy.gapCount > 0)
            {
                string msg = "GAP DETECTED";
                if (strategy.gapCount > 0) msg = "ALERTA: FALTAN DIAS\n" + strategy.gapCount + " NIVELES OCULTOS\nCARGA MAS HISTORIAL";
                Draw.TextFixed(strategy, "GapWarning", "\n\n\n\n\n\n\n\n\n\n\n\n" + msg, TextPosition.TopRight, Brushes.Red, new SimpleFont("Arial", 12) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 100);
            }
            
            if (strategy.isLagAlertActive)
            {
                string lagMsg = string.Format("⚠️ LAG: {0:F1}s - ORDERS BLOCKED", strategy.currentChartLag);
                Draw.TextFixed(strategy, "LagAlert", "\n\n\n\n\n\n\n" + lagMsg, TextPosition.TopRight, Brushes.Yellow, new SimpleFont("Arial", 14) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 100);
            }
            else
            {
                strategy.RemoveDrawObject("LagAlert");
            }
        }

        // =========================================================
        // UI: CONTROL BUTTONS
        // =========================================================
        public void InitializeControlButtons()
        {
            if (buttonsInitialized || strategy.ChartControl == null) return;
            
            strategy.ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    buttonPanel = new System.Windows.Controls.StackPanel();
                    buttonPanel.Orientation = Orientation.Horizontal;
                    buttonPanel.HorizontalAlignment = HorizontalAlignment.Right;
                    buttonPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    buttonPanel.Margin = new Thickness(0, 0, 10, 10);
                    
                    btnPause = CreateControlButton("↕ AMBOS", Brushes.ForestGreen);
                    btnClose = CreateControlButton("✖ CLOSE", Brushes.Crimson);
                    
                    btnPause.Click += OnDirectionClick;
                    btnClose.Click += OnCloseClick;
                    
                    buttonPanel.Children.Add(btnPause);
                    buttonPanel.Children.Add(btnClose);
                    
                    strategy.UserControlCollection.Add(buttonPanel);
                    buttonsInitialized = true;
                    Log(strategy.Time[0] + " CONTROL BUTTONS: Initialized (Bottom Right)");
                }
                catch (Exception ex)
                {
                    Log(strategy.Time[0] + " CONTROL BUTTONS ERROR: " + ex.Message);
                }
            });
        }
        
        private System.Windows.Controls.Button CreateControlButton(string text, Brush bgColor)
        {
            var btn = new System.Windows.Controls.Button();
            btn.Content = text;
            btn.Width = 85;
            btn.Height = 24;
            btn.Margin = new Thickness(3);
            btn.Background = bgColor;
            btn.Foreground = Brushes.White;
            btn.FontWeight = FontWeights.Bold;
            btn.FontSize = 11;
            btn.BorderThickness = new Thickness(0);
            return btn;
        }
        
        public void OnDirectionClick(object sender, RoutedEventArgs e)
        {
            switch (strategy.currentTradingMode)
            {
                case TradingMode.Normal:
                    strategy.currentTradingMode = TradingMode.LongOnly;
                    break;
                case TradingMode.LongOnly:
                    strategy.currentTradingMode = TradingMode.ShortOnly;
                    break;
                case TradingMode.ShortOnly:
                    strategy.currentTradingMode = TradingMode.Paused;
                    break;
                case TradingMode.Paused:
                    strategy.currentTradingMode = TradingMode.Normal;
                    break;
            }
            Log(strategy.Time[0] + " CONTROL: Mode = " + strategy.currentTradingMode);
            UpdateButtonStates();
        }
        
        public void OnCloseClick(object sender, RoutedEventArgs e)
        {
            ClosePositionManual();
        }
        
        public void ClosePositionManual()
        {
            if (strategy.Position.MarketPosition == MarketPosition.Flat)
            {
                Log(strategy.Time[0] + " MANUAL CLOSE: No position to close");
                return;
            }
            
            int qty = Math.Abs(strategy.Position.Quantity);
            
            try
            {
                // Cancel existing orders
                if (strategy.stopOrder != null && (strategy.stopOrder.OrderState == OrderState.Working || strategy.stopOrder.OrderState == OrderState.Accepted))
                {
                    strategy.CancelOrderWrapper(strategy.stopOrder);
                    Log(strategy.Time[0] + " MANUAL CLOSE: Cancelled SL");
                }
                if (strategy.tp1Order != null && (strategy.tp1Order.OrderState == OrderState.Working || strategy.tp1Order.OrderState == OrderState.Accepted))
                {
                    strategy.CancelOrderWrapper(strategy.tp1Order);
                    Log(strategy.Time[0] + " MANUAL CLOSE: Cancelled TP1");
                }
                if (strategy.tp2Order != null && (strategy.tp2Order.OrderState == OrderState.Working || strategy.tp2Order.OrderState == OrderState.Accepted))
                {
                    strategy.CancelOrderWrapper(strategy.tp2Order);
                    Log(strategy.Time[0] + " MANUAL CLOSE: Cancelled TP2");
                }
                
                // Close position via Wrapper (since SubmitOrderUnmanaged is protected)
                if (strategy.Position.MarketPosition == MarketPosition.Long)
                    strategy.SubmitOrderUnmanagedWrapper(0, OrderAction.Sell, OrderType.Market, qty, 0, 0, "", "ManualClose_Long");
                else
                    strategy.SubmitOrderUnmanagedWrapper(0, OrderAction.BuyToCover, OrderType.Market, qty, 0, 0, "", "ManualClose_Short");
                
                Log(strategy.Time[0] + " MANUAL CLOSE SUBMITTED: Qty=" + qty);
                strategy.currentEntryState = EntryState.Idle;
                strategy.setupLevelName = "";
            }
            catch (Exception ex)
            {
                Log(strategy.Time[0] + " MANUAL CLOSE FAILED: " + ex.Message);
            }
        }
        
        public void UpdateButtonStates()
        {
            strategy.ChartControl?.Dispatcher.InvokeAsync(() =>
            {
                if (btnPause == null) return;
                
                switch (strategy.currentTradingMode)
                {
                    case TradingMode.Normal:
                        btnPause.Content = "↕ AMBOS";
                        btnPause.Background = Brushes.ForestGreen;
                        break;
                    case TradingMode.LongOnly:
                        btnPause.Content = "↑ LONG";
                        btnPause.Background = Brushes.DodgerBlue;
                        break;
                    case TradingMode.ShortOnly:
                        btnPause.Content = "↓ SHORT";
                        btnPause.Background = Brushes.OrangeRed;
                        break;
                    case TradingMode.Paused:
                        btnPause.Content = "⏸ NINGUNO";
                        btnPause.Background = Brushes.Gray;
                        break;
                }
            });
        }
        
        public void CleanupControlButtons()
        {
            if (strategy.ChartControl == null) return;
            
            strategy.ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (btnPause != null) btnPause.Click -= OnDirectionClick;
                    if (btnClose != null) btnClose.Click -= OnCloseClick;
                    
                    if (buttonPanel != null && strategy.UserControlCollection.Contains(buttonPanel))
                        strategy.UserControlCollection.Remove(buttonPanel);
                }
                catch { }
            });
        }
    }
}
