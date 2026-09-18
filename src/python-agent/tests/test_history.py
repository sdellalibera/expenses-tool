"""Tests for the Cosmos-backed conversation history provider."""

from __future__ import annotations

from agent_framework import Content, Message

from expenses_agent.agent import SessionScopedHistoryProvider, build_session_id, split_session_id


def test_session_id_round_trip():
    session_id = build_session_id("user-1", "conv-1")

    assert split_session_id(session_id) == ("user-1", "conv-1")


def test_split_session_id_handles_missing_user():
    assert split_session_id("conv-only") == (None, "conv-only")
    assert split_session_id(None) == (None, None)


async def test_saves_user_and_assistant_turns(fake_mcp):
    provider = SessionScopedHistoryProvider(fake_mcp)
    provider.pending_attachments = ["receipt.jpg"]

    await provider.save_messages(
        build_session_id("user-1", "conv-1"),
        [
            Message(role="system", contents=[Content.from_text("context")]),
            Message(role="user", contents=[Content.from_text("Add this to Munich")]),
            Message(role="assistant", contents=[Content.from_text("Stored 42.50 EUR.")]),
        ],
    )

    stored = fake_mcp.conversations["user-1::conv-1"]["messages"]

    assert [m["role"] for m in stored] == ["user", "assistant"]
    assert stored[0]["attachments"] == ["receipt.jpg"]
    assert stored[1]["text"] == "Stored 42.50 EUR."


async def test_records_tool_calls_in_the_transcript(fake_mcp):
    provider = SessionScopedHistoryProvider(fake_mcp)

    await provider.save_messages(
        build_session_id("user-1", "conv-1"),
        [
            Message(
                role="assistant",
                contents=[
                    Content.from_function_call(call_id="1", name="create_expense", arguments={}),
                    Content.from_text("Saved."),
                ],
            )
        ],
    )

    stored = fake_mcp.conversations["user-1::conv-1"]["messages"]

    assert stored[0]["toolCalls"] == ["create_expense"]


async def test_loads_only_replayable_turns(fake_mcp):
    await fake_mcp.append_conversation_messages(
        "user-1",
        "conv-1",
        [
            {"role": "user", "text": "Add this to Munich", "toolCalls": [], "attachments": []},
            {"role": "tool", "text": "{...}", "toolCalls": [], "attachments": []},
            {"role": "assistant", "text": "Stored 42.50 EUR.", "toolCalls": ["create_expense"], "attachments": []},
            {"role": "assistant", "text": "", "toolCalls": [], "attachments": []},
        ],
    )

    provider = SessionScopedHistoryProvider(fake_mcp)
    history = await provider.get_messages(build_session_id("user-1", "conv-1"))

    assert [m.role for m in history] == ["user", "assistant"]
    assert history[0].text == "Add this to Munich"


async def test_history_is_capped(fake_mcp):
    await fake_mcp.append_conversation_messages(
        "user-1",
        "conv-1",
        [{"role": "user", "text": f"message {i}", "toolCalls": [], "attachments": []} for i in range(30)],
    )

    provider = SessionScopedHistoryProvider(fake_mcp, max_messages=5)
    history = await provider.get_messages(build_session_id("user-1", "conv-1"))

    assert len(history) == 5
    assert history[-1].text == "message 29"


async def test_unknown_session_returns_no_history(fake_mcp):
    provider = SessionScopedHistoryProvider(fake_mcp)

    assert await provider.get_messages(build_session_id("user-1", "missing")) == []
    assert await provider.get_messages(None) == []


async def test_history_is_scoped_per_user(fake_mcp):
    await fake_mcp.append_conversation_messages(
        "user-1", "conv-1", [{"role": "user", "text": "mine", "toolCalls": [], "attachments": []}]
    )

    provider = SessionScopedHistoryProvider(fake_mcp)

    assert await provider.get_messages(build_session_id("user-2", "conv-1")) == []
