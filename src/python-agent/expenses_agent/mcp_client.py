"""Typed client for the C# MCP server.

Everything the agent stores or reads in Cosmos DB for **records** goes through
the MCP server — the agent never opens a database connection for trips or
expenses. This module wraps the raw MCP ``call_tool`` plumbing in small typed
helpers used by:

* the REST endpoints the React frontend calls (trips / expenses / conversations),
* the Cosmos backed conversation history provider.

The agent's *own* tool calling uses :class:`agent_framework.MCPStreamableHTTPTool`
against the same server, so both paths hit identical tools.

A fresh MCP session is opened per call. The C# server runs its HTTP transport in
stateless mode, so this costs one extra round trip and avoids holding an
``anyio`` cancel scope across FastAPI's startup and shutdown tasks.
"""

from __future__ import annotations

import json
import logging
from typing import Any

import httpx
from mcp import ClientSession
from mcp.types import CallToolResult

try:  # mcp >= 1.29 renamed the transport helper.
    from mcp.client.streamable_http import streamable_http_client
except ImportError:  # pragma: no cover - older mcp releases
    from mcp.client.streamable_http import streamablehttp_client as streamable_http_client

from .observability import tracer

logger = logging.getLogger(__name__)


class McpToolError(RuntimeError):
    """Raised when an MCP tool call fails."""


class _Session:
    """One short-lived MCP session over streamable HTTP."""

    def __init__(self, endpoint: str, timeout: float) -> None:
        self._endpoint = endpoint
        self._timeout = timeout

    async def __aenter__(self) -> ClientSession:
        # `streamable_http_client` takes its timeout from the httpx client.
        self._http = httpx.AsyncClient(timeout=self._timeout)
        self._transport = streamable_http_client(self._endpoint, http_client=self._http)
        read, write, _ = await self._transport.__aenter__()
        self._session = ClientSession(read, write)
        session = await self._session.__aenter__()
        await session.initialize()
        return session

    async def __aexit__(self, exc_type, exc, tb) -> None:
        try:
            await self._session.__aexit__(exc_type, exc, tb)
        finally:
            try:
                await self._transport.__aexit__(exc_type, exc, tb)
            finally:
                await self._http.aclose()


class ExpensesMcpClient:
    """Small MCP client for the expenses MCP server."""

    def __init__(self, endpoint: str, *, timeout: float = 30.0) -> None:
        self._endpoint = endpoint
        self._timeout = timeout

    @property
    def endpoint(self) -> str:
        return self._endpoint

    async def __aenter__(self) -> "ExpensesMcpClient":
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        """No persistent connection is held; kept for symmetry with callers."""
        return None

    async def list_tool_names(self) -> list[str]:
        async with _Session(self._endpoint, self._timeout) as session:
            result = await session.list_tools()
            return [tool.name for tool in result.tools]

    async def call(self, name: str, arguments: dict[str, Any] | None = None) -> Any:
        """Call an MCP tool and return its structured result."""
        arguments = {k: v for k, v in (arguments or {}).items() if v is not None}

        with tracer().start_as_current_span(f"mcp.call_tool {name}") as span:
            span.set_attribute("mcp.tool.name", name)
            span.set_attribute("mcp.server.url", self._endpoint)

            async with _Session(self._endpoint, self._timeout) as session:
                result = await session.call_tool(name, arguments)

            if result.isError:
                message = _result_text(result) or f"MCP tool '{name}' failed"
                span.set_attribute("mcp.tool.error", message)
                logger.error("MCP tool '%s' returned an error: %s", name, message)
                raise McpToolError(message)

            span.set_attribute("mcp.tool.success", True)
            return _unwrap(result)

    # ---- Trips -------------------------------------------------------

    async def list_trips(self, user_id: str, status: str | None = None) -> list[dict[str, Any]]:
        return _as_list(await self.call("list_trips", {"userId": user_id, "status": status}))

    async def get_trip(self, user_id: str, trip_id: str) -> dict[str, Any] | None:
        return await self.call("get_trip", {"userId": user_id, "tripId": trip_id})

    async def create_trip(self, user_id: str, name: str, **kwargs: Any) -> dict[str, Any]:
        return await self.call("create_trip", {"userId": user_id, "name": name, **kwargs})

    async def delete_trip(self, user_id: str, trip_id: str) -> bool:
        return bool(await self.call("delete_trip", {"userId": user_id, "tripId": trip_id}))

    # ---- Expenses ----------------------------------------------------

    async def list_expenses(self, user_id: str, trip_id: str | None = None) -> list[dict[str, Any]]:
        return _as_list(await self.call("list_expenses", {"userId": user_id, "tripId": trip_id}))

    async def get_expense(self, user_id: str, expense_id: str) -> dict[str, Any] | None:
        return await self.call("get_expense", {"userId": user_id, "expenseId": expense_id})

    async def delete_expense(self, user_id: str, expense_id: str) -> bool:
        return bool(await self.call("delete_expense", {"userId": user_id, "expenseId": expense_id}))

    # ---- Conversations ----------------------------------------------

    async def list_conversations(self, user_id: str) -> list[dict[str, Any]]:
        return _as_list(await self.call("list_conversations", {"userId": user_id}))

    async def get_conversation(self, user_id: str, conversation_id: str) -> dict[str, Any] | None:
        return await self.call("get_conversation", {"userId": user_id, "conversationId": conversation_id})

    async def append_conversation_messages(
        self,
        user_id: str,
        conversation_id: str,
        messages: list[dict[str, Any]],
        *,
        title: str | None = None,
        trip_id: str | None = None,
    ) -> dict[str, Any]:
        return await self.call(
            "append_conversation_messages",
            {
                "userId": user_id,
                "conversationId": conversation_id,
                "messages": messages,
                "title": title,
                "tripId": trip_id,
            },
        )


def _result_text(result: CallToolResult) -> str:
    parts = [getattr(block, "text", None) for block in (result.content or [])]
    return "\n".join(p for p in parts if p)


def _unwrap(result: CallToolResult) -> Any:
    """Prefer the structured payload, falling back to parsing the text block."""
    structured = getattr(result, "structuredContent", None)
    if structured is None:
        structured = getattr(result, "structured_content", None)

    if structured is not None:
        # The C# server wraps non-object returns (bool, list) in {"result": ...}.
        if isinstance(structured, dict) and set(structured.keys()) == {"result"}:
            return structured["result"]
        return structured

    text = _result_text(result)
    if not text:
        return None
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return text


def _as_list(value: Any) -> list[dict[str, Any]]:
    if value is None:
        return []
    if isinstance(value, list):
        return value
    if isinstance(value, dict):
        return [value]
    return []
