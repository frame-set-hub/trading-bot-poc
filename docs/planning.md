# Trading Bot POC — Strategy Planning & Backtest Roadmap

**See also:** [STRATEGY-LOGIC.md](./STRATEGY-LOGIC.md) (current pine logic) · [ARCHITECTURE.md](./ARCHITECTURE.md) (TradingView ↔ Lambda ↔ Binance) · [../execute.md](../execute.md) (progress tracker) · [NotebookLM research notebook](https://notebooklm.google.com/notebook/9db4787d-26a7-4cfd-ad2a-a308efb222ab) (evidence base)

---

## 1. Strategy Identity (Locked)

The RSI+Stoch+Fibo system is formally a **Range-Breakout strategy with Dual-Oscillator Regime Filtering and Fibonacci Extension targets**. Archetype cross-reference:

| Component | Archetype | Source |
|---|---|---|
| Entry logic | Hybrid of **2-bar reversal** + Linda Raschke **"Holy Grail"** (pullback + oscillator confirm) | NotebookLM — Algorithmic Trading Architectures |
| Trend classification | Bulkowski **U-shape (~65-70% follow-through)** vs **V-shape (<40%)** | NotebookLM — same |
| Regime reset | **RSI "cutting the mountain"** — documented technique | NotebookLM — same |
| TP placement | **Golden Fibo extension** 1.618 (institutional target) / 2.618 (parabolic target) | LuxAlgo — Basic Guide to Fibonacci Extensions |

---

## 2. Design Decisions (Phase 1 Lock-in)

Decisions that are fixed for MVP and only revisited after Phase-1 data is in.

### 2.1 Entry Logic
- **Timeframe scope:** HTF-only for MVP (1H primary).
- **Trigger sequence:**
  1. Breakout event: close crosses Support/Resistance (from prior Stoch OVB/OVS cycle extremes).
  2. Trend marked active (PERFECT or V-SHAPE classified).
  3. **Wait for pullback** — do NOT enter on the breakout bar.
  4. During pullback, Stoch %K must reach **<20 (long) / >80 (short)** at least once.
  5. Arm entry when Stoch %K crosses %D in trend direction.
  6. Fire entry when `close > high[1]` (long) or `close < low[1]` (short).
- **Rationale:** evidence from the research notebook shows close-of-breakout-bar entries suffer slippage and late-entry bias; pullback-break entries improve R:R and win rate at the cost of frequency (which is acceptable).

### 2.2 Exit & Targets — Adaptive by Trend Type
TP placement **adapts to the breakout classification** set in [STRATEGY-LOGIC.md §Step 3](./STRATEGY-LOGIC.md).

| Trend type | TP1 (50%) | TP2 (50%) | Rationale |
|---|---|---|---|
| **PERFECT** | Fibo **1.618** | Fibo **2.618** | Follow-through ~65-70%. 1.618 acts as a target zone the trend usually breaks through. |
| **V-SHAPE** | Fibo **1.5** | Fibo **2.0** | Follow-through <40%. 1.618 acts as a *rejection wall* (mentor rule: V-shape that fails to break 1.618 = Fibo destroyed). TP at 1.5 books gains *before* the rejection zone. |

**Why 1.5 for V-SHAPE — expectancy argument:**
The mentor's "Fibo destroyed if not reaching 1.618" rule (Clip 10) implies 1.618 functions as resistance for V-shape moves. If V-shape follow-through is ~40%, then ~60% of V-shapes get rejected at or before 1.618. Closing at 1.5 captures the bird-in-hand:

```
TP=1.5   → 0.55 × 1.5R − 0.45 × 1R = +0.375R per trade
TP=1.618 → 0.40 × 1.618R − 0.60 × 1R = +0.047R per trade
```

Phase 2 WFO will sweep 1.272 / 1.382 / 1.5 to find the empirical optimum (1.5 is non-canonical Fibo, locked for Phase 1 on the user-trader's intuition + this expectancy framing).

- **SL (initial):** entry-bar structural low/high **− 0.5× ATR buffer** (to survive institutional wick-outs / stop hunts).
- **Trailing stop:** **swing-low trailing** based on `lastCompletedOvsLow` (long) / `lastCompletedOvbHigh` (short) — the existing Stoch-swing logic already in `pine/rsi_stoch_strategy.pine`. **Do not** use 1-bar previous-bar trailing as exit (that logic is for entry only).
- **Force exits:** RSI reset (into 70/30) + trend invalidation rules from [STRATEGY-LOGIC.md §Step 4](./STRATEGY-LOGIC.md), refined by §2.6 Fibo-Shift rule below.

### 2.3 Risk Sizing
- **Risk per trade:** **1% of equity** (default) — backtestable up to 2%.
- **Never** `strategy.percent_of_equity=100` — documented as guaranteed Risk-of-Ruin in NotebookLM research.
- Position size formula: `qty = (equity × 0.01) / (entry - SL)`.

### 2.4 Instrument Scope (Phase 1)
- **BTCUSDT** (crypto, high volatility, 24/7)
- **XAUUSD** (gold, trend-friendly)
- **EURUSD** (forex, lower vol baseline)
- Rationale: cover three regime types (crypto chop, commodity trend, forex range) to catch regime-dependent failures early.

### 2.5 Deferred (not in Phase 1)
- MTF execution (V-SHAPE recovery) — Phase 3.
- Volume filter on breakout / pullback — Phase 2.
- Walk-Forward Optimization harness — Phase 2.
- Candlestick reversal entries as triggers (currently in indicator) — evaluated but ranked below pullback+Stoch trigger.
- RSI divergence as entry gate — deferred pending data.
- Stoch shallow/deep predictor (mentor "จิ้มลึก vs จิ้มไม่ลึก", Clip 11) — Phase 2 filter.
- HTF oscillator soft-exit (mentor "ชน week overbought = take profit", Clip 11) — Phase 2 exit alternative.
- Sub-wave reversal as early HTF entry (mentor Clip 11) — Phase 3 (paired with MTF execution).
- MTF Fibo cluster as **priority target** (vs current "visual highlight") — Phase 3 enhancement.

### 2.6 Fibo-Shift Rule (mentor Clip 10) — Phase 1 lock-in
Refines the existing RSI-reset / "cut the mountain" logic.

When a trend is invalidated (price returns through `fiboHead`):
- **Path A — Fibo destroyed AND RSI reached OVB/OVS:** keep current behavior — reset all state ("cut the mountain"). Wait for new structure to form.
- **Path B — Fibo destroyed AND RSI did NOT reach OVB/OVS:** **DO NOT reset.** Instead, **shift the Fibo to encompass the broader swing** (one degree wider). The previous V-SHAPE attempt becomes the inner leg of a larger PERFECT structure. Trade resumes against the wider Fibo.

**Why this matters:** the current pine code resets on every invalidation, which throws away setups that mentor classifies as "still developing" (broader perfect forming around a failed inner V-shape). Adding Path B should reduce the false-reset rate and recover trades currently being missed.

### 2.7 Sub-wave Reversal — Alt HTF Confirmation (mentor Clip 11) — Phase 3
HTF (e.g., 1H) trend reversal can be confirmed two ways:
- (a) HTF candlestick reversal pattern on close, OR
- (b) **LTF (e.g., 15m) sub-wave fully forms a reversal structure** ("สับเวฟฟอร์มตัว").

Path (b) gives an earlier entry without waiting for HTF candle close. Defer to Phase 3 since it requires MTF state plumbing already planned for V-SHAPE recovery.

### 2.8 Smart Partial Exit — Stoch Extreme + Reversal (Phase 1 lock-in, Step 3c)
Take 50% off the table when an early reversal forms before TP1 — prevents trailing SL from giving back unrealized gains.

**Trigger (Long):**
- Position open & in profit (`close > entry`)
- Stoch %K ≥ OVB level (≥ 80)
- Bearish reversal candle on current bar: `close < low[1]`
- Not already partial-closed during this Stoch OVB cycle

**Trigger (Short, symmetric):**
- Position open & in profit
- Stoch %K ≤ OVS level (≤ 20)
- Bullish reversal: `close > high[1]`
- Not already partial-closed during this Stoch OVS cycle

**Action:** `strategy.close(currentEntryId, qty_percent=50, comment="Stoch <OVB|OVS> partial")`

**Why:** trailing SL (Stoch-swing-based) only ratchets when a NEW Stoch cycle completes. In the gap, a reversal can roll the trade from green → small profit / breakeven before trailing fires. Locking 50% at the reversal:
1. Captures real profit even if TP1 never reached
2. Leaves 50% runner for Fibo targets if reversal is a fakeout
3. Mirrors mentor Clip 11's "HTF oscillator soft-exit" — but on the trade's own TF (no MTF wiring)

**Edge cases:**
- Re-trigger guard: `partialClosedThisOvbCycle` (long) / `partialClosedThisOvsCycle` (short). Reset on `stochExitedOvb` / `stochExitedOvs`.
- Order vs TP1: if TP1 hits first then partial fires → closes 50% of remainder = 25% of original. Acceptable additional lock.
- Toggle: `useSmartPartialExit` input (default ON).

---

## 3. POC Success Criteria

Phase 1 must pass **ALL** of the following on 2-year backtest data before moving to Phase 2:

| Metric | Threshold | Why |
|---|---|---|
| **Profit Factor (after 0.1% commission)** | > 1.3 | Minimum edge after realistic fees |
| **Max Drawdown** | < 25% | Survivable for retail account sizing |
| **Win Rate** | 30-50% | Expected range for Fibo-extension breakout systems |
| **Min Trade Count** | ≥ 50 / symbol / year | Statistical significance baseline |
| **OOS Sharpe (walk-forward)** | > 0.5 | Proves params aren't curve-fit |
| **Consecutive Loss Max** | < 10 | Psychological/sizing constraint for live trading |

If any threshold fails → iterate on design within Phase 1 before adding complexity.

---

## 4. Phased Roadmap

### 🚀 Phase 1 — HTF-Only MVP *(current phase)*
Establish baseline backtest with Phase-1 lock-in rules above.
- [ ] Rewrite pine entry logic: pullback + Stoch K-cross-D + `close > high[1]`.
- [ ] Add ATR buffer to SL.
- [ ] Set risk sizing to 1% per trade.
- [ ] Verify strategy script and indicator script signal the **same** entries (no divergence).
- [ ] Run 2-year backtest on BTCUSDT 1H, XAUUSD 1H, EURUSD 1H.
- [ ] Collect metrics per §3 success criteria.

### 🚀 Phase 2 — Robustness Validation
Only if Phase 1 passes success criteria.
- [ ] Implement volatility regime filter (BB-in-Keltner / Bollinger Squeeze) to block chop.
- [ ] Set up Walk-Forward Optimization harness (rolling 12-month in-sample, 3-month out-of-sample).
- [ ] Re-run backtest with volume filter on breakout (explosive vol) and pullback (declining vol).
- [ ] Validate OOS Sharpe > 0.5 on WFO windows.
- [ ] **Stoch depth predictor (mentor):** track previous Stoch cycle peak/trough depth; if last cycle was shallow (e.g., barely touched 80 or 20), tag the current setup as "drag-out expected" and adjust position management (e.g., looser trailing, hold runner longer).
- [ ] **HTF oscillator soft-exit (mentor):** add optional rule — when in profit and HTF (Weekly or 4H) Stoch hits OVB/OVS, take 50% off regardless of Fibo target. Test as both replacement for and supplement to Fibo TPs.

### 🚀 Phase 3 — MTF V-SHAPE Recovery *(Trigger-conditional)*
Only if Phase 2 log shows meaningful V-SHAPE profits being missed (logged as "skipped trades" in Phase 1).
- [ ] Add HTF state export (1H) → LTF execution (5m or 15m).
- [ ] Implement pullback+Stoch trigger on LTF while HTF trend is active.
- [ ] Test **without** distance filter first (Config B — see §5).
- [ ] **Sub-wave reversal alt entry (§2.7):** allow LTF reversal structure to confirm HTF reversal earlier than HTF candle close.
- [ ] **Fibo cluster as priority target:** when current-TF Fibo level overlaps HTF Fibo within threshold (existing MTF cluster logic from `rsi_stoch_state.pine`), bias the strategy toward that level — e.g., move TP1 to the cluster level if it sits between current TP1 and TP2.

### 🚀 Phase 4 — MTF + Distance Filter *(Conditional on Phase 3)*
Only if Phase 3 shows V-SHAPE recovery works but late-extended entries hurt PF.
- [ ] Add "HTF price must be below Fibo 1.0-1.3" gate for long entries (block catching top).
- [ ] Re-run Config C backtest, compare to A and B.

### 🚀 Phase 5 — Live Forward Test
- [ ] Deploy winning config to Binance Testnet via existing Lambda webhook flow.
- [ ] 30 days forward test with small real-money position (0.5% risk, not 1%).
- [ ] Only move to production after forward-test matches backtest metrics ±20%.

---

## 5. Backtest Plan — 3-Way Comparison

All three configs share §2 design lock-in except for entry TF and distance filter.

### 5.1 Configurations

| ID | Context TF | Entry TF | Distance Filter | Expected Character |
|----|-----------|----------|-----------------|--------------------|
| **A — HTF-only** | 1H | 1H | n/a | Conservative, misses V-SHAPE, lower frequency, higher per-trade R:R |
| **B — MTF no-filter** | 1H | 5m | none | Recovers V-SHAPE, risk of catching top on extended HTF moves |
| **C — MTF + distance** | 1H | 5m | HTF price must be between Fibo 0.0–1.3 at entry | Balance: V-SHAPE recovery without late-entry risk |

### 5.2 Dataset
- **Symbols:** BTCUSDT, XAUUSD, EURUSD.
- **Period:** 2 years (2024-04 → 2026-04).
- **Split:** 60% train (2024-04 → 2025-07) / 40% test (2025-08 → 2026-04).
- **Commission:** 0.1% per side (Binance spot-like).
- **Slippage:** 0.05% on market entries to simulate realistic fills.

### 5.3 Metrics to Report (per config × symbol)

| Category | Metrics |
|---|---|
| **PnL** | Total Return, Profit Factor, Expectancy per trade |
| **Risk** | Max Drawdown %, Calmar Ratio, Max consecutive losses |
| **Quality** | Sharpe, Sortino, Avg R:R realized |
| **Frequency** | Trades/month, Avg hold time |
| **Breakdown** | Win rate for PERFECT vs V-SHAPE separately; TP1 hit-rate by trend type (expect V-SHAPE TP1 hit-rate > PERFECT TP1 hit-rate due to closer target) |
| **Cost sensitivity** | PnL at 0%, 0.1%, 0.2%, 0.4% commission |

### 5.4 Win Condition Decision Matrix

```
           PF(test) ≥ 1.3?   MaxDD ≤ 25%?   OOS Sharpe ≥ 0.5?   → Verdict
Config A         ✅                ✅              ✅            Adopt A, stop here
Config A         ✅                ✅              ❌            Iterate WFO, re-test
Config A         ❌                any             any           Redesign Phase 1
Config A passes, B better by ≥ 15% PF and MaxDD delta ≤ +5%     → Adopt B
B passes but late-entry failures > 30% of V-SHAPE trades         → Try C
C > B by ≥ 10% PF with equal MaxDD                               → Adopt C
```

### 5.5 Pitfalls to Watch

- **Survivorship bias:** backtesting only liquid pairs skews results — include one lower-liquidity pair if possible.
- **Look-ahead bias:** HTF `request.security` must use `barmerge.lookahead_off` (already set in `rsi_stoch_state.pine`).
- **Overfitting to regime:** 2024-2026 has specific macro regime. If Config A wins only in one half, flag it.
- **Sample size for V-SHAPE:** V-SHAPE breakouts are rare — Phase-1 skip-log may have too few examples to statistically justify Phase 3.

---

## 6. Feature Evolution & Trigger Conditions

Mirror of the RAG POC's lean/agile rule: **don't build a feature until data says you need it.**

| Feature | Rationale (why add it) | Trigger Condition | Skip if... |
|---|---|---|---|
| **Volatility regime filter** (BB/Keltner) | Breakout systems bleed in chop | Phase 1 shows > 40% losing trades occur when ATR < 30-day avg | chop doesn't dominate PnL distribution |
| **Volume confirm on breakout** | Filter fake breakouts | Phase 1 shows ≥ 25% of losers came from low-volume breakouts | volume doesn't correlate with win-rate |
| **Walk-Forward Optimization** | Prove params not curve-fit | Phase 1 passes but reviewers can't rule out overfitting | Phase 1 OOS already stable (unlikely) |
| **MTF V-SHAPE recovery** (Config B) | Phase 1 skip-log shows V-SHAPE trades worth catching | Missed V-SHAPE PnL projected > 20% of realized PnL over test window | Missed PnL < 10% or adds DD risk > 5% |
| **MTF + distance filter** (Config C) | Config B over-entries on extended HTF moves | Config B max-DD rises > 5% vs A AND V-SHAPE late-entry failures > 30% | Config B already meets all thresholds |
| **RSI divergence entry** | Add secondary setup for range markets | Phase 1 equity curve has long flat patches corresponding to range periods | no sustained range periods in test data |
| **Stoch depth predictor** | Mentor rule: shallow previous cycle predicts deep next cycle + drag-out trend | Phase 2 data shows correlation between previous-cycle depth and next-cycle trade duration/PnL | no measurable correlation |
| **HTF oscillator soft-exit** | Mentor rule: HTF OVB/OVS = take profit | Phase 2 backtest shows fixed Fibo TPs leave > 30% of profits on the table when HTF reversal could have been used | Fibo TPs already capture > 80% of theoretical maximum exit |
| **Sub-wave reversal alt entry** | Earlier HTF confirmation via LTF structure | Phase 3 MTF infrastructure is in place AND Phase 1 shows late-entry bias | MTF not adopted |
| **Fibo cluster priority target** | MTF cluster zones are higher-conviction TPs | Phase 3 confirms MTF entry; cluster zones overlap with trade-paths > 30% of trades | clusters rarely align with target zones |
| **Live forward (Phase 5)** | Only do this after robust backtest | Phase 2 WFO stable | backtest unstable |

---

## 7. Gap Analysis & Known Issues (from current code)

Derived from `pine/rsi_stoch_strategy.pine` + `pine/rsi_stoch_state.pine` audit — these must be fixed before Phase 1 backtest is meaningful.

| # | Issue | Impact | Fix in |
|---|---|---|---|
| 1 | `default_qty_value=100` with `percent_of_equity` = 100% equity per trade | Guaranteed Risk of Ruin | Phase 1 Step 1 |
| 2 | Entry is at breakout bar close, not pullback break | Slippage + low R:R | Phase 1 Step 1 |
| 3 | SL at exact entry-bar extreme | Stop-hunt magnet | Phase 1 Step 2 |
| 4 | Strategy trailing = Stoch swing; Indicator trailing = ATR — divergent visual signals | Credibility gap between chart visuals and backtest results | Phase 1 Step 4 |
| 5 | No volatility / regime filter | Bleed in chop | Phase 2 |
| 6 | Hardcoded params (RSI 14, Stoch 9/3/3, 80/20, 70/30, 0.3 reverse threshold) | Overfitting risk | Phase 2 WFO |
| 7 | MTF cluster only detects **target** confluence, not entry context | Incomplete MTF utility | Phase 3 |

---

## 8. Open Questions

To revisit before Phase 1 backtest launch:
1. Which Stoch K/D cross is canonical — K crosses D from below while K was <20, or K exits the <20 zone? (Ref: NotebookLM recommends K-cross-D with K previously <20.)
2. Should `close > high[1]` require `high[1]` to be the pullback low-point, or any recent bar? (Tighter interpretation reduces false entries.)
3. **ATR buffer style — research vs mentor.** Mentor (Clip 10) doesn't use ATR — uses pure structural SL at the bar low/high. Research source ("Beyond Basic Support and Resistance") recommends 0.5–1.5× ATR buffer to survive institutional wick-outs. Phase 1 lock-in: keep `atrBufferMult=0.5` as default, A/B test against `0` in Phase 1 Step 5 smoke tests; full sweep (0/0.3/0.5/1.0/1.5) in Phase 2 WFO.
4. Force-close on RSI reset — keep as hard rule, or soft (close 50%)? Hard is safer, soft captures more runner upside.

---

## 9. Deferred Knowledge / Out of Scope

Knowledge captured from research but **explicitly excluded** from the current plan. Documented here so it isn't re-discovered later as if new.

### 9.1 2-Fibo Averaging-Down (mentor Clip 11) — SKIPPED
**What it is:** if a position is in drawdown but the macro trend thesis is intact, hold and add a second position only after price moves "2 Fibo channels" against the original entry.

**Why skipped:**
1. Conflicts with the 1% risk-per-trade rule. Adding a second position effectively doubles risk on the worst trades.
2. Risk-of-Ruin grows non-linearly. With ~40% win rate, averaging on a losing leg compounds losses across consecutive failures.
3. Discretionary technique — mentor uses fundamental/macro context that the algorithmic system lacks.
4. Backtest-deceptive: averaging down looks profitable in mean-reverting samples but blows up in trending-against-you regimes.
5. Adds **leverage**, not **edge** — the system doesn't predict the market better, it just bets bigger on losers.

**Safer alternative if scaling is desired:** **Pyramiding on confirmation** (add to winners, not losers — Turtle-style). Defer to Phase 4+ if Phase 1-3 baseline proves stable.

---

## 10. Research Reference

- **NotebookLM notebook:** `9db4787d-26a7-4cfd-ad2a-a308efb222ab` (66 sources — GitHub repo, pine scripts, 63 deep-research sources on dual oscillators, Fibo extensions, breakout systems, walk-forward validation, trailing stops, pullback entries).
- **Archetype anchor source:** *"Algorithmic Trading Architectures: A Comprehensive Analysis of Dual-Oscillator Filters, Fibonacci Projections, and Breakout Structural Integrity"* — the deep-research synthesis doc generated for this notebook.
- **Evidence for Golden Fibo target weight:** LuxAlgo *A Basic Guide to Fibonacci Extensions*.
- **Stoch confirmation rule (K-cross-D with K in OVS):** *RSI Vs. Stochastic: Which Is The Best Oscillator For Trading Big Tech Stocks*.
- **Walk-forward + 0.1% commission survivability:** arXiv 2602.10785 — *Novel approach to trading strategy parameter optimization using double out-of-sample data and walk-forward techniques*.
