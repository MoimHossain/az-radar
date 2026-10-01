# PRD - Lifecycle Calendar Horizon Completeness

> **Status:** Implemented and deployed to the demo environment (2026-10-01)
> **Feature area:** Lifecycle Calendar, Azure Updates and Microsoft Learn intelligence
> **Date:** 2026-10-01

## 1. Summary

A Regulated Industry customer runs CloudLens side by side with its legacy Azure lifecycle
dashboard. Their product owner reported that retirements shown in the legacy dashboard's
**7-12 months**, **13-24 months** and **25-36 months** columns are missing from the CloudLens
Lifecycle Calendar.

Investigation shows the data is crawled, but the calendar never reads it. `/api/calendar` loads
only the **500 most recently published** feed items and then filters by deadline. Long-horizon
retirements are typically announced 1-3 years before they take effect, so they are the first
items pushed out of that window. The quarter view adds a second horizon limit (four quarters),
and the watchlist matcher and deadline extraction silently remove further items.

This PRD makes the calendar deadline-driven and horizon-complete, adds legacy-dashboard parity
buckets, and makes relevance and deadline gaps observable.

## 2. Evidence

Measured on the maintainer demo environment (2026-10-01) against the 18 traceable items the
customer flagged.

| Finding | Data |
|---|---|
| Azure Updates catalog enumerated per crawl | 9,918 posts (no lookback cutoff) |
| Flagged items present in the catalog | 18 of 21; 3 have no Azure Updates post (Key Vault API versions, Data Factory legacy mode, Redis "entire service") |
| Simulated retention with a watchlist covering the customer's estate | ~6,900 items |
| Oldest `publishDate` inside the top-500 window | 2025-11-18 |
| Flagged items cut by the 500 cap | **13 of 18** (rank 573-1,710) |
| Flagged items still visible | 5 of 18, all published in 2026 |

Representative cut items: Application Gateway WAF v2 configuration (published 2024-03, due
2027-03-31), NSG flow logs (2024-09, due 2027-09-30), OS disks on Standard HDD (2025-09, due
2028-09-08), Linux Consumption plan for Functions (2025-09, due 2028-09-30).

### Root causes

| # | Cause | Location |
|---|---|---|
| RC1 | Calendar reads `TOP 500 ... ORDER BY c.publishDate DESC`, then filters by deadline | `src/AzRadar.Api/Program.cs` `/api/calendar`; `CosmosDbService.GetFeedItemsAsync` |
| RC2 | Same 500 cap on doc insights (`ORDER BY c.lastAnalyzedAt DESC`) and on `/api/dashboard/stats` | `Program.cs` `/api/calendar`, `/api/dashboard/stats` |
| RC3 | Quarter view renders only the current and next three quarters | `LifecycleCalendarPage.tsx` `getQuarters()` |
| RC4 | Watchlist matching is near-exact (normalized name, token set, or acronym). Items that do not match are deleted without a trace | `WatchlistRelevanceMatcher.cs`; `AzureUpdatesJobHandler.cs`; `MsLearnIntelligenceJobHandler.cs` |
| RC5 | `llmAnalysis.deadline` is a free-form string. A retirement without an extracted deadline never reaches the calendar | `LlmAnalysis.Deadline`; `LlmAnalyzerService.cs` prompts |

Out of scope but noted: the legacy dashboard is **inventory-driven** (it lists retirements that
affect resources the customer owns). CloudLens is **announcement-driven**. Full parity, including
the 3 items with no Azure Updates post, requires an inventory source (see section 10).

## 3. Goals

1. Every retained item with a deadline from 90 days ago through at least 36 months ahead appears
   in the Lifecycle Calendar, however long ago it was published.
2. Provide horizon buckets matching the legacy dashboard: Out of support, Within 6 months,
   7-12 months, 13-24 months and 25-36 months, plus Later.
3. Make the quarter view span the full horizon of the data.
4. Reduce false-negative watchlist discards and make every discard auditable.
5. Recover deadlines for retirement and deprecation items where the LLM returned none.
6. Keep dashboard stats consistent with the calendar.

## 4. Non-goals

- Inventory-aware retirement detection (Azure Advisor or Resource Graph). Tracked as future work.
- Changing Service Health routing or the Teams/Wiki dispatch paths.
- Re-running LLM analysis on items whose source content is unchanged.
- Re-processing demo-environment items stored with `llmAnalysisSkipped = true`. That is a separate
  backfill concern, and production customer environments do not use it.

## 5. Requirements

### R1 - Deadline-driven calendar query (fixes RC1, RC2)

1. Add a normalized, queryable field `lifecycleDeadline` (ISO `yyyy-MM-dd`, nullable) to
   `FeedItem` and `DocInsight`. Derive it from `llmAnalysis.deadline` on write by parsing it
   invariantly, accepting ISO dates and common long forms. Leave it null when the value is
   unparseable.
2. Add `ICosmosDbService.GetCalendarFeedItemsAsync(from, to)` and
   `GetCalendarDocInsightsAsync(from, to)`, which query
   `WHERE IS_DEFINED(c.lifecycleDeadline) AND c.lifecycleDeadline >= @from AND c.lifecycleDeadline <= @to`
   using full continuation-token paging. There must be no `TOP` cap based on publish date.
3. `/api/calendar` uses these methods. Default window: `from = today - 90d`, `to = today + 60 months`.
   Optional `from` and `to` query parameters are clamped to a maximum span of 10 years.
4. Return only the calendar projection fields, not full documents. `rawContent` can be large.
5. `/api/dashboard/stats` computes deadline-based counts (retirements, urgent deadlines) from the
   same calendar query, so the stats and the calendar always agree.
6. **Migration:** an idempotent startup or one-shot routine fills in `lifecycleDeadline` on
   existing documents from their stored `llmAnalysis.deadline`, with no LLM call. Feed-item
   crawls skip unchanged content and would otherwise never write the new field. The routine
   must patch only the new field so it cannot overwrite concurrent writes (same principle as the
   job-liveness patch rules).

### R2 - Horizon buckets and full-range quarter view (fixes RC3)

1. Add a **Horizon** view to `LifecycleCalendarPage` with columns: Out of support (deadline
   passed, within the 90-day lookback), Within 6 months, 7-12 months, 13-24 months, 25-36 months
   and Later.
   Bucket boundaries use calendar months from today.
2. Each card shows service, title, deadline and change type, and opens the existing detail panel.
3. The quarter view derives its quarters from the earliest to the latest deadline in the
   filtered set, instead of a fixed four quarters.
4. Existing filters (change type, severity, keyword) apply to every view.

### R3 - Watchlist matching robustness and audit (fixes RC4)

1. Persist `products` from the Azure Updates catalog on `FeedItem`. Today only tags and product
   categories are stored, so the matcher's source-product input cannot be inspected after a crawl.
2. Extend `WatchlistRelevanceMatcher` with **containment matching**: a watched service matches when
   its significant words occur as a contiguous phrase in the affected service name, or the reverse.
   For example, "Virtual Machines" matches "Azure Virtual Machines Scale Sets". A single-word part
   must be the first significant word ("Cosmos" matches "Azure Cosmos DB", while "Azure Files"
   does not match "Azure NetApp Files").
   Guard against generic single tokens (`service`, `plan`, `account`, `api`) to avoid
   over-matching.
3. Ship a maintained alias map for common lifecycle-relevant families, for example:
   Virtual Machines covers VMSS, Managed Disks, Azure Disk Encryption, Dependency Agent and
   VM Insights; Key Vault covers Managed HSM; Network Watcher covers NSG Flow Logs; Front Door
   covers Azure CDN; Azure Cache for Redis covers Redis Cache. The map is applied in addition to
   user-entered aliases.
4. **Discard audit:** instead of silently deleting, record each discard as a
   `JobDiagnosticEntry` (`step = "watchlist-discard"`) with the item title, source products,
   extracted services and regions. Rate-limit to keep diagnostics bounded, and always include a
   per-job discard count. The Crawl Jobs page surfaces the discard count and a sample list.
5. Existing behaviour is preserved: items that do not match are still not shown or stored as
   feed items.

### R4 - Deadline recovery (fixes RC5)

1. When `changeType` is `retirement`, `deprecation`, `breaking-change` or `migration-required`
   and `llmAnalysis.deadline` is null or unparseable, run a deterministic extractor over the title
   and summary. It should handle patterns such as "on September 30, 2028", "by 30 September 2028",
   "on 08 September 2028" and "03/31/2027".
2. A recovered deadline sets `lifecycleDeadline` and marks `deadlineSource = "extracted"`
   (versus `"llm"`).
3. Retirement-type items that still have no deadline are counted, listed in a
   "Retirements without deadline" diagnostic, and shown in the calendar UI as an
   **Undated retirements** group, so they are never silently invisible.

## 6. API changes

```
GET /api/calendar?from=2026-07-03&to=2031-10-01
```

Response items gain `deadlineSource` (`llm` | `extracted`) and `publishDate`. Existing fields are
unchanged. The `CalendarItem` TypeScript type is extended accordingly. A separate
`GET /api/calendar/undated` returns retirement-type items without a deadline.

## 7. Acceptance criteria

1. With more than 500 retained feed items, an item published 2024-03 with deadline 2027-03-31 is
   returned by `/api/calendar` and shown in the 7-12 months bucket (relative to 2026-10).
2. Of the 18 flagged items present in the catalog, all whose service is on the watchlist appear
   in the correct horizon bucket after migration and one crawl.
3. Quarter view shows quarters through 2029 when items are due in 2029.
4. `/api/dashboard/stats` retirement and urgent counts equal the counts derived from
   `/api/calendar`.
5. A watchlist entry "Virtual Machines" retains "OS disks on Standard HDD", "Azure Disk
   Encryption" and "VM Insights Map and Dependency Agent" announcements.
6. Every watchlist discard is reflected in the job's discard count and diagnostics.
7. A retirement whose LLM deadline is null but whose title states "will be retired on September
   30, 2028" receives `lifecycleDeadline = 2028-09-30` and `deadlineSource = extracted`.
8. Existing unit tests pass. New tests cover: deadline normalization, horizon bucketing, the
   containment matcher (positive and over-match negatives), deadline extraction patterns and
   calendar query paging.

## 8. Rollout

1. Deploy API and JobHost together. The migration in R1.6 runs once and is idempotent.
2. Verify on the demo environment: compare `/api/calendar` counts before and after, and check that
   long-horizon items appear.
3. Publish release notes for customers explaining that new alias matching may retain additional
   items after the next crawl.

## 9. Risks

| Risk | Mitigation |
|---|---|
| Containment matching retains noise | Generic-token guard; alias map curated; discard/retain diagnostics to tune |
| Larger calendar payloads | Projection-only response; deadline window bounds the result |
| Cosmos RU cost of range query | Default indexing covers `/lifecycleDeadline`; query is bounded by window |
| Deadline extractor misreads dates | Only applied when LLM deadline is missing; `deadlineSource` exposes provenance |

## 10. Future work

- **Inventory-aware retirements:** ingest Azure Advisor service-retirement recommendations or
  Resource Graph data for the customer's subscriptions using the existing UAMI. This shows which
  retirements affect resources the customer actually deploys, which is how the legacy dashboard
  works, and it covers retirements with no Azure Updates post.
- Optional export of the horizon view (PDF or image) for governance reporting.

## 11. Implementation and verification (2026-10-01)

Implemented R1-R4. One design refinement came out of live testing: set-based token containment
matched "Azure Storage" (family member "Azure Files") to "Azure NetApp Files", so containment now
requires a contiguous phrase, and a single-word part must be the first significant word.

| Check | Result |
|---|---|
| Unit tests (`dotnet test tests\AzRadar.Shared.Tests`) | 173/173 pass |
| Frontend type-check | Pass |
| `/api/calendar`, default window | 30 items before, 37 after deploy, 42 after one crawl |
| Horizon distribution after deploy | 7-12 months: 4, 13-24 months: 7, 25-36 months: 9 |
| `/api/calendar/undated` | 55 lifecycle items with no announced date are now visible |
| `/api/dashboard/stats` deadlines vs `/api/calendar` | Equal (42 = 42) |
| Invalid window (`?from=bad`) | HTTP 400 |
| Deadline backfill (Cosmos Patch metrics) | 17 feed items and 616 doc insights stamped. 616 exceeds the old 500 cap |
| Crawl 1 (new matcher) | 19 retained (4 the week before), 82 discards audited as diagnostics |
| Crawl 2 (phrase containment) | The 3 Azure NetApp Files false positives were removed via "no longer matches" |

Remaining gap in the demo environment: 9,842 Azure Updates were stored by an earlier backfill with
LLM analysis skipped, so they have no deadline and are not shown. Showing their long-horizon
retirements requires a bounded re-analysis. Customer environments with fully analyzed history
benefit directly from the removed 500-item cap.
