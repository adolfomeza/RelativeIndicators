/*
*	Arrows Drawing Tool made with ♡ by beo
* 	Last edit 02/04/2021
*	https://priceactiontradingsystem.com/link-to-forum/topic/pats-toolbar-custom-drawing-tools-why-this-is-better/
*/

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Serialization;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion


public enum ArrowSizeEnum {	Small, Medium, Big, VeryBig }

namespace NinjaTrader.NinjaScript.DrawingTools
{
	public abstract class MyChartMarker : DrawingTool
	{
		private	Brush areaBrush;
		[CLSCompliant(false)]
		protected DeviceBrush areaDeviceBrush = new DeviceBrush();
		private Brush outlineBrush;
		[CLSCompliant(false)]
		protected DeviceBrush outlineDeviceBrush = new DeviceBrush();
		protected ArrowSizeEnum arrowSize = ArrowSizeEnum.Small;

		public ChartAnchor	Anchor { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 1)]
		[XmlIgnore]
		public Brush AreaBrush
		{ 
			get { return areaBrush; }
			set { areaBrush = value; areaDeviceBrush.Brush = value; }
		}

		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		protected double BarWidth
		{
			get
			{
				if (AttachedTo != null)
				{
					ChartBars chartBars = AttachedTo.ChartObject as ChartBars;
					if (chartBars == null)
					{
						Gui.NinjaScript.IChartBars iChartBars = AttachedTo.ChartObject as Gui.NinjaScript.IChartBars;
						if (iChartBars != null) chartBars = iChartBars.ChartBars;
					}
					if (chartBars != null && chartBars.Properties.ChartStyle != null) return chartBars.Properties.ChartStyle.BarWidth;
				}
				return MinimumSize;
			}
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesOutlineBrush", GroupName = "NinjaScriptGeneral", Order = 2)]
		[XmlIgnore]
		public Brush OutlineBrush
		{
			get { return outlineBrush; }
			set { outlineBrush = value; outlineDeviceBrush.Brush = value; }
		}

		[Browsable(false)]
		public string OutlineBrushSerialize
		{
			get { return Serialize.BrushToString(OutlineBrush);	}
			set { OutlineBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 10)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Outline Width", GroupName = "NinjaScriptGeneral", Order = 3)]
		public int OutlineWidth
		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "Size", GroupName = "NinjaScriptGeneral", Order = 4)]
		public ArrowSizeEnum ArrowSize
		{
			get { return arrowSize; }
			set { arrowSize = value; }
		}

		public static float MinimumSize { get { return 5f; } }

		public override IEnumerable<ChartAnchor> Anchors { get { return new[]{Anchor}; } }

		protected override void Dispose(bool disposing)
		{
			areaDeviceBrush.RenderTarget	= null;
			outlineDeviceBrush.RenderTarget	= null;
		}

		public override Cursor GetCursor(ChartControl cc, ChartPanel cp, ChartScale cs, Point point)
		{
			if (DrawingState == DrawingState.Building) return Cursors.Pen;
			if (DrawingState == DrawingState.Moving) return IsLocked ? Cursors.No : Cursors.SizeAll;
			// this is fired whenever the chart marker is selected.
			return (point - Anchor.GetPoint(cc, cp, cs)).Length <= Math.Max(15d, 10d * (BarWidth / 5d)) ? IsLocked ?  Cursors.Arrow : Cursors.SizeAll : null;
		}

		public override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			return Anchor.IsEditing ? new Point[0] : new[]{ Anchor.GetPoint(cc, cc.ChartPanels[cs.PanelIndex], cs) };
		}

		public override bool IsVisibleOnChart(ChartControl cc, ChartScale cs, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building) return false;
			if (!IsAutoScale && (Anchor.Price < cs.MinValue || Anchor.Price > cs.MaxValue)) return false;
			return Anchor.Time >= firstTimeOnChart && Anchor.Time <= lastTimeOnChart;
		}

		public override void OnMouseDown(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					dataPoint.CopyDataValues(Anchor);
					Anchor.IsEditing = false;
					DrawingState = DrawingState.Normal;
					IsSelected = false;
					break;
				case DrawingState.Normal:
					if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) { IsSelected = false; break; }
					if (GetCursor(cc, cp, cs, dataPoint.GetPoint(cc, cp, cs)) != null) DrawingState = DrawingState.Moving;
					else IsSelected = false;
					break;
			}
		}

		public override void OnMouseMove(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState != DrawingState.Moving || IsLocked && DrawingState != DrawingState.Building) return;
			dataPoint.CopyDataValues(Anchor);
		}

		public override void OnMouseUp(ChartControl control, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving) DrawingState = DrawingState.Normal;
		}
	}

	public abstract class MyArrowMarkerBase : MyChartMarker
	{
		[XmlIgnore]
		[Browsable(false)]
		public bool	IsUpArrow { get; protected set; }

		public override Point[] GetSelectionPoints(ChartControl cc, ChartScale cs)
		{
			if (Anchor.IsEditing) return new Point[0];
			Point pixelPointArrowTop = Anchor.GetPoint(cc, cc.ChartPanels[cs.PanelIndex], cs);
			return new [] { new Point(pixelPointArrowTop.X, pixelPointArrowTop.Y) };
		}

		public override void OnMouseMove(ChartControl cc, ChartPanel cp, ChartScale cs, ChartAnchor dataPoint)
		{
			if (DrawingState != DrawingState.Moving || IsLocked) return;
			Anchor.UpdateFromPoint(dataPoint.GetPoint(cc, cp, cs), cc, cs);
		}

		public override void OnRender(ChartControl cc, ChartScale cs)
		{
			if (Anchor.IsEditing) return;

			areaDeviceBrush.RenderTarget 	= RenderTarget;
			outlineDeviceBrush.RenderTarget = RenderTarget;

			ChartPanel panel			= cc.ChartPanels[cs.PanelIndex];
			Point pixelPoint			= Anchor.GetPoint(cc, panel, cs);
			SharpDX.Vector2 endVector	= pixelPoint.ToVector2();

			// the geometry is created with 0,0 as point origin, and pointing UP by default, so translate & rotate as needed
			// If down arrow flip it around. beware due to our translation we rotate on origin
			SharpDX.Matrix3x2 transformMatrix = !IsUpArrow ? SharpDX.Matrix3x2.Rotation(MathHelper.DegreesToRadians(180), SharpDX.Vector2.Zero) * SharpDX.Matrix3x2.Translation(endVector) : SharpDX.Matrix3x2.Translation(endVector);

			RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			RenderTarget.Transform		= transformMatrix;
			
			float x = arrowSize == ArrowSizeEnum.Medium ? 1.5f : arrowSize == ArrowSizeEnum.Big ? 2f : arrowSize == ArrowSizeEnum.VeryBig ? 3f : 1f;
			float barWidth			= Math.Max((float) BarWidth, MinimumSize) * x;
			float arrowHeight		= barWidth * 3f;
			float arrowPointHeight	= barWidth;
			float arrowStemWidth	= barWidth / 3f;

			SharpDX.Direct2D1.PathGeometry arrowPathGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
			SharpDX.Direct2D1.GeometrySink geometrySink = arrowPathGeometry.Open();
			geometrySink.BeginFigure(SharpDX.Vector2.Zero, SharpDX.Direct2D1.FigureBegin.Filled);

			geometrySink.AddLine(new SharpDX.Vector2(barWidth, arrowPointHeight));
			geometrySink.AddLine(new SharpDX.Vector2(arrowStemWidth, arrowPointHeight));
			geometrySink.AddLine(new SharpDX.Vector2(arrowStemWidth, arrowHeight));
			geometrySink.AddLine(new SharpDX.Vector2(-arrowStemWidth, arrowHeight));
			geometrySink.AddLine(new SharpDX.Vector2(-arrowStemWidth, arrowPointHeight));
			geometrySink.AddLine(new SharpDX.Vector2(-barWidth, arrowPointHeight));

			geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
			geometrySink.Close(); // note this calls dispose for you. but not the other way around

			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? cc.SelectionBrush : areaDeviceBrush.BrushDX;
			if (tmpBrush != null) RenderTarget.FillGeometry(arrowPathGeometry, tmpBrush);
			tmpBrush = IsInHitTest ? cc.SelectionBrush : outlineDeviceBrush.BrushDX;
			if (tmpBrush != null) RenderTarget.DrawGeometry(arrowPathGeometry, tmpBrush, (float)OutlineWidth);
			arrowPathGeometry.Dispose();
			RenderTarget.Transform = SharpDX.Matrix3x2.Identity;
		}
	}

	public class ArrowDownRed : MyArrowMarkerBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawArrowDown; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Anchor	= new ChartAnchor
				{
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor,
					IsEditing	= true,
				};
				Name					= "Arrow Down Red";
				AreaBrush				= Brushes.Red;
				OutlineBrush			= Brushes.Red;
				IsUpArrow				= false;
				DisplayOnChartsMenus 	= false;
				ArrowSize				= ArrowSizeEnum.Small;
				OutlineWidth 			= 1;
			}
			else if (State == State.Terminated) Dispose();
		}
	}

	public class ArrowDownGreen : MyArrowMarkerBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawArrowDown; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Anchor	= new ChartAnchor
				{
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor,
					IsEditing	= true,
				};
				Name					= "Arrow Down Green";
				AreaBrush				= Brushes.LimeGreen;
				OutlineBrush			= Brushes.LimeGreen;
				IsUpArrow				= false;
				DisplayOnChartsMenus 	= false;
				ArrowSize 				= ArrowSizeEnum.Small;
				OutlineWidth 			= 1;
			}
			else if (State == State.Terminated) Dispose();
		}
	}

	public class ArrowUpBlue : MyArrowMarkerBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawArrowUp; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Anchor	= new ChartAnchor
				{
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor,
					IsEditing	= true,
				};
				Name					= "Arrow Up Blue";
				AreaBrush				= Brushes.Blue;
				OutlineBrush			= Brushes.Blue;
				IsUpArrow				= true;
				DisplayOnChartsMenus 	= false;
				ArrowSize 				= ArrowSizeEnum.Small;
				OutlineWidth 			= 1;
			}
			else if (State == State.Terminated) Dispose();
		}
	}

	public class ArrowUpGreen : MyArrowMarkerBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawArrowUp; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Anchor	= new ChartAnchor
				{
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor,
					IsEditing	= true,
				};
				Name					= "Arrow Up Green";
				AreaBrush				= Brushes.LimeGreen;
				OutlineBrush			= Brushes.LimeGreen;
				IsUpArrow				= true;
				DisplayOnChartsMenus 	= false;
				ArrowSize 				= ArrowSizeEnum.Small;
				OutlineWidth 			= 1;
			}
			else if (State == State.Terminated) Dispose();
		}
	}

	public static partial class Draw
	{
		// this function does all the actual instance creation and setup
		private static T MyChartMarkerCore<T>(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, DateTime time, double yVal, Brush brush, bool isGlobal, string templateName) where T : MyChartMarker
		{
			if (owner == null)
				throw new ArgumentException("owner");
			if (time == Core.Globals.MinDate && barsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");
			if (yVal.ApproxCompare(double.MinValue) == 0 || yVal.ApproxCompare(double.MaxValue) == 0)
				throw new ArgumentException("bad Y value");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = string.Format("{0}{1}", GlobalDrawingToolManager.GlobalDrawingToolTagPrefix, tag);

			T chartMarkerT = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;
			
			if (chartMarkerT == null)
				return default(T);

			DrawingTool.SetDrawingToolCommonValues(chartMarkerT, tag, isAutoScale, owner, isGlobal);
			
			// dont nuke existing anchor refs 
			ChartAnchor anchor;

			anchor = DrawingTool.CreateChartAnchor(owner, barsAgo, time, yVal);
			anchor.CopyDataValues(chartMarkerT.Anchor);

			// dont forget to set anchor as not editing or else it wont be drawn
			chartMarkerT.Anchor.IsEditing = false;

			// can be null when loaded from templateName
			if (brush != null) chartMarkerT.AreaBrush = brush;

			chartMarkerT.SetState(State.Active);
			return chartMarkerT;
		}

		// arrow down
		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow red pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowDownRed ArrowDownRed(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowDownRed>(owner, tag, isAutoScale, int.MinValue, time, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow green pointing down.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowDownGreen ArrowDownGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowDownGreen>(owner, tag, isAutoScale, int.MinValue, time, y, null, isGlobal, templateName);
		}

		// arrow up
		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow blue pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowUpBlue ArrowUpBlue(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowUpBlue>(owner, tag, isAutoScale, int.MinValue, time, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush)
		{
			return MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null);
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, int.MinValue, time, y, brush, false, null));
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, barsAgo, Core.Globals.MinDate, y, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow green pointing up.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static ArrowUpGreen ArrowUpGreen(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, bool isGlobal, string templateName)
		{
			return MyChartMarkerCore<ArrowUpGreen>(owner, tag, isAutoScale, int.MinValue, time, y, null, isGlobal, templateName);
		}
	}
}
