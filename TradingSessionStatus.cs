#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Input;
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
		private static readonly int[] TimeLabelIntervals = { 1, 2, 5, 10, 15, 30, 60, 120, 240, 360, 720, 1440, 2880, 10080 };

		private TextFormat timelineTextFormat;
		private TextFormat secondaryTimeTextFormat;
		private SharpDX.Direct2D1.SolidColorBrush timelineTextBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineBorderBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineActiveBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineNeutralBrush;
		private SharpDX.Direct2D1.SolidColorBrush[] quarterBrushes;
		private ChartControl crosshairChartControl;
		private System.Windows.Controls.Border crosshairTimeMarker;
		private System.Windows.Controls.TextBlock crosshairTimeText;
		private TimeZoneInfo sessionTimeZone;

		private DateTime now = Core.Globals.Now;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Displays New York-time Quarterly Theory sessions and 90-minute quarters in a timeline below price.";
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
				ShowSecondaryTimeZone = true;
				UseLocalMachineTime = false;
				SecondaryTimeZoneId = "Israel Standard Time";

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
				sessionTimeZone = FindTimeZoneOrDefault("Eastern Standard Time", Core.Globals.GeneralOptions.TimeZoneInfo);
				System.Diagnostics.Debug.Assert(
					GetSessionQuarterIndex(3) == 0
					&& GetSessionQuarterIndex(0) == 1
					&& GetWeekQuarterIndex(DayOfWeek.Wednesday) == 2
					&& ShouldDrawLayer(0.1f, 90f, 6f)
					&& !ShouldDrawLayer(0.01f, 90f, 6f)
					&& GetTimeLabelIntervalMinutes(1f) == 60
					&& GetFirstTimeLabel(new DateTime(2026, 1, 1, 12, 34, 0), 60).Hour == 13
					&& GetQuarterStart(new DateTime(2026, 1, 2, 19, 44, 0)) == new DateTime(2026, 1, 2, 19, 30, 0)
					&& GetTradingDayStart(new DateTime(2026, 1, 2, 2, 0, 0)) == new DateTime(2026, 1, 1, 18, 0, 0)
					&& ConvertChartTime(new DateTime(2026, 1, 1, 15, 0, 0),
						TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"), sessionTimeZone).Hour == 18
					&& ConvertChartTime(new DateTime(2026, 1, 1, 12, 0, 0), TimeZoneInfo.Utc,
						TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time")).Hour == 14,
					"Quarterly timeline mapping is invalid.");

				timelineTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.SemiBold, FontStyle.Normal, 10f)
				{
					TextAlignment = TextAlignment.Center,
					ParagraphAlignment = ParagraphAlignment.Center,
					WordWrapping = WordWrapping.NoWrap
				};
				secondaryTimeTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.SemiBold, FontStyle.Normal, 12f)
				{
					TextAlignment = TextAlignment.Center,
					ParagraphAlignment = ParagraphAlignment.Center,
					WordWrapping = WordWrapping.NoWrap
				};
			}
			else if (State == State.Historical)
			{
				SubscribeCrosshair();
			}
			else if (State == State.Terminated)
			{
				UnsubscribeCrosshair();
				DisposeDxResources();

				timelineTextFormat?.Dispose();
				timelineTextFormat = null;
				secondaryTimeTextFormat?.Dispose();
				secondaryTimeTextFormat = null;
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

			DrawQuarterlyTimeline(chartControl, fromIndex, toIndex, visibleStart, visibleEnd);
		}

		private void DrawQuarterlyTimeline(ChartControl chartControl, int fromIndex, int toIndex,
			DateTime visibleStart, DateTime visibleEnd)
		{
			if (RenderTarget == null || ChartPanel == null || sessionTimeZone == null || timelineTextFormat == null
				|| (ShowSecondaryTimeZone && secondaryTimeTextFormat == null) || timelineTextBrush == null
				|| timelineBorderBrush == null || timelineActiveBrush == null || timelineNeutralBrush == null
				|| quarterBrushes == null || quarterBrushes.Length != 4)
				return;

			const float quarterHeight = 22f;
			const float sessionHeight = 24f;
			const float dayHeight = 20f;
			const float secondaryTimeHeight = 20f;
			const float bottomMargin = 2f;
			const float minDetailedCellWidth = 6f;
			const float minDayCellWidth = 2f;

			float stripTop = (float)ChartPanel.Y + (float)ChartPanel.H
				- quarterHeight - sessionHeight - dayHeight
				- (ShowSecondaryTimeZone ? secondaryTimeHeight : 0f) - bottomMargin;
			if (stripTop < ChartPanel.Y)
				return;

			float quarterY = stripTop;
			float sessionY = quarterY + quarterHeight;
			float dayY = sessionY + sessionHeight;
			float secondaryTimeY = dayY + dayHeight;
			double visibleMinutes = Math.Max(1d, (visibleEnd - visibleStart).TotalMinutes);
			float pixelsPerMinute = (float)ChartPanel.W / (float)visibleMinutes;
			bool timeBased = chartControl.BarSpacingType == BarSpacingType.TimeBased;
			bool drawQuarters = !timeBased || ShouldDrawLayer(pixelsPerMinute, 90f, minDetailedCellWidth);
			bool drawSessions = !timeBased || ShouldDrawLayer(pixelsPerMinute, 360f, minDetailedCellWidth);
			bool drawDays = !timeBased || ShouldDrawLayer(pixelsPerMinute, 1440f, minDayCellWidth);
			if (!drawQuarters && !drawSessions && !drawDays && !ShowSecondaryTimeZone)
				return;
			TimeZoneInfo chartTimeZone = Core.Globals.GeneralOptions.TimeZoneInfo;
			DateTime sessionVisibleStart = ConvertChartTime(visibleStart, chartTimeZone, sessionTimeZone);
			DateTime sessionVisibleEnd = ConvertChartTime(visibleEnd, chartTimeZone, sessionTimeZone);

			if (!timeBased)
				DrawBarAlignedTimeline(chartControl, fromIndex, toIndex, chartTimeZone,
					quarterY, quarterHeight, sessionY, sessionHeight, dayY, dayHeight);
			else if (drawQuarters || drawSessions)
			{
				DateTime sessionStart = sessionVisibleStart.Date.AddHours((sessionVisibleStart.Hour / 6) * 6);
				float sessionStartX = chartControl.GetXByTime(ConvertChartTime(sessionStart, sessionTimeZone, chartTimeZone));
				while (sessionStart <= sessionVisibleEnd)
				{
					DateTime sessionEnd = sessionStart.AddHours(6);
					float sessionEndX = chartControl.GetXByTime(ConvertChartTime(sessionEnd, sessionTimeZone, chartTimeZone));
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
								: chartControl.GetXByTime(ConvertChartTime(
									sessionStart.AddMinutes((quarterIndex + 1) * 90), sessionTimeZone, chartTimeZone));
							DrawTimelineCell(quarterStartX, quarterEndX, quarterY, quarterHeight,
								QuarterLabels[quarterIndex], quarterBrushes[quarterIndex]);
							quarterStartX = quarterEndX;
						}
					}

					sessionStart = sessionEnd;
					sessionStartX = sessionEndX;
				}
			}

			if (timeBased && drawDays)
			{
				DateTime tradingDayStart = sessionVisibleStart.Date.AddDays(-1).AddHours(18);
				float tradingDayStartX = chartControl.GetXByTime(ConvertChartTime(tradingDayStart, sessionTimeZone, chartTimeZone));
				for (; tradingDayStart <= sessionVisibleEnd; tradingDayStart = tradingDayStart.AddDays(1))
				{
					DateTime tradingDayEnd = tradingDayStart.AddDays(1);
					float tradingDayEndX = chartControl.GetXByTime(ConvertChartTime(tradingDayEnd, sessionTimeZone, chartTimeZone));
					DayOfWeek tradingDay = tradingDayEnd.DayOfWeek;
					int weekQuarter = GetWeekQuarterIndex(tradingDay);
					DrawTimelineCell(tradingDayStartX, tradingDayEndX, dayY, dayHeight,
						DayLabels[(int)tradingDay], weekQuarter >= 0 ? quarterBrushes[weekQuarter] : timelineNeutralBrush);
					tradingDayStartX = tradingDayEndX;
				}
			}

			DateTime nowTime = ConvertChartTime(Now, chartTimeZone, sessionTimeZone);
			int activeSessionIndex = nowTime.Hour / 6;
			DateTime activeSessionStart = nowTime.Date.AddHours(activeSessionIndex * 6);
			int activeQuarterIndex = Math.Min(3, (int)(nowTime - activeSessionStart).TotalMinutes / 90);
			DateTime activeQuarterStart = activeSessionStart.AddMinutes(activeQuarterIndex * 90);
			DateTime activeTradingDayStart = nowTime.TimeOfDay >= TimeSpan.FromHours(18)
				? nowTime.Date.AddHours(18)
				: nowTime.Date.AddDays(-1).AddHours(18);

			if (timeBased && drawQuarters)
				DrawTimelineOutline(chartControl,
					ConvertChartTime(activeQuarterStart, sessionTimeZone, chartTimeZone),
					ConvertChartTime(activeQuarterStart.AddMinutes(90), sessionTimeZone, chartTimeZone), quarterY, quarterHeight, 2f);
			if (timeBased && drawSessions)
				DrawTimelineOutline(chartControl,
					ConvertChartTime(activeSessionStart, sessionTimeZone, chartTimeZone),
					ConvertChartTime(activeSessionStart.AddHours(6), sessionTimeZone, chartTimeZone), sessionY, sessionHeight, 2f);
			if (timeBased && drawDays)
				DrawTimelineOutline(chartControl,
					ConvertChartTime(activeTradingDayStart, sessionTimeZone, chartTimeZone),
					ConvertChartTime(activeTradingDayStart.AddDays(1), sessionTimeZone, chartTimeZone), dayY, dayHeight, 2f);
			if (ShowSecondaryTimeZone)
				DrawSecondaryTimeZone(chartControl, fromIndex, toIndex, visibleStart, visibleEnd,
					pixelsPerMinute, secondaryTimeY, secondaryTimeHeight);
		}

		private void DrawBarAlignedTimeline(ChartControl chartControl, int fromIndex, int toIndex,
			TimeZoneInfo chartTimeZone, float quarterY, float quarterHeight,
			float sessionY, float sessionHeight, float dayY, float dayHeight)
		{
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
			DateTime sessionTime = ConvertChartTime(Bars.GetTime(fromIndex), chartTimeZone, sessionTimeZone);
			DateTime sessionStart = GetSessionStart(sessionTime);
			DateTime quarterStart = GetQuarterStart(sessionTime);
			DateTime tradingDayStart = GetTradingDayStart(sessionTime);
			DateTime nowTime = ConvertChartTime(Now, chartTimeZone, sessionTimeZone);
			DateTime activeSessionStart = GetSessionStart(nowTime);
			DateTime activeQuarterStart = GetQuarterStart(nowTime);
			DateTime activeTradingDayStart = GetTradingDayStart(nowTime);
			float quarterStartX = panelLeft;
			float sessionStartX = panelLeft;
			float tradingDayStartX = panelLeft;
			float previousBarX = chartControl.GetXByBarIndex(ChartBars, fromIndex);

			for (int index = fromIndex + 1; index <= toIndex; index++)
			{
				float barX = chartControl.GetXByBarIndex(ChartBars, index);
				float boundaryX = (previousBarX + barX) * 0.5f;
				sessionTime = ConvertChartTime(Bars.GetTime(index), chartTimeZone, sessionTimeZone);
				DateTime nextSessionStart = GetSessionStart(sessionTime);
				DateTime nextQuarterStart = GetQuarterStart(sessionTime);
				DateTime nextTradingDayStart = GetTradingDayStart(sessionTime);

				if (nextQuarterStart != quarterStart)
				{
					int quarterIndex = (int)((quarterStart - sessionStart).TotalMinutes / 90d);
					DrawTimelineCell(quarterStartX, boundaryX, quarterY, quarterHeight,
						QuarterLabels[quarterIndex], quarterBrushes[quarterIndex]);
					if (quarterStart == activeQuarterStart)
						DrawTimelineOutline(quarterStartX, boundaryX, quarterY, quarterHeight, 2f);
					quarterStart = nextQuarterStart;
					quarterStartX = boundaryX;
				}

				if (nextSessionStart != sessionStart)
				{
					int sessionIndex = sessionStart.Hour / 6;
					DrawTimelineCell(sessionStartX, boundaryX, sessionY, sessionHeight,
						SessionLabels[sessionIndex], quarterBrushes[GetSessionQuarterIndex(sessionIndex)]);
					if (sessionStart == activeSessionStart)
						DrawTimelineOutline(sessionStartX, boundaryX, sessionY, sessionHeight, 2f);
					sessionStart = nextSessionStart;
					sessionStartX = boundaryX;
				}

				if (nextTradingDayStart != tradingDayStart)
				{
					DayOfWeek tradingDay = tradingDayStart.AddDays(1).DayOfWeek;
					int weekQuarter = GetWeekQuarterIndex(tradingDay);
					DrawTimelineCell(tradingDayStartX, boundaryX, dayY, dayHeight,
						DayLabels[(int)tradingDay], weekQuarter >= 0 ? quarterBrushes[weekQuarter] : timelineNeutralBrush);
					if (tradingDayStart == activeTradingDayStart)
						DrawTimelineOutline(tradingDayStartX, boundaryX, dayY, dayHeight, 2f);
					tradingDayStart = nextTradingDayStart;
					tradingDayStartX = boundaryX;
				}

				previousBarX = barX;
			}

			int finalQuarterIndex = (int)((quarterStart - sessionStart).TotalMinutes / 90d);
			DrawTimelineCell(quarterStartX, panelRight, quarterY, quarterHeight,
				QuarterLabels[finalQuarterIndex], quarterBrushes[finalQuarterIndex]);
			if (quarterStart == activeQuarterStart)
				DrawTimelineOutline(quarterStartX, panelRight, quarterY, quarterHeight, 2f);

			int finalSessionIndex = sessionStart.Hour / 6;
			DrawTimelineCell(sessionStartX, panelRight, sessionY, sessionHeight,
				SessionLabels[finalSessionIndex], quarterBrushes[GetSessionQuarterIndex(finalSessionIndex)]);
			if (sessionStart == activeSessionStart)
				DrawTimelineOutline(sessionStartX, panelRight, sessionY, sessionHeight, 2f);

			DayOfWeek finalTradingDay = tradingDayStart.AddDays(1).DayOfWeek;
			int finalWeekQuarter = GetWeekQuarterIndex(finalTradingDay);
			DrawTimelineCell(tradingDayStartX, panelRight, dayY, dayHeight,
				DayLabels[(int)finalTradingDay], finalWeekQuarter >= 0 ? quarterBrushes[finalWeekQuarter] : timelineNeutralBrush);
			if (tradingDayStart == activeTradingDayStart)
				DrawTimelineOutline(tradingDayStartX, panelRight, dayY, dayHeight, 2f);
		}

		private void DrawSecondaryTimeZone(ChartControl chartControl, int fromIndex, int toIndex,
			DateTime visibleStart, DateTime visibleEnd, float pixelsPerMinute, float y, float height)
		{
			const float titleWidth = 110f;
			const float labelWidth = 52f;
			const float minLabelSpacing = 55f;
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
			TimeZoneInfo targetTimeZone = GetSecondaryTimeZone();
			float titleRight = Math.Min(panelRight, panelLeft + titleWidth);
			int intervalMinutes = GetTimeLabelIntervalMinutes(pixelsPerMinute);
			DateTime labelTime = GetFirstTimeLabel(visibleStart, intervalMinutes);
			float lastLabelX = float.MinValue;
			var row = new RectangleF(panelLeft, y, panelRight - panelLeft, height);

			RenderTarget.FillRectangle(row, timelineNeutralBrush);
			RenderTarget.DrawRectangle(row, timelineBorderBrush, 1f);
			RenderTarget.DrawText(
				UseLocalMachineTime ? "Local" : targetTimeZone.Id == "Israel Standard Time" ? "Jerusalem" : targetTimeZone.Id,
				secondaryTimeTextFormat, new RectangleF(panelLeft, y, titleRight - panelLeft, height), timelineTextBrush);

			if (chartControl.BarSpacingType != BarSpacingType.TimeBased)
			{
				for (int index = fromIndex; index <= toIndex; index++)
				{
					float x = chartControl.GetXByBarIndex(ChartBars, index);
					if (x < titleRight + labelWidth * 0.5f || x > panelRight - labelWidth * 0.5f || x - lastLabelX < minLabelSpacing)
						continue;

					DateTime convertedTime = ConvertChartTime(Bars.GetTime(index),
						Core.Globals.GeneralOptions.TimeZoneInfo, targetTimeZone);
					RenderTarget.DrawLine(new Vector2(x, y), new Vector2(x, y + height), timelineBorderBrush, 1f);
					RenderTarget.DrawText(convertedTime.ToString("HH:mm"), secondaryTimeTextFormat,
						new RectangleF(x - labelWidth * 0.5f, y, labelWidth, height), timelineTextBrush);
					lastLabelX = x;
				}
				return;
			}

			for (; labelTime <= visibleEnd; labelTime = labelTime.AddMinutes(intervalMinutes))
			{
				float x = chartControl.GetXByTime(labelTime);
				if (x < titleRight + labelWidth * 0.5f || x > panelRight - labelWidth * 0.5f || x - lastLabelX < minLabelSpacing)
					continue;

				DateTime convertedTime = ConvertChartTime(labelTime, Core.Globals.GeneralOptions.TimeZoneInfo, targetTimeZone);
				RenderTarget.DrawLine(new Vector2(x, y), new Vector2(x, y + height), timelineBorderBrush, 1f);
				RenderTarget.DrawText(convertedTime.ToString("HH:mm"), secondaryTimeTextFormat,
					new RectangleF(x - labelWidth * 0.5f, y, labelWidth, height), timelineTextBrush);
				lastLabelX = x;
			}

		}

		private void SubscribeCrosshair()
		{
			ChartControl control = ChartControl;
			if (control == null)
				return;

			crosshairChartControl = control;
			control.Dispatcher.InvokeAsync(() =>
			{
				if (crosshairChartControl != control || State == State.Terminated || UserControlCollection == null)
					return;

				crosshairTimeText = new System.Windows.Controls.TextBlock
				{
					Foreground = Brushes.Gainsboro,
					FontFamily = new System.Windows.Media.FontFamily("Arial"),
					FontSize = 12,
					FontWeight = System.Windows.FontWeights.SemiBold,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					VerticalAlignment = System.Windows.VerticalAlignment.Center,
					TextAlignment = System.Windows.TextAlignment.Center
				};
				crosshairTimeMarker = new System.Windows.Controls.Border
				{
					Background = Brushes.Black,
					BorderBrush = Brushes.DimGray,
					BorderThickness = new System.Windows.Thickness(1),
					Child = crosshairTimeText,
					Height = ChartingExtensions.ConvertFromVerticalPixels(20, control.PresentationSource),
					HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
					IsHitTestVisible = false,
					Opacity = 0.96,
					VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
					Visibility = System.Windows.Visibility.Collapsed,
					Width = ChartingExtensions.ConvertFromHorizontalPixels(68, control.PresentationSource)
				};
				System.Windows.Controls.Panel.SetZIndex(crosshairTimeMarker, int.MaxValue);
				UserControlCollection.Add(crosshairTimeMarker);
				control.PreviewMouseMove += OnChartMouseMove;
				control.MouseLeave += OnChartMouseLeave;
			});
		}

		private void UnsubscribeCrosshair()
		{
			ChartControl control = crosshairChartControl;
			System.Windows.Controls.Border marker = crosshairTimeMarker;
			crosshairChartControl = null;
			crosshairTimeMarker = null;
			crosshairTimeText = null;
			if (control == null)
				return;

			control.Dispatcher.InvokeAsync(() =>
			{
				control.PreviewMouseMove -= OnChartMouseMove;
				control.MouseLeave -= OnChartMouseLeave;
				if (marker != null && UserControlCollection != null && UserControlCollection.Contains(marker))
					UserControlCollection.Remove(marker);
			});
		}

		private void OnChartMouseMove(object sender, MouseEventArgs e)
		{
			ChartControl control = crosshairChartControl;
			if (!ShowSecondaryTimeZone || control == null || crosshairTimeMarker == null || crosshairTimeText == null
				|| control.PresentationSource == null || ChartPanel == null
				|| control.CrosshairType == CrosshairType.Off)
			{
				OnChartMouseLeave(sender, e);
				return;
			}
			if (control.Properties.CrosshairIsLocked && crosshairTimeMarker.Visibility == System.Windows.Visibility.Visible)
				return;

			System.Windows.Point panelPoint = e.GetPosition(ChartPanel);
			if (panelPoint.X < 0 || panelPoint.X > ChartPanel.ActualWidth
				|| panelPoint.Y < 0 || panelPoint.Y > ChartPanel.ActualHeight)
			{
				OnChartMouseLeave(sender, e);
				return;
			}

			int deviceX = ChartingExtensions.ConvertToHorizontalPixels(e.GetPosition(control).X, control.PresentationSource);
			DateTime chartTime = control.BarSpacingType == BarSpacingType.TimeBased
				? control.GetTimeByX(deviceX)
				: control.GetTimeBySlotIndex(Math.Round(control.GetSlotIndexByX(deviceX)));
			DateTime crosshairTime = ConvertChartTime(chartTime,
				Core.Globals.GeneralOptions.TimeZoneInfo, GetSecondaryTimeZone());
			double markerWidth = crosshairTimeMarker.Width;
			double gap = ChartingExtensions.ConvertFromHorizontalPixels(6, control.PresentationSource);
			double titleRight = ChartingExtensions.ConvertFromHorizontalPixels(110, control.PresentationSource);
			double markerLeft = panelPoint.X + gap;
			if (markerLeft + markerWidth > ChartPanel.ActualWidth)
				markerLeft = panelPoint.X - markerWidth - gap;
			markerLeft = Math.Max(titleRight, Math.Min(ChartPanel.ActualWidth - markerWidth, markerLeft));

			crosshairTimeText.Text = crosshairTime.ToString("HH:mm:ss");
			crosshairTimeMarker.Margin = new System.Windows.Thickness(markerLeft, 0, 0,
				ChartingExtensions.ConvertFromVerticalPixels(2, control.PresentationSource));
			crosshairTimeMarker.Visibility = System.Windows.Visibility.Visible;
		}

		private void OnChartMouseLeave(object sender, MouseEventArgs e)
		{
			if (crosshairChartControl != null && crosshairChartControl.CrosshairType != CrosshairType.Off
				&& crosshairChartControl.Properties.CrosshairIsLocked && ShowSecondaryTimeZone)
				return;
			if (crosshairTimeMarker != null)
				crosshairTimeMarker.Visibility = System.Windows.Visibility.Collapsed;
		}

		private TimeZoneInfo GetSecondaryTimeZone()
		{
			if (UseLocalMachineTime)
				return TimeZoneInfo.Local;

			return FindTimeZoneOrDefault(string.IsNullOrWhiteSpace(SecondaryTimeZoneId)
				? "Israel Standard Time"
				: SecondaryTimeZoneId, TimeZoneInfo.Local);
		}

		private static TimeZoneInfo FindTimeZoneOrDefault(string id, TimeZoneInfo fallback)
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (TimeZoneNotFoundException)
			{
				return fallback;
			}
			catch (InvalidTimeZoneException)
			{
				return fallback;
			}
		}

		private static DateTime ConvertChartTime(DateTime time, TimeZoneInfo sourceTimeZone, TimeZoneInfo targetTimeZone)
		{
			return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), sourceTimeZone, targetTimeZone);
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
			float startX = chartControl.GetXByTime(start);
			float endX = chartControl.GetXByTime(end);
			DrawTimelineOutline(startX, endX, y, height, width);
		}

		private void DrawTimelineOutline(float startX, float endX, float y, float height, float width)
		{
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
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

		private static DateTime GetSessionStart(DateTime time)
		{
			return time.Date.AddHours((time.Hour / 6) * 6);
		}

		private static DateTime GetQuarterStart(DateTime time)
		{
			DateTime sessionStart = GetSessionStart(time);
			return sessionStart.AddMinutes((int)(time - sessionStart).TotalMinutes / 90 * 90);
		}

		private static DateTime GetTradingDayStart(DateTime time)
		{
			return time.TimeOfDay >= TimeSpan.FromHours(18)
				? time.Date.AddHours(18)
				: time.Date.AddDays(-1).AddHours(18);
		}

		private static bool ShouldDrawLayer(float pixelsPerMinute, float durationMinutes, float minimumWidth)
		{
			return pixelsPerMinute * durationMinutes >= minimumWidth;
		}

		private static int GetTimeLabelIntervalMinutes(float pixelsPerMinute)
		{
			foreach (int minutes in TimeLabelIntervals)
				if (minutes * pixelsPerMinute >= 55f)
					return minutes;
			return TimeLabelIntervals[TimeLabelIntervals.Length - 1];
		}

		private static DateTime GetFirstTimeLabel(DateTime time, int intervalMinutes)
		{
			long intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
			long remainder = time.Ticks % intervalTicks;
			return remainder == 0 ? time : time.AddTicks(intervalTicks - remainder);
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

		public sealed class TimeZoneIdConverter : StringConverter
		{
			public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
			{
				return true;
			}

			public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
			{
				return true;
			}

			public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
			{
				var timeZones = TimeZoneInfo.GetSystemTimeZones();
				var ids = new string[timeZones.Count];
				for (int index = 0; index < timeZones.Count; index++)
					ids[index] = timeZones[index].Id;
				return new StandardValuesCollection(ids);
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

		[Display(Name = "ShowSecondaryTimeZone", Description = "Show a converted time row below the Quarterly Theory timeline", GroupName = "Secondary Time", Order = 0)]
		public bool ShowSecondaryTimeZone { get; set; }

		[Display(Name = "UseLocalMachineTime", Description = "Use the computer's local time instead of the selected time zone", GroupName = "Secondary Time", Order = 1)]
		public bool UseLocalMachineTime { get; set; }

		[TypeConverter(typeof(TimeZoneIdConverter))]
		[Display(Name = "SecondaryTimeZone", Description = "Time zone shown when local machine time is off", GroupName = "Secondary Time", Order = 2)]
		public string SecondaryTimeZoneId { get; set; }

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
