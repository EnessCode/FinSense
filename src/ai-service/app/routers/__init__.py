"""FastAPI internal API routers."""

from app.routers.assistant import router as assistant_router
from app.routers.receipts import router as receipts_router

__all__ = [
    "assistant_router",
    "receipts_router",
]
