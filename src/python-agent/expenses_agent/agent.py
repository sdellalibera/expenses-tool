"""The expenses agent, built on the Microsoft Agent Framework.

Pipeline of a single chat turn:

1. The React frontend posts text plus zero or more receipt photos to ``/chat``.
2. :class:`~agent_framework.foundry.ContentUnderstandingContextProvider` analyses
   every photo with the ``ExpensesAnalyzer`` Content Understanding analyzer. The
   raw analysis is stripped down with ``azure.ai.contentunderstanding.to_llm_input``
   (fields only, no page markdown) before it reaches the model context.
3. The model turns that YAML into JSON with the ``TranslateYAMLToJSON`` tool and
   then calls the MCP server's CRUD tools to create the trip / expense records.
4. :class:`~expenses_agent.history.McpConversationHistoryProvider` persists the
   transcript in Cosmos DB, again through the MCP server.
"""

from __future__ import annotations

import logging
import uuid
from dataclasses import dataclass, field
from typing import Any

from agent_framework import Agent, Content, MCPStreamableHTTPTool, Message
from agent_framework._agents import AgentSession
from agent_framework.foundry import ContentUnderstandingContextProvider, FoundryChatClient
from azure.identity.aio import DefaultAzureCredential

from .config import Settings
from .history import McpConversationHistoryProvider, collect_tool_calls
from .mcp_client import ExpensesMcpClient
from .memory import CosmosMemory
from .observability import tracer
from .tools.parser_tool import translateYAMLtoJSON

logger = logging.getLogger(__name__)

SESSION_SEPARATOR = "::"

INSTRUCTIONS = """You are the expenses assistant of a business-travel expenses app.

Your job is to keep a user's work trips and the expenses booked on them tidy in the
database. Every expense MUST belong to a trip: that is what keeps a Munich trip
separate from a Seattle one.

## Tools
You manage records exclusively through the MCP tools (`create_trip`, `list_trips`,
`get_trip`, `find_trip_by_name`, `update_trip`, `delete_trip`, `create_expense`,
`list_expenses`, `get_expense`, `update_expense`, `delete_expense`). Never invent
identifiers: always read them back from a tool result.

Every tool takes a `userId`. Always pass the user id given to you in the context
message of the current turn.

## Receipts
When the user attaches a photo, an Azure AI Content Understanding analyzer runs on
it automatically. Its result is stripped down with `to_llm_input` to just the
extracted fields, and injected as a YAML front-matter block (merchant name,
category, total amount, line items and confidence scores). Use the
`TranslateYAMLToJSON` tool to turn that YAML into JSON before you read it, then:

1. Work out which trip the receipt belongs to.
   - If the user named a trip, resolve it with `find_trip_by_name` or `list_trips`.
   - If exactly one trip is open, use it.
   - Otherwise ask the user which trip to use, or offer to create one.
2. Call `create_expense` once per receipt with the merchant, category, date, total
   amount, currency and the line items (description, category, price, quantity).
   Use the receipt's own date when present; otherwise use today's date.
3. Confirm to the user in one or two short sentences what you stored, including the
   merchant, the total and the trip name.

Never create an expense from a photo without also storing it via `create_expense`.
If the analyzer produced no usable data, say so and ask for a clearer photo.

## Style
Be concise and friendly. Use the user's currency. Never show raw JSON or YAML unless
the user explicitly asks for it. When you are missing a required detail, ask one
focused question instead of guessing.
"""


@dataclass(slots=True)
class ChatTurn:
    """Result of one chat turn, shaped for the frontend."""

    conversation_id: str
    user_id: str
    reply: str
    tool_calls: list[str] = field(default_factory=list)
    attachments: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "conversationId": self.conversation_id,
            "userId": self.user_id,
            "reply": self.reply,
            "toolCalls": self.tool_calls,
            "attachments": self.attachments,
        }


@dataclass(slots=True)
class Attachment:
    """One uploaded receipt photo."""

    data: bytes
    content_type: str
    filename: str


def build_session_id(user_id: str, conversation_id: str) -> str:
    return f"{user_id}{SESSION_SEPARATOR}{conversation_id}"


def split_session_id(session_id: str | None) -> tuple[str | None, str | None]:
    if not session_id or SESSION_SEPARATOR not in session_id:
        return None, session_id
    user_id, _, conversation_id = session_id.partition(SESSION_SEPARATOR)
    return user_id, conversation_id


class SessionScopedHistoryProvider(McpConversationHistoryProvider):
    """History provider that derives the user from the session id.

    A single :class:`~agent_framework.Agent` instance serves every user, so the
    provider cannot be bound to one user at construction time. The session id
    carries ``<userId>::<conversationId>``.
    """

    def __init__(self, client: ExpensesMcpClient, *, max_messages: int = 40) -> None:
        super().__init__(client, user_id="", max_messages=max_messages)

    async def get_messages(self, session_id: str | None, *, state=None, **kwargs):  # type: ignore[override]
        user_id, conversation_id = split_session_id(session_id)
        if not user_id or not conversation_id:
            return []
        self._user_id = user_id  # type: ignore[attr-defined]
        return await super().get_messages(conversation_id, state=state, **kwargs)

    async def save_messages(self, session_id: str | None, messages, *, state=None, **kwargs):  # type: ignore[override]
        user_id, conversation_id = split_session_id(session_id)
        if not user_id or not conversation_id:
            return
        self._user_id = user_id  # type: ignore[attr-defined]
        await super().save_messages(conversation_id, messages, state=state, **kwargs)


class ExpensesAgent:
    """Owns the Agent Framework agent and its dependencies."""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._credential: DefaultAzureCredential | None = None
        self._agent: Agent | None = None
        self._cu: ContentUnderstandingContextProvider | None = None
        self._mcp_tool: MCPStreamableHTTPTool | None = None
        self._history: SessionScopedHistoryProvider | None = None
        self.memory = CosmosMemory()
        self.mcp: ExpensesMcpClient | None = None
        self.startup_error: str | None = None

    # ---- lifecycle ---------------------------------------------------

    async def __aenter__(self) -> "ExpensesAgent":
        await self.start()
        return self

    async def __aexit__(self, exc_type, exc, tb) -> None:
        await self.stop()

    async def start(self) -> None:
        settings = self._settings

        if settings.mcp_endpoint:
            self.mcp = ExpensesMcpClient(settings.mcp_endpoint)
        else:
            logger.error("No MCP server endpoint configured; record operations are unavailable")
            self.startup_error = "MCP server endpoint is not configured."
            return

        if not settings.is_agent_configured:
            logger.error("Foundry endpoint/deployment missing; the chat endpoint is unavailable")
            self.startup_error = "Foundry endpoint or deployment is not configured."
            return

        self._credential = DefaultAzureCredential()

        context_providers = []
        if settings.content_understanding_endpoint:
            self._cu = ContentUnderstandingContextProvider(
                endpoint=settings.content_understanding_endpoint,
                credential=self._credential,
                analyzer_id=settings.analyzer_id,
                max_wait=None,
                # Delegates to `azure.ai.contentunderstanding.to_llm_input`, which strips
                # the raw analysis down to just the extracted receipt fields instead of
                # injecting the whole page markdown into the prompt.
                output_sections=list(settings.content_understanding_sections),
            )
            await self._cu.__aenter__()
            context_providers.append(self._cu)
            logger.info(
                "Content Understanding enabled (endpoint=%s, analyzer=%s, sections=%s)",
                settings.content_understanding_endpoint,
                settings.analyzer_id,
                settings.content_understanding_sections,
            )
        else:
            logger.warning("Content Understanding endpoint not configured; receipts will not be analysed")

        self._history = SessionScopedHistoryProvider(self.mcp, max_messages=settings.max_history_messages)
        context_providers.append(self._history)

        # Durable cross-session memory. Optional: the agent still runs (with the
        # verbatim transcript only) when Cosmos or the models are unavailable.
        memory_provider = await self.memory.start(settings)
        if memory_provider is not None:
            context_providers.append(memory_provider)

        self._mcp_tool = MCPStreamableHTTPTool(
            name="expenses-records",
            url=settings.mcp_endpoint,
            description="CRUD operations on the work trips, expenses and conversations stored in Cosmos DB.",
            # NOTE: do not set `request_timeout` here. agent-framework 1.17 hands it to
            # mcp 2.x as a timedelta while the JSON-RPC dispatcher expects seconds,
            # which makes `initialize()` fail with a TypeError.
        )
        await self._mcp_tool.__aenter__()

        self._agent = Agent(
            client=FoundryChatClient(
                project_endpoint=settings.foundry_endpoint,
                model=settings.foundry_deployment,
                credential=self._credential,
            ),
            name="expensesagent",
            description="Creates and manages business travel expenses from receipt photos.",
            instructions=INSTRUCTIONS,
            context_providers=context_providers,
            tools=[self._mcp_tool, translateYAMLtoJSON],
        )

        logger.info("Expenses agent ready (model=%s)", settings.foundry_deployment)

    async def stop(self) -> None:
        await self.memory.stop()
        if self._mcp_tool is not None:
            await _safe_aexit(self._mcp_tool)
            self._mcp_tool = None
        if self._cu is not None:
            await _safe_aexit(self._cu)
            self._cu = None
        if self.mcp is not None:
            await self.mcp.aclose()
        if self._credential is not None:
            await self._credential.close()
            self._credential = None

    # ---- chat --------------------------------------------------------

    @property
    def is_ready(self) -> bool:
        return self._agent is not None

    async def chat(
        self,
        *,
        user_id: str,
        message: str,
        conversation_id: str | None = None,
        attachments: list[Attachment] | None = None,
    ) -> ChatTurn:
        if self._agent is None or self._history is None:
            raise RuntimeError(self.startup_error or "The agent is not configured.")

        attachments = attachments or []
        conversation_id = conversation_id or str(uuid.uuid4())
        session_id = build_session_id(user_id, conversation_id)

        with tracer().start_as_current_span("agent.chat") as span:
            span.set_attribute("expenses.user_id", user_id)
            span.set_attribute("expenses.conversation_id", conversation_id)
            span.set_attribute("expenses.attachment_count", len(attachments))
            span.set_attribute("expenses.message_length", len(message))

            logger.info(
                "Chat turn from user=%s conversation=%s attachments=%d: %s",
                user_id,
                conversation_id,
                len(attachments),
                _truncate(message),
            )

            contents: list[Content] = [Content.from_text(message or "Here is a receipt.")]
            for attachment in attachments:
                contents.append(
                    Content.from_data(
                        attachment.data,
                        attachment.content_type or "image/jpeg",
                        additional_properties={"filename": attachment.filename},
                    )
                )

            self._history.pending_attachments = [a.filename for a in attachments]

            run_messages = [
                Message(role="system", contents=[Content.from_text(self._turn_context(user_id, conversation_id))]),
                Message(role="user", contents=contents),
            ]

            response = await self._agent.run(run_messages, session=AgentSession(session_id=session_id))

            tool_calls: list[str] = []
            for msg in response.messages or []:
                tool_calls.extend(collect_tool_calls(msg))

            reply = (response.text or "").strip() or "Done."

            span.set_attribute("expenses.tool_calls", ",".join(tool_calls))
            span.set_attribute("expenses.reply_length", len(reply))
            logger.info(
                "Agent replied to user=%s conversation=%s (tools: %s): %s",
                user_id,
                conversation_id,
                ", ".join(tool_calls) or "none",
                _truncate(reply),
            )

            return ChatTurn(
                conversation_id=conversation_id,
                user_id=user_id,
                reply=reply,
                tool_calls=tool_calls,
                attachments=[a.filename for a in attachments],
            )

    def _turn_context(self, user_id: str, conversation_id: str) -> str:
        from datetime import date

        return (
            f"Context for this turn:\n"
            f"- userId: {user_id} (pass this as the `userId` argument of every tool call)\n"
            f"- conversationId: {conversation_id}\n"
            f"- today: {date.today().isoformat()}"
        )


async def _safe_aexit(resource: Any) -> None:
    try:
        await resource.__aexit__(None, None, None)
    except Exception:  # pragma: no cover - shutdown must not fail
        logger.debug("Error while closing %s", type(resource).__name__, exc_info=True)


def _truncate(value: str, limit: int = 200) -> str:
    value = (value or "").replace("\n", " ")
    return value if len(value) <= limit else value[: limit - 1] + "…"
