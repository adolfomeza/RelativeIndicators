"""
AI Engine para Streamlit Audit App
Motor de análisis con Gemini Pro para insights de trading cuantitativos
v1.0 - 2026-01-02
"""

import google.generativeai as genai
import streamlit as st
import os
from dotenv import load_dotenv

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
            
            "chat": {
                "system": f"""{base_context}

Tienes acceso a los datos del backtest del usuario:
{{context_data}}

Responde preguntas sobre el rendimiento, sugiere mejoras, y proporciona 
análisis cuantitativo cuando se solicite. Sé conversacional pero preciso."""
            }
        }
    
    @st.cache_data(ttl=300)  # Cache por 5 minutos
    def analyze_chart(_self, chart_type: str, data: dict, brief: bool = True) -> str:
        """
        Genera análisis para un gráfico específico
        
        Args:
            chart_type: Tipo de gráfico (equity_curve, long_vs_short, etc.)
            data: Diccionario con datos del gráfico
            brief: True para análisis corto, False para completo
            
        Returns:
            Texto con el análisis
        """
        try:
            if chart_type not in _self.prompts:
                return "⚠️ Tipo de gráfico no soportado"
            
            prompt_template = _self.prompts[chart_type]["brief" if brief else "full"]
            
            # Reemplazar placeholders con datos reales
            # F-strings ya convierten {{ a {, así que buscamos llaves simples
            prompt = prompt_template
            for key, value in data.items():
                # Buscar {key} (llave simple, no doble)
                placeholder = "{" + key + "}"
                if isinstance(value, float):
                    prompt = prompt.replace(placeholder, f"{value:,.2f}")
                else:
                    prompt = prompt.replace(placeholder, str(value))
            
            # Usar modelo apropiado
            model = _self.model_fast if brief else _self.model_full
            
            response = model.generate_content(prompt)
            return response.text
            
        except Exception as e:
            return f"⚠️ Error en análisis: {str(e)}"

    
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
    
    with st.container():
        st.markdown("---")
        
        # Análisis breve (siempre visible)
        with st.spinner("🧠 Analizando..."):
            brief_analysis = analyzer.analyze_chart(chart_type, data, brief=True)
        
        st.markdown(f"🧠 **Análisis IA:**")
        st.markdown(brief_analysis)
        
        # Botón para análisis completo
        if st.button(f"📝 Ver Análisis Completo", key=unique_key):
            with st.spinner("Generando análisis detallado..."):
                full_analysis = analyzer.analyze_chart(chart_type, data, brief=False)
            
            with st.expander("📋 Análisis Detallado", expanded=True):
                st.markdown(full_analysis)
