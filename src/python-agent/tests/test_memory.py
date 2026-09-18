"""Tests for the Cosmos durable-memory wiring.

The provider itself is a third-party component; what matters here is that the
agent degrades gracefully — a missing or broken memory backend must never stop
the chat endpoint from working.
"""

from __future__ import annotations

from unittest.mock import AsyncMock

import pytest

from expenses_agent.config import Settings, load_settings
from expenses_agent.memory import CosmosMemory


def _settings(**overrides) -> Settings:
    base = Settings(
        foundry_endpoint="https://example.services.ai.azure.com/api/projects/expenses",
        foundry_deployment="gpt5",
        cosmos_endpoint="https://localhost:8081",
        cosmos_key="key",
        cosmos_database="db",
        memory_chat_model="gpt5mini",
        memory_embedding_model="TextEmbedding3Large",
    )
    for key, value in overrides.items():
        setattr(base, key, value)
    return base


async def test_disabled_by_configuration():
    memory = CosmosMemory()

    assert await memory.start(_settings(enable_cosmos_memory=False)) is None
    assert memory.is_enabled is False
    assert memory.error is None


@pytest.mark.parametrize(
    "missing",
    ["cosmos_endpoint", "foundry_endpoint", "memory_chat_model", "memory_embedding_model"],
)
async def test_skipped_when_configuration_is_incomplete(missing: str):
    memory = CosmosMemory()

    assert await memory.start(_settings(**{missing: None})) is None
    assert memory.is_enabled is False
    assert memory.error is not None and "missing" in memory.error


async def test_backend_failure_is_reported_but_not_raised(monkeypatch: pytest.MonkeyPatch):
    memory = CosmosMemory()

    async def boom(_settings):
        raise RuntimeError("cosmos is down")

    monkeypatch.setattr(memory, "_build", boom)

    assert await memory.start(_settings()) is None
    assert memory.is_enabled is False
    assert "cosmos is down" in memory.error


@pytest.mark.parametrize("close_error", [None, RuntimeError("close failed")])
async def test_backend_failure_closes_partial_client(monkeypatch: pytest.MonkeyPatch, close_error):
    memory = CosmosMemory()
    client = AsyncMock()
    client.close.side_effect = close_error

    async def boom(_settings):
        memory._client = client
        raise RuntimeError("indexing policy rejected")

    monkeypatch.setattr(memory, "_build", boom)

    assert await memory.start(_settings()) is None
    client.close.assert_awaited_once()
    assert memory._client is None
    assert memory.is_enabled is False
    assert "indexing policy rejected" in memory.error

    await memory.stop()
    client.close.assert_awaited_once()


async def test_provider_is_returned_when_the_backend_builds(monkeypatch: pytest.MonkeyPatch):
    memory = CosmosMemory()
    sentinel = object()

    async def build(_settings):
        return sentinel

    monkeypatch.setattr(memory, "_build", build)

    assert await memory.start(_settings()) is sentinel
    assert memory.is_enabled is True
    assert memory.error is None

    await memory.stop()
    assert memory.is_enabled is False


async def test_stop_is_safe_when_never_started():
    await CosmosMemory().stop()


def test_settings_read_cosmos_from_the_aspire_connection_string(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv(
        "ConnectionStrings__cosmos-db",
        "AccountEndpoint=https://localhost:22752;AccountKey=abc123;DisableServerCertificateValidation=True;",
    )
    monkeypatch.setenv("COSMOS_DATABASE", "db")
    monkeypatch.setenv("MEMORY_EMBEDDING_MODEL", "TextEmbedding3Large")
    monkeypatch.setenv("MEMORY_CHAT_MODEL", "gpt5mini")
    monkeypatch.delenv("COSMOS_ENDPOINT", raising=False)
    monkeypatch.delenv("COSMOS_KEY", raising=False)
    monkeypatch.delenv("ENABLE_COSMOS_MEMORY", raising=False)

    settings = load_settings()

    assert settings.cosmos_endpoint == "https://localhost:22752"
    assert settings.cosmos_key == "abc123"
    assert settings.cosmos_database == "db"
    assert settings.memory_chat_model == "gpt5mini"
    assert settings.memory_embedding_model == "TextEmbedding3Large"
    assert settings.enable_cosmos_memory is True


def test_durable_memory_can_be_switched_off(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("ENABLE_COSMOS_MEMORY", "false")

    assert load_settings().enable_cosmos_memory is False


def test_memory_chat_model_falls_back_to_the_agent_deployment(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("ConnectionStrings__gpt5", "Endpoint=https://foundry.example/;Deployment=gpt5")
    monkeypatch.delenv("MEMORY_CHAT_MODEL", raising=False)
    monkeypatch.delenv("FOUNDRY_DEPLOYMENT", raising=False)
    monkeypatch.delenv("FOUNDRY_MODEL", raising=False)

    assert load_settings().memory_chat_model == "gpt5"
