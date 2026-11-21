"""Comprehensive pytest suite for auth, deterministic calculators, OCR, and endpoints."""

import base64
from decimal import Decimal
import io
from pathlib import Path
import sys
from unittest.mock import MagicMock

from fastapi.testclient import TestClient
from PIL import Image
import pytest

# Ensure `src/ai-service` is importable regardless of pytest invocation directory
SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(SERVICE_ROOT))

from app.config import Settings  # noqa: E402
from app.main import app  # noqa: E402
from app.providers.base import match_category  # noqa: E402
from app.providers.deterministic_engine import (  # noqa: E402
    DeterministicFinancialEngine,
    SmartLocalProvider,
)
from app.providers.factory import AIProviderFactory  # noqa: E402
from app.schemas.chat import (  # noqa: E402
    ChatRequestDto,
    FinancialContextDto,
    InvestmentsSummaryDto,
    RecurringSummaryDto,
)

TEST_INTERNAL_KEY = "test-internal-secret-key"


def _sample_financial_context() -> dict:
    return {
        "total_balance": "42500.00",
        "monthly_income": "85000.00",
        "monthly_expense": "34200.50",
        "net_cash_flow": "50799.50",
        "savings_rate_percent": "59.76",
        "top_categories": [
            {
                "category_name": "Market",
                "amount": "12450.00",
                "percentage": "36.40",
                "transaction_count": 14,
            },
            {
                "category_name": "Yeme & İçme",
                "amount": "6300.00",
                "percentage": "18.42",
                "transaction_count": 9,
            },
        ],
        "budget_alerts": [
            {
                "category_name": "Market",
                "limit_amount": "15000.00",
                "spent_amount": "12450.00",
                "utilization_percent": "83.00",
                "status": "Warning",
            }
        ],
        "member_breakdown": [
            {"member_name": "Ahmet", "amount": "20100.00", "percentage": "58.77"},
            {"member_name": "Ayşe", "amount": "14100.50", "percentage": "41.23"},
        ],
        "investments_summary": {
            "total_value": "150000.00",
            "total_cost": "120000.00",
            "unrealized_profit_loss": "30000.00",
            "profit_loss_percentage": "25.00",
            "asset_count": 4,
        },
    }


def _make_png_bytes(width: int = 200, height: int = 400, color: str = "white") -> bytes:
    buf = io.BytesIO()
    Image.new("RGB", (width, height), color=color).save(buf, format="PNG")
    return buf.getvalue()


@pytest.fixture
def client(monkeypatch: pytest.MonkeyPatch) -> TestClient:
    monkeypatch.setenv("INTERNAL_API_KEY", TEST_INTERNAL_KEY)
    return TestClient(app)


# ---------------------------------------------------------------------------
# 1. Internal API Key Authentication Tests
# ---------------------------------------------------------------------------


def test_missing_or_invalid_internal_api_key_rejected(client: TestClient) -> None:
    payload = {
        "question": "Bu ay hangi kategoride çok harcadım?",
        "locale": "tr-TR",
        "workspace_type": "Personal",
        "financial_context": _sample_financial_context(),
    }

    # Missing header -> 401
    resp_missing = client.post("/api/v1/internal/ai/chat", json=payload)
    assert resp_missing.status_code == 401

    # Wrong header -> 403
    resp_invalid = client.post(
        "/api/v1/internal/ai/chat",
        json=payload,
        headers={"X-Internal-Api-Key": "wrong-secret"},
    )
    assert resp_invalid.status_code == 403

    # Receipt endpoint missing header -> 401
    resp_ocr_missing = client.post(
        "/api/v1/internal/ocr/receipt",
        json={"raw_ocr_text": "MİGROS TOPLAM 150,00"},
    )
    assert resp_ocr_missing.status_code == 401


# ---------------------------------------------------------------------------
# 2. Over-Length & Empty Input Tests
# ---------------------------------------------------------------------------


def test_over_length_and_empty_input_rejected_cleanly(client: TestClient) -> None:
    over_length_question = "Bütçe harcamam ne durumda? " * 15  # > 250 chars
    assert len(over_length_question) > 250

    resp = client.post(
        "/api/v1/internal/ai/chat",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        json={
            "question": over_length_question,
            "locale": "tr-TR",
            "workspace_type": "Personal",
            "financial_context": _sample_financial_context(),
        },
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["is_finance_related"] is False
    assert data["intent"] == "OFF_TOPIC_REJECTED"
    assert "karakter" in data["answer"] or "shorten" in data["answer"].lower()

    empty_resp = client.post(
        "/api/v1/internal/ai/chat",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        json={
            "question": "   ",
            "locale": "en-US",
            "workspace_type": "Personal",
            "financial_context": _sample_financial_context(),
        },
    )
    assert empty_resp.status_code == 200
    assert empty_resp.json()["intent"] == "OFF_TOPIC_REJECTED"


# ---------------------------------------------------------------------------
# 3. Deterministic Financial Engine: 10 Analytical Calculators
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_deterministic_engine_10_calculators_unique_answers() -> None:
    engine = DeterministicFinancialEngine()
    ctx_dict = _sample_financial_context()

    def _make_req(q: str, locale: str = "tr-TR", ws_type: str = "Personal") -> ChatRequestDto:
        return ChatRequestDto(
            question=q,
            locale=locale,
            workspace_type=ws_type,
            financial_context=FinancialContextDto.model_validate(ctx_dict),
        )

    # 1. Category Specific Spend
    res_cat = await engine.chat(_make_req("Market harcamam ne kadar oldu?"))
    assert res_cat.intent == "SPENDING_QUERY"
    assert "Market" in res_cat.answer
    assert "12.450,00" in res_cat.answer
    assert res_cat.highlighted_category == "Market"

    # 2. Month-End Burn Rate & Projection
    res_burn = await engine.chat(_make_req("Ay sonunu çıkarabilir miyim, gidişat nasıl?"))
    assert res_burn.intent == "BUDGET_ANALYSIS"
    assert "günlük harcama" in res_burn.answer.lower() or "tempoyla" in res_burn.answer.lower() or "surplus" in res_burn.answer.lower()

    # 3. Top Spending Categories Breakdown
    res_top = await engine.chat(_make_req("En çok nereye para gidiyor?"))
    assert res_top.intent == "SPENDING_QUERY"
    assert "Market" in res_top.answer
    assert "Yeme & İçme" in res_top.answer or "büyük harcama" in res_top.answer

    # 4. Budget Alerts & Overspending
    res_alerts = await engine.chat(_make_req("Bütçe aşım riskim var mı?"))
    assert res_alerts.intent == "BUDGET_ANALYSIS"
    assert "Market" in res_alerts.answer
    assert "%83" in res_alerts.answer

    # 5. Savings & 50/30/20 Rule Simulation
    res_savings = await engine.chat(_make_req("Tasarrufumu nasıl artırırım? 50/30/20 simülasyonu yap."))
    assert res_savings.intent == "SAVINGS_TIP"
    assert "50/30/20" in res_savings.answer
    assert "%20" in res_savings.answer

    # 6. Wallet Runway & Liquidity
    res_runway = await engine.chat(_make_req("Param ne kadar süre yeter, likidite nedir?"))
    assert res_runway.intent == "SPENDING_QUERY"
    assert "42.500,00" in res_runway.answer
    assert "ay" in res_runway.answer.lower() or "gün" in res_runway.answer.lower()

    # 7. Income vs Expense Cash Flow Balance
    res_flow = await engine.chat(_make_req("Gelir gider dengem ve nakit akışım nasıl?"))
    assert res_flow.intent == "SPENDING_QUERY"
    assert "85.000,00" in res_flow.answer
    assert "34.200,50" in res_flow.answer
    assert "50.799,50" in res_flow.answer

    # 8. Household Member Split
    res_hh = await engine.chat(_make_req("Kimin ne kadar ortak payı var?", ws_type="Household"))
    assert res_hh.intent == "HOUSEHOLD_SPLIT"
    assert "Ahmet" in res_hh.answer
    assert "Ayşe" in res_hh.answer

    # 9. Investment & Wealth Status
    res_inv = await engine.chat(_make_req("Yatırım portföyüm ne durumda, borsada ne kadarım var?"))
    assert res_inv.intent == "SAVINGS_TIP"
    assert "150.000,00" in res_inv.answer or "30.000,00" in res_inv.answer

    # 10. Help / Capabilities Guide
    res_help = await engine.chat(_make_req("Merhaba, ne yapabilirsin?"))
    assert "FinSense" in res_help.answer
    assert "1." in res_help.answer

    # Verify all answers are distinct and customized
    all_answers = {
        res_cat.answer,
        res_burn.answer,
        res_top.answer,
        res_alerts.answer,
        res_savings.answer,
        res_runway.answer,
        res_flow.answer,
        res_hh.answer,
        res_inv.answer,
        res_help.answer,
    }
    assert len(all_answers) == 10, "Every single calculator must produce a distinct deterministic response!"


@pytest.mark.asyncio
async def test_deterministic_engine_english_support() -> None:
    engine = DeterministicFinancialEngine()
    ctx_dict = _sample_financial_context()

    req_en = ChatRequestDto(
        question="Will I make it to month-end?",
        locale="en-US",
        workspace_type="Personal",
        financial_context=FinancialContextDto.model_validate(ctx_dict),
    )
    res_en = await engine.chat(req_en)
    assert res_en.is_finance_related is True
    assert "daily burn rate" in res_en.answer.lower() or "surplus" in res_en.answer.lower()
    assert res_en.provider_used == "deterministic-engine"


# ---------------------------------------------------------------------------
# 4. Receipt OCR Extraction & Turkish Thermal Parser
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_turuncu_market_receipt_parsing() -> None:
    engine = DeterministicFinancialEngine()
    turuncu_ocr = (
        "TURUNCU MARKET\n"
        "EMRE AYDIN\n"
        "GÖKTÜRK MAH. BAĞLAR CAD. NO:109/B\n"
        "ÇANKAYA/ANKARA\n"
        "CUMHURİYET V.D:638 1127 2134\n"
        "TEŞEKKÜR EDERİZ\n"
        "16/12/2017 FİŞ NO: 0001\n"
        "SAAT: 10:56\n"
        "EKMEK %1 *128,95\n"
        "KDV *1,28\n"
        "TOP *128,95\n"
        "KREDİ *128,95"
    )
    result = await engine.extract_receipt(
        image_bytes=None,
        mime_type=None,
        raw_ocr_text=turuncu_ocr,
        locale="tr-TR",
        available_categories=["Market", "Yeme & İçme", "Ulaşım", "Alışveriş"],
    )
    assert result.is_valid_receipt is True
    assert result.merchant_name == "Turuncu Market"
    assert result.transaction_date == "2017-12-16"
    assert result.tax_amount == Decimal("1.28")
    assert result.total_amount == Decimal("128.95")
    assert result.suggested_category == "Market"
    assert result.provider_used == "deterministic-engine"

    # Multi-line thermal receipt where TOP is on previous line
    multiline_ocr = turuncu_ocr.replace("TOP *128,95", "TOP\n*128,95")
    multiline_result = await engine.extract_receipt(
        image_bytes=None,
        mime_type=None,
        raw_ocr_text=multiline_ocr,
        locale="tr-TR",
        available_categories=["Market", "Yeme & İçme", "Ulaşım", "Alışveriş"],
    )
    assert multiline_result.total_amount == Decimal("128.95")
    assert multiline_result.tax_amount == Decimal("1.28")


@pytest.mark.asyncio
async def test_ocr_engine_invoked_for_image_bytes(monkeypatch: pytest.MonkeyPatch) -> None:
    import app.providers.deterministic_engine as engine_mod

    monkeypatch.setattr(
        engine_mod,
        "extract_text_from_image_bytes",
        lambda _bytes: "TURUNCU MARKET\n16/12/2017\nKDV *1,28\nTOP *128,95",
    )
    engine = DeterministicFinancialEngine()
    result = await engine.extract_receipt(
        image_bytes=_make_png_bytes(400, 600),
        mime_type="image/jpeg",
        raw_ocr_text=None,
        locale="tr-TR",
        available_categories=["Market", "Alışveriş"],
        filename="scan.jpg",
    )
    assert result.is_valid_receipt is True
    assert result.merchant_name == "Turuncu Market"
    assert result.total_amount == Decimal("128.95")
    assert result.tax_amount == Decimal("1.28")
    assert result.transaction_date == "2017-12-16"


def test_receipt_ocr_endpoint_resizes_oversized_image(
    client: TestClient,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured_dimensions: list[tuple[int, int]] = []

    class _SpyEngine(DeterministicFinancialEngine):
        async def extract_receipt(self, **kwargs):  # type: ignore[override]
            img_bytes = kwargs.get("image_bytes")
            assert img_bytes is not None
            with Image.open(io.BytesIO(img_bytes)) as processed:
                captured_dimensions.append(processed.size)
            return await super().extract_receipt(**kwargs)

    monkeypatch.setattr(AIProviderFactory, "create", lambda _settings=None: _SpyEngine())

    oversized_png = _make_png_bytes(width=2560, height=1920)
    resp = client.post(
        "/api/v1/internal/ocr/receipt",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        files={"file": ("migros_receipt.png", oversized_png, "image/png")},
        data={"locale": "tr-TR"},
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["is_valid_receipt"] is True
    assert captured_dimensions, "Expected preprocessed image to reach provider"
    w, h = captured_dimensions[0]
    assert max(w, h) <= 1280


def test_receipt_ocr_rejects_non_receipt_and_corrupt(client: TestClient) -> None:
    resp_non_receipt = client.post(
        "/api/v1/internal/ocr/receipt",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        json={
            "raw_ocr_text": "2x + 5 = 15 matematik ödev sorusu çözümü",
            "locale": "tr-TR",
        },
    )
    assert resp_non_receipt.status_code == 200
    assert resp_non_receipt.json()["is_valid_receipt"] is False

    resp_corrupt = client.post(
        "/api/v1/internal/ocr/receipt",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        files={"file": ("broken.jpg", b"not-an-image", "image/jpeg")},
    )
    assert resp_corrupt.status_code == 400


def test_receipt_ocr_rejects_oversized_payload_with_413(client: TestClient) -> None:
    oversized_bytes = b"0" * (10 * 1024 * 1024 + 1)

    resp_multipart = client.post(
        "/api/v1/internal/ocr/receipt",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        files={"file": ("huge.jpg", oversized_bytes, "image/jpeg")},
    )
    assert resp_multipart.status_code == 413

    oversized_b64 = base64.b64encode(oversized_bytes).decode("ascii")
    resp_json = client.post(
        "/api/v1/internal/ocr/receipt",
        headers={"X-Internal-Api-Key": TEST_INTERNAL_KEY},
        json={"image_base64": oversized_b64, "locale": "tr-TR"},
    )
    assert resp_json.status_code == 413


# ---------------------------------------------------------------------------
# 5. Health & Provider Factory Tests
# ---------------------------------------------------------------------------


def test_health_endpoint(client: TestClient) -> None:
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "healthy"
    assert data["engine"] == "deterministic"


def test_factory_returns_deterministic_engine() -> None:
    provider = AIProviderFactory.create()
    assert isinstance(provider, DeterministicFinancialEngine)
    assert isinstance(provider, SmartLocalProvider)
    assert provider.provider_name == "deterministic-engine"
