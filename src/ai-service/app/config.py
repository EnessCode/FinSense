"""Configuration settings for the FinSense Deterministic Financial & Receipt OCR microservice."""

from pathlib import Path
from pydantic_settings import BaseSettings, SettingsConfigDict

_SERVICE_DIR = Path(__file__).resolve().parent.parent
_ENV_FILES = (_SERVICE_DIR / ".env", _SERVICE_DIR.parent.parent / ".env")


class Settings(BaseSettings):
    """Application settings loaded from environment variables or .env."""

    model_config = SettingsConfigDict(
        env_file=_ENV_FILES,
        env_file_encoding="utf-8",
        extra="ignore",
    )

    app_name: str = "FinSense Deterministic Financial & Receipt OCR Service"
    app_version: str = "2.0.0"
    environment: str = "development"

    internal_api_key: str = "dev-internal-secret-key"

    max_input_chars: int = 250
    max_output_chars: int = 450
    max_image_dimension: int = 1280
    request_timeout_seconds: float = 20.0

    cors_origins: list[str] = [
        "http://localhost:5000",
        "http://127.0.0.1:5000",
        "http://localhost:5173",
        "http://localhost:8081",
    ]


def get_settings() -> Settings:
    """Return a fresh Settings instance reflecting current environment variables and .env."""
    return Settings()
