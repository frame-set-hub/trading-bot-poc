# Trading Bot PoC - Project Context & Rules

## Architecture Overview

This is a **serverless algorithmic trading system** that receives trading signals from TradingView via webhooks, processes them through an AWS Lambda function, and executes orders on Binance.

**Signal Flow:**
```
TradingView (Pine Script v5)
  → Webhook (JSON payload)
    → AWS API Gateway (HTTP API)
      → AWS Lambda (Python / FastAPI + Mangum)
        → Binance API (python-binance)
          → Order Executed
```

**Key Components:**
- **TradingView**: Generates BUY/SELL signals via Pine Script v5 strategies/indicators. Sends alerts as webhook POST requests.
- **API Gateway (HTTP API)**: Entry point. Routes incoming webhooks to the Lambda function. Uses HTTP API (not REST API) for lower latency and cost.
- **AWS Lambda**: Stateless compute. Receives the webhook payload, validates it, and forwards the trade to Binance. Wrapped with Mangum for ASGI compatibility.
- **Binance**: Executes the actual market/limit orders via the `python-binance` SDK.

## Directory Tree

```
trading-bot-poc/
├── claude.md                  # Project context and rules (this file)
├── README.md                  # Project documentation
├── requirements.txt           # Python dependencies
├── src/
│   ├── main.py                # FastAPI app + Mangum handler (Lambda entry point)
│   ├── config.py              # Settings & environment variable loading
│   ├── schemas.py             # Pydantic models for request/response validation
│   ├── services/
│   │   ├── binance_client.py  # Binance API wrapper (order execution)
│   │   └── signal_parser.py   # Webhook payload parsing & validation
│   └── utils/
│       ├── logger.py          # Structured logging (JSON format for CloudWatch)
│       └── security.py        # Webhook authentication (passphrase/IP validation)
├── tests/
│   ├── test_main.py           # Integration tests for API endpoints
│   └── test_signal_parser.py  # Unit tests for signal parsing logic
├── pine/
│   └── strategy.pine          # TradingView Pine Script v5 strategy source
├── infra/
│   └── template.yaml          # AWS SAM / CloudFormation template (optional)
└── .github/
    └── workflows/
        └── deploy.yml         # CI/CD pipeline (optional)
```

## Coding Rules

### 1. AWS Lambda Best Practices
- **Keep the deployment package small.** Only include necessary dependencies. Avoid heavy libraries when a lighter alternative exists.
- **Use a single `handler` entry point** via Mangum wrapping the FastAPI app. Do not create multiple Lambda functions for this PoC.
- **Initialize SDK clients outside the handler** (module-level) to reuse connections across warm invocations.
- **Set appropriate timeouts.** Lambda timeout should be 30s max for this use case. API Gateway timeout is 29s.
- **Use environment variables** for all configuration (see Security section).

### 2. Security
- **NEVER hardcode API keys, secrets, or passphrases** in source code.
- **ALL sensitive values must come from environment variables** (or AWS Secrets Manager / SSM Parameter Store in production).
  - `BINANCE_API_KEY`
  - `BINANCE_API_SECRET`
  - `WEBHOOK_PASSPHRASE` (shared secret between TradingView and Lambda)
- **Validate every incoming webhook** against the shared passphrase before processing.
- **Use HTTPS only.** API Gateway enforces this by default.
- **Never log sensitive data** (API keys, secrets, full request bodies containing credentials).

### 3. Clean Code Principles
- **Type hints everywhere.** All function signatures must include type annotations.
- **Pydantic for validation.** Use Pydantic models (`schemas.py`) for all request/response data.
- **Single Responsibility.** Each module has one clear purpose (see directory tree).
- **Fail fast, fail loudly.** Validate inputs at the boundary (API layer). Return clear HTTP error codes.
- **Structured logging.** Use JSON-formatted logs for CloudWatch compatibility. Include `request_id` in all log entries.
- **No print statements.** Use the `logging` module exclusively.
- **Keep functions short.** If a function exceeds ~30 lines, consider splitting it.
