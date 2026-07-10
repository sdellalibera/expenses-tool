"""FastAPI host exposing the expenses agent.

Mirrors the existing C# agent's surface so the current frontend keeps working:
  * ``POST /chat``              — JSON ``{ message, conversationId? }``
  * ``POST /expenses/report``  — multipart form with ``image`` (+ optional ``note``,
                                 ``conversationId``)
"""

from __future__ import annotations

import uuid
from contextlib import asynccontextmanager
from collections.abc import AsyncIterator

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from .agent import ExpensesAgent, build_expenses_agent


class ChatRequest(BaseModel):
    message: str
    conversationId: str | None = None


def _conversation_id(value: str | None) -> str:
    return value or str(uuid.uuid4())


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    async with build_expenses_agent() as agent:
        app.state.agent = agent
        yield


def create_app() -> FastAPI:
    app = FastAPI(title="Expenses Agent (Python)", lifespan=_lifespan)

    # CORS is open for the PoC so the phone frontend can call the agent directly.
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_methods=["*"],
        allow_headers=["*"],
    )

    @app.get("/health")
    async def health() -> dict[str, str]:
        return {"status": "ok"}

    @app.post("/chat")
    async def chat(request: ChatRequest) -> JSONResponse:
        agent: ExpensesAgent = app.state.agent
        conversation_id = _conversation_id(request.conversationId)
        reply = await agent.chat(request.message, conversation_id)
        return JSONResponse({"conversationId": conversation_id, "reply": reply})

    @app.post("/expenses/report")
    async def report(
        image: UploadFile = File(...),
        note: str | None = Form(default=None),
        conversationId: str | None = Form(default=None),
    ) -> JSONResponse:
        agent: ExpensesAgent = app.state.agent
        conversation_id = _conversation_id(conversationId)

        image_bytes = await image.read()
        if not image_bytes:
            return JSONResponse(status_code=400, content={"error": "Empty 'image' upload."})

        media_type = image.content_type or "application/octet-stream"
        summary = await agent.analyze_receipt(
            image_bytes=image_bytes,
            media_type=media_type,
            conversation_id=conversation_id,
            note=note,
        )
        return JSONResponse({"conversationId": conversation_id, "analysisSummary": summary})

    return app


app = create_app()
