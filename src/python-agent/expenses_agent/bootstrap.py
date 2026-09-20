"""Idempotent Content Understanding data-plane setup; never deploys Azure models.

Model deployments and the resource identity's access to them are provisioned by
Aspire. This module fills missing CU defaults and creates a missing analyzer,
waiting until both are readable before chat is made ready.
"""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable
from importlib.resources import files
import json
import logging
import math
from pathlib import Path
import time
from typing import Any, TypeVar

from azure.ai.contentunderstanding.aio import ContentUnderstandingClient
from azure.ai.contentunderstanding.models import ContentAnalyzer
from azure.core.exceptions import (
    ClientAuthenticationError,
    HttpResponseError,
    ResourceNotFoundError,
    ServiceRequestError,
    ServiceResponseError,
)
from azure.identity.aio import DefaultAzureCredential

from .config import Settings, load_settings

logger = logging.getLogger(__name__)
API_VERSION = "2025-11-01"
POLL_INTERVAL = 2.0
AUTH_PROPAGATION_SECONDS = 60.0
_T = TypeVar("_T")


class ContentUnderstandingBootstrapError(RuntimeError):
    """The configured analyzer cannot safely be used."""


def load_analyzer_definition() -> dict[str, Any]:
    packaged = files("expenses_agent").joinpath("ExpensesAnalyzer.json")
    source = Path(__file__).resolve().parents[2] / "analyzers" / "ExpensesAnalyzer.json"
    try:
        text = packaged.read_text(encoding="utf-8") if packaged.is_file() else source.read_text(encoding="utf-8")
        definition = json.loads(text)
        required = {"merchant", "category", "date", "totalAmount", "currency", "lineItems"}
        if not isinstance(definition, dict):
            raise ValueError("Definition must be a JSON object.")
        schema = definition.get("fieldSchema")
        fields = schema.get("fields") if isinstance(schema, dict) else None
        if not isinstance(fields, dict) or not required <= fields.keys():
            raise ValueError("Missing canonical camelCase expense fields.")
        if fields.keys() - (required | {"notes"}):
            raise ValueError("Only receipt fields belong in the analyzer; application metadata is supplied separately.")
        if set(definition) - {"description", "baseAnalyzerId", "config", "models", "fieldSchema"}:
            raise ValueError("Definition contains unsupported or exported read-only metadata.")
        ContentAnalyzer(definition)
        return definition
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise ContentUnderstandingBootstrapError(f"Invalid ExpensesAnalyzer definition: {exc}") from exc


def model_deployments(settings: Settings) -> dict[str, str]:
    return {
        "gpt-5": settings.content_understanding_completion_deployment,
        "gpt-5-mini": settings.content_understanding_mini_deployment,
        "text-embedding-3-large": settings.content_understanding_embedding_deployment,
        "prebuilt-analyzer-completion": settings.content_understanding_completion_deployment,
        "prebuilt-analyzer-completion-mini": settings.content_understanding_mini_deployment,
        "prebuilt-analyzer-embedding": settings.content_understanding_embedding_deployment,
    }


class _Bootstrap:
    def __init__(self, client: ContentUnderstandingClient, settings: Settings) -> None:
        self.client = client
        self.settings = settings
        self.started = time.monotonic()
        self.phase = "reading analyzer"

    async def call(self, operation: Callable[[], Awaitable[_T]], *, missing_ok: bool = False) -> _T | None:
        delay = POLL_INTERVAL
        while True:
            try:
                return await operation()
            except ResourceNotFoundError:
                if missing_ok:
                    return None
                # The resource endpoint can become reachable before its CU data plane.
            except ClientAuthenticationError as exc:
                if exc.status_code != 403 or time.monotonic() - self.started >= AUTH_PROPAGATION_SECONDS:
                    raise ContentUnderstandingBootstrapError(
                        f"Authentication/authorization failed while {self.phase}. "
                        "Check DefaultAzureCredential and the Cognitive Services User role on the Foundry resource."
                    ) from exc
            except HttpResponseError as exc:
                status = exc.status_code
                if status == 403 and time.monotonic() - self.started >= AUTH_PROPAGATION_SECONDS:
                    raise ContentUnderstandingBootstrapError(
                        f"Authorization failed while {self.phase} after allowing for RBAC propagation. "
                        "Grant Cognitive Services User to the application identity on the Foundry resource."
                    ) from exc
                if status not in {403, 408, 429, 500, 502, 503, 504}:
                    # A 409 is handled by the create-if-missing callers, not retried blindly.
                    raise
            except (ServiceRequestError, ServiceResponseError):
                pass
            logger.info("Content Understanding not yet available while %s; retrying in %.0fs", self.phase, delay)
            await asyncio.sleep(delay)
            delay = min(delay * 2, 10.0)

    async def run(self, definition: dict[str, Any]) -> None:
        analyzer = await self.call(
            lambda: self.client.get_analyzer(self.settings.analyzer_id), missing_ok=True
        )
        self.phase = "configuring model deployment defaults"
        required = model_deployments(self.settings)
        while True:
            defaults = await self.call(self.client.get_defaults)
            current = dict(defaults.model_deployments or {})
            missing = {name: deployment for name, deployment in required.items() if not current.get(name)}
            if not missing:
                break
            try:
                # The SDK sends a merge patch: other mappings are never removed.
                await self.call(lambda: self.client.update_defaults(model_deployments=missing))
            except HttpResponseError as exc:
                if exc.status_code != 409:
                    raise
                # Another startup may be initializing this shared resource; re-read.
            await asyncio.sleep(POLL_INTERVAL)

        if analyzer is None:
            self.phase = f"creating analyzer '{self.settings.analyzer_id}'"
            try:
                poller = await self.call(
                    lambda: self.client.begin_create_analyzer(
                        analyzer_id=self.settings.analyzer_id,
                        resource=ContentAnalyzer(definition),
                        allow_replace=False,
                    )
                )
                await self.call(poller.result)
            except HttpResponseError as exc:
                if exc.status_code != 409:
                    raise
                # Creation won by another process: never replace its analyzer.

        self.phase = f"waiting for analyzer '{self.settings.analyzer_id}' readiness"
        while True:
            analyzer = await self.call(
                lambda: self.client.get_analyzer(self.settings.analyzer_id), missing_ok=True
            )
            status = (
                str(getattr(analyzer.status, "value", analyzer.status)).lower()
                if analyzer is not None else "creating"
            )
            if status == "ready":
                logger.info("Content Understanding analyzer '%s' is ready", self.settings.analyzer_id)
                return
            if status not in {"creating", "none", ""}:
                raise ContentUnderstandingBootstrapError(
                    f"Analyzer '{self.settings.analyzer_id}' is '{status}', not ready. "
                    "Inspect its definition and model deployments; existing analyzers are not overwritten."
                )
            await asyncio.sleep(POLL_INTERVAL)


async def bootstrap_content_understanding(
    settings: Settings,
    credential: Any,
    *,
    client: ContentUnderstandingClient | None = None,
) -> None:
    """Ensure missing CU resources exist; safe for repeated/concurrent startups."""
    if not settings.content_understanding_endpoint:
        raise ContentUnderstandingBootstrapError("Content Understanding endpoint is not configured.")
    timeout = settings.content_understanding_bootstrap_timeout
    if not math.isfinite(timeout) or timeout <= 0:
        raise ContentUnderstandingBootstrapError("CONTENT_UNDERSTANDING_BOOTSTRAP_TIMEOUT must be finite and positive.")
    definition = load_analyzer_definition()

    async def ensure(active_client: ContentUnderstandingClient) -> None:
        bootstrap = _Bootstrap(active_client, settings)
        try:
            async with asyncio.timeout(settings.content_understanding_bootstrap_timeout):
                await bootstrap.run(definition)
        except TimeoutError as exc:
            raise ContentUnderstandingBootstrapError(
                f"Content Understanding bootstrap timed out after "
                f"{settings.content_understanding_bootstrap_timeout:g}s while {bootstrap.phase}. "
                "Check endpoint access, completed model deployments and RBAC assignments."
            ) from exc
        except HttpResponseError as exc:
            raise ContentUnderstandingBootstrapError(
                f"Content Understanding bootstrap failed while {bootstrap.phase} (HTTP {exc.status_code}): {exc}"
            ) from exc

    if client is not None:
        await ensure(client)
    else:
        async with ContentUnderstandingClient(
            settings.content_understanding_endpoint, credential, api_version=API_VERSION, retry_total=0
        ) as owned_client:
            await ensure(owned_client)


def main() -> None:
    async def run() -> None:
        async with DefaultAzureCredential() as credential:
            await bootstrap_content_understanding(load_settings(), credential)

    logging.basicConfig(level=logging.INFO)
    asyncio.run(run())


if __name__ == "__main__":
    main()
