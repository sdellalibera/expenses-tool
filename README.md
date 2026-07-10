# Expenses Tool Demo

A small PoC for an agent-driven expenses tool. A React frontend (designed for
phones) captures a photo of a receipt, uploads it to Azure Blob Storage via the
agent's API, and the agent extracts the expense data and replies with a summary.
Structured expense records are stored in SQL Server and exposed to agents through
a SQL MCP Server (Data API builder).

## Architecture

For the full architecture — including the target Foundry-hosted Python agent,
Content Understanding integration, both MCP servers, identity/security, and the
local vs. Azure environments — see [`ARCHITECTURE.md`](./ARCHITECTURE.md).

## Setup

New to this repo? Follow [`docs/SETUP.md`](./docs/SETUP.md) for the full,
in-order setup (tooling, Azure + Entra prerequisites, secrets, venv) before
running.

## Components

| Project | Purpose |
| --- | --- |
| `src/aspire` | Aspire AppHost wiring Cosmos (emulator), Azure Storage (Azurite), SQL Server, the SQL MCP Server, Foundry, the Storage MCP server, the agent, and the frontend. |
| `src/expenses-agent` | Python agent (Microsoft Agent Framework), Foundry-hosted target. Uses Content Understanding + the SQL/Storage MCP tools. Exposes `POST /chat` and `POST /expenses/report`. |
| `src/expenses-database` | SQL project: `schema.sql` (ExpenseReport + Expense tables) and `dab-config.json` for the SQL MCP Server. |
| `src/storage-mcpserver` | MCP server exposing blob tools: `save_blob`, `list_blobs`, `get_blob_read_url`, `get_blob_metadata`, `delete_blob`. |
| `src/frontend` | React + Vite app with mobile camera capture (`<input capture="environment">`). |
| `src/shared-services` / `src/servicedefaults` | Shared session store and OpenTelemetry/Health defaults. |

## Data model

Receipt/invoice images are kept in Azure Blob Storage; SQL stores only structured
data and a **reference** to each image (the `ReceiptBlobKey` column plus hash, size
and MIME metadata) — the image bytes are never stored in the database. Tables:

- `dbo.ExpenseReport` — groups the expenses submitted by a user.
- `dbo.Expense` — a single expense line item, referencing its report and its
  receipt blob.

See `src/expenses-database/schema.sql`.

## SQL database + SQL MCP Server

Modeled on the [Data API builder SQL MCP Server quickstart](https://learn.microsoft.com/en-us/azure/data-api-builder/mcp/quickstart-dotnet-aspire).

- **Locally** the AppHost runs SQL Server as a container (via **Podman**) with a
  persistent data volume; the schema in `src/expenses-database/schema.sql` is applied
  on first start through Aspire's creation script.
- **On deploy** the same resource is provisioned as an **Azure SQL Database**, and
  the `sql-mcp-server` Data API builder container deploys to **Azure Container Apps**.
- The `sql-mcp-server` container (`mcr.microsoft.com/azure-databases/data-api-builder`)
  reads `src/expenses-database/dab-config.json` and exposes the `ExpenseReport` and
  `Expense` entities as MCP tools (`/mcp`), plus REST (`/api`) and GraphQL (`/graphql`).

The `dab-config.json` is generated/edited with the DAB CLI (pinned in
`.config/dotnet-tools.json`):

```bash
dotnet tool restore
dotnet dab validate -c src/expenses-database/dab-config.json
```

> The agent is not yet wired to the SQL MCP tools — that integration is intentionally
> left for a later step.

## Configuration

Before running the Aspire AppHost, configure the required Azure settings and
parameters using `aspire secret set` from the `src/aspire` directory.

```bash
cd src/aspire

# Azure deployment settings
aspire secret set Azure:SubscriptionId "<your-subscription-id>"
aspire secret set Azure:ResourceGroup "<your-resource-group>"
aspire secret set Azure:Location "<your-azure-region>"

# Existing Foundry resource to reference
aspire secret set Parameters:existingFoundryName "<your-foundry-name>"
aspire secret set Parameters:existingFoundryResourceGroup "<foundry-resource-group>"

# Optional: Microsoft Entra auth for the MCP endpoints. When set, the MCP servers
# validate Entra tokens and the agent acquires them; when omitted, the MCP servers
# accept anonymous calls (local development).
aspire secret set Parameters:entraTenantId "<your-tenant-id>"
aspire secret set Parameters:sqlMcpAudience "api://<sql-mcp-app-id>"
aspire secret set Parameters:storageMcpAudience "api://<storage-mcp-app-id>"
aspire secret set Parameters:contentUnderstandingEndpoint "https://<cu-resource>.cognitiveservices.azure.com/"
```

> The Python `expenses-agent` runs via Aspire's `AddUvicornApp`, which uses the
> project's `.venv`. Create it once before `aspire run`:
> `cd ../expenses-agent; python -m venv .venv; .\.venv\Scripts\Activate.ps1; pip install --pre -e .`

## Running locally

```bash
cd src/aspire
aspire run
```

Aspire will start:

- Azurite (blob emulator) on port 27000 with a persistent `expenses` container.
- Cosmos DB preview emulator.
- SQL Server (container) with the `expensesdb` schema applied.
- The SQL MCP Server (Data API builder) exposing the expense entities.
- The Storage MCP server (talks to Azurite).
- The Python `expenses-agent` (uvicorn), externally reachable.
- The Vite dev server for `frontend` (also externally reachable).

### Using the frontend from your phone

1. Make sure your phone is on the same Wi-Fi network as your dev machine.
2. From the Aspire dashboard, copy the **external** URL exposed for `frontend`.
3. Open it on your phone, tap **Open camera**, snap a receipt, then tap
   **Create expense report**.
4. The frontend POSTs the image (multipart `image` field) to
   `POST /expenses/report` on the agent. The agent uploads it to the
   `expenses` blob container, analyzes it, and summarizes the result.

## API quick reference (expenses-agent)

- `POST /chat` — JSON `{ message, conversationId? }`. Free-form chat.
- `POST /expenses/report` — multipart form with:
  - `image` (required) — the captured photo
  - `note` (optional) — free-form note appended to the agent prompt
  - `conversationId` (optional) — continue an existing conversation
  Response: `{ conversationId, blobName, analysisSummary }`.
