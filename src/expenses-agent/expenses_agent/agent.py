"""Assembles the expenses agent from a Foundry chat client, the Content
Understanding context provider, and the SQL + Storage MCP servers (as tools)."""

from __future__ import annotations

from contextlib import AsyncExitStack, asynccontextmanager
from collections.abc import AsyncIterator, Iterator
from pathlib import Path

import httpx
from agent_framework import Agent, Content, MCPStreamableHTTPTool, Message
from agent_framework._sessions import AgentSession
from agent_framework.foundry import ContentUnderstandingContextProvider, FoundryChatClient
from azure.core.credentials import TokenCredential
from azure.identity import DefaultAzureCredential

from .config import AgentSettings, load_settings

_PROMPT_PATH = Path(__file__).parent / "prompts" / "system_prompt.md"


def _load_system_prompt() -> str:
    return _PROMPT_PATH.read_text(encoding="utf-8")


class _EntraBearerAuth(httpx.Auth):
    """httpx auth flow that attaches an Entra bearer token to every request.

    Unlike the MCP tool's ``header_provider`` (which only authenticates individual
    tool calls), an auth flow on the HTTP client also authenticates the initial
    ``initialize`` / ``list_tools`` handshake — required when the MCP endpoint is
    fully protected. Tokens are cached and refreshed by the credential.
    """

    def __init__(self, credential: TokenCredential, scope: str) -> None:
        self._credential = credential
        self._scope = scope

    def auth_flow(self, request: httpx.Request) -> Iterator[httpx.Request]:
        token = self._credential.get_token(self._scope)
        request.headers["Authorization"] = f"Bearer {token.token}"
        yield request


def _mcp_http_client(credential: TokenCredential, scope: str | None) -> httpx.AsyncClient | None:
    """Build an authenticated HTTP client for an MCP server, or None when the
    scope is unset (local/dev — the MCP server accepts anonymous calls)."""
    if not scope:
        return None
    return httpx.AsyncClient(
        auth=_EntraBearerAuth(credential, scope),
        follow_redirects=True,
        timeout=httpx.Timeout(30.0, read=None),
    )


class ExpensesAgent:
    """Thin wrapper over the framework Agent with per-conversation sessions."""

    def __init__(self, agent: Agent) -> None:
        self._agent = agent
        self._sessions: dict[str, AgentSession] = {}

    def _session(self, conversation_id: str) -> AgentSession:
        session = self._sessions.get(conversation_id)
        if session is None:
            session = AgentSession()
            self._sessions[conversation_id] = session
        return session

    async def chat(self, message: str, conversation_id: str) -> str:
        """Free-form chat turn."""
        response = await self._agent.run(message, session=self._session(conversation_id))
        return response.text

    async def analyze_receipt(
        self,
        image_bytes: bytes,
        media_type: str,
        conversation_id: str,
        note: str | None = None,
    ) -> str:
        """Analyze a receipt image, persist it, and record the expense.

        The image is attached to the message so the Content Understanding context
        provider analyzes it automatically. The agent is then expected to persist
        the image via the Storage MCP and create an Expense row via the SQL MCP.
        """
        instruction = (
            "A receipt/invoice image is attached. Its extracted content is provided "
            "to you by Content Understanding. Follow your instructions to persist the "
            "image and record the expense, then reply with a short summary."
        )
        if note:
            instruction += f"\n\nUser note: {note}"

        message = Message(
            role="user",
            contents=[
                Content.from_text(instruction),
                Content.from_data(data=image_bytes, media_type=media_type),
            ],
        )
        response = await self._agent.run(message, session=self._session(conversation_id))
        return response.text


@asynccontextmanager
async def build_expenses_agent(settings: AgentSettings | None = None) -> AsyncIterator[ExpensesAgent]:
    """Async context manager that builds the agent and manages resource lifetimes.

    Uses ``DefaultAzureCredential`` so the same code authenticates via ``az login``
    locally and via managed identity when deployed.
    """
    settings = settings or load_settings()
    credential = DefaultAzureCredential()

    async with AsyncExitStack() as stack:
        client = FoundryChatClient(
            project_endpoint=settings.foundry_project_endpoint,
            model=settings.foundry_model,
            credential=credential,
        )

        content_understanding = ContentUnderstandingContextProvider(
            endpoint=settings.content_understanding_endpoint,
            credential=credential,
            analyzer_id=settings.content_understanding_analyzer_id,
            # None => block until extraction completes before calling the model.
            max_wait=None,
        )
        await stack.enter_async_context(content_understanding)

        sql_http = _mcp_http_client(credential, settings.sql_mcp_scope)
        storage_http = _mcp_http_client(credential, settings.storage_mcp_scope)
        if sql_http is not None:
            stack.push_async_callback(sql_http.aclose)
        if storage_http is not None:
            stack.push_async_callback(storage_http.aclose)

        sql_mcp = MCPStreamableHTTPTool(
            name="expenses-sql",
            description="Read and write structured expense records (ExpenseReport, Expense) in SQL.",
            url=settings.require("sql_mcp_url"),
            http_client=sql_http,
        )
        storage_mcp = MCPStreamableHTTPTool(
            name="expenses-storage",
            description="Save and read receipt/invoice images in blob storage.",
            url=settings.require("storage_mcp_url"),
            http_client=storage_http,
        )
        await stack.enter_async_context(sql_mcp)
        await stack.enter_async_context(storage_mcp)

        agent = Agent(
            client=client,
            name="ExpensesAgent",
            instructions=_load_system_prompt(),
            context_providers=[content_understanding],
            tools=[sql_mcp, storage_mcp],
        )

        yield ExpensesAgent(agent)
