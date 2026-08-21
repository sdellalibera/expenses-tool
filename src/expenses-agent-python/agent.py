"""Expenses agent — invoice image ingestion.

FastAPI service (run with uvicorn) that receives an invoice **image** uploaded
by the React frontend, runs Azure AI Content Understanding with the
``prebuilt-invoice`` analyzer, and renders the result to a clean YAML block
using ``azure.ai.contentunderstanding.to_llm_input``.

The YAML output (front-matter fields + markdown body) is the payload this
agent will later hand off to a downstream agent via A2A. A2A wiring is
intentionally out of scope here — the HTTP endpoint returns the YAML so the
caller (or a future A2A transport) can forward it.

Endpoints:
    POST /expenses/report   multipart/form-data field ``image`` — invoice image
    GET  /health            liveness probe

Environment variables:
    AZURE_CONTENTUNDERSTANDING_ENDPOINT  — Content Understanding endpoint URL

Authentication uses ``DefaultAzureCredential`` (``az login`` locally, managed
identity when deployed).
"""

from __future__ import annotations

import logging
import mimetypes
import os
from contextlib import asynccontextmanager

from azure.ai.contentunderstanding import to_llm_input
from azure.ai.contentunderstanding.aio import ContentUnderstandingClient
from azure.identity.aio import DefaultAzureCredential
from dotenv import load_dotenv
from fastapi import FastAPI, File, Form, HTTPException, Response, UploadFile

load_dotenv()

logger = logging.getLogger("expenses_agent")


# --- Configuration ---------------------------------------------------------

# MIME types accepted by CU's prebuilt-invoice analyzer for image input.
_SUPPORTED_IMAGE_TYPES: frozenset[str] = frozenset(
    {"image/jpeg", "image/png", "image/tiff", "image/bmp"}
)

_INVOICE_ANALYZER_ID = "prebuilt-invoice"


def _require_env(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        raise RuntimeError(f"Required environment variable '{name}' is not set.")
    return value


def _resolve_image_media_type(content_type: str | None, filename: str | None) -> str:
    mt = (content_type or "").split(";", 1)[0].strip().lower() or None
    if mt not in _SUPPORTED_IMAGE_TYPES and filename:
        guessed, _ = mimetypes.guess_type(filename)
        mt = guessed
    if mt not in _SUPPORTED_IMAGE_TYPES:
        raise HTTPException(
            status_code=415,
            detail=(
                f"Unsupported invoice image type '{content_type}'. "
                f"Supported: {sorted(_SUPPORTED_IMAGE_TYPES)}"
            ),
        )
    return mt


# --- FastAPI app -----------------------------------------------------------


@asynccontextmanager
async def _lifespan(app: FastAPI):
    endpoint = _require_env("AZURE_CONTENTUNDERSTANDING_ENDPOINT")
    credential = DefaultAzureCredential()
    client = ContentUnderstandingClient(endpoint, credential)

    app.state.credential = credential
    app.state.cu_client = client
    logger.info("Expenses agent ready (CU=%s)", endpoint)
    try:
        yield
    finally:
        await client.close()
        await credential.close()


app = FastAPI(title="Expenses Agent (Python)", lifespan=_lifespan)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/expenses/report")
async def create_expense_report(
    image: UploadFile = File(..., description="Invoice image (JPEG/PNG/TIFF/BMP)"),
    note: str | None = Form(default=None),
    conversationId: str | None = Form(default=None),  # noqa: N803 — matches frontend contract
) -> Response:
    """Receive an invoice image from the frontend and return CU YAML output.

    The response body is the YAML block produced by
    ``azure.ai.contentunderstanding.to_llm_input`` — front-matter with source
    metadata plus the ``fields`` section extracted by ``prebuilt-invoice``.
    This is the exact payload the downstream agent will consume once A2A is
    wired up.
    """
    media_type = _resolve_image_media_type(image.content_type, image.filename)
    data = await image.read()
    if not data:
        raise HTTPException(status_code=400, detail="Uploaded image is empty.")

    filename = image.filename or f"invoice{mimetypes.guess_extension(media_type) or ''}"
    client: ContentUnderstandingClient = app.state.cu_client

    try:
        poller = await client.begin_analyze_binary(
            _INVOICE_ANALYZER_ID,
            binary_input=data,
            content_type=media_type,
        )
        result = await poller.result()
    except Exception:
        logger.exception("Content Understanding analysis failed for %s", filename)
        raise HTTPException(status_code=502, detail="Invoice analysis failed.")

    # Fields only — prebuilt-invoice's structured fields are all we need for
    # the downstream expense agent. Skip markdown to keep the payload lean.
    yaml_output = to_llm_input(
        result,
        include_markdown=False,
        include_fields=True,
        custom_metadata={
            "source": filename,
            "analyzer_id": _INVOICE_ANALYZER_ID,
            **({"note": note} if note else {}),
            **({"conversation_id": conversationId} if conversationId else {}),
        },
    )

    logger.info(
        "Extracted invoice fields for %s (%d bytes YAML, conversationId=%s)",
        filename,
        len(yaml_output),
        conversationId,
    )
    return Response(content=yaml_output, media_type="application/yaml")


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(
        "agent:app",
        host="0.0.0.0",
        port=int(os.environ.get("PORT", "8000")),
    )
