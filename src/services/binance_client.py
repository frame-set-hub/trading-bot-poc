from binance.client import Client
from binance.exceptions import BinanceAPIException

from src.config import settings
from src.utils.logger import get_logger

logger = get_logger(__name__)

# Initialize client at module level to reuse across warm Lambda invocations
_client: Client | None = None


def _get_client() -> Client:
    """Lazily initializes and returns the Binance client."""
    global _client
    if _client is None:
        _client = Client(
            api_key=settings.BINANCE_API_KEY,
            api_secret=settings.BINANCE_API_SECRET,
            testnet=settings.BINANCE_TESTNET,
        )
        logger.info("Binance client initialized (testnet=%s)", settings.BINANCE_TESTNET)
    return _client


def place_market_buy(symbol: str, quote_qty: float) -> dict:
    """Places a market buy order using USDT quote quantity.

    Args:
        symbol: Trading pair (e.g. "BTCUSDT").
        quote_qty: Amount in quote currency (USDT) to spend.

    Returns:
        Binance order response dict.
    """
    client = _get_client()
    try:
        order = client.order_market_buy(
            symbol=symbol,
            quoteOrderQty=quote_qty,
        )
        logger.info(
            "Market BUY executed: symbol=%s, quote_qty=%s, orderId=%s",
            symbol,
            quote_qty,
            order.get("orderId"),
        )
        return order
    except BinanceAPIException as e:
        logger.error("Binance API error on BUY: code=%s, msg=%s", e.code, e.message)
        raise


def place_market_sell(symbol: str, quantity: float) -> dict:
    """Places a market sell order for a given base asset quantity.

    Args:
        symbol: Trading pair (e.g. "BTCUSDT").
        quantity: Amount of base asset to sell.

    Returns:
        Binance order response dict.
    """
    client = _get_client()
    try:
        order = client.order_market_sell(
            symbol=symbol,
            quantity=quantity,
        )
        logger.info(
            "Market SELL executed: symbol=%s, quantity=%s, orderId=%s",
            symbol,
            quantity,
            order.get("orderId"),
        )
        return order
    except BinanceAPIException as e:
        logger.error("Binance API error on SELL: code=%s, msg=%s", e.code, e.message)
        raise
