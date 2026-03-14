import hmac

from src.config import settings
from src.utils.logger import get_logger

logger = get_logger(__name__)


def verify_passphrase(incoming: str) -> bool:
    """Validates the webhook passphrase using constant-time comparison."""
    if not settings.WEBHOOK_PASSPHRASE:
        logger.warning("WEBHOOK_PASSPHRASE is not set — rejecting request")
        return False
    return hmac.compare_digest(incoming, settings.WEBHOOK_PASSPHRASE)
