using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class RsiStochFiboBot : Robot
    {
        // ─── INDICATOR PARAMS ──────────────────────────────────────
        [Parameter("RSI Length", DefaultValue = 14, MinValue = 2, Group = "Indicators")]
        public int RsiLength { get; set; }

        [Parameter("Stoch %K", DefaultValue = 14, MinValue = 1, Group = "Indicators")]
        public int StochK { get; set; }

        [Parameter("Stoch %D", DefaultValue = 3, MinValue = 1, Group = "Indicators")]
        public int StochD { get; set; }

        [Parameter("Stoch Smooth", DefaultValue = 3, MinValue = 1, Group = "Indicators")]
        public int StochSmooth { get; set; }

        [Parameter("OVB Level", DefaultValue = 80.0, Group = "Indicators")]
        public double OvbLevel { get; set; }

        [Parameter("OVS Level", DefaultValue = 20.0, Group = "Indicators")]
        public double OvsLevel { get; set; }

        [Parameter("ATR Length", DefaultValue = 14, MinValue = 1, Group = "Indicators")]
        public int AtrLength { get; set; }

        // ─── RISK / SL / TP PARAMS ─────────────────────────────────
        [Parameter("ATR Buffer Mult", DefaultValue = 0.5, MinValue = 0.0, Group = "Risk")]
        public double AtrBufferMult { get; set; }

        [Parameter("Risk %", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("RR Minimum", DefaultValue = 3.0, MinValue = 1.0, Group = "Risk")]
        public double RrMinimum { get; set; }

        [Parameter("TP Perfect 1", DefaultValue = 1.618, Group = "Targets")]
        public double TpPerfect1 { get; set; }

        [Parameter("TP Perfect 2", DefaultValue = 2.618, Group = "Targets")]
        public double TpPerfect2 { get; set; }

        [Parameter("TP V-Shape 1", DefaultValue = 1.5, Group = "Targets")]
        public double TpVShape1 { get; set; }

        [Parameter("TP V-Shape 2", DefaultValue = 2.0, Group = "Targets")]
        public double TpVShape2 { get; set; }

        [Parameter("Use Smart Partial Exit", DefaultValue = true, Group = "Targets")]
        public bool UseSmartPartialExit { get; set; }

        [Parameter("Label", DefaultValue = "RSF", Group = "Misc")]
        public string Label { get; set; }

        // ─── INDICATORS ────────────────────────────────────────────
        private RelativeStrengthIndex _rsi;
        private StochasticOscillator _stoch;
        private AverageTrueRange _atr;

        // ─── STOCH OVB/OVS CYCLE STATE ─────────────────────────────
        private bool _stochInOvb;
        private bool _stochInOvs;
        private double _priceCycleHigh = double.NaN;
        private double _priceCycleLow = double.NaN;
        private double _lastCompletedOvbHigh = double.NaN;
        private double _lastCompletedOvsLow = double.NaN;

        // ─── TREND STATE ───────────────────────────────────────────
        private bool _trendActive;
        private int _trendDirection;        // +1 long, -1 short
        private string _trendType = "";     // "PERFECT" or "V-SHAPE"
        private double _fiboHead = double.NaN;   // most recent completed OVB high (long) / OVS low (short)
        private double _fiboTail = double.NaN;   // initial breakout opposite extreme (kept for trend context)
        private bool _hadPullback;

        // ─── SWING / SETUP STATE ───────────────────────────────────
        private double _swingLow = double.NaN;
        private double _swingHigh = double.NaN;
        private double _preSwingLowHigh = double.NaN;
        private double _preSwingHighLow = double.NaN;
        private bool _setupConsumed;

        // ─── SMART PARTIAL EXIT STATE ──────────────────────────────
        private bool _partialClosedThisOvbCycle;
        private bool _partialClosedThisOvsCycle;

        // ─── HELPERS ───────────────────────────────────────────────
        private double Px(int idx) => Bars.ClosePrices.Last(idx);
        private double Hi(int idx) => Bars.HighPrices.Last(idx);
        private double Lo(int idx) => Bars.LowPrices.Last(idx);
        private double K(int idx) => _stoch.PercentK.Last(idx);
        private double Atr(int idx) => _atr.Result.Last(idx);

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════
        protected override void OnStart()
        {
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiLength);
            _stoch = Indicators.StochasticOscillator(StochK, StochD, StochSmooth, MovingAverageType.Simple);
            _atr = Indicators.AverageTrueRange(AtrLength, MovingAverageType.Exponential);

            Print($"RsiStochFiboBot started — Symbol={SymbolName} TF={TimeFrame} " +
                  $"Risk={RiskPercent}% RR>={RrMinimum} ATRbuf={AtrBufferMult}");
        }

        protected override void OnBar()
        {
            // Need enough bars for indicators to settle
            if (Bars.Count < Math.Max(StochK + StochD + StochSmooth, AtrLength) + 5)
                return;

            UpdateStochCycles();
            UpdateTrendAndSwings();

            if (UseSmartPartialExit)
                HandleSmartPartialExit();

            TryEnter();
        }

        // ═══════════════════════════════════════════════════════════
        // 1. STOCH OVB/OVS CYCLE TRACKING
        // ═══════════════════════════════════════════════════════════
        private void UpdateStochCycles()
        {
            double k = K(0);
            bool nowInOvb = k >= OvbLevel;
            bool nowInOvs = k <= OvsLevel;

            // ── OVB cycle ──
            if (nowInOvb)
            {
                if (!_stochInOvb)
                {
                    // Entering OVB
                    _priceCycleHigh = Hi(0);
                    _stochInOvb = true;
                }
                else
                {
                    // Inside OVB → track max high
                    _priceCycleHigh = Math.Max(_priceCycleHigh, Hi(0));
                }
            }
            else if (_stochInOvb)
            {
                // Exiting OVB → record completed cycle
                _lastCompletedOvbHigh = _priceCycleHigh;
                _stochInOvb = false;
                _partialClosedThisOvbCycle = false;
                // Dynamic Fibo: update head to latest OVB high during long trend
                if (_trendActive && _trendDirection > 0)
                    _fiboHead = _lastCompletedOvbHigh;
            }

            // ── OVS cycle ──
            if (nowInOvs)
            {
                if (!_stochInOvs)
                {
                    _priceCycleLow = Lo(0);
                    _stochInOvs = true;
                }
                else
                {
                    _priceCycleLow = Math.Min(_priceCycleLow, Lo(0));
                }
            }
            else if (_stochInOvs)
            {
                _lastCompletedOvsLow = _priceCycleLow;
                _stochInOvs = false;
                _partialClosedThisOvsCycle = false;
                // Dynamic Fibo: update head to latest OVS low during short trend
                if (_trendActive && _trendDirection < 0)
                    _fiboHead = _lastCompletedOvsLow;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 2. TREND BREAKOUT + SWING TRACKING
        // ═══════════════════════════════════════════════════════════
        private void UpdateTrendAndSwings()
        {
            double close = Px(0);

            // ── Trend activation: breakout of completed cycle extreme ──
            if (!_trendActive)
            {
                // Long breakout
                if (!double.IsNaN(_lastCompletedOvbHigh) && !double.IsNaN(_lastCompletedOvsLow)
                    && close > _lastCompletedOvbHigh)
                {
                    _trendActive = true;
                    _trendDirection = +1;
                    _fiboHead = _lastCompletedOvbHigh;
                    _fiboTail = _lastCompletedOvsLow;
                    _trendType = _hadPullback ? "PERFECT" : "V-SHAPE";
                    _swingLow = double.NaN;
                    _preSwingLowHigh = double.NaN;
                    _setupConsumed = false;
                    _hadPullback = false;
                    Print($"TREND LONG {_trendType} head={_fiboHead:F2} tail={_fiboTail:F2}");
                }
                // Short breakout
                else if (!double.IsNaN(_lastCompletedOvsLow) && !double.IsNaN(_lastCompletedOvbHigh)
                    && close < _lastCompletedOvsLow)
                {
                    _trendActive = true;
                    _trendDirection = -1;
                    _fiboHead = _lastCompletedOvsLow;
                    _fiboTail = _lastCompletedOvbHigh;
                    _trendType = _hadPullback ? "PERFECT" : "V-SHAPE";
                    _swingHigh = double.NaN;
                    _preSwingHighLow = double.NaN;
                    _setupConsumed = false;
                    _hadPullback = false;
                    Print($"TREND SHORT {_trendType} head={_fiboHead:F2} tail={_fiboTail:F2}");
                }
                else
                {
                    // Pre-trend pullback flag: if Stoch entered OVS during forming
                    if (_stochInOvs) _hadPullback = true;
                }
                return;
            }

            // ── Trend invalidation: price closes below swing low (structural breakdown) ──
            // Only fire if we have an active swing reference
            if (_trendDirection > 0 && !double.IsNaN(_swingLow) && close < _swingLow)
            {
                ResetTrend();
                return;
            }
            if (_trendDirection < 0 && !double.IsNaN(_swingHigh) && close > _swingHigh)
            {
                ResetTrend();
                return;
            }

            // ── Swing tracking during active trend ──
            if (_trendDirection > 0)
            {
                // Long: track lowest bar while Stoch is in OVS
                bool isNewSwingLow = double.IsNaN(_swingLow) || Lo(0) < _swingLow;
                if (_stochInOvs && isNewSwingLow)
                {
                    _preSwingLowHigh = Hi(1); // high of bar BEFORE new swing low
                    _swingLow = Lo(0);
                    _setupConsumed = false;   // re-arm for each new swing low
                }
            }
            else
            {
                bool isNewSwingHigh = double.IsNaN(_swingHigh) || Hi(0) > _swingHigh;
                if (_stochInOvb && isNewSwingHigh)
                {
                    _preSwingHighLow = Lo(1);
                    _swingHigh = Hi(0);
                    _setupConsumed = false;
                }
            }
        }

        private void ResetTrend()
        {
            Print($"TREND RESET (was {_trendType} dir={_trendDirection})");
            _trendActive = false;
            _trendDirection = 0;
            _trendType = "";
            _fiboHead = double.NaN;
            _fiboTail = double.NaN;
            _swingLow = double.NaN;
            _swingHigh = double.NaN;
            _preSwingLowHigh = double.NaN;
            _preSwingHighLow = double.NaN;
            _setupConsumed = false;
            _hadPullback = false;
        }

        // ═══════════════════════════════════════════════════════════
        // 3. ENTRY (reversal candle + signed RR ≥ 3)
        // ═══════════════════════════════════════════════════════════
        private void TryEnter()
        {
            if (!_trendActive) return;
            if (_setupConsumed) return;
            // Block new entry if any open position from this bot on this symbol
        bool hasOpenPos = false;
        foreach (var p in Positions)
            if (p.SymbolName == SymbolName && p.Label != null && p.Label.StartsWith(Label))
            { hasOpenPos = true; break; }
        if (hasOpenPos) return;

            double close = Px(0);
            double atr = Atr(0);

            if (_trendDirection > 0)
            {
                if (double.IsNaN(_swingLow)) return;
                if (double.IsNaN(_preSwingLowHigh)) return;
                // Reversal candle: close > high of bar BEFORE swing low (pine v2.6 rule)
                if (close <= _preSwingLowHigh) return;

                _setupConsumed = true;

                double sl = _swingLow - AtrBufferMult * atr;
                double tp1Mult = _trendType == "PERFECT" ? TpPerfect1 : TpVShape1;
                double tp2Mult = _trendType == "PERFECT" ? TpPerfect2 : TpVShape2;
                // Dynamic range: from latest OVB high down to current swing low
                double head = double.IsNaN(_fiboHead) ? _lastCompletedOvbHigh : _fiboHead;
                double range = head - _swingLow;
                double tp1 = head + range * (tp1Mult - 1.0);
                double tp2 = head + range * (tp2Mult - 1.0);

                double risk = close - sl;
                double reward = tp1 - close;
                if (risk <= 0 || reward <= 0) return;
                double rr = reward / risk;
                if (rr < RrMinimum) return;

                Print($"LONG ENTRY candidate @ {close:F2} SL={sl:F2} TP1={tp1:F2} head={head:F2} swLo={_swingLow:F2} range={range:F2} RR={rr:F2}");
                FireOrder(TradeType.Buy, close, sl, tp1, tp2, _trendType);
            }
            else if (_trendDirection < 0)
            {
                if (double.IsNaN(_swingHigh)) return;
                if (double.IsNaN(_preSwingHighLow)) return;
                if (close >= _preSwingHighLow) return;

                _setupConsumed = true;

                double sl = _swingHigh + AtrBufferMult * atr;
                double tp1Mult = _trendType == "PERFECT" ? TpPerfect1 : TpVShape1;
                double tp2Mult = _trendType == "PERFECT" ? TpPerfect2 : TpVShape2;
                double head = double.IsNaN(_fiboHead) ? _lastCompletedOvsLow : _fiboHead;
                double range = _swingHigh - head;
                double tp1 = head - range * (tp1Mult - 1.0);
                double tp2 = head - range * (tp2Mult - 1.0);

                double risk = sl - close;
                double reward = close - tp1;
                if (risk <= 0 || reward <= 0) return;
                double rr = reward / risk;
                if (rr < RrMinimum) return;

                Print($"SHORT ENTRY candidate @ {close:F2} SL={sl:F2} TP1={tp1:F2} head={head:F2} swHi={_swingHigh:F2} range={range:F2} RR={rr:F2}");
                FireOrder(TradeType.Sell, close, sl, tp1, tp2, _trendType);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 4. ORDER PLACEMENT (split position: 50% TP1 + 50% TP2)
        // ═══════════════════════════════════════════════════════════
        private void FireOrder(TradeType side, double entry, double sl, double tp1, double tp2, string trendType)
        {
            double riskCash = Account.Balance * (RiskPercent / 100.0);
            double slDist = Math.Abs(entry - sl);
            if (slDist <= 0) return;

            // Total raw units sized for 1× risk; will split into 2 positions
            // Profit/loss per unit per price-unit move = symbol's tick value
            // Volume in units: riskCash / (slDist / Symbol.PipSize) / Symbol.PipValue
            double unitsTotal = riskCash / slDist / (Symbol.PipValue / Symbol.PipSize);
            double unitsHalf = Symbol.NormalizeVolumeInUnits(unitsTotal / 2.0, RoundingMode.Down);

            if (unitsHalf < Symbol.VolumeInUnitsMin) {
                Print($"Volume too small: {unitsHalf} < min {Symbol.VolumeInUnitsMin}");
                return;
            }

            string suffix = trendType == "PERFECT" ? "-P" : "-V";
            string baseLabel = (side == TradeType.Buy ? "Long" : "Short") + suffix;

            // Position 1 — TP1
            var r1 = ExecuteMarketOrder(side, SymbolName, unitsHalf,
                Label + "_" + baseLabel + "-TP1",
                stopLossPips: PriceToPips(slDist),
                takeProfitPips: PriceToPips(Math.Abs(tp1 - entry)));

            // Position 2 — TP2
            var r2 = ExecuteMarketOrder(side, SymbolName, unitsHalf,
                Label + "_" + baseLabel + "-TP2",
                stopLossPips: PriceToPips(slDist),
                takeProfitPips: PriceToPips(Math.Abs(tp2 - entry)));

            if (r1.IsSuccessful && r2.IsSuccessful)
            {
                Print($"{baseLabel} ENTRY @ {entry:F2} SL={sl:F2} TP1={tp1:F2} TP2={tp2:F2} " +
                      $"units={unitsHalf}×2 RR={(Math.Abs(tp1 - entry) / slDist):F2}");
            }
            else
            {
                Print($"ORDER FAILED: r1={r1.Error} r2={r2.Error}");
            }
        }

        private double PriceToPips(double priceDist) => priceDist / Symbol.PipSize;

        // ═══════════════════════════════════════════════════════════
        // 5. SMART PARTIAL EXIT (close 50% at Stoch extreme + reversal)
        // ═══════════════════════════════════════════════════════════
        private void HandleSmartPartialExit()
        {
            var positions = Positions.FindAll(Label, SymbolName);
            if (positions.Length == 0) return;

            double close = Px(0);

            foreach (var pos in positions)
            {
                bool inProfit = (pos.TradeType == TradeType.Buy && close > pos.EntryPrice)
                             || (pos.TradeType == TradeType.Sell && close < pos.EntryPrice);
                if (!inProfit) continue;

                if (pos.TradeType == TradeType.Buy
                    && _stochInOvb
                    && close < Lo(1)
                    && !_partialClosedThisOvbCycle)
                {
                    // Bearish reversal at Stoch OVB while long+profit → close TP1 leg if still open
                    if (pos.Label.EndsWith("-TP1"))
                    {
                        ClosePosition(pos);
                        _partialClosedThisOvbCycle = true;
                        Print($"SMART PARTIAL EXIT (long) — closed {pos.Label} @ {close:F2}");
                    }
                }
                else if (pos.TradeType == TradeType.Sell
                    && _stochInOvs
                    && close > Hi(1)
                    && !_partialClosedThisOvsCycle)
                {
                    if (pos.Label.EndsWith("-TP1"))
                    {
                        ClosePosition(pos);
                        _partialClosedThisOvsCycle = true;
                        Print($"SMART PARTIAL EXIT (short) — closed {pos.Label} @ {close:F2}");
                    }
                }
            }
        }
    }
}
