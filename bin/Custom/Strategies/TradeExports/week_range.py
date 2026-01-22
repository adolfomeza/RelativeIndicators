#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Análisis con rango correcto: 5-10 enero (SIN incluir el día 10)
"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"

print("=" * 80)
print("ANÁLISIS: Semana 5-9 enero 2025 (excluyendo viernes 10)")
print("=" * 80)

# 1. NT Export
nt_files = glob.glob(playback_dir + r"\NinjaTrader*.csv")
df_nt = pd.read_csv(nt_files[0], encoding='utf-8')

def parse_nt_money(val):
    if pd.isna(val):
        return 0.0
    s = str(val).replace('$', '').replace(',', '').strip()
    try:
        return float(s) / 100.0
    except:
        return 0.0

df_nt['NetPnL'] = df_nt['Profit'].apply(parse_nt_money)

# 2. Strategy CSV
col_names = ['TradeId','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice',
             'Result','GrossPnL','Commission','NetPnL','MAE','MFE','SetupName','Attempt',
             'RiskReward','DeltaEntry','DeltaDir','SessionDelta','DeltaTP1','LevelAge',
             'Quantity','ExecutionId','EntryMode','ExitStrategy','RiskModel']

df_str = pd.read_csv(playback_dir + r"\MNQ_03-25.csv", names=col_names, header=0, skiprows=1)
df_str['ExitTime'] = pd.to_datetime(df_str['ExitTime'], errors='coerce')

# 3. Filtrar por rango correcto: 5-9 enero (SIN día 10)
week_start = pd.to_datetime('2025-01-05')
week_end = pd.to_datetime('2025-01-09 23:59:59')  # Hasta el 9, NO el 10

df_week = df_str[(df_str['ExitTime'] >= week_start) & (df_str['ExitTime'] <= week_end)].copy()

week_total = df_week['NetPnL'].sum()

print(f"\nStrategy CSV - Semana 5-9 enero:")
print(f"  Trades: {len(df_week)}")
print(f"  PnL Total: ${week_total:,.2f}")

print(f"\nNT Trade Performance:")
print(f"  Trades: {len(df_nt)}")
print(f"  PnL Total: ${df_nt['NetPnL'].sum():,.2f}")

print(f"\n" + "=" * 80)
print(f"COMPARACIÓN")
print(f"=" * 80)
print(f"Strategy (5-9 enero):  ${week_total:,.2f}")
print(f"NT:                    ${df_nt['NetPnL'].sum():,.2f}")
print(f"DIFERENCIA:            ${week_total - df_nt['NetPnL'].sum():,.2f}")

# Mostrar trades de la semana
print(f"\n--- TRADES EN SEMANA 5-9 ENERO ---")
print(df_week[['TradeId', 'ExitTime', 'Result', 'NetPnL']].to_string())

print(f"\n--- TRADES EXCLUIDOS (día 10 y posteriores) ---")
df_excluded = df_str[df_str['ExitTime'] > week_end]
print(f"Total excluidos: {len(df_excluded)}")
print(f"PnL excluido: ${df_excluded['NetPnL'].sum():,.2f}")
if len(df_excluded) > 0:
    print(df_excluded[['TradeId', 'ExitTime', 'NetPnL']].head(10).to_string())

print(f"\n" + "=" * 80)
