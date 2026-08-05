#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class TradingSessionStatus : Indicator
	{
		private static readonly string[] QuarterLabels = { "Q1", "Q2", "Q3", "Q4" };
		private static readonly string[] SessionLabels = { "Lon", "AM", "PM", "Asia" };
		private static readonly string[] DayLabels = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

		private TextFormat timelineTextFormat;
		private SharpDX.Direct2D1.SolidColorBrush timelineTextBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineBorderBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineActiveBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineNeutralBrush;
		private SharpDX.Direct2D1.SolidColorBrush[] quarterBrushes;

		private DateTime now = Core.Globals.Now;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Displays Quarterly Theory sessions and 90-minute quarters in a timeline below price, using NinjaTrader's configured time zone.";
				Name = "TradingSessionStatus";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				IsChartOnly = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;

				ShowLabel = false;
				LabelPosition = TextPositionFine.TopMiddle;

					DrawSessionLines = false;
					DrawSessionCloseLines = false;
					ShowLineLabels = true;
					AvoidLabelOverlap = true;
					LineWidth = 1;
					LineLabelOffsetTicks = 8;

				AsiaStartTime = DateTime.Parse("18:00");
				AsiaEndTime = DateTime.Parse("04:00");
				LondonStartTime = DateTime.Parse("03:00");
				LondonEndTime = DateTime.Parse("12:00");
				NewYorkStartTime = DateTime.Parse("09:30");
				NewYorkEndTime = DateTime.Parse("16:00");

				AsiaLineBrush = Brushes.DeepSkyBlue;
				LondonLineBrush = Brushes.Goldenrod;
				NewYorkLineBrush = Brushes.DodgerBlue;
			}
			else if (State == State.DataLoaded)
			{
				System.Diagnostics.Debug.Assert(
					GetSessionQuarterIndex(3) == 0
					&& GetSessionQuarterIndex(0) == 1
					&& GetWeekQuarterIndex(DayOfWeek.Wednesday) == 2
					&& ShouldDrawLayer(0.1f, 90f, 6f)
					&& !ShouldDrawLayer(0.01f, 90f, 6f),
					"Quarterly timeline mapping is invalid.");

				timelineTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.SemiBold, FontStyle.Normal, 10f)
				{
					TextAlignment = TextAlignment.Center,
					ParagraphAlignment = ParagraphAlignment.Center,
					WordWrapping = WordWrapping.NoWrap
				};
			}
			else if (State == State.Terminated)
			{
				DisposeDxResources();

				timelineTextFormat?.Dispose();
				timelineTextFormat = null;
			}
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();

			DisposeDxResources();
			if (RenderTarget == null)
				return;

			timelineTextBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.12f, 0.14f, 0.18f, 0.95f));
			timelineBorderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.24f, 0.27f, 0.32f, 0.55f));
			timelineActiveBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.04f, 0.05f, 0.07f, 0.95f));
			timelineNeutralBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.72f, 0.74f, 0.77f, 0.92f));
			quarterBrushes = new[]
			{
				new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.74f, 0.79f, 0.86f, 0.92f)),
				new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.91f, 0.74f, 0.76f, 0.92f)),
				new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.74f, 0.84f, 0.72f, 0.92f)),
				new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.72f, 0.76f, 0.87f, 0.92f))
			};
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			if (chartControl == null || chartScale == null || ChartBars == null || Bars == null || Bars.Count < 1 || ChartPanel == null)
				return;

			int fromIndex = ChartBars.FromIndex;
			int toIndex = ChartBars.ToIndex;
			if (fromIndex < 0 || toIndex < 0)
				return;

			fromIndex = Math.Max(0, fromIndex);
			toIndex = Math.Min(Bars.Count - 1, toIndex);
			if (fromIndex > toIndex)
				return;

			DateTime visibleStart = Bars.GetTime(fromIndex);
			DateTime visibleEnd = Bars.GetTime(toIndex);

			DrawQuarterlyTimeline(chartControl, visibleStart, visibleEnd);
		}

		private void DrawQuarterlyTimeline(ChartControl chartControl, DateTime visibleStart, DateTime visibleEnd)
		{
			if (RenderTarget == null || ChartPanel == null || timelineTextFormat == null || timelineTextBrush == null
				|| timelineBorderBrush == null || timelineActiveBrush == null || timelineNeutralBrush == null
				|| quarterBrushes == null || quarterBrushes.Length != 4)
				return;

			const float quarterHeight = 22f;
			const float sessionHeight = 24f;
			const float dayHeight = 20f;
			const float bottomMargin = 2f;
			const float minDetailedCellWidth = 6f;
			const float minDayCellWidth = 2f;

			float stripTop = (float)ChartPanel.Y + (float)ChartPanel.H
				- quarterHeight - sessionHeight - dayHeight - bottomMargin;
			if (stripTop < ChartPanel.Y)
				return;

			float quarterY = stripTop;
			float sessionY = quarterY + quarterHeight;
			float dayY = sessionY + sessionHeight;
			double visibleMinutes = Math.Max(1d, (visibleEnd - visibleStart).TotalMinutes);
			float pixelsPerMinute = (float)ChartPanel.W / (float)visibleMinutes;
			bool drawQuarters = ShouldDrawLayer(pixelsPerMinute, 90f, minDetailedCellWidth);
			bool drawSessions = ShouldDrawLayer(pixelsPerMinute, 360f, minDetailedCellWidth);
			bool drawDays = ShouldDrawLayer(pixelsPerMinute, 1440f, minDayCellWidth);
			if (!drawQuarters && !drawSessions && !drawDays)
				return;

			if (drawQuarters || drawSessions)
			{
				DateTime sessionStart = visibleStart.Date.AddHours((visibleStart.Hour / 6) * 6);
				float sessionStartX = chartControl.GetXByTime(sessionStart);
				while (sessionStart <= visibleEnd)
				{
					DateTime sessionEnd = sessionStart.AddHours(6);
					float sessionEndX = chartControl.GetXByTime(sessionEnd);
					int sessionIndex = sessionStart.Hour / 6;

					if (drawSessions)
						DrawTimelineCell(sessionStartX, sessionEndX, sessionY, sessionHeight,
							SessionLabels[sessionIndex], quarterBrushes[GetSessionQuarterIndex(sessionIndex)]);

					if (drawQuarters)
					{
						float quarterStartX = sessionStartX;
						for (int quarterIndex = 0; quarterIndex < 4; quarterIndex++)
						{
							float quarterEndX = quarterIndex == 3
								? sessionEndX
								: chartControl.GetXByTime(sessionStart.AddMinutes((quarterIndex + 1) * 90));
							DrawTimelineCell(quarterStartX, quarterEndX, quarterY, quarterHeight,
								QuarterLabels[quarterIndex], quarterBrushes[quarterIndex]);
							quarterStartX = quarterEndX;
						}
					}

					sessionStart = sessionEnd;
					sessionStartX = sessionEndX;
				}
			}

			if (drawDays)
			{
				DateTime tradingDayStart = visibleStart.Date.AddDays(-1).AddHours(18);
				float tradingDayStartX = chartControl.GetXByTime(tradingDayStart);
				for (; tradingDayStart <= visibleEnd; tradingDayStart = tradingDayStart.AddDays(1))
				{
					DateTime tradingDayEnd = tradingDayStart.AddDays(1);
					float tradingDayEndX = chartControl.GetXByTime(tradingDayEnd);
					DayOfWeek tradingDay = tradingDayEnd.DayOfWeek;
					int weekQuarter = GetWeekQuarterIndex(tradingDay);
					DrawTimelineCell(tradingDayStartX, tradingDayEndX, dayY, dayHeight,
						DayLabels[(int)tradingDay], weekQuarter >= 0 ? quarterBrushes[weekQuarter] : timelineNeutralBrush);
					tradingDayStartX = tradingDayEndX;
				}
			}

			DateTime nowTime = Now;
			int activeSessionIndex = nowTime.Hour / 6;
			DateTime activeSessionStart = nowTime.Date.AddHours(activeSessionIndex * 6);
			int activeQuarterIndex = Math.Min(3, (int)(nowTime - activeSessionStart).TotalMinutes / 90);
			DateTime activeQuarterStart = activeSessionStart.AddMinutes(activeQuarterIndex * 90);
			DateTime activeTradingDayStart = nowTime.TimeOfDay >= TimeSpan.FromHours(18)
				? nowTime.Date.AddHours(18)
				: nowTime.Date.AddDays(-1).AddHours(18);

			if (drawQuarters)
				DrawTimelineOutline(chartControl, activeQuarterStart, activeQuarterStart.AddMinutes(90), quarterY, quarterHeight, 2f);
			if (drawSessions)
				DrawTimelineOutline(chartControl, activeSessionStart, activeSessionStart.AddHours(6), sessionY, sessionHeight, 2f);
			if (drawDays)
				DrawTimelineOutline(chartControl, activeTradingDayStart, activeTradingDayStart.AddDays(1), dayY, dayHeight, 2f);
		}

		private void DrawTimelineCell(float startX, float endX, float y, float height,
			string text, SharpDX.Direct2D1.Brush fillBrush)
		{
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
			float left = Math.Max(panelLeft, Math.Min(startX, endX));
			float right = Math.Min(panelRight, Math.Max(startX, endX));
			if (right - left < 1f)
				return;

			var rect = new RectangleF(left, y, right - left, height);
			RenderTarget.FillRectangle(rect, fillBrush);
			RenderTarget.DrawRectangle(rect, timelineBorderBrush, 1f);
			if (rect.Width >= 18f)
				RenderTarget.DrawText(text, timelineTextFormat, rect, timelineTextBrush);
		}

		private void DrawTimelineOutline(ChartControl chartControl, DateTime start, DateTime end, float y, float height, float width)
		{
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
			float startX = chartControl.GetXByTime(start);
			float endX = chartControl.GetXByTime(end);
			float left = Math.Max(panelLeft, Math.Min(startX, endX));
			float right = Math.Min(panelRight, Math.Max(startX, endX));
			if (right <= left)
				return;

			RenderTarget.DrawRectangle(new RectangleF(left, y, right - left, height), timelineActiveBrush, width);
		}

		private static int GetSessionQuarterIndex(int sessionIndex)
		{
			// 18:00 Asia = Q1, 00:00 London = Q2, 06:00 AM = Q3, 12:00 PM = Q4.
			return (sessionIndex + 1) % 4;
		}

		private static bool ShouldDrawLayer(float pixelsPerMinute, float durationMinutes, float minimumWidth)
		{
			return pixelsPerMinute * durationMinutes >= minimumWidth;
		}

		private static int GetWeekQuarterIndex(DayOfWeek dayOfWeek)
		{
			switch (dayOfWeek)
			{
				case DayOfWeek.Monday: return 0;
				case DayOfWeek.Tuesday: return 1;
				case DayOfWeek.Wednesday: return 2;
				case DayOfWeek.Thursday: return 3;
				default: return -1;
			}
		}

		private void DisposeDxResources()
		{
			timelineTextBrush?.Dispose();
			timelineTextBrush = null;

			timelineBorderBrush?.Dispose();
			timelineBorderBrush = null;

			timelineActiveBrush?.Dispose();
			timelineActiveBrush = null;

			timelineNeutralBrush?.Dispose();
			timelineNeutralBrush = null;

			if (quarterBrushes != null)
			{
				foreach (SharpDX.Direct2D1.SolidColorBrush brush in quarterBrushes)
					brush?.Dispose();
				quarterBrushes = null;
			}
		}

		private DateTime Now
		{
			get
			{
				now = Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;

				if (now.Millisecond > 0)
					now = Core.Globals.MinDate.AddSeconds((long)Math.Floor(now.Subtract(Core.Globals.MinDate).TotalSeconds));

				return now;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "ShowLabel", GroupName = "Display", Order = 0)]
		public bool ShowLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "LabelPosition", Description = "Where the session label is displayed on the chart", GroupName = "Display", Order = 1)]
		public TextPositionFine LabelPosition { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DrawSessionLines", Description = "Draw vertical lines at each session open", GroupName = "Lines", Order = 0)]
		public bool DrawSessionLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "DrawSessionCloseLines", Description = "Draw vertical lines at each session close", GroupName = "Lines", Order = 1)]
		public bool DrawSessionCloseLines { get; set; }

			[NinjaScriptProperty]
			[Display(Name = "ShowLineLabels", Description = "Show vertical labels and bottom time boxes for session lines", GroupName = "Lines", Order = 2)]
			public bool ShowLineLabels { get; set; }

			[NinjaScriptProperty]
			[Display(Name = "AvoidLabelOverlap", Description = "Align session markers between bars (reduces overlap with candles)", GroupName = "Lines", Order = 3)]
			public bool AvoidLabelOverlap { get; set; }

			[Range(1, 10), NinjaScriptProperty]
			[Display(Name = "LineWidth", Description = "Width of session lines", GroupName = "Lines", Order = 4)]
			public int LineWidth { get; set; }

			[Range(0, 200), NinjaScriptProperty]
			[Display(Name = "LineLabelOffsetTicks", Description = "Offset session labels (fine-tuning)", GroupName = "Lines", Order = 5)]
			public int LineLabelOffsetTicks { get; set; }

			[XmlIgnore]
			[Display(Name = "AsiaLineBrush", GroupName = "Lines", Order = 6)]
			public Brush AsiaLineBrush { get; set; }

		[Browsable(false)]
		public string AsiaLineBrushSerialize
		{
			get { return Serialize.BrushToString(AsiaLineBrush); }
			set { AsiaLineBrush = Serialize.StringToBrush(value); }
		}

			[XmlIgnore]
			[Display(Name = "LondonLineBrush", GroupName = "Lines", Order = 7)]
			public Brush LondonLineBrush { get; set; }

		[Browsable(false)]
		public string LondonLineBrushSerialize
		{
			get { return Serialize.BrushToString(LondonLineBrush); }
			set { LondonLineBrush = Serialize.StringToBrush(value); }
		}

			[XmlIgnore]
			[Display(Name = "NewYorkLineBrush", GroupName = "Lines", Order = 8)]
			public Brush NewYorkLineBrush { get; set; }

		[Browsable(false)]
		public string NewYorkLineBrushSerialize
		{
			get { return Serialize.BrushToString(NewYorkLineBrush); }
			set { NewYorkLineBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "AsiaStartTime", Description = "Asia session start time (chart time zone)", GroupName = "Sessions", Order = 0)]
		public DateTime AsiaStartTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "AsiaEndTime", Description = "Asia session end time (chart time zone)", GroupName = "Sessions", Order = 1)]
		public DateTime AsiaEndTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "LondonStartTime", Description = "London session start time (chart time zone)", GroupName = "Sessions", Order = 2)]
		public DateTime LondonStartTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "LondonEndTime", Description = "London session end time (chart time zone)", GroupName = "Sessions", Order = 3)]
		public DateTime LondonEndTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "NewYorkStartTime", Description = "New York session start time (chart time zone)", GroupName = "Sessions", Order = 4)]
		public DateTime NewYorkStartTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "NewYorkEndTime", Description = "New York session end time (chart time zone)", GroupName = "Sessions", Order = 5)]
		public DateTime NewYorkEndTime { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradingSessionStatus[] cacheTradingSessionStatus;
		public TradingSessionStatus TradingSessionStatus(bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			return TradingSessionStatus(Input, showLabel, labelPosition, drawSessionLines, drawSessionCloseLines, showLineLabels, avoidLabelOverlap, lineWidth, lineLabelOffsetTicks, asiaStartTime, asiaEndTime, londonStartTime, londonEndTime, newYorkStartTime, newYorkEndTime);
		}

		public TradingSessionStatus TradingSessionStatus(ISeries<double> input, bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			if (cacheTradingSessionStatus != null)
				for (int idx = 0; idx < cacheTradingSessionStatus.Length; idx++)
					if (cacheTradingSessionStatus[idx] != null && cacheTradingSessionStatus[idx].ShowLabel == showLabel && cacheTradingSessionStatus[idx].LabelPosition == labelPosition && cacheTradingSessionStatus[idx].DrawSessionLines == drawSessionLines && cacheTradingSessionStatus[idx].DrawSessionCloseLines == drawSessionCloseLines && cacheTradingSessionStatus[idx].ShowLineLabels == showLineLabels && cacheTradingSessionStatus[idx].AvoidLabelOverlap == avoidLabelOverlap && cacheTradingSessionStatus[idx].LineWidth == lineWidth && cacheTradingSessionStatus[idx].LineLabelOffsetTicks == lineLabelOffsetTicks && cacheTradingSessionStatus[idx].AsiaStartTime == asiaStartTime && cacheTradingSessionStatus[idx].AsiaEndTime == asiaEndTime && cacheTradingSessionStatus[idx].LondonStartTime == londonStartTime && cacheTradingSessionStatus[idx].LondonEndTime == londonEndTime && cacheTradingSessionStatus[idx].NewYorkStartTime == newYorkStartTime && cacheTradingSessionStatus[idx].NewYorkEndTime == newYorkEndTime && cacheTradingSessionStatus[idx].EqualsInput(input))
						return cacheTradingSessionStatus[idx];
			return CacheIndicator<TradingSessionStatus>(new TradingSessionStatus(){ ShowLabel = showLabel, LabelPosition = labelPosition, DrawSessionLines = drawSessionLines, DrawSessionCloseLines = drawSessionCloseLines, ShowLineLabels = showLineLabels, AvoidLabelOverlap = avoidLabelOverlap, LineWidth = lineWidth, LineLabelOffsetTicks = lineLabelOffsetTicks, AsiaStartTime = asiaStartTime, AsiaEndTime = asiaEndTime, LondonStartTime = londonStartTime, LondonEndTime = londonEndTime, NewYorkStartTime = newYorkStartTime, NewYorkEndTime = newYorkEndTime }, input, ref cacheTradingSessionStatus);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradingSessionStatus TradingSessionStatus(bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			return indicator.TradingSessionStatus(Input, showLabel, labelPosition, drawSessionLines, drawSessionCloseLines, showLineLabels, avoidLabelOverlap, lineWidth, lineLabelOffsetTicks, asiaStartTime, asiaEndTime, londonStartTime, londonEndTime, newYorkStartTime, newYorkEndTime);
		}

		public Indicators.TradingSessionStatus TradingSessionStatus(ISeries<double> input , bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			return indicator.TradingSessionStatus(input, showLabel, labelPosition, drawSessionLines, drawSessionCloseLines, showLineLabels, avoidLabelOverlap, lineWidth, lineLabelOffsetTicks, asiaStartTime, asiaEndTime, londonStartTime, londonEndTime, newYorkStartTime, newYorkEndTime);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradingSessionStatus TradingSessionStatus(bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			return indicator.TradingSessionStatus(Input, showLabel, labelPosition, drawSessionLines, drawSessionCloseLines, showLineLabels, avoidLabelOverlap, lineWidth, lineLabelOffsetTicks, asiaStartTime, asiaEndTime, londonStartTime, londonEndTime, newYorkStartTime, newYorkEndTime);
		}

		public Indicators.TradingSessionStatus TradingSessionStatus(ISeries<double> input , bool showLabel, TextPositionFine labelPosition, bool drawSessionLines, bool drawSessionCloseLines, bool showLineLabels, bool avoidLabelOverlap, int lineWidth, int lineLabelOffsetTicks, DateTime asiaStartTime, DateTime asiaEndTime, DateTime londonStartTime, DateTime londonEndTime, DateTime newYorkStartTime, DateTime newYorkEndTime)
		{
			return indicator.TradingSessionStatus(input, showLabel, labelPosition, drawSessionLines, drawSessionCloseLines, showLineLabels, avoidLabelOverlap, lineWidth, lineLabelOffsetTicks, asiaStartTime, asiaEndTime, londonStartTime, londonEndTime, newYorkStartTime, newYorkEndTime);
		}
	}
}

#endregion
