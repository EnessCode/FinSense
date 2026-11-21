"""Internal Receipt OCR endpoint supporting multipart upload and JSON base64/text."""

import base64
import binascii
import io
import json
from typing import Optional

from fastapi import APIRouter, Depends, HTTPException, Request, status
from PIL import Image, ImageOps, UnidentifiedImageError
from pydantic import ValidationError
from starlette.datastructures import UploadFile

from app.config import Settings, get_settings
from app.providers.factory import AIProviderFactory
from app.schemas.receipt import (
    STANDARD_RECEIPT_CATEGORIES,
    ReceiptExtractionResponse,
    ReceiptOcrRequest,
)
from app.security.auth import verify_internal_api_key

router = APIRouter(
    prefix="/api/v1/internal/ocr",
    tags=["Internal Receipt OCR"],
    dependencies=[Depends(verify_internal_api_key)],
)

_ALLOWED_IMAGE_FORMATS = {"JPEG", "PNG", "WEBP", "BMP", "GIF"}
MAX_RECEIPT_FILE_SIZE_BYTES = 10 * 1024 * 1024  # 10 MB (matches ASP.NET Core AiController)


def _enforce_max_upload_size(num_bytes: int) -> None:
    """Reject receipt payloads larger than 10 MB with HTTP 413 Payload Too Large."""
    if num_bytes > MAX_RECEIPT_FILE_SIZE_BYTES:
        raise HTTPException(
            status_code=status.HTTP_413_CONTENT_TOO_LARGE,
            detail="Uploaded receipt image exceeds the maximum allowed size of 10 MB.",
        )


def preprocess_receipt_image(
    image_bytes: bytes,
    max_dimension: int = 1280,
) -> tuple[bytes, str]:
    """Validate image format, strip EXIF via re-encoding, and resize longest side <= 1280px."""
    if not image_bytes:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Uploaded image file is empty.",
        )

    try:
        with Image.open(io.BytesIO(image_bytes)) as probe:
            img_format = (probe.format or "").upper()
            if img_format not in _ALLOWED_IMAGE_FORMATS:
                raise HTTPException(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    detail=f"Unsupported image format: {img_format or 'unknown'}.",
                )
            probe.verify()

        with Image.open(io.BytesIO(image_bytes)) as img:
            img = ImageOps.exif_transpose(img)
            if max(img.width, img.height) > max_dimension:
                img.thumbnail((max_dimension, max_dimension), Image.Resampling.LANCZOS)

            if img.mode in ("RGBA", "LA", "P"):
                background = Image.new("RGB", img.size, (255, 255, 255))
                rgba_img = img.convert("RGBA")
                background.paste(rgba_img, mask=rgba_img.split()[3])
                output_img = background
            elif img.mode != "RGB":
                output_img = img.convert("RGB")
            else:
                output_img = img

            out_buffer = io.BytesIO()
            output_img.save(out_buffer, format="JPEG", quality=88, optimize=True)
            return out_buffer.getvalue(), "image/jpeg"
    except HTTPException:
        raise
    except (UnidentifiedImageError, Image.DecompressionBombError, OSError, ValueError) as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Invalid or corrupted receipt image: {exc}",
        ) from exc


def _decode_base64_image(raw_b64: str) -> bytes:
    payload = raw_b64.strip()
    if payload.startswith("data:") and "," in payload:
        payload = payload.split(",", 1)[1]
    payload = "".join(payload.split())
    # Fast pre-check before allocating decoded bytes
    max_b64_chars = ((MAX_RECEIPT_FILE_SIZE_BYTES + 2) // 3) * 4
    if len(payload) > max_b64_chars:
        _enforce_max_upload_size(MAX_RECEIPT_FILE_SIZE_BYTES + 1)
    try:
        decoded = base64.b64decode(payload, validate=True)
    except (binascii.Error, ValueError) as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid base64-encoded image data.",
        ) from exc
    _enforce_max_upload_size(len(decoded))
    return decoded


def _parse_categories_form(values: list[object]) -> list[str]:
    categories: list[str] = []
    for item in values:
        if not isinstance(item, str):
            continue
        stripped = item.strip()
        if not stripped:
            continue
        if stripped.startswith("["):
            try:
                parsed = json.loads(stripped)
                if isinstance(parsed, list):
                    categories.extend(str(c).strip() for c in parsed if str(c).strip())
                    continue
            except json.JSONDecodeError:
                pass
        categories.extend(part.strip() for part in stripped.split(",") if part.strip())
    return categories or list(STANDARD_RECEIPT_CATEGORIES)


@router.post("/receipt", response_model=ReceiptExtractionResponse)
async def extract_receipt(
    request: Request,
    settings: Settings = Depends(get_settings),
) -> ReceiptExtractionResponse:
    """Extract structured receipt fields from a multipart image or JSON base64/OCR text payload."""
    content_type = (request.headers.get("content-type") or "").lower()

    image_bytes: Optional[bytes] = None
    mime_type: Optional[str] = None
    raw_ocr_text: Optional[str] = None
    locale = "tr-TR"
    available_categories: list[str] = list(STANDARD_RECEIPT_CATEGORIES)
    filename: Optional[str] = None

    if "application/json" in content_type:
        try:
            body = await request.json()
            dto = ReceiptOcrRequest.model_validate(body)
        except (ValueError, ValidationError) as exc:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
                detail=f"Invalid JSON receipt payload: {exc}",
            ) from exc

        locale = dto.locale
        available_categories = dto.available_categories or list(STANDARD_RECEIPT_CATEGORIES)
        raw_ocr_text = dto.raw_ocr_text
        if dto.image_base64:
            raw_bytes = _decode_base64_image(dto.image_base64)
            image_bytes, mime_type = preprocess_receipt_image(
                raw_bytes, max_dimension=settings.max_image_dimension
            )

    elif "multipart/form-data" in content_type:
        form = await request.form()
        uploaded = form.get("file")
        if isinstance(uploaded, UploadFile):
            filename = uploaded.filename
            raw_bytes = await uploaded.read(MAX_RECEIPT_FILE_SIZE_BYTES + 1)
            _enforce_max_upload_size(len(raw_bytes))
            image_bytes, mime_type = preprocess_receipt_image(
                raw_bytes, max_dimension=settings.max_image_dimension
            )

        locale_val = form.get("locale")
        if isinstance(locale_val, str) and locale_val.strip():
            locale = locale_val.strip()

        raw_text_val = form.get("raw_ocr_text")
        if isinstance(raw_text_val, str) and raw_text_val.strip():
            raw_ocr_text = raw_text_val.strip()

        available_categories = _parse_categories_form(form.getlist("available_categories"))

    else:
        raise HTTPException(
            status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE,
            detail="Content-Type must be multipart/form-data or application/json.",
        )

    if image_bytes is None and not raw_ocr_text:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Provide a receipt image ('file' or 'image_base64') or 'raw_ocr_text'.",
        )

    provider = AIProviderFactory.create(settings)
    return await provider.extract_receipt(
        image_bytes=image_bytes,
        mime_type=mime_type,
        raw_ocr_text=raw_ocr_text,
        locale=locale,
        available_categories=available_categories,
        filename=filename,
    )
