"""Console entry point: run the FastAPI host with uvicorn."""

from __future__ import annotations

import uvicorn
from dotenv import load_dotenv

from .config import load_settings


def main() -> None:
    load_dotenv()
    settings = load_settings()
    uvicorn.run(
        "expenses_agent.app:app",
        host=settings.host,
        port=settings.port,
        log_level="info",
    )


if __name__ == "__main__":
    main()
