# AzRadar Infrastructure (Bicep)

Infrastructure-as-Code for the **VNet-protected** AzRadar platform.

## What this deploys

| Resource | Purpose |
|----------|---------|
| Virtual network (`az-radar-vnet`) | Private network with 3 subnets |
| `snet-private-endpoints` | Hosts the Cosmos DB private endpoint |
| `snet-app-api` (delegated) | Regional VNet integration for the API app |
| `snet-app-job` (delegated) | Regional VNet integration for the JobHost app |
| Private DNS zone `privatelink.documents.azure.com` | Resolves Cosmos to its private IP |
| Cosmos DB (`*-cosmos-*`) | Serverless, **AAD-only**, **public access disabled**, private endpoint only |
| Cosmos SQL DB + 8 containers | `crawl-jobs`, `feed-items`, `change-feed-leases`, `watchlist`, `doc-insights`, `app-config`, `blast-radius-results` (`/id`), `job-diagnostics` (`/jobId`) |
| App Service plans (`*-api-plan`, `*-job-plan`) | 2× B1 Linux |
| Web apps (`*-api`, `*-jobhost`) | Containerized (Docker Hub), VNet-integrated, UAMI-assigned |
| User-assigned managed identity (`*-uami`) | Created by this template; used for Cosmos + LLM auth |
| Cosmos data-plane role assignments | Built-in **Cosmos DB Data Contributor** for the created identity |

## Network design

```
                    Internet (public)
                          │
              ┌───────────▼───────────┐        ┌──────────────────────────┐
              │  az-radar-api (HTTPS)  │        │  Azure OpenAI (LLM)       │
              │  az-radar-jobhost      │───────▶│  PUBLIC — other team owns │
              └───────────┬───────────┘  UAMI  └──────────────────────────┘
                          │ vnetRouteAllEnabled
            ┌─────────────▼──────────────── VNet 10.20.0.0/16 ───────────────┐
            │  snet-app-api (delegated)   snet-app-job (delegated)            │
            │                                                                 │
            │            snet-private-endpoints                               │
            │                    │ Private Endpoint (Sql)                     │
            │                    ▼                                            │
            │            Cosmos DB  (publicNetworkAccess = Disabled)          │
            └─────────────────────────────────────────────────────────────────┘
```

* Both web apps route **all** outbound traffic through the VNet, so the Cosmos
  private-endpoint DNS resolves and traffic stays private.
* The LLM endpoint is intentionally **not** in the VNet — it is reached over the
  public internet using the same managed identity.

## Authentication — no keys

* Cosmos DB has `disableLocalAuth = true` (account keys disabled).
* Apps authenticate to **Cosmos DB** and **Azure OpenAI** with a
  **user-assigned managed identity** via `DefaultAzureCredential`.
* This template **creates the identity** and grants it the **Cosmos DB Built-in
  Data Contributor** data-plane role at the account scope automatically.
* The **LLM (Azure OpenAI / AI Foundry) role assignment is NOT done here** — that
  resource is owned by another team and may live in another subscription where
  this deployment has no permission to assign roles. Grant it manually (below).

> The database and all containers are pre-created at the control plane because
> the data-plane role cannot create databases/containers — the app's
> `CreateIfNotExists` calls then succeed as no-ops.

## ⚠️ Manual step — grant the identity access to the LLM

After deployment, grant the created UAMI access to the Azure OpenAI resource
using the deployment outputs (`managedIdentityPrincipalId`):

```powershell
# Read the outputs
$pid = az deployment group show -g <rg> -n <deployment> `
  --query properties.outputs.managedIdentityPrincipalId.value -o tsv

# Grant "Cognitive Services OpenAI User" on the AI resource (run by the team
# that owns it, or anyone with Owner/User Access Administrator on that scope)
az role assignment create `
  --assignee-object-id $pid `
  --assignee-principal-type ServicePrincipal `
  --role "Cognitive Services OpenAI User" `
  --scope "<azure-openai-resource-id>"
```

## Prerequisites

* Azure CLI (`az`) logged in to the target subscription.
* Bicep CLI (`az bicep`).
* Permission to create resources + Cosmos data-plane role assignments in the
  target resource group. (LLM role assignment is a separate manual step.)

## Parameters

Edit `main.bicepparam`:

| Parameter | Description |
|-----------|-------------|
| `namePrefix` | Prefix for resource names (default `az-radar`) |
| `managedIdentityName` | Name of the UAMI this template creates (default `<prefix>-uami`) |
| `additionalAppIdentityResourceIds` | Optional extra UAMIs to attach to both apps |
| `additionalCosmosDataPrincipalIds` | Optional extra principals to grant Cosmos access |
| `openAiEndpoint` | Public Azure OpenAI endpoint (out of IaC scope) |
| `openAiDeploymentName` | Model deployment name (e.g. `gpt-4o`) |
| `apiImage` / `jobImage` | Docker Hub image tags (blue/green; no ACR) |

## Deploy

```powershell
./deploy.ps1 -ResourceGroup az-radar-rg -Location westeurope
```

Or directly with the CLI:

```powershell
az group create -n az-radar-rg -l westeurope
az deployment group create `
  -g az-radar-rg `
  -f infra/main.bicep `
  -p infra/main.bicepparam
```

If a web app started before VNet integration / RBAC propagation completed,
restart it once:

```powershell
az webapp restart -g az-radar-rg -n az-radar-api
az webapp restart -g az-radar-rg -n az-radar-jobhost
```

## Validate

```powershell
# Cosmos is private
az cosmosdb show -g az-radar-rg -n <cosmosAccountName> --query publicNetworkAccess  # Disabled

# Apps are VNet-integrated
az webapp show -g az-radar-rg -n az-radar-api --query virtualNetworkSubnetId

# End-to-end: API reaches Cosmos through the private endpoint
curl https://az-radar-api.azurewebsites.net/api/health
curl https://az-radar-api.azurewebsites.net/api/dashboard/stats
```

## Tear down

```powershell
./cleanup.ps1 -ResourceGroup az-radar-iac-test-rg
```

## Out of scope

* The Azure OpenAI / AI Foundry resource and its `gpt-4o` deployment are owned
  by a different team and are **not** created by this template.
* Container images are built/pushed separately (see `Dockerfile.api` /
  `Dockerfile.jobhost`) — this template only references the published tags.
