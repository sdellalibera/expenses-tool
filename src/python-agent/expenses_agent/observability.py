"""Logging and OpenTelemetry wiring for the expenses agent.

The Aspire AppHost injects ``OTEL_EXPORTER_OTLP_ENDPOINT``/``OTEL_EXPORTER_OTLP_PROTOCOL``,
so traces, metrics and logs land in the Aspire dashboard next to the .NET
resources. Agent Framework's ``configure_otel_providers`` instruments the agent,
its chat client and its tool calls; we additionally instrument FastAPI and httpx.
"""

from __future__ import annotations

import logging
import os
import sys

from opentelemetry import trace

_TRACER_NAME = "expenses-agent"

_configured = False


def configure(service_name: str = "expenses-agent", *, enable_sensitive_data: bool = True) -> None:
    """Configure structured logging and OpenTelemetry providers exactly once."""
    global _configured
    if _configured:
        return
    _configured = True

    logging.basicConfig(
        level=os.environ.get("LOG_LEVEL", "INFO").upper(),
        format="%(asctime)s %(levelname)-8s %(name)s: %(message)s",
        stream=sys.stdout,
        force=True,
    )
    # These are chatty and drown the interesting agent logs.
    logging.getLogger("azure.core.pipeline.policies.http_logging_policy").setLevel(logging.WARNING)
    logging.getLogger("azure.identity").setLevel(logging.WARNING)
    logging.getLogger("httpx").setLevel(logging.WARNING)
    # MCP requests may contain deterministic upload payloads, never log their bodies.
    logging.getLogger("mcp").setLevel(logging.WARNING)

    os.environ.setdefault("OTEL_SERVICE_NAME", service_name)

    try:
        from agent_framework.observability import configure_otel_providers

        configure_otel_providers(
            service_name=service_name,
            enable_sensitive_data=enable_sensitive_data,
        )
        logging.getLogger(__name__).info(
            "OpenTelemetry configured (endpoint=%s)", os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT", "<none>")
        )
    except Exception:  # pragma: no cover - telemetry must never break the app
        logging.getLogger(__name__).warning("Could not configure OpenTelemetry providers", exc_info=True)


def instrument_app(app) -> None:
    """Attach FastAPI + httpx instrumentation to the running application."""
    try:
        from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

        FastAPIInstrumentor.instrument_app(app, excluded_urls="health,alive")
    except Exception:  # pragma: no cover
        logging.getLogger(__name__).warning("Could not instrument FastAPI", exc_info=True)

    try:
        from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor

        HTTPXClientInstrumentor().instrument()
    except Exception:  # pragma: no cover
        logging.getLogger(__name__).warning("Could not instrument httpx", exc_info=True)


def tracer() -> trace.Tracer:
    """Tracer used for the agent's own spans (chat turns, MCP calls, CU calls)."""
    return trace.get_tracer(_TRACER_NAME)
