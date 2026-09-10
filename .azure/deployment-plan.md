# Azure Deployment Plan

> **Status:** Deployed

Generated: 2026-09-08

---

## 1. Project Overview

**Goal:** Deploy and test centralized Azure Service Health ingestion from the
existing Event Hub through normalized Cosmos persistence and the durable
dispatcher boundary, including a gated synthetic-event publisher.

**Path:** Add Components

**Deployment boundary:** The user explicitly authorized updating the API/UI and
JobHost applications. Use the blue/green image strategy for both.

---

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Development / pilot |
| Scale | Initial single-subscription pilot, designed for later scale-out |
| Budget | Balanced |
| Subscription | MOHOSSA-M365CPI50986977 (`5e22addc-6168-4683-afd0-789a121ca5d3`) |
| Resource group | `az-radar-vnet-rg` |
| Location | Central US (`centralus`) |
| Deployment scope | Service Health infrastructure, API/UI, and JobHost App Services |

The user subsequently instructed: "go deploy the changes in app. and test it
out if it works".

---

## 3. Components Detected

| Component | Type | Technology | Path |
|-----------|------|------------|------|
| AzRadar API/UI | Existing app | .NET 8 + React on App Service | `src/AzRadar.Api`, `src/az-radar-ui` |
| AzRadar JobHost | Existing worker | .NET 8 on App Service | `src/AzRadar.JobHost` |
| Shared services | Existing library | .NET 8 | `src/AzRadar.Shared` |
| Infrastructure | Existing IaC | Bicep | `infra` |
| Service Health design | Product requirements | Markdown | `prds/service-health-dispatch.md` |

---

## 4. Recipe Selection

**Selected:** Azure CLI + scoped Bicep

**Rationale:** The repository already uses Bicep, but the normal
`infra/main.bicep` owns the API/UI App Service and is too broad for the
confirmed deployment boundary. A dedicated additive entry point will target
only Service Health resources and existing Cosmos/VNet dependencies.

The API/UI deployment uses the repository's Docker Hub blue/green process.
The current API image is `blue`, so this release will deploy `green`. The
current JobHost image is `green`, so this release will deploy `blue`.

---

## 5. Architecture

| Component | Azure Service | SKU / Configuration |
|-----------|---------------|---------------------|
| Ingestion namespace | Azure Event Hubs | Standard, 1 throughput unit, single region |
| Event stream | Event Hub | `service-health`, 4 partitions, 7-day retention |
| Consumers | Event Hub consumer groups | `azradar-live`, `azradar-replay` |
| Provisioning identity | User-assigned managed identity | Dedicated to subscription onboarding |
| Runtime access | Azure RBAC | Existing AzRadar UAMI gets Data Receiver |
| Azure Monitor publishing | Event Hubs authorization rule | Dedicated rule referenced by diagnostic settings |
| Network ingress | Private Endpoint | Existing `snet-private-endpoints` |
| Name resolution | Private DNS | `privatelink.servicebus.windows.net` linked to existing VNet |
| Registry persistence | Cosmos DB containers | `service-health-subscriptions`, `service-health-channels`, partition key `/id` |
| Pilot source | Subscription Activity Log diagnostic setting | Only `ServiceHealth` category |

No public AzRadar webhook or inbound endpoint is introduced. Trusted Azure
services may publish to Event Hubs; AzRadar consumption uses managed identity
through the private endpoint.

---

## 6. Provisioning Limit Checklist

The Microsoft.Quota CLI returned no Event Hubs quota records for Central US, so
documented Azure limits and live resource counts are used for unsupported
resource types.

| Resource Type | Number to Deploy | Total After Deployment | Limit/Quota | Notes |
|---------------|------------------|------------------------|-------------|-------|
| `Microsoft.EventHub/namespaces` | 1 | 1 | 1,000 per subscription per region | Live count in Central US: 0; Premium creation was rejected in Central US, so the pilot uses Standard |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | 1 | 8 | 200 per subscription | Live subscription count: 7; official Azure limits |
| `Microsoft.Network/privateEndpoints` | 1 | 10 | 1,000 per VNet | Live subscription count: 9; target `/24` subnet currently has 3 allocated endpoint IP configurations |
| `Microsoft.Network/privateDnsZones` | 1 | 10 | 1,000 per subscription | Live subscription count: 9; official Azure limits |
| Cosmos DB serverless containers | 2 | 11 | 100 per account | Live target database count: 9; official Cosmos DB serverless limits |
| Event Hub consumer groups | 2 | 2 | 100 per event hub in Premium | New Event Hub; official Event Hubs limits |

**Status:** All planned resources are comfortably within documented limits.

---

## 7. Execution Checklist

### Phase 1: Planning

- [x] Analyze workspace and existing Azure environment
- [x] Gather requirements and deployment boundary
- [x] Confirm subscription and Central US location with user
- [x] Inventory live resources and existing VNet/Cosmos dependencies
- [x] Invoke Azure quota workflow and validate capacity
- [x] Select scoped Bicep deployment recipe
- [x] User approved additive-infrastructure-only deployment

### Phase 2: Execution

- [x] Create scoped Service Health Bicep entry point
- [x] Complete local .NET, TypeScript, and Bicep validation
- [x] Run Azure deployment validation and what-if
- [x] Set plan status to `Ready for Validation`

### Phase 3: Validation

- [x] Invoke `azure-validate`
- [x] All validation checks pass for the ingestion release
  - [x] 1. Core Validation (CLI, auth, build, validate, what-if)
  - [x] 2. Docker Build (API and JobHost)
  - [x] 3. Azure Policy Validation
- [x] Record validation proof for the ingestion release
- [x] Set status to `Validated`

### Phase 4: Deployment

- [x] Invoke `azure-deploy`
- [x] Deploy additive infrastructure
- [x] Verify Event Hubs, UAMI, private networking, Cosmos containers, and RBAC
- [x] Configure the pilot subscription diagnostic setting for `ServiceHealth`
- [x] Confirm the protected API/UI App Service image and configuration are unchanged
- [x] Set status to `Deployed`

### Phase 5: API/UI deployment

- [x] Build and validate `moimhossain/az-radar-api:blue`
- [x] Push the blue image to Docker Hub
- [x] Attach the provisioning UAMI while retaining `az-radar-uami`
- [x] Configure Service Health app settings
- [x] Switch only `azr-api-x8c5i2` to the blue image
- [x] Verify API/UI health
- [x] Register subscription `5e22addc-6168-4683-afd0-789a121ca5d3` through the deployed API
- [x] Verify the subscription is persisted as `active`
- [x] Verify the Service Health UI route renders
- [x] Set status to `Deployed`

---

## 8. Validation Proof

> To be populated only by the `azure-validate` skill.

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Scoped Bicep preflight | `validate-deployment.ps1 -Scope group -ResourceGroup az-radar-vnet-rg -Template .\infra\service-health.bicep -Parameters .\infra\service-health.bicepparam -Subscription 5e22addc-6168-4683-afd0-789a121ca5d3` | Pass after switching the pilot from Premium to Standard because Central US rejected new Premium namespace creation | 2026-09-10T11:15:57+02:00 |
| Detailed what-if | `az deployment group what-if ... --result-format ResourceIdOnly --no-pretty-print` | Pass: 11 creates, 3 idempotent deploys, 0 modifications, 0 deletions, 3 deferred RBAC evaluations | 2026-09-10T11:15:57+02:00 |
| Private networking retry | ARM validation plus detailed what-if after setting Event Hubs public network access to `Disabled` | Pass: private-endpoint-only namespace; 2 creates, 14 idempotent deploys, 0 deletes, 3 deferred RBAC evaluations | 2026-09-10T11:15:57+02:00 |
| .NET build | `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore --verbosity quiet` | Pass: 0 warnings, 0 errors | 2026-09-10T11:15:57+02:00 |
| React type check | `npx tsc -p src\az-radar-ui\tsconfig.json --noEmit` | Pass | 2026-09-10T11:15:57+02:00 |
| Azure Policy review | `az policy assignment list --subscription 5e22addc-6168-4683-afd0-789a121ca5d3 --disable-scope-strict-match` | Pass: assigned Defender policies do not conflict with planned resource types, SKU, tags, or network configuration | 2026-09-10T11:15:57+02:00 |
| Static RBAC review | Review of `infra/service-health.bicep`, `infra/modules/event-hubs.bicep`, and `infra/modules/service-health-subscription.bicep` | Pass: runtime UAMI receives Event Hubs Data Receiver at namespace scope; provisioning UAMI receives diagnostic-setting permissions at subscription scope and Event Hub authorization-rule permissions at resource scope | 2026-09-10T11:15:57+02:00 |
| API release build | `dotnet build src\AzRadar.Api\AzRadar.Api.csproj --no-restore -c Release --verbosity quiet` | Pass | 2026-09-10T11:45:05+02:00 |
| API tests | `dotnet test tests\AzRadar.Api.Tests --no-restore --verbosity quiet` | Pass | 2026-09-10T11:45:05+02:00 |
| UI type check | `npx tsc -p src\az-radar-ui\tsconfig.json --noEmit` | Pass | 2026-09-10T11:45:05+02:00 |
| Blue production image | `docker build --no-cache ... -f Dockerfile.api -t moimhossain/az-radar-api:blue .` | Pass: `sha256:83b77cd45debc6f6c72255526d159e0591d43b755ef8cf3dcbf37fa869291997` | 2026-09-10T11:45:05+02:00 |

**Validated by:** azure-validate skill
**Validation timestamp:** 2026-09-10T11:15:57+02:00

### Ingestion release validation

| Check | Command Run | Result |
|-------|-------------|--------|
| Final .NET build | `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore --verbosity quiet` | Pass: 0 warnings, 0 errors |
| Service Health tests | `dotnet test tests\AzRadar.Shared.Tests --no-restore --filter "FullyQualifiedName~ServiceHealth" --verbosity quiet` | Pass: 3 tests |
| API tests | `dotnet test tests\AzRadar.Api.Tests --no-restore --verbosity quiet` | Pass |
| API image | `docker build --no-cache ... -f Dockerfile.api -t moimhossain/az-radar-api:green .` | Pass: `sha256:133a0ea92e552b7e34d0506b6c30ac69fd23595c9ad64a120afd7fe13abbde9c` |
| JobHost image | `docker build --no-cache ... -f Dockerfile.jobhost -t moimhossain/az-radar-jobhost:blue .` | Pass: `sha256:90f04e08c9ad4a646614b4cc4d3746908324b5ab867fc73a644326bdaa73dec1` |
| Scoped Bicep preflight | `validate-deployment.ps1` plus full-payload what-if | Pass: 4 Cosmos container creates, no deletes |
| Bicep lint | `az bicep lint --file infra\service-health.bicep` | Pass |
| Azure Policy review | `az policy assignment list ...` | Pass: assigned Defender policies do not conflict with this release |

## Role Assignment Verification

- Status: Verified
- Identities checked: existing AzRadar runtime UAMI and dedicated Service Health provisioning UAMI
- Roles confirmed: Event Hubs Data Receiver and Data Sender for the runtime UAMI; Cosmos DB Built-in Data Contributor remains assigned by the existing infrastructure; diagnostic-setting and authorization-rule management roles remain assigned to the provisioning UAMI
- Scope: Event Hubs roles are namespace-scoped, Cosmos data access is account-scoped, and provisioning permissions are limited to the required subscription/resource operations
- Issues: None

---

## 9. Files

| File | Purpose | Status |
|------|---------|--------|
| `.azure/deployment-plan.md` | Deployment source of truth | Complete |
| `infra/service-health.bicep` | Additive deployment entry point | Complete |
| `infra/modules/event-hubs.bicep` | Event Hubs, private endpoint, DNS, and central RBAC | Complete |
| `infra/modules/service-health-subscription.bicep` | Pilot subscription role and diagnostic setting | Complete |
| `infra/modules/cosmos-service-health-containers.bicep` | Add two containers to existing Cosmos DB | Complete |

---

## 10. Rollback

The deployment is additive. If validation or testing fails, stop before
deleting resources. Any cleanup that deletes the diagnostic setting, Event
Hubs namespace, identity, private endpoint, DNS zone, role assignments, or
Cosmos containers requires separate explicit user approval.

---

## 11. Deployment Verification

- Deployment: `service-health-pilot-20260910`
- State: Succeeded
- Event Hubs namespace:
  `az-radar-service-health-qrdtyepf7wbja.servicebus.windows.net`
- Event Hub: `service-health`
- SKU: Standard. Central US rejected creation of a new Premium namespace.
- Public network access: Disabled
- Private endpoint: Approved and provisioned in `snet-private-endpoints`
- Trusted Microsoft services: Enabled for Azure Monitor publishing
- Diagnostic setting: `az-radar-service-health`
- Exported Activity Log category: `ServiceHealth` only
- Runtime identity: `az-radar-uami` has Azure Event Hubs Data Receiver on the
  namespace
- Provisioning identity client ID:
  `a3bff8ed-6aa9-4306-a557-b02298edb876`
- Provisioning identity has the custom diagnostic-setting role at subscription
  scope and the Event Hub authorization-rule role at resource scope
- Cosmos containers created with `/id` partition keys:
  `service-health-subscriptions`, `service-health-channels`
- Protected API/UI App Service remains running on
  `DOCKER|moimhossain/az-radar-api:green`
- Event Hubs currently reports zero incoming messages. No Service Health event
  was published in the subscription during the verification window, and Azure
  Monitor does not replay historical Activity Log entries when a diagnostic
  setting is created.

## 12. Next Step

The Service Health configuration UI is live at
`https://azr-api-x8c5i2.azurewebsites.net/service-health`. The pilot
subscription is registered and verified as `active`.

## 13. API/UI Deployment Verification

- Image: `moimhossain/az-radar-api:blue`
- Docker digest:
  `sha256:cab4d6e0008127a53c0ba9b941c6f5948ff1c41621eeb107956d96f3920591a3`
- Existing runtime UAMI retained
- Dedicated provisioning UAMI attached
- Service Health API returned the registered subscription with status `active`
- Verify endpoint successfully re-read and validated the subscription
  diagnostic setting
- Azure diagnostic setting remains enabled for `ServiceHealth` only
- `/service-health` returned HTTP 200
- Deployed JavaScript contains the subscription registration and platform
  Teams channel configuration views

## 14. Event Ingestion to Dispatcher Boundary

The user approved implementation through the dispatcher handoff:

- Consume `azradar-live` from the private Event Hub using the runtime UAMI.
- Start from the earliest retained event when no partition checkpoint exists.
- Normalize and deduplicate Service Health Activity Log records.
- Persist raw payloads, normalized facts, AI enrichment, and Cosmos checkpoints.
- Match enabled platform channel rules and create immutable pending delivery
  intents, but do not send to Teams yet.
- Quarantine malformed or oversized payloads without blocking a partition.
- Add a configuration-gated UI/API test publisher that sends synthetic
  Service Health records directly to Event Hubs using managed identity.
- Display recent ingested events and pending delivery intents in the UI.

Deployment additions:

- Four Cosmos containers: events, delivery intents, checkpoints, quarantine.
- Azure Event Hubs Data Sender for the runtime UAMI.
- API image: current `blue`, deploy `green`.
- JobHost image: current `green`, deploy `blue`.
- Enable ingress only on JobHost.
- Enable synthetic publishing only on the pilot API.

Validation and deployment:

- [x] Build solution
- [x] Type-check UI
- [x] Run targeted Service Health tests
- [x] Validate updated scoped Bicep and what-if
- [x] Deploy containers and sender RBAC
- [x] Build and push API `green`
- [x] Build and push JobHost `blue`
- [x] Configure and deploy both applications
- [x] Publish a synthetic event through the API
- [x] Verify Event Hub consumption, Cosmos checkpoint, normalized event, and routing state
- [x] Set status to `Deployed`

### Ingestion deployment results

- Infrastructure deployment `service-health-ingestion-20260910` succeeded.
- API/UI runs `moimhossain/az-radar-api:green`.
- JobHost runs `moimhossain/az-radar-jobhost:blue` with `Always On` enabled.
- Runtime UAMI has Event Hubs Data Receiver, Event Hubs Data Sender, and Cosmos DB Built-in Data Contributor.
- Synthetic event `TEST-20260910122732` was normalized and AI-enriched with routing status `ready-for-dispatch`.
- Pending delivery intent `d9fa50ddc6e8081e73dcf4a29fb19f6c53b60918171f802657096693000e4849` was created for the pilot incident channel.
- Event Hubs reported active consumer connections, outgoing messages, and no user errors.
- External Teams delivery remains intentionally unimplemented.

### Live Role Verification

- Existing runtime UAMI principal `1d009d7d-59a6-489e-929d-2b1a6fe6f97b` has Azure Event Hubs Data Receiver and Azure Event Hubs Data Sender on the Service Health namespace.
- The same principal has Cosmos DB Built-in Data Contributor on the AzRadar Cosmos account.
- Status: Pass.
