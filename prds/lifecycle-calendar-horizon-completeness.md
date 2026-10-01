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
   filtered set, instead of a fixed four quarters. **Superseded by R5**, which replaces this
   data-driven range with a fixed three-year quarter grid.
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

### R5 - Three-year quarter grid (legacy dashboard parity) — Implemented (see section 12)

**Problem.** The legacy dashboard used by Regulated Industry plans 36 months ahead (Within 6
months, 7-12, 13-24 and 25-36 months). Before this PRD, the CloudLens quarter view showed one
year. With R2.3 it grows and shrinks with the data, so the layout changes as filters change, and
empty future quarters are not shown. Platform owners asked for a stable layout that visibly
covers three years, for example Q1 2028 and Q2 2028.

**Requirements**

1. **Fixed window.** The quarter view always shows 12 quarters: the current calendar quarter
   plus the next 11. On 2026-10-01 that is Q4 2026 to Q3 2029. Each quarter appears even when it
   has no items ("No items this quarter"), so the user can see that the period is covered.
2. **Edge buckets.**
   - A leading **Past due** block holds items whose deadline is before the current quarter
     (within the calendar API's 90-day lookback). It is shown only when it has items.
   - A trailing **Later (after <last quarter>)** block holds items due after the 12th quarter.
     It is also shown only when it has items.
   - No item is silently dropped from the view.
3. **Year grouping.** Quarters are grouped by calendar year. Each year has a header with the
   year and its item count, followed by a four-column row aligned Q1-Q4. A partial first or
   last year keeps its column positions: Q4 2026 sits in the Q4 column.
   - Medium width: two columns. Narrow width: one column. Column alignment is dropped at both
     widths.
4. **Horizon colour cue.** Each quarter card gets a top accent in the colour of its legacy
   horizon band, so the view reads like the legacy report.
   - Bands, by quarter offset from the current quarter:
     - offsets 0-1 → Within 6 months (red)
     - offsets 2-3 → 7-12 months (orange)
     - offsets 4-7 → 13-24 months (blue)
     - offsets 8-11 → 25-36 months (green)
   - Past due uses grey and Later uses neutral.
   - The bands match the Horizon view exactly when today is at the start of a quarter.
     Otherwise they are an approximation (at most two months). A legend explains this.
5. **Dense quarters.** Each quarter card shows a count badge. When a card has more than eight
   items, its body scrolls internally (bounded height), so 12 quarters stay scannable on one
   screen. Card content and the detail panel are unchanged (reuse `renderBucketCard`).
6. **Header.** A one-line summary above the grid, for example "Q4 2026 – Q3 2029 · 12 quarters ·
   42 items". Filters (change type, severity, source, keyword) apply to all buckets and counts.
7. **No API change.** The default `/api/calendar` window (today − 90 days to today + 60 months)
   already covers the grid and the Later bucket.
8. **Horizon filter (quarter view only).** A single-select control above the grid with the
   options below. The default is All.

   | Option | Quarters shown |
   |---|---|
   | All | Past due (if any) + all 12 quarters + Later (if any) |
   | Past due | Past due block only (deadline before the current quarter, within the 90-day lookback) |
   | Within 6 months | Quarter offsets 0-1 (red band) |
   | Within 7-12 months | Quarter offsets 2-3 (orange band) |
   | Within 13-24 months | Quarter offsets 4-7 (blue band) |
   | After 25 months | Quarter offsets 8-11 (green band) + Later (if any) |

   - Selection is by quarter band, the same mapping as the colour cue in R5.4. Whole quarter
     blocks are shown or hidden, never split, so the filter and the colours always agree.
   - Past due has its own option, mirroring the legacy dashboard's separate "Out of Support"
     column. It is excluded from every time-band option, so the bands never overlap. When Past
     due has no items, its option still appears, shows a count of 0 and displays an empty state
     ("Nothing past due").
   - Year groups with no visible quarters are hidden. The remaining quarters keep their Q1-Q4
     column alignment.
   - Each option shows its item count, for example "Within 13-24 months (7)". Counts respect the
     other active filters (change type, severity, source, keyword).
   - The summary line reflects the selection, for example "Q4 2027 – Q3 2028 · 4 quarters · 7 items".
   - The selection is kept while switching views during the session and resets to All on reload.
     It does not affect the Timeline, Horizon or Calendar views.
9. **Implementation shape.**
   - Move the bucketing into a pure helper, `buildQuarterGrid(items, today, quarterCount = 12)`,
     in `src/az-radar-ui/src/utils/quarterGrid.ts`.
   - It returns `{ pastDue, years: [{ year, quarters: [{ label, index, band, items }] }], later }`.
   - It replaces `getQuarters` and `quarterOf`. All date handling stays local-date based
     (`parseDeadline`) to avoid UTC shifting.

**R5 acceptance criteria (as of 2026-10-01)**

1. The quarter view renders exactly 12 quarter cards, Q4 2026 to Q3 2029, including Q1 2028 and
   Q2 2028, whether or not those quarters have items.
2. An item due 2028-12-31 appears in Q4 2028 with the 25-36 month (green) accent. An item due
   2028-09-30 appears in Q3 2028 with the 13-24 month (blue) accent. An item due 2027-03-31
   appears in Q1 2027 with the red accent.
3. An item due 2026-08-31 appears under Past due. An item due 2029-12-31 appears under Later.
4. The sum of all bucket counts equals the number of filtered calendar items.
5. Applying a change type, severity or keyword filter changes counts but never the set of
   12 quarter cards.
6. With the horizon filter on Within 13-24 months (on 2026-10-01), only Q4 2027 to Q3 2028 are
   shown, under year headers 2027 and 2028. The 2026 and 2029 groups and Past due are hidden.
7. Within 6 months shows only Q4 2026 and Q1 2027. After 25 months shows Q4 2028 to Q3 2029 plus
   Later when it has items. Past due shows only the Past due block (an item due 2026-08-31
   appears there and in no other option). All restores the full grid.
8. Each option's count equals the number of items in the blocks it shows. The counts of Past due
   and the four time bands add up to the count of All.
9. Frontend type-check and the production build pass. A live check on the demo environment shows
   the grid and each filter option with the deployed data.

## 7. Acceptance criteria

1. With more than 500 retained feed items, an item published 2024-03 with deadline 2027-03-31 is
   returned by `/api/calendar` and shown in the 7-12 months bucket (relative to 2026-10).
2. Of the 18 flagged items present in the catalog, all whose service is on the watchlist appear
   in the correct horizon bucket after migration and one crawl.
3. Quarter view shows quarters through 2029 when items are due in 2029 (see R5 criteria below).
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

## 12. R5 implementation and verification (2026-10-01)

Implemented R5 in the UI only. `src/az-radar-ui/src/utils/quarterGrid.ts` holds the pure bucketing
and filter logic, and `LifecycleCalendarPage.tsx` renders it. The API is unchanged.

| Check | Result |
|---|---|
| Production build (`npm run build`) | Pass |
| ESLint on changed files | No new findings. The two `set-state-in-effect` findings already exist in the previous version |
| Grid logic (compiled helper run under Node, today = 2026-10-01) | Acceptance criteria 1-3 and 6-8 pass, including the empty-data and empty Past due cases |
| Live grid (`/api/calendar`, 42 items) | 12 quarters, Q4 2026 to Q3 2029 |
| Live filter counts | Past due 12, Within 6 months 5, 7-12 months 9, 13-24 months 6, After 25 months 10. These add up to All (42) |
| Live per-quarter counts | Q4 2026: 4 · 2027: 1/6/3/0 · 2028: 1/3/2/1 · 2029: 8/1/0 · Later: 0 |

The quarter-band counts differ slightly from the month-based Horizon view (section 11). The quarter
view assigns whole quarters to a band, so a deadline near a band edge can land one band apart. This
is the R5.4 approximation, and it is exact on the first day of a quarter.
