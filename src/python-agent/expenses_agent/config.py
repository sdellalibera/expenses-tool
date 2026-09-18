"""Environment driven configuration for the expenses agent.

Every value comes from an environment variable that the Aspire AppHost injects
(service discovery, connection strings, OTLP endpoint), so the agent has no
hard-coded endpoints.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass, field

logger = logging.getLogger(__name__)


def _parse_connection_string(value: str) -> dict[str, str]:
    """Parse an ``key=value;key=value`` Aspire connection string."""
    parsed: dict[str, str] = {}
    for part in value.split(";"):
        key, sep, val = part.partition("=")
        if sep and key.strip():
            parsed[key.strip().lower()] = val.strip()
    return parsed


def _first_env(*names: str, default: str | None = None) -> str | None:
    for name in names:
        value = os.environ.get(name)
        if value:
            return value
    return default


def _discover_service_url(service_name: str) -> str | None:
    """Resolve a URL injected by Aspire service discovery.

    Aspire exposes references as ``services__<name>__<scheme>__<index>``.
    HTTP is preferred over HTTPS locally because the Python HTTP client does not
    trust the ASP.NET Core development certificate.
    """
    for scheme in ("http", "https"):
        for index in range(4):
            value = os.environ.get(f"services__{service_name}__{scheme}__{index}")
            if value:
                return value.rstrip("/")
    return None


@dataclass(slots=True)
class Settings:
    """Resolved configuration for one process."""

    foundry_endpoint: str | None = None
    foundry_deployment: str | None = None
    content_understanding_endpoint: str | None = None
    analyzer_id: str = "ExpensesAnalyzer"
    # Which sections of the Content Understanding analysis reach the model.
    # "fields" only keeps the structured receipt fields and drops the full page
    # markdown, which is what `to_llm_input` strips the raw analysis down to.
    content_understanding_sections: list[str] = field(default_factory=lambda: ["fields"])
    mcp_server_url: str | None = None
    # Durable cross-session memory (Agent Memory Toolkit on Cosmos DB).
    enable_cosmos_memory: bool = True
    cosmos_endpoint: str | None = None
    cosmos_key: str | None = None
    cosmos_database: str = "db"
    memory_chat_model: str | None = None
    memory_embedding_model: str | None = None
    memory_top_k: int = 5
    host: str = "0.0.0.0"
    port: int = 8000
    service_name: str = "expenses-agent"
    otlp_endpoint: str | None = None
    enable_sensitive_telemetry: bool = True
    allowed_origins: list[str] = field(default_factory=lambda: ["*"])
    max_history_messages: int = 40

    @property
    def mcp_endpoint(self) -> str | None:
        """Full URL of the MCP server's streamable HTTP endpoint."""
        if not self.mcp_server_url:
            return None
        return f"{self.mcp_server_url.rstrip('/')}/mcp"

    @property
    def is_agent_configured(self) -> bool:
        """True when enough configuration exists to talk to Foundry."""
        return bool(self.foundry_endpoint and self.foundry_deployment)

    def describe(self) -> dict[str, object]:
        """Non-secret view of the configuration, used by /health."""
        return {
            "foundryEndpoint": self.foundry_endpoint,
            "foundryDeployment": self.foundry_deployment,
            "contentUnderstandingEndpoint": self.content_understanding_endpoint,
            "analyzerId": self.analyzer_id,
            "contentUnderstandingSections": self.content_understanding_sections,
            "mcpEndpoint": self.mcp_endpoint,
            "cosmosMemory": {
                "enabled": self.enable_cosmos_memory,
                "endpoint": self.cosmos_endpoint,
                "database": self.cosmos_database,
                "chatModel": self.memory_chat_model,
                "embeddingModel": self.memory_embedding_model,
            },
            "otlpEndpoint": self.otlp_endpoint,
        }


def load_settings() -> Settings:
    """Build :class:`Settings` from the current process environment."""
    settings = Settings()

    # Foundry: Aspire hands the model deployment over as a connection string.
    connection = _parse_connection_string(
        _first_env("ConnectionStrings__gpt5", "ConnectionStrings__foundry", default="") or ""
    )
    settings.foundry_endpoint = _first_env("FOUNDRY_ENDPOINT", "FOUNDRY_PROJECT_ENDPOINT") or connection.get("endpoint")
    settings.foundry_deployment = _first_env("FOUNDRY_DEPLOYMENT", "FOUNDRY_MODEL") or connection.get("deployment") or connection.get("model")

    settings.content_understanding_endpoint = _first_env(
        "contentUnderstandingEndpoint",
        "CONTENT_UNDERSTANDING_ENDPOINT",
        "AZURE_CONTENTUNDERSTANDING_ENDPOINT",
    )
    settings.analyzer_id = _first_env("CONTENT_UNDERSTANDING_ANALYZER_ID", default="ExpensesAnalyzer") or "ExpensesAnalyzer"

    sections = _first_env("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS")
    if sections:
        allowed = {"markdown", "fields"}
        requested = [s.strip().lower() for s in sections.split(",") if s.strip().lower() in allowed]
        if requested:
            settings.content_understanding_sections = requested

    settings.mcp_server_url = _first_env("MCP_SERVER_URL") or _discover_service_url("mcp-server")

    # Cosmos DB, used directly by the durable-memory provider (records still go
    # through the MCP server).
    cosmos = _parse_connection_string(_first_env("ConnectionStrings__cosmos-db", default="") or "")
    settings.cosmos_endpoint = _first_env("COSMOS_ENDPOINT") or cosmos.get("accountendpoint")
    settings.cosmos_key = _first_env("COSMOS_KEY") or cosmos.get("accountkey")
    settings.cosmos_database = _first_env("COSMOS_DATABASE", default="db") or "db"
    settings.memory_chat_model = _first_env("MEMORY_CHAT_MODEL") or settings.foundry_deployment
    settings.memory_embedding_model = _first_env("MEMORY_EMBEDDING_MODEL")
    settings.memory_top_k = int(_first_env("MEMORY_TOP_K", default="5") or "5")
    settings.enable_cosmos_memory = (_first_env("ENABLE_COSMOS_MEMORY", default="true") or "true").lower() not in {
        "false",
        "0",
        "no",
    }

    settings.host = _first_env("HOST", default="0.0.0.0") or "0.0.0.0"
    settings.port = int(_first_env("PORT", default="8000") or "8000")
    settings.service_name = _first_env("OTEL_SERVICE_NAME", default="expenses-agent") or "expenses-agent"
    settings.otlp_endpoint = _first_env("OTEL_EXPORTER_OTLP_ENDPOINT")

    origins = _first_env("ALLOWED_ORIGINS")
    if origins:
        settings.allowed_origins = [o.strip() for o in origins.split(",") if o.strip()]

    logger.info("Resolved settings: %s", settings.describe())
    return settings
