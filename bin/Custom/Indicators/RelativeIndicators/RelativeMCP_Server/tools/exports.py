"""Lectura de TradeExports (CSV con trades generados por la suite)."""
from __future__ import annotations

import csv
from pathlib import Path
from datetime import datetime

from ..paths import trade_exports_dir


def list_accounts() -> dict:
    """Cuentas con subcarpetas bajo TradeExports/."""
    d = trade_exports_dir()
    if not d.is_dir():
        return {"error": f"no existe {d}", "accounts": []}
    accs = []
    for child in sorted(d.iterdir()):
        if child.is_dir() and not child.name.startswith("."):
            csvs = list(child.glob("*.csv"))
            accs.append({
                "account": child.name,
                "csv_count": len(csvs),
            })
    return {"dir": str(d), "accounts": accs}


def list_trade_files(account: str) -> dict:
    """CSVs en la carpeta de una cuenta."""
    d = trade_exports_dir() / account
    if not d.is_dir():
        return {"error": f"no existe {d}", "files": []}
    files = []
    for f in sorted(d.glob("*.csv")):
        stat = f.stat()
        files.append({
            "name": f.name,
            "size": stat.st_size,
            "mtime": datetime.fromtimestamp(stat.st_mtime).isoformat(),
        })
    return {"account": account, "files": files}


def read_trades(
    account: str,
    csv_file: str,
    limit: int = 100,
    tail: bool = True,
) -> dict:
    """Lee filas de un CSV de trades.

    Si ``tail=True`` devuelve las últimas ``limit`` filas (ordenadas ascendentemente).
    """
    path = trade_exports_dir() / account / csv_file
    if not path.is_file():
        return {"error": f"no existe {path}", "rows": []}
    with path.open("r", encoding="utf-8", errors="replace", newline="") as fh:
        reader = csv.DictReader(fh)
        rows = list(reader)
        header = reader.fieldnames or []
    if tail and limit > 0:
        rows = rows[-limit:]
    elif limit > 0:
        rows = rows[:limit]
    return {
        "file": str(path),
        "header": header,
        "total_rows_in_file": sum(1 for _ in path.open("r", encoding="utf-8")) - 1,
        "returned": len(rows),
        "rows": rows,
    }


def compute_stats(
    account: str,
    csv_file: str,
    group_by: str | None = "Quality",
) -> dict:
    """Estadísticas básicas: win rate, PnL total, por grupo opcional.

    ``group_by`` típicos: ``Quality``, ``Direction``, ``ExitReason``. Pasa ``None`` para global.
    """
    path = trade_exports_dir() / account / csv_file
    if not path.is_file():
        return {"error": f"no existe {path}"}

    def _bucket_init() -> dict:
        return {
            "trades": 0,
            "wins": 0,
            "losses": 0,
            "flat": 0,
            "pnl_sum": 0.0,
            "pnl_max": None,
            "pnl_min": None,
        }

    def _update(b: dict, pnl: float) -> None:
        b["trades"] += 1
        if pnl > 0:
            b["wins"] += 1
        elif pnl < 0:
            b["losses"] += 1
        else:
            b["flat"] += 1
        b["pnl_sum"] += pnl
        b["pnl_max"] = pnl if b["pnl_max"] is None else max(b["pnl_max"], pnl)
        b["pnl_min"] = pnl if b["pnl_min"] is None else min(b["pnl_min"], pnl)

    global_bucket = _bucket_init()
    groups: dict[str, dict] = {}
    with path.open("r", encoding="utf-8", errors="replace", newline="") as fh:
        reader = csv.DictReader(fh)
        for row in reader:
            try:
                pnl = float(row.get("PnL", "0") or "0")
            except ValueError:
                continue
            _update(global_bucket, pnl)
            if group_by:
                key = row.get(group_by, "<none>") or "<none>"
                _update(groups.setdefault(key, _bucket_init()), pnl)

    def _finalize(b: dict) -> dict:
        t = b["trades"]
        return {
            **b,
            "win_rate": (b["wins"] / t) if t else 0.0,
            "avg_pnl": (b["pnl_sum"] / t) if t else 0.0,
        }

    return {
        "file": str(path),
        "global": _finalize(global_bucket),
        "group_by": group_by,
        "groups": {k: _finalize(v) for k, v in sorted(groups.items())} if group_by else None,
    }
