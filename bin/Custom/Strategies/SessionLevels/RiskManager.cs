using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum RiskModelType
    {
        Standard,
        Apteros
    }

    public enum ApterosRiskBasis
    {
        PercentageOfBalance, // Default: Balance * %
        DrawdownAllocation   // New: Drawdown / Days
    }

    public class RiskManager
    {
        private SessionLevelsStrategy strategy;
        
        // Apteros State
        private const string APTEROS_STATE_FILE = "ApterosState.txt";
        private string sharedStatePath;
        private DateTime lastSyncTime;
        private double startOfDayBalance = 0;
        private bool isDailyLockout = false;
        
        public RiskManager(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
            this.sharedStatePath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Strategies", "Data", APTEROS_STATE_FILE);
        }

        public void InitializeState()
        {
            // Initial read/write logic for Start of Day Balance
            SyncState();
        }

        public void SyncState()
        {
            try
            {
                // Ensure directory exists
                string dir = Path.GetDirectoryName(sharedStatePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Default state
                double fileBalance = 0;
                long fileDateTicks = 0;
                bool fileLockout = false;

                if (File.Exists(sharedStatePath))
                {
                    string content = File.ReadAllText(sharedStatePath);
                    // Format: Balance|Ticks|Lockout
                    var parts = content.Split('|');
                    if (parts.Length >= 3)
                    {
                        double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fileBalance);
                        long.TryParse(parts[1], out fileDateTicks);
                        bool.TryParse(parts[2], out fileLockout);
                    }
                }

                // Check if file is from TODAY
                DateTime fileDate = new DateTime(fileDateTicks);
                bool isToday = (fileDate.Date == DateTime.Now.Date);

                if (!isToday)
                {
                    // NEW DAY: Reset everything
                    // The first strategy to run this code initiates the day
                    startOfDayBalance = strategy.Account.Get(AccountItem.CashValue, Currency.UsDollar);
                    isDailyLockout = false;
                    
                    // Write new state
                    WriteState(startOfDayBalance, DateTime.Now.Ticks, false);
                    strategy.Print("Apteros Risk: New Day Initialized. Balance: " + startOfDayBalance);
                }
                else
                {
                    // SAME DAY: Sync with file
                    startOfDayBalance = fileBalance;
                    isDailyLockout = fileLockout;
                }
            }
            catch (Exception ex)
            {
                strategy.Print("Apteros Risk Sync Error: " + ex.Message);
            }
        }

        private void WriteState(double balance, long ticks, bool lockout)
        {
            try
            {
                 string content = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}|{1}|{2}", balance, ticks, lockout);
                 File.WriteAllText(sharedStatePath, content);
            }
            catch {}
        }

        public double GetEffectiveRiskPerTrade(
            RiskModelType model, 
            double standardRisk, 
            double accountBalance, 
            double apterosDailyPct, 
            int apterosOpportunities,
            ApterosRiskBasis riskBasis,
            double maxDrawdown,
            int allocationDays)
        {
            if (model == RiskModelType.Apteros)
            {
                double dailyLimit = 0;
                
                if (riskBasis == ApterosRiskBasis.DrawdownAllocation)
                {
                    // Logic: (MaxDrawdown / Days)
                    // Example: $5000 / 20 = $250 Daily Limit
                    dailyLimit = maxDrawdown / (double)allocationDays;
                }
                else
                {
                   // Logic: (StartOfDayBalance * Pct) / Opportunities
                   // Note: Using StartOfDayBalance (Static to the day) is safer than fluctuating current balance
                   if (startOfDayBalance <= 0) SyncState(); // Ensure we have a valid start balance
                
                   double baseBalance = startOfDayBalance > 0 ? startOfDayBalance : accountBalance;
                   dailyLimit = baseBalance * (apterosDailyPct / 100.0);
                }

                // Final Risk = Daily Limit / Opportunities
                double risk = dailyLimit / (double)apterosOpportunities;
                
                return risk;
            }
            
            // Default Standard
            return standardRisk;
        }

        public bool CheckRiskState(RiskModelType model, double currentAccountValue, double dailyLossLimitPct, double maxTrailingDrawdown)
        {
            if (model != RiskModelType.Apteros) return true;

            // 1. Sync first to see if anyone else locked us out
            SyncState();
            
            if (isDailyLockout) return false;

            // 2. Check Daily Loss (Soft Breach)
            double dailyLimitAmount = startOfDayBalance * (dailyLossLimitPct / 100.0);
            double currentLoss = startOfDayBalance - currentAccountValue;
            
            if (currentLoss >= dailyLimitAmount)
            {
                // Soft Breach Warning/Action
                strategy.Print(string.Format("APTEROS DAILY LIMIT HIT! Loss=${0:F2} > Limit=${1:F2}", currentLoss, dailyLimitAmount));
                
                // Set Lockout
                isDailyLockout = true;
                WriteState(startOfDayBalance, DateTime.Now.Ticks, true);
                
                return false;
            }

            // 3. Check Trailing Drawdown (Hard Breach) -> To be implemented fully with persistent HighWaterMark
            // For now, simple check against start balance ? No, Apteros is Trailing from High Water Mark.
            // We need to persist HighWaterMark too.
            // ...
            
            return true;
        }
    }
}
