#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Comparación EXACTA trade por trade entre NT y Strategy CSV
Para identificar diferencias en precios, comisiones, o PnL
"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"

print("=" * 80)
print("COMPARACIÓN TRADE POR TRADE - NT vs Strategy CSV")
print("=" * 80)

# 1. Leer NT Export
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

# Parse todas las columnas de dinero de NT
df_nt['EntryPrice'] = pd.to_numeric(df_nt['Entry price'], errors='coerce')
df_nt['ExitPrice'] = pd.to_numeric(df_nt['Exit price'], errors='coerce')
df_nt['Qty'] = pd.to_numeric(df_nt['Qty'], errors='coerce')
df_nt['Profit_NT'] = df_nt['Profit'].apply(parse_nt_money)
df_nt['Commission_NT'] = df_nt['Commission'].apply(parse_nt_money) if 'Commission' in df_nt.columns else 0

print(f"\nNT Trade Performance:")
print(f"  Total trades: {len(df_nt)}")
print(f"  PnL Total: ${df_nt['Profit_NT'].sum():,.2f}")
print(f"  Comisiones Total: ${df_nt['Commission_NT'].sum():,.2f}")

# 2. Leer Strategy CSV
col_names = ['TradeId','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice',
             'Result','GrossPnL','Commission','NetPnL','MAE','MFE','SetupName','Attempt',
             'RiskReward','DeltaEntry','DeltaDir','SessionDelta','DeltaTP1','LevelAge',
             'Quantity','ExecutionId','EntryMode','ExitStrategy','RiskModel']

df_str = pd.read_csv(playback_dir + r"\MNQ_03-25.csv", names=col_names, header=0, skiprows=1)

print(f"\nStrategy CSV:")
print(f"  Total trades: {len(df_str)}")
print(f"  PnL Total: ${df_str['NetPnL'].sum():,.2f}")
print(f"  Comisiones Total: ${df_str['Commission'].sum():,.2f}")

# 3. Comparar primeros 10 trades lado a lado
print(f"\n" + "=" * 80)
print("PRIMEROS 10 TRADES - COMPARACIÓN DETALLADA")
print("=" * 80)

print("\n--- NT Export ---")
print(df_nt[['Trade number', 'Qty', 'Entry price', 'Exit price', 'Profit', 'Profit_NT', 'Commission_NT']].head(10).to_string())

print("\n--- Strategy CSV ---")
print(df_str[['TradeId', 'Quantity', 'EntryPrice', 'ExitPrice', 'GrossPnL', 'Commission', 'NetPnL']].head(10).to_string())

# 4. Calcular diferencias específicas
print(f"\n" + "=" * 80)
print("ANÁLISIS DE DIFERENCIAS")
print("=" * 80)

# Comparar totales
nt_total = df_nt['Profit_NT'].sum()
str_total = df_str['NetPnL'].sum()
diff = str_total - nt_total

print(f"\nPnL Total:")
print(f"  NT:       ${nt_total:,.2f}")
print(f"  Strategy: ${str_total:,.2f}")
print(f"  DIFF:     ${diff:,.2f}")

# Comparar comisiones
nt_comm = df_nt['Commission_NT'].sum()
str_comm = df_str['Commission'].sum()
comm_diff = str_comm - nt_comm

print(f"\nComisiones Total:")
print(f"  NT:       ${nt_comm:,.2f}")
print(f"  Strategy: ${str_comm:,.2f}")
print(f"  DIFF:     ${comm_diff:,.2f}")

# Comparar cantidad de trades
print(f"\nCantidad de trades:")
print(f"  NT:       {len(df_nt)} ejecuciones")
print(f"  Strategy: {len(df_str)} ejecuciones")
print(f"  DIFF:     {len(df_nt) - len(df_str)} trades")

# 5. Mostrar datos crudos del primer trade para verificar formato
print(f"\n" + "=" * 80)
print("VERIFICACIÓN: Primer trade (datos crudos)")
print("=" * 80)
print("\nNT Export - Fila 1:")
print(df_nt.iloc[0][['Trade number', 'Qty', 'Entry price', 'Exit price', 'Profit', 'Commission']].to_string())

print("\nStrategy CSV - Fila 1:")
print(df_str.iloc[0][['TradeId', 'Quantity', 'EntryPrice', 'ExitPrice', 'GrossPnL', 'Commission', 'NetPnL']].to_string())

# 6. Calcular PnL manualmente para verificar
first_nt = df_nt.iloc[0]
first_str = df_str.iloc[0]

# MNQ tick value = $2 (cada tick = 0.25 puntos = $0.50)
tick_value = 2.0

if first_nt['EntryPrice'] > 0:
    price_diff_nt = first_nt['EntryPrice'] - first_nt['ExitPrice']  # Short: Entry - Exit
    pnl_calc_nt = price_diff_nt * tick_value * first_nt['Qty']
    
    print(f"\n--- Cálculo manual PnL (primer trade NT) ---")
    print(f"Entry: {first_nt['EntryPrice']}, Exit: {first_nt['ExitPrice']}")
    print(f"Diff: {price_diff_nt} puntos")
    print(f"Qty: {first_nt['Qty']}")
    print(f"PnL calculado: ${pnl_calc_nt:,.2f}")
    print(f"PnL reportado NT: ${first_nt['Profit_NT']:,.2f}")

if first_str['EntryPrice'] > 0:
    price_diff_str = first_str['EntryPrice'] - first_str['ExitPrice']  # Short: Entry - Exit
    pnl_calc_str = price_diff_str * tick_value * first_str['Quantity']
    
    print(f"\n--- Cálculo manual PnL (primer trade Strategy) ---")
    print(f"Entry: {first_str['EntryPrice']}, Exit: {first_str['ExitPrice']}")
    print(f"Diff: {price_diff_str} puntos")
    print(f"Qty: {first_str['Quantity']}")
    print(f"PnL calculado: ${pnl_calc_str:,.2f}")
    print(f"PnL reportado: ${first_str['GrossPnL']:,.2f}")
    print(f"NetPnL: ${first_str['NetPnL']:,.2f}")

print(f"\n" + "=" * 80)
