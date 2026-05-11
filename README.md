# AzRadar — Azure Lifecycle Sentinel

> **Never be blindsided by an Azure change again.**

AzRadar is an AI-powered lifecycle management platform that continuously monitors Azure's change landscape, maps changes to deployed resources, and orchestrates the response — turning lifecycle management from a reactive scramble into a proactive, measured, and auditable process.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB)](https://react.dev/)
[![FluentUI v9](https://img.shields.io/badge/FluentUI-v9-0078D4)](https://react.fluentui.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## The Problem

Enterprise organizations operate a **platform team ↔ workload team** model. When Azure announces lifecycle changes — certificate deprecations, SKU retirements, API version sunsets, TLS policy changes — neither team has a systematic way to:

1. **Discover** the change before it becomes urgent
2. **Map** the change to actual deployed resources across 50–500+ subscriptions
3. **Route** the notification to the right team based on resource ownership
4. **Track** remediation completion before the deadline
5. **Report** lifecycle readiness posture to leadership

The result: reactive firefighting, missed deadlines, and production incidents.

---

## What Phase 1 Implements (Current State)

Phase 1 focuses on **Azure Updates feed ingestion with LLM analysis**:

- Reads the Azure Updates RSS feed
- Uses Azure OpenAI (GPT-4o) to classify each update (change type, severity, affected services, action required, deadlines, migration path, effort estimate)
- Deduplicates feed items using SHA256 hashing
- Stores everything in Cosmos DB
- Professional FluentUI v9 dashboard to visualize results and trigger crawl jobs
- Extensible job framework for adding new data sources

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                         AzRadar System                           │
│                                                                  │
│  ┌────────────────────────────────────────┐                      │
│  │     App Service (Linux Container)      │                      │
│  │     moimhossain/az-radar-api           │                      │
│  │                                        │                      │
│  │  ┌──────────────┐  ┌───────────────┐   │                      │
│  │  │  React +     │  │  .NET 8       │   │                      │
│  │  │  FluentUI v9 │  │  Minimal API  │   │                      │
│  │  │  Dashboard   │  │  REST         │   │                      │
│  │  └──────────────┘  └───────┬───────┘   │                      │
│  └────────────────────────────┼───────────┘                      │
│                               │                                  │
│                               ▼                                  │
│                    ┌──────────────────┐                           │
│                    │   Cosmos DB      │                           │
│                    │   (az-radar-db)  │◄──── Change Feed          │
│                    └──────────────────┘         │                 │
│                                                 │                 │
│  ┌──────────────────────────────────────────────┼────────┐       │
│  │     App Service (Linux Container)            │        │       │
│  │     moimhossain/az-radar-jobhost             ▼        │       │
│  │                                                       │       │
│  │  ┌───────────────────┐  ┌──────────────────────────┐  │       │
│  │  │  Change Feed      │  │  Job Handlers            │  │       │
│  │  │  Processor        │──│  ├── AzureUpdatesHandler  │  │       │
│  │  │                   │  │  └── (future handlers)   │  │       │
│  │  └───────────────────┘  └──────────┬───────────────┘  │       │
│  └─────────────────────────────────────┼─────────────────┘       │
│                                        │                         │
│                          ┌─────────────┴──────────────┐          │
│                          │    Azure OpenAI (GPT-4o)   │          │
│                          │    LLM Analysis Engine     │          │
│                          └────────────────────────────┘          │
└──────────────────────────────────────────────────────────────────┘

All authentication via User-Assigned Managed Identity (UAMI) — zero keys
```

---

## Data Flow

1. User creates a "crawl job" (e.g., "Azure Updates check") via the dashboard
2. API writes a `CrawlJob` document to the Cosmos DB `crawl-jobs` container (status: `pending`)
3. JobHost monitors `crawl-jobs` via Cosmos DB Change Feed Processor
4. Change Feed triggers: JobHost claims the job (ETag-based optimistic concurrency)
5. **AzureUpdatesJobHandler:**
   1. Determines lookback window (last 7 days on first run, or since latest known item)
   2. Fetches the Azure Updates RSS feed
   3. Deduplicates against existing items via SHA256 hash of `source + guid`
   4. Runs LLM analysis on each new item via Azure OpenAI GPT-4o
   5. Stores enriched feed items to Cosmos DB
6. Job status updated to `completed` with result summary
7. Dashboard auto-refreshes to show new items with AI analysis

---

## Solution Structure

```
az-radar/
├── src/
│   ├── AzRadar.Shared/           # Shared library (models, interfaces, services)
│   │   ├── Configuration/        # CosmosDbSettings, OpenAiSettings
│   │   ├── Interfaces/           # IJobHandler, IFeedReader, ILlmAnalyzer, ICosmosDbService
│   │   ├── Models/               # CrawlJob, FeedItem, LlmAnalysis
│   │   └── Services/             # CosmosDbService, AzureUpdatesFeedReader,
│   │                             # LlmAnalyzerService, AzureUpdatesJobHandler
│   ├── AzRadar.Api/              # .NET 8 Minimal API + static SPA host
│   ├── AzRadar.JobHost/          # Background worker (Change Feed consumer)
│   └── az-radar-ui/              # React + TypeScript + FluentUI v9 dashboard
├── tests/
│   ├── AzRadar.Shared.Tests/     # 24 unit tests (feed parsing, dedup, job handler)
│   └── AzRadar.Api.Tests/        # API integration tests (placeholder)
├── Dockerfile.api                # Multi-stage: .NET API + React frontend
├── Dockerfile.jobhost            # Multi-stage: .NET background worker
└── AzRadar.slnx                  # Solution file
```

---

## Technology Stack

| Component | Technology | Rationale |
|---|---|---|
| API | .NET 8 Minimal API | Modern, clean, no controllers |
| Dashboard | React + TypeScript + FluentUI v9 | Microsoft design language, professional look |
| Background Jobs | .NET 8 Worker with Cosmos Change Feed | Event-driven, no polling, built-in checkpointing |
| Data Store | Azure Cosmos DB (Serverless) | Flexible schema, change feed, global distribution |
| AI Analysis | Azure OpenAI (GPT-4o) | Enterprise-grade, data stays in tenant |
| Authentication | User-Assigned Managed Identity | Zero keys, zero secrets in config |
| Deployment | Docker containers on Azure App Service | Simple, portable, scalable |
| Container Registry | Docker Hub (`moimhossain/*`) | Public images, fast pull |

---

## Cosmos DB Design

**Database:** `az-radar-db`

| Container | Partition Key | Purpose |
|---|---|---|
| `crawl-jobs` | `/id` | Job lifecycle; Change Feed source for JobHost |
| `feed-items` | `/id` | Ingested + AI-analyzed feed items (dedup by SHA256) |
| `change-feed-leases` | `/id` | Change Feed Processor lease management |

### CrawlJob Document

```json
{
  "id": "guid",
  "jobType": "azure-updates",
  "status": "pending | processing | completed | failed",
  "createdAt": "2026-05-11T10:21:20Z",
  "startedAt": "2026-05-11T10:21:22Z",
  "completedAt": "2026-05-11T10:22:12Z",
  "result": { "newItems": 15, "totalChecked": 15, "skippedItems": 0 },
  "error": null,
  "attemptCount": 1
}
```

### FeedItem Document (with LLM Analysis)

```json
{
  "id": "sha256-hash-32-chars",
  "source": "azure-updates",
  "title": "Retirement: Azure Document Intelligence v3.0 API...",
  "link": "https://azure.microsoft.com/updates?id=561176",
  "publishDate": "2026-05-06T22:15:33Z",
  "summary": "...",
  "categories": ["Retirements", "AI + machine learning"],
  "rawContent": "...",
  "llmAnalysis": {
    "changeType": "retirement",
    "severity": "high",
    "affectedServices": ["Azure AI Document Intelligence"],
    "affectedResourceTypes": ["Microsoft.CognitiveServices/accounts"],
    "actionRequired": "Migrate to v4.0 API before March 30, 2029",
    "deadline": "2029-03-30",
    "effortEstimate": "medium",
    "migrationPath": "Update API calls from v3.0 to v4.0 endpoints",
    "microsoftDocLinks": ["https://azure.microsoft.com/updates?id=561176"],
    "aiConfidence": 0.92,
    "briefSummary": "Azure Document Intelligence v3.0 API will be retired on March 30, 2029. Platform teams should plan migration to the v4.0 API."
  },
  "firstSeenAt": "2026-05-11T10:21:22Z",
  "crawlJobId": "3e1d0713-..."
}
```

---

## Extensible Job Framework

```csharp
public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(CrawlJob job, CancellationToken ct);
}
```

The Change Feed processor dispatches to the correct handler based on `jobType`. Adding a new job type (e.g., `"check-ms-learn-aks-docs"`) requires:

1. Implement `IJobHandler`
2. Register in DI: `services.AddSingleton<IJobHandler, MyNewHandler>()`

That's it — the Change Feed dispatcher routes automatically.

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/health` | Health check |
| `GET` | `/api/dashboard/stats` | Dashboard summary stats |
| `POST` | `/api/crawl-jobs` | Create a new crawl job |
| `GET` | `/api/crawl-jobs` | List crawl jobs (with `?limit=N`) |
| `GET` | `/api/crawl-jobs/{id}` | Get specific crawl job |
| `DELETE` | `/api/crawl-jobs/{id}` | Delete a crawl job |
| `GET` | `/api/feed-items` | List feed items (with `?source=X&limit=N`) |
| `GET` | `/api/feed-items/{id}` | Get specific feed item with LLM analysis |
| `GET` | `/` | Serves the React SPA dashboard |

---

## Dashboard Pages

The dashboard uses Microsoft FluentUI v9 for a professional enterprise look:

1. **Dashboard** — Stats overview: total jobs, completed/pending/failed counts, feed items tracked, critical/high severity counts
2. **Crawling Jobs** — Create new crawl jobs, monitor status, see results. Auto-refreshes every 10 seconds. Job creation dialog with job type selector.
3. **Azure Updates** — Browse all ingested feed items with expandable AI analysis panels showing severity badges, change type icons, affected services, migration paths, confidence scores, and links to Microsoft docs.

---

## Azure Resources

Deployed in resource group `az-radar-rg`:

| Resource | Type | Notes |
|---|---|---|
| `az-radar-cosmos` | Cosmos DB (Serverless) | West Europe, AAD-only auth |
| `az-radar-api-plan` | App Service Plan (B1 Linux) | Hosts API + UI container |
| `az-radar-api` | Web App (Container) | `moimhossain/az-radar-api:latest` |
| `az-radar-job-plan` | App Service Plan (B1 Linux) | Hosts JobHost container |
| `az-radar-jobhost` | Web App (Container) | `moimhossain/az-radar-jobhost:latest` |
| UAMI | User-Assigned Managed Identity | Shared identity for Cosmos + OpenAI |
| OCTOLAMP-FOUNDRY26 | Azure AI Services | GPT-4o deployment (pre-existing) |

---

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- Docker
- Azure CLI (logged in)

---

## Build & Test

```bash
# Build the solution
dotnet build AzRadar.slnx -p:Platform="Any CPU"

# Run unit tests (24 tests)
dotnet test tests/AzRadar.Shared.Tests

# Build Docker images
docker build -f Dockerfile.api -t moimhossain/az-radar-api:latest .
docker build -f Dockerfile.jobhost -t moimhossain/az-radar-jobhost:latest .

# Push to Docker Hub
docker push moimhossain/az-radar-api:latest
docker push moimhossain/az-radar-jobhost:latest

# Frontend dev (local)
cd src/az-radar-ui
npm install
npm run dev

# Restart Azure containers after push
az webapp restart --name az-radar-api --resource-group az-radar-rg
az webapp restart --name az-radar-jobhost --resource-group az-radar-rg
```

---

## Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Event bus | Cosmos DB Change Feed (not Service Bus) | Simpler, fewer moving parts, change feed is free, built-in checkpointing |
| Partition key | `/id` on all containers | High cardinality avoids hot partitions; change feed sees all changes regardless |
| Job concurrency | ETag-based optimistic locking | Prevents duplicate processing when multiple workers run |
| Dedup strategy | SHA256(`source` + normalized `guid`) as document ID | Deterministic, case-insensitive, collision-resistant |
| Auth model | UAMI everywhere (no keys) | Zero secrets in config, enterprise-grade security |
| API + UI hosting | Single container serving both | Simpler deployment, no CORS needed, SPA fallback routing built-in |
| Frontend framework | FluentUI v9 | Microsoft design language, enterprise-appropriate look and feel |
| LLM analysis | Per-item sequential (Phase 1) | Simple; designed for micro-batching upgrade in future |

---

## Future Roadmap

### Phase 2: Intelligence

- Service Health API integration (planned maintenance, health advisories)
- Azure Advisor integration (deprecation recommendations)
- Blast Radius Engine — map changes → deployed resources via Azure Resource Graph
- AI Action Plan Generator (per-team remediation plans with code snippets)
- Severity amplification (AI-adjusted severity based on resource count, environment, deadline proximity)

### Phase 3: Enterprise Dashboard

- Timeline view with countdown to deadlines
- Heatmap: subscription × lifecycle event matrix
- Team scorecard with readiness scoring
- Drill-down to affected resources
- Executive report export (PDF / PowerPoint)
- Self-service "Am I Affected?" query for workload teams

### Phase 4: Integration & Advanced

- Azure DevOps work item sync (auto-create, bi-directional)
- ServiceNow integration (Change Requests, Incidents)
- MS Learn documentation change monitoring
- Proactive drift detection (deprecated configurations)
- What-If scenario analyzer
- Predictive lifecycle intelligence

### Planned Data Sources

| Source | What It Catches | Status |
|---|---|---|
| Azure Updates RSS | New features, deprecations, retirements, previews | ✅ Implemented |
| Azure Service Health | Maintenance windows, health advisories | 🔜 Phase 2 |
| Azure Advisor | Deprecation recommendations, best practices | 🔜 Phase 2 |
| Azure Resource Graph | Resource configuration drift | 🔜 Phase 2 |
| MSRC | Critical security bulletins | 🔜 Phase 4 |
| MS Learn docs | Documentation changes for specific services | 🔜 Phase 4 |

---

## License

MIT