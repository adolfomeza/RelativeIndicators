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
        }
    }

    public class IndicatorState
    {
        public string Key;
        public DateTime UpdatedAt;   // UTC
        public IDictionary<string, object> Payload;
    }
}
