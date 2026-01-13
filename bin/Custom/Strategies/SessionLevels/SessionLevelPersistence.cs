using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Helper class to persist session levels to file.
    /// Solves the issue of levels being recalculated incorrectly on strategy restart.
    /// </summary>
    public class SessionLevelPersistence
    {
        private SessionLevelsStrategy strategy;
        private string cachePath;

        public SessionLevelPersistence(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
            string userDataDir = NinjaTrader.Core.Globals.UserDataDir.TrimEnd(Path.DirectorySeparatorChar);
            // Path: Documents/NinjaTrader 8/bin/Custom/Strategies/SessionLevels/Cache
            cachePath = Path.Combine(userDataDir, "bin", "Custom", "Strategies", "SessionLevels", "Cache");
            
            if (!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
            }
        }

        private string GetFilePath(string instrumentName)
        {
            // File: levels_Instrument_YYYYMMDD.xml
            string safeName = instrumentName.Replace("/", "-").Replace(" ", "_");
            string dateStr = strategy.Time[0].Date.ToString("yyyyMMdd"); 
            // Note: Uses strategy time. In real-time this is Today. In backtest this is current bar date.
            // CAUTION: If strategy restarts mid-day, Time[0] is correct.
            return Path.Combine(cachePath, $"levels_{safeName}_{dateStr}.xml");
        }

        public void SaveLevels(List<SessionLevel> levels)
        {
            try
            {
                if (levels == null || levels.Count == 0) return;

                string filePath = GetFilePath(strategy.Instrument.FullName);
                XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevel>));
                
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    serializer.Serialize(writer, levels);
                }
                
                // strategy.Log($"[PERSISTENCE] Saved {levels.Count} levels to {filePath}");
            }
            catch (Exception ex)
            {
                strategy.Log($"[PERSISTENCE ERROR] Save failed: {ex.Message}");
            }
        }

        public List<SessionLevel> LoadLevels()
        {
            try
            {
                string filePath = GetFilePath(strategy.Instrument.FullName);
                if (!File.Exists(filePath)) 
                {
                    strategy.Log($"[PERSISTENCE] No cache file found for today ({Path.GetFileName(filePath)})");
                    return null;
                }

                XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevel>));
                List<SessionLevel> loadedLevels;

                using (StreamReader reader = new StreamReader(filePath))
                {
                    loadedLevels = (List<SessionLevel>)serializer.Deserialize(reader);
                }

                if (loadedLevels != null)
                {
                    // RESTORE COLORS & NON-SERIALIZED FIELDS
                    foreach (var lvl in loadedLevels)
                    {
                        if (lvl.Name.Contains("Asia")) lvl.Color = Brushes.White;
                        else if (lvl.Name.Contains("Europe")) lvl.Color = Brushes.Yellow;
                        else if (lvl.Name.Contains("USA")) lvl.Color = Brushes.RoyalBlue;
                        else lvl.Color = Brushes.Gray;
                    }
                }
                
                strategy.Log($"[PERSISTENCE] Successfully LOADED {loadedLevels?.Count ?? 0} levels from disk.");
                return loadedLevels;
            }
            catch (Exception ex)
            {
                strategy.Log($"[PERSISTENCE ERROR] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}
