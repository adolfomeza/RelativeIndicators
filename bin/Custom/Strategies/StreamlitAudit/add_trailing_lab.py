#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Script para agregar el código del Trailing Lab de forma segura
Evita problemas de encoding de PowerShell
"""

# Código del tab9 a agregar
TAB9_CODE = '''

# ========================================================================================
# TAB 9: TRAILING STOP LABORATORY (v2.25)
# ========================================================================================

with tab9:
    st.markdown("## 🔬 Laboratorio de Trailing Stop")
    st.markdown("Análisis comparativo de estrategias de trailing stop para optimizar TP2")
    
    # Filter TP2 trades that exited via SL
    if 'ExitReason' in df.columns and 'MFE' in df.columns:
        tp2_sl_trades = df[
            (df['ExitReason'].str.contains('SL_', na=False)) &
            (df['MFE'] > 0)
        ].copy()
        
        if len(tp2_sl_trades) == 0:
            st.warning("⚠️ No hay trades de TP2 que salieron por SL con MFE positivo.")
        else:
            st.success(f"📊 {len(tp2_sl_trades)} trades encontrados para análisis")
            
            with st.expander("⚙️ Configuración", expanded=True):
                col1, col2 = st.columns(2)
                
                with col1:
                    st.markdown("**Métodos Activos**")
                    use_porcentual = st.checkbox("Trailing Porcentual", value=True)
                    use_retroceso = st.checkbox("Trailing Retroceso", value=True)
                
                with col2:
                    st.markdown("**Parámetros**")
                    if use_porcentual:
                        pct = st.slider("Porcentaje a mantener", 30, 70, 50, 5)
                    if use_retroceso:
                        activation = st.slider("Activación (ticks)", 10, 30, 15, 5)
                        retrace = st.slider("Retroceso (ticks)", 5, 15, 10, 1)
                    
                    initial_sl = st.number_input("SL Inicial (ticks)", 8, 20, 12)
            
            if st.button("🚀 Ejecutar Simulación", type="primary"):
                results = []
                
                for idx, trade in tp2_sl_trades.iterrows():
                    entry = trade.get('EntryPrice', 0)
                    exit_p = trade.get('ExitPrice', 0)
                    mfe = trade.get('MFE', 0)
                    mae = trade.get('MAE', 0)
                    direction = trade.get('Type', 'Long')
                    actual_pnl = trade.get('PnL', 0)
                    
                    try:
                        path = reconstruct_price_path(entry, mfe, mae, exit_p, direction)
                        trail_results = {'TradeID': idx, 'Actual_PnL': actual_pnl}
                        
                        if use_porcentual:
                            exit_price, reason = simulate_trailing_porcentual(
                                entry, path, direction, pct, initial_sl)
                            pnl = calculate_pnl(entry, exit_price, direction)
                            trail_results['Porcentual_PnL'] = pnl
                            trail_results['Porcentual_Improvement'] = pnl - actual_pnl
                        
                        if use_retroceso:
                            exit_price, reason = simulate_trailing_retroceso(
                                entry, path, direction, activation, retrace, initial_sl)
                            pnl = calculate_pnl(entry, exit_price, direction)
                            trail_results['Retroceso_PnL'] = pnl
                            trail_results['Retroceso_Improvement'] = pnl - actual_pnl
                        
                        results.append(trail_results)
                    except Exception as e:
                        continue
                
                if results:
                    results_df = pd.DataFrame(results)
                    
                    st.markdown("### 📊 Resultados Comparativos")
                    
                    summary_data = []
                    summary_data.append({
                        'Método': 'Sin Trailing (Actual)',
                        'Trades': len(results_df),
                        'PnL Total': f"${results_df['Actual_PnL'].sum():.2f}",
                        'PnL Promedio': f"${results_df['Actual_PnL'].mean():.2f}",
                        'Win Rate': f"{(results_df['Actual_PnL'] > 0).mean() * 100:.1f}%",
                        'Mejora': '-'
                    })
                    
                    if use_porcentual:
                        pnl_total = results_df['Porcentual_PnL'].sum()
                        improvement = ((pnl_total / results_df['Actual_PnL'].sum()) - 1) * 100
                        summary_data.append({
                            'Método': f'Trailing Porcentual {pct}%',
                            'Trades': len(results_df),
                            'PnL Total': f"${pnl_total:.2f}",
                            'PnL Promedio': f"${results_df['Porcentual_PnL'].mean():.2f}",
                            'Win Rate': f"{(results_df['Porcentual_PnL'] > 0).mean() * 100:.1f}%",
                            'Mejora': f"+{improvement:.1f}%" if improvement > 0 else f"{improvement:.1f}%"
                        })
                    
                    if use_retroceso:
                        pnl_total = results_df['Retroceso_PnL'].sum()
                        improvement = ((pnl_total / results_df['Actual_PnL'].sum()) - 1) * 100
                        summary_data.append({
                            'Método': f'Trailing Retroceso {activation}t/{retrace}t',
                            'Trades': len(results_df),
                            'PnL Total': f"${pnl_total:.2f}",
                            'PnL Promedio': f"${results_df['Retroceso_PnL'].mean():.2f}",
                            'Win Rate': f"{(results_df['Retroceso_PnL'] > 0).mean() * 100:.1f}%",
                            'Mejora': f"+{improvement:.1f}%" if improvement > 0 else f"{improvement:.1f}%"
                        })
                    
                    summary_df = pd.DataFrame(summary_data)
                    st.dataframe(summary_df, use_container_width=True)
                    
                    with st.expander("🔍 Resultados Detallados"):
                        st.dataframe(results_df, use_container_width=True)
                    
                    st.markdown("### 🎯 Recomendación")
                    if len(summary_df) > 1:
                        best_idx = summary_df.iloc[1:]['PnL Total'].str.replace('$', '').str.replace(',', '').astype(float).idxmax()
                        best_row = summary_df.iloc[best_idx]
                        st.success(f"**Mejor Método:** {best_row['Método']} con mejora de {best_row['Mejora']}")
                else:
                    st.error("No se pudieron simular los trades.")
    else:
        st.warning("⚠️ El CSV no contiene las columnas necesarias (ExitReason, MFE).")
'''

def main():
    # Leer app.py
    with open('app.py', 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verificar que no esté ya agregado
    if 'TAB 9: TRAILING STOP LABORATORY' in content:
        print("⚠️ Trailing Lab ya está en el archivo")
        return
    
    # Agregar al final
    new_content = content + TAB9_CODE
    
    # Escribir con encoding correcto
    with open('app.py', 'w', encoding='utf-8') as f:
        f.write(new_content)
    
    print("✅ Trailing Lab agregado exitosamente")
    print(f"Nuevas líneas totales: {len(new_content.splitlines())}")

if __name__ == '__main__':
    main()
