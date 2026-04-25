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

### Step 1 — Fix sizing & risk
- [ ] Remove `default_qty_type=strategy.percent_of_equity, default_qty_value=100`.
- [ ] Add `riskPercent` input (default 1%, range 0.25–2%).
- [ ] Compute `qty = (equity × riskPercent) / |entry − SL|` at entry.
- [ ] Verify Strategy Report shows realistic drawdown (not 100% ruin).

### Step 2 — Entry logic rewrite (HTF pullback + Stoch trigger)
- [ ] After breakout, set `trendActive` but do NOT fire entry.
- [ ] Track `stochReachedExtreme` flag — set true once Stoch %K touches <20 (long path) or >80 (short path) during pullback.
- [ ] Track `stochTurning` — %K crosses %D in the trend direction.
- [ ] Fire long when: `trendActive & direction==1 & stochReachedExtreme & stochTurning & close > high[1]`.
- [ ] Symmetric short condition.
- [ ] Reset `stochReachedExtreme` and `stochTurning` on RSI reset or trend invalidation.

### Step 3 — SL with ATR buffer
- [ ] Compute `atr14` once per bar.
- [ ] Long SL = `low[0] − 0.5 × atr14` at entry bar.
- [ ] Short SL = `high[0] + 0.5 × atr14`.
- [ ] Expose ATR buffer multiplier as input for Phase 2 sweep.

### Step 3b — Adaptive TP by trend type
- [ ] Add inputs: `tpPerfect1=1.618`, `tpPerfect2=2.618`, `tpVShape1=1.5`, `tpVShape2=2.0`.
- [ ] At entry, capture `tpLevel1 = trendType=="PERFECT" ? tpPerfect1 : tpVShape1`.
- [ ] Compute `tp1Val` / `tp2Val` from captured levels (freeze at entry — don't recompute if trendType flips mid-trade).
- [ ] Verify Strategy Report hit-rate breakdown: PERFECT-TP1 vs V-SHAPE-TP1 (V-SHAPE TP1 should have noticeably higher hit rate since target is closer).

### Step 4 — Sync indicator with strategy
- [ ] Remove ATR-trailing-as-entry logic from `rsi_stoch_state.pine` (was used as entry in indicator, strategy ignored it).
- [ ] Indicator should plot the **same** BUY/SELL labels that the strategy would fire — no divergence.
- [ ] Keep existing Stoch-swing trailing stop (`lastCompletedOvsLow` / `lastCompletedOvbHigh`) in strategy — that stays as exit.

### Step 5 — Instrument smoke tests
- [ ] Run on BTCUSDT 1H, 6-month sample — sanity check entries form after pullbacks, SL doesn't sit on extreme wicks.
- [ ] Run on XAUUSD 1H, 6-month sample.
- [ ] Run on EURUSD 1H, 6-month sample.
- [ ] Fix any Pine runtime errors / NaN issues.
- [ ] **A/B test: ATR Buffer style vs Mentor-pure style** (per [§7 Open Question 3](./docs/planning.md#8-open-questions)):
  - [ ] Run each symbol with `atrBufferMult = 0.5` (research-style, default) — record PF, WinRate, MaxDD.
  - [ ] Re-run with `atrBufferMult = 0` (mentor-pure, structural SL only) — record same.
  - [ ] Note per-symbol winner (may differ — crypto wicks more, forex less).
  - [ ] Decision: lock per-symbol default, OR keep 0.5 as universal default and revisit in Phase 2 WFO.

### Step 6 — Fibo-Shift logic on invalidation (mentor Clip 10)
Implements [docs/planning.md §2.6](./docs/planning.md#26-fibo-shift-rule-mentor-clip-10--phase-1-lock-in).
- [ ] On `trendInvalidated`, check `rsiInOvb` / `rsiInOvs` history since trend started.
- [ ] **Path A** — RSI did reach extreme → existing reset behavior (cut the mountain).
- [ ] **Path B** — RSI did NOT reach extreme → keep trend active but shift `fiboHead` / `fiboEnd` to encompass the broader prior swing (one degree wider Stoch cycle). Reclassify trend as PERFECT.
- [ ] Add `trendShiftCount` to flag trades that came from Path B for backtest analysis.
- [ ] Verify Path B trades have win rate ≥ Path A reset+re-entry trades (else the shift logic is no help).

### Step 7 — Full Phase-1 backtest (Config A)
- [ ] 2 years × 3 symbols × 1H.
- [ ] Export Strategy Report for each.
- [ ] Log V-SHAPE breakouts that were **skipped** (no pullback formed) — for Phase-3 trigger evaluation.
- [ ] Log Fibo-Shift trades separately — count + PnL contribution.
- [ ] Record metrics from [docs/planning.md §5.3](./docs/planning.md#53-metrics-to-report-per-config--symbol).
- [ ] Decision: pass / iterate / redesign per [§5.4 matrix](./docs/planning.md#54-win-condition-decision-matrix).

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
| **Entry philosophy** | Pullback + Stoch K-cross-D (with prior <20/>80 touch) + `close > high[1]`. Archetype: Raschke Holy Grail + 2-bar reversal hybrid. |
| **Adaptive TP** | PERFECT → TP1=1.618 / TP2=2.618. V-SHAPE → TP1=1.5 / TP2=2.0. Rationale: V-SHAPE follow-through <40%, so 1.618 acts as rejection wall — TP at 1.5 books gains *before* the rejection zone. Expectancy math: 0.55×1.5R − 0.45×1R = +0.375R vs 0.40×1.618R − 0.60×1R = +0.047R. |
| **Fibo Shift on invalidation** | mentor Clip 10 — if Fibo destroyed but RSI didn't reach OVB/OVS, shift Fibo wider instead of resetting (V-SHAPE → broader PERFECT). Phase 1 lock-in. |
| **2-Fibo averaging-down** | **SKIPPED** — conflicts with risk-per-trade rule. Knowledge captured in [docs/planning.md §9.1](./docs/planning.md#91-2-fibo-averaging-down-mentor-clip-11--skipped). Pyramiding-on-confirmation considered as Phase 4+ alternative if needed. |
| **V-SHAPE handling** | Accepted miss in Phase 1 (evidence: <40% follow-through). Recover only if skip-log justifies Phase 3. |
| **Commission** | 0.1% per side (Binance-like). Backtest must remain profitable at 0.2% stress test. |
| **Data window** | 2024-04 → 2026-04 (2 years), 60/40 train/test split. |
| **Decision cadence** | Revisit plan after each phase completes — no auto-promotion to next phase. |

---

## Research Reference

See [docs/planning.md §9](./docs/planning.md#9-research-reference) for full list. NotebookLM notebook: `9db4787d-26a7-4cfd-ad2a-a308efb222ab`.
