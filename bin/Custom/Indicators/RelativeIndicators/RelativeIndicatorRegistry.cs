#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
#endregion

// RelativeIndicatorRegistry — registro in-memory de estado runtime de indicadores.
//
// Cualquier indicador de la suite puede publicar snapshots de su estado actual
// (valores de señales, último VWAP, delta de sesión, etc.) y el AddOn
// RelativeObserver los expone en el endpoint /indicator-state/ para consumo
// desde el MCP / Claude mientras desarrollás.
//
// Uso desde un indicador (opt-in):
//
//   RelativeIndicatorRegistry.Publish("RelativeVwap:MES 06-26:1Minute",
//       new Dictionary<string, object>
//       {
//           ["vwap"] = vwapValue,
//           ["delta_global"] = deltaGlobal,
//           ["signal2_active"] = signal2,
//           ["last_update"] = Time[0],
//       });
//
// El key es libre — convención sugerida:
//   "{IndicatorName}:{Instrument.FullName}:{BarsPeriod.Value}{BarsPeriod.BarsPeriodType}"
//
// Thread-safe. Live reads desde el HttpListener; writes desde OnBarUpdate.

namespace NinjaTrader.NinjaScript.AddOns
{
    public static class RelativeIndicatorRegistry
    {
        private static readonly ConcurrentDictionary<string, IndicatorState> _states =
            new ConcurrentDictionary<string, IndicatorState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Publica (o sobrescribe) el estado de un indicador bajo la clave dada.
        /// Se guarda una copia defensiva del payload — los modificaciones posteriores
        /// al diccionario pasado como argumento no afectan lo publicado.
        /// </summary>
        public static void Publish(string key, IDictionary<string, object> payload)
        {
            if (string.IsNullOrEmpty(key)) return;
            var copy = payload == null
                ? new Dictionary<string, object>()
                : payload.ToDictionary(kv => kv.Key, kv => kv.Value);
            var state = new IndicatorState
            {
                Key = key,
                UpdatedAt = DateTime.UtcNow,
                Payload = copy,
            };
            _states[key] = state;
        }

        /// <summary>Remueve una clave (por ejemplo en State.Terminated de un indicador).</summary>
        public static void Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _states.TryRemove(key, out _);
        }

        /// <summary>Snapshot de todos los estados publicados.</summary>
        public static IReadOnlyList<IndicatorState> Snapshot()
        {
            return _states.Values.ToList();
        }

        /// <summary>Obtiene un estado por clave. Devuelve null si no existe.</summary>
        public static IndicatorState Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            _states.TryGetValue(key, out var s);
            return s;
        }

        /// <summary>Limpia todo el registro.</summary>
        public static void Clear()
        {
            _states.Clear();
            _queryHandlers.Clear();
        }

        // ===========================================================================
        // Historical query handlers — point-in-time replay support
        // ===========================================================================
        //
        // Cada indicador puede registrar un callback que, dado un timestamp histórico,
        // devuelve un dict con su estado en ESE momento (leído de sus Series<double>
        // internas). Permite snapshots replay sin recomputar nada.
        //
        // Uso desde un indicador (en State.DataLoaded):
        //
        //     RelativeIndicatorRegistry.RegisterQueryHandler(
        //         "RelativeDailyVwap:" + Instrument.FullName,
        //         asOf => {
        //             int idx = Bars.GetBar(asOf);
        //             return new Dictionary<string, object> {
        //                 ["dvah"] = UpperBand1.GetValueAt(idx),
        //                 ["vwap"] = Values[2].GetValueAt(idx),
        //                 ["dval"] = LowerBand1.GetValueAt(idx),
        //                 ["bar_time"] = Bars.GetTime(idx),
        //             };
        //         });
        //
        // Y en State.Terminated:
        //     RelativeIndicatorRegistry.UnregisterQueryHandler(key);
        //
        // El AddOn RelativeObserver expone esto vía /indicator-state/{key}/at?ts=...

        public delegate IDictionary<string, object> HistoricalQueryHandler(DateTime asOf);

        private static readonly ConcurrentDictionary<string, HistoricalQueryHandler> _queryHandlers =
            new ConcurrentDictionary<string, HistoricalQueryHandler>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterQueryHandler(string key, HistoricalQueryHandler handler)
        {
            if (string.IsNullOrEmpty(key) || handler == null) return;
            _queryHandlers[key] = handler;
        }

        public static void UnregisterQueryHandler(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _queryHandlers.TryRemove(key, out _);
        }

        /// <summary>
        /// Invoca el query handler registrado bajo `key` con el timestamp dado.
        /// Devuelve null si no hay handler. Si el handler tira excepción, devuelve
        /// un dict con la clave "error" en lugar de propagar.
        /// </summary>
        public static IDictionary<string, object> QueryAt(string key, DateTime asOf)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_queryHandlers.TryGetValue(key, out var handler)) return null;
            try { return handler(asOf); }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["error"] = ex.GetType().Name + ": " + ex.Message,
                };
            }
        }

        public static IReadOnlyList<string> QueryHandlerKeys()
        {
            return _queryHandlers.Keys.ToList();
        }
    }

    public class IndicatorState
    {
        public string Key;
        public DateTime UpdatedAt;   // UTC
        public IDictionary<string, object> Payload;
    }
}
