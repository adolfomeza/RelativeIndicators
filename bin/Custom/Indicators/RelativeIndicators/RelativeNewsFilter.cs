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
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
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

		public class NewsEvent
		{
			public string Title { get; set; }
			public string Country { get; set; }
			public DateTime Time { get; set; } // Local NinjaTrader Time
			public string Impact { get; set; }
		}

        private DateTime _selectedEventTime = DateTime.MinValue;  // Track currently selected event
        private HashSet<string> _sentEmailKeys = new HashSet<string>();  // Track sent emails to avoid duplicates
          public override void OnRenderTargetChanged()
        {
            // Subscribe to mouse events when the control is ready
            if (ChartControl != null)
            {
                ChartControl.MouseLeftButtonDown -= OnMouseDown; // Unsub first to be safe
                ChartControl.MouseLeftButtonDown += OnMouseDown;
            }
            base.OnRenderTargetChanged();
        }

        protected override void OnStateChange()
        {
            if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.MouseLeftButtonDown -= OnMouseDown;
            }
            
            if (State == State.SetDefaults)
            {
                // Defaults
                PauseBeforeMinutes = 5;
                PauseAfterMinutes = 10;
                FilterImpact = "High"; // Low, Medium, High
                CustomCurrencies = ""; // Empty = Auto
                ShowLines = true;
                ShowHistoricalNews = false; // Default off
                LineColor = Brushes.Red; // Uses for Region Area
                TextColor = Brushes.White;
                IsOverlay = true;       // Force Overlay on Main Chart
                
                // Email defaults
                EnableEmailAlerts = false;
                EmailAlertMinutes = 15;
                SmtpServer = "smtp.gmail.com";
                SmtpPort = 587;
                EmailFrom = "";
                EmailTo = "";
                EmailPassword = "";
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
                
                if (!_downloaded)
                {
                    _downloaded = true;
                    // Fire and Forget Download
                    Task.Run(async () => await DownloadAndParseParams());
                }
            }
            base.OnStateChange();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_events == null || _events.Count == 0 || ChartControl == null) return;

            try
            {
                double mouseX = e.GetPosition(ChartControl as IInputElement).X;
                DateTime clickedTime = ChartControl.GetTimeByX((int)mouseX);

                // Find event where clickedTime is inside the [Before, After] window
                NewsEvent hit = null;

                lock (_lock)
                {
                    foreach (var ev in _events)
                    {
                        // Window: Start = Time - Before, End = Time + After
                        DateTime start = ev.Time.AddMinutes(-PauseBeforeMinutes);
                        DateTime end = ev.Time.AddMinutes(PauseAfterMinutes);
                        
                        if (clickedTime >= start && clickedTime <= end)
                        {
                            hit = ev;
                            break; // Take first hit
                        }
                    }
                }

                if (hit != null)
                {
                    // Toggle Selection
                    if (_selectedEventTime == hit.Time)
                         _selectedEventTime = DateTime.MinValue; 
                    else
                         _selectedEventTime = hit.Time;      

                    _needsDrawing = true;
                    Print("RelativeNewsFilter: Clicked Event " + hit.Title);
                }
            }
            catch (Exception ex)
            {
                Print("RelativeNewsFilter OnMouseDown Error: " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            // Custom News Filtering Logic
            if (_events == null || _events.Count == 0) 
            {
                IsNewsImminent = false;
                MinutesToNews = 999;
                return;
            }

            DateTime currentBarTime = Time[0];
            bool imminent = false;
            string title = "";
            double minDiff = 999;

            lock (_lock)
            {
                foreach (var ev in _events)
                {
                    TimeSpan diff = ev.Time - currentBarTime;
                    double totalMinutes = diff.TotalMinutes; 
                    
                    // Send email if approaching and within alert window
                    if (totalMinutes > 0 && totalMinutes <= EmailAlertMinutes)
                    {
                        SendNewsEmail(ev, totalMinutes);
                    }
                    
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
            
            // Visual Update Trigger
            if (_needsDrawing)
            {
                Print("OnBarUpdate: _needsDrawing is TRUE, calling DrawNewsLines()");
                _needsDrawing = false;
                DrawNewsLines();
                ForceRefresh();
            }
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
                         TimeZoneInfo nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                         DateTime localTime = TimeZoneInfo.ConvertTime(eventTimeEst, nyZone, TimeZoneInfo.Local);
                         
                         parsed.Add(new NewsEvent 
                         {
                             Title = ev.Element("title")?.Value,
                             Country = country,
                             Impact = impact,
                             Time = localTime
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
                
                // Always try to execute "Today's" logic (Download or Load)
                // If local file missing, download it.
                if (!System.IO.File.Exists(todayCachePath))
                {
                    try 
                    {
                        using (var client = new HttpClient())
                        {
                             // Only download if we don't have today's file
                             string webData = await client.GetStringAsync(FeedUrl);
                             System.IO.File.WriteAllText(todayCachePath, webData);
                             Print("RelativeNewsFilter: Downloaded & Cached Today's Data.");
                        }
                    }
                    catch (Exception ex) { Print("RelativeNewsFilter Download Failed: " + ex.Message); }
                }

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

        private void DrawNewsLines()
        {
            Print("DrawNewsLines() called - ShowLines: " + ShowLines + " Events: " + (_events != null ? _events.Count.ToString() : "null"));
            
            if (!ShowLines)
            {
                Print("  ShowLines is FALSE, returning");
                return;
            }
            
            lock (_lock)
            {
                Print("  Drawing " + _events.Count + " event zones...");
                
                foreach (var ev in _events)
                {
                    string tag = "NewsRegion_" + ev.Title + "_" + ev.Time.Ticks;
                    string textTag = tag + "_txt";

                    // 1. Draw Region (Rectangle)
                    // Start: Time - Before
                    // End: Time + After
                    DateTime start = ev.Time.AddMinutes(-PauseBeforeMinutes);
                    DateTime end = ev.Time.AddMinutes(PauseAfterMinutes);
                    
                    // Draw Full Height Strip
                    // NOTE: Double.MaxValue/MinValue allows full vertical span, but MUST set IsAutoScale=false to avoid crushing scale.
                    NinjaTrader.NinjaScript.DrawingTools.Rectangle rect = Draw.Rectangle(this, tag, false, start, double.MaxValue, end, double.MinValue, Brushes.Transparent, LineColor, 30);
                    rect.IsAutoScale = false; 

                    // Debug text to confirm drawing
                    Print("  Drawing News Strip: " + ev.Title + " from " + start.ToString("yyyy-MM-dd HH:mm") + " to " + end.ToString("yyyy-MM-dd HH:mm"));

                    // 2. Draw Text (Only if Selected)
                    if (ev.Time == _selectedEventTime)
                    {
                        int barIdx = Bars.GetBar(ev.Time);
                        if (barIdx >= 0)
                        {
                            int barsAgo = CurrentBars[0] - barIdx;
                            // Usage: Input[0] instead of Values[0][0] since we have no plots
                            NinjaTrader.NinjaScript.DrawingTools.Text t = Draw.Text(this, textTag, ev.Title, barsAgo, Input[0], Brushes.White);
                            
                            t.AreaBrush = Brushes.Black;
                            t.AreaOpacity = 80; // High contrast
                        }
                    }
                    else
                    {
                        RemoveDrawObject(textTag);
                    }
                }
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
			
			// If FilterImpact is High, only allow High
			if (FilterImpact.Equals("High", StringComparison.OrdinalIgnoreCase))
			{
				return impact == "high";
			}
			// If Medium, allow High and Medium
			if (FilterImpact.Equals("Medium", StringComparison.OrdinalIgnoreCase))
			{
				return impact == "high" || impact == "medium";
			}
			// If Low, allow all
			return true;
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
		[Display(Name="FilterImpact", Order=3, GroupName="Parameters")]
		public string FilterImpact // High, Medium, Low
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="CustomCurrencies", Description="Comma separated (e.g. USD,EUR). Leave empty for Auto.", Order=4, GroupName="Parameters")]
		public string CustomCurrencies
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="ShowLines", Order=5, GroupName="Visual")]
		public bool ShowLines
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="LineColor", Order=6, GroupName="Visual")]
		public Brush LineColor
		{ get; set; }
		
		/*
		[Browsable(false)]
		public string LineColorSerializable
		{
			get { return Serialize.BrushToString(LineColor); }
			set { LineColor = Serialize.BrushFromString(value); }
		}
		*/
		
		[XmlIgnore]
		[Display(Name="TextColor", Order=7, GroupName="Visual")]
		public Brush TextColor
		{ get; set; }
		
		
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
		public RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, customCurrencies, showLines, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input, int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			if (cacheRelativeNewsFilter != null)
				for (int idx = 0; idx < cacheRelativeNewsFilter.Length; idx++)
					if (cacheRelativeNewsFilter[idx] != null && cacheRelativeNewsFilter[idx].PauseBeforeMinutes == pauseBeforeMinutes && cacheRelativeNewsFilter[idx].PauseAfterMinutes == pauseAfterMinutes && cacheRelativeNewsFilter[idx].FilterImpact == filterImpact && cacheRelativeNewsFilter[idx].CustomCurrencies == customCurrencies && cacheRelativeNewsFilter[idx].ShowLines == showLines && cacheRelativeNewsFilter[idx].EnableEmailAlerts == enableEmailAlerts && cacheRelativeNewsFilter[idx].EmailAlertMinutes == emailAlertMinutes && cacheRelativeNewsFilter[idx].SmtpServer == smtpServer && cacheRelativeNewsFilter[idx].SmtpPort == smtpPort && cacheRelativeNewsFilter[idx].EmailFrom == emailFrom && cacheRelativeNewsFilter[idx].EmailTo == emailTo && cacheRelativeNewsFilter[idx].EmailPassword == emailPassword && cacheRelativeNewsFilter[idx].EqualsInput(input))
						return cacheRelativeNewsFilter[idx];
			return CacheIndicator<RelativeIndicators.RelativeNewsFilter>(new RelativeIndicators.RelativeNewsFilter(){ PauseBeforeMinutes = pauseBeforeMinutes, PauseAfterMinutes = pauseAfterMinutes, FilterImpact = filterImpact, CustomCurrencies = customCurrencies, ShowLines = showLines, EnableEmailAlerts = enableEmailAlerts, EmailAlertMinutes = emailAlertMinutes, SmtpServer = smtpServer, SmtpPort = smtpPort, EmailFrom = emailFrom, EmailTo = emailTo, EmailPassword = emailPassword }, input, ref cacheRelativeNewsFilter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, customCurrencies, showLines, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input , int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, customCurrencies, showLines, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(Input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, customCurrencies, showLines, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}

		public Indicators.RelativeIndicators.RelativeNewsFilter RelativeNewsFilter(ISeries<double> input , int pauseBeforeMinutes, int pauseAfterMinutes, string filterImpact, string customCurrencies, bool showLines, bool enableEmailAlerts, int emailAlertMinutes, string smtpServer, int smtpPort, string emailFrom, string emailTo, string emailPassword)
		{
			return indicator.RelativeNewsFilter(input, pauseBeforeMinutes, pauseAfterMinutes, filterImpact, customCurrencies, showLines, enableEmailAlerts, emailAlertMinutes, smtpServer, smtpPort, emailFrom, emailTo, emailPassword);
		}
	}
}

#endregion
