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

- Enumerates the complete Azure Updates catalog and reconciles it with the RSS feed
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
   1. Reads every page of the public Release Communications Azure catalog, with full descriptions
   2. Reads Azure Updates RSS and reconciles its IDs and modification dates with the catalog
   3. Checks enumeration completeness against the catalog's reported count (no lookback or item cap)
   4. Deduplicates by the existing SHA256 ID and detects revised source content using a separate hash
   5. Analyzes new/revised posts via Azure OpenAI and stores them in Cosmos DB; revisions use ETag-conditional replacement
6. Job status updated to `completed` with result summary
7. Dashboard auto-refreshes to show new items with AI analysis

### Azure Updates coverage

The ingestion sources are the public [Azure Updates catalog](https://www.microsoft.com/releasecommunications/api/v2/azure/)
and [RSS feed](https://www.microsoft.com/releasecommunications/api/v2/azure/rss).
The catalog is traversed in stable ID order using every `@odata.nextLink`, without `$top`
(which limits the total OData result, not just the page size). Front Door continuation
URLs are normalized back to the public endpoint, preserving their query parameters.
RSS-only posts and posts with newer RSS modification dates are fetched individually
for full descriptions. Processing then starts with the most recently modified posts.

Do not use one unfiltered `get_recent_azure_updates` MCP call as a crawler. The live
MCP schema uses `skip`, not `top`, returns 50 items per page in ID order rather than
date order, and truncates list descriptions. This previously caused repeated crawls
of the same 50 historical posts. The MCP client is no longer the ingestion dependency.

Every successful crawl covers the union exposed by both sources during that run.
HTTP errors, invalid payloads, missing pages, repeated IDs/continuations, changed
catalog counts, and stale RSS detail responses fail the job rather than reporting
partial coverage as success. The job's diagnostics record catalog, RSS, and unique
item counts. Results distinguish **new**, **updated**, and **skipped** posts.
Previously imported items without a content hash are refreshed once, keeping their
original document IDs and first-seen timestamps. Failed AI analyses are retried on
subsequent crawls even when the source content is unchanged.

The first complete crawl is a full historical backfill. To avoid LLM cost, select
**Historical backfill: skip LLM analysis** when creating the Azure Updates job, or
POST `{"jobType":"azure-updates","skipLlmAnalysis":true}` to `/api/crawl-jobs`.
This flag applies only to that job and defaults to false. It makes zero LLM calls,
stores full source content without fabricated AI classifications, and marks records
with `llmAnalysisSkipped=true`. Normal crawls skip those records while their source
content is unchanged; newly discovered or revised posts still receive normal analysis.
Without this flag, the first run can require thousands of LLM calls.
Later crawls still enumerate the full catalog,
but analyze only new/revised posts or prior failed analyses. If a crawl fails, rerun
it: already persisted items remain and are reconciled again. Run crawls regularly;
RSS is a rolling window (200 entries observed), not an archive, and upstream
publication/cache delays or posts removed before any crawl cannot be recovered or
guaranteed by this application. No automatic polling schedule is added by this change.

Large source bodies are stored with lossless GZip compression and transparently
decoded by the API. Summaries are bounded previews; the complete source remains
in `rawContent` when read. Content that still exceeds the storage safety limits
fails explicitly rather than being silently discarded.

### Job liveness

Executors claim jobs only when they have capacity to start them. Waiting jobs stay
`pending`. Every executing job writes `lastHeartbeatAt` immediately and every
30 seconds, including during slow source requests or LLM calls. Progress writes
record `lastProgressAt` separately. Both use conditional Cosmos patches, so progress
cannot erase a concurrent heartbeat or a previous attempt overwrite a newer one.

A processing job is marked stale only after **two minutes without a heartbeat**,
not because it has been running for a long time. The API supplies this liveness
state to the UI. Missing heartbeats mean the worker may have stopped or lost
connectivity, not that deletion is safe: deleting any active job requires confirmation.
Legacy jobs without heartbeat data show unknown liveness rather than being presumed
stalled. A live heartbeat confirms worker liveness, while the separate progress
timestamp helps diagnose work that is alive but not advancing. Failed heartbeat
writes cancel the executing handler and surface a job failure; no automatic retry
or duplicate execution is started.

---

## Solution Structure

```
az-radar/
├── src/
│   ├── AzRadar.Shared/           # Shared library (models, interfaces, services)
│   │   ├── Configuration/        # CosmosDbSettings, OpenAiSettings
│   │   ├── Interfaces/           # IJobHandler, IAzureUpdatesSource, ILlmAnalyzer, ICosmosDbService
│   │   ├── Models/               # CrawlJob, FeedItem, LlmAnalysis
│   │   └── Services/             # CosmosDbService, AzureUpdatesSource,
│   │                             # LlmAnalyzerService, AzureUpdatesJobHandler
│   ├── AzRadar.Api/              # .NET 8 Minimal API + static SPA host
│   ├── AzRadar.JobHost/          # Background worker (Change Feed consumer)
│   └── az-radar-ui/              # React + TypeScript + FluentUI v9 dashboard
├── tests/
│   ├── AzRadar.Shared.Tests/     # Source coverage, parsing, dedup, and job handler tests
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
| Azure Updates catalog + RSS | Full catalog, RSS reconciliation, and revised announcements | ✅ Implemented |
| Azure Service Health | Maintenance windows, health advisories | 🔜 Phase 2 |
| Azure Advisor | Deprecation recommendations, best practices | 🔜 Phase 2 |
| Azure Resource Graph | Resource configuration drift | ✅ Implemented |
| MSRC | Critical security bulletins | ✅ Implemented |
| MS Learn docs | Documentation changes for specific services | ✅ Implemented |

---

## License

MIT