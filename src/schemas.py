from pydantic import BaseModel, Field


class WebhookPayload(BaseModel):
    """Incoming webhook payload from TradingView."""

    passphrase: str = Field(..., description="Shared secret for authentication")
    action: str = Field(..., pattern="^(BUY|SELL)$", description="Trade action")
    symbol: str = Field(..., description="Trading pair (e.g. BTCUSDT)")
    price: float = Field(..., gt=0, description="Signal price at trigger")
    timestamp: str = Field(..., description="Signal timestamp from TradingView")


class OrderResponse(BaseModel):
    """Response after processing a trade signal."""

    status: str
    action: str
    symbol: str
    detail: str
