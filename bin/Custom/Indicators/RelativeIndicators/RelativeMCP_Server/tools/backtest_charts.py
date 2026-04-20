"""Gráficos del backtest NADRO — PnL curves + trade distributions por setup."""
from __future__ import annotations

import os
from datetime import datetime
from pathlib import Path

import matplotlib
matplotlib.use("Agg")  # sin display
import matplotlib.pyplot as plt
import matplotlib.dates as mdates

from . import backtest as bt_tool


_SETUP_COLORS = {
    "BPB": "#2ecc71",   # verde (edge principal)
    "RPB": "#3498db",   # azul
    "IPB": "#f39c12",   # naranja
    "EF":  "#e74c3c",   # rojo
}


def _reports_dir() -> Path:
    p = Path(__file__).resolve().parent.parent / "reports"
    p.mkdir(exist_ok=True)
    return p


def _build_curve(trades: list[dict], point_value: float = 5.0) -> tuple[list, list]:
    """Curva de PnL acumulada en USD a partir de lista de trades ordenados por entry_time."""
    if not trades:
        return [], []
    sorted_trades = sorted(trades, key=lambda t: t["entry_bar_time"])
    times = []
    cum = 0.0
    values = []
    for t in sorted_trades:
        cum += t["pnl_pts"] * point_value
        times.append(datetime.fromisoformat(t["entry_bar_time"].split(".")[0]))
        values.append(cum)
    return times, values


def generate_backtest_charts(
    backtest_result: dict,
    instrument: str = "MES",
    out_prefix: str | None = None,
) -> dict:
    """Genera 4 gráficos PNG del resultado de ``nadro_backtest``:

    1. equity_curve.png    — PnL acumulado global + por setup superpuestos
    2. by_setup.png        — 4 subplots (uno por setup), cada uno con su equity curve
    3. daily_pnl.png       — barras diarias W/L en colores
    4. trade_distribution.png — histograma de PnL por trade por setup

    Returns:
        dict con las rutas de los PNG generados.
    """
    out_dir = _reports_dir()
    prefix = out_prefix or f"{instrument}_{datetime.now().strftime('%Y%m%d_%H%M%S')}"

    trades = backtest_result.get("trades", [])
    if not trades:
        return {"error": "sin trades para graficar"}

    point_value = backtest_result.get("config", {}).get("point_value", 5.0)
    stats = backtest_result.get("stats", {})

    # Agrupar trades por tipo base
    by_setup: dict[str, list] = {"BPB": [], "RPB": [], "IPB": [], "EF": []}
    for t in trades:
        key = t["setup_type"].split("_")[0]
        if key in by_setup:
            by_setup[key].append(t)

    paths = {}

    # --- 1. Equity curve global + por setup superpuestos ---
    fig, ax = plt.subplots(figsize=(12, 6))
    times_all, values_all = _build_curve(trades, point_value)
    if times_all:
        ax.plot(times_all, values_all, color="black", linewidth=2.2,
                label=f"TOTAL (${values_all[-1]:+.0f})", zorder=10)

    for setup_name, setup_trades in by_setup.items():
        if not setup_trades:
            continue
        times, values = _build_curve(setup_trades, point_value)
        color = _SETUP_COLORS[setup_name]
        ax.plot(times, values, color=color, linewidth=1.6, alpha=0.9,
                label=f"{setup_name} ({len(setup_trades)}t, ${values[-1]:+.0f})")

    ax.axhline(0, color="gray", linewidth=0.8, linestyle="--")
    ax.set_title(f"NADRO Backtest — Equity Curve — {instrument}\n"
                 f"{len(trades)} trades | WR {stats.get('win_rate', 0):.1%} | "
                 f"PF {stats.get('profit_factor', '-')} | MaxDD ${stats.get('max_drawdown_usd', 0):.0f}",
                 fontsize=13, fontweight="bold")
    ax.set_xlabel("Fecha")
    ax.set_ylabel("PnL Acumulado (USD)")
    ax.legend(loc="best", fontsize=10)
    ax.grid(True, alpha=0.3)
    ax.xaxis.set_major_formatter(mdates.DateFormatter("%m-%d"))
    fig.autofmt_xdate()
    path1 = out_dir / f"{prefix}_equity_curve.png"
    fig.savefig(path1, dpi=100, bbox_inches="tight")
    plt.close(fig)
    paths["equity_curve"] = str(path1)

    # --- 2. Subplots por setup (2x2) ---
    fig, axes = plt.subplots(2, 2, figsize=(14, 10))
    for ax, (setup_name, setup_trades) in zip(axes.flatten(), by_setup.items()):
        color = _SETUP_COLORS[setup_name]
        if not setup_trades:
            ax.text(0.5, 0.5, f"{setup_name}\n(0 setups)", ha="center", va="center",
                    fontsize=14, color="gray", transform=ax.transAxes)
            ax.set_xticks([])
            ax.set_yticks([])
            continue
        times, values = _build_curve(setup_trades, point_value)
        ax.plot(times, values, color=color, linewidth=2)
        ax.axhline(0, color="gray", linewidth=0.8, linestyle="--")
        # Marcadores W/L
        wins = [(datetime.fromisoformat(t["entry_bar_time"].split(".")[0]),
                 sum(x["pnl_pts"] for x in setup_trades[:setup_trades.index(t)+1]) * point_value)
                for t in setup_trades if t["pnl_pts"] > 0]
        losses = [(datetime.fromisoformat(t["entry_bar_time"].split(".")[0]),
                   sum(x["pnl_pts"] for x in setup_trades[:setup_trades.index(t)+1]) * point_value)
                  for t in setup_trades if t["pnl_pts"] <= 0]
        if wins:
            wx, wy = zip(*wins)
            ax.scatter(wx, wy, color="green", s=40, zorder=5, marker="^")
        if losses:
            lx, ly = zip(*losses)
            ax.scatter(lx, ly, color="red", s=40, zorder=5, marker="v")

        setup_stats = backtest_result.get("stats_by_setup_type", {}).get(setup_name, {})
        title = (f"{setup_name}: {setup_stats.get('n_trades', 0)}t  "
                 f"WR {setup_stats.get('win_rate', 0):.0%}  "
                 f"PnL ${setup_stats.get('total_pnl_usd', 0):+.0f}  "
                 f"PF {setup_stats.get('profit_factor', '-')}")
        ax.set_title(title, fontsize=11, fontweight="bold")
        ax.set_xlabel("Fecha", fontsize=9)
        ax.set_ylabel("PnL Acumulado (USD)", fontsize=9)
        ax.grid(True, alpha=0.3)
        ax.xaxis.set_major_formatter(mdates.DateFormatter("%m-%d"))
        ax.tick_params(axis="both", labelsize=8)

    fig.suptitle(f"NADRO Backtest por Setup — {instrument}", fontsize=14, fontweight="bold")
    fig.autofmt_xdate()
    plt.tight_layout()
    path2 = out_dir / f"{prefix}_by_setup.png"
    fig.savefig(path2, dpi=100, bbox_inches="tight")
    plt.close(fig)
    paths["by_setup"] = str(path2)

    # --- 3. Daily PnL bars ---
    fig, ax = plt.subplots(figsize=(14, 6))
    daily = backtest_result.get("daily_breakdown", [])
    if daily:
        dates = [datetime.strptime(d["date"], "%Y-%m-%d") for d in daily]
        pnls = [d["pnl_usd"] for d in daily]
        colors = ["#2ecc71" if p > 0 else "#e74c3c" for p in pnls]
        ax.bar(dates, pnls, color=colors, edgecolor="black", linewidth=0.6, width=0.8)
        ax.axhline(0, color="black", linewidth=0.8)
        ax.set_title(f"PnL Diario — {instrument} ({len(daily)} días con trades)",
                     fontsize=13, fontweight="bold")
        ax.set_xlabel("Fecha")
        ax.set_ylabel("PnL del día (USD)")
        ax.grid(True, alpha=0.3, axis="y")
        ax.xaxis.set_major_formatter(mdates.DateFormatter("%m-%d"))
        fig.autofmt_xdate()
    path3 = out_dir / f"{prefix}_daily_pnl.png"
    fig.savefig(path3, dpi=100, bbox_inches="tight")
    plt.close(fig)
    paths["daily_pnl"] = str(path3)

    # --- 4. Trade distribution (histograma) ---
    fig, ax = plt.subplots(figsize=(12, 6))
    for setup_name, setup_trades in by_setup.items():
        if not setup_trades:
            continue
        pnls_pts = [t["pnl_pts"] for t in setup_trades]
        ax.hist(pnls_pts, bins=15, color=_SETUP_COLORS[setup_name], alpha=0.5,
                edgecolor="black", label=f"{setup_name} ({len(setup_trades)})")
    ax.axvline(0, color="black", linewidth=1, linestyle="--")
    ax.set_title(f"Distribución de PnL por trade (pts) — {instrument}",
                 fontsize=13, fontweight="bold")
    ax.set_xlabel("PnL del trade (puntos)")
    ax.set_ylabel("Frecuencia")
    ax.legend()
    ax.grid(True, alpha=0.3, axis="y")
    path4 = out_dir / f"{prefix}_distribution.png"
    fig.savefig(path4, dpi=100, bbox_inches="tight")
    plt.close(fig)
    paths["distribution"] = str(path4)

    return {
        "instrument": instrument,
        "charts_generated": paths,
        "output_dir": str(out_dir),
    }


def nadro_backtest_with_charts(
    instrument: str,
    days_back: int = 30,
    tf: str = "15m",
    window_start: str = "07:00",
    window_end: str = "23:00",
    **kwargs,
) -> dict:
    """Corre el backtest NADRO y genera los 4 gráficos automáticamente."""
    result = bt_tool.nadro_backtest(
        instrument=instrument,
        days_back=days_back,
        tf=tf,
        window_start=window_start,
        window_end=window_end,
        **kwargs,
    )
    if "error" in result:
        return result
    charts = generate_backtest_charts(result, instrument=instrument.replace(" ", "_"))
    result["charts"] = charts
    return result
