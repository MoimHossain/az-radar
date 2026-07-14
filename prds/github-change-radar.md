# PRD — GitHub Change Radar (Repository Watchlist)

> **Status:** Proposed
> **Author:** @MoimHossain (drafted with Copilot)
> **Date:** 2026-07-14
> **Feature area:** Change ingestion — new source
> **Related:** `prds/PlanV2.md` (§ Multi-source change ingestion, MS Learn documentation change tracking)

---

## 1. Summary

Add a new change-ingestion source that watches **GitHub repositories** (starting with the
open-source Microsoft docs repos such as `MicrosoftDocs/azure-aks-docs` and
`MicrosoftDocs/azure-docs`) for **newly added or modified documentation/source files** and uses
the LLM to decide whether each change is something an **Azure platform team must know about**
(retirement, deprecation, breaking change, major new feature, security advisory).

This directly closes the recency gap identified in the current MS Learn Intelligence job, which
uses semantic documentation *search* (no dates, no source URLs, no "since last time" cursor) and
therefore cannot reliably surface recently announced changes. Git history **is** a dated,
diff-able change feed — we consume it directly.

### Why now
- The existing `ms-learn-intelligence` job re-searches the same evergreen doc pages, dedups by
  content hash, and cannot enumerate "all changes in the recent past."
- Verified feasibility: the public GitHub REST API (unauthenticated) returns commits with real
  dates, commit messages, and per-file `added`/`modified`/`removed` status **plus diffs** for
  `MicrosoftDocs/azure-aks-docs` under `articles/aks`. A recent commit was literally a WS2022
  retirement doc change — exactly the signal we want.

---

## 2. Goals & Non-Goals

### Goals
1. Let a user register a **GitHub repository** (+ optional sub-path filters + branch) to watch.
2. On first registration, apply a **cutoff date** (default: now − 30 days) so old history is
   ignored; the user sets this via a date/time picker.
3. A new **`github-crawl`** job that discovers **unseen** commits/file changes since the last
   scan, harvests the diffs, and feeds them to the LLM to detect platform-relevant changes.
4. Maintain durable **"seen" state** in Cosmos so every run is incremental (no re-processing).
5. Emit **per-step diagnostics** (progress + errors, incl. rate limits) visible in the UI.
6. **Cross-reference** each flagged change against existing Azure Updates findings and, if a
   related item exists, surface that linkage on the produced insight.

### Non-Goals (v1)
- Non-GitHub sources (GitLab, Azure DevOps Repos).
- Private repositories beyond what a supplied read-only PAT can access.
- Scheduled/automatic runs and webhook-driven ingestion (manual trigger in v1; scheduling is a
  fast follow).
- Rewriting the existing `ms-learn-intelligence` handler (it can remain as an enrichment source).

---

## 3. Terminology & Naming

| Concept | Proposed name | Alternatives considered |
|---|---|---|
| Feature | **GitHub Change Radar** | Repo Radar, Source Watch |
| UI page / watchlist | **Repository Watchlist** | "Documents Watchlist" (rejected — it's repos, not documents), Repo Watch, Source Watchlist |
| Watchlist entity | `RepoWatchItem` | RepoSubscription, WatchedRepo |
| Job type | `github-crawl` | github-docs-crawl, repo-scan |
| Job handler | `GitHubCrawlJobHandler` | — |
| Insight source tag | `github` | repo, github-docs |

> **Naming recommendation:** call the page **"Repository Watchlist"** and the feature
> **"GitHub Change Radar."** "Documents Watchlist" undersells it — the unit of subscription is a
> repository, and the change unit is a commit/file diff, not a document.

---

## 4. User Stories

1. *As a platform engineer,* I add `https://github.com/MicrosoftDocs/azure-aks-docs` (path
   `articles/aks`) to the Repository Watchlist and set a cutoff of "1 month ago," so I only get
   recent changes.
2. *As a platform engineer,* I run a **GitHub Change Radar** job and see it pick up every new/
   changed doc since my last scan — with real commit dates and clickable source links.
3. *As a platform engineer,* I open a produced insight and immediately see the LLM's verdict:
   *does this require my attention, and why?*
4. *As a platform engineer,* when a flagged change matches an existing Azure Update, the insight
   tells me "this is also tracked in Azure Updates: `<title>`," so I can correlate.
5. *As an operator,* when a run hits a GitHub rate limit or a bad repo URL, I see a clear error in
   the job diagnostics view.

---

## 5. Functional Requirements

### 5.1 Repository Watchlist (new entity)

`RepoWatchItem` — stored in a new Cosmos container `repo-watchlist` (partition key `/id`):

| Field | Type | Notes |
|---|---|---|
| `id` | string (GUID) | |
| `repoUrl` | string | Full `https://github.com/{owner}/{repo}` URL entered by the user |
| `owner` | string | Parsed from URL |
| `repo` | string | Parsed from URL |
| `branch` | string? | Optional; default = repo's default branch |
| `pathFilters` | string[] | Optional sub-paths to watch (e.g. `articles/aks`); empty = whole repo |
| `label` | string | Friendly display name (default: `{owner}/{repo}`) |
| `cutoffDate` | DateTimeOffset | Ignore commits authored before this instant. Default now − 30 days |
| `enabled` | bool | Allow pause without deleting |
| `addedAt` | DateTimeOffset | |
| **Cursor (scan state, embedded):** | | |
| `lastScannedCommitSha` | string? | Newest commit processed |
| `lastScannedCommitDate` | DateTimeOffset? | Used as `since` on next run |
| `lastScanAt` | DateTimeOffset? | |
| `lastScanStatus` | string? | `ok` / `error` / `rate-limited` |
| `lastScanError` | string? | |

**API**
- `GET  /api/repo-watchlist` → list
- `POST /api/repo-watchlist` → create (validates & parses URL; rejects malformed/non-GitHub URLs)
- `DELETE /api/repo-watchlist/{id}` → remove
- `PATCH /api/repo-watchlist/{id}` (optional v1) → toggle `enabled`, edit cutoff/paths

`CreateRepoWatchRequest`: `{ repoUrl, branch?, pathFilters?, label?, cutoffDate }`.

### 5.2 GitHub Change Crawl job

New `IJobHandler` with `JobType => "github-crawl"`, registered in
`ServiceCollectionExtensions.AddAzRadarSharedServices`. Triggered via the existing
`POST /api/crawl-jobs` with `{ "jobType": "github-crawl" }` (processes **all enabled**
`RepoWatchItem`s), or with an optional `repoWatchId` to scan a single repo ("Scan now").

**Algorithm (per enabled `RepoWatchItem`):**

1. **Determine window.**
   - First scan (`lastScannedCommitDate == null`): `since = cutoffDate`.
   - Subsequent scans: `since = lastScannedCommitDate` (re-scan overlap is harmless — see dedup).
2. **List commits** — `GET /repos/{owner}/{repo}/commits?sha={branch}&path={pathFilter}&since={ISO8601}`,
   paginated (`per_page=100`, follow `Link` headers), one call per path filter (or one call for
   whole-repo). Stop paging when older than `since`/`cutoffDate`.
3. **Expand each commit** — `GET /repos/{owner}/{repo}/commits/{sha}` → `files[]` with
   `filename`, `status` (`added`/`modified`/`removed`/`renamed`), `additions`, `deletions`,
   `patch` (unified diff), and `blob_url`.
4. **Filter files** — keep documentation/source files (default: `*.md`, `*.mdx`, and files under
   `pathFilters`). Drop obvious noise (see §5.4).
5. **Dedup by "seen" key** — insight `id = SHA256("github:{owner}/{repo}:{sha}:{filename}")`.
   If an insight with that id already exists → **skip** (increment `skipped`). This makes cursor
   overlap and re-runs idempotent.
6. **LLM analysis** — for each surviving file change, call the analyzer (§5.3) with the diff,
   commit message, file path, status, and author/date. The LLM returns a structured verdict
   including `requiresAttention` + justification.
7. **Cross-reference Azure Updates** (§5.5) — attach any matching `feed-items`.
8. **Persist** the insight (§5.6) and update the job's running `CrawlJobResult`
   (`newItems`/`skippedItems`/`totalChecked`) incrementally (same pattern as existing handlers).
9. **Advance cursor** — after the repo finishes, set `lastScannedCommitSha` / `lastScannedCommitDate`
   to the newest commit seen, `lastScanAt = now`, `lastScanStatus = ok`. On failure, record
   `error`/`rate-limited` but **do not** advance the cursor (so the next run retries the window).

**Idempotency guarantee:** cursor advancement + per-file `seen` id together ensure a given commit/
file is analyzed by the LLM at most once, even across overlapping windows, retries, or concurrent
runs.

### 5.3 LLM analysis (platform-relevance verdict)

Add `ILlmAnalyzer.AnalyzeDocChangeAsync(RepoChangeContext ctx, CancellationToken ct)` returning an
`LlmAnalysis`. A **dedicated system prompt** (distinct from the feed-item prompt) instructs the
model that it is reviewing a **git diff of Azure documentation/source** on behalf of an Azure
**platform engineering team**, and must decide whether the change is material.

Extend `LlmAnalysis` (additive fields — safe for existing consumers):
- `requiresAttention: bool` — the headline verdict.
- `attentionJustification: string` — 1–3 sentences explaining *why* (or why not).

Reuse existing `LlmAnalysis` fields: `changeType` (retirement / deprecation / breaking-change /
security-advisory / new-feature / general-availability / update…), `severity`,
`affectedServices`, `affectedResourceTypes`, `actionRequired`, `deadline`, `migrationPath`,
`aiConfidence`, `briefSummary`.

Diff payload is truncated to a safe size (e.g. 8 000 chars, matching existing truncation) before
being sent to the model. For `removed` files, note that the document was **deleted** — itself a
potential retirement signal.

### 5.4 Noise filtering (pre-LLM, cost & signal control)

Skip (count as `skipped`, log at `info`) changes that are almost certainly not platform-relevant,
to save tokens and avoid false alerts:
- Non-content files (images, TOC-only `toc.yml`, `.openpublishing.*`, CODEOWNERS, media).
- Diffs whose only changes are front-matter/metadata (`ms.date`, `author`, `ms.author`,
  `ms.topic`, `ms.reviewer`), pure whitespace, or link/typo fixes (heuristic on the `patch`).
- Optional keyword pre-pass: prioritize diffs containing `retire`, `deprecat`, `breaking`,
  `end of support`, `migrat`, `sunset`, `removed`, `no longer`, `GA`, `preview`.
- Hard caps per run: `MaxCommitsPerRepo`, `MaxFilesPerCommit`, `MaxFilesPerRun` (configurable) to
  bound cost/time; overflow is recorded in diagnostics and picked up next run (cursor only
  advances past fully-processed commits).

### 5.5 Cross-reference with Azure Updates findings

After the LLM flags an insight, search existing `feed-items` (Azure Updates source) for related
items and attach the matches.

- New `ICosmosDbService.SearchFeedItemsAsync(FeedItemSearchQuery q, int limit, ct)` — Cosmos query
  matching on overlap of `llmAnalysis.affectedServices` / `affectedResourceTypes` **and/or**
  keyword `CONTAINS` on `title`/`summary`.
- v1 matching rule: an azure-update is "related" if it shares ≥1 affected service **and** the
  change types are compatible (e.g. both retirement), OR there is a strong title keyword overlap.
- Attach up to N (e.g. 5) matches to `DocInsight.relatedFeedItems` (id, title, link, publishDate).
- The insight detail UI renders: *"Also tracked in Azure Updates: …"* with links.

### 5.6 Insight storage

Reuse the existing `DocInsight` model + `doc-insights` container (keeps Dashboard / Calendar /
Doc-Insights UI working) with `source = "github"` and **additive** fields:

| New `DocInsight` field | Type | Notes |
|---|---|---|
| `commitSha` | string | Source commit |
| `commitDate` | DateTimeOffset | **Real** authored date (fixes the "no date" gap) |
| `changeKind` | string | `added` / `modified` / `removed` / `renamed` |
| `repoUrl` | string | Owner/repo |
| `filePath` | string | Path within repo |
| `relatedFeedItems` | list | Cross-referenced Azure Updates (id, title, link, publishDate) |

- `DocUrl` = the GitHub `blob_url` for the file at that commit (always correct/clickable). If the
  repo is a known Learn docs repo, additionally compute the `learn.microsoft.com` URL from the
  path (best-effort, optional).
- `id` = deterministic `seen` key from §5.2 step 5.
- `contentHash` = hash of the diff/new content (lets a later commit to the same file produce a new
  insight only when content actually differs).

### 5.7 Diagnostics (progress + errors in UI)

Reuse the existing `JobDiagnosticEntry` + `ICosmosDbService.StoreDiagnosticAsync` +
`GET /api/crawl-jobs/{id}/diagnostics` endpoint + the existing diagnostics viewer (as used by
Blast Radius). Emit entries such as:
- `info` — "Scanning {owner}/{repo} path={path} since={date}", "Fetched {n} commits (page {p})".
- `success` — "Flagged: {title} (changeType={type}, severity={sev}, requiresAttention=true)".
- `warning` — "Skipped {n} noise/metadata-only changes", "Truncated diff to 8000 chars".
- `error` — GitHub `403`/rate-limit (include reset time), `404` (bad repo/branch), auth failures,
  network timeouts, LLM call failures. Include `durationMs`, `resultCount`, `attempt` where useful.

---

## 6. Architecture

```
POST /api/crawl-jobs {jobType:"github-crawl", repoWatchId?}
        │  (Cosmos change feed)
        ▼
ChangeFeedWorker ──► GitHubCrawlJobHandler
                          │
        ┌─────────────────┼───────────────────────────┐
        ▼                 ▼                             ▼
  IGitHubClient     ILlmAnalyzer.AnalyzeDocChange   ICosmosDbService
  (list commits,    (diff-aware system prompt,      (repo-watchlist CRUD,
   get commit files, returns LlmAnalysis +           doc-insights upsert,
   diffs)            requiresAttention/justification) feed-items search,
                                                      diagnostics, cursor)
```

### New abstractions (mirror existing `IFeedReader` / `IMcpDocsClient` patterns)
- `IGitHubClient` (impl `GitHubClient`, registered singleton with a named `HttpClient`):
  - `ListCommitsAsync(owner, repo, branch?, path?, since, ct) : IReadOnlyList<GitHubCommitRef>`
  - `GetCommitAsync(owner, repo, sha, ct) : GitHubCommitDetail` (with `files[]` incl. `patch`)
  - Handles pagination, `User-Agent`, `Accept: application/vnd.github+json`, optional
    `Authorization: Bearer {PAT}`, and 403/rate-limit detection (`X-RateLimit-Remaining`,
    `Retry-After`).
- `GitHubCrawlJobHandler : IJobHandler` — orchestration described in §5.2.
- (Optional, for extensibility) `IRepoChangeSource` so GitLab/ADO can be added later. Not required
  for v1.

### Configuration
New `GitHubSettings` (bound from config, section `GitHub`):
- `ApiBaseUrl` (default `https://api.github.com`)
- `Token` (optional PAT — see §8 Security)
- `MaxCommitsPerRepo`, `MaxFilesPerCommit`, `MaxFilesPerRun`
- `RequestTimeoutSeconds`, `DiffMaxChars` (default 8000)

---

## 7. Data / Infrastructure changes

- **New Cosmos container `repo-watchlist`** (`/id`). Per the repo's Cosmos convention, the app's
  data-plane RBAC role **cannot** create containers, so it **must be pre-created at the control
  plane in IaC** — add it to the `containers` array in `infra/modules/cosmos.bicep`.
- Reuse existing `doc-insights` and `job-diagnostics` containers (additive `DocInsight` fields only
  — no schema migration needed; Cosmos is schemaless).
- Update `CosmosDbSettings` (+ default) with `RepoWatchlistContainer = "repo-watchlist"` and add
  container accessors + CRUD in `CosmosDbService` (mirroring watchlist).
- `CosmosDbService.InitializeAsync` lists the new container via `CreateContainerIfNotExistsAsync`
  (no-op when IaC has pre-created it — consistent with existing behavior).

---

## 8. Security & Rate Limits

- **Docs repos are public** → unauthenticated GitHub API works but is capped at **60 requests/
  hour**, which is insufficient for multi-repo, paginated commit expansion.
- **Decision:** support an **optional read-only GitHub PAT** (fine-grained, `contents:read` on
  public repos) to raise the limit to **5 000 requests/hour**. The user enters it in the UI and it
  is **stored in Cosmos `app-config`** under key `github-pat` (same mechanism as the Blast Radius
  UAMI client id) — **not** Key Vault.
  - **Rationale:** the Cosmos account is already VNet-protected (public access disabled, private
    endpoint, AAD-only RBAC), so a secret stored there sits behind the same network + identity
    boundary as all other data. Adding Key Vault would mean provisioning and VNet-protecting a
    second resource for one low-value secret. The PAT is **read-only over public repositories**, so
    its leak blast-radius is effectively zero (it grants only public read, which is already public).
  - The repo's "ZERO KEYS" rule targets **Azure** services (use UAMI, never Azure keys). A GitHub
    PAT is an external third-party credential and is unavoidable for rate headroom.
- **Mandatory safeguards for the stored PAT:**
  - **Never return it through the API.** The existing `GET /api/config/{key}` endpoint returns the
    raw `AppConfig.Value`, so it must **special-case** the `github-pat` key — return only
    `{ configured: true|false }` or a masked `••••1234`, never the raw token. The PAT is
    **write-only** from the UI's perspective (set/replace/clear via the existing
    `PUT /api/config/{key}`), never read back.
  - Fine-grained PAT scoped to `contents:read` on public repos, with an **expiry**; surface a
    manual-rotation reminder in the UI.
  - **Never log the PAT.** Redact `Authorization` headers in any diagnostic output.
- Handle secondary rate limits and `Retry-After` gracefully; surface as `warning`/`error`
  diagnostics rather than failing the whole job. If no PAT is configured, run unauthenticated and
  emit a `warning` diagnostic noting the 60/hr cap.

New `AppConfigKeys.GitHubPat = "github-pat"`. UI writes via the existing `PUT /api/config/github-pat`
(value masked in the list UI); the handler reads it via `GetAppConfigAsync("github-pat")`.

---

## 9. UI / UX

### New page: **Repository Watchlist** (FluentUI v9, hamburger nav)
- Table of watched repos: label, repo URL (link), paths, branch, cutoff, last scan (status/time),
  enabled toggle, delete.
- "Add repository" dialog:
  - Repo URL (text, validated).
  - Path filters (tag input / multi-line, optional).
  - Branch (optional).
  - **Cutoff date/time** — FluentUI **DatePicker** (+ time selection), **default = now − 30 days**,
    required. Helper text: *"Changes committed before this date are ignored on the first scan."*
  - Label (optional).
- "Scan now" per-row action → `POST /api/crawl-jobs {jobType:"github-crawl", repoWatchId}`.

### CrawlJobsPage
- Add **GitHub Change Radar** to the job-type selector (creates a `github-crawl` job over all
  enabled repos).
- Existing job diagnostics viewer already covers progress/errors — reused as-is.

### Insight surfaces
- Doc-Insights / Feed views: `github` source badge; show `commitDate`, `changeKind`,
  `requiresAttention` (prominent), and the justification.
- Detail panel: clickable GitHub source link + **"Also tracked in Azure Updates"** section listing
  `relatedFeedItems`.
- `client.ts`: add `RepoWatchItem` type + `getRepoWatchlist` / `addRepoWatch` / `removeRepoWatch`;
  extend `DocInsight` + `LlmAnalysis` interfaces with the new fields.

---

## 10. Acceptance Criteria

1. Adding a repo with a cutoff of "30 days ago" and running a job produces insights **only** for
   commits authored on/after the cutoff.
2. Running the same job twice back-to-back yields **0 new / all skipped** for unchanged history
   (idempotent), and picks up exactly the new commits on a subsequent real change.
3. Each produced insight has a **real commit date**, a **working GitHub source link**, a
   `changeKind`, and an LLM `requiresAttention` verdict + justification.
4. A commit that only edits `ms.date`/front-matter is **skipped** (noise filter), visible in
   diagnostics.
5. When a flagged retirement matches an existing Azure Update, the insight lists that update.
6. A bad repo URL, a private repo without access, or a GitHub rate-limit produces a clear
   **error** diagnostic and does **not** advance the cursor.
7. New `repo-watchlist` container is provisioned by IaC (`cosmos.bicep`).

---

## 11. Testing

Unit tests (mirror `tests/AzRadar.Shared.Tests`, mock `IGitHubClient` + `ILlmAnalyzer`):
- Cutoff filtering (commits before cutoff excluded on first scan).
- Cursor advancement + idempotent re-run (seen-id dedup).
- Noise filter (metadata-only / whitespace / TOC skipped; substantive diff kept).
- Deterministic `seen` id generation.
- Cross-reference matching (service overlap + change-type compatibility).
- URL parsing (owner/repo/branch/path extraction; malformed URL rejected).
- Rate-limit / 404 handling → error diagnostic, cursor not advanced.

Build/verify: `dotnet build AzRadar.slnx -p:Platform="Any CPU"`,
`dotnet test tests/AzRadar.Shared.Tests`, `cd src/az-radar-ui && npx tsc --noEmit`.

---

## 12. Rollout

1. IaC: add `repo-watchlist` container → deploy `infra`.
2. Ship shared library (models, `IGitHubClient`, handler, DI, Cosmos methods) + API endpoints + UI.
3. Configure GitHub PAT via Key Vault reference (optional but recommended).
4. Blue-green Docker deploy of **both** `az-radar-api` and `az-radar-jobhost` (JobHost runs the new
   handler; API serves the UI/endpoints) using alternating `blue`/`green` tags.

---

## 13. Resolved Decisions

1. **PAT storage → Cosmos `app-config` (key `github-pat`), not Key Vault.** Cosmos is already
   VNet-protected + AAD-only; the PAT is read-only over public repos (near-zero leak blast radius).
   Mandatory: write-only from the UI, excluded/masked in `GET /api/config/{key}`, never logged.
   (See §8.)
2. **Store-all with `requiresAttention` flag.** Every analyzed change is persisted (flagged
   true/false); the UI defaults to showing only `requiresAttention=true` with a "show everything"
   toggle. This preserves an audit trail **and** keeps the job cheap/idempotent — persisting the
   unflagged "seen" records prevents re-sending them to the LLM on every run.
3. **Repository Watchlist is fully independent** in v1 — repos are entered manually by the user; no
   auto-suggest from the Service Watchlist. (Auto-suggest is a possible later enhancement.)
4. **Manual trigger only** in v1 (via "Scan now" and the CrawlJobs page). Scheduled/timer runs are
   a fast-follow, out of scope here.
5. **Insight link = GitHub blob URL** (the file at that exact commit — always correct). Computing
   the rendered `learn.microsoft.com` URL is deferred to a later best-effort enhancement.
