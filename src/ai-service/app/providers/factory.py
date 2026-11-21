"""Factory for deterministic financial reasoning and local receipt OCR engine."""

import logging

from app.config import Settings
from app.providers.base import BaseAIProvider
from app.providers.deterministic_engine import (
    DeterministicFinancialEngine,
    SmartLocalProvider,
)

logger = logging.getLogger(__name__)


class AIProviderFactory:
    """Provides the deterministic financial and OCR engine."""

    @staticmethod
    def create(settings: Settings | None = None) -> BaseAIProvider:
        return DeterministicFinancialEngine()
