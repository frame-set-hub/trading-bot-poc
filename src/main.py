from fastapi import FastAPI, HTTPException, status
from mangum import Mangum

from src.config import settings
from src.schemas import OrderResponse, WebhookPayload
from src.services.binance_client import place_market_buy, place_market_sell
from src.utils.logger import get_logger
from src.utils.security import verify_passphrase

logger = get_logger(__name__)

app = FastAPI(title="Trading Bot PoC", version="0.1.0")


@app.get("/health")
def health_check() -> dict:
    """Health check endpoint for monitoring."""
    return {"status": "ok"}


@app.post("/webhook", response_model=OrderResponse)
def receive_webhook(payload: WebhookPayload) -> OrderResponse:
    """Receives a TradingView webhook, validates it, and executes the trade."""

    # 1. Authenticate
    if not verify_passphrase(payload.passphrase):
        logger.warning("Unauthorized webhook attempt")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid passphrase",
        )

    logger.info("Webhook received: action=%s, symbol=%s", payload.action, payload.symbol)

    # 2. Execute trade
    try:
        if payload.action == "BUY":
            order = place_market_buy(
                symbol=payload.symbol,
                quote_qty=settings.TRADE_QUANTITY_USDT,
            )
            return OrderResponse(
                status="executed",
                action="BUY",
                symbol=payload.symbol,
                detail=f"Market buy order placed. orderId={order.get('orderId')}",
            )

        # SELL
        order = place_market_sell(
            symbol=payload.symbol,
            quantity=settings.TRADE_QUANTITY_USDT,
        )
        return OrderResponse(
            status="executed",
            action="SELL",
            symbol=payload.symbol,
            detail=f"Market sell order placed. orderId={order.get('orderId')}",
        )

    except Exception as e:
        logger.error("Trade execution failed: %s", str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Trade execution failed: {str(e)}",
        )


# AWS Lambda entry point — Mangum wraps the ASGI app for Lambda/API Gateway
handler = Mangum(app, lifespan="off")
