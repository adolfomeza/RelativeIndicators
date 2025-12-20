/*
*	Measure Drawing Tool made with ♡ by beo
* 	Last edit 02/04/2021
*	https://priceactiontradingsystem.com/link-to-forum/topic/pats-toolbar-custom-drawing-tools-why-this-is-better/
*/

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;

#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Measure IDrawingTool.
	/// </summary>
	public class Measure : DrawingTool
	{
		private bool							isLastMeasureUp;
		private	int								areaOpacity					= 5;
		private	Brush							areaBrushUp					= Brushes.Green;
		private	Brush							areaBrushDown				= Brushes.Red;
		private	readonly DeviceBrush			areaBrushDevice				= new DeviceBrush();
		private const int 						cursorSensitivity 			= 15;
		private	ChartAnchor						editingAnchor;
		private bool							isTextCreated;
		private const float						textMargin					= 3f;
		private SharpDX.DirectWrite.TextFormat	textFormat;
		private SharpDX.DirectWrite.TextLayout	textLayout;
		private Brush							textBrush;
		private	readonly DeviceBrush			textDeviceBrush				= new DeviceBrush();
		private	readonly DeviceBrush			textBackgroundDeviceBrush	= new DeviceBrush();
		private string							yValueString1;
		private string							yValueString2;
		private string 							timeText;
		private ValueUnit						yValueDisplayUnit1			= ValueUnit.Price;
		private ValueUnit						yValueDisplayUnit2			= ValueUnit.Currency;

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor, TextAnchor }; } }
		
		[Display(Order = 1)]
		public ChartAnchor		StartAnchor		{ get; set; }
		[Display(Order = 2)]
		public ChartAnchor		EndAnchor		{ get; set;	}
		[Display(Order = 3)]
		public ChartAnchor		TextAnchor		{ get; set; }

		[XmlIgnore] 
		[Display(ResourceType = typeof(Custom.Resource), Name = "Color Area Up", GroupName = "NinjaScriptGeneral", Order = 1)]
		public Brush AreaBrushUp
		{
			get { return areaBrushUp; }
			set { areaBrushUp = value; if (areaBrushUp != null) { if (areaBrushUp.IsFrozen) areaBrushUp = areaBrushUp.Clone(); areaBrushUp.Freeze(); } areaBrushDevice.Brush = null; }
		}

		[Browsable(false)]
		public string AreaBrushUpSerialize
		{
			get { return Serialize.BrushToString(AreaBrushUp); }
			set { AreaBrushUp = Serialize.StringToBrush(value); }
		}

		[XmlIgnore] 
		[Display(ResourceType = typeof(Custom.Resource), Name = "Color Area Down", GroupName = "NinjaScriptGeneral", Order = 2)]
		public Brush AreaBrushDown
		{
			get { return areaBrushDown; }
			set { areaBrushDown = value; if (areaBrushDown != null) { if (areaBrushDown.IsFrozen) areaBrushDown = areaBrushDown.Clone(); areaBrushDown.Freeze(); } areaBrushDevice.Brush = null; }
		}

		[Browsable(false)]
		public string AreaBrushDownSerialize
		{
			get { return Serialize.BrushToString(AreaBrushDown); }
			set { AreaBrushDown = Serialize.StringToBrush(value); }
		}

		[Range(0,100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 3)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set { areaOpacity = Math.Max(0, Math.Min(100, value)); areaBrushDevice.Brush = null; }
		} 

		[Display(ResourceType = typeof(Custom.Resource), Name = "Line", GroupName = "NinjaScriptGeneral", Order = 5)]
		public Stroke 			LineColor		{ get; set; }

        private bool ShouldDrawText { get { return DrawingState == DrawingState.Moving || EndAnchor != null; }  }
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolText", GroupName = "NinjaScriptGeneral", Order = 4)]
		public Brush		 	TextColor
		{
			get { return textBrush; }
			set { textBrush = value; textDeviceBrush.Brush = value; }
		}
		
		[Browsable(false)]
		public string TextColorSerialize
		{
			get { return  Serialize.BrushToString(TextColor); }
			set { TextColor = Serialize.StringToBrush(value); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "Y value 1 display unit", GroupName = "NinjaScriptGeneral", Order = 6)]
		public ValueUnit 	YValueDisplayUnit1
		{ 
			get { return yValueDisplayUnit1; }
			set { yValueDisplayUnit1 = value; isTextCreated = false; }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "Y value 2 display unit", GroupName = "NinjaScriptGeneral", Order = 7)]
		public ValueUnit 	YValueDisplayUnit2
		{ 
			get { return yValueDisplayUnit2; }
			set { yValueDisplayUnit2 = value; isTextCreated = false; }
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			try 
			{
				if (textLayout != null) textLayout.Dispose();
				textFormat = null;
				textDeviceBrush.RenderTarget = null;
				textBackgroundDeviceBrush.RenderTarget = null;
			}
			catch { }
			finally { LineColor = null; }
		}
		
		public override Cursor GetCursor(ChartControl cc, ChartPanel cp, ChartScale cs, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:	return Cursors.Pen;
				case DrawingState.Moving:	return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:
					if (IsLocked) return Cursors.No;
					return editingAnchor == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
				default:
					// see if we are near an anchor right away. this is is cheap so no big deal to do often
					ChartAnchor closest = GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);
					if (closest != null)
					{
						if (IsLocked) return Cursors.Arrow;
						return closest == TextAnchor ? Cursors.SizeAll : closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
					}
					// draw move cursor if cursor is near line path anywhere
					Point	startAnchorPoint	= StartAnchor.GetPoint(cc, cp, cs);
					Point	endAnchorPoint		= EndAnchor.GetPoint(cc, cp, cs);
					Point	txtAnchorPoint		= TextAnchor.GetPoint(cc, cp, cs);
					Vector	startEndVector		= endAnchorPoint - startAnchorPoint;

					//Text Outline Box Path as well
					UpdateTextLayout(cc, ChartPanel, cs);
					double textX				= txtAnchorPoint.X - textLayout.MaxWidth / 2f - textMargin;
					double textY				= txtAnchorPoint.Y - (endAnchorPoint.Y < startAnchorPoint.Y ? 3f : -2f) * textLayout.MaxHeight + textLayout.MaxHeight / 2f;
					Point txtLeft = new Point(textX, textY);
					Point txtRight = new Point(textX + textLayout.MaxWidth, textY);
					Vector txtVector = txtRight - txtLeft;

					if (MathHelper.IsPointAlongVector(point, startAnchorPoint, startEndVector, cursorSensitivity) ||
						MathHelper.IsPointAlongVector(point, txtLeft, txtVector, cursorSensitivity))
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					return null;
			}
		}

		public sealed override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			if (DrawingState == DrawingState.Building) return new Point[0];
			ChartPanel	cp	= cc.ChartPanels[cs.PanelIndex];
			return new[] { StartAnchor.GetPoint(cc, cp, cs), EndAnchor.GetPoint(cc, cp, cs) };
		}
		
		public override object Icon { get { return Icons.DrawRuler; } }

		public override bool IsVisibleOnChart(ChartControl cc, ChartScale cs, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building) return true;
			DateTime minTime = Core.Globals.MaxDate;
			DateTime maxTime = Core.Globals.MinDate;
			foreach (ChartAnchor anchor in Anchors)
			{
				if (anchor.Time < minTime) minTime = anchor.Time;
				if (anchor.Time > maxTime) maxTime = anchor.Time;
			}
			if ((minTime <= lastTimeOnChart) || (minTime <= firstTimeOnChart && maxTime >= firstTimeOnChart)) return true;
			return false;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
			if (!IsVisible) return;
			MinValue = Anchors.Select(a => a.Price).Min();
			MaxValue = Anchors.Select(a => a.Price).Max();
		}

		public override void OnBarsChanged() { isTextCreated = false; }

		public override void OnMouseDown(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						dataPoint.CopyDataValues(EndAnchor);
						dataPoint.CopyDataValues(TextAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;
                        TextAnchor.IsEditing = false;
					}
					
					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing && !TextAnchor.IsEditing)
					{
						DrawingState = DrawingState.Normal;
						IsSelected = false; 
					}
					break;
				case DrawingState.Normal:
					if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) { IsSelected = false; break; }
					Point point = dataPoint.GetPoint(cc, cp, cs);
					editingAnchor = GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);
					if (editingAnchor != null && editingAnchor != TextAnchor)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else if (editingAnchor == null || IsLocked || editingAnchor == TextAnchor)
					{
						if (GetCursor(cc, cp, cs, point) != null) DrawingState = DrawingState.Moving;
						else IsSelected = false;
					}
					break;
			}
		}
		
		public override void OnMouseMove(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building) return;
			
			if (DrawingState == DrawingState.Building)
			{
				if (EndAnchor.IsEditing)
				{
					dataPoint.CopyDataValues(EndAnchor);
                    Point start = StartAnchor.GetPoint(cc, cp, cs);
                    Point mousePoint = dataPoint.GetPoint(cc, cp, cs);
                    TextAnchor.UpdateFromPoint(new Point(start.X + (mousePoint.X - start.X) / 2f, mousePoint.Y), cc, cs);
					isTextCreated = false;
				}
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
			{
				dataPoint.CopyDataValues(editingAnchor);
				Point start = StartAnchor.GetPoint(cc, cp, cs);
				Point end = EndAnchor.GetPoint(cc, cp, cs);
				TextAnchor.UpdateFromPoint(new Point(start.X + (end.X - start.X) / 2f, end.Y), cc, cs);
				isTextCreated = false;
			}
			else if (DrawingState == DrawingState.Moving)
			{
				foreach (ChartAnchor anchor in Anchors) anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint,cc, cp, cs, this);
				if (textLayout != null) textLayout.Dispose();
				textLayout = null;
			}
		}

		public override void OnMouseUp(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building) return;
			DrawingState = DrawingState.Normal;
			if (editingAnchor != null) editingAnchor.IsEditing = false;
			editingAnchor = null;
		}
		
		public override void OnRender(ChartControl cc, ChartScale cs)
		{
			LineColor.RenderTarget				= RenderTarget;
			// first of all, turn on anti-aliasing to smooth out our line
			RenderTarget.AntialiasMode			= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			ChartPanel panel					= cc.ChartPanels[cs.PanelIndex];
			Point lineStartPoint 				= StartAnchor.GetPoint(cc, panel, cs);
			Point lineEndPoint					= EndAnchor.GetPoint(cc, panel, cs);
		
			double strokePixAdjust				= (LineColor.Width % 2).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector strokePixAdjustVec			= new Vector(strokePixAdjust, strokePixAdjust);

			SharpDX.Vector2 startVec			= (lineStartPoint + strokePixAdjustVec).ToVector2();
			SharpDX.Vector2 endVec				= (lineEndPoint + strokePixAdjustVec).ToVector2();
			SharpDX.Direct2D1.Brush tmpBrush	= IsInHitTest ? cc.SelectionBrush : LineColor.BrushDX;
			RenderTarget.DrawLine(startVec, endVec, tmpBrush, LineColor.Width, LineColor.StrokeStyle);
			RenderTarget.DrawLine(startVec, (new Point(lineEndPoint.X, lineStartPoint.Y) + strokePixAdjustVec).ToVector2(), tmpBrush, LineColor.Width, LineColor.StrokeStyle);
			RenderTarget.DrawLine(endVec, (new Point(lineStartPoint.X, lineEndPoint.Y) + strokePixAdjustVec).ToVector2(), tmpBrush, LineColor.Width, LineColor.StrokeStyle);

			bool up = lineEndPoint.Y < lineStartPoint.Y;
			if (!IsInHitTest && ((up && AreaBrushUp != null) || (!up && AreaBrushDown != null)))
			{
				if (areaBrushDevice.Brush == null || isLastMeasureUp == null || (up && !isLastMeasureUp) || (!up && isLastMeasureUp))
				{
					Brush brushCopy			= up ? areaBrushUp.Clone() : areaBrushDown.Clone();
					brushCopy.Opacity		= areaOpacity / 100d; 
					areaBrushDevice.Brush	= brushCopy;
					isLastMeasureUp			= up;
				}
				areaBrushDevice.RenderTarget = RenderTarget;
			}
			else
			{
				areaBrushDevice.RenderTarget = null;
				areaBrushDevice.Brush = null;
			}

			SharpDX.RectangleF rectArea = new SharpDX.RectangleF((float)(lineStartPoint.X + strokePixAdjust), 
																	(float)(lineStartPoint.Y + strokePixAdjust), 
																	(float)(lineEndPoint.X - lineStartPoint.X), (float)(lineEndPoint.Y - lineStartPoint.Y));
					
			if (!IsInHitTest && areaBrushDevice.BrushDX != null) RenderTarget.FillRectangle(rectArea, areaBrushDevice.BrushDX);

			
			if (ShouldDrawText)
			{
				UpdateTextLayout(cc, ChartPanel, cs);
				textDeviceBrush.RenderTarget			= RenderTarget;
				// Text rec uses same settings as mini data box
				textBackgroundDeviceBrush.Brush			= Application.Current.FindResource("ChartControl.DataBoxBackground") as Brush;
				textBackgroundDeviceBrush.RenderTarget	= RenderTarget;

				Brush borderBrush						= Application.Current.FindResource("BorderThinBrush") as Brush;
				object thicknessResource				= Application.Current.FindResource("BorderThinThickness");
				double thickness						= thicknessResource as double? ?? 1;
				Stroke textBorderStroke					= new Stroke(borderBrush ?? LineColor.Brush, DashStyleHelper.Solid,Convert.ToSingle(thickness)) { RenderTarget = RenderTarget };

				Point			textEndPoint			= TextAnchor.GetPoint(cc, panel, cs);

				float				rectPixAdjust		= (float)(strokePixAdjust / 2f);
				SharpDX.RectangleF	rect				= new SharpDX.RectangleF((float)(textEndPoint.X - textLayout.MaxWidth / 2f - textMargin + rectPixAdjust),
																				(float)(textEndPoint.Y - (lineEndPoint.Y < lineStartPoint.Y ? 3f : -2f) * textLayout.MaxHeight + rectPixAdjust),
																				textLayout.MaxWidth + textMargin * 2f, textLayout.MaxHeight + textMargin * 2f);

				if (textBackgroundDeviceBrush.BrushDX != null && !IsInHitTest)
					RenderTarget.FillRectangle(rect, textBackgroundDeviceBrush.BrushDX);
				RenderTarget.DrawRectangle(rect, textBorderStroke.BrushDX, textBorderStroke.Width, textBorderStroke.StrokeStyle);
			
				if (textDeviceBrush.BrushDX != null && !IsInHitTest)
					RenderTarget.DrawTextLayout(new SharpDX.Vector2((float)(rect.X + textMargin + strokePixAdjust), (float)(rect.Y + textMargin + strokePixAdjust)), textLayout, textDeviceBrush.BrushDX);
			}
		}
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= "Measure";
				DisplayOnChartsMenus 	= false;
				DrawingState			= DrawingState.Building;
				StartAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
				TextAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				TextAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorText;
				LineColor				= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				TextColor				= Application.Current.FindResource("ChartControl.DataBoxForeground") as Brush ?? Brushes.CornflowerBlue;
			}
			else if (State == State.Terminated)
			{
				Dispose();
			}
		}
		
		private string getYvalueString(ValueUnit vu, ChartBars chartBars, double yDiffPrice, double yDiffTicks)
		{
			string s = "";
			switch (vu)
			{
				case ValueUnit.Price	: s = chartBars.Bars.Instrument.MasterInstrument.FormatPrice(yDiffPrice) + " pts"; break;
				case ValueUnit.Currency	: 
					s = AttachedTo.Instrument.MasterInstrument.InstrumentType == InstrumentType.Forex
						? Core.Globals.FormatCurrency((int)Math.Abs(yDiffTicks) * Account.All[0].ForexLotSize * (AttachedTo.Instrument.MasterInstrument.TickSize * AttachedTo.Instrument.MasterInstrument.PointValue))
						: Core.Globals.FormatCurrency((int)Math.Abs(yDiffTicks) * (AttachedTo.Instrument.MasterInstrument.TickSize * AttachedTo.Instrument.MasterInstrument.PointValue)); 
					break;
				case ValueUnit.Percent	: s = (yDiffPrice / AttachedTo.Instrument.MasterInstrument.RoundToTickSize(StartAnchor.Price)).ToString("P", Core.Globals.GeneralOptions.CurrentCulture); break;
				case ValueUnit.Ticks	: s = yDiffTicks.ToString("F0") + " ticks"; break;
				case ValueUnit.Pips		:
					// show tenth pips (if available)
					double pips = Math.Abs(yDiffTicks/10);
					char decimalChar = Char.Parse(Core.Globals.GeneralOptions.CurrentCulture.NumberFormat.NumberDecimalSeparator);
					s = (Int32.Parse(pips.ToString("F1").Split(decimalChar)[1]) > 0 ? pips.ToString("F1").Replace(decimalChar, '\'') : pips.ToString("F0")) + " pips";
					break;
			}
			return s;
		}

		private void UpdateTextLayout(ChartControl cc, ChartPanel cp, ChartScale cs)
		{
			if (isTextCreated && textLayout != null && !textLayout.IsDisposed) return;
			if (textFormat != null && !textFormat.IsDisposed) textFormat.Dispose();
			if (textLayout != null && !textLayout.IsDisposed) textLayout.Dispose();
		
			ChartBars chartBars = GetAttachedToChartBars();
			if (chartBars == null) return;	// bars can be null while chart is initializing

			double yDiffPrice	= AttachedTo.Instrument.MasterInstrument.RoundToTickSize(EndAnchor.Price - StartAnchor.Price);
			double yDiffTicks	= yDiffPrice / AttachedTo.Instrument.MasterInstrument.TickSize;

			yValueString1 = getYvalueString(YValueDisplayUnit1, chartBars, yDiffPrice, yDiffTicks);
			yValueString2 = getYvalueString(YValueDisplayUnit2, chartBars, yDiffPrice, yDiffTicks);
			
			TimeSpan timeDiff = EndAnchor.Time - StartAnchor.Time;
			// trim off millis/ticks, match NT7 time formatting
			timeDiff = new TimeSpan(timeDiff.Days, timeDiff.Hours, timeDiff.Minutes, timeDiff.Seconds);

			bool isMultiDay = Math.Abs(timeDiff.TotalHours) >= 24;

			if (chartBars.Bars.BarsPeriod.BarsPeriodType == BarsPeriodType.Day)
			{
				int timeDiffDay = Math.Abs(timeDiff.Days);
				timeText = timeDiffDay > 1 ? Math.Abs(timeDiff.Days) + " " + Custom.Resource.Days  : Math.Abs(timeDiff.Days) + " " + Custom.Resource.Day;
			}
			else
			{
				timeText	= isMultiDay ? string.Format("{0}\n{1,25}", 
											string.Format(Custom.Resource.NinjaScriptDrawingToolRulerDaysFormat, Math.Abs(timeDiff.Days)),
											timeDiff.Subtract(new TimeSpan(timeDiff.Days, 0, 0, 0)).Duration().ToString()) : timeDiff.Duration().ToString();
			}

			Point startPoint = StartAnchor.GetPoint(cc, cp, cs);
			Point endPoint = EndAnchor.GetPoint(cc, cp, cs);
			int startIdx = chartBars.GetBarIdxByX(cc, (int)startPoint.X);
			int endIdx = chartBars.GetBarIdxByX(cc, (int)endPoint.X);
			int numBars = endIdx - startIdx;

			SimpleFont wpfFont			= cc.Properties.LabelFont ?? new SimpleFont();
			textFormat					= wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment	= SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping		= SharpDX.DirectWrite.WordWrapping.NoWrap;
			// format text to our text rectangle bounds (it will wrap to these constraints), nt7 format
			// NOTE: Environment.NewLine doesnt work right here
			string text = string.Format("{0}{1}  {2}  {3}  {4}", numBars, " bars", timeText, yValueString1, yValueString2);
			// give big values for max width/height, we will trim to actual used
			textLayout				= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, text, textFormat, 600, 600); 
			// use measured max width/height
			textLayout.MaxWidth		= textLayout.Metrics.Width;
			textLayout.MaxHeight	= textLayout.Metrics.Height;
			isTextCreated			= true;
		}
	}

	public static partial class Draw
	{
		private static Measure MeasureCore(NinjaScriptBase owner, string tag,  bool isAutoScale, int startBarsAgo, DateTime startTime, double startY, 
			int endBarsAgo, DateTime endTime, double endY, int textBarsAgo, DateTime textTime, double textY, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");
			if (startTime == Core.Globals.MinDate && endTime == Core.Globals.MinDate && startBarsAgo == int.MinValue && endBarsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			Measure measure = DrawingTool.GetByTagOrNew(owner, typeof(Measure), tag, templateName) as Measure;
			
			if (measure == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(measure, tag, isAutoScale, owner, isGlobal);

			ChartAnchor startAnchor	= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
			ChartAnchor endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);
			ChartAnchor txtAnchor	= DrawingTool.CreateChartAnchor(owner, textBarsAgo, textTime, textY);

			startAnchor.CopyDataValues(measure.StartAnchor);
			endAnchor.CopyDataValues(measure.EndAnchor);
			txtAnchor.CopyDataValues(measure.TextAnchor);
			measure.SetState(State.Active);
			return measure;
		}

		/// <summary>
		/// Draws a measure.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="textBarsAgo">The number of bars ago (x value) of the 3rd anchor point</param>
		/// <param name="textY">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static Measure Measure(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, int textBarsAgo, double textY)
		{
			return MeasureCore(owner, tag, isAutoScale, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, textBarsAgo, Core.Globals.MinDate, textY, false, null);
		}

		/// <summary>
		/// Draws a measure.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="textTime">The time of the 3rd anchor point</param>
		/// <param name="textY">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static Measure Measure(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, DateTime textTime, double textY)
		{
			return MeasureCore(owner, tag, isAutoScale, int.MinValue, startTime, startY, int.MinValue, endTime, endY, int.MinValue, textTime, textY, false, null);
		}

		/// <summary>
		/// Draws a measure.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="textBarsAgo">The number of bars ago (x value) of the 3rd anchor point</param>
		/// <param name="textY">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Measure Measure(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, int textBarsAgo, double textY, bool isGlobal, string templateName)
		{
			return MeasureCore(owner, tag, isAutoScale, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, textBarsAgo, Core.Globals.MinDate, textY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a measure.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="textTime">The time of the 3rd anchor point</param>
		/// <param name="textY">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Measure Measure(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, DateTime textTime, double textY, bool isGlobal, string templateName)
		{
			return MeasureCore(owner, tag, isAutoScale, int.MinValue, startTime, startY, int.MinValue, endTime, endY, int.MinValue, textTime, textY, isGlobal, templateName);
		}
	}
}
