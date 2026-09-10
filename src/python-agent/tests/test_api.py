"""Tests for the FastAPI surface of the agent.

The agent itself is faked (see ``conftest.py``) so these run offline.
"""

from __future__ import annotations

import io


def test_health_reports_ready_agent(client):
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "healthy"
    assert body["service"] == "expensesagent"
    assert body["agentReady"] is True
    assert body["configuration"]["mcpEndpoint"] == "http://fake/mcp"


def test_health_reports_degraded_agent(client, fake_agent):
    fake_agent.set_ready(False, "Foundry endpoint is not configured.")

    body = client.get("/health").json()

    assert body["status"] == "degraded"
    assert body["error"] == "Foundry endpoint is not configured."


def test_alive_endpoint(client):
    assert client.get("/alive").json() == {"status": "alive"}


def test_chat_with_text_only(client, fake_agent):
    response = client.post("/chat", data={"userId": "u1", "message": "Create a trip to Munich"})

    assert response.status_code == 200
    body = response.json()
    assert body["reply"] == fake_agent.reply
    assert body["userId"] == "u1"
    assert body["conversationId"] == "conv-1"
    assert fake_agent.chat_calls[0]["message"] == "Create a trip to Munich"


def test_chat_with_receipt_photo(client, fake_agent):
    response = client.post(
        "/chat",
        data={"userId": "u1", "message": "Add this to the Munich trip", "conversationId": "conv-42"},
        files={"images": ("receipt.jpg", io.BytesIO(b"fake-jpeg-bytes"), "image/jpeg")},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["conversationId"] == "conv-42"
    assert body["attachments"] == ["receipt.jpg"]
    assert body["toolCalls"] == ["create_expense"]
    assert fake_agent.chat_calls[0]["attachments"] == ["receipt.jpg"]


def test_chat_accepts_multiple_photos(client, fake_agent):
    response = client.post(
        "/chat",
        data={"userId": "u1", "message": ""},
        files=[
            ("images", ("a.jpg", io.BytesIO(b"a"), "image/jpeg")),
            ("images", ("b.jpg", io.BytesIO(b"b"), "image/jpeg")),
        ],
    )

    assert response.status_code == 200
    assert response.json()["attachments"] == ["a.jpg", "b.jpg"]


def test_chat_requires_a_user_id(client):
    assert client.post("/chat", data={"userId": "   ", "message": "hi"}).status_code == 400


def test_chat_requires_content(client):
    assert client.post("/chat", data={"userId": "u1", "message": "   "}).status_code == 400


def test_chat_returns_503_when_agent_is_not_ready(client, fake_agent):
    fake_agent.set_ready(False, "Foundry endpoint is not configured.")

    response = client.post("/chat", data={"userId": "u1", "message": "hi"})

    assert response.status_code == 503
    assert "Foundry" in response.json()["detail"]


def test_model_rate_limiting_is_reported_as_429(client, fake_agent):
    async def rate_limited(**_kwargs):
        raise RuntimeError("Error code: 429 - Too Many Requests")

    fake_agent.chat = rate_limited  # type: ignore[method-assign]

    response = client.post("/chat", data={"userId": "u1", "message": "hi"})

    assert response.status_code == 429
    assert "rate limited" in response.json()["detail"]


def test_unexpected_agent_failures_become_502(client, fake_agent):
    async def boom(**_kwargs):
        raise RuntimeError("model exploded")

    fake_agent.chat = boom  # type: ignore[method-assign]

    response = client.post("/chat", data={"userId": "u1", "message": "hi"})

    assert response.status_code == 502
    assert "model exploded" in response.json()["detail"]


def test_mcp_failures_during_chat_become_502(client, fake_agent):
    from expenses_agent.mcp_client import McpToolError

    async def boom(**_kwargs):
        raise McpToolError("trip does not exist")

    fake_agent.chat = boom  # type: ignore[method-assign]

    response = client.post("/chat", data={"userId": "u1", "message": "hi"})

    assert response.status_code == 502
    assert response.json()["detail"] == "trip does not exist"


def test_trips_endpoint_returns_summaries(client, fake_mcp):
    trip = fake_mcp.add_trip("u1", "Munich kickoff", destination="Munich")
    fake_mcp.add_expense("u1", trip["id"], totalAmount=42.5)
    fake_mcp.add_expense("u1", trip["id"], totalAmount=7.5)

    body = client.get("/api/trips", params={"userId": "u1"}).json()

    assert len(body) == 1
    assert body[0]["trip"]["name"] == "Munich kickoff"
    assert body[0]["expenseCount"] == 2
    assert body[0]["totalAmount"] == 50.0


def test_trip_detail_and_404(client, fake_mcp):
    trip = fake_mcp.add_trip("u1", "Munich kickoff")

    ok = client.get(f"/api/trips/{trip['id']}", params={"userId": "u1"})
    missing = client.get("/api/trips/nope", params={"userId": "u1"})

    assert ok.status_code == 200
    assert ok.json()["trip"]["id"] == trip["id"]
    assert missing.status_code == 404


def test_expenses_can_be_filtered_by_trip(client, fake_mcp):
    munich = fake_mcp.add_trip("u1", "Munich kickoff")
    seattle = fake_mcp.add_trip("u1", "Seattle summit")
    fake_mcp.add_expense("u1", munich["id"])
    fake_mcp.add_expense("u1", munich["id"])
    fake_mcp.add_expense("u1", seattle["id"])

    all_expenses = client.get("/api/expenses", params={"userId": "u1"}).json()
    munich_expenses = client.get("/api/expenses", params={"userId": "u1", "tripId": munich["id"]}).json()

    assert len(all_expenses) == 3
    assert len(munich_expenses) == 2


def test_expense_detail_and_404(client, fake_mcp):
    trip = fake_mcp.add_trip("u1", "Munich kickoff")
    expense = fake_mcp.add_expense("u1", trip["id"], merchant="Hotel Bayern")

    ok = client.get(f"/api/expenses/{expense['id']}", params={"userId": "u1"})
    missing = client.get("/api/expenses/nope", params={"userId": "u1"})

    assert ok.status_code == 200
    assert ok.json()["merchant"] == "Hotel Bayern"
    assert missing.status_code == 404


def test_records_are_scoped_to_the_requesting_user(client, fake_mcp):
    trip = fake_mcp.add_trip("u1", "Munich kickoff")
    fake_mcp.add_expense("u1", trip["id"])

    assert client.get("/api/trips", params={"userId": "u2"}).json() == []
    assert client.get("/api/expenses", params={"userId": "u2"}).json() == []
    assert client.get(f"/api/trips/{trip['id']}", params={"userId": "u2"}).status_code == 404


def test_delete_expense(client, fake_mcp):
    trip = fake_mcp.add_trip("u1", "Munich kickoff")
    expense = fake_mcp.add_expense("u1", trip["id"])

    deleted = client.delete(f"/api/expenses/{expense['id']}", params={"userId": "u1"})

    assert deleted.status_code == 200
    assert deleted.json()["deleted"] is True
    assert client.get("/api/expenses", params={"userId": "u1"}).json() == []


def test_conversations_endpoints(client, fake_mcp):
    import anyio

    anyio.run(
        fake_mcp.append_conversation_messages,
        "u1",
        "conv-1",
        [{"role": "user", "text": "Add my hotel receipt", "toolCalls": [], "attachments": []}],
    )

    index = client.get("/api/conversations", params={"userId": "u1"}).json()
    detail = client.get("/api/conversations/conv-1", params={"userId": "u1"}).json()

    assert index[0]["id"] == "conv-1"
    assert detail["messages"][0]["text"] == "Add my hotel receipt"
    assert client.get("/api/conversations/missing", params={"userId": "u1"}).status_code == 404
