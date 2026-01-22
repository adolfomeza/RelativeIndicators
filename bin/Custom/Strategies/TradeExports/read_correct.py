#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Leer CSV con header correcto (26 columnas)
"""

import pandas as pd

strategy_csv = r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback\MNQ_03-25.csv"

# Header correcto de 26 columnas (según línea 4938 del código C#)
col_names = ['TradeId','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice',
             'Result','GrossPnL','Commission','NetPnL','MAE','MFE','SetupName','Attempt',
             'RiskReward','DeltaEntry','DeltaDir','SessionDelta','DeltaTP1','LevelAge',
             'Quantity','ExecutionId','EntryMode','ExitStrategy','RiskModel']

# Leer saltando el header incorrecto y usando nombres correctos
df = pd.read_csv(strategy_csv, names=col_names, header=0, skiprows=1)

print("=" * 80)
print("LECTURA CORRECTA DEL CSV")
print("=" * 80)
print(f"\nTotal filas: {len(df)}")
print(f"\nPrimeras 5 filas - NetPnL:")
print(df[['TradeId', 'Result', 'GrossPnL', 'Commission', 'NetPnL']].head(10).to_string())

print(f"\n" + "=" * 80)
print("PNL TOTAL:")
print("=" * 80)
total_net = df['NetPnL'].sum()
print(f"Suma NetPnL: ${total_net:,.2f}")

# Agrupar por trade lógico
df['LogicalID'] = df['TradeId'].astype(str).str.split('.').str[0]
grouped = df.groupby('LogicalID')['NetPnL'].sum()
grouped_total = grouped.sum()

print(f"\nTRADES AGRUPADOS:")
print(f"  Total trades lógicos: {len(grouped)}")
print(f"  NetPnL agrupado: ${grouped_total:,.2f}")
