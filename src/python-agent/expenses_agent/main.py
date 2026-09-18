"""Entry point: ``python expenses_agent/main.py`` (used by the Aspire AppHost)."""

from __future__ import annotations

import logging
import os
import sys
from pathlib import Path

# Allow `python expenses_agent/main.py` as well as `python -m expenses_agent.main`.
_PARENT = Path(__file__).resolve().parent.parent
if str(_PARENT) not in sys.path:
    sys.path.insert(0, str(_PARENT))

from expenses_agent.api import create_app  # noqa: E402
from expenses_agent.config import load_settings  # noqa: E402

logger = logging.getLogger(__name__)

settings = load_settings()
app = create_app(settings)


def main() -> None:
    import uvicorn

    logger.info("Expenses agent listening on http://%s:%s", settings.host, settings.port)
    uvicorn.run(
        app,
        host=settings.host,
        port=settings.port,
        log_level=os.environ.get("LOG_LEVEL", "info").lower(),
        access_log=False,
    )


if __name__ == "__main__":
    main()
