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
3. **Content Understanding** lives on the same AI Services account. Import the
   analyzer definition in [`../src/analyzers/ExpensesAnalyzer.json`](../src/analyzers/ExpensesAnalyzer.json)
   as an analyzer named **`ExpensesAnalyzer`**.

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

Everything else — the Cosmos connection string, the model deployment connection
string, service URLs and the OTLP endpoint — is injected by Aspire at run time.

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

Aspire starts, in order: the Cosmos preview emulator (Podman), the MCP server
(which creates the `db` database and the `trips` / `expenses` / `conversations`
containers), the Python agent, and the Vite frontend. Open the dashboard URL it
prints, then the **frontend** endpoint and go to `/home`.

To use it from a phone, open the frontend's external URL on the same network —
the Vite dev server proxies `/chat` and `/api` to the agent, so no extra
configuration is needed.

---

## 7. Verify

```bash
# MCP server (C#)
dotnet test src/mcp-server.tests/mcp-server.tests.csproj

# Agent (Python)
cd src/python-agent && .venv/Scripts/python -m pytest

# Frontend
cd src/frontend && npm test
```

The Python suite includes cross-language contract tests that run only when
`MCP_SERVER_URL` is set:

```powershell
$env:Cosmos__UseInMemory = "true"
dotnet run --project src/mcp-server/mcp-server.csproj --no-launch-profile --urls http://localhost:5290
# in another shell
cd src/python-agent
$env:MCP_SERVER_URL = "http://localhost:5290"
.venv/Scripts/python -m pytest tests/test_mcp_contract.py
```

Health checks:

* MCP server: `GET /health` → `Healthy`
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
| `unsupported operand type(s) for +: 'float' and 'datetime.timedelta'` | `mcp` 2.x is installed. The Agent Framework requires `mcp` 1.x — reinstall with `pip install "mcp>=1.29,<2"`. |
| Containers do not start | `podman machine start`, then confirm `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman` is set in `src/aspire/aspire.config.json`. |
