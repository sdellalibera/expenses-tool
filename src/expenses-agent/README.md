# Expenses Agent (Python)

Python rewrite of the expenses agent using the **Microsoft Agent Framework
(MAF)**, targeted at **Azure AI Foundry** hosting. It analyzes receipt/invoice
images with **Azure AI Content Understanding** and uses two MCP servers as tools:

- **`expenses-sql`** — the SQL MCP Server (Data API builder) for `ExpenseReport`
  and `Expense` records.
- **`expenses-storage`** — the custom Storage MCP Server for receipt images.

> This is the primary expenses agent (it replaced the earlier C# version). It is
> wired into the Aspire AppHost and targets Azure AI Foundry hosting. See
> [`../../ARCHITECTURE.md`](../../ARCHITECTURE.md).

## Layout

```
expenses_agent/
  config.py      # env/service-discovery settings resolution
  agent.py       # builds the MAF Agent (FoundryChatClient + CU provider + MCP tools)
  app.py         # FastAPI host: POST /chat, POST /expenses/report
  __main__.py    # uvicorn entry point
  prompts/system_prompt.md
```

## Prerequisites

- Python 3.11+
- An Azure AI Foundry project with a chat model deployment
- An Azure AI Content Understanding resource
- `az login` locally (uses `DefaultAzureCredential`; managed identity when deployed)
- The SQL and Storage MCP servers reachable (run the Aspire AppHost, or set
  `SQL_MCP_URL` / `STORAGE_MCP_URL`)

## Run locally

```bash
cd src/expenses-agent
python -m venv .venv
. .venv/Scripts/Activate.ps1        # Windows PowerShell
pip install --pre -e .
copy .env.example .env               # then edit values
python -m expenses_agent
```

The API listens on `http://localhost:8080` by default (`/chat`,
`/expenses/report`, `/health`). Under the Aspire AppHost it runs via
`AddUvicornApp` and gets the MCP server URLs through service discovery.

## Notes / TODO

- **Managed identity roles**: grant the agent identity the Content Understanding
  role(s) and `Storage Blob Data Reader` + `Storage Blob Delegator` (see
  `ARCHITECTURE.md`).
- **MCP auth (Entra)**: when the MCP endpoints require Entra tokens, set
  `SQL_MCP_SCOPE` / `STORAGE_MCP_SCOPE` (e.g. `api://<app-id>/.default`); the agent
  acquires tokens with `DefaultAzureCredential` and sends them as `Authorization:
  Bearer`. When unset (local dev), no token is sent.
- **Foundry hosting**: package with `agent-framework-foundry-hosting` for the
  Foundry Agent Server (deployment wiring is a follow-up).
