
import pandas as pd
import os

# Paths
strategy_path = r"c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback\MNQ_03-25.csv"
nt_path = r"c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback\NT_Export.csv"

def clean_money(val):
    if pd.isna(val): return 0.0
    # Remove $ and , and . (since . seems to be thousands separator in this specific NT export format)
    # Be careful: if . is decimal, we break it. 
    # But we decided the format implies NO decimal point (cents scaling).
    s = str(val).replace('$', '').replace(',', '').replace('.', '')
    try:
        return float(s)
    except:
        return 0.0

def parse_nt_date(s):
    # Format: 6/1/25 3:19:04 a. m.
    # Needs to handle "a. m." / "p. m."
    s = s.replace("a. m.", "AM").replace("p. m.", "PM")
    # Spanish locale day/month/year likely -> 6/1/25 = Jan 6th
    return pd.to_datetime(s, dayfirst=True)

def load_data():
    print(f"Loading Strategy: {strategy_path}")
    df_s = pd.read_csv(strategy_path)
    
    print(f"Loading NT: {nt_path}")
    try:
        df_nt = pd.read_csv(nt_path)
    except Exception as e:
        print(f"Error reading NT CSV: {e}")
        return None, None

    return df_s, df_nt

def analyze(df_s, df_nt):
    print("Normalizing data for matching...")
    
    # helper for strategy
    df_s['Time'] = pd.to_datetime(df_s['EntryTime'])
    
    # helper for NT
    df_nt['Time'] = df_nt['Entry time'].apply(parse_nt_date)
    
    # Cleaning NT numeric
    df_nt['ProfitNum'] = df_nt['Profit'].apply(clean_money) / 100.0
    df_nt['CommNum'] = df_nt['Commission'].apply(clean_money) / 100.0
    
    # Group by Time to handle splits
    s_grouped = df_s.groupby('Time').agg({
        'NetPnL': 'sum',
        'Quantity': 'sum',
        'PnL': 'sum',
        'Commission': 'sum',
        'ID': 'first'
    }).reset_index()
    
    nt_grouped = df_nt.groupby('Time').agg({
        'ProfitNum': 'sum', # Profit is Net in NT export
        'Qty': 'sum',
        'CommNum': 'sum',
        'Trade number': 'first'
    }).reset_index()
    
    # Strategy Totals
    s_pnl = df_s['PnL'].sum()
    s_comm = df_s['Commission'].sum()
    s_net = df_s['NetPnL'].sum()
    
    # NT Totals
    nt_net = df_nt['ProfitNum'].sum()
    nt_comm = df_nt['CommNum'].sum()
    nt_gross = nt_net + nt_comm
    
    # Differences
    diff_net = s_net - nt_net
    diff_gross = s_pnl - nt_gross
    diff_comm = s_comm - nt_comm

    # Merge on Time
    merged = pd.merge(s_grouped, nt_grouped, on='Time', how='outer', suffixes=('_S', '_NT'))
    merged['Diff'] = merged['NetPnL'].fillna(0) - merged['ProfitNum'].fillna(0)
    merged['AbsDiff'] = merged['Diff'].abs()
    
    discrepancies = merged[merged['AbsDiff'] > 5.0].sort_values('AbsDiff', ascending=False)
    
    missing_in_nt = merged[merged['ProfitNum'].isna()]
    missing_in_s = merged[merged['NetPnL'].isna()]

    # Write to file
    out_file = r"c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\playback\discrepancies_utf8.txt"
    with open(out_file, "w", encoding="utf-8") as f:
        f.write("-" * 30 + "\n")
        f.write("STRATEGY EXPORT TOTALS\n")
        f.write(f"PnL (Gross): ${s_pnl:,.2f}\n")
        f.write(f"Commission:  ${s_comm:,.2f}\n")
        f.write(f"Net PnL:     ${s_net:,.2f}\n")
        f.write(f"Trades/Rows: {len(df_s)}\n")
        f.write(f"Grouped Trades: {len(s_grouped)}\n")
        f.write("-" * 30 + "\n")

        f.write("NT EXPORT TOTALS (Assumed /100 scaling)\n")
        f.write(f"Net PnL (from Profit col): ${nt_net:,.2f}\n")
        f.write(f"Commission:                ${nt_comm:,.2f}\n")
        f.write(f"Gross PnL (Calc):          ${nt_gross:,.2f}\n")
        f.write(f"Trades/Rows:               {len(df_nt)}\n")
        f.write(f"Grouped Trades:            {len(nt_grouped)}\n")
        f.write("-" * 30 + "\n")
        
        f.write("DIFFERENCES (Strategy - NT)\n")
        f.write(f"Net PnL Diff:   ${diff_net:,.2f}\n")
        f.write(f"Gross PnL Diff: ${diff_gross:,.2f}\n")
        f.write(f"Commission Diff:${diff_comm:,.2f}\n")
        
        f.write("-" * 80 + "\n")
        f.write("TOP DISCREPANCIES (Diff > $5.00)\n")
        f.write(f"{'Time':<20} | {'S_Net':>10} | {'NT_Net':>10} | {'Diff':>10} | {'S_Qty':>5} | {'NT_Qty':>5}\n")
        f.write("-" * 80 + "\n")
        
        for _, row in discrepancies.head(50).iterrows():
            f.write(f"{str(row['Time']):<20} | {row['NetPnL']:10.2f} | {row['ProfitNum']:10.2f} | {row['Diff']:10.2f} | {row['Quantity']:5.0f} | {row['Qty']:5.0f}\n")
            
        f.write("-" * 80 + "\n")
        
        if not missing_in_nt.empty:
            f.write(f"\nWARNING: {len(missing_in_nt)} trades in Strategy but NOT in NT.\n")
            f.write(missing_in_nt[['Time', 'NetPnL']].head(20).to_string() + "\n")
            
        if not missing_in_s.empty:
            f.write(f"\nWARNING: {len(missing_in_s)} trades in NT but NOT in Strategy.\n")
            # Sort by time
            missing_in_s = missing_in_s.sort_values('Time')
            f.write(missing_in_s[['Time', 'ProfitNum', 'Qty']].to_string() + "\n")
            
            f.write(f"Missing Range: {missing_in_s['Time'].min()} to {missing_in_s['Time'].max()}\n")

    print(f"Analysis written to {out_file}")

if __name__ == "__main__":
    df_s, df_nt = load_data()
    if df_s is not None and df_nt is not None:
        analyze(df_s, df_nt)
