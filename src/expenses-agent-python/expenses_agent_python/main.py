"""
Main FastAPI server for the Expenses Python agent.
"""
import logging
import os
from contextlib import asynccontextmanager
from typing import Any, Optional

import uvicorn
from dotenv import load_dotenv
from fastapi import FastAPI, File, Form, UploadFile
from fastapi.middleware.cors import CORSMiddleware

# OpenTelemetry imports
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

# Microsoft Agent Framework
from agent_framework.observability import configure_otel_providers

# Local imports
import sys
from pathlib import Path

# Ensure the parent directory is on sys.path so `tools` and this package
# resolve whether invoked as `python expenses_agent_python/main.py` or `python -m`.
_PARENT_DIR = Path(__file__).resolve().parent.parent
if str(_PARENT_DIR) not in sys.path:
    sys.path.insert(0, str(_PARENT_DIR))

from expenses_agent_python.agent_executor import ExpensesAgentExecutor  # noqa: E402

load_dotenv()

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def _configure_from_aspire_connection_string() -> None:
    """Map the Aspire GPT-5 connection string to the agent configuration."""
    connection_string = os.environ.get("ConnectionStrings__gpt5", "")
    values = {
        key.strip().lower(): value.strip()
        for part in connection_string.split(";")
        if "=" in part
        for key, _, value in (part.partition("="),)
    }

    endpoint = values.get("endpoint")
    deployment = values.get("deployment")
    if endpoint:
        os.environ.setdefault("FOUNDRY_ENDPOINT", endpoint)
    if deployment:
        os.environ.setdefault("FOUNDRY_DEPLOYMENT", deployment)


_configure_from_aspire_connection_string()


def create_app() -> FastAPI:
    """Create and configure the FastAPI application."""
    configure_otel_providers(enable_sensitive_data=True)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        executor = ExpensesAgentExecutor()
        async with executor:
            app.state.executor = executor
            yield

    app_instance = FastAPI(title="Expenses Agent", lifespan=lifespan)

    app_instance.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    @app_instance.get("/health")
    async def health():
        return {"status": "healthy", "service": "expensesagent"}

    @app_instance.post("/expenses/report")
    async def report(
        image: UploadFile = File(...),
        userId: str = Form(...),  # noqa: N803 — matches frontend contract
        note: Optional[str] = Form(default=None),
        conversationId: Optional[str] = Form(default=None),  # noqa: N803 — matches frontend contract
    ) -> dict[str, Any]:
        executor: ExpensesAgentExecutor = app_instance.state.executor
        data = await image.read()
        return await executor.report(
            image_bytes=data,
            content_type=image.content_type,
            filename=image.filename,
            user_id=userId,
            note=note,
            conversation_id=conversationId,
        )

    otel_endpoint = os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT")
    if otel_endpoint:
        trace.set_tracer_provider(TracerProvider())
        otlp_exporter = OTLPSpanExporter(endpoint=otel_endpoint)
        processor = BatchSpanProcessor(otlp_exporter)
        trace.get_tracer_provider().add_span_processor(processor)

    FastAPIInstrumentor().instrument_app(app_instance)

    return app_instance


app = create_app()


def main():
    """Main entry point for the application."""
    port = int(os.environ.get("PORT", 8000))
    host = os.environ.get("HOST", "0.0.0.0")

    logger.info(f"Expenses Agent starting on http://{host}:{port}")
    uvicorn.run(app, host=host, port=port, log_level="info")


if __name__ == "__main__":
    main()
