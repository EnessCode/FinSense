"""Base provider interface and shared category utilities for deterministic engines."""

from abc import ABC, abstractmethod
from typing import Optional

from app.schemas.chat import ChatRequestDto, ChatResponseDto
from app.schemas.receipt import STANDARD_RECEIPT_CATEGORIES, ReceiptExtractionResponse


def _tr_lower(text: str) -> str:
    """Normalize Turkish characters for case-insensitive matching."""
    return text.replace("İ", "i").replace("I", "ı").lower().replace("\u0307", "")


def match_category(
    candidate: Optional[str],
    available_categories: list[str],
) -> Optional[str]:
    """Match a candidate category name against workspace categories or standard defaults."""
    categories = available_categories or list(STANDARD_RECEIPT_CATEGORIES)
    if not candidate:
        return None
    norm_candidate = _tr_lower(candidate.strip())
    for category in categories:
        if _tr_lower(category.strip()) == norm_candidate:
            return category
    for category in categories:
        norm_cat = _tr_lower(category.strip())
        if norm_candidate in norm_cat or norm_cat in norm_candidate:
            return category
    return candidate if candidate in STANDARD_RECEIPT_CATEGORIES else None


class BaseAIProvider(ABC):
    """Abstract base interface for deterministic financial and receipt engines."""

    provider_name: str = "deterministic-engine"

    @abstractmethod
    async def chat(self, request: ChatRequestDto) -> ChatResponseDto:
        """Generate a deterministic mathematical financial analysis."""

    @abstractmethod
    async def extract_receipt(
        self,
        *,
        image_bytes: Optional[bytes],
        mime_type: Optional[str],
        raw_ocr_text: Optional[str],
        locale: str,
        available_categories: list[str],
        filename: Optional[str] = None,
    ) -> ReceiptExtractionResponse:
        """Extract structured receipt fields using local OCR and regex."""


BaseProvider = BaseAIProvider
