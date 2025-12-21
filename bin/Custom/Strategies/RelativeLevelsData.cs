#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Define los tipos de niveles soportados.
    /// </summary>
    public enum VirginLevelType
    {
        Manual,
        AsiaHigh,
        AsiaLow,
        EuropeHigh,
        EuropeLow,
        USAHigh,
        USALow,
        SessionHigh, // Generico
        SessionLow   // Generico
    }

    /// <summary>
    /// Representa un nivel de liquidez "Virgen".
    /// </summary>
    public class VirginLevel
    {
        [XmlAttribute]
        public double Price { get; set; }

        [XmlAttribute]
        public DateTime Date { get; set; }

        [XmlAttribute]
        public VirginLevelType Type { get; set; }

        [XmlAttribute]
        public bool IsResistance { get; set; }

        // Constructor vacio para serializacion
        public VirginLevel() { }

        public VirginLevel(double price, DateTime date, VirginLevelType type, bool isResistance)
        {
            Price = price;
            Date = date;
            Type = type;
            IsResistance = isResistance;
        }
    }

    /// <summary>
    /// Gestiona la carga, guardado y limpieza de niveles virgenes.
    /// </summary>
    public static class VirginLevelsManager
    {
        public static List<VirginLevel> LoadLevels(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // Retorna lista vacia si no existe el archivo
                return new List<VirginLevel>();
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<VirginLevel>));
                using (FileStream stream = new FileStream(filePath, FileMode.Open))
                {
                    return (List<VirginLevel>)serializer.Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process("Error cargando Virgin Levels: " + ex.Message, PrintTo.OutputTab1);
                return new List<VirginLevel>();
            }
        }

        public static void SaveLevels(string filePath, List<VirginLevel> levels)
        {
            try
            {
                // Asegurar que el directorio existe
                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                XmlSerializer serializer = new XmlSerializer(typeof(List<VirginLevel>));
                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    serializer.Serialize(stream, levels);
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process("Error guardando Virgin Levels: " + ex.Message, PrintTo.OutputTab1);
            }
        }

        /// <summary>
        /// Elimina niveles más antiguos que 'cutoffDate' y niveles duplicados cercanos.
        /// </summary>
        public static void AuditLevels(List<VirginLevel> levels, DateTime cutoffDate)
        {
            if (levels == null) return;

            // 1. Remover antiguos
            levels.RemoveAll(l => l.Date < cutoffDate);

            // 2. (Opcional) Remover duplicados exactos o muy cercanos
            // Por ahora solo removemos duplicados exactos de Precio y Tipo
            // Implementacion simple O(N^2) - suficiente para listas pequeñas (< 1000)
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (Math.Abs(levels[i].Price - levels[j].Price) < 0.00001 &&
                        levels[i].Type == levels[j].Type)
                    {
                        levels.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
