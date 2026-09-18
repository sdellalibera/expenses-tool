"""Cross-language contract test: the Python MCP client against the real C# server.

Skipped unless ``MCP_SERVER_URL`` points at a running MCP server. Start one with::

    $env:Cosmos__UseInMemory = "true"
    dotnet run --project src/mcp-server --urls http://localhost:5290
    $env:MCP_SERVER_URL = "http://localhost:5290"

This is the test that proves the agent's view of the tools matches what the
server actually exposes (argument names, result shapes, error behaviour).
"""

from __future__ import annotations

import os
import uuid

import pytest

from expenses_agent.history import McpConversationHistoryProvider
from expenses_agent.mcp_client import ExpensesMcpClient, McpToolError

MCP_SERVER_URL = os.environ.get("MCP_SERVER_URL")

pytestmark = pytest.mark.skipif(
    not MCP_SERVER_URL,
    reason="Set MCP_SERVER_URL to run the live MCP contract tests.",
)


@pytest.fixture
async def mcp():
    client = ExpensesMcpClient(f"{MCP_SERVER_URL.rstrip('/')}/mcp")
    try:
        yield client
    finally:
        await client.aclose()


@pytest.fixture
def user_id() -> str:
    return f"user-{uuid.uuid4().hex}"


async def test_server_advertises_every_tool_the_agent_needs(mcp: ExpensesMcpClient):
    names = set(await mcp.list_tool_names())

    assert {
        "create_trip",
        "list_trips",
        "get_trip",
        "find_trip_by_name",
        "update_trip",
        "delete_trip",
        "create_expense",
        "list_expenses",
        "get_expense",
        "update_expense",
        "delete_expense",
        "append_conversation_messages",
        "get_conversation",
        "list_conversations",
        "delete_conversation",
    } <= names


async def test_full_receipt_flow(mcp: ExpensesMcpClient, user_id: str):
    trip = await mcp.create_trip(user_id, "Munich kickoff", destination="Munich", startDate="2026-03-01")

    assert trip["name"] == "Munich kickoff"
    assert trip["status"] == "open"

    expense = await mcp.call(
        "create_expense",
        {
            "userId": user_id,
            "tripId": trip["id"],
            "merchant": "Hofbrauhaus",
            "totalAmount": 42.5,
            "date": "2026-03-02",
            "category": "food",
            "currency": "EUR",
            "lineItems": [{"description": "Schnitzel", "category": "food", "price": 24.5, "quantity": 1}],
        },
    )

    assert expense["merchant"] == "Hofbrauhaus"
    assert expense["totalAmount"] == 42.5
    assert len(expense["lineItems"]) == 1

    listed = await mcp.list_expenses(user_id, trip["id"])
    assert [e["id"] for e in listed] == [expense["id"]]

    detail = await mcp.get_expense(user_id, expense["id"])
    assert detail is not None and detail["merchant"] == "Hofbrauhaus"

    summaries = await mcp.list_trips(user_id)
    assert len(summaries) == 1
    assert summaries[0]["expenseCount"] == 1
    assert summaries[0]["totalAmount"] == 42.5
    assert summaries[0]["trip"]["id"] == trip["id"]


async def test_expenses_are_grouped_per_trip(mcp: ExpensesMcpClient, user_id: str):
    munich = await mcp.create_trip(user_id, "Munich kickoff")
    seattle = await mcp.create_trip(user_id, "Seattle summit")

    for trip_id, amount in ((munich["id"], 10.0), (munich["id"], 20.0), (seattle["id"], 30.0)):
        await mcp.call(
            "create_expense",
            {
                "userId": user_id,
                "tripId": trip_id,
                "merchant": "Somewhere",
                "totalAmount": amount,
                "date": "2026-03-02",
            },
        )

    assert len(await mcp.list_expenses(user_id, munich["id"])) == 2
    assert len(await mcp.list_expenses(user_id, seattle["id"])) == 1
    assert len(await mcp.list_expenses(user_id)) == 3


async def test_unknown_trip_surfaces_a_tool_error(mcp: ExpensesMcpClient, user_id: str):
    with pytest.raises(McpToolError):
        await mcp.call(
            "create_expense",
            {
                "userId": user_id,
                "tripId": "does-not-exist",
                "merchant": "Hofbrauhaus",
                "totalAmount": 10,
                "date": "2026-03-02",
            },
        )


async def test_reading_a_missing_record_returns_none(mcp: ExpensesMcpClient, user_id: str):
    # The very first turn of a new chat reads a conversation that does not exist
    # yet, so this must be a normal `None` rather than a protocol error.
    assert await mcp.get_conversation(user_id, "missing") is None
    assert await mcp.get_trip(user_id, "missing") is None
    assert await mcp.get_expense(user_id, "missing") is None


async def test_delete_returns_a_real_boolean(mcp: ExpensesMcpClient, user_id: str):
    trip = await mcp.create_trip(user_id, "Throwaway")

    assert await mcp.delete_trip(user_id, trip["id"]) is True
    assert await mcp.delete_trip(user_id, trip["id"]) is False


async def test_conversation_history_round_trips_through_the_server(mcp: ExpensesMcpClient, user_id: str):
    from agent_framework import Content, Message

    from expenses_agent.agent import SessionScopedHistoryProvider, build_session_id

    provider: McpConversationHistoryProvider = SessionScopedHistoryProvider(mcp)
    session_id = build_session_id(user_id, "conv-1")

    await provider.save_messages(
        session_id,
        [
            Message(role="user", contents=[Content.from_text("Add my hotel receipt")]),
            Message(role="assistant", contents=[Content.from_text("Stored 210 EUR.")]),
        ],
    )

    history = await provider.get_messages(session_id)

    assert [m.role for m in history] == ["user", "assistant"]
    assert history[0].text == "Add my hotel receipt"

    index = await mcp.list_conversations(user_id)
    assert index[0]["id"] == "conv-1"
    assert index[0]["messageCount"] == 2
