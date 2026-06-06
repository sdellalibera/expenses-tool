# Expenses Tool Demo

A small PoC for an agent-driven expenses tool. A React frontend (designed for
phones) captures a photo of a receipt, uploads it to Azure Blob Storage via the
agent's API, and the agent uses Azure AI Content Understanding to extract the
expense data and reply with a summary.

## Components

| Project | Purpose |
| --- | --- |
| `src/aspire` | Aspire AppHost wiring Cosmos (emulator), Azure Storage (Azurite), Foundry, both MCP servers, the agent, and the frontend. |
| `src/expenses-agent` | ASP.NET Core agent host. Exposes `POST /chat` and `POST /expenses/report` (multipart image upload). |
| `src/content-understanding-mcpserver` | MCP server exposing analyzer tools: `ListAnalyzers`, `CreateAnalyzer`, `AnalyzeDocumentBytes`, `AnalyzeDocumentByUrl`. |
| `src/storage-mcpserver` | MCP server exposing blob tools: `ListBlobs`, `GetBlobReadUrl`, `GetBlobMetadata`, `DeleteBlob`. |
| `src/expenses-frontend` | React + Vite app with mobile camera capture (`<input capture="environment">`). |
| `src/shared-services` / `src/servicedefaults` | Shared session store and OpenTelemetry/Health defaults. |

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
```

## Running locally

```bash
cd src/aspire
aspire run
```

Aspire will start:

- Azurite (blob emulator) on port 27000 with a persistent `expenses` container.
- Cosmos DB preview emulator.
- The Content Understanding MCP server.
- The Storage MCP server (talks to Azurite).
- The expenses-agent API (CORS-enabled, externally reachable).
- The Vite dev server for `expenses-frontend` (also externally reachable).

### Using the frontend from your phone

1. Make sure your phone is on the same Wi-Fi network as your dev machine.
2. From the Aspire dashboard, copy the **external** URL exposed for `frontend`.
3. Open it on your phone, tap **Open camera**, snap a receipt, then tap
   **Create expense report**.
4. The frontend POSTs the image (multipart `image` field) to
   `POST /expenses/report` on the agent. The agent uploads it to the
   `expenses` blob container and asks the agent to:
   - call `ListAnalyzers` and select the receipts/invoice analyzer,
   - call `GetBlobReadUrl` to obtain a SAS URL,
   - call `AnalyzeDocumentByUrl`, and
   - summarize the result.

## API quick reference (expenses-agent)

- `POST /chat` — JSON `{ message, conversationId? }`. Free-form chat.
- `POST /expenses/report` — multipart form with:
  - `image` (required) — the captured photo
  - `note` (optional) — free-form note appended to the agent prompt
  - `conversationId` (optional) — continue an existing conversation
  Response: `{ conversationId, blobName, analysisSummary }`.
