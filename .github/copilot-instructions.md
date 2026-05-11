# AzRadar — Copilot Instructions

## Project Overview

AzRadar (Azure Lifecycle Sentinel) is an AI-powered platform that monitors Azure's change landscape (updates, retirements, deprecations) and analyzes them using LLM to help enterprise platform teams stay ahead of lifecycle changes.

**Live URL:** https://az-radar-api.azurewebsites.net

## Architecture

- **AzRadar.Api** — .NET 8 Minimal API serving REST endpoints + React FluentUI v9 SPA (co-hosted). Runs as a Docker container on Azure App Service.
- **AzRadar.JobHost** — .NET 8 background worker using Cosmos DB Change Feed to process crawl jobs. Runs as a separate Docker container on Azure App Service.
- **AzRadar.Shared** — Shared library with models, interfaces, and services used by both API and JobHost.
- **az-radar-ui** — React + TypeScript + FluentUI v9 dashboard (built into the API container).

## Azure Resources (Resource Group: `az-radar-rg`)

| Resource | Type |
|----------|------|
| `az-radar-cosmos` | Cosmos DB (Serverless, AAD-only auth) |
| `az-radar-api-plan` | App Service Plan (B1 Linux) — hosts API+UI |
| `az-radar-api` | Web App (Container) |
| `az-radar-job-plan` | App Service Plan (B1 Linux) — hosts JobHost |
| `az-radar-jobhost` | Web App (Container) |

**AI Services (pre-existing, do NOT create):**
- Endpoint: `https://<ASK USER>.cognitiveservices.azure.com/`
- Resource: `/subscriptions/<READ FROM AZURE CLI>/resourceGroups/OCTOLAMP-FOUNDRY26/providers/Microsoft.CognitiveServices/accounts/OCTOLAMP-FOUNDRY26`
- Deployment: `gpt-4o`

## Authentication — ZERO KEYS

All services authenticate via **User-Assigned Managed Identity (UAMI)**. Never use API keys or connection string keys.

- **UAMI Resource:** `/subscriptions/<READ FROM AZURE CLI>/resourceGroups/AzureOpenAI/providers/Microsoft.ManagedIdentity/userAssignedIdentities/UAMI-Azure-AI-Resource-Identity`
- **Client ID:** `c6fec013-d3e4-497c-9c49-3bf14fa305ce`
- Used for both Cosmos DB and Azure OpenAI authentication.

## Build & Test

```bash
# Build
dotnet build AzRadar.slnx -p:Platform="Any CPU"

# Test (24 unit tests)
dotnet test tests/AzRadar.Shared.Tests

# Frontend type-check
cd src/az-radar-ui && npx tsc --noEmit
```

## Docker Images & Blue-Green Deployment

Images are hosted on Docker Hub under `moimhossain/`.

### Blue-Green Tag Strategy

Always use **blue-green alternating tags** when deploying:

1. Check which tag the App Service is currently running (`blue` or `green`)
2. Build and push the **opposite** tag
3. Update the App Service to point to the new tag

```powershell
# Check current tag
az webapp config container show --name az-radar-api --resource-group az-radar-rg --query "[?name=='DOCKER_CUSTOM_IMAGE_NAME'].value" -o tsv

# If current is 'blue', build and push 'green' (and vice versa)
docker build --no-cache -f Dockerfile.api -t moimhossain/az-radar-api:green .
docker push moimhossain/az-radar-api:green

# Update App Service to the new tag
az webapp config container set --name az-radar-api --resource-group az-radar-rg --container-image-name moimhossain/az-radar-api:green --enable-app-service-storage false
az webapp restart --name az-radar-api --resource-group az-radar-rg
```

Same applies to `moimhossain/az-radar-jobhost`.

### Dockerfiles

- `Dockerfile.api` — Multi-stage: builds React frontend + .NET API into one image, serves SPA from `/wwwroot`
- `Dockerfile.jobhost` — Multi-stage: builds .NET JobHost worker

## Code Conventions

- **.NET 8 Minimal API** — no controllers, all endpoints in `Program.cs`
- **FluentUI v9** for all UI components — Microsoft design language
- **System.Text.Json** for serialization (not Newtonsoft)
- **`IJobHandler` interface** for extensible job types — register in DI and the Change Feed dispatcher routes automatically by `jobType`
- **SHA256 dedup** — feed items are deduplicated using `SHA256(source + normalized_guid)` as the Cosmos document ID
- **ETag-based optimistic concurrency** for job claiming
- **Cosmos DB partition key is `/id`** on all containers

## Key Files

| File | Purpose |
|------|---------|
| `src/AzRadar.Shared/Interfaces/IJobHandler.cs` | Job handler contract — implement to add new job types |
| `src/AzRadar.Shared/Services/AzureUpdatesJobHandler.cs` | Azure Updates RSS feed handler |
| `src/AzRadar.Shared/Services/LlmAnalyzerService.cs` | Azure OpenAI GPT-4o analysis |
| `src/AzRadar.Shared/Services/AzureUpdatesFeedReader.cs` | RSS feed reader (URL: microsoft.com/releasecommunications) |
| `src/AzRadar.Shared/Services/CosmosDbService.cs` | All Cosmos DB operations |
| `src/AzRadar.Shared/ServiceCollectionExtensions.cs` | DI registration for all shared services |
| `src/AzRadar.Api/Program.cs` | API endpoints + SPA hosting |
| `src/AzRadar.JobHost/Worker.cs` | Change Feed processor — dispatches jobs to handlers |
| `src/az-radar-ui/src/components/AppShell.tsx` | App shell with hamburger sidebar navigation |
| `src/az-radar-ui/src/pages/FeedItemsPage.tsx` | Azure Updates page (table + right-panel detail) |
| `src/az-radar-ui/src/pages/CrawlJobsPage.tsx` | Crawl Jobs page (create, monitor, delete) |
| `src/az-radar-ui/src/pages/DashboardPage.tsx` | Dashboard overview with stats cards |
| `src/az-radar-ui/src/api/client.ts` | API client with TypeScript types |

## Git Conventions

- Always GPG-sign commits (do NOT use `commit.gpgsign=false`)
- Stage changes but let the user commit if GPG signing times out
- Use conventional commit messages: `feat:`, `fix:`, `docs:`, `refactor:`

## What NOT To Do

- Do NOT create new Azure AI/OpenAI resources — use the existing OCTOLAMP-FOUNDRY26
- Do NOT use API keys or connection string keys for any Azure service — always use UAMI
- Do NOT use `latest` tag for Docker images — use blue/green alternating tags
- Do NOT use `dotnet new sln` — the solution file is `AzRadar.slnx` (not `.sln`)
