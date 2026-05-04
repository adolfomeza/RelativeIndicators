from pathlib import Path
import os


def nt_home() -> Path:
    override = os.environ.get("NT_HOME")
    if override:
        return Path(override)
    return Path.home() / "Documents" / "NinjaTrader 8"


def logs_dir() -> Path:
    return nt_home() / "log"


def trace_dir() -> Path:
    return nt_home() / "trace"


def vwap_levels_dir() -> Path:
    return nt_home() / "bin" / "Custom" / "VwapLevels"


def trade_exports_dir() -> Path:
    return nt_home() / "bin" / "Custom" / "Strategies" / "TradeExports"


def project_root() -> Path:
    return Path(__file__).resolve().parent.parent


def markups_dir() -> Path:
    """Carpeta donde se persisten los markups NADRO para el indicador
    RelativeNadroMarkup. Formato por archivo: ``{INSTRUMENT}_YYYY-MM-DD.json``.
    """
    return project_root() / "Docs" / "Nadro" / "markups"
