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
    model_max_output_tokens: int = 2048
    model_reasoning_effort: str = "low"
    model_max_retries: int = 2
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
    max_history_tokens: int = 4096
    receipt_cache_version: str = "1"

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
            "modelMaxOutputTokens": self.model_max_output_tokens,
            "modelReasoningEffort": self.model_reasoning_effort,
            "maxHistoryTokens": self.max_history_tokens,
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
        _first_env("ConnectionStrings__gpt5-4", "ConnectionStrings__foundry", default="") or ""
    )
    settings.foundry_endpoint = _first_env("FOUNDRY_ENDPOINT", "FOUNDRY_PROJECT_ENDPOINT") or connection.get("endpoint")
    settings.foundry_deployment = _first_env("FOUNDRY_DEPLOYMENT", "FOUNDRY_MODEL") or connection.get("deployment") or connection.get("model")
    settings.model_max_output_tokens = int(_first_env("MODEL_MAX_OUTPUT_TOKENS", default="2048"))
    settings.model_reasoning_effort = _first_env("MODEL_REASONING_EFFORT", default="low")
    settings.model_max_retries = int(_first_env("MODEL_MAX_RETRIES", default="2"))
    settings.max_history_tokens = int(_first_env("MAX_HISTORY_TOKENS", default="4096"))
    settings.receipt_cache_version = _first_env("RECEIPT_CACHE_VERSION", default="1")
    if not 256 <= settings.max_history_tokens <= 32768:
        raise ValueError("MAX_HISTORY_TOKENS must be between 256 and 32768.")
    if not 256 <= settings.model_max_output_tokens <= 16384:
        raise ValueError("MODEL_MAX_OUTPUT_TOKENS must be between 256 and 16384.")
    if settings.model_reasoning_effort not in {"none", "low", "medium", "high"}:
        raise ValueError("MODEL_REASONING_EFFORT must be none, low, medium or high.")
    if not 0 <= settings.model_max_retries <= 5:
        raise ValueError("MODEL_MAX_RETRIES must be between 0 and 5.")

    settings.content_understanding_endpoint = _first_env(
        "contentUnderstandingEndpoint",
        "CONTENT_UNDERSTANDING_ENDPOINT",
        "AZURE_CONTENTUNDERSTANDING_ENDPOINT",
    )
    settings.analyzer_id = _first_env("CONTENT_UNDERSTANDING_ANALYZER_ID", default="ExpensesAnalyzer") or "ExpensesAnalyzer"
    embedding = _parse_connection_string(
        _first_env("ConnectionStrings__TextEmbedding3Large", "ConnectionStrings__textembedding3large", default="") or ""
    )

    sections = _first_env("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS")
    if sections:
        allowed = {"markdown", "fields"}
        requested = [s.strip().lower() for s in sections.split(",") if s.strip().lower() in allowed]
        if requested:
            settings.content_understanding_sections = requested

    settings.mcp_server_url = _first_env("MCP_SERVER_URL") or _discover_service_url("mcp-server")

    # Cosmos DB, used directly by the durable-memory provider (records still go
    # through the MCP server).
    cosmos_connection = (_first_env("ConnectionStrings__cosmos-db", default="") or "").strip()
    cosmos = _parse_connection_string(cosmos_connection)
    # Aspire publishes an endpoint for managed identity, but the emulator uses
    # AccountEndpoint/AccountKey. Neither form should require a production key.
    cosmos_endpoint = cosmos.get("accountendpoint")
    if not cosmos_endpoint and cosmos_connection.lower().startswith(("https://", "http://")):
        cosmos_endpoint = cosmos_connection
    settings.cosmos_endpoint = _first_env("COSMOS_ENDPOINT") or cosmos_endpoint
    settings.cosmos_key = _first_env("COSMOS_KEY") or cosmos.get("accountkey")
    settings.cosmos_database = _first_env("COSMOS_DATABASE", default="db") or "db"
    settings.memory_chat_model = _first_env("MEMORY_CHAT_MODEL") or settings.foundry_deployment
    settings.memory_embedding_model = _first_env("MEMORY_EMBEDDING_MODEL") or embedding.get("deployment") or embedding.get("model")
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
