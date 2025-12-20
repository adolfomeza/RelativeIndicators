/*
*	Measured Move Drawing Tool made with ♡ by beo
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
	/// Represents an interface that exposes information regarding a Measured Move IDrawingTool.
	/// </summary>
	public class MeasuredMove : DrawingTool 
	{
		private ChartControl 	mycc;
		private MenuItem 		myMenuItem1;
		private Separator 		separatorItem;
		private bool 			hideLeg1 = false;

		private	const	double			cursorSensitivity = 15;
		private			ChartAnchor		editingAnchor;
		private			bool			isReadyForMovingSecondLeg;
		private			bool			updateEndAnc;
		private 		int				movingLeg = 0;

		public override object Icon
        {
            get
            {
                Grid myCanvas = new Grid { Height = 16, Width = 16 };
                System.Windows.Shapes.Path p1 = new System.Windows.Shapes.Path();
                System.Windows.Shapes.Path p2 = new System.Windows.Shapes.Path();
                p1.Fill = p2.Fill = Application.Current.FindResource("FontActionBrush") as Brush ?? Brushes.Blue;
                p1.Data = System.Windows.Media.Geometry.Parse("M 1 4 L 6 4 L 6 5 L 4 5 L 4 14 L 6 14 L 6 15 L 1 15 L 1 14 L 3 14 L 3 5 L 1 5 Z");
                p2.Data = System.Windows.Media.Geometry.Parse("M 10 1 L 15 1 L 15 2 L 13 2 L 13 11 L 15 11 L 15 12 L 10 12 L 10 11 L 12 11 L 12 2 L 10 2 Z");
                myCanvas.Children.Add(p1);
                myCanvas.Children.Add(p2);
                return myCanvas;
            }
        }

		public override IEnumerable<ChartAnchor> Anchors {
			get { 
				return new[] {
					Leg1StartAnchor,
					Leg1EndAnchor,
					Leg2StartAnchor,
					Leg2EndAnchor,
					Leg2EndLeftAnchor,
					Leg2EndRightAnchor
				}; 
			}
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "Leg 1", GroupName = "NinjaScriptLines", Order = 1)]
		public Stroke Stroke { get; set; }

		[Display(Order = 0), ExcludeFromTemplate]
		public ChartAnchor Leg1StartAnchor { get; set; }

		[Display(Order = 10), ExcludeFromTemplate]
		public ChartAnchor Leg1EndAnchor { get; set; }

		[Display(Order = 20), ExcludeFromTemplate]
		public ChartAnchor Leg2StartAnchor { get; set; }

		[Display(Order = 30), ExcludeFromTemplate]
		public ChartAnchor Leg2EndAnchor { get; set; }

		[Display(Order = 40), ExcludeFromTemplate]
		public ChartAnchor Leg2EndLeftAnchor { get; set; }

		[Display(Order = 50), ExcludeFromTemplate]
		public ChartAnchor Leg2EndRightAnchor { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "Leg 2", GroupName = "NinjaScriptLines", Order = 2)]
		public Stroke ParallelStroke { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "Draw Serifs", GroupName = "NinjaScriptLines", Order = 3)]
		public bool DrawSerifs { get; set; }

		[Range(1, 50)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Serifs Length", GroupName = "NinjaScriptLines", Order = 4)]
		public int SerifsLength { get; set; }

		public override bool SupportsAlerts { get { return false; } }

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);
			MeasuredMove mm = ninjaScript as MeasuredMove;
			if (mm != null) mm.isReadyForMovingSecondLeg = isReadyForMovingSecondLeg;
		}

		protected override void Dispose(bool disposing) { base.Dispose(disposing); }

		protected override void OnStateChange()
		{
			switch (State)
			{
				case State.SetDefaults:
					Description				= "Measured Move";
					Name					= "Measured Move";
					DisplayOnChartsMenus 	= false;
					DrawingState			= DrawingState.Building;
					Leg1StartAnchor			= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg1Start" };
					Leg1EndAnchor			= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg1End" };
					Leg2StartAnchor			= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg2Start", Time = DateTime.MinValue };
					Leg2EndAnchor			= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg2End", Time = DateTime.MinValue };
					Leg2EndLeftAnchor		= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg2EndLeft", Time = DateTime.MinValue };
					Leg2EndRightAnchor		= new ChartAnchor { IsEditing = true, DrawingTool = this, IsBrowsable = true, DisplayName = "Leg2EndRight", Time = DateTime.MinValue };
					ParallelStroke			= new Stroke(Brushes.DarkOrange, 2f);
					Stroke					= new Stroke(Brushes.DarkOrange, 2f);
					DrawSerifs				= true;
					SerifsLength			= 2;
					break;
				case State.Terminated:
					Dispose();
					RemoveMenuHandlers();
					break;
			}
		}

		public override Cursor GetCursor(ChartControl cc, ChartPanel cp, ChartScale cs, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building	: return Cursors.Pen;
				case DrawingState.Moving	: return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing	:
					if (editingAnchor == null) return null;
					if (IsLocked) return Cursors.Arrow;
					return (editingAnchor == Leg2EndLeftAnchor || editingAnchor == Leg2EndRightAnchor) ? Cursors.SizeWE : (editingAnchor == Leg2EndAnchor) ? Cursors.ScrollWE : (editingAnchor == Leg2StartAnchor) ? Cursors.SizeAll : Cursors.SizeNWSE;
				default:

					Point startAnchorPixelPoint		= Leg1StartAnchor.GetPoint(cc, cp, cs);
					Point startAnchor2PixelPoint	= Leg2StartAnchor.GetPoint(cc, cp, cs);

					ChartAnchor closest				= GetClosestAnchor(cc, cp, cs, cursorSensitivity, point);

					if (closest != null)
					{
						movingLeg = closest == Leg2StartAnchor ? 2 : 0;
						if (IsLocked) return Cursors.Arrow;
						return (closest == Leg2EndLeftAnchor || closest == Leg2EndRightAnchor) ? Cursors.SizeWE : (closest == Leg2EndAnchor) ? Cursors.ScrollWE : (closest == Leg2StartAnchor) ? Cursors.SizeAll : Cursors.SizeNWSE;
					}
						
					Point	endAnchorPixelPoint		= Leg1EndAnchor.GetPoint(cc, cp, cs);
					Point	endAnchor2PixelPoint	= Leg2EndAnchor.GetPoint(cc, cp, cs);
					Point 	endAnchor2LeftPixelPoint = Leg2EndLeftAnchor.GetPoint(cc, cp, cs);
					Point 	endAnchor2RightPixelPoint = Leg2EndRightAnchor.GetPoint(cc, cp, cs);
					Vector	totalVector				= endAnchorPixelPoint - startAnchorPixelPoint;
					Vector	totalVector2			= endAnchor2PixelPoint - startAnchor2PixelPoint;
					Vector  totalVector3			= endAnchor2RightPixelPoint - endAnchor2LeftPixelPoint;

					if (MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, totalVector, cursorSensitivity))
					{
						movingLeg = 1;
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
					else if (MathHelper.IsPointAlongVector(point, startAnchor2PixelPoint, totalVector2, cursorSensitivity))
					{
						movingLeg = 2;
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
					else if (MathHelper.IsPointAlongVector(point, endAnchor2LeftPixelPoint, totalVector3, cursorSensitivity))
					{
						movingLeg = 2;
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
					movingLeg = 0;
					return null;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			ChartPanel	cp	= cc.ChartPanels[cs.PanelIndex];
			if (DrawingState == DrawingState.Building) return new Point[0];
			return new[] { 
				Leg1StartAnchor.GetPoint(cc, cp, cs), 
				Leg1EndAnchor.GetPoint(cc, cp, cs),
				Leg2StartAnchor.GetPoint(cc, cp, cs),
				Leg2EndLeftAnchor.GetPoint(cc, cp, cs), 
				Leg2EndAnchor.GetPoint(cc, cp, cs), 
				Leg2EndRightAnchor.GetPoint(cc, cp, cs)
			};
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (Anchors.Any(a => a.Time >= firstTimeOnChart && a.Time <= lastTimeOnChart)) return true;

			ChartPanel	panel		= chartControl.ChartPanels[chartScale.PanelIndex];
			Point		startPoint	= Leg1StartAnchor.GetPoint(chartControl, panel, chartScale);
			Point		endPoint	= Leg1EndAnchor.GetPoint(chartControl, panel, chartScale);
			Point		startPoint2	= Leg2StartAnchor.GetPoint(chartControl, panel, chartScale);
			Point		endPoint2	= startPoint2 + (endPoint - startPoint);

			Point		maxPoint	= GetExtendedPoint(startPoint, endPoint);
			Point		maxPoint2	= GetExtendedPoint(startPoint2, endPoint2);
			Point		minPoint	= GetExtendedPoint(endPoint, startPoint);
			Point		minPoint2	= GetExtendedPoint(endPoint2, startPoint2);
			Point[]		points		= { maxPoint, maxPoint2, minPoint, minPoint2 };
			double		minX		= points.Select(p => p.X).Min();
			double		maxX		= points.Select(p => p.X).Max();

			DateTime	minTime		= chartControl.GetTimeByX((int)minX);
			DateTime	startTime	= chartControl.GetTimeByX((int)startPoint.X);
			DateTime	endTime		= chartControl.GetTimeByX((int)endPoint.X);
			DateTime	maxTime		= chartControl.GetTimeByX((int)maxX);

			// first check if any anchor is in visible range
			foreach (DateTime time in new[] { minTime, startTime, endTime, maxTime })
				if (time >= firstTimeOnChart && time <= lastTimeOnChart)
					return true;

			// check crossthrough and keep in mind the anchors could be 'backwards' 
			if ((minTime <= firstTimeOnChart && maxTime >= lastTimeOnChart) || (startTime <= firstTimeOnChart && endTime >= lastTimeOnChart)
				|| (endTime <= firstTimeOnChart && startTime >= lastTimeOnChart))
				return true;

			return false;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible) return;

			if (Anchors.Any(a => !a.IsEditing))
				foreach (ChartAnchor anchor in Anchors)
				{
					MinValue = Math.Min(anchor.Price, MinValue);
					MaxValue = Math.Max(anchor.Price, MaxValue);
				}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (Leg1StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(Leg1StartAnchor);
						dataPoint.CopyDataValues(Leg1EndAnchor);
						Leg1StartAnchor.IsEditing = false;
					}
					else if (Leg1EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(Leg1EndAnchor);
						dataPoint.CopyDataValues(Leg2EndAnchor);
						dataPoint.CopyDataValues(Leg2EndLeftAnchor);
						dataPoint.CopyDataValues(Leg2EndRightAnchor);
						Leg1EndAnchor.IsEditing = false;
					}

					if (!Leg1StartAnchor.IsEditing && !Leg1EndAnchor.IsEditing)
						SetParallelLine(chartControl, Leg2StartAnchor.IsEditing);

					if (!isReadyForMovingSecondLeg)
					{
						// if we just plopped second line, move it. if we just finished moving it, we're done with initial building
						if (!Leg2StartAnchor.IsEditing) isReadyForMovingSecondLeg = true;
					}
					else 
					{
						isReadyForMovingSecondLeg		= false;
						DrawingState					= DrawingState.Normal;
						IsSelected						= false;
						Leg2EndAnchor.IsEditing 		= false;
						Leg2EndLeftAnchor.IsEditing		= false;
						Leg2EndRightAnchor.IsEditing	= false;
					}
					break;
				case DrawingState.Normal:
				case DrawingState.Moving:
					if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) { IsSelected = false; break; }
					Point point		= dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					editingAnchor	= GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);

					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState			= DrawingState.Editing;
					}
					else if (editingAnchor == null || IsLocked)
					{
						if (GetCursor(chartControl, chartPanel, chartScale, point) == null)
							IsSelected = false;
						else
							DrawingState = DrawingState.Moving;
					}
					break;
			}
		}

		public override void OnMouseMove(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building) return;

			if (DrawingState == DrawingState.Building)
			{
				if (Leg1EndAnchor.IsEditing)
					dataPoint.CopyDataValues(Leg1EndAnchor);
				else if (isReadyForMovingSecondLeg)
				{
					Leg2StartAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Point startPoint2 = Leg2StartAnchor.GetPoint(cc, cp, cs);
					Point endPoint2	= startPoint2 + (Leg1EndAnchor.GetPoint(cc, cp, cs) - Leg1StartAnchor.GetPoint(cc, cp, cs));
					Leg2EndAnchor.UpdateFromPoint(endPoint2, cc, cs);
					int endsLength = SerifsLength * 10;
					Leg2EndLeftAnchor.UpdateFromPoint(new Point(endPoint2.X - endsLength, endPoint2.Y), cc, cs);
					Leg2EndRightAnchor.UpdateFromPoint(new Point(endPoint2.X + endsLength, endPoint2.Y), cc, cs);
				}
			}
			else if (DrawingState == DrawingState.Editing)
			{
				if (Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
				{
					Leg1StartAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndAnchor.MoveAnchor(dataPoint, InitialMouseDownAnchor, cc, cp, cs, this);
					Leg2EndLeftAnchor.MoveAnchor(dataPoint, InitialMouseDownAnchor, cc, cp, cs, this);
					Leg2EndRightAnchor.MoveAnchor(dataPoint, InitialMouseDownAnchor, cc, cp, cs, this);
				}

				if (!Leg1StartAnchor.IsEditing &&
					Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
				{
					Leg1EndAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndLeftAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndRightAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
				}
					
				if (!Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					Leg2EndRightAnchor.IsEditing)
				{
					if (dataPoint.GetPoint(cc, cp, cs).X > (Leg2EndAnchor.GetPoint(cc, cp, cs).X + (SerifsLength * 10)))
					{
						Leg2EndRightAnchor.Time = dataPoint.Time;
						Leg2EndRightAnchor.SlotIndex = dataPoint.SlotIndex;
					}
				}

				if (!Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
				{
					if (dataPoint.GetPoint(cc, cp, cs).X < (Leg2EndAnchor.GetPoint(cc, cp, cs).X - (SerifsLength * 10)))
					{
						Leg2EndLeftAnchor.Time = dataPoint.Time;
						Leg2EndLeftAnchor.SlotIndex = dataPoint.SlotIndex;
					}
				}

				if (!Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
				{
					Leg2EndAnchor.Time = dataPoint.Time;
					Leg2EndAnchor.SlotIndex = dataPoint.SlotIndex;
					Leg2EndLeftAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndRightAnchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
					Leg2EndLeftAnchor.Price = Leg2EndAnchor.Price;
					Leg2EndRightAnchor.Price = Leg2EndAnchor.Price;
				}
					
				if (!Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					!Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
					DrawingState = DrawingState.Moving;

				if (!Leg1StartAnchor.IsEditing &&
					!Leg1EndAnchor.IsEditing &&
					Leg2StartAnchor.IsEditing &&
					!Leg2EndAnchor.IsEditing &&
					!Leg2EndLeftAnchor.IsEditing &&
					!Leg2EndRightAnchor.IsEditing)
					DrawingState = DrawingState.Moving;
			}
			else if (DrawingState == DrawingState.Moving)
			{
				if (movingLeg == 1)
				{
					foreach (ChartAnchor anchor in new[] { Leg1StartAnchor, Leg1EndAnchor })
						anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
				}
				else if (movingLeg == 2)
				{
					foreach (ChartAnchor anchor in new[] { Leg2StartAnchor, Leg2EndAnchor, Leg2EndLeftAnchor, Leg2EndRightAnchor })
						anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, cc, cp, cs, this);
				}
			}
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building) return;
			if (DrawingState == DrawingState.Editing && updateEndAnc) updateEndAnc = false;
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
			
			myMenuItem1 = new MenuItem { Header = "Hide Leg 1", IsCheckable = true, IsChecked = hideLeg1 };
			separatorItem  = new Separator { Style = Application.Current.TryFindResource("MainMenuSeparator") as Style };
			
			myMenuItem1.Click += MyMenuItem1_Click;
		}
		
		private void RemoveMenuHandlers()
		{
			if (mycc == null || myMenuItem1 == null) return;

			myMenuItem1.Click -= MyMenuItem1_Click;
			
            mycc.ContextMenuOpening -= ChartControl_ContextMenuOpening;
            mycc.ContextMenuClosing -= ChartControl_ContextMenuClosing;
			
			if(mycc.ContextMenu.Items.Contains(myMenuItem1))
            {
                myMenuItem1.Click -= MyMenuItem1_Click;
                mycc.ContextMenu.Items.Remove(myMenuItem1);
            }

			if(mycc.ContextMenu.Items.Contains(separatorItem))
            {
                mycc.ContextMenu.Items.Remove(separatorItem);
            }
		}

		private void ChartControl_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
           if(mycc.ContextMenu.Items.Contains(myMenuItem1)) mycc.ContextMenu.Items.Remove(myMenuItem1);
		   if(mycc.ContextMenu.Items.Contains(separatorItem)) mycc.ContextMenu.Items.Remove(separatorItem);
        }

        private void ChartControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
			if (!this.IsSelected) return;
			if (mycc.ContextMenu.Items.Contains(myMenuItem1) == false) mycc.ContextMenu.Items.Insert(3, myMenuItem1);
			if (mycc.ContextMenu.Items.Contains(separatorItem) == false) mycc.ContextMenu.Items.Insert(4, separatorItem);
        }
		
        private void MyMenuItem1_Click(object sender, RoutedEventArgs e)
        {
			hideLeg1 = !hideLeg1;
			this.ForceRefresh();
        }

		public override void OnRender(ChartControl cc, ChartScale cs)
		{
			// Here we capture ChartControl and create our menu items
			if (mycc == null) AddMenuHandlers(cc);

			Stroke.RenderTarget				= RenderTarget;
			ParallelStroke.RenderTarget		= RenderTarget;
			RenderTarget.AntialiasMode		= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			
			ChartPanel cp			= cc.ChartPanels[cs.PanelIndex];

			Point startPoint			= Leg1StartAnchor.GetPoint(cc, cp, cs);
			Point endPoint				= Leg1EndAnchor.GetPoint(cc, cp, cs);
			Point startPoint2			= Leg2StartAnchor.GetPoint(cc, cp, cs);
			Point endPoint2				= Leg2EndAnchor.GetPoint(cc, cp, cs);

			SharpDX.Vector2 startVec	= startPoint.ToVector2();
			SharpDX.Vector2 endVec		= endPoint.ToVector2();
			SharpDX.Vector2 startVec2	= startPoint2.ToVector2();
			SharpDX.Vector2 endVec2		= endPoint2.ToVector2();

			Point maxPoint				= GetExtendedPoint(startPoint, endPoint);
			Point maxPoint2				= Leg2StartAnchor.Time > DateTime.MinValue ? GetExtendedPoint(startPoint2, endPoint2) : new Point(-1, -1);
			Point minPoint				= GetExtendedPoint(endPoint, startPoint);
			Point minPoint2				= Leg2StartAnchor.Time > DateTime.MinValue ? GetExtendedPoint(endPoint2, startPoint2) : new Point(-1, -1);

			int endsLength = SerifsLength * 10;

			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? cc.SelectionBrush : Stroke.BrushDX;
			if (!hideLeg1) RenderTarget.DrawLine(startVec, endVec, tmpBrush, Stroke.Width, Stroke.StrokeStyle);

			if (DrawSerifs)
			{
				Point p1 = new Point(startPoint.X - endsLength, startPoint.Y);
				Point p2 = new Point(startPoint.X + endsLength, startPoint.Y);
				RenderTarget.DrawLine(p1.ToVector2(), p2.ToVector2(), tmpBrush, Stroke.Width, Stroke.StrokeStyle);

				Point p3 = new Point(endPoint.X - endsLength, endPoint.Y);
				Point p4 = new Point(endPoint.X + endsLength, endPoint.Y);
				RenderTarget.DrawLine(p3.ToVector2(), p4.ToVector2(), tmpBrush, Stroke.Width, Stroke.StrokeStyle);
			}
			
			if (DrawingState == DrawingState.Building && !isReadyForMovingSecondLeg) return;
			
			tmpBrush = IsInHitTest ? cc.SelectionBrush : ParallelStroke.BrushDX;
			RenderTarget.DrawLine(startVec2, endVec2, tmpBrush, ParallelStroke.Width, ParallelStroke.StrokeStyle);

			if (DrawSerifs)
			{
				Point p5 = new Point(startPoint2.X - endsLength, startPoint2.Y);
				Point p6 = new Point(startPoint2.X + endsLength, startPoint2.Y);
				RenderTarget.DrawLine(p5.ToVector2(), p6.ToVector2(), tmpBrush, Stroke.Width, ParallelStroke.StrokeStyle);
			}
			
			RenderTarget.DrawLine(Leg2EndLeftAnchor.GetPoint(cc, cp, cs).ToVector2(), Leg2EndRightAnchor.GetPoint(cc, cp, cs).ToVector2(), tmpBrush, ParallelStroke.Width, ParallelStroke.StrokeStyle);
		}

		private void SetParallelLine(ChartControl chartControl, bool initialSet)
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
				if (chartControl.BarSpacingType != BarSpacingType.TimeBased)
				{
					Leg2StartAnchor.SlotIndex = Leg1EndAnchor.SlotIndex;
					Leg2StartAnchor.Time = chartControl.GetTimeBySlotIndex(Leg2StartAnchor.SlotIndex);
				}
				else
					Leg2StartAnchor.Time = Leg1EndAnchor.Time;

				Leg2StartAnchor.Price		= Leg1EndAnchor.Price;
				Leg2StartAnchor.StartAnchor = InitialMouseDownAnchor;
			}
			
			Leg2StartAnchor.IsEditing = false;
		}
	}

	public static partial class Draw
	{
		private static MeasuredMove MeasuredMoveCore(NinjaScriptBase owner, string tag, bool isAutoScale,
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

			MeasuredMove measuredMove = DrawingTool.GetByTagOrNew(owner, typeof(MeasuredMove), tag, templateName) as MeasuredMove;
			if (measuredMove == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(measuredMove, tag, isAutoScale, owner, isGlobal);

			ChartAnchor		startAnchor		= DrawingTool.CreateChartAnchor(owner, anchor1BarsAgo, anchor1Time, anchor1Y);
			ChartAnchor		endAnchor		= DrawingTool.CreateChartAnchor(owner, anchor2BarsAgo, anchor2Time, anchor2Y);
			ChartAnchor		trendAnchor		= DrawingTool.CreateChartAnchor(owner, anchor3BarsAgo, anchor3Time, anchor3Y);

			startAnchor.CopyDataValues(measuredMove.Leg1StartAnchor);
			endAnchor.CopyDataValues(measuredMove.Leg1EndAnchor);
			trendAnchor.CopyDataValues(measuredMove.Leg2StartAnchor);
			measuredMove.SetState(State.Active);
			return measuredMove;
		}

		/// <summary>
		/// Draws a measured move.
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
		public static MeasuredMove MeasuredMove(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y,
												int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y)
		{
			return MeasuredMoveCore(owner, tag, isAutoScale,
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, false, null);
		}

		/// <summary>
		/// Draws a measured move.
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
		public static MeasuredMove MeasuredMove(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time,
												double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y)
		{
			return MeasuredMoveCore(owner, tag, isAutoScale,
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, false, null);
		}

		/// <summary>
		/// Draws a measured move.
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
		public static MeasuredMove MeasuredMove(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y,
												int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, bool isGlobal, string templateName)
		{
			return MeasuredMoveCore(owner, tag, isAutoScale,
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a measured move.
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
		public static MeasuredMove MeasuredMove(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time,
												double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, bool isGlobal, string templateName)
		{
			return MeasuredMoveCore(owner, tag, isAutoScale,
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, isGlobal, templateName);
		}
	}
}