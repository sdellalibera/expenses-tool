# Setup Guide

Everything a new developer needs to configure this repository before running it,
in order. See [`../ARCHITECTURE.md`](../ARCHITECTURE.md) for how the pieces fit
together.

---

## 1. Prerequisites (tooling)

| Tool | Version | Install (Windows) |
| --- | --- | --- |
| .NET SDK | 10.0+ | `winget install Microsoft.DotNet.SDK.10` |
| Python | 3.11+ | `winget install Python.Python.3.13` |
| Node.js + npm | LTS | `winget install OpenJS.NodeJS.LTS` |
| Podman (container runtime) | 5.x | `winget install RedHat.Podman` |
| Azure CLI | 2.7x+ | `winget install Microsoft.AzureCLI` |
| Aspire CLI | 13.4+ | `dotnet tool install -g aspire.cli` (or the installer from aspire.dev) |

> This repo uses **Podman** as the container runtime. After installing, run
> `podman machine init` and `podman machine start` once. Aspire auto-detects
> Podman when Docker is absent.

Restore the repo-local **Data API builder CLI** (pinned in `.config/dotnet-tools.json`):

```bash
dotnet tool restore
```

---

## 2. Azure prerequisites

You need, in your own Azure subscription / Entra tenant:

1. An **Azure AI Foundry** project with a **chat model deployment** (e.g. `gpt-5`
   or `gpt-4.1`). Note the **project endpoint** (`https://<name>.services.ai.azure.com/`).
2. **Azure AI Content Understanding**, which is part of the Foundry / AI Services
   resource. Note its **cognitive-services endpoint**
   (`https://<name>.cognitiveservices.azure.com/`) and ensure the required CU
   default model deployments exist (GPT-4.1, GPT-4.1-mini, text-embedding-3-large).
3. Permission to **create an app registration** in your Entra tenant (below).

Sign in:

```bash
az login
```

---

## 3. Create the Entra app registration (MCP audience)

The MCP servers are reached by the Foundry-hosted agent over **public ingress**,
so their `/mcp` endpoints validate **Entra JWTs**. This app registration defines
the **audience** the servers validate and the agent requests a token for.

Aspire/`azd` provisions managed identities + RBAC automatically, but it does **not**
create this app registration — do it once:

```bash
# Create the app registration that represents the MCP API (the audience)
appId=$(az ad app create --display-name "expenses-mcp" --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad app update --id "$appId" --identifier-uris "api://$appId"
az ad sp create --id "$appId"          # service principal so tokens can target it

tenantId=$(az account show --query tenantId -o tsv)
echo "Audience (client-id): $appId"
echo "Tenant:               $tenantId"
```

> **Audience format:** Entra **v2.0** tokens carry `aud = <client-id GUID>`, not the
> `api://…` URI. Use the **client-id GUID** as the audience value below; the agent
> requests `<client-id>/.default` and the token's `aud` matches what the servers validate.

**Enable local development tokens.** When you run locally, the agent authenticates
as *you* (`az login`), not a managed identity. For your user token to be accepted by
the MCP servers, the app must expose a delegated scope and pre-authorize the Azure
CLI (client `04b07795-8ddb-461a-bbee-02f9e1bf7b46`) so `<client-id>/.default` works
without an interactive consent prompt (otherwise you get `AADSTS65001`). Add a
delegated scope `access_as_user`, set `requestedAccessTokenVersion = 2`, and add the
Azure CLI to `preAuthorizedApplications` via two `az rest PATCH` calls to
`https://graph.microsoft.com/v1.0/applications/$objectId` (scope first, then the
pre-authorization). When deployed, the agent's **managed identity** obtains an
app-only token instead — no user consent involved.

You can reuse this one registration for both MCP servers (default), or create a
second one for tighter isolation.

---

## 4. Configure AppHost secrets

All configuration is stored as AppHost **user secrets**. Run from `src/aspire`:

```bash
cd src/aspire

# --- Azure deployment target ---
aspire secret set Azure:SubscriptionId  "<your-subscription-id>"
aspire secret set Azure:ResourceGroup   "<your-resource-group>"
aspire secret set Azure:Location        "<your-azure-region>"

# --- Foundry ---
aspire secret set Parameters:existingFoundryName          "<your-foundry-name>"
aspire secret set Parameters:existingFoundryResourceGroup "<foundry-resource-group>"
```

The AppHost sources the **agent + MCP configuration from environment variables**
(read via `builder.Configuration`, so they show up on each resource in the Aspire
dashboard). Set them either as real environment variables or as AppHost secrets
(secrets populate the same configuration):

```bash
# Foundry / model
aspire secret set FOUNDRY_PROJECT_ENDPOINT "https://<name>.services.ai.azure.com/"
aspire secret set FOUNDRY_MODEL            "gpt-5"

# Content Understanding
aspire secret set AZURE_CONTENTUNDERSTANDING_ENDPOINT "https://<name>.cognitiveservices.azure.com/"

# Entra auth for the MCP endpoints (from step 3). Audience = app client-id GUID.
aspire secret set AZURE_AD_TENANT_ID "<tenant-id>"
aspire secret set AZURE_AD_AUDIENCE  "<app-client-id>"
aspire secret set AZURE_AD_ISSUER    "https://login.microsoftonline.com/<tenant-id>/v2.0"
aspire secret set SQL_MCP_SCOPE      "<app-client-id>/.default"
aspire secret set STORAGE_MCP_SCOPE  "<app-client-id>/.default"
```

**How each environment variable is used:**

| Env variable | Used by | Effect |
| --- | --- | --- |
| `AZURE_AD_AUDIENCE` + `AZURE_AD_ISSUER` | SQL MCP (DAB) | validate Entra tokens on `/mcp` |
| `AZURE_AD_TENANT_ID` + `AZURE_AD_AUDIENCE` | Storage MCP | JWT bearer auth + `RequireAuthorization()` on `/mcp` |
| `SQL_MCP_SCOPE` / `STORAGE_MCP_SCOPE` | Agent | scope the agent requests a token for (`<aud>/.default`) |
| `AZURE_CONTENTUNDERSTANDING_ENDPOINT` | Agent | Content Understanding endpoint |
| `FOUNDRY_PROJECT_ENDPOINT` / `FOUNDRY_MODEL` | Agent | Foundry chat client |

> The AppHost contains **no hard-coded values** — every resource env value comes
> from an environment variable (`builder.Configuration["..."]`). If an Entra
> variable is empty, that MCP server accepts anonymous calls (handy for local dev);
> set them to require Entra tokens. The agent acquires tokens automatically via
> managed identity / `DefaultAzureCredential`.

---

## 5. Python agent virtual environment

The Python `expenses-agent` runs via Aspire's `AddPythonApp`, which uses the
`.venv` in the agent directory. Create it once:

```bash
cd src/expenses-agent-python
python -m venv .venv
. .venv/Scripts/Activate.ps1          # Windows PowerShell (use .venv/bin/activate on macOS/Linux)
python -m pip install --upgrade pip
python -m pip install --pre -r requirements.txt # --pre: Content Understanding packages are pre-release
```

The agent authenticates with `DefaultAzureCredential`, so `az login` (step 2) is
enough locally.

---

## 6. Frontend dependencies

```bash
cd src/frontend
npm install
```

(Aspire also restores these on first run.)

---

## 7. Run locally

```bash
cd src/aspire
aspire run
```

Aspire (on Podman) starts: SQL Server + schema, the SQL MCP Server (DAB), the
Storage MCP Server + Azurite, the Cosmos emulator, the Python agent (uvicorn), and
the Vite frontend. Open the dashboard link it prints; use the **external** URL for
`frontend` from your phone.

---

## 8. Deploy to Azure

```bash
cd src/aspire
aspire deploy
```

This provisions **Azure SQL Database**, **Azure Container Apps** (SQL MCP, Storage
MCP, agent, frontend), **Azure Storage**, and creates **managed identities + RBAC**
automatically.

**After the first deploy — grant the agent's managed identity access:**

- **Blob**: `Storage Blob Data Reader` + `Storage Blob Delegator` (add
  `Storage Blob Data Contributor` if the agent must upload/delete).
- **Content Understanding**: the CU role(s) on the AI Services resource
  ([What's new](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/whats-new)).

(Aspire assigns the SQL DB user automatically via its deployment script.)

---

## 9. Optional hardening

- **Restrict MCP callers to the agent only:** define an **app role** on the
  `expenses-mcp` registration and assign it to the agent's managed identity, then
  check the role on the servers (currently they validate `aud` + `iss` only).
- **Lock down the SQL MCP:** remove the `anonymous` permission from the entities in
  `src/expenses-database/dab-config.json` so only authenticated (Entra) calls work.
- **Prefer managed identity everywhere** (already the default); avoid client secrets.
