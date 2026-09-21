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
| Dev tunnel CLI | Latest | `winget install Microsoft.devtunnel` |

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
   deployments (`gpt5-4` for `gpt-5.4`, `gpt5-4-mini` for `gpt-5.4-mini`, and
   `TextEmbedding3Large`) on first run.
3. **Content Understanding** lives on the same AI Services account. The AppHost's
   PowerShell `analyzer-setup` resource creates **`ExpensesAnalyzer`** from
   [Expenses.json](../src/analyzers/Expenses.json) if missing, after the model
   deployments are ready. Python starts independently of this script. The Azure
   CLI identity must have permission to manage Content Understanding analyzers
   and model defaults (for example, **Cognitive Services User** on the account).
   The agent still uses `DefaultAzureCredential` for inference.

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
Only MCP and the read API receive Blob Storage access. The PowerShell setup
resource receives the Content Understanding endpoint via `.WithArgs()`; script
environment annotations are not projected into the in-process runspace.

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

The AppHost uses the
[Aspire PowerShell hosting integration](https://aspire.dev/integrations/frameworks/powershell/powershell-host/)
to run [Initialize-ExpensesAnalyzer.ps1](../scripts/Initialize-ExpensesAnalyzer.ps1)
as the finite `analyzer-setup` resource. Its output appears in the Aspire dashboard
logs. The script uses the existing Azure CLI login to obtain a Cognitive Services
token; it does not store or log the token and does not prompt for login.

You can also run it from PowerShell 7 after the model deployments are provisioned:

```powershell
# From the repository root, after az login
.\scripts\Initialize-ExpensesAnalyzer.ps1 `
   -ContentUnderstandingEndpoint "https://<name>.services.ai.azure.com/"
```

If the analyzer exists, the script prints `Analyzer already exists: ExpensesAnalyzer.`
and returns without writes. Otherwise it fills missing model-default mappings,
creates the analyzer with `allowReplace=false`, waits until it is ready and prints
`Analyzer ready: ExpensesAnalyzer.` Authentication, definition and creation errors
fail setup but do not prevent the agent from starting. Receipt analysis still
requires a successfully provisioned analyzer. Transient HTTP failures are retried
within the configured timeout.

| Script parameter | Default / Aspire source |
| --- | --- |
| `ContentUnderstandingEndpoint` | Required; AppHost `contentUnderstandingEndpoint` parameter |
| `AnalyzerPath` | `src/analyzers/Expenses.json`, resolved to an absolute path by the AppHost |
| `AnalyzerId` | `ExpensesAnalyzer`; also passed to Python as `CONTENT_UNDERSTANDING_ANALYZER_ID` |
| `CompletionDeployment` | AppHost chat deployment name, `gpt5-4` |
| `MiniDeployment` | AppHost mini deployment name, `gpt5-4-mini` |
| `EmbeddingDeployment` | AppHost embedding deployment name, `TextEmbedding3Large` |
| `TimeoutSeconds` | `300` seconds |

Use the Content Understanding **account** endpoint, not the Foundry project URL.
Keep it on the same account as the model deployments. The script does not deploy
models or overwrite existing mappings. If an older analyzer exists, update its
schema and completion model to `gpt-5.4` deliberately, or change the AppHost's
`AnalyzerId` constant to create a new one and keep Python in sync. Existing
`prebuilt-analyzer-completion` and `prebuilt-analyzer-completion-mini` default
mappings also need to be updated to `gpt5-4` and `gpt5-4-mini`, respectively.

**Hosted deployments:** Aspire excludes PowerShell resources from the deployment
manifest. Run this same script as a provisioning step before starting the agent
on a new account. The Python agent no longer creates analyzers or model mappings.
The analyzer definition stays in the repository for this provisioning step; it
is not bundled in Python distributions or copied into the agent image. The
runtime only needs the CU endpoint and remote analyzer ID, `ExpensesAnalyzer`.

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

Aspire starts the Cosmos preview emulator and Azurite (Podman) and creates the
`db` database, its `trips` / `expenses` / `conversations` containers, and the private
`receipt-images` blob container from the AppHost declarations. The MCP server and
read API start independently once storage is ready. Python waits for MCP and the
chat model; its memory-model waits only apply when publishing with durable memory
enabled. Vite starts without waiting for the backends, so early requests can fail
until the corresponding service is ready; reload the page after startup if needed.
Independently of Python startup, `analyzer-setup` runs once per AppHost run after
the model deployments are ready; existing analyzers are reported without
modification. Wait for successful setup before uploading receipts to a new account.
Open the dashboard URL it prints, then the **frontend** endpoint and go to `/home`.

When running services outside Aspire, provision the database and private containers
beforehand. The services no longer create them on startup. Azure deployments keep
blob public access disabled at the storage-account level.

### Phone access over HTTPS

Sign in once with `devtunnel user login` before starting Aspire. Local runs add
an authenticated `frontend-tunnel` resource using Aspire's Dev Tunnels integration.
The dashboard's **frontend** resource keeps both ways to reach the app:

* The original `http://localhost:<port>` endpoint for your computer.
* **Phone (HTTPS):** the `https://...devtunnels.ms/home` link for your phone.

The **frontend-tunnel** resource also shows this link as **Frontend (HTTPS)**
in its URLs section.

Open the HTTPS tunnel URL on your phone, sign in with the same account used by
the dev tunnel CLI, and go to `/home`. Allow camera access when prompted to take
a receipt photo. HTTPS enables browser camera access; the phone does not need
to be on the same network as your computer.

Only the frontend endpoint is tunneled. Vite still routes `/chat` and `/health`
to the agent and `/api` to the read API through same-origin requests. Anonymous
access is not enabled because this demo can read and modify expense records.
Stop `frontend-tunnel` in the dashboard or stop Aspire when the demo is finished.
The tunnel is local-development-only and is not included when publishing.

When running services outside Aspire, `VITE_AGENT_API_URL` selects the agent (default
`http://localhost:8000`) and `VITE_RECORDS_API_URL` selects the read API (default
`http://localhost:5291`). For direct cross-origin calls, `ALLOWED_ORIGINS` can
restrict the origins accepted by each service (comma-separated); the demo
defaults to any origin, and the read API allows only GET operations.

---

## 7. Build and smoke run

```powershell
# From the repository root
dotnet build .\src.slnx -c Release
dotnet build .\src\aspire\apphost.cs -c Release
.\src\python-agent\.venv\Scripts\python -m compileall -q .\src\python-agent\expenses_agent
npm --prefix .\src\frontend run build
```

The previous unit-test suites have been removed. These build and syntax checks
do not exercise live Foundry, MCP or storage; use the smoke workflow below and
the [README checklist](../README.md#build-and-smoke-check). See the
[agent file guide](../README.md#agent-file-guide) for each module's responsibility.

After pulling the optimization changes, reinstall the Python package to pick up
the explicit `httpx2` and `tiktoken` dependencies, then restart both MCP and Python
(normally by restarting Aspire). The new checkpoint MCP tool must be running
before submitting receipts. See [Token budgets and retries](../README.md#token-budgets-and-retries)
for tuning settings. The first tokenizer initialization may download the public
encoding data; prewarm `tiktoken.get_encoding("o200k_base")` in restricted environments.

After starting Aspire, upload a receipt in `/home`, assign it to a trip, and
open its expense detail. Confirm the photo still appears after reloading.
Trips, expenses and conversation reads should target the `expenses-api`
service; only chat and health requests should target Python. Request creates,
updates and deletions in chat, and confirm the corresponding MCP tool call and
the resulting record state through the read API. Original photos
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
| Chat returns HTTP 429 | Inspect `receipt.analyze` and `model.http` to identify the failing stage. Honor `Retry-After`, inspect effective limit headers and usage, then adjust the relevant budget or quota. The draft/photo are retained and completed receipt processing is reused on retry in the same conversation. |
| Output budget exhausted | Review records before retrying, then raise `MODEL_MAX_OUTPUT_TOKENS` for larger receipts. Some model-selected tools may already have succeeded. |
| History budget exceeded | Increase `MAX_HISTORY_TOKENS` or start a new conversation; latest-turn receipt JSON is never truncated silently. |
| `analyzer-setup` fails | Read its Aspire resource logs. Check `az login`, the Content Understanding account endpoint, analyzer-management permissions and the three model deployments. Existing analyzers are not overwritten automatically; deliberately update an older analyzer's schema using the checked-in definition. |
| Receipt photo is not available on an old expense | Records created before blob persistence have no `photoUrl`; original image bytes cannot be recovered from a filename. Upload the receipt again to store it. |
| Blob access fails | Check the `receipt-storage` emulator and MCP health; in Azure, MCP needs Blob Data Contributor and the read API needs Blob Data Reader. The container must remain private. |
| `unsupported operand type(s) for +: 'float' and 'datetime.timedelta'` | `mcp` 2.x is installed. The Agent Framework requires `mcp` 1.x — reinstall with `pip install "mcp>=1.29,<2"`. |
| Containers do not start | `podman machine start`, then confirm `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman` is set in `src/aspire/aspire.config.json`. |
