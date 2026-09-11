# PRD - Service and Region Scoped Intelligence

> **Status:** Implemented
> **Issue:** #7
> **Feature area:** Azure Updates and Microsoft Learn intelligence
> **Date:** 2026-09-11

## 1. Summary

Extend each Service Watchlist entry with optional Azure regions and use the existing LLM
enrichment output to decide whether an Azure Update or Microsoft Learn document is relevant
before it is retained. The product must stop showing updates for unwatched services and must
exclude explicitly regional updates that do not overlap the regions selected for that service.

The design does **not** send the complete watchlist to the LLM for every item. The LLM already
extracts canonical affected services; it will additionally extract affected Azure regions.
AzRadar then applies a deterministic matcher that understands aliases, Azure service-name word
order, and common acronyms such as `AKS`. This keeps token use and latency constant as the
watchlist grows beyond 50 entries.

## 2. Goals

1. Let a user add `{ serviceName, regions[] }` from the Service Watchlist UI.
2. Keep regions optional. No selected region means the service is watched globally.
3. Populate the region selector from a maintained Azure public-region catalog exposed by the API.
4. Retain Azure Updates and Microsoft Learn insights only when they match a watched service and
   its optional region scope.
5. Remove a previously stored item when a crawl determines that it no longer matches the current
   watchlist.
6. Preserve existing watchlist documents and API clients; missing `regions` deserialize as an
   empty global scope.

## 3. Non-goals

- Resource-instance or subscription-specific filtering.
- Service Health routing changes.
- Sovereign-cloud regions in the first region catalog.
- An embedding deployment or a second LLM call solely for relevance classification.
- Automatically rewriting user-entered service names.

## 4. User journeys

1. A user enters `AKS`, selects `West Europe` and `North Europe`, and adds the entry.
2. An AKS announcement that applies globally is retained.
3. An AKS announcement explicitly limited to West Europe is retained.
4. An AKS announcement explicitly limited to East US is discarded.
5. An Artifact Streaming announcement is discarded because no watched service matches it.
6. A user adds `Azure App Service` with no regions; App Service announcements from any or no
   region are retained.

## 5. Data and API

`WatchlistItem` gains:

```json
{
  "regions": ["West Europe", "North Europe"]
}
```

`regions` is always returned as an array. Empty means global scope.

- `GET /api/azure-regions` returns canonical display names sorted alphabetically.
- `POST /api/watchlist` accepts `regions`.
- Unknown regions and an empty service name return `400 Bad Request`.
- Existing aliases, search terms, and resource-provider fields remain supported.

`LlmAnalysis` gains `affectedRegions: string[]`. The analysis prompt requires canonical Azure
region display names where possible, `global` for an explicitly worldwide change, and an empty
array when the source does not identify a region.

## 6. Relevance algorithm

### 6.1 Service matching

For each analyzed item:

1. Build candidates from `affectedServices`; Azure Updates also use source product names when the
   model omits services.
2. Build watch terms from `serviceName`, `aliases`, and `searchTerms`.
3. Compare normalized names, order-independent significant token sets, and acronyms.
   - `AKS` matches `Azure Kubernetes Service`.
   - `Azure Redis Cache` matches `Azure Cache for Redis`.
   - Broad substring matching is deliberately avoided to prevent unrelated-service false matches.
4. At least one watched service must match.

The LLM performs semantic extraction once as part of the normal enrichment call. Matching cost is
then local and approximately `O(watchlist entries x affected services)` with very small strings;
the prompt does not grow with watchlist size.

### 6.2 Region matching

- A watchlist entry with no regions matches the service globally.
- An analysis with no affected regions, or one marked `global`/`all regions`, is treated as a
  service-wide announcement and matches any selected region.
- If the source identifies one or more specific regions, at least one must equal a selected
  canonical region after case/spacing normalization.

### 6.3 Persistence

- Relevant new or changed content is stored normally.
- Irrelevant new content is not stored.
- If an existing item is re-evaluated as irrelevant, it is permanently deleted from its Cosmos
  container together with any cached blast-radius summaries derived from it.
- Unchanged stored items are still checked against the current watchlist before the handler takes
  its normal deduplication shortcut. This makes watchlist removals effective on the next crawl.
- Empty watchlist means no Azure Updates or Microsoft Learn intelligence is retained by these
  handlers.
- LLM failure produces no affected service and therefore fails closed rather than displaying an
  unverified item.
- Explicit Azure Updates backfills created with `skipLlmAnalysis=true` preserve their existing
  no-LLM behavior and are not relevance-filtered until they are analyzed by a normal changed-item
  crawl.

## 7. User interface

The Service Watchlist add row contains:

- Service name text input.
- Optional multi-select Azure region dropdown.
- Add button.

The table includes a Regions column. Empty scope is displayed as `All regions`; scoped entries
show their selected canonical region names.

## 8. Security and operations

- No new credentials, secrets, Azure resources, or network dependencies.
- Azure OpenAI and Cosmos DB continue using the existing user-assigned managed identity.
- Region names are served from application code, so loading the page does not require ARM Reader
  access or an Azure subscription API call.
- Filtering occurs inside JobHost before persistence, so downstream dashboard, calendar, blast
  radius, and API views inherit the same scoped dataset.

## 9. Failure handling

- Invalid watchlist input is rejected explicitly with `400`.
- Cosmos deletion failures propagate and fail the crawl rather than reporting success.
- LLM failures are logged by the analyzer and the resulting unclassified item is discarded.
- Concurrent feed replacement retains the existing optimistic-concurrency behavior.

## 10. Test and acceptance criteria

1. `AKS` matches an analysis for `Azure Kubernetes Service`.
2. Reordered canonical service names match, while an unrelated service does not.
3. Empty region scope matches global and regional announcements.
4. A global announcement matches a region-scoped watch.
5. Explicit non-overlapping regions do not match.
6. Azure Updates stores matching items and deletes known non-matching items.
7. Microsoft Learn stores matching documents and rejects/deletes unrelated search results.
8. Existing watchlist JSON without `regions` remains valid.
9. Frontend type-check, focused unit tests, and the .NET solution build pass.
10. Deployment uses the repository's blue-green image strategy and live API/UI smoke tests confirm
    region catalog retrieval and scoped watchlist creation.

## 11. Rollout and rollback

Deploy API/UI and JobHost using opposite blue-green tags. Run a controlled crawl with a scoped
watchlist and verify retained/discarded counts and dashboard contents. Rollback is an App Service
image-tag switch. The additive Cosmos fields require no database migration; older images ignore
`regions` and `affectedRegions`.
