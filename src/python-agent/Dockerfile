FROM python:3.13-slim-bookworm

WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

COPY python-agent/pyproject.toml python-agent/hatch_build.py ./
COPY python-agent/expenses_agent ./expenses_agent
COPY analyzers/ExpensesAnalyzer.json ./expenses_agent/ExpensesAnalyzer.json

RUN pip install --no-cache-dir --pre . \
    && useradd --create-home --uid 10001 --user-group appuser

USER appuser
ENTRYPOINT ["python", "-m", "expenses_agent.main"]
