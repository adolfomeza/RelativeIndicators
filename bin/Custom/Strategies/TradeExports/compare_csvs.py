#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Script para comparar los CSVs de NinjaTrader Trade Performance vs Strategy Export
Identifica discrepancias en PnL para análisis de reconciliación
"""

import pandas as pd
import sys
import glob
from datetime import datetime

# Paths
playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"
nt_files = glob.glob(playback_dir + r"\NinjaTrader*.csv")
if not nt_files:
    print("ERROR: No se encontró el archivo de NinjaTrader Grid")
    sys.exit(1)
nt_export = nt_files[0]
strategy_export = playback_dir + r"\MNQ_03-25.csv"

print("="*80)
print("ANÁLISIS DE DISCREPANCIA - Trade Performance vs Strategy CSV")
print("="*80)

# 1. Leer NT Export
try:
    df_nt = pd.read_csv(nt_export, encoding='utf-8')
    print(f"\n✓ NT Export leído: {len(df_nt)} filas")
    print(f"Columnas: {list(df_nt.columns[:10])}")
except Exception as e:
    print(f"\n✗ Error leyendo NT Export: {e}")
    sys.exit(1)

# 2. Leer Strategy Export  
try:
    df_str = pd.read_csv(strategy_export, encoding='utf-8')
    print(f"\n✓ Strategy Export leído: {len(df_str)} filas")
    print(f"Columnas: {list(df_str.columns[:10])}")
except Exception as e:
    print(f"\n✗ Error leyendo Strategy Export: {e}")
    sys.exit(1)

# 3. Parse dates - NT usa formato especial: "6/1/25 3:19:04 a. m." = Enero 6, 2025 3:19 AM
def parse_nt_datetime(val):
    """Parse NT datetime format: M/D/YY H:MM:SS a.m./p.m."""
    if pd.isna(val):
        return pd.NaT
    try:
        s = str(val).strip()
        # Replace Spanish a.m./p.m. with AM/PM
        s = s.replace(' a. m.', ' AM').replace(' p. m.', ' PM')
        # Parse with dayfirst=False (M/D/YY format)
        return pd.to_datetime(s, format='%m/%d/%y %I:%M:%S %p', errors='coerce')
    except:
        return pd.NaT

df_nt['Entry time'] = df_nt['Entry time'].apply(parse_nt_datetime)
df_nt['Exit time'] = df_nt['Exit time'].apply(parse_nt_datetime)

df_str['EntryTime'] = pd.to_datetime(df_str['EntryTime'], errors='coerce')
df_str['ExitTime'] = pd.to_datetime(df_str['ExitTime'], errors='coerce')

# Show date ranges
print(f"\n--- RANGOS DE FECHAS ---")
print(f"NT Export:")
print(f"  Primer trade: {df_nt['Entry time'].min()}")
print(f"  Último trade: {df_nt['Exit time'].max()}")
print(f"\nStrategy Export:")
print(f"  Primer trade: {df_str['EntryTime'].min()}")
print(f"  Último trade: {df_str['ExitTime'].max()}")

# 4. Filter week Jan 5-11, 2025 (AMPLIADO: 1-15 para capturar todo enero)
week_start = pd.to_datetime('2025-01-01')
week_end = pd.to_datetime('2025-01-15 23:59:59')

df_nt_week = df_nt[(df_nt['Exit time'] >= week_start) & (df_nt['Exit time'] <= week_end)].copy()
df_str_week = df_str[(df_str['ExitTime'] >= week_start) & (df_str['ExitTime'] <= week_end)].copy()

print(f"\n" + "="*80)
print(f"SEMANA: {week_start.date()} a {week_end.date()}")
print("="*80)

print(f"\nNT Export - Trades en semana: {len(df_nt_week)}")
print(f"Strategy Export - Trades en semana: {len(df_str_week)}")

# 5. Parse PnL (NT uses format like "$-8200" = -$82.00)
def parse_nt_money(val):
    if pd.isna(val):
        return 0.0
    s = str(val).replace('$', '').replace(',', '').strip()
    try:
        return float(s) / 100.0  # NT exports * 100
    except:
        return 0.0

df_nt_week['PnL_Parsed'] = df_nt_week['Profit'].apply(parse_nt_money)
df_str_week['PnL_Parsed'] = pd.to_numeric(df_str_week['NetPnL'], errors='coerce')

# 6. Totals
nt_total = df_nt_week['PnL_Parsed'].sum()
str_total = df_str_week['PnL_Parsed'].sum()

print(f"\n" + "-"*80)
print(f"PnL TOTAL - NT Trade Performance: ${nt_total:,.2f}")
print(f"PnL TOTAL - Strategy CSV:         ${str_total:,.2f}")
print(f"DISCREPANCIA:                      ${str_total - nt_total:,.2f}")
print("-"*80)

# 7. Detailed comparison
print(f"\n" + "="*80)
print("ANÁLISIS DETALLADO")
print("="*80)

# Show first 5 trades from each
print("\n--- NT Export (primeros 5 trades) ---")
print(df_nt_week[['Trade number', 'Entry time', 'Exit time', 'Profit', 'PnL_Parsed']].head().to_string())

print("\n--- Strategy Export (primeros 5 trades) ---")
print(df_str_week[['ID', 'EntryTime', 'ExitTime', 'NetPnL', 'PnL_Parsed']].head().to_string())

# 8. Count comparison
print(f"\n" + "="*80)
print("POSIBLES CAUSAS DE DISCREPANCIA")
print("="*80)

print(f"\n1. Diferencia en cantidad de trades:")
print(f"   NT: {len(df_nt_week)} trades")
print(f"   Strategy: {len(df_str_week)} trades")
print(f"   Diferencia: {abs(len(df_nt_week) - len(df_str_week))} trades")

# 9. Check for duplicates in strategy export
dup_count = df_str_week.duplicated(subset=['EntryTime', 'EntryPrice', 'ExitPrice']).sum()
print(f"\n2. Trades duplicados en Strategy CSV: {dup_count}")

# 10. Group by logical trade ID prefix (e.g., "20250108_85")
if 'ID' in df_str_week.columns:
    df_str_week['LogicalID'] = df_str_week['ID'].astype(str).str.split('.').str[0]
    logical_trades = df_str_week.groupby('LogicalID').agg({
        'PnL_Parsed': 'sum',
        'ID': 'count'
    }).rename(columns={'ID': 'Executions'})
    
    print(f"\n3. Trades lógicos agrupados (Strategy CSV):")
    print(f"   Total trades lógicos: {len(logical_trades)}")
    print(f"   Total ejecuciones: {logical_trades['Executions'].sum()}")
    print(f"   PnL total agrupado: ${logical_trades['PnL_Parsed'].sum():,.2f}")

# 11. Commission comparison
if 'Commission' in df_nt.columns and 'Commission' in df_str.columns:
    nt_comm = df_nt_week['Commission'].apply(parse_nt_money).sum()
    str_comm = df_str_week['Commission'].sum()
    print(f"\n4. Comisiones:")
    print(f"   NT: ${nt_comm:,.2f}")
    print(f"   Strategy: ${str_comm:,.2f}")
    print(f"   Diferencia: ${abs(nt_comm - str_comm):,.2f}")

print(f"\n" + "="*80)
print("FIN DEL ANÁLISIS")
print("="*80)
