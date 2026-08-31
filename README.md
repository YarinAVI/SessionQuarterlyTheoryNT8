# Session Quarterly Theory for NinjaTrader 8

`TradingSessionStatus` displays a compact Quarterly Theory timeline below price. It uses the chart/NinjaTrader time zone, follows playback time when Market Replay is active, and can show a second converted time row.

## Timeline

The indicator renders three aligned timeline rows and an optional converted-time row:

1. **90-minute quarters** — every six-hour session is divided into `Q1`, `Q2`, `Q3`, and `Q4`.
2. **Daily sessions** — the 24-hour cycle is divided into four six-hour quarters.
3. **Trading day** — Monday through Thursday are colored as weekly `Q1` through `Q4`; Friday and weekends use a neutral color.
4. **Secondary time** — converted from NinjaTrader's configured time zone, with daylight-saving rules applied automatically.

| Daily quarter | Session | Time |
| --- | --- | --- |
| Q1 | Asia | 18:00–00:00 |
| Q2 | London | 00:00–06:00 |
| Q3 | AM | 06:00–12:00 |
| Q4 | PM | 12:00–18:00 |

The active 90-minute quarter, session, and trading day are outlined when they are visible on the chart.

## Installation

1. Copy `TradingSessionStatus.cs` to:

   ```text
   Documents\NinjaTrader 8\bin\Custom\Indicators\
   ```

2. Open **NinjaTrader 8 → New → NinjaScript Editor**.
3. Press **F5** to compile.
4. Add **TradingSessionStatus** to a chart from the Indicators window.

## Notes

- Session times are fixed to the Quarterly Theory cycle shown above.
- Times use NinjaTrader's configured chart time zone.
- The secondary time row is enabled by default and uses `Israel Standard Time` (Jerusalem). Choose another Windows time zone or enable `UseLocalMachineTime` in the indicator properties.
- With NinjaTrader's crosshair enabled, the converted `HH:mm:ss` marker follows the pointer beside the crosshair so the line does not cover it.
- Timeline detail automatically reduces when zoomed out to avoid unnecessary rendering work.
- Legacy vertical session lines are not rendered.
- The indicator is chart-only and does not place trades.
