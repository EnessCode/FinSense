"""Internal service-to-service API key authentication dependency."""

import hmac
from typing import Optional

from fastapi import Depends, Header, HTTPException, status

from app.config import Settings, get_settings

_DEV_KEYS = {
    "dev-internal-secret-key",
    "change_me_to_a_32_byte_random_internal_key",
}


async def verify_internal_api_key(
    x_internal_api_key: Optional[str] = Header(default=None, alias="X-Internal-Api-Key"),
    settings: Settings = Depends(get_settings),
) -> str:
    """Verify the X-Internal-Api-Key header using constant-time comparison."""
    expected_key = settings.internal_api_key or "dev-internal-secret-key"
    is_dev = (settings.environment or "development").lower() in ("development", "dev", "local")

    # If header is missing:
    if not x_internal_api_key:
        if is_dev and expected_key in _DEV_KEYS:
            # Allow local internal calls without rejecting in dev mode
            return expected_key
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing X-Internal-Api-Key header.",
        )

    # Check against expected_key
    if hmac.compare_digest(x_internal_api_key.encode("utf-8"), expected_key.encode("utf-8")):
        return x_internal_api_key

    # In dev mode, check against known default development keys or accept local calls
    if is_dev:
        for dev_k in _DEV_KEYS:
            if hmac.compare_digest(x_internal_api_key.encode("utf-8"), dev_k.encode("utf-8")):
                return x_internal_api_key
        if expected_key in _DEV_KEYS:
            return x_internal_api_key

    raise HTTPException(
        status_code=status.HTTP_403_FORBIDDEN,
        detail="Invalid X-Internal-Api-Key header.",
    )
