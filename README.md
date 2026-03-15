# Trading Bot PoC

A serverless algorithmic trading system — receives signals from TradingView and executes orders on Binance via AWS Lambda.

## Table of Contents

- [Quick Start](#quick-start)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Architecture](#architecture)
  - [Overview Diagram](#overview-diagram)
  - [Tech Stack](#tech-stack)
  - [Request Flow (Sequence)](#request-flow-sequence)
  - [Lambda Internal Flow](#lambda-internal-flow)
  - [Deploy Flow (SAM)](#deploy-flow-sam)
  - [Security & Credentials Flow](#security--credentials-flow)
- [Architecture Trade-offs](#architecture-trade-offs)
- [Cleanup](#cleanup)
- [Changelog](#changelog)

---

## Quick Start

### 1. Configure environment

```bash
cp .env.example .env
# Edit .env and fill in your real values
```

`.env` keys:

| Variable | Description |
|---|---|
| `BINANCE_API_KEY` | Binance API Key (recommend Testnet key first) |
| `BINANCE_API_SECRET` | Binance API Secret |
| `WEBHOOK_PASSPHRASE` | Shared secret between TradingView and Lambda |
| `BINANCE_TESTNET` | Use Testnet (`True`) or Production (`False`) |
| `TRADE_QUANTITY_USDT` | *(optional)* USDT amount per order (default: `10.0`) |
| `TRADE_SYMBOL` | *(optional)* Trading pair (default: `BTCUSDT`) |

### 2. Run local server

```bash
python -m venv venv && source venv/bin/activate
pip install -r requirements.txt
set -a && source .env && set +a
uvicorn src.main:app --reload --port 8000
```

### 3. Test webhook

```bash
curl -X POST http://localhost:8000/webhook \
  -H "Content-Type: application/json" \
  -d '{"passphrase":"your_shared_secret","action":"BUY","symbol":"BTCUSDT","price":67500.00,"timestamp":"2026-03-14T10:00:00Z"}'
```

### 4. Deploy to AWS (SAM)

```bash
cd infra && sam build && sam deploy --guided
```

### 5. Run tests

```bash
pytest tests/ -v
```

---

## Project Structure

```
trading-bot-poc/
├── claude.md                  # Project context & rules
├── README.md                  # This document
├── requirements.txt           # Python dependencies
├── src/
│   ├── main.py                # FastAPI app + Mangum handler (Lambda entry point)
│   ├── config.py              # Settings & environment variable loading
│   ├── schemas.py             # Pydantic models (request/response validation)
│   ├── services/
│   │   └── binance_client.py  # Binance API wrapper (order execution)
│   └── utils/
│       ├── logger.py          # Structured logging (JSON for CloudWatch)
│       └── security.py        # Webhook authentication (passphrase validation)
├── pine/
│   ├── ema-12-26-strategy.pine     # EMA 12/26 Crossover strategy
│   ├── rsi_stoch_state.pine       # RSI+Stoch Trend & Fibo (overlay=true, กราฟหลัก)
│   ├── rsi_stoch_indicator.pine   # RSI+Stoch Indicator (overlay=false, pane ด้านล่าง)
│   └── rsi_stoch_strategy.pine   # RSI+Stoch Strategy — backtest with Strategy Report
├── infra/
│   └── template.yaml          # AWS SAM template (Lambda + API Gateway)
└── tests/
    ├── test_main.py           # Integration tests
    └── test_signal_parser.py  # Unit tests
```

## Prerequisites

- Python 3.11+
- AWS CLI (`aws configure`)
- AWS SAM CLI (`sam --version`)
- Binance account with API Keys (recommend Testnet first)
- TradingView account (Pro+ for Webhook alerts)

---

## Architecture

### Overview Diagram

```
                    ┌─── AWS Cloud ──────────────────────────────┐
                    │                                             │
┌──────────────┐    │  ┌──────────────────┐  ┌────────────────┐  │    ┌──────────────┐
│  TradingView │    │  │   API Gateway    │  │  AWS Lambda    │  │    │   Binance    │
│              │    │  │   (HTTP API)     │  │                │  │    │   Exchange   │
│  Pine Script │    │  │                  │  │  FastAPI app   │  │    │              │
│  v5 Strategy │────│──│  POST /webhook ──│──│  + Mangum      │──│────│  Market      │
│              │    │  │  GET  /health    │  │                │  │    │  Orders      │
│  EMA 12/26   │    │  │                  │  │  1. Auth       │  │    │              │
│  Crossover   │    │  │  HTTPS only      │  │  2. Validate   │  │    │  python-     │
│              │    │  │  Auto-scaling    │  │  3. Execute    │  │    │  binance SDK │
└──────────────┘    │  └──────────────────┘  └────────────────┘  │    └──────────────┘
                    │                                             │
                    └─────────────────────────────────────────────┘
```

### Tech Stack

```
┌──────────────────────────────────────────────────────────────────┐
│                     Trading Bot PoC Tech Stack                    │
├────────────────┬─────────────────────────────────────────────────┤
│   Layer        │   Technology                                     │
├────────────────┼─────────────────────────────────────────────────┤
│   Signal       │   TradingView (Pine Script v5 — EMA Crossover)  │
├────────────────┼─────────────────────────────────────────────────┤
│   Transport    │   Webhook (HTTPS POST — JSON payload)           │
├────────────────┼─────────────────────────────────────────────────┤
│   Gateway      │   AWS API Gateway (HTTP API)                    │
├────────────────┼─────────────────────────────────────────────────┤
│   Compute      │   AWS Lambda (Python 3.11 — 256MB / 30s)       │
├────────────────┼─────────────────────────────────────────────────┤
│   Framework    │   FastAPI + Mangum (ASGI → Lambda adapter)      │
├────────────────┼─────────────────────────────────────────────────┤
│   Validation   │   Pydantic v2 + pydantic-settings               │
├────────────────┼─────────────────────────────────────────────────┤
│   Exchange     │   Binance (python-binance SDK — Testnet/Prod)   │
├────────────────┼─────────────────────────────────────────────────┤
│   IaC/Deploy   │   AWS SAM (template.yaml → CloudFormation)      │
├────────────────┼─────────────────────────────────────────────────┤
│   Logging      │   Structured JSON logs → CloudWatch             │
└────────────────┴─────────────────────────────────────────────────┘
```

### Request Flow (Sequence)

```
  TradingView          API Gateway            Lambda (FastAPI)           Binance
      │                     │                       │                      │
      │  EMA Crossover!     │                       │                      │
      │  alert() triggers   │                       │                      │
      │                     │                       │                      │
      │  POST /webhook      │                       │                      │
      │  {passphrase,       │                       │                      │
      │   action: "BUY",    │                       │                      │
      │   symbol, price}    │                       │                      │
      │────────────────────▶│                       │                      │
      │                     │  Proxy Integration    │                      │
      │                     │──────────────────────▶│                      │
      │                     │                       │                      │
      │                     │                ┌──────┴──────┐               │
      │                     │                │ 1. Security │               │
      │                     │                │ verify_     │               │
      │                     │                │ passphrase  │               │
      │                     │                └──────┬──────┘               │
      │                     │                       │                      │
      │                     │                       │ Invalid?             │
      │                     │   401 Unauthorized    │                      │
      │                     │◀ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─│                      │
      │                     │                       │                      │
      │                     │                       │ Valid                │
      │                     │                ┌──────┴──────┐               │
      │                     │                │ 2. Pydantic │               │
      │                     │                │ Validate    │               │
      │                     │                │ payload     │               │
      │                     │                └──────┬──────┘               │
      │                     │                       │                      │
      │                     │                       │  order_market_buy()  │
      │                     │                       │─────────────────────▶│
      │                     │                       │                      │
      │                     │                       │              ┌───────┴──────┐
      │                     │                       │              │ Execute      │
      │                     │                       │              │ Market Order │
      │                     │                       │              └───────┬──────┘
      │                     │                       │                      │
      │                     │                       │  Order Confirmation  │
      │                     │                       │◀─────────────────────│
      │                     │                       │  {orderId, status}   │
      │                     │                       │                      │
      │                     │  200 OK               │                      │
      │                     │  {status: "executed"} │                      │
      │                     │◀──────────────────────│                      │
      │                     │                       │                      │
      │  200 OK             │                       │                      │
      │◀────────────────────│                       │                      │
      │                     │                       │                      │
```

### Lambda Internal Flow

```
                    ┌─────────────────────────────────────────────────────┐
                    │              AWS Lambda (Python 3.11)                │
                    │                                                     │
  API Gateway ─────▶│  ┌──────────────────────────────────────────────┐   │
  (event)           │  │  Mangum (ASGI Adapter)                       │   │
                    │  │  Converts Lambda event → ASGI request        │   │
                    │  └────────────────┬─────────────────────────────┘   │
                    │                   │                                  │
                    │                   ▼                                  │
                    │  ┌──────────────────────────────────────────────┐   │
                    │  │  FastAPI Router                               │   │
                    │  │                                               │   │
                    │  │  POST /webhook ──▶ receive_webhook()          │   │
                    │  │  GET  /health  ──▶ health_check()             │   │
                    │  └────────────────┬─────────────────────────────┘   │
                    │                   │ calls                            │
                    │                   ▼                                  │
                    │  ┌─────────────────┐    ┌────────────────────────┐  │
                    │  │  security.py    │    │  schemas.py            │  │
                    │  │                 │    │                        │  │
                    │  │  verify_        │    │  WebhookPayload       │  │
                    │  │  passphrase()   │    │  (Pydantic model)     │  │
                    │  │  hmac.compare   │    │  action, symbol,      │  │
                    │  │                 │    │  price, timestamp     │  │
                    │  └─────────────────┘    └────────────────────────┘  │
                    │                   │                                  │
                    │                   ▼                                  │
                    │  ┌──────────────────────────────────────────────┐   │
                    │  │  binance_client.py                            │   │
                    │  │                                               │   │
                    │  │  _client (Module-level — reuse across warm)   │   │
                    │  │                                               │   │
                    │  │  place_market_buy(symbol, quote_qty)          │   │──▶ Binance API
                    │  │  place_market_sell(symbol, quantity)          │   │
                    │  └──────────────────────────────────────────────┘   │
                    │                   │ uses                             │
                    │                   ▼                                  │
                    │  ┌─────────────────┐    ┌────────────────────────┐  │
                    │  │  config.py      │    │  logger.py             │  │
                    │  │                 │    │                        │  │
                    │  │  Settings       │    │  JSONFormatter         │  │
                    │  │  (pydantic-     │    │  Structured logs       │  │──▶ CloudWatch
                    │  │   settings)     │    │  for CloudWatch        │  │
                    │  │  Reads from ENV │    │                        │  │
                    │  └─────────────────┘    └────────────────────────┘  │
                    └─────────────────────────────────────────────────────┘
```

### Deploy Flow (SAM)

```
  Developer                  SAM CLI                           AWS
      │                        │                                 │
      │  sam build             │                                 │
      │───────────────────────▶│                                 │
      │                        │                                 │
      │                 ┌──────┴──────┐                          │
      │                 │ Read        │                          │
      │                 │ template.   │                          │
      │                 │ yaml        │                          │
      │                 └──────┬──────┘                          │
      │                        │                                 │
      │  sam deploy --guided   │                                 │
      │───────────────────────▶│                                 │
      │                        │                                 │
      │                 ┌──────┴──────┐                          │
      │                 │ Collect     │                          │
      │                 │ Params      │                          │
      │                 │ API Key     │                          │
      │                 │ API Secret  │                          │
      │                 │ Passphrase  │                          │
      │                 │ (NoEcho)    │                          │
      │                 └──────┬──────┘                          │
      │                        │                                 │
      │                 [1] Upload to S3                          │
      │                        │────────────────────────────────▶│
      │                        │   deployment package             │
      │                        │                                 │
      │                 [2] Create CloudFormation Stack           │
      │                        │────────────────────────────────▶│
      │                        │   ├── Lambda Function            │
      │                        │   ├── HTTP API Gateway           │
      │                        │   ├── IAM Execution Role         │
      │                        │   └── CloudWatch Log Group       │
      │                        │                                 │
      │                        │   Stack Created                 │
      │                        │◀────────────────────────────────│
      │                        │                                 │
      │  Outputs:              │                                 │
      │  ApiEndpoint: https:// │                                 │
      │  <api-id>.execute-api  │                                 │
      │  .<region>.amazonaws   │                                 │
      │◀───────────────────────│                                 │
      │                        │                                 │
```

### Security & Credentials Flow

```
┌──────────────────────────────────────────────────────────────────────┐
│                       Credentials Flow                                │
│                                                                      │
│   .env (LOCAL ONLY — never commit to Git)                            │
│   ┌──────────────────────────────────────────────┐                   │
│   │ BINANCE_API_KEY=your_api_key                 │                   │
│   │ BINANCE_API_SECRET=your_api_secret           │                   │
│   │ WEBHOOK_PASSPHRASE=your_shared_secret        │                   │
│   │ BINANCE_TESTNET=True                         │                   │
│   └──────────────┬───────────────────────────────┘                   │
│                  │                                                    │
│        ┌─────────┴─────────┐                                         │
│        ▼                   ▼                                          │
│   Local Dev            SAM Deploy                                    │
│   (source .env)        (--parameter-overrides)                       │
│        │                   │                                          │
│        ▼                   ▼                                          │
│   uvicorn              CloudFormation                                │
│   (reads ENV)          Parameters (NoEcho)                           │
│        │                   │                                          │
│        │                   ▼                                          │
│        │            ┌──────────────────────┐                         │
│        │            │  Lambda Environment  │                         │
│        │            │  Variables           │                         │
│        │            │  (encrypted at rest) │                         │
│        │            └──────────┬───────────┘                         │
│        │                       │                                      │
│        └───────────┬───────────┘                                     │
│                    ▼                                                  │
│        ┌──────────────────────┐        ┌───────────────────────┐     │
│        │  pydantic-settings   │        │  security.py          │     │
│        │  Settings class      │───────▶│  hmac.compare_digest  │     │
│        │  Reads ENV auto      │        │  Validates passphrase │     │
│        └──────────────────────┘        └───────────────────────┘     │
│                                                                      │
│   .gitignore prevents .env from being committed to repository       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Pine Script: RSI+Stoch Pro Indicator

`pine/rsi_stoch_state.pine` — a 7-module indicator built on RSI + Stochastic that identifies trend structure, Fibonacci targets, divergence signals, multi-timeframe confluence, and entry/exit management.

### 3-File Architecture

The RSI+Stoch system is split into 3 files due to Pine Script's overlay limitation (a single script can only draw on the price chart OR a separate pane, not both):

```
┌──────────────────────────────────────────────────────────────────────┐
│  rsi_stoch_state.pine (overlay=true — กราฟหลัก)                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  Modules 1-4, 6-7: State → Zones → Trends → Fibo → MTF →    │  │
│  │  Entry/Exit with BUY/SELL labels, SL lines, trailing stop     │  │
│  │  Draws: Support/Resistance lines, Fibo levels, trade labels   │  │
│  └────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────┤
│  rsi_stoch_indicator.pine (overlay=false — oscillator pane)          │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  RSI + Stoch lines, zone backgrounds, RSI Divergence (Mod 5)  │  │
│  │  Draws: %K/%D, RSI, RSI MA, OVB/OVS zones, Div lines/labels  │  │
│  └────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────┤
│  rsi_stoch_strategy.pine (strategy — backtest & Strategy Report)     │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  Same logic as state.pine + strategy.entry/exit integration   │  │
│  │  TP1 = Fibo 1.618, TP2 = Fibo 2.618, SL = entry candle H/L  │  │
│  │  Generates: P&L, Equity Curve, Win Rate, Trade List           │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

**Usage:** Add `rsi_stoch_state.pine` + `rsi_stoch_indicator.pine` together on a chart for live analysis. Use `rsi_stoch_strategy.pine` separately to run backtests and view the TradingView Strategy Report.

### Module Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    RSI + Stoch Pro — Module Flow                      │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  MODULE 1: State Tracking                                     │  │
│  │  ┌──────────────────┐   ┌──────────────────┐                  │  │
│  │  │ Stoch OVB/OVS    │   │ RSI OVB/OVS      │                 │  │
│  │  │ Zone detection   │   │ Zone detection    │                 │  │
│  │  │ Cycle High/Low   │   │ (Reset trigger)   │                 │  │
│  │  └────────┬─────────┘   └────────┬──────────┘                 │  │
│  └───────────┼──────────────────────┼────────────────────────────┘  │
│              │                      │                                │
│              ▼                      ▼                                │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  MODULE 2: Base Zone (กรอบปรับฐาน)                            │  │
│  │                                                               │  │
│  │  Stoch exits OVS ──▶ Resistance = prev OVB High              │  │
│  │  Stoch exits OVB ──▶ Support    = prev OVS Low               │  │
│  │                                                               │  │
│  │  RSI enters OVB/OVS ──▶ "ตัดภูเขา" Reset ทุกอย่าง            │  │
│  └─────────────────────────────┬─────────────────────────────────┘  │
│                                │                                     │
│                                ▼                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  MODULE 3: Trend Recognition                                  │  │
│  │                                                               │  │
│  │  %K breaks Resistance ──▶ Breakout UP                        │  │
│  │  %K breaks Support    ──▶ Breakout DOWN                      │  │
│  │                                                               │  │
│  │  Has Higher Low / Lower High in zone?                        │  │
│  │     YES ──▶ PERFECT Trend                                    │  │
│  │     NO  ──▶ V-SHAPE Trend                                    │  │
│  └─────────────────────────────┬─────────────────────────────────┘  │
│                                │                                     │
│                                ▼                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  MODULE 4: Fibonacci Targets & Invalidation                   │  │
│  │                                                               │  │
│  │  Fibo Head (0.0) = กรอบที่ไม่ถูกเบรก                          │  │
│  │  Fibo End  (1.0) = กรอบที่ถูกเบรก                             │  │
│  │                                                               │  │
│  │  Levels: 0.618 │ 1.618 │ 2.0 │ 2.618 │ 4.236               │  │
│  │                                                               │  │
│  │  Invalidation:                                                │  │
│  │    %K reverses past Fibo Head ──▶ Trend destroyed            │  │
│  │    V-Shape: reverses before 1.618 ──▶ Trend destroyed        │  │
│  └─────────────────────────────┬─────────────────────────────────┘  │
│                                │                                     │
│              ┌─────────────────┼──────────────────┐                  │
│              ▼                                    ▼                  │
│  ┌────────────────────────┐  ┌────────────────────────────────────┐ │
│  │  MODULE 5: Divergence  │  │  MODULE 6: MTF Fibo Cluster        │ │
│  │                        │  │                                    │ │
│  │  Price LL + RSI HL     │  │  request.security() ──▶ HTF Stoch │ │
│  │  = Bullish Div         │  │                                    │ │
│  │  "Stoch > OVB 1 รอบ"   │  │  HTF Fibo 1.618 / 2.618          │ │
│  │                        │  │  vs Current TF Fibo               │ │
│  │  Price HH + RSI LH    │  │                                    │ │
│  │  = Bearish Div         │  │  Match within threshold?          │ │
│  │  "Stoch > OVS 1 รอบ"   │  │  YES ──▶ GOLD line (Cluster)     │ │
│  │                        │  │  NO  ──▶ Normal color             │ │
│  └────────────────────────┘  └────────────────────────────────────┘ │
│                                │                                     │
│                                ▼                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  MODULE 7: Entry Trigger & Risk Management                    │  │
│  │                                                               │  │
│  │  Candlestick Reversal:                                        │  │
│  │    Pin Bar / Engulfing at support/resistance                  │  │
│  │                                                               │  │
│  │  Trade Entry: BUY/SELL labels on chart                        │  │
│  │  Stop Loss: Entry candle High/Low                             │  │
│  │  Trailing Stop: ATR-based (1.5x ATR, 14-period)              │  │
│  │                                                               │  │
│  │  Strategy version adds:                                       │  │
│  │    TP1 = Fibo 1.618 (50%) │ TP2 = Fibo 2.618 (50%)          │  │
│  │    strategy.entry() / strategy.exit() with split qty          │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Data Dependencies

```
State Tracking (1) ──▶ Base Zone (2) ──▶ Trend Recognition (3) ──▶ Fibo Targets (4)
                                                                        │
RSI Pivots ────────────────────────▶ Divergence (5)                     │
                                                                        │
HTF Stoch (request.security) ──────────────────────────▶ MTF Cluster (6)
                                                                        │
Candlestick + ATR ─────────────────────────────────────▶ Entry/Exit (7)
```

### Key Concepts

| Concept | Description |
|---|---|
| **OVB / OVS** | Stoch > 80 = Overbought, Stoch < 20 = Oversold |
| **กรอบปรับฐาน** | Support/Resistance built from completed Stoch cycles |
| **ตัดภูเขา** | RSI entering OVB/OVS resets all zones and trends |
| **Perfect Trend** | Breakout with Higher Low (up) or Lower High (down) in zone |
| **V-Shape Trend** | Breakout without structural confirmation — stricter invalidation |
| **Fibo Head** | The unbroken zone line — if %K crosses back, trend is invalidated |
| **Fibo Cluster** | Current TF Fibo aligns with HTF Fibo within threshold — high significance zone |

### Visual Elements

| Element | Color | Meaning |
|---|---|---|
| Stoch %K / %D | Green / Red | Stochastic oscillator lines |
| RSI / RSI MA | White / Yellow | RSI and its moving average |
| Resistance line | Red | Base zone upper bound |
| Support line | Green | Base zone lower bound |
| PERFECT label | Lime (up) / Orange (down) | Structural breakout confirmed |
| V-SHAPE label | Aqua (up) / Fuchsia (down) | Fast breakout without structure |
| Fibo 1.618 | Orange dashed | First outer target |
| Fibo 2.618 | Red dashed | Extended target |
| Fibo 4.236 | Purple dashed | Maximum extension |
| **CLUSTER** | **Gold solid (thick)** | **MTF confluence — high significance** |
| BULL/BEAR DIV | Green/Red line + label | RSI divergence with Stoch prediction |
| BUY label | Green ▲ | Long entry triggered |
| SELL label | Red ▼ | Short entry triggered |
| SL line | Red dashed | Stop loss level |
| INVALIDATED | Gray X | Trend destroyed |
| Background | Red/Green tint | Stoch currently in OVB/OVS zone |

### Settings (Inputs)

| Group | Parameter | Default |
|---|---|---|
| RSI | Length / MA Length | 14 / 14 |
| Stochastic | %K / %D / Smoothing | 9 / 3 / 3 |
| Zone | Stoch OVB/OVS, RSI OVB/OVS | 80/20, 70/30 |
| Divergence | Pivot Lookback L/R | 5 / 5 |
| MTF Cluster | Auto-detect HTF, Manual TF, Threshold | true, 60, 3.0 |
| Entry | ATR Length, ATR Multiplier | 14, 1.5 |
| Strategy | Direction (Long/Short/Both), Use TP2, TP1 Split % | Both, true, 50 |
| Strategy | Initial Capital, Commission | 100,000, 0.1% |

### Known Limitations & Testing Notes

- **Step 6 (MTF Cluster)** is the most complex module — HTF state tracking uses simplified logic compared to the current TF. Edge cases may occur when the HTF has insufficient data or when the chart TF doesn't map cleanly to a higher TF.
- **Fibo levels operate on price** (post-fix). Base zones use `high`/`low` from completed Stoch cycles, not Stoch values.
- **Label/line limits**: TradingView has a max of ~500 labels and ~500 lines per indicator. Long backtests with many signals may hit this limit.
- **`request.security` repainting**: The HTF data uses `barmerge.lookahead_off` to avoid repainting, but transitions between timeframes may still show brief inconsistencies on real-time bars.

---

## Architecture Trade-offs

### 1. AWS Lambda (Serverless / Event-Driven) - [Chosen for this PoC]
- **Pros:** Highly cost-effective (often 100% free under AWS Free Tier), Zero Maintenance (No server management), and Auto-scaling. Perfectly suited for event-driven architectures (Webhook -> Lambda execution -> Terminate).
- **Cons:** Potential Cold Start latency (100ms - 2s). Cannot maintain long-running WebSocket connections (15-minute execution limit).
- **Best For:** Swing Trade, Trend Following, or end-of-candle strategies (5m, 15m, 1H) where a 1-2 second slippage does not impact profitability.

### 2. Amazon EC2 / VPS (Server-based / Always-On) - [Alternative]
- **Pros:** Always running (No Cold Starts), supports persistent WebSocket connections for real-time tick-by-tick orderbook data.
- **Cons:** Fixed costs (pay hourly/monthly even when idle), requires OS maintenance, security patching, and manual load balancing.
- **Best For:** High-Frequency Trading (HFT), Arbitrage, or Market Making requiring sub-millisecond execution.

---

## Cleanup

Remove all AWS resources:

```bash
# Delete CloudFormation stack (removes Lambda + API Gateway + IAM Role)
sam delete --stack-name trading-bot

# Or use AWS CLI directly
aws cloudformation delete-stack --stack-name trading-bot
```

---

## Changelog

- **2026-03-14**: Added AWS SAM deployment template (`infra/template.yaml`) with HTTP API Gateway and secure parameter handling.
- **2026-03-14**: Built FastAPI backend — webhook endpoint, Binance integration, Mangum handler, Pydantic schemas, structured logging, passphrase auth.
- **2026-03-15**: Added RSI+Stoch Strategy backtest version (`pine/rsi_stoch_strategy.pine`) — TP1/TP2 Fibo targets, split position exits, TradingView Strategy Report.
- **2026-03-15**: Added Module 7: Entry Trigger & Risk Management — candlestick reversal patterns, ATR trailing stop, BUY/SELL labels.
- **2026-03-15**: Fixed scale bug: base zones now use price High/Low instead of Stoch values (0-100). Split into 3 files for overlay compatibility.
- **2026-03-15**: Added RSI+Stoch Pro 7-module indicator (`pine/rsi_stoch_state.pine`) — state tracking, base zones, trend recognition, Fibonacci targets, RSI divergence, MTF Fibo clustering, entry/exit management.
- **2026-03-14**: Added Pine Script v5 EMA Crossover strategy (`pine/ema-12-26-strategy.pine`) with webhook alerts.
- **2026-03-14**: Initial project setup — created `claude.md` (project context & rules) and `README.md` (documentation).
