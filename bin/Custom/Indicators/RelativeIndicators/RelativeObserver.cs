#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// RelativeObserver — AddOn puente HTTP para el servidor MCP RelativeIndicators.
//
// Expone un HttpListener en http://localhost:7891/ con endpoints de solo-lectura
// que permiten al MCP Python consultar estado vivo de NT8 (cotizaciones, barras,
// ticks, lista de suscripciones). Escrito en 2026-04-20.
//
// Alcance intencional:
//   * Solo lectura, sin ejecución de órdenes.
//   * Localhost exclusivamente (nunca exponer al exterior).
//   * No toca Draw / Series / UI — los handlers HTTP corren en threadpool.
//   * Thread-safe vía _stateLock; OnMarketData escribe snapshots, el handler lee.
//
// Endpoints:
//   GET  /health                        → info básica
//   GET  /subscriptions                 → lista de instrumentos suscritos
//   POST /subscribe/{instrument}        → suscribe market data
//   DELETE /subscribe/{instrument}      → cancela suscripción
//   GET  /quote/{instrument}            → last/bid/ask/volume
//   GET  /ticks/{instrument}?n=200      → últimos N ticks del buffer circular
//   GET  /bars/{instrument}?tf=1m&n=50  → últimas N barras (request async)
//
// Registrado en NinjaTrader.Custom.csproj como:
//   <Compile Include="Indicators\RelativeIndicators\RelativeObserver.cs" />

namespace NinjaTrader.NinjaScript.AddOns
{
    public class RelativeObserver : AddOnBase
    {
        #region Configuration

        private const string LISTEN_PREFIX = "http://localhost:7891/";
        private const int TICK_BUFFER_SIZE = 5000;
        private const int BARS_REQUEST_TIMEOUT_MS = 15000;

        #endregion

        #region Fields

        private static RelativeObserver _instance;
        private static readonly object _instanceLock = new object();

        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private DateTime _startTime;

        // Estado de suscripciones. Toda mutación debe ir bajo _stateLock.
        private readonly object _stateLock = new object();
        private readonly Dictionary<string, InstrumentSubscription> _subs =
            new Dictionary<string, InstrumentSubscription>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RelativeObserver";
                Description = "HTTP bridge para MCP (localhost:7891) — solo lectura";
            }
            else if (State == State.Active)
            {
                lock (_instanceLock)
                {
                    if (_instance != null && _instance != this)
                    {
                        Log("Otra instancia activa — ignorando State.Active duplicado.");
                        return;
                    }
                    _instance = this;
                }
                StartListener();
            }
            else if (State == State.Terminated)
            {
                StopListener();
                lock (_instanceLock)
                {
                    if (_instance == this) _instance = null;
                }
            }
        }

        private void StartListener()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(LISTEN_PREFIX);
                _listener.Start();
                _running = true;
                _startTime = DateTime.UtcNow;
                _listenerThread = new Thread(ListenerLoop)
                {
                    IsBackground = true,
                    Name = "RelativeObserver.HttpListener",
                };
                _listenerThread.Start();
                Log("HTTP listening en " + LISTEN_PREFIX);
            }
            catch (Exception ex)
            {
                Log("ERROR al arrancar HttpListener: " + ex.Message);
            }
        }

        private void StopListener()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }

            // Cancelar todas las suscripciones
            List<InstrumentSubscription> toDispose;
            lock (_stateLock)
            {
                toDispose = _subs.Values.ToList();
                _subs.Clear();
            }
            foreach (var s in toDispose)
                try { s.Dispose(); } catch { }

            Log("HTTP listener detenido.");
        }

        #endregion

        #region Listener Loop

        private void ListenerLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (_running) Log("GetContext error: " + ex.Message);
                    continue;
                }

                ThreadPool.QueueUserWorkItem(state =>
                {
                    try { HandleRequest((HttpListenerContext)state); }
                    catch (Exception ex) { Log("Handler error: " + ex.Message); }
                }, ctx);
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath ?? "/";
                string method = ctx.Request.HttpMethod;
                string[] parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                {
                    WriteJson(ctx, 200, "{\"service\":\"RelativeObserver\",\"version\":\"0.1.0\"}");
                    return;
                }

                string root = parts[0].ToLowerInvariant();

                if (root == "health" && method == "GET") { HandleHealth(ctx); return; }
                if (root == "subscriptions" && method == "GET") { HandleSubscriptionsList(ctx); return; }
                if (root == "accounts" && method == "GET") { HandleAccounts(ctx); return; }
                if (root == "positions" && method == "GET") { HandlePositions(ctx); return; }
                if (root == "orders" && method == "GET") { HandleOrders(ctx); return; }
                if (root == "executions" && method == "GET") { HandleExecutions(ctx); return; }
                if (root == "trades" && method == "GET") { HandleTrades(ctx); return; }
                if (root == "charts" && method == "GET") { HandleCharts(ctx); return; }
                if (root == "indicator-state" && method == "GET" && parts.Length == 1)
                { HandleIndicatorStatesList(ctx); return; }
                // /indicator-state/{key}/at?ts=ISO  → query historica via QueryAt handler
                if (root == "indicator-state" && method == "GET" && parts.Length >= 3
                    && parts[parts.Length - 1].Equals("at", StringComparison.OrdinalIgnoreCase))
                {
                    string keyJoined = Uri.UnescapeDataString(string.Join("/", parts.Skip(1).Take(parts.Length - 2)));
                    HandleIndicatorStateAt(ctx, keyJoined);
                    return;
                }
                if (root == "indicator-state" && method == "GET" && parts.Length >= 2)
                { HandleIndicatorState(ctx, Uri.UnescapeDataString(string.Join("/", parts.Skip(1)))); return; }
                if (root == "print-output" && method == "GET") { HandlePrintOutput(ctx); return; }
                if (root == "print-output" && method == "DELETE") { HandlePrintOutputClear(ctx); return; }

                if (parts.Length >= 2)
                {
                    string instrumentName = Uri.UnescapeDataString(parts[1]);

                    if (root == "subscribe" && method == "POST")
                    { HandleSubscribe(ctx, instrumentName); return; }
                    if (root == "subscribe" && method == "DELETE")
                    { HandleUnsubscribe(ctx, instrumentName); return; }
                    if (root == "quote" && method == "GET")
                    { HandleQuote(ctx, instrumentName); return; }
                    if (root == "ticks" && method == "GET")
                    { HandleTicks(ctx, instrumentName); return; }
                    if (root == "bars" && method == "GET")
                    { HandleBars(ctx, instrumentName); return; }
                }

                WriteJson(ctx, 404, "{\"error\":\"not found: " + Escape(method + " " + path) + "\"}");
            }
            catch (Exception ex)
            {
                try { WriteJson(ctx, 500, "{\"error\":" + Quote(ex.Message) + "}"); }
                catch { }
            }
        }

        #endregion

        #region Endpoint: /health

        private void HandleHealth(HttpListenerContext ctx)
        {
            int subCount;
            lock (_stateLock) subCount = _subs.Count;
            double uptime = (DateTime.UtcNow - _startTime).TotalSeconds;

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"service\":\"RelativeObserver\",");
            sb.Append("\"version\":\"0.1.0\",");
            sb.Append("\"running\":true,");
            sb.Append("\"uptime_seconds\":").Append(uptime.ToString("F1", CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"subscriptions\":").Append(subCount).Append(",");
            sb.Append("\"connections\":[");
            var conns = Connection.Connections.ToList();
            for (int i = 0; i < conns.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":").Append(Quote(conns[i].Options.Name));
                sb.Append(",\"status\":").Append(Quote(conns[i].Status.ToString()));
                sb.Append(",\"price_status\":").Append(Quote(conns[i].PriceStatus.ToString()));
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        #endregion

        #region Endpoint: /subscriptions

        private void HandleSubscriptionsList(HttpListenerContext ctx)
        {
            List<string> names;
            lock (_stateLock) names = _subs.Keys.ToList();

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(names.Count).Append(",\"instruments\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Quote(names[i]));
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        #endregion

        #region Endpoint: /subscribe

        private void HandleSubscribe(HttpListenerContext ctx, string instrumentName)
        {
            var sub = EnsureSubscription(instrumentName, out string err);
            if (sub == null)
            {
                WriteJson(ctx, 400, "{\"error\":" + Quote(err) + "}");
                return;
            }
            WriteJson(ctx, 200, "{\"subscribed\":true,\"instrument\":" + Quote(sub.FullName) + "}");
        }

        private void HandleUnsubscribe(HttpListenerContext ctx, string instrumentName)
        {
            InstrumentSubscription sub = null;
            lock (_stateLock)
            {
                if (_subs.TryGetValue(instrumentName, out sub))
                    _subs.Remove(instrumentName);
            }
            if (sub != null) sub.Dispose();
            WriteJson(ctx, 200, "{\"subscribed\":false,\"instrument\":" + Quote(instrumentName) + "}");
        }

        private InstrumentSubscription EnsureSubscription(string instrumentName, out string error)
        {
            error = null;
            lock (_stateLock)
            {
                if (_subs.TryGetValue(instrumentName, out var existing))
                    return existing;
            }

            Instrument instrument;
            try { instrument = Instrument.GetInstrument(instrumentName); }
            catch (Exception ex) { error = "GetInstrument falló: " + ex.Message; return null; }

            if (instrument == null)
            {
                error = "Instrumento no encontrado: " + instrumentName;
                return null;
            }

            var sub = new InstrumentSubscription(instrument, TICK_BUFFER_SIZE);
            sub.Start();

            lock (_stateLock)
            {
                // Re-check por race condition
                if (_subs.TryGetValue(instrumentName, out var existing))
                {
                    sub.Dispose();
                    return existing;
                }
                _subs[instrumentName] = sub;
                // También indexar por FullName canónico
                if (!_subs.ContainsKey(sub.FullName))
                    _subs[sub.FullName] = sub;
            }
            return sub;
        }

        #endregion

        #region Endpoint: /quote

        private void HandleQuote(HttpListenerContext ctx, string instrumentName)
        {
            var sub = EnsureSubscription(instrumentName, out string err);
            if (sub == null)
            {
                WriteJson(ctx, 400, "{\"error\":" + Quote(err) + "}");
                return;
            }

            QuoteSnapshot q;
            lock (sub.DataLock) q = sub.GetQuoteSnapshot();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"instrument\":").Append(Quote(sub.FullName)).Append(",");
            sb.Append("\"last\":").Append(FormatDouble(q.Last)).Append(",");
            sb.Append("\"bid\":").Append(FormatDouble(q.Bid)).Append(",");
            sb.Append("\"ask\":").Append(FormatDouble(q.Ask)).Append(",");
            sb.Append("\"last_volume\":").Append(q.LastVolume).Append(",");
            sb.Append("\"day_volume\":").Append(q.DayVolume).Append(",");
            sb.Append("\"last_time\":").Append(Quote(FormatTime(q.LastTime))).Append(",");
            sb.Append("\"tick_count\":").Append(q.TickCount).Append(",");
            sb.Append("\"deduped_count\":").Append(q.DedupedCount);
            sb.Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        #endregion

        #region Endpoint: /ticks

        private void HandleTicks(HttpListenerContext ctx, string instrumentName)
        {
            var sub = EnsureSubscription(instrumentName, out string err);
            if (sub == null)
            {
                WriteJson(ctx, 400, "{\"error\":" + Quote(err) + "}");
                return;
            }

            int n = ParseIntQuery(ctx.Request.QueryString, "n", 200);
            if (n <= 0) n = 200;
            if (n > TICK_BUFFER_SIZE) n = TICK_BUFFER_SIZE;

            TickRecord[] ticks;
            lock (sub.DataLock) ticks = sub.GetLastTicks(n);

            var sb = new StringBuilder();
            sb.Append("{\"instrument\":").Append(Quote(sub.FullName));
            sb.Append(",\"count\":").Append(ticks.Length);
            sb.Append(",\"ticks\":[");
            for (int i = 0; i < ticks.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var t = ticks[i];
                sb.Append("{\"t\":").Append(Quote(FormatTime(t.Time)));
                sb.Append(",\"type\":").Append(Quote(t.Type));
                sb.Append(",\"price\":").Append(FormatDouble(t.Price));
                sb.Append(",\"vol\":").Append(t.Volume);
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        #endregion

        #region Endpoint: /bars

        private void HandleBars(HttpListenerContext ctx, string instrumentName)
        {
            Instrument instrument;
            try { instrument = Instrument.GetInstrument(instrumentName); }
            catch (Exception ex)
            {
                WriteJson(ctx, 400, "{\"error\":" + Quote("GetInstrument falló: " + ex.Message) + "}");
                return;
            }
            if (instrument == null)
            {
                WriteJson(ctx, 404, "{\"error\":\"instrumento no encontrado\"}");
                return;
            }

            int n = ParseIntQuery(ctx.Request.QueryString, "n", 50);
            if (n <= 0) n = 50;
            if (n > 50000) n = 50000; // bumped from 2000 to soportar backtests >30 días en 5m

            string tf = (ctx.Request.QueryString["tf"] ?? "1m").Trim().ToLowerInvariant();
            if (!TryParseTimeframe(tf, out BarsPeriodType periodType, out int value))
            {
                WriteJson(ctx, 400, "{\"error\":" + Quote("timeframe inválido: " + tf + ". Ejemplos: 1m, 5m, 15m, 1h, 1d, 1t, 100t") + "}");
                return;
            }

            BarsRequest req = new BarsRequest(instrument, n)
            {
                BarsPeriod = new BarsPeriod { BarsPeriodType = periodType, Value = value },
            };

            var reset = new ManualResetEventSlim();
            BarsRequest resultReq = null;
            ErrorCode barsErrorCode = ErrorCode.NoError;
            string barsErrorMsg = null;
            try
            {
                // NT8: Action<BarsRequest, ErrorCode, string> — no Exception.
                req.Request(new Action<BarsRequest, ErrorCode, string>((r, ec, msg) =>
                {
                    resultReq = r;
                    barsErrorCode = ec;
                    barsErrorMsg = msg;
                    reset.Set();
                }));
            }
            catch (Exception ex)
            {
                WriteJson(ctx, 500, "{\"error\":" + Quote("BarsRequest falló: " + ex.Message) + "}");
                return;
            }

            if (!reset.Wait(BARS_REQUEST_TIMEOUT_MS))
            {
                WriteJson(ctx, 504, "{\"error\":\"BarsRequest timeout\"}");
                return;
            }

            if (barsErrorCode != ErrorCode.NoError)
            {
                WriteJson(ctx, 500, "{\"error\":" + Quote(barsErrorCode + ": " + (barsErrorMsg ?? "")) + "}");
                return;
            }

            var bars = resultReq != null ? resultReq.Bars : null;
            if (bars == null)
            {
                WriteJson(ctx, 500, "{\"error\":\"bars null\"}");
                return;
            }

            int total = bars.Count;
            int from = Math.Max(0, total - n);

            var sb = new StringBuilder();
            sb.Append("{\"instrument\":").Append(Quote(instrument.FullName));
            sb.Append(",\"timeframe\":").Append(Quote(tf));
            sb.Append(",\"count\":").Append(total - from);
            sb.Append(",\"bars\":[");
            bool first = true;
            for (int i = from; i < total; i++)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"t\":").Append(Quote(FormatTime(bars.GetTime(i))));
                sb.Append(",\"o\":").Append(FormatDouble(bars.GetOpen(i)));
                sb.Append(",\"h\":").Append(FormatDouble(bars.GetHigh(i)));
                sb.Append(",\"l\":").Append(FormatDouble(bars.GetLow(i)));
                sb.Append(",\"c\":").Append(FormatDouble(bars.GetClose(i)));
                sb.Append(",\"v\":").Append(bars.GetVolume(i));
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static bool TryParseTimeframe(string tf, out BarsPeriodType type, out int value)
        {
            type = BarsPeriodType.Minute; value = 1;
            if (string.IsNullOrEmpty(tf)) return false;
            char suffix = tf[tf.Length - 1];
            string numPart = tf.Substring(0, tf.Length - 1);
            if (!int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
                return false;
            switch (suffix)
            {
                case 's': type = BarsPeriodType.Second; return true;
                case 'm': type = BarsPeriodType.Minute; return true;
                case 'h': type = BarsPeriodType.Minute; value *= 60; return true;
                case 'd': type = BarsPeriodType.Day; return true;
                case 't': type = BarsPeriodType.Tick; return true;
                case 'v': type = BarsPeriodType.Volume; return true;
                case 'r': type = BarsPeriodType.Range; return true;
                default: return false;
            }
        }

        #endregion

        #region Endpoint: /accounts

        private void HandleAccounts(HttpListenerContext ctx)
        {
            var accounts = Account.All.ToList();
            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(accounts.Count).Append(",\"accounts\":[");
            for (int i = 0; i < accounts.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var a = accounts[i];
                sb.Append("{\"name\":").Append(Quote(a.Name));
                sb.Append(",\"display_name\":").Append(Quote(SafeString(() => a.DisplayName)));
                sb.Append(",\"connection\":").Append(Quote(SafeString(() => a.Connection != null ? a.Connection.Options.Name : "")));
                sb.Append(",\"connection_status\":").Append(Quote(SafeString(() => a.Connection != null ? a.Connection.Status.ToString() : "")));
                int posCount = 0, ordCount = 0;
                try { posCount = a.Positions.Count(p => p.MarketPosition != MarketPosition.Flat); } catch { }
                try { ordCount = a.Orders.Count(o => IsActiveOrderState(o.OrderState)); } catch { }
                sb.Append(",\"open_positions\":").Append(posCount);
                sb.Append(",\"active_orders\":").Append(ordCount);
                sb.Append(",\"cash_value\":").Append(FormatDouble(SafeAccountItem(a, AccountItem.CashValue)));
                sb.Append(",\"buying_power\":").Append(FormatDouble(SafeAccountItem(a, AccountItem.BuyingPower)));
                sb.Append(",\"realized_pnl\":").Append(FormatDouble(SafeAccountItem(a, AccountItem.RealizedProfitLoss)));
                sb.Append(",\"unrealized_pnl\":").Append(FormatDouble(SafeAccountItem(a, AccountItem.UnrealizedProfitLoss)));
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static double SafeAccountItem(Account a, AccountItem item)
        {
            try { return a.Get(item, Currency.UsDollar); }
            catch { return double.NaN; }
        }

        private static string SafeString(Func<string> fn)
        {
            try { return fn() ?? ""; } catch { return ""; }
        }

        private static bool IsActiveOrderState(OrderState s)
        {
            return s == OrderState.Working
                || s == OrderState.Accepted
                || s == OrderState.Submitted
                || s == OrderState.ChangeSubmitted
                || s == OrderState.CancelSubmitted
                || s == OrderState.TriggerPending
                || s == OrderState.PartFilled;
        }

        #endregion

        #region Endpoint: /positions

        private void HandlePositions(HttpListenerContext ctx)
        {
            string accFilter = ctx.Request.QueryString["account"];
            bool includeFlat = string.Equals(ctx.Request.QueryString["include_flat"], "true", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.Append("{\"positions\":[");
            bool first = true;
            int count = 0;

            foreach (var acc in Account.All)
            {
                if (!string.IsNullOrEmpty(accFilter) &&
                    !acc.Name.Equals(accFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                IEnumerable<Position> positions;
                try { positions = acc.Positions.ToList(); }
                catch { continue; }

                foreach (var p in positions)
                {
                    if (!includeFlat && p.MarketPosition == MarketPosition.Flat) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    count++;

                    sb.Append("{\"account\":").Append(Quote(acc.Name));
                    sb.Append(",\"instrument\":").Append(Quote(SafeString(() => p.Instrument != null ? p.Instrument.FullName : "")));
                    sb.Append(",\"market_position\":").Append(Quote(p.MarketPosition.ToString()));
                    sb.Append(",\"quantity\":").Append(p.Quantity);
                    sb.Append(",\"avg_price\":").Append(FormatDouble(p.AveragePrice));

                    // Obtenemos el precio de mercado actual para calcular unrealized PnL.
                    // GetRealizedProfitLoss no existe en Position — el realized vive en Account.
                    double lastPrice = double.NaN;
                    try { lastPrice = p.Instrument != null && p.Instrument.MarketData != null && p.Instrument.MarketData.Last != null
                        ? p.Instrument.MarketData.Last.Price
                        : double.NaN; } catch { }

                    double unr = double.NaN;
                    try
                    {
                        if (!double.IsNaN(lastPrice))
                            unr = p.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastPrice);
                        else
                            unr = p.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                    }
                    catch { }
                    sb.Append(",\"unrealized_pnl\":").Append(FormatDouble(unr));
                    sb.Append(",\"last_price\":").Append(FormatDouble(lastPrice));

                    sb.Append("}");
                }
            }
            sb.Append("],\"count\":").Append(count).Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        #endregion

        #region Endpoint: /orders

        private void HandleOrders(HttpListenerContext ctx)
        {
            string accFilter = ctx.Request.QueryString["account"];
            string stateFilter = ctx.Request.QueryString["state"]; // ej: "active", "filled", "all"
            if (string.IsNullOrEmpty(stateFilter)) stateFilter = "active";

            var sb = new StringBuilder();
            sb.Append("{\"orders\":[");
            bool first = true;
            int count = 0;

            foreach (var acc in Account.All)
            {
                if (!string.IsNullOrEmpty(accFilter) &&
                    !acc.Name.Equals(accFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                IEnumerable<Order> orders;
                try { orders = acc.Orders.ToList(); }
                catch { continue; }

                foreach (var o in orders)
                {
                    bool keep;
                    switch (stateFilter.ToLowerInvariant())
                    {
                        case "active": keep = IsActiveOrderState(o.OrderState); break;
                        case "filled": keep = o.OrderState == OrderState.Filled || o.OrderState == OrderState.PartFilled; break;
                        case "all": keep = true; break;
                        default: keep = IsActiveOrderState(o.OrderState); break;
                    }
                    if (!keep) continue;

                    if (!first) sb.Append(",");
                    first = false;
                    count++;

                    sb.Append("{\"account\":").Append(Quote(acc.Name));
                    // Order.Id es long (ID interno); OrderId es string del broker.
                    sb.Append(",\"id\":").Append(Quote(SafeString(() => o.Id.ToString(CultureInfo.InvariantCulture))));
                    sb.Append(",\"order_id\":").Append(Quote(SafeString(() => o.OrderId)));
                    sb.Append(",\"name\":").Append(Quote(SafeString(() => o.Name)));
                    sb.Append(",\"instrument\":").Append(Quote(SafeString(() => o.Instrument != null ? o.Instrument.FullName : "")));
                    sb.Append(",\"action\":").Append(Quote(o.OrderAction.ToString()));
                    sb.Append(",\"type\":").Append(Quote(o.OrderType.ToString()));
                    sb.Append(",\"state\":").Append(Quote(o.OrderState.ToString()));
                    sb.Append(",\"quantity\":").Append(o.Quantity);
                    sb.Append(",\"filled\":").Append(o.Filled);
                    sb.Append(",\"limit_price\":").Append(FormatDouble(o.LimitPrice));
                    sb.Append(",\"stop_price\":").Append(FormatDouble(o.StopPrice));
                    sb.Append(",\"avg_fill_price\":").Append(FormatDouble(SafeDouble(() => o.AverageFillPrice)));
                    sb.Append(",\"time\":").Append(Quote(FormatTime(SafeDateTime(() => o.Time))));
                    sb.Append(",\"tif\":").Append(Quote(o.TimeInForce.ToString()));
                    sb.Append(",\"oco\":").Append(Quote(SafeString(() => o.Oco)));
                    sb.Append("}");
                }
            }
            sb.Append("],\"count\":").Append(count).Append(",\"state_filter\":").Append(Quote(stateFilter)).Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static double SafeDouble(Func<double> fn) { try { return fn(); } catch { return double.NaN; } }
        private static DateTime SafeDateTime(Func<DateTime> fn) { try { return fn(); } catch { return DateTime.MinValue; } }

        #endregion

        #region Endpoint: /executions

        private void HandleExecutions(HttpListenerContext ctx)
        {
            string accFilter = ctx.Request.QueryString["account"];
            int n = ParseIntQuery(ctx.Request.QueryString, "n", 50);
            if (n <= 0) n = 50;
            if (n > 500) n = 500;

            // Opcional: filtro por antigüedad
            int sinceHours = ParseIntQuery(ctx.Request.QueryString, "since_hours", 0);
            DateTime? cutoff = sinceHours > 0 ? (DateTime?)DateTime.Now.AddHours(-sinceHours) : null;

            var all = new List<Execution>();
            foreach (var acc in Account.All)
            {
                if (!string.IsNullOrEmpty(accFilter) &&
                    !acc.Name.Equals(accFilter, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var ex in acc.Executions)
                    {
                        if (cutoff.HasValue && SafeDateTime(() => ex.Time) < cutoff.Value) continue;
                        all.Add(ex);
                    }
                }
                catch { }
            }

            // Orden cronológico descendente, toma últimos n, luego re-orden ascendente
            all.Sort((a, b) => SafeDateTime(() => a.Time).CompareTo(SafeDateTime(() => b.Time)));
            if (all.Count > n) all = all.GetRange(all.Count - n, n);

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(all.Count).Append(",\"executions\":[");
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var ex = all[i];
                sb.Append("{\"account\":").Append(Quote(SafeString(() => ex.Account != null ? ex.Account.Name : "")));
                sb.Append(",\"id\":").Append(Quote(SafeString(() => ex.ExecutionId)));
                sb.Append(",\"time\":").Append(Quote(FormatTime(SafeDateTime(() => ex.Time))));
                sb.Append(",\"instrument\":").Append(Quote(SafeString(() => ex.Instrument != null ? ex.Instrument.FullName : "")));
                sb.Append(",\"market_position\":").Append(Quote(SafeString(() => ex.MarketPosition.ToString())));
                sb.Append(",\"price\":").Append(FormatDouble(SafeDouble(() => ex.Price)));
                sb.Append(",\"quantity\":").Append(SafeInt(() => ex.Quantity));
                sb.Append(",\"commission\":").Append(FormatDouble(SafeDouble(() => ex.Commission)));
                sb.Append(",\"order_id\":").Append(Quote(SafeString(() => ex.Order != null ? ex.Order.OrderId : "")));
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static int SafeInt(Func<int> fn) { try { return fn(); } catch { return 0; } }

        #endregion

        #region Endpoint: /trades

        private void HandleTrades(HttpListenerContext ctx)
        {
            string accFilter = ctx.Request.QueryString["account"];
            int n = ParseIntQuery(ctx.Request.QueryString, "n", 50);
            if (n <= 0) n = 50;
            if (n > 500) n = 500;

            var all = new List<TradePair>();

            foreach (var acc in Account.All)
            {
                if (!string.IsNullOrEmpty(accFilter) &&
                    !acc.Name.Equals(accFilter, StringComparison.OrdinalIgnoreCase)) continue;

                // Intento 1: SystemPerformance.AllTrades (cubre trades originados por strategies)
                var fromPerf = TryGetSystemPerformanceTrades(acc);
                if (fromPerf.Count > 0) { all.AddRange(fromPerf); continue; }

                // Intento 2: emparejar Executions manualmente (cubre trading manual/ChartTrader/ATM)
                all.AddRange(PairExecutions(acc));
            }

            // Orden por entry_time ascendente, últimos n
            all.Sort((a, b) => a.EntryTime.CompareTo(b.EntryTime));
            int start = Math.Max(0, all.Count - n);

            var sb = new StringBuilder();
            sb.Append("{\"trades\":[");
            for (int i = start; i < all.Count; i++)
            {
                if (i > start) sb.Append(",");
                var t = all[i];
                sb.Append("{\"account\":").Append(Quote(t.Account));
                sb.Append(",\"instrument\":").Append(Quote(t.Instrument));
                sb.Append(",\"direction\":").Append(Quote(t.Direction));
                sb.Append(",\"quantity\":").Append(t.Quantity);
                sb.Append(",\"entry_time\":").Append(Quote(FormatTime(t.EntryTime)));
                sb.Append(",\"entry_price\":").Append(FormatDouble(t.EntryPrice));
                sb.Append(",\"exit_time\":").Append(Quote(FormatTime(t.ExitTime)));
                sb.Append(",\"exit_price\":").Append(FormatDouble(t.ExitPrice));
                sb.Append(",\"profit_currency\":").Append(FormatDouble(t.ProfitCurrency));
                sb.Append(",\"profit_points\":").Append(FormatDouble(t.ProfitPoints));
                sb.Append(",\"profit_ticks\":").Append(t.ProfitTicks);
                sb.Append(",\"mae\":").Append(FormatDouble(t.Mae));
                sb.Append(",\"mfe\":").Append(FormatDouble(t.Mfe));
                sb.Append(",\"commission\":").Append(FormatDouble(t.Commission));
                sb.Append(",\"duration_seconds\":").Append(FormatDouble(t.DurationSeconds));
                sb.Append(",\"source\":").Append(Quote(t.Source));
                sb.Append("}");
            }
            sb.Append("],\"count\":").Append(all.Count - start).Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static List<TradePair> TryGetSystemPerformanceTrades(Account acc)
        {
            var result = new List<TradePair>();
            try
            {
                var sysPerf = GetPropertyValue(acc, "SystemPerformance");
                if (sysPerf == null) return result;
                var allTrades = GetPropertyValue(sysPerf, "AllTrades") as System.Collections.IEnumerable;
                if (allTrades == null) return result;

                foreach (var t in allTrades)
                {
                    if (t == null) continue;
                    var entry = GetPropertyValue(t, "Entry");
                    var exit = GetPropertyValue(t, "Exit");
                    if (entry == null || exit == null) continue;

                    var instr = GetPropertyValue(entry, "Instrument");
                    var entryTime = SafeDateTime(() => Convert.ToDateTime(GetPropertyValue(entry, "Time")));
                    var exitTime = SafeDateTime(() => Convert.ToDateTime(GetPropertyValue(exit, "Time")));

                    result.Add(new TradePair
                    {
                        Account = acc.Name,
                        Instrument = SafeString(() => (GetPropertyValue(instr, "FullName") ?? "").ToString()),
                        Direction = SafeString(() => (GetPropertyValue(entry, "MarketPosition") ?? "").ToString()),
                        Quantity = SafeInt(() => Convert.ToInt32(GetPropertyValue(t, "Quantity") ?? 0)),
                        EntryTime = entryTime,
                        ExitTime = exitTime,
                        EntryPrice = SafeDouble(() => Convert.ToDouble(GetPropertyValue(entry, "Price"))),
                        ExitPrice = SafeDouble(() => Convert.ToDouble(GetPropertyValue(exit, "Price"))),
                        ProfitCurrency = SafeDouble(() => Convert.ToDouble(GetPropertyValue(t, "ProfitCurrency"))),
                        ProfitPoints = SafeDouble(() => Convert.ToDouble(GetPropertyValue(t, "ProfitPoints"))),
                        ProfitTicks = SafeInt(() => Convert.ToInt32(GetPropertyValue(t, "ProfitTicks") ?? 0)),
                        Mae = SafeDouble(() => Convert.ToDouble(GetPropertyValue(t, "MaeCurrency"))),
                        Mfe = SafeDouble(() => Convert.ToDouble(GetPropertyValue(t, "MfeCurrency"))),
                        Commission = SafeDouble(() => Convert.ToDouble(GetPropertyValue(t, "Commission"))),
                        DurationSeconds = (exitTime - entryTime).TotalSeconds,
                        Source = "system_performance",
                    });
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Empareja executions en trades cerrados para trading manual / ChartTrader / ATM.
        /// Algoritmo: para cada instrumento, sortea executions por tiempo, lleva net position
        /// firmada. Cuando net transita de 0 a distinto-de-0 marca entry; cuando vuelve a 0
        /// cierra el trade. Soporta pyramiding simple (acumula quantity en entry).
        /// No soporta flips (long→short directo); los trata como cierre + nueva entrada.
        /// </summary>
        private static List<TradePair> PairExecutions(Account acc)
        {
            var result = new List<TradePair>();
            List<Execution> allEx;
            try { allEx = acc.Executions.ToList(); } catch { return result; }

            var byInstr = new Dictionary<string, List<Execution>>();
            foreach (var ex in allEx)
            {
                string name = SafeString(() => ex.Instrument != null ? ex.Instrument.FullName : "?");
                if (!byInstr.TryGetValue(name, out var list))
                {
                    list = new List<Execution>();
                    byInstr[name] = list;
                }
                list.Add(ex);
            }

            foreach (var kv in byInstr)
            {
                var execs = kv.Value;
                execs.Sort((a, b) => SafeDateTime(() => a.Time).CompareTo(SafeDateTime(() => b.Time)));

                int net = 0;
                double entryCostWeighted = 0;    // sum(price*qty) para avg entry
                int entryQtyAccum = 0;
                DateTime entryTime = DateTime.MinValue;
                double mae = 0, mfe = 0;          // aproximado vs avg entry
                double commissionAccum = 0;
                string direction = "";

                foreach (var ex in execs)
                {
                    int qty = SafeInt(() => ex.Quantity);
                    double price = SafeDouble(() => ex.Price);
                    double comm = SafeDouble(() => ex.Commission);
                    DateTime t = SafeDateTime(() => ex.Time);
                    bool isLong = ex.MarketPosition == MarketPosition.Long;
                    int signed = qty * (isLong ? 1 : -1);
                    int prevNet = net;
                    net += signed;
                    commissionAccum += double.IsNaN(comm) ? 0 : comm;

                    if (prevNet == 0 && net != 0)
                    {
                        // Entry fresh
                        entryCostWeighted = price * qty;
                        entryQtyAccum = qty;
                        entryTime = t;
                        direction = isLong ? "Long" : "Short";
                        mae = 0; mfe = 0;
                    }
                    else if (Math.Sign(prevNet) == Math.Sign(net) && net != 0)
                    {
                        // Pyramiding — agregamos al entry
                        entryCostWeighted += price * qty;
                        entryQtyAccum += qty;
                    }
                    else if (net == 0 && prevNet != 0)
                    {
                        // Close
                        double avgEntry = entryQtyAccum > 0 ? entryCostWeighted / entryQtyAccum : price;
                        double sign = direction == "Long" ? 1.0 : -1.0;
                        double profitPts = (price - avgEntry) * sign;

                        double pointValue = 1.0;
                        try
                        {
                            var mi = GetPropertyValue(ex.Instrument, "MasterInstrument");
                            if (mi != null)
                                pointValue = Convert.ToDouble(GetPropertyValue(mi, "PointValue"));
                        }
                        catch { }
                        double tickSize = 0.25;
                        try
                        {
                            var mi = GetPropertyValue(ex.Instrument, "MasterInstrument");
                            if (mi != null)
                                tickSize = Convert.ToDouble(GetPropertyValue(mi, "TickSize"));
                        }
                        catch { }

                        double profitCurrency = profitPts * pointValue * entryQtyAccum - commissionAccum;
                        int profitTicks = tickSize > 0 ? (int)Math.Round(profitPts / tickSize) : 0;

                        result.Add(new TradePair
                        {
                            Account = acc.Name,
                            Instrument = kv.Key,
                            Direction = direction,
                            Quantity = entryQtyAccum,
                            EntryTime = entryTime,
                            ExitTime = t,
                            EntryPrice = avgEntry,
                            ExitPrice = price,
                            ProfitCurrency = profitCurrency,
                            ProfitPoints = profitPts,
                            ProfitTicks = profitTicks,
                            Mae = mae,
                            Mfe = mfe,
                            Commission = commissionAccum,
                            DurationSeconds = (t - entryTime).TotalSeconds,
                            Source = "executions_paired",
                        });

                        // Reset
                        entryCostWeighted = 0;
                        entryQtyAccum = 0;
                        commissionAccum = 0;
                        direction = "";
                    }
                    // Flip (prev y new distintos signos, ambos != 0): no manejado — se ignora por simplicidad.
                }
            }
            return result;
        }

        private class TradePair
        {
            public string Account;
            public string Instrument;
            public string Direction;
            public int Quantity;
            public DateTime EntryTime;
            public DateTime ExitTime;
            public double EntryPrice;
            public double ExitPrice;
            public double ProfitCurrency;
            public double ProfitPoints;
            public int ProfitTicks;
            public double Mae;
            public double Mfe;
            public double Commission;
            public double DurationSeconds;
            public string Source;
        }

        #endregion

        #region Endpoint: /indicator-state

        private void HandleIndicatorStatesList(HttpListenerContext ctx)
        {
            var states = RelativeIndicatorRegistry.Snapshot();
            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(states.Count).Append(",\"states\":[");
            for (int i = 0; i < states.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var s = states[i];
                sb.Append("{\"key\":").Append(Quote(s.Key));
                sb.Append(",\"updated_at\":").Append(Quote(FormatTime(s.UpdatedAt)));
                sb.Append(",\"payload\":");
                AppendPayload(sb, s.Payload);
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private void HandleIndicatorState(HttpListenerContext ctx, string key)
        {
            var s = RelativeIndicatorRegistry.Get(key);
            if (s == null)
            {
                WriteJson(ctx, 404, "{\"error\":\"no state para key: " + Escape(key) + "\"}");
                return;
            }
            var sb = new StringBuilder();
            sb.Append("{\"key\":").Append(Quote(s.Key));
            sb.Append(",\"updated_at\":").Append(Quote(FormatTime(s.UpdatedAt)));
            sb.Append(",\"payload\":");
            AppendPayload(sb, s.Payload);
            sb.Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        // GET /indicator-state/{key}/at?ts=2026-04-22T09:25:00
        // Invoca el HistoricalQueryHandler registrado para `key` con el timestamp
        // dado y devuelve el dict resultante (DVAH/VWAP/DVAL/etc segun el indicador).
        private void HandleIndicatorStateAt(HttpListenerContext ctx, string key)
        {
            string tsStr = ctx.Request.QueryString["ts"];
            if (string.IsNullOrEmpty(tsStr))
            {
                WriteJson(ctx, 400, "{\"error\":\"falta query param ?ts=ISO timestamp\"}");
                return;
            }
            DateTime asOf;
            if (!DateTime.TryParse(tsStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out asOf))
            {
                if (!DateTime.TryParse(tsStr, out asOf))
                {
                    WriteJson(ctx, 400, "{\"error\":\"ts no parseable: " + Escape(tsStr) + "\"}");
                    return;
                }
            }

            var payload = RelativeIndicatorRegistry.QueryAt(key, asOf);
            if (payload == null)
            {
                WriteJson(ctx, 404, "{\"error\":\"no query handler registrado para key: " + Escape(key) + "\",\"available_keys\":" + JsonStringList(RelativeIndicatorRegistry.QueryHandlerKeys()) + "}");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"key\":").Append(Quote(key));
            sb.Append(",\"as_of\":").Append(Quote(FormatTime(asOf)));
            sb.Append(",\"payload\":");
            AppendPayload(sb, payload);
            sb.Append("}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static string JsonStringList(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var s in items)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append(Quote(s));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static void AppendPayload(StringBuilder sb, IDictionary<string, object> payload)
        {
            sb.Append("{");
            if (payload != null)
            {
                bool first = true;
                foreach (var kv in payload)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Quote(kv.Key)).Append(":").Append(FormatValue(kv.Value));
                }
            }
            sb.Append("}");
        }

        private static string FormatValue(object v)
        {
            if (v == null) return "null";
            if (v is bool b) return b ? "true" : "false";
            if (v is double d) return FormatDouble(d);
            if (v is float f) return FormatDouble(f);
            if (v is decimal dec) return ((double)dec).ToString("G17", CultureInfo.InvariantCulture);
            if (v is sbyte || v is byte || v is short || v is ushort
                || v is int || v is uint || v is long || v is ulong)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (v is DateTime dt) return Quote(FormatTime(dt));
            if (v is string s) return Quote(s);
            // Recursive: dicts → JSON object, IEnumerable (no string) → JSON array.
            // Permite payloads anidados (composites con sus VAH/VAL/POC, listas de pVAs, etc).
            if (v is IDictionary<string, object> dictSO)
            {
                var sb = new StringBuilder();
                AppendPayload(sb, dictSO);
                return sb.ToString();
            }
            if (v is System.Collections.IDictionary dictGen)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                bool first = true;
                foreach (System.Collections.DictionaryEntry kv in dictGen)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Quote(Convert.ToString(kv.Key, CultureInfo.InvariantCulture)));
                    sb.Append(":").Append(FormatValue(kv.Value));
                }
                sb.Append("}");
                return sb.ToString();
            }
            if (v is System.Collections.IEnumerable enumerable)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(FormatValue(item));
                }
                sb.Append("]");
                return sb.ToString();
            }
            return Quote(v.ToString());
        }

        #endregion

        #region Endpoint: /print-output

        private void HandlePrintOutput(HttpListenerContext ctx)
        {
            int n = ParseIntQuery(ctx.Request.QueryString, "n", 200);
            if (n <= 0) n = 200;
            string indicatorFilter = ctx.Request.QueryString["indicator"];
            string instrumentFilter = ctx.Request.QueryString["instrument"];
            int minLevel = ParseIntQuery(ctx.Request.QueryString, "level_min", 1);
            int sinceMinutes = ParseIntQuery(ctx.Request.QueryString, "since_minutes", 0);
            DateTime? cutoff = sinceMinutes > 0 ? (DateTime?)DateTime.UtcNow.AddMinutes(-sinceMinutes) : null;

            // Oversample para poder filtrar sin quedarnos cortos
            int fetch = n * 5;
            if (fetch > RelativeLog.BufferSize) fetch = RelativeLog.BufferSize;
            var entries = RelativeLog.Snapshot(fetch);

            var filtered = new List<RelativeLogEntry>(Math.Min(entries.Length, n));
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                if ((int)e.Level < minLevel) continue;
                if (cutoff.HasValue && e.Timestamp < cutoff.Value) continue;
                if (!string.IsNullOrEmpty(indicatorFilter) &&
                    !string.Equals(e.Indicator, indicatorFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(instrumentFilter) &&
                    !string.Equals(e.Instrument, instrumentFilter, StringComparison.OrdinalIgnoreCase)) continue;
                filtered.Add(e);
            }

            // Si filtramos demasiado, tomamos últimos N
            int startIdx = Math.Max(0, filtered.Count - n);
            var sb = new StringBuilder();
            sb.Append("{\"total_count\":").Append(RelativeLog.TotalCount);
            sb.Append(",\"buffer_size\":").Append(RelativeLog.BufferSize);
            sb.Append(",\"returned\":").Append(filtered.Count - startIdx);
            sb.Append(",\"entries\":[");
            for (int i = startIdx; i < filtered.Count; i++)
            {
                if (i > startIdx) sb.Append(",");
                var e = filtered[i];
                sb.Append("{\"t\":").Append(Quote(FormatTime(e.Timestamp)));
                sb.Append(",\"level\":").Append(Quote(e.Level.ToString()));
                sb.Append(",\"indicator\":").Append(Quote(e.Indicator ?? ""));
                sb.Append(",\"instrument\":").Append(Quote(e.Instrument ?? ""));
                sb.Append(",\"period\":").Append(Quote(e.Period ?? ""));
                sb.Append(",\"bar_time\":").Append(Quote(FormatTime(e.BarTime)));
                sb.Append(",\"bar\":").Append(e.CurrentBar);
                sb.Append(",\"msg\":").Append(Quote(e.Message ?? ""));
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private void HandlePrintOutputClear(HttpListenerContext ctx)
        {
            RelativeLog.Clear();
            WriteJson(ctx, 200, "{\"cleared\":true}");
        }

        #endregion

        #region Endpoint: /charts

        private void HandleCharts(HttpListenerContext ctx)
        {
            var results = new List<Dictionary<string, string>>();
            bool includeAll = string.Equals(
                ctx.Request.QueryString["all"], "true", StringComparison.OrdinalIgnoreCase);

            // NT8 mantiene sus ventanas en NinjaTrader.Core.Globals.AllWindows
            // (no en System.Windows.Application.Current.Windows). Es miembro no
            // documentado públicamente — lo accedemos por reflection para robustez.
            System.Collections.IEnumerable allWindows = null;
            try
            {
                var globalsType = Type.GetType("NinjaTrader.Core.Globals, NinjaTrader.Core");
                if (globalsType != null)
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    var prop = globalsType.GetProperty("AllWindows", flags);
                    if (prop != null)
                        allWindows = prop.GetValue(null, null) as System.Collections.IEnumerable;
                    if (allWindows == null)
                    {
                        var field = globalsType.GetField("AllWindows", flags);
                        if (field != null)
                            allWindows = field.GetValue(null) as System.Collections.IEnumerable;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteJson(ctx, 500, "{\"error\":" + Quote("reflection AllWindows: " + ex.Message) + "}");
                return;
            }

            if (allWindows == null)
            {
                WriteJson(ctx, 500, "{\"error\":\"NinjaTrader.Core.Globals.AllWindows no accesible\"}");
                return;
            }

            // Snapshot de la colección antes de enumerar (evita mutación concurrente)
            var windowList = new List<object>();
            try { foreach (var w in allWindows) if (w != null) windowList.Add(w); }
            catch (Exception ex)
            {
                WriteJson(ctx, 500, "{\"error\":" + Quote("enumerate AllWindows: " + ex.Message) + "}");
                return;
            }

            foreach (var win in windowList)
            {
                string typeName = win.GetType().Name;
                string fullTypeName = win.GetType().FullName ?? typeName;

                bool looksLikeChart =
                    typeName.Equals("Chart", StringComparison.OrdinalIgnoreCase)
                    || typeName.IndexOf("Chart", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("SuperDom", StringComparison.OrdinalIgnoreCase) >= 0
                    || fullTypeName.IndexOf("NinjaTrader.Gui.Chart", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!includeAll && !looksLikeChart) continue;

                var info = new Dictionary<string, string>
                {
                    ["type"] = typeName,
                    ["full_type"] = fullTypeName,
                    ["looks_like_chart"] = looksLikeChart.ToString(),
                };

                // Cada NTWindow tiene su propio Dispatcher — debemos marshalear ahí
                // para acceder a propiedades visuales (Title, ActiveChartControl, etc.)
                var disp = GetPropertyValue(win, "Dispatcher") as System.Windows.Threading.Dispatcher;
                Action extract = () =>
                {
                    info["title"] = SafeString(() =>
                    {
                        var t = GetPropertyValue(win, "Title");
                        return t == null ? "" : t.ToString();
                    });
                    var visible = GetPropertyValue(win, "IsVisible");
                    if (visible != null) info["is_visible"] = visible.ToString();
                    var active = GetPropertyValue(win, "IsActive");
                    if (active != null) info["is_active"] = active.ToString();
                    TryExtractChartInfo(win, info);
                };

                try
                {
                    if (disp != null && !disp.HasShutdownStarted)
                        disp.Invoke(extract);
                    else
                        extract();
                }
                catch (Exception ex)
                {
                    info["dispatcher_error"] = ex.Message;
                }

                results.Add(info);
            }

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(results.Count).Append(",\"charts\":[");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{");
                bool first = true;
                foreach (var kv in results[i])
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Quote(kv.Key)).Append(":").Append(Quote(kv.Value ?? ""));
                }
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static void TryExtractChartInfo(object win, Dictionary<string, string> info)
        {
            // Intentamos acceder a propiedades comunes via reflection — evita acoplarnos a la API interna.
            object cc = GetPropertyValue(win, "ActiveChartControl");
            if (cc == null) cc = GetPropertyValue(win, "ChartControl");
            if (cc == null) return;

            var instr = GetPropertyValue(cc, "Instrument");
            if (instr != null)
                info["instrument"] = SafeString(() => (GetPropertyValue(instr, "FullName") ?? "").ToString());

            var period = GetPropertyValue(cc, "BarsPeriod");
            if (period != null)
            {
                info["bars_period"] = SafeString(() => period.ToString());
                var periodType = GetPropertyValue(period, "BarsPeriodType");
                var periodValue = GetPropertyValue(period, "Value");
                if (periodType != null) info["period_type"] = periodType.ToString();
                if (periodValue != null) info["period_value"] = periodValue.ToString();
            }

            // Lista de indicadores cargados (nombres de clase)
            var indicators = GetPropertyValue(cc, "Indicators");
            if (indicators is System.Collections.IEnumerable enumerable)
            {
                var names = new List<string>();
                foreach (var ind in enumerable)
                {
                    if (ind != null)
                        names.Add(ind.GetType().Name);
                }
                info["indicators"] = string.Join(",", names);
                info["indicator_count"] = names.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static object GetPropertyValue(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var prop = obj.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                return prop != null ? prop.GetValue(obj, null) : null;
            }
            catch { return null; }
        }

        #endregion

        #region HTTP helpers

        private static void WriteJson(HttpListenerContext ctx, int status, string json)
        {
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        private static int ParseIntQuery(System.Collections.Specialized.NameValueCollection q, string key, int dflt)
        {
            string raw = q[key];
            if (string.IsNullOrEmpty(raw)) return dflt;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            return dflt;
        }

        private static string Quote(string s)
        {
            if (s == null) return "null";
            return "\"" + Escape(s) + "\"";
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 4);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string FormatDouble(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return d.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string FormatTime(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            return t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static void Log(string msg)
        {
            try
            {
                NinjaTrader.Code.Output.Process(
                    "[RelativeObserver " + DateTime.Now.ToString("HH:mm:ss") + "] " + msg,
                    PrintTo.OutputTab1);
            }
            catch { }
        }

        #endregion
    }

    #region Support types

    internal struct TickRecord
    {
        public DateTime Time;
        public string Type; // "Last" | "Bid" | "Ask"
        public double Price;
        public long Volume;
    }

    internal struct QuoteSnapshot
    {
        public double Last;
        public double Bid;
        public double Ask;
        public long LastVolume;
        public long DayVolume;
        public DateTime LastTime;
        public long TickCount;
        public long DedupedCount;
    }

    internal class InstrumentSubscription : IDisposable
    {
        public readonly Instrument Instrument;
        public readonly string FullName;
        public readonly object DataLock = new object();

        private readonly TickRecord[] _ring;
        private int _head;          // próximo índice a escribir
        private int _filled;        // cuántos slots usados (<= _ring.Length)

        // Dedupe: guardamos el último tick insertado para descartar duplicados exactos
        // causados por múltiples subscribers del mismo feed (ej. chart abierto + AddOn).
        private TickRecord _lastStored;
        private bool _hasLastStored;
        private long _dedupedCount;

        private double _last = double.NaN;
        private double _bid = double.NaN;
        private double _ask = double.NaN;
        private long _lastVolume;
        private long _dayVolume;
        private DateTime _lastTime;
        private long _tickCount;

        private bool _attached;
        private bool _disposed;

        public InstrumentSubscription(Instrument instrument, int ringSize)
        {
            Instrument = instrument;
            FullName = instrument.FullName;
            _ring = new TickRecord[ringSize];
        }

        public void Start()
        {
            if (_attached) return;
            // En NT8, Instrument.MarketData es una propiedad (no evento) que devuelve
            // el MarketData holder; el evento real es MarketData.Update.
            // Ref: https://ninjatrader.com/support/helpguides/nt8/marketdata.htm
            if (Instrument.Dispatcher != null && !Instrument.Dispatcher.HasShutdownStarted)
                Instrument.Dispatcher.InvokeAsync(() => Instrument.MarketData.Update += OnMarketData);
            else
                Instrument.MarketData.Update += OnMarketData;
            _attached = true;
        }

        private void OnMarketData(object sender, MarketDataEventArgs e)
        {
            if (_disposed) return;
            string type;
            switch (e.MarketDataType)
            {
                case MarketDataType.Last: type = "Last"; break;
                case MarketDataType.Bid:  type = "Bid";  break;
                case MarketDataType.Ask:  type = "Ask";  break;
                default: return; // ignorar DailyHigh/Low/Settlement/etc. por ahora
            }

            var rec = new TickRecord
            {
                Time = e.Time,
                Type = type,
                Price = e.Price,
                Volume = e.Volume,
            };

            lock (DataLock)
            {
                // Dedupe: descartar duplicado exacto del anterior.
                if (_hasLastStored
                    && _lastStored.Time == rec.Time
                    && _lastStored.Type == rec.Type
                    && _lastStored.Price == rec.Price
                    && _lastStored.Volume == rec.Volume)
                {
                    _dedupedCount++;
                    return;
                }
                _lastStored = rec;
                _hasLastStored = true;

                _ring[_head] = rec;
                _head = (_head + 1) % _ring.Length;
                if (_filled < _ring.Length) _filled++;
                _tickCount++;
                _lastTime = e.Time;

                if (e.MarketDataType == MarketDataType.Last)
                {
                    _last = e.Price;
                    _lastVolume = e.Volume;
                    _dayVolume += e.Volume; // NOTA: aproximado desde arranque; no day cumulative real
                }
                else if (e.MarketDataType == MarketDataType.Bid) _bid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask) _ask = e.Price;
            }
        }

        /// <summary>Snapshot actual de la cotización (caller debe tener DataLock).</summary>
        public QuoteSnapshot GetQuoteSnapshot()
        {
            return new QuoteSnapshot
            {
                Last = _last,
                Bid = _bid,
                Ask = _ask,
                LastVolume = _lastVolume,
                DayVolume = _dayVolume,
                LastTime = _lastTime,
                TickCount = _tickCount,
                DedupedCount = _dedupedCount,
            };
        }

        /// <summary>Últimos N ticks, del más viejo al más nuevo (caller debe tener DataLock).</summary>
        public TickRecord[] GetLastTicks(int n)
        {
            int count = Math.Min(n, _filled);
            if (count == 0) return new TickRecord[0];
            var result = new TickRecord[count];
            // Copiamos en orden cronológico: empezamos en _head - _filled (módulo)
            int start = (_head - _filled + _ring.Length) % _ring.Length;
            // Pero queremos solo los últimos N, así que ajustamos start
            int startLast = (_head - count + _ring.Length) % _ring.Length;
            for (int i = 0; i < count; i++)
                result[i] = _ring[(startLast + i) % _ring.Length];
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_attached)
            {
                try { Instrument.MarketData.Update -= OnMarketData; } catch { }
                _attached = false;
            }
        }
    }

    #endregion
}
