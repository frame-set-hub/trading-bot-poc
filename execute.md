# Trading Bot POC — Execution Progress

Track progress here — **update whenever a sub-task completes**. Supports re-opening a new session with clear context.

**Related:** [docs/planning.md](./docs/planning.md) (why these steps exist) · [docs/STRATEGY-LOGIC.md](./docs/STRATEGY-LOGIC.md) (current pine logic) · [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) (infrastructure) · [NotebookLM notebook](https://notebooklm.google.com/notebook/9db4787d-26a7-4cfd-ad2a-a308efb222ab)

---

## ✅ MVP Foundation (Infrastructure) — Completed
- [x] TradingView → Webhook → AWS Lambda → Binance flow working (EMA 12/26 reference strategy).
- [x] FastAPI + Mangum + Binance SDK plumbing in place.
- [x] SAM deploy template.
- [x] Pine scripts scaffolded: `rsi_stoch_strategy.pine`, `rsi_stoch_state.pine`, `rsi_stoch_indicator.pine`.
- [x] Strategy logic documented in `docs/STRATEGY-LOGIC.md`.
- [x] Deep-research pass via NotebookLM — 66 sources ingested, critique captured.

---

## 🚀 Phase 1 — HTF-Only MVP *(current)*

Target: baseline backtest passes the success criteria in [docs/planning.md §3](./docs/planning.md#3-poc-success-criteria).

Working file: `pine/rsi_stoch_strategy_v2.pine` (kept v1 untouched for A/B compare).

### Step 1 — Fix sizing & risk ✅ DONE (v2)
- [x] Removed `strategy.percent_of_equity=100`. Now `default_qty_type=strategy.fixed`.
- [x] Added `riskPercent` input (default 1%, range 0.1-5%).
- [x] `qty = (equity × riskPercent / 100) / |entry − SL|` at entry.
- [x] Strategy Report no longer shows 100% ruin scenario.

### Step 2 — Entry logic ⚠️ ITERATED 5× — IMPROVING (v2.4 implemented + bug fixes)
- [x] **v2 (initial):** `trendActive + stochReachedExtreme + stochCrossedUp + close>high[1]` (all same bar) — too strict, 8 trades only
- [x] **v2.1:** added `stochCrossWindow=5 bars` — 53 trades but Fibo cycles too small → -4.52% PnL
- [x] **v2.2:** added `rrMinimum=3.0` filter to skip small-Fibo trades — partial fix
- [x] **v2.3:** removed RSI Reset force-close + state reset (mentor Clip 10 alignment) — 416 trades, -23% (DISASTER — `close>high[1]` fired on every continuation bar)
- [x] **v2.4 (proper reversal candle):** rewrote entry to track swing low + fire on `close > preSwingLowHigh` + consume per pullback. Dropped Stoch K-cross-D. SL = `swingLow − ATR×0.5`. → 94 trades, 21.28% WR, -1.49% (huge improvement from disaster)
- [x] **v2.4 fix (signed RR):** discovered `calcRR()` used `math.abs()` for reward → trades with TP on wrong side of entry (e.g. Long with TP < entry, when price ran past Fibo target on continuation) passed RR≥3 filter. Fixed: `calcRR(side, ...)` now uses signed reward (`tp - entry` for long, `entry - tp` for short); reward ≤ 0 → RR = 0 → blocked.
- [x] **v2.4 visual cleanup:** added `showEntryBox` input (default OFF — big green/red label was blocking chart). Added `showAtrInfo` table at top-right showing ATR(14), 0.5×ATR, suggested Long/Short SL prices.
- [x] **v2.4 logic fix ("ผิดแท่ง" / wrong bar entry chase):** Found a bug where if the FIRST breakout candle was blocked by the RR filter, the setup wasn't consumed. This caused the strategy to fire repeatedly on subsequent candles, entering late in the move with a worse RR (chasing). Fixed by replacing the complex `preSwingLowHigh` with a pure `close > high[1]` check (matching "แท่งกลับตัว = ทะลุ high ของแท่งก่อนหน้า" literally), and moving `setupConsumed = true` OUTSIDE the RR check so it consumes the setup immediately upon the first reversal, blocking any re-entries unless a new swing low is formed.

### Step 3 — SL with ATR buffer ✅ DONE (v2 + v2.4 swing-low fix)
- [x] `atrBufferMult` input default 0.5 (configurable 0-2.0).
- [x] Long SL = `low − atrBufferMult × ATR`, short symmetric.
- [x] Open question logged: ATR (research) vs structural pure (mentor) — to A/B in Step 5.
- [x] **v2.4 implemented:** SL = `swingLow − ATR×buffer` (long) / `swingHigh + ATR×buffer` (short) — properly anchored to structural pivot, not entry-bar extreme.
- [x] **v2.4 visual:** ATR info table at top-right shows current ATR(14) + suggested SL prices, so SL distance is easy to eyeball before opening manual trades on TradingView.

### Step 3b — Adaptive TP by trend type ✅ DONE (v2.5)
- [x] Added inputs: `tpPerfect1=1.618`, `tpPerfect2=2.618`, `tpVShape1=1.5`, `tpVShape2=2.0` (group "Targets").
- [x] `tp1FiboLevel = trendType == "PERFECT" ? tpPerfect1 : tpVShape1` (same for tp2).
- [x] Trade tags include `-P` / `-V` suffix (e.g. `Long-P`, `Short-V`) → Strategy Tester groups by trend type.
- [ ] Verify V-SHAPE TP1 hit-rate > PERFECT TP1 hit-rate (awaiting backtest run).
- Note: TP captured at entry implicitly (trendType doesn't change mid-trade in Phase 1; will need explicit freeze when Fibo Shift / Step 6 lands).

### Step 3c — Smart Partial Exit at Stoch Extreme + Reversal ✅ DONE (v2.7)
Implements [docs/planning.md §2.8](./docs/planning.md#28-smart-partial-exit--stoch-extreme--reversal-phase-1-lock-in-step-3c).
- [x] Added `useSmartPartialExit` input (default ON, group "Targets").
- [x] `var string currentEntryId` tracks current entry tag (Long-P / Long-V / Short-P / Short-V) — set at entry, cleared when position size = 0.
- [x] Long: in-profit + `stochInOvb` + `close < low[1]` + not yet partial-closed this OVB cycle → `strategy.close(currentEntryId, qty_percent=50, comment="Stoch OVB partial")`.
- [x] Short symmetric (in-profit + `stochInOvs` + `close > high[1]` → `comment="Stoch OVS partial"`).
- [x] Re-trigger guards: `partialClosedThisOvbCycle` / `partialClosedThisOvsCycle` reset on `stochExitedOvb` / `stochExitedOvs`.
- [x] Visual marker: orange diamond above bar (long partial) / below bar (short partial).
- [ ] Verify in backtest: Trailing SL trade count drops; "Stoch partial" exits appear in trade log.

### Step 2 follow-up — v2.6 reversal candle correction + ATR SL bands ✅ DONE
- [x] User feedback (XAUUSD 1D, v2.5): 76 trades, 22.37% WR, +27.99% PnL — gain OK but WR low. "แท่งกลับตัวไม่ตรงหลัก" + ATR ไม่เห็นบน chart.
- [x] **Fix 1 (reversal candle):** changed entry trigger from `close > high[1]` (any prior bar) to `close > preSwingLowHigh` (high of bar BEFORE swing low) — matches mentor's literal rule "การเบรกแท่งก่อนหน้าของแท่งต่ำสุด". Added `var float preSwingLowHigh / preSwingHighLow`, captured at every new swing low/high update.
- [x] **Fix 2 (ATR SL bands):** added `plot()` of `low − atrBufferMult × ATR` (lime line below price) + `high + atrBufferMult × ATR` (red line above price) on chart. Toggle via existing `showAtrInfo` input. Now user sees SL distance at every bar without having to eyeball.

### Step 4 — Sync indicator with strategy ✅ DONE
- [x] Strip ATR-trailing-as-entry from `rsi_stoch_state.pine`.
- [x] Indicator BUY/SELL labels = strategy entries (no divergence).
- [x] Keep Stoch-swing trailing stop in strategy.

### Step 5 — Instrument smoke tests ⚠️ PARTIAL (XAUUSD 1D + 4H tested, results below)
- [x] Tested XAUUSD 4H (v1 vs v2 comparison, 24 vs 8 trades — see Iteration Log §1)
- [x] Tested XAUUSD 1D (v2.1 → v2.2 → v2.3 progression — see Iteration Log §2-4)
- [ ] BTCUSDT 1H smoke test (after Step 2 stable)
- [ ] EURUSD 1H smoke test (after Step 2 stable)
- [ ] **A/B test: ATR Buffer style vs Mentor-pure** — run each with `atrBufferMult=0.5` then `atrBufferMult=0`, log per-symbol winner.

### Step 6 — Fibo-Shift logic on invalidation ⏳ NOT STARTED
- [ ] **Status note:** mentor's "RSI extreme alone shouldn't cut" rule was partially addressed in v2.3 (removed agressive RSI reset). Still need explicit Path B (shift Fibo wider when invalidated + RSI calm).
- [ ] **Blocked by:** Step 2 (v2.4) — current entry instability makes Fibo Shift testing meaningless.

### Step 7 — Full Phase-1 backtest (Config A) ⏳ NOT STARTED
- [ ] 2 years × 3 symbols × 1H baseline.
- [ ] Skip-log V-SHAPE breakouts (no pullback) → Phase 3 trigger evaluation.
- [ ] Decision per success criteria in planning.md §3.
- [ ] **Blocked by:** Steps 2 + 5 + 3b stable.

---

## 📊 Iteration Log & Feedback (Phase 1, Steps 1-3)

Each row = one pine version tested on TradingView. Used to track what worked, what failed, why.

### §1 — XAUUSD 4H baseline (v1 reference vs v2 initial)

| Version | Trades | Win Rate | P&L | Max DD | Notes |
|---|---|---|---|---|---|
| v1 (original, 100% equity) | 24 | 50% (12/24) | +2.34% | (high — Risk-of-Ruin) | Aggressive sizing inflates % gain |
| **v2 (1% risk + pullback entry)** | 8 | 62.5% (5/8) | +0.02% | 2.26% | ⚠️ **Too few trades** — Stoch cross + close>high[1] same-bar requirement too strict |

**Diagnosis §1:** v2 entry condition required Stoch K cross D AND close>high[1] on the SAME bar — these events rarely align. Win rate higher (better quality) but trade count too low for statistical significance.

**Action taken:** v2.1 added `stochCrossWindow=5 bars` (allow K cross within last N bars) + persistent BUY/SELL labels.

---

### §2 — XAUUSD 1D + window fix (v2.1)

| Symbol/TF | Trades | Win Rate | P&L | Max DD | Notes |
|---|---|---|---|---|---|
| XAUUSD 1D | 53 | 33.96% (18/53) | **−4.52%** | 17.88% | ⚠️ Trade count up, but quality dropped — Fibo cycles too small on 1D |

**Diagnosis §2:** Fibo ranges captured by code = small recent Stoch cycles (~14-18 USD) instead of structural swings user expected (~93 USD). Cause: aggressive RSI reset wipes cycle history → only short cycles fit.

**Action taken:** v2.2 added `rrMinimum=3.0` filter — block trades where TP1 too close to entry. Trust whatever swing TF gives, filter at output.

---

### §3 — XAUUSD 1D + RR filter (v2.2)

| Test | Trades | Win Rate | P&L | Notes |
|---|---|---|---|---|
| XAUUSD 1D, rr≥3 | (not separately recorded) | — | — | Visible on chart: yellow X marks = blocked trades. RR filter working as intended. |

**Diagnosis §3:** RR filter screens out small-Fibo trades successfully (yellow X clusters visible). But continuation entries in strong trends still missed because RSI Reset wipes state.

**Action taken:** v2.3 removed RSI Reset force-close + state reset on RSI extreme alone (per mentor Clip 10 rule).

---

### §4 — XAUUSD 1D + no RSI Reset (v2.3) — DISASTER

| Symbol/TF | Trades | Win Rate | P&L | Max DD | Notes |
|---|---|---|---|---|---|
| XAUUSD 1D | **416** | **10.82% (45/416)** | **−23.21%** | 44.52% | 🔴 **Entry condition fundamentally wrong** — fires on every uptrend bar |

**Diagnosis §4:** Removing RSI Reset exposed an underlying bug — `close > high[1]` (current bar's close above PREVIOUS bar's high) is satisfied on MOST uptrend bars, not a unique reversal event. Combined with continuation logic, entries fire on consecutive bars.

**Critical insight:** Claude misinterpreted the mentor's "previous-bar break" rule. Mentor meant:
- Find the **swing low** (lowest bar of the pullback)
- Look at the bar **BEFORE** that swing low
- "แท่งกลับตัว" = close above THAT specific bar's high (not just any prior bar's high)
- This is a UNIQUE event per pullback, not a recurring condition

**Action taken:** v2.4 — rewrote entry trigger to use proper swing-low + pre-swing-low-high logic.

---

### §5 — XAUUSD 1D + proper reversal candle (v2.4 first run)

| Symbol/TF | Trades | Win Rate | P&L | Max DD | Notes |
|---|---|---|---|---|---|
| XAUUSD 1D | **94** | **21.28% (20/94)** | **−1.49%** | 12.99% | ✅ Major improvement from v2.3 disaster (-23% → -1.5%). 1 entry per pullback as intended. |

**Diagnosis §5:** Proper swing-low logic working — entries no longer fire every bar. Trade count down 4.5×. Some entries still bad: visual inspection on chart shows trades where `Long @ 2338` but `TP2 = 2275` (TP below entry on a Long).

**Root cause:** When a long trend extends past its original Fibo TP target (1.618), subsequent pullback-recovery entries can fire ABOVE the TP zone. `calcRR()` was using `math.abs()` for reward — a negative reward (TP on wrong side of entry) became positive after abs → passed RR≥3 filter → bad trade fired.

**Action taken:** v2.4 fix — `calcRR(side, ...)` now uses signed reward direction. Long requires `tp > entry`, Short requires `tp < entry`. Reward ≤ 0 → RR = 0 → automatically blocked.

**Plus visual cleanup:** added `showEntryBox` toggle (default OFF — was blocking chart). Added ATR info table top-right with current ATR(14), 0.5×ATR, suggested SL prices.

---

### §6 — XAUUSD 1D + signed RR + logic fix (v2.4 final) — pending test

**Diagnosis §6:** User reported "ผิดแท่ง" (entering on the wrong bar). Analysis showed that if the initial valid reversal candle was blocked due to a bad RR (<3), `longSetupConsumed` remained false. Because `close > preSwingLowHigh` remained true for the rest of the uptrend, the strategy kept attempting to enter on every subsequent bar, eventually catching a trade late in the move (which is dangerous and gives bad prices). Additionally, the rule required waiting for Stoch to mathematically exit the OVS zone, which delayed entries unnecessarily.

**Action taken:** 
1. Rewrote reversal to pure `close > high[1]` (break previous candle's high).
2. Removed the `not stochInOvs` restriction.
3. Moved `setupConsumed := true` to trigger immediately on the very first reversal candle. If it fails the RR filter, the trade is skipped and NOT chased on later bars. It only re-arms if price makes a new `swingLow`.

Expected:
- Trade count drop further (wrong-direction TPs filtered + no chasing late entries)
- Win rate up (only valid R:R trades fire on the actual initial reversal)
- P&L break-even or positive

**Status:** Code ready, awaiting user re-run on TradingView.

---

### §7 — Adaptive TP (v2.5) — pending test

**Change:** TP1/TP2 ระดับ Fibo เปลี่ยนตาม trendType ที่ entry:
- **PERFECT** → TP1=1.618, TP2=2.618 (Golden ratio มาตรฐาน)
- **V-SHAPE** → TP1=1.5, TP2=2.0 (หนีก่อน rejection wall ที่ 1.618)

**Trade IDs:** `Long-P` / `Long-V` / `Short-P` / `Short-V` → Strategy Tester แสดง breakdown ตาม trend type ได้ตรงๆ

**คาดหวัง:**
- V-SHAPE TP1 hit-rate **สูงกว่า** PERFECT TP1 hit-rate (target ใกล้กว่า)
- V-SHAPE avg win เล็กลง (1.5R vs 1.618R) แต่ count เพิ่ม
- Net PnL ขึ้นกับสัดส่วน V-SHAPE vs PERFECT ใน asset/TF นั้นๆ
- Math expectancy: V-SHAPE 0.55×1.5R − 0.45×1R = +0.375R/trade vs 0.40×1.618R − 0.60×1R = +0.047R/trade (theoretical edge)

**Status:** Code ready, awaiting user backtest run.

---

## 🎯 Next Actions (in order)

1. ~~Complete v2.4 implementation~~ ✅ DONE (swing-low + reversal candle + signed RR + visual cleanup)
2. ~~Step 3b — Adaptive TP~~ ✅ DONE (v2.5: PERFECT 1.618/2.618 vs V-SHAPE 1.5/2.0, trade tags -P/-V)

3. **Test v2.5 on XAUUSD 1D** *(awaiting user)*
   - Expect: trade count similar to v2.4 (entry logic unchanged)
   - Expect: V-SHAPE trades hit TP1 more often (target closer)
   - Verify in Strategy Tester "List of Trades" — see -P vs -V suffix breakdown

4. **Test v2.5 on multiple symbols/TFs**
   - XAUUSD 4H + 1D
   - BTCUSDT 1H + 4H
   - EURUSD 1H + 4H

5. **A/B ATR buffer (Step 5 sub-task)**
   - atrBufferMult = 0.5 (research) vs 0 (mentor pure) per symbol
   - User now has ATR info table to eyeball both before deciding

6. **Then Step 4 (Sync indicator) → Step 6 (Fibo Shift) → Step 7 (full backtest)**

---

## 🚀 Phase 2 — Robustness *(blocked until Phase 1 passes)*
- [ ] Add Bollinger-in-Keltner squeeze filter — block entries outside squeeze regime.
- [ ] Add explosive-volume condition on breakout bar + declining-volume on pullback.
- [ ] Build WFO harness (rolling 12m IS / 3m OOS).
- [ ] Re-run Config A with WFO; validate OOS Sharpe > 0.5.
- [ ] Parameter sweep: RSI length (10-20), Stoch %K (7-14), ATR buffer (0.3-1.5), reverse threshold (0.2-0.4), **V-SHAPE TP1 (1.272 / 1.382 / 1.5)**, **V-SHAPE TP2 (1.786 / 2.0 / 2.272)**.
- [ ] **Stoch depth predictor (mentor Clip 11):** record previous Stoch cycle peak/trough depth; correlate shallow-previous → deep-current cycle and longer trend hold time. If correlation > 0.3, enable as regime tag.
- [ ] **HTF oscillator soft-exit (mentor Clip 11):** add optional rule — when in profit and HTF (4H or Weekly) Stoch hits OVB/OVS, take 50% off regardless of Fibo target. Test as (a) replacement for Fibo TP1, (b) added on top of Fibo TPs.

---

## 🚀 Phase 3 — MTF V-SHAPE Recovery (Config B) *(trigger-conditional)*

**Trigger:** Phase-1 skip-log shows missed V-SHAPE PnL projected > 20% of realized PnL.
- [ ] Design HTF state export via `request.security` (1H context).
- [ ] Implement LTF (5m) pullback+Stoch entry gated on HTF `trendActive`.
- [ ] Verify no look-ahead bias (`barmerge.lookahead_off`).
- [ ] Run 2-year backtest × 3 symbols, compare to Phase 1.
- [ ] Compute ΔPF, ΔMaxDD, ΔSharpe vs Config A.
- [ ] **Sub-wave reversal alt entry (mentor Clip 11):** allow LTF reversal structure (sub-wave forming reversal) to confirm HTF reversal earlier than HTF candle close. Tag these as separate entry type for stat breakdown.
- [ ] **Fibo cluster priority target:** when current-TF Fibo level overlaps HTF Fibo within `clusterThresh` (existing code in `rsi_stoch_state.pine`), if cluster price sits between TP1 and TP2, override TP1 to the cluster level. Compare PnL with/without this rule.

---

## 🚀 Phase 4 — MTF + Distance Filter (Config C) *(trigger-conditional)*

**Trigger:** Config B MaxDD rises > 5% vs Config A AND V-SHAPE late-entry failures > 30%.
- [ ] Add distance gate: block entry if HTF price > Fibo 1.3 from fiboHead.
- [ ] Sweep distance threshold 1.0–1.5 in Phase 2-style WFO.
- [ ] Run full backtest, compare to Config B.
- [ ] Final 3-way decision per [§5.4 matrix](./docs/planning.md#54-win-condition-decision-matrix).

---

## 🚀 Phase 5 — Live Forward *(gated on Phase 2 or later passing)*
- [ ] Wire winning pine config to existing `alert()` → webhook → Lambda → Binance flow.
- [ ] Start on Binance **Testnet** for 14 days — zero code changes to prod path, just `BINANCE_TESTNET=True`.
- [ ] Promote to production on 0.5% risk for 30 days.
- [ ] Compare live metrics to backtest — must match within ±20% on PF, DD, WinRate.

---

## Notes & Decisions

| Context | Latest detail / decisions |
|-------|----------------|
| **Scope (Phase 1)** | HTF-only (1H). MTF, WFO, volatility filter all deferred — trigger-conditional. |
| **Risk sizing** | **1% per trade, max 2%.** Never 100% equity. |
| **Entry philosophy** | **v2.4:** "Reversal candle" = close above high of bar BEFORE swing low. Single event per pullback (not recurring). Drop Stoch K-cross-D requirement (structural break = trigger). |
| **Earlier entry attempts (deprecated)** | v2 used `close>high[1]` literally → fired on every uptrend bar (416 trades disaster in v2.3). Rewrote in v2.4 using proper swing-low logic per mentor clip 10. |
| **Adaptive TP** | PERFECT → TP1=1.618 / TP2=2.618. V-SHAPE → TP1=1.5 / TP2=2.0. Rationale: V-SHAPE follow-through <40%, so 1.618 acts as rejection wall — TP at 1.5 books gains *before* the rejection zone. Expectancy math: 0.55×1.5R − 0.45×1R = +0.375R vs 0.40×1.618R − 0.60×1R = +0.047R. |
| **Fibo Shift on invalidation** | mentor Clip 10 — if Fibo destroyed but RSI didn't reach OVB/OVS, shift Fibo wider instead of resetting (V-SHAPE → broader PERFECT). Phase 1 lock-in. |
| **2-Fibo averaging-down** | **SKIPPED** — conflicts with risk-per-trade rule. Knowledge captured in [docs/planning.md §9.1](./docs/planning.md#91-2-fibo-averaging-down-mentor-clip-11--skipped). Pyramiding-on-confirmation considered as Phase 4+ alternative if needed. |
| **V-SHAPE handling** | Accepted miss in Phase 1 (evidence: <40% follow-through). Recover only if skip-log justifies Phase 3. |
| **Commission** | 0.1% per side (Binance-like). Backtest must remain profitable at 0.2% stress test. |
| **Data window** | 2024-04 → 2026-04 (2 years), 60/40 train/test split. |
| **Decision cadence** | Revisit plan after each phase completes — no auto-promotion to next phase. |

---

## 🚀 Phase 2 / Phase 3 Ideas (from NotebookLM Analysis)
*(Deferred from Phase 1 to keep MVP simple and testable)*
- **Long Candle Filter**: If the reversal candle is too long (measured vs ATR), SL is too far, RR is skewed. Wait for a smaller pullback before entering.
- **MTF Take Profit**: If the Mother Timeframe (e.g., Day/Week) hits Overbought/Oversold, close the trade immediately even if Fibo targets are not met.
- **Breakeven Trailing SL**: Once the trade is in profit (e.g., hits Fibo 161.8 or forms a new minor swing), move SL to entry price to make it risk-free.
- **Stoch Dragging Filter ("จิ้มไม่ลึก เตรียมรากเลื้อย")**: If the previous Stoch extreme was shallow, the current one will likely drag. Avoid trading reversals during a deep drag.
- **MTF "3 Waves" (3 ขยัก) & "ตึงมือ"**: Advanced context rules based on Sub-Wave count and MTF alignment for filtering or scalp counter-trends.

---

## Research Reference

See [docs/planning.md §9](./docs/planning.md#9-research-reference) for full list. NotebookLM notebook: `9db4787d-26a7-4cfd-ad2a-a308efb222ab`.
