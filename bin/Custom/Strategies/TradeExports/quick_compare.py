#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ANÁLISIS SIMPLE: Comparar PnL total entre archivos sin importar fechas
para identificar la fuente de la discrepancia de $43.50
"""

import pandas as pd
import glob

# Find files
playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"
nt_files = glob.glob(playback_dir + r"\NinjaTrader*.csv")
nt_export = nt_files[0]
strategy_export = playback_dir + r"\MNQ_03-25.csv"

print("=" * 80)
print("ANÁLISIS RÁPIDO: PnL TOTAL (Sin filtro de fechas)")
print("=" * 80)

# Leer NT
df_nt = pd.read_csv(nt_export, encoding='utf-8')
print(f"\nNT Export: {len(df_nt)} trades")
print(f"Columnas principales: {list(df_nt.columns[:8])}")

# Leer Strategy
df_str = pd.read_csv(strategy_export, encoding='utf-8')
print(f"\nStrategy Export: {len(df_str)} trades")
print(f"Columnas principales: {list(df_str.columns[:8])}")

# Parse PnL de NT (formato: "$-8200" = -$82.00)
def parse_nt_money(val):
    if pd.isna(val):
        return 0.0
    s = str(val).replace('$', '').replace(',', '').strip()
    try:
        return float(s) / 100.0
    except:
        return 0.0

df_nt['PnL_Parsed'] = df_nt['Profit'].apply(parse_nt_money)

# PnL Strategy (NetPnL ya está en dólares)
df_str['PnL_Parsed'] = pd.to_numeric(df_str['NetPnL'], errors='coerce')

# Totales
nt_total = df_nt['PnL_Parsed'].sum()
str_total = df_str['PnL_Parsed'].sum()

print("\n" + "=" * 80)
print("RESULTADO")
print("=" * 80)
print(f"PnL TOTAL - NT Trade Performance: ${nt_total:,.2f} ({len(df_nt)} trades)")
print(f"PnL TOTAL - Strategy CSV:         ${str_total:,.2f} ({len(df_str)} trades)")
print(f"DISCREPANCIA:                      ${str_total - nt_total:,.2f}")
print(f"Diferencia en cantidad:            {abs(len(df_nt) - len(df_str))} trades")

# Mostrar primeros trades de cada uno
print("\n" + "=" * 80)
print("PRIMEROS 5 TRADES - NT Export")
print("=" * 80)
print(df_nt[['Trade number', 'Instrument', 'Qty', 'Profit', 'PnL_Parsed']].head().to_string())

print("\n" + "=" * 80)
print("PRIMEROS 5 TRADES - Strategy Export")
print("=" * 80)
print(df_str[['ID', 'Instrument', 'NetPnL', 'PnL_Parsed']].head().to_string())

# Agrupar Strategy por Trade Lógico (IDs sin '.2', '.3', etc)
if 'ID' in df_str.columns:
    df_str['LogicalID'] = df_str['ID'].astype(str).str.split('.').str[0]
    logical_summary = df_str.groupby('LogicalID').agg({
        'PnL_Parsed': 'sum',
        'ID': 'count'
    }).rename(columns={'ID': 'Contracts'})
    
    logical_total = logical_summary['PnL_Parsed'].sum()
    
    print("\n" + "=" * 80)
    print(f"TRADES LÓGICOS AGRUPADOS (Strategy)")
    print("=" * 80)
    print(f"Total trades lógicos: {len(logical_summary)}")
    print(f"Total contratos: {logical_summary['Contracts'].sum()}")
    print(f"PnL agrupado: ${logical_total:,.2f}")
    
    print("\n--- Primeros 10 trades lógicos ---")
    print(logical_summary.head(10).to_string())

print("\n" + "=" * 80)
print("FIN - Revisar números arriba para identificar discrepancia")
print("=" * 80)
