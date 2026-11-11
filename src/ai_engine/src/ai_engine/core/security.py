"""Internal service authentication (OWASP A01/A07, defense in depth).

The engine is only reachable on the private Docker network, but every request (except /health)
must still carry the shared secret sent by the .NET API.
"""

import hmac
from typing import Annotated

from fastapi import Depends, Header, HTTPException, status

from ai_engine.core.config import Settings, get_settings


async def require_internal_api_key(
    settings: Annotated[Settings, Depends(get_settings)],
    x_internal_api_key: Annotated[str | None, Header()] = None,
) -> None:
    expected = settings.engine_api_key
    if not expected or not x_internal_api_key:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED)
    # Constant-time comparison to avoid timing attacks.
    if not hmac.compare_digest(x_internal_api_key.encode(), expected.encode()):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED)
