# PRD — Registered-Subscription Azure Service Health Intelligence and Teams Dispatch

> **Status:** Proposed
> **Author:** @MoimHossain (drafted with Copilot)
> **Date:** 2026-09-10
> **Feature area:** Service Health ingestion, intelligence, and notification routing
> **Customer context:** Regulated-industry platform engineering; pilot on explicitly registered
> subscriptions with a future path to approximately 5,000 subscriptions
> **Related:** `prds/PlanV2.md` (Smart Notification Router), existing Blast Radius capability

---

## 1. Executive Summary

Extend AzRadar into a centrally operated Azure Service Health distribution service that:

1. Lets an administrator explicitly register one or more pilot subscription IDs in the UI.
2. Configures each registered subscription to stream Service Health Activity Log records to a
   central Event Hub.
3. Requires no alert-rule expertise or ongoing configuration from workload teams.
4. Does not expose a new public inbound HTTP endpoint.
5. Correlates duplicate subscription-level notifications into a central incident view.
6. Uses deterministic policy plus Azure OpenAI enrichment to decide how an event should be
   summarized and routed.
7. Lets an administrator configure platform-team Microsoft Teams channels and select the Service
   Health event types each channel receives.
8. Preserves an auditable history independent of Azure Service Health portal retention.

The recommended architecture is:

```text
Explicitly registered Azure subscriptions (pilot subset)
  Subscription Activity Log
    category = ServiceHealth
             |
             | centrally managed diagnostic settings
             v
Azure Event Hubs (private, central ingestion)
             |
             v
Service Health Ingress Worker
  validate -> normalize -> deduplicate -> correlate -> persist
             |
             v
Cosmos DB subscription registry + canonical event + routing ledger
             |
             +----> Azure Resource Graph reconciliation/enrichment
             |
             +----> deterministic policy -> Azure OpenAI enrichment
             |
             v
Transactional outbox / Cosmos change feed
             |
             v
Azure Service Bus topic (durable delivery work)
             |
             v
Teams dispatch worker
             |
             v
Azure Bot Service -> tenant-managed Teams notification app
```

This uses Event Hubs and Service Bus for different purposes:

- **Event Hubs** is the Azure Monitor-native, high-throughput ingestion boundary.
- **Service Bus** is the reliable work-dispatch boundary, providing controlled retries,
  dead-lettering, per-destination throttling, and delivery isolation.

Azure Resource Graph is retained as a critical reconciliation and impacted-resource source, but
not as the only ingestion mechanism. Polling alone is less suitable for urgent operational events
because it is not a durable push stream and can miss intermediate updates between polls.

---

## 2. Problem Statement

Azure automatically publishes personalized Service Health notifications into each affected
subscription's Activity Log. The data exists without customers creating alert rules, but the
operational response does not.

At regulated-enterprise scale, asking thousands of workload teams to create and maintain correct
Service Health alert rules would create:

- Long implementation lead time.
- Inconsistent subscription, service, region, and event-type filters.
- Alert gaps when teams misunderstand Azure Monitor.
- Duplicated action groups and unmanaged webhook secrets.
- No tenant-wide audit of which subscriptions are covered.
- Alert fatigue caused by inconsistent urgency and routing.
- Ongoing drift as subscriptions and teams are created, moved, or retired.

The platform team needs a centrally managed service that turns events from explicitly registered
subscriptions into a reliable platform-channel notification stream, with a future path to
tenant-wide ownership-aware routing.

### Important clarification

Creating a Service Health alert does not by itself create an indefinite Azure-hosted archive of
every notification delivery. A durable history exists only if a receiver stores the payload or
the Activity Log is exported to a durable destination. AzRadar must therefore persist raw event
versions and delivery outcomes explicitly.

---

## 3. Design Principles

1. **No workload-team setup.** The platform team owns collection, routing, and coverage.
2. **No public inbound AzRadar endpoint.** Azure events enter through Azure messaging services.
3. **Zero application secrets.** AzRadar uses managed identity for Event Hubs consumption,
   Service Bus, Cosmos DB, Azure OpenAI, Key Vault, Resource Graph, and supported outbound APIs.
4. **Urgency is deterministic.** AI may enrich and recommend; it must not silently suppress or
   downgrade critical, security, or active service-issue notifications.
5. **At-least-once delivery is assumed.** Every stage must be idempotent.
6. **One Azure incident, not one message per subscription.** Correlate subscription copies by tracking ID while
   retaining subscription-level impact.
7. **Platform-first routing.** In v1, administrators explicitly select which event families each
   central platform channel receives; workload ownership is a later phase.
8. **Updates and resolutions are first-class.** A resolution is not a new unrelated alert; it
   updates the existing incident thread and ledger.
9. **Replay is safe.** Reprocessing Event Hub or Cosmos data must not duplicate Teams posts.
10. **Private-by-default networking.** Consumers use private endpoints; Azure platform producers
    use explicitly approved trusted-service paths where a native service cannot use a private
    endpoint.

---

## 4. Goals and Non-Goals

### Goals

1. Let an administrator register and remove individual subscription IDs through the AzRadar UI.
2. Apply and verify the Service Health collection baseline for every registered subscription.
3. Receive the four subscription-scoped operational Service Health families carried by the
   Activity Log `ServiceHealth` category: Service Issues, Planned Maintenance, Health Advisories,
   and Security Advisories.
4. Persist raw, normalized, correlated, enriched, and delivered representations.
5. Route to one or more platform-team Teams channels using administrator-selected event types.
6. Keep the data and routing model extensible for future subscription-specific workload-team
   channels without implementing that self-service model in v1.
7. Provide AI-generated summaries and recommended actions with explainable confidence.
8. Provide a coverage dashboard, dead-letter view, routing preview, and delivery audit trail.
9. Keep the current AzRadar API/UI VNet-restricted; this feature must not require changing its
    public exposure.

### Non-Goals for v1

- Replacing Azure Status, Azure Service Health, or the Azure portal.
- Resource Health monitoring for every individual Azure resource.
- Automatic technical remediation of affected resources.
- Paging/on-call integration such as PagerDuty or ServiceNow.
- A custom Teams bot with interactive commands.
- Creating one Teams channel per subscription.
- Workload-team self-service channel registration or subscription-specific routing.
- Azure DevOps work item creation or dispatch.
- Billing updates, because Microsoft does not publish billing notifications into the subscription
  Activity Log.
- Tenant-level or global-only notifications that are not written to a registered subscription's
  Activity Log.
- Allowing an LLM to make the sole decision to discard an operational event.
- Cross-tenant collection outside the regulated enterprise's tenant.

---

## 5. Users and User Stories

### Personas

| Persona | Need |
|---|---|
| Platform operations engineer | Know that all registered subscriptions are covered and urgent events are routed correctly. |
| Platform team member | Receive selected classes of Service Health information in an established Teams channel. |
| Service owner | See aggregated impact across subscriptions using their Azure service. |
| Security operations | Receive security advisories through a restricted route without leaking sensitive payloads. |
| Product owner / auditor | Review event history, routing decisions, acknowledgements, and delivery failures. |

### User stories

1. *As a platform operator,* I can register a pilot subscription ID and see whether AzRadar
   successfully configured and verified its `ServiceHealth` diagnostic setting.
2. *As a platform team member,* I receive one Teams message for an Azure incident affecting
   multiple registered subscriptions, not one message per subscription.
3. *As an operator,* I see an existing message updated or threaded when Microsoft publishes a new
   incident update or resolution.
4. *As an administrator,* I can configure one channel for Service Issues only and another for
   Planned Maintenance, without sending Health Advisories to either channel.
5. *As a security operator,* sensitive security advisories are sent only to approved restricted
   channels and are not submitted to the LLM unless explicitly permitted.
6. *As an administrator,* I can verify why an event was routed to a channel: registered
   subscriptions, event type, status, and routing rule are visible.
7. *As an operator,* I can replay a failed delivery without creating a duplicate Teams post.

---

## 6. Architecture Decision

### 6.1 Recommended ingestion: Activity Log diagnostic settings to Event Hubs

Azure Service Health notifications are written to the affected subscription's Activity Log.
Azure Monitor diagnostic settings can stream the Activity Log to Event Hubs. Configure each
administrator-registered subscription to export only the `ServiceHealth` category to one central
Event Hub.

The diagnostic setting is subscription-scoped, but AzRadar manages its lifecycle after explicit
registration by an administrator. Workload teams do not create or maintain it.

#### Exact coverage of the `ServiceHealth` Activity Log category

The Activity Log category is a broad envelope, not one Service Health subtype. Streaming only
`ServiceHealth` excludes noisy Activity Log categories such as `Administrative`, `Policy`,
`ResourceHealth`, `Alert`, and `Autoscale`, while retaining the subscription-scoped operational
Service Health records required by this feature.

| Service Health portal family | Covered by subscription Activity Log `ServiceHealth`? | Typical normalized signal |
|---|---:|---|
| Service Issue / incident | Yes | `incidentType = Incident`; operation contains `/incident/` |
| Planned Maintenance | Yes | `incidentType = Maintenance`; operation contains `/maintenance/` |
| Health Advisory | Yes | Commonly `ActionRequired`, `Information`, or related advisory metadata |
| Security Advisory | Yes, subject to Azure access and sensitivity restrictions | `Security` and sensitivity metadata |
| Billing Update | **No** | Billing notifications are not shown in the subscription Activity Log |

Additional boundary: subscription Activity Log export does not guarantee tenant-level or global-only
notifications that are not tied to a subscription. Billing and tenant/global-only collection require
separate future ingestion paths and are explicitly outside v1.

#### Why this is preferred

- Native Azure Monitor destination; no custom public webhook.
- Push-based delivery rather than periodic discovery.
- Natural replay window through Event Hubs retention.
- Separates Azure event production from AzRadar availability.
- Supports private networking for AzRadar consumers.
- Captures event lifecycle changes, not just the latest polled state.
- Application-managed registration makes pilot coverage measurable without requiring a
  management-group rollout.

#### v1 subscription registration flow

1. An administrator enters a subscription ID in the AzRadar UI.
2. The API validates the subscription ID and checks the dedicated provisioning UAMI's access.
3. AzRadar creates or updates a deterministic subscription-scoped diagnostic setting that exports
   only `ServiceHealth` to the configured central Event Hub.
4. AzRadar reads the setting back and marks registration as `active`, `permission-required`,
   `configuration-failed`, or `disabled`.
5. A reconciliation job periodically verifies only the explicitly registered subscriptions and
   repairs configuration drift when authorized.
6. Removing a subscription from AzRadar disables monitoring and, after explicit confirmation,
   removes only the diagnostic setting owned by AzRadar.

Management-group policy assignment and automatic discovery of all tenant subscriptions are future
scale-out options, not part of v1.

#### Azure Monitor authorization constraint

Diagnostic settings use an Event Hubs authorization rule as part of their platform configuration.
This is an Azure Monitor service integration constraint, not an application-held secret. AzRadar
must not read or store the rule keys. Runtime consumption by AzRadar uses managed identity with
`Azure Event Hubs Data Receiver`.

Use a dedicated namespace and authorization rule for Azure Monitor ingestion only. Do not reuse it
for application producers or consumers.

#### Dedicated subscription-provisioning UAMI

Provision a separate user-assigned managed identity for subscription onboarding and reconciliation.
Its client ID is configured independently from the existing AzRadar runtime UAMI.

Proposed configuration:

```text
ServiceHealthProvisioning:ManagedIdentityClientId
ServiceHealthProvisioning:DiagnosticSettingName
ServiceHealthProvisioning:EventHubAuthorizationRuleId
ServiceHealthProvisioning:EventHubName
```

The provisioning identity requires, at minimum:

- Read access to validate each registered subscription.
- `Microsoft.Insights/diagnosticSettings/read`
- `Microsoft.Insights/diagnosticSettings/write`
- `Microsoft.Insights/diagnosticSettings/delete` if UI removal is allowed to remove the setting.
- Access to read the Event Hub destination metadata.
- `Microsoft.EventHub/namespaces/authorizationRules/listKeys/action` on the dedicated Event Hubs
  authorization rule because Azure Monitor diagnostic-setting configuration requires access to
  that rule.

Use a custom role rather than broad Contributor where customer governance permits. Scope the
diagnostic-setting permissions to each registered subscription and scope Event Hub permissions to
the dedicated central authorization rule.

The Bicep deployment must create:

- The dedicated provisioning UAMI.
- Its central-resource role assignments.
- App settings containing its client ID and the diagnostic-setting destination identifiers.

The UAMI cannot grant itself access to an external subscription. Before registration succeeds, a
customer administrator must assign the documented custom role to this identity on that subscription.
The UI must show the UAMI principal/client ID and provide a copyable Bicep/CLI onboarding command.
Registration fails clearly with `permission-required` when the assignment is absent; AzRadar must
not fall back to the broader runtime identity.

### 6.2 Event Hubs namespace

Recommended production posture:

- Event Hubs Standard for the initial pilot, with Premium as a supported production upgrade when
  regional availability and isolation requirements justify it.
- One region only in v1.
- Private endpoint for AzRadar consumers.
- Public network access disabled, with the Azure Monitor trusted-service bypass enabled for
  diagnostic settings.
- Minimum two consumer groups:
  - `azradar-live`
  - `azradar-replay` or `azradar-audit`
- Event retention sized for operational recovery; target seven days for production.
- Optional Event Hubs Capture to an immutable/retention-controlled storage account for forensic
  archive and reprocessing.

Multi-region failover and explicit zone-resilience requirements are deferred. Service Health volume
is expected to be modest, so Standard is sufficient for the pilot. The tier remains configurable
because some regions or subscriptions can restrict new Premium namespace creation.

### 6.3 Why not use Service Bus as the Azure Monitor ingestion endpoint?

Azure Monitor diagnostic settings natively stream Activity Log data to Event Hubs, not Service Bus.
Service Bus remains valuable after normalization, where messages represent reliable delivery work
rather than an event stream.

### 6.4 Why not use an alert-rule webhook or Logic App endpoint?

| Concern | Webhook/Logic App implication |
|---|---|
| Inbound exposure | A normal webhook target must be publicly reachable; this triggers regulated-enterprise WAF, firewall, reverse-proxy, certificate, and exception processes. |
| Scale | Alert rules and action groups still need centrally managed scope and drift handling. |
| Coupling | Azure Monitor delivery becomes coupled to endpoint availability and payload handling. |
| Replay | Webhook retries are bounded and do not provide a general replay log. |
| Payload processing | Logic Apps can transform payloads, but business correlation and ownership logic would be split outside AzRadar. |

An Action Group with an Event Hub action and managed identity is a technically valid alternative
when the organization specifically wants alert-rule filtering before ingestion. It is not the default because it
introduces alert-rule lifecycle and scope management without removing the central deployment need.

### 6.5 Why not use Azure Resource Graph polling alone?

Azure Resource Graph exposes `ServiceHealthResources` across all subscriptions the caller can
access, including event and impacted-resource records. It is excellent for:

- Initial backfill.
- Finding active events at startup.
- Recovering after an Event Hubs outage or retention breach.
- Enriching events with impacted resource IDs.
- Detecting subscription/event pairs missing from the streaming path.
- Verifying the collection baseline.

It is not the primary real-time source because:

- Polling introduces latency.
- The query returns current queryable state rather than a durable ordered event stream.
- Intermediate event updates can be lost between polling intervals.
- API throttling and result paging become part of the critical notification path.

Run a reconciliation job every 15 minutes for active events and a daily wider-window reconciliation.

---

## 7. End-to-End Processing Flow

### 7.1 Collection

1. An administrator registers a subscription ID in AzRadar.
2. The dedicated provisioning UAMI creates and verifies the subscription diagnostic setting.
3. Azure creates a Service Health record in each affected registered subscription's Activity Log.
4. The diagnostic setting streams the `ServiceHealth` category to the central Event Hub.
5. The Event Hub retains the original event until its configured retention expires.

### 7.2 Ingress and normalization

1. `ServiceHealthIngressWorker` reads from the `azradar-live` consumer group using the AzRadar UAMI.
2. It validates required envelope fields and enforces a maximum payload size.
3. It stores the raw payload or a lossless compressed representation.
4. It normalizes Azure's dynamic Service Health properties into a stable AzRadar model.
5. It identifies duplicates using the Azure event ID plus a content hash.
6. It upserts the subscription-event projection and the tenant-level correlated incident.
7. It checkpoints the Event Hub partition only after durable Cosmos persistence.
8. The App Service hosting the continuous ingress worker must have `Always On` enabled so the
   listener is not unloaded during periods without inbound HTTP traffic.

Malformed events go to a quarantine store with the source partition, offset, reason, and payload
hash. They must not block the partition indefinitely.

The primary ingress discriminator is the exported Activity Log category:

```text
category == "ServiceHealth"
```

The normalizer then derives the four supported event families from `operationName.value`,
`properties.incidentType`, event tags, and sensitivity metadata:

| Normalized family | Example source signals |
|---|---|
| `ServiceIssue` | `incidentType = Incident`, `Microsoft.ServiceHealth/incident/action` |
| `PlannedMaintenance` | `incidentType = Maintenance`, `Microsoft.ServiceHealth/maintenance/action` |
| `HealthAdvisory` | `incidentType = ActionRequired` or `Information`, advisory/retirement metadata |
| `SecurityAdvisory` | `incidentType = Security` or equivalent security and sensitivity metadata |

Unknown or newly introduced Service Health classifications are retained and routed to an
operator-visible review state rather than discarded.

### 7.3 Correlation

Azure Resource Graph can return one record per `subscriptionId + trackingId`, while the portal
groups by tracking ID. AzRadar follows both models:

- **Subscription impact key:** `tenantId + subscriptionId + trackingId`
- **Tenant incident key:** `tenantId + trackingId`
- **Raw version key:** Azure event ID when available; otherwise a hash of subscription, tracking ID,
  status, last update time, and canonicalized content

The central incident aggregates:

- Affected subscription count.
- Affected registered subscriptions.
- Affected services and regions.
- Impacted resources where available.
- Current Azure status.
- Earliest impact start.
- Latest Azure update.
- Resolution time.
- Number and history of source updates.

Correlation must never discard a subscription-specific detail. The tenant view is an aggregate over
durable subscription projections.

### 7.4 Ownership resolution (future phase)

Ownership-aware workload-team routing is not part of v1. All registered-subscription events are
visible centrally, and routing is based on event type to platform-team channels.

Future ownership resolution may use this priority order:

1. Approved enterprise CMDB/application registry mapping.
2. Explicit AzRadar subscription-to-team mapping.
3. Subscription tags from Azure Resource Graph.
4. Impacted-resource tags.
5. Management group or organizational-unit to platform-domain mapping.
6. Service/region fallback rule.
7. Default platform triage channel.

An ownership match includes:

- `ownerSource`
- `ownerId`
- `ownerDisplayName`
- `confidence`
- `matchedField`
- `resolvedAt`
- `mappingVersion`

Low-confidence or conflicting matches may add a team as an observer but must not remove the platform
fallback route.

### 7.5 Deterministic policy

Policy evaluates before any LLM call.

Minimum rules:

| Event | Default behavior |
|---|---|
| Active `ServiceIssue` with Critical/Error level | Immediate dispatch to every enabled platform channel subscribed to Service Issues. AI cannot suppress or downgrade. |
| `SecurityAdvisory` | Dispatch only to platform channels explicitly subscribed to Security Advisories; redact sensitive content by default; AI disabled unless explicitly allowed. |
| `PlannedMaintenance` | Dispatch to channels explicitly subscribed to Planned Maintenance; later versions may add lead-time and digest rules. |
| `HealthAdvisory` / Retirement | Dispatch to channels explicitly subscribed to Health Advisories. |
| Resolution/update | Update the existing incident delivery; do not create an unrelated notification. |
| Unknown event type | Platform triage route; never silently discard. |

### 7.6 AI enrichment

Azure OpenAI produces structured enrichment:

- Plain-language title and summary.
- What changed since the prior event version.
- Recommended operational action.
- Suggested audience and urgency.
- Estimated customer impact.
- Tags such as networking, compute, identity, data, or security.
- Confidence and evidence fields.

AI safeguards:

1. Send only an allowlisted subset of the event payload.
2. Treat Microsoft event text as untrusted input and protect prompts from instruction injection.
3. Require JSON schema output.
4. Store prompt-template version, model deployment, result, confidence, and execution status.
5. Use deterministic fallback wording if the model fails, times out, or returns invalid output.
6. Never allow AI to lower Azure's stated event level.
7. Never allow AI to remove a mandatory route.
8. Apply a short timeout and bounded retries; notification processing continues without AI.
9. Support an event-type denylist for payloads that are not approved for model processing.
10. Display AI-derived fields separately from Microsoft-authored facts.

### 7.7 Routing

Routing rules select audiences based on:

- Enabled platform-team channel.
- Event family selected for that channel: Service Issue, Planned Maintenance, Health Advisory,
  and/or Security Advisory.
- Optionally Azure event level and status.
- All explicitly registered subscriptions in v1.
- Azure service, region, subscription, ownership, and workload-team routing in a future phase.
- AI tags and confidence, but only as additive criteria.
- Time of day and digest preference.

Rules produce immutable delivery intents. Every intent references the event version, rule version,
channel configuration version, and rendered-template version that produced it.

### 7.8 Durable dispatch through Service Bus

Publish one message per delivery intent to a Service Bus topic.

Recommended logical subscriptions:

- `teams-realtime`
- `teams-digest`
- `audit-export`

Use:

- Managed identity for producers and consumers.
- Premium tier for production if the organization requires private endpoints, predictable performance, and
  stronger isolation.
- Private endpoints and disabled unrestricted public access.
- Duplicate detection using `deliveryIntentId`.
- Sessions keyed by `channelId` when ordering of incident updates must be preserved.
- Dead-letter queues for exhausted or invalid deliveries.
- Scheduled messages for digest windows and delayed retries where appropriate.

The consumer completes a Service Bus message only after writing the delivery result to Cosmos.

---

## 8. Teams Delivery Design

### 8.1 Selected architecture: tenant-managed Teams notification app

AzRadar uses an Azure Bot resource with the Microsoft Teams channel enabled and a tenant-managed,
notification-only Teams app. A dedicated Bot Gateway receives authenticated Azure Bot activities at
`/api/messages`, captures installation and conversation context, and persists the conversation
reference in Cosmos DB. A separate dispatch worker sends Adaptive Cards proactively through the Bot
Connector after consuming durable delivery work from Service Bus.

This is the target architecture rather than a temporary webhook bridge. It avoids user-owned flow
lifecycles, supports stable Teams activity identifiers and future message updates, and provides a
tenant-admin-governed application installation model. The Bot Gateway is the only new public ingress
surface. WAF or Front Door hardening is deferred for the pilot, but Bot JWT validation is mandatory.

### 8.2 Operational constraints

- The Teams app is installed into each centrally managed channel selected for the pilot.
- Installation and conversation-update activities create a disabled destination in Cosmos.
- A CloudLens administrator selects event families and explicitly enables the destination.
- Persist the Agents SDK conversation reference and required claims; do not store access tokens.
- Start with standard channels. Private-channel support and posting identity must be validated in
  the regulated enterprise's tenant.
- Keep Adaptive Cards within Teams and Bot Connector limits.
- Enforce per-channel rate limiting and bounded concurrency.
- Respect `Retry-After` and use exponential backoff for HTTP 429 and transient 5xx responses.
- Circuit-break a failing channel independently so one broken installation does not delay others.
- Mark removed installations inactive and stop routing to them.
- Never log Bot Connector tokens or unrestricted conversation-reference payloads.

### 8.3 Channel models

Support these models without changing ingestion:

| Model | Example | Best use |
|---|---|---|
| Central event-type channel | `Azure Service Incidents` | Broad opt-in visibility. |
| Platform-domain channel | `Azure Network Health` | Shared platform ownership. |
| Region channel | `Azure West Europe Health` | Regional operations. |
| Workload-team channel | `Payments Platform Alerts` | Future phase: subscription-specific accountable ownership. |
| Restricted security channel | `Azure Security Advisories` | Sensitive communications. |
| Digest channel | `Azure Health Weekly Digest` | Low-urgency awareness. |

For v1, use a small number of centrally curated platform-team channels. Each channel configuration
contains `subscribedEventTypes`, for example:

```json
{
  "subscribedEventTypes": ["ServiceIssue"]
}
```

That channel receives incidents from every registered subscription and does not receive Planned
Maintenance, Health Advisories, or Security Advisories unless those types are also selected.
Do not implement workload-team or subscription-specific channel registration in v1.

### 8.4 Message behavior

The first message contains:

- Microsoft event title and tracking ID.
- Status and event type.
- Affected services and regions.
- Affected enterprise subscriptions/resources for that audience.
- Microsoft-authored summary and recommended actions.
- Clearly labelled AI summary and recommended next action.
- Impact start, maintenance window, or deadline.
- Link to Azure Service Health and the VNet-restricted AzRadar detail view.
- Data classification label.

Subsequent updates:

- Prefer updating the existing card if the chosen Teams mechanism supports a stable update path.
- Otherwise post a concise threaded/follow-up message referencing the original incident.
- Resolutions use an explicit resolved state and elapsed duration.
- Minor text-only updates may be coalesced for a configurable window to reduce noise.

The delivery ledger stores the Teams response identifier when one is available.

### 8.5 Teams administration boundary

Bicep provisions Azure Bot Service, managed identities, App Services, Service Bus, Cosmos containers,
networking, and RBAC. The Teams app manifest package is generated from the deployed Bot UAMI client
ID. Upload, tenant approval, app-policy assignment, and channel installation remain Microsoft 365
tenant administration operations and are not performed by Azure Resource Manager.

---

## 9. Deferred Integrations

Azure DevOps Boards work-item creation is explicitly deferred. It is not part of the v1 data model,
runtime, identity permissions, UI, acceptance criteria, or deployment. The durable outbox and
destination abstraction should avoid preventing a future Azure DevOps dispatcher, but no
Azure DevOps-specific behavior is designed or implemented in this phase.

---

## 10. Data Model

All new Cosmos containers use partition key `/id`, following the current repository convention.
Where high-volume access later requires a different partition strategy, treat that as a deliberate
migration rather than silently diverging.

### 10.1 `service-health-subscriptions`

Explicit administrator-managed subscription registry.

| Field | Purpose |
|---|---|
| `id` | Subscription ID. |
| `displayName`, `tenantId` | Validated Azure metadata. |
| `status` | registering, active, permission-required, configuration-failed, disabled, removing. |
| `diagnosticSettingName` | AzRadar-owned setting name. |
| `eventHubAuthorizationRuleId`, `eventHubName` | Expected destination. |
| `provisioningIdentityClientId` | Dedicated UAMI used for the operation. |
| `lastVerifiedAt`, `lastProvisioningAttemptAt` | Operational state. |
| `lastErrorCode`, `lastErrorMessage` | Redacted actionable failure. |
| `createdAt`, `createdBy`, `updatedAt`, `updatedBy` | Audit metadata. |

### 10.2 `service-health-raw-events`

Append-only event versions.

| Field | Purpose |
|---|---|
| `id` | Azure event ID or deterministic SHA256 fallback. |
| `tenantId`, `subscriptionId`, `trackingId` | Source identity. |
| `eventHubPartition`, `sequenceNumber`, `offset`, `enqueuedAt` | Replay/audit metadata. |
| `payloadHash` | Duplicate and integrity check. |
| `rawContentCompressed` | Lossless source payload. |
| `receivedAt` | AzRadar ingestion time. |
| `schemaVersion` | Normalizer version. |

### 10.3 `service-health-subscription-events`

Current projection for one affected subscription.

| Field | Purpose |
|---|---|
| `id` | SHA256 of tenant, subscription, and tracking ID. |
| `trackingId`, `subscriptionId` | Correlation identity. |
| `eventType`, `eventSubType`, `eventLevel`, `status` | Normalized Azure facts. |
| `title`, `summary`, `description`, `recommendedActions` | Microsoft-authored content. |
| `affectedServices`, `affectedRegions` | Scope. |
| `impactStartTime`, `impactMitigationTime`, `lastUpdateTime` | Lifecycle. |
| `latestRawEventId`, `versionCount` | Source linkage. |
| `ownershipResolution` | Team mapping result. |

### 10.4 `service-health-incidents`

Tenant-level correlated incident.

| Field | Purpose |
|---|---|
| `id` | SHA256 of tenant and tracking ID. |
| `trackingId` | Azure incident identity. |
| `subscriptionIds`, `subscriptionCount` | Tenant impact. |
| `registeredSubscriptionImpacts` | Aggregated impact across registered subscriptions. |
| `resourceImpacts` | Resource Graph enrichment. |
| `currentStatus`, `currentVersion`, `lastUpdateTime` | Lifecycle. |
| `aiEnrichment` | Structured AI output and provenance. |
| `policyDecision` | Mandatory routes and explanation. |
| `firstSeenAt`, `resolvedAt` | AzRadar lifecycle. |

### 10.5 `ownership-mappings`

Deferred, versioned workload-team mappings for a future phase. This container is not required for
v1.

```json
{
  "id": "mapping-id",
  "scopeType": "subscription | resource | tag | management-group | service-region",
  "scopeValue": "...",
  "teamId": "...",
  "channelIds": ["..."],
  "businessCriticality": "critical | high | normal | low",
  "dataClassification": "standard | restricted",
  "priority": 100,
  "enabled": true,
  "version": 3
}
```

### 10.6 `notification-channels`

```json
{
  "id": "channel-id",
  "type": "teams-bot",
  "displayName": "Platform Operations / Azure Service Incidents",
  "tenantId": "<tenant-id>",
  "teamId": "<team-id>",
  "channelId": "<channel-id>",
  "conversationReferenceId": "<conversation-reference-id>",
  "registrationStatus": "registered",
  "subscribedEventTypes": ["ServiceIssue"],
  "rateLimitPerSecond": 2,
  "dataClassification": "standard",
  "enabled": true,
  "healthStatus": "healthy | degraded | disabled",
  "lastTestedAt": "..."
}
```

### 10.7 `routing-rules`

Versioned, ordered rules with:

- Deterministic match criteria.
- Mandatory and optional destinations.
- Real-time versus digest cadence.
- AI use permission.
- Coalescing window.
- Event-update behavior.
- Dry-run flag.

### 10.8 `delivery-intents`

Immutable outbox records representing intended work.

| Field | Purpose |
|---|---|
| `id` | Deterministic ID used as Service Bus message ID. |
| `incidentId`, `eventVersion` | Source. |
| `channelId`, `ruleId`, `ruleVersion` | Destination decision. |
| `renderedPayloadHash` | Idempotency/update decision. |
| `status` | pending, published, delivering, delivered, failed, dead-lettered, suppressed. |
| `notBefore` | Scheduled delivery/digest time. |

### 10.9 `notification-deliveries`

Append-only attempts and final outcomes:

- Delivery intent ID.
- Attempt number.
- Start/end timestamps.
- HTTP/result code.
- External Teams message identifier when available.
- Retry classification.
- Redacted error.
- Dead-letter reason.
- Correlation ID.

---

## 11. Integration with Existing AzRadar

### Reuse

- Existing UAMI-first authentication pattern.
- Existing Cosmos DB service and change-feed experience.
- Existing Azure OpenAI deployment and structured JSON output.
- Existing Resource Graph client and blast-radius concepts.
- Existing `IJobHandler` framework for scheduled reconciliation, replay, future ownership refresh, digest,
  and coverage-audit jobs.
- Existing API/UI for administration and reporting.

### New runtime components

The raw Event Hubs consumer should not be implemented as a normal user-created crawl job. It is a
continuously running event processor with partition checkpoints and different scaling semantics.

Recommended components:

1. **Service Health Ingress Worker**
   - Continuous Event Processor Client.
   - Normalization, persistence, and correlation.
2. **Service Health Enrichment Worker**
   - Cosmos change-feed driven.
   - Resource Graph, deterministic policy, AI enrichment, and outbox creation.
3. **Notification Dispatch Worker**
   - Service Bus consumers for Teams.

For a pilot these can share the current JobHost deployment if process isolation and scaling are
carefully bounded. Production should separate continuous ingestion from the existing crawl-job
execution lane so a long LLM crawl cannot delay an urgent health incident.

### New `IJobHandler` jobs

- `service-health-reconcile`
- `service-health-replay`
- `service-health-coverage-audit`
- `service-health-digest`

### API/UI areas

1. **Service Health**
   - Active, planned, advisory, security, and history views.
2. **Coverage**
   - Explicitly registered subscriptions, provisioning state, diagnostic-setting verification,
     latest observed event, and actionable permission errors.
3. **Subscriptions**
   - Add/register subscription ID, inspect configuration, retry provisioning, disable, and remove.
4. **Routes**
   - Teams channel registration, event-family multi-select, dry-run preview, and test delivery.
5. **Deliveries**
   - Delivered, retrying, failed, dead-lettered, and replay actions.
6. **AI audit**
   - Microsoft facts versus AI suggestions, confidence, and prompt/model version.

---

## 12. Functional Requirements

### FR-1 Subscription inventory and coverage

- Store an explicit registry of subscription IDs added by an administrator.
- Validate subscription format, accessibility, tenant, and provisioning-UAMI authorization.
- Track registering, active, permission-required, configuration-failed, disabled, and removing
  states.
- Verify that the required diagnostic setting is present and correctly targets the central Event
  Hub.
- Report settings that are missing, disabled, modified, or blocked by authorization/deny assignment.
- Reconcile only registered subscriptions.
- Never enumerate or onboard every subscription in a management group during v1.

### FR-2 Event ingestion

- Consume all Event Hub partitions concurrently.
- Persist before checkpoint.
- Tolerate duplicate and out-of-order events.
- Quarantine invalid payloads without stopping unrelated events.
- Record source offsets for replay.

### FR-3 Lifecycle

- Model active, updated, mitigated, and resolved states.
- Retain every source version.
- Correlate versions and subscription copies.
- Detect material changes before creating a new delivery intent.

### FR-4 Platform-channel routing

- Route one incident to every enabled platform channel whose `subscribedEventTypes` contains the
  normalized event family.
- Include affected registered subscriptions and resources in the central platform message.
- Do not implement workload-team ownership routing in v1.
- Explain every route and non-route.
- Support dry-run evaluation without external delivery.

### FR-5 AI

- Generate enrichment asynchronously.
- Never block mandatory dispatch beyond a configurable short enrichment budget.
- Fall back to a deterministic template.
- Keep source facts and AI output distinguishable.
- Support manual correction and feedback without modifying raw Azure data.

### FR-6 Teams

- Test a channel before enabling it.
- Let an administrator select any combination of `ServiceIssue`, `PlannedMaintenance`,
  `HealthAdvisory`, and `SecurityAdvisory`.
- Apply the selection across events from all registered subscriptions in v1.
- Do not infer subscriptions or workload ownership from the Teams channel.
- Send Adaptive Cards using a centrally approved template.
- Throttle independently per channel.
- Store external identifiers when returned.
- Redact secrets and restricted source fields from logs.
- Disable or quarantine repeatedly failing channels with operator visibility.

### FR-7 Replay and recovery

- Replay by Event Hub time/offset, raw event ID, tracking ID, subscription, or delivery intent.
- Default replay to dry-run.
- Require explicit confirmation before replaying external deliveries.
- Preserve idempotency across replay.

---

## 13. Non-Functional Requirements

### Scale

- A small administrator-selected subscription set at pilot launch.
- Design target of 10,000 subscriptions without changing the logical architecture.
- Burst target: 100,000 source records in 15 minutes, accounting for one Azure tracking ID being
  emitted into many affected subscriptions.
- 10,000 configured notification channels as an upper design boundary, though the pilot uses far
  fewer.

### Latency SLO

| Stage | Target |
|---|---|
| Event enqueued to durable raw persistence | 95% within 2 minutes |
| Raw persistence to mandatory delivery intent | 95% within 2 minutes |
| Mandatory Teams delivery, excluding Teams outage/throttling | 95% within 5 minutes end-to-end |
| Registered subscription provisioning | 95% within 5 minutes when required RBAC already exists |

Azure Monitor source-delivery latency is an external dependency and must be measured separately
from AzRadar processing latency.

### Availability and recovery

- No event loss from a single worker restart.
- Recovery Point Objective: no loss after Event Hub acceptance.
- Recovery Time Objective: 60 minutes for the dispatch platform.
- Reconciliation identifies events that did not arrive through the primary stream.

### Retention

Proposed defaults, subject to the organization's records policy:

- Event Hubs replay: 7 days.
- Raw Service Health event archive: 13 months.
- Correlated incident and delivery ledger: 24 months.
- Security advisory retention: separate restricted policy.
- Operational logs: 90 days hot, longer archive if required.

### Cost controls

- Filter the diagnostic setting to `ServiceHealth` only.
- Coalesce subscription copies before AI calls.
- Call the LLM once per material central incident version, not once per subscription copy.
- Cache registered-subscription metadata.
- Use digests for low-urgency events.
- Set token and concurrency budgets with visible throttling, never silent dropping.

---

## 14. Security and Compliance

### Identity and RBAC

Use separate managed identities for:

| Identity | Minimum access |
|---|---|
| Subscription provisioning UAMI | Registered-subscription diagnostic-setting read/write/delete as configured; read access for validation; Event Hub authorization-rule `listKeys` on the dedicated rule. |
| Ingress worker | Event Hubs Data Receiver; Cosmos data-plane write to Service Health containers. |
| Enrichment worker | Cosmos read/write; Resource Graph Reader scope; Azure OpenAI user; Service Bus sender. |
| Bot Gateway | Azure Bot UAMI for Connector authentication; Cosmos read/write for conversation registration. |
| Teams dispatcher | Service Bus sender/receiver; Cosmos delivery read/write; attached Azure Bot UAMI for proactive Connector authentication. |
| Coverage reconciler | Uses the dedicated subscription provisioning UAMI; does not use the general runtime identity. |

Do not grant broad Owner or Contributor at tenant root. The v1 design requires no tenant-root or
management-group role. Use a custom role on each explicitly registered subscription and narrowly
scoped central-resource permissions.

### Network

- Private endpoints for Event Hubs consumer access, Service Bus, Cosmos DB, and Azure
  OpenAI where supported by the regulated enterprise's landing zone.
- Private DNS integrated with the workload VNet.
- Controlled outbound egress to Azure Bot Service through the approved firewall path.
- The dedicated Bot Gateway is public for Azure Bot callbacks; core API, workers, messaging, and
  data stores remain private.
- Enable the Event Hubs trusted Microsoft services bypass only because Azure Monitor diagnostic
  settings require it; document and monitor this exception.

### Infrastructure as code

The repository Bicep must provision and configure:

- One single-region Event Hubs namespace and Service Health event hub, using Standard for the pilot
  and allowing a later Premium upgrade.
- Consumer groups for live processing and replay/audit.
- The dedicated subscription provisioning UAMI.
- The existing/runtime identities' data-plane roles for Event Hubs, Cosmos DB, Service Bus,
  Key Vault, Resource Graph, and Azure OpenAI as applicable.
- The provisioning UAMI's narrow role on the central Event Hub authorization rule.
- App settings for the provisioning UAMI client ID, diagnostic-setting name, Event Hub name, and
  authorization-rule resource ID.

Role assignments on customer-selected subscriptions are intentionally not created automatically by
the central deployment. Provide a reusable subscription-scope Bicep module or CLI command that a
privileged customer administrator runs for each pilot subscription before registering it in the UI.

### Data protection

- TLS 1.2 or higher.
- Customer-managed keys where required by organizational policy and supported by the selected SKU.
- Conversation references are treated as restricted routing metadata and redacted from logs,
  exceptions, and telemetry.
- Restricted security events stored in a separate access-controlled container or account if organizational
  classification policy requires stronger separation.
- Field-level allowlists for Teams, telemetry, and Azure OpenAI.
- Full audit of configuration and manual replay changes.

### Security advisories

Some security advisories require elevated access and may contain sensitive details. The feature must:

- Preserve Azure RBAC restrictions.
- Avoid broad distribution based only on normal ownership mapping.
- Use restricted routes and explicit recipients.
- Disable AI processing by default.
- Redact message previews in general operational dashboards.
- Audit every view and delivery where required by organizational policy.

---

## 15. Reliability and Failure Handling

| Failure | Required behavior |
|---|---|
| Duplicate Activity Log record | No duplicate raw ID; aggregate remains correct. |
| Out-of-order update | Keep all versions; current projection selected by Azure update time plus deterministic tie-breaker. |
| Event Hubs unavailable | Azure retains events; worker reconnects with bounded exponential backoff. |
| Consumer checkpoint corruption | Rebuild from Event Hub retention or Capture archive using idempotent writes. |
| Cosmos unavailable | Do not checkpoint Event Hub; retry without losing source event. |
| Resource Graph unavailable | Mark enrichment pending; mandatory event route continues with subscription-level data. |
| Azure OpenAI unavailable | Deterministic message sends; AI status is failed/pending. |
| Service Bus unavailable | Outbox remains unpublished and retries. |
| Teams 429 | Honor `Retry-After`, reschedule, and preserve per-channel ordering. |
| Teams app uninstalled or conversation invalidated | Disable the destination; dead-letter after policy; route critical events to platform fallback. |
| Poison message | Quarantine or dead-letter with redacted diagnostics and replay tooling. |

### Delivery state machine

```text
planned -> queued -> delivering -> delivered
                    |              |
                    v              v
                  retrying       superseded
                    |
                    v
                dead-lettered
```

No delivery is considered successful solely because a message was published to Service Bus.
Success means the external destination accepted it and the outcome was persisted.

---

## 16. Observability

### Metrics

- Expected subscriptions.
- Covered subscriptions.
- Missing/drifted/exempt subscriptions.
- Event Hub lag by partition.
- Raw events received by event type.
- Duplicate ratio.
- Correlation fan-in: subscription records per tracking ID.
- AI success, latency, token use, and fallback rate.
- Delivery intents by destination and urgency.
- Teams success, retry, throttle, and dead-letter rates.
- End-to-end latency from Azure event time and Event Hub enqueue time.
- Reconciliation discrepancies.

### Alerts

- Coverage below 99.9%.
- No Service Health records observed for an abnormal period while coverage remains expected.
- Event Hub consumer lag exceeds threshold.
- Critical event has no delivered mandatory route within five minutes.
- Dead-letter count greater than zero for critical deliveries.
- Teams channel health failure.
- Reconciliation finds an active event missing from the stream.

### Correlation

Every log and trace uses:

- `trackingId`
- `subscriptionId` where applicable
- `rawEventId`
- `incidentId`
- `deliveryIntentId`
- `channelId`
- `traceId`

Never include Bot Connector tokens, serialized conversation references, or unrestricted event
descriptions in operational log properties.

---

## 17. Administration and Guardrails

### Channel onboarding

1. Operator creates the Teams Workflow with approved ownership.
2. Workflow URL is placed in Key Vault.
3. Operator registers channel metadata and secret URI in AzRadar.
4. AzRadar sends a synthetic test card.
5. Operator verifies receipt.
6. Channel remains `dry-run` until a routing preview is approved.
7. Operator activates the channel.

### Routing rule onboarding

- Every new rule starts in dry-run.
- Preview shows historical events that would have matched.
- Preview estimates message count by channel.
- Activation records approver, timestamp, and rule version.
- A kill switch disables a channel, rule, event type, or all external dispatch.
- A platform fallback route cannot be removed while mandatory event types are enabled.

### Subscription onboarding

1. A customer administrator grants the dedicated subscription provisioning UAMI the required
   custom role on the target subscription.
2. An AzRadar administrator enters the subscription ID in the UI.
3. AzRadar validates access and creates or updates its named `ServiceHealth` diagnostic setting.
4. Coverage becomes green only after AzRadar reads the setting back and verifies the Event Hub
   destination and category.
5. The UI exposes retry and copyable onboarding instructions when permission is missing.

The application must not require management-group access and must not automatically discover or
configure unregistered subscriptions in v1.

- An end-to-end synthetic Service Health event cannot be generated on demand, so ingestion health
  also relies on configuration checks, Event Hub platform metrics, and periodic reconciliation.

---

## 18. Delivery Phases

### Phase 0 — Feasibility and regulated-industry approvals

- Provision the dedicated subscription provisioning UAMI through Bicep.
- Grant its custom onboarding role to one test subscription.
- Register that subscription through the AzRadar UI and validate diagnostic-setting deployment.
- Validate central Event Hub destination and trusted-service firewall exception.
- Validate actual Service Health Activity Log payloads and update semantics.
- Validate Service Health Resource Graph fields and sensitive advisory access.
- Validate Teams Workflow creation, standard/private channel behavior, co-ownership, outbound
  firewall route, throttling, and Adaptive Card rendering.

**Exit:** one real or captured Service Health event traverses Event Hub to a test Teams channel
without a public AzRadar endpoint.

### Phase 1 — Central visibility

- Event Hub ingestion.
- Raw and normalized persistence.
- Tenant correlation by tracking ID.
- ARG reconciliation.
- Subscription registration and coverage dashboard.
- No external delivery except a test channel.

**Exit:** all pilot subscriptions are covered and event history is queryable.

### Phase 2 — Platform Teams dispatch

- Deterministic policy.
- AI summary with safe fallback.
- Service Bus dispatch.
- Administrator-managed platform Teams channels with selectable event families.
- Delivery ledger, retries, and dead-letter UI.

**Exit:** platform team accepts relevance, latency, and noise during a four-week pilot.

### Phase 3 — Workload-team-aware scale-out

- CMDB/tag-based ownership integration.
- Workload-team channel onboarding scoped to selected subscriptions.
- Digests and maintenance lead-time policy.
- Incident update/resolution behavior.
- Self-service subscription to approved central channels.
- Optional management-group policy deployment after successful pilot validation.

**Exit:** agreed ownership-match target and channel SLO are met.

### Phase 4 — Deferred integrations and resilience

- Optional Azure DevOps work items.
- Acknowledgement and escalation.
- Restricted security routing.
- Historical trend and vendor-risk reporting.
- Multi-region and zone-resilience evaluation.

---

## 19. Acceptance Criteria

1. Every active subscription explicitly registered for the pilot has a verified `ServiceHealth`
   diagnostic setting or an actionable `permission-required`/failure state.
2. No workload team must create an Azure Service Health alert rule or action group.
3. AzRadar exposes no new public inbound endpoint.
4. A single tracking ID affecting multiple subscriptions produces one central incident with complete
   subscription impact.
5. Duplicate and replayed source records do not create duplicate Teams messages.
6. Critical active Service Issues create a mandatory platform delivery intent without depending on
   AI.
7. AI failure does not block or suppress deterministic dispatch.
8. Every normalized event family has an explicit platform-channel route or an operator-visible
   no-route state.
9. Teams 429 and transient failures retry without blocking other channels.
10. Exhausted deliveries are dead-lettered with operator-visible replay controls.
11. Event updates and resolutions link to the existing incident and delivery history.
12. Raw event, normalized facts, AI enrichment, routing decision, and delivery result are auditable.
13. Workflow URLs and other secrets never appear in Cosmos documents, application logs, or API
    responses.
14. ARG reconciliation can restore or identify an active event missing from the primary stream.
15. The feature can be disabled globally without stopping Service Health collection and archival.
16. A channel configured for Service Issues receives incidents from all registered subscriptions
    and does not receive Planned Maintenance or Health Advisories.
17. Billing updates and tenant/global-only notifications are shown as unsupported by the v1
    Activity Log ingestion path rather than being implied as covered.
18. Subscription onboarding uses the dedicated provisioning UAMI and never falls back to the
    general AzRadar runtime UAMI.

---

## 20. Success Metrics

| Metric | Pilot target |
|---|---|
| Registered-subscription collection coverage | 100% configured or actionable failure state |
| Mandatory incident delivery success | >= 99.9% excluding confirmed Teams outage |
| End-to-end p95 after Event Hub enqueue | <= 5 minutes |
| Duplicate external notifications | < 0.1% |
| Critical events sent through deterministic fallback due to AI failure | Measured; no missed delivery |
| Platform operator rating of signal relevance | >= 4/5 after pilot |

---

## 21. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Provisioning UAMI lacks access to a registered subscription | Reject activation with `permission-required` and show the exact role-assignment instructions. |
| Azure Monitor requires Event Hub trusted-service bypass | Isolate the namespace, restrict all other network paths, document the exception, and monitor configuration drift. |
| One incident generates thousands of subscription records | Correlate by tracking ID before AI and routing; retain subscription details in the aggregate. |
| Teams app is removed or blocked by tenant policy | Detect uninstall events, disable routing, monitor health, and keep a fallback channel. |
| Teams throttles burst delivery | Per-channel Service Bus sessions, rate limiter, coalescing, digests, and `Retry-After` handling. |
| Sensitive security content leaks to broad channels or AI | Restricted event path, field allowlists, AI disabled by default, and separate authorization. |
| AI hallucination changes operational meaning | Keep Microsoft facts unchanged, label AI text, enforce schema, and prohibit AI severity downgrade/suppression. |
| Current JobHost is busy with crawl/LLM work | Separate continuous ingress and dispatch lanes from crawl jobs in production. |
| Event Hubs retention expires during prolonged outage | Enable Capture/archive and ARG reconciliation. |
| Routing-rule mistake floods channels | Dry-run by default, historical preview, volume estimate, approval, rate limits, and kill switch. |
| Billing or tenant/global-only events are assumed to be covered | Display the v1 coverage boundary in the UI and documentation; add a separate future ingestion source if required. |

---

## 22. Open Decisions for a Regulated Enterprise

These decisions do not block the architecture, but must be resolved before production:

1. Which initial subscription IDs are approved for the pilot?
2. Who performs the one-time role assignment for the subscription provisioning UAMI?
3. What exact custom-role permissions and delete behavior will security approve?
4. What is the approved single Azure region?
5. Is the Azure Monitor trusted-service firewall bypass approved for the dedicated Event Hub?
6. How many central platform Teams channels should the pilot use, and are they standard or private?
7. Which event families should each initial channel receive?
8. Which tenant app catalog, app permission policy, and installation process are approved for the Teams app?
9. Which event types may be sent to Azure OpenAI, especially security advisories?
10. What are the data classification and retention requirements for Service Health payloads?
11. What is the platform fallback and out-of-band escalation path when Teams is unavailable?

---

## 23. Alternatives Summary

| Architecture | Public AzRadar endpoint | Workload-team effort | Durability/replay | Tenant-scale fit | Decision |
|---|---:|---:|---:|---:|---|
| Per-subscription email/webhook alerts | Usually yes for custom receiver | High | Low unless receiver stores | Poor | Reject |
| Service Health alert -> Logic App -> Teams | No AzRadar endpoint, but distributed workflow | Medium/high | Bounded | Moderate | Not primary |
| Service Health alert -> Action Group Event Hub | No | None if centrally deployed | Good | Good | Valid alternative |
| Registered subscription diagnostic setting -> Event Hub | No | Admin grants UAMI once; AzRadar configures setting | Strong | Strong | **Recommend for v1** |
| Resource Graph polling only | No | None | Application-managed snapshots | Moderate | Reconciliation only |
| Direct AzRadar polling of REST API per subscription | No | None | Application-managed | Operationally expensive | Reject as primary |

---

## 24. Research Notes and Official References

Research was checked against current Microsoft documentation on 2026-09-10.

1. [Service Health notifications](https://learn.microsoft.com/azure/service-health/service-health-notifications-properties)
   — Service Health notifications are written to the subscription Activity Log.
2. [Azure Activity Log event schema](https://learn.microsoft.com/azure/azure-monitor/platform/activity-log-schema)
   — describes the Service Health category and streamed schema differences.
3. [Diagnostic settings in Azure Monitor](https://learn.microsoft.com/azure/azure-monitor/platform/diagnostic-settings)
   — Activity Log export destinations, Event Hub requirements, cross-subscription destinations, TLS,
   and trusted Microsoft services firewall behavior.
4. [Roles, permissions, and security in Azure Monitor](https://learn.microsoft.com/azure/azure-monitor/fundamentals/roles-permissions-security)
   — permissions required to configure diagnostic settings and access destination authorization
   rules.
5. [Azure Event Hubs private endpoints and trusted Microsoft services](https://learn.microsoft.com/azure/event-hubs/private-link-service#trusted-microsoft-services)
   — Azure Monitor diagnostic settings and action groups are supported trusted-service scenarios.
6. [Azure Resource Graph tables overview for Service Health](https://learn.microsoft.com/azure/service-health/azure-resource-graph-overview)
   — event, impacted-resource, lifecycle, and sensitivity fields.
7. [Azure Resource Graph sample queries for Service Health](https://learn.microsoft.com/azure/service-health/resource-graph-samples)
   — cross-subscription active incident, maintenance, and advisory queries.
8. [Azure Service Health portal and retention](https://learn.microsoft.com/azure/service-health/service-health-portal-update)
   — portal retention and REST access behavior.
9. [Azure Monitor action groups](https://learn.microsoft.com/azure/azure-monitor/alerts/action-groups)
   — Event Hub action support, managed identity support, webhook constraints, and retry behavior.
10. [Create activity log alerts for Service Health using Bicep](https://learn.microsoft.com/azure/service-health/alerts-activity-log-service-notifications-bicep)
   — automated alert-rule configuration and event filters.
11. [Proactive messages in Teams](https://learn.microsoft.com/microsoftteams/platform/bots/how-to/conversations/send-proactive-messages)
    and [Azure Bot Service identity](https://learn.microsoft.com/azure/bot-service/bot-builder-authentication)
    — conversation installation requirements, proactive delivery, and managed identity.
12. [Azure Event Hubs quotas and limits](https://learn.microsoft.com/azure/event-hubs/event-hubs-quotas)
    — tier, partition, throughput, retention, and feature limits.
13. [Azure Service Bus architecture best practices](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-performance-improvements)
    and [queues, topics, and subscriptions](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-queues-topics-subscriptions)
    — reliable delivery, competing consumers, publish/subscribe, and performance considerations.

---

## 25. Final Recommendation

Proceed with a pilot based on:

1. **Subscription Activity Log `ServiceHealth` export to one central Event Hub.**
2. **Central deployment and continuous coverage reconciliation owned by the platform team.**
   For v1 this applies only to subscriptions explicitly registered through the UI.
3. **A dedicated AzRadar Event Hubs ingress worker with raw, normalized, and tenant-correlated
   Cosmos records.**
4. **Resource Graph reconciliation and impacted-resource enrichment.**
5. **Deterministic mandatory routing, with Azure OpenAI used only for additive enrichment.**
6. **A Cosmos outbox feeding Service Bus for reliable, isolated destination delivery.**
7. **A Bicep-provisioned Azure Bot, tenant-managed Teams notification app, public minimal Bot
   Gateway, and proactive dispatch worker, filtered by administrator-selected event family.**
8. **A dedicated, Bicep-provisioned subscription provisioning UAMI whose client ID is separately
   configurable and whose permissions are granted only on registered subscriptions.**

This design gives a regulated enterprise a controlled pilot without requiring management-group
enforcement, isolates the required public Bot callback from the core platform, and creates an auditable platform that can evolve
from selected subscriptions and platform channels into tenant-scale, ownership-aware workload
routing.
