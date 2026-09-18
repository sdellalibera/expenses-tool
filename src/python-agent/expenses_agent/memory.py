"""Durable cross-session memory backed by Azure Cosmos DB.

Uses the Agent Memory Toolkit through
:class:`agent_framework_azure_cosmos_memory.CosmosMemoryContextProvider`, which
plugs into the same ``before_run``/``after_run`` hooks the agent already uses:

* **before_run** – retrieves the facts and procedures relevant to the current
  turn (vector search over previous turns) and injects them into the context.
* **after_run** – stores the turn and, in the background, extracts long-lived
  memories, thread summaries and a user profile.

This is what makes the agent remember *across* conversations — for example that
a user always books the same hotel chain, or reports in EUR. It complements the
verbatim transcript kept by
:class:`~expenses_agent.history.McpConversationHistoryProvider`, which supplies
the last few literal turns so follow-ups like "yes, use that trip" still work.

The toolkit owns its own Cosmos containers (``memories_turns``,
``memories_summaries``, …) and talks to Cosmos **directly**, so unlike the trip
and expense records it does not travel through the MCP server.
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)


class CosmosMemory:
    """Owns the Cosmos memory provider and the client it may have created."""

    def __init__(self) -> None:
        self.provider: Any | None = None
        self._client: Any | None = None
        self.error: str | None = None

    @property
    def is_enabled(self) -> bool:
        return self.provider is not None

    async def start(self, settings) -> Any | None:
        """Build the provider, or return ``None`` and record why it was skipped."""
        if not settings.enable_cosmos_memory:
            logger.info("Cosmos durable memory is disabled (ENABLE_COSMOS_MEMORY=false)")
            return None

        missing = [
            name
            for name, value in (
                ("cosmos endpoint", settings.cosmos_endpoint),
                ("foundry endpoint", settings.foundry_endpoint),
                ("memory chat model", settings.memory_chat_model),
                ("memory embedding model", settings.memory_embedding_model),
            )
            if not value
        ]
        if missing:
            self.error = f"Cosmos durable memory not configured (missing: {', '.join(missing)})"
            logger.warning(self.error)
            return None

        try:
            self.provider = await self._build(settings)
        except Exception as exc:  # pragma: no cover - depends on live Azure/emulator
            self.error = f"Cosmos durable memory unavailable: {exc}"
            logger.warning(self.error, exc_info=True)
            await self.stop()
            return None

        logger.info(
            "Cosmos durable memory enabled (database=%s, chat=%s, embedding=%s)",
            settings.cosmos_database,
            settings.memory_chat_model,
            settings.memory_embedding_model,
        )
        return self.provider

    async def _build(self, settings) -> Any:
        from agent_framework_azure_cosmos_memory import CosmosMemoryContextProvider

        if settings.cosmos_key:
            # The local emulator only accepts its well-known key, and the provider
            # does not forward a key itself, so build the toolkit client here.
            from azure.cosmos.agent_memory.aio import AsyncCosmosMemoryClient

            self._client = AsyncCosmosMemoryClient(
                cosmos_endpoint=settings.cosmos_endpoint,
                cosmos_key=settings.cosmos_key,
                cosmos_database=settings.cosmos_database,
                ai_foundry_endpoint=settings.foundry_endpoint,
                embedding_deployment_name=settings.memory_embedding_model,
                chat_deployment_name=settings.memory_chat_model,
                use_default_credential=False,
            )
            await self._client.create_memory_store()

            return CosmosMemoryContextProvider(
                memory_client=self._client,
                top_k=settings.memory_top_k,
            )

        # Deployed: managed identity / DefaultAzureCredential for both Cosmos and Foundry.
        return CosmosMemoryContextProvider(
            cosmos_endpoint=settings.cosmos_endpoint,
            cosmos_database=settings.cosmos_database,
            foundry_endpoint=settings.foundry_endpoint,
            chat_model=settings.memory_chat_model,
            embedding_model=settings.memory_embedding_model,
            top_k=settings.memory_top_k,
        )

    async def stop(self) -> None:
        if self._client is not None:
            try:
                await self._client.close()
            except Exception:  # pragma: no cover - shutdown must not fail
                logger.debug("Error while closing the Cosmos memory client", exc_info=True)
            self._client = None
        self.provider = None
