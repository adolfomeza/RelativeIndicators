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

namespace NinjaTrader.NinjaScript.Strategies
{
    // v1.12.0: Trading Mode Control
    public enum TradingMode { Normal, Paused, LongOnly, ShortOnly }

    public enum VwapCalculationMode
    {
        Typical, // (H+L+C)/3
        Close,   // Close
        OHLC4    // (O+H+L+C)/4
    }

    public enum EntryState { Idle, WaitingForConfirmation, WaitingForVwapMitigation, workingOrder, PositionActive }

    // v1.14.73: Entry Mode Selection
    public enum EntryMode
    {
        APlusRetrace,  // Original A+ method: Wait for VWAP pullback
        Anticipado     // Anticipated: Enter immediately on confirmation candle close
    }

    public enum AnticipatedOrderType
    {
        Market,  // Market order
        Limit    // Limit order at close price
    }

    // Level Persistence
    public class SessionLevel
    {
        public string Name;
        public double Price;
        public DateTime StartTime;
        public DateTime EndTime;
        public TimeSpan ActualSessionEnd; // v1.14.49: To validate if session is closed for same-day trading
        public DateTime MitigationTime; // When it was touched
        public bool IsResistance; // True = High, False = Low
        public bool IsMitigated;
        [XmlIgnore]
        public Brush Color;
        public string Tag; // For Drawing
        
        // VWAP Data
        public double VolSum;
        public double PvSum;
        public bool JustReset;
        
        // v1.10.25: Retry tracking
        public int EntryAttempts = 0;
    }

    // GLOBAL ETH SESSION VWAP LOGIC
    public class SessionVWAP
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

    public class SessionLevelData
    {
        public string Name;
        public double Price;
        public DateTime StartTime;
        public DateTime EndTime;
        public DateTime MitigationTime;
        public bool IsResistance;
        public bool IsMitigated;
        public double VolSum;
        public double PvSum;
        public string Tag;
        // Color is not serialized easily, we infer it from Name or defaults.
    }
}
