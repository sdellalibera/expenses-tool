"""Shared pytest fixtures.

The tests never touch Azure: the MCP server is replaced by an in-memory fake
that implements exactly the tool contract the real C# server exposes.
"""

from __future__ import annotations

import uuid
from typing import Any

import pytest

from expenses_agent.agent import Attachment, ChatTurn, ExpensesAgent
from expenses_agent.config import Settings


class FakeMcpClient:
    """In-memory stand-in for :class:`expenses_agent.mcp_client.ExpensesMcpClient`."""

    def __init__(self) -> None:
        self.trips: dict[str, dict[str, Any]] = {}
        self.expenses: dict[str, dict[str, Any]] = {}
        self.conversations: dict[str, dict[str, Any]] = {}
        self.calls: list[tuple[str, dict[str, Any]]] = []
        self.endpoint = "http://fake/mcp"

    async def aclose(self) -> None:  # pragma: no cover - nothing to release
        return None

    # -- trips ---------------------------------------------------------

    def add_trip(self, user_id: str, name: str, **kwargs: Any) -> dict[str, Any]:
        trip = {"id": str(uuid.uuid4()), "userId": user_id, "name": name, "status": "open", **kwargs}
        self.trips[trip["id"]] = trip
        return trip

    async def create_trip(self, user_id: str, name: str, **kwargs: Any) -> dict[str, Any]:
        self.calls.append(("create_trip", {"userId": user_id, "name": name, **kwargs}))
        return self.add_trip(user_id, name, **kwargs)

    async def list_trips(self, user_id: str, status: str | None = None) -> list[dict[str, Any]]:
        self.calls.append(("list_trips", {"userId": user_id, "status": status}))
        return [
            {
                "trip": t,
                "expenseCount": len([e for e in self.expenses.values() if e["tripId"] == t["id"]]),
                "totalAmount": sum(e["totalAmount"] for e in self.expenses.values() if e["tripId"] == t["id"]),
            }
            for t in self.trips.values()
            if t["userId"] == user_id and (status is None or t.get("status") == status)
        ]

    async def get_trip(self, user_id: str, trip_id: str) -> dict[str, Any] | None:
        self.calls.append(("get_trip", {"userId": user_id, "tripId": trip_id}))
        trip = self.trips.get(trip_id)
        if not trip or trip["userId"] != user_id:
            return None
        related = [e for e in self.expenses.values() if e["tripId"] == trip_id]
        return {"trip": trip, "expenseCount": len(related), "totalAmount": sum(e["totalAmount"] for e in related)}

    async def delete_trip(self, user_id: str, trip_id: str) -> bool:
        self.calls.append(("delete_trip", {"userId": user_id, "tripId": trip_id}))
        trip = self.trips.get(trip_id)
        if not trip or trip["userId"] != user_id:
            return False
        for expense_id in [e["id"] for e in self.expenses.values() if e["tripId"] == trip_id]:
            self.expenses.pop(expense_id, None)
        self.trips.pop(trip_id)
        return True

    # -- expenses ------------------------------------------------------

    def add_expense(self, user_id: str, trip_id: str, **kwargs: Any) -> dict[str, Any]:
        expense = {
            "id": str(uuid.uuid4()),
            "userId": user_id,
            "tripId": trip_id,
            "merchant": "Hofbrauhaus",
            "category": "food",
            "date": "2026-03-02",
            "totalAmount": 42.5,
            "currency": "EUR",
            "lineItems": [],
            **kwargs,
        }
        self.expenses[expense["id"]] = expense
        return expense

    async def list_expenses(self, user_id: str, trip_id: str | None = None) -> list[dict[str, Any]]:
        self.calls.append(("list_expenses", {"userId": user_id, "tripId": trip_id}))
        return [
            e
            for e in self.expenses.values()
            if e["userId"] == user_id and (trip_id is None or e["tripId"] == trip_id)
        ]

    async def get_expense(self, user_id: str, expense_id: str) -> dict[str, Any] | None:
        self.calls.append(("get_expense", {"userId": user_id, "expenseId": expense_id}))
        expense = self.expenses.get(expense_id)
        return expense if expense and expense["userId"] == user_id else None

    async def delete_expense(self, user_id: str, expense_id: str) -> bool:
        self.calls.append(("delete_expense", {"userId": user_id, "expenseId": expense_id}))
        expense = self.expenses.get(expense_id)
        if not expense or expense["userId"] != user_id:
            return False
        self.expenses.pop(expense_id)
        return True

    # -- conversations -------------------------------------------------

    async def list_conversations(self, user_id: str) -> list[dict[str, Any]]:
        self.calls.append(("list_conversations", {"userId": user_id}))
        return [
            {
                "id": c["id"],
                "userId": c["userId"],
                "title": c["title"],
                "messageCount": len(c["messages"]),
            }
            for c in self.conversations.values()
            if c["userId"] == user_id
        ]

    async def get_conversation(self, user_id: str, conversation_id: str) -> dict[str, Any] | None:
        self.calls.append(("get_conversation", {"userId": user_id, "conversationId": conversation_id}))
        return self.conversations.get(f"{user_id}::{conversation_id}")

    async def append_conversation_messages(
        self,
        user_id: str,
        conversation_id: str,
        messages: list[dict[str, Any]],
        *,
        title: str | None = None,
        trip_id: str | None = None,
    ) -> dict[str, Any]:
        self.calls.append(
            (
                "append_conversation_messages",
                {"userId": user_id, "conversationId": conversation_id, "messages": messages},
            )
        )
        key = f"{user_id}::{conversation_id}"
        conversation = self.conversations.setdefault(
            key,
            {"id": conversation_id, "userId": user_id, "title": title or "New conversation", "messages": []},
        )
        conversation["messages"].extend(messages)
        return conversation


class FakeAgent(ExpensesAgent):
    """Agent whose chat turn is scripted, so the API can be tested offline."""

    def __init__(self, settings: Settings, mcp: FakeMcpClient) -> None:
        super().__init__(settings)
        self.mcp = mcp  # type: ignore[assignment]
        self._ready = True
        self.chat_calls: list[dict[str, Any]] = []
        self.reply = "Stored your expense on the Munich trip."

    @property
    def is_ready(self) -> bool:  # type: ignore[override]
        return self._ready

    def set_ready(self, ready: bool, error: str | None = None) -> None:
        self._ready = ready
        self.startup_error = error

    async def start(self) -> None:  # pragma: no cover - not used
        return None

    async def stop(self) -> None:  # pragma: no cover - not used
        return None

    async def chat(
        self,
        *,
        user_id: str,
        message: str,
        conversation_id: str | None = None,
        attachments: list[Attachment] | None = None,
    ) -> ChatTurn:
        attachments = attachments or []
        self.chat_calls.append(
            {
                "userId": user_id,
                "message": message,
                "conversationId": conversation_id,
                "attachments": [a.filename for a in attachments],
            }
        )
        return ChatTurn(
            conversation_id=conversation_id or "conv-1",
            user_id=user_id,
            reply=self.reply,
            tool_calls=["create_expense"] if attachments else [],
            attachments=[a.filename for a in attachments],
        )


@pytest.fixture
def settings() -> Settings:
    return Settings(
        foundry_endpoint="https://example.services.ai.azure.com/api/projects/expenses",
        foundry_deployment="gpt5",
        content_understanding_endpoint="https://example.services.ai.azure.com/",
        mcp_server_url="http://fake",
    )


@pytest.fixture
def fake_mcp() -> FakeMcpClient:
    return FakeMcpClient()


@pytest.fixture
def fake_agent(settings: Settings, fake_mcp: FakeMcpClient) -> FakeAgent:
    return FakeAgent(settings, fake_mcp)


@pytest.fixture
def client(settings: Settings, fake_agent: FakeAgent):
    from fastapi.testclient import TestClient

    from expenses_agent.api import create_app

    app = create_app(settings, agent=fake_agent)
    with TestClient(app) as test_client:
        yield test_client
