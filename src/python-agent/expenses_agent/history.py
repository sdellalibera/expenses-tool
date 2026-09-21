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

import tiktoken
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
        max_tokens: int = 4096,
        source_id: str = "cosmos_conversation_history",
    ) -> None:
        super().__init__(source_id)
        self._client = client
        self._user_id = user_id
        self._max_messages = max_messages
        self._max_tokens = max_tokens
        self._encoding = tiktoken.get_encoding("o200k_base")

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
            eligible = [item for item in stored if item.get("role") in _REPLAYED_ROLES and _text_of(item)]
            latest_receipt = next((index for index in range(len(eligible) - 1, -1, -1) if eligible[index].get("receiptContext")), None)
            turns: list[list[Message]] = []
            for index, item in enumerate(eligible):
                if item["role"] == "user":
                    turns.append([])
                if not turns:
                    continue
                text = _text_of(item) if index == latest_receipt else item.get("text", "")
                if text:
                    turns[-1].append(Message(role=item["role"], contents=[text]))
            replayed: list[Message] = []
            tokens = 0
            for turn in reversed(turns):
                turn_tokens = sum(len(self._encoding.encode(message.text or "", disallowed_special=())) + 6 for message in turn)
                if tokens + turn_tokens > self._max_tokens or len(replayed) + len(turn) > self._max_messages:
                    if not replayed and turn:
                        raise ValueError("The latest conversation turn exceeds MAX_HISTORY_TOKENS; increase the budget or start a new conversation.")
                    break
                replayed = turn + replayed
                tokens += turn_tokens

            span.set_attribute("expenses.history_count", len(replayed))
            span.set_attribute("expenses.history_tokens_estimated", tokens)
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
