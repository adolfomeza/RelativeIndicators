"""
AI Engine para Streamlit Audit App
Motor de análisis con Gemini Pro para insights de trading cuantitativos
v1.0 - 2026-01-02
"""

import google.generativeai as genai
import streamlit as st
import os
import json
from dotenv import load_dotenv

# Configuración de Persistencia
USAGE_FILE = "ai_billing.json"

def get_usage_history():
    """Carga el historial acumulado de uso"""
    if os.path.exists(USAGE_FILE):
        try:
            with open(USAGE_FILE, 'r') as f:
                return json.load(f)
        except:
            return {'total_cost': 0.0, 'total_tokens': 0}
    return {'total_cost': 0.0, 'total_tokens': 0}

def update_usage_history(new_cost, new_tokens):
    """Actualiza y guarda el historial acumulado"""
    history = get_usage_history()
    history['total_cost'] = history.get('total_cost', 0.0) + new_cost
    history['total_tokens'] = history.get('total_tokens', 0) + new_tokens
    
    try:
        with open(USAGE_FILE, 'w') as f:
            json.dump(history, f)
    except Exception as e:
        print(f"Error saving usage history: {e}")
    
    return history

# Cargar API key desde .env
load_dotenv()

class QuantAIAnalyzer:
    """Clase principal para análisis de trading con Gemini AI"""
    
    def __init__(self, api_key: str = None):
        """
        Inicializa el analizador con la API key de Gemini
        
        Args:
            api_key: API key de Gemini. Si None, intenta leer de .env
        """
        if api_key is None:
            api_key = os.getenv("GEMINI_API_KEY")
        
        if not api_key:
            raise ValueError("API Key de Gemini requerida")
        
        genai.configure(api_key=api_key)
        
        # Modelo rápido para análisis breves (gemini-2.0-flash está disponible)
        self.model_fast = genai.GenerativeModel('gemini-2.0-flash')
        # Modelo completo para análisis detallados (gemini-2.5-pro está disponible)
        self.model_full = genai.GenerativeModel('gemini-2.5-pro')



        
        # Prompts base por tipo de gráfico
        self.prompts = self._init_prompts()
    
    def _init_prompts(self) -> dict:
        """Define prompts especializados para cada tipo de gráfico"""
        base_context = """Eres un experto trader cuantitativo analizando resultados de backtest 
de una estrategia de trading de futuros. Tu análisis debe ser:
- Directo y accionable
- Basado en datos, no opiniones
- En español
- Con recomendaciones específicas cuando aplique"""
        
        return {
            "equity_curve": {
                "brief": f"""{base_context}
                
Analiza esta curva de equidad en 2-3 líneas máximo:
Total PnL: ${{total_pnl}}
Max Drawdown: ${{max_drawdown}}
Win Rate: {{win_rate}}%
Profit Factor: {{pf}}

Enfócate en: tendencia general, severidad de drawdowns, y si el crecimiento es consistente.""",
                
                "full": f"""{base_context}

Haz un análisis DETALLADO de esta curva de equidad:
Total PnL: ${{total_pnl}}
Max Drawdown: ${{max_drawdown}}  
Win Rate: {{win_rate}}%
Profit Factor: {{pf}}
Total Trades: {{total_trades}}

Incluye:
1. Evaluación de la tendencia (alcista/lateral/bajista)
2. Análisis de drawdowns (severidad, duración estimada, recuperación)
3. Consistencia del edge (¿es lineal o depende de pocos trades?)
4. Riesgo de ruina estimado
5. Recomendaciones concretas de mejora
6. Puntuación del sistema (1-10) con justificación"""
            },
            
            "long_vs_short": {
                "brief": f"""{base_context}

Analiza Long vs Short en 2-3 líneas:
PnL Long: ${{pnl_long}}
PnL Short: ${{pnl_short}}
Trades Long: {{trades_long}}
Trades Short: {{trades_short}}

Enfócate en: sesgo direccional y si ambas direcciones aportan valor.""",

                "full": f"""{base_context}

Análisis DETALLADO de rendimiento direccional:
PnL Long: ${{pnl_long}}
PnL Short: ${{pnl_short}}
Trades Long: {{trades_long}}
Trades Short: {{trades_short}}
Win Rate Long: {{wr_long}}%
Win Rate Short: {{wr_short}}%

Incluye:
1. ¿Hay sesgo direccional? ¿Es problemático?
2. ¿Alguna dirección debería desactivarse?
3. ¿El mercado actual favorece una dirección?
4. Recomendaciones de position sizing por dirección
5. Ajustes de SL/TP por dirección si aplica"""
            },
            
            "setup_performance": {
                "brief": f"""{base_context}

Analiza rendimiento por setup en 2-3 líneas:
{{setup_data}}

Identifica: setups rentables vs tóxicos.""",

                "full": f"""{base_context}

Análisis DETALLADO por setup:
{{setup_data}}

Incluye:
1. Ranking de setups por rentabilidad ajustada al riesgo
2. Setups a DESACTIVAR (tóxicos)
3. Setups a POTENCIAR (mayor tamaño)
4. Patrones en nombres de setup (¿ciertos niveles funcionan mejor?)
5. Correlación entre tipo de nivel y rentabilidad"""
            },
            
            "tier_analysis": {
                "brief": f"""{base_context}

Analiza escala de TPs en 2-3 líneas:
{{tier_data}}

Enfócate en: ¿los runners (TP2+) valen la pena?""",

                "full": f"""{base_context}

Análisis DETALLADO de estrategia de escala:
{{tier_data}}

Incluye:
1. ¿TP1 está financiando correctamente el riesgo?
2. ¿Los runners (TP2, TP3) tienen edge positivo?
3. Sharpe ratio por tier - ¿cuál tier tiene mejor retorno/riesgo?
4. Recomendación: ¿agregar más tiers, reducirlos, o mantener?
5. Distribución óptima de contratos por tier"""
            },
            
            "drawdown": {
                "brief": f"""{base_context}

Analiza drawdown en 2-3 líneas:
Max Drawdown: ${{max_dd}}
Drawdown actual: ${{current_dd}}

Enfócate en: severidad y riesgo.""",

                "full": f"""{base_context}

Análisis DETALLADO de riesgo:
Max Drawdown: ${{max_dd}}
Periodos en drawdown: aproximadamente {{dd_periods}}%

Incluye:
1. ¿El drawdown es aceptable para el capital? 
2. Análisis de "time underwater" - ¿cuánto tiempo en pérdida?
3. Patrón de recuperación (V-shape vs U-shape vs L-shape)
4. Capital mínimo recomendado para este sistema
5. Ajustes de riesgo si drawdown es excesivo"""
            },
            
            "mae_mfe": {
                "brief": f"""{base_context}

Analiza MAE/MFE en 2-3 líneas:
Promedio MAE: ${{avg_mae}}
Promedio MFE: ${{avg_mfe}}
Eficiencia: {{efficiency}}%

Enfócate en: ¿entradas precisas o sufriendo mucho?""",

                "full": f"""{base_context}

Análisis DETALLADO de eficiencia de entrada:
Promedio MAE (dolor máximo): ${{avg_mae}}
Promedio MFE (ganancia máxima): ${{avg_mfe}}
Eficiencia de captura: {{efficiency}}%
% trades con MAE=0: {{sniper_pct}}%

Incluye:
1. ¿Las entradas son tipo "sniper" o sufren mucho retroceso?
2. ¿El stop loss está bien calibrado vs MAE típico?
3. ¿Estamos capturando suficiente del MFE disponible?
4. Recomendaciones de ajuste de SL basadas en MAE
5. Recomendaciones de ajuste de TP basadas en MFE"""
            },
            
            "monte_carlo": {
                "brief": f"""{base_context}

Analiza simulación Monte Carlo en 2-3 líneas:
Riesgo de ruina: {{risk_of_ruin}}%
Peor drawdown simulado: ${{worst_dd}}
Capital sugerido: ${{suggested_capital}}

Enfócate en: robustez del sistema.""",

                "full": f"""{base_context}

Análisis DETALLADO de Monte Carlo:
Simulaciones: {{n_sims}}
Riesgo de ruina: {{risk_of_ruin}}%
Peor drawdown simulado: ${{worst_dd}}
Mejor caso final: ${{best_case}}
Peor caso final: ${{worst_case}}

Incluye:
1. ¿El sistema es robusto ante diferentes secuencias de trades?
2. ¿Qué tan probable es la ruina con capitalización actual?
3. Capital mínimo para sobrevivir "cisne negro"
4. Análisis de distribución de resultados finales
5. ¿El sistema depende de "suerte" o tiene edge real?"""
            },
            
            "calendar": {
                "brief": f"""{base_context}

Analiza calendario de trading en 2-3 líneas:
PnL del mes: ${{month_pnl}}
Mejor día: ${{best_day}} (${{best_day_pnl}})
Peor día: ${{worst_day}} (${{worst_day_pnl}})

Enfócate en: patrones temporales.""",

                "full": f"""{base_context}

Análisis DETALLADO temporal:
PnL del mes: ${{month_pnl}}
Días verdes: {{green_days}}
Días rojos: {{red_days}}
Mejor día: {{best_day}}
Peor día: {{worst_day}}

Incluye:
1. ¿Hay patrón semanal? (¿Lunes malo, viernes bueno?)
2. ¿Días de alta volatilidad (NFP, FOMC) afectan?
3. ¿Es consistente o depende de pocos días ganadores?
4. Recomendaciones de días a evitar
5. Análisis de rachas (ganadoras/perdedoras)"""
            },
            
            "hourly": {
                "brief": f"""{base_context}

Analiza rendimiento horario en 2-3 líneas:
{{hourly_data}}

Identifica: horas muertas vs horas doradas.""",

                "full": f"""{base_context}

Análisis DETALLADO por hora:
{{hourly_data}}

Incluye:
1. Horas DORADAS (alto win rate + alto PnL)
2. Horas MUERTAS (pérdida consistente - desactivar)
3. Correlación con sesiones (Asia, Europa, USA)
4. Recomendación de ventanas de trading
5. Filtro horario sugerido para la estrategia"""
            },
            
            "levels": {
                "brief": f"""{base_context}

Analiza rendimiento por nivel en 2-3 líneas:
{{levels_data}}

Identifica: niveles rentables vs tóxicos.""",

                "full": f"""{base_context}

Análisis DETALLADO por nivel de sesión:
{{levels_data}}

Incluye:
1. ¿Qué sesiones tienen mejores niveles? (Asia, Europe, USA)
2. ¿Highs vs Lows - cuál funciona mejor?
3. Niveles a BLOQUEAR (filtro tóxico)
4. Niveles a PRIORIZAR (mayor tamaño)
5. Interacción entre sesiones (agresor vs defensor)"""
            },

            "levels_analysis": {
                "brief": f"""{base_context}

Analiza niveles y penetración en 2-3 líneas:
Mejor zona: {{best_zone}} (${{best_zone_pnl}})
Peor zona: {{worst_zone}} (${{worst_zone_pnl}})

Insight MAE:
{{penetration_insight}}

Recomendación rápida de ajuste.""",

                "full": f"""{base_context}

Análisis DETALLADO de niveles y penetración:
Mejor zona: {{best_zone}} (${{best_zone_pnl}})
Peor zona: {{worst_zone}} (${{worst_zone_pnl}})
Total PnL Zonas: ${{total_pnl}}

Insight de Penetración (MAE):
{{penetration_insight}}

Incluye:
1. ¿Los niveles respetados generan suficiente recorrido (MFE)?
2. ¿La mejor zona justifica aumentar tamaño de posición?
3. Análisis de la "Zona Muerta" de penetración - ¿dónde poner el SL?
4. Estrategia sugerida para la peor zona (¿Fade o ignorar?)
5. Evaluación de robustez de la ruptura"""
            },
            
            "interaction_matrix": {
                "brief": f"""{base_context}

Analiza interacción de sesiones en 2-3 líneas:
Mejor combinación: {{best_combo}} (${{best_pnl}})
Peor combinación: {{worst_combo}} (${{worst_pnl}})

Identifica qué sesión 'domina' los niveles de las otras.""",
                
                "full": f"""{base_context}

Análisis DETALLADO de Matriz Agresor vs Defensor:
Mejor combinación: {{best_combo}} (${{best_pnl}})
Peor combinación: {{worst_combo}} (${{worst_pnl}})

Incluye:
1. ¿Qué sesión (Agresor) es más efectiva rompiendo niveles?
2. ¿Qué niveles (Defensor) son más frágiles?
3. Análisis de patrones Long vs Short (¿alguna asimetría?)
4. Combinaciones tóxicas a evitar (Ej: Asia rompiendo High de USA)
5. Recomendación táctica por sesión de trading actual"""
            },
            
            # New Level Analysis Module Types
            "performance_dashboard": {
                "brief": f"""{base_context}
                
Analiza este Dashboard de Niveles en 2-3 líneas:
{{zone_metrics}}

Identifica: mejor/peor zona y si hay zonas que deben desactivarse.""",
                
                "full": f"""{base_context}

Análisis DETALLADO del Dashboard de Rendimiento por Zona:
{{zone_metrics}}

Incluye:
1. Ranking de zonas por Sharpe (riesgo-ajustado)
2. ¿Hay zonas premium (Sharpe > 1.5) que merecen más capital?
3. ¿Hay zonas tóxicas que deben filtrarse completamente?
4. Análisis de Win Rate vs R:R - ¿equilibrio correcto?
5. Recomendación de asignación de capital por zona"""
            },
            
            "directionality_matrix": {
                "brief": f"""{base_context}

Analiza Matriz Direccional en 2-3 líneas:
{{dir_matrix}}

Identifica: bias direccional por zona (Long/Short).""",
                
                "full": f"""{base_context}

Análisis DETALLADO de Direccionalidad:
{{dir_matrix}}

Incluye:
1. ¿Qué zonas tienen sesgo claro (>20% diferencia Long vs Short)?
2. ¿Deberías deshabilitar alguna dirección en zonas específicas?
3. ¿El mercado actual favorece longs o shorts en estas zonas?
4. Recomendación de filtros direccionales para código C#
5. Análisis de consistencia - ¿el bias es estable o ruido?"""
            },
            
            "temporal_performance": {
                "brief": f"""{base_context}

Analiza rendimiento temporal en 2-3 líneas:
{{temporal_data}}

Identifica: ventanas horarias tóxicas.""",
                
                "full": f"""{base_context}

Análisis DETALLADO Temporal (Zona x Hora):
{{temporal_data}}

Incluye:
1. ¿Qué combinaciones Zona+Hora son sistemáticamente perdedoras?
2. Correlación con sesiones de mercado (Asia/Europe/USA overlap)
3. ¿Bajo volumen o alta volatilidad causan pérdidas?
4. Código C# sugerido para filtrar horarios tóxicos
5. Ventanas óptimas de trading por zona"""
            },
            
            "toxic_combinations": {
                "brief": f"""{base_context}

Analiza combinaciones tóxicas en 2-3 líneas:
{{toxic_combos}}

Identifica: peor patrón multi-variable.""",
                
                "full": f"""{base_context}

Análisis DETALLADO de Filtros Multi-Variable:
{{toxic_combos}}

Incluye:
1. Peores combinaciones Zona+Dirección+Hora - ¿cuánto te cuestan?
2. ¿Estos patrones son ruido o sistemáticos?
3. Código C# completo para filtrar las 3 peores combinaciones
4. Impacto en PnL si eliminas estos trades (estimación)
5. ¿Hay patrón común entre todas las combinaciones tóxicas?"""
            },

            "r_ladder": {
                "brief": f"""{base_context}

Analiza este gráfico R-Ladder (Alcance vs PnL) en 2-3 líneas:
Alcance 1R: {{reached_1r}}%
Alcance 5R: {{reached_5r}}%
Alcance 10R: {{reached_10r}}%
PnL Max (20R): ${{pnl_20r}}
PnL Medio (5R): ${{pnl_5r}}

Dime si la estrategia busca "Home Runs" o scalps cortos.""",

                "full": f"""{base_context}

Análisis DETALLADO de R-Ladder (Capacidad de Runner):
Alcance 1R: {{reached_1r}}%
Alcance 5R: {{reached_5r}}%
Alcance 10R: {{reached_10r}}%
Alcance 20R: {{reached_20r}}%

PnL Acumulado en 5R: ${{pnl_5r}}
PnL Acumulado en 10R: ${{pnl_10r}}
PnL Acumulado en 20R: ${{pnl_20r}}

Incluye:
1. **Curva de Energía:** ¿El PnL sigue subiendo fuerte hasta 20R o se aplana?
2. **Diagnóstico de Salida:** ¿Estamos saliendo muy temprano (dejando dinero en la mesa)?
3. **Estrategia de Scaling:** Basado en la caída de probabilidad, ¿dónde sugieres sacar parciales?
4. **Viabilidad de Home Run:** ¿Es realista buscar 20R con este % de alcance ({{reached_20r}}%)?
5. **Recomendación Final:** ¿Trailing Stop agresivo o TP fijo lejano?"""
            },
            
            "chat": {
                "system": f"""{base_context}

Tienes acceso a los datos del backtest del usuario:
{{context_data}}

Responde preguntas sobre el rendimiento, sugiere mejoras, y proporciona 
análisis cuantitativo cuando se solicite. Sé conversacional pero preciso."""
            }
        }
    
    @st.cache_data(ttl=1800)  # Cache por 30 minutos (optimizado para sesiones largas)
    def analyze_chart(_self, chart_type: str, data: dict, brief: bool = True):
        """
        Genera análisis para un gráfico específico
        
        Args:
            chart_type: Tipo de gráfico (equity_curve, long_vs_short, etc.)
            data: Diccionario con datos del gráfico
            brief: True para análisis corto, False para completo
            
        Returns:
            Tuple (texto_análisis, metadatos_uso)
            donde metadatos_uso = {
                'input_tokens': int,
                'output_tokens': int,
                'total_tokens': int,
                'cost_usd': float
            }
        """
        try:
            if chart_type not in _self.prompts:
                return "⚠️ Tipo de gráfico no soportado", None
            
            prompt_template = _self.prompts[chart_type]["brief" if brief else "full"]
            
            # Reemplazar placeholders con datos reales
            prompt = prompt_template
            for key, value in data.items():
                placeholder = "{" + key + "}"
                if isinstance(value, float):
                    prompt = prompt.replace(placeholder, f"{value:,.2f}")
                else:
                    prompt = prompt.replace(placeholder, str(value))
            
            # Usar modelo apropiado
            model = _self.model_fast if brief else _self.model_full
            
            response = model.generate_content(prompt)
            
            # Capturar metadatos de uso REALES de la API
            usage = {
                'input_tokens': response.usage_metadata.prompt_token_count,
                'output_tokens': response.usage_metadata.candidates_token_count,
                'total_tokens': response.usage_metadata.total_token_count,
                'cost_usd': 0.0  # Calcularemos después
            }
            
            # Calcular costo real según tarifas de Gemini
            # Gemini 2.0 Flash: $0.075/1M input, $0.30/1M output
            # Gemini 2.5 Pro: más caro, pero usamos flash para brief
            if brief:  # gemini-2.0-flash
                cost_input = (usage['input_tokens'] / 1_000_000) * 0.075
                cost_output = (usage['output_tokens'] / 1_000_000) * 0.30
            else:  # gemini-2.5-pro (más caro)
                cost_input = (usage['input_tokens'] / 1_000_000) * 1.25
                cost_output = (usage['output_tokens'] / 1_000_000) * 5.00
            
            usage['cost_usd'] = cost_input + cost_output
            
            # Persistir uso automáticamente (solo si no es cache hit, el código corre)
            update_usage_history(usage['cost_usd'], usage['total_tokens'])
            
            return response.text, usage
            
        except Exception as e:
            return f"⚠️ Error en análisis: {str(e)}", None


    
    def chat(self, user_message: str, context_data: dict) -> str:
        """
        Chat interactivo con el asistente Quant
        
        Args:
            user_message: Pregunta del usuario
            context_data: Datos del backtest para contexto
            
        Returns:
            Respuesta del asistente
        """
        try:
            system_prompt = self.prompts["chat"]["system"]
            
            # Construir contexto
            context_str = "\n".join([f"- {k}: {v}" for k, v in context_data.items()])
            system_prompt = system_prompt.replace("{{context_data}}", context_str)
            
            full_prompt = f"{system_prompt}\n\nUsuario: {user_message}"
            
            response = self.model_full.generate_content(full_prompt)
            
            # Track Usage (Pro Model)
            usage = response.usage_metadata
            input_tokens = usage.prompt_token_count
            output_tokens = usage.candidates_token_count
            total_tokens = usage.total_token_count
            
            # Gemini 1.5 Pro Cost
            cost = (input_tokens / 1e6 * 1.25) + (output_tokens / 1e6 * 5.00)
            
            update_usage_history(cost, total_tokens)
            
            return response.text
            
        except Exception as e:
            return f"⚠️ Error: {str(e)}"


def get_analyzer() -> QuantAIAnalyzer:
    """
    Factory function para obtener instancia del analizador.
    Usa session_state para mantener una sola instancia.
    """
    if 'ai_analyzer' not in st.session_state:
        try:
            st.session_state.ai_analyzer = QuantAIAnalyzer()
        except ValueError:
            return None
    return st.session_state.ai_analyzer


def show_ai_analysis(chart_name: str, chart_type: str, data: dict, key_suffix: str = ""):
    """
    Componente UI para mostrar análisis de IA debajo de un gráfico
    
    Args:
        chart_name: Nombre visible del gráfico (para UI)
        chart_type: Tipo de gráfico para prompts
        data: Datos para el análisis
        key_suffix: Sufijo único para keys de Streamlit
    """
    analyzer = get_analyzer()
    
    if analyzer is None:
        return  # Sin API key, no mostrar nada
    
    unique_key = f"ai_{chart_type}_{key_suffix}"
    
    if 'ai_usage_stats' not in st.session_state:
        st.session_state.ai_usage_stats = {'cost': 0.0, 'tokens': 0}

    with st.container():
        st.markdown("---")
        
        # Análisis breve visible directamente
        st.subheader(f"🧠 Análisis IA: {chart_name}")
        
        with st.spinner("🧠 Analizando..."):
            brief_analysis, usage = analyzer.analyze_chart(chart_type, data, brief=True)
            
            # Actualizar estadísticas globales si hay uso nuevo
            if usage:
                st.session_state.ai_usage_stats['cost'] += usage['cost_usd']
                st.session_state.ai_usage_stats['tokens'] += usage['total_tokens']
        
        # Mostrar widget de costo
        if usage:
            st.caption(f"💰 **Costo Análisis**: ${usage['cost_usd']:.5f} USD | 🎫 **Tokens**: {usage['total_tokens']} | 📉 **Total Sesión**: ${st.session_state.ai_usage_stats['cost']:.4f}")

        st.markdown(brief_analysis)
        
        # Botones de acción
        col1, col2 = st.columns([3, 1])
        
        with col1:
            if st.button(f"📝 Ver Análisis Completo", key=f"{unique_key}_full"):
                with st.spinner("Generando análisis detallado..."):
                    full_analysis, full_usage = analyzer.analyze_chart(chart_type, data, brief=False)
                    
                    if full_usage:
                        st.session_state.ai_usage_stats['cost'] += full_usage['cost_usd']
                        st.session_state.ai_usage_stats['tokens'] += full_usage['total_tokens']
                
                # Mostrar en sub-expander
                with st.expander("📋 Análisis Detallado", expanded=True):
                    if full_usage:
                        st.caption(f"💰 Costo Detallado: ${full_usage['cost_usd']:.5f}")
                    st.markdown(full_analysis)
        
        with col2:
            # Botón de copiar (placeholder - requiere JS personalizado)
                if st.button(f"📋 Copiar", key=f"{unique_key}_copy", help="Copiar análisis"):
                    st.info("💡 Puedes seleccionar y copiar el texto arriba", icon="ℹ️")
