"""Acceso a logs y traces de NinjaTrader 8.

Formato log.txt: ``YYYY-MM-DD HH:MM:SS:mmm|LEVEL|CATEGORY|MESSAGE``
Niveles: 1=Info, 2=Warning, 3=Error.
"""
from __future__ import annotations

from dataclasses import dataclass, asdict
from datetime import datetime, timedelta
from pathlib import Path
import re
from typing import Iterable

from ..paths import logs_dir, trace_dir


LEVEL_NAMES = {1: "Info", 2: "Warning", 3: "Error"}
LOG_LINE_RE = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}:\d{3})\|"
    r"(?P<level>\d+)\|(?P<category>\d+)\|(?P<message>.*)$"
)


@dataclass
class LogEntry:
    timestamp: str
    level: int
    level_name: str
    category: int
    message: str


def _parse_line(line: str) -> LogEntry | None:
    m = LOG_LINE_RE.match(line.rstrip("\r\n"))
    if not m:
        return None
    lvl = int(m.group("level"))
    return LogEntry(
        timestamp=m.group("ts"),
        level=lvl,
        level_name=LEVEL_NAMES.get(lvl, f"L{lvl}"),
        category=int(m.group("category")),
        message=m.group("message"),
    )


def _ts_to_dt(ts: str) -> datetime:
    # "2026-04-19 22:46:17:859" -> datetime con milisegundos
    date_part, ms = ts.rsplit(":", 1)
    dt = datetime.strptime(date_part, "%Y-%m-%d %H:%M:%S")
    return dt.replace(microsecond=int(ms) * 1000)


def _today_log_files(english: bool = True) -> list[Path]:
    d = logs_dir()
    if not d.is_dir():
        return []
    today = datetime.now().strftime("%Y%m%d")
    suffix = ".en.txt" if english else ".txt"
    files = sorted(d.glob(f"log.{today}.*{suffix}"))
    if english:
        # excluir los non-english que terminan en solo ".txt"
        files = [f for f in files if f.name.endswith(".en.txt")]
    else:
        files = [f for f in files if not f.name.endswith(".en.txt")]
    return files


def _latest_log_files(english: bool = True, n: int = 1) -> list[Path]:
    d = logs_dir()
    if not d.is_dir():
        return []
    suffix = ".en.txt" if english else ".txt"
    files = sorted(
        (f for f in d.glob(f"log.*{suffix}") if english == f.name.endswith(".en.txt")),
        key=lambda p: p.stat().st_mtime,
    )
    return files[-n:]


def _read_tail(path: Path, lines: int) -> list[str]:
    """Lee las últimas N líneas de un archivo UTF-8 de forma eficiente."""
    if not path.is_file():
        return []
    try:
        data = path.read_bytes()
    except OSError:
        return []
    text = data.decode("utf-8", errors="replace")
    all_lines = text.splitlines()
    return all_lines[-lines:] if lines > 0 else all_lines


def tail_nt_log(lines: int = 100, level_min: int = 1, english: bool = True) -> dict:
    """Últimas N líneas del log más reciente de NT, filtradas por nivel mínimo."""
    files = _latest_log_files(english=english, n=1)
    if not files:
        return {"error": f"no log files en {logs_dir()}", "entries": []}
    path = files[0]
    raw = _read_tail(path, lines * 3)  # oversample por si hay líneas filtradas
    entries: list[dict] = []
    for ln in raw:
        e = _parse_line(ln)
        if e is None:
            continue
        if e.level < level_min:
            continue
        entries.append(asdict(e))
    return {
        "file": str(path),
        "count": len(entries),
        "entries": entries[-lines:],
    }


def search_nt_log(
    pattern: str,
    since_minutes: int = 60,
    case_sensitive: bool = False,
    english: bool = True,
) -> dict:
    """Busca regex en los logs de las últimas N minutos (across todos los logs del día)."""
    flags = 0 if case_sensitive else re.IGNORECASE
    try:
        rx = re.compile(pattern, flags)
    except re.error as exc:
        return {"error": f"regex inválida: {exc}", "matches": []}

    cutoff = datetime.now() - timedelta(minutes=since_minutes)
    files = _today_log_files(english=english) or _latest_log_files(english=english, n=2)
    matches: list[dict] = []
    for path in files:
        for ln in _read_tail(path, 0):
            e = _parse_line(ln)
            if e is None:
                continue
            try:
                if _ts_to_dt(e.timestamp) < cutoff:
                    continue
            except ValueError:
                pass
            if rx.search(e.message):
                matches.append({**asdict(e), "file": path.name})
    return {
        "pattern": pattern,
        "since_minutes": since_minutes,
        "count": len(matches),
        "matches": matches,
    }


def list_indicator_traces() -> dict:
    """Carpetas de trace por indicador (tu suite escribe ahí vía LogToFile)."""
    d = trace_dir()
    if not d.is_dir():
        return {"error": f"no existe {d}", "indicators": []}
    subs = []
    for child in sorted(d.iterdir()):
        if child.is_dir():
            files = sorted(child.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True)
            subs.append({
                "indicator": child.name,
                "file_count": len(files),
                "latest": files[0].name if files else None,
                "latest_mtime": (
                    datetime.fromtimestamp(files[0].stat().st_mtime).isoformat()
                    if files else None
                ),
            })
    return {"trace_dir": str(d), "indicators": subs}


def tail_indicator_trace(indicator: str, lines: int = 100, file: str | None = None) -> dict:
    """Últimas N líneas del trace de un indicador.

    ``indicator`` debe ser el nombre de la carpeta (ej. 'RelativeVwap').
    ``file`` opcional — si no se da, toma el más reciente.
    """
    d = trace_dir() / indicator
    if not d.is_dir():
        return {"error": f"no existe {d}", "entries": []}
    if file:
        path = d / file
    else:
        files = sorted(d.iterdir(), key=lambda p: p.stat().st_mtime)
        if not files:
            return {"error": "carpeta vacía", "entries": []}
        path = files[-1]
    raw = _read_tail(path, lines)
    return {"file": str(path), "count": len(raw), "lines": raw}


def get_trace_today() -> dict:
    """Archivos trace.YYYYMMDD.* del día de hoy (diagnóstico global de NT)."""
    d = trace_dir()
    if not d.is_dir():
        return {"error": f"no existe {d}", "files": []}
    today = datetime.now().strftime("%Y%m%d")
    files = sorted(d.glob(f"trace.{today}.*.txt"))
    return {
        "date": today,
        "files": [
            {
                "name": f.name,
                "size": f.stat().st_size,
                "mtime": datetime.fromtimestamp(f.stat().st_mtime).isoformat(),
            }
            for f in files
        ],
    }
