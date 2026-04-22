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
	/// RelativeNadroMarkup v0.1.1 (POC) — plasma en el chart el análisis NADRO hecho en MCP.
	/// Lee archivos JSON de Docs/Nadro/markups/{INSTRUMENT}_YYYY-MM-DD.json y pinta:
	/// confluencias, niveles, entry/stop/targets, flechas.
	/// </summary>
	public class RelativeNadroMarkup : Indicator
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
				DaysBack = 1;
				ShowVerticalAnchor = true;
				ShowConfluences = true;
				ShowHypos = true;
				ShowLevels = true;
				ShowNotes = true;

				ConfluenceOpacity = 22;
				LabelFontSize = 11;
				NotesFontSize = 10;
				ArrowLengthBars = 30;
				ShowAnalysisText = true;
				AnalysisAreaWidth = 380;

				ConfluenceAPlusColor = Brushes.OrangeRed;
				ConfluenceAColor = Brushes.Orange;
				ConfluenceBColor = Brushes.Gold;
				LevelColor = Brushes.LightGray;
				EntryColor = Brushes.White;
				StopColor = Brushes.Crimson;
				TargetColor = Brushes.DeepSkyBlue;
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
			}
		}

		protected override void OnBarUpdate()
		{
			if (State != State.Realtime && CurrentBar < Bars.Count - 2) return;

			string currentInstrument = Instrument.MasterInstrument.Name;
			if (currentInstrument != _lastInstrument)
			{
				_lastRead = DateTime.MinValue;
				_lastInstrument = currentInstrument;
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

				try
				{
					string raw = File.ReadAllText(path);
					var root = ser.DeserializeObject(raw) as Dictionary<string, object>;
					if (root == null) continue;

					object snaps;
					if (!root.TryGetValue("snapshots", out snaps)) continue;
					var snapArr = snaps as object[];
					if (snapArr == null) continue;

					foreach (var s in snapArr)
					{
						var snapDict = s as Dictionary<string, object>;
						if (snapDict == null) continue;
						var parsed = ParseSnapshot(snapDict);
						if (parsed != null) _snapshots.Add(parsed);
					}
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

			float chartRight = (float)ChartPanel.X + (float)ChartPanel.W;

			var aPlus = ConfluenceAPlusColor.ToDxBrush(RenderTarget);
			var aBrush = ConfluenceAColor.ToDxBrush(RenderTarget);
			var bBrush = ConfluenceBColor.ToDxBrush(RenderTarget);
			var lvlBr = LevelColor.ToDxBrush(RenderTarget);
			var entryBr = EntryColor.ToDxBrush(RenderTarget);
			var stopBr = StopColor.ToDxBrush(RenderTarget);
			var tgtBr = TargetColor.ToDxBrush(RenderTarget);
			var anchorBr = AnchorColor.ToDxBrush(RenderTarget);
			var notesBr = NotesColor.ToDxBrush(RenderTarget);
			var pendBr = ArrowPendingColor.ToDxBrush(RenderTarget);
			var trigBr = ArrowTriggeredColor.ToDxBrush(RenderTarget);
			var fillBr = ArrowFilledColor.ToDxBrush(RenderTarget);
			var stopArrBr = ArrowStoppedColor.ToDxBrush(RenderTarget);
			var missBr = ArrowMissedColor.ToDxBrush(RenderTarget);
			_labelBgBrushCache = System.Windows.Media.Brushes.Black.ToDxBrush(RenderTarget);

			// Palette por hipo (h1, h2, ...) para distinguir labels visualmente
			var hypoBrushes = new List<SharpDX.Direct2D1.Brush>();
			foreach (var b in _hypoPalette) hypoBrushes.Add(b.ToDxBrush(RenderTarget));

			var labelFmt = new SimpleFont("Arial", LabelFontSize).ToDirectWriteTextFormat();
			labelFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			labelFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

			var notesFmt = new SimpleFont("Arial", NotesFontSize).ToDirectWriteTextFormat();
			notesFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			notesFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;

			foreach (var snap in _snapshots)
			{
				float xAnchor = (float)chartControl.GetXByTime(snap.Timestamp);
				if (xAnchor < 0) continue;

				// Anti-colisión de labels (reuso patrón RelativeVwapLevels)
				var obstacles = new List<SharpDX.RectangleF>();

				if (ShowVerticalAnchor)
				{
					DrawDashedVertical(xAnchor, (float)ChartPanel.Y,
						(float)ChartPanel.Y + (float)ChartPanel.H, anchorBr);
				}

				if (ShowConfluences)
				{
					foreach (var c in snap.Confluences)
					{
						SharpDX.Direct2D1.Brush br = bBrush;
						if (!string.IsNullOrEmpty(c.Grade))
						{
							if (c.Grade.StartsWith("A+")) br = aPlus;
							else if (c.Grade.StartsWith("A")) br = aBrush;
							else br = bBrush;
						}
						br.Opacity = (float)(ConfluenceOpacity / 100.0);

						float yTop = (float)chartScale.GetYByValue(c.PriceMax);
						float yBot = (float)chartScale.GetYByValue(c.PriceMin);
						if (yBot - yTop < 1) yBot = yTop + 1;

						float xStart = xAnchor;
						float xEnd = Math.Min(chartRight, xAnchor + ArrowLengthBars * 10f);
						if (xEnd <= xStart) xEnd = xStart + 1;

						RenderTarget.FillRectangle(
							new SharpDX.RectangleF(xStart, yTop, xEnd - xStart, yBot - yTop), br);

						br.Opacity = 1f;
						string lblTxt = (c.Grade ?? "") + " " + (c.Label ?? "") + " [" + c.Members.Count + "]";
						DrawLabelAt(xStart + 4, yTop - LabelFontSize - 2, lblTxt, labelFmt, notesBr, obstacles, 360);
					}
				}

				if (ShowLevels)
				{
					foreach (var l in snap.Levels)
					{
						float y = (float)chartScale.GetYByValue(l.Price);
						DrawDashedHorizontal(xAnchor, chartRight, y, lvlBr);
						DrawLabelAt(xAnchor + 4, y - LabelFontSize, l.Label, labelFmt, lvlBr, obstacles, 180);
					}
				}

				if (ShowHypos)
				{
					float xArrowEnd = Math.Min(chartRight, xAnchor + ArrowLengthBars * 8f);

					// Dedupe líneas target compartidas entre hipos (misma Y)
					var drawnTargetYs = new HashSet<int>();

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

						RenderTarget.DrawLine(new Vector2(xAnchor, yEntry), new Vector2(xArrowEnd, yEntry), entryBr, 1.5f);
						string setup = h.SetupType ?? "";
						if (h.SetupCompanions != null && h.SetupCompanions.Count > 0)
							setup += " / " + string.Join(" / ", h.SetupCompanions);
						string horizonTag = (h.TradingHorizon == "swing") ? " [SWING]" : "";
						string eTxt = DisplayHypoId(h.Id) + " E " + h.Entry.ToString("0.##") + " " + h.Direction + " " + setup + " " + h.Grade + horizonTag;
						DrawLabelAt(xAnchor + 4, yEntry - LabelFontSize, eTxt, labelFmt, notesBr, obstacles, 220);

						RenderTarget.DrawLine(new Vector2(xAnchor, yStop), new Vector2(xArrowEnd, yStop), stopBr, 1.5f);
						string sTxt = "S" + h.Id + " " + h.Stop.ToString("0.##");
						DrawLabelAt(xAnchor + 4, yStop - LabelFontSize, sTxt, labelFmt, notesBr, obstacles, 140);

						for (int ti = 0; ti < h.Targets.Count; ti++)
						{
							var t = h.Targets[ti];
							float yT = (float)chartScale.GetYByValue(t.Price);
							int yKey = (int)Math.Round(yT);
							if (!drawnTargetYs.Contains(yKey))
							{
								RenderTarget.DrawLine(new Vector2(xAnchor, yT), new Vector2(xArrowEnd, yT), tgtBr, 1f);
								drawnTargetYs.Add(yKey);
							}
							string tTxt = "T" + (ti + 1) + h.Id + " " + t.Price.ToString("0.##") + " RR" + t.RR.ToString("0.0");
							DrawLabelAt(xAnchor + 4, yT - LabelFontSize, tTxt, labelFmt, notesBr, obstacles, 180);
						}

						if (h.Targets.Count > 0)
						{
							double lastTP = h.Targets[h.Targets.Count - 1].Price;
							float yTF = (float)chartScale.GetYByValue(lastTP);
							DrawArrow(xAnchor + 2, yEntry, xArrowEnd, yTF, arrBr);

							// STOP TIGHT: si el trade fue stopped_out pero el setup alcanzó al menos T1,
							// dibujar flecha secundaria PUNTEADA verde hacia el último target alcanzado.
							// Revela visualmente que el setup iba bien pero el stop fue muy ajustado.
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
									DrawDashedArrow(xAnchor + 2, yEntry, xArrowEnd - 4, yTgt, fillBr);

									// Badge textual "STOP TIGHT" cerca del entry
									var tightRect = new SharpDX.RectangleF(
										xAnchor + 240, yEntry - LabelFontSize - 2,
										120, LabelFontSize + 4);
									if (_labelBgBrushCache != null)
									{
										_labelBgBrushCache.Opacity = 0.82f;
										RenderTarget.FillRectangle(tightRect, _labelBgBrushCache);
										_labelBgBrushCache.Opacity = 1f;
									}
									RenderTarget.DrawText("STOP TIGHT T" + (lastReachedIdx + 1),
										labelFmt, tightRect, fillBr);
								}
							}
						}
					}
				}

				if (ShowAnalysisText && !string.IsNullOrEmpty(snap.AnalysisText))
				{
					float panelLeft = (float)ChartPanel.X;
					float panelTop = (float)ChartPanel.Y + 44;
					float boxRight = Math.Min(xAnchor - 2, panelLeft + AnalysisAreaWidth);
					float boxWidth = boxRight - panelLeft;
					if (boxWidth > 80)
					{
						if (_labelBgBrushCache != null)
						{
							_labelBgBrushCache.Opacity = 0.78f;
							RenderTarget.FillRectangle(
								new SharpDX.RectangleF(panelLeft, panelTop, boxWidth, (float)ChartPanel.H - 8),
								_labelBgBrushCache);
							_labelBgBrushCache.Opacity = 1f;
						}
						var atRect = new SharpDX.RectangleF(panelLeft + 6, panelTop + 4, boxWidth - 12, (float)ChartPanel.H - 12);
						RenderTarget.DrawText(snap.AnalysisText, notesFmt, atRect, notesBr);
					}
				}

				if (ShowNotes)
				{
					string notes = "NADRO " + snap.Id + " | " + snap.Timestamp.ToString("HH:mm") + " | " + snap.Regime + "\n" +
						(string.IsNullOrEmpty(snap.Summary) ? "" : snap.Summary);
					// Caja de notas en esquina superior izquierda fija del panel (no junto al anchor)
					var noteRect = new SharpDX.RectangleF((float)ChartPanel.X + 8, (float)ChartPanel.Y + 18, 640, 24);
					RenderTarget.DrawText(notes, notesFmt, noteRect, notesBr);
				}
			}

			aPlus.Dispose(); aBrush.Dispose(); bBrush.Dispose();
			lvlBr.Dispose(); entryBr.Dispose(); stopBr.Dispose(); tgtBr.Dispose();
			anchorBr.Dispose(); notesBr.Dispose();
			pendBr.Dispose(); trigBr.Dispose(); fillBr.Dispose(); stopArrBr.Dispose(); missBr.Dispose();
			foreach (var b in hypoBrushes) b.Dispose();
			if (_labelBgBrushCache != null) { _labelBgBrushCache.Dispose(); _labelBgBrushCache = null; }
			labelFmt.Dispose(); notesFmt.Dispose();
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

			// Fondo sólido detrás del texto para ocultar líneas bajo el label
			if (_labelBgBrushCache != null && !string.IsNullOrEmpty(text))
			{
				float estW = text.Length * (LabelFontSize * 0.58f) + 6f;
				_labelBgBrushCache.Opacity = 0.8f;
				var bgRect = new SharpDX.RectangleF(x - 2, y, estW, LabelFontSize + 4);
				RenderTarget.FillRectangle(bgRect, _labelBgBrushCache);
				_labelBgBrushCache.Opacity = 1f;
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
		[Display(Name = "Target", GroupName = "04. Colores", Order = 6)]
		public Brush TargetColor { get; set; }
		[Browsable(false)]
		public string TargetColorSerializable
		{
			get { return Serialize.BrushToString(TargetColor); }
			set { TargetColor = Serialize.StringToBrush(value); }
		}

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
