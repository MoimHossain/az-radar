# PRD - Azure DevOps Wiki Dispatch for Service Health

> **Status:** Proposed
> **Author:** @MoimHossain (drafted with Copilot)
> **Date:** 2026-09-23
> **Feature area:** Service Health dispatching targets
> **Product name:** CloudLens
> **Related:** `prds/service-health-dispatch.md`,
> `prds/service-health-identity-and-auth-flow.md`

---

## 1. Executive Summary

Extend CloudLens Service Health dispatching with an Azure DevOps Wiki target while preserving the
existing Microsoft Teams channel experience.

A CloudLens platform administrator will be able to register multiple designated Azure DevOps Wiki
pages and assign each page the Azure regions in which that audience operates.
CloudLens will continuously maintain each page as a Service Health hub containing:

- Current status across all four supported Service Health event families.
- An executive action recommendation.
- Active Service Issues.
- Upcoming Planned Maintenance.
- Active Health Advisories.
- Active Security Advisories.
- Recently resolved or completed events.
- Static guidance, frequently asked questions, and support information.

The pilot will implement both supported authentication options:

- A Personal Access Token (PAT), accepted once through the UI, stored as a secret in Azure Key
  Vault, and never returned by the API or stored in Cosmos DB.
- A user-assigned managed identity selected by client ID, using short-lived Microsoft Entra tokens
  and Azure DevOps resource permissions.

The project owner's development validation will use the PAT route because the Azure DevOps
organization used for testing is connected to a different Microsoft Entra tenant than the
CloudLens Azure deployment. Managed identity remains a full pilot deliverable because it is the
preferred customer authentication model, even when that local test environment cannot complete a
same-tenant managed identity end-to-end validation.

The wiki is a **state projection**, not an append-only message destination. Each relevant event
change causes CloudLens to rebuild the complete Markdown document from the latest persisted Service
Health state and update the same wiki page URI. A daily reconciliation also rebuilds the page so a
temporary delivery failure cannot leave it permanently stale.

Each wiki projection is region-scoped. An event with one or more canonically normalized affected
Azure regions is eligible only when at least one region exactly intersects the target's configured
regions. CloudLens uses a conservative fallback for unscoped events: explicit global scope, missing
or empty region data, and values from which no canonical region can be resolved are dispatched to
every active wiki target. It is safer to show an event whose regional scope is uncertain than to
silently discard potentially relevant Service Health information.

---

## 2. Current State

The Service Health page currently contains three top-level tabs:

1. **Register subscription**
2. **Recent ingested events**
3. **Pending delivery intents**

The Register subscription tab also contains the existing **Teams app channel routing** section.
Teams destinations are discovered when the CloudLens Teams app is installed and registered in a
channel. A platform administrator then selects any combination of:

- Service Issues
- Planned Maintenance
- Health Advisories
- Security Advisories

The current routing processor creates durable delivery intents only for registered Teams app
channels whose selected event families match the normalized Service Health event.

This enhancement must retain that Teams behavior while making dispatch destinations easier to
discover and manage.

---

## 3. Problem Statement

Teams messages are effective for immediate awareness, but they are not an ideal durable,
organization-wide status view:

- Important events can move out of view as channel conversations continue.
- Executives and service owners may not belong to the dispatch channel.
- Operators need a stable page they can bookmark and share.
- Readers need one current view rather than a sequence of event messages.
- Customers may already use an Azure DevOps Wiki as their platform operations knowledge hub.

CloudLens needs a second dispatch target that converts its current Service Health state into a
clear, continuously maintained wiki page without requiring operators to copy event information
manually.

---

## 4. Goals and Non-Goals

### 4.1 Goals

1. Add **Dispatching targets** as a fourth top-level tab on the Service Health page.
2. Move the existing Teams channel routing UI into Dispatching targets without changing its
   registration or event-family selection behavior.
3. Add an **Azure DevOps Wiki** target-management view.
4. Let an administrator register a designated Azure DevOps Wiki page by entering its browser URI,
   display name, and authentication details.
5. Support PAT authentication in the pilot.
6. Support user-assigned managed identity authentication in the pilot.
7. Publish all four supported Service Health event families to the wiki projection.
8. Update the same page when an event is created, materially updated, resolved, or completed.
9. Run a daily reconciliation that repairs missed or stale wiki projections.
10. Preserve an auditable delivery history and expose actionable authentication, permission,
    concurrency, and rendering failures.
11. Keep secrets out of Cosmos DB, application logs, API responses, delivery intents, and Service
    Bus messages.
12. Require every Azure DevOps Wiki target to define one or more Azure regions and include only
    events whose normalized affected-region set intersects that target configuration.
13. Reuse one canonical Azure-region resolver across target validation, Service Health
    normalization, event-driven routing, reconciliation, rendering, and synthetic tests.

### 4.2 Non-Goals for the Initial Release

- Creating a new Azure DevOps organization, project, wiki, repository, or page.
- Supporting Azure DevOps Server/on-premises in the pilot.
- Letting CloudLens browse all projects or wikis available to the credential.
- Creating one wiki page per event.
- Creating child pages, attachments, work items, pull requests, or comments.
- Replacing the configured page URI after each update.
- Allowing custom Markdown templates in the initial release.
- Selecting a subset of event families for the wiki in the initial release. The wiki always
  represents all four supported families.
- Fuzzy, geographic-proximity, paired-region, geography, or country inference. Region routing is
  based only on canonical Azure-region identity.
- Using the PAT outside the specific configured Azure DevOps organization and wiki operation.
- Automatically attaching an arbitrary user-assigned managed identity to the CloudLens App Service.
- Automatically creating Azure DevOps organization membership, licenses, or wiki permissions for a
  managed identity.
- Implementing cross-tenant managed identity federation or onboarding in the pilot.
- Changing existing Teams dispatch behavior, card layout, installation, or registration.

---

## 5. Personas and User Stories

| Persona | Need |
|---|---|
| CloudLens platform administrator | Register, test, enable, update, and remove a wiki dispatch target. |
| Platform operations engineer | Trust that the wiki reflects the latest persisted Service Health state. |
| Executive or service owner | Read a concise current status and understand whether action is required. |
| Security administrator | Use managed identity for customers where possible and ensure PATs are protected where required. |
| Auditor | See when, why, and by which target version a wiki page was updated or failed. |

### User stories

1. *As a platform administrator,* I can open Service Health > Dispatching targets > Azure DevOps
   Wiki and register a designated wiki page.
2. *As a platform administrator,* I can paste the normal browser URI of an Azure DevOps Wiki page
   rather than manually discovering REST API identifiers.
3. *As a platform administrator,* I can provide a PAT once and see only a masked configured state
   afterward.
4. *As a platform administrator,* I can test authentication and page-write permission before
   enabling automatic updates.
5. *As a reader,* I can bookmark one page whose URI remains unchanged as Service Health events
   change.
6. *As an operator,* I see all four event families represented even when one or more categories
   currently have no active events.
7. *As an operator,* I see recently resolved events move out of the active section without losing
   short-term history.
8. *As an operator,* a burst of related event updates does not create conflicting or out-of-order
   wiki edits.
9. *As an administrator,* I can rotate the PAT without deleting and recreating the target.
10. *As a platform administrator,* I can select managed identity, enter a client ID, and receive
    actionable prerequisites if that identity is not attached or authorized.
11. *As a platform administrator,* I can select the Azure regions where my estate is deployed when
    I register a wiki target and edit that selection later.
12. *As an operator,* my wiki contains an event only when the event directly names at least one
    region configured for that wiki.
13. *As an operator,* I can publish synthetic matching and non-matching regional events through the
    normal ingestion pipeline to prove the routing behavior.

---

## 6. User Experience

### 6.1 Service Health top-level tabs

The Service Health page will contain:

1. **Register subscription**
2. **Recent ingested events**
3. **Pending delivery intents**
4. **Dispatching targets**

The existing Teams routing card will be removed from Register subscription and placed under
Dispatching targets. No Teams fields or behaviors are otherwise changed.

### 6.2 Dispatching targets navigation

Use a Fluent UI v9 secondary `TabList` inside the Dispatching targets view:

- **Teams channels**
- **Azure DevOps Wiki**

Secondary tabs are preferred over a page-local left navigation because there are initially only two
peer target types and the existing page is already tab-oriented. A left navigation can be introduced
later if the number of destination types grows substantially.

### 6.3 Teams channels subtab

Render the existing Teams channel routing experience as-is:

- Registered CloudLens Teams app destinations.
- Team and channel name.
- Registration state.
- Channel identifier.
- Four event-family checkboxes.
- Selecting no event families pauses delivery through the existing behavior.

This PRD does not introduce a separate Teams enabled switch.

### 6.4 Azure DevOps Wiki subtab

The empty state explains that CloudLens maintains each designated page as a live Service Health
hub.
The registration form contains:

| Field | Required | Behavior |
|---|---:|---|
| Display name | Yes | Human-readable target name, for example `Platform Service Health Wiki`. |
| Wiki page URI | Yes | Normal browser URI copied from Azure DevOps. |
| Authentication method | Yes | `Personal Access Token` or `Managed Identity`; both are pilot options. |
| Personal Access Token | For PAT | Password input, write-only, never redisplayed. |
| Managed identity client ID | For managed identity | User-assigned managed identity client ID. |
| Azure regions | Yes | Searchable multi-select populated from the canonical Azure region catalog; at least one region is required. |

The Azure-region control must:

- Use Fluent UI v9 multi-select behavior with type-ahead search, selected-region tags, keyboard
  support, and a clear selected count.
- Display canonical Azure region names such as `West Europe` and `Japan East`.
- Submit canonical values from `/api/azure-regions`; free-form values are not accepted.
- Deduplicate selections case-insensitively and impose a configurable upper bound, initially 100,
  to protect request and document size.
- Explain that events naming known Azure regions must match a selected region, while global or
  unscoped events are shown because their impact cannot be safely narrowed.

After registration, show:

- Display name.
- Organization, project, wiki, and page path parsed from the URI.
- Authentication type.
- Selected Azure regions and selected-region count.
- Credential state, such as `configured`, `expiring`, `invalid`, or `rotation required`.
- Target state: `draft`, `validating`, `active`, `degraded`, `disabled`, or `permission-required`.
- Last successful update.
- Last attempted update.
- Last rendered content hash.
- Last error with a safe, actionable message.
- **Test connection**, **Publish now**, **Replace credential**, **Disable/Enable**, and **Remove**
  actions.
- **Edit regions**, which validates and saves the complete replacement region set and queues a
  full projection refresh.

The raw PAT, Key Vault secret URI, access token, and authorization header must never be displayed.

### 6.5 Registration flow

1. Administrator enters target details, selects one or more Azure regions, and provides a PAT or
   managed identity client ID.
2. API validates the URI and accepts the PAT over HTTPS.
3. API stores the PAT in Key Vault and persists only the resulting secret reference.
4. API parses and resolves the organization, project, wiki identifier, and page path.
5. API reads the page and captures its current ETag.
6. API performs a safe write test only after explicit **Test connection** or **Save and activate**:
   it publishes the CloudLens page content, not arbitrary test text.
7. Target becomes active only after authentication, permission, read, and write checks pass.
8. The page URI remains the configured browser URI.
9. Changing the region selection queues a target refresh. Events removed from scope disappear from
   the next successful projection; newly in-scope events appear without requiring re-ingestion.

If validation fails, the target is retained in a non-active actionable state when safe to do so.
The UI must distinguish invalid URI, authentication failure, missing wiki permission, page not
found, tenant mismatch, throttling, and transient Azure DevOps failure.

---

## 7. Product Behavior

### 7.1 Multiple targets, one stable page per target

The initial release supports multiple active Azure DevOps Wiki targets per CloudLens deployment.
Each target has independent credentials, delivery state, reconciliation, and a stable page URI.
Registering the same canonical page URI more than once is rejected.

CloudLens always updates each target's configured page. It must not:

- Generate a new page per event.
- Change the configured page path.
- Create date-stamped pages.
- Redirect readers to a new URI.
- Append duplicate copies of an event on each refresh.

### 7.2 Events represented

The page includes the four normalized Service Health event families already supported by CloudLens:

| CloudLens event type | Wiki section |
|---|---|
| `ServiceIssue` | Active Service Health Events |
| `PlannedMaintenance` | Upcoming Planned Maintenance |
| `HealthAdvisory` | Active Health Advisories |
| `SecurityAdvisory` | Security Advisories |

All events from subscriptions currently registered in the CloudLens deployment are eligible for the
wiki only after applying the target's region filter. Tenant-isolation requirements from the
existing Service Health design continue to apply.

### 7.3 Region-scoped event eligibility

Every Azure DevOps Wiki target stores an explicit, non-empty set of canonical Azure region names.
For a target `T` and canonical event `E`:

```text
eligible(E, T) =
  E.scopeClassification is global-or-unscoped
  OR intersection(E.affectedRegions, T.includedRegions) is not empty
```

The rule applies equally to Service Issues, Planned Maintenance, Health Advisories, and Security
Advisories. Event family does not bypass regional eligibility.

Matching requirements:

- Compare canonical region identities using ordinal case-insensitive equality.
- Do not use substring, token overlap, edit distance, LLM classification, or geographic inference.
  For example, `Central US` must not match `North Central US`, and `East US` must not match
  `East US 2`.
- An event that names multiple regions is eligible when any canonical event region intersects the
  target's configured regions.
- Missing or empty event region data is classified as unscoped and matches every active wiki
  target.
- `Global`, `Worldwide`, `All regions`, and equivalent approved markers are classified as global
  and match every active wiki target.
- If no canonical Azure region can be resolved from one or more non-empty source values, the event
  is classified as unscoped/unknown and matches every active wiki target.
- If at least one canonical Azure region resolves, normal intersection matching applies. Additional
  unresolved values do not turn an otherwise known, non-matching regional event into a global
  match.
- Synthetic events follow exactly the same normalization and matching path as production events.
- Teams routing remains unchanged and does not inherit wiki region filtering.

Region identity must be implemented through a shared `AzureRegionResolver`, evolved from the
existing Azure region catalog and the normalization approach used by `WatchlistRelevanceMatcher`.
The resolver owns:

- Canonical display names used by API and UI.
- Stable normalized keys created by removing non-alphanumeric characters and applying invariant
  lowercase.
- Explicit aliases for known Azure forms, including display names and ARM/location identifiers
  such as `West Europe`/`westeurope` and `East US 2`/`eastus2`.
- Collision detection at startup or test time so two canonical regions cannot share a normalized
  key or alias.
- `TryResolve` and collection-normalization operations that return canonical names plus rejected
  values for diagnostics.

The alias map is explicit and version-controlled. Unknown text is never guessed into a region.
Target input containing an unknown value is rejected with `invalid-region`; event input containing
unknown values is retained for audit and counted in telemetry. An unknown-only event uses the
conservative all-target fallback; an event with at least one resolved canonical region uses normal
intersection matching.

The Service Health normalizer must produce `affectedRegions` as a deduplicated collection rather
than relying only on the current singular `region` string. It must accept the known source shapes:
scalar strings, arrays, and delimited region lists from approved Service Health fields. Delimiter
splitting is allowed only before exact alias resolution; it must not create fuzzy matches. The
legacy singular `region` field may remain temporarily for API compatibility and display, but
routing and projection must use `affectedRegions`.

Service Health updates can omit region data after the initial notification, especially during
resolution. Before filtering, the canonical event-lifecycle resolver groups versions by tenant,
subscription, and tracking ID and uses the newest non-empty normalized affected-region set. A later
explicit non-empty region set replaces the earlier set; an empty update does not erase previously
known scope. This prevents a resolution update with no region field from leaving an old active
event stranded on a target page.

Region filtering is enforced at two boundaries:

1. **Ingestion routing:** create/coalesce a wiki refresh intent only for active wiki targets whose
   configured regions intersect the event's effective affected regions.
2. **Projection:** immediately before rendering, the wiki worker resolves current event lifecycle
   state and filters the full canonical event set for that target again.

The second check is authoritative. It protects daily reconciliation, manual publish, retries,
target-region edits, and replay from rendering out-of-scope events. Filtering must occur before
page counts, status recommendations, rendered hashes, and page-size calculations.

At scale, ingestion must not issue one database query per target or repeatedly scan all targets for
every event. The dispatcher maintains a tenant-scoped immutable routing index:

```text
canonical region key -> active wiki target IDs
```

The index is built from safe target metadata, updated after target writes or by Cosmos DB change
feed, and atomically replaced so readers never observe a partially updated index. A cache miss or
stale-index detection may fall back to one bounded target-list read for the tenant, not one read per
target. Intent creation remains idempotent, so index refresh races may create duplicate refresh
requests but cannot create duplicate wiki writes.

Projection must not depend on a fixed maximum such as the latest 1,000 stored events. The worker
uses paginated, tenant-scoped queries for active and recently resolved lifecycle candidates, then
collapses versions and applies the target region filter. Query windows and continuation tokens must
ensure every event eligible for the configured wiki history window can be considered.

Existing wiki targets created before this field exists must not silently retain all-region
behavior. Migration sets them to `configuration-required`, stops automatic and manual publication,
and prompts an administrator to select at least one region. Saving valid regions restores the prior
enabled state and queues a full refresh.

### 7.4 Update triggers

Create or coalesce a wiki refresh request when:

- A new supported event is persisted.
- A material update changes status, title, summary, service, region, timing, impact, or recommended
  actions.
- An event becomes resolved, closed, or completed.
- A previously resolved event becomes active again.
- An administrator activates or changes the wiki target.
- An administrator changes the target's selected regions.
- An administrator selects **Publish now**.
- The daily reconciliation schedule runs.

The target is a complete projection. A refresh request does not contain the final page body; the
worker reads the latest canonical event state immediately before rendering.

A known regional event whose canonical regions do not intersect a target does not create an intent
for that target. Global and unscoped fallback events create intents for every active wiki target.
This is an optimization only; projection-time filtering remains mandatory.

### 7.5 Update frequency

- Event-driven target: publish within five minutes of a material event update under normal
  conditions.
- Burst behavior: coalesce refresh requests for the same wiki target over a short configurable
  window, initially 30 seconds.
- Daily reconciliation: rebuild at least once every 24 hours, initially at a configurable UTC
  time.
- Manual publish: bypass the coalescing delay when there is no update already in progress.

The daily job is a repair and consistency mechanism. It must not create a new Azure DevOps commit
when the rendered content hash is unchanged, except when a product decision explicitly requires the
visible `Last Updated` timestamp to advance. For the initial release, `Last Updated` represents the
latest successful material projection update, avoiding empty daily commits.

### 7.6 Ordering and consistency

All work for one wiki target must be serialized using a Service Bus session or an equivalent
target-scoped lock. The implementation must:

- Prevent concurrent writes to the same page.
- Render from the latest canonical state, not from an older queued event snapshot.
- Collapse obsolete pending refreshes after a newer projection succeeds.
- Use the page ETag with `If-Match` for edits.
- On HTTP 412/precondition failure, reread the page and ETag, then retry within policy.
- Never overwrite an administrator's concurrent change silently.

CloudLens owns the full designated page in the initial release. The UI must warn that manual edits
to the page are overwritten on the next successful projection. Future template customization may
introduce protected or marker-delimited regions, but that is not part of this release.

### 7.7 Event lifecycle and retention in the page

The renderer classifies canonical events as follows:

| Classification | Rule |
|---|---|
| Active | Latest event status is not recognized as resolved, closed, completed, or cancelled. |
| Upcoming maintenance | Planned Maintenance is active and its end/completion state has not passed. |
| Recently resolved | Latest state is resolved, closed, completed, or cancelled and resolution time is within the configured history window. |
| Historical only | Resolved outside the page history window; retained in CloudLens storage but omitted from the wiki. |

The initial recently resolved history window is 30 days and configurable.

Where Azure payloads omit a normalized field, the renderer uses an explicit neutral value such as
`Not specified by Microsoft`; it must not invent source facts.

### 7.8 Empty states

Each dynamic section remains present even when empty. Examples:

- `No active service issues.`
- `No upcoming planned maintenance.`
- `No active health advisories.`
- `No active security advisories.`
- `No events were resolved during the last 30 days.`

The status table uses healthy/none states only when the corresponding active count is zero.

---

## 8. Status and Executive Recommendation Rules

The page summary status must be deterministic. CloudLens may use its existing LLM capability to
produce concise executive narrative, correlate or synthesize repetitive event details, explain
business impact, and improve recommended actions. The LLM cannot determine whether the page reports
normal operations or action required, change event counts, alter Microsoft-authored facts, or
remove customer-requested sections.

### 8.1 Category status

| Category | Zero active events | One or more active events |
|---|---|---|
| Service Issues | `🟢 Healthy` | `🔴 Active` |
| Planned Maintenance | `🟢 None` | `🟡 Upcoming` |
| Health Advisories | `🟢 None` | `🟡 Active` |
| Security Advisories | `🟢 None` | `🟡 Review` by default; `🔴 Action Required` when source severity/status policy requires action |

### 8.2 Overall recommendation

Apply the highest applicable state:

1. `🔴 Action Required`
   - At least one active Service Issue; or
   - At least one Security Advisory classified by deterministic policy as requiring action.
2. `🟡 Awareness Required`
   - No red condition; and
   - At least one Planned Maintenance, Health Advisory, or non-critical active Security Advisory.
3. `🟢 Normal Operations`
   - No active events in any of the four families.

Show only the applicable recommendation row prominently. The template may retain a small legend for
all three meanings.

---

## 9. Wiki Rendering Contract

The structure in this section is based on the customer's suggested Service Health dashboard and is
the baseline product requirement, not merely an illustrative example. CloudLens should preserve its
headings, category coverage, executive-summary intent, event detail sections, guidance, FAQ, and
footer. Implementation may make small Azure DevOps Markdown compatibility or accessibility
adjustments, but must not substantially replace or simplify the customer-provided experience
without product approval.

### 9.1 Rendering principles

1. Produce Azure DevOps Wiki-compatible Markdown.
2. Keep Microsoft-authored facts separate from CloudLens AI analysis.
3. Escape table delimiters, HTML-sensitive values, and Markdown sequences from external content.
4. Sanitize links and permit only approved `https` URLs.
5. Never render raw Event Hub payloads, credentials, tenant secrets, subscription authorization
   data, or internal exception details.
6. Sort active events deterministically:
   - Severity/action required first.
   - Event family.
   - Latest update descending.
   - Tracking ID as a stable tie-breaker.
7. Deduplicate correlated subscription copies into one displayed event while showing the affected
   services, regions, and approved subscription display names/count.
8. Bound page size and per-field length. If Azure DevOps rejects the page because of size, fail
   visibly rather than silently dropping arbitrary events.
9. Include a generator footer and the successful projection timestamp.
10. Preserve the customer-provided dashboard structure and static explanatory content.
11. Use deterministic rendering for headings, tables, counts, status indicators, ordering, empty
    states, tracking IDs, dates, services, regions, and source status.
12. Permit LLM assistance only for bounded narrative fields where it improves readability or
    usefulness.

### 9.2 LLM-assisted rendering

CloudLens already has an Azure OpenAI-backed Service Health analysis path. The wiki renderer may
reuse persisted LLM analysis or request a projection-specific structured analysis when needed.
Useful LLM-assisted fields include:

- A short executive narrative beneath the deterministic overall status.
- Plain-language business impact.
- Recommended operator preparation or next actions.
- A concise explanation of why an advisory matters.
- Consolidation of repetitive Microsoft updates into a current summary.
- Grouping or synthesis of closely related correlated records after deterministic correlation.

LLM use must follow these constraints:

- Rendering must still succeed with deterministic fallbacks when the LLM is unavailable, times out,
  or returns invalid output.
- The LLM receives only the approved, normalized event fields required for the requested
  enrichment.
- Security Advisory restrictions from the existing Service Health design continue to apply.
- LLM output is schema-validated, length-bounded, sanitized, and clearly distinguishable from
  Microsoft-authored content.
- LLM output cannot fabricate affected services, regions, tracking IDs, timestamps, severity,
  status, maintenance windows, event counts, or resolution state.
- LLM output cannot suppress an active event or downgrade a deterministic action-required state.
- The renderer should reuse stored analysis for an unchanged event version rather than call the LLM
  on every daily reconciliation.
- Prompt/model/schema version and whether fallback rendering was used are recorded in the delivery
  audit.

### 9.3 Source and AI field mapping

| Wiki field | Preferred source |
|---|---|
| Event title | Microsoft title; CloudLens suggested title only when clearly labelled. |
| Status | Normalized Microsoft event status. |
| Azure service | Normalized source service. |
| Regions | Correlated normalized regions. |
| Tracking ID | Microsoft tracking ID. |
| Last updated/published | Latest canonical source timestamp. |
| Summary/What happened | Microsoft summary. |
| Business impact | CloudLens analysis, labelled as CloudLens-generated, or deterministic fallback. |
| Recommended actions | Microsoft actions first; CloudLens recommendations separately labelled. |
| Latest Microsoft update | Latest Microsoft-authored update text. |
| Severity | Source severity or deterministic normalized severity. |
| Maintenance window | Source start/end values when provided. |

If the current event model does not yet contain a required source field, implementation must extend
normalization and persistence rather than infer it from unrelated text.

### 9.4 Customer-provided canonical page structure

The initial renderer must follow this customer-provided structure. Repeating event blocks are
emitted once per correlated event, and placeholders are replaced with sanitized values, approved
LLM-assisted narrative, deterministic fallback text, or explicit empty states.

```markdown
# CloudLens Service Health Hub

> Stay informed about Azure service events that may impact your business operations.

---

## Current Service Status

**Last Updated:** _{{LastUpdated}}_

| Category | Status | Active Events |
|-----------|--------|---------------|
| Service Issues | {{ServiceIssueStatus}} | {{ServiceIssueCount}} |
| Planned Maintenance | {{MaintenanceStatus}} | {{MaintenanceCount}} |
| Health Advisories | {{HealthAdvisoryStatus}} | {{HealthAdvisoryCount}} |
| Security Advisories | {{SecurityAdvisoryStatus}} | {{SecurityAdvisoryCount}} |

---

# Executive Summary

## Do I Need To Take Action?

| Overall Status | Business Recommendation |
|----------------|-------------------------|
| {{OverallStatus}} | {{BusinessRecommendation}} |

---

# Active Service Health Events

{{#ServiceIssues}}
## 🔴 Service Issue

### {{EventTitle}}

| Property | Value |
|----------|-------|
| Status | {{Status}} |
| Azure Service | {{AzureService}} |
| Affected Regions | {{Regions}} |
| Event Type | Service Issue |
| Tracking ID | {{TrackingId}} |
| Last Updated | {{LastUpdated}} |

### What Happened?
{{Summary}}

### Business Impact
{{BusinessImpact}}

### Recommended Actions
{{RecommendedActions}}

### Latest Microsoft Update
> {{LatestUpdate}}

---
{{/ServiceIssues}}

# Upcoming Planned Maintenance

{{#PlannedMaintenance}}
## 🟡 Planned Maintenance

### {{MaintenanceTitle}}

| Property | Value |
|----------|-------|
| Azure Service | {{AzureService}} |
| Region(s) | {{Regions}} |
| Scheduled Date | {{ScheduledDate}} |
| Maintenance Window | {{MaintenanceWindow}} |
| Status | {{Status}} |

### What Is Changing?
{{MaintenanceDescription}}

### Potential Business Impact
{{BusinessImpact}}

### Recommended Preparation
{{RecommendedPreparation}}

---
{{/PlannedMaintenance}}

# Active Health Advisories

{{#HealthAdvisories}}
## 🟡 Health Advisory

### {{AdvisoryTitle}}

| Property | Value |
|----------|-------|
| Azure Service | {{AzureService}} |
| Tracking ID | {{TrackingId}} |
| Published | {{PublishedDate}} |
| Status | {{Status}} |

### Summary
{{AdvisorySummary}}

### Why Should You Care?
{{BusinessExplanation}}

### Recommended Actions
{{RecommendedActions}}

---
{{/HealthAdvisories}}

# Security Advisories

{{#SecurityAdvisories}}
## 🔒 Security Advisory

### {{SecurityAdvisoryTitle}}

| Property | Value |
|----------|-------|
| Severity | {{Severity}} |
| Azure Service | {{AzureService}} |
| Tracking ID | {{TrackingId}} |
| Last Updated | {{LastUpdated}} |

### Summary
{{Summary}}

### Potential Risk
{{CustomerRisk}}

### Recommended Actions
{{RecommendedActions}}

---
{{/SecurityAdvisories}}

# Recently Resolved Events

| Date | Type | Service | Title | Status |
|------|------|---------|-------|--------|
{{ResolvedEventRows}}

---

# Understanding Service Health Notifications

### Service Issues
Unexpected Azure service disruptions that may affect availability, performance, connectivity, or
functionality.

### Planned Maintenance
Scheduled Microsoft activities that may temporarily affect Azure services and require preparation.

### Health Advisories
Announcements regarding platform changes, service retirements, configuration recommendations, or
future actions.

### Security Advisories
Important security-related notifications that may require assessment, review, or remediation.

---

# Why Am I Receiving These Notifications?

Cloud services are highly reliable, but planned maintenance, service incidents, health advisories,
and security notifications can occasionally occur.

Being informed allows your organization to:

✅ Respond faster to service disruptions

✅ Reduce troubleshooting effort

✅ Determine whether an issue is within Azure or your own environment

✅ Prepare for planned maintenance and platform changes

✅ Improve communication with users and stakeholders

✅ Protect critical business operations

---

# Frequently Asked Questions

## How often is this page updated?

The CloudLens Service Health Hub is automatically updated whenever Microsoft publishes a relevant
Azure Service Health notification. CloudLens also performs a daily reconciliation.

## Does every alert require action?

No. Some notifications are informational, while others may require preparation or remediation.
Recommended actions are provided when applicable.

## How do I know if my services are impacted?

Review the affected services, regions, business impact, and recommended actions for each
notification.

## Who manages this service?

The CloudLens Service Health Hub is maintained by the Platform Operations team to provide a
centralized and transparent view of Azure service health information.

---

# Contact & Support

If you require assistance or believe a service event is impacting your environment, contact your
support team using your standard support process.

---

_This page is automatically generated and maintained by CloudLens Service Health Intelligence._
```

The Platform Operations and support wording is fixed configuration for the initial release. Future
template customization is a separate feature.

---

## 10. Azure DevOps Wiki Integration

### 10.1 URI support

The user supplies the normal Azure DevOps Wiki browser URI, for example a URL whose host is
`dev.azure.com` and whose route identifies:

- Organization.
- Project.
- Wiki.
- Page path.

The API must parse, URL-decode, validate, and persist canonical identifiers. It must reject:

- Non-HTTPS URIs.
- Hosts outside the approved Azure DevOps host allowlist.
- URIs without an organization, project, wiki, or page path.
- Fragments or query parameters that cannot be mapped unambiguously.
- Credentials embedded in the URI.
- Redirects to an unapproved host.

Persist the original safe browser URI for display and a separately constructed REST API URI for
runtime use. Never call an arbitrary user-provided URL directly after registration; construct API
requests from validated identifiers.

### 10.2 REST API

Use the Azure DevOps Wiki Pages REST API:

```text
PUT https://dev.azure.com/{organization}/{project}/_apis/wiki/wikis/{wikiIdentifier}/pages
    ?path={path}&api-version=7.1
```

The request body is:

```json
{
  "content": "<complete rendered Markdown>"
}
```

For an existing page:

1. Read the current page and ETag.
2. Send the complete replacement Markdown with `If-Match: <etag>`.
3. Store the new response ETag and content hash after success.

The configured page is expected to exist. Automatic creation may be supported by the underlying API
but is not enabled in the initial product flow because the administrator is explicitly registering
a designated page URI.

### 10.3 Update comment

Where supported, associate a concise comment with the wiki change, for example:

```text
CloudLens Service Health projection update
```

Do not include event payloads, tracking details, credentials, or internal correlation identifiers
in the commit comment.

---

## 11. Authentication and Authorization

### 11.1 Pilot option: PAT

The PAT option supports environments where tenant boundaries or customer policy prevent the
CloudLens deployment identity from being added to the target Azure DevOps organization. The project
owner's pilot deployment will be validated through this route because the test Azure DevOps
organization is connected to a different Microsoft Entra tenant.

The PAT must follow these controls:

- Organization-scoped rather than global.
- Minimum scope required to read and update wiki pages: Azure DevOps `vso.wiki_write`.
- Minimum Azure DevOps access level required by the organization.
- Created for a controlled automation/service identity where customer policy permits, not an
  individual's informal long-lived token.
- Short expiration according to customer policy.
- Rotated before expiration.
- Stored only in Azure Key Vault.
- Redacted from telemetry, exceptions, traces, Cosmos DB, Service Bus, and API responses.
- Sent only to the validated `https://dev.azure.com/{organization}` endpoint.

PAT requests use Azure DevOps Basic authentication over TLS. The authorization value is built only
in memory for the outbound request and is never persisted.

The UI cannot reliably discover PAT expiry from the secret value. The administrator should enter an
optional expiry date during registration or replacement so CloudLens can warn before rotation. This
metadata is not secret.

### 11.2 PAT Key Vault lifecycle

1. API receives PAT over HTTPS.
2. API writes a new version of a target-specific secret in Key Vault.
3. API persists only a secret identifier/reference and non-secret expiry metadata.
4. Dispatcher retrieves the secret through its managed identity.
5. Credential replacement creates a new secret version.
6. The old version remains available only for the configured Key Vault recovery/rotation period.
7. Removing a target disables dispatch immediately and schedules deletion or expiry of the
   target-specific secret according to repository-standard secret lifecycle policy.

If Key Vault write fails, target registration fails. CloudLens must never fall back to Cosmos DB or
application configuration for PAT storage.

### 11.3 Pilot option: user-assigned managed identity

Managed identity is a full pilot authentication option and the preferred customer deployment model.
It accepts a user-assigned managed identity client ID. Supplying a client ID is not sufficient by
itself. Activation requires all of the following:

1. The identity exists in the Microsoft Entra tenant connected to the Azure DevOps organization, or
   an explicitly supported cross-tenant design is configured.
2. The identity is attached to the CloudLens dispatch worker App Service.
3. The identity is explicitly added to the Azure DevOps organization.
4. Azure DevOps project/wiki permissions grant only the required page read/write capability.
5. The identity has the Azure DevOps access/license level required by the organization.
6. CloudLens can acquire an Azure DevOps token for
   `499b84ac-1321-427f-aa17-267ca6975798/.default`.

The dispatcher will use `ManagedIdentityCredential(clientId)` or the repository-standard equivalent
to acquire short-lived Microsoft Entra tokens. Azure DevOps permissions remain managed in Azure
DevOps; Microsoft Entra application permissions alone do not authorize wiki access.

The UI must show these prerequisites and report which validation failed. CloudLens must not attempt
to create, attach, license, or grant Azure DevOps permissions to a user-provided managed identity
automatically.

The managed identity implementation must be covered by automated credential-provider, token-scope,
configuration-validation, and Azure DevOps client tests during the pilot. Same-tenant end-to-end
validation should be completed in a customer or dedicated test tenant where the identity can be
attached to the worker and added to the Azure DevOps organization. The project owner's cross-tenant
test organization is not the acceptance environment for that specific end-to-end path.

### 11.4 Authentication strategy abstraction

Implement a target credential provider abstraction, conceptually:

```text
IAzureDevOpsCredentialProvider
  - PatAzureDevOpsCredentialProvider
  - ManagedIdentityAzureDevOpsCredentialProvider
```

The wiki client and renderer must not contain authentication-specific branching beyond receiving an
authorization credential/token from the provider.

---

## 12. Architecture

### 12.1 Target architecture

```text
Service Health Event Hub
        |
        v
Existing normalization, deduplication, correlation, and enrichment
        |
        v
Target-aware routing / projection refresh intent
        |
        v
Cosmos DB durable delivery intent
        |
        v
Service Bus topic
        |
        +------------------------+
        |                        |
        v                        v
Existing Teams worker      Azure DevOps Wiki worker
        |                        |
        v                        +--> Key Vault PAT
CloudLens Teams app             |
                                 +--> Managed identity token
                                 |
                                 v
                         Azure DevOps Wiki REST API
                                 |
                                 v
                         Same designated wiki page
```

### 12.2 Preserve Teams implementation

Teams remains a destination-specific dispatcher. The enhancement must not route wiki work through
the Teams worker or add wiki fields to Teams conversation references.

### 12.3 Generic dispatch target

Evolve the current channel-centric concept into a dispatch-target concept while preserving backward
compatibility:

```text
ServiceHealthDispatchTarget
  id
  type: teams-bot | azure-devops-wiki
  displayName
  tenantId
  status
  includedRegions
  regionFilterVersion
  createdAt
  updatedAt
  configuration
  credentialReference
  health
```

Teams-specific fields remain available for `teams-bot`. Wiki-specific fields are present only for
`azure-devops-wiki`. Use strongly typed server models rather than an unvalidated arbitrary
dictionary at API boundaries.

Existing Teams records may remain in the current Cosmos collection during an incremental migration,
but routing and delivery code should use a common target identifier and type discriminator.

### 12.4 Wiki refresh intents

A wiki intent represents a request to reconcile one target to the latest canonical state:

| Field | Purpose |
|---|---|
| `id` | Deterministic intent identifier. |
| `targetId` | Azure DevOps Wiki target. |
| `targetType` | `azure-devops-wiki`. |
| `triggerEventId` | Optional event that caused the refresh. |
| `reason` | event-created, event-updated, resolved, target-activated, manual, or daily-reconcile. |
| `status` | pending, queued, dispatching, retry-scheduled, delivered, superseded, dead-lettered, or cancelled. |
| `notBefore` | Coalescing or retry time. |
| `attemptCount` | Delivery attempts. |
| `renderedContentHash` | Hash produced by the successful attempt. |
| `externalVersion` | Latest Azure DevOps page ETag after success. |
| `lastErrorCode` | Safe classified error. |
| `targetConfigurationVersion` | Target version that the worker must reload before projection. |

Multiple event changes can map to one target refresh. The latest successful full projection
supersedes older pending refreshes for that target.

### 12.5 Worker responsibilities

The Azure DevOps Wiki worker:

1. Claims serialized work for the target.
2. Loads and validates the active target.
3. Rejects processing if the target has no valid canonical regions or requires configuration.
4. Loads current canonical active and recently resolved event versions.
5. Resolves event lifecycle state and effective affected regions.
6. Filters events by intersection with the target's canonical included regions.
7. Renders deterministic Markdown from only the filtered event set.
8. Computes the rendered content hash.
9. Skips the write if the hash matches the last successful projection.
10. Retrieves PAT or managed identity token through the credential provider.
11. Reads the page and current ETag.
12. Updates the page with `If-Match`.
13. Persists success, new ETag, content hash, timestamp, matched event/region counts, and delivery
    audit.
14. Classifies failures for retry, operator action, or dead-letter.

---

## 13. Data Model

### 13.1 Azure DevOps Wiki target

```json
{
  "id": "target-id",
  "type": "azure-devops-wiki",
  "displayName": "Platform Service Health Wiki",
  "tenantId": "<deployment-tenant-id>",
  "organization": "contoso",
  "projectId": "<project-id-or-canonical-name>",
  "wikiIdentifier": "<wiki-id>",
  "pagePath": "/CloudLens-Service-Health-Hub",
  "browserUri": "https://dev.azure.com/...",
  "authenticationType": "pat",
  "credentialSecretReference": "<key-vault-secret-reference>",
  "managedIdentityClientId": null,
  "includedRegions": [
    "North Europe",
    "West Europe"
  ],
  "regionFilterVersion": 1,
  "credentialExpiresAt": "...",
  "status": "active",
  "lastValidatedAt": "...",
  "lastAttemptedAt": "...",
  "lastSucceededAt": "...",
  "lastRenderedContentHash": "<sha256>",
  "lastExternalVersion": "<etag>",
  "lastErrorCode": null,
  "lastErrorMessage": null,
  "createdAt": "...",
  "updatedAt": "..."
}
```

Rules:

- `credentialSecretReference` is populated only for PAT authentication.
- `managedIdentityClientId` is populated only for managed identity authentication.
- API responses return a boolean/masked credential state, not the secret reference.
- The model supports multiple future targets even while the pilot enforces one active target.
- `includedRegions` is required, contains 1-100 unique canonical Azure region names, and is stored
  in deterministic canonical-name order.
- `regionFilterVersion` increments whenever the region selection changes and participates in
  refresh coalescing and audit.
- Missing `includedRegions` on a legacy record maps to `configuration-required`, never to all
  regions.

### 13.2 Canonical Service Health region scope

The canonical event model adds:

```json
{
  "affectedRegions": [
    "North Europe",
    "West Europe"
  ],
  "unresolvedRegionValues": [],
  "regionResolutionVersion": 1
}
```

Rules:

- `affectedRegions` contains unique canonical region names in deterministic order.
- `unresolvedRegionValues` is bounded, sanitized diagnostic metadata. It drives the conservative
  all-target fallback only when no canonical region resolves; otherwise it does not override normal
  intersection matching.
- `regionResolutionVersion` enables safe reprocessing if the explicit alias catalog evolves.
- The current singular `region` value may remain during compatibility migration but is not an
  authoritative routing field.

### 13.3 Delivery audit

Each attempt records:

- Target and intent ID.
- Trigger reason.
- Attempt number.
- Started/completed timestamps.
- Result classification and HTTP status.
- Previous and resulting ETag where safe.
- Rendered content hash.
- Active and resolved event counts.
- Target region-filter version, configured-region count, matched-event count, and matched canonical
  regions.
- Count of events included by direct region match, global fallback, missing-region fallback, and
  unknown-only fallback, plus events excluded for non-intersecting known regions.
- Retry-after value when provided.
- Redacted error.
- Correlation ID.

The audit record must not contain the PAT, authorization header, access token, Key Vault secret
value, or complete raw page body.

---

## 14. API Requirements

Recommended endpoints:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/service-health/dispatch-targets` | List safe target summaries. |
| `POST` | `/api/service-health/dispatch-targets/azure-devops-wiki` | Register a wiki target and credential. |
| `PUT` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}` | Update non-secret configuration. |
| `PUT` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}/credential` | Replace PAT or configure managed identity. |
| `POST` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}/test` | Validate auth, read, ETag, and write capability. |
| `POST` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}/publish` | Queue immediate full projection. |
| `POST` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}/enable` | Enable after successful validation. |
| `POST` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}/disable` | Stop new automatic refreshes. |
| `DELETE` | `/api/service-health/dispatch-targets/azure-devops-wiki/{id}` | Disable and remove target metadata and retire its secret. |

Existing Teams channel endpoints remain supported. A later cleanup may expose Teams through the
generic dispatch-target route, but that is not required to deliver this feature.

Wiki create and update requests include `includedRegions`. The server validates every value through
the shared region resolver, rejects the entire request if any value is unknown, canonicalizes and
deduplicates the set, and returns the canonical stored values. Updates use the existing target ETag
or an explicit configuration version to prevent two administrators from silently overwriting each
other's region selection. A successful region update increments `regionFilterVersion` and queues a
full refresh.

`GET /api/azure-regions` remains the UI catalog endpoint and returns canonical display names. It may
later return structured metadata, but target requests must depend only on stable canonical names.

The synthetic-event request is extended with required `regions`:

```json
{
  "subscriptionId": "<subscription-id>",
  "eventType": "PlannedMaintenance",
  "regions": [
    "Japan East"
  ]
}
```

Synthetic regions use the same validation and canonicalization as target configuration. At least
one and at most 20 regions are required. The publisher emits realistic Service Health source
shapes, and the event must still traverse Event Hub, normalization, matching, persistence, intent
creation, dispatch, and projection; the API must not directly inject a delivery intent.

Validation responses use safe product error codes, including:

- `invalid-wiki-uri`
- `unsupported-host`
- `authentication-failed`
- `credential-expired`
- `identity-not-attached`
- `identity-not-in-organization`
- `permission-required`
- `page-not-found`
- `etag-conflict`
- `rate-limited`
- `azure-devops-unavailable`
- `rendered-page-too-large`
- `invalid-region`
- `region-selection-required`
- `target-configuration-conflict`

---

## 15. Functional Requirements

### FR-1 Navigation

- Add the fourth top-level tab.
- Move existing Teams routing to Dispatching targets > Teams channels.
- Preserve Teams event-family configuration and messages.

### FR-2 Wiki registration

- Accept a display name, Azure DevOps Wiki page URI, authentication type, credential input, and one
  or more canonical Azure regions.
- Parse canonical Azure DevOps identifiers from the browser URI.
- Require the page to exist.
- Validate read and write permission before activation.
- Support multiple active wiki targets and reject duplicate page registrations.
- Support editing regions after registration with optimistic concurrency and a full refresh.
- Put legacy targets with no configured regions into `configuration-required`.

### FR-3 Secret handling

- Store PATs only in Key Vault.
- Return only masked credential state.
- Support safe credential replacement.
- Warn based on configured expiry metadata.
- Never log secrets.

### FR-4 Rendering

- Render all four event families.
- Rebuild the complete page from canonical state.
- Resolve event lifecycle state and filter by the target's included regions before computing counts,
  recommendations, page size, or content hash.
- Preserve the customer-provided page structure and intent.
- Include deterministic status counts and overall recommendation.
- Include active and recently resolved sections.
- Sanitize all external values.
- Use explicit empty states.
- Keep source facts and AI content distinguishable.
- Use the existing LLM capability for bounded narrative enrichment where it improves the dashboard,
  with schema validation and deterministic fallback.

### FR-5 Delivery

- Queue on material event changes and target activation.
- Coalesce burst updates.
- Serialize writes per target.
- Use ETag optimistic concurrency.
- Skip unchanged content.
- Retry transient failures.
- Dead-letter exhausted or non-retryable deliveries with operator visibility.

### FR-6 Reconciliation

- Run at least daily.
- Detect a stale target whose last successful projection is older than the allowed threshold.
- Rebuild from current canonical state.
- Avoid a write when the rendered content is unchanged.

### FR-7 Lifecycle

- Move resolved/completed events into recently resolved history.
- Remove events from the wiki after the configured page-history window without deleting them from
  CloudLens storage.
- Re-activate events if Microsoft reopens them.

### FR-8 Administration

- Test connection.
- Publish now.
- View and edit the selected Azure regions.
- Enable/disable.
- Rotate credential.
- Remove target.
- Display safe delivery health and errors.

### FR-9 Managed identity

- Keep authentication behind a credential-provider abstraction.
- Persist authentication type and optional client ID.
- Implement managed identity credential acquisition and validation in the pilot.
- Do not make page rendering or delivery intent formats authentication-specific.
- Document attachment and Azure DevOps permission prerequisites in the UI.
- Allow a target to use managed identity from initial registration or migrate from PAT without
  changing its page URI or target ID.

### FR-10 Region matching and synthetic validation

- Maintain one shared canonical region resolver for API validation, event normalization, routing,
  projection, and tests.
- Store target and event regions as canonical names; use exact normalized-key or explicit-alias
  identity only.
- Match when any canonical event region intersects any target region.
- Match every active wiki target when the event is explicitly global, has missing/empty region
  data, or has non-empty region values from which no canonical region can be resolved.
- Exclude only events with one or more resolved canonical regions and no intersection with the
  target.
- Preserve the newest non-empty effective region scope across event lifecycle updates that omit
  region data.
- Keep Teams routing behavior unchanged.
- Let administrators select 1-20 canonical regions when publishing a synthetic event.
- Exercise both matching and non-matching synthetic events through the real Event Hub ingestion and
  wiki projection path.
- Record safe routing diagnostics without placing full raw event payloads in delivery intents.

---

## 16. Non-Functional Requirements

### Reliability

- At-least-once refresh triggering with idempotent full-page writes.
- No concurrent writes to one target.
- No permanent staleness after a transient event-driven delivery failure when daily reconciliation
  succeeds.
- Dispatcher restart must not lose pending refresh work.

### Performance

| Measure | Target |
|---|---|
| Material event persistence to successful wiki update, p95 | <= 5 minutes |
| Manual Publish now to worker start, p95 | <= 30 seconds |
| Daily reconciliation coverage | Every active target within 24 hours |
| Duplicate identical wiki writes | 0 |
| Region-matching evaluation per event/target | In-memory set intersection; no per-target database query |
| Supported selected regions per wiki target | 1-100 |

### Security

- HTTPS only.
- Host allowlisting and canonical URI construction to prevent SSRF.
- Key Vault for PATs.
- Managed identity for Key Vault, Cosmos DB, and Service Bus access.
- Least-privilege Azure DevOps scope and resource permissions.
- No secret-shaped values in structured logs.
- API authorization restricted to CloudLens platform administrators.
- Security Advisory rendering follows the existing data-classification and AI restrictions.
- Region configuration changes use optimistic concurrency and are included in the administrative
  audit trail.

### Observability

Metrics:

- Wiki refresh requested, coalesced, started, skipped, succeeded, retried, and dead-lettered.
- Render duration and page size.
- Active event counts by family.
- Routing candidate targets, region-matched targets, and region-filtered targets by event family.
- Unknown/unresolved source region values by normalized hash or bounded safe label, never raw
  payload.
- Projection events included/excluded by reason: `direct-match`, `global-fallback`,
  `missing-region-fallback`, `unknown-region-fallback`, or `no-intersection`.
- Region-filter configuration changes and legacy targets awaiting configuration.
- Azure DevOps response class.
- Credential expiry warning count.
- Time since last successful projection.

Alerts:

- Active target has no successful update within the stale threshold.
- PAT is approaching configured expiry.
- Authentication or permission failure.
- Repeated ETag conflict.
- Repeated HTTP 429/5xx.
- Dead-lettered wiki intent.
- Page exceeds supported size.
- A registered wiki target has missing or invalid region configuration.
- Unknown Service Health region values exceed a configurable rate, indicating source-schema or
  catalog drift.

---

## 17. Failure Handling

| Failure | Behavior |
|---|---|
| PAT invalid or expired | Do not retry continuously; mark `authentication-failed`, show rotation action, and retain events for later projection. |
| Missing wiki permission | Mark `permission-required`; provide required Azure DevOps scope/resource guidance. |
| Page deleted or moved | Mark degraded/page-not-found; never create a different page silently. |
| HTTP 412 ETag conflict | Reread ETag and retry with bounded attempts; then require operator review if conflicts continue. |
| HTTP 429 | Honor `Retry-After` and reschedule. |
| Azure DevOps 5xx/network timeout | Exponential backoff with jitter, then dead-letter after policy exhaustion. |
| Key Vault unavailable | Retry as infrastructure failure; do not use a cached long-lived secret beyond approved in-memory lifetime. |
| Rendering failure | Dead-letter with a safe error; do not publish a partial page. |
| Page too large | Fail visibly and report counts/size; do not silently omit active critical events. |
| Worker outage | Pending intents remain durable; daily reconciliation repairs state after recovery. |
| Manual page edit | ETag detects conflict; CloudLens retries only according to ownership policy and surfaces repeated conflict. |
| Managed identity client ID not attached | Mark `identity-not-attached`, explain the App Service attachment prerequisite, and keep the target disabled. |
| Target has no valid selected regions | Mark `configuration-required`; do not queue or publish until corrected. |
| Target update contains an unknown region | Reject the complete update with `invalid-region`; retain the prior valid configuration. |
| Event has no region, only unknown/unparseable region values, or a global marker | Persist for audit, route to every active wiki target, include it in every projection, and emit the applicable fallback telemetry reason. |
| Region alias catalog changes | Version the resolver, add collision tests, and reprocess canonical region fields before enabling routing under the new version. |
| Concurrent region edits | Reject stale configuration version/ETag with `target-configuration-conflict`; never merge silently. |

---

## 18. Delivery Phases

### Phase 1 - UI and target model

- Add Dispatching targets top-level tab.
- Move existing Teams routing UI unchanged.
- Add Azure DevOps Wiki registration and health UI.
- Add required region multi-select and edit-regions workflow.
- Add generic target and wiki target models.
- Add shared Azure region resolver and canonical region API contract.
- Add safe API contracts and URI parser.

**Exit:** an administrator can save a disabled wiki target with a validated non-empty canonical
region set without exposing the PAT.

### Phase 2 - Wiki projection and both authentication providers

- Key Vault secret storage and rotation.
- PAT credential provider.
- Managed identity credential provider and Azure DevOps token acquisition.
- Managed identity attachment, organization-membership, and permission validation.
- Azure DevOps Wiki REST client.
- Canonical Markdown renderer.
- Optional LLM-assisted narrative fields with deterministic fallback.
- Target-scoped refresh intents and Service Bus subscription.
- Wiki worker, ETag handling, retries, and audit.
- Event-driven, manual, and daily refresh.
- Region-aware ingestion routing and authoritative projection-time filtering.
- Synthetic event region selection through the real ingestion path.

**Exit:** all four event families update the same designated page through either PAT or managed
identity authentication, subject to each environment's tenant and Azure DevOps authorization
prerequisites, and only when their effective affected regions intersect the target configuration.

### Phase 3 - Operational hardening

- Staleness alerts.
- Credential-expiry warnings.
- Page-size protection.
- Replay/dead-letter controls.
- Load and burst testing.
- Security and failure-mode validation.
- PAT end-to-end validation against the project owner's cross-tenant Azure DevOps test
  organization.
- Managed identity automated integration coverage and same-tenant end-to-end validation in a
  suitable customer or dedicated test environment.
- Region resolver alias/collision coverage, lifecycle-scope regression tests, and high-target-count
  routing/load tests.

**Exit:** both authentication options meet pilot functional acceptance, with PAT proven in the
project owner's deployed test path and managed identity proven in a same-tenant authorized
environment.

### 18.1 Region-filter validation strategy

Automated unit and integration coverage must include:

| Scenario | Target regions | Event regions | Expected wiki result |
|---|---|---|---|
| Exact canonical match | `West Europe` | `West Europe` | Included |
| ARM alias match | `West Europe` | `westeurope` | Included as `West Europe` |
| Case/punctuation normalization | `West Europe` | `WEST-EUROPE` through an approved alias | Included |
| Similar-name protection | `Central US` | `North Central US` | Excluded |
| Generation protection | `East US` | `East US 2` | Excluded |
| Multi-region intersection | `North Europe` | `Japan East`, `North Europe` | Included |
| No intersection | `West Europe` | `Japan East` | Excluded |
| Missing event region | `West Europe` | empty | Included by missing-region fallback |
| Unknown-only event region | `West Europe` | `Moon Base 1` | Included by unknown-region fallback and observed |
| Known non-match plus unknown value | `West Europe` | `Japan East`, `Moon Base 1` | Excluded because a canonical region resolved and did not intersect |
| Global marker | `West Europe` | `Global` | Included by global fallback |
| Resolution omits region | `West Europe` | active=`West Europe`, resolved=empty | Resolution remains in scope and removes the active event |
| Scope changes | `West Europe` | old=`West Europe`, new=`Japan East` | Removed on next projection |
| Region edit adds scope | old target=`West Europe`, new target adds=`Japan East` | `Japan East` | Appears after target refresh without re-ingestion |
| Region edit removes scope | old target includes=`Japan East`, new target removes it | `Japan East` | Disappears after target refresh |
| Teams independence | wiki=`West Europe`; Teams subscribed | `Japan East` | Wiki excluded; Teams unchanged |

The Service Health UI's synthetic-event controls add a searchable canonical region multi-select.
For manual end-to-end validation, an administrator can:

1. Configure a wiki target for `West Europe`.
2. Publish one synthetic event for `West Europe` and verify that a wiki refresh intent is created
   and the event appears.
3. Publish the same event family for `Japan East` and verify that no wiki intent is created for
   that target and the page remains unchanged.
4. Publish a multi-region event containing `Japan East` and `West Europe` and verify that it
   appears once.
5. Repeat for all four event families.
6. Resolve a matching synthetic event with a payload that omits region and verify that lifecycle
   scope retention removes it from the active section correctly.

Tests must assert persisted `matchingChannelIds`, delivery intents, worker filtering, rendered page
content, rendered hash behavior, and routing telemetry. Direct renderer-only tests are insufficient.

---

## 19. Acceptance Criteria

1. Service Health shows four top-level tabs, including **Dispatching targets**.
2. Dispatching targets contains **Teams channels** and **Azure DevOps Wiki** secondary tabs.
3. The Teams channels view retains the current discovered-channel and four-checkbox behavior.
4. An administrator can register one existing Azure DevOps Wiki page using its browser URI and
   either a PAT or user-assigned managed identity client ID, and must select at least one canonical
   Azure region.
5. The PAT route works against the project owner's deployed cross-tenant Azure DevOps test
   organization.
6. The PAT is stored in Key Vault and is absent from Cosmos DB, Service Bus, logs, traces, API
   responses, and UI state after submission.
7. The managed identity provider acquires an Azure DevOps Microsoft Entra token for the required
   resource and works in a same-tenant environment where the identity is attached and authorized.
8. A target cannot become active until CloudLens validates authentication, page existence, read
   access, and write access.
9. CloudLens updates the configured page rather than creating a new page or changing its URI.
10. The page preserves the customer-provided structure and contains current counts and sections for
    Service Issues, Planned Maintenance, Health
   Advisories, and Security Advisories.
11. Approved LLM-assisted narrative can enrich the dashboard, but LLM failure still produces a
    complete deterministic page.
12. A new material event update is reflected on the wiki within five minutes at p95 under normal
   conditions.
13. A resolved or completed event leaves its active section and appears in Recently Resolved.
14. Duplicate or replayed event records do not create duplicate event blocks or identical wiki
    writes.
15. Bursts of event updates are coalesced and writes for the same page are serialized.
16. Existing-page updates use ETag/`If-Match`; concurrent edits are never silently overwritten.
17. A daily reconciliation repairs a wiki target that missed an earlier event-driven update.
18. An unchanged rendered content hash does not create an unnecessary wiki write.
19. Authentication, permission, page-not-found, throttling, and concurrency failures are
    distinguishable and actionable in the UI.
20. Transient Azure DevOps failures retry without blocking Teams delivery.
21. Wiki failures do not modify, pause, or delete existing Teams destinations.
22. The rendered page never includes secrets, raw payloads, or unapproved internal identifiers.
23. A target can migrate between PAT and managed identity without a renderer, target ID, event
    history, or page-URI migration.
24. An administrator can edit a target's Azure regions later; the update uses optimistic
    concurrency and queues a full refresh.
25. Events from all four supported families with resolved canonical regions are included only when
    at least one affected region intersects the target's configured region set.
26. Missing or empty region data, explicit global scope, and unknown/unparseable-only region data
    use a conservative fallback that creates refresh intents for and includes the event on every
    active wiki target.
27. Similar names such as `Central US`/`North Central US` and `East US`/`East US 2` never
    cross-match.
28. A multi-region event is included once when any event region matches.
29. A lifecycle update that omits region retains the newest previously known non-empty region scope,
    allowing resolved/completed events to leave the active wiki section correctly.
30. Event-driven routing and projection-time rendering both enforce the same shared canonical
    matcher.
31. Existing Teams routing remains unchanged by wiki region filters.
32. Existing wiki targets without region configuration become `configuration-required` and cannot
    publish until an administrator selects valid regions.
33. Synthetic matching and non-matching events can be published with selected regions through the
    normal Event Hub path, with observable intents and projection results.
34. Projection considers every active and recently resolved event in the configured history window
    through pagination; it is not capped to a fixed latest-event count.

---

## 20. Success Metrics

| Metric | Pilot target |
|---|---|
| Successful wiki projection updates | >= 99.5% excluding confirmed Azure DevOps outage or invalid customer credential |
| Wiki projection staleness | < 5 minutes p95 after a material event update |
| Targets stale for more than 24 hours without an alert | 0 |
| Duplicate identical page updates | 0 |
| PAT exposure in logs/storage outside Key Vault | 0 |
| Teams delivery regression attributable to wiki feature | 0 |
| False-positive wiki inclusion from non-matching region | 0 |
| False-negative wiki exclusion for canonical/approved alias match | 0 |
| Active wiki targets with empty or invalid region configuration | 0 |
| Platform administrator rating of setup experience | >= 4/5 |

---

## 21. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| PAT is tied to a person and expires unexpectedly | Recommend controlled automation identity, capture expiry metadata, alert before expiry, support rotation, and prefer the pilot's managed identity option for customers. |
| User provides a malicious or incorrect URI | Strict HTTPS host allowlist, canonical identifier parsing, no arbitrary outbound URL calls, and redirect validation. |
| Full-page ownership overwrites manual edits | Warn clearly, validate ETag, designate the page as CloudLens-managed, and defer custom regions/templates. |
| Many event updates create excessive wiki commits | Coalesce by target and skip unchanged rendered hashes. |
| Wiki page becomes too large | Bound field lengths, monitor size, retain all active critical events, and fail visibly before silent truncation. |
| PAT has excessive scope | Require/document `vso.wiki_write`, organization-scoped tokens, and least privilege; warn against full-scoped/global PATs. |
| Managed identity is assumed to work from client ID alone | Validate App Service attachment, tenant, Azure DevOps organization membership, license/access, and resource permissions. |
| Azure DevOps and CloudLens are in different tenants | Use PAT for the project owner's pilot validation; do not misrepresent managed identity as cross-tenant capable without an explicit supported onboarding design. |
| Concurrent human edit conflicts with CloudLens | Use ETag/`If-Match`, bounded reread/retry, and operator-visible degraded state. |
| Wiki dispatch failure blocks urgent Teams notifications | Separate Service Bus subscription/worker and destination-specific retry circuit. |
| AI-generated business impact is mistaken for Microsoft guidance | Respect the customer-provided template, label CloudLens analysis, preserve Microsoft facts, validate structured output, and use deterministic status rules and fallback. |
| Fuzzy matching routes an event to the wrong geography | Use exact canonical identities and explicit aliases only; prohibit substring and LLM-based region inference. |
| Service Health uses a new or unexpected region value | Exclude it from wiki routing, retain bounded diagnostics, alert on catalog drift, and update the explicit resolver through a reviewed change. |
| Resolution updates omit their original region | Resolve lifecycle versions before filtering and carry forward the newest prior non-empty canonical region set. |
| Event-driven matching and daily projection diverge | Use the same shared resolver/matcher, with projection-time filtering as the authoritative safety boundary. |
| Conservative fallback creates noise for an unscoped event | Prefer visibility over silent loss, label the event region as global/not specified, and emit fallback telemetry so source quality can be improved. |
| Large target counts make per-event routing expensive | Maintain a tenant-scoped immutable region-to-target index and use idempotent intent creation. |
| Fixed event query limits omit older active incidents | Use paginated tenant/time/status queries covering the complete active and recent-history windows. |
| Legacy targets continue receiving all regions silently | Migrate them to `configuration-required` and block publication until regions are selected. |

---

## 22. Open Decisions Before Implementation

1. Which Azure Key Vault will hold customer-provided Azure DevOps PATs, and which runtime identity
   receives secret get/set permissions?
2. What PAT expiry warning thresholds should the UI use, for example 30, 14, and 7 days?
3. What exact UTC time should the daily reconciliation run?
4. What maximum rendered page size should CloudLens enforce before calling Azure DevOps?
5. Should approved subscription display names be shown on the wiki, or only aggregate subscription
   counts?
6. What source status values map to resolved/closed/completed for each of the four event families?
7. Should the static Contact & Support text be deployment configuration in the first release or use
   the fixed Platform Operations wording?
8. Which CloudLens dispatch App Service will own the pilot user-assigned managed identity, and how
   will administrators request that identity attachment?
9. Which customer or dedicated same-tenant environment will provide managed identity end-to-end
    pilot validation?

---

## 23. Microsoft References

- [Azure DevOps Wiki Pages - Create or Update REST API 7.1](https://learn.microsoft.com/rest/api/azure/devops/wiki/pages/create-or-update?view=azure-devops-rest-7.1)
- [Use service principals and managed identities in Azure DevOps](https://learn.microsoft.com/azure/devops/integrate/get-started/authentication/service-principal-managed-identity?view=azure-devops)
- [Use personal access tokens](https://learn.microsoft.com/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops)
