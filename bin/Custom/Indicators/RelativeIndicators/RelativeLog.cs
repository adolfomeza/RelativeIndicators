#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaTrader.NinjaScript;
#endregion

// RelativeLog — captura estructurada de logs de indicadores RelativeIndicators.
//
// NT8 no expone API pública para interceptar Print() de terceros. En lugar de
// romper el Output Window, este módulo provee un método de logging paralelo
// que los indicadores de la suite usan voluntariamente. Cada log se guarda en
// un buffer circular accesible vía HTTP (AddOn RelativeObserver, endpoint
// /print-output) Y también se envía al Output Window con Output.Process para
// preservar el flujo habitual.
//
// Uso desde un indicador:
//
//   using NinjaTrader.NinjaScript.AddOns;
//   // en lugar de: Print("delta = " + delta);
//   this.RLog("delta = {0}", delta);
//   this.RLog(RelativeLogLevel.Warning, "señal débil: score={0}", score);
//
// El RLog extension method captura automáticamente:
//   - Timestamp UTC
//   - Nombre del indicador (Name)
//   - Instrument.FullName
//   - BarsPeriod resumido (ej: "1Minute")
//   - Bar time (Time[0]) si está disponible
//   - CurrentBar
//   - Nivel (Info/Warn/Error)
//   - Mensaje formateado
//
// Thread-safe. Buffer circular fijo (configurable via BufferSize). Los logs
// viejos se sobrescriben automáticamente.

namespace NinjaTrader.NinjaScript.AddOns
{
    public enum RelativeLogLevel { Info = 1, Warning = 2, Error = 3 }

    public class RelativeLogEntry
    {
        public DateTime Timestamp;        // UTC
        public RelativeLogLevel Level;
        public string Indicator;          // Name
        public string Instrument;         // FullName
        public string Period;             // "1Minute", "5Minute", etc.
        public DateTime BarTime;          // Time[0] si aplica
        public int CurrentBar;
        public string Message;
    }

    public static class RelativeLog
    {
        #region Configuration

        public const int BufferSize = 2000;

        // Si true, cada entrada también va al Output Window via Output.Process
        public static bool MirrorToOutputWindow = true;

        #endregion

        #region Ring buffer

        private static readonly object _lock = new object();
        private static readonly RelativeLogEntry[] _ring = new RelativeLogEntry[BufferSize];
        private static int _head;
        private static int _filled;
        private static long _totalCount;

        public static long TotalCount { get { lock (_lock) return _totalCount; } }

        public static void Append(RelativeLogEntry entry)
        {
            if (entry == null) return;
            lock (_lock)
            {
                _ring[_head] = entry;
                _head = (_head + 1) % _ring.Length;
                if (_filled < _ring.Length) _filled++;
                _totalCount++;
            }

            if (MirrorToOutputWindow)
            {
                try
                {
                    string tag = entry.Level == RelativeLogLevel.Info ? "I"
                              : entry.Level == RelativeLogLevel.Warning ? "W" : "E";
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "[{0} {1} {2}/{3}] {4}",
                        tag, entry.Indicator, entry.Instrument, entry.Period, entry.Message);
                    NinjaTrader.Code.Output.Process(line, PrintTo.OutputTab1);
                }
                catch { }
            }
        }

        /// <summary>Copia cronológica (del más viejo al más nuevo) de las últimas N entradas.</summary>
        public static RelativeLogEntry[] Snapshot(int n)
        {
            lock (_lock)
            {
                int count = Math.Min(n <= 0 ? _filled : n, _filled);
                if (count == 0) return new RelativeLogEntry[0];
                var result = new RelativeLogEntry[count];
                int start = (_head - count + _ring.Length) % _ring.Length;
                for (int i = 0; i < count; i++)
                    result[i] = _ring[(start + i) % _ring.Length];
                return result;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _head = 0;
                _filled = 0;
                // No reseteamos _totalCount — útil como contador monotónico
            }
        }

        #endregion
    }

    /// <summary>Extension methods que los indicadores usan para loggear estructurado.</summary>
    public static class RelativeLogExtensions
    {
        public static void RLog(this NinjaScriptBase ns, string format, params object[] args)
        {
            RLogCore(ns, RelativeLogLevel.Info, format, args);
        }

        public static void RLog(this NinjaScriptBase ns, RelativeLogLevel level, string format, params object[] args)
        {
            RLogCore(ns, level, format, args);
        }

        public static void RLogW(this NinjaScriptBase ns, string format, params object[] args)
        {
            RLogCore(ns, RelativeLogLevel.Warning, format, args);
        }

        public static void RLogE(this NinjaScriptBase ns, string format, params object[] args)
        {
            RLogCore(ns, RelativeLogLevel.Error, format, args);
        }

        private static void RLogCore(NinjaScriptBase ns, RelativeLogLevel level, string format, object[] args)
        {
            string msg;
            try { msg = args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args); }
            catch (Exception ex) { msg = "[format error: " + ex.Message + "] " + format; }

            var entry = new RelativeLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = msg,
                Indicator = SafeGet(() => ns != null ? ns.Name : ""),
                Instrument = SafeGet(() =>
                {
                    var inst = ns != null ? GetProp(ns, "Instrument") : null;
                    return inst == null ? "" : (GetProp(inst, "FullName") ?? "").ToString();
                }),
                Period = SafeGet(() =>
                {
                    var bp = ns != null ? GetProp(ns, "BarsPeriod") : null;
                    if (bp == null) return "";
                    var val = GetProp(bp, "Value");
                    var type = GetProp(bp, "BarsPeriodType");
                    return (val == null ? "?" : val.ToString()) + (type == null ? "" : type.ToString());
                }),
                BarTime = SafeGetDT(() =>
                {
                    // Time[0] del NinjaScriptBase (si existe bar)
                    var times = ns != null ? GetProp(ns, "Time") : null;
                    if (times == null) return DateTime.MinValue;
                    // Time es una series indexable. Accedemos vía indexer[0]
                    var mi = times.GetType().GetMethod("get_Item", new Type[] { typeof(int) });
                    if (mi == null) return DateTime.MinValue;
                    try { return (DateTime)mi.Invoke(times, new object[] { 0 }); } catch { return DateTime.MinValue; }
                }),
                CurrentBar = SafeGetInt(() => Convert.ToInt32(GetProp(ns, "CurrentBar") ?? -1)),
            };
            RelativeLog.Append(entry);
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var p = obj.GetType().GetProperty(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy);
                return p != null ? p.GetValue(obj, null) : null;
            }
            catch { return null; }
        }

        private static string SafeGet(Func<string> fn)
        {
            try { return fn() ?? ""; } catch { return ""; }
        }

        private static DateTime SafeGetDT(Func<DateTime> fn)
        {
            try { return fn(); } catch { return DateTime.MinValue; }
        }

        private static int SafeGetInt(Func<int> fn)
        {
            try { return fn(); } catch { return -1; }
        }
    }
}
