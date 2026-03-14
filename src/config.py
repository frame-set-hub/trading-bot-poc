import os

from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    """Application settings loaded from environment variables."""

    BINANCE_API_KEY: str = ""
    BINANCE_API_SECRET: str = ""
    WEBHOOK_PASSPHRASE: str = ""

    # Trading defaults
    TRADE_QUANTITY_USDT: float = 10.0
    TRADE_SYMBOL: str = "BTCUSDT"

    # Binance testnet toggle
    BINANCE_TESTNET: bool = True

    class Config:
        env_file = ".env"
        case_sensitive = True


settings = Settings()
