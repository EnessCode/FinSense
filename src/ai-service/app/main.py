"""FastAPI application entrypoint for the FinSense Deterministic Financial & Receipt OCR microservice."""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.config import Settings, get_settings
from app.providers.base import BaseAIProvider
from app.providers.factory import AIProviderFactory
from app.routers import assistant_router, receipts_router


def get_ai_provider(settings: Settings | None = None) -> BaseAIProvider:
    """Return the deterministic financial and OCR engine."""
    if settings is None:
        settings = get_settings()
    return AIProviderFactory.create(settings)


def create_app() -> FastAPI:
    """Create and configure the FastAPI application instance."""
    settings = get_settings()

    application = FastAPI(
        title=settings.app_name,
        version=settings.app_version,
        docs_url="/docs" if settings.environment != "production" else None,
        redoc_url=None,
    )

    application.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_origins,
        allow_credentials=False,
        allow_methods=["GET", "POST", "OPTIONS"],
        allow_headers=["Content-Type", "X-Internal-Api-Key", "Accept-Language"],
    )

    application.include_router(receipts_router)
    application.include_router(assistant_router)

    @application.get("/health", tags=["Health"])
    async def health_check() -> dict[str, str]:
        current = get_settings()
        return {
            "status": "healthy",
            "service": current.app_name,
            "version": current.app_version,
            "engine": "deterministic",
        }

    return application


app = create_app()

__all__ = ["app", "create_app", "get_ai_provider"]
