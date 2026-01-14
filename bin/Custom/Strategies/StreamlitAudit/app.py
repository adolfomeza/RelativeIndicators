import streamlit as st
import pandas as pd
import plotly.express as px
import plotly.graph_objects as go
import numpy as np
import os
import calendar
import glob
import datetime
from datetime import datetime
import streamlit.components.v1 as components

# AI Engine for Quant Analysis
from ai_engine import show_ai_analysis, get_analyzer, get_usage_history, update_usage_history

# --- COMMISSION RATES (2025 NinjaTrader Brokerage) ---
# All-in rates (Commission + NFA + Exchange + Routing) per side
COMMISSION_RATES = {
    'Free (Default)': {
        'Micro': 0.91,      # MES, MNQ, M2K, MYM
        'Standard': 2.74,   # ES, NQ, YM, RTY
        'MicroCrypto': 1.56,# MBT, MET
        'Nano': 0.35,       # Nano Bitcoin
        'Commodity': 3.09,  # CL, GC, SI, HG
        'MicroCom': 0.77    # MCL, MGC, M6E
    },
    'Lifetime (Lifetime)': {
        'Micro': 0.56,
        'Standard': 2.09,
        'MicroCrypto': 0.81,
        'Nano': 0.15,
        'Commodity': 2.44, # Approx for CL
        'MicroCom': 0.42
    }
}

# --- PAGE CONFIG ---

st.set_page_config(page_title="Laboratorio de Auditoría Quant", layout="wide", page_icon="🕵️‍♂️")

# Init Session State
if 'selected_date_audit' not in st.session_state:
    st.session_state.selected_date_audit = None

# --- CUSTOM CSS ---
# --- CUSTOM CSS & THEME ---
st.markdown("""
<style>
    /* Main Background */
    .stApp {
        background-color: #0E1117;
        font-family: 'Inter', sans-serif;
    }
    
    /* Global Text */
    h1, h2, h3 {
        color: #E6E6E6 !important;
        font-weight: 700;
    }
    p, label, .stMarkdown {
        color: #B0B0B0 !important;
    }

    /* Metric Cards (Glassmorphism) */
    div[data-testid="metric-container"] {
        background-color: rgba(22, 27, 34, 0.8);
        border: 1px solid rgba(48, 54, 61, 0.5);
        padding: 20px;
        border-radius: 12px;
        box-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
        transition: transform 0.2s ease, box-shadow 0.2s ease;
    }
    div[data-testid="metric-container"]:hover {
        transform: translateY(-2px);
        box-shadow: 0 8px 12px rgba(0, 255, 153, 0.1);
        border-color: #00FF99;
    }
    div[data-testid="metric-container"] label {
        color: #8B949E !important;
        font-size: 0.9rem;
    }
    div[data-testid="metric-container"] div[data-testid="stMetricValue"] {
        color: #00FF99 !important;
        font-size: 1.8rem !important;
        text-shadow: 0 0 10px rgba(0, 255, 153, 0.3);
    }
    
    /* Buttons */
    .stButton > button {
        background: linear-gradient(45deg, #238636, #2EA043);
        color: white;
        border: none;
        border-radius: 8px;
        padding: 0.5rem 1rem;
        font-weight: 600;
        transition: all 0.3s ease;
    }
    .stButton > button:hover {
        background: linear-gradient(45deg, #2EA043, #3FB950);
        box-shadow: 0 0 15px rgba(46, 160, 67, 0.4);
    }

    /* Sidebar */
    [data-testid="stSidebar"] {
        background-color: #010409;
        border-right: 1px solid #30363D;
    }
    
    /* Tabs */
    .stTabs [data-baseweb="tab-list"] {
        gap: 24px;
        background-color: transparent;
    }
    .stTabs [data-baseweb="tab"] {
        height: 50px;
        white-space: pre-wrap;
        background-color: transparent;
        border-radius: 4px;
        color: #8B949E;
        font-weight: 600;
    }
    .stTabs [aria-selected="true"] {
        background-color: transparent;
        color: #00FF99 !important;
        border-bottom: 2px solid #00FF99;
    }
    
    /* v2.2.1: Chart Containers with rounded borders - NO SCROLLBARS */
    div[data-testid="stPlotlyChart"] {
        background-color: rgba(22, 27, 34, 0.6);
        border: 1px solid rgba(100, 110, 120, 0.7);
        border-radius: 16px;
        padding: 15px;
        margin: 10px 0;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
        overflow: hidden !important;
    }
    div[data-testid="stPlotlyChart"] > div,
    div[data-testid="stPlotlyChart"] > div > div,
    div[data-testid="stPlotlyChart"] iframe {
        overflow: hidden !important;
        max-width: 100% !important;
    }
    div[data-testid="stPlotlyChart"]:hover {
        border-color: rgba(0, 255, 153, 0.5);
        box-shadow: 0 6px 16px rgba(0, 255, 153, 0.1);
    }
    
    /* DataFrame tables with rounded borders - no scroll */
    div[data-testid="stDataFrame"] {
        background-color: rgba(22, 27, 34, 0.6);
        border: 1px solid rgba(100, 110, 120, 0.7);
        border-radius: 12px;
        padding: 10px;
        overflow: hidden !important;
    }
    div[data-testid="stDataFrame"] > div,
    div[data-testid="stDataFrame"] > div > div {
        overflow: hidden !important;
    }
    
    /* Global scrollbar hide for charts */
    div[data-testid="stPlotlyChart"] *::-webkit-scrollbar,
    div[data-testid="stDataFrame"] *::-webkit-scrollbar {
        display: none !important;
        width: 0 !important;
        height: 0 !important;
    }
    div[data-testid="stPlotlyChart"] *,
    div[data-testid="stDataFrame"] * {
        scrollbar-width: none !important;
        -ms-overflow-style: none !important;
    }
    
    /* Custom Calendar Grid overrides */
    .cal-container {
        display: grid;
        grid-template-columns: repeat(8, 1fr);
        gap: 8px;
        margin-top: 15px;
    }
    .cal-header {
        text-align: center;
        font-weight: 700;
        color: #8B949E;
        padding: 8px;
        text-transform: uppercase;
        font-size: 0.8rem;
    }
    .cal-day {
        background-color: #161B22;
        border: 1px solid #30363D;
        border-radius: 8px;
        min-height: 100px;
        padding: 10px;
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        transition: all 0.2s;
    }
    .cal-day:hover {
        border-color: #CCFF00;
        transform: scale(1.02);
        z-index: 2;
    }
    .cal-day.green { 
        background: linear-gradient(135deg, rgba(204, 255, 0, 0.2) 0%, rgba(22, 27, 34, 0) 100%);
        border-left: 3px solid #CCFF00;
    }
    .cal-day.red { 
        background: linear-gradient(135deg, rgba(218, 54, 51, 0.2) 0%, rgba(22, 27, 34, 0) 100%);
        border-left: 3px solid #FF4444; 
    }
    .cal-date { font-size: 14px; color: #E6E6E6; font-weight: bold;}
    .cal-pnl-pos { color: #CCFF00; font-size: 16px; font-weight: 800; text-shadow: 0 0 5px rgba(204,255,0,0.3);}
    .cal-pnl-neg { color: #FF4444; font-size: 16px; font-weight: 800; text-shadow: 0 0 5px rgba(255,68,68,0.3);}
    .cal-trades { font-size: 11px; color: #8B949E; margin-top: 4px; font-style: italic;}
    .cal-empty { background-color: transparent; border: none; }
    
    .cal-weekly {
        background-color: #0D1117;
        border: 1px dashed #30363D;
        border-radius: 8px;
        padding: 10px;
        display: flex;
        flex-direction: column;
        justify-content: center;
        align-items: center;
        opacity: 0.8;
    }
    .cal-weekly-title { font-size: 12px; color: #8B949E; text-transform: uppercase; font-weight: bold; margin-bottom: 5px; }
    
    /* ACCESSIBILITY OVERRIDES (Colorblind Friendly) */
    /* Info/Warning/Success/Error Boxes -> Gray Scale with Borders */
    div[data-testid="stAlert"] {
        background-color: #161B22; /* Dark Gray Background */
        color: #C9D1D9; /* Light Gray Text */
        border: 1px solid #30363D;
        border-radius: 8px;
    }
    
    /* Specific Borders for context (keeping it subtle) */
    div[data-testid="stAlert"][data-test-style="info"] { border-left: 4px solid #58A6FF; }
    div[data-testid="stAlert"][data-test-style="warning"] { border-left: 4px solid #D29922; }
    div[data-testid="stAlert"][data-test-style="success"] { border-left: 4px solid #238636; }
    div[data-testid="stAlert"][data-test-style="danger"] { border-left: 4px solid #DA3633; }
    
    /* Force text inside alerts to be Gray/White */
    div[data-testid="stAlert"] > div {
        color: #C9D1D9 !important;
    }
    div[data-testid="stAlert"] p {
        color: #C9D1D9 !important;
    }
    
</style>
""", unsafe_allow_html=True)

# --- CHART THEME HELPER ---
def apply_premium_style(fig, title=None):
    """Applies a consistent Premium/Dark/Neon theme to Plotly figures."""
    if title: fig.update_layout(title=title)
    
    fig.update_layout(
        font_family="Inter, sans-serif",
        font_size=12,
        font_color="#B0B0B0",
        title_font_size=20,
        title_font_color="#E6E6E6",
        paper_bgcolor="rgba(0,0,0,0)", # Transparent
        plot_bgcolor="rgba(0,0,0,0)",  # Transparent
        hoverlabel=dict(
            bgcolor="#161B22",
            font_size=13,
            font_family="Monospace"
        ),
        xaxis=dict(
            gridcolor="#30363D",
            showgrid=True,
            zerolinecolor="#30363D"
        ),
        yaxis=dict(
            gridcolor="#30363D",
            showgrid=True,
            zerolinecolor="#30363D"
        ),
        legend=dict(
            orientation="h",
            yanchor="bottom",
            y=1.02,
            xanchor="right",
            x=1
        ),
        margin=dict(l=40, r=40, t=60, b=40),
        height=450  # v2.2.1: Minimum height to avoid scroll
    )
    return fig


# =============================================================================
# HELPER FUNCTIONS FOR EXECUTIVE REPORT
# =============================================================================

def generate_executive_report(df):
    """Generates comprehensive executive report compiling all analyses"""
    report = ""
    
    # Header
    report += "=" * 80 + "\n"
    report += "🎯 REPORTE EJECUTIVO - TRADING ANALYSIS\n"
    report += "=" * 80 + "\n\n"
    report += f"Generado: {datetime.now().strftime('%Y-%m-%d %H:%M')}\n"
    
    if 'EntryTime' in df.columns and 'ExitTime' in df.columns:
        report += f"Período: {df['EntryTime'].min().date()} a {df['ExitTime'].max().date()}\n"
    report += f"Total Registros: {len(df)}\n\n"
    
    # Sections
    report += compile_exec_summary(df)
    report += compile_instrument_perf(df)
    report += compile_levels_perf(df)
    report += compile_filter_recommendations(df)
    report += generate_csharp_filters(df)
    report += compile_action_plan(df)
    
    # NEW: R-Ladder Analysis
    r_ladder_text, r_df = analyze_r_ladder(df, max_r=20)
    report += r_ladder_text
    
    # NEW: Scaling Out Simulation
    scaling_text, scaling_df = analyze_scaling_out(df, r_df, position_sizes=[3, 5, 10, 20])
    report += scaling_text
    
    return report, r_df, scaling_df


def compile_exec_summary(df):
    """Section 1: Executive Summary"""
    section = "=" * 80 + "\n"
    section += "1. RESUMEN EJECUTIVO\n"
    section += "=" * 80 + "\n\n"
    
    total_trades = len(df)
    net_pnl = df['PnL'].sum()
    wins = len(df[df['PnL'] > 0])
    losses = len(df[df['PnL'] <= 0])
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    
    avg_win = df[df['PnL'] > 0]['PnL'].mean() if wins > 0 else 0
    avg_loss = abs(df[df['PnL'] <= 0]['PnL'].mean()) if losses > 0 else 0
    profit_factor = (wins * avg_win) / (losses * avg_loss) if losses > 0 and avg_loss > 0 else 0
    
    section += "📊 Métricas Clave:\n"
    section += f"  - Total Trades: {total_trades}\n"
    section += f"  - PnL Neto: ${net_pnl:,.2f}\n"
    section += f"  - Win Rate: {win_rate:.1f}%\n"
    section += f"  - Profit Factor: {profit_factor:.2f}\n"
    section += f"  - Avg Win: ${avg_win:.2f} | Avg Loss: ${avg_loss:.2f}\n\n"
    
    if net_pnl > 1000 and win_rate > 40:
        verdict = "✅ RENTABLE - Sistema con edge positivo"
    elif net_pnl > 0:
        verdict = "⚠️ MARGINAL - Requiere optimización"
    else:
        verdict = "❌ PERDEDOR - Requiere revisión profunda"
    
    section += f"🎯 Veredicto Global: {verdict}\n\n"
    return section


def compile_instrument_perf(df):
    """Section 2: Analysis by Instrument"""
    section = "=" * 80 + "\n"
    section += "2. PERFORMANCE POR INSTRUMENTO\n"
    section += "=" * 80 + "\n\n"
    
    if 'Instrument' not in df.columns:
        section += "⚠️ No hay información de instrumento.\n\n"
        return section
    
    inst_stats = df.groupby('Instrument').agg({
        'PnL': ['sum', 'count'],
        'Result': lambda x: (x.str.contains('TP', na=False)).sum()
    })
    
    inst_stats.columns = ['PnL', 'Trades', 'Wins']
    inst_stats['WR'] = (inst_stats['Wins'] / inst_stats['Trades'] * 100).round(1)
    inst_stats = inst_stats.sort_values('PnL', ascending=False)
    
    for inst in inst_stats.index:
        data = inst_stats.loc[inst]
        verdict = "✅ MANTENER" if data['PnL'] > 0 else "❌ DESHABILITAR"
        section += f"{inst}:\n"
        section += f"  PnL: ${data['PnL']:,.2f} | Trades: {int(data['Trades'])} | WR: {data['WR']:.1f}% → {verdict}\n\n"
    
    return section


def compile_levels_perf(df):
    """Section 3: Global Levels Analysis"""
    section = "=" * 80 + "\n"
    section += "3. ANÁLISIS DE NIVELES\n"
    section += "=" * 80 + "\n\n"
    
    level_df = df[df['SetupName'].str.contains('Asia|Europe|USA', case=False, na=False)].copy()
    
    if level_df.empty:
        section += "⚠️ No se detectaron trades de niveles.\n\n"
        return section
    
    level_df['Zone'] = level_df['SetupName'].str.extract(r'(Asia|Europe|USA)\s*(Low|High)', expand=False).apply(lambda x: f"{x[0]} {x[1]}", axis=1)
    zone_stats = level_df.groupby('Zone')['PnL'].agg(['sum', 'count']).sort_values('sum', ascending=False)
    
    section += "🏆 TOP 5 MEJORES ZONAS:\n"
    for i, (zone, data) in enumerate(zone_stats.head(5).iterrows(), 1):
        section += f"  {i}. {zone}: ${data['sum']:,.0f} ({int(data['count'])} trades)\n"
    
    section += "\n❌ ZONAS PROBLEMÁTICAS:\n"
    bad_zones = zone_stats[zone_stats['sum'] < -100]
    if not bad_zones.empty:
        for zone, data in bad_zones.iterrows():
            section += f"  - {zone}: ${data['sum']:,.0f} → FILTRAR\n"
    else:
        section += "  ✅ No hay zonas extremadamente tóxicas\n"
    
    section += "\n"
    return section


def compile_filter_recommendations(df):
    """Section 4: Recommended Filters"""
    section = "=" * 80 + "\n"
    section += "4. FILTROS RECOMENDADOS\n"
    section += "=" * 80 + "\n\n"
    
    level_df = df[df['SetupName'].str.contains('Asia|Europe|USA', case=False, na=False)].copy()
    
    if level_df.empty:
        section += "⚠️ Datos insuficientes.\n\n"
        return section
    
    level_df['Zone'] = level_df['SetupName'].str.extract(r'(Asia|Europe|USA)\s*(Low|High)', expand=False).apply(lambda x: f"{x[0]} {x[1]}", axis=1)
    zone_pnl = level_df.groupby('Zone')['PnL'].sum()
    toxic_zones = zone_pnl[zone_pnl < -200].sort_values()
    
    section += "🔴 ZONAS A DESHABILITAR (PnL < -$200):\n"
    if not toxic_zones.empty:
        total_impact = abs(toxic_zones.sum())
        for zone, pnl in toxic_zones.items():
            section += f"  - {zone} (Pérdida: ${pnl:,.0f})\n"
        section += f"\n  💰 Impacto Estimado: +${total_impact:,.0f}\n\n"
    else:
        section += "  ✅ No hay zonas que califiquen\n\n"
    
    return section


def generate_csharp_filters(df):
    """Section 5: Generated C# Code"""
    section = "=" * 80 + "\n"
    section += "5. CÓDIGO C# GENERADO\n"
    section += "=" * 80 + "\n\n"
    section += "// Agregar a SessionLevelsStrategy.cs\n\n"
    
    level_df = df[df['SetupName'].str.contains('Asia|Europe|USA', case=False, na=False)].copy()
    
    if not level_df.empty:
        level_df['Zone'] = level_df['SetupName'].str.extract(r'(Asia|Europe|USA)\s*(Low|High)', expand=False).apply(lambda x: f"{x[0]} {x[1]}", axis=1)
        zone_pnl = level_df.groupby('Zone')['PnL'].sum()
        enabled = zone_pnl[zone_pnl > -200].index.tolist()
        
        section += "private List<string> EnabledZones = new List<string> {\n"
        for zone in enabled:
            section += f'    "{zone}",  // ${zone_pnl[zone]:,.0f}\n'
        section += "};\n\n"
    
    section += "private int MaxLevelAgeDays = 0;\n\n"
    return section


def compile_action_plan(df):
    """Section 6: Action Plan"""
    section = "=" * 80 + "\n"
    section += "6. PLAN DE ACCIÓN\n"
    section += "=" * 80 + "\n\n"
    
    section += "✅ Pasos Inmediatos:\n"
    section += "  1. Copiar código C# a SessionLevelsStrategy.cs\n"
    section += "  2. Recompilar estrategia\n"
    section += "  3. Ejecutar backtest de validación\n"
    section += "  4. Comparar PnL antes/después\n\n"
    
    section += "📊 Monitoreo:\n"
    section += "  - Ejecutar análisis semanal\n"
    section += "  - Ajustar filtros según nuevos datos\n\n"
    
    section += "⚠️ Advertencias:\n"
    section += "  - Validar en forward test\n"
    section += "  - Sample mínimo: 30 trades\n\n"
    
    section += "=" * 80 + "\n"
    section += "FIN DEL REPORTE\n"
    section += "=" * 80 + "\n"
    
    return section


def analyze_r_ladder(df, max_r=20):
    """
    Analiza cuántos trades alcanzaron cada nivel R (1R, 2R, ..., max_r R).

    Args:
        df: DataFrame con columnas 'MAE', 'MFE', 'PnL', 'Direction'
        max_r: Nivel máximo de R a analizar (default: 20)

    Returns:
        tuple: (section_text: str, r_df: DataFrame or None)
    """
    # v1.15.21: Ensure max_r is integer
    max_r = int(max_r)

    section = "=" * 80 + "\n"
    section += "7. ANÁLISIS MFE R-LADDER (1R → 20R)\n"
    section += "=" * 80 + "\n\n"

    # Validar que tenemos los datos necesarios
    if 'MAE' not in df.columns or 'MFE' not in df.columns:
        section += "⚠️ ADVERTENCIA: No se encontraron columnas MAE/MFE en el CSV.\n"
        section += "   Ejecuta un backtest reciente para generar estos datos.\n\n"
        return section, None

    # Filtrar datos válidos
    df_copy = df.copy()
    df_copy = df_copy.dropna(subset=['MAE', 'MFE'])

    # Convertir MAE a valores absolutos (puede ser negativo en el CSV)
    df_copy['MAE'] = df_copy['MAE'].abs()
    df_copy = df_copy[df_copy['MAE'] > 0]  # Evitar división por cero

    if len(df_copy) == 0:
        section += "⚠️ No hay datos válidos para análisis (MAE = 0 o NaN).\n\n"
        return section, None

    # Calcular MFE en términos de R
    # R = MFE / MAE (cuántas veces el riesgo inicial capturamos como ganancia)
    df_copy['MFE_R'] = df_copy['MFE'] / df_copy['MAE']

    # Crear tabla de análisis
    r_data = []
    total_trades = len(df_copy)
    avg_risk = df_copy['MAE'].mean()

    for r_level in range(1, max_r + 1):
        # Trades que alcanzaron este nivel R
        reached = df_copy[df_copy['MFE_R'] >= r_level]
        count_reached = len(reached)
        percent_reached = (count_reached / total_trades * 100) if total_trades > 0 else 0
        
        # PnL potencial si todos los trades que alcanzaron este nivel
        # hubieran salido exactamente en r_level
        potential_pnl = count_reached * r_level * avg_risk
        
        r_data.append({
            'R_Level': f"{r_level}R",
            'R_Numeric': r_level,
            'Trades_Reached': count_reached,
            'Percent_Reached': percent_reached,
            'Potential_PnL': potential_pnl,
        })
    
    r_df = pd.DataFrame(r_data)
    r_df['Cumulative_PnL'] = r_df['Potential_PnL'].cumsum()
    
    # Generar reporte de texto
    section += "📊 DISTRIBUCIÓN DE ALCANCE POR NIVEL R\n"
    section += "-" * 80 + "\n"
    section += f"{'R Level':<10} {'Alcanzado':<12} {'% Total':<12} {'PnL Potencial':<18} {'PnL Acum.':<15}\n"
    section += "-" * 80 + "\n"
    
    for _, row in r_df.iterrows():
        section += f"{row['R_Level']:<10} "
        section += f"{row['Trades_Reached']:<12} "
        section += f"{row['Percent_Reached']:<12.1f}% "
        section += f"${row['Potential_PnL']:<17,.0f} "
        section += f"${row['Cumulative_PnL']:<14,.0f}\n"
    
    section += "\n"
    
    # Análisis de "punto dulce"
    # Filtrar solo los primeros 10R para evitar outliers
    r_df_filtered = r_df[r_df['R_Numeric'] <= 10].copy()
    
    # Buscar el nivel R con mejor balance: alto % alcance + alto PnL incremental
    r_df_filtered['Score'] = r_df_filtered['Percent_Reached'] * r_df_filtered['Potential_PnL'] / 10000
    
    if len(r_df_filtered) > 0:
        best_r_idx = r_df_filtered['Score'].idxmax()
        best_r = r_df_filtered.loc[best_r_idx]
        
        section += "💡 RECOMENDACIONES DE TAKE PROFIT\n"
        section += "-" * 80 + "\n"
        
        # TP1: Buscar nivel con >70% de alcance
        high_prob = r_df[r_df['Percent_Reached'] >= 70].tail(1)
        if not high_prob.empty:
            tp1_r = high_prob.iloc[0]
            section += f"✅ TP1 Sugerido: {tp1_r['R_Level']} (Probabilidad Alta)\n"
            section += f"   → {tp1_r['Percent_Reached']:.1f}% de trades alcanzan este nivel\n\n"
        else:
            section += f"✅ TP1 Sugerido: 2R (Estándar)\n"
            tp1_percent = r_df[r_df['R_Level'] == '2R']['Percent_Reached'].values[0] if '2R' in r_df['R_Level'].values else 0
            section += f"   → {tp1_percent:.1f}% de trades alcanzan este nivel\n\n"
        
        section += f"✅ TP2 Sugerido: {best_r['R_Level']} (Punto Dulce)\n"
        section += f"   → Balance óptimo entre probabilidad ({best_r['Percent_Reached']:.1f}%) y ganancia\n\n"
    
    # Identificar nivel donde menos del 10% alcanza
    low_prob = r_df[r_df['Percent_Reached'] < 10].head(1)
    if not low_prob.empty:
        section += f"⚠️ Niveles >{low_prob.iloc[0]['R_Level']}: Menos del 10% alcanza\n"
        section += f"   → No recomendado usar como TP fijo\n\n"
    
    section += "\n"
    return section, r_df


def plot_r_ladder_chart(r_df):
    """
    Crea gráfico de cascada mostrando % de alcance y PnL potencial por nivel R.
    
    Args:
        r_df: DataFrame retornado por analyze_r_ladder()
    
    Returns:
        Plotly figure or None
    """
    if r_df is None or r_df.empty:
        return None
    
    fig = go.Figure()
    
    # Barra: Porcentaje de alcance
    fig.add_trace(go.Bar(
        x=r_df['R_Level'],
        y=r_df['Percent_Reached'],
        name='% Alcanzado',
        marker_color='#2EA043',
        yaxis='y',
        text=r_df['Percent_Reached'].apply(lambda x: f"{x:.1f}%"),
        textposition='outside'
    ))
    
    # Línea: PnL Acumulado
    fig.add_trace(go.Scatter(
        x=r_df['R_Level'],
        y=r_df['Cumulative_PnL'],
        name='PnL Acumulado',
        mode='lines+markers',
        marker_color='#00D9FF',
        line=dict(width=3),
        yaxis='y2'
    ))
    
    fig.update_layout(
        title="R-Ladder Analysis: Alcance vs PnL Potencial",
        xaxis_title="Nivel R",
        yaxis=dict(
            title=dict(
                text="% de Trades que Alcanzan",
                font=dict(color="#2EA043")
            ),
            tickfont=dict(color="#2EA043"),
            range=[0, 105]
        ),
        yaxis2=dict(
            title=dict(
                text="PnL Acumulado ($)",
                font=dict(color="#00D9FF")
            ),
            tickfont=dict(color="#00D9FF"),
            overlaying='y',
            side='right'
        ),
        hovermode='x unified',
        showlegend=True
    )
    
    apply_premium_style(fig)
    return fig


def analyze_scaling_out(df, r_df, position_sizes=[3, 5, 10, 20]):
    """
    Simula diferentes estrategias de scaling out distribuyendo contratos
    uniformemente entre niveles R.
    
    Args:
        df: DataFrame original con trades
        r_df: DataFrame de R-Ladder (output de analyze_r_ladder)
        position_sizes: Lista de tamaños de posición a simular
    
    Returns:
        tuple: (section_text: str, comparison_df: DataFrame)
    """
    section = "=" * 80 + "\n"
    section += "8. SIMULACIÓN SCALING OUT DINÁMICO\n"
    section += "=" * 80 + "\n\n"
    
    # Validar que tenemos datos
    if r_df is None or r_df.empty:
        section += "⚠️ No hay datos de R-Ladder para simular scaling out.\n\n"
        return section, None
    
    if 'MAE' not in df.columns or 'MFE' not in df.columns:
        section += "⚠️ Requiere columnas MAE/MFE para simulación.\n\n"
        return section, None
    
    # Preparar datos
    df_copy = df.copy()
    df_copy = df_copy.dropna(subset=['MAE', 'MFE'])
    
    # Convertir MAE a valores absolutos (puede ser negativo en el CSV)
    df_copy['MAE'] = df_copy['MAE'].abs()
    df_copy = df_copy[df_copy['MAE'] > 0]
    
    if len(df_copy) == 0:
        section += "⚠️ No hay datos válidos.\n\n"
        return section, None
    
    df_copy['MFE_R'] = df_copy['MFE'] / df_copy['MAE']
    avg_risk = df_copy['MAE'].mean()
    total_trades = len(df_copy)
    
    # Calcular PnL del sistema actual (baseline)
    current_pnl = df_copy['PnL'].sum()
    
    section += "📊 COMPARACIÓN DE ESTRATEGIAS DE SALIDA\n"
    section += "-" * 80 + "\n\n"
    
    # Simular diferentes configuraciones
    results = []
    
    for n_contracts in position_sizes:
        # v1.15.21: Ensure n_contracts is integer to avoid 'float' object cannot be interpreted as an integer error
        n_contracts = int(n_contracts)

        # Determinar en qué niveles R salir
        if n_contracts <= 20:
            # Distribuir uniformemente: 1 contrato cada (20/n_contracts) niveles R
            step = 20 / n_contracts
            exit_levels = [int((i + 1) * step) for i in range(n_contracts)]
        else:
            # Si hay más de 20 contratos, saturamos en 20R
            exit_levels = list(range(1, 21))
            # Distribuir excedente proporcionalmente
            contracts_per_level = [1] * 20
            remaining = int(n_contracts - 20)  # v1.15.21: Ensure remaining is integer
            for i in range(remaining):
                contracts_per_level[i % 20] += 1
        
        # Calcular PnL total para esta estrategia
        total_pnl = 0
        total_contracts_exited = 0
        
        for trade_idx, trade in df_copy.iterrows():
            trade_mfe_r = trade['MFE_R']
            trade_risk = trade['MAE']
            
            # Para este trade, ver cuántos contratos salen en cada nivel
            for level_idx, r_level in enumerate(exit_levels):
                if trade_mfe_r >= r_level:
                    # Este contrato sale en este nivel R
                    if n_contracts <= 20:
                        contracts = 1
                    else:
                        contracts = contracts_per_level[level_idx] if level_idx < len(contracts_per_level) else 1
                    
                    pnl_per_contract = r_level * trade_risk
                    total_pnl += pnl_per_contract * contracts
                    total_contracts_exited += contracts
                else:
                    # Si no alcanzó este nivel, los contratos restantes salen en SL
                    if n_contracts <= 20:
                        remaining_contracts = n_contracts - level_idx
                    else:
                        remaining_contracts = sum(contracts_per_level[level_idx:])
                    
                    # SL = -1R por contrato
                    total_pnl += (-trade_risk) * remaining_contracts
                    total_contracts_exited += remaining_contracts
                    break
            else:
                # Si el trade alcanzó todos los niveles, todos los contratos salieron
                pass
        
        # Calcular métricas
        avg_pnl_per_trade = total_pnl / total_trades if total_trades > 0 else 0
        avg_r_exit = avg_pnl_per_trade / avg_risk if avg_risk > 0 else 0
        
        results.append({
            'Strategy': f"{n_contracts} Contratos",
            'Exit_Levels': len(exit_levels),
            'Total_PnL': total_pnl,
            'Avg_R_Exit': avg_r_exit,
            'vs_Current': total_pnl - current_pnl
        })
    
    # Agregar sistema actual como referencia
    avg_current_r = (current_pnl / total_trades) / avg_risk if avg_risk > 0 else 0
    results.append({
        'Strategy': 'Sistema Actual (TP1/TP2)',
        'Exit_Levels': 2,  # Asumiendo TP1 y TP2
        'Total_PnL': current_pnl,
        'Avg_R_Exit': avg_current_r,
        'vs_Current': 0
    })
    
    comparison_df = pd.DataFrame(results)
    
    # Mostrar tabla
    section += f"{'Estrategia':<30} {'Niveles':<10} {'PnL Total':<15} {'Avg R':<10} {'vs Actual':<15}\n"
    section += "-" * 80 + "\n"
    
    for _, row in comparison_df.iterrows():
        marker = "⭐ " if row['vs_Current'] == max(comparison_df['vs_Current']) and row['vs_Current'] > 0 else "   "
        section += f"{marker}{row['Strategy']:<28} {row['Exit_Levels']:<10} "
        section += f"${row['Total_PnL']:<14,.0f} {row['Avg_R_Exit']:<10.2f} "
        
        if row['vs_Current'] > 0:
            section += f"+${row['vs_Current']:,.0f}\n"
        elif row['vs_Current'] < 0:
            section += f"-${abs(row['vs_Current']):,.0f}\n"
        else:
            section += f"(baseline)\n"
    
    section += "\n"
    
    # Encontrar mejor estrategia
    best = comparison_df.loc[comparison_df['Total_PnL'].idxmax()]
    
    section += "💡 RECOMENDACIONES\n"
    section += "-" * 80 + "\n"
    
    if best['Strategy'] == 'Sistema Actual (TP1/TP2)':
        section += "✅ Tu sistema actual (TP1/TP2) YA ES ÓPTIMO\n"
        section += "   → No se recomienda cambiar a scaling out uniforme\n\n"
    else:
        improvement = best['vs_Current']
        section += f"🎯 Mejor Estrategia: {best['Strategy']} ({best['Exit_Levels']} niveles)\n"
        section += f"   → Mejora estimada: +${improvement:,.0f} sobre sistema actual\n"
        section += f"   → Salida promedio: {best['Avg_R_Exit']:.2f}R\n\n"
        
        # Detallar niveles de salida
        n_best = int(best['Strategy'].split()[0])
        if n_best <= 20:
            step = 20 / n_best
            # v1.15.21: Ensure n_best is integer for range()
            levels = [int((i + 1) * step) for i in range(int(n_best))]
            section += f"   📋 Niveles de Salida Sugeridos:\n"
            for i, level in enumerate(levels, 1):
                section += f"      TP{i}: {level}R (1 contrato)\n"
    
    section += "\n⚠️ NOTA: Esta es una simulación teórica. En práctica real:\n"
    section += "   - Slippage puede reducir PnL\n"
    section += "   - Comisiones aumentan con más órdenes\n"
    section += "   - Gestión de múltiples salidas es más compleja\n\n"
    
    section += "\n"
    return section, comparison_df


# --- 1. DATA LOADING & PRE-PROCESSING (CLUSTERING) ---
# v2.1: Updated to support new CSV format with Commission, NetPnL, Attempt, RiskReward
@st.cache_data
def load_and_process_data(target_path, license_tier='Free (Default)'):
    # V_MULTI: Support for Glob Patterns (e.g. backtest_log_*.csv)
    files_to_load = []
    
    if "*" in target_path:
        files_to_load = glob.glob(target_path)
    elif os.path.exists(target_path):
        files_to_load = [target_path]
        
    if not files_to_load:
        return None
        
    all_dfs = []
    
    for filepath in files_to_load:
        try:
            # 1. Detect Header
            with open(filepath, 'r') as f:
                first_line = f.readline().strip()
                
            has_header = first_line.startswith("ID") or first_line.startswith('"ID"')
            
            # v2.1: Updated column names to match new CSV format
            # New format: ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,Commission,NetPnL,MAE,MFE,Setup,Attempt,RiskReward
            col_names_new = ['ID','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice','Result','GrossPnL','Commission','NetPnL','MAE','MFE','SetupName','Attempt','RiskReward']
            # Legacy format fallback
            col_names_legacy = ['ID','Instrument','EntryTime','Type','EntryPrice','ExitTime','ExitPrice','Result','PnL','SetupName','MAE','MFE','Account']
            
            if has_header:
                df_temp = pd.read_csv(filepath, on_bad_lines='skip', engine='python')
            else:
                # Try new format first based on column count
                df_temp = pd.read_csv(filepath, names=col_names_new, header=None, on_bad_lines='skip', engine='python')
                
            # Sanitize headers
            df_temp.columns = df_temp.columns.str.strip()
            
            # v2.1: Normalize column names for compatibility
            # Rename 'Setup' to 'SetupName' if using new format
            if 'Setup' in df_temp.columns and 'SetupName' not in df_temp.columns:
                df_temp.rename(columns={'Setup': 'SetupName'}, inplace=True)
            
            # v2.1: Use NetPnL if available, fallback to PnL
            if 'NetPnL' in df_temp.columns:
                df_temp['PnL'] = pd.to_numeric(df_temp['NetPnL'], errors='coerce')
                if 'GrossPnL' not in df_temp.columns and 'PnL' in df_temp.columns:
                    # Original PnL column was Gross
                    pass
            elif 'PnL' not in df_temp.columns and 'GrossPnL' in df_temp.columns:
                df_temp['PnL'] = pd.to_numeric(df_temp['GrossPnL'], errors='coerce')
            
            # v2.1: Ensure new columns exist with defaults
            if 'Attempt' not in df_temp.columns:
                df_temp['Attempt'] = 1
            else:
                df_temp['Attempt'] = pd.to_numeric(df_temp['Attempt'], errors='coerce').fillna(1).astype(int)
                
            if 'RiskReward' not in df_temp.columns:
                df_temp['RiskReward'] = 0.0
            else:
                df_temp['RiskReward'] = pd.to_numeric(df_temp['RiskReward'], errors='coerce').fillna(0.0)
                
            if 'Commission' not in df_temp.columns:
                df_temp['Commission'] = 0.0
            else:
                df_temp['Commission'] = pd.to_numeric(df_temp['Commission'], errors='coerce').fillna(0.0)
            
            # Validation
            if 'EntryTime' in df_temp.columns:
                 all_dfs.append(df_temp)
                 
        except Exception as e:
            # st.error(f"Skipping file {filepath}: {e}")
            continue

    if not all_dfs:
        return None
        
    # Combine
    df = pd.concat(all_dfs, ignore_index=True)

    # v1.14.14: Deduplicate trades based on unique keys
    # Fix for inconsistent backtest results (User Re-Runs)
    if not df.empty:
        # PnL can vary slightly due to floating point, so don't use it for deduplication key.
        # ID is usually reset on each backtest run (1, 2, 3...)
        # Instrument + EntryTime + ID should be unique enough.
        # First ensure dates are parsed for reliable comparison
        if 'EntryTime' in df.columns:
             df['EntryTime'] = pd.to_datetime(df['EntryTime'], format='mixed', errors='coerce')
        if 'ExitTime' in df.columns:
             df['ExitTime'] = pd.to_datetime(df['ExitTime'], format='mixed', errors='coerce')

        # Drop exact duplicates first (row-wise)
        df.drop_duplicates(inplace=True)

        # Then drop logical duplicates (same trade ID at same time)
        # Keep 'last' assuming latest run is most relevant
        subset_cols = ['Instrument', 'EntryTime']
        if 'ID' in df.columns:
            subset_cols.append('ID')
        
        duplicates_count = df.duplicated(subset=subset_cols).sum()
        if duplicates_count > 0:
            # st.warning(f"Se eliminaron {duplicates_count} trades duplicados (mismas ejecuciones).")
            df.drop_duplicates(subset=subset_cols, keep='last', inplace=True)
        
        # v1.14.15: Apply Dynamic Commissions
        # Recalculate NetPnL based on selected License Tier
        rates = COMMISSION_RATES[license_tier]
        
        def calculate_commission(row):
            inst = str(row['Instrument']).upper()
            rate = rates['Standard'] # Default
            
            # Logic to detect type
            if inst.startswith('M') and not inst.startswith('MY'): # Micro general
                if 'MBT' in inst or 'MET' in inst: rate = rates['MicroCrypto']
                elif 'MCL' in inst or 'MGC' in inst or 'MHG' in inst: rate = rates['MicroCom']
                else: rate = rates['Micro']
            elif inst.startswith('MYM') or inst.startswith('M2K'): # Explicit Micros
                rate = rates['Micro']
            elif inst in ['CL', 'GC', 'SI', 'HG', '6E', '6B', '6J']: # Commodities/Currencies
                rate = rates['Commodity']
            
            return rate * 2 # Round trip
            
        # Apply
        df['Commission'] = df.apply(calculate_commission, axis=1)
        
        # Recalculate Net PnL
        # Note: 'PnL' in CSV is typically Gross if explicitly exported as such, or Net if strategy did it.
        # But we want to OVERWRITE the strategy's static commission.
        # So we reconstruct Gross from PnL + OldCommission (if exists) or just treat PnL as Gross if Commission was 0
        
        # Safest way: Assume 'GrossPnL' exists (we checked load logic). If not, derive it.
        if 'GrossPnL' not in df.columns:
             # Fallback: Assume current PnL is Gross for safety or try to reverse
             df['GrossPnL'] = df['PnL'] 
        
        df['NetPnL'] = df['GrossPnL'] - df['Commission']
        # Update main PnL column to be Net for analysis
        df['PnL'] = df['NetPnL']

    # Basic Cleaning
    try:
        # Remove currency symbols if present
        cols_to_clean = ['EntryPrice', 'ExitPrice', 'PnL', 'MAE', 'MFE', 'RiskReward', 'Commission']
        for col in cols_to_clean:
            if col in df.columns and df[col].dtype == object:
                df[col] = df[col].astype(str).str.replace('$', '').str.replace('€', '').str.replace(',', '')
                df[col] = pd.to_numeric(df[col], errors='coerce')
        
        # Parse Dates
        df['EntryTime'] = pd.to_datetime(df['EntryTime'], format='mixed')
        df['ExitTime'] = pd.to_datetime(df['ExitTime'], format='mixed')
        
        # Sort by Time to ensure ranking works even if CSV is mixed
        df = df.sort_values(by=['EntryTime', 'Instrument'])

        # v1.15.21: Validate DataFrame is not empty after cleaning
        if df.empty or len(df) == 0:
            return None

        # --- SESSION AGGRESSOR LOGIC ---
        # Identify "Who" (Time Session) is breaking the level
        # v2.2.2: DST-aware for USA session (9:30 summer, 10:30 winter)
        
        def is_dst(dt):
            """Check if date is in US Daylight Saving Time (March-November)"""
            # DST in USA: 2nd Sunday of March to 1st Sunday of November
            year = dt.year
            
            # March: 2nd Sunday (day 8-14)
            march_start = None
            for day in range(8, 15):
                if datetime(year, 3, day).weekday() == 6:  # Sunday = 6
                    march_start = datetime(year, 3, day, 2, 0)
                    break
            
            # November: 1st Sunday (day 1-7)
            nov_end = None
            for day in range(1, 8):
                if datetime(year, 11, day).weekday() == 6:
                    nov_end = datetime(year, 11, day, 2, 0)
                    break
            
            if march_start and nov_end:
                return march_start <= dt.replace(tzinfo=None) < nov_end
            return False  # Default to winter if can't determine
        
        def get_aggressor(dt):
            h = dt.hour
            m = dt.minute
            total_minutes = h * 60 + m
            
            # v2.2.2: USA start depends on DST
            # Summer (DST): 9:30 = 570 min
            # Winter: 10:30 = 630 min
            usa_start = 570 if is_dst(dt) else 630
            
            # Asia: 18:00 (1080m) to 02:30 (150m) -> encompassing midnight
            if total_minutes >= 1080 or total_minutes < 150:
                return 'Asia'
            # Europe: 02:30 (150m) to USA start
            elif 150 <= total_minutes < usa_start:
                return 'Europe'
            # USA: 9:30/10:30 to 18:00 (1080m)
            elif usa_start <= total_minutes < 1080:
                return 'USA'
            else:
                return 'USA'  # Fallback

        df['Aggressor'] = df['EntryTime'].apply(get_aggressor)
        
        # --- CLUSTERING LOGIC (The "Quant" Step) ---
        # Group by Entry characteristics to identify the Logical Trade
        # A "Trade" is defined by same Instrument, EntryTime and Direction (Type)
        # OLD: df['Trade_Clust_ID'] = df.groupby(['Instrument', 'EntryTime', 'Type']).ngroup()
        
        # v1.14.16: Clustering by Strategy ID (Parent)
        # Format: 105, 105.1, 105.2 -> Parent is 105
        # This matches NinjaTrader's "Trades" view exactly
        if 'ID' in df.columns:
            # Cast to string, split by dot, take first part
            df['ParentID'] = df['ID'].astype(str).apply(lambda x: x.split('.')[0])
            df['Trade_Clust_ID'] = df['ParentID']
        else:
             # Fallback for legacy CSVs without ID column
             df['Trade_Clust_ID'] = df.groupby(['Instrument', 'EntryTime', 'Type']).ngroup()
        
        # v2.2: Extract Exit Tier from Result name (TP1, TP2, TP3... or SL)
        # Instead of ranking by exit time, use the actual TP number
        import re
        def extract_tier(result_name):
            """Extract tier number from result name like 'TP1_Long', 'SL_Short', etc."""
            if pd.isna(result_name):
                return 'Unknown'
            result_upper = str(result_name).upper()
            
            # Match TP followed by number (TP1, TP2, TP3, etc.)
            tp_match = re.search(r'TP(\d+)', result_upper)
            if tp_match:
                return f"TP{tp_match.group(1)}"
            
            # Check for SL/Stop Loss
            if 'SL' in result_upper or 'STOP' in result_upper or 'LOSS' in result_upper:
                return 'SL'
            
            # v2.2.1: Classify Emergency and Close exits
            if 'EMERGENCY' in result_upper or 'CLOSE' in result_upper or 'EXIT' in result_upper:
                return 'Emergency'
            
            # v2.2.1: Check for WIN without TP number (generic win)
            if 'WIN' in result_upper:
                return 'TP1'  # Treat generic wins as TP1
            
            # Fallback: show actual result name instead of 'Other'
            result_str = str(result_name)
            return result_str[:15] if len(result_str) > 15 else result_str
        
        df['Exit_Tier'] = df['Result'].apply(extract_tier)
        
        # Keep Exit_Rank for backwards compatibility (numeric for charts)
        # TP1=1, TP2=2, TP3=3, ..., SL=0, Other=-1
        def tier_to_rank(tier):
            if tier.startswith('TP'):
                try:
                    return int(tier[2:])
                except:
                    return 99
            elif tier == 'SL':
                return 0
            else:
                return -1
        
        df['Exit_Rank'] = df['Exit_Tier'].apply(tier_to_rank)
        
        # Calculate Max Tier per trade (for context)
        df['Max_Rank'] = df.groupby('Trade_Clust_ID')['Exit_Rank'].transform('max')
        
    except Exception as e:
        st.error(f"Data Processing Error: {e}")
        return None
        
    return df

@st.cache_data
def load_market_data(instrument, date_obj):
    """Loads OHLC data from matching Strategy Export file."""
    try:
        # Clean Instrument Name to match Strategy Logic
        safe_instr = instrument.replace(" ", "_").replace("/", "-")
        date_str = date_obj.strftime("%Y-%m-%d")
        
        filename = f"{safe_instr}_{date_str}.csv"
        path = os.path.join(r"C:\Users\prueba\Documents\NinjaTrader 8\MarketData_Exports", filename)
        
        if not os.path.exists(path):
            return None
            
        # Parse CSV with Robustness
        # Context: Schema changed from 7 cols (OHLCV) to 10 cols (OHLCV + Context).
        # Existing files might have mixed rows, causing C ParserError.
        expected_cols = ['Date','Time','Open','High','Low','Close','Volume','HighVWAP','LowVWAP','LevelPrice']
        
        try:
            df_ohlc = pd.read_csv(path)
        except pd.errors.ParserError:
            # Fallback for Mixed Files (Ragged rows)
            # engine='python' handles lines with fewer columns by filling NaN
            try:
                df_ohlc = pd.read_csv(path, names=expected_cols, header=0, engine='python')
            except Exception as e:
                st.warning(f"⚠️ Archivo corrupto (borrar carpeta MarketData_Exports): {filename}")
                return None

        # Normalize Columns if old file (pad with 0/NaN)
        for col in ['HighVWAP','LowVWAP','LevelPrice']:
             if col not in df_ohlc.columns:
                 df_ohlc[col] = 0

        # Combine Date + Time
        # Strategy writes: Date, Time (HH:mm:ss)
        df_ohlc['Datetime'] = pd.to_datetime(df_ohlc['Date'] + ' ' + df_ohlc['Time'])
        
        return df_ohlc
    except Exception as e:
        st.error(f"Error loading OHLC: {e}")
        return None

# --- 2. SIDEBAR FILTERS ---
st.sidebar.title("🎛️ Panel de Control")

# API Status Indicator
analyzer = get_analyzer()
if analyzer is not None:
    # v1.14.15: Dynamic Commission Selector (Placed at top for visibility)
    st.sidebar.subheader("💰 Comisiones")
    license_tier = st.sidebar.selectbox(
        "Licencia NinjaTrader",
        options=list(COMMISSION_RATES.keys()),
        index=0,
        help="¡Simula tu ahorro! Cambia entre licencia Free y Lifetime para recalcular el PnL Neto."
    )
    st.sidebar.markdown("---")
    
    st.sidebar.success("🤖 IA: Activa")
    
    # AI Cost Metrics (Persistent + Session)
    if 'ai_usage_stats' in st.session_state:
        st.sidebar.markdown("---")
        
        # Datos Sesión
        s_cost = st.session_state.ai_usage_stats.get('cost', 0.0)
        s_tokens = st.session_state.ai_usage_stats.get('tokens', 0)
        
        # Datos Históricos (Archivo)
        history = get_usage_history()
        t_cost = history.get('total_cost', 0.0)
        t_tokens = history.get('total_tokens', 0)
        
        # Si la sesión tiene datos frescos no guardados aún (por delay de cache), sumarlos visualmente?
        # Nota: update_usage_history ya guarda al disco. Así que history debería tener todo.
        # Pero ai_engine guarda SOLO lo de charts. Chat y Reporte manual guardan abajo. 
        # Asumiremos que history es la fuente de verdad total.
        
        st.sidebar.markdown("### 📊 Consumo IA")
        
        # Métricas Sesión Actual
        st.sidebar.caption("🟢 Sesión Actual")
        c1, c2 = st.sidebar.columns(2)
        c1.metric("Costo", f"${s_cost:.4f}")
        c2.metric("Tokens", f"{s_tokens}")
        
        # Métricas Históricas
        st.sidebar.caption("📚 Total Histórico")
        c3, c4 = st.sidebar.columns(2)
        c3.metric("Total $", f"${t_cost:.4f}")
        c4.metric("Total Tokens", f"{t_tokens:,}")
else:
    st.sidebar.warning("⚠️ IA: Sin API Key (.env)")


# Force Reset Logic
if st.sidebar.button("🗑️ BORRAR (SOLO BACKTEST)"):
    try:
        base_dir = r"C:\Users\prueba\Documents\NinjaTrader 8"
        # V_MULTI: Delete all backtest logs
        pattern = os.path.join(base_dir, "backtest_log_*.csv")
        files_to_del = glob.glob(pattern)
        
        for f_del in files_to_del:
            try: os.remove(f_del)
            except: pass
            
        # Legacy cleanup
        p1 = os.path.join(base_dir, "backtest_log.csv")
        if os.path.exists(p1): os.remove(p1)
        
        p2 = os.path.join(base_dir, "TradeAnalyzer", "backtest_data.js")
        if os.path.exists(p2): os.remove(p2)
        
        st.sidebar.success(f"Archivos borrados ({len(files_to_del)}). Ejecuta Backtest de nuevo.")
        st.cache_data.clear()
    except Exception as e:
        st.sidebar.error(f"Error borrando: {e}")

# Data Source Selector (v2.0: Separated by execution context)
# Sticky Selection via Query Params
qp_source = st.query_params.get("src", "backtest")
idx_source = {"backtest": 0, "playback": 1, "DEMO": 2}.get(qp_source, 0)

def on_src_change():
    # v1.15.21: Check if key exists before accessing to prevent AttributeError on first run
    if "data_source_persist" in st.session_state:
        val = st.session_state.data_source_persist
        source_map = {
            "📊 Backtest (Strategy Analyzer)": "backtest",
            "⏪ Playback (Market Replay)": "playback",
            "📁 DEMO": "DEMO"
        }
        st.query_params["src"] = source_map.get(val, "backtest")

data_source = st.sidebar.radio(
    "Fuente de Datos", 
    [
        "📊 Backtest (Strategy Analyzer)",
        "⏪ Playback (Market Replay)",
        "📁 DEMO"
    ], 
    index=idx_source,
    key="data_source_persist",
    on_change=on_src_change
)

# Base directory for strategies folder (portable)
strategies_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
trade_exports_dir = os.path.join(strategies_dir, "TradeExports")

if data_source == "📊 Backtest (Strategy Analyzer)":
    default_path = os.path.join(trade_exports_dir, "backtest", "*.csv")
elif data_source == "⏪ Playback (Market Replay)":
    default_path = os.path.join(trade_exports_dir, "playback", "*.csv")
elif data_source == "📁 DEMO":
    # v2.2.3: Support dynamic account folders (e.g. DEMO123456)
    default_path = os.path.join(trade_exports_dir, "DEMO*", "*.csv")



data_path = st.sidebar.text_input("Ruta CSV", default_path)

if st.sidebar.button("Recargar Datos"):
    st.cache_data.clear()

# Debug: Raw Data Inspector
with st.sidebar.expander("🔍 Inspector de Datos Crudos"):
    # V_MULTI: Only inspect if specific file
    if "*" not in data_path and os.path.exists(data_path):
        try:
            with open(data_path, 'r') as f:
                head = [next(f) for _ in range(5)]
            st.code("".join(head), language="csv")
            st.markdown("Confirma aquí si la fecha ya viene como 2025 desde Ninja.")
        except:
            st.error("No se puede leer el archivo.")

df_raw = load_and_process_data(data_path, license_tier)

if df_raw is None:
    st.warning("⚠️ Esperando datos. Por favor ejecuta un Backtest en NinjaTrader primero.")
    st.stop()

# Interactive Filters
instruments = ['Todos'] + list(df_raw['Instrument'].unique())
selected_inst = st.sidebar.selectbox("Instrumento", instruments)

setups = ['Todos'] + list(df_raw['SetupName'].unique())
selected_setup = st.sidebar.selectbox("Nombre del Setup", setups)

# Date Filter
# V_FIX: Drop NaT rows to prevent crashes
df_raw = df_raw.dropna(subset=['EntryTime'])

if df_raw.empty:
    st.warning("⚠️ Data loaded but contains no valid dates.")
    st.stop()

min_date = df_raw['EntryTime'].min().date()
max_date = df_raw['EntryTime'].max().date()

# Safety: Ensure min <= max (basic logic, but NaT could mess it up)
if min_date > max_date:
    min_date = max_date

try:
    date_range = st.sidebar.date_input(
        "Rango de Fechas",
        value=(min_date, max_date),
        min_value=min_date,
        max_value=max_date,
        format="DD/MM/YYYY"
    )
except Exception as e:
    st.error(f"Date Error: {e}")
    date_range = (min_date, max_date)

# Commission Input
commission = st.sidebar.number_input("Comisión por Contrato (RT)", value=2.04, step=0.01)

# Apply Filters
df = df_raw.copy()

# Date Logic
if len(date_range) == 2:
    start_d, end_d = date_range
    # Filter inclusive
    mask = (df['EntryTime'].dt.date >= start_d) & (df['EntryTime'].dt.date <= end_d)
    df = df[mask]
    
if selected_inst != 'Todos':
    df = df[df['Instrument'] == selected_inst]
if selected_setup != 'Todos':
    df = df[df['SetupName'] == selected_setup]

# Account Filter
if 'Account' in df.columns:
    accounts = ['Todos'] + list(df['Account'].unique())
    selected_acc = st.sidebar.selectbox("Cuenta", accounts)
    if selected_acc != 'Todos':
        df = df[df['Account'] == selected_acc]

# Apply Commission (Net PnL)
# Apply Commission (Net PnL)
# Assuming CSV 'PnL' is Gross. We subtract commission per row (since each row is 1 contract)
if commission > 0:
    df['PnL'] = df['PnL'] - commission
    df['PnL_Gross'] = df['PnL'] + commission # Keep gross for reference if needed

# --- 3. METRICS ENGINE ---

# Validation: Check for duplicates
if 'EntryTime' in df.columns:
    init_len = len(df)
    # Deduplication DISABLED to allow multi-contract exits (identical time/price but unique ID)
    # df = df.drop_duplicates(subset=['Instrument', 'EntryTime', 'ExitTime', 'Type', 'EntryPrice', 'ExitPrice'])
    # Checks are now handled by Strategy Logic preventing duplicate logs.
    pass
    
total_pnl = df['PnL'].sum()
trade_gb = df.groupby('Trade_Clust_ID')['PnL'].sum() # PnL per Logial Trade
total_trades = len(trade_gb)
win_loss = trade_gb > 0
win_rate = win_loss.mean() * 100
avg_win = trade_gb[trade_gb > 0].mean() if not trade_gb[trade_gb > 0].empty else 0
avg_loss = trade_gb[trade_gb <= 0].mean() if not trade_gb[trade_gb <= 0].empty else 0
pf = abs(trade_gb[trade_gb > 0].sum() / trade_gb[trade_gb <= 0].sum()) if trade_gb[trade_gb <= 0].sum() != 0 else 0

# --- CHAT INTERACTIVO CON IA (después de calcular métricas) ---
st.sidebar.markdown("---")
st.sidebar.markdown("### 🤖 Asistente IA")

# Check if AI is available
if get_analyzer() is None:
    st.sidebar.info("💡 Configura GEMINI_API_KEY en .env para habilitar chat")
else:
    # Initialize chat history
    if 'chat_history' not in st.session_state:
        st.session_state.chat_history = []
    
    # Chat input
    user_question = st.sidebar.text_input(
        "Pregunta sobre tus trades:",
        placeholder="Ej: ¿Por qué MNQ tiene mejor WR que MES?",
        key="ai_chat_input"
    )
    
    if st.sidebar.button("💬 Enviar", key="ai_chat_send", use_container_width=True) and user_question:
        # Prepare context (all variables are now available)
        context = {
            "total_pnl": f"${total_pnl:,.2f}",
            "total_trades": total_trades,
            "win_rate": f"{win_rate:.1f}%",
            "profit_factor": f"{pf:.2f}",
            "instruments": ", ".join(df['Instrument'].unique().tolist()),
            "setups": ", ".join(df['SetupName'].unique().tolist()),
            "date_range": f"{min_date} to {max_date}",
            "selected_instrument": selected_inst,
            "selected_setup": selected_setup
        }
        
        # Get AI response
        with st.spinner("🧠 Analizando tu pregunta..."):
            response = get_analyzer().chat(user_question, context)
        
        # Save to history
        st.session_state.chat_history.append({
            "question": user_question,
            "answer": response
        })
        
        # Force rerun to show new message
        st.rerun()
    
    # Display chat history (reversed, most recent first)
    if st.session_state.chat_history:
        st.sidebar.markdown("#### 💬 Historial")
        
        # Show last 5 conversations
        for i, msg in enumerate(reversed(st.session_state.chat_history[-5:])):
            with st.sidebar.expander(f"Q: {msg['question'][:35]}...", expanded=(i==0)):
                st.markdown(f"**Pregunta:** {msg['question']}")
                st.markdown("---")
                st.markdown(msg['answer'])
        
        # Clear history button
        if len(st.session_state.chat_history) > 0:
            if st.sidebar.button("🗑️ Limpiar Historial", use_container_width=True):
                st.session_state.chat_history = []
                st.rerun()

# --- UI LAYOUT ---

st.title("🔬 Auditor de Microestructura Quant")
st.markdown(f"**Dataset:** {len(df)} Ejecuciones | **Trades Lógicos:** {total_trades}")

# Top KPI Row
kpi1, kpi2, kpi3, kpi4, kpi5 = st.columns(5)
kpi1.metric("Beneficio Neto", f"${total_pnl:,.2f}")
kpi2.metric("Factor de Beneficio", f"{pf:.2f}")
kpi3.metric("Tasa de Acierto", f"{win_rate:.1f}%")
kpi4.metric("Promedio x Trade", f"${trade_gb.mean():.2f}")
# v2.2: Show max tier name instead of number
max_tier = df[df['Exit_Rank'] == df['Exit_Rank'].max()]['Exit_Tier'].iloc[0] if len(df) > 0 else 'N/A'
kpi5.metric("Max Tier Usado", max_tier)

# Tabs
tab1, tab2, tab3, tab4, tab5, tab6, tab7, tab8, tab9, tab10 = st.tabs([
    "📊 Tablero", 
    "🧅 Análisis de Escala", 
    "📉 Análisis de Riesgo", 
    "🎯 MAE/MFE", 
    "🎲 Monte Carlo", 
    "📅 Calendario", 
    "⏰ Análisis Temporal", 
    "🧱 Análisis de Niveles", 
    "🆚 Live vs Backtest",
    "🎯 Reporte Ejecutivo"
])

with tab1:
    st.markdown("### Curva de Equidad (Por Ejecución)")
    
    # v1.14.32: Chart Type Selector
    chart_type = st.radio(
        "Tipo de Gráfico",
        options=["📈 Curva de Equidad", "📊 Barras por Trade"],
        horizontal=True,
        key="equity_chart_type"
    )
    
    # Standard Equity Curve
    df = df.sort_values('ExitTime')
    df['Cumulative_PnL'] = df['PnL'].cumsum()
    
    # Calculate drawdown for AI context
    equity_curve = df['Cumulative_PnL'].values
    high_water_mark = np.maximum.accumulate(equity_curve)
    drawdown = equity_curve - high_water_mark
    
    if chart_type == "📈 Curva de Equidad":
        # Convert ExitTime to string for categorical axis (no gaps)
        df['ExitLabel'] = df['ExitTime'].dt.strftime('%m/%d %H:%M')
        fig_eq = px.line(df, x='ExitLabel', y='Cumulative_PnL')
        fig_eq.update_traces(line_color='#00FF99', line_width=2)
        fig_eq.update_xaxes(type='category')
        fig_eq = apply_premium_style(fig_eq, title='Equidad del Portafolio')
    else:
        # Bar chart showing PnL per trade with colors (no gaps)
        colors = ['#00FF99' if pnl >= 0 else '#FF4444' for pnl in df['PnL']]
        # Convert ExitTime to string to make it categorical (no gaps)
        df['ExitLabel'] = df['ExitTime'].dt.strftime('%m/%d %H:%M')
        fig_eq = px.bar(df, x='ExitLabel', y='PnL', color_discrete_sequence=['#00FF99'])
        fig_eq.update_traces(marker_color=colors)
        fig_eq.update_xaxes(type='category')  # Force categorical axis
        fig_eq = apply_premium_style(fig_eq, title='PnL por Trade')
    
    st.plotly_chart(fig_eq, use_container_width=True)
    
    # AI Analysis for Equity Curve
    show_ai_analysis(
        chart_name="Curva de Equidad",
        chart_type="equity_curve",
        data={
            "total_pnl": total_pnl,
            "max_drawdown": drawdown.min(),
            "win_rate": win_rate,
            "pf": pf,
            "total_trades": total_trades
        },
        key_suffix="tab1_equity"
    )
    
    col1, col2 = st.columns(2)
    with col1:
        st.markdown("### Rendimiento Long vs Short")
        # Ensure we aggregate by Trade ID first to avoid double counting mixed logic? 
        # Actually PnL is additive, so group by Type is fine.
        type_perf = df.groupby('Type')['PnL'].sum().reset_index()
        
        # Calculate stats for AI
        long_data = df[df['Type'] == 'Long']
        short_data = df[df['Type'] == 'Short']
        pnl_long = long_data['PnL'].sum() if len(long_data) > 0 else 0
        pnl_short = short_data['PnL'].sum() if len(short_data) > 0 else 0
        trades_long = long_data['Trade_Clust_ID'].nunique() if len(long_data) > 0 else 0
        trades_short = short_data['Trade_Clust_ID'].nunique() if len(short_data) > 0 else 0
        wr_long = (long_data.groupby('Trade_Clust_ID')['PnL'].sum() > 0).mean() * 100 if trades_long > 0 else 0
        wr_short = (short_data.groupby('Trade_Clust_ID')['PnL'].sum() > 0).mean() * 100 if trades_short > 0 else 0
        
        # Apply matching colors (Green/Red) from PnL chart
        type_colors = ['#00FF99' if pnl >= 0 else '#FF4444' for pnl in type_perf['PnL']]
        fig_type = px.bar(type_perf, x='Type', y='PnL')
        fig_type.update_traces(marker_color=type_colors)
        fig_type = apply_premium_style(fig_type, title='Rendimiento Long vs Short')
        st.plotly_chart(fig_type, use_container_width=True)
        
        # AI Analysis for Long vs Short
        show_ai_analysis(
            chart_name="Long vs Short",
            chart_type="long_vs_short",
            data={
                "pnl_long": pnl_long,
                "pnl_short": pnl_short,
                "trades_long": trades_long,
                "trades_short": trades_short,
                "wr_long": wr_long,
                "wr_short": wr_short
            },
            key_suffix="tab1_longshort"
        )
        
    with col2:
        st.markdown("### PnL por Setup")
        setup_perf = df.groupby('SetupName')['PnL'].sum().sort_values().reset_index()
        fig_setup = px.bar(setup_perf, y='SetupName', x='PnL', orientation='h', color='PnL', color_continuous_scale='RdBu')
        fig_setup = apply_premium_style(fig_setup)
        st.plotly_chart(fig_setup, use_container_width=True)
        
        # AI Analysis for Setup Performance
        setup_summary = setup_perf.to_string(index=False)
        show_ai_analysis(
            chart_name="PnL por Setup",
            chart_type="setup_performance",
            data={
                "setup_data": setup_summary
            },
            key_suffix="tab1_setup"
        )

with tab2:
    st.header("Análisis de Escala (Cebolla) 🧅")
    st.markdown("""
    **Hipótesis:** Las salidas tempranas capturan scalps de alta probabilidad, mientras que las salidas tardías (runners) proveen los retornos de 'cola gruesa' pero con mayor varianza.
    Este módulo valida si tus runners valen el riesgo.
    """)
    
    # 1. Tier Performance Table (v2.2: Using Exit_Tier for labels)
    tier_stats = df.groupby('Exit_Tier').agg(
        Executions=('PnL', 'count'),
        WinRate=('PnL', lambda x: (x > 0).mean()),
        AvgPnL=('PnL', 'mean'),
        TotalPnL=('PnL', 'sum'),
        StdDev=('PnL', 'std')
    ).reset_index()
    
    tier_stats['Sharpe_Proxy'] = tier_stats['AvgPnL'] / tier_stats['StdDev']
    
    st.dataframe(tier_stats.style.format({
        'WinRate': '{:.1%}',
        'AvgPnL': '${:.2f}',
        'TotalPnL': '${:,.2f}',
        'StdDev': '${:.2f}',
        'Sharpe_Proxy': '{:.2f}'
    }), use_container_width=True)
    
    # 2. Distribution Plot (v2.2: Using Exit_Tier for labels)
    fig_box = px.box(df, x='Exit_Tier', y='PnL', color='Exit_Tier', points="all")
    fig_box = apply_premium_style(fig_box, title="Distribución PnL por Tier (Volatilidad)")
    st.plotly_chart(fig_box, use_container_width=True)
    
    # --- AUTOMATED INTERPRETATION LOGIC (v2.2: Using Exit_Tier) ---
    tiers = sorted(df['Exit_Tier'].unique(), key=lambda x: (0 if x=='SL' else 1 if x.startswith('TP') else 2, x))
    insight_text = ""
    
    prev_tier = None
    prev_median = None
    
    for tier in tiers:
        # Get data for this tier
        subset = df[df['Exit_Tier'] == tier]['PnL']
        if subset.empty: continue
        
        # Stats
        q1 = subset.quantile(0.25)
        median = subset.median()
        q3 = subset.quantile(0.75)
        iqr = q3 - q1
        max_val = subset.max()
        
        # Heuristics (v2.2: Using tier name)
        insight_text += f"**{tier}:**\n"
        
        # 1. Median Analysis
        if median <= 0:
            insight_text += f"- ⚠️ **Caja Aplastada:** La mediana es ${median:.2f} (Breakeven o Negativa). La mayoría de trades normales no ganan dinero.\n"
        else:
            insight_text += f"- ✅ **Base Sólida:** La mediana es positiva (${median:.2f}). El trade 'típico' suma valor.\n"
            
        # 2. Outlier Dependency
        # Standard Outlier definition: > Q3 + 1.5*IQR
        upper_fence = q3 + (1.5 * iqr)
        if max_val > upper_fence:
            insight_text += f"- 🚀 **Dependencia de 'Home Runs':** Tienes outliers muy positivos (${max_val:,.0f}) muy por encima de la caja normal. **Lectura:** Este contrato paga las facturas gracias a pocos golpes de suerte, no por consistencia.\n"
        
        # 3. Comparative Alpha (vs Previous Tier) - v2.2 fixed
        if prev_tier is not None and prev_median is not None:
            if median <= prev_median:
                 insight_text += f"- 📉 **Sin Alfa Extra:** Este tier NO mejora la mediana del anterior ({prev_tier}: ${prev_median:.2f} vs {tier}: ${median:.2f}). **Lectura:** Si esta caja no es visiblemente más alta, solo estás añadiendo riesgo sin recompensa base.\n"
            elif median > prev_median * 1.2:
                 insight_text += f"- 📈 **Aporte de Valor:** Este tier desplaza la caja hacia arriba. Está capturando una ventaja real.\n"
        
        # Save for next iteration
        prev_tier = tier
        prev_median = median
        insight_text += "\n"

    # 3. Cumulative Contribution (v2.2.1: Conditional colors - lime for positive, red for negative)
    tier_stats['Color'] = tier_stats['TotalPnL'].apply(lambda x: '#00FF00' if x >= 0 else '#FF4444')
    fig_cum_ranks = px.bar(tier_stats, x='Exit_Tier', y='TotalPnL', color='TotalPnL',
                           color_continuous_scale=[[0, '#FF4444'], [0.5, '#333333'], [1, '#00FF00']])
    fig_cum_ranks.update_traces(marker_color=tier_stats['Color'].tolist())
    fig_cum_ranks.update_layout(showlegend=False, coloraxis_showscale=False)
    fig_cum_ranks = apply_premium_style(fig_cum_ranks, "Contribución Neta por Tier")
    st.plotly_chart(fig_cum_ranks, use_container_width=True)
    
    # AI Analysis for Tier Performance
    tier_summary = tier_stats.to_string(index=False)
    show_ai_analysis(
        chart_name="Distribución de Tiers",
        chart_type="tier_analysis",
        data={"tier_data": tier_summary},
        key_suffix="tab2_tiers"
    )

    st.info(f"""
    🧠 **Insight de Experto Quant: La Cebolla de Rentabilidad**
    
    {insight_text}
    
    *   **Filosofía:** En sistemas de escala, los primeros contratos (T1, T2) suelen tener alto Win Rate para "financiar" el riesgo de los últimos contratos (Runners).
    *   **Acción:** Revisa la columna **'Sharpe_Proxy'**. Si tus últimos Tiers tienen un Sharpe bajo y apenas suman PnL total, estás asumiendo volatilidad inútil. Córtalos y reduce tu riesgo.
    """)

with tab3:
    st.header("Riesgo & Drawdown")
    
    # Drawdown Calc
    equity_curve = df['Cumulative_PnL'].values
    high_water_mark = np.maximum.accumulate(equity_curve)
    drawdown = equity_curve - high_water_mark
    
    df['Drawdown'] = drawdown
    
    fig_dd = px.area(df, x='ExitTime', y='Drawdown', color_discrete_sequence=['#FF4444'])
    fig_dd = apply_premium_style(fig_dd, 'Gráfico Submarino (Drawdown)')
    st.plotly_chart(fig_dd, use_container_width=True)
    
    st.metric("Max Drawdown", f"${drawdown.min():,.2f}")
    
    # AI Analysis for Drawdown
    dd_periods = len(df[df['Drawdown'] < 0]) / len(df) * 100 if len(df) > 0 else 0
    show_ai_analysis(
        chart_name="Análisis de Riesgo",
        chart_type="drawdown",
        data={
            "max_dd": abs(drawdown.min()),
            "current_dd": abs(drawdown[-1]) if len(drawdown) > 0 else 0,
            "dd_periods": dd_periods
        },
        key_suffix="tab3_drawdown"
    )

    st.info("""
    🧠 **Insight de Experto Quant: Asimetría del Drawdown**
    
    *   **Recuperación:** Una caída del 50% requiere un 100% de retorno para volver a breakeven. Protege tu capital agresivamente.
    *   **Duración (Time Underwater):** No solo importa *cuánto* pierdes, sino *por cuánto tiempo*. Un sistema que pasa 6 meses en negativo destruye la psicología del operador.
    *   **Consejo:** Si tu "Underwater Plot" muestra periodos de recuperación planos y eternos (meses), tu sistema carece de "alfa" para salir de hoyos. Busca recuperaciones rápidas en forma de 'V'.
    """)

with tab4:
    st.header("Optimización: MAE vs PnL")
    st.markdown("Optimización: **Excursión Adversa Máxima** (Dolor Máximo Aguantado) vs Beneficio Final.")
    
    if 'MAE' in df.columns:
        fig_scatter = px.scatter(df, x='MAE', y='PnL', color='Exit_Rank', 
                                 hover_data=['Instrument', 'SetupName', 'MFE'])
        # Invert MAE axis? No, 0 is good.
        fig_scatter.add_vline(x=0, line_dash="dash", line_color="#00FF99")
        fig_scatter = apply_premium_style(fig_scatter, "MAE Real vs PnL (Frontera de Eficiencia)")
        st.plotly_chart(fig_scatter, use_container_width=True)
        
        # --- AUTOMATED MAE INSIGHTS ---
        mae_insight = ""
        winners = df[df['PnL'] > 0].copy()
        
        if not winners.empty:
            count_win = len(winners)
            
            # 1. Sniper (Zero MAE)
            # Assuming MAE is absolute positive value
            snipers = winners[winners['MAE'] == 0]
            n_snipers = len(snipers)
            pct_snipers = (n_snipers / count_win) * 100
            
            # 2. Danger Zone (Inefficient Winners: Pain > 50% of Gain)
            # Avoid division by zero
            # Logic: If MAE > PnL * 0.5, it was a "scary" ride.
            inefficient = winners[winners['MAE'] > (winners['PnL'] * 0.5)]
            n_inefficient = len(inefficient)
            pct_inefficient = (n_inefficient / count_win) * 100
            
            mae_insight += f"**Análisis de tus {count_win} Trades Ganadores:**\n"
            
            # Message Logic based on Sample Size
            if count_win < 5:
                # Small Sample: Speak in counts, not %
                if n_snipers > 0:
                    mae_insight += f"- 🎯 **Francotirador:** {n_snipers} de tus {count_win} ganadores ({(n_snipers/count_win)*100:.0f}%) tuvieron **CERO dolor** (MAE=0). Excelente timing.\n"
                else:
                    mae_insight += f"- ℹ️ **Timing Normal:** Ninguno de tus {count_win} ganadores fue una entrada perfecta (todos tuvieron algo de retroceso).\n"
                    
                if n_inefficient > 0:
                    mae_insight += f"- ⚠️ **ZONA DE PELIGRO:** {n_inefficient} de tus {count_win} ganadores sufrieron un retroceso severo (>50% de lo ganado).\n"
                else:
                    mae_insight += f"- ✅ **Alta Eficiencia:** Ningún ganador sufrió retroceso excesivo. Tus stops están funcionando bien.\n"
            else:
                # Large Sample: Use Percentages
                if pct_snipers > 10:
                    mae_insight += f"- 🎯 **Francotirador:** Un increíble **{pct_snipers:.1f}%** de tus ganadores tuvieron **CERO dolor** (MAE=0). Excelente timing de entrada.\n"
                else:
                    mae_insight += f"- ℹ️ **Timing Normal:** Solo el {pct_snipers:.1f}% fueron entradas perfectas sin retroceso (MAE=0).\n"
                    
                if pct_inefficient > 30:
                    mae_insight += f"- ⚠️ **ZONA DE PELIGRO:** El **{pct_inefficient:.1f}%** de tus ganadores sufrieron un retroceso severo (>50% de la ganancia) antes de funcionar. **Acción:** Estas son las 'verdes a la derecha'. Considera ajustar el Stop Loss, estás regalando demasiado espacio.\n"
                elif pct_inefficient < 10:
                     mae_insight += f"- ✅ **Alta Eficiencia:** Solo un {pct_inefficient:.1f}% de trades sufrieron mucho. Tus stops parecen estar bien calibrados.\n"
        
        st.info(f"""
        🧠 **Insight de Experto Quant: La Frontera de Eficiencia**
        
        {mae_insight}
        
        *   **El Gráfico:** Muestra cuánto dolor (MAE - Eje X) tuviste que aguantar para obtener una ganancia (PnL - Eje Y).
        *   **Zona Óptima (Arriba-Izquierda):** Ganancias altas con poco dolor (poca excursión negativa). Entradas tipo "Sniper".
        *   **Zona de Peligro (Arriba-Derecha):** Ganaste dinero, PERO el precio se fue muy en contra antes de volver. Esto es "suerte" o stops demasiado amplios.
        """)
        
        # AI Analysis for MAE/MFE
        avg_mae = df['MAE'].mean() if 'MAE' in df.columns else 0
        avg_mfe = df['MFE'].mean() if 'MFE' in df.columns else 0
        efficiency = (total_pnl / (avg_mfe * len(df)) * 100) if avg_mfe > 0 and len(df) > 0 else 0
        sniper_pct = pct_snipers if 'pct_snipers' in dir() else 0
        
        show_ai_analysis(
            chart_name="MAE vs MFE",
            chart_type="mae_mfe",
            data={
                "avg_mae": avg_mae,
                "avg_mfe": avg_mfe,
                "efficiency": efficiency,
                "sniper_pct": sniper_pct
            },
            key_suffix="tab4_mae"
        )
    else:
        st.warning("⚠️ Datos de MAE no encontrados. Por favor ejecuta el backtest con la estrategia actualizada.")

with tab5:
    st.header("🎲 Simulación Monte Carlo")
    st.markdown("Re-muestreo de **Trades Lógicos Completos** para probar la robustez del sistema.")
    
    # Visual Config
    with st.expander("⚙️ Configuración de Simulación", expanded=True):
        col_cfg1, col_cfg2, col_cfg3 = st.columns(3)
        
        n_sims = col_cfg1.slider("Número de Simulaciones", 50, 2000, 200, 50, help="Más simulaciones = Mejor precisión estadística, pero más lento.")
        
        horizon_opt = col_cfg2.selectbox("Horizonte de Proyección", 
                                        ["Igual al Backtest", "Próximos 100 Trades", "Próximos 250 Trades (aprox 1 año)"])
        
        mc_opacity = col_cfg3.slider("Opacidad de Líneas", 0.05, 1.0, 0.1, 0.05)

    # Logic Prep
    trade_pnls = df.groupby('Trade_Clust_ID')['PnL'].sum().values
    n_history_trades = len(trade_pnls)
    
    # Init vars for scope safety
    worst_drawdown_mc = 0
    rec_capital = 0
    metric_comment = ""
    
    # Calculate Horizon
    if horizon_opt == "Igual al Backtest":
        n_horizon = n_history_trades
    elif horizon_opt == "Próximos 100 Trades":
        n_horizon = 100
    else:
        n_horizon = 250

    # Sample Size Warning
    confidence_msg = ""
    if n_history_trades < 30:
        st.error(f"⚠️ **ADVERTENCIA DE DATOS:** Solo tienes {n_history_trades} trades históricos. Los resultados de Monte Carlo serán poco fiables (Basura entra, Basura sale). Se recomienda > 50 trades.")
    elif n_history_trades < 100:
        st.warning(f"⚠️ **Precaución:** Tienes {n_history_trades} trades. La simulación es útil pero tiene margen de error.")
    else:
        st.success(f"✅ **Datos Robustos:** Tienes {n_history_trades} trades. La base estadística es sólida para re-muestreo.")

    if st.button("Ejecutar Simulación"):
        
        simulations = []
        progress_bar = st.progress(0)
        
        for i in range(n_sims):
            # Shuffle logic with Horizon
            shuffled = np.random.choice(trade_pnls, size=n_horizon, replace=True)
            sim_curve = np.cumsum(shuffled)
            simulations.append(sim_curve)
            progress_bar.progress((i+1)/n_sims)
            
        # Plotting
        fig_mc = go.Figure()
        
        for sim in simulations:
            fig_mc.add_trace(go.Scatter(y=sim, mode='lines', 
                                      line=dict(color='#00FFFF', width=1), 
                                      opacity=mc_opacity,
                                      hoverinfo='skip', showlegend=False))
            
        # Add Original (Only if horizon matches substantially, otherwise misleading?)
        if horizon_opt == "Igual al Backtest":
             original_curve = np.cumsum(trade_pnls)
             fig_mc.add_trace(go.Scatter(y=original_curve, mode='lines', line=dict(color='#FFD700', width=3), name='Original (Real)'))
      
        fig_mc = apply_premium_style(fig_mc, "Simulación Monte Carlo (Caminos Aleatorios)")
        fig_mc.update_layout(xaxis_title="Cant. Trades", yaxis_title="Equidad")
        # Calculate Metrics
        final_values = [s[-1] for s in simulations]
        risk_of_loss = sum(1 for v in final_values if v < 0) / n_sims * 100
        worst_case_pnl = min(final_values)
        best_case_pnl = max(final_values)
        
        # Calculate Worst Drawdown across ALL simulations
        worst_drawdown_mc = 0
        for sim in simulations:
            peak = np.maximum.accumulate(sim)
            dd = peak - sim
            max_dd = dd.max()
            if max_dd > worst_drawdown_mc:
                worst_drawdown_mc = max_dd
                
        rec_capital = worst_drawdown_mc * 2.0
        
        # Metrics Display
        mc1, mc2, mc3, mc4 = st.columns(4)
        mc1.metric("Riesgo de Ruina", f"{risk_of_loss:.1f}%", delta_color="inverse" if risk_of_loss > 0 else "normal")
        mc2.metric("Peor Final PnL", f"${worst_case_pnl:,.2f}")
        mc3.metric("Peor Drawdown Sim", f"-${worst_drawdown_mc:,.2f}", help="La caída más profunda observada en cualquiera de las 100 simulaciones")
        mc4.metric("Capital Sugerido", f"${rec_capital:,.2f}", delta="#1 Safety Rule")

        st.plotly_chart(fig_mc, use_container_width=True)

    # Dynamic Analysis Text
    if worst_drawdown_mc != 0:
        # User has run simulation
        if risk_of_loss > 20:
             metric_comment = f"⚠️ **ALERTA:** Tu sistema tiene un **{risk_of_loss:.1f}%** de probabilidad de perder dinero en 100 trades. Esto confirma la fragilidad mencionada."
        elif risk_of_loss > 0:
             metric_comment = f"ℹ️ **NOTA:** Existe un **{risk_of_loss:.1f}%** de riesgo de terminar negativo. Es aceptable, pero vigila el Drawdown."
        else:
             metric_comment = "✅ **ROBUSTO:** En 100 simulaciones, ninguna terminó en pérdidas. Tu sistema tiene una esperanza matemática muy sólida."
             
        capital_advice = f"""
        *   **Capitalización:** Tu cuenta debe soportar no el Drawdown Histórico, sino al menos **1.5x a 2x** el peor Drawdown visible en estas simulaciones para sobrevivir al "Cisne Negro".
        *   **Recomendación de Capital:** Basado en el peor Drawdown simulado **(-${worst_drawdown_mc:,.2f})**, recomendamos un capital mínimo de **${rec_capital:,.2f}** (2x Drawdown)."""
    else:
        # Default / Pre-run text
        metric_comment = "ℹ️ **Instrucción:** Ejecuta la simulación para obtener un análisis de fragilidad personalizado."
        capital_advice = """
        *   **Capitalización:** Tu cuenta debe soportar no el Drawdown Histórico, sino al menos **1.5x a 2x** el peor Drawdown visible en simulaciones de Monte Carlo.
        *   **Recomendación de Capital:** Ejecuta la simulación para calcular el "Cisne Negro" específico de tu estrategia."""

    st.warning(f"""
    🧠 **Insight de Experto Quant: Riesgo de Secuencia y Capitalización**
    
    {metric_comment}
    
    *   **Ley de los Grandes Números:** Si tienes pocos datos (<50 trades), Monte Carlo asume que tu futuro será una repetición exacta de ese breve pasado. Eso es peligroso. A mayor muestra (10 años), más confiable es la proyección.
    {capital_advice}
    """)
    
    # AI Analysis for Monte Carlo (only show if simulation was run)
    if 'worst_drawdown_mc' in locals() and worst_drawdown_mc != 0:
        show_ai_analysis(
            chart_name="Simulación Monte Carlo",
            chart_type="monte_carlo",
            data={
                "n_sims": n_sims,
                "risk_of_ruin": 0,  # Placeholder - would need actual calculation
                "worst_dd": abs(worst_drawdown_mc) if worst_drawdown_mc < 0 else 0,
                "best_case": max([sim[-1] for sim in simulations]) if 'simulations' in locals() and simulations else 0,
                "worst_case": min([sim[-1] for sim in simulations]) if 'simulations' in locals() and simulations else 0,
                "suggested_capital": rec_capital
            },
            key_suffix="tab5_montecarlo"
        )


with tab6:
    st.header("📅 Calendario de Trading")
    
    # Calendar Data Prep
    # Group PnL by Date
    df['ExitDate'] = df['ExitTime'].dt.date
    daily_stats = df.groupby('ExitDate').agg(
        DailyPnL=('PnL', 'sum'),
        TradeCount=('Trade_Clust_ID', 'nunique') # Count Unique Logical Trades (Setups) not Contracts
    ).reset_index()
    daily_stats['ExitDate'] = pd.to_datetime(daily_stats['ExitDate'])
    
    # 1. Month Selector
    if not daily_stats.empty:
        # Get unique Year-Months
        daily_stats['YearMonth'] = daily_stats['ExitDate'].dt.to_period('M')
        available_months = daily_stats['YearMonth'].unique().astype(str)
        available_months = sorted(available_months, reverse=True) # Newest first
        st.markdown("---")
    
        # 1. Month Selector
        # Determine default index from URL if available
        default_ix = 0
        if "audit_month" in st.query_params:
            url_month = st.query_params["audit_month"]
            if url_month in available_months:
                default_ix = available_months.index(url_month)
        
        selected_month_str = st.selectbox("Seleccionar Mes", available_months, index=default_ix, key="calendar_month_selector")
        
        # Filter for selected month
        y_str, m_str = selected_month_str.split('-')
        year_sel = int(y_str)
        month_sel = int(m_str)
        
        # Filter stats
        month_stats = daily_stats[daily_stats['YearMonth'].astype(str) == selected_month_str].set_index('ExitDate')
        
        # Monthly Totals
        m_pnl = month_stats['DailyPnL'].sum()
        m_color = "#CCFF00" if m_pnl >= 0 else "#FF4444"
        st.markdown(f"### {calendar.month_name[month_sel]} {year_sel}: <span style='color:{m_color}'>${m_pnl:,.2f}</span>", unsafe_allow_html=True)
        
        # 2. CALENDAR GRID (HTML + Query Params for Click)
        # Check for click event via Query Params
        # Compatible with newer Streamlit versions (st.query_params)
        try:
            qp = st.query_params
            if "audit_date" in qp:
                clicked_date_str = qp["audit_date"]
                try:
                    # Valid date format YYYY-MM-DD
                    st.session_state.selected_date_audit = pd.to_datetime(clicked_date_str)
                    
                    # v2.3.0: Clear query param after processing to prevent tab jump on page reload
                    del st.query_params["audit_date"]
                    
                    # INJECT JS TO RESTORE TAB (Auto-Click "Calendario")
                    # This runs after the reload triggered by the link
                    js_restore_tab = """
                    <script>
                        function clickCalendarTab() {
                            const tabs = window.parent.document.querySelectorAll('button[data-baseweb="tab"]');
                            let found = false;
                            tabs.forEach(tab => {
                                // Check both text content and inner divs
                                if (tab.innerText && tab.innerText.includes("Calendario")) {
                                    tab.click();
                                    found = true;
                                }
                            });
                            return found;
                        }
                        
                        function attemptRestore(count) {
                            if (count > 5) return; // Max retries
                            const success = clickCalendarTab();
                            if (!success) {
                                setTimeout(() => attemptRestore(count + 1), 500);
                            }
                        }
                        
                        // Start polling
                        setTimeout(() => attemptRestore(0), 500);
                    </script>
                    """
                    components.html(js_restore_tab, height=0)
                    
                except:
                    pass
                # Optional: Clear param so it doesn't stick (requires re-run, might flicker)
                # st.query_params.clear() 
        except:
             # Fallback for older streamlit
             pass

        # Generate Calendar Grid
        cal_obj = calendar.monthcalendar(year_sel, month_sel)
        week_days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
        
        # HTML Building
        html = '<div class="cal-container">'
        
        # Headers
        for day in week_days:
            html += f'<div class="cal-header">{day}</div>'
        html += '<div class="cal-header" style="color:#E6E6E6;">Σ Wk</div>' # 8th Column Header
            
        # Weeks
        for week in cal_obj:
            weekly_pnl = 0
            weekly_has_data = False
            
            for day_num in week:
                if day_num == 0:
                    html += '<div class="cal-day cal-empty"></div>'
                else:
                    # Look up data
                    current_date = pd.Timestamp(year=year_sel, month=month_sel, day=day_num)
                    date_str = current_date.strftime("%Y-%m-%d")
                    current_month_str = f"{year_sel}-{month_sel:02d}"
                    
                    pnl_txt = ""
                    trades_txt = ""
                    cell_class = "cal-day"
                    
                    # Interactivity Wrapper: Link to self with param
                    link_start = f'<a href="?audit_date={date_str}&audit_month={current_month_str}" target="_self" style="text-decoration:none; color:inherit; display:block; height:100%;">'
                    link_end = '</a>'
                    
                    if current_date in month_stats.index:
                        row = month_stats.loc[current_date]
                        dpnl = row['DailyPnL']
                        cnt = int(row['TradeCount'])
                        
                        # Accumulate Weekly
                        weekly_pnl += dpnl
                        weekly_has_data = True
                        
                        if dpnl > 0:
                            cell_class += " green"
                            pnl_span = f'<div class="cal-pnl-pos">+${dpnl:,.0f}</div>'
                        elif dpnl < 0:
                            cell_class += " red"
                            pnl_span = f'<div class="cal-pnl-neg">-${abs(dpnl):,.0f}</div>'
                        else:
                            pnl_span = f'<div class="cal-pnl-pos">${dpnl:,.0f}</div>'
                            
                        trades_txt = f'<div class="cal-trades">{cnt} Trades</div>'
                        pnl_txt = pnl_span
                        
                        # Only wrap in link if there is data to show!
                        content = f'<div class="{cell_class}"><div class="cal-date">{day_num}</div><div>{pnl_txt}{trades_txt}</div></div>'
                        html += f'{link_start}{content}{link_end}'
                    else:
                        pnl_txt = '<div style="color:#444">-</div>'
                        html += f'<div class="{cell_class}"><div class="cal-date">{day_num}</div><div>{pnl_txt}{trades_txt}</div></div>'
            
            # --- WEEKLY SUMMARY CELL (8th Column) ---
            w_class = "cal-weekly"
            w_val_html = '<div style="color:#444">-</div>'
            
            if weekly_has_data:
                if weekly_pnl > 0:
                    w_val_html = f'<div class="cal-pnl-pos" style="font-size:14px;">+${weekly_pnl:,.0f}</div>'
                elif weekly_pnl < 0:
                     w_val_html = f'<div class="cal-pnl-neg" style="font-size:14px;">-${abs(weekly_pnl):,.0f}</div>'
                else:
                     w_val_html = f'<div class="cal-pnl-pos" style="font-size:14px;">${weekly_pnl:,.0f}</div>'
            
            html += f'<div class="{w_class}"><div class="cal-weekly-title">Total</div>{w_val_html}</div>'
                    
        html += '</div>'
        
        st.markdown(html, unsafe_allow_html=True)
        
        st.divider()

        # 3. DETAILED VIEW (If Date Selected)
        if st.session_state.selected_date_audit:
            sel_date = st.session_state.selected_date_audit
            # Validate if selected date is within current data context? Not strictly necessary.
            
            st.header(f"🕵️ Análisis Detallado: {sel_date.strftime('%A, %d %B %Y')}")
            
            # Filter Data for Day
            day_df = df[df['ExitDate'] == sel_date.date()].copy()
            
            if not day_df.empty:
                d_pnl = day_df['PnL'].sum()
                d_cnt = day_df['Trade_Clust_ID'].nunique()
                
                # Daily Metrics
                m1, m2, m3 = st.columns(3)
                m1.metric("PnL Neto Diario", f"${d_pnl:,.2f}", delta_color="normal" if d_pnl >=0 else "inverse")
                m2.metric("Total Trades", d_cnt)
                
                # Intraday Equity
                day_df_sorted = day_df.sort_values('ExitTime')
                day_df_sorted['Daily_Cum_PnL'] = day_df_sorted['PnL'].cumsum()
                
                fig_day = px.line(day_df_sorted, x='ExitTime', y='Daily_Cum_PnL', markers=True)
                fig_day.add_hline(y=0, line_dash="dash", line_color="#E6E6E6")
                fig_day = apply_premium_style(fig_day, "Rendimiento Intradía")
                st.plotly_chart(fig_day, use_container_width=True)
                
                # Trade Table
                st.subheader("📝 Historial de Ejecuciones")
                display_cols = ['Instrument', 'EntryTime', 'Type', 'EntryPrice', 'ExitPrice', 'PnL', 'SetupName', 'Result']
                valid_cols = [c for c in display_cols if c in day_df.columns]
                
                st.dataframe(day_df[valid_cols].style.format({
                    'EntryPrice': '{:.2f}',
                    'ExitPrice': '{:.2f}',
                    'PnL': '${:.2f}',
                    'EntryTime': lambda t: t.strftime('%H:%M:%S')
                }), use_container_width=True)
                
                # --- REPLAY SECTION ---
                st.divider()
                st.subheader("📺 Replay de Mercado (Gráfico OHLC)")
                
                # Selector
                trade_ids_day = day_df['Trade_Clust_ID'].unique()
                if len(trade_ids_day) > 0:
                    sel_replay_id = st.selectbox("Seleccionar Trade para Visualizar", trade_ids_day)
                    
                    # Logic
                    replay_row = day_df[day_df['Trade_Clust_ID'] == sel_replay_id].iloc[0]
                    r_instr = replay_row['Instrument']
                    r_date = replay_row['EntryTime'].date()
                    
                    # Load Logs
                    df_ohlc = load_market_data(r_instr, r_date)
                    
                    if df_ohlc is not None:
                         # Filter Range (Zoom into Trade)
                         trade_entry = replay_row['EntryTime']
                         trade_exit = replay_row['ExitTime'] 
                         
                         # Buffer: Show 30 mins before entry and 30 mins after exit
                         start_plot = trade_entry - pd.Timedelta(minutes=30)
                         end_plot = trade_exit + pd.Timedelta(minutes=30)
                         
                         mask = (df_ohlc['Datetime'] >= start_plot) & (df_ohlc['Datetime'] <= end_plot)
                         df_plot = df_ohlc[mask].copy()
                         
                         if not df_plot.empty:
                             # Plot Candlestick
                             fig_rep = go.Figure(data=[go.Candlestick(
                                 x=df_plot['Datetime'],
                                 open=df_plot['Open'],
                                 high=df_plot['High'],
                                 low=df_plot['Low'],
                                 close=df_plot['Close'],
                                 name=r_instr
                             )])
                             
                             # --- ADVANCED OVERLAYS (Solid 1px) ---
                             # VWAP High
                             if 'HighVWAP' in df_plot.columns and df_plot['HighVWAP'].sum() > 0:
                                 fig_rep.add_trace(go.Scatter(
                                     x=df_plot['Datetime'], y=df_plot['HighVWAP'],
                                     mode='lines',
                                     line=dict(color='gray', width=1), # Solid
                                     name='High VWAP', opacity=0.5
                                 ))
                             
                             # VWAP Low
                             if 'LowVWAP' in df_plot.columns and df_plot['LowVWAP'].sum() > 0:
                                 fig_rep.add_trace(go.Scatter(
                                     x=df_plot['Datetime'], y=df_plot['LowVWAP'],
                                     mode='lines',
                                     line=dict(color='gray', width=1), # Solid
                                     name='Low VWAP', opacity=0.5
                                 ))
                                 
                             # Active Level (Yellow/Gold)
                             if 'LevelPrice' in df_plot.columns and df_plot['LevelPrice'].max() > 0:
                                 # We only want to plot the level if it's "real" (non-zero)
                                 # Filter out 0s to avoid messing up scale
                                 lvl_series = df_plot['LevelPrice'].replace(0, pd.NA)
                                 fig_rep.add_trace(go.Scatter(
                                     x=df_plot['Datetime'], y=lvl_series,
                                     mode='lines',
                                     line=dict(color='#FFD700', width=1), # Solid Gold
                                     name='Setup Level'
                                 ))

                             # --- CONNECTION LINES (Trade Path) ---
                             # --- CONNECTION LINES (Trade Path) ---
                             # Reverting to Trace (go.Scatter) as Shapes are finicky with Date Axes.
                             # Using explicit to_pydatetime() to ensure compatibility.
                             fig_rep.add_trace(go.Scatter(
                                 x=[trade_entry.to_pydatetime(), trade_exit.to_pydatetime()], 
                                 y=[replay_row['EntryPrice'], replay_row['ExitPrice']],
                                 mode='lines',
                                 line=dict(color='white', width=3, dash='dash'),
                                 name='Trade Path',
                                 opacity=0.9
                             ))
                             
                             # Overlay Markers (Triangles)
                             # Entry
                             fig_rep.add_trace(go.Scatter(
                                 x=[trade_entry], 
                                 y=[replay_row['EntryPrice']],
                                 mode='markers',
                                 marker=dict(symbol='triangle-up', size=15, color='#00FF00'), # Green Up
                                 name='Entrada'
                             ))
                             
                             # Exit
                             fig_rep.add_trace(go.Scatter(
                                 x=[trade_exit], 
                                 y=[replay_row['ExitPrice']],
                                 mode='markers',
                                 marker=dict(symbol='triangle-down', size=15, color='#FF4444'), # Red Down
                                 name='Salida'
                             ))
                             
                             fig_rep = apply_premium_style(fig_rep, f"Replay: {r_instr} | {replay_row['SetupName']}")
                             fig_rep.update_layout(xaxis_rangeslider_visible=False)
                             
                             st.plotly_chart(fig_rep, use_container_width=True)
                             st.caption("ℹ️ Mostrando 30min antes/después del trade para contexto.")
                             
                         else:
                             st.warning("Datos encontrados pero el rango de tiempo no coincide. Revisa la hora.")
                    else:
                        st.info(f"⚠️ No hay datos de gráfico (OHLC) para {r_instr} el {r_date}. ¿Activaste 'ExportChartData' en NinjaTrader?")
                else:
                    st.info("No hay trades para reproducir.")
            else:
                st.info(f"No trades found for {sel_date.date()} in current dataset.")
                
        # AI Analysis for Calendar (at month level)
        if 'month_stats' in locals() and not month_stats.empty:
            month_pnl = month_stats['DailyPnL'].sum()
            best_day_idx = month_stats['DailyPnL'].idxmax()
            worst_day_idx = month_stats['DailyPnL'].idxmin()
            green_days = (month_stats['DailyPnL'] > 0).sum()
            red_days = (month_stats['DailyPnL'] < 0).sum()
            
            show_ai_analysis(
                chart_name="Patrones de Calendario",
                chart_type="calendar",
                data={
                    "month_pnl": month_pnl,
                    "best_day": best_day_idx.strftime('%d'),  # best_day_idx is already the date (index)
                    "best_day_pnl": month_stats.loc[best_day_idx, 'DailyPnL'],
                    "worst_day": worst_day_idx.strftime('%d'),  # worst_day_idx is already the date (index)
                    "worst_day_pnl": month_stats.loc[worst_day_idx, 'DailyPnL'],
                    "green_days": green_days,
                    "red_days": red_days
                },
                key_suffix="tab6_calendar"
            )
                
    else:
        st.info("No data available for calendar.")


with tab7:
    st.header("⏰ Análisis de Microestructura Temporal")
    
    if not df.empty:
        df['Hour'] = df['EntryTime'].dt.hour
        df['Weekday'] = df['EntryTime'].dt.day_name()
        
        # Order Weekdays
        days_order = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']
        
        col1, col2 = st.columns(2)
        
        with col1:
            st.subheader("Rendimiento por Hora")
            hour_stats = df.groupby('Hour')['PnL'].sum().reset_index()
            fig_hour = px.bar(hour_stats, x='Hour', y='PnL', color='PnL', color_continuous_scale='RdBu')
            fig_hour = apply_premium_style(fig_hour, "Distribución Horaria")
            st.plotly_chart(fig_hour, use_container_width=True)
            
        with col2:
            st.subheader("Rendimiento por Día")
            day_stats = df.groupby('Weekday')['PnL'].sum().reindex(days_order).reset_index()
            fig_day_bar = px.bar(day_stats, x='Weekday', y='PnL', color='PnL', color_continuous_scale='RdBu')
            fig_day_bar = apply_premium_style(fig_day_bar, "Distribución Semanal")
            st.plotly_chart(fig_day_bar, use_container_width=True)
            
        # --- AUTOMATED TIME INSIGHTS ---
        time_insight = ""
        
        # 1. Analyze Toxic Hours
        toxic_hours = hour_stats[hour_stats['PnL'] < 0]
        toxic_pnl_h = toxic_hours['PnL'].sum()
        
        # 2. Analyze Toxic Days
        day_stats_calc = df.groupby('Weekday')['PnL'].sum() # Re-calc without reindex format for ease
        toxic_days = day_stats_calc[day_stats_calc < 0]
        toxic_pnl_d = toxic_days.sum()
        
        current_total = total_pnl
        
        # Scenario A: Hourly Optimization
        if not toxic_hours.empty:
            bad_hours_list = [f"{h}:00" for h in toxic_hours['Hour'].tolist()]
            optimized_total = current_total - toxic_pnl_h # Subtracting negative = Adding value
            improvement_pct = ((optimized_total - current_total) / abs(current_total)) * 100 if current_total != 0 else 0
            
            time_insight += f"**⏳ Horas Tóxicas Detectadas:** {', '.join(bad_hours_list)}\n"
            time_insight += f"- 🛑 Estas horas te están costando **${abs(toxic_pnl_h):,.2f}**.\n"
            time_insight += f"- 💡 **Proyección:** Si dejas de operar en estas horas, tu Beneficio Neto subiría a **${optimized_total:,.2f}** (Mejora del +{improvement_pct:.1f}%).\n\n"
        else:
            time_insight += "✅ **Horario Limpio:** No tienes horas sistemáticamente perdedoras.\n\n"
            
        # Scenario B: Daily Optimization
        if not toxic_days.empty:
            bad_days_list = toxic_days.index.tolist()
            time_insight += f"**🗓️ Días Tóxicos Detectados:** {', '.join(bad_days_list)}\n"
            time_insight += f"- 🛑 Los {', '.join(bad_days_list)} restan **${abs(toxic_pnl_d):,.2f}** a tu cuenta.\n"
        else:
             time_insight += "✅ **Calendario Limpio:** Eres consistente todos los días de la semana.\n"

            
        st.info(f"""
        🧠 **Insight de Experto Quant: Edges Temporales**  
        {time_insight}
        
        *   **Acción:** Evita horas donde pierdes consistentemente. Los mejores traders *eliminan momentos tóxicos* en lugar de operar todo el día.
        *   **Dato:** Las horas de empalme de sesiones (9-11 AM ET, donde Europa todavía opera) suelen ser las más líquidas pero también más erráticas. Verifica si tus ganancias están ahí o en NY pure (11 AM-4 PM).
        """)
        
        # AI Analysis for Hourly Patterns
        hourly_stats_ai = df.groupby('Hour').agg({
            'PnL': ['sum', 'mean', 'count']
        }).round(2)
        
        show_ai_analysis(
            chart_name="Patrones Horarios",
            chart_type="hourly",
            data={"hourly_data": hourly_stats_ai.to_string()},
            key_suffix="tab7_hourly"
        )
    else:
        st.warning("No hay datos temporales para analizar.")

    # -------------------------------------------------------------------------
    # TAB 8: LEVEL ANALYSIS (New)
    # -------------------------------------------------------------------------
    with tab8:
        st.header("🧱 Análisis de Niveles y Zonas")
        
        # 1. Parsing Logic (v2.2.1: Support both "Europe Low" and "Europe Low 3 Days" formats)
        import re
        
        def parse_level_data(row):
            setup = str(row['SetupName'])
            
            # First try: Regex for "USA Low 3 Days" or "Asia High 1 Days" (with days)
            match_with_days = re.search(r'(Asia|Europe|USA)\s(High|Low)\s(\d+)\sDays', setup)
            if match_with_days:
                return pd.Series([f"{match_with_days.group(1)} {match_with_days.group(2)}", int(match_with_days.group(3))])
            
            # Second try: Simple format "Europe Low", "Asia High", "USA Low" (without days)
            match_simple = re.search(r'(Asia|Europe|USA)\s(High|Low)', setup, re.IGNORECASE)
            if match_simple:
                return pd.Series([f"{match_simple.group(1)} {match_simple.group(2)}", 0])  # 0 days = current day
            
            # Fallback
            return pd.Series(["Other", -1])

        # Apply parsing
        level_df = df.copy()
        level_df[['Zone', 'DaysAgo']] = level_df.apply(parse_level_data, axis=1)
        
        # Filter only Valid Level Trades
        level_df = level_df[level_df['Zone'] != "Other"]
        
        if level_df.empty:
            st.warning("⚠️ No se detectaron trades de Niveles (Formato 'Session High/Low' no encontrado).")
        else:
            # ================================================================
            # SECTION 1: ENHANCED PERFORMANCE DASHBOARD
            # ================================================================
            st.subheader("📊 Dashboard de Rendimiento por Zona")
            
            # Calculate comprehensive metrics per zone
            zone_metrics = level_df.groupby('Zone').agg({
                'PnL': ['sum', 'mean', 'std', 'count'],
                'Result': lambda x: (x.str.contains('TP', na=False)).sum()  # Wins
            }).round(2)
            
            zone_metrics.columns = ['Total_PnL', 'Avg_PnL', 'Std_PnL', 'Trades', 'Wins']
            zone_metrics['Win_Rate'] = (zone_metrics['Wins'] / zone_metrics['Trades'] * 100).round(1)
            zone_metrics['Sharpe_Proxy'] = (zone_metrics['Avg_PnL'] / zone_metrics['Std_PnL']).round(2)
            zone_metrics['Sharpe_Proxy'] = zone_metrics['Sharpe_Proxy'].replace([np.inf, -np.inf], 0)
            
            # Calculate R:R (Winners vs Losers)
            rr_data = []
            for zone in level_df['Zone'].unique():
                zone_trades = level_df[level_df['Zone'] == zone]
                wins = zone_trades[zone_trades['PnL'] > 0]['PnL'].mean()
                losses = abs(zone_trades[zone_trades['PnL'] <= 0]['PnL'].mean())
                rr = wins / losses if losses > 0 else 0
                rr_data.append({'Zone': zone, 'RR': round(rr, 2)})
            
            rr_df = pd.DataFrame(rr_data).set_index('Zone')
            zone_metrics = zone_metrics.join(rr_df)
            
            # Sort by Total PnL descending
            zone_metrics = zone_metrics.sort_values('Total_PnL', ascending=False)
            
            # Display formatted table
            display_metrics = zone_metrics[['Total_PnL', 'Win_Rate', 'RR', 'Trades', 'Avg_PnL', 'Sharpe_Proxy']].copy()
            display_metrics.columns = ['PnL Total ($)', 'Win Rate (%)', 'R:R', 'Trades', 'Avg Win ($)', 'Sharpe']
            
            st.dataframe(
                display_metrics.style.format({
                    'PnL Total ($)': '${:,.0f}',
                    'Win Rate (%)': '{:.1f}%',
                    'R:R': '{:.2f}',
                    'Avg Win ($)': '${:.0f}',
                    'Sharpe': '{:.2f}'
                }).background_gradient(cmap='RdYlGn', subset=['PnL Total ($)', 'Win Rate (%)']),
                use_container_width=True
            )
            
            # Auto-generated Insights (Section 1)
            best_zone = zone_metrics.index[0]
            worst_zone = zone_metrics.index[-1]
            best_wr = zone_metrics.loc[best_zone, 'Win_Rate']
            worst_wr = zone_metrics.loc[worst_zone, 'Win_Rate']
            best_pnl = zone_metrics.loc[best_zone, 'Total_PnL']
            worst_pnl = zone_metrics.loc[worst_zone, 'Total_PnL']
            
            insight_s1 = f"""
🧠 **Insight de Experto Quant: Jerarquía de Niveles**

**🏆 Tu Mejor Edge:** {best_zone}
- Win Rate: {best_wr:.1f}% | PnL: ${best_pnl:,.0f}
- **Acción:** Prioriza este setup. Considera aumentar tamaño de posición aquí.

**⚠️ Tu Mayor Lastre:** {worst_zone}
- Win Rate: {worst_wr:.1f}% | PnL: ${worst_pnl:,.0f}
- **Acción:** {"Filtra este nivel completamente" if worst_pnl < -200 else "Reduce frecuencia o ajusta lógica"}
            """
            
            # Check for Sharpe outliers
            high_sharpe = zone_metrics[zone_metrics['Sharpe_Proxy'] > 1.5]
            if not high_sharpe.empty:
                insight_s1 += f"\n\n**💎 Zonas Premium (Sharpe > 1.5):** {', '.join(high_sharpe.index.tolist())}"
                insight_s1 += "\n- Estas zonas tienen retorno/riesgo excepcional. Son tus *verdaderos edges*."
            
            st.info(insight_s1)
            
            
            # Prepare clean data for AI (Section 1)
            ai_perf_summary = "Dashboard de Rendimiento por Zona:\n\n"
            ai_perf_summary += "REGLA: PnL > 0 = RENTABLE (mantener). Sharpe > 1.5 = PREMIUM. Trades < 10 = Muestra insuficiente.\n\n"
            
            for zone in zone_metrics.index:
                data = zone_metrics.loc[zone]
                pnl = data['Total_PnL']
                wr = data['Win_Rate']
                rr = data['RR']
                trades = int(data['Trades'])
                sharpe = data['Sharpe_Proxy']
                
                verdict = "✅ RENTABLE" if pnl > 0 else "❌ PERDEDOR"
                if pnl > 500 and sharpe > 1.5:
                    verdict += " (PREMIUM)"
                if trades < 10:
                    verdict += " ⚠️ MUESTRA PEQUEÑA"
                
                ai_perf_summary += f"{zone}: PnL ${pnl:,.0f} ({verdict})\n"
                ai_perf_summary += f"  - Win Rate: {wr:.1f}%, R:R: {rr:.2f}, Trades: {trades}, Sharpe: {sharpe:.2f}\n"
            
            # AI Analysis Button (Premium)
            show_ai_analysis(
                chart_name="Dashboard de Rendimiento",
                chart_type="performance_dashboard",
                data={"zone_metrics": ai_perf_summary},
                key_suffix="tab8_section1"
            )
            
            st.markdown("---")
            
            # ================================================================
            # SECTION 1B: TEMPORAL DECAY (Full Width)
            # ================================================================
            st.subheader("⏳ Decaimiento Temporal por Zona")
            
            # Create pivot: Zone (Y) vs DaysAgo (X), values = PnL
            heatmap_data = level_df.pivot_table(
                index='Zone', 
                columns='DaysAgo', 
                values='PnL', 
                aggfunc='sum', 
                fill_value=0
            )
            
            # Sort zones by total PnL (best to worst)
            zone_totals = heatmap_data.sum(axis=1).sort_values(ascending=False)
            heatmap_data = heatmap_data.loc[zone_totals.index]
            
            # Create heatmap
            fig_days = go.Figure(data=go.Heatmap(
                z=heatmap_data.values,
                x=[f"{int(d)} días" if d > 0 else "Hoy" for d in heatmap_data.columns],
                y=heatmap_data.index,
                colorscale='RdBu',
                zmid=0,  # Center at 0 (red=loss, blue=profit)
                text=heatmap_data.values.round(0),
                texttemplate='$%{text}',
                textfont={"size": 10},
                hovertemplate='<b>%{y}</b><br>Antigüedad: %{x}<br>PnL: $%{z:.0f}<extra></extra>',
                colorbar=dict(title="PnL")
            ))
            
            fig_days = apply_premium_style(fig_days, "Rendimiento por Zona y Antigüedad")
            fig_days.update_layout(
                xaxis_title="Antigüedad del Nivel",
                yaxis_title="Zona",
                height=400
            )
            st.plotly_chart(fig_days, use_container_width=True)

            # ================================================================
            # SECTION 2: DIRECTIONALITY MATRIX
            # ================================================================
            st.markdown("---")
            st.subheader("🎯 Matriz Direccional: ¿Long o Short?")
            
            # Create pivot: Zone x Direction
            dir_matrix = level_df.pivot_table(
                index='Zone',
                columns='Type',
                values='PnL',
                aggfunc=['sum', lambda x: (level_df.loc[x.index, 'Result'].str.contains('TP', na=False)).sum(), 'count']
            )
            
            # Flatten columns
            dir_matrix.columns = ['_'.join(col).strip() for col in dir_matrix.columns.values]
            
            # Calculate Win Rates
            for direction in ['Long', 'Short']:
                if f'sum_{direction}' in dir_matrix.columns:
                    wins_col = f'<lambda>_{direction}'
                    total_col = f'count_{direction}'
                    if wins_col in dir_matrix.columns and total_col in dir_matrix.columns:
                        dir_matrix[f'WR_{direction}'] = (dir_matrix[wins_col] / dir_matrix[total_col] * 100).round(1)
            
            # Display in two columns
            col_dir1, col_dir2 = st.columns(2)
            
            with col_dir1:
                st.markdown("**PnL por Dirección**")
                pnl_display = dir_matrix[[col for col in dir_matrix.columns if col.startswith('sum_')]].copy()
                pnl_display.columns = [col.replace('sum_', '') for col in pnl_display.columns]
                st.dataframe(
                    pnl_display.style.format('${:,.0f}').background_gradient(cmap='RdYlGn', axis=None),
                    use_container_width=True
                )
            
            with col_dir2:
                st.markdown("**Win Rate (%) por Dirección**")
                wr_display = dir_matrix[[col for col in dir_matrix.columns if col.startswith('WR_')]].copy()
                wr_display.columns = [col.replace('WR_', '') for col in wr_display.columns]
                st.dataframe(
                    wr_display.style.format('{:.1f}%').background_gradient(cmap='RdYlGn', axis=None, vmin=0, vmax=100),
                    use_container_width=True
                )

            # Auto-generated Insights (Section 2)
            insight_s2 = "🧠 **Insight de Experto Quant: Bias Direccional**\n\n"
            
            directional_findings = []
            for zone in dir_matrix.index:
                long_wr = dir_matrix.loc[zone, 'WR_Long'] if 'WR_Long' in dir_matrix.columns else None
                short_wr = dir_matrix.loc[zone, 'WR_Short'] if 'WR_Short' in dir_matrix.columns else None
                
                # Case 1: Both directions have data
                if pd.notna(long_wr) and pd.notna(short_wr):
                    diff = abs(long_wr - short_wr)
                    if diff > 20:  # Significant bias
                        better_dir = "LONG" if long_wr > short_wr else "SHORT"
                        directional_findings.append(
                            f"**{zone}**: Sesgo claro hacia {better_dir} ({max(long_wr, short_wr):.1f}% vs {min(long_wr, short_wr):.1f}%)"
                        )
                
                # Case 2: Only LONG has data (100% bias)
                elif pd.notna(long_wr) and pd.isna(short_wr):
                    directional_findings.append(
                        f"**{zone}**: Solo opera LONG (WR: {long_wr:.1f}%) - Sesgo extremo"
                    )
                
                # Case 3: Only SHORT has data (100% bias)
                elif pd.isna(long_wr) and pd.notna(short_wr):
                    directional_findings.append(
                        f"**{zone}**: Solo opera SHORT (WR: {short_wr:.1f}%) - Sesgo extremo"
                    )
            
            if directional_findings:
                insight_s2 += "**Zonas con Bias Claro:**\n"
                for finding in directional_findings[:3]:  # Top 3
                    insight_s2 += f"- {finding}\n"
                insight_s2 += "\n**Acción:** Considera deshabilitar la dirección débil en estas zonas para mejorar consistencia."
            else:
                insight_s2 += "✅ **Balance Direccional:** Tus zonas funcionan de manera similar en ambas direcciones. Mantén flexibilidad."
            
            st.info(insight_s2)
            
            
            # Prepare clean data for AI
            ai_summary = "Rendimiento por Zona y Dirección:\n\n"
            ai_summary += "IMPORTANTE: Un Win Rate bajo con PnL POSITIVO es válido (R:R alto). No rechaces setups solo por WR bajo.\n\n"
            
            for zone in dir_matrix.index:
                long_pnl = dir_matrix.loc[zone, 'sum_Long'] if 'sum_Long' in dir_matrix.columns else None
                short_pnl = dir_matrix.loc[zone, 'sum_Short'] if 'sum_Short' in dir_matrix.columns else None
                long_wr = dir_matrix.loc[zone, 'WR_Long'] if 'WR_Long' in dir_matrix.columns else None
                short_wr = dir_matrix.loc[zone, 'WR_Short'] if 'WR_Short' in dir_matrix.columns else None
                
                ai_summary += f"{zone}:\n"
                if pd.notna(long_pnl):
                    verdict = "✅ RENTABLE" if long_pnl > 0 else "❌ PERDEDOR"
                    ai_summary += f"  - LONG: PnL ${long_pnl:,.0f} ({verdict}), Win Rate {long_wr:.1f}%\n"
                if pd.notna(short_pnl):
                    verdict = "✅ RENTABLE" if short_pnl > 0 else "❌ PERDEDOR"
                    ai_summary += f"  - SHORT: PnL ${short_pnl:,.0f} ({verdict}), Win Rate {short_wr:.1f}%\n"
                if pd.isna(long_pnl) and pd.notna(short_pnl):
                    ai_summary += f"  - Solo opera SHORT\n"
                elif pd.notna(long_pnl) and pd.isna(short_pnl):
                    ai_summary += f"  - Solo opera LONG\n"
            
            # AI Analysis Button (Premium)
            show_ai_analysis(
                chart_name="Matriz Direccional",
                chart_type="directionality_matrix",
                data={"dir_matrix": ai_summary},
                key_suffix="tab8_section2"
            )

            # ================================================================
            # SECTION 3: TEMPORAL PERFORMANCE (Zone x Hour)
            # ================================================================
            st.markdown("---")
            st.subheader("⏰ Rendimiento Temporal: Zona x Hora del Día")
            
            # Extract hour from EntryTime
            if 'EntryTime' in level_df.columns:
                level_df['Hour'] = pd.to_datetime(level_df['EntryTime']).dt.hour
                
                # Create pivot: Zone (Y) x Hour (X)
                temporal_pivot = level_df.pivot_table(
                    index='Zone',
                    columns='Hour',
                    values='PnL',
                    aggfunc='sum',
                    fill_value=0
                )
                
                # Sort zones by total
                temporal_pivot = temporal_pivot.loc[temporal_pivot.sum(axis=1).sort_values(ascending=False).index]
                
                # Create heatmap
                fig_temporal = go.Figure(data=go.Heatmap(
                    z=temporal_pivot.values,
                    x=[f"{int(h)}:00" for h in temporal_pivot.columns],
                    y=temporal_pivot.index,
                    colorscale='RdBu',
                    zmid=0,
                    text=temporal_pivot.values.round(0),
                    texttemplate='$%{text}',
                    textfont={"size": 9},
                    hovertemplate='<b>%{y}</b><br>Hora: %{x}<br>PnL: $%{z:.0f}<extra></extra>',
                    colorbar=dict(title="PnL")
                ))
                
                fig_temporal = apply_premium_style(fig_temporal, "Rendimiento por Zona y Hora")
                fig_temporal.update_layout(
                    xaxis_title="Hora del Día (ET)",
                    yaxis_title="Zona",
                    height=400
                )
                st.plotly_chart(fig_temporal, use_container_width=True)
                
                # Automated Toxic Time Detection
                toxic_combinations = []
                for zone in temporal_pivot.index:
                    for hour in temporal_pivot.columns:
                        pnl = temporal_pivot.loc[zone, hour]
                        if pnl < -100:  # Threshold for "toxic"
                            toxic_combinations.append({
                                'Zone': zone,
                                'Hour': f"{int(hour)}:00",
                                'Loss': f"${pnl:.0f}"
                            })
                
                if toxic_combinations:
                    st.warning(f"⚠️ **{len(toxic_combinations)} Combinaciones Tóxicas Detectadas** (Pérdida > $100):")
                    toxic_df = pd.DataFrame(toxic_combinations)
                    st.dataframe(toxic_df, use_container_width=True)
                    
                # Auto-generated Insights (Section 3)
                if toxic_combinations:
                    worst_combo = toxic_combinations[0]
                    insight_s3 = f"""
🧠 **Insight de Experto Quant: Ventanas Tóxicas**

**⚠️ Peor Combinación:** {worst_combo['Zone']} a las {worst_combo['Hour']}
- Pérdida: {worst_combo['Loss']}
- **Hipótesis:** Probablemente coincide con bajo volumen, empalme de sesiones, o datos económicos.
- **Acción:** Agrega en tu código: `if (zone == '{worst_combo['Zone'].split()[0]}' && hour == {worst_combo['Hour'].split(':')[0]}) return;`

**Patrón General:** Las ventanas tóxicas suelen ser:
- 12-13 PM (Lunch, bajo volumen)
- 15:30+ (Near close, comportamiento errático)
                    """
                    st.info(insight_s3)
                else:
                    st.success("✅ **Horario Limpio:** No se detectaron ventanas horarias sistemáticamente tóxicas.")
                
                # Prepare clean data for AI (Section 3)
                ai_temporal_summary = "Rendimiento Temporal (Zona x Hora):\n\n"
                ai_temporal_summary += "CONTEXTO: Asia 20-03 ET, Europe 03-12 ET, USA 09-16 ET. Lunch 12-13 ET (bajo volumen).\n"
                ai_temporal_summary += "REGLA: Ventana con PnL < -$100 = TÓXICA (filtrar). < 5 trades = ruido.\n\n"
                
                # Format top toxic combinations
                if toxic_combinations:
                    ai_temporal_summary += "VENTANAS TÓXICAS:\n"
                    for combo in toxic_combinations[:5]:  # Top 5
                        ai_temporal_summary += f"- {combo['Zone']} a las {combo['Hour']}: {combo['Loss']}\n"
                    ai_temporal_summary += f"\nTotal detectadas: {len(toxic_combinations)}\n"
                else:
                    ai_temporal_summary += "✅ No se detectaron ventanas tóxicas significativas.\n"
                
                # Add full matrix summary
                ai_temporal_summary += "\nMATRIZ COMPLETA (Zona x Hora con PnL):\n"
                for zone in temporal_pivot.index:
                    ai_temporal_summary += f"\n{zone}:\n"
                    for hour in temporal_pivot.columns:
                        pnl = temporal_pivot.loc[zone, hour]
                        if pnl != 0:
                            verdict = "TÓXICA" if pnl < -100 else ("RENTABLE" if pnl > 100 else "neutral")
                            ai_temporal_summary += f"  {int(hour)}:00-{int(hour)+1}:00 = ${pnl:.0f} ({verdict})\n"
                
                # AI Analysis Button (Premium)
                show_ai_analysis(
                    chart_name="Rendimiento Temporal",
                    chart_type="temporal_performance",
                    data={"temporal_data": ai_temporal_summary},
                    key_suffix="tab8_section3"
                )
                
            else:
                st.info("No hay información de hora en los datos para análisis temporal.")

            # --- DEEP INSIGHT: PENETRATION ANALYSIS ---
            st.markdown("---")
            st.subheader("🧪 Análisis de Penetración (Punto de No Retorno)")
            
            # Scatter: MAE (Penetration) vs PnL
            # We want to see: Is there a MAE value where NO trades win?
            
            level_df['Size_Fixed'] = 15 # Constant size for visibility
            
            fig_penetration = px.scatter(
                level_df, 
                x='MAE', 
                y='PnL', 
                color='Zone', 
                symbol='Zone', # V_ACCESSIBILITY: Shapes allow distinction without color
                size='Size_Fixed',
                size_max=15, 
                hover_data=['Result', 'SetupName', 'ExitDate'], 
                color_discrete_map={'Asia': '#FFFF00', 'Europe': '#4169E1', 'USA': '#FFFFFF'} # High Contrast (Yellow/Blue/White)
            )
            
            # Move legend to bottom to avoid clutter
            fig_penetration.update_layout(
                legend=dict(
                    orientation="h",
                    yanchor="bottom",
                    y=1.05, # Slightly higher to avoid title clash
                    xanchor="right",
                    x=1,
                    title=None # Remove legend title "Result"
                )
            )
            
            # Add a vertical line cursor or threshold? 
            # Let's calculate the "Death Line" -> The MAE percentile purely for Losers
            
            fig_penetration = apply_premium_style(fig_penetration, "Penetración de Nivel (MAE) vs Resultado")
            st.plotly_chart(fig_penetration, use_container_width=True)
            
            # Automated Heuristic for Penetration (v2.2.1: Use absolute MAE values)
            winners_l = level_df[level_df['PnL'] > 0].copy()
            losers_l = level_df[level_df['PnL'] <= 0].copy()
            
            pen_insight = ""
            
            if not winners_l.empty:
                # MAE comes as negative values, so we use absolute value for analysis
                winners_l['MAE_abs'] = winners_l['MAE'].abs()
                losers_l['MAE_abs'] = losers_l['MAE'].abs() if not losers_l.empty else 0
                
                max_mae_winner = winners_l['MAE_abs'].quantile(0.95)  # 95th percentile of winners MAE
                avg_mae_winner = winners_l['MAE_abs'].mean()
                avg_mae_loser = losers_l['MAE_abs'].mean() if not losers_l.empty else 0
                
                pen_insight += f"**🛡️ El Límite de Tolerancia:** El 95% de tus trades ganadores soportaron una penetración máxima de **${max_mae_winner:.2f} USD**.\n"
                pen_insight += f"- **Dolor Promedio Ganadores:** ${avg_mae_winner:.2f} | **Dolor Promedio Perdedores:** ${avg_mae_loser:.2f}\n"
                pen_insight += f"- **Interpretación:** Si el precio cruza el nivel y tu negativo flotante supera **${max_mae_winner:.2f}**, la probabilidad de recuperación cae drásticamente.\n"
                pen_insight += f"- **Acción Sugerida:** Considera tu Stop Loss técnico cerca de ${max_mae_winner:.2f}. Más allá = ruptura real, no cacería de liquidez.\n"
            else:
                pen_insight += "No hay suficientes trades ganadores para calcular un límite de tolerancia estadístico.\n"
                
            pen_insight += f"\n- **¿'Falso Quiebre' o 'Ruptura'?** Los puntos verdes muestran cuánto dolor aguantaron los trades que *funcionaron*.\n"
            pen_insight += f"- **Zona Muerta:** El espacio vacío a la *derecha* de los puntos verdes = zona donde el precio *rompe con intención*."
                                
            st.info(f"""
            🧠 **Insight de Experto Quant: Profundidad de Mercado**
            
            {pen_insight}
            """)
            
            # AI Analysis for Levels (Penetration + Stats)
            if 'zone_stats' in locals() and not zone_stats.empty:
                best_zone = zone_stats.iloc[0]['Zone']
                best_zone_pnl = zone_stats.iloc[0]['PnL']
                worst_zone = zone_stats.iloc[-1]['Zone']
                worst_zone_pnl = zone_stats.iloc[-1]['PnL']
                
                show_ai_analysis(
                    chart_name="Análisis de Niveles",
                    chart_type="levels_analysis",
                    data={
                        "best_zone": best_zone,
                        "best_zone_pnl": best_zone_pnl,
                        "worst_zone": worst_zone,
                        "worst_zone_pnl": worst_zone_pnl,
                        "total_pnl": zone_stats['PnL'].sum(),
                        "penetration_insight": pen_insight
                    },
                    key_suffix="tab8_levels"
                )

            # --- INTERACTION MATRIX (Who Breaks Who?) ---
            st.markdown("---")
            st.subheader("⚔️ Matriz de Interacción: Agresor vs Defensor")
            st.caption("¿Qué sesión (Agresor) es más efectiva rompiendo los niveles de quién (Defensor)?")
            
            # v2.2.1: Extract base session (Asia/Europe/USA) from zone names like "Europe Low"
            level_df['Session'] = level_df['Zone'].str.extract(r'(Asia|Europe|USA)', expand=False)
            
            # Filter valid sessions only
            matrix_df = level_df[level_df['Session'].isin(['Asia', 'Europe', 'USA'])].copy()
            
            if not matrix_df.empty and 'Aggressor' in matrix_df.columns:
                # v2.2.2: THREE HEATMAPS - General, Longs, Shorts
                
                # 1. GENERAL HEATMAP (All directions)
                st.markdown("### 🌐 Todos los Trades (Long + Short)")
                fig_matrix = px.density_heatmap(
                    matrix_df,
                    x='Session',
                    y='Aggressor',
                    z='PnL',
                    histfunc='sum',
                    color_continuous_scale='RdYlGn',
                    text_auto='.0f',
                )
                apply_premium_style(fig_matrix)
                fig_matrix.update_layout(
                    xaxis_title="Defensor",
                    yaxis_title="Atacante",
                    coloraxis_colorbar_title="PnL ($)",
                    height=350
                )
                st.plotly_chart(fig_matrix, use_container_width=True)
                
                # 2. SIDE BY SIDE: LONGS vs SHORTS
                col_hm1, col_hm2 = st.columns(2)
                
                # Filter data by direction
                long_df = matrix_df[matrix_df['Type'] == 'Long']
                short_df = matrix_df[matrix_df['Type'] == 'Short']
                
                with col_hm1:
                    st.markdown("### 📈 Solo LONGS")
                    if not long_df.empty:
                        fig_long = px.density_heatmap(
                            long_df,
                            x='Session',
                            y='Aggressor',
                            z='PnL',
                            histfunc='sum',
                            color_continuous_scale='RdYlGn',
                            text_auto='.0f',
                        )
                        apply_premium_style(fig_long)
                        fig_long.update_layout(
                            xaxis_title="Defensor",
                            yaxis_title="Atacante",
                            coloraxis_showscale=False,
                            height=300
                        )
                        st.plotly_chart(fig_long, use_container_width=True)
                    else:
                        st.info("No hay datos de Longs")
                
                with col_hm2:
                    st.markdown("### 📉 Solo SHORTS")
                    if not short_df.empty:
                        fig_short = px.density_heatmap(
                            short_df,
                            x='Session',
                            y='Aggressor',
                            z='PnL',
                            histfunc='sum',
                            color_continuous_scale='RdYlGn',
                            text_auto='.0f',
                        )
                        apply_premium_style(fig_short)
                        fig_short.update_layout(
                            xaxis_title="Defensor",
                            yaxis_title="Atacante",
                            coloraxis_showscale=False,
                            height=300
                        )
                        st.plotly_chart(fig_short, use_container_width=True)
                    else:
                        st.info("No hay datos de Shorts para la matriz.")
                
                st.markdown("---")
                
                try:
                    # Insight Generation
                    combo_stats = matrix_df.groupby(['Aggressor', 'Session']).agg({
                        'PnL': ['sum', 'count', 'mean']
                    }).reset_index()
                    combo_stats.columns = ['Sesión_Trading', 'Nivel_Origen', 'PnL_Total', 'Trades', 'PnL_Promedio']
                    
                    best = combo_stats.loc[combo_stats['PnL_Total'].idxmax()]
                    worst = combo_stats.loc[combo_stats['PnL_Total'].idxmin()]
                    
                    # v2.2.3: Insights más claros y accionables
                    st.markdown("### 💡 Interpretación de la Matriz")
                    st.info("""
**¿Cómo leer esto?**
- **Sesión de Trading** = Cuándo TÚ operas (ej: durante horario USA)
- **Nivel de Origen** = De qué sesión es el nivel (ej: Low creado en Asia)
- **PnL Positivo** = Esa combinación funciona para ti (el nivel se respeta)
- **PnL Negativo** = Esa combinación NO funciona (el nivel falla o tu timing es malo)
                    """)
                    
                    st.success(f"""🎯 **MEJOR Combinación:** Operar durante **{best['Sesión_Trading']}** en niveles de **{best['Nivel_Origen']}**
→ Ganancia: ${best['PnL_Total']:,.0f} en {int(best['Trades'])} trades (${best['PnL_Promedio']:.2f}/trade)
→ **Acción:** Prioriza esta combinación""")
                    
                    st.error(f"""⚠️ **PEOR Combinación:** Operar durante **{worst['Sesión_Trading']}** en niveles de **{worst['Nivel_Origen']}**
→ Pérdida: ${worst['PnL_Total']:,.0f} en {int(worst['Trades'])} trades
→ **Acción:** Considera BLOQUEAR esta combinación en tu estrategia""")
                    
                    # Table summary
                    st.markdown("### 📊 Resumen Completo")
                    st.dataframe(combo_stats.sort_values('PnL_Total', ascending=False).style.format({
                        'PnL_Total': '${:,.0f}',
                        'PnL_Promedio': '${:.2f}'
                    }), use_container_width=True)
                    
                    # AI Analysis for Interaction Matrix
                    if 'combo_stats' in locals() and not combo_stats.empty:
                        best_combo = f"{combo_stats.iloc[0]['Sesión_Trading']} atacando {combo_stats.iloc[0]['Nivel_Origen']}"
                        best_combo_pnl = combo_stats.iloc[0]['PnL_Total']
                        worst_combo = f"{combo_stats.iloc[-1]['Sesión_Trading']} atacando {combo_stats.iloc[-1]['Nivel_Origen']}"
                        worst_combo_pnl = combo_stats.iloc[-1]['PnL_Total']
                        
                        show_ai_analysis(
                            chart_name="Matriz de Interacción",
                            chart_type="interaction_matrix",
                            data={
                                "best_combo": best_combo,
                                "best_pnl": best_combo_pnl,
                                "worst_combo": worst_combo,
                                "worst_pnl": worst_combo_pnl
                            },
                            key_suffix="tab8_matrix"
                        )
                    
                    # v2.2.2: GRANULAR ANALYSIS BY DIRECTION (Long vs Short)
                    st.markdown("---")
                    st.subheader("🎯 Análisis por Dirección (Long vs Short)")
                    st.caption("⚡ La misma combinación puede ser rentable en LONG pero tóxica en SHORT (o viceversa)")
                    
                    # Group by Attacker, Defender AND Type (Long/Short)
                    direction_stats = matrix_df.groupby(['Aggressor', 'Session', 'Type']).agg({
                        'PnL': ['sum', 'count', 'mean']
                    }).reset_index()
                    direction_stats.columns = ['Sesión_Trading', 'Nivel_Origen', 'Dirección', 'PnL_Total', 'Trades', 'PnL_Promedio']
                    direction_stats = direction_stats.sort_values('PnL_Total', ascending=False)
                    
                    # Color-coded table
                    def color_pnl(val):
                        if isinstance(val, (int, float)):
                            return 'color: #00FF00' if val >= 0 else 'color: #FF4444'
                        return ''
                    
                    st.dataframe(direction_stats.style.format({
                        'PnL_Total': '${:,.0f}',
                        'PnL_Promedio': '${:.2f}'
                    }).applymap(color_pnl, subset=['PnL_Total', 'PnL_Promedio']), use_container_width=True)
                    
                    # Find best/worst by direction
                    best_long = direction_stats[direction_stats['Dirección'] == 'Long'].head(1)
                    worst_long = direction_stats[direction_stats['Dirección'] == 'Long'].tail(1)
                    best_short = direction_stats[direction_stats['Dirección'] == 'Short'].head(1)
                    worst_short = direction_stats[direction_stats['Dirección'] == 'Short'].tail(1)
                    
                    col_dir1, col_dir2 = st.columns(2)
                    with col_dir1:
                        st.markdown("**📈 LONGS - Recomendaciones**")
                        if not best_long.empty:
                            b = best_long.iloc[0]
                            st.success(f"✅ Operar LONG durante {b['Sesión_Trading']} en niveles {b['Nivel_Origen']}: ${b['PnL_Total']:,.0f}")
                        if not worst_long.empty:
                            w = worst_long.iloc[0]
                            if w['PnL_Total'] < 0:
                                st.error(f"❌ EVITAR LONG durante {w['Sesión_Trading']} en niveles {w['Nivel_Origen']}: ${w['PnL_Total']:,.0f}")
                    
                    with col_dir2:
                        st.markdown("**📉 SHORTS - Recomendaciones**")
                        if not best_short.empty:
                            b = best_short.iloc[0]
                            st.success(f"✅ Operar SHORT durante {b['Sesión_Trading']} en niveles {b['Nivel_Origen']}: ${b['PnL_Total']:,.0f}")
                        if not worst_short.empty:
                            w = worst_short.iloc[0]
                            if w['PnL_Total'] < 0:
                                st.error(f"❌ EVITAR SHORT durante {w['Sesión_Trading']} en niveles {w['Nivel_Origen']}: ${w['PnL_Total']:,.0f}")
                    
                except Exception as e:
                    st.warning(f"Error generando insights: {e}")
            else:
                st.info("No hay suficientes datos de zonas para generar la matriz.")

            # ================================================================
            # SECTION 5: TOXIC COMBINATION FILTERS
            # ================================================================
            st.markdown("---")
            st.subheader("🔥 Filtro de Combinaciones Tóxicas")
            st.caption("Identifica patrones multi-variables que generan pérdidas sistemáticas")
            
            # Create comprehensive combination table
            filter_df = level_df.copy()
            filter_df['Hour_Bracket'] = pd.to_datetime(filter_df['EntryTime']).dt.hour.apply(
                lambda x: f"{x}:00-{x+1}:00"
            )
            
            # Group by Zone + Direction + Hour
            combo_analysis = filter_df.groupby(['Zone', 'Type', 'Hour_Bracket']).agg({
                'PnL': ['sum', 'count', 'mean'],
                'Result': lambda x: (x.str.contains('TP', na=False)).sum()
            }).round(2)
            
            combo_analysis.columns = ['Total_PnL', 'Trades', 'Avg_PnL', 'Wins']
            combo_analysis['Win_Rate'] = (combo_analysis['Wins'] / combo_analysis['Trades'] * 100).round(1)
            combo_analysis = combo_analysis.reset_index()
            
            # Sort by worst performers
            combo_analysis = combo_analysis.sort_values('Total_PnL')
            
            # Show top toxic combinations
            toxic_combos = combo_analysis[combo_analysis['Total_PnL'] < 0].head(10)
            
            if not toxic_combos.empty:
                st.dataframe(
                    toxic_combos.style.format({
                        'Total_PnL': '${:,.0f}',
                        'Avg_PnL': '${:.0f}',
                        'Win_Rate': '{:.1f}%'
                    }).background_gradient(cmap='Reds', subset=['Total_PnL', 'Win_Rate']),
                    use_container_width=True
                )
                
                # Auto-generated Insights (Section 5)
                if not toxic_combos.empty:
                    worst_combo = toxic_combos.iloc[0]
                    insight_s5 = f"""
🧠 **Insight de Experto Quant: Filtros Multi-Variable**

**🔴 Peor Patron:** {worst_combo['Zone']} {worst_combo['Type']} durante {worst_combo['Hour_Bracket']}
- Pérdida Total: ${worst_combo['Total_PnL']:,.0f} en {int(worst_combo['Trades'])} trades
- Win Rate: {worst_combo['Win_Rate']:.1f}%

**Código Sugerido (C#):**
```csharp
// En tu método de validación de entrada:
if (setupZone == "{worst_combo['Zone'].split()[0]}" && 
    entryDirection == Position.{worst_combo['Type']} && 
    Time[0].Hour >= {worst_combo['Hour_Bracket'].split(':')[0]} && 
    Time[0].Hour < {int(worst_combo['Hour_Bracket'].split(':')[0]) + 1})
{{
    Print("Filtro Tóxico activado - Trade cancelado");
    return;
}}
```

**Impacto Estimado:** Eliminar estos {int(toxic_combos.head(3)['Trades'].sum())} trades tóxicos mejoraría tu PnL en ${abs(toxic_combos.head(3)['Total_PnL'].sum()):,.0f}
                    """
                    st.info(insight_s5)
                else:
                    st.success("✅ **Limpio:** No se encontraron patrones multi-variable tóxicos.")
                
                # Prepare clean data for AI (Section 5)
                ai_toxic_summary = "Análisis de Combinaciones Tóxicas (Zona+Dirección+Hora):\n\n"
                ai_toxic_summary += "REGLA: Combos con <5 trades = ruido. Combos con >10 trades y PnL muy negativo = SISTEMÁTICO (filtrar).\n\n"
                
                if not toxic_combos.empty:
                    ai_toxic_summary += f"TOP {min(len(toxic_combos), 10)} PEORES COMBINACIONES:\n\n"
                    for idx, row in toxic_combos.head(10).iterrows():
                        zone = row['Zone']
                        direction = row['Type']
                        hour = row['Hour_Bracket']
                        pnl = row['Total_PnL']
                        trades = int(row['Trades'])
                        wr = row['Win_Rate']
                        
                        verdict = "⚠️ RUIDO" if trades < 5 else ("🔴 TÓXICO SISTEMÁTICO" if trades >= 10 else "⚠️ MONITOREAR")
                        
                        ai_toxic_summary += f"{idx+1}. {zone} {direction} {hour}\n"
                        ai_toxic_summary += f"   PnL: ${pnl:,.0f}, Trades: {trades}, WR: {wr:.1f}% ({verdict})\n\n"
                    
                    # Pattern analysis
                    ai_toxic_summary += "\nPATRONES DETECTADOS:\n"
                    zone_counts = toxic_combos['Zone'].value_counts()
                    hour_counts = toxic_combos['Hour_Bracket'].value_counts()
                    
                    if not zone_counts.empty:
                        ai_toxic_summary += f"- Zona más problemática: {zone_counts.index[0]} ({zone_counts.iloc[0]} combos tóxicos)\n"
                    if not hour_counts.empty:
                        ai_toxic_summary += f"- Hora más problemática: {hour_counts.index[0]} ({hour_counts.iloc[0]} combos tóxicos)\n"
                    
                    total_impact = abs(toxic_combos.head(5)['Total_PnL'].sum())
                    ai_toxic_summary += f"\nIMPACTO: Filtrar top 5 combos mejoraría PnL en ${total_impact:,.0f}\n"
                else:
                    ai_toxic_summary += "✅ No se detectaron combinaciones multi-variable tóxicas.\n"
                
                # AI Analysis Button (Premium)
                show_ai_analysis(
                    chart_name="Combinaciones Tóxicas",
                    chart_type="toxic_combinations",
                    data={"toxic_combos": ai_toxic_summary},
                    key_suffix="tab8_section5"
                )
                
            else:
                st.success("✅ No se detectaron combinaciones tóxicas significativas")

            # ================================================================
            # SECTION 6: ACTIONABLE RECOMMENDATIONS
            # ================================================================
            st.markdown("---")
            st.subheader("✅ Recomendaciones Accionables para Código")
            
            avoid_list = []
            prioritize_list = []
            
            # Analyze zone performance
            for zone in zone_metrics.index:
                zone_data = zone_metrics.loc[zone]
                
                # Toxic zones (WR < 40% and negative PnL)
                if zone_data['Win_Rate'] < 40 and zone_data['Total_PnL'] < 0:
                    avoid_list.append(f"🚫 **{zone}**: WR {zone_data['Win_Rate']:.1f}%, PnL ${zone_data['Total_PnL']:.0f}")
                
                # Premium zones (WR > 60% and positive PnL)
                elif zone_data['Win_Rate'] > 60 and zone_data['Total_PnL'] > 500:
                    prioritize_list.append(f"✅ **{zone}**: WR {zone_data['Win_Rate']:.1f}%, PnL ${zone_data['Total_PnL']:.0f}, R:R {zone_data['RR']:.2f}")
            
            # Add directional insights
            if 'dir_matrix' in locals():
                for direction in ['Long', 'Short']:
                    wr_col = f'WR_{direction}'
                    pnl_col = f'sum_{direction}'
                    
                    if wr_col in dir_matrix.columns and pnl_col in dir_matrix.columns:
                        for zone in dir_matrix.index:
                            wr = dir_matrix.loc[zone, wr_col]
                            pnl = dir_matrix.loc[zone, pnl_col]
                            
                            if pd.notna(wr) and pd.notna(pnl):
                                if wr < 30 and pnl < -200:
                                    avoid_list.append(f"🚫 **{zone} {direction}**: WR {wr:.1f}%, Pérdida ${pnl:.0f}")
            
            col_rec1, col_rec2 = st.columns(2)
            
            with col_rec1:
                st.markdown("### 🚫 EVITAR (Filtros Sugeridos)")
                if avoid_list:
                    for item in avoid_list[:5]:  # Top 5
                        st.markdown(item)
                else:
                    st.success("✅ No hay patrones tóxicos claros para filtrar")
            
            with col_rec2:
                st.markdown("### ✅ PRIORIZAR (Edges Confirmados)")
                if prioritize_list:
                    for item in prioritize_list[:5]:  # Top 5
                        st.markdown(item)
                else:
                    st.info("ℹ️ Ejecuta más trades para identificar edges claros")

    # -------------------------------------------------------------------------
    # TAB 9: LIVE vs BACKTEST (Reality Check)
    # -------------------------------------------------------------------------
    with tab9:
        st.header("🆚 Realidad (Live) vs Expectativa (Backtest)")
        
        # 1. Load Backtest Data (Benchmark)
        # Assuming Data Source logic provided 'df' as the ACTIVE data (likely Live).
        # We need to explicitly load 'backtest_log.csv' relative to the active file path
        
        try:
            # Construct Backtest Path
            # Base logic: If current is live_log, look for backtest_log.csv in same dir
            # Or assume standard path.
            base_dir_bt = os.path.dirname(data_path)
            # Try specific pattern or default legacy name
            bt_files = glob.glob(os.path.join(base_dir_bt, "backtest_log*.csv"))
            
            df_bt = None
            if bt_files:
                # Use the most recent or largest? Let's use the first one found or specific one if user matches setup
                # Ideally we want a 'Benchmark' file. Let's pick the largest one assuming it has the most history
                bt_file = max(bt_files, key=os.path.getsize)
                df_bt = load_and_process_data(bt_file, license_tier)
            
            if df_bt is None or df_bt.empty:
                st.warning("⚠️ No se encontró un archivo de Backtest (`backtest_log*.csv`) para comparar. Ejecuta una simulación primero.")
            else:
                # 2. METRIC RADAR (Deviation)
                st.subheader("📡 Radar de Desviación")
                
                # Calculate metrics for both
                def calc_stats(d):
                    total_pnl = d['PnL'].sum()
                    wins = d[d['PnL'] > 0]
                    losses = d[d['PnL'] <= 0]
                    win_rate = (len(wins) / len(d) * 100) if not d.empty else 0
                    avg_win = wins['PnL'].mean() if not wins.empty else 0
                    avg_loss = losses['PnL'].mean() if not losses.empty else 0
                    pf = abs(wins['PnL'].sum() / losses['PnL'].sum()) if losses['PnL'].sum() != 0 else 0
                    return total_pnl, win_rate, pf, avg_win, avg_loss
                
                l_total, l_wr, l_pf, l_aw, l_al = calc_stats(df) # Live (Active)
                b_total, b_wr, b_pf, b_aw, b_al = calc_stats(df_bt) # Backtest
                
                # Create Delta DataFrame
                delta_data = {
                    'Métrica': ['Win Rate', 'Profit Factor', 'Avg Win', 'Avg Loss'],
                    'Backtest (Expectativa)': [f"{b_wr:.1f}%", f"{b_pf:.2f}", f"${b_aw:.2f}", f"${b_al:.2f}"],
                    'Live (Realidad)': [f"{l_wr:.1f}%", f"{l_pf:.2f}", f"${l_aw:.2f}", f"${l_al:.2f}"],
                    'Desviación': [
                        f"{l_wr - b_wr:.1f}%",
                        f"{(l_pf - b_pf) / b_pf * 100:.1f}%" if b_pf != 0 else "0%",
                        f"{(l_aw - b_aw) / b_aw * 100:.1f}%" if b_aw != 0 else "0%",
                        f"{(l_al - b_al) / b_al * 100:.1f}%" if b_al != 0 else "0%"
                    ]
                }
                st.table(pd.DataFrame(delta_data))
                
                # 3. EQUITY TUNNEL (Overlay)
                st.subheader("📉 Túnel de Realidad (Curvas Superpuestas)")
                st.markdown("Comparando la forma de la curva. Ambas se normalizan iniciando en 0.")
                
                # Normalize Data for Plotting (Cumulative PnL starting at 0)
                df_bt_eq = df_bt.sort_values('ExitTime').reset_index(drop=True)
                df_bt_eq['CumPnL'] = df_bt_eq['PnL'].cumsum()
                df_bt_eq['TradeNum'] = df_bt_eq.index + 1
                
                df_live_eq = df.sort_values('ExitTime').reset_index(drop=True)
                df_live_eq['CumPnL'] = df_live_eq['PnL'].cumsum()
                df_live_eq['TradeNum'] = df_live_eq.index + 1
                
                # Create traces manually for generic X axis (Trade Number) to compare shapes regardless of dates
                import plotly.graph_objects as go
                fig_overlay = go.Figure()
                
                # Backtest (Shadow)
                fig_overlay.add_trace(go.Scatter(
                    x=df_bt_eq['TradeNum'], 
                    y=df_bt_eq['CumPnL'], 
                    mode='lines', 
                    name='Backtest (Benchmark)',
                    line=dict(color='gray', width=2, dash='dash')
                ))
                
                # Live (Reality)
                fig_overlay.add_trace(go.Scatter(
                    x=df_live_eq['TradeNum'], 
                    y=df_live_eq['CumPnL'], 
                    mode='lines', 
                    name='Live (Real)',
                    line=dict(color='#00FFAA', width=3)
                ))
                
                fig_overlay = apply_premium_style(fig_overlay, "Convergencia de Curvas (Por # de Trade)")
                st.plotly_chart(fig_overlay, use_container_width=True)
                
                # 4. Z-SCORE ANOMALY
                # Is current performance statistically impossible?
                # Sliding Window Z-Score on PnL
                
                window = 20
                if len(df_bt) > window and len(df) > 5:
                    # Calculate Rolling Mean/Std of BACKTEST
                    bt_rolling_mean = df_bt['PnL'].rolling(window).mean()
                    bt_rolling_std = df_bt['PnL'].rolling(window).std()
                    
                    # Get "Safe Bounds" from Backtest (e.g., 2 Std Devs via distribution)
                    bt_mean_global = df_bt['PnL'].mean()
                    bt_std_global = df_bt['PnL'].std()
                    lower_bound = bt_mean_global - (2 * bt_std_global)
                    
                    # Check recent Live Performance
                    recent_live_avg = df['PnL'].tail(10).mean()
                    current_z_score = (recent_live_avg - bt_mean_global) / bt_std_global
                    
                    anomaly_status = "✅ Normal"
                    color_status = "green"
                    if current_z_score < -2:
                        anomaly_status = "🚨 ANOMALÍA NEGATIVA (Falla de Sistema)"
                        color_status = "red"
                    elif current_z_score > 2:
                        anomaly_status = "⚠️ ANOMALÍA POSITIVA (Suerte Inusual)"
                        color_status = "orange"
                        
                    st.info(f"""
                    🧠 **Insight de Validación (Z-Score)**
                    
                    *   **Estado de Salud:** :{color_status}[{anomaly_status}] (Z-Score: {current_z_score:.2f})
                    *   **Diagnóstico:** Tu promedio reciente de PnL (${recent_live_avg:.2f}) se compara contra la media histórica (${bt_mean_global:.2f} ± ${bt_std_global:.2f}).
                    *   **Interpretación:** Si el Z-Score cae bajo -2.0, tu rendimiento actual es estadísticamente *peor* que el 95% de tu historia simulada. **Peligro de ruptura de modelo.**
                    """)

        except Exception as e:
            st.error(f"Error en comparación Reality Check: {e}")
    
    # =========================================================================
    # TAB 10: EXECUTIVE REPORT IA
    # =========================================================================
    with tab10:
        st.title("🎯 Reporte Ejecutivo IA")
        st.caption("Compilación estratégica de todos los análisis con recomendaciones accionables")
        
        # Auto-generate report on first load or if data changed
        if 'executive_report' not in st.session_state or st.button("🔄 Regenerar Reporte", key="regen_exec_report"):
            with st.spinner("📊 Compilando análisis global..."):
                try:
                    # Generate the report (returns tuple: report_text, r_df, scaling_df)
                    report_text, r_df, scaling_df = generate_executive_report(df)
                    st.session_state['executive_report'] = report_text
                    st.session_state['r_ladder_df'] = r_df  # Store R-Ladder DataFrame
                    st.session_state['scaling_df'] = scaling_df  # Store Scaling Comparison DataFrame
                    st.session_state['report_timestamp'] = datetime.now().strftime('%Y-%m-%d %H:%M')
                except Exception as e:
                    st.error(f"Error generando reporte: {e}")
                    st.session_state['executive_report'] = None
                    st.session_state['r_ladder_df'] = None
                    st.session_state['scaling_df'] = None
        
        # Show report if exists
        if 'executive_report' in st.session_state and st.session_state['executive_report']:
            # Header with export button
            col_header1, col_header2 = st.columns([4, 1])
            
            with col_header1:
                st.success(f"✅ Reporte generado: {st.session_state.get('report_timestamp', 'N/A')}")
            
            with col_header2:
                # Export button
                st.download_button(
                    label="📥 Exportar",
                    data=st.session_state['executive_report'],
                    file_name=f"executive_report_{datetime.now().strftime('%Y%m%d_%H%M')}.txt",
                    mime="text/plain",
                    key="download_exec_report"
                )
            
            st.markdown("---")
            
            # Display report in monospace font for better formatting
            st.markdown(f"```\n{st.session_state['executive_report']}\n```")
            
            # NEW: R-Ladder Visualization
            if 'r_ladder_df' in st.session_state and st.session_state['r_ladder_df'] is not None:
                st.markdown("---")
                st.subheader("📊 Visualización R-Ladder")
                st.caption("Análisis interactivo de niveles R alcanzados")
                
                fig_r_ladder = plot_r_ladder_chart(st.session_state['r_ladder_df'])
                if fig_r_ladder:
                    st.plotly_chart(fig_r_ladder, use_container_width=True)
                    
                    # AI Analysis for R-Ladder
                    r_df = st.session_state['r_ladder_df']
                    
                    # Helper to safe get value
                    def get_r_val(r_str, col):
                        row = r_df[r_df['R_Level'] == r_str]
                        return row[col].values[0] if not row.empty else 0
                    
                    show_ai_analysis(
                        chart_name="Potencial de Runners (R-Ladder)",
                        chart_type="r_ladder",
                        data={
                            "reached_1r": get_r_val('1R', 'Percent_Reached'),
                            "reached_5r": get_r_val('5R', 'Percent_Reached'),
                            "reached_10r": get_r_val('10R', 'Percent_Reached'),
                            "reached_20r": get_r_val('20R', 'Percent_Reached'),
                            "pnl_5r": get_r_val('5R', 'Cumulative_PnL'),
                            "pnl_10r": get_r_val('10R', 'Cumulative_PnL'),
                            "pnl_20r": get_r_val('20R', 'Cumulative_PnL')
                        },
                        key_suffix="tab10_rladder"
                    )
                
                # Show detailed table in expander
                with st.expander("📋 Ver Tabla Detallada R-Ladder"):
                    st.dataframe(
                        st.session_state['r_ladder_df'][['R_Level', 'Trades_Reached', 'Percent_Reached', 'Potential_PnL', 'Cumulative_PnL']],
                        use_container_width=True,
                        hide_index=True
                    )
            
            # Optional: AI analysis of the full report
            analyzer = get_analyzer()
            if analyzer:
                st.markdown("---")
                if st.button("🤖  Análisis IA Profundo del Reporte", key="ai_full_report"):
                    with st.spinner("Analizando reporte completo con IA..."):
                        try:
                            # For full report, we'll use a specific prompt
                            prompt = f"""Eres un experto trader cuantitativo. Analiza este reporte ejecutivo completo y provee:
1. Validación de las conclusiones
2. Insights adicionales no mencionados
3. Sugerencias de optimización avanzadas
4. Advertencias sobre posibles sesgos en los datos

REPORTE:
{st.session_state['executive_report']}
"""
                            response = analyzer.model_full.generate_content(prompt)
                            
                            # TRACK COST & USAGE
                            usage = response.usage_metadata
                            if usage:
                                # Gemini 1.5 Pro Pricing: $1.25/1M input, $5.00/1M output
                                input_cost = (usage.prompt_token_count / 1_000_000) * 1.25
                                output_cost = (usage.candidates_token_count / 1_000_000) * 5.00
                                total_cost = input_cost + output_cost
                                
                                # Update Session State & Persistence
                                if 'ai_usage_stats' not in st.session_state:
                                    st.session_state.ai_usage_stats = {'cost': 0.0, 'tokens': 0}
                                
                                st.session_state.ai_usage_stats['cost'] += total_cost
                                st.session_state.ai_usage_stats['tokens'] += usage.total_token_count
                                
                                update_usage_history(total_cost, usage.total_token_count)
                                
                                # Display Cost Widget
                                st.caption(f"💰 Costo: ${total_cost:.4f} | Tokens: {usage.total_token_count} (Modelo: Gemini Pro)")

                            st.markdown("### 🤖 Análisis IA Profundo:")
                            st.markdown(response.text)
                        except Exception as e:
                            st.warning(f"Error en análisis IA: {e}")

            st.markdown("---")
            st.subheader("⚙️ Configuración Automática para NinjaTrader")
            st.caption("Guarda estos ajustes para que la Estrategia los cargue automáticamente (requiere activar 'Auto Load AI Config' en NinjaTrader).")
            
            with st.form("ai_config_form"):
                col_cfg1, col_cfg2 = st.columns(2)
                with col_cfg1:
                    zones_input = st.text_area("Zonas Habilitadas (Separadas por comas, Ej: Asia High, USA Low)", value="", help="Deja vacío para habilitar todas.")
                with col_cfg2:
                    max_age_input = st.number_input("Edad Máxima Niveles (Días)", min_value=0, max_value=365, value=0, help="0 = Sin límite")
                
                submitted = st.form_submit_button("💾 Guardar Archivo ai_config.json")
                
                if submitted:
                    import json
                    config_data = {
                        "enabled_zones": [z.strip() for z in zones_input.split(',') if z.strip()],
                        "max_age": int(max_age_input),
                        "generated_at": datetime.now().strftime('%Y-%m-%d %H:%M:%S')
                    }
                    
                    try:
                        with open("ai_config.json", "w") as f:
                            json.dump(config_data, f, indent=4)
                        st.success("✅ Archivo ai_config.json guardado exitosamente! NinjaTrader lo leerá al reiniciar la estrategia.")
                        st.code(json.dumps(config_data, indent=4), language="json")
                    except Exception as e:
                        st.error(f"Error guardando archivo: {e}")
        else:
            st.info("⏳ Generando reporte automáticamente...")

