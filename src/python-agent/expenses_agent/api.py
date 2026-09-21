"""Chat transport and health endpoints for the expenses agent.

All business operations go through the agent and its MCP tools.
The separate .NET API serves all frontend reads, including private receipt photos.
"""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from typing import Annotated, Any

from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware

from .agent import Attachment, ExpensesAgent
from .config import Settings, load_settings
from .images import SUPPORTED_IMAGE_TYPES, validate_image
from .mcp_client import McpToolError
from .observability import configure, instrument_app, retry_after_seconds

logger = logging.getLogger(__name__)

MAX_UPLOAD_BYTES = 12 * 1024 * 1024


def create_app(settings: Settings | None = None, agent: ExpensesAgent | None = None) -> FastAPI:
    """Build the FastAPI application.

    An injected ``agent`` lets the caller own its lifecycle.
    """
    settings = settings or load_settings()
    configure(settings.service_name, enable_sensitive_data=settings.enable_sensitive_telemetry)

    owns_agent = agent is None

    @asynccontextmanager
    async def lifespan(application: FastAPI):
        instance = agent or ExpensesAgent(settings)
        application.state.agent = instance
        application.state.settings = settings
        try:
            if owns_agent:
                await instance.start()
            yield
        finally:
            if owns_agent:
                await instance.stop()

    app = FastAPI(
        title="Expenses Agent",
        version="0.1.0",
        description="Microsoft Agent Framework agent that turns receipt photos into expense records.",
        lifespan=lifespan,
    )

    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.allowed_origins,
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
        expose_headers=["Retry-After"],
    )

    def current_agent(request: Request) -> ExpensesAgent:
        return request.app.state.agent

    # ---- health ------------------------------------------------------

    @app.get("/health", tags=["ops"])
    async def health(request: Request) -> dict[str, Any]:
        instance = current_agent(request)
        return {
            "status": "healthy" if instance.is_ready else "degraded",
            "service": "expensesagent",
            "agentReady": instance.is_ready,
            "durableMemory": instance.memory.is_enabled,
            "durableMemoryError": instance.memory.error,
            "error": instance.startup_error,
            "configuration": request.app.state.settings.describe(),
        }

    @app.get("/alive", tags=["ops"])
    async def alive() -> dict[str, str]:
        return {"status": "alive"}

    # ---- chat --------------------------------------------------------

    @app.post("/chat", tags=["chat"])
    async def chat(
        userId: Annotated[str, Form(description="Identifier of the signed-in user.")],
        message: Annotated[str, Form(description="What the user typed.")] = "",
        conversationId: Annotated[str | None, Form(description="Conversation to continue.")] = None,
        images: Annotated[list[UploadFile] | None, File(description="Receipt photos.")] = None,
        request: Request = None,  # type: ignore[assignment]
    ) -> dict[str, Any]:
        if not userId.strip():
            raise HTTPException(status_code=400, detail="userId is required.")

        attachments: list[Attachment] = []
        for upload in images or []:
            filename = upload.filename or "receipt"
            content_type = (upload.content_type or "").split(";", 1)[0].strip().lower()
            if content_type not in SUPPORTED_IMAGE_TYPES:
                raise HTTPException(
                    status_code=415,
                    detail=f"'{filename}' must be a JPEG, PNG, GIF, WebP, BMP or TIFF image.",
                )
            data = await upload.read(MAX_UPLOAD_BYTES + 1)
            if not data:
                raise HTTPException(status_code=400, detail=f"'{filename}' is empty. Choose a receipt photo.")
            if len(data) > MAX_UPLOAD_BYTES:
                raise HTTPException(status_code=413, detail=f"'{filename}' is larger than 12 MiB.")
            try:
                validate_image(data, content_type)
            except ValueError as exc:
                raise HTTPException(status_code=400, detail=f"'{filename}': {exc}") from exc
            attachments.append(
                Attachment(
                    data=data,
                    content_type=content_type,
                    filename=filename,
                )
            )

        if not message.strip() and not attachments:
            raise HTTPException(status_code=400, detail="Send a message, a photo, or both.")

        instance = current_agent(request)
        if not instance.is_ready:
            raise HTTPException(status_code=503, detail=instance.startup_error or "The agent is not ready.")

        try:
            turn = await instance.chat(
                user_id=userId,
                message=message,
                conversation_id=(conversationId or None),
                attachments=attachments,
            )
        except McpToolError as exc:
            logger.exception("MCP tool failure during chat")
            raise HTTPException(status_code=502, detail=str(exc)) from exc
        except Exception as exc:  # noqa: BLE001 - surface a useful message to the UI
            logger.exception("Chat turn failed")
            detail = str(exc) or exc.__class__.__name__
            # Foundry model quota exhaustion is the most common failure in a demo
            # environment; make it obvious instead of a bare 500.
            if "429" in detail or "Too Many Requests" in detail or "rate limit" in detail.lower():
                raise HTTPException(
                    status_code=429,
                    detail="The Foundry model deployment is rate limited (HTTP 429). Wait a moment and try again, or raise the deployment quota.",
                    headers={"Retry-After": str(retry_after_seconds(exc) or 60)},
                ) from exc
            raise HTTPException(status_code=502, detail=f"The agent could not complete this turn: {detail}") from exc

        return turn.to_dict()

    instrument_app(app)
    return app
