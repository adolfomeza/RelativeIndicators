/*
*	Channel Drawing Tool made with ♡ by beo
* 	Last edit 03/03/2021
*	https://priceactiontradingsystem.com/link-to-forum/topic/pats-toolbar-custom-drawing-tools-why-this-is-better/
*/

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Channel IDrawingTool.
	/// </summary>
	public class Channel : PriceLevelContainer
	{
		private ChartControl 	mycc;
		private MenuItem 		myMenuItem1, myMenuItem2, myMenuItem3, myMenuItem4;
		private Separator 		separatorItem;

		private				int									areaOpacity;
		private				Brush								areaBrush;	
		private	readonly	DeviceBrush							areaDeviceBrush				= new DeviceBrush();
		private	const		double								cursorSensitivity			= 15;
		private				ChartAnchor							editingAnchor;
		private				SharpDX.Direct2D1.PathGeometry		fillMainGeometry, fillLeftGeometry, fillRightGeometry;
		private				SharpDX.Vector2[]					fillMainFig, fillLeftFig, fillRightFig;
		private				bool								isReadyForMovingSecondLeg;
		private				Point								end2Point, midPoint, mid2Point, leftPoint, rightPoint, tmp1, tmp2, tmp3, tmp4;

		public override object Icon { get { return Gui.Tools.Icons.DrawTrendChannel; } }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 1)]
		public Brush AreaBrush
		{
			get { return areaBrush; }
			set { areaBrush = value.ToFrozenBrush(); }
		}

		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 2)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set
			{
				int newOpacity = Math.Max(0, Math.Min(100, value));
				if (newOpacity != areaOpacity)
				{
					areaOpacity = newOpacity;
					areaDeviceBrush.Brush = null;
				}
			}
		}

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { TrendStartAnchor, TrendEndAnchor, ParallelStartAnchor }; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciRetracementsExtendLinesRight", GroupName = "NinjaScriptLines")]
		public bool IsExtendedLinesRight { get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciRetracementsExtendLinesLeft", GroupName = "NinjaScriptLines")]
		public bool IsExtendedLinesLeft { get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Midline", GroupName = "NinjaScriptLines")]
		public bool ShowMidline { get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Measured Moves", GroupName = "NinjaScriptLines")]
		public bool ShowMeasuredMoves { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTrendChannelTrendStroke", GroupName = "NinjaScriptLines", Order = 1)]
		public Stroke Stroke { get; set; }

		[Display(Order = 0), ExcludeFromTemplate]
		public ChartAnchor TrendStartAnchor { get; set; }

		[Display(Order = 5), ExcludeFromTemplate]
		public ChartAnchor TrendMiddleAnchor { get; set; }

		[Display(Order = 10), ExcludeFromTemplate]
		public ChartAnchor TrendEndAnchor { get; set; }

		[Display(Order = 15), ExcludeFromTemplate]
		public ChartAnchor LeftAnchor { get; set; }

		[Display(Order = 20), ExcludeFromTemplate]
		public ChartAnchor ParallelStartAnchor { get; set; }

		[Display(Order = 25), ExcludeFromTemplate]
		public ChartAnchor ParallelMiddleAnchor { get; set; }

		[Display(Order = 30), ExcludeFromTemplate]
		public ChartAnchor ParallelEndAnchor { get; set; }

		[Display(Order = 35), ExcludeFromTemplate]
		public ChartAnchor RightAnchor { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTrendChannelParallelStroke", GroupName = "NinjaScriptLines", Order = 2)]
		public Stroke ParallelStroke { get; set; }

		public override bool SupportsAlerts { get { return true; } }

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);
			Channel tc = ninjaScript as Channel;
			if (tc != null) tc.isReadyForMovingSecondLeg = isReadyForMovingSecondLeg;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (areaDeviceBrush != null) areaDeviceBrush.RenderTarget = null;
			if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
			if (fillMainGeometry != null) fillMainGeometry.Dispose();
			if (fillRightGeometry != null) fillRightGeometry.Dispose();
		}

		protected override void OnStateChange()
		{
			switch (State)
			{
				case State.SetDefaults:
					Description						= "Trend Channel";
					Name							= "Channel";
					DisplayOnChartsMenus			= false;
					DrawingState					= DrawingState.Building;
					TrendStartAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = Custom.Resource.NinjaScriptDrawingToolTrendChannelStart1AnchorDisplayName };
					TrendEndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = Custom.Resource.NinjaScriptDrawingToolTrendChannelEnd1AnchorDisplayName };
					ParallelStartAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = Custom.Resource.NinjaScriptDrawingToolTrendChannelStart2AnchorDisplayName, Time = DateTime.MinValue };
					ParallelEndAnchor				= new ChartAnchor { IsEditing = false, DrawingTool = this, IsBrowsable = false };
					TrendMiddleAnchor				= new ChartAnchor { IsEditing = false, DrawingTool = this, IsBrowsable = false };
					ParallelMiddleAnchor			= new ChartAnchor { IsEditing = false, DrawingTool = this, IsBrowsable = false };
					LeftAnchor						= new ChartAnchor { IsEditing = false, DrawingTool = this, IsBrowsable = false };
					RightAnchor						= new ChartAnchor { IsEditing = false, DrawingTool = this, IsBrowsable = false };
					ParallelStroke					= new Stroke(Brushes.RoyalBlue, 2f);
					Stroke							= new Stroke(Brushes.RoyalBlue, 2f);
					AreaBrush						= Brushes.RoyalBlue;
					AreaOpacity						= 0;
					ShowMidline						= true;
					ShowMeasuredMoves				= false;
					break;
				case State.Terminated:
					Dispose();
					break;
			}
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			if (PriceLevels == null || PriceLevels.Count == 0) yield break;
			foreach (PriceLevel trendLevel in PriceLevels)
			{
				yield return new AlertConditionItem
				{
					Name					= trendLevel.Name,
					ShouldOnlyDisplayName	= true,
					Tag						= trendLevel,
				};
			}
		}

		private bool arePointsClose(Point a, Point b, double sensitivity) { return Math.Abs(a.X - b.X) < sensitivity && Math.Abs(a.Y - b.Y) < sensitivity; }

		public override Cursor GetCursor(ChartControl cc, ChartPanel cp, ChartScale cs, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building	: return Cursors.Pen;
				case DrawingState.Moving	: return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing	:
					if (editingAnchor == null) return null;
					return IsLocked ? Cursors.No :
						editingAnchor == TrendStartAnchor ? Cursors.SizeNESW : editingAnchor == TrendEndAnchor ? Cursors.SizeNWSE :
						(editingAnchor == TrendMiddleAnchor || editingAnchor == ParallelMiddleAnchor) ? Cursors.SizeNS :
						(editingAnchor == LeftAnchor || editingAnchor == RightAnchor) ? Cursors.ScrollWE : Cursors.ScrollAll;

				default:
					ChartAnchor closest				= GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);
					if (closest != null) return IsLocked ? Cursors.Arrow : closest == TrendStartAnchor ? Cursors.SizeNESW : closest == TrendEndAnchor ? Cursors.SizeNWSE : Cursors.ScrollAll;

					if (arePointsClose(point, end2Point, cursorSensitivity)) return Cursors.ScrollAll;
					if (arePointsClose(point, midPoint, cursorSensitivity) || arePointsClose(point, mid2Point, cursorSensitivity)) return Cursors.SizeNS;
					if (arePointsClose(point, leftPoint, cursorSensitivity) || arePointsClose(point, rightPoint, cursorSensitivity)) return Cursors.ScrollWE;

					Point startAnchorPixelPoint		= TrendStartAnchor.GetPoint(cc, cp, cs);
					Point startAnchor2PixelPoint	= ParallelStartAnchor.GetPoint(cc, cp, cs);
					Point endAnchorPixelPoint		= TrendEndAnchor.GetPoint(cc, cp, cs);
					Point endAnchor2PixelPoint	= startAnchor2PixelPoint + (endAnchorPixelPoint - startAnchorPixelPoint);
					bool  left2right 				= startAnchorPixelPoint.X <= endAnchorPixelPoint.X;

					if ((IsExtendedLinesLeft && left2right) || (IsExtendedLinesRight && !left2right))
					{
						if (MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, GetExtendedPoint(endAnchorPixelPoint, startAnchorPixelPoint) - startAnchorPixelPoint, cursorSensitivity) ||
							MathHelper.IsPointAlongVector(point, startAnchor2PixelPoint, GetExtendedPoint(endAnchor2PixelPoint, startAnchor2PixelPoint) - startAnchor2PixelPoint, cursorSensitivity))
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}

					if (IsExtendedLinesRight && left2right || IsExtendedLinesLeft && !left2right)
					{
						if (MathHelper.IsPointAlongVector(point, endAnchorPixelPoint, GetExtendedPoint(startAnchorPixelPoint, endAnchorPixelPoint) - endAnchorPixelPoint, cursorSensitivity) ||
							MathHelper.IsPointAlongVector(point, endAnchor2PixelPoint, GetExtendedPoint(startAnchor2PixelPoint, endAnchor2PixelPoint) - endAnchor2PixelPoint, cursorSensitivity))
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}

					if (MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, endAnchorPixelPoint - startAnchorPixelPoint, cursorSensitivity) ||
						MathHelper.IsPointAlongVector(point, startAnchor2PixelPoint, endAnchor2PixelPoint - startAnchor2PixelPoint, cursorSensitivity))
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;

					return null;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			if (DrawingState == DrawingState.Building) return new Point[0];
			ChartPanel	cp	= cc.ChartPanels[cs.PanelIndex];

			Point		startPoint	= TrendStartAnchor.GetPoint(cc, cp, cs);
			Point		endPoint	= TrendEndAnchor.GetPoint(cc, cp, cs);
			Point		start2Point	= ParallelStartAnchor.GetPoint(cc, cp, cs);
						end2Point	= start2Point + (endPoint - startPoint);
						midPoint	= new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
						mid2Point	= new Point((start2Point.X + end2Point.X) / 2, (start2Point.Y + end2Point.Y) / 2);
						leftPoint	= new Point((startPoint.X + start2Point.X) / 2, (startPoint.Y + start2Point.Y) / 2);
						rightPoint	= new Point((endPoint.X + end2Point.X) / 2, (endPoint.Y + end2Point.Y) / 2);

			return new[] { startPoint, midPoint, endPoint, start2Point, mid2Point, end2Point, leftPoint, rightPoint };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl cc, ChartScale cs)
		{
			ChartPanel	panel		= cc.ChartPanels[PanelIndex];
			Point		startPoint	= TrendStartAnchor.GetPoint(cc, panel, cs);
			Point		endPoint	= TrendEndAnchor.GetPoint(cc, panel, cs);
			bool 		left2right 	= startPoint.X <= endPoint.X;
			
			PriceLevel trendLevel 	= conditionItem.Tag as PriceLevel;
			Vector startDir 		= trendLevel.Value / 100 * (ParallelStartAnchor.GetPoint(cc, panel, cs) - startPoint);
			Vector lineVector 		= endPoint - startPoint;
			Point newStartPoint 	= new Point(startPoint.X + startDir.X, startPoint.Y + startDir.Y);
			Point newEndPoint 		= new Point(newStartPoint.X + lineVector.X, newStartPoint.Y + lineVector.Y);
			
			double firstBarX		= cc.GetXByTime(values[0].Time);
			double firstBarY		= cs.GetYByValue(values[0].Value);
			
			Point alertStartPoint	= newStartPoint.X <= newEndPoint.X ? newStartPoint : newEndPoint;
			Point alertEndPoint		= newEndPoint.X >= newStartPoint.X ? newEndPoint : newStartPoint;
			
			if (IsExtendedLinesLeft && left2right || IsExtendedLinesRight && !left2right)
			{
				Point minPoint = GetExtendedPoint(alertEndPoint, alertStartPoint);
				if (minPoint.X > -1 || minPoint.Y > -1) alertStartPoint = minPoint;
			}

			if (IsExtendedLinesRight && left2right || IsExtendedLinesLeft && !left2right)
			{
				Point maxPoint = GetExtendedPoint(alertStartPoint, alertEndPoint);
				if (maxPoint.X > -1 || maxPoint.Y > -1) alertEndPoint = maxPoint;
			}

			if (firstBarX < alertStartPoint.X || firstBarX > alertEndPoint.X) return false;

			// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(alertStartPoint, alertEndPoint, new Point(firstBarX, firstBarY));
			// for vertical things, think of a vertical line rotated 90 degrees to lay flat, where it's normal vector is 'up'
			switch (condition)
			{
				case Condition.Greater		: return pointLocation == MathHelper.PointLineLocation.LeftOrAbove;
				case Condition.GreaterEqual	: return pointLocation == MathHelper.PointLineLocation.LeftOrAbove || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Less			: return pointLocation == MathHelper.PointLineLocation.RightOrBelow;
				case Condition.LessEqual	: return pointLocation == MathHelper.PointLineLocation.RightOrBelow || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Equals		: return pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.NotEqual		: return pointLocation != MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.CrossAbove	:
				case Condition.CrossBelow	:
					Predicate<ChartAlertValue> predicate = v =>
					{
						double barX = cc.GetXByTime(v.Time);
						double barY = cs.GetYByValue(v.Value);
						// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
						MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(alertStartPoint, alertEndPoint, new Point(barX, barY));
						if (condition == Condition.CrossAbove) return ptLocation == MathHelper.PointLineLocation.LeftOrAbove;
						return ptLocation == MathHelper.PointLineLocation.RightOrBelow;
					};
					return MathHelper.DidPredicateCross(values, predicate);
			}
			
			return false;
		}

		public override bool IsVisibleOnChart(ChartControl cc, ChartScale cs, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (Anchors.Any(a => a.Time >= firstTimeOnChart && a.Time <= lastTimeOnChart)) return true;

			ChartPanel	panel		= cc.ChartPanels[cs.PanelIndex];
			Point		startPoint	= TrendStartAnchor.GetPoint(cc, panel, cs);
			Point		endPoint	= TrendEndAnchor.GetPoint(cc, panel, cs);
			Point		startPoint2	= ParallelStartAnchor.GetPoint(cc, panel, cs);
			Point		endPoint2	= startPoint2 + (endPoint - startPoint);
			Point[]		points		= { GetExtendedPoint(startPoint, endPoint), GetExtendedPoint(startPoint2, endPoint2), GetExtendedPoint(endPoint, startPoint), GetExtendedPoint(endPoint2, startPoint2) };

			DateTime	minTime		= cc.GetTimeByX((int)points.Select(p => p.X).Min());
			DateTime	startTime	= cc.GetTimeByX((int)startPoint.X);
			DateTime	endTime		= cc.GetTimeByX((int)endPoint.X);
			DateTime	maxTime		= cc.GetTimeByX((int)points.Select(p => p.X).Max());

			// first check if any anchor is in visible range
			foreach (DateTime time in new[] { minTime, startTime, endTime, maxTime }) if (time >= firstTimeOnChart && time <= lastTimeOnChart) return true;

			// check crossthrough and keep in mind the anchors could be 'backwards' 
			return ((minTime <= firstTimeOnChart && maxTime >= lastTimeOnChart) || (startTime <= firstTimeOnChart && endTime >= lastTimeOnChart) || (endTime <= firstTimeOnChart && startTime >= lastTimeOnChart));
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
			if (!IsVisible) return;
			if (Anchors.Any(a => !a.IsEditing)) foreach (ChartAnchor anchor in Anchors) { MinValue = Math.Min(anchor.Price, MinValue); MaxValue = Math.Max(anchor.Price, MaxValue); }
		}

		private void editingMode(ChartAnchor anchor) { editingAnchor = anchor; editingAnchor.IsEditing = true; DrawingState = DrawingState.Editing; }

		public override void OnMouseDown(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (TrendStartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(TrendStartAnchor);
						dataPoint.CopyDataValues(TrendEndAnchor);
						TrendStartAnchor.IsEditing = false;
					}
					else if (TrendEndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(TrendEndAnchor);
						TrendEndAnchor.IsEditing = false;
					}

					if (!TrendStartAnchor.IsEditing && !TrendEndAnchor.IsEditing) SetParallelLine(cc, ParallelStartAnchor.IsEditing);

					if (!isReadyForMovingSecondLeg)
					{
						// if we just plopped second line, move it. if we just finished moving it, we're done with initial building
						if (!ParallelStartAnchor.IsEditing) isReadyForMovingSecondLeg = true;
					}
					else 
					{
						isReadyForMovingSecondLeg	= false;
						DrawingState				= DrawingState.Normal;
						IsSelected					= false;
					}
					break;
				case DrawingState.Normal:
				case DrawingState.Moving:
					if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) { IsSelected = false; break; }
					Point point		= dataPoint.GetPoint(cc, cp, cs);
					editingAnchor	= GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);

					if (editingAnchor != null) editingMode(editingAnchor);
					else if (arePointsClose(point, end2Point, cursorSensitivity)) editingMode(ParallelEndAnchor);
					else if (arePointsClose(point, midPoint, cursorSensitivity))
					{
						tmp1 = midPoint;
						tmp2 = mid2Point;
						tmp3 = ParallelStartAnchor.GetPoint(cc, cp, cs);
						tmp4 = end2Point;
						editingMode(TrendMiddleAnchor);
					}
					else if (arePointsClose(point, mid2Point, cursorSensitivity))
					{
						tmp1 = midPoint;
						tmp2 = mid2Point;
						tmp3 = TrendStartAnchor.GetPoint(cc, cp, cs);
						editingMode(ParallelMiddleAnchor);
					}
					else if (arePointsClose(point, leftPoint, cursorSensitivity))
					{
						tmp1 = leftPoint;
						tmp2 = rightPoint;
						tmp3 = TrendStartAnchor.GetPoint(cc, cp, cs);
						tmp4 = ParallelStartAnchor.GetPoint(cc, cp, cs);
						editingMode(LeftAnchor);
					}
					else if (arePointsClose(point, rightPoint, cursorSensitivity))
					{
						tmp1 = leftPoint;
						tmp2 = rightPoint;
						tmp3 = TrendEndAnchor.GetPoint(cc, cp, cs);
						editingMode(RightAnchor);
					}
					else if (editingAnchor == null || IsLocked)
					{
						if (GetCursor(cc, cp, cs, point) == null) IsSelected = false;
						else DrawingState = DrawingState.Moving;
					}
					break;
			}
		}

        // Calculate the projection point between point pt and the segment p1 --> p2.
        // http://csharphelper.com/blog/2016/09/find-the-shortest-distance-between-a-point-and-a-line-segment-in-c/
        private Point FindProjectionToSegment(Point pt, Point p1, Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            if ((dx == 0) && (dy == 0)) return p1;
            double t = ((pt.X - p1.X) * dx + (pt.Y - p1.Y) * dy) / (dx * dx + dy * dy);
            return new Point(p1.X + t * dx, p1.Y + t * dy);
        }

		public override void OnMouseMove(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building) return;

			if (DrawingState == DrawingState.Building)
			{
				if (TrendEndAnchor.IsEditing) dataPoint.CopyDataValues(TrendEndAnchor);
				else if (isReadyForMovingSecondLeg)
                {
                    Point mousePoint = dataPoint.GetPoint(cc, cp, cs);
                    Point start = TrendStartAnchor.GetPoint(cc, cp, cs);
                    Point proj = FindProjectionToSegment(mousePoint, start, TrendEndAnchor.GetPoint(cc, cp, cs));
                    ParallelStartAnchor.UpdateFromPoint(new Point(start.X - proj.X + mousePoint.X, start.Y - proj.Y + mousePoint.Y), cc, cs);
                }	
			}
			else if (DrawingState == DrawingState.Editing)
			{
				if (TrendStartAnchor.IsEditing)
                {
					TrendStartAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
                    ParallelStartAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
                }
                else if (TrendEndAnchor.IsEditing)
                {
                    TrendEndAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
                }
				else if (ParallelStartAnchor.IsEditing || ParallelEndAnchor.IsEditing)
				{
					ParallelStartAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
				}
				else if (TrendMiddleAnchor.IsEditing)
				{
					Point proj = FindProjectionToSegment(dataPoint.GetPoint(cc, cp, cs), tmp1, tmp2);
					double deltaX = proj.X - tmp2.X;
					double deltaY = proj.Y - tmp2.Y;
					TrendStartAnchor.UpdateFromPoint(new Point(tmp3.X + deltaX, tmp3.Y + deltaY), cc, cs);
					TrendEndAnchor.UpdateFromPoint(new Point(tmp4.X + deltaX, tmp4.Y + deltaY), cc, cs);
				}
				else if (ParallelMiddleAnchor.IsEditing)
				{
					Point proj = FindProjectionToSegment(dataPoint.GetPoint(cc, cp, cs), tmp1, tmp2);
					ParallelStartAnchor.UpdateFromPoint(new Point(tmp3.X + proj.X - tmp1.X, tmp3.Y + proj.Y - tmp1.Y), cc, cs);
				}
				else if (LeftAnchor.IsEditing)
				{
					Point proj = FindProjectionToSegment(dataPoint.GetPoint(cc, cp, cs), tmp1, tmp2);
					double deltaX = proj.X - tmp1.X;
					double deltaY = proj.Y - tmp1.Y;
					TrendStartAnchor.UpdateFromPoint(new Point(tmp3.X + deltaX, tmp3.Y + deltaY), cc, cs);
					ParallelStartAnchor.UpdateFromPoint(new Point(tmp4.X + deltaX, tmp4.Y + deltaY), cc, cs);
				}
				else if (RightAnchor.IsEditing)
				{
					Point proj = FindProjectionToSegment(dataPoint.GetPoint(cc, cp, cs), tmp1, tmp2);
					TrendEndAnchor.UpdateFromPoint(new Point(tmp3.X + proj.X - tmp2.X, tmp3.Y + proj.Y - tmp2.Y), cc, cs);
				}
				else DrawingState = DrawingState.Moving;
			}
			else if (DrawingState == DrawingState.Moving) foreach (ChartAnchor anchor in Anchors) anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
		}

		public override void OnMouseUp(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building) return;
			if (editingAnchor != null) editingAnchor.IsEditing = false;
			editingAnchor = null;
			DrawingState = DrawingState.Normal;
		}

		private void AddMenuHandlers(ChartControl cc)
		{
			if (cc == null) return;
			mycc = cc;
			mycc.ContextMenuOpening += ChartControl_ContextMenuOpening;
            mycc.ContextMenuClosing += ChartControl_ContextMenuClosing;
			
			myMenuItem1 = new MenuItem { Header = "Extend Left", IsCheckable = true, IsChecked = this.IsExtendedLinesLeft };
			myMenuItem2 = new MenuItem { Header = "Extend Right", IsCheckable = true, IsChecked = this.IsExtendedLinesRight };
			myMenuItem3 = new MenuItem { Header = "Show Midline", IsCheckable = true, IsChecked = this.ShowMidline };
			myMenuItem4 = new MenuItem { Header = "Show Measured Moves", IsCheckable = true, IsChecked = this.ShowMeasuredMoves };
			separatorItem  = new Separator { Style = Application.Current.TryFindResource("MainMenuSeparator") as Style };
			
			myMenuItem1.Click += MyMenuItem1_Click;
			myMenuItem2.Click += MyMenuItem2_Click;
			myMenuItem3.Click += MyMenuItem3_Click;
			myMenuItem4.Click += MyMenuItem4_Click;
		}
		
		private void RemoveMenuHandlers()
		{
			if (mycc == null || myMenuItem1 == null || myMenuItem2 == null || myMenuItem3 == null || myMenuItem4 == null) return;

			myMenuItem1.Click -= MyMenuItem1_Click;
            myMenuItem2.Click -= MyMenuItem2_Click;
			myMenuItem3.Click -= MyMenuItem3_Click;
			myMenuItem4.Click -= MyMenuItem4_Click;
			
            mycc.ContextMenuOpening -= ChartControl_ContextMenuOpening;
            mycc.ContextMenuClosing -= ChartControl_ContextMenuClosing;
			
			if(mycc.ContextMenu.Items.Contains(myMenuItem1))
            {
                myMenuItem1.Click -= MyMenuItem1_Click;
                mycc.ContextMenu.Items.Remove(myMenuItem1);
            }
            
            if(mycc.ContextMenu.Items.Contains(myMenuItem2))
            {
                myMenuItem2.Click -= MyMenuItem2_Click;
                mycc.ContextMenu.Items.Remove(myMenuItem2);
            }

			if(mycc.ContextMenu.Items.Contains(myMenuItem3))
            {
                myMenuItem3.Click -= MyMenuItem3_Click;
                mycc.ContextMenu.Items.Remove(myMenuItem3);
            }

			if(mycc.ContextMenu.Items.Contains(myMenuItem4))
            {
                myMenuItem4.Click -= MyMenuItem4_Click;
                mycc.ContextMenu.Items.Remove(myMenuItem4);
            }

			if(mycc.ContextMenu.Items.Contains(separatorItem))
            {
                mycc.ContextMenu.Items.Remove(separatorItem);
            }
		}

		private void ChartControl_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
           if(mycc.ContextMenu.Items.Contains(myMenuItem1)) mycc.ContextMenu.Items.Remove(myMenuItem1);
           if(mycc.ContextMenu.Items.Contains(myMenuItem2)) mycc.ContextMenu.Items.Remove(myMenuItem2);
		   if(mycc.ContextMenu.Items.Contains(myMenuItem3)) mycc.ContextMenu.Items.Remove(myMenuItem3);
		   if(mycc.ContextMenu.Items.Contains(myMenuItem4)) mycc.ContextMenu.Items.Remove(myMenuItem4);
		   if(mycc.ContextMenu.Items.Contains(separatorItem)) mycc.ContextMenu.Items.Remove(separatorItem);
        }

        private void ChartControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
			if (!this.IsSelected) return;
			if (mycc.ContextMenu.Items.Contains(myMenuItem3) == false) mycc.ContextMenu.Items.Insert(3, myMenuItem3);
			if (mycc.ContextMenu.Items.Contains(myMenuItem4) == false) mycc.ContextMenu.Items.Insert(4, myMenuItem4);
			if (mycc.ContextMenu.Items.Contains(myMenuItem2) == false) mycc.ContextMenu.Items.Insert(5, myMenuItem2);
			if (mycc.ContextMenu.Items.Contains(myMenuItem1) == false) mycc.ContextMenu.Items.Insert(6, myMenuItem1);
			if (mycc.ContextMenu.Items.Contains(separatorItem) == false) mycc.ContextMenu.Items.Insert(7, separatorItem);
        }
		
        private void MyMenuItem1_Click(object sender, RoutedEventArgs e)
        {
			IsExtendedLinesLeft = !IsExtendedLinesLeft;
			this.ForceRefresh();
        }

        private void MyMenuItem2_Click(object sender, RoutedEventArgs e)
        {
			IsExtendedLinesRight = !IsExtendedLinesRight;
			this.ForceRefresh();
        }

		private void MyMenuItem3_Click(object sender, RoutedEventArgs e)
        {
			ShowMidline = !ShowMidline;
			this.ForceRefresh();
        }

		private void MyMenuItem4_Click(object sender, RoutedEventArgs e)
        {
			ShowMeasuredMoves = !ShowMeasuredMoves;
			this.ForceRefresh();
        }

		public override void OnRender(ChartControl cc, ChartScale cs)
		{
			// Here we capture ChartControl and create our menu items
			if (mycc == null) AddMenuHandlers(cc);

			Stroke.RenderTarget				= RenderTarget;
			ParallelStroke.RenderTarget		= RenderTarget;
			RenderTarget.AntialiasMode		= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			
			if (!IsInHitTest && AreaBrush != null)
			{
				if (areaDeviceBrush.Brush == null)
				{
					Brush brushCopy			= areaBrush.Clone();
					brushCopy.Opacity		= areaOpacity / 100d; 
					areaDeviceBrush.Brush	= brushCopy;
				}
				areaDeviceBrush.RenderTarget	= RenderTarget;
			}
			else 
			{
				areaDeviceBrush.RenderTarget	= null;
				areaDeviceBrush.Brush			= null;
			}
			
			ChartPanel panel			= cc.ChartPanels[cs.PanelIndex];

			Point startPoint			= TrendStartAnchor.GetPoint(cc, panel, cs);
			Point endPoint				= TrendEndAnchor.GetPoint(cc, panel, cs);
			Point startPoint2			= ParallelStartAnchor.GetPoint(cc, panel, cs);
			Point endPoint2				= startPoint2 + (endPoint - startPoint);
			bool left2right 			= startPoint.X <= endPoint.X;

			SharpDX.Vector2 startVec	= startPoint.ToVector2();
			SharpDX.Vector2 endVec		= endPoint.ToVector2();
			SharpDX.Vector2 startVec2	= startPoint2.ToVector2();
			SharpDX.Vector2 endVec2		= endPoint2.ToVector2();

			Point maxPoint				= GetExtendedPoint(startPoint, endPoint);
			Point maxPoint2				= ParallelStartAnchor.Time > DateTime.MinValue ? GetExtendedPoint(startPoint2, endPoint2) : new Point(-1, -1);
			Point minPoint				= GetExtendedPoint(endPoint, startPoint);
			Point minPoint2				= ParallelStartAnchor.Time > DateTime.MinValue ? GetExtendedPoint(endPoint2, startPoint2) : new Point(-1, -1);

			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? cc.SelectionBrush : Stroke.BrushDX;
			RenderTarget.DrawLine(startVec, endVec, tmpBrush, Stroke.Width, Stroke.StrokeStyle);
			
			if (DrawingState == DrawingState.Building && !isReadyForMovingSecondLeg) return;
			
			tmpBrush = IsInHitTest ? cc.SelectionBrush : ParallelStroke.BrushDX;
			RenderTarget.DrawLine(startVec2, endVec2, tmpBrush, ParallelStroke.Width, ParallelStroke.StrokeStyle);

			fillMainFig			= new SharpDX.Vector2[4];
			fillMainFig[0]		= startPoint2.ToVector2();
			fillMainFig[1]		= endPoint2.ToVector2();
			fillMainFig[2]		= endPoint.ToVector2();
			fillMainFig[3]		= startPoint.ToVector2();
			fillMainGeometry	= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

			SharpDX.Direct2D1.GeometrySink geometrySinkMain = fillMainGeometry.Open();

			geometrySinkMain.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
			geometrySinkMain.AddLines(fillMainFig);
			geometrySinkMain.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
			geometrySinkMain.Close();

			if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillMainGeometry, areaDeviceBrush.BrushDX);

			if (IsExtendedLinesLeft && left2right || IsExtendedLinesRight && !left2right)
			{
				if (minPoint.X > -1 || minPoint.Y > -1) RenderTarget.DrawLine(startVec, minPoint.ToVector2(), Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);
				if (minPoint2.X > -1 || minPoint2.Y > -1) RenderTarget.DrawLine(startVec2, minPoint2.ToVector2(), ParallelStroke.BrushDX, ParallelStroke.Width, ParallelStroke.StrokeStyle);

				if (minPoint2.Y > 0 && minPoint2.X < ChartPanel.X && minPoint2.Y < ChartPanel.H + ChartPanel.Y && minPoint.X > ChartPanel.X && minPoint.Y > ChartPanel.H + ChartPanel.Y
					|| minPoint.Y > 0 && minPoint.X < ChartPanel.X && minPoint.Y < ChartPanel.H + ChartPanel.Y && minPoint2.X > ChartPanel.X && minPoint2.Y > ChartPanel.H + ChartPanel.Y)
				{
					Point extLowLeftPoint	= new Point(ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillLeftFig				= new SharpDX.Vector2[5];
					fillLeftFig[0]			= startPoint2.ToVector2();
					fillLeftFig[1]			= minPoint2.ToVector2();
					fillLeftFig[2]			= extLowLeftPoint.ToVector2();
					fillLeftFig[3]			= minPoint.ToVector2();
					fillLeftFig[4]			= startPoint.ToVector2();
					
					if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
					
					fillLeftGeometry	= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkLeft = fillLeftGeometry.Open();
					geometrySinkLeft.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkLeft.AddLines(fillLeftFig);
					geometrySinkLeft.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkLeft.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillLeftGeometry, areaDeviceBrush.BrushDX);
				}
				else if (minPoint2.X > ChartPanel.X && minPoint2.Y < ChartPanel.Y && minPoint.X < ChartPanel.X && minPoint.Y < ChartPanel.H + ChartPanel.Y
						|| minPoint.X > ChartPanel.X && minPoint.Y < ChartPanel.Y && minPoint2.X < ChartPanel.X && minPoint2.Y < ChartPanel.H + ChartPanel.Y)
				{
					Point extUppLeftPoint	= new Point(ChartPanel.X, ChartPanel.Y);
					fillLeftFig				= new SharpDX.Vector2[5];
					fillLeftFig[0]			= startPoint2.ToVector2();
					fillLeftFig[1]			= minPoint2.ToVector2();
					fillLeftFig[2]			= extUppLeftPoint.ToVector2();
					fillLeftFig[3]			= minPoint.ToVector2();
					fillLeftFig[4]			= startPoint.ToVector2();
					
					if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
					
					fillLeftGeometry	= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkLeft = fillLeftGeometry.Open();
					geometrySinkLeft.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkLeft.AddLines(fillLeftFig);
					geometrySinkLeft.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkLeft.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillLeftGeometry, areaDeviceBrush.BrushDX);
				}
				else if (minPoint2.X < ChartPanel.W + ChartPanel.X && minPoint2.Y < ChartPanel.Y && minPoint.X > ChartPanel.W + ChartPanel.X && minPoint.Y < ChartPanel.H + ChartPanel.Y
						|| minPoint.X < ChartPanel.W + ChartPanel.X && minPoint.Y < ChartPanel.Y && minPoint2.X > ChartPanel.W + ChartPanel.X && minPoint2.Y < ChartPanel.H + ChartPanel.Y)
				{
					Point extUppRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.Y);
					fillLeftFig				= new SharpDX.Vector2[5];
					fillLeftFig[0]			= startPoint2.ToVector2();
					fillLeftFig[1]			= minPoint2.ToVector2();
					fillLeftFig[2]			= extUppRightPoint.ToVector2();
					fillLeftFig[3]			= minPoint.ToVector2();
					fillLeftFig[4]			= startPoint.ToVector2();
					
					if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
					
					fillLeftGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkLeft = fillLeftGeometry.Open();
					geometrySinkLeft.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkLeft.AddLines(fillLeftFig);
					geometrySinkLeft.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkLeft.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillLeftGeometry, areaDeviceBrush.BrushDX);
				}
				else if (minPoint2.Y > 0 && minPoint2.X > ChartPanel.W + ChartPanel.X && minPoint2.Y < ChartPanel.H + ChartPanel.Y && minPoint.X < ChartPanel.W + ChartPanel.X && minPoint.Y > ChartPanel.H + ChartPanel.Y
						|| minPoint.Y > 0 && minPoint.X > ChartPanel.W + ChartPanel.X && minPoint.Y < ChartPanel.H + ChartPanel.Y && minPoint2.X < ChartPanel.W + ChartPanel.X && minPoint2.Y > ChartPanel.H + ChartPanel.Y)
				{
					Point extLowRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillLeftFig				= new SharpDX.Vector2[5];
					fillLeftFig[0]			= startPoint2.ToVector2();
					fillLeftFig[1]			= minPoint2.ToVector2();
					fillLeftFig[2]			= extLowRightPoint.ToVector2();
					fillLeftFig[3]			= minPoint.ToVector2();
					fillLeftFig[4]			= startPoint.ToVector2();
					
					if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
					
					fillLeftGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkLeft = fillLeftGeometry.Open();
					geometrySinkLeft.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkLeft.AddLines(fillLeftFig);
					geometrySinkLeft.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkLeft.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillLeftGeometry, areaDeviceBrush.BrushDX);
				}
				else
				{
					Point extUppLeftPoint	= new Point(ChartPanel.X, ChartPanel.Y);
					Point extUppRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.Y);
					Point extLowLeftPoint	= new Point(ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					Point extLowRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillLeftFig				= new SharpDX.Vector2[4];
					fillLeftFig[0]			= startPoint2.ToVector2();
					
					if (startPoint.Y < endPoint.Y && startPoint.X < endPoint.X && endPoint2.Y > (ChartPanel.Y + ChartPanel.H) && startPoint2.X < ChartPanel.X)
						fillLeftFig[1]		= extUppLeftPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X > endPoint.X && endPoint2.Y > (ChartPanel.Y + ChartPanel.H) && startPoint2.X > (ChartPanel.X + ChartPanel.W))
						fillLeftFig[1]		= extUppRightPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X < endPoint.X && endPoint2.Y < ChartPanel.Y && startPoint2.X < ChartPanel.X)
						fillLeftFig[1]		= extLowLeftPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X > endPoint.X && endPoint2.Y < ChartPanel.Y && startPoint2.X > (ChartPanel.X +ChartPanel.W))
						fillLeftFig[1]		= extLowRightPoint.ToVector2();
					else
						fillLeftFig[1]		= minPoint2.ToVector2();
					
					if (startPoint.Y < endPoint.Y && startPoint.X < endPoint.X && endPoint.Y > (ChartPanel.Y + ChartPanel.H) && startPoint.X < ChartPanel.X)
						fillLeftFig[2]		= extUppLeftPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X > endPoint.X && endPoint.Y > (ChartPanel.Y + ChartPanel.H) && startPoint.X > (ChartPanel.X + ChartPanel.W))
						fillLeftFig[2]		= extUppRightPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X < endPoint.X && endPoint.Y < 0 && startPoint.X < ChartPanel.X)
						fillLeftFig[2]		= extLowLeftPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X > endPoint.X && endPoint.Y < ChartPanel.Y && startPoint.X > (ChartPanel.X + ChartPanel.W))
						fillLeftFig[2]		= extLowRightPoint.ToVector2();
					else
						fillLeftFig[2]		= minPoint.ToVector2();
					
					fillLeftFig[3]			= startPoint.ToVector2();
					
					if (fillLeftGeometry != null) fillLeftGeometry.Dispose();
					
					fillLeftGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkLeft = fillLeftGeometry.Open();
					geometrySinkLeft.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkLeft.AddLines(fillLeftFig);
					geometrySinkLeft.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkLeft.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillLeftGeometry, areaDeviceBrush.BrushDX);
				}
			}

			if (IsExtendedLinesRight && left2right || IsExtendedLinesLeft && !left2right)
			{
				if (maxPoint.X > -1 || maxPoint.Y > -1) RenderTarget.DrawLine(endVec, maxPoint.ToVector2(), Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);
				if (maxPoint2.X > -1 || maxPoint2.Y > -1) RenderTarget.DrawLine(endVec2, maxPoint2.ToVector2(), ParallelStroke.BrushDX, ParallelStroke.Width, ParallelStroke.StrokeStyle);

				if (maxPoint2.Y > 0 && maxPoint2.X < ChartPanel.X && maxPoint2.Y < ChartPanel.H + ChartPanel.Y && maxPoint.X > ChartPanel.X && maxPoint.Y > ChartPanel.H + ChartPanel.Y
					|| maxPoint.Y > 0 && maxPoint.X < ChartPanel.X && maxPoint.Y < ChartPanel.H + ChartPanel.Y && maxPoint2.X > ChartPanel.X && maxPoint2.Y > ChartPanel.H + ChartPanel.Y)
				{
					Point extLowLeftPoint	= new Point(ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillRightFig		= new SharpDX.Vector2[5];
					fillRightFig[0]		= endPoint2.ToVector2();
					fillRightFig[1]		= maxPoint2.ToVector2();
					fillRightFig[2]		= extLowLeftPoint.ToVector2();
					fillRightFig[3]		= maxPoint.ToVector2();
					fillRightFig[4]		= endPoint.ToVector2();
					
					if (fillRightGeometry != null) fillRightGeometry.Dispose();
					
					fillRightGeometry	= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkRight = fillRightGeometry.Open();
					geometrySinkRight.BeginFigure(new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkRight.AddLines(fillRightFig);
					geometrySinkRight.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkRight.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillRightGeometry, areaDeviceBrush.BrushDX);
				}
				else if (maxPoint2.X > ChartPanel.X && maxPoint2.Y < ChartPanel.Y && maxPoint.X < ChartPanel.X && maxPoint.Y < ChartPanel.H + ChartPanel.Y
						|| maxPoint.X > ChartPanel.X && maxPoint.Y < ChartPanel.Y && maxPoint2.X < ChartPanel.X && maxPoint2.Y < ChartPanel.H + ChartPanel.Y)
				{
					Point extUppLeftPoint	= new Point(ChartPanel.X, ChartPanel.Y);
					fillRightFig			= new SharpDX.Vector2[5];
					fillRightFig[0]			= endPoint2.ToVector2();
					fillRightFig[1]			= maxPoint2.ToVector2();
					fillRightFig[2]			= extUppLeftPoint.ToVector2();
					fillRightFig[3]			= maxPoint.ToVector2();
					fillRightFig[4]			= endPoint.ToVector2();
					
					if (fillRightGeometry != null) fillRightGeometry.Dispose();
					
					fillRightGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkRight = fillRightGeometry.Open();
					geometrySinkRight.BeginFigure(new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkRight.AddLines(fillRightFig);
					geometrySinkRight.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkRight.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillRightGeometry, areaDeviceBrush.BrushDX);
				}
				else if (maxPoint2.X < ChartPanel.W + ChartPanel.X && maxPoint2.Y < ChartPanel.Y && maxPoint.X > ChartPanel.W + ChartPanel.X && maxPoint.Y < ChartPanel.H + ChartPanel.Y
						|| maxPoint.X < ChartPanel.W + ChartPanel.X && maxPoint.Y < ChartPanel.Y && maxPoint2.X > ChartPanel.W + ChartPanel.X && maxPoint2.Y < ChartPanel.H + ChartPanel.Y)
				{
					Point extUppRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.Y);
					fillRightFig			= new SharpDX.Vector2[5];
					fillRightFig[0]			= endPoint2.ToVector2();
					fillRightFig[1]			= maxPoint2.ToVector2();
					fillRightFig[2]			= extUppRightPoint.ToVector2();
					fillRightFig[3]			= maxPoint.ToVector2();
					fillRightFig[4]			= endPoint.ToVector2();
					
					if (fillRightGeometry != null) fillRightGeometry.Dispose();
					
					fillRightGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkRight = fillRightGeometry.Open();
					geometrySinkRight.BeginFigure(new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkRight.AddLines(fillRightFig);
					geometrySinkRight.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkRight.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillRightGeometry, areaDeviceBrush.BrushDX);
				}
				else if (maxPoint2.Y > 0 && maxPoint2.X > ChartPanel.W + ChartPanel.X && maxPoint2.Y < ChartPanel.H + ChartPanel.Y && maxPoint.X < ChartPanel.W + ChartPanel.X && maxPoint.Y > ChartPanel.H + ChartPanel.Y
						|| maxPoint.Y > 0 && maxPoint.X > ChartPanel.W + ChartPanel.X && maxPoint.Y < ChartPanel.H + ChartPanel.Y && maxPoint2.X < ChartPanel.W + ChartPanel.X && maxPoint2.Y > ChartPanel.H + ChartPanel.Y)
				{
					Point extLowRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillRightFig			= new SharpDX.Vector2[5];
					fillRightFig[0]			= endPoint2.ToVector2();
					fillRightFig[1]			= maxPoint2.ToVector2();
					fillRightFig[2]			= extLowRightPoint.ToVector2();
					fillRightFig[3]			= maxPoint.ToVector2();
					fillRightFig[4]			= endPoint.ToVector2();
					
					if (fillRightGeometry != null) fillRightGeometry.Dispose();
					
					fillRightGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkRight = fillRightGeometry.Open();
					geometrySinkRight.BeginFigure(new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkRight.AddLines(fillRightFig);
					geometrySinkRight.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkRight.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillRightGeometry, areaDeviceBrush.BrushDX);
				}
				else
				{
					Point extUppRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.Y);
					Point extUppLeftPoint	= new Point(ChartPanel.X, ChartPanel.Y);
					Point extLowRightPoint	= new Point(ChartPanel.W + ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					Point extLowLeftPoint	= new Point(ChartPanel.X, ChartPanel.H + ChartPanel.Y);
					fillRightFig			= new SharpDX.Vector2[4];
					fillRightFig[0]			= endPoint2.ToVector2();
					
					if (startPoint.Y > endPoint.Y && startPoint.X < endPoint.X && endPoint2.X > (ChartPanel.X + ChartPanel.W) && startPoint2.Y > (ChartPanel.Y + ChartPanel.H))
						fillRightFig[1]		= extUppRightPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X > endPoint.X && endPoint2.X < ChartPanel.X && startPoint2.Y > (ChartPanel.Y + ChartPanel.H))
						fillRightFig[1]		= extUppLeftPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X < endPoint.X && endPoint2.X > (ChartPanel.X + ChartPanel.W) && startPoint2.Y < ChartPanel.Y)
						fillRightFig[1]		= extLowRightPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X > endPoint.X && endPoint2.X < ChartPanel.X && startPoint2.Y < ChartPanel.Y)
						fillRightFig[1]		= extLowLeftPoint.ToVector2();
					else
						fillRightFig[1] = maxPoint2.ToVector2();
					
					if (startPoint.Y > endPoint.Y && startPoint.X < endPoint.X && endPoint.X > (ChartPanel.X + ChartPanel.W) && startPoint.Y > (ChartPanel.Y + ChartPanel.H))
						fillRightFig[2]		= extUppRightPoint.ToVector2();
					else if (startPoint.Y > endPoint.Y && startPoint.X > endPoint.X && endPoint.X < ChartPanel.X && startPoint.Y > (ChartPanel.Y + ChartPanel.H))
						fillRightFig[2]		= extUppLeftPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X < endPoint.X && endPoint.X > (ChartPanel.X + ChartPanel.W) && startPoint.Y < ChartPanel.Y)
						fillRightFig[2]		= extLowRightPoint.ToVector2();
					else if (startPoint.Y < endPoint.Y && startPoint.X > endPoint.X && endPoint.X < ChartPanel.X && startPoint.Y < ChartPanel.Y)
						fillRightFig[2]		= extLowLeftPoint.ToVector2();
					else
						fillRightFig[2]		= maxPoint.ToVector2();
					
					fillRightFig[3]			= endPoint.ToVector2();
					
					if (fillRightGeometry != null) fillRightGeometry.Dispose();
					
					fillRightGeometry		= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);

					SharpDX.Direct2D1.GeometrySink geometrySinkRight = fillRightGeometry.Open();
					geometrySinkRight.BeginFigure(new SharpDX.Vector2((float)endPoint.X, (float)endPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySinkRight.AddLines(fillRightFig);
					geometrySinkRight.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySinkRight.Close();

					if (areaDeviceBrush != null && areaDeviceBrush.RenderTarget != null && areaDeviceBrush.BrushDX != null) RenderTarget.FillGeometry(fillRightGeometry, areaDeviceBrush.BrushDX);
				}
			}

			PriceLevel midLine = PriceLevels.Find(x => x.Value == 50);
			if (midLine != null) midLine.IsVisible = ShowMidline;
			else
			{
				midLine = new PriceLevel(50, Stroke.Brush, 1, DashStyleHelper.Dot, 100);
				midLine.IsVisible = ShowMidline;
				PriceLevels.Add(midLine);
			}

			PriceLevel mmUp = PriceLevels.Find(x => x.Value == 200);
			if (mmUp != null) mmUp.IsVisible = ShowMeasuredMoves;
			else
			{
				mmUp = new PriceLevel(200, Stroke.Brush, 1, DashStyleHelper.Dash, 100);
				mmUp.IsVisible = ShowMidline;
				PriceLevels.Add(mmUp);
			}

			PriceLevel mmDown = PriceLevels.Find(x => x.Value == -100);
			if (mmDown != null) mmDown.IsVisible = ShowMeasuredMoves;
			else
			{
				mmDown = new PriceLevel(-100, Stroke.Brush, 1, DashStyleHelper.Dash, 100);
				mmDown.IsVisible = ShowMidline;
				PriceLevels.Add(mmDown);
			}

			SetAllPriceLevelsRenderTarget();

			foreach (PriceLevel trendLevel in PriceLevels.Where(tl => tl.IsVisible && tl.Stroke != null))
			{
				Vector startDir = trendLevel.Value / 100 * (startPoint2 - startPoint);
				Vector lineVector = endPoint - startPoint;
				Point newStartPoint = new Point(startPoint.X + startDir.X, startPoint.Y + startDir.Y);
				Point newEndPoint = new Point(newStartPoint.X + lineVector.X, newStartPoint.Y + lineVector.Y);

				RenderTarget.DrawLine(newStartPoint.ToVector2(), newEndPoint.ToVector2(), trendLevel.Stroke.BrushDX, trendLevel.Stroke.Width, trendLevel.Stroke.StrokeStyle);

				Point maxPoint3 = GetExtendedPoint(newStartPoint, newEndPoint);
				Point minPoint3 = GetExtendedPoint(newEndPoint, newStartPoint);

				if ((IsExtendedLinesLeft && left2right || IsExtendedLinesRight && !left2right) && (minPoint3.X > -1 || minPoint3.Y > -1))
					RenderTarget.DrawLine(newStartPoint.ToVector2(), minPoint3.ToVector2(), trendLevel.Stroke.BrushDX, trendLevel.Stroke.Width, trendLevel.Stroke.StrokeStyle);

				if ((IsExtendedLinesRight && left2right || IsExtendedLinesLeft && !left2right) && (maxPoint3.X > -1 || maxPoint3.Y > -1))
					RenderTarget.DrawLine(newEndPoint.ToVector2(), maxPoint3.ToVector2(), trendLevel.Stroke.BrushDX, trendLevel.Stroke.Width, trendLevel.Stroke.StrokeStyle);
			}
		}

		private void SetParallelLine(ChartControl cc, bool initialSet)
		{
			// when intial set is true, user just finished their trend line, we need to initialize
			// a parallel line somewhere, copy the first line (StartAnchor -> EndAnchor) starting where user clicked
			// as second line (Start2Anchor -> End2Anchor)
			// if initial set is false, this was called from an edit, user could have edited trend line, we need to 
			// update parallel anchors to stay parallel in price

			// NOTE: use pixel values for line time conversion but time
			// can end up non-linear which would not be correct
			if (initialSet)
			{
				if (cc.BarSpacingType != BarSpacingType.TimeBased)
				{
					ParallelStartAnchor.SlotIndex = TrendStartAnchor.SlotIndex;
					ParallelStartAnchor.Time = cc.GetTimeBySlotIndex(ParallelStartAnchor.SlotIndex);
				}
				else
					ParallelStartAnchor.Time = TrendStartAnchor.Time;

				ParallelStartAnchor.Price		= TrendStartAnchor.Price;
				ParallelStartAnchor.StartAnchor = TrendStartAnchor;
			}
			ParallelStartAnchor.IsEditing	= false;
		}
	}

	public static partial class Draw
	{
		private static Channel ChannelCore(NinjaScriptBase owner, string tag, bool isAutoScale,
			int anchor1BarsAgo, DateTime anchor1Time, double anchor1Y,
			int anchor2BarsAgo, DateTime anchor2Time, double anchor2Y,
			int anchor3BarsAgo, DateTime anchor3Time, double anchor3Y, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = string.Format("{0}{1}", GlobalDrawingToolManager.GlobalDrawingToolTagPrefix, tag);

			Channel channel = DrawingTool.GetByTagOrNew(owner, typeof(Channel), tag, templateName) as Channel;
			if (channel == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(channel, tag, isAutoScale, owner, isGlobal);

			ChartAnchor		startAnchor		= DrawingTool.CreateChartAnchor(owner, anchor1BarsAgo, anchor1Time, anchor1Y);
			ChartAnchor		endAnchor		= DrawingTool.CreateChartAnchor(owner, anchor2BarsAgo, anchor2Time, anchor2Y);
			ChartAnchor		trendAnchor		= DrawingTool.CreateChartAnchor(owner, anchor3BarsAgo, anchor3Time, anchor3Y);

			startAnchor.CopyDataValues(channel.TrendStartAnchor);
			endAnchor.CopyDataValues(channel.TrendEndAnchor);
			trendAnchor.CopyDataValues(channel.ParallelStartAnchor);
			channel.SetState(State.Active);
			return channel;
		}

		/// <summary>
		/// Draws a channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x value) of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value of the 1st anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="anchor2Y">The y value of the 2nd anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x value) of the 3rd anchor point</param>
		/// <param name="anchor3Y">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static Channel Channel(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y,
												int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y)
		{
			return ChannelCore(owner, tag, isAutoScale,
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, false, null);
		}

		/// <summary>
		/// Draws a channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1Time">The time of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value of the 1st anchor point</param>
		/// <param name="anchor2Time">The time of the 2nd anchor point</param>
		/// <param name="anchor2Y">The y value of the 2nd anchor point</param>
		/// <param name="anchor3Time">The time of the 3rd anchor point</param>
		/// <param name="anchor3Y">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static Channel Channel(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time,
												double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y)
		{
			return ChannelCore(owner, tag, isAutoScale,
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, false, null);
		}

		/// <summary>
		/// Draws a channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x value) of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value of the 1st anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="anchor2Y">The y value of the 2nd anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x value) of the 3rd anchor point</param>
		/// <param name="anchor3Y">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Channel Channel(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y,
												int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, bool isGlobal, string templateName)
		{
			return ChannelCore(owner, tag, isAutoScale,
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1Time">The time of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value of the 1st anchor point</param>
		/// <param name="anchor2Time">The time of the 2nd anchor point</param>
		/// <param name="anchor2Y">The y value of the 2nd anchor point</param>
		/// <param name="anchor3Time">The time of the 3rd anchor point</param>
		/// <param name="anchor3Y">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Channel Channel(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time,
												double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, bool isGlobal, string templateName)
		{
			return ChannelCore(owner, tag, isAutoScale,
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, isGlobal, templateName);
		}
	}
}