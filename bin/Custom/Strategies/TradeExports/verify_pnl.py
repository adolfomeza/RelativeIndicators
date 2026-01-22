#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Verificación simple: ¿Cuál es el PnL real de cada archivo?
"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"
nt_files = glob.glob(playback_dir + r"\NinjaTrader*.csv")
nt_export = nt_files[0]
strategy_export = playback_dir + r"\MNQ_03-25.csv"

print("=" * 80)
print("VERIFICACIÓN DE PNL - ¿Qué muestran realmente?")
print("=" * 80)

# NT Export
df_nt = pd.read_csv(nt_export, encoding='utf-8')

def parse_nt_money(val):
    if pd.isna(val):
        return 0.0
    s = str(val).replace('$', '').replace(',', '').strip()
    try:
        return float(s) / 100.0
    except:
        return 0.0

df_nt['PnL'] = df_nt['Profit'].apply(parse_nt_money)
nt_total = df_nt['PnL'].sum()

print(f"\n1. NT TRADE PERFORMANCE (archivo exportado manualmente)")
print(f"   Trades: {len(df_nt)}")
print(f"   PnL Total: ${nt_total:,.2f}")

# Strategy Export
df_str = pd.read_csv(strategy_export, encoding='utf-8')
df_str['PnL'] = pd.to_numeric(df_str['NetPnL'], errors='coerce').fillna(0)
str_total = df_str['PnL'].sum()
str_count = len(df_str)

print(f"\n2. STRATEGY CSV (generado automáticamente)")
print(f"   Filas totales: {str_count}")
print(f"   PnL Total (sumando TODAS las filas): ${str_total:,.2f}")

# Agrupar por Trade Lógico
df_str['LogicalID'] = df_str['ID'].astype(str).str.split('.').str[0]
logical_trades = df_str.groupby('LogicalID')['PnL'].sum()
logical_total = logical_trades.sum()
logical_count = len(logical_trades)

print(f"\n3. STRATEGY CSV AGRUPADO (1 fila = 1 trade completo)")
print(f"   Trades lógicos: {logical_count}")
print(f"   PnL Total agrupado: ${logical_total:,.2f}")

print(f"\n" + "=" * 80)
print(f"RESUMEN")
print(f"=" * 80)
print(f"NT muestra:        ${nt_total:,.2f} ({len(df_nt)} ejecuciones)")
print(f"Strategy muestra:  ${str_total:,.2f} ({str_count} filas)")
print(f"Strategy agrupado: ${logical_total:,.2f} ({logical_count} trades)")
print(f"\nDISCREPANCIA NT vs Strategy (sin agrupar):  ${abs(nt_total - str_total):,.2f}")
print(f"DISCREPANCIA NT vs Strategy (agrupado):     ${abs(nt_total - logical_total):,.2f}")

# ¿Por qué la app muestra $1,680.20?
print(f"\n" + "=" * 80)
print(f"ANÁLISIS: ¿Por qué la app muestra $1,680.20?")
print(f"=" * 80)

# Mostrar distribución de PnL
print(f"\nDistribución de PnL por tipo de resultado:")
if 'Result' in df_str.columns:
    result_summary = df_str.groupby('Result')['PnL'].agg(['count', 'sum'])
    print(result_summary.to_string())

print(f"\n" + "=" * 80)
