"""Tests for MCP result unwrapping and configuration resolution."""

from __future__ import annotations

import json

import pytest
from mcp.types import CallToolResult, TextContent

from expenses_agent.config import Settings, load_settings
from expenses_agent.mcp_client import _as_list, _unwrap


def _result(*, structured=None, text: str | None = None, is_error: bool = False) -> CallToolResult:
    content = [TextContent(type="text", text=text)] if text is not None else []
    return CallToolResult(content=content, structuredContent=structured, isError=is_error)


def test_unwrap_prefers_structured_content():
    assert _unwrap(_result(structured={"id": "trip-1"})) == {"id": "trip-1"}


def test_unwrap_unpacks_scalar_result_envelope():
    # The C# server wraps non-object returns (bool, list) in {"result": ...}.
    assert _unwrap(_result(structured={"result": True})) is True
    assert _unwrap(_result(structured={"result": [{"id": "a"}]})) == [{"id": "a"}]


def test_unwrap_falls_back_to_json_text():
    assert _unwrap(_result(text=json.dumps({"id": "trip-1"}))) == {"id": "trip-1"}


def test_unwrap_returns_plain_text_when_not_json():
    assert _unwrap(_result(text="not json")) == "not json"


def test_unwrap_handles_empty_result():
    assert _unwrap(_result()) is None


def test_as_list_normalises_shapes():
    assert _as_list(None) == []
    assert _as_list({"id": "a"}) == [{"id": "a"}]
    assert _as_list([{"id": "a"}]) == [{"id": "a"}]


def test_mcp_endpoint_is_derived_from_the_service_url():
    assert Settings(mcp_server_url="http://localhost:5000/").mcp_endpoint == "http://localhost:5000/mcp"
    assert Settings().mcp_endpoint is None


def test_settings_read_the_aspire_connection_string(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("ConnectionStrings__gpt5", "Endpoint=https://foundry.example/;Deployment=gpt5")
    monkeypatch.setenv("services__mcp-server__http__0", "http://localhost:5123")
    monkeypatch.setenv("contentUnderstandingEndpoint", "https://cu.example/")
    for name in ("FOUNDRY_ENDPOINT", "FOUNDRY_PROJECT_ENDPOINT", "FOUNDRY_DEPLOYMENT", "FOUNDRY_MODEL", "MCP_SERVER_URL"):
        monkeypatch.delenv(name, raising=False)

    settings = load_settings()

    assert settings.foundry_endpoint == "https://foundry.example/"
    assert settings.foundry_deployment == "gpt5"
    assert settings.mcp_endpoint == "http://localhost:5123/mcp"
    assert settings.is_agent_configured is True


def test_content_understanding_defaults_to_stripped_fields_only():
    assert Settings().content_understanding_sections == ["fields"]


def test_content_understanding_sections_are_configurable(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS", "fields, markdown")

    assert load_settings().content_understanding_sections == ["fields", "markdown"]


def test_invalid_content_understanding_sections_are_ignored(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS", "nonsense")

    assert load_settings().content_understanding_sections == ["fields"]


def test_settings_without_foundry_are_not_configured(monkeypatch: pytest.MonkeyPatch):
    for name in (
        "ConnectionStrings__gpt5",
        "ConnectionStrings__foundry",
        "FOUNDRY_ENDPOINT",
        "FOUNDRY_PROJECT_ENDPOINT",
        "FOUNDRY_DEPLOYMENT",
        "FOUNDRY_MODEL",
    ):
        monkeypatch.delenv(name, raising=False)

    assert load_settings().is_agent_configured is False
