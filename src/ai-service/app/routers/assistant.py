"""Internal Financial Coach Chat endpoint protected by auth and deterministic logic."""

from fastapi import APIRouter, Depends

from app.config import Settings, get_settings
from app.providers.factory import AIProviderFactory
from app.schemas.chat import ChatRequestDto, ChatResponseDto
from app.security.auth import verify_internal_api_key

router = APIRouter(
    prefix="/api/v1/internal/ai",
    tags=["Internal AI Financial Coach"],
    dependencies=[Depends(verify_internal_api_key)],
)


@router.post("/chat", response_model=ChatResponseDto)
async def financial_coach_chat(
    payload: ChatRequestDto,
    settings: Settings = Depends(get_settings),
) -> ChatResponseDto:
    """Analyze pre-calculated deterministic financial summaries with rule-based calculators."""
    is_en = (payload.locale or "").lower().startswith("en")
    stripped_q = (payload.question or "").strip()

    if not stripped_q:
        return ChatResponseDto(
            is_finance_related=False,
            intent="OFF_TOPIC_REJECTED",
            answer="Lütfen bir finansal soru girin." if not is_en else "Please enter a financial question.",
            highlighted_category=None,
            suggested_action=None,
            provider_used="deterministic-engine",
        )

    if len(stripped_q) > settings.max_input_chars:
        return ChatResponseDto(
            is_finance_related=False,
            intent="OFF_TOPIC_REJECTED",
            answer=(
                f"Sorunuz çok uzun. Lütfen {settings.max_input_chars} karakter altında özetleyiniz."
                if not is_en
                else f"Your question is too long. Please shorten it to under {settings.max_input_chars} characters."
            ),
            highlighted_category=None,
            suggested_action=None,
            provider_used="deterministic-engine",
        )

    engine = AIProviderFactory.create(settings)
    return await engine.chat(payload)
