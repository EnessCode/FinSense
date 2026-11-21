"""Pydantic v2 contracts for the Receipt OCR extraction endpoint."""

from decimal import Decimal
from typing import Any, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator

STANDARD_RECEIPT_CATEGORIES: tuple[str, ...] = (
    "Market",
    "Yeme & İçme",
    "Ulaşım",
    "Faturalar",
    "Alışveriş",
    "Sağlık",
    "Eğlence",
)


class ReceiptOcrRequest(BaseModel):
    """JSON request contract for receipt OCR extraction."""

    model_config = ConfigDict(extra="ignore")

    image_base64: Optional[str] = Field(
        default=None,
        description="Base64-encoded receipt image bytes (optionally with data URI prefix).",
    )
    raw_ocr_text: Optional[str] = Field(
        default=None,
        description="Optional pre-extracted or raw OCR text from the receipt.",
    )
    locale: str = Field(default="tr-TR", max_length=32)
    available_categories: list[str] = Field(
        default_factory=lambda: list(STANDARD_RECEIPT_CATEGORIES),
        description="Available workspace expense category names for classification.",
    )
    mime_type: Optional[str] = Field(default=None, max_length=128)

    @field_validator("locale", mode="before")
    @classmethod
    def _coerce_locale(cls, v: Any) -> str:
        if not v:
            return "tr-TR"
        return str(v).strip()[:32] or "tr-TR"

    @field_validator("available_categories", mode="before")
    @classmethod
    def _coerce_categories(cls, v: Any) -> list[str]:
        if not v:
            return list(STANDARD_RECEIPT_CATEGORIES)
        if isinstance(v, list):
            cleaned = [str(item).strip() for item in v if item is not None and str(item).strip()]
            return cleaned or list(STANDARD_RECEIPT_CATEGORIES)
        return list(STANDARD_RECEIPT_CATEGORIES)

    @field_validator("mime_type", mode="before")
    @classmethod
    def _coerce_mime_type(cls, v: Any) -> Optional[str]:
        if v is None:
            return None
        s = str(v).strip()
        return s[:128] if s else None


class ReceiptExtractionResponse(BaseModel):
    """Structured receipt extraction response returned to the .NET backend."""

    model_config = ConfigDict(extra="ignore")

    is_valid_receipt: bool = Field(
        description="False if the image/input is a non-receipt (selfie, landscape, homework, etc.)."
    )
    rejection_reason: Optional[str] = None
    merchant_name: Optional[str] = None
    transaction_date: Optional[str] = Field(
        default=None,
        description="Normalized ISO calendar date (YYYY-MM-DD).",
    )
    total_amount: Optional[Decimal] = None
    tax_amount: Optional[Decimal] = None
    currency: str = Field(default="TRY", max_length=8)
    suggested_category: Optional[str] = None
    confidence: float = Field(default=0.0, ge=0.0, le=1.0)
    provider_used: str = "deterministic-engine"
