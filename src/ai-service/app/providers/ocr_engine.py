"""Local RapidOCR engine wrapper with singleton caching and line reconstruction."""

from functools import lru_cache
import io
import logging
from typing import Any

from PIL import Image

logger = logging.getLogger(__name__)


@lru_cache(maxsize=1)
def _get_ocr_engine() -> Any:
    """Create and cache a singleton RapidOCR instance."""
    from rapidocr_onnxruntime import RapidOCR

    return RapidOCR()


def _reconstruct_lines(ocr_results: list[Any]) -> str:
    """Group bounding boxes on the same visual row left-to-right and emit top-to-bottom lines."""
    items: list[tuple[float, float, float, str]] = []
    for entry in ocr_results:
        if not isinstance(entry, (list, tuple)) or len(entry) < 2:
            continue
        box, text = entry[0], str(entry[1]).strip()
        if not text:
            continue
        try:
            ys = [float(pt[1]) for pt in box]
            xs = [float(pt[0]) for pt in box]
            center_y = (min(ys) + max(ys)) / 2.0
            height = max(max(ys) - min(ys), 8.0)
            left_x = min(xs)
        except (TypeError, ValueError, IndexError):
            center_y, height, left_x = 0.0, 12.0, 0.0
        items.append((center_y, left_x, height, text))

    if not items:
        return ""

    items.sort(key=lambda item: (item[0], item[1]))

    lines: list[list[tuple[float, float, float, str]]] = []
    for item in items:
        cy, _, h, _ = item
        if not lines:
            lines.append([item])
            continue
        last_line = lines[-1]
        avg_y = sum(x[0] for x in last_line) / len(last_line)
        avg_h = sum(x[2] for x in last_line) / len(last_line)
        if abs(cy - avg_y) <= max(avg_h, h) * 0.6:
            last_line.append(item)
        else:
            lines.append([item])

    reconstructed: list[str] = []
    for line in lines:
        line.sort(key=lambda item: item[1])
        reconstructed.append(" ".join(item[3] for item in line))
    return "\n".join(reconstructed)


def extract_text_from_image_bytes(image_bytes: bytes) -> str:
    """Run local RapidOCR on image bytes and return reconstructed text lines."""
    if not image_bytes:
        return ""

    try:
        import numpy as np

        with Image.open(io.BytesIO(image_bytes)) as img:
            rgb_img = img.convert("RGB")
            img_array = np.array(rgb_img)

        engine = _get_ocr_engine()
        result, _ = engine(img_array)
        if not result:
            return ""
        return _reconstruct_lines(result)
    except Exception as exc:  # noqa: BLE001 - graceful fallback when OCR fails or is unavailable
        logger.warning("Local RapidOCR extraction failed: %s", exc)
        return ""
