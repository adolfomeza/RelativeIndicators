/*
*	Support Line Drawing Tool made with ♡ by beo
* 	Last edit 02/26/2021
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
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;

#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Line IDrawingTool.
	/// </summary>
	public class SupportLine : DrawingTool
	{
		private ChartControl 		mycc;
		private MenuItem 			myMenuItem1;
	    private MenuItem 			myMenuItem2;
		private Separator 			separatorItem;

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor }; } }
		[Display(Order = 2)]
		public ChartAnchor	EndAnchor		{ get; set; }
		[Display(Order = 1)]
		public ChartAnchor StartAnchor		{ get; set; }

		public override object Icon
        {
            get
            {
                Grid myCanvas = new Grid { Height = 16, Width = 16 };
                System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
                p.Fill = Application.Current.FindResource("FontActionBrush") as Brush ?? Brushes.Blue;;
				p.Data = System.Windows.Media.Geometry.Parse("M 0 8 L 16 8 L 16 9 L 0 9 Z");
                myCanvas.Children.Add(p);
                return myCanvas;
            }
        }

		private	const	double			cursorSensitivity = 15;
		private			ChartAnchor		editingAnchor;

		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptGeneral", Name = "Support Line", Order = 99)]
		public Stroke Stroke { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptGeneral", Name = "Extend Left", Order = 100)]
		public bool ExtendLeft { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptGeneral", Name = "Extend Right", Order = 101)]
		public bool ExtendRight { get; set; }

		public override bool SupportsAlerts { get { return true; } }

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
			if (!IsVisible) return;
			if (Anchors.Any(a => !a.IsEditing)) foreach (ChartAnchor anchor in Anchors) { MinValue = Math.Min(anchor.Price, MinValue); MaxValue = Math.Max(anchor.Price, MaxValue); }
		}

		public override Cursor GetCursor(ChartControl cc, ChartPanel cp, ChartScale cs, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:	return Cursors.Pen;
				case DrawingState.Moving:	return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:	return IsLocked ? Cursors.No : Cursors.SizeWE;
				default:
					Point startPoint = StartAnchor.GetPoint(cc, cp, cs);
					ChartAnchor closest = GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);
					if (closest != null) return IsLocked ? Cursors.Arrow : Cursors.SizeWE;
					Point	endPoint		= EndAnchor.GetPoint(cc, cp, cs);
					Point	minPoint		= startPoint;
					Point	maxPoint		= endPoint;
					bool left2right = startPoint.X <= endPoint.X;
					minPoint = (left2right && ExtendLeft) || (!left2right && ExtendRight) ? GetExtendedPoint(cc, cp, cs, EndAnchor, StartAnchor) : startPoint;
					maxPoint = (left2right && ExtendRight) || (!left2right && ExtendLeft) ? GetExtendedPoint(cc, cp, cs, StartAnchor, EndAnchor) : endPoint;
					return MathHelper.IsPointAlongVector(point, minPoint, maxPoint - minPoint, cursorSensitivity) ? IsLocked ? Cursors.Arrow : Cursors.SizeAll : null;
			}
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			yield return new AlertConditionItem { Name = "Support Line", ShouldOnlyDisplayName = true };
		}

		public sealed override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			if (DrawingState == DrawingState.Building) return new Point[0];
			ChartPanel	cp	= cc.ChartPanels[cs.PanelIndex];
			Point		endPoint	= EndAnchor.GetPoint(cc, cp, cs);
			return new[]{ new Point(StartAnchor.GetPoint(cc, cp, cs).X, endPoint.Y), endPoint };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl cc, ChartScale cs)
		{
			if (values.Length < 1) return false;
			ChartPanel cp = cc.ChartPanels[PanelIndex];
			// h line and v line have much more simple alert handling
			// get start / end points of what is absolutely shown for our vector
			Point lineStartPoint	= StartAnchor.GetPoint(cc, cp, cs);
			Point lineEndPoint		= EndAnchor.GetPoint(cc, cp, cs);

			// need to adjust vector to rendered extensions
			Point maxPoint = GetExtendedPoint(cc, cp, cs, StartAnchor, EndAnchor);
			Point minPoint = GetExtendedPoint(cc, cp, cs,EndAnchor, StartAnchor);
			lineStartPoint = minPoint;
			lineEndPoint = maxPoint;

			double minLineX = double.MaxValue;
			double maxLineX = double.MinValue;

			foreach (Point point in new[]{lineStartPoint, lineEndPoint})
			{
				minLineX = Math.Min(minLineX, point.X);
				maxLineX = Math.Max(maxLineX, point.X);
			}

			// first thing, if our smallest x is greater than most recent bar, we have nothing to do yet.
			// do not try to check Y because lines could cross through stuff
			double firstBarX = values[0].ValueType == ChartAlertValueType.StaticValue ? minLineX : cc.GetXByTime(values[0].Time);
			double firstBarY = cs.GetYByValue(values[0].Value);

			// dont have to take extension into account as its already handled in min/max line x

			// bars completely passed our line
			if (maxLineX < firstBarX) return false;

			// bars not yet to our line
			if (minLineX > firstBarX) return false;

			// NOTE: normalize line points so the leftmost is passed first. Otherwise, our vector
			// math could end up having the line normal vector being backwards if user drew it backwards.
			// but we dont care the order of anchors, we want 'up' to mean 'up'!
			Point leftPoint		= lineStartPoint.X < lineEndPoint.X ? lineStartPoint : lineEndPoint;
			Point rightPoint	= lineEndPoint.X > lineStartPoint.X ? lineEndPoint : lineStartPoint;

			Point barPoint = new Point(firstBarX, firstBarY);
			// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(leftPoint, rightPoint, barPoint);
			// for vertical things, think of a vertical line rotated 90 degrees to lay flat, where it's normal vector is 'up'
			switch (condition)
			{
				case Condition.Greater:			return pointLocation == MathHelper.PointLineLocation.LeftOrAbove;
				case Condition.GreaterEqual:	return pointLocation == MathHelper.PointLineLocation.LeftOrAbove || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Less:			return pointLocation == MathHelper.PointLineLocation.RightOrBelow;
				case Condition.LessEqual:		return pointLocation == MathHelper.PointLineLocation.RightOrBelow || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Equals:			return pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.NotEqual:		return pointLocation != MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.CrossAbove:
				case Condition.CrossBelow:
					Predicate<ChartAlertValue> predicate = v =>
					{
						double barX = cc.GetXByTime(v.Time);
						double barY = cs.GetYByValue(v.Value);
						Point stepBarPoint = new Point(barX, barY);
						MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(leftPoint, rightPoint, stepBarPoint);
						if (condition == Condition.CrossAbove) return ptLocation == MathHelper.PointLineLocation.LeftOrAbove;
						return ptLocation == MathHelper.PointLineLocation.RightOrBelow;
					};
					return MathHelper.DidPredicateCross(values, predicate);
			}

			return false;
		}

		public override bool IsVisibleOnChart(ChartControl cc, ChartScale cs, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building) return true;

			DateTime	minTime = Core.Globals.MaxDate;
			DateTime	maxTime = Core.Globals.MinDate;

			ChartPanel cp = cc.ChartPanels[PanelIndex];
			Point startPoint = StartAnchor.GetPoint(cc, cp, cs);
			Point endPoint = EndAnchor.GetPoint(cc, cp, cs);
			Point minPoint = ExtendLeft ? GetExtendedPoint(cc, cp, cs, EndAnchor, StartAnchor) : startPoint;
			Point maxPoint = ExtendRight ? GetExtendedPoint(cc, cp, cs, StartAnchor, EndAnchor) : endPoint;

			foreach (Point pt in new[] { minPoint, maxPoint })
			{
				DateTime time = cc.GetTimeByX((int) pt.X);
				if (time > maxTime) maxTime = time;
				if (time < minTime) minTime = time;
			}

			// check offscreen vertically. make sure to check the line doesnt cut through the scale, so check both are out
			if ((StartAnchor.Price < cs.MinValue || StartAnchor.Price > cs.MaxValue) && !IsAutoScale) return false; // horizontal line only has one anchor to whiff

			// hline extends, but otherwise try to check if line horizontally crosses through visible chart times in some way
			if ((minTime > lastTimeOnChart || maxTime < firstTimeOnChart)) return false;

			return true;
		}

		public override void OnMouseDown(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						StartAnchor.IsEditing = false;

						// give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;
					}

					// is initial building done (both anchors set)
					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						StartAnchor.Price = EndAnchor.Price;
						DrawingState = DrawingState.Normal;
						IsSelected = false;
					}
					break;
				case DrawingState.Normal:
					if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) { IsSelected = false; break; }
					Point point = dataPoint.GetPoint(cc, cp, cs);
					// see if they clicked near a point to edit, if so start editing
					editingAnchor = GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);

					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else
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

			if (DrawingState == DrawingState.Building && EndAnchor.IsEditing) dataPoint.CopyDataValues(EndAnchor);
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
			{
                editingAnchor.Time = dataPoint.Time;
				editingAnchor.SlotIndex	= dataPoint.SlotIndex;
			}
			else if (DrawingState == DrawingState.Moving) foreach (ChartAnchor anchor in Anchors) anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
		}

		public override void OnMouseUp(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing) DrawingState = DrawingState.Normal;
			if (editingAnchor != null) editingAnchor.IsEditing = false;
			editingAnchor = null;
		}

		private void AddMenuHandlers(ChartControl cc)
		{
			if (cc == null) return;
			mycc = cc;
			mycc.ContextMenuOpening += ChartControl_ContextMenuOpening;
            mycc.ContextMenuClosing += ChartControl_ContextMenuClosing;
			
			myMenuItem1 = new MenuItem { Header = "Extend Left", IsCheckable = true, IsChecked = this.ExtendLeft };
			myMenuItem2 = new MenuItem { Header = "Extend Right", IsCheckable = true, IsChecked = this.ExtendRight };
			separatorItem  = new Separator { Style = Application.Current.TryFindResource("MainMenuSeparator") as Style };
			
			myMenuItem1.Click += MyMenuItem1_Click;
			myMenuItem2.Click += MyMenuItem2_Click;
		}
		
		private void RemoveMenuHandlers()
		{
			if (mycc == null || myMenuItem1 == null || myMenuItem2 == null) return;

			myMenuItem1.Click -= MyMenuItem1_Click;
            myMenuItem2.Click -= MyMenuItem2_Click;
			
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

			if(mycc.ContextMenu.Items.Contains(separatorItem))
            {
                mycc.ContextMenu.Items.Remove(separatorItem);
            }
		}

		private void ChartControl_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
           if(mycc.ContextMenu.Items.Contains(myMenuItem1)) mycc.ContextMenu.Items.Remove(myMenuItem1);
           if(mycc.ContextMenu.Items.Contains(myMenuItem2)) mycc.ContextMenu.Items.Remove(myMenuItem2);
		   if(mycc.ContextMenu.Items.Contains(separatorItem)) mycc.ContextMenu.Items.Remove(separatorItem);
        }

        private void ChartControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
			if (!this.IsSelected) return;
			if (mycc.ContextMenu.Items.Contains(myMenuItem2) == false) mycc.ContextMenu.Items.Insert(3, myMenuItem2);
			if (mycc.ContextMenu.Items.Contains(myMenuItem1) == false) mycc.ContextMenu.Items.Insert(4, myMenuItem1);
			if (mycc.ContextMenu.Items.Contains(separatorItem) == false) mycc.ContextMenu.Items.Insert(5, separatorItem);
        }
		
        private void MyMenuItem1_Click(object sender, RoutedEventArgs e)
        {
			ExtendLeft = !ExtendLeft;
			this.ForceRefresh();
        }

        private void MyMenuItem2_Click(object sender, RoutedEventArgs e)
        {
			ExtendRight = !ExtendRight;
			this.ForceRefresh();
        }

		public override void OnRender(ChartControl cc, ChartScale cs)
		{
			// Here we capture ChartControl and create our menu items
			if (mycc == null) AddMenuHandlers(cc);

			if (Stroke == null)	return;
			Stroke.RenderTarget									= RenderTarget;
			SharpDX.Direct2D1.AntialiasMode	oldAntiAliasMode	= RenderTarget.AntialiasMode;
			SharpDX.Direct2D1.Brush			tmpBrush			= IsInHitTest ? cc.SelectionBrush : Stroke.BrushDX;
			RenderTarget.AntialiasMode							= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			ChartPanel						panel				= cc.ChartPanels[cs.PanelIndex];
			Point							startPoint			= StartAnchor.GetPoint(cc, panel, cs);

			// align to full pixel to avoid unneeded aliasing
			double							strokePixAdj		= ((double)(Stroke.Width % 2)).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector							pixelAdjustVec		= new Vector(strokePixAdj, strokePixAdj);

			Point					endPoint					= EndAnchor.GetPoint(cc, panel, cs);
			Point					newStartPoint				= new Point(startPoint.X, endPoint.Y);
			bool 					left2right					= startPoint.X <= endPoint.X;

			// convert our start / end pixel points to directx 2d vectors
			Point					endPointAdjusted	= endPoint + pixelAdjustVec;
			SharpDX.Vector2			endVec				= endPointAdjusted.ToVector2();
			Point					startPointAdjusted	= newStartPoint + pixelAdjustVec;
			SharpDX.Vector2			startVec			= startPointAdjusted.ToVector2();
			if (ExtendLeft && !ExtendRight)
			{
				endPointAdjusted	= (left2right ? endPoint : newStartPoint) + pixelAdjustVec;
				endVec				= endPointAdjusted.ToVector2();
				startPointAdjusted	= new Point(panel.X, endPoint.Y) + pixelAdjustVec;
				startVec			= startPointAdjusted.ToVector2();
			}
			else if (ExtendRight && !ExtendLeft)
			{
				endPointAdjusted	= new Point(panel.X + panel.W, endPoint.Y) + pixelAdjustVec;
				endVec				= endPointAdjusted.ToVector2();
				startPointAdjusted	= (left2right ? newStartPoint : endPoint) + pixelAdjustVec;
				startVec			= startPointAdjusted.ToVector2();
			}
			else if (ExtendLeft && ExtendRight)
			{
				endPointAdjusted	= new Point(panel.X + panel.W, endPoint.Y) + pixelAdjustVec;
				endVec				= endPointAdjusted.ToVector2();
				startPointAdjusted	= new Point(panel.X, endPoint.Y) + pixelAdjustVec;
				startVec			= startPointAdjusted.ToVector2();
			}
			RenderTarget.DrawLine(startVec, endVec, tmpBrush, Stroke.Width, Stroke.StrokeStyle);
			return;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name            = "Support Line";
				DrawingState	= DrawingState.Building;
				DisplayOnChartsMenus = false;

				EndAnchor	= new ChartAnchor
				{
					IsEditing		= true,
					DrawingTool		= this,
					DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd,
					IsBrowsable		= true
				};

				StartAnchor	= new ChartAnchor
				{
					IsEditing		= true,
					DrawingTool		= this,
					DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart,
					IsBrowsable		= true
				};

				Stroke = new Stroke(Brushes.CornflowerBlue, 2f);
				ExtendLeft = false;
				ExtendRight = false;
			}
			else if (State == State.Terminated)
			{
				Dispose();
                RemoveMenuHandlers();
			}
		}
	}

	public static partial class Draw
	{
		private static T DrawSupportLineTypeCore<T>(NinjaScriptBase owner, bool isAutoScale, string tag,
										int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
										Brush brush, DashStyleHelper dashStyle, int width, bool isGlobal, string templateName) where T : SupportLine
		{
			if (owner == null) throw new ArgumentException("owner");
			if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = string.Format("{0}{1}", GlobalDrawingToolManager.GlobalDrawingToolTagPrefix, tag);

			T lineT = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;

			if (lineT == null) return null;

			else if (startTime == Core.Globals.MinDate && endTime == Core.Globals.MinDate && startBarsAgo == int.MinValue && endBarsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			DrawingTool.SetDrawingToolCommonValues(lineT, tag, isAutoScale, owner, isGlobal);

			// dont nuke existing anchor refs on the instance
			ChartAnchor startAnchor;

            startAnchor				= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
            ChartAnchor endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);
            startAnchor.CopyDataValues(lineT.StartAnchor);
            endAnchor.CopyDataValues(lineT.EndAnchor);

			if (brush != null) lineT.Stroke = new Stroke(brush, dashStyle, width) { RenderTarget = lineT.Stroke.RenderTarget };

			lineT.SetState(State.Active);
			return lineT;
		}

		// line overloads
		private static SupportLine SupportLine(NinjaScriptBase owner, bool isAutoScale, string tag, int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return DrawSupportLineTypeCore<SupportLine>(owner, isAutoScale, tag, startBarsAgo, startTime, startY, endBarsAgo, endTime, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return SupportLine(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return SupportLine(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return SupportLine(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () => SupportLine(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () => SupportLine(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, string templateName)
		{
			return DrawSupportLineTypeCore<SupportLine>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, null, DashStyleHelper.Dash, 0, false, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, string templateName)
		{
			return DrawSupportLineTypeCore<SupportLine>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, null, DashStyleHelper.Dash, 0, false, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return DrawSupportLineTypeCore<SupportLine>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static SupportLine SupportLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return DrawSupportLineTypeCore<SupportLine>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}
	}
}
