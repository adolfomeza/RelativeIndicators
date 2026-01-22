#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Investigación: ¿Por qué NT calcula PnL diferente?
Analizando los primeros 20 trades en detalle
"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"

print("=" * 100)
print("INVESTIGACIÓN: ¿POR QUÉ NT CALCULA PNL DIFERENTE?")
print("=" * 100)

# Leer NT Export
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

# Parse columnas
df_nt['Entry'] = pd.to_numeric(df_nt['Entry price'], errors='coerce')
df_nt['Exit'] = pd.to_numeric(df_nt['Exit price'], errors='coerce')
df_nt['Qty'] = pd.to_numeric(df_nt['Qty'], errors='coerce')
df_nt['Profit_NT'] = df_nt['Profit'].apply(parse_nt_money)
df_nt['Comm_NT'] = df_nt['Commission'].apply(parse_nt_money) if 'Commission' in df_nt.columns else 0
df_nt['Direction'] = df_nt['Market pos.']

# MNQ: 1 punto = 4 ticks de 0.25
# Valor de tick = $0.50
# Valor de punto = $2.00
TICK_SIZE = 0.25
TICK_VALUE = 0.50
POINT_VALUE = 2.00

# Calcular PnL manualmente para cada trade
results = []

for idx, row in df_nt.head(20).iterrows():
    entry = row['Entry']
    exit_price = row['Exit']
    qty = row['Qty']
    direction = row['Direction']
    profit_nt = row['Profit_NT']
    comm_nt = row['Comm_NT']
    
    # Calcular price difference
    if direction == 'Short':
        price_diff = entry - exit_price  # Ganancia cuando exit < entry
    else:  # Long
        price_diff = exit_price - entry  # Ganancia cuando exit > entry
    
    # Cálculos posibles
    calc_by_tick = (price_diff / TICK_SIZE) * TICK_VALUE * qty
    calc_by_point = price_diff * POINT_VALUE * qty
    
    # ¿Es NetPnL o GrossPnL?
    net_pnl = profit_nt - comm_nt
    gross_from_net = profit_nt + comm_nt
    
    # Ver si algún cálculo coincide
    match_tick = abs(calc_by_tick - profit_nt) < 0.01
    match_point = abs(calc_by_point - profit_nt) < 0.01
    match_net = abs(net_pnl - profit_nt) < 0.01
    match_with_comm = abs(calc_by_point - gross_from_net) < 0.01
    
    results.append({
        'Trade': row['Trade number'],
        'Dir': direction,
        'Entry': entry,
        'Exit': exit_price,
        'Qty': qty,
        'PriceDiff': price_diff,
        'Calc_Tick': calc_by_tick,
        'Calc_Point': calc_by_point,
        'NT_Profit': profit_nt,
        'NT_Comm': comm_nt,
        'NT_Net': net_pnl,
        'Match_Point': '✓' if match_point else '',
        'Match_Tick': '✓' if match_tick else '',
        'Diff': profit_nt - calc_by_point
    })

df_results = pd.DataFrame(results)

print(f"\nANÁLISIS DE LOS PRIMEROS 20 TRADES")
print("=" * 100)
print("\nComparación de cálculos:")
print(df_results.to_string(index=False))

# Mostrar estadísticas
print(f"\n" + "=" * 100)
print("ESTADÍSTICAS DE DIFERENCIAS")
print("=" * 100)

matches_point = (df_results['Match_Point'] == '✓').sum()
matches_tick = (df_results['Match_Tick'] == '✓').sum()

print(f"\nTrades que coinciden con cálculo por PUNTO ($2.00/punto): {matches_point}/{len(df_results)}")
print(f"Trades que coinciden con cálculo por TICK ($0.50/tick): {matches_tick}/{len(df_results)}")

avg_diff = df_results['Diff'].mean()
max_diff = df_results['Diff'].max()
min_diff = df_results['Diff'].min()

print(f"\nDiferencia promedio (NT - Cálculo): ${avg_diff:,.2f}")
print(f"Diferencia máxima: ${max_diff:,.2f}")
print(f"Diferencia mínima: ${min_diff:,.2f}")

# Análisis de patrones
print(f"\n" + "=" * 100)
print("ANÁLISIS DE PATRONES")
print("=" * 100)

# Agrupar por tipo de diferencia
import warnings
warnings.filterwarnings('ignore', category=FutureWarning)
diff_ranges = df_results.groupby(pd.cut(df_results['Diff'], bins=[-200, -20, -10, -1, 1, 10, 20, 200], observed=False)).size()
print("\nDistribución de diferencias:")
print(diff_ranges.to_string())

# Ver si la diferencia es proporcional a algo
print(f"\n--- ¿La diferencia es proporcional a la cantidad? ---")
df_results['Diff_Per_Contract'] = df_results['Diff'] / df_results['Qty']
print(df_results[['Trade', 'Qty', 'Diff', 'Diff_Per_Contract']].to_string(index=False))

# Hipótesis: ¿NT incluye comisiones en "Profit"?
print(f"\n" + "=" * 100)
print("HIPÓTESIS: ¿NT INCLUYE COMISIONES EN 'PROFIT'?")
print("=" * 100)

df_results['Calc_With_Comm'] = df_results['Calc_Point'] - df_results['NT_Comm']
df_results['Match_With_Comm'] = abs(df_results['NT_Profit'] - df_results['Calc_With_Comm']) < 0.01

matches_with_comm = (df_results['Match_With_Comm']).sum()
print(f"\nTrades que coinciden si restamos comisión: {matches_with_comm}/{len(df_results)}")

if matches_with_comm > 10:
    print("\n✅ CONFIRMADO: NT reporta 'Profit' como NET PnL (ya con comisiones restadas)")
else:
    print("\n❌ No es ese el patrón")

print(f"\n" + "=" * 100)

# Mostrar ejemplo detallado del primer trade
print("\nEJEMPLO DETALLADO - TRADE #1:")
print("=" * 100)
first = df_results.iloc[0]
print(f"Dirección: {first['Dir']}")
print(f"Entry: {first['Entry']}, Exit: {first['Exit']}")
print(f"Cantidad: {first['Qty']} contratos")
print(f"Diferencia de precio: {first['PriceDiff']} puntos")
print(f"\nCálculos:")
print(f"  Por punto ($2/punto): {first['PriceDiff']} × $2 × {first['Qty']} = ${first['Calc_Point']:,.2f}")
print(f"  Por tick ($0.50/tick): {first['PriceDiff']/TICK_SIZE} × $0.50 × {first['Qty']} = ${first['Calc_Tick']:,.2f}")
print(f"\nNT reporta:")
print(f"  Profit: ${first['NT_Profit']:,.2f}")
print(f"  Commission: ${first['NT_Comm']:,.2f}")
print(f"  Net (Profit - Comm): ${first['NT_Net']:,.2f}")
print(f"\nDiferencia: ${first['Diff']:,.2f}")
print(f"Diferencia por contrato: ${first['Diff_Per_Contract']:,.2f}/contrato")

print(f"\n" + "=" * 100)
