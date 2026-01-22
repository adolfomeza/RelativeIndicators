#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Investigación NT - Versión simplificada"""

import pandas as pd
import glob

playback_dir = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback"

nt_files = glob.glob(playback_dir + r"\NinjaTrader*.csv")
df_nt = pd.read_csv(nt_files[0], encoding='utf-8')

def parse_nt_money(val):
    s = str(val).replace('$', '').replace(',', '').strip()
    try:
        return float(s) / 100.0
    except:
        return 0.0

df_nt['Entry'] = pd.to_numeric(df_nt['Entry price'], errors='coerce')
df_nt['Exit'] = pd.to_numeric(df_nt['Exit price'], errors='coerce')
df_nt['Qty'] = pd.to_numeric(df_nt['Qty'], errors='coerce')
df_nt['Profit_NT'] = df_nt['Profit'].apply(parse_nt_money)
df_nt['Direction'] = df_nt['Market pos.']

results = []
for idx, row in df_nt.head(10).iterrows():
    # Cálculo estándar de futuros
    if row['Direction'] == 'Short':
        price_diff = row['Entry'] - row['Exit']
    else:
        price_diff = row['Exit'] - row['Entry']
    
    calc_pnl = price_diff * 2.0 * row['Qty']  # $2/punto
    
    results.append({
        'Trade': int(row['Trade number']),
        'Dir': row['Direction'][:5],
        'Entry': f"{row['Entry']:.2f}",
        'Exit': f"{row['Exit']:.2f}",
        'Qty': int(row['Qty']),
        'Diff': f"{price_diff:.2f}",
        'Calc': f"${calc_pnl:.2f}",
        'NT': f"${row['Profit_NT']:.2f}",
        'Delta': f"${row['Profit_NT'] - calc_pnl:.2f}"
    })

print("\n" + "="*90)
print("10 PRIMEROS TRADES - NT vs Cálculo Manual")
print("="*90)
df_r = pd.DataFrame(results)
print(df_r.to_string(index=False))

print("\n" + "="*90)
print("CONCLUSIÓN")
print("="*90)

# Verificar si NT ya incluye comisiones
total_nt = df_nt.head(10)['Profit_NT'].sum()
total_calc = sum([float(r['Calc'].replace('$','')) for r in results])

print(f"\nTotal NT (10 trades): ${total_nt:,.2f}")
print(f"Total Calculado: ${total_calc:,.2f}")
print(f"Diferencia: ${total_nt - total_calc:,.2f}")

print(f"\nHIPÓTESIS:")
print(f"- Si diferencia ≈ comisiones → NT resta comisiones en 'Profit'")
print(f"- Si diferencia ≈ 0 → NT reporta PnL bruto")
print(f"- Si diferencia es otra → NT usa fórmula diferente")

print("\n" + "="*90)
