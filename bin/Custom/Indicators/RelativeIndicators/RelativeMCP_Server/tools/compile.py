"""Detecta si NT recompiló recientemente y si fue exitoso.

Señales:
- mtime del ``NinjaTrader.Custom.dll`` vs ahora → última compilación exitosa.
- Uptime del AddOn RelativeObserver → indica si hubo restart reciente (F7
  reinicia el AddOn).
- Coincidencia temporal entre DLL mtime y AddOn uptime → F7 exitoso.
"""
from __future__ import annotations

import os
import time
from datetime import datetime

from . import observer
from ..paths import nt_home


def _dll_path() -> str:
    return os.path.join(str(nt_home()), "bin", "Custom", "NinjaTrader.Custom.dll")


def check_compile_status() -> dict:
    """Devuelve información sobre la última compilación exitosa de NT8.

    - Un DLL con mtime reciente (<10 min) indica compile exitoso.
    - Si el AddOn uptime ≈ age del DLL, indica que fue el mismo evento (F7).
    - Si DLL mtime es viejo después de un F7, significa que la compilación falló
      y el binario anterior se mantiene.
    """
    dll = _dll_path()
    result: dict = {
        "dll_path": dll,
        "dll_exists": os.path.exists(dll),
    }
    if not result["dll_exists"]:
        result["error"] = "DLL no encontrado"
        return result

    mtime = os.path.getmtime(dll)
    now = time.time()
    age_seconds = now - mtime
    result["dll_mtime"] = datetime.fromtimestamp(mtime).isoformat()
    result["dll_age_seconds"] = round(age_seconds, 1)
    result["dll_age_minutes"] = round(age_seconds / 60, 2)

    # Consultar AddOn uptime
    try:
        health = observer.health()
        addon_uptime = health.get("uptime_seconds") if health else None
        addon_reachable = health.get("addon_reachable", True) if health else False
    except Exception as exc:
        addon_uptime = None
        addon_reachable = False
        result["addon_error"] = str(exc)

    result["addon_reachable"] = addon_reachable
    result["addon_uptime_seconds"] = addon_uptime
    if addon_uptime is not None:
        result["addon_uptime_minutes"] = round(addon_uptime / 60, 2)

    # Interpretación
    if age_seconds < 60:
        result["last_compile_status"] = "very_recent"  # <1 min
        result["interpretation"] = "Compilación exitosa hace menos de 1 minuto."
    elif age_seconds < 600:
        result["last_compile_status"] = "recent"  # <10 min
        result["interpretation"] = (
            f"Compilación exitosa hace {age_seconds/60:.1f} minutos."
        )
    else:
        result["last_compile_status"] = "older"
        result["interpretation"] = (
            f"Última compilación hace {age_seconds/3600:.1f} horas — sin F7 reciente."
        )

    # Si el user acaba de hacer F7 pero el DLL es viejo → falló
    if addon_uptime is not None:
        if addon_uptime < 120 and age_seconds > 180:
            result["flag"] = "compile_likely_failed"
            result["interpretation"] += (
                " ⚠ AddOn reinició hace poco pero el DLL no se actualizó — "
                "probable ERROR DE COMPILACIÓN. Mirá el NinjaScript Editor."
            )
        elif abs(addon_uptime - age_seconds) < 30:
            result["flag"] = "compile_and_restart_aligned"
            result["interpretation"] += (
                " ✓ AddOn uptime coincide con DLL mtime — F7 exitoso confirmado."
            )

    return result
