"""Configuration resolution for the expenses agent.

Values are resolved from environment variables. When run under the Aspire
AppHost, the MCP server URLs are injected via service discovery variables of the
form ``services__<resource>__<scheme>__<index>``; explicit ``*_MCP_URL`` variables
override those for standalone runs.
"""

from __future__ import annotations

import os
from dataclasses import dataclass


def _first_env(*names: str) -> str | None:
    for name in names:
        value = os.environ.get(name)
        if value:
            return value
    return None


def _resolve_mcp_base_url(resource: str, *fallback_env: str) -> str | None:
    """Resolve an MCP server base URL from Aspire service discovery or explicit env."""
    return _first_env(
        f"services__{resource}__https__0",
        f"services__{resource}__http__0",
        *fallback_env,
    )


def _mcp_endpoint(base_url: str | None) -> str | None:
    """Append the MCP path to a base URL if not already present."""
    if not base_url:
        return None
    base = base_url.rstrip("/")
    return base if base.endswith("/mcp") else f"{base}/mcp"


@dataclass(slots=True)
class AgentSettings:
    """Resolved settings for the expenses agent."""

    # Foundry chat client (FoundryChatClient reads these from the environment too).
    foundry_project_endpoint: str | None
    foundry_model: str | None

    # Azure AI Content Understanding.
    content_understanding_endpoint: str | None
    content_understanding_analyzer_id: str | None

    # MCP servers (full /mcp endpoints).
    sql_mcp_url: str | None
    storage_mcp_url: str | None

    # Entra token scopes for the MCP servers (e.g. "api://<app-id>/.default").
    # When set, the agent acquires a token and sends it as a bearer header; when
    # unset (local dev), no auth header is sent.
    sql_mcp_scope: str | None
    storage_mcp_scope: str | None

    # Web host binding.
    host: str
    port: int

    def require(self, field: str) -> str:
        value = getattr(self, field)
        if not value:
            raise RuntimeError(
                f"Required setting '{field}' is not configured. "
                "Set the corresponding environment variable (see .env.example)."
            )
        return value


def load_settings() -> AgentSettings:
    """Build :class:`AgentSettings` from the current environment."""
    return AgentSettings(
        foundry_project_endpoint=_first_env(
            "FOUNDRY_PROJECT_ENDPOINT",
            "AZURE_AI_PROJECT_ENDPOINT",
            # Aspire injects the Foundry/project connection under these names.
            "ConnectionStrings__foundry",
            "ConnectionStrings__expenses-poc",
        ),
        foundry_model=_first_env("FOUNDRY_MODEL", "AZURE_OPENAI_DEPLOYMENT_NAME"),
        content_understanding_endpoint=_first_env("AZURE_CONTENTUNDERSTANDING_ENDPOINT"),
        content_understanding_analyzer_id=_first_env("AZURE_CONTENTUNDERSTANDING_ANALYZER_ID"),
        sql_mcp_url=_mcp_endpoint(_resolve_mcp_base_url("sql-mcp-server", "SQL_MCP_URL")),
        storage_mcp_url=_mcp_endpoint(_resolve_mcp_base_url("storagemcp", "STORAGE_MCP_URL", "STORAGEMCP_HTTP")),
        sql_mcp_scope=_first_env("SQL_MCP_SCOPE"),
        storage_mcp_scope=_first_env("STORAGE_MCP_SCOPE"),
        host=os.environ.get("HOST", "0.0.0.0"),
        port=int(os.environ.get("PORT", "8080")),
    )
