# Trading Skill - Chart Analysis and Bot Strategy

This skill is the trading brain for this project.

Primary use:

- Analyze chart screenshots that the user sends.
- Explain market context using the RSI+Stoch+Fibo system.
- Decide whether the chart is in range, breakout, pullback, entry setup, active trade, invalidation, or no-trade state.
- Translate discretionary chart analysis into rules that can later be implemented in Pine Script and the bot.

Secondary use:

- Guide Pine Script strategy implementation.
- Keep TradingView bot behavior aligned with the chart-analysis method.
- Preserve the full roadmap so future code changes do not drift away from the trading plan.

The current implementation source of truth is:

- `pine/rsi_stoch_strategy_v2.pine` - active bot/backtest strategy
- `pine/rsi_stoch_state.pine` - overlay state visualization
- `pine/rsi_stoch_indicator.pine` - oscillator pane and divergence visualization
- `docs/STRATEGY-LOGIC.md` - current strategy logic reference
- `docs/planning.md` - roadmap and backtest criteria
- `execute.md` - iteration history

---

## 1. Core Identity

The system is a **Range-Breakout strategy with Dual-Oscillator Regime Filtering and Fibonacci Extension targets**.

It is not a simple RSI/Stoch buy-low sell-high strategy.

The system reads the market in this order:

1. Market structure and phase.
2. Stoch OVB/OVS cycles.
3. RSI regime state.
4. Support/resistance range built from completed oscillator cycles.
5. Breakout direction.
6. Trend type: `PERFECT` or `V-SHAPE`.
7. Pullback quality.
8. Reversal candle / sub-wave confirmation.
9. Risk, SL, Fibo TP, and invalidation.

When analyzing a chart, never jump directly to "buy" or "sell". First name the current state.

---

## 2. Chart Analysis Mission

When the user sends a chart image, analyze it as a trader first and as a coder second.

Always answer:

1. What market phase is visible?
2. What are the important completed Stoch OVB/OVS cycles?
3. Where are the current support/resistance range levels?
4. Has price broken the range?
5. If breakout happened, is it `PERFECT` or `V-SHAPE`?
6. Has pullback happened yet?
7. Is there a valid reversal trigger?
8. Where are entry, SL, TP1, TP2, and invalidation?
9. Is the setup worth trading, or should it be skipped?
10. What would the bot need to detect this state?

If the screenshot does not show enough information, say exactly what is missing. Common missing data:

- Symbol and timeframe.
- Full RSI/Stoch pane.
- Enough left-side history to identify completed OVB/OVS cycles.
- Price scale or clear candle highs/lows.
- Existing strategy labels or Fibo levels.

---

## 3. Trading Philosophy

- **Context first.** Candle patterns are meaningless without market phase.
- **Do not chase breakout candles.** Breakout-bar entries usually have poor R:R.
- **Indicators are secondary.** RSI and Stoch identify cycle state and pullback state; structure triggers entries.
- **Trade liquidity and trapped behavior, not prediction.** Good entries often happen after retail traders are trapped by a sweep, failed breakout, or emotional continuation.
- **Every trade needs a thesis and an invalidation.** If the invalidation point is unclear, skip.
- **Every trade must pay for its risk.** If RR to TP1 is too small, skip.
- **Do not add complexity before measurement.** New filters are added only after backtest or visual review proves they solve a real failure mode.

---

## 4. Manual Chart Reading Flow

Use this flow whenever reading a screenshot.

### Step 1 - Identify Market Phase

Classify the chart:

- `Range`: price cycling between support/resistance with no confirmed breakout.
- `Breakout`: price has closed outside a completed range.
- `Trend`: breakout has follow-through and Fibo targets are active.
- `Pullback`: active trend is retracing after breakout.
- `Transition`: failed breakout, Fibo destroyed, or unclear structure.
- `No-trade`: insufficient RR, unclear levels, late entry, or missing confirmation.

### Step 2 - Mark Stoch Cycles

Look at Stoch:

- OVB zone: `k > 80`
- OVS zone: `k < 20`

For each completed cycle:

- Completed OVB cycle gives a price high.
- Completed OVS cycle gives a price low.

Use these price highs/lows to build range levels.

### Step 3 - Build Base Range

Current system range:

- Resistance = price high from the last completed OVB cycle.
- Support = price low from the last completed OVS cycle.

Do not draw levels from random candles if Stoch cycles do not support them.

### Step 4 - Detect Breakout

Breakout rules:

- Long breakout: candle closes above resistance.
- Short breakout: candle closes below support.

Prefer close confirmation. Wicks alone are not enough.

### Step 5 - Classify Trend

After breakout, classify:

- `PERFECT` long: Stoch troughs show a higher low before breakout.
- `PERFECT` short: Stoch peaks show a lower high before breakout.
- `V-SHAPE`: breakout happens without structural oscillator confirmation.

Interpretation:

- `PERFECT`: stronger structure, can target fuller Fibo extension.
- `V-SHAPE`: faster and more fragile, take profit earlier.

### Step 6 - Wait for Pullback

Do not enter immediately after breakout.

Long:

- Wait for Stoch to touch OVS after breakout.
- Track the pullback low.

Short:

- Wait for Stoch to touch OVB after breakout.
- Track the pullback high.

### Step 7 - Confirm Reversal

Long reversal:

- Identify `swingLow`, the lowest low of the pullback.
- Identify `preSwingLowHigh`, the high of the candle before the swing-low candle.
- Valid reversal when price closes above `preSwingLowHigh`.

Short reversal:

- Identify `swingHigh`, the highest high of the pullback.
- Identify `preSwingHighLow`, the low of the candle before the swing-high candle.
- Valid reversal when price closes below `preSwingHighLow`.

### Step 8 - Validate Risk

For a long:

- Entry = reversal close or planned execution price.
- SL = swing low minus ATR buffer, or exact swing low for mentor-pure mode.
- TP1 = adaptive Fibo target.
- RR = `(TP1 - entry) / (entry - SL)`.

For a short:

- Entry = reversal close or planned execution price.
- SL = swing high plus ATR buffer, or exact swing high for mentor-pure mode.
- TP1 = adaptive Fibo target.
- RR = `(entry - TP1) / (SL - entry)`.

Default minimum:

- RR must be at least `3.0`.

If TP is on the wrong side of entry, RR is invalid and the setup is skipped.

---

## 5. Bot Strategy State Machine

This section maps chart analysis to Pine/bot logic.

### 5.1 Stoch Cycle Tracking

The bot tracks price extremes while Stoch is inside OVB/OVS:

- Stoch in OVB (`k > 80`) -> track highest price high in that OVB cycle.
- Stoch exits OVB -> save as `lastCompletedOvbHigh`.
- Stoch in OVS (`k < 20`) -> track lowest price low in that OVS cycle.
- Stoch exits OVS -> save as `lastCompletedOvsLow`.

These are price levels, not oscillator values.

### 5.2 Base Range

- After Stoch exits OVS, `resistanceLine = lastCompletedOvbHigh`.
- After Stoch exits OVB, `supportLine = lastCompletedOvsLow`.

Breakout:

- `breakoutUp = close > resistanceLine and close[1] <= resistanceLine`
- `breakoutDown = close < supportLine and close[1] >= supportLine`

### 5.3 Trend Type

- Long `PERFECT`: breakout up with prior Stoch trough higher than previous trough.
- Short `PERFECT`: breakout down with prior Stoch peak lower than previous peak.
- Otherwise `V-SHAPE`.

Trade IDs:

- `Long-P`, `Short-P` for `PERFECT`.
- `Long-V`, `Short-V` for `V-SHAPE`.

---

## 6. Entry Rules

Current bot implementation is HTF-only.

### Long Entry

1. Breakout above resistance.
2. Trend active, direction long.
3. Stoch touches OVS after breakout.
4. Track `swingLow`.
5. Capture `preSwingLowHigh`.
6. Entry trigger: `close > preSwingLowHigh`.
7. Consume setup after first trigger. Do not chase later bars unless a new lower `swingLow` forms.

### Short Entry

1. Breakout below support.
2. Trend active, direction short.
3. Stoch touches OVB after breakout.
4. Track `swingHigh`.
5. Capture `preSwingHighLow`.
6. Entry trigger: `close < preSwingHighLow`.
7. Consume setup after first trigger. Do not chase later bars unless a new higher `swingHigh` forms.

### Not Current Entry Behavior

These are future ideas, not active bot rules:

- Limit entry at broken structure retest.
- FVG-backed entry.
- LTF sub-wave entry.
- MTF V-SHAPE recovery.

---

## 7. Risk Management

### Position Sizing

Risk is sized from stop distance:

```pine
riskCash = strategy.equity * riskPercent / 100.0
qty = riskCash / math.abs(entryPrice - slPrice)
```

Defaults:

- `riskPercent = 1.0`
- `default_qty_type = strategy.fixed`
- Never use 100% equity sizing.

### Stop Loss

Current bot default:

- Long SL = `swingLow - ATR * atrBufferMult`
- Short SL = `swingHigh + ATR * atrBufferMult`
- Default `atrBufferMult = 0.5`

Mentor-pure A/B mode:

- Long SL = exact `swingLow`.
- Short SL = exact `swingHigh`.
- Test by setting `atrBufferMult = 0`.

### RR Filter

Use signed directional reward:

```pine
calcRR(string side, float entryPrice, float slPrice, float tpPrice) =>
    risk = math.abs(entryPrice - slPrice)
    reward = side == "Long" ? tpPrice - entryPrice : entryPrice - tpPrice
    risk > 0 and reward > 0 ? reward / risk : 0.0
```

Default:

- `rrMinimum = 3.0`

Never use absolute reward. Wrong-direction TP must block the trade.

---

## 8. Fibo Targets and Trade Management

### Adaptive Targets

| Trend Type | TP1 | TP2 | Behavior |
|---|---:|---:|---|
| `PERFECT` | 1.618 | 2.618 | Let structure play out further |
| `V-SHAPE` | 1.5 | 2.0 | Take profit earlier before 1.618 rejection |

Default exits:

- TP1 closes 50%.
- TP2 closes the remaining position.
- If `useTP2 = false`, TP1 closes 100%.

### Smart Partial Exit

Use this to lock profit before TP1 if momentum reaches the opposite extreme and reverses.

Long partial:

- Position is open.
- Position is in profit.
- Stoch is in OVB.
- Bearish reversal: `close < low[1]`.
- No partial already fired in the current OVB cycle.
- Close 50%.

Short partial:

- Position is open.
- Position is in profit.
- Stoch is in OVS.
- Bullish reversal: `close > high[1]`.
- No partial already fired in the current OVS cycle.
- Close 50%.

### Trailing Stop

Trailing stop uses Stoch swing structure:

- Long trailing stop ratchets up to completed OVS lows.
- Short trailing stop ratchets down to completed OVB highs.

Do not use one-bar trailing as the main trailing stop.

---

## 9. Invalidation and Fibo Shift

### Current Implemented Invalidation

Long invalidation:

- `close < fiboHead`

Short invalidation:

- `close > fiboHead`

V-SHAPE additional invalidation:

- Reverses more than 30% of Fibo range before reaching 1.618.

### Planned Fibo-Shift Rule

When Fibo is destroyed:

- **Path A: Fibo destroyed + RSI reached OVB/OVS**
  - Reset all state.
  - The mountain is cut.
  - Wait for a new structure.

- **Path B: Fibo destroyed + RSI did not reach OVB/OVS**
  - Do not reset.
  - Shift Fibo wider to include broader swing.
  - Treat the failed inner V-SHAPE as part of a larger developing structure.

This is a planned rule. Use it in manual chart analysis, but mark it clearly if the current bot does not implement it yet.

---

## 10. RSI and Divergence

Current RSI role:

- Regime awareness.
- Helps decide reset vs Fibo shift.
- Can help warn of exhaustion.

Current bot behavior:

- RSI extreme alone does not wipe state.
- RSI divergence is visualized in the indicator but is not a mandatory entry gate.

Manual chart analysis may mention divergence as supporting evidence, but do not promote it to a primary signal unless the user asks for discretionary analysis.

---

## 11. Full Implementation Roadmap

Keep this plan inside the skill so chart analysis, Pine code, and bot work stay aligned.

### Phase 1 - HTF-Only MVP

Goal: establish baseline backtest with the current HTF-only strategy.

Current and required rules:

- Range built from Stoch OVB/OVS price cycles.
- Breakout closes beyond support/resistance.
- Trend type classified as `PERFECT` or `V-SHAPE`.
- Wait for pullback.
- Enter on structural reversal candle.
- Risk per trade defaults to 1%.
- SL uses structural pivot plus ATR buffer.
- RR minimum defaults to 3.0.
- Adaptive targets by trend type.
- Strategy and overlay indicator must show the same entries.
- Smart partial exit is enabled by default.

Validation still needed:

- BTCUSDT 1H smoke test.
- XAUUSD 1H smoke test after v2.7.
- EURUSD 1H smoke test.
- ATR buffer A/B: `0.5` vs `0.0`.
- Confirm V-SHAPE TP1 hit-rate is higher than PERFECT TP1 hit-rate.
- Implement Fibo-Shift Path B only after entry behavior is stable.

### Phase 2 - Robustness Validation

Only add these if Phase 1 data shows the need:

- Volatility regime filter for chop.
- Volume confirmation on breakout/pullback.
- Walk-forward optimization.
- Stoch depth predictor:
  - Shallow previous Stoch cycle means the next move may drag out.
  - Do not enter too early on shallow pullback behavior.
- HTF oscillator soft-exit:
  - If higher timeframe reaches OVB/OVS while trade is profitable, consider partial close.

### Phase 3 - MTF V-SHAPE Recovery

Only if Phase 1/2 logs show meaningful V-SHAPE profits are being missed.

Plan:

- HTF provides context, Fibo, and trend state.
- LTF provides entry timing.
- Add LTF pullback + reversal entry while HTF trend remains active.
- Add sub-wave reversal as earlier confirmation before HTF candle close.
- Test without distance filter first.
- Use MTF Fibo cluster as priority target if current-TF and HTF levels overlap.

### Phase 4 - MTF with Distance Filter

Only if Phase 3 recovers V-SHAPE but late entries hurt profit factor.

Distance filter:

- For long, HTF price should be below the late-extension danger zone, roughly Fibo 1.0-1.3.
- For short, symmetric rule.
- Goal: recover V-SHAPE without buying the top or shorting the bottom.

### Phase 5 - Live Forward Test

Only after backtest passes:

- Deploy winning config to Binance Testnet.
- Run 30 days forward test.
- Use reduced live risk, e.g. 0.5%.
- Move to production only if forward results match backtest within reasonable tolerance.

---

## 12. Backtest Discipline

Phase 1 success criteria:

| Metric | Threshold |
|---|---:|
| Profit Factor after commission | > 1.3 |
| Max Drawdown | < 25% |
| Win Rate | 30-50% |
| Trade Count | >= 50 / symbol / year |
| OOS Sharpe | > 0.5 |
| Max Consecutive Losses | < 10 |

Primary formal symbols:

- BTCUSDT
- XAUUSD
- EURUSD

Primary formal timeframe:

- 1H

Track breakdown:

- PERFECT vs V-SHAPE.
- Long vs Short.
- TP1 hit-rate by trend type.
- Commission sensitivity.
- ATR buffer `0.0` vs `0.5`.
- Trades skipped by RR.
- Trades skipped because price already passed TP.

---

## 13. Image Analysis Checklist

When reading a chart image, use this checklist.

### Visible Context

- Symbol:
- Timeframe:
- Current price:
- Chart range visible:
- Are RSI/Stoch panes visible?
- Are strategy labels/Fibo lines visible?

### Structure

- Market phase:
- Current support:
- Current resistance:
- Last completed OVB price high:
- Last completed OVS price low:
- Breakout direction:
- Trend type:

### Pullback and Entry

- Has post-breakout pullback happened?
- Did Stoch touch the required zone?
- Long `swingLow` / short `swingHigh`:
- Reversal trigger level:
- Is trigger confirmed by close or only wick?
- Is entry late?

### Risk and Trade Plan

- Possible entry:
- SL:
- TP1:
- TP2:
- RR to TP1:
- Invalidation:
- Partial-exit condition:
- Decision: trade / wait / skip.

### Bot Translation

- What state should Pine be in?
- What variable likely changed?
- What condition is missing?
- Is this current-bot behavior or future-roadmap behavior?

---

## 14. Response Template for Chart Analysis

When the user sends a chart screenshot, answer in this structure.

```text
ภาพรวม:
- Phase:
- Direction bias:
- Trade state:

ระดับสำคัญ:
- Support:
- Resistance:
- Fibo head/end:
- TP1/TP2:

Setup:
- Breakout:
- Trend type:
- Pullback:
- Reversal trigger:

Risk:
- Entry:
- SL:
- RR:
- Invalidation:

สรุป:
- Trade / Wait / Skip:
- เหตุผล:
- Bot rule ที่เกี่ยวข้อง:
```

Keep the answer practical. If levels are approximate because they come from an image, say they are approximate.

---

## 15. Pine Script Rules

- Use Pine Script v5 only.
- One strategy or indicator per file.
- Keep `rsi_stoch_strategy_v2.pine` as active strategy unless deliberately creating a new version.
- Keep `rsi_stoch_strategy.pine` as v1 comparison.
- Keep strategy and overlay indicator entries synced.
- Use signed directional reward for RR.
- Consume entry setups on the first valid reversal trigger, even if RR blocks the trade.
- Avoid repainting and lookahead. Use `barmerge.lookahead_off` with `request.security`.
- Make experiments configurable with inputs.
- Keep debug labels optional.

---

## 16. TradingView Webhook Contract

When Pine sends alerts to the bot, payloads should match backend schemas.

Required fields:

- `action`
- `symbol`
- `price`
- `timestamp`

Security:

- `WEBHOOK_PASSPHRASE` comes from environment variables.
- `BINANCE_API_KEY` and `BINANCE_API_SECRET` come from environment variables.
- Never hardcode credentials.
- Never log API keys, secrets, or sensitive full request bodies.

---

## 17. Decision Rules

Use these rules when uncertain:

- If range is unclear, do not trade.
- If breakout is only a wick, wait.
- If breakout happened but no pullback, wait.
- If pullback happened but no reversal close, wait.
- If RR < 3.0, skip.
- If entry is beyond TP1 or TP is on the wrong side, skip.
- If V-SHAPE reaches rejection area near 1.618 without strength, take profit earlier or avoid new entry.
- If Fibo is destroyed and RSI reached extreme, reset.
- If Fibo is destroyed and RSI did not reach extreme, consider broader Fibo shift in manual analysis.
- If chart image lacks oscillator pane or enough history, state the limitation before giving a trade call.
