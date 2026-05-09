# cBot Port Session Context — Pine v2.7 → C# cBot Headless Backtest Loop

**Session goal:** Port `pine/rsi_stoch_strategy_v2.pine` (v2.7) → C# cBot, run headless backtest loop, iterate until pass criteria. Bypass mode (`--dangerously-skip-permissions`) — autonomous.

**Date started:** 2026-05-09

---

## 🎯 Stop Condition (when to report user)

Loop stops + reports ONLY when **all** Phase-1 success criteria pass on 2-yr backtest:

| Metric | Threshold |
|---|---|
| Profit Factor (after 0.1% commission) | > 1.3 |
| Max Drawdown | < 25% |
| Win Rate | 30-50% |
| Trade Count | ≥ 50 / symbol / year |
| OOS Sharpe | > 0.5 |
| Max Consecutive Losses | < 10 |

Source: `docs/planning.md §3`

**Also stop & ask user when:** build/backtest fails 3× same root cause, or strategy needs major direction change.

---

## 📁 File Locations

| What | Where |
|---|---|
| Pine reference (v2.7) | `pine/rsi_stoch_strategy_v2.pine` |
| Pine progress log | `execute.md` |
| Strategy logic doc | `docs/STRATEGY-LOGIC.md` |
| Strategy planning + criteria | `docs/planning.md` |
| **This session context** | `docs/CBOT-PORT-SESSION.md` ← you're here |
| **cBot source** | `~/cTrader-bots/src/RsiStochFiboBot/RsiStochFiboBot/RsiStochFiboBot.cs` |
| **cBot csproj** | `~/cTrader-bots/src/RsiStochFiboBot/RsiStochFiboBot/RsiStochFiboBot.csproj` |
| **Built .algo** | `~/cTrader-bots/algos/RsiStochFiboBot.algo` |
| **Backtest reports** | `~/cTrader-bots/reports/run-NNN.json` |
| **Credentials (password)** | `~/cTrader-bots/credentials/ctid.pwd` (chmod 600) |
| **Iteration log** | `docs/CBOT-ITERATION-LOG.md` (created after first run) |
| dotnet CLI | `/usr/local/share/dotnet/dotnet` (NOT on PATH) |
| ctrader-headless skill | `~/.claude/skills/ctrader-headless-backtest/SKILL.md` |
| ctrader-mac-cAlgo skill | `~/.claude/skills/ctrader-mac-cAlgo/SKILL.md` |
| ctrader-verify-algo skill | `~/.claude/skills/ctrader-verify-algo/SKILL.md` |
| NLM research notebook | `https://notebooklm.google.com/notebook/9db4787d-26a7-4cfd-ad2a-a308efb222ab` |

---

## 🔐 Credentials (NOT stored in this file)

- **cTID:** `frametrigger@gmail.com`
- **Account ID:** `5815976` (cTrader DEMO)
- **Password file:** `~/cTrader-bots/credentials/ctid.pwd` ← user fills this in (currently empty, blocker)

User confirmed: demo account OK, can also create new demo accounts for parallel/optimization runs if needed.

---

## 🚧 Current Blockers (as of session start)

| # | Blocker | Owner | Status |
|---|---------|-------|--------|
| 1 | Docker Desktop daemon not running | user | ⏳ pending |
| 2 | `~/cTrader-bots/credentials/ctid.pwd` is 0 bytes | user | ⏳ pending |

**To resume:** when Docker running + password file has content → unblock M5 (first backtest).

---

## 📐 Strategy Logic Summary (from pine v2.7)

### State engine
- Stoch %K cycles in/out OVB (≥80) and OVS (≤20)
- During OVB: track `priceCycleHigh` (max high while Stoch ≥80)
- During OVS: track `priceCycleLow` (min low while Stoch ≤20)
- On Stoch exit OVB → `lastCompletedOvbHigh = priceCycleHigh`
- On Stoch exit OVS → `lastCompletedOvsLow = priceCycleLow`

### Trend activation (breakout)
- Long trend: `close > lastCompletedOvbHigh` (and lastCompletedOvsLow exists)
  - PERFECT if breakout had a pullback (Stoch entered OVS during prior trend forming)
  - V-SHAPE if no pullback (clean breakout)
- Short trend: symmetric
- Fibo head = breakout level, Fibo tail = opposite extreme; Fibo TPs measured from head

### Entry (long, short symmetric)
1. trendActive = true (after breakout)
2. Pullback occurred → swingLow tracked = lowest bar after breakout / since last entry
3. preSwingLowHigh = high of bar before swingLow
4. **Trigger:** `close > high[1]` (previous candle break)
5. setupConsumed == false (pullback not yet fired)
6. RR (signed) ≥ 3.0:
   - entry = close
   - SL = swingLow − ATR(14) × 0.5
   - TP1 = fiboHead + (fiboHead − fiboTail) × tpMultiplier
   - reward = TP1 − entry (must be > 0; uses signed math, NOT abs)
   - risk = entry − SL
   - RR = reward / risk
7. Fire MARKET BUY → setupConsumed := true → re-arm only on NEW swingLow

### Sizing (1% risk)
- `qty = (equity × 0.01) / |entry − SL|`
- Round to symbol's volume step (cTrader: NormalizeVolumeInUnits)

### TP placement (adaptive by trendType)
- PERFECT: TP1 = Fibo 1.618, TP2 = Fibo 2.618
- V-SHAPE: TP1 = Fibo 1.5, TP2 = Fibo 2.0
- Trade tag includes `-P` or `-V` for stat breakdown
- Split position: 50% closes at TP1, 50% closes at TP2 (or via separate orders, see implementation note)

### Exits
- TP1 hit → close 50%, move SL to breakeven (TBD: pine doesn't do this, but mentor pattern)
- TP2 hit → close rest
- SL hit → full close
- Trailing stop: when new `lastCompletedOvsLow` forms (long), trail SL up (Stoch-swing trailing)
- Smart partial exit (50%) when:
  - Long: in-profit + Stoch ≥ 80 + close < low[1] (bearish reversal candle)
  - Short: in-profit + Stoch ≤ 20 + close > high[1] (bullish reversal candle)
- Re-trigger guard: per-cycle flag, reset on stochExitedOvb / stochExitedOvs

### Mentor patterns (deferred / Phase 2+)
- Sub-Wave 3 entry confirmation
- FVG (Fair Value Gap) + Fibo cluster as priority TP
- 3-pullback rule
- LTF anchor logic (MTF V-SHAPE recovery)
- Fibo Shift on invalidation (Path B — broader perfect)

---

## 🔁 Loop Architecture

```
┌──────────────────────────────────────────────────────────┐
│ ITER N                                                    │
├──────────────────────────────────────────────────────────┤
│ 1. Read this file + iteration_log.md                     │
│ 2. Edit ~/cTrader-bots/src/RsiStochFiboBot/.../*.cs       │
│ 3. dotnet build -c Release → .algo                        │
│ 4. cp .algo → ~/cTrader-bots/algos/                       │
│ 5. docker run ctrader-console metadata <algo>             │
│    (verify FullAccess, parameters)                        │
│ 6. docker run ctrader-console backtest \                  │
│    --ctid --pwd-file --account --symbol --period          │
│    --start --end --data-mode m1 --commission 15           │
│    --report-json reports/run-N.json                       │
│ 7. Parse JSON → extract Net P&L, PF, DD%, WR, Sharpe      │
│ 8. Append result to docs/CBOT-ITERATION-LOG.md            │
│ 9. Decide:                                                 │
│    - PASS criteria → STOP, report user                    │
│    - FAIL → diagnose root cause → propose ONE change      │
│      → goto step 2                                         │
│ 10. Hard limit: 15 iter same date range (overfitting)     │
└──────────────────────────────────────────────────────────┘
```

### Symbols/TFs to test (in priority order)
1. **XAUUSD H1** (primary — pine baseline tested here)
2. XAUUSD 4H, 1D
3. EURUSD H1
4. BTCUSDT H1, 4H

### Backtest defaults
- Period: `01/01/2024` → `30/04/2026` (DD/MM/YYYY UTC, ~2.3 years)
- Data-mode: `m1` (M1 OHLC, fast iteration)
- Balance: 10000 (or 100000 to match pine)
- Commission: 15 (0.15% per side ~ pine's 0.1%)
- Spread: 1 (default)

### Parser fields (defensive — first-run reveals exact schema)
- `NetProfit`, `GrossProfit`, `GrossLoss`
- `MaxBalanceDrawdown`, `MaxEquityDrawdownPercent`
- `ProfitFactor`, `SharpeRatio`, `SortinoRatio`
- `TotalTrades`, `WinningTrades`, `LosingTrades`, `WinRate`

---

## 📊 Iteration Log (append per run)

See `docs/CBOT-ITERATION-LOG.md` (created after first run).

Format per entry:
```markdown
### Iter N — {date} {symbol} {tf}
- Hypothesis: {what we changed and why}
- Code diff: {key change in cBot}
- Result: PnL=X% PF=Y DD=Z% Trades=N WR=W% Sharpe=S
- Diagnosis: {what worked / what didn't}
- Next: {one change to try}
```

---

## ✅ Milestone Status

| ID | Milestone | Status |
|---|---|---|
| M0 | Pre-flight: Docker daemon + password file | ⏳ Blocked (user — both still not done) |
| M1 | Skeleton cBot — csproj + .cs + dotnet build OK | ✅ DONE (build 0 errors 0 warn, .algo 12,563 bytes) |
| M2 | State engine (Stoch cycles, trend, swings) | ✅ DONE in v0.1 cBot (awaiting behavioral verify) |
| M3 | Entry/SL/Sizing | ✅ DONE in v0.1 cBot (awaiting behavioral verify) |
| M4 | Adaptive TP + Smart Partial Exit | ✅ DONE in v0.1 cBot (awaiting behavioral verify) |
| M5 | First backtest + iterate to pass criteria | ⏳ Blocked (waiting on Docker + password) |

**cBot v0.1 is complete.** Single-shot port covering all of M1-M4. Once Docker + password fixed → straight to M5 backtest loop. Logic notes for v0.1:
- Pullback detection uses `_hadPullback` flag (Stoch entered OVS during pre-trend) — close-enough to pine, may refine
- swingLow only tracked while `_stochInOvs` — pine tracks pullback minimum more loosely; refine if iter 1 shows missed setups
- Invalidation = `close < fiboTail` (lenient) — pine uses fiboHead+RSI combo; refine if too few/many resets
- Split position (50% TP1 + 50% TP2 with shared SL) — chosen for intra-bar fill accuracy

---

## 📝 Open Questions / Decisions Made

| Q | Decision |
|---|---|
| Single position vs split position for TP1/TP2? | **Split position** — 50% size each, separate TPs, shared SL. More accurate intra-bar fills than manual close. |
| TP1 hit → move SL to breakeven? | **Not in pine v2.7** — leave out for first run. Reconsider as Phase 2 enhancement. |
| Initial capital? | **10000** for backtest (matches skill default), but pine uses 100000. Initial run: 10000. |
| Commission? | **15 = 0.15%** per side (slightly more conservative than pine's 0.1%). |
| Risk %? | **1.0** default, allow optimization 0.5-2.0. |
| Bar processing timing? | **OnBar** (close-of-bar) — matches `process_orders_on_close=true` in pine. |
| Detect intra-bar TP1/TP2 hit? | Engine handles via Position.TakeProfit auto-fill — accurate at backtest engine level. |

---

## 🔄 Resume Instructions (if session dies)

1. Read this file
2. Read `docs/CBOT-ITERATION-LOG.md` (latest iter status)
3. Check task list (TaskList tool)
4. Verify file paths in §"File Locations" still exist
5. Verify blockers (§"Current Blockers") still resolved
6. Continue from current milestone in §"Milestone Status"
7. If stuck — re-read pine v2.7 + planning.md §2-3

User's flexibility note (2026-05-09): "strategy ผมว่ายังเข้มงวดเกินไป — เรียนรู้ระหว่างผล backtest ได้" → don't rigidly port; adapt during loop.

---

## 🚀 Commands Cheat Sheet

```bash
# 1. Build cBot
export PATH="/usr/local/share/dotnet:$PATH"
dotnet build -c Release \
  ~/cTrader-bots/src/RsiStochFiboBot/RsiStochFiboBot/RsiStochFiboBot.csproj
cp ~/cTrader-bots/src/RsiStochFiboBot/RsiStochFiboBot/bin/Release/net6.0/RsiStochFiboBot.algo \
   ~/cTrader-bots/algos/

# 2. Verify .algo metadata
docker run --rm \
  --mount type=bind,src=$HOME/cTrader-bots,dst=/mnt/Robots \
  ghcr.io/spotware/ctrader-console:latest metadata \
  /mnt/Robots/algos/RsiStochFiboBot.algo

# 3. Verify auth
docker run --rm \
  --mount type=bind,src=$HOME/cTrader-bots,dst=/mnt/Robots \
  ghcr.io/spotware/ctrader-console:latest accounts \
  --ctid="frametrigger@gmail.com" \
  --pwd-file=/mnt/Robots/credentials/ctid.pwd

# 4. Backtest
docker run --rm \
  --mount type=bind,src=$HOME/cTrader-bots,dst=/mnt/Robots \
  ghcr.io/spotware/ctrader-console:latest backtest \
  /mnt/Robots/algos/RsiStochFiboBot.algo \
  --ctid="frametrigger@gmail.com" \
  --pwd-file=/mnt/Robots/credentials/ctid.pwd \
  --account=5815976 \
  --symbol=XAUUSD \
  --period=H1 \
  --start="01/01/2024" \
  --end="30/04/2026" \
  --data-mode=m1 \
  --balance=10000 \
  --commission=15 \
  --spread=1 \
  --report-json=/mnt/Robots/reports/run-001.json
```
