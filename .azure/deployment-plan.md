# Azure Deployment Plan

> **Status:** Deployed

Generated: 2026-09-08

## Current release: Service and region scoped watchlist (2026-09-11)

Issue #7 adds optional Azure regions to Service Watchlist entries and filters Azure Updates and
Microsoft Learn intelligence before persistence using LLM-extracted canonical services/regions
plus deterministic alias, acronym, and region matching.

This is an application-image-only release to the existing App Services:

- API/UI: `azr-api-x8c5i2` in `az-radar-vnet-rg`
- JobHost: `azr-job-x8c5i2` in `az-radar-vnet-rg`
- Location: Central US
- Recipe: Azure CLI App Service container image update
- Infrastructure, identities, RBAC, networking, and app settings: unchanged
- Rollback: switch each App Service to its previous blue/green image tag

Planned validation:

- [x] All validation checks pass
  - [x] Core validation: Azure CLI/authentication and application build; ARM validate/what-if are
        not applicable because this release changes only existing App Service image references.
  - [x] Docker build for API/UI and JobHost
  - [x] Azure Policy validation
- [x] `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore`
- [x] 35 focused Azure Updates, Microsoft Learn, and watchlist relevance tests
- [x] `npx tsc --noEmit`
- [x] `git diff --check`
- [x] Confirm Azure subscription, resource group location, and current image tags
- [x] Build and push opposite blue/green API and JobHost images
- [x] Update and restart both App Services
- [x] Verify `/api/azure-regions`, scoped watchlist persistence, UI availability, and JobHost health

## 7. Validation Proof

- 2026-09-11: full solution build succeeded with 0 warnings and 0 errors.
- 2026-09-11: focused test suite passed 35/35.
- 2026-09-11: frontend TypeScript check completed successfully.
- 2026-09-11: patch whitespace validation completed successfully.
- 2026-09-11: Azure CLI authentication confirmed subscription
  `MOHOSSA-M365CPI50986977` (`5e22addc-6168-4683-afd0-789a121ca5d3`); the user confirmed
  Central US and the existing `az-radar-vnet-rg` target.
- 2026-09-11: existing API and JobHost user-assigned managed identities confirmed attached.
- 2026-09-11: static Bicep review confirmed UAMI attachment in `infra/modules/web-app.bicep`,
  Cosmos data-plane contributor assignments in `infra/modules/cosmos-rbac.bicep`, and the
  existing Azure OpenAI user role design in `infra/modules/openai.bicep`.
- 2026-09-11: subscription policy assignments are existing Defender policies and do not conflict
  with an image-only update.
- 2026-09-11: standard Docker build was blocked by Docker-to-NuGet `NU1301`; locally restored
  Release publishes and the validated runtime-only Dockerfile produced API blue image
  `sha256:4f4d4fd541e80db72512a27bcb5f71071576cc12de16218e2696b19db90feba6` and JobHost green
  image `sha256:f4dfeac288ecec5380905cb7b2df26daf7b9f3b46d9931bfac440ba76996b57e`.

### Deployment result

- Docker Hub API blue digest:
  `sha256:f2922fb58d7d608a9063149f9bac6c5a76904ba4c863df0d89848a0d03342fc6`.
- Docker Hub JobHost green digest:
  `sha256:aef2f756109af8588d40a0fdc7f2506d0e35d86b0c779406819618c7571b70af`.
- `azr-api-x8c5i2` is running API blue with Always On enabled.
- `azr-job-x8c5i2` is running JobHost green with Always On enabled.
- Live API/UI smoke test returned HTTP 200, 58 Azure regions, five watchlist entries with the
  additive `regions` field, and explicit HTTP 400 rejection for an unknown region.
- Live runtime role verification confirmed Cosmos DB Built-in Data Contributor and Cognitive
  Services OpenAI User for the JobHost UAMI. App Service log download remains intentionally
  inaccessible from the public client because the SCM endpoint is IP restricted.

## Current release: Teams destination activation (2026-09-10)

The user authorized deployment of the pending API/UI changes and activation of
the registered Incidents Teams channel. This supersedes the earlier demo-time
API/UI restriction. Target: existing `azr-api-x8c5i2` in `az-radar-vnet-rg`,
Central US, subscription `5e22addc-6168-4683-afd0-789a121ca5d3`.

Deploy application code only: no infrastructure, identity, networking, or app
setting changes. Current API image is `moimhossain/az-radar-api:green`;
build and deploy `blue`, retaining `green` for rollback.

- [x] All validation checks pass for the Teams administration release
  - [x] Core validation: CLI/auth, targeted API/dispatch tests, frontend build;
        ARM validate/what-if are not applicable to this image-only release.
  - [x] Docker build for API/UI
  - [x] Azure Policy validation
  - [x] Existing runtime identity role verification
- [x] Deploy API/UI blue image and confirm updated destination metadata/UI
- [x] Enable only the registered Incidents destination for `ServiceIssue`
- [x] Publish synthetic incident and confirm durable Teams delivery receipt

### Teams administration release validation proof (2026-09-10, 16:35-16:44 CEST)

- `az account show`, `az webapp config show`, `az group show`: confirmed approved
  subscription, existing Central US target, and current green image.
- `dotnet test tests\AzRadar.Api.Tests --no-restore --verbosity quiet`: exit 0.
- `dotnet test src\Dispatching\AzRadar.Dispatching.Tests --no-restore --verbosity quiet`:
  4 passed.
- `dotnet publish src\AzRadar.Api\AzRadar.Api.csproj --no-restore -c Release -p:UseAppHost=false ...`:
  succeeded.
- `npm --prefix src\az-radar-ui run build`: TypeScript and Vite succeeded;
  existing bundle-size advisory only. Restored dependencies after missing Vite.
- Standard Docker build hit unavailable NuGet.org; the Microsoft public mirror
  lacked ResourceGraph. Used locally restored/published output and the existing
  `src\Dispatching\Dockerfile.prepublished` with `APP_DLL=AzRadar.Api.dll`.
  Copied the freshly built UI into `wwwroot` before containerization.
- Final blue image build succeeded:
  `sha256:e1d5ec7671e3740d6842388f7eb186201fe750960ed4424db3422496ae102016`.
- `az policy assignment list`: existing Defender assignments do not conflict
  with this image-only update; no resource/property changes beyond the image.
- Static existing Bicep role mapping and live role queries confirm runtime
  Cosmos Data Contributor plus Event Hubs Data Sender/Receiver. No RBAC changes.
- ARM/Bicep provisioning, quota changes and what-if: not applicable, since
  this release deploys only an image to an existing App Service.

Prior release evidence follows for historical context.

### Worker correction discovered during end-to-end activation

The first synthetic incident reached the outbox and Service Bus, but failed before
Teams delivery because default JSON deserialization did not restore the SDK's
camel-cased conversation routing fields. Use `ProtocolJsonSerializer.ToObject`
to pair with the gateway's `Conversation.ToJson()`. Reject incomplete references
as permanent failures instead of retrying eight times.

Scope expands only to an image update of the existing private dispatch worker:
`az-radar-dispatch-ay637nckh3ebc`, green to blue. No gateway, network, identity
or infrastructure changes; retain green as rollback.

- [x] Core validation: SDK round-trip regression tests, 7 dispatch tests passed
- [x] Worker Release publish and Docker build succeeded
- [x] Policy and static role configuration unchanged from validated deployment
- [x] Deploy corrected worker and publish a new synthetic incident

Validation proof (2026-09-10, 16:49 CEST):
`dotnet test src\Dispatching\AzRadar.Dispatching.Tests --verbosity quiet` passed
all 7 tests; Release publish and `Dockerfile.prepublished` build succeeded.
Worker blue image: `sha256:f6252a372e95743c1b32e3a2417d1f9df95be6183b19e0456a04fb45bb314d14`.
Live worker role queries confirm Service Bus Data Sender/Receiver at namespace
scope and Cosmos Data Contributor at account scope; both UAMIs remain attached.
The failed synthetic attempt is retained in the delivery audit, not deleted.

### Teams activation deployment result (2026-09-10, 16:52 CEST)

- API/UI: `azr-api-x8c5i2`, blue image, registry digest
  `sha256:a4fff51e871729ae078d5ff664b013476c715f396037d608c03f02508b710e2e`.
  Health endpoint returned healthy and the new `index-Dn7AtHJA.js` UI bundle is served.
- Worker: `az-radar-dispatch-ay637nckh3ebc`, blue image, registry digest
  `sha256:e92853ac2e82e900019e5dcd3ae0433738081b5275d3efc8d170f2f86a14f798`.
  Private ingress and Always On retained.
- Registered Incidents channel enabled with exactly `ServiceIssue`. Existing
  conversation and tenant/channel identifiers were preserved.
- Synthetic event `TEST-20260910145218` reached Teams at 16:52:33 CEST.
  Delivery intent `5393322113d8047e6f43ad856f16211e3fe1845a52240e01a92c6d77c0af1a6e`
  is `delivered`, attempt count 1, no errors, Teams activity ID `1789051953441`.
- Previous green API and worker images remain available for rollback.
- Administration UI: https://azr-api-x8c5i2.azurewebsites.net/service-health

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

---

## 14. CloudLens Teams Dispatch Phase

**Goal:** Deploy the isolated CloudLens Teams dispatch boundary through Azure
Bot Service, a public minimal Bot Gateway, private Service Bus, and a private
dispatch worker. Generate the tenant-uploadable Teams app package and stop for
the user's Teams admin installation step.

**Deployment boundary:** This phase creates only new dispatch resources and
images. It does not modify, restart, or switch the existing production API/UI
App Service. The related API/UI administration changes remain code-only until
a separately authorized release.

### Architecture

| Component | Azure service | Configuration |
|-----------|---------------|---------------|
| Bot registration | Azure Bot Service | F0, Teams channel, UAMI-backed, CloudLens display identity |
| Bot callback | Linux App Service | Dedicated B1 plan, public HTTPS `/api/messages`, Bot JWT validation |
| Durable delivery | Service Bus | Premium namespace, private endpoint, duplicate detection |
| Dispatch consumer | Linux App Service | Dedicated B1 plan, VNet integrated, Always On |
| Runtime identities | User-assigned managed identities | Separate gateway and worker identities; bot UAMI also attached to worker for proactive sends |
| Conversation state | Existing Cosmos account | Two `/id` containers for conversation references and delivery attempts |
| Teams package | Repository artifact | Manifest and generated PNG icons branded CloudLens |

### Provisioning Limit Checklist

Quota CLI returned no provider-specific records for Microsoft.Web,
Microsoft.ServiceBus, or Microsoft.Network in Central US. Azure Resource Graph
counts and documented fixed limits are used for these unsupported quota
surfaces.

| Resource Type | Number to Deploy | Total After Deployment | Limit/Quota | Notes |
|---------------|------------------|------------------------|-------------|-------|
| `Microsoft.Web/serverfarms` | 2 | 4 | 100 per resource group in the documented App Service limit surface | Current Central US count: 2 |
| `Microsoft.Web/sites` | 2 | 4 | App Service subscription limits remain well above the pilot count | Current Central US count: 2 |
| `Microsoft.ServiceBus/namespaces` | 1 | 1 | 100 namespaces per subscription | Current Central US count: 0 |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | 2 | 8 | 200 per subscription | Current Central US count: 6 |
| `Microsoft.BotService/botServices` | 1 | 1 | Pilot remains below documented subscription limits | Current count: 0 |
| `Microsoft.Network/privateEndpoints` | 1 | 4 | 1,000 per VNet | Current Central US count: 3 |
| Cosmos DB serverless containers | 2 | 17 | 25 per serverless account | Current target account count: 15 |

**Status:** All planned dispatch resources are within the applicable limits.

### Execution Checklist

- [x] Implement dispatch contracts and Cosmos repository
- [x] Implement transactional outbox publisher
- [x] Implement Service Bus Teams delivery consumer
- [x] Implement proactive Teams messaging and delivery audit
- [x] Implement authenticated Bot Gateway and channel registration
- [x] Add CloudLens Teams manifest and packaging script
- [x] Add standalone Bicep and dispatch Dockerfiles
- [x] Add administration API/UI support for discovered bot destinations
- [x] Build solution, type-check UI, run dispatch tests, compile Bicep
- [x] Confirm Central US subscription context from the approved pilot plan
- [x] Check dispatch resource provisioning limits
- [x] Mark dispatch phase Ready for Validation
- [x] Invoke `azure-validate`
  - [x] 1. Core Validation (CLI, authentication, Bicep build, ARM validation, and what-if)
  - [x] 2. Docker Build (Bot Gateway and dispatch worker)
  - [x] 3. Azure Policy Validation
- [x] Build and push initial dispatch images
- [x] Invoke `azure-deploy`
- [x] Deploy and verify new dispatch resources
- [x] Generate CloudLens Teams app package using the deployed Bot client ID
- [x] Hand the package to the user for Teams admin portal upload and installation

### Dispatch Role Assignment Verification

- Status: Verified
- Identities checked: `az-radar-bot-gateway-uami`,
  `az-radar-dispatch-worker-uami`
- Roles confirmed: gateway UAMI receives Cosmos DB Built-in Data Contributor;
  worker UAMI receives Cosmos DB Built-in Data Contributor, Azure Service Bus
  Data Sender, and Azure Service Bus Data Receiver at resource scope
- Bot Connector authentication uses the gateway UAMI client ID through Azure
  Bot Service and the Agents SDK; no credential or Azure data-plane role is
  used for that OAuth flow
- Issues: None

### Dispatch Validation Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Azure preflight | `validate-deployment.ps1 -Scope group -ResourceGroup az-radar-vnet-rg -Template .\src\Dispatching\infra\main.bicep -Parameters .\src\Dispatching\infra\main.bicepparam -Subscription 5e22addc-6168-4683-afd0-789a121ca5d3` | Pass: authenticated, Bicep compiled, ARM validation passed, what-if reported 20 creates, 0 modifies, 0 deletes | 2026-09-10T15:51:19+02:00 |
| Solution build | `dotnet build AzRadar.slnx -p:Platform="Any CPU"` | Pass: 0 warnings, 0 errors | 2026-09-10T15:51:19+02:00 |
| Dispatch tests | `dotnet test src\Dispatching\AzRadar.Dispatching.Tests\AzRadar.Dispatching.Tests.csproj --no-restore` | Pass: 4 tests | 2026-09-10T15:51:19+02:00 |
| UI type check | `npx tsc --noEmit` from `src\az-radar-ui` | Pass | 2026-09-10T15:51:19+02:00 |
| Bot Gateway image | Local Release publish plus `Dockerfile.prepublished` | Pass: `sha256:0c2e21282b7185aa7905e910a22ac66f0f53e820ac893d23ab178ad38c906562` | 2026-09-10T15:51:19+02:00 |
| Dispatch worker image | Local Release publish plus `Dockerfile.prepublished` | Pass: `sha256:e334d87a44bbde737c87a98f7019678599aa2a7ccc222c1a6e95379eee2f2544` | 2026-09-10T15:51:19+02:00 |
| Teams package | `package.ps1 -BotClientId 11111111-1111-1111-1111-111111111111` | Pass: manifest, color icon, and outline icon packaged | 2026-09-10T15:51:19+02:00 |
| Azure policy review | `az policy assignment list --subscription 5e22addc-6168-4683-afd0-789a121ca5d3 --disable-scope-strict-match` | Pass: assigned Defender initiatives do not deny planned resource types | 2026-09-10T15:51:19+02:00 |

### Dispatch Deployment Verification

- Deployment: `cloudlens-teams-dispatch-20260910`
- State: Succeeded
- CloudLens Bot client ID: `8df1e881-4682-4919-88f0-9a5035a572d1`
- Bot Gateway: `https://az-radar-bot-ay637nckh3ebc.azurewebsites.net`
- Bot Gateway health: running
- Azure Bot Teams channel: enabled and provisioned
- Service Bus namespace: `az-radar-dispatch-ay637nckh3ebc`
- Topic/subscription: `service-health-delivery` / `teams-realtime`
- Dispatch worker image: `moimhossain/az-radar-dispatch-worker:green`
- Dispatch worker ingress: disabled
- Worker liveness: Service Bus reported active connections, opened connections,
  successful requests, and incoming requests after the green deployment
- Cosmos containers: `teams-conversation-references`,
  `teams-delivery-attempts`, both partitioned by `/id`
- Live RBAC: worker has Azure Service Bus Data Sender and Data Receiver;
  gateway and worker have Cosmos DB Built-in Data Contributor
- Teams package:
  `src\Dispatching\TeamsApp\artifacts\CloudLens-Teams-App.zip`
- Existing production API/UI App Service: unchanged
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
