"""Conversation history persisted in Cosmos DB through the MCP server.

Agent Framework calls :meth:`get_messages` before every run and
:meth:`save_messages` after it, which is exactly where the demo persists the
chat transcript. Only user/assistant text turns are replayed into the model:
tool calls are recorded for the audit trail (and shown in the UI) but are not
re-sent, so a reloaded conversation can never contain a dangling tool call.
"""

from __future__ import annotations

import logging
from typing import Any, Sequence

from agent_framework import HistoryProvider, Message

from .mcp_client import ExpensesMcpClient
from .observability import tracer

logger = logging.getLogger(__name__)

_REPLAYED_ROLES = {"user", "assistant"}


class McpConversationHistoryProvider(HistoryProvider):
    """History provider backed by the ``conversations`` Cosmos container."""

    def __init__(
        self,
        client: ExpensesMcpClient,
        user_id: str,
        *,
        max_messages: int = 40,
        source_id: str = "cosmos_conversation_history",
    ) -> None:
        super().__init__(source_id)
        self._client = client
        self._user_id = user_id
        self._max_messages = max_messages

    def resolve_session(self, session_id: str | None) -> tuple[str | None, str | None]:
        return self._user_id, session_id

    async def get_messages(
        self,
        session_id: str | None,
        *,
        state: dict[str, Any] | None = None,
        **kwargs: Any,
    ) -> list[Message]:
        user_id, conversation_id = self.resolve_session(session_id)
        if not user_id or not conversation_id:
            return []

        with tracer().start_as_current_span("conversation.load") as span:
            span.set_attribute("expenses.user_id", user_id)
            span.set_attribute("expenses.conversation_id", conversation_id)

            conversation = await self._client.get_conversation(user_id, conversation_id)
            if not conversation:
                span.set_attribute("expenses.history_count", 0)
                return []

            stored = conversation.get("messages") or []
            replayed = [
                Message(role=item.get("role", "user"), contents=[_text_of(item)])
                for item in stored[-self._max_messages :]
                if item.get("role") in _REPLAYED_ROLES and _text_of(item)
            ]

            span.set_attribute("expenses.history_count", len(replayed))
            logger.info(
                "Loaded %d history message(s) for conversation %s", len(replayed), session_id
            )
            return replayed

    async def save_messages(
        self,
        session_id: str | None,
        messages: Sequence[Message],
        *,
        state: dict[str, Any] | None = None,
        **kwargs: Any,
    ) -> None:
        user_id, conversation_id = self.resolve_session(session_id)
        if not user_id or not conversation_id:
            return

        payload = self._to_payload(messages)
        if not payload:
            return

        with tracer().start_as_current_span("conversation.save") as span:
            span.set_attribute("expenses.user_id", user_id)
            span.set_attribute("expenses.conversation_id", conversation_id)
            span.set_attribute("expenses.message_count", len(payload))

            await self._client.append_conversation_messages(user_id, conversation_id, payload)
            logger.info("Persisted %d message(s) for conversation %s", len(payload), session_id)

    def _to_payload(self, messages: Sequence[Message]) -> list[dict[str, Any]]:
        payload: list[dict[str, Any]] = []
        for message in messages:
            role = str(getattr(message.role, "value", message.role) or "user")
            if role not in _REPLAYED_ROLES:
                continue

            text = (message.text or "").strip()
            calls = collect_tool_calls(message)
            properties = message.additional_properties or {}
            if not text and not calls:
                continue

            payload.append(
                {
                    "role": role,
                    "text": properties.get("displayText", text),
                    "receiptContext": properties.get("receiptContext") if role == "user" else None,
                    "toolCalls": calls or properties.get("toolCalls", []),
                    "attachments": properties.get("attachments", []) if role == "user" else [],
                }
            )

        return payload


def _text_of(item: dict[str, Any]) -> str:
    return "\n\n".join(
        value.strip() for value in (item.get("text"), item.get("receiptContext")) if value and value.strip()
    )


def collect_tool_calls(message: Message) -> list[str]:
    """Names of the functions/tools invoked inside a message, if any."""
    names: list[str] = []
    for content in getattr(message, "contents", None) or []:
        name = getattr(content, "name", None)
        if name and getattr(content, "type", "") in {"function_call", "mcp_server_tool_call"}:
            names.append(str(name))
    return names
