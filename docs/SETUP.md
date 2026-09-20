# Setup Guide

Everything needed to run this repository locally, in order. See
[`../README.md`](../README.md) for how the pieces fit together.

---

## 1. Prerequisites

| Tool | Version | Install (Windows) |
| --- | --- | --- |
| .NET SDK | 10.0+ | `winget install Microsoft.DotNet.SDK.10` |
| Python | 3.11+ (3.13/3.14 tested) | `winget install Python.Python.3.13` |
| Node.js + npm | LTS | `winget install OpenJS.NodeJS.LTS` |
| Podman | 5.x+ | `winget install RedHat.Podman` |
| Azure CLI | 2.7x+ | `winget install Microsoft.AzureCLI` |
| Aspire CLI | 13.5+ | `curl -sSL https://aspire.dev/install.sh \| bash` (or the Windows installer) |

This repo uses **Podman** as the container runtime. After installing:

```bash
podman machine init
podman machine start
```

The AppHost launch profiles already set `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman`
(see `src/aspire/aspire.config.json`), so Aspire uses Podman even though Docker
is not installed.

> **Windows on ARM:** create the Python virtual environment with an **x64**
> interpreter. Several dependencies (`cryptography`, `grpcio`, `pydantic-core`)
> ship no `win_arm64` wheels and would otherwise try to build from source.

---

## 2. Azure prerequisites

1. Sign in: `az login`.
2. You need permission to create (or reuse) an **Azure AI Foundry** account in a
   resource group. The AppHost provisions the account, a project and three model
   deployments (`gpt5`, `gpt5mini`, `TextEmbedding3Large`) on first run.
3. **Content Understanding** lives on the same AI Services account. The agent
   automatically creates **`ExpensesAnalyzer`** from
   [`../src/analyzers/ExpensesAnalyzer.json`](../src/analyzers/ExpensesAnalyzer.json)
   if missing, after the Foundry project and model deployments are ready. Its
   startup waits for analyzer creation before accepting chat. The identity used
   by `DefaultAzureCredential` must be allowed to manage Content Understanding
   analyzers and model defaults, not just run inference.

---

## 3. AppHost configuration

All configuration is stored as AppHost **user secrets**. Run from `src/aspire`:

```bash
cd src/aspire

# Where the Azure resources are provisioned
aspire secret set Azure:SubscriptionId "<your-subscription-id>"
aspire secret set Azure:ResourceGroup  "<your-resource-group>"
aspire secret set Azure:Location       "<your-azure-region>"
aspire secret set Azure:TenantId       "<your-tenant-id>"

# Content Understanding endpoint (the AI Services account, with trailing slash)
aspire secret set Parameters:contentUnderstandingEndpoint "https://<name>.services.ai.azure.com/"
```

Everything else — Cosmos and Blob Storage connection information, model
deployment connection strings, service URLs and the OTLP endpoint — is injected
by Aspire at run time using `.WithReference()`. Do not copy storage account keys
into configuration. Azurite stores receipts in a persistent local volume;
published infrastructure creates a private `receipt-images` blob container.
Only MCP and the read API receive Blob Storage access.

> User secrets are keyed by the **path** of `apphost.cs`, so a fresh clone or a
> git worktree starts with an empty secret store. Run `aspire secret list` to check.

---

## 4. Python virtual environment

Aspire's `AddPythonApp` uses the `.venv` inside the agent directory.

```bash
cd src/python-agent
py -3.13 -m venv .venv                 # use an x64 interpreter on ARM devices
.venv/Scripts/python -m pip install --upgrade pip
.venv/Scripts/python -m pip install --pre -e .
```

`--pre` is required: the Content Understanding and Cosmos memory packages are
pre-release. The agent authenticates with `DefaultAzureCredential`, so `az login`
is enough locally.

Local Aspire runs disable cross-conversation durable memory because the preview
Cosmos emulator rejects the memory toolkit's vector/full-text indexing policy.
Trips, expenses, and conversation transcripts still persist through the MCP server.
Durable memory remains enabled for deployment and requires a Cosmos DB backend
supporting the toolkit's indexes. To test it locally against such a backend,
replace the emulator connection, enable `ENABLE_COSMOS_MEMORY`, and add the
agent's `.WithReference(cosmos)` in run mode as well as publish mode.

### Analyzer initialization

The startup initializer is also available as a standalone command, using the
same environment and Azure identity as the agent:

```powershell
# From src/python-agent, with the Content Understanding endpoint configured
.\.venv\Scripts\python -m expenses_agent.bootstrap
# Equivalent installed entry point: expenses-analyzer-bootstrap
```

It checks the analyzer, fills missing Content Understanding model-default
mappings, creates the analyzer only if absent, and waits for readiness. It does
not deploy models or replace an existing analyzer/default mapping. The checked-in
definition is bundled in both Python wheels and source distributions.
The AppHost's hosted-agent image also copies it explicitly from `src/analyzers`;
the custom `src/python-agent/Dockerfile` builds with `src` as its context.

| Variable | Default / Aspire source |
| --- | --- |
| `CONTENT_UNDERSTANDING_ANALYZER_ID` | `ExpensesAnalyzer` |
| `CONTENT_UNDERSTANDING_BOOTSTRAP_TIMEOUT` | `300` seconds |
| `CONTENT_UNDERSTANDING_COMPLETION_DEPLOYMENT` | `ConnectionStrings__gpt5` deployment, otherwise `gpt5` |
| `CONTENT_UNDERSTANDING_MINI_DEPLOYMENT` | `ConnectionStrings__gpt5mini` deployment, otherwise `gpt5mini` |
| `CONTENT_UNDERSTANDING_EMBEDDING_DEPLOYMENT` | `ConnectionStrings__TextEmbedding3Large` deployment, otherwise `TextEmbedding3Large` |

Explicit deployment overrides take precedence over Aspire references. Keep the
endpoint on the same Foundry account as these deployments. If an older analyzer
already exists, update its schema deliberately or use a new analyzer ID; startup
will not silently overwrite it.

---

## 5. Frontend dependencies

```bash
cd src/frontend
npm install
npm approve-scripts esbuild     # npm 11+ asks before running install scripts
```

---

## 6. Run

```bash
cd src/aspire
aspire run
```

Aspire starts the Cosmos preview emulator and Azurite (Podman), then the MCP server
(which creates the `db` database and the `trips` / `expenses` / `conversations`
containers and private `receipt-images` blob container), the read API, Python
agent, and Vite frontend. Analyzer initialization runs automatically during the
agent's first startup and checks readiness on subsequent starts; existing
analyzers are not replaced. Open the dashboard URL it
prints, then the **frontend** endpoint and go to `/home`.

To use it from a phone, open the frontend's external URL on the same network —
the Vite dev server routes `/chat`, `/commands` and `/health` to the agent and
`/api` to the read API, so no extra configuration is needed. When running
services outside Aspire, `VITE_AGENT_API_URL` selects the agent (default
`http://localhost:8000`) and `VITE_RECORDS_API_URL` selects the read API (default
`http://localhost:5291`). For direct cross-origin calls, `ALLOWED_ORIGINS` can
restrict the origins accepted by each service (comma-separated); the demo
defaults to any origin, and the read API allows only GET operations.

---

## 7. Build and smoke run

```powershell
# From the repository root
dotnet build .\src.slnx
dotnet build .\src\aspire\apphost.cs
.\src\python-agent\.venv\Scripts\python -m compileall -q .\src\python-agent\expenses_agent
cd .\src\frontend
npm run build
```

After starting Aspire, upload a receipt in `/home`, assign it to a trip, and
open its expense detail. Confirm the photo still appears after reloading.
Trips, expenses and conversation reads should target the `expenses-api`
service; only chat and delete commands should target Python. Original photos
remain in storage when a trip/expense is deleted, and can be explicitly removed
with the MCP `delete_receipt_image` tool once no expense references them.

`Cosmos:UseInMemory=true` is an isolated, process-local development option, not
a replacement for Cosmos when both services run: their in-memory stores are
separate. Aspire always connects both services to the same Cosmos database.

Health checks:

* MCP server: `GET /health` → `Healthy`
* Read API: `GET /health` → `Healthy`
* Agent: `GET /health` → JSON including `agentReady`, `durableMemory` and the
  resolved endpoints. Use it to confirm the Foundry, Content Understanding and
  MCP endpoints were injected correctly.

---

## 8. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `Cannot add resource ... with name 'expenses'` | Aspire resource names are unique app-wide. The Foundry project is `expenses`, so the Cosmos container resource is registered as `expense-records`. |
| Agent `/health` never responds | Something else is holding its port. Stop stray `python`/`ExpensesMcpServer` processes from a previous run and restart. |
| `Tool X has an output schema but did not return structured content` | An MCP tool that can return `null` must not declare an output schema — see the comments on `get_trip` / `get_expense` / `get_conversation`. |
| `Failed to parse indexing policy` | The Cosmos **preview emulator** does not support the vector index the memory toolkit creates. The agent falls back automatically; set `ENABLE_COSMOS_MEMORY=false` to silence it. |
| Chat returns HTTP 429 | The Foundry model deployment is rate limited. Wait, or raise the deployment's quota. |
| Analyzer bootstrap fails | Check Content Understanding endpoint, analyzer-management permissions, and the three model deployments. Existing analyzers are not overwritten automatically; deliberately update an older analyzer's schema using the checked-in definition. |
| Receipt photo is not available on an old expense | Records created before blob persistence have no `photoUrl`; original image bytes cannot be recovered from a filename. Upload the receipt again to store it. |
| Blob access fails | Check the `receipt-storage` emulator and MCP health; in Azure, MCP needs Blob Data Contributor and the read API needs Blob Data Reader. The container must remain private. |
| `unsupported operand type(s) for +: 'float' and 'datetime.timedelta'` | `mcp` 2.x is installed. The Agent Framework requires `mcp` 1.x — reinstall with `pip install "mcp>=1.29,<2"`. |
| Containers do not start | `podman machine start`, then confirm `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman` is set in `src/aspire/aspire.config.json`. |
