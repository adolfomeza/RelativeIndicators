#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Análisis detallado: ¿Qué trades causan la diferencia de $43.50?
NT: $1,636.70
App: $1,680.20
Diferencia: $43.50
"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"

print("=" * 80)
print("ANÁLISIS DE DISCREPANCIA: $43.50")
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

df_nt['NetPnL'] = df_nt['Profit'].apply(parse_nt_money)
nt_total = df_nt['NetPnL'].sum()

print(f"\n1. NT TRADE PERFORMANCE")
print(f"   Total ejecuciones: {len(df_nt)}")
print(f"   PnL Total: ${nt_total:,.2f}")

# 2. Leer Strategy CSV con nombres correctos
col_names = ['TradeId','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice',
             'Result','GrossPnL','Commission','NetPnL','MAE','MFE','SetupName','Attempt',
             'RiskReward','DeltaEntry','DeltaDir','SessionDelta','DeltaTP1','LevelAge',
             'Quantity','ExecutionId','EntryMode','ExitStrategy','RiskModel']

df_str = pd.read_csv(playback_dir + r"\MNQ_03-25.csv", names=col_names, header=0, skiprows=1)

# Parse dates
df_str['EntryTime'] = pd.to_datetime(df_str['EntryTime'], errors='coerce')
df_str['ExitTime'] = pd.to_datetime(df_str['ExitTime'], errors='coerce')

strategy_total = df_str['NetPnL'].sum()

print(f"\n2. STRATEGY CSV (SIN FILTROS)")
print(f"   Total trades: {len(df_str)}")
print(f"   PnL Total: ${strategy_total:,.2f}")

# 3. Aplicar filtro de trades históricos (viernes → post-domingo)
# Excluir trades que:
# - Entraron el viernes (day_of_week == 4)
# - Y salieron después del domingo (day_of_week > 6 o lunes siguiente)

df_str['EntryDayOfWeek'] = df_str['EntryTime'].dt.dayofweek  # 0=Monday, 4=Friday
df_str['ExitDayOfWeek'] = df_str['ExitTime'].dt.dayofweek

# Trades históricos: viernes → lunes o después
df_str['IsHistorical'] = (df_str['EntryDayOfWeek'] == 4) & (df_str['ExitDayOfWeek'] >= 0)

df_filtered = df_str[~df_str['IsHistorical']].copy()
filtered_total = df_filtered['NetPnL'].sum()

print(f"\n3. STRATEGY CSV (CON FILTRO DE HISTÓRICOS)")
print(f"   Trades después de filtro: {len(df_filtered)}")
print(f"   PnL después de filtro: ${filtered_total:,.2f}")
print(f"   Trades filtrados: {len(df_str) - len(df_filtered)}")
print(f"   PnL filtrado: ${strategy_total - filtered_total:,.2f}")

# 4. Comparación final
print(f"\n" + "=" * 80)
print("RESUMEN DE DISCREPANCIA")
print("=" * 80)
print(f"NT Trade Performance:        ${nt_total:,.2f}")
print(f"Strategy (filtrado):         ${filtered_total:,.2f}")
print(f"DIFERENCIA:                  ${filtered_total - nt_total:,.2f}")

# 5. Análisis de la diferencia
if abs(filtered_total - nt_total) > 0.01:
    print(f"\n" + "=" * 80)
    print("POSIBLES CAUSAS DE LA DIFERENCIA")
    print("=" * 80)
    
    # Comparar cantidades
    print(f"\nCantidad de trades:")
    print(f"  NT: {len(df_nt)} ejecuciones")
    print(f"  Strategy filtrado: {len(df_filtered)} ejecuciones")
    print(f"  Diferencia: {abs(len(df_nt) - len(df_filtered))} trades")
    
    # Ver si hay trades agrupados vs individuales
    df_filtered['LogicalID'] = df_filtered['TradeId'].astype(str).str.split('.').str[0]
    logical_count = df_filtered['LogicalID'].nunique()
    
    print(f"\nTrades lógicos (Strategy agrupado): {logical_count}")
    
    # Mostrar trades históricos que fueron excluidos
    historical_trades = df_str[df_str['IsHistorical']]
    if len(historical_trades) > 0:
        print(f"\n--- TRADES HISTÓRICOS EXCLUIDOS (viernes→lunes) ---")
        print(historical_trades[['TradeId', 'EntryTime', 'ExitTime', 'NetPnL']].to_string())
    
    # Buscar trades con PnL cercano a $43.50
    print(f"\n--- BUSCANDO TRADES QUE SUMEN ~$43.50 ---")
    # Buscar combinaciones
    target = filtered_total - nt_total
    print(f"Objetivo: ${target:,.2f}")
    
    # Mostrar trades individuales cercanos
    close_trades = df_filtered[
        (df_filtered['NetPnL'] >= target - 10) & 
        (df_filtered['NetPnL'] <= target + 10)
    ]
    if len(close_trades) > 0:
        print("\nTrades con PnL cercano a la discrepancia:")
        print(close_trades[['TradeId', 'Result', 'NetPnL', 'EntryTime']].to_string())
    
    # Agrupar por resultado para ver patrones
    print(f"\n--- DISTRIBUCIÓN POR TIPO DE RESULTADO ---")
    result_dist = df_filtered.groupby('Result')['NetPnL'].agg(['count', 'sum'])
    print(result_dist.to_string())

print(f"\n" + "=" * 80)
