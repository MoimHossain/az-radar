# PRD - Azure DevOps Wiki Dispatch for Service Health

> **Status:** Proposed
> **Author:** @MoimHossain (drafted with Copilot)
> **Date:** 2026-09-18
> **Feature area:** Service Health dispatching targets
> **Product name:** CloudLens
> **Related:** `prds/service-health-dispatch.md`,
> `prds/service-health-identity-and-auth-flow.md`

---

## 1. Executive Summary

Extend CloudLens Service Health dispatching with an Azure DevOps Wiki target while preserving the
existing Microsoft Teams channel experience.

A CloudLens platform administrator will be able to register multiple designated Azure DevOps Wiki
pages.
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

After registration, show:

- Display name.
- Organization, project, wiki, and page path parsed from the URI.
- Authentication type.
- Credential state, such as `configured`, `expiring`, `invalid`, or `rotation required`.
- Target state: `draft`, `validating`, `active`, `degraded`, `disabled`, or `permission-required`.
- Last successful update.
- Last attempted update.
- Last rendered content hash.
- Last error with a safe, actionable message.
- **Test connection**, **Publish now**, **Replace credential**, **Disable/Enable**, and **Remove**
  actions.

The raw PAT, Key Vault secret URI, access token, and authorization header must never be displayed.

### 6.5 Registration flow

1. Administrator enters target details and a PAT.
2. API validates the URI and accepts the PAT over HTTPS.
3. API stores the PAT in Key Vault and persists only the resulting secret reference.
4. API parses and resolves the organization, project, wiki identifier, and page path.
5. API reads the page and captures its current ETag.
6. API performs a safe write test only after explicit **Test connection** or **Save and activate**:
   it publishes the CloudLens page content, not arbitrary test text.
7. Target becomes active only after authentication, permission, read, and write checks pass.
8. The page URI remains the configured browser URI.

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
wiki. Tenant-isolation requirements from the existing Service Health design continue to apply.

### 7.3 Update triggers

Create or coalesce a wiki refresh request when:

- A new supported event is persisted.
- A material update changes status, title, summary, service, region, timing, impact, or recommended
  actions.
- An event becomes resolved, closed, or completed.
- A previously resolved event becomes active again.
- An administrator activates or changes the wiki target.
- An administrator selects **Publish now**.
- The daily reconciliation schedule runs.

The target is a complete projection. A refresh request does not contain the final page body; the
worker reads the latest canonical event state immediately before rendering.

### 7.4 Update frequency

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

### 7.5 Ordering and consistency

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

### 7.6 Event lifecycle and retention in the page

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

### 7.7 Empty states

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

Multiple event changes can map to one target refresh. The latest successful full projection
supersedes older pending refreshes for that target.

### 12.5 Worker responsibilities

The Azure DevOps Wiki worker:

1. Claims serialized work for the target.
2. Loads and validates the active target.
3. Loads current canonical active and recently resolved events.
4. Renders deterministic Markdown.
5. Computes the rendered content hash.
6. Skips the write if the hash matches the last successful projection.
7. Retrieves PAT or managed identity token through the credential provider.
8. Reads the page and current ETag.
9. Updates the page with `If-Match`.
10. Persists success, new ETag, content hash, timestamp, and delivery audit.
11. Classifies failures for retry, operator action, or dead-letter.

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

### 13.2 Delivery audit

Each attempt records:

- Target and intent ID.
- Trigger reason.
- Attempt number.
- Started/completed timestamps.
- Result classification and HTTP status.
- Previous and resulting ETag where safe.
- Rendered content hash.
- Active and resolved event counts.
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

---

## 15. Functional Requirements

### FR-1 Navigation

- Add the fourth top-level tab.
- Move existing Teams routing to Dispatching targets > Teams channels.
- Preserve Teams event-family configuration and messages.

### FR-2 Wiki registration

- Accept a display name, Azure DevOps Wiki page URI, authentication type, and credential input.
- Parse canonical Azure DevOps identifiers from the browser URI.
- Require the page to exist.
- Validate read and write permission before activation.
- Support multiple active wiki targets and reject duplicate page registrations.

### FR-3 Secret handling

- Store PATs only in Key Vault.
- Return only masked credential state.
- Support safe credential replacement.
- Warn based on configured expiry metadata.
- Never log secrets.

### FR-4 Rendering

- Render all four event families.
- Rebuild the complete page from canonical state.
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

### Security

- HTTPS only.
- Host allowlisting and canonical URI construction to prevent SSRF.
- Key Vault for PATs.
- Managed identity for Key Vault, Cosmos DB, and Service Bus access.
- Least-privilege Azure DevOps scope and resource permissions.
- No secret-shaped values in structured logs.
- API authorization restricted to CloudLens platform administrators.
- Security Advisory rendering follows the existing data-classification and AI restrictions.

### Observability

Metrics:

- Wiki refresh requested, coalesced, started, skipped, succeeded, retried, and dead-lettered.
- Render duration and page size.
- Active event counts by family.
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

---

## 18. Delivery Phases

### Phase 1 - UI and target model

- Add Dispatching targets top-level tab.
- Move existing Teams routing UI unchanged.
- Add Azure DevOps Wiki registration and health UI.
- Add generic target and wiki target models.
- Add safe API contracts and URI parser.

**Exit:** an administrator can save a disabled wiki target without exposing the PAT.

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

**Exit:** all four event families update the same designated page through either PAT or managed
identity authentication, subject to each environment's tenant and Azure DevOps authorization
prerequisites.

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

**Exit:** both authentication options meet pilot functional acceptance, with PAT proven in the
project owner's deployed test path and managed identity proven in a same-tenant authorized
environment.

---

## 19. Acceptance Criteria

1. Service Health shows four top-level tabs, including **Dispatching targets**.
2. Dispatching targets contains **Teams channels** and **Azure DevOps Wiki** secondary tabs.
3. The Teams channels view retains the current discovered-channel and four-checkbox behavior.
4. An administrator can register one existing Azure DevOps Wiki page using its browser URI and
   either a PAT or user-assigned managed identity client ID.
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
