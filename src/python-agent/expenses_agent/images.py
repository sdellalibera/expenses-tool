"""Validate original uploads and prepare CU-compatible analysis copies."""

from __future__ import annotations

from io import BytesIO
import warnings

from PIL import Image, UnidentifiedImageError

SUPPORTED_IMAGE_TYPES = {
    "image/jpeg": "JPEG",
    "image/png": "PNG",
    "image/gif": "GIF",
    "image/webp": "WEBP",
    "image/bmp": "BMP",
    "image/tiff": "TIFF",
}


def validate_image(data: bytes, content_type: str) -> None:
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(BytesIO(data)) as image:
                if image.format != SUPPORTED_IMAGE_TYPES.get(content_type):
                    raise ValueError("The image contents do not match the declared image type.")
                image.verify()
    except (UnidentifiedImageError, OSError, SyntaxError) as exc:
        raise ValueError("The file is not a readable receipt image.") from exc
    except (Image.DecompressionBombError, Image.DecompressionBombWarning) as exc:
        raise ValueError("The image dimensions are too large. Resize the receipt photo and retry.") from exc


def analysis_image(data: bytes, content_type: str) -> tuple[bytes, str]:
    """CU's document analyzer accepts neither GIF nor WebP; retain originals in Blob."""
    if content_type not in {"image/gif", "image/webp"}:
        return data, content_type
    with Image.open(BytesIO(data)) as image:
        # A receipt is a still image; analyze the first frame of animated uploads.
        with BytesIO() as output:
            image.convert("RGB").save(output, format="PNG")
            return output.getvalue(), "image/png"
