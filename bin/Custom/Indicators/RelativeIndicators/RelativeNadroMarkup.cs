#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	/// <summary>
	/// RelativeNadroMarkup v0.1.5 (POC) — plasma en el chart el análisis NADRO hecho en MCP.
	/// Lee archivos JSON de Docs/Nadro/markups/{INSTRUMENT}_YYYY-MM-DD.json y pinta:
	/// confluencias, niveles, entry/stop/targets, flechas.
	/// v0.1.2: labels de HYPOS como cards negros sólidos (entry/stop/target).
	///         Render en 2 pasadas: PASS 1 todas las líneas+flechas, PASS 2 todas las labels (encima).
	/// v0.1.3: 3 botones en toolbar nativa NT (📸 Pit-Open, ⚡ Ad-Hoc, 🌙 EOD Review).
	/// v0.1.4: snapshot requests PLAYBACK-SAFE con captured_data completo.
	/// v0.1.5: FIX timestamp obsoleto vía Bars.GetTime() en lugar de Time[0].
	/// v0.1.6: FIX completo. Bars.GetClose/Open/High/Low/Volume/Time directos en lugar de
	///         Close[0]/Open[idx]/etc — todos los indexadores dependen del último OnBarUpdate
	///         del indicador. Si chart estaba en otra pestaña (IsSuspendedWhileInactive=true),
	///         Close[0]/Time[idx] devolvían valores de la última sesión que vio el indicador.
	///         Ahora precio + 50 bars + timestamp se leen del array Bars directo.
	/// </summary>
	public enum RelativeNadroRenderLayer
	{
		Full,
		BriefingOnly,
		MarkupOnly
	}

	public partial class RelativeNadroMarkup : Indicator
	{
		#region Data classes

		private class MarkupTarget
		{
			public string Label;
			public double Price;
			public double RR;
		}

		private class MarkupHypo
		{
			public string Id;
			public string Direction;
			public string SetupType;
			public List<string> SetupCompanions = new List<string>();
			public string TradingHorizon;
			public double Entry;
			public double Stop;
			public double RiskPts;
			public string Grade;
			public string Notes;
			public List<MarkupTarget> Targets = new List<MarkupTarget>();
			public string OutcomeStatus = "pending";
			public bool SetupReachedT1 = false;
			public bool SetupReachedT2 = false;
			public bool SetupReachedT3 = false;
		}

		private class MarkupConfluence
		{
			public string Label;
			public double PriceMin;
			public double PriceMax;
			public string Grade;
			public List<string> Members = new List<string>();
		}

		private class MarkupLevel
		{
			public string Label;
			public double Price;
		}

		private class MarkupSnapshot
		{
			public string Id;
			public DateTime Timestamp;
			public double PriceAtAnalysis;
			public string Regime;
			public string Bias;
			public string Summary;
			public string AnalysisText;
			public List<MarkupConfluence> Confluences = new List<MarkupConfluence>();
			public List<MarkupLevel> Levels = new List<MarkupLevel>();
			public List<MarkupHypo> Hypos = new List<MarkupHypo>();
		}

		#endregion

		private List<MarkupSnapshot> _snapshots = new List<MarkupSnapshot>();
		private DateTime _lastRead = DateTime.MinValue;
		private string _markupsDir;
		private string _lastInstrument = "";

		// PERF: cache de markups parseados por mtime. Evita re-leer y re-parsear
		// JSONs de cada día N días atrás cuando nada cambió en disco. Granularidad
		// File.GetLastWriteTimeUtc().
		private struct MarkupCacheEntry { public DateTime Mtime; public List<MarkupSnapshot> Snapshots; }
		private Dictionary<string, MarkupCacheEntry> _markupParseCache = new Dictionary<string, MarkupCacheEntry>();

		// v: scroll con mouse wheel sobre el panel del briefing
		private int _scrollOffsetLines = 0;
		private SharpDX.RectangleF _briefingRect;
		private bool _hasBriefingRect = false;
		private bool _wheelHooked = false;

		// PERF: cache de brushes WPF→DX (evita 14+ allocations por frame).
		// Reusados entre frames mientras el RenderTarget viva. Disposed en OnRenderTargetChanged.
		private Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush> _dxBrushCache;
		// PERF: TextFormat cacheados (evita 2+ allocations por frame).
		private SharpDX.DirectWrite.TextFormat _cachedLabelFmt;
		private SharpDX.DirectWrite.TextFormat _cachedNotesFmt;
		private int _cachedLabelFontSize = -1;
		private int _cachedNotesFontSize = -1;

		private SharpDX.Direct2D1.Brush GetCachedDxBrush(System.Windows.Media.Brush wpfBrush)
		{
			if (wpfBrush == null || RenderTarget == null) return null;
			if (_dxBrushCache == null)
				_dxBrushCache = new Dictionary<System.Windows.Media.Brush, SharpDX.Direct2D1.Brush>();

			SharpDX.Direct2D1.Brush dx;
			if (_dxBrushCache.TryGetValue(wpfBrush, out dx) && dx != null && !dx.IsDisposed)
				return dx;

			dx = wpfBrush.ToDxBrush(RenderTarget);
			_dxBrushCache[wpfBrush] = dx;
			return dx;
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();
			DisposeBrushCache();
			DisposeTextFormatCache();
		}

		private void DisposeBrushCache()
		{
			if (_dxBrushCache != null)
			{
				foreach (var kv in _dxBrushCache)
					kv.Value?.Dispose();
				_dxBrushCache.Clear();
			}
			if (_labelBgBrushCache != null) { _labelBgBrushCache.Dispose(); _labelBgBrushCache = null; }
		}

		private void DisposeTextFormatCache()
		{
			_cachedLabelFmt?.Dispose(); _cachedLabelFmt = null;
			_cachedNotesFmt?.Dispose(); _cachedNotesFmt = null;
			_cachedLabelFontSize = -1;
			_cachedNotesFontSize = -1;
		}
		private NinjaTrader.Gui.Chart.ChartControl _hookedControl = null;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Plasma en el chart los análisis NADRO (snapshot MCP).";
				Name = "RelativeNadroMarkup";
				IsOverlay = true;
				Calculate = Calculate.OnPriceChange;
				IsAutoScale = false;
				IsSuspendedWhileInactive = true;

				RefreshSeconds = 10;
				DaysBack = 5;
				ShowVerticalAnchor = true;
				ShowConfluences = true;
				ShowHypos = true;
				ShowLevels = true;
				ShowNotes = true;

				ConfluenceOpacity = 22;
				DimPastOpacity = 50;
				LabelFontSize = 11;
				NotesFontSize = 10;
				ArrowLengthBars = 30;
				ShowAnalysisText = true;
				AnalysisAreaWidth = 380;
				TopPadding = 110;
				RenderLayer = RelativeNadroRenderLayer.Full;
				MarkupOpacity = 100;

				ConfluenceAPlusColor = Brushes.OrangeRed;
				ConfluenceAColor = Brushes.Orange;
				ConfluenceBColor = Brushes.Gold;
				LineInTheSandColor = Brushes.Yellow;
				LineInTheSandOpacity = 50;
				LevelDimOpacity = 30;  // Rank & Distill: niveles no usados en hipos atenuados al 30%
				LevelColor = Brushes.LightGray;
				EntryColor = Brushes.White;
				StopColor = Brushes.Crimson;
				TargetColor = Brushes.DeepSkyBlue;       // T1, T2 (intermedios) — color anterior
				FinalTargetColor = Brushes.RoyalBlue;    // último target (destino macro NADRO)
				TargetZonePercent = 0.0;   // 0 = solo línea sin zona (default). Subir > 0 para activar zona.
				TargetZoneOpacity = 50;
				AnchorColor = Brushes.DarkGray;

				ArrowPendingColor = Brushes.Gold;
				ArrowTriggeredColor = Brushes.Cyan;
				ArrowFilledColor = Brushes.LimeGreen;
				ArrowStoppedColor = Brushes.Red;
				ArrowMissedColor = Brushes.DimGray;

				NotesColor = Brushes.White;
			}
			else if (State == State.DataLoaded)
			{
				_markupsDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"NinjaTrader 8", "bin", "Custom", "Indicators", "RelativeIndicators",
					"Docs", "Nadro", "markups");

				// v0.1.3: hook chart toolbar for snapshot request buttons
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync((Action)(() => AddToolBar()));
			}
			else if (State == State.Terminated)
			{
				if (_wheelHooked && _hookedControl != null)
				{
					try { _hookedControl.PreviewMouseWheel -= OnChartMouseWheel; } catch { }
					_hookedControl = null;
					_wheelHooked = false;
				}
				_hasBriefingRect = false;

				// PERF: liberar caches SharpDX
				DisposeBrushCache();
				DisposeTextFormatCache();

				// v0.1.3: cleanup chart toolbar
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync((Action)(() => RemoveToolBar()));
			}
		}

		private void OnChartMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
		{
			if (!_hasBriefingRect) return;
			try
			{
				var pt = e.GetPosition(sender as System.Windows.IInputElement);
				if (pt.X >= _briefingRect.Left && pt.X <= _briefingRect.Right
					&& pt.Y >= _briefingRect.Top && pt.Y <= _briefingRect.Bottom)
				{
					_scrollOffsetLines += (e.Delta > 0 ? -3 : 3);
					if (_scrollOffsetLines < 0) _scrollOffsetLines = 0;
					e.Handled = true;
					if (ChartControl != null) ChartControl.InvalidateVisual();
				}
			}
			catch { }
		}

		protected override void OnBarUpdate()
		{
			if (State != State.Realtime && CurrentBar < Bars.Count - 2) return;

			string currentInstrument = Instrument.MasterInstrument.Name;
			if (currentInstrument != _lastInstrument)
			{
				_lastRead = DateTime.MinValue;
				_lastInstrument = currentInstrument;
				_markupParseCache.Clear(); // PERF: cache mapea filename → snapshots; al cambiar de instrumento ya no aplica.
			}

			if ((DateTime.Now - _lastRead).TotalSeconds >= RefreshSeconds)
			{
				ReadMarkups(currentInstrument);
				_lastRead = DateTime.Now;
			}
		}

		private void ReadMarkups(string instrument)
		{
			_snapshots.Clear();
			if (!Directory.Exists(_markupsDir)) return;

			DateTime cutoff = DateTime.Today.AddDays(-Math.Max(0, DaysBack - 1));

			string[] files;
			try { files = Directory.GetFiles(_markupsDir, instrument + "_*.json"); }
			catch { return; }

			var ser = new JavaScriptSerializer();
			ser.MaxJsonLength = 20 * 1024 * 1024;

			foreach (var path in files)
			{
				DateTime fileDate;
				if (!TryParseDateFromFileName(path, out fileDate)) continue;
				if (fileDate < cutoff) continue;

				// PERF: short-circuit si el archivo no cambió desde la última lectura.
				DateTime mtime;
				try { mtime = File.GetLastWriteTimeUtc(path); } catch { mtime = DateTime.MinValue; }

				MarkupCacheEntry cached;
				if (_markupParseCache.TryGetValue(path, out cached) && cached.Mtime == mtime && cached.Snapshots != null)
				{
					_snapshots.AddRange(cached.Snapshots);
					continue;
				}

				try
				{
					string raw = File.ReadAllText(path);
					var root = ser.DeserializeObject(raw) as Dictionary<string, object>;
					if (root == null) continue;

					object snaps;
					if (!root.TryGetValue("snapshots", out snaps)) continue;
					var snapArr = snaps as object[];
					if (snapArr == null) continue;

					var parsedList = new List<MarkupSnapshot>(snapArr.Length);
					foreach (var s in snapArr)
					{
						var snapDict = s as Dictionary<string, object>;
						if (snapDict == null) continue;
						var parsed = ParseSnapshot(snapDict);
						if (parsed != null) parsedList.Add(parsed);
					}
					_snapshots.AddRange(parsedList);
					_markupParseCache[path] = new MarkupCacheEntry { Mtime = mtime, Snapshots = parsedList };
				}
				catch (Exception ex)
				{
					Print("[RelativeNadroMarkup] error leyendo " + path + ": " + ex.Message);
				}
			}
		}

		private bool TryParseDateFromFileName(string path, out DateTime date)
		{
			date = DateTime.MinValue;
			string name = Path.GetFileNameWithoutExtension(path);
			int idx = name.LastIndexOf('_');
			if (idx < 0 || idx >= name.Length - 1) return false;
			string dstr = name.Substring(idx + 1);
			return DateTime.TryParseExact(dstr, "yyyy-MM-dd",
				System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.None, out date);
		}

		private MarkupSnapshot ParseSnapshot(Dictionary<string, object> d)
		{
			try
			{
				var snap = new MarkupSnapshot();
				snap.Id = GetString(d, "id", "");
				snap.Timestamp = GetDateTime(d, "timestamp");
				snap.PriceAtAnalysis = GetDouble(d, "price_at_analysis", 0);
				snap.Regime = GetString(d, "regime", "");
				snap.Bias = GetString(d, "bias", "");
				snap.Summary = GetString(d, "summary", "");
				snap.AnalysisText = GetString(d, "analysis_text", "");

				object o;
				if (d.TryGetValue("confluences", out o) && o is object[])
				{
					foreach (var c in (object[])o)
					{
						var cd = c as Dictionary<string, object>;
						if (cd == null) continue;
						var mc = new MarkupConfluence();
						mc.Label = GetString(cd, "label", "");
						mc.PriceMin = GetDouble(cd, "price_min", 0);
						mc.PriceMax = GetDouble(cd, "price_max", 0);
						mc.Grade = GetString(cd, "grade", "");
						object m;
						if (cd.TryGetValue("members", out m) && m is object[])
							foreach (var mm in (object[])m) mc.Members.Add(mm.ToString());
						snap.Confluences.Add(mc);
					}
				}

				if (d.TryGetValue("levels", out o) && o is object[])
				{
					foreach (var l in (object[])o)
					{
						var ld = l as Dictionary<string, object>;
						if (ld == null) continue;
						var ml = new MarkupLevel();
						ml.Label = GetString(ld, "label", "");
						ml.Price = GetDouble(ld, "price", 0);
						snap.Levels.Add(ml);
					}
				}

				if (d.TryGetValue("hypos", out o) && o is object[])
				{
					foreach (var h in (object[])o)
					{
						var hd = h as Dictionary<string, object>;
						if (hd == null) continue;
						var mh = new MarkupHypo();
						mh.Id = GetString(hd, "id", "");
						mh.Direction = GetString(hd, "direction", "");
						mh.SetupType = GetString(hd, "setup_type", "");
						object companionsObj;
						if (hd.TryGetValue("setup_companions", out companionsObj) && companionsObj is object[])
						{
							foreach (var c in (object[])companionsObj) mh.SetupCompanions.Add(c.ToString());
						}
						mh.TradingHorizon = GetString(hd, "trading_horizon", "");
						mh.Entry = GetDouble(hd, "entry", 0);
						mh.Stop = GetDouble(hd, "stop", 0);
						mh.RiskPts = GetDouble(hd, "risk_pts", 0);
						mh.Grade = GetString(hd, "grade", "");
						mh.Notes = GetString(hd, "notes", "");

						object t;
						if (hd.TryGetValue("targets", out t) && t is object[])
						{
							foreach (var tt in (object[])t)
							{
								var td = tt as Dictionary<string, object>;
								if (td == null) continue;
								var mt = new MarkupTarget();
								mt.Label = GetString(td, "label", "");
								mt.Price = GetDouble(td, "price", 0);
								mt.RR = GetDouble(td, "rr", 0);
								mh.Targets.Add(mt);
							}
						}

						object oc;
						if (hd.TryGetValue("outcome", out oc) && oc is Dictionary<string, object>)
						{
							var ocd = (Dictionary<string, object>)oc;
							mh.OutcomeStatus = GetString(ocd, "status", "pending");
							// Prioriza trade_status si existe (schema v2)
							string ts = GetString(ocd, "trade_status", "");
							if (!string.IsNullOrEmpty(ts)) mh.OutcomeStatus = ts;
							mh.SetupReachedT1 = GetBool(ocd, "setup_reached_t1", false);
							mh.SetupReachedT2 = GetBool(ocd, "setup_reached_t2", false);
							mh.SetupReachedT3 = GetBool(ocd, "setup_reached_t3", false);
						}

						snap.Hypos.Add(mh);
					}
				}

				return snap;
			}
			catch { return null; }
		}

		#region JSON helpers

		private static string GetString(Dictionary<string, object> d, string key, string def)
		{
			object v;
			return d.TryGetValue(key, out v) && v != null ? v.ToString() : def;
		}

		private static double GetDouble(Dictionary<string, object> d, string key, double def)
		{
			object v;
			if (!d.TryGetValue(key, out v) || v == null) return def;
			if (v is double) return (double)v;
			if (v is int) return (int)v;
			if (v is decimal) return (double)(decimal)v;
			double res;
			if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
				System.Globalization.CultureInfo.InvariantCulture, out res)) return res;
			return def;
		}

		private static bool GetBool(Dictionary<string, object> d, string key, bool def)
		{
			object v;
			if (!d.TryGetValue(key, out v) || v == null) return def;
			if (v is bool) return (bool)v;
			string s = v.ToString().Trim().ToLowerInvariant();
			if (s == "true" || s == "1" || s == "yes") return true;
			if (s == "false" || s == "0" || s == "no") return false;
			return def;
		}

		private static DateTime GetDateTime(Dictionary<string, object> d, string key)
		{
			object v;
			if (!d.TryGetValue(key, out v) || v == null) return DateTime.MinValue;
			DateTime dt;
			if (DateTime.TryParse(v.ToString(),
				System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeLocal, out dt)) return dt;
			return DateTime.MinValue;
		}

		#endregion

		private static readonly Brush[] _hypoPalette = new Brush[]
		{
			Brushes.DeepSkyBlue, Brushes.Magenta, Brushes.YellowGreen, Brushes.Orange, Brushes.HotPink
		};

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (chartControl == null || _snapshots.Count == 0 || !IsVisible) return;

			// v: hookear mouse wheel una sola vez
			if (!_wheelHooked)
			{
				try
				{
					chartControl.PreviewMouseWheel += OnChartMouseWheel;
					_hookedControl = chartControl;
					_wheelHooked = true;
				}
				catch { }
			}

			float chartRight = (float)ChartPanel.X + (float)ChartPanel.W;

			// v: layer control + opacity
			bool showMarkup = RenderLayer != RelativeNadroRenderLayer.BriefingOnly;
			bool showBriefing = RenderLayer != RelativeNadroRenderLayer.MarkupOnly;

			// PERF: brushes cacheados via GetCachedDxBrush (reusan entre frames).
			// Antes: 14+ new SolidColorBrush por frame. Ahora: 0 (todos del cache).
			var aPlus = GetCachedDxBrush(ConfluenceAPlusColor);
			var aBrush = GetCachedDxBrush(ConfluenceAColor);
			var bBrush = GetCachedDxBrush(ConfluenceBColor);
			var lvlBr = GetCachedDxBrush(LevelColor);
			var entryBr = GetCachedDxBrush(EntryColor);
			var stopBr = GetCachedDxBrush(StopColor);
			var tgtBr = GetCachedDxBrush(TargetColor);
			var finalTgtBr = GetCachedDxBrush(FinalTargetColor);
			var anchorBr = GetCachedDxBrush(AnchorColor);
			var notesBr = GetCachedDxBrush(NotesColor);
			var pendBr = GetCachedDxBrush(ArrowPendingColor);
			var trigBr = GetCachedDxBrush(ArrowTriggeredColor);
			var fillBr = GetCachedDxBrush(ArrowFilledColor);
			var stopArrBr = GetCachedDxBrush(ArrowStoppedColor);
			var missBr = GetCachedDxBrush(ArrowMissedColor);
			if (_labelBgBrushCache == null || _labelBgBrushCache.IsDisposed)
				_labelBgBrushCache = System.Windows.Media.Brushes.Black.ToDxBrush(RenderTarget);

			// Palette por hipo (cacheada también)
			var hypoBrushes = new List<SharpDX.Direct2D1.Brush>();
			foreach (var b in _hypoPalette) hypoBrushes.Add(GetCachedDxBrush(b));

			// v: aplicar opacidad en modo MarkupOnly (todos los brushes de markup, incluyendo labels)
			if (RenderLayer == RelativeNadroRenderLayer.MarkupOnly && MarkupOpacity < 100)
			{
				float op = MarkupOpacity / 100f;
				aPlus.Opacity = op; aBrush.Opacity = op; bBrush.Opacity = op;
				lvlBr.Opacity = op;
				entryBr.Opacity = op; stopBr.Opacity = op; tgtBr.Opacity = op;
				anchorBr.Opacity = op;
				notesBr.Opacity = op;
				pendBr.Opacity = op; trigBr.Opacity = op; fillBr.Opacity = op;
				stopArrBr.Opacity = op; missBr.Opacity = op;
				foreach (var b in hypoBrushes) b.Opacity = op;
			}

			// PERF: TextFormats cacheados, recreados solo si LabelFontSize/NotesFontSize cambian.
			if (_cachedLabelFmt == null || _cachedLabelFontSize != LabelFontSize)
			{
				_cachedLabelFmt?.Dispose();
				_cachedLabelFmt = new SimpleFont("Arial", LabelFontSize).ToDirectWriteTextFormat();
				_cachedLabelFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
				_cachedLabelFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
				_cachedLabelFontSize = LabelFontSize;
			}
			if (_cachedNotesFmt == null || _cachedNotesFontSize != NotesFontSize)
			{
				_cachedNotesFmt?.Dispose();
				_cachedNotesFmt = new SimpleFont("Arial", NotesFontSize).ToDirectWriteTextFormat();
				_cachedNotesFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
				_cachedNotesFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;
				_cachedNotesFontSize = NotesFontSize;
			}
			var labelFmt = _cachedLabelFmt;
			var notesFmt = _cachedNotesFmt;

			// v: identificar snapshot "activo" para evitar superposicion de paneles.
			// Los markups graficos (lineas, flechas, niveles) se pintan para todos,
			// pero el panel de texto + caja de notas solo para el snapshot activo
			// = el que tiene su anchor mas cerca del centro del viewport visible.
			// Si ninguno esta en viewport, usar el ultimo (mas reciente) como fallback.
			MarkupSnapshot activeSnap = null;
			{
				float viewLeft = (float)ChartPanel.X;
				float viewRight = chartRight;
				float viewCenter = (viewLeft + viewRight) * 0.5f;
				float minDist = float.MaxValue;
				foreach (var s in _snapshots)
				{
					float xA = (float)chartControl.GetXByTime(s.Timestamp);
					if (xA < viewLeft || xA > viewRight) continue;
					float d = Math.Abs(xA - viewCenter);
					if (d < minDist) { minDist = d; activeSnap = s; }
				}
				if (activeSnap == null && _snapshots.Count > 0)
					activeSnap = _snapshots[_snapshots.Count - 1];
			}

			foreach (var snap in _snapshots)
			{
				float xAnchor = (float)chartControl.GetXByTime(snap.Timestamp);
				if (xAnchor < 0) continue;

				// Anti-colisión de labels (reuso patrón RelativeVwapLevels)
				var obstacles = new List<SharpDX.RectangleF>();

				// Opacar el "pasado" (área a la izquierda del anchor) con un velo negro
				// semi-transparente. Resalta visualmente que solo el lado derecho del
				// snapshot es relevante para la operativa actual.
				if (showMarkup && DimPastOpacity > 0 && snap == activeSnap)
				{
					float dimRight = xAnchor;
					float dimLeft = (float)ChartPanel.X;
					if (dimRight > dimLeft && _labelBgBrushCache != null)
					{
						float prevOp = _labelBgBrushCache.Opacity;
						_labelBgBrushCache.Opacity = (float)(DimPastOpacity / 100.0);
						RenderTarget.FillRectangle(
							new SharpDX.RectangleF(dimLeft, (float)ChartPanel.Y,
								dimRight - dimLeft, (float)ChartPanel.H),
							_labelBgBrushCache);
						_labelBgBrushCache.Opacity = prevOp;
					}
				}

				if (ShowVerticalAnchor && showMarkup)
				{
					DrawDashedVertical(xAnchor, (float)ChartPanel.Y,
						(float)ChartPanel.Y + (float)ChartPanel.H, anchorBr);
				}

				if (ShowConfluences && showMarkup)
				{
					var lisBrush = GetCachedDxBrush(LineInTheSandColor);
					foreach (var c in snap.Confluences)
					{
						// Detección Line in the Sand (LIS): label que contiene
						// "LINE IN THE SAND" (case-insensitive). Pintado distinto
						// para destacar el pivote direccional del día.
						bool isLis = !string.IsNullOrEmpty(c.Label)
							&& c.Label.IndexOf("LINE IN THE SAND",
								StringComparison.OrdinalIgnoreCase) >= 0;

						SharpDX.Direct2D1.Brush br;
						float opacity;
						if (isLis && lisBrush != null)
						{
							br = lisBrush;
							opacity = (float)(LineInTheSandOpacity / 100.0);
						}
						else
						{
							br = bBrush;
							if (!string.IsNullOrEmpty(c.Grade))
							{
								if (c.Grade.StartsWith("A+")) br = aPlus;
								else if (c.Grade.StartsWith("A")) br = aBrush;
								else br = bBrush;
							}
							opacity = (float)(ConfluenceOpacity / 100.0);
						}
						br.Opacity = opacity;

						float yTop = (float)chartScale.GetYByValue(c.PriceMax);
						float yBot = (float)chartScale.GetYByValue(c.PriceMin);
						if (yBot - yTop < 1) yBot = yTop + 1;

						float xStart = xAnchor;
						float xEnd;
						if (isLis)
						{
							// LIS se extiende hasta el final del chart visible — cubre
							// la vela actual + el resto de la sesión. NADRO: la zona
							// pivote del día se mantiene activa hasta cierre de sesión.
							xEnd = chartRight;
						}
						else
						{
							xEnd = Math.Min(chartRight, xAnchor + ArrowLengthBars * 10f);
						}
						if (xEnd <= xStart) xEnd = xStart + 1;

						RenderTarget.FillRectangle(
							new SharpDX.RectangleF(xStart, yTop, xEnd - xStart, yBot - yTop), br);

						br.Opacity = 1f;
						string lblTxt = (c.Grade ?? "") + " " + (c.Label ?? "") + " [" + c.Members.Count + "]";
						// Texto centrado verticalmente dentro de la zona (centro entre yTop y yBot).
						float yMid = (yTop + yBot) * 0.5f - LabelFontSize * 0.5f;
						DrawLabelAt(xStart + 4, yMid, lblTxt, labelFmt, notesBr, obstacles, 360);
					}
				}

				// Rank & Distill (NADRO Guía 04 §7): niveles usados en hipos = full opacity,
				// los demás = atenuados (LevelDimOpacity %) para reducir ruido visual.
				var usedPrices = new List<double>();
				if (snap.Hypos != null)
				{
					foreach (var h in snap.Hypos)
					{
						usedPrices.Add(h.Entry);
						usedPrices.Add(h.Stop);
						if (h.Targets != null)
							foreach (var t in h.Targets) usedPrices.Add(t.Price);
					}
				}
				double levelTol = TickSize * 2;
				Func<double, bool> isLevelUsed = (price) =>
				{
					foreach (var up in usedPrices)
						if (Math.Abs(price - up) <= levelTol) return true;
					return false;
				};
				float dimOpacity = (float)(LevelDimOpacity / 100.0);

				// PASS 1 niveles: solo las líneas dashed (debajo de las líneas de hypos).
				if (ShowLevels && showMarkup)
				{
					foreach (var l in snap.Levels)
					{
						float y = (float)chartScale.GetYByValue(l.Price);
						bool used = isLevelUsed(l.Price);
						float prevOp = lvlBr.Opacity;
						lvlBr.Opacity = used ? 1.0f : dimOpacity;
						DrawDashedHorizontal(xAnchor, chartRight, y, lvlBr);
						lvlBr.Opacity = prevOp;
					}
				}

				if (ShowHypos && showMarkup)
				{
					// Decoupled — labels y flechas se posicionan independientemente:
					//   - labels a la IZQUIERDA del anchor (no tapan price action que está a la derecha)
					//     fallback derecha si no hay 260px libres a la izquierda.
					//   - flechas siempre apuntan al FUTURO (derecha) si hay espacio suficiente,
					//     fallback izquierda solo cuando el anchor está pegado al borde derecho.
					bool labelsLeft = (xAnchor - (float)ChartPanel.X) >= 260f;
					bool arrowFlipped = (chartRight - xAnchor) < 80f; // solo flip arrow si no hay nada a la derecha

					float xArrowStart, xArrowEnd;
					if (arrowFlipped)
					{
						xArrowEnd = xAnchor;
						xArrowStart = Math.Max((float)ChartPanel.X, xAnchor - ArrowLengthBars * 8f);
					}
					else
					{
						xArrowStart = xAnchor;
						xArrowEnd = Math.Min(chartRight, xAnchor + ArrowLengthBars * 8f);
					}
					// En modo izquierda usamos right-align al anchor (el X concreto se calcula
					// por label ya que el ancho del texto varía). En modo derecha, alineamos a
					// xAnchor + 4 como antes.
					float labelEntryX = xAnchor + 4f;
					float labelStopX  = xAnchor + 4f;
					float labelTgtX   = xAnchor + 4f;
					// Alias retrocompat para el resto del bloque que aún referencia flipLeft.
					bool flipLeft = arrowFlipped;

					// Dedupe líneas target compartidas entre hipos (misma Y)
					var drawnTargetYs = new HashSet<int>();

					// PASS 1 — TODAS las líneas, flechas y dashed arrows de TODOS los hypos.
					// Se dibujan ANTES que cualquier label para que las labels (PASS 2) queden
					// SIEMPRE encima y sean legibles.
					for (int hi = 0; hi < snap.Hypos.Count; hi++)
					{
						var h = snap.Hypos[hi];
						SharpDX.Direct2D1.Brush arrBr = pendBr;
						string st = (h.OutcomeStatus ?? "pending").ToLowerInvariant();
						if (st == "filled") arrBr = fillBr;
						else if (st == "stopped_out") arrBr = stopArrBr;
						else if (st == "triggered") arrBr = trigBr;
						else if (st == "not_triggered") arrBr = missBr;
						else arrBr = pendBr;

						float yEntry = (float)chartScale.GetYByValue(h.Entry);
						float yStop = (float)chartScale.GetYByValue(h.Stop);

						RenderTarget.DrawLine(new Vector2(xArrowStart, yEntry), new Vector2(xArrowEnd, yEntry), entryBr, 1.5f);
						RenderTarget.DrawLine(new Vector2(xArrowStart, yStop), new Vector2(xArrowEnd, yStop), stopBr, 1.5f);

						for (int ti = 0; ti < h.Targets.Count; ti++)
						{
							var t = h.Targets[ti];
							float yT = (float)chartScale.GetYByValue(t.Price);
							int yKey = (int)Math.Round(yT);
							if (!drawnTargetYs.Contains(yKey))
							{
								// Color: el ÚLTIMO target = destino macro NADRO (FinalTargetColor,
								// default RoyalBlue). Targets intermedios (T1, T2 si hay T3)
								// usan TargetColor (default DeepSkyBlue).
								// Solo línea fina, sin zona rectangular.
								bool isFinalTarget = (ti == h.Targets.Count - 1);
								var thisBr = (isFinalTarget && finalTgtBr != null) ? finalTgtBr : tgtBr;
								RenderTarget.DrawLine(new Vector2(xArrowStart, yT), new Vector2(xArrowEnd, yT), thisBr, 1f);
								drawnTargetYs.Add(yKey);
							}
						}

						if (h.Targets.Count > 0)
						{
							double lastTP = h.Targets[h.Targets.Count - 1].Price;
							float yTF = (float)chartScale.GetYByValue(lastTP);
							if (flipLeft)
								DrawArrow(xArrowEnd - 2, yEntry, xArrowStart, yTF, arrBr);
							else
								DrawArrow(xArrowStart + 2, yEntry, xArrowEnd, yTF, arrBr);

							// STOP TIGHT: si el trade fue stopped_out pero alcanzó T1, flecha secundaria.
							if (st == "stopped_out" && h.SetupReachedT1)
							{
								int lastReachedIdx = -1;
								if (h.SetupReachedT3 && h.Targets.Count >= 3) lastReachedIdx = 2;
								else if (h.SetupReachedT2 && h.Targets.Count >= 2) lastReachedIdx = 1;
								else if (h.SetupReachedT1 && h.Targets.Count >= 1) lastReachedIdx = 0;

								if (lastReachedIdx >= 0)
								{
									double tgtP = h.Targets[lastReachedIdx].Price;
									float yTgt = (float)chartScale.GetYByValue(tgtP);
									if (flipLeft)
										DrawDashedArrow(xArrowEnd - 2, yEntry, xArrowStart + 4, yTgt, fillBr);
									else
										DrawDashedArrow(xArrowStart + 2, yEntry, xArrowEnd - 4, yTgt, fillBr);
								}
							}
						}
					}

					// PASS 2a — labels de niveles. Se dibujan AHORA (después de líneas de
					// hypos en PASS 1) para que el fondo del label cubra cualquier línea
					// de entry/stop/target de hypo que coincida con el mismo precio.
					// Aplica Rank & Distill: niveles no usados se atenúan.
					if (ShowLevels && showMarkup)
					{
						foreach (var l in snap.Levels)
						{
							float y = (float)chartScale.GetYByValue(l.Price);
							bool used = isLevelUsed(l.Price);
							float prevOp = lvlBr.Opacity;
							lvlBr.Opacity = used ? 1.0f : dimOpacity;
							DrawLabelAt(xAnchor + 4, y - LabelFontSize, l.Label, labelFmt, lvlBr, obstacles, 180);
							lvlBr.Opacity = prevOp;
						}
					}

					// PASS 2 — TODAS las labels de hypos como cards negros sólidos con borde.
					// Se dibujan al final para quedar SIEMPRE encima de líneas y flechas.
					for (int hi = 0; hi < snap.Hypos.Count; hi++)
					{
						var h = snap.Hypos[hi];
						string st = (h.OutcomeStatus ?? "pending").ToLowerInvariant();
						float yEntry = (float)chartScale.GetYByValue(h.Entry);
						float yStop = (float)chartScale.GetYByValue(h.Stop);

						string setup = h.SetupType ?? "";
						if (h.SetupCompanions != null && h.SetupCompanions.Count > 0)
							setup += " / " + string.Join(" / ", h.SetupCompanions);
						string horizonTag = (h.TradingHorizon == "swing") ? " [SWING]" : "";
						string eTxt = DisplayHypoId(h.Id) + " E " + h.Entry.ToString("0.##") + " " + h.Direction + " " + setup + " " + h.Grade + horizonTag;
						// Right-align al anchor cuando labels van a la izquierda — evita que el
						// fondo del label se desborde más allá de la línea del snapshot.
						float eEstW = eTxt.Length * (LabelFontSize * 0.62f) + 8f;
						float xE = labelsLeft ? (xAnchor - eEstW - 4f) : labelEntryX;
						DrawHypoLabel(xE, yEntry - LabelFontSize, eTxt, labelFmt, entryBr, obstacles, Math.Max(220f, eEstW));

						string sTxt = "S" + h.Id + " " + h.Stop.ToString("0.##");
						float sEstW = sTxt.Length * (LabelFontSize * 0.62f) + 8f;
						float xS = labelsLeft ? (xAnchor - sEstW - 4f) : labelStopX;
						DrawHypoLabel(xS, yStop - LabelFontSize, sTxt, labelFmt, stopBr, obstacles, Math.Max(140f, sEstW));

						for (int ti = 0; ti < h.Targets.Count; ti++)
						{
							var t = h.Targets[ti];
							float yT = (float)chartScale.GetYByValue(t.Price);
							string tTxt = "T" + (ti + 1) + h.Id + " " + t.Price.ToString("0.##") + " RR" + t.RR.ToString("0.0");
							float tEstW = tTxt.Length * (LabelFontSize * 0.62f) + 8f;
							float xT = labelsLeft ? (xAnchor - tEstW - 4f) : labelTgtX;
							DrawHypoLabel(xT, yT - LabelFontSize, tTxt, labelFmt, tgtBr, obstacles, Math.Max(180f, tEstW));
						}

						// Badge "STOP TIGHT" si aplica
						if (st == "stopped_out" && h.SetupReachedT1 && h.Targets.Count > 0)
						{
							int lastReachedIdx = -1;
							if (h.SetupReachedT3 && h.Targets.Count >= 3) lastReachedIdx = 2;
							else if (h.SetupReachedT2 && h.Targets.Count >= 2) lastReachedIdx = 1;
							else if (h.SetupReachedT1 && h.Targets.Count >= 1) lastReachedIdx = 0;

							if (lastReachedIdx >= 0)
							{
								var tightRect = new SharpDX.RectangleF(
									labelsLeft ? xAnchor - 360f : xAnchor + 240f, yEntry - LabelFontSize - 2,
									120, LabelFontSize + 4);
								if (_labelBgBrushCache != null)
								{
									_labelBgBrushCache.Opacity = 1f;
									RenderTarget.FillRectangle(tightRect, _labelBgBrushCache);
								}
								RenderTarget.DrawText("STOP TIGHT T" + (lastReachedIdx + 1),
									labelFmt, tightRect, fillBr);
							}
						}
					}
				}

				if (ShowAnalysisText && showBriefing && snap == activeSnap && !string.IsNullOrEmpty(snap.AnalysisText))
				{
					float panelLeft = (float)ChartPanel.X;
					float panelTop = (float)ChartPanel.Y + TopPadding;
					float boxRight = Math.Min(xAnchor - 2, panelLeft + AnalysisAreaWidth);
					float boxWidth = boxRight - panelLeft;
					if (boxWidth > 80)
					{
						float boxHeight = (float)ChartPanel.H - 8;
						// v: guardar rect para hit-test del mouse wheel
						_briefingRect = new SharpDX.RectangleF(panelLeft, panelTop, boxWidth, boxHeight);
						_hasBriefingRect = true;

						if (_labelBgBrushCache != null)
						{
							_labelBgBrushCache.Opacity = 0.78f;
							RenderTarget.FillRectangle(
								new SharpDX.RectangleF(panelLeft, panelTop, boxWidth, boxHeight),
								_labelBgBrushCache);
							_labelBgBrushCache.Opacity = 1f;
						}
						var atRect = new SharpDX.RectangleF(panelLeft + 6, panelTop + 4, boxWidth - 12, (float)ChartPanel.H - 12);

						// v: Concatenar bloque de HIPOS al final del analysis_text
						string fullText = snap.AnalysisText;
						if (snap.Hypos != null && snap.Hypos.Count > 0)
						{
							var sb = new System.Text.StringBuilder();
							sb.Append(fullText);
							sb.Append("\n\n=== HIPOS ===");
							for (int hi = 0; hi < snap.Hypos.Count; hi++)
							{
								var h = snap.Hypos[hi];
								string setup = h.SetupType ?? "";
								if (h.SetupCompanions != null && h.SetupCompanions.Count > 0)
									setup += " / " + string.Join(" / ", h.SetupCompanions);
								string dir = (h.Direction ?? "").ToUpperInvariant();
								string hTag = (h.TradingHorizon == "swing") ? " [SWING]" : "";
								double risk = Math.Abs(h.Entry - h.Stop);
								sb.Append("\n[" + DisplayHypoId(h.Id) + "] " + setup + " " + dir + " grade " + h.Grade + hTag);
								sb.Append("\n  E " + h.Entry.ToString("0.##") + "  S " + h.Stop.ToString("0.##") + "  (risk " + risk.ToString("0.##") + " pts)");
								for (int ti = 0; ti < h.Targets.Count; ti++)
								{
									var t = h.Targets[ti];
									sb.Append("\n  T" + (ti + 1) + " " + t.Price.ToString("0.##") + "  RR " + t.RR.ToString("0.0"));
								}
							}
							fullText = sb.ToString();
						}

						// v: aplicar scroll offset (saltar N líneas lógicas desde arriba)
						if (_scrollOffsetLines > 0 && !string.IsNullOrEmpty(fullText))
						{
							string[] lines = fullText.Split('\n');
							int skip = Math.Min(_scrollOffsetLines, Math.Max(0, lines.Length - 3));
							if (skip > 0) fullText = string.Join("\n", lines.Skip(skip));
						}

						RenderTarget.DrawText(fullText, notesFmt, atRect, notesBr);
					}
				}

				if (ShowNotes && showBriefing && snap == activeSnap)
				{
					string notes = "NADRO " + snap.Id + " | " + snap.Timestamp.ToString("HH:mm") + " | " + snap.Regime + "\n" +
						(string.IsNullOrEmpty(snap.Summary) ? "" : snap.Summary);
					// Caja de notas en esquina superior izquierda fija del panel (no junto al anchor)
					var noteRect = new SharpDX.RectangleF((float)ChartPanel.X + 8, (float)ChartPanel.Y + TopPadding - 66, 640, 60);
					RenderTarget.DrawText(notes, notesFmt, noteRect, notesBr);
				}
			}

			// PERF: NO dispose — brushes/TextFormats están en _dxBrushCache + _cachedLabelFmt/_cachedNotesFmt.
			// Se reutilizan entre frames y se liberan en OnRenderTargetChanged → DisposeBrushCache.
			// Resetear opacity si MarkupOnly mode la modificó (los brushes son compartidos).
			if (RenderLayer == RelativeNadroRenderLayer.MarkupOnly && MarkupOpacity < 100)
			{
				if (aPlus != null) aPlus.Opacity = 1f;
				if (aBrush != null) aBrush.Opacity = 1f;
				if (bBrush != null) bBrush.Opacity = 1f;
				if (lvlBr != null) lvlBr.Opacity = 1f;
				if (entryBr != null) entryBr.Opacity = 1f;
				if (stopBr != null) stopBr.Opacity = 1f;
				if (tgtBr != null) tgtBr.Opacity = 1f;
				if (anchorBr != null) anchorBr.Opacity = 1f;
				if (notesBr != null) notesBr.Opacity = 1f;
				if (pendBr != null) pendBr.Opacity = 1f;
				if (trigBr != null) trigBr.Opacity = 1f;
				if (fillBr != null) fillBr.Opacity = 1f;
				if (stopArrBr != null) stopArrBr.Opacity = 1f;
				if (missBr != null) missBr.Opacity = 1f;
				foreach (var b in hypoBrushes) if (b != null) b.Opacity = 1f;
			}
		}

		private SharpDX.Direct2D1.Brush _labelBgBrushCache;

		private void DrawLabelAt(float x, float y, string text, TextFormat fmt,
			SharpDX.Direct2D1.Brush brush, List<SharpDX.RectangleF> obstacles, float approxWidth)
		{
			int pad = LabelFontSize / 2 + 3;
			var rect = new SharpDX.RectangleF(x, y - pad, approxWidth, LabelFontSize + pad * 2);

			for (int attempt = 0; attempt < 20; attempt++)
			{
				bool collision = false;
				foreach (var obs in obstacles)
				{
					if (rect.Bottom >= obs.Top && rect.Top <= obs.Bottom &&
					    rect.Right > obs.Left && rect.Left < obs.Right)
					{
						collision = true;
						break;
					}
				}
				if (!collision) break;
				x += approxWidth + 8;
				rect = new SharpDX.RectangleF(x, y - pad, approxWidth, LabelFontSize + pad * 2);
			}

			// Fondo sólido detrás del texto para ocultar líneas bajo el label.
			// Opacidad 1.0 (full black) y rect ampliado para cubrir bien la línea
			// dashed que puede pasar por el centro del precio (priceY).
			if (_labelBgBrushCache != null && !string.IsNullOrEmpty(text))
			{
				float estW = text.Length * (LabelFontSize * 0.58f) + 6f;
				_labelBgBrushCache.Opacity = 1f;
				// Ampliamos el rect: 4px más arriba y 6px más abajo para cubrir descenders,
				// la línea del nivel y posibles desplazamientos de baseline del texto.
				var bgRect = new SharpDX.RectangleF(x - 2, y - 4, estW, LabelFontSize + 12);
				RenderTarget.FillRectangle(bgRect, _labelBgBrushCache);
			}

			var drawRect = new SharpDX.RectangleF(x, y, approxWidth, LabelFontSize * 2);
			RenderTarget.DrawText(text, fmt, drawRect, brush);
			obstacles.Add(rect);
		}

		// Label especial para HYPOS: card negro sólido + borde, font ligeramente mayor.
		// Pensado para llamarse en una segunda pasada (DESPUÉS de dibujar todas las líneas
		// y flechas) para que el card quede ENCIMA y sea legible.
		private void DrawHypoLabel(float x, float y, string text, TextFormat fmt,
			SharpDX.Direct2D1.Brush brush, List<SharpDX.RectangleF> obstacles, float approxWidth)
		{
			int pad = LabelFontSize / 2 + 4;
			var rect = new SharpDX.RectangleF(x, y - pad, approxWidth, LabelFontSize + pad * 2);

			for (int attempt = 0; attempt < 20; attempt++)
			{
				bool collision = false;
				foreach (var obs in obstacles)
				{
					if (rect.Bottom >= obs.Top && rect.Top <= obs.Bottom &&
					    rect.Right > obs.Left && rect.Left < obs.Right)
					{
						collision = true;
						break;
					}
				}
				if (!collision) break;
				x += approxWidth + 8;
				rect = new SharpDX.RectangleF(x, y - pad, approxWidth, LabelFontSize + pad * 2);
			}

			// Fondo negro SÓLIDO (sin borde) para que las labels se lean encima de líneas/zonas
			if (_labelBgBrushCache != null && !string.IsNullOrEmpty(text))
			{
				float estW = text.Length * (LabelFontSize * 0.62f) + 8f;
				float bgH = LabelFontSize + 6f;
				var bgRect = new SharpDX.RectangleF(x - 3, y - 1, estW, bgH);

				_labelBgBrushCache.Opacity = 1f;
				RenderTarget.FillRectangle(bgRect, _labelBgBrushCache);
			}

			var drawRect = new SharpDX.RectangleF(x, y, approxWidth, LabelFontSize * 2);
			RenderTarget.DrawText(text, fmt, drawRect, brush);
			obstacles.Add(rect);
		}

		private void DrawDashedHorizontal(float xStart, float xEnd, float y, SharpDX.Direct2D1.Brush brush)
		{
			float dash = 6f, gap = 4f;
			float x = xStart;
			while (x < xEnd)
			{
				float x2 = Math.Min(xEnd, x + dash);
				RenderTarget.DrawLine(new Vector2(x, y), new Vector2(x2, y), brush, 1f);
				x = x2 + gap;
			}
		}

		private void DrawDashedVertical(float x, float yTop, float yBot, SharpDX.Direct2D1.Brush brush)
		{
			float dash = 6f, gap = 4f;
			float y = yTop;
			while (y < yBot)
			{
				float y2 = Math.Min(yBot, y + dash);
				RenderTarget.DrawLine(new Vector2(x, y), new Vector2(x, y2), brush, 1f);
				y = y2 + gap;
			}
		}

		private void DrawArrow(float x0, float y0, float x1, float y1, SharpDX.Direct2D1.Brush brush)
		{
			RenderTarget.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), brush, 2f);

			float dx = x1 - x0, dy = y1 - y0;
			float len = (float)Math.Sqrt(dx * dx + dy * dy);
			if (len < 0.001f) return;
			float ux = dx / len, uy = dy / len;
			float headLen = 12f, headHalfW = 6f;
			float bx = x1 - ux * headLen;
			float by = y1 - uy * headLen;
			float lx = bx + (-uy) * headHalfW;
			float ly = by + (ux) * headHalfW;
			float rx = bx - (-uy) * headHalfW;
			float ry = by - (ux) * headHalfW;

			RenderTarget.DrawLine(new Vector2(x1, y1), new Vector2(lx, ly), brush, 2f);
			RenderTarget.DrawLine(new Vector2(x1, y1), new Vector2(rx, ry), brush, 2f);
			RenderTarget.DrawLine(new Vector2(lx, ly), new Vector2(rx, ry), brush, 2f);
		}

		private void DrawDashedArrow(float x0, float y0, float x1, float y1, SharpDX.Direct2D1.Brush brush)
		{
			// Cuerpo punteado
			float dx = x1 - x0, dy = y1 - y0;
			float len = (float)Math.Sqrt(dx * dx + dy * dy);
			if (len < 0.001f) return;
			float ux = dx / len, uy = dy / len;
			float dash = 7f, gap = 5f;
			float walked = 0f;
			while (walked < len)
			{
				float seg = Math.Min(dash, len - walked);
				float sx = x0 + ux * walked;
				float sy = y0 + uy * walked;
				float ex = x0 + ux * (walked + seg);
				float ey = y0 + uy * (walked + seg);
				RenderTarget.DrawLine(new Vector2(sx, sy), new Vector2(ex, ey), brush, 2f);
				walked += seg + gap;
			}

			// Cabeza (triángulo al final)
			float headLen = 11f, headHalfW = 5.5f;
			float bx = x1 - ux * headLen;
			float by = y1 - uy * headLen;
			float lx = bx + (-uy) * headHalfW;
			float ly = by + (ux) * headHalfW;
			float rx = bx - (-uy) * headHalfW;
			float ry = by - (ux) * headHalfW;
			RenderTarget.DrawLine(new Vector2(x1, y1), new Vector2(lx, ly), brush, 2f);
			RenderTarget.DrawLine(new Vector2(x1, y1), new Vector2(rx, ry), brush, 2f);
			RenderTarget.DrawLine(new Vector2(lx, ly), new Vector2(rx, ry), brush, 2f);
		}

				private static string DisplayHypoId(string id)
		{
			if (string.IsNullOrEmpty(id)) return "HYPO";
			if (id.Length >= 2 && (id[0] == 'h' || id[0] == 'H') && char.IsDigit(id[1]))
				return "HYPO " + id.Substring(1);
			return id.ToUpperInvariant();
		}

#region Properties

		[Range(1, 3600)]
		[Display(Name = "Refrescar (seg)", GroupName = "01. General", Order = 0)]
		public int RefreshSeconds { get; set; }

		[Range(0, 30)]
		[Display(Name = "Dias hacia atras", GroupName = "01. General", Order = 1)]
		public int DaysBack { get; set; }

		[Display(Name = "Mostrar ancla temporal", GroupName = "02. Visibilidad", Order = 0)]
		public bool ShowVerticalAnchor { get; set; }

		[Display(Name = "Mostrar confluencias", GroupName = "02. Visibilidad", Order = 1)]
		public bool ShowConfluences { get; set; }

		[Display(Name = "Mostrar hipotesis", GroupName = "02. Visibilidad", Order = 2)]
		public bool ShowHypos { get; set; }

		[Display(Name = "Mostrar niveles", GroupName = "02. Visibilidad", Order = 3)]
		public bool ShowLevels { get; set; }

		[Display(Name = "Mostrar notas", GroupName = "02. Visibilidad", Order = 4)]
		public bool ShowNotes { get; set; }

		[Range(0, 100)]
		[Display(Name = "Opacidad confluencia %", GroupName = "03. Estilo", Order = 0)]
		public int ConfluenceOpacity { get; set; }

		[Range(0, 95)]
		[Display(Name = "Opacar pasado del snapshot %", Description = "0 = no opaca; 50 = oscurece 50% el area a la izquierda del anchor", GroupName = "03. Estilo", Order = 4)]
		public int DimPastOpacity { get; set; }

		[Range(7, 28)]
		[Display(Name = "Tamano label", GroupName = "03. Estilo", Order = 1)]
		public int LabelFontSize { get; set; }

		[Range(7, 28)]
		[Display(Name = "Tamano notas", GroupName = "03. Estilo", Order = 2)]
		public int NotesFontSize { get; set; }

		[Range(5, 500)]
		[Display(Name = "Largo flecha (px)", GroupName = "03. Estilo", Order = 3)]
		public int ArrowLengthBars { get; set; }

		[XmlIgnore]
		[Display(Name = "Confluencia A+", GroupName = "04. Colores", Order = 0)]
		public Brush ConfluenceAPlusColor { get; set; }
		[Browsable(false)]
		public string ConfluenceAPlusColorSerializable
		{
			get { return Serialize.BrushToString(ConfluenceAPlusColor); }
			set { ConfluenceAPlusColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Confluencia A", GroupName = "04. Colores", Order = 1)]
		public Brush ConfluenceAColor { get; set; }
		[Browsable(false)]
		public string ConfluenceAColorSerializable
		{
			get { return Serialize.BrushToString(ConfluenceAColor); }
			set { ConfluenceAColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Confluencia B", GroupName = "04. Colores", Order = 2)]
		public Brush ConfluenceBColor { get; set; }
		[Browsable(false)]
		public string ConfluenceBColorSerializable
		{
			get { return Serialize.BrushToString(ConfluenceBColor); }
			set { ConfluenceBColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Line in the Sand", Description = "Color de la zona LIS (pivote direccional del día)", GroupName = "04. Colores", Order = 5)]
		public Brush LineInTheSandColor { get; set; }
		[Browsable(false)]
		public string LineInTheSandColorSerializable
		{
			get { return Serialize.BrushToString(LineInTheSandColor); }
			set { LineInTheSandColor = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[Display(Name = "Opacidad LIS %", Description = "Opacidad de la zona Line in the Sand (default 50)", GroupName = "03. Estilo", Order = 1)]
		public int LineInTheSandOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Opacidad niveles no usados %", Description = "Rank & Distill (NADRO §7): niveles que no aparecen como entry/stop/target en ninguna hipo se atenúan a este % (default 30). Reduce ruido visual.", GroupName = "03. Estilo", Order = 4)]
		public int LevelDimOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "Nivel", GroupName = "04. Colores", Order = 3)]
		public Brush LevelColor { get; set; }
		[Browsable(false)]
		public string LevelColorSerializable
		{
			get { return Serialize.BrushToString(LevelColor); }
			set { LevelColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Entry", GroupName = "04. Colores", Order = 4)]
		public Brush EntryColor { get; set; }
		[Browsable(false)]
		public string EntryColorSerializable
		{
			get { return Serialize.BrushToString(EntryColor); }
			set { EntryColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Stop", GroupName = "04. Colores", Order = 5)]
		public Brush StopColor { get; set; }
		[Browsable(false)]
		public string StopColorSerializable
		{
			get { return Serialize.BrushToString(StopColor); }
			set { StopColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Target intermedio (T1, T2)", Description = "Color de los targets intermedios (NADRO: destinos por los que el precio puede pasar)", GroupName = "04. Colores", Order = 6)]
		public Brush TargetColor { get; set; }
		[Browsable(false)]
		public string TargetColorSerializable
		{
			get { return Serialize.BrushToString(TargetColor); }
			set { TargetColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Target final (último)", Description = "Color del último target = destino macro NADRO (zona neutral azul donde el operador espera y observa)", GroupName = "04. Colores", Order = 7)]
		public Brush FinalTargetColor { get; set; }
		[Browsable(false)]
		public string FinalTargetColorSerializable
		{
			get { return Serialize.BrushToString(FinalTargetColor); }
			set { FinalTargetColor = Serialize.StringToBrush(value); }
		}

		[Range(0.0, 1.0)]
		[Display(Name = "Ancho zona target % precio", Description = "Anchura de la zona neutral azul como % del precio target (default 0.05 = 0.05%). 0 = solo línea sin zona.", GroupName = "03. Estilo", Order = 2)]
		public double TargetZonePercent { get; set; }

		[Range(1, 100)]
		[Display(Name = "Opacidad zona target %", Description = "Opacidad de la zona azul de destino (default 50)", GroupName = "03. Estilo", Order = 3)]
		public int TargetZoneOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "Ancla temporal", GroupName = "04. Colores", Order = 7)]
		public Brush AnchorColor { get; set; }
		[Browsable(false)]
		public string AnchorColorSerializable
		{
			get { return Serialize.BrushToString(AnchorColor); }
			set { AnchorColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Flecha: pendiente", GroupName = "05. Outcomes", Order = 0)]
		public Brush ArrowPendingColor { get; set; }
		[Browsable(false)]
		public string ArrowPendingColorSerializable
		{
			get { return Serialize.BrushToString(ArrowPendingColor); }
			set { ArrowPendingColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Flecha: triggered", GroupName = "05. Outcomes", Order = 1)]
		public Brush ArrowTriggeredColor { get; set; }
		[Browsable(false)]
		public string ArrowTriggeredColorSerializable
		{
			get { return Serialize.BrushToString(ArrowTriggeredColor); }
			set { ArrowTriggeredColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Flecha: filled", GroupName = "05. Outcomes", Order = 2)]
		public Brush ArrowFilledColor { get; set; }
		[Browsable(false)]
		public string ArrowFilledColorSerializable
		{
			get { return Serialize.BrushToString(ArrowFilledColor); }
			set { ArrowFilledColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Flecha: stop_out", GroupName = "05. Outcomes", Order = 3)]
		public Brush ArrowStoppedColor { get; set; }
		[Browsable(false)]
		public string ArrowStoppedColorSerializable
		{
			get { return Serialize.BrushToString(ArrowStoppedColor); }
			set { ArrowStoppedColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Flecha: not_triggered", GroupName = "05. Outcomes", Order = 4)]
		public Brush ArrowMissedColor { get; set; }
		[Browsable(false)]
		public string ArrowMissedColorSerializable
		{
			get { return Serialize.BrushToString(ArrowMissedColor); }
			set { ArrowMissedColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Texto notas", GroupName = "05. Outcomes", Order = 5)]
		public Brush NotesColor { get; set; }
		[Browsable(false)]
		public string NotesColorSerializable
		{
			get { return Serialize.BrushToString(NotesColor); }
			set { NotesColor = Serialize.StringToBrush(value); }
		}


		[Display(Name = "Mostrar texto analisis", GroupName = "02. Visibilidad", Order = 5)]
		public bool ShowAnalysisText { get; set; }

		[Range(100, 900)]
		[Display(Name = "Ancho panel analisis (px)", GroupName = "03. Estilo", Order = 4)]
		public int AnalysisAreaWidth { get; set; }

		[Range(20, 300)]
		[Display(Name = "Padding superior panel (px)", GroupName = "03. Estilo", Order = 5)]
		public int TopPadding { get; set; }

		[Display(Name = "Modo de render", GroupName = "03. Estilo", Order = 6, Description = "Full = todo; BriefingOnly = solo panel y notas; MarkupOnly = solo lineas/flechas (cargar PRIMERO para que quede atras)")]
		public RelativeNadroRenderLayer RenderLayer { get; set; }

		[Range(20, 100)]
		[Display(Name = "Opacidad markup (%)", GroupName = "03. Estilo", Order = 7, Description = "Solo aplica en modo MarkupOnly. 50% recomendado para ver velas a traves.")]
		public int MarkupOpacity { get; set; }

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeNadroMarkup[] cacheRelativeNadroMarkup;
		public RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup()
		{
			return RelativeNadroMarkup(Input);
		}

		public RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup(ISeries<double> input)
		{
			if (cacheRelativeNadroMarkup != null)
				for (int idx = 0; idx < cacheRelativeNadroMarkup.Length; idx++)
					if (cacheRelativeNadroMarkup[idx] != null &&  cacheRelativeNadroMarkup[idx].EqualsInput(input))
						return cacheRelativeNadroMarkup[idx];
			return CacheIndicator<RelativeIndicators.RelativeNadroMarkup>(new RelativeIndicators.RelativeNadroMarkup(), input, ref cacheRelativeNadroMarkup);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup()
		{
			return indicator.RelativeNadroMarkup(Input);
		}

		public Indicators.RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup(ISeries<double> input )
		{
			return indicator.RelativeNadroMarkup(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup()
		{
			return indicator.RelativeNadroMarkup(Input);
		}

		public Indicators.RelativeIndicators.RelativeNadroMarkup RelativeNadroMarkup(ISeries<double> input )
		{
			return indicator.RelativeNadroMarkup(input);
		}
	}
}

#endregion
