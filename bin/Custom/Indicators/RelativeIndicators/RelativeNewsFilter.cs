#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Http;
using System.IO;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators; // For enum visibility in generated code
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — RLog + Registry
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public enum NewsImpactType
    {
        HighOnly,
        HighAndMedium,
        All
    }

	public class RelativeNewsFilter : Indicator
	{
		private const string FeedUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.xml";
		private List<NewsEvent> _events = new List<NewsEvent>();
		private bool _downloaded = false;
        private bool _needsDrawing = false;
		private object _lock = new object();

		[XmlIgnore]
		public bool IsNewsImminent { get; private set; }
		
		[XmlIgnore]
		public string NextNewsTitle { get; private set; }

        [XmlIgnore]
		public double MinutesToNews { get; private set; }

        [Display(Name = "Show Historical News", GroupName = "Visual", Order = 10)]
        public bool ShowHistoricalNews { get; set; }

        [Display(Name = "Debug: Simulate Event", GroupName = "Visual", Order = 11)]
        public bool DebugSimulateEvent { get; set; }



		public class NewsEvent
		{
			public string Title { get; set; }
			public string Country { get; set; }
			public DateTime Time { get; set; } // Local NinjaTrader Time
			public string Impact { get; set; }
		}

        private DateTime _selectedEventTime = DateTime.MinValue;  // Track currently selected event
        private HashSet<string> _sentEmailKeys = new HashSet<string>();  // Track sent emails to avoid duplicates
        private System.Windows.Threading.DispatcherTimer _timer;

        // Direct2D Resources
        private SharpDX.Direct2D1.Brush dxZoneBrush;
        private SharpDX.Direct2D1.Brush dxUserLineBrush;
        private SharpDX.Direct2D1.Brush dxTextBrush;
        private SharpDX.Direct2D1.Brush dxPanelBgBrush;
        private SharpDX.DirectWrite.TextFormat dxTextFormat;
        private SharpDX.DirectWrite.TextFormat dxPanelFormat;
        private SharpDX.DirectWrite.TextFormat dxPanelTitleFormat;

        public override void OnRenderTargetChanged()
        {
            // Subscribe to mouse events
            try
            {
                if (ChartControl != null)
                {
                    ChartControl.MouseLeftButtonDown -= OnMouseDown;
                    ChartControl.MouseLeftButtonDown += OnMouseDown;
                }
            }
            catch {}

            // Cleanup old D2D resources
            DisposeD2DResources();

            base.OnRenderTargetChanged();
        }

        private void DisposeD2DResources()
        {
            if (dxZoneBrush != null) { dxZoneBrush.Dispose(); dxZoneBrush = null; }
            if (dxUserLineBrush != null) { dxUserLineBrush.Dispose(); dxUserLineBrush = null; }
            if (dxTextBrush != null) { dxTextBrush.Dispose(); dxTextBrush = null; }
            if (dxPanelBgBrush != null) { dxPanelBgBrush.Dispose(); dxPanelBgBrush = null; }
            if (dxTextFormat != null) { dxTextFormat.Dispose(); dxTextFormat = null; }
            if (dxPanelFormat != null) { dxPanelFormat.Dispose(); dxPanelFormat = null; }
            if (dxPanelTitleFormat != null) { dxPanelTitleFormat.Dispose(); dxPanelTitleFormat = null; }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (_events == null || _events.Count == 0) return;
            if (Bars == null || chartControl == null) return;

            // Use the RenderTarget property from the base Indicator class
            // But we need to verify if it is valid.
            // SharpDX.Direct2D1.RenderTarget renderTarget = RenderTarget; 
            // NOTE: RenderTarget property is available in Indicator.

            // 1. Create Resources if needed
            try 
            {
                CreateD2DResources(RenderTarget);
            }
            catch { return; }

            // 2. Loop and Draw
            // Lock to ensure thread safety with timer updates
            lock (_lock)
            {
                // Calculate Bounds based on the specific ChartPanel (The price panel)
                // This ensures we draw inside the visible chart area and not at the bottom of the whole window
                float panelTop = ChartPanel.Y;
                float panelH = ChartPanel.H;
                float panelBottom = panelTop + panelH;
                float panelWidth = ChartPanel.W;

                // Stack tracker for label vertical positioning
                Dictionary<int, int> xStack = new Dictionary<int, int>();
                
                foreach (var ev in _events)
                {
                    // Calculate Time Window
                    DateTime start = ev.Time.AddMinutes(-PauseBeforeMinutes);
                    DateTime end = ev.Time.AddMinutes(PauseAfterMinutes);

                    // Convert to X Coordinates
                    // GetXByTime is efficient
                    int startX = chartControl.GetXByTime(start);
                    int endX = chartControl.GetXByTime(end);
                    int eventX = chartControl.GetXByTime(ev.Time);

                    // Performance: Skip if off-screen (with some buffer)
                    if (endX < 0 || startX > panelWidth) continue;

                    // A. Draw Zone (Rectangle)
                    if (ShowZones)
                    {
                        float width = Math.Max(1, endX - startX);
                        // Draw from Top to Bottom of the PANEL
                        SharpDX.RectangleF rect = new SharpDX.RectangleF(startX, panelTop, width, panelH);
                        RenderTarget.FillRectangle(rect, dxZoneBrush);
                    }

                    // B. Draw Event Marker (Diamond)
                    if (ShowLines)
                    {
                        // Calculate Y position at "Bottom" of the PANEL (just above the axis line)
                        float bottomY = panelBottom - 15f; 
                        float halfSize = 6f;

                        // Diamond Geometry
                        // Top: (X, Y-12), Right: (X+6, Y-6), Bottom: (X, Y), Left: (X-6, Y-6)
                        var p1 = new Vector2(eventX, bottomY - halfSize * 2); // Top
                        var p2 = new Vector2(eventX + halfSize, bottomY - halfSize); // Right
                        var p3 = new Vector2(eventX, bottomY); // Bottom
                        var p4 = new Vector2(eventX - halfSize, bottomY - halfSize); // Left

                        RenderTarget.FillGeometry(CreateDiamondGeometry(RenderTarget.Factory, p1, p2, p3, p4), dxUserLineBrush);
                    }

                    // C. Draw Text & Selection
                    bool isSelected = ev.Time == _selectedEventTime;
                    bool isImminent = (IsNewsImminent && ev.Title == NextNewsTitle);

                    if (isSelected || isImminent)
                    {
                        // Stack Logic: Get current stack height for this X coordinate
                        int stackIndex = 0;
                        if (xStack.ContainsKey(eventX))
                        {
                            stackIndex = xStack[eventX];
                            xStack[eventX]++;
                        }
                        else
                        {
                            xStack[eventX] = 1;
                        }

                        // 1. Draw Text Just Above the Diamond/Marker (Stacked)
                        // Base Y = panelBottom - 45f
                        // Stack Offset = stackIndex * 20f
                        
                        float textY = panelBottom - 45f - (stackIndex * 20f);
                        
                        // Center text on the eventX
                        RenderTarget.DrawText(ev.Title, dxTextFormat, new SharpDX.RectangleF(eventX - 100, textY, 200, 20), dxTextBrush);

                        // 2. Info Panel REMOVED as per user request
                        // if (isSelected) { DrawInfoPanel(...) } 
                    }
                }
            }
        }

        private void CreateD2DResources(SharpDX.Direct2D1.RenderTarget renderTarget)
        {
            if (dxZoneBrush == null)
            {
                SharpDX.Color zoneColor = ToSharpDX(LineColor);
                // Apply Opacity: Convert 0-100 int to 0.0-1.0 float * Alpha
                float alpha = (ZoneOpacity / 100f); 
                zoneColor.A = (byte)(255 * alpha);
                dxZoneBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, zoneColor);
            }

            if (dxUserLineBrush == null)
            {
                dxUserLineBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, ToSharpDX(LineColor));
            }

            if (dxTextBrush == null)
            {
                dxTextBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, ToSharpDX(TextColor));
            }

            if (dxPanelBgBrush == null)
            {
                // Black, 80% Opacity
                dxPanelBgBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color(0, 0, 0, 200)); 
            }

            if (dxTextFormat == null)
            {
                dxTextFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12f);
                dxTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                dxTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            }

            if (dxPanelFormat == null)
            {
                dxPanelFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 12f);
                dxPanelFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
            }
             if (dxPanelTitleFormat == null)
            {
                dxPanelTitleFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 13f);
                dxPanelTitleFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
            }
        }

        private SharpDX.Direct2D1.PathGeometry CreateDiamondGeometry(SharpDX.Direct2D1.Factory factory, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
             SharpDX.Direct2D1.PathGeometry geometry = new SharpDX.Direct2D1.PathGeometry(factory);
             using (GeometrySink sink = geometry.Open())
             {
                 sink.BeginFigure(p1, FigureBegin.Filled);
                 sink.AddLine(p2);
                 sink.AddLine(p3);
                 sink.AddLine(p4);
                 sink.EndFigure(FigureEnd.Closed);
                 sink.Close();
             }
             return geometry;
        }

        private void DrawInfoPanel(SharpDX.Direct2D1.RenderTarget renderTarget, NewsEvent ev)
        {
            // Fixed Position: Bottom Left
            float padding = 10f;
            float panelW = 250f;
            float panelH = 100f;
            float x = 20f;
            float y = renderTarget.Size.Height - panelH - 40f; // Above scrollbar area

            // Background
            renderTarget.FillRectangle(new SharpDX.RectangleF(x, y, panelW, panelH), dxPanelBgBrush);
            
            // Text
            float textX = x + padding;
            float textY = y + padding;
            
            renderTarget.DrawText("SELECTED NEWS", dxPanelTitleFormat, new SharpDX.RectangleF(textX, textY, panelW, 20), dxTextBrush);
            textY += 25;
            
            string info = $"Title: {ev.Title}\nTime: {ev.Time:HH:mm}\nImpact: {ev.Impact}\nCountry: {ev.Country}";
            renderTarget.DrawText(info, dxPanelFormat, new SharpDX.RectangleF(textX, textY, panelW, 80), dxTextBrush);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_events == null || ChartControl == null) return;
            
            // Hit Test Logic
            System.Windows.Point mousePt = e.GetPosition(ChartControl as IInputElement);
            double mouseX = mousePt.X;
            DateTime clickedTime = ChartControl.GetTimeByX((int)mouseX);

            NewsEvent hit = null;
            lock (_lock)
            {
                foreach (var ev in _events)
                {
                    DateTime start = ev.Time.AddMinutes(-PauseBeforeMinutes);
                    DateTime end = ev.Time.AddMinutes(PauseAfterMinutes);
                    if (clickedTime >= start && clickedTime <= end)
                    {
                        hit = ev;
                        break;
                    }
                }
            }

            if (hit != null)
            {
                if (_selectedEventTime == hit.Time) 
                    _selectedEventTime = DateTime.MinValue;
                else 
                    _selectedEventTime = hit.Time;
                
                // Force redraw instantly
                ChartControl.InvalidateVisual();
            }
        }

        // Helper: WPF Brush -> SharpDX Color
        private SharpDX.Color ToSharpDX(System.Windows.Media.Brush wpfBrush)
        {
            if (wpfBrush is System.Windows.Media.SolidColorBrush scb)
            {
                return new SharpDX.Color(scb.Color.R, scb.Color.G, scb.Color.B, scb.Color.A);
            }
            return SharpDX.Color.White;
        }

        protected override void OnStateChange()
        {
            if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.MouseLeftButtonDown -= OnMouseDown;
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }
            }
            
            if (State == State.SetDefaults)
            {
                Name = "RelativeNewsFilter";
                Description = "Displays Economic News Calendar events on the chart.";
                
                // Defaults
                PauseBeforeMinutes = 5;
                PauseAfterMinutes = 10;
                FilterImpact = NewsImpactType.HighOnly;
                AutoSyncTimeZone = false; // Default to Local (PC) Time
                TimeOffset = 0;
                CustomCurrencies = ""; // Empty = Auto
                ShowZones = true;
                ShowLines = true;
                ShowHistoricalNews = true; // FORCE ON for visibility
                ZoneOpacity = 70; // Default stronger opacity
                LineColor = Brushes.Red; // Uses for Region Area
                TextColor = Brushes.White;
                IsOverlay = true;       // Force Overlay on Main Chart
                IsAutoScale = false;    // Don't auto-scale news lines
                
                // Email defaults
                EnableEmailAlerts = false;
                EmailAlertMinutes = 15;
                SmtpServer = "smtp.gmail.com";
                SmtpPort = 587;
                EmailFrom = "";
                EmailTo = "";
                EmailPassword = "";
                
                DebugSimulateEvent = true; // FORCE ON for visibility
            }
            else if (State == State.Configure)
            {
                IsNewsImminent = false;
                MinutesToNews = 999;
            }
            else if (State == State.DataLoaded)
            {
                // Clean old cache files first
                CleanOldCacheFiles();
                
                if (DebugSimulateEvent)
                {
                    // Add a fake event 30 mins ago and one in 30 mins
                    _events.Add(new NewsEvent { 
                        Title = "TEST PRIOR EVENT", 
                        Country = "USD", 
                        Impact = "High", 
                        Time = DateTime.Now.AddMinutes(-30) 
                    });
                    
                     _events.Add(new NewsEvent { 
                        Title = "TEST FUTURE EVENT", 
                        Country = "USD", 
                        Impact = "High", 
                        Time = DateTime.Now.AddMinutes(30) 
                    });
                    
                    _needsDrawing = true;
                    Print("RelativeNewsFilter: Added Debug Fake Events");
                }

                if (!_downloaded)
                {
                    // v2: _downloaded se setea tras download exitoso (en DownloadAndParseParams)
                    // Si falla, permite retry en la próxima pasada del timer
                    Task.Run(async () => await DownloadAndParseParams());
                }

                // Start Timer for independent updates (1 second)
                // Ensure we use the main UI Dispatcher
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_timer == null)
                    {
                        _timer = new System.Windows.Threading.DispatcherTimer();
                        _timer.Interval = new TimeSpan(0, 0, 1);
                        _timer.Tick += OnTimerTick;
                        _timer.Start();
                    }
                });
            }
            base.OnStateChange();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                CalculateNewsState();
                if (ChartControl != null) ChartControl.InvalidateVisual(); // Instant Redraw
            }
            catch (Exception ex)
            {
                Print("RelativeNewsFilter Timer Error: " + ex.ToString());
            }
        }
        
        protected override void OnBarUpdate()
        {
            // OnBarUpdate is NOT used for drawing in Direct2D

            // --- RelativeMCP observability ---
            // Publica próximo evento + ventana de impacto — gatekeeper de volatilidad
            // NADRO 5.0 Self-Prep (The Work): si hay evento de alto impacto en X min,
            // el Risk Manager decide si operar o no.
            if (CurrentBar >= 0 && BarsInProgress == 0)
            {
                try
                {
                    DateTime now = Time[0];
                    NewsEvent nextEvent = null;
                    NewsEvent nextHighImpact = null;
                    int events_next_hour = 0, events_next_24h = 0;

                    if (_events != null)
                    {
                        foreach (var ev in _events)
                        {
                            if (ev.Time < now) continue;
                            double minutes = (ev.Time - now).TotalMinutes;
                            if (minutes <= 60) events_next_hour++;
                            if (minutes <= 1440) events_next_24h++;
                            if (nextEvent == null || ev.Time < nextEvent.Time)
                                nextEvent = ev;
                            if ((ev.Impact == "High" || ev.Impact == "high") &&
                                (nextHighImpact == null || ev.Time < nextHighImpact.Time))
                                nextHighImpact = ev;
                        }
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["bar"] = CurrentBar,
                        ["bar_time"] = now,
                        ["close"] = Close[0],
                        ["events_total"] = _events != null ? _events.Count : 0,
                        ["events_next_hour"] = events_next_hour,
                        ["events_next_24h"] = events_next_24h,
                    };
                    if (nextEvent != null)
                    {
                        payload["next_event_title"] = nextEvent.Title ?? "";
                        payload["next_event_country"] = nextEvent.Country ?? "";
                        payload["next_event_impact"] = nextEvent.Impact ?? "";
                        payload["next_event_time"] = nextEvent.Time;
                        payload["next_event_minutes"] = (nextEvent.Time - now).TotalMinutes;
                    }
                    if (nextHighImpact != null)
                    {
                        payload["next_high_impact_title"] = nextHighImpact.Title ?? "";
                        payload["next_high_impact_country"] = nextHighImpact.Country ?? "";
                        payload["next_high_impact_time"] = nextHighImpact.Time;
                        payload["next_high_impact_minutes"] = (nextHighImpact.Time - now).TotalMinutes;
                        payload["high_impact_within_30min"] =
                            (nextHighImpact.Time - now).TotalMinutes <= 30;
                    }

                    RelativeIndicatorRegistry.Publish(
                        string.Format("{0}:{1}:{2}{3}", typeof(RelativeNewsFilter).Name,
                            Instrument.FullName, BarsPeriod.Value, BarsPeriod.BarsPeriodType),
                        payload);

                    if (IsFirstTickOfBar && State == State.Realtime && nextEvent != null)
                        this.RLog("bar={0} next='{1}' ({2}) impact={3} in {4:F1}min | total_24h={5}",
                            CurrentBar, nextEvent.Title, nextEvent.Country,
                            nextEvent.Impact, (nextEvent.Time - now).TotalMinutes, events_next_24h);
                }
                catch { }
            }
            // --- end RelativeMCP ---
        }

        private void CalculateNewsState()
        {
             if (_events == null) return;
             
             DateTime currentBarTime = DateTime.Now; 
             bool imminent = false;
             string title = "";
             double minDiff = 999;
             
             lock (_lock)
             {
                 foreach (var ev in _events)
                 {
                     TimeSpan diff = ev.Time - currentBarTime;
                     double totalMinutes = diff.TotalMinutes; 
                     
                     if (totalMinutes <= PauseBeforeMinutes && totalMinutes >= -PauseAfterMinutes)
                     {
                         imminent = true;
                         title = ev.Title;
                         if (Math.Abs(totalMinutes) < Math.Abs(minDiff)) minDiff = totalMinutes;
                     }
                 }
             }

             IsNewsImminent = imminent;
             NextNewsTitle = title;
             MinutesToNews = minDiff;
        }



        // Helper to parse individual XML content
        private List<NewsEvent> ParseNewsXml(string xmlContent, string[] targets)
        {
            List<NewsEvent> parsed = new List<NewsEvent>();
            try 
            {
                if (string.IsNullOrWhiteSpace(xmlContent)) return parsed;
                XDocument doc = XDocument.Parse(xmlContent);
                
                foreach (var ev in doc.Descendants("event"))
                {
                    string impact = ev.Element("impact")?.Value;
                    string country = ev.Element("country")?.Value;
                    
                    if (!IsImpactRelevant(impact)) continue;
                    if (!targets.Contains(country)) continue;
                    
                    string dateStr = ev.Element("date")?.Value;
                    string timeStr = ev.Element("time")?.Value;
                    
                    if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(timeStr)) continue;
                    
                    DateTime eventTimeEst;
                    string combined = dateStr + " " + timeStr;
                    
                    if (DateTime.TryParseExact(combined, "MM-dd-yyyy h:mmtt", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out eventTimeEst))
                    {
                         // Fix: XML feed is in UTC, not EST. (13:30 PPI example confirmed)
                         TimeZoneInfo sourceZone = TimeZoneInfo.Utc;
                         
                         // Determine Target TimeZone
                         TimeZoneInfo targetZone = TimeZoneInfo.Local;
                         
                         // Auto Sync Logic: Calculate Offset between Chart TZ and Local TZ
                         if (AutoSyncTimeZone && Bars != null && Bars.TradingHours != null)
                         {
                             // Fix CS0029: TradingHours.TimeZone returns string ID in some versions
                             string tzId = Bars.TradingHours.TimeZone.ToString();
                             try { 
                                 targetZone = TimeZoneInfo.FindSystemTimeZoneById(tzId); 
                             }
                             catch { targetZone = TimeZoneInfo.Local; }
                         }
                         
                         // 1. Convert timestamp to Local Time (Base)
                         DateTime convertedTime = TimeZoneInfo.ConvertTime(eventTimeEst, sourceZone, TimeZoneInfo.Local);

                         // 2. If AutoSync is on, apply the difference between Target (Unique Chart TZ) and Local
                         if (AutoSyncTimeZone && !targetZone.Equals(TimeZoneInfo.Local))
                         {
                             // Use GetUtcOffset(Now) to account for DST, not just Base Offset
                             TimeSpan targetOffset = targetZone.GetUtcOffset(DateTime.Now);
                             TimeSpan localOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
                             
                             TimeSpan diff = targetOffset - localOffset;
                             convertedTime = convertedTime.Add(diff);
                         }

                         // 3. Apply Manual Offset
                         if (TimeOffset != 0)
                             convertedTime = convertedTime.AddHours(TimeOffset);
                             
                         // DEBUG TIMEZONE
                         if (CurrentBar < 5) // Print once/few times
                             Print(string.Format("RelativeNewsFilter TIME DEBUG: Event='{0}' UTC='{1}' TargetZone='{2}' LocalZone='{5}' Converted='{3}' Offset={4}", 
                                 ev.Element("title")?.Value, eventTimeEst, targetZone.Id, convertedTime, TimeOffset, TimeZoneInfo.Local.DisplayName));
                         
                         parsed.Add(new NewsEvent 
                         {
                             Title = ev.Element("title")?.Value,
                             Country = country,
                             Impact = impact,
                             Time = convertedTime
                         });
                    }
                }
            }
            catch (Exception ex) { Print("RelativeNewsFilter XML Parse Warning: " + ex.Message); }
            return parsed;
        }

        private async Task DownloadAndParseParams()
        {
            try 
            {
                // Ensure TLS 1.2 is used (critical for many https feeds)
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

                // Cache System
                string dateKey = DateTime.Now.ToString("yyyyMMdd");
                string cacheDir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NewsCache");
                if (!System.IO.Directory.Exists(cacheDir)) System.IO.Directory.CreateDirectory(cacheDir);
                
                string todayCachePath = System.IO.Path.Combine(cacheDir, "NewsCache_" + dateKey + ".xml");
                List<NewsEvent> allEvents = new List<NewsEvent>();
                string[] targets = GetTargetCurrencies();
                
                // Track Uniqueness to avoid duplicates from overlapping cache files
                HashSet<string> seenEvents = new HashSet<string>();

                // 1. Identify Files to Load
                List<string> filesToLoad = new List<string>();
                
                // v2: Always try to execute "Today's" logic (Download or Load)
                // Download con retry + user-agent (algunos CDN bloquean peticiones sin UA)
                bool downloadOk = System.IO.File.Exists(todayCachePath);
                if (!downloadOk)
                {
                    const int maxRetries = 3;
                    for (int attempt = 1; attempt <= maxRetries && !downloadOk; attempt++)
                    {
                        try
                        {
                            using (var client = new HttpClient())
                            {
                                client.Timeout = TimeSpan.FromSeconds(15);
                                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                                    "Mozilla/5.0 (NinjaTrader RelativeNewsFilter)");
                                string webData = await client.GetStringAsync(FeedUrl);
                                if (string.IsNullOrWhiteSpace(webData) || webData.Length < 100)
                                    throw new Exception("respuesta vacía o demasiado corta");
                                System.IO.File.WriteAllText(todayCachePath, webData);
                                downloadOk = true;
                                Print(string.Format(
                                    "RelativeNewsFilter: Downloaded & Cached Today's Data ({0} bytes, attempt {1}).",
                                    webData.Length, attempt));
                            }
                        }
                        catch (Exception ex)
                        {
                            Print(string.Format(
                                "RelativeNewsFilter Download Failed (attempt {0}/{1}): {2}",
                                attempt, maxRetries, ex.Message));
                            if (attempt < maxRetries)
                                await Task.Delay(attempt * 2000); // backoff 2s, 4s, 6s
                        }
                    }
                }
                // Marcar como descargado SOLO si tenemos archivo de hoy (evita retry loop infinito
                // dentro de la misma sesión, pero permite reintento si nunca se descargó)
                if (downloadOk) _downloaded = true;

                // If user wants history, load ALL files. Else, only today.
                if (ShowHistoricalNews)
                {
                    filesToLoad.AddRange(System.IO.Directory.GetFiles(cacheDir, "*.xml"));
                }
                else
                {
                    if (System.IO.File.Exists(todayCachePath)) filesToLoad.Add(todayCachePath);
                }

                // 2. Process Files
                foreach (string path in filesToLoad)
                {
                    try 
                    {
                        string content = System.IO.File.ReadAllText(path);
                        List<NewsEvent> fileEvents = ParseNewsXml(content, targets);
                        
                        foreach (var ev in fileEvents)
                        {
                             // Unique Key: Time + Title + Country
                             string key = ev.Time.ToString("yyyyMMddHHmm") + "_" + ev.Country + "_" + ev.Title;
                             if (!seenEvents.Contains(key))
                             {
                                 seenEvents.Add(key);
                                 allEvents.Add(ev);
                             }
                        }
                    }
                    catch { /* Skip bad file */ }
                }

                // Sort by Time
                allEvents = allEvents.OrderBy(e => e.Time).ToList();

                lock (_lock)
                {
                    _events = allEvents;
                }
                
                _needsDrawing = true;
                Print("RelativeNewsFilter: Loaded " + allEvents.Count + " unique events (History: " + ShowHistoricalNews + ")");
                
                // DEBUG: Print each event details
                foreach (var ev in allEvents)
                {
                    Print("  Event: " + ev.Title + " | " + ev.Country + " | " + ev.Impact + " | Time: " + ev.Time.ToString("yyyy-MM-dd HH:mm"));
                }
            }
            catch (Exception ex)
            {
                Print("RelativeNewsFilter Critical Error: " + ex.Message);
            }
        }
	
	private string[] GetTargetCurrencies()
	{
		if (!string.IsNullOrEmpty(CustomCurrencies))
		{
			return CustomCurrencies.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
							   .Select(s => s.Trim().ToUpper()).ToArray();
		}
		
		// Auto-Detect based on Instrument
		if (Instrument == null) return new string[] { "USD" };
		
		string name = Instrument.MasterInstrument.Name;
		
		// ===== EQUITY INDICES =====
		// Standard E-mini
		if (name.StartsWith("ES") || name.Contains("S&P")) return new string[] { "USD" };
		if (name.StartsWith("NQ") || name.Contains("NASDAQ")) return new string[] { "USD" };
		if (name.StartsWith("YM") || name.Contains("Dow")) return new string[] { "USD" };
		if (name.StartsWith("RTY") || name.Contains("Russell")) return new string[] { "USD" };
		
		// Micro E-mini
		if (name.StartsWith("MES")) return new string[] { "USD" }; // Micro S&P
		if (name.StartsWith("MNQ")) return new string[] { "USD" }; // Micro NASDAQ
		if (name.StartsWith("MYM")) return new string[] { "USD" }; // Micro Dow
		if (name.StartsWith("M2K")) return new string[] { "USD" }; // Micro Russell
		
		// European Indices
		if (name.StartsWith("FDAX") || name.Contains("DAX")) return new string[] { "EUR" };
		if (name.StartsWith("FESX") || name.Contains("STOXX")) return new string[] { "EUR" };
		
		// ===== FOREX =====
		if (name.StartsWith("6E") || name.Contains("Euro")) return new string[] { "EUR", "USD" };
		if (name.StartsWith("6A") || name.Contains("Aud")) return new string[] { "AUD", "USD" };
		if (name.StartsWith("6J") || name.Contains("Yen")) return new string[] { "JPY", "USD" };
		if (name.StartsWith("6B") || name.Contains("Pound")) return new string[] { "GBP", "USD" };
		if (name.StartsWith("6C") || name.Contains("CAD")) return new string[] { "CAD", "USD" };
		if (name.StartsWith("6S") || name.Contains("CHF") || name.Contains("Franc")) return new string[] { "CHF", "USD" };
		if (name.StartsWith("6M") || name.Contains("MXN") || name.Contains("Peso")) return new string[] { "MXN", "USD" };
		if (name.StartsWith("6N") || name.Contains("NZD")) return new string[] { "NZD", "USD" };
		
		// ===== COMMODITIES =====
		// Metals
		if (name.StartsWith("GC") || name.Contains("Gold")) return new string[] { "USD" };
		if (name.StartsWith("SI") || name.Contains("Silver")) return new string[] { "USD" };
		if (name.StartsWith("HG") || name.Contains("Copper")) return new string[] { "USD" };
		if (name.StartsWith("PL") || name.Contains("Platinum")) return new string[] { "USD" };
		
		// Energy
		if (name.StartsWith("CL") || name.Contains("Crude")) return new string[] { "USD" };
		if (name.StartsWith("NG") || name.Contains("Natural Gas")) return new string[] { "USD" };
		if (name.StartsWith("RB") || name.Contains("Gasoline")) return new string[] { "USD" };
		if (name.StartsWith("HO") || name.Contains("Heating Oil")) return new string[] { "USD" };
		
		// Agriculture
		if (name.StartsWith("ZC") || name.Contains("Corn")) return new string[] { "USD" };
		if (name.StartsWith("ZW") || name.Contains("Wheat")) return new string[] { "USD" };
		if (name.StartsWith("ZS") || name.Contains("Soybean")) return new string[] { "USD" };
		if (name.StartsWith("ZL") || name.Contains("Soy Oil")) return new string[] { "USD" };
		if (name.StartsWith("ZM") || name.Contains("Soy Meal")) return new string[] { "USD" };
		if (name.StartsWith("KC") || name.Contains("Coffee")) return new string[] { "USD" };
		if (name.StartsWith("SB") || name.Contains("Sugar")) return new string[] { "USD" };
		if (name.StartsWith("CT") || name.Contains("Cotton")) return new string[] { "USD" };
		if (name.StartsWith("CC") || name.Contains("Cocoa")) return new string[] { "USD" };
		
		// ===== TREASURIES =====
		if (name.StartsWith("ZB") || name.Contains("30-Year")) return new string[] { "USD" };
		if (name.StartsWith("ZN") || name.Contains("10-Year")) return new string[] { "USD" };
		if (name.StartsWith("ZF") || name.Contains("5-Year")) return new string[] { "USD" };
		if (name.StartsWith("ZT") || name.Contains("2-Year")) return new string[] { "USD" };
		if (name.StartsWith("UB") || name.Contains("Ultra")) return new string[] { "USD" };
		
		// Default
		return new string[] { "USD" };
	}
		
		private bool IsImpactRelevant(string impact)
		{
			if (string.IsNullOrEmpty(impact)) return false;
			impact = impact.ToLower();
            
            switch (FilterImpact)
            {
                case NewsImpactType.HighOnly:
                    return impact == "high";
                case NewsImpactType.HighAndMedium:
                    return impact == "high" || impact == "medium";
                case NewsImpactType.All:
                    return true;
                default:
                    return impact == "high";
            }
		}

	
	// ===== CACHE MANAGEMENT =====
	private void CleanOldCacheFiles()
	{
		try
		{
			string cacheDir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "NewsCache");
			if (!System.IO.Directory.Exists(cacheDir)) return;
			
			DateTime threshold = DateTime.Now.AddDays(-7);
			var oldFiles = System.IO.Directory.GetFiles(cacheDir, "*.xml")
				.Where(f => System.IO.File.GetLastWriteTime(f) < threshold)
				.ToList();
			
			foreach (var file in oldFiles)
			{
				System.IO.File.Delete(file);
				Print($"RelativeNewsFilter: Deleted old cache file: {System.IO.Path.GetFileName(file)}");
			}
			
			if (oldFiles.Count > 0)
				Print($"RelativeNewsFilter: Cleaned {oldFiles.Count} old cache files");
		}
		catch (Exception ex)
		{
			Print($"RelativeNewsFilter: Cache cleanup error: {ex.Message}");
		}
	}
	
	private bool IsValidCacheFile(string path)
	{
		try
		{
			if (!System.IO.File.Exists(path)) return false;
			
			System.IO.FileInfo info = new System.IO.FileInfo(path);
			if (info.Length == 0) return false; // Empty file
			if (info.Length > 10 * 1024 * 1024) return false; // > 10MB suspicious
			
			// Try to parse as XML
			string content = System.IO.File.ReadAllText(path);
			XDocument.Parse(content);
			return true;
		}
		catch
		{
			return false;
		}
	}
	
	// ===== EMAIL ALERTS =====
	private string GetEmailKey(NewsEvent ev)
	{
		return ev.Time.ToString("yyyyMMddHHmm") + "_" + ev.Title;
	}
	
	private void SendNewsEmail(NewsEvent ev, double minutesUntil)
	{
		if (!EnableEmailAlerts) return;
		if (string.IsNullOrEmpty(EmailTo) || string.IsNullOrEmpty(SmtpServer)) return;
		
		string key = GetEmailKey(ev);
		if (_sentEmailKeys.Contains(key)) return; // Already sent
		
		try
		{
			using (var client = new System.Net.Mail.SmtpClient(SmtpServer, SmtpPort))
			{
				client.EnableSsl = true;
				client.Credentials = new System.Net.NetworkCredential(EmailFrom, EmailPassword);
				
				string subject = $"📰 NEWS ALERT: {ev.Title} in {Math.Abs(minutesUntil):F0} min";
				string body = $"=== NEWS ALERT ===\n" +
							 $"Event: {ev.Title}\n" +
							 $"Country: {ev.Country}\n" +
							 $"Impact: {ev.Impact}\n" +
							 $"Time: {ev.Time:yyyy-MM-dd HH:mm}\n" +
							 $"Minutes Until: {minutesUntil:F0}\n" +
							 $"==================\n" +
							 $"Avoid trading during this period.";
				
				var message = new System.Net.Mail.MailMessage(EmailFrom, EmailTo, subject, body);
				client.Send(message);
				
				_sentEmailKeys.Add(key);
				Print($"RelativeNewsFilter: Email sent for {ev.Title}");
			}
		}
		catch (Exception ex)
		{
			Print($"RelativeNewsFilter: Email error: {ex.Message}");
		}
	}
	
		#region Properties
		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name="PauseBeforeMinutes", Order=1, GroupName="Parameters")]
		public int PauseBeforeMinutes
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name="PauseAfterMinutes", Order=2, GroupName="Parameters")]
		public int PauseAfterMinutes
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="News Impact Filter", Order=3, GroupName="Parameters")]
		public NewsImpactType FilterImpact
		{ get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Auto Sync TimeZone", Description="Automatically detects chart timezone offset from local system.", Order=4, GroupName="Parameters")]
        public bool AutoSyncTimeZone
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Time Offset (Hours)", Description="Manually adjust news time if out of sync.", Order=5, GroupName="Parameters")]
        public int TimeOffset
        { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="CustomCurrencies", Description="Comma separated (e.g. USD,EUR). Leave empty for Auto.", Order=6, GroupName="Parameters")]
		public string CustomCurrencies
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show Event Marker", Order=5, GroupName="Visual")]
		public bool ShowLines
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show Zones", Order=5, GroupName="Visual")]
		public bool ShowZones
		{ get; set; }

        [Range(0, 100)]
        [Display(Name = "Zone Opacity", GroupName = "Visual", Order = 6)]
        public int ZoneOpacity { get; set; }
		
		[XmlIgnore]
		[Display(Name="Zone Color", Order=6, GroupName="Visual")]
		public System.Windows.Media.Brush LineColor
		{ get; set; }
		
		[Browsable(false)]
		public string LineColorSerializable
		{
			get { return Serialize.BrushToString(LineColor); }
			set { LineColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="TextColor", Order=7, GroupName="Visual")]
		public System.Windows.Media.Brush TextColor
		{ get; set; }

        [Browsable(false)]
		public string TextColorSerializable
		{
			get { return Serialize.BrushToString(TextColor); }
			set { TextColor = Serialize.StringToBrush(value); }
		}
		
		
		// ===== EMAIL CONFIGURATION =====
		[NinjaScriptProperty]
		[Display(Name="Enable Email Alerts", Order=10, GroupName="Email")]
		public bool EnableEmailAlerts { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Email Alert Minutes Before", Description="Send email X minutes before event", Order=11, GroupName="Email")]
		[Range(1, 60)]
		public int EmailAlertMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name="SMTP Server", Description="Example: smtp.gmail.com", Order=12, GroupName="Email")]
		public string SmtpServer { get; set; }

		[NinjaScriptProperty]
		[Display(Name="SMTP Port", Order=13, GroupName="Email")]
		[Range(1, 65535)]
		public int SmtpPort { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Email From", Description="Your email address", Order=14, GroupName="Email")]
		public string EmailFrom { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Email To", Description="Recipient email address", Order=15, GroupName="Email")]
		public string EmailTo { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Email Password", Description="Your email password or app password", Order=16, GroupName="Email")]
		public string EmailPassword { get; set; }
		
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeNewsFilter[] cacheRelativeNewsFilter;
		public RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, autoSyncTimeZone, timeOffset, customCurrencies, showLines, showZones, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input, int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			if (cacheRelativeNewsFilter != null)
				for (int idx = 0; idx < cacheRelativeNewsFilter.Length; idx++)
					if (cacheRelativeNewsFilter[idx] != null && cacheRelativeNewsFilter[idx].PauseBeforeMinutes == pauseBeforeMinutes && cacheRelativeNewsFilter[idx].PauseAfterMinutes == pauseAfterMinutes && cacheRelativeNewsFilter[idx].FilterImpact == filterImpact && cacheRelativeNewsFilter[idx].AutoSyncTimeZone == autoSyncTimeZone && cacheRelativeNewsFilter[idx].TimeOffset == timeOffset && cacheRelativeNewsFilter[idx].CustomCurrencies == customCurrencies && cacheRelativeNewsFilter[idx].ShowLines == showLines && cacheRelativeNewsFilter[idx].ShowZones == showZones && cacheRelativeNewsFilter[idx].EnableEmailAlerts == enableEmailAlerts && cacheRelativeNewsFilter[idx].EmailAlertMinutes == emailAlertMinutes && cacheRelativeNewsFilter[idx].SmtpServer == smtpServer && cacheRelativeNewsFilter[idx].SmtpPort == smtpPort && cacheRelativeNewsFilter[idx].EmailFrom == emailFrom && cacheRelativeNewsFilter[idx].EmailTo == emailTo && cacheRelativeNewsFilter[idx].EmailPassword == emailPassword && cacheRelativeNewsFilter[idx].EqualsInput(input))
						return cacheRelativeNewsFilter[idx];
			return CacheIndicator<RelativeIndicators.RelativeNewsFilter>(new RelativeIndicators.RelativeNewsFilter(){ PauseBeforeMinutes = pauseBeforeMinutes, PauseAfterMinutes = pauseAfterMinutes, FilterImpact = filterImpact, AutoSyncTimeZone = autoSyncTimeZone, TimeOffset = timeOffset, CustomCurrencies = customCurrencies, ShowLines = showLines, ShowZones = showZones, EnableEmailAlerts = enableEmailAlerts, EmailAlertMinutes = emailAlertMinutes, SmtpServer = smtpServer, SmtpPort = smtpPort, EmailFrom = emailFrom, EmailTo = emailTo, EmailPassword = emailPassword }, input, ref cacheRelativeNewsFilter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, autoSyncTimeZone, timeOffset, customCurrencies, showLines, showZones, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input , int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, autoSyncTimeZone, timeOffset, customCurrencies, showLines, showZones, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, autoSyncTimeZone, timeOffset, customCurrencies, showLines, showZones, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input , int pauseBeforeMinutes, int pauseAfterMinutes, NewsImpactType filterImpact, bool autoSyncTimeZone, int timeOffset, string customCurrencies, bool showLines, bool showZones, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, autoSyncTimeZone, timeOffset, customCurrencies, showLines, showZones, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}
	}
}

#endregion
