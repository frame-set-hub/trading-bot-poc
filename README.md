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
│   └── strategy.pine          # TradingView Pine Script v5 (EMA Crossover)
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
- **2026-03-14**: Added Pine Script v5 EMA Crossover strategy (`pine/strategy.pine`) with webhook alerts.
- **2026-03-14**: Initial project setup — created `claude.md` (project context & rules) and `README.md` (documentation).
