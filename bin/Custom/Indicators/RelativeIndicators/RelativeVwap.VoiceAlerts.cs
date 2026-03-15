#region Using declarations
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class RelativeVwap
    {
        #region Voice Alert System

        private DateTime _lastVoiceAlertTime = DateTime.MinValue;
        private const double MinSecondsBetweenAlerts = 3.0; // Minimum 3 seconds between alerts to avoid spam

        private void InitializeVoiceAlerts()
        {
            // No initialization needed for PowerShell TTS approach
            if (ShowDebugLogs) Print("[VOICE] Voice alerts initialized (using PowerShell TTS)");
        }

        private void DisposeVoiceAlerts()
        {
            // No cleanup needed
        }

        /// <summary>
        /// Speaks the entry signal alert (yellow candle)
        /// </summary>
        private void SpeakEntrySignal(bool isLong, int sequenceNumber)
        {
            // Throttle alerts to prevent spam
            if ((DateTime.Now - _lastVoiceAlertTime).TotalSeconds < MinSecondsBetweenAlerts)
                return;

            _lastVoiceAlertTime = DateTime.Now;

            try
            {
                // Build message: "MNQ Entry 1 Detected"
                string instrument = Instrument.MasterInstrument.Name;
                string direction = isLong ? "Long" : "Short";
                string message = string.Format("{0} Entry {1} Detected {2}", instrument, sequenceNumber, direction);

                if (ShowDebugLogs) Print("[VOICE ALERT] " + message);

                // Speak asynchronously using PowerShell to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        // Use PowerShell to invoke Windows TTS
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = string.Format("-Command \"Add-Type -AssemblyName System.Speech; $speak = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speak.Speak('{0}')\"", message),
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        using (Process process = Process.Start(psi))
                        {
                            // Don't wait for completion - fire and forget
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("Error speaking alert: " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Print("Error in SpeakEntrySignal: " + ex.Message);
            }
        }

        /// <summary>
        /// Speaks the level touch alert using PowerShell and Windows TTS
        /// </summary>
        private void SpeakLevelTouch(string sessionName, bool isHigh, int daysOld)
        {
            // Throttle alerts to prevent spam
            if ((DateTime.Now - _lastVoiceAlertTime).TotalSeconds < MinSecondsBetweenAlerts)
                return;

            _lastVoiceAlertTime = DateTime.Now;

            try
            {
                // Build message: "MNQ Asia High day 2"
                string instrument = Instrument.MasterInstrument.Name;
                string levelType = isHigh ? "High" : "Low";
                string daysText = daysOld == 0 ? "today" : (daysOld == 1 ? "day 1" : "day " + daysOld);

                string message = string.Format("{0} {1} {2} {3}", instrument, sessionName, levelType, daysText);

                if (ShowDebugLogs) Print("[VOICE ALERT] " + message);

                // Speak asynchronously using PowerShell to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        // Use PowerShell to invoke Windows TTS
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = string.Format("-Command \"Add-Type -AssemblyName System.Speech; $speak = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speak.Speak('{0}')\"", message),
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        using (Process process = Process.Start(psi))
                        {
                            // Don't wait for completion - fire and forget
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("Error speaking alert: " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Print("Error in SpeakLevelTouch: " + ex.Message);
            }
        }

        #endregion
    }
}
