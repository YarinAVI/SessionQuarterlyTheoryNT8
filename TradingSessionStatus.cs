#region Using declarations
using System;
using System.Collections.Generic;
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
		private class SessionRenderResources : IDisposable
		{
			public Brush WpfBrush { get; private set; }
			public SharpDX.Direct2D1.SolidColorBrush LineBrush { get; private set; }
			public SharpDX.Direct2D1.SolidColorBrush TextBrush { get; private set; }
			public SharpDX.Direct2D1.SolidColorBrush AxisFillBrush { get; private set; }

			public void Recreate(SharpDX.Direct2D1.RenderTarget renderTarget, Brush brush)
			{
				WpfBrush = brush;

				DisposeDx();
				if (renderTarget == null)
					return;

				SharpDX.Color4 baseColor = GetBrushColor(brush, 1f);
				LineBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color4(baseColor.Red, baseColor.Green, baseColor.Blue, 0.55f));
				TextBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color4(baseColor.Red, baseColor.Green, baseColor.Blue, 0.95f));
				AxisFillBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color4(baseColor.Red, baseColor.Green, baseColor.Blue, 0.90f));
			}

			public void Dispose()
			{
				DisposeDx();
				WpfBrush = null;
			}

			private void DisposeDx()
			{
				LineBrush?.Dispose();
				LineBrush = null;

				TextBrush?.Dispose();
				TextBrush = null;

				AxisFillBrush?.Dispose();
				AxisFillBrush = null;
			}
		}

		private SessionRenderResources asiaResources;
		private SessionRenderResources londonResources;
		private SessionRenderResources newYorkResources;
		private SharpDX.Direct2D1.SolidColorBrush axisTextBrush;
		private SharpDX.Direct2D1.SolidColorBrush axisBorderBrush;
		private SharpDX.Direct2D1.StrokeStyle axisBorderStrokeStyle;
		private SharpDX.Direct2D1.StrokeStyle closeLineStrokeStyle;
		private TextFormat sessionLabelTextFormat;
		private TextFormat axisBoxTextFormat;
		private TextFormat timelineTextFormat;
		private SharpDX.Direct2D1.SolidColorBrush timelineTextBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineBorderBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineActiveBrush;
		private SharpDX.Direct2D1.SolidColorBrush timelineNeutralBrush;
		private SharpDX.Direct2D1.SolidColorBrush[] quarterBrushes;

		private string instanceTag;
		private string lastLabel;
		private DateTime now = Core.Globals.Now;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Displays Quarterly Theory sessions and 90-minute quarters in a timeline below price, using NinjaTrader's configured time zone.";
				Name = "TradingSessionStatus";
				Calculate = Calculate.OnEachTick;
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
					&& GetWeekQuarterIndex(DayOfWeek.Wednesday) == 2,
					"Quarterly timeline mapping is invalid.");

				instanceTag = Guid.NewGuid().ToString("N");
				lastLabel = string.Empty;
				asiaResources = new SessionRenderResources();
				londonResources = new SessionRenderResources();
				newYorkResources = new SessionRenderResources();

				sessionLabelTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.SemiBold, FontStyle.Normal, 12f)
				{
					TextAlignment = TextAlignment.Leading,
					ParagraphAlignment = ParagraphAlignment.Near,
					WordWrapping = WordWrapping.NoWrap
				};

				axisBoxTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontWeight.SemiBold, FontStyle.Normal, 12f)
				{
					TextAlignment = TextAlignment.Leading,
					ParagraphAlignment = ParagraphAlignment.Center,
					WordWrapping = WordWrapping.NoWrap
				};

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

				sessionLabelTextFormat?.Dispose();
				sessionLabelTextFormat = null;

				axisBoxTextFormat?.Dispose();
				axisBoxTextFormat = null;

				timelineTextFormat?.Dispose();
				timelineTextFormat = null;

				asiaResources = null;

				londonResources = null;

				newYorkResources = null;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 0)
				return;

			if (ShowLabel)
			{
				string label = BuildLabel(Now);
				if (!string.Equals(label, lastLabel, StringComparison.Ordinal))
				{
					Draw.TextFixedFine(this, $"TradingSessionStatus_Label_{instanceTag}", label, LabelPosition);
					lastLabel = label;
				}
			}
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();

			DisposeDxResources();
			if (RenderTarget == null)
				return;

			asiaResources?.Recreate(RenderTarget, AsiaLineBrush);
			londonResources?.Recreate(RenderTarget, LondonLineBrush);
			newYorkResources?.Recreate(RenderTarget, NewYorkLineBrush);

			axisTextBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 1f));
			axisBorderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0f, 0f, 0f, 0.35f));
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

			axisBorderStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, new SharpDX.Direct2D1.StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Solid,
				StartCap = SharpDX.Direct2D1.CapStyle.Flat,
				EndCap = SharpDX.Direct2D1.CapStyle.Flat,
				LineJoin = SharpDX.Direct2D1.LineJoin.Miter
			});

			closeLineStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, new SharpDX.Direct2D1.StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Dash,
				StartCap = SharpDX.Direct2D1.CapStyle.Flat,
				EndCap = SharpDX.Direct2D1.CapStyle.Flat,
				LineJoin = SharpDX.Direct2D1.LineJoin.Miter
			});
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

			float stripTop = (float)ChartPanel.Y + (float)ChartPanel.H
				- quarterHeight - sessionHeight - dayHeight - bottomMargin;
			if (stripTop < ChartPanel.Y)
				return;

			float quarterY = stripTop;
			float sessionY = quarterY + quarterHeight;
			float dayY = sessionY + sessionHeight;
			DateTime firstDay = visibleStart.Date.AddDays(-1);
			DateTime lastDay = visibleEnd.Date.AddDays(1);

			for (DateTime day = firstDay; day <= lastDay; day = day.AddDays(1))
			{
				for (int sessionIndex = 0; sessionIndex < 4; sessionIndex++)
				{
					DateTime sessionStart = day.AddHours(sessionIndex * 6);
					DateTime sessionEnd = sessionStart.AddHours(6);
					int sessionQuarter = GetSessionQuarterIndex(sessionIndex);

					DrawTimelineCell(chartControl, sessionStart, sessionEnd, sessionY, sessionHeight,
						GetSessionName(sessionIndex), quarterBrushes[sessionQuarter]);

					for (int quarterIndex = 0; quarterIndex < 4; quarterIndex++)
					{
						DateTime quarterStart = sessionStart.AddMinutes(quarterIndex * 90);
						DrawTimelineCell(chartControl, quarterStart, quarterStart.AddMinutes(90), quarterY,
							quarterHeight, "Q" + (quarterIndex + 1), quarterBrushes[quarterIndex]);
					}
				}

				DateTime tradingDayStart = day.AddHours(18);
				DateTime tradingDate = day.AddDays(1);
				int weekQuarter = GetWeekQuarterIndex(tradingDate.DayOfWeek);
				DrawTimelineCell(chartControl, tradingDayStart, tradingDayStart.AddDays(1), dayY, dayHeight,
					tradingDate.ToString("ddd"), weekQuarter >= 0 ? quarterBrushes[weekQuarter] : timelineNeutralBrush);
			}

			DateTime nowTime = Now;
			int activeSessionIndex = nowTime.Hour / 6;
			DateTime activeSessionStart = nowTime.Date.AddHours(activeSessionIndex * 6);
			int activeQuarterIndex = Math.Min(3, (int)(nowTime - activeSessionStart).TotalMinutes / 90);
			DateTime activeQuarterStart = activeSessionStart.AddMinutes(activeQuarterIndex * 90);
			DateTime activeTradingDayStart = nowTime.TimeOfDay >= TimeSpan.FromHours(18)
				? nowTime.Date.AddHours(18)
				: nowTime.Date.AddDays(-1).AddHours(18);

			DrawTimelineOutline(chartControl, activeQuarterStart, activeQuarterStart.AddMinutes(90), quarterY, quarterHeight, 2f);
			DrawTimelineOutline(chartControl, activeSessionStart, activeSessionStart.AddHours(6), sessionY, sessionHeight, 2f);
			DrawTimelineOutline(chartControl, activeTradingDayStart, activeTradingDayStart.AddDays(1), dayY, dayHeight, 2f);
		}

		private void DrawTimelineCell(ChartControl chartControl, DateTime start, DateTime end, float y, float height,
			string text, SharpDX.Direct2D1.Brush fillBrush)
		{
			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;
			float startX = chartControl.GetXByTime(start);
			float endX = chartControl.GetXByTime(end);
			float left = Math.Max(panelLeft, Math.Min(startX, endX));
			float right = Math.Min(panelRight, Math.Max(startX, endX));
			if (right <= left)
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

		private static string GetSessionName(int sessionIndex)
		{
			switch (sessionIndex)
			{
				case 0: return "Lon";
				case 1: return "AM";
				case 2: return "PM";
				default: return "Asia";
			}
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

		private void DrawSessionMarker(ChartControl chartControl, ChartScale chartScale, DateTime time, string label, SessionRenderResources resources, int sessionId, bool isClose, HashSet<long> dedupe, Dictionary<int, int> markerCollisionCounts, SharpDX.Direct2D1.StrokeStyle strokeStyle)
		{
			if (chartControl == null || resources?.LineBrush == null || ChartPanel == null || Bars == null || Bars.Count < 1)
				return;

			DateTime firstBarTime = Bars.GetTime(0);
			DateTime lastBarTime = Bars.GetTime(Bars.Count - 1);
			if (time < firstBarTime || time > lastBarTime)
				return;

			int barIndex = Bars.GetBar(time);
			if (barIndex < 0)
				return;

			long dedupeKey = ((long)barIndex << 3) | ((long)(sessionId & 0x3) << 1) | (isClose ? 1 : 0);
			if (dedupe != null && !dedupe.Add(dedupeKey))
				return;

			float x = GetMarkerX(chartControl, time, barIndex, isClose);
			float left = (float)ChartPanel.X;
			float right = left + (float)ChartPanel.W;
			if (x < left || x > right)
				return;

			float yTop = (float)ChartPanel.Y;
			float yBottom = yTop + (float)ChartPanel.H;

			float width = Math.Max(1f, LineWidth);
			if (strokeStyle == null)
				RenderTarget.DrawLine(new Vector2(x, yTop), new Vector2(x, yBottom), resources.LineBrush, width);
			else
				RenderTarget.DrawLine(new Vector2(x, yTop), new Vector2(x, yBottom), resources.LineBrush, width, strokeStyle);

			if (ShowLineLabels && resources.TextBrush != null && resources.AxisFillBrush != null)
			{
				int collisionSlot = ReserveCollisionSlot(markerCollisionCounts, x);
				DrawVerticalLabel(x, label, resources.TextBrush, yTop, yBottom, barIndex, chartScale, collisionSlot);
				DrawAxisBox(x, time, resources.AxisFillBrush, collisionSlot);
			}
		}

		private static int ReserveCollisionSlot(Dictionary<int, int> markerCollisionCounts, float x)
		{
			if (markerCollisionCounts == null)
				return 0;

			int key = (int)Math.Round(x);
			if (markerCollisionCounts.TryGetValue(key, out int count))
			{
				markerCollisionCounts[key] = count + 1;
				return count;
			}

			markerCollisionCounts[key] = 1;
			return 0;
		}

		private float GetMarkerX(ChartControl chartControl, DateTime time, int barIndex, bool isClose)
		{
			float x = chartControl.GetXByBarIndex(ChartBars, barIndex);
			if (!AvoidLabelOverlap || Bars == null || Bars.Count < 2)
				return x;

			DateTime barTime = Bars.GetTime(barIndex);
			if (time == barTime)
			{
				if (barIndex > 0)
				{
					float xPrev = chartControl.GetXByBarIndex(ChartBars, barIndex - 1);
					return (xPrev + x) / 2f;
				}

				float xNext = chartControl.GetXByBarIndex(ChartBars, barIndex + 1);
				return (x + xNext) / 2f;
			}

			if (isClose)
			{
				float xNext;
				if (barIndex < Bars.Count - 1)
				{
					xNext = chartControl.GetXByBarIndex(ChartBars, barIndex + 1);
				}
				else if (barIndex > 0)
				{
					float xPrev = chartControl.GetXByBarIndex(ChartBars, barIndex - 1);
					xNext = x + (x - xPrev);
				}
				else
				{
					xNext = x + 10f;
				}

				return (x + xNext) / 2f;
			}

			return x;
		}

		private static DateTime GetSessionEndTime(DateTime day, TimeSpan start, TimeSpan end)
		{
			DateTime endTime = day.Add(end);
			if (end <= start)
				endTime = day.AddDays(1).Add(end);

			return endTime;
		}

		private void DrawVerticalLabel(float x, string text, SharpDX.Direct2D1.Brush brush, float yTop, float yBottom, int barIndex, ChartScale chartScale, int collisionSlot)
		{
			if (string.IsNullOrWhiteSpace(text) || sessionLabelTextFormat == null || RenderTarget == null)
				return;

			const float padY = 6f;
			using (var layout = new TextLayout(Core.Globals.DirectWriteFactory, text, sessionLabelTextFormat, 300f, 24f))
			{
				float labelLen = (float)Math.Max(1d, layout.Metrics.Width);
				float labelThickness = (float)Math.Max(1d, layout.Metrics.Height);
				float panelHeight = Math.Max(1f, yBottom - yTop);
				float bottomReservation = Math.Min(panelHeight * 0.30f, 44f);

				float yMin = yTop + padY;
				float yMax = yBottom - bottomReservation - labelLen;
				if (yMax < yMin)
					yMax = yMin;

				// Default: keep the label near the bottom like TradingView.
				float yStart = yMax;

				// Dynamic: if we overlap nearby candles, move above/below the occupied range.
				if (chartScale != null && TryGetOccupiedYRange(barIndex, chartScale, out float occTop, out float occBottom))
				{
					const float gap = 10f;
					occTop = Math.Max(yTop, Math.Min(yBottom, occTop));
					occBottom = Math.Max(yTop, Math.Min(yBottom, occBottom));

					float labelTop = yStart;
					float labelBottom = yStart + labelLen;
					bool overlaps = labelTop <= occBottom && labelBottom >= occTop;

					if (overlaps)
					{
						float belowStart = occBottom + gap;
						float belowY = Math.Max(yMin, belowStart);
						bool belowFits = belowY <= yMax;

						float aboveY = Math.Min(yMax, (occTop - gap) - labelLen);
						bool aboveFits = aboveY >= yMin;

						if (belowFits)
						{
							// Put it as low as possible while staying below the candle range.
							yStart = yMax;
							if (yStart < belowY)
								yStart = belowY;
						}
						else if (aboveFits)
						{
							// Put it as low as possible while staying above the candle range.
							yStart = aboveY;
						}
					}
				}

					yStart += LineLabelOffsetTicks;
					yStart = Math.Max(yMin, Math.Min(yMax, yStart));

					float originY = yStart + labelLen;
					float originX = GetLabelOriginX(x, labelThickness, collisionSlot);
					Vector2 origin = new Vector2(originX, originY);

					Matrix3x2 oldTransform = RenderTarget.Transform;
					RenderTarget.Transform = Matrix3x2.Rotation(-(float)(Math.PI / 2.0), origin) * oldTransform;
					RenderTarget.DrawTextLayout(origin, layout, brush);
					RenderTarget.Transform = oldTransform;
				}
			}

			private bool TryGetOccupiedYRange(int barIndex, ChartScale chartScale, out float yTop, out float yBottom)
			{
				yTop = float.MaxValue;
				yBottom = float.MinValue;

				if (Bars == null || Bars.Count < 1 || chartScale == null)
					return false;

				int first = Math.Max(0, barIndex - 2);
				int last = Math.Min(Bars.Count - 1, barIndex + 2);
				if (first > last)
					return false;

				for (int idx = first; idx <= last; idx++)
				{
					if (idx < 0 || idx >= Bars.Count)
						continue;

					double high = Bars.GetHigh(idx);
					double low = Bars.GetLow(idx);
					float highY = chartScale.GetYByValue(high);
					float lowY = chartScale.GetYByValue(low);
					float barTop = Math.Min(highY, lowY);
					float barBottom = Math.Max(highY, lowY);
					yTop = Math.Min(yTop, barTop);
					yBottom = Math.Max(yBottom, barBottom);
				}

			if (yTop == float.MaxValue || yBottom == float.MinValue)
				return false;

			const float pad = 6f;
			yTop -= pad;
			yBottom += pad;
			return true;
		}

		private float GetLabelOriginX(float lineX, float labelThickness, int collisionSlot)
		{
			if (ChartPanel == null)
				return lineX;

			float panelLeft = (float)ChartPanel.X;
			float panelRight = panelLeft + (float)ChartPanel.W;

			float spacing = GetEstimatedBarSpacing();
			float pad = Math.Max(6f, spacing * 0.15f);

			int slot = Math.Max(0, collisionSlot);
			int layer = slot / 2;
			bool preferLeft = (slot % 2) == 0;

			float step = Math.Max(1f, labelThickness + pad);
			float originLeft = lineX - step - (layer * step);
			float originRight = lineX + pad + (layer * step);

			bool leftOk = originLeft >= panelLeft;
			bool rightOk = (originRight + labelThickness) <= panelRight;

			if (preferLeft)
			{
				if (leftOk)
					return originLeft;
				if (rightOk)
					return originRight;
			}
			else
			{
				if (rightOk)
					return originRight;
				if (leftOk)
					return originLeft;
			}

			return Math.Max(panelLeft, Math.Min(panelRight - labelThickness, originLeft));
		}

		private float GetEstimatedBarSpacing()
		{
			if (ChartBars == null || ChartPanel == null)
				return 10f;

			int fromIndex = ChartBars.FromIndex;
			int toIndex = ChartBars.ToIndex;
			if (fromIndex < 0 || toIndex <= fromIndex)
				return 10f;

			float width = (float)ChartPanel.W;
			int count = Math.Max(1, toIndex - fromIndex);
			return width / count;
		}

		private void DrawAxisBox(float x, DateTime time, SharpDX.Direct2D1.Brush fillBrush, int collisionSlot)
		{
			if (axisBoxTextFormat == null || axisTextBrush == null || axisBorderBrush == null || RenderTarget == null || ChartPanel == null)
				return;

			string text = time.ToString("dd MMM ''yy  HH:mm");

			const float paddingX = 8f;
			const float paddingY = 4f;
			const float radius = 4f;
			float bottomMargin = 3f + (LineLabelOffsetTicks * 0.5f);

			using (var layout = new TextLayout(Core.Globals.DirectWriteFactory, text, axisBoxTextFormat, 240f, 24f))
			{
				float textW = (float)Math.Ceiling(layout.Metrics.Width);
				float textH = (float)Math.Ceiling(layout.Metrics.Height);

				float boxW = textW + (paddingX * 2);
				float boxH = textH + (paddingY * 2);

				float left = x - (boxW / 2f);
				float panelLeft = (float)ChartPanel.X + 2f;
				float panelRight = (float)ChartPanel.X + (float)ChartPanel.W - 2f;
				if (left < panelLeft)
					left = panelLeft;
				if (left + boxW > panelRight)
					left = panelRight - boxW;

				float top = (float)ChartPanel.Y + (float)ChartPanel.H - bottomMargin - boxH;
				if (collisionSlot > 0)
				{
					const float gapY = 2f;
					top -= collisionSlot * (boxH + gapY);
					float minTop = (float)ChartPanel.Y + 2f;
					if (top < minTop)
						top = minTop;
				}

				var rect = new RectangleF(left, top, boxW, boxH);
				var rr = new SharpDX.Direct2D1.RoundedRectangle
				{
					Rect = rect,
					RadiusX = radius,
					RadiusY = radius
				};

				RenderTarget.FillRoundedRectangle(rr, fillBrush);
				RenderTarget.DrawRoundedRectangle(rr, axisBorderBrush, 1f, axisBorderStrokeStyle);

				RenderTarget.DrawTextLayout(new Vector2(left + paddingX, top + paddingY), layout, axisTextBrush);
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

			axisTextBrush?.Dispose();
			axisTextBrush = null;

			axisBorderBrush?.Dispose();
			axisBorderBrush = null;

			axisBorderStrokeStyle?.Dispose();
			axisBorderStrokeStyle = null;

			closeLineStrokeStyle?.Dispose();
			closeLineStrokeStyle = null;

			asiaResources?.Dispose();
			londonResources?.Dispose();
			newYorkResources?.Dispose();
		}

		private static SharpDX.Color4 GetBrushColor(Brush brush, float alpha)
		{
			if (brush is SolidColorBrush scb)
			{
				System.Windows.Media.Color c = scb.Color;
				return new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, Math.Max(0f, Math.Min(1f, alpha)));
			}

			return new SharpDX.Color4(1f, 1f, 1f, Math.Max(0f, Math.Min(1f, alpha)));
		}

		private string BuildLabel(DateTime time)
		{
			bool inAsia = IsInSession(time, AsiaStartTime.TimeOfDay, AsiaEndTime.TimeOfDay);
			bool inLondon = IsInSession(time, LondonStartTime.TimeOfDay, LondonEndTime.TimeOfDay);
			bool inNy = IsInSession(time, NewYorkStartTime.TimeOfDay, NewYorkEndTime.TimeOfDay);

			List<string> active = new List<string>(3);
			if (inAsia) active.Add("ASIA");
			if (inLondon) active.Add("LONDON");
			if (inNy) active.Add("NY");

			if (active.Count == 0)
				return "Session: NONE";
			if (active.Count == 1)
				return $"Session: {active[0]}";

			return $"Session: {string.Join(" + ", active)} (OVERLAP)";
		}

		private static bool IsInSession(DateTime time, TimeSpan start, TimeSpan end)
		{
			TimeSpan tod = time.TimeOfDay;

			if (start == end)
				return true;

			if (start < end)
				return tod >= start && tod < end;

			return tod >= start || tod < end;
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
