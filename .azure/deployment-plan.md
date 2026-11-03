# Azure Deployment Plan

> **Status:** Ready for Validation - strict query response contract correction (2026-11-03)

## AI settings rollout - initial plan

Scope: update the provider PRD for fine-grained PAT onboarding, implement
Settings -> AI settings with Foundry/Copilot prompt tests and explicit activation,
preserve Foundry/UAMI by default, and use Key Vault-backed PAT storage for
background jobs. No OAuth app or callback URI in this initial version.

Target: existing API/UI and JobHost in `az-radar-vnet-rg`, subscription
`5e22addc-6168-4683-afd0-789a121ca5d3`; confirm existing region and Azure context.
Recipe: existing AZCLI/container rollout with alternate blue/green tags.
Preserve private registry/network configuration, scheduling, and unrelated apps.

### Requirements and components

Modify the existing .NET 8 Shared/API/JobHost and React FluentUI application.
Initial release uses a user-owned fine-grained PAT with the Copilot Requests
account permission. No GitHub App, OAuth callback, or organization-app preview.
PATs are write-only, accessed server-side through managed identity and Key Vault.
The UI separates test from activation and keeps Foundry as the existing default.

Extract a provider-neutral completion transport beneath existing analysis
prompts. Use a pinned, organization-approved SDK/runtime and inference-only
sessions with no tools. Persist provider revisions without credentials and
preserve scheduled-chain identity, success-only post-jobs, and Service Health
non-LLM fallback behavior. Expired/revoked PATs fail visibly; never fall back
across providers automatically.

### Azure context and infrastructure inventory

Read-only Azure verification on 2026-11-03 found subscription
`MOHOSSA-M365CPI50986977` (`5e22addc-6168-4683-afd0-789a121ca5d3`),
resource group `az-radar-vnet-rg`, region `centralus`.

| Resource | Planned new instances | Approach / capacity |
|---|---|---|
| App Services | 0 | Update existing API `azr-api-x8c5i2` and worker `azr-job-x8c5i2`; retain plans. |
| Container registry | 0 | Existing private ACR; alternate current image tags and restore network/export restrictions after builds. |
| Key Vault | 0 | Reuse `az-radar-wiki-ay637nckh3` with separate AI secret names and existing managed identities; verify API write and worker read roles. |
| Cosmos DB accounts/containers | 0 | Existing app-config/job/schedule storage; no credentials in records. |
| Regional compute/network capacity | 0 | No regional capacity increase; provisioning quota increases are not required. Benchmark runtime memory before rollout. |

### Management access - explicitly accepted initial-release limitation

ARM `authsettingsV2` read confirmed App Service authentication is disabled on
the public API. The user explicitly approved unauthenticated initial-release
management on 2026-11-03, with write-only PAT submission and no token retrieval
route. Customer deployments will restrict/disable dashboard access through
network settings after setup. No Entra/GitHub App registration is authorized.
Use bounded global rate limits, request-size limits, redacted errors, and Key
Vault storage. Write-only does not prevent unauthorized changes or use of
saved credentials for billable tests: this remains an accepted demo risk, not
a production security guarantee.

### Delivery and verification

1. Obtain approval of this plan, existing Azure context, and management boundary.
2. Update provider PRD to the selected PAT-first scope and implement backend/UI.
3. Validate backend build and focused tests, existing scheduling regressions,
   frontend type-check/build/lint, secret-redaction/access-control behavior,
   SDK/runtime packaging, bounded inference/cancellation, and Foundry regression.
4. Invoke azure-validate with fresh proof; only then invoke azure-deploy.
5. Deploy worker/API compatible images via existing AZCLI blue/green recipe;
   preserve Always On and private registry/network configuration.
6. Smoke-test live UI/API, Foundry inference, settings persistence, validation
   boundaries, and worker/provider integration. Anonymous management is explicitly
   accepted for this demo. Live successful Copilot inference requires a user-provided
   eligible PAT entered directly in the UI, not chat or logs. Do not borrow the
   operator's ambient GitHub credentials.

Roll back only the scoped images/settings. Leave Foundry selected until the
user explicitly activates a successfully tested Copilot connection. No new
Foundry resources, Azure API keys, or unrelated dispatch changes.

### Fresh implementation proof

### Section 7: AI settings validation proof

- [x] All validation checks pass
  - [x] Core CLI/auth/Bicep/ARM validation and scoped what-if
  - [x] API and JobHost container builds with verified native assets and notices
  - [x] Azure Policy validation
  - [x] Static role mapping verification

Fresh Azure validation on 2026-11-03:

- Final corrected images: run `cju` API green Succeeded,
  `sha256:31f1788cccb01e0a997bd53735ba805b803a622c9da03aa390e202336c3d2ac4`;
  run `cjv` JobHost blue Succeeded,
  `sha256:1f51f45b1e13e40167bdb6c50d72c081ea3168393e314af3566040521b005465`.
  Registry restrictions restored after both completed.
- After the Foundry compatibility correction: shared tests 208 passed; solution
  build zero warnings/errors; scoped infrastructure validation rerun OVERALL PASS.
  Existing static role mappings are unchanged; live resource-scoped Key Vault,
  Cosmos, AcrPull, and Cognitive Services OpenAI User roles were also confirmed.
- Live pre-correction checks already verified 400 input validation, 403 foreign
  origin rejection, 413 request-size limit, blocked generic config access, and
  sanitized 502 for a deliberately invalid PAT. Container logs confirmed the
  Linux Copilot runtime started; successful Copilot authentication remains untested.
- Ran the skill's `validate-deployment.ps1 -Scope group -ResourceGroup
  az-radar-vnet-rg -Template infra\ai-settings-rollout.bicep -Parameters
  <session-files>\ai-settings.parameters.json -Subscription
  5e22addc-6168-4683-afd0-789a121ca5d3`.
- Scoped validation helper: OVERALL PASS (CLI/auth, Bicep build, ARM validation,
  what-if). Structured what-if changes only JobHost app settings; every other
  resource is Ignore. No resource creates or deletes.
- Applicable policy assignment query returned no assignments.
- ACR run `cjs`: API blue Succeeded,
  `sha256:0fb7acc470734d1ac277e241b12bd2939e19678728506e0679e1624e659799c0`.
- ACR run `cjt`: JobHost green Succeeded,
  `sha256:dbf6b9fdf04d2ddd168a2694f4ef6c34d2a5ac7718360cd3f562ebba442e8691`.
- Native Linux assets and CLI/SDK license notices verified in both image builds.
  Registry restored to public Disabled/default Deny/export disabled/admin false.
- Earlier run `cjr` failed because queue-ID capture restored the firewall too
  soon. Corrected the build orchestration; both replacement builds succeeded.
- The first scoped deployment failed before either image was changed: ARM
  rejected `listApplicationSettings` on the site resource, despite preflight
  validation passing. Listing the child directly creates an ARM self-dependency.
  Corrected the scoped template to accept a secure snapshot of current settings,
  captured immediately before updating, and merge only the vault URI.
  No existing setting values are written to repository files or deployment logs.
  Corrected-template validation rerun: OVERALL PASS (Bicep/ARM/what-if); the
  secure snapshot contains all 22 existing settings. Image builds and static
  role mappings are unchanged and remain verified.
- Build commands: `az acr build --registry azrxon32oitl5v66 --file
  Dockerfile.api --image az-radar-api:blue --no-wait --no-logs -o json .`
  and the corresponding `Dockerfile.jobhost` / `az-radar-jobhost:green`.
  Queried each exact run ID with `az acr task show-run` until Succeeded.

### Role assignment verification

Status: Verified (static code review). This scoped template adds no role grants.
Existing shared API/JobHost UAMI uses resource-scoped Key Vault Secrets Officer
for secret version writes/reads, Cosmos DB Built-in Data Contributor for profiles,
jobs and schedules, and AcrPull for images. Reviewed the mappings in
`src\Dispatching\infra\main.bicep`, `infra\modules\cosmos-rbac.bicep`, and
`infra\modules\container-registry.bicep`; `infra\modules\web-app.bicep` retains
managed identity image pulls and Always On. Live role checks follow deployment.

The scoped `infra\ai-settings-rollout.bicep` only merges the existing vault URI
into JobHost app settings. It preserves all other settings and does not redeploy
the main infrastructure or the demo dispatch stack.

- Updated provider PRD with explicitly approved PAT-first/write-only/no-auth scope.
- Added provider settings/test UI, dedicated rate-limited/size-bounded API,
  isolated SDK runtime, versioned Key Vault credentials, conditional Foundry
  resolution, immutable Cosmos profiles, and nonsecret job/chain snapshots.
- Shared tests: 208 passed (14 new provider/profile-pinning/Foundry compatibility cases).
- Solution build: passed, zero warnings/errors; local SDK is preview but target
  runtime remains .NET 8.
- API test project contains no discoverable tests; do not report it as passing tests.
- Frontend production build, type-check, and changed-file ESLint passed.
- Actual .NET 8 SDK/native runtime startup passed with an isolated home and
  deliberately invalid test token; no inference or ambient user credentials.
- NuGet restore succeeded for pinned SDK 1.0.22, CLI/runtime 1.0.98 (GitHub
  releases October 14). Native assets copied into real application publish output.
- Dockerfiles assert Linux native assets and retain CLI/SDK license notices.
- Existing API/JobHost both use UAMI principal `1d009d7d-59a6-489e-929d-2b1a6fe6f97b`,
  already Key Vault Secrets Officer on the reused vault. No new role grant needed.
- JobHost currently lacks vault URI app setting: wire the existing URI during
  scoped rollout. Canonical `infra\main.bicep` already supplies it to both apps.
- Existing images: API green (deploy blue), JobHost blue (deploy green).
- Both Always On true. Registry public Disabled/default Deny/export disabled.
- Functional scope: automated provider/service and UI compilation checks complete;
  Azure-dependent UI/inference checks will run against the deployed containers.
  Live successful Copilot inference remains dependent on user-provided PAT.

### Current deployment and live verification

The corrected API green (`cju`) and JobHost blue (`cjv`) images are deployed.
Both Always On settings and UAMI private image pulls remain enabled. The worker
remains Running with public networking Disabled; ACR is public Disabled/default
Deny/export disabled/admin false. All 22 original worker app settings were
preserved; only the existing private vault URI was added.

Live role verification passed: shared API/JobHost principal has resource-scoped
Key Vault Secrets Officer, AcrPull, Cosmos DB Built-in Data Contributor, and
Cognitive Services OpenAI User. Queried `az role assignment list --scope
<resource-id> --assignee-object-id <principal-id>` and the Cosmos SQL role list.

Completed checks:

- Foundry real API inference, verified Save / Apply and reload, test-without-save,
  stale ETag 412, 6/minute limiter 429, and passive health endpoint.
- Rendered UI: Foundry default and Test button, Copilot selector, empty write-only
  password field, permission hints, and Save disabled when no PAT is configured.
- Input validation 400, foreign-origin 403, body-size 413, reserved generic config
  rejection, and invalid-token 502 without leaking credentials or SDK exceptions.
- One-time Foundry chain `b68daa5e517f566a7c39ccf9ea99da2fe9e7d66d73a98f963db7e4ff342f5975`:
  primary and post-job succeeded with actual LLM query-generation diagnostics,
  the same saved profile, and correct parent linkage. No duplicate occurrence.
- Real Copilot API test: GPT-5.4 returned the requested marker, HTTP 200,
  14,215 ms inference latency. This initial test did not save the supplied PAT.
- The user then explicitly approved Key Vault storage, one scheduled Copilot
  test, and leaving Copilot active. Save / Apply succeeded; a second inference
  succeeded with no PAT in the request, proving server-side vault reuse.
- Rendered UI reloaded the saved Copilot selection, showed only the configured
  marker with an empty password input, and completed a real Copilot inference.

Pending final check: scheduled Copilot Blast Radius job in schedule
`6307bbc5e70dcb46c487dc80b295162e3bc52afe48787d389d878427ba2ab8fe`.
It uses the saved immutable Copilot profile and should complete without a browser
session or submitted PAT. Retain the diagnostic job history, remove only the
temporary test schedule definitions after completion, and keep Copilot active
as explicitly requested.

The first Copilot scan checked all 73 items but finished partial: 34 ARG queries
succeeded, 38 items were skipped, and one item remained unresolved. Six initial
ARG requests contained explanatory prose; retries repaired five, while prose
around a SKIP response caused the remaining partial result. Authentication,
Key Vault reuse, pinned execution and background inference worked.

Corrected query generation to request and validate a JSON envelope containing an
executable `query` or explicit null; prose/invalid schema fails visibly instead
of being executed or silently treated as a skip. The retry prompt follows this
same response contract. Added six targeted regression cases; shared suite now
214 passed. The user explicitly approved deploying this correction and running
one additional scheduled Copilot scan, leaving Copilot active. Build opposite
tags API blue / JobHost green and complete fresh validation before rollout.

Live page: https://azr-api-x8c5i2.azurewebsites.net/ai-settings

Do not roll back to the first AI images (`cjs`/`cjt`), which contain the Foundry
output-cap regression. If a rollback is required, drain active jobs and use the
known-good pre-AI image digests recorded in the previous schedules rollout,
rather than assuming an overwritten color tag is a known-good version.

---

## Previous schedules rollout

> **Previous status:** Deployed and verified - JobHost schedules rollout (2026-11-03)

## Current rollout

The user approved implementing and deploying durable JobHost scheduling to the
existing Central US API/UI and JobHost in `az-radar-vnet-rg`, subscription
`5e22addc-6168-4683-afd0-789a121ca5d3`. No Function App, hosting plan, or dispatch
stack changes. Update `prds\job-schedules.md`, implement schedules and recovery,
validate backend/frontend, then use opposite blue/green tags in the existing
private ACR. Deploy compatible JobHost before API. Preserve managed identity,
private registry access, and Always On. Test controlled one-time runs and chaining.

Recipe: AZCLI / existing container deployment. The historical deployment evidence
below applies only to the earlier ACR rollout, not this change.

### Section 7: Current validation proof (schedules)

- [x] All validation checks pass
  - [x] Core CLI/auth/Bicep/ARM validation and isolated what-if
  - [x] Container builds (ACR Tasks; local Docker daemon is unavailable)
  - [x] Azure Policy validation
  - [x] Backend build/tests and frontend build/type-check/targeted lint
  - [x] Static and live runtime role review

Fresh checks on 2026-11-03:

- `dotnet test tests\AzRadar.Shared.Tests --no-restore -p:Platform="Any CPU"`:
  194 passed (including 21 scheduling cases).
- `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore`: passed,
  zero warnings/errors.
- Frontend `npm run build`, `npx tsc --noEmit`: passed. Existing bundle-size
  advisory remains.
- Targeted ESLint on all changed UI: passed after correcting the initial-load effect.
- `az bicep build` and `az deployment group validate` on
  `infra\schedules-rollout.bicep`: passed.
- Isolated what-if: Create schedule-control only; all unrelated resources Ignore,
  no modifications/deletions.
- Confirmed existing UAMI Cosmos Data Contributor role at account scope.
- Live API image blue -> deploy green; JobHost green -> deploy blue.
- Both live apps have Always On disabled; enable during the scoped rollout.
- Private ACR final state must remain public Disabled/default Deny/export disabled.
- `.dockerignore` excludes local operator files and deployment state from ACR context.
- Canonical AZCLI validation helper: OVERALL PASS. Direct structured what-if
  confirms exactly one new container (the helper's text count duplicates Create).
- Static role review: existing cosmos-rbac module grants account-scoped Cosmos
  Data Contributor; no new roles are needed for the new container. Live API and
  JobHost UAMI `az-radar-uami` has this role and registry-scoped AcrPull.
- Policy assignments retrieved with the authenticated target-subscription Azure CLI:
  no applicable assignments returned. Azure MCP policy retrieval used a different
  tenant identity and returned 403; CLI/ARM validation succeeded in the target tenant.
- Fresh ACR container builds succeeded: API green `cjp`, JobHost blue `cjq`.
  Initial streamed-log attempt `cjn` failed its push after a Windows CLI Unicode
  output error triggered registry restoration; rebuilt successfully using no-logs.
- Registry verified restored: public Disabled, default Deny, export disabled,
  admin disabled. Existing dependency audit reports 22 vulnerabilities (14 moderate,
  8 high); no dependencies changed in this rollout.

No new deployment is authorized by the historical validation proof.

### Current deployment evidence

- Isolated deployment `schedules-20261103`: Succeeded; only schedule-control added.
- JobHost blue deployed before API green. Existing images for rollback:
  JobHost green, API blue (do not overwrite these during this rollout).
- API image digest: `sha256:7f59fe09375be76aa076f90cf7788f3342d53d132205237e993544e472016495`.
- JobHost image digest: `sha256:4e34d57a99e4c9d9edf27a36b33feb6620a0bbeac94059930628d05681d30f13`.
- Both apps verified Always On true and ACR managed-identity credentials true.
- Registry verified private after deployment: Disabled/Deny/export disabled/admin false.
- API health returns supportsSchedules=true; `/schedules` returns 200, and
  deployed asset `index-DX-CU36z.js` contains the new editor and post-job controls.
- Live RBAC verification passed: runtime UAMI has registry AcrPull and account-
  scoped Cosmos Data Contributor.
- JobHost public ingress remains Disabled. Direct workstation health/SCM probes
  correctly return 403; no network restrictions were weakened. Actual scheduler
  execution is verified through the durable run/job records, not an external
  health endpoint response.
- Live preview, invalid-zone rejection, creation idempotency/conflict, ETag-required
  edits, stale-ETag rejection, pause/edit/resume, list/history, and overlap rejection
  passed. Europe/Amsterdam spring-gap and autumn-fold previews passed on Linux.
- Controlled smoke schedule persisted; JobHost stopped before its 08:21 UTC due
  time and started after it. Exactly one occurrence was admitted at 08:23:03 UTC
  after cold start; the primary was claimed once.
- Run `4df455b71961904d983f115e16da47d11e6ad4383aceb7958287609ee84b5038`:
  succeeded at 08:45:28 UTC. Primary job
  `7b95c761007184a0cdfb621069e95487ed7255a1d1d72d4150a20d268cf2b64e`
  checked 10,016 Azure Updates, retained 11 new items, succeeded at 08:41:55 UTC,
  and persisted one completion-barrier page.
- Post-job `e85386820fc519cc8946ecdebeea6be7decd22bc1d1a3f9e8ac9dd8fdc7ad7f7`
  started at 08:43:29 UTC (after primary success), checked 73 retirements, stored
  seven Blast Radius summaries, and succeeded at 08:45:16 UTC. One execution
  attempt per job; parent/run correlations correct; no premature post dispatch.
- Completion-barrier application succeeded in the live worker. Fresh-process
  handoff between primary/post was not separately fault-injected.
- Scheduled-job deletion guard returned 409. Final one-time definition had no
  active run or next occurrence. Smoke schedule soft-deleted after completion;
  diagnostic job/run history retained. Visible schedule list is empty; no
  unintended recurring smoke work remains.
- Final API health passed. Multi-replica load, mid-handler crash recovery, forced
  upstream failure, independent alerting and coordinated retention cleanup remain
  outside this successful smoke test; relevant negative transitions have unit tests.

---

Generated: 2026-09-24T08:20:12+02:00

---

## 1. Project Overview

**Goal:** Replace public Docker Hub image distribution with a privately networked Azure Container
Registry, configure AzRadar App Services to pull through the VNet with managed identity, publish
new images, deploy them, and verify the live test environment.

**Path:** Add Components to an existing deployment.

**PRD:** `prds/private-azure-container-registry.md`

---

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Development/test |
| Scale | Small |
| Budget | Security-first, cost-aware |
| Subscription | `MOHOSSA-M365CPI50986977` (`5e22addc-6168-4683-afd0-789a121ca5d3`) |
| Resource group | `az-radar-vnet-rg` |
| Location | Central US (`centralus`) |

The subscription, resource group, and location were confirmed from the user-provided Azure portal
URI and the authorized Azure CLI context.

---

## 3. Current State and Components Detected

The live resource group contains four running Linux container App Services:

| Component | Technology | Live App Service | Current image |
|-----------|------------|------------------|---------------|
| API + React UI | .NET 8 + React | `azr-api-x8c5i2` | `moimhossain/az-radar-api:green` |
| Crawl JobHost | .NET 8 worker | `azr-job-x8c5i2` | `moimhossain/az-radar-jobhost:blue` |
| CloudLens bot gateway | .NET 8 | `az-radar-bot-ay637nckh3ebc` | `moimhossain/az-radar-bot-gateway:green` |
| Service Health dispatch worker | .NET 8 worker | `az-radar-dispatch-ay637nckh3ebc` | `moimhossain/az-radar-dispatch-worker:green` |

Repository analysis confirms that:

- `infra/main.bicep` and `infra/main.bicepparam` default to Docker Hub images.
- `infra/modules/web-app.bicep` explicitly configures public unauthenticated image pulls.
- `src/Dispatching/infra/main.bicep` also points its App Services at Docker Hub images.
- No ACR resource, ACR private endpoint, `privatelink.azurecr.io` zone, or `AcrPull`
  role assignment exists in the repository.
- No ACR exists in `az-radar-vnet-rg`.
- The target VNet already has a `/24` private-endpoint subnet and delegated API/worker App Service
  integration subnets.

---

## 4. Recipe Selection

**Selected:** Standalone Bicep with Azure CLI.

**Rationale:** The repository already uses modular resource-group-scope Bicep and an Azure CLI
deployment script. The feature is an additive infrastructure change and does not require an AZD
conversion.

---

## 5. Architecture

**Stack:** Linux containers on Azure App Service with a private Premium ACR.

### Service Mapping

| Component | Azure Service | SKU |
|-----------|---------------|-----|
| Private container registry | Azure Container Registry | Premium |
| Registry network access | Azure Private Endpoint | Existing endpoint subnet |
| Registry DNS | Azure Private DNS | `privatelink.azurecr.io` |
| Runtime image access | Azure RBAC | `AcrPull` |
| API/UI runtime | Azure App Service | Existing B1 Linux plan |
| JobHost runtime | Azure App Service | Existing B1 Linux plan |
| Bot gateway runtime | Azure App Service | Existing B1 Linux plan |
| Dispatch worker runtime | Azure App Service | Existing B1 Linux plan |

### Security and Network Design

- ACR administrator credentials and anonymous pull remain disabled.
- ACR public network access is disabled in the final state.
- The ACR private endpoint is created in `snet-private-endpoints`.
- `privatelink.azurecr.io` is linked to `az-radar-vnet`.
- Runtime identities receive only `AcrPull`, scoped to ACR.
- App Services use user-assigned managed identity registry authentication.
- App Service image pull over VNet is explicitly enabled.
- Build and push use the authorized Azure CLI identity; no registry password is stored.

### Rollout Sequence

1. Provision ACR, private networking, DNS, and RBAC while retaining Docker Hub image references.
2. Build and publish uniquely tagged images to ACR.
3. Verify repository manifests and `AcrPull` propagation.
4. Switch App Services to ACR image references and managed-identity pulls.
5. Verify all public endpoints and continuous workers.

---

## 6. Provisioning Limit Checklist

The Microsoft.Quota API returned `BadRequest` for `Microsoft.ContainerRegistry`, so unsupported
resource types use live Azure CLI counts plus Microsoft Learn service-limit documentation.

| Resource Type | Number to Deploy | Total After Deployment | Limit/Quota | Notes |
|---------------|------------------|------------------------|-------------|-------|
| `Microsoft.ContainerRegistry/registries` | 1 | 2 in subscription | 100 registries per subscription | Live count: 1; quota API unsupported; official Azure limits |
| `Microsoft.Network/privateEndpoints` | 1 | 8 in subscription/target estate | 1,000 per VNet | Live subscription count: 7; official Azure limits |
| `Microsoft.Network/privateDnsZones` | 1 | 7 in subscription | 1,000 per subscription | Live subscription count: 6; official Azure limits |
| `Microsoft.Authorization/roleAssignments` | 3 | 63 in subscription | 4,000 per subscription | Live count: 60; one assignment per unique runtime pull identity |

**Status:** All planned resources are within documented limits.

---

## 7. Execution Checklist

### Phase 1: Planning

- [x] Analyze workspace
- [x] Gather requirements
- [x] Confirm subscription, resource group, and location
- [x] Inspect the live deployment
- [x] Verify the current image sources
- [x] Confirm no ACR exists in the target resource group
- [x] Prepare and validate the resource inventory
- [x] Scan the codebase
- [x] Select the Bicep recipe
- [x] Plan architecture, rollout, and rollback
- [x] Create the PRD
- [x] User approved this plan

### Phase 2: Execution

- [x] Research exact Bicep resource schemas and App Service ACR properties
- [x] Add the ACR/private endpoint/private DNS/RBAC module
- [x] Add an isolated live-environment ACR rollout template
- [x] Wire ACR through `infra/main.bicep`
- [x] Configure API and JobHost managed-identity pulls over VNet
- [x] Configure dispatch worker and optional bot gateway pulls over VNet
- [x] Update Bicep parameters, deployment scripts, and infrastructure documentation
- [x] Build and run the smallest local verification suites
- [x] Confirm the isolated Azure what-if contains no deletions
- [x] Update plan status to `Ready for Validation`

### Phase 3: Validation

- [x] Invoke `azure-validate`
- [x] All validation checks pass
  - [x] Core validation (Azure CLI, authentication, Bicep build, ARM validation, and what-if)
  - [x] Bicep linting
  - [x] Azure Policy validation
  - [x] Isolated rollout contains no deletions
  - [x] .NET solution build
  - [x] Shared unit tests
  - [x] Frontend TypeScript type-check
- [x] Build all Bicep templates
- [x] Run Bicep deployment validation against `az-radar-vnet-rg`
- [x] Validate .NET solution/tests and frontend type-check
- [x] Confirm the deployment change set is additive and expected
- [x] Update plan status to `Validated`
- [x] Record validation proof below

### Phase 4: Deployment

- [x] Invoke `azure-deploy`
- [x] Provision ACR/private networking/RBAC
- [x] Build and push/import all required images
- [x] Verify image manifests
- [x] Deploy ACR image references
- [x] Verify managed-identity `AcrPull` role assignments
- [x] Verify private DNS and disabled public registry access
- [x] Verify live API/UI endpoint
- [x] Verify JobHost and dispatch worker liveness
- [x] Verify bot gateway when deployed
- [x] Update plan status to `Deployed`

---

## 8. Validation Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Canonical Bicep validation | `validate-deployment.ps1 -Scope group -ResourceGroup az-radar-vnet-rg -Template .\infra\acr-rollout.bicep -Subscription 5e22addc-6168-4683-afd0-789a121ca5d3` | Pass; CLI/auth/build/ARM validation/what-if, Create 6, Modify 0, Delete 0 | 2026-09-24T08:56:49+02:00 |
| Bicep lint | `az bicep lint --file` for rollout, platform, and dispatch templates | Pass | 2026-09-24T08:56:49+02:00 |
| Azure Policy | `policy_assignment_list` at the target resource-group scope | Pass; assignments reviewed | 2026-09-24T08:56:49+02:00 |
| Static RBAC | Review `AcrPull` assignments in `container-registry.bicep` and `acr-rollout.bicep` | Pass; least-privilege ACR scope for three runtime identities | 2026-09-24T08:56:49+02:00 |
| Solution build | `dotnet build AzRadar.slnx -p:Platform="Any CPU"` | Pass; 0 warnings, 0 errors | 2026-09-24T08:56:49+02:00 |
| Unit tests | `dotnet test tests/AzRadar.Shared.Tests --no-build` | Pass; 102/102 | 2026-09-24T08:56:49+02:00 |
| Frontend type-check | `cd src\az-radar-ui; npx tsc --noEmit` | Pass | 2026-09-24T08:56:49+02:00 |
| PowerShell parsing | Parse deployment scripts with `System.Management.Automation.Language.Parser` | Pass | 2026-09-24T08:56:49+02:00 |

**Validated by:** azure-validate skill

**Validation timestamp:** 2026-09-24T08:56:49+02:00

---

## 9. Role Assignment Verification

- **Status:** Verified
- **Identities checked:** `az-radar-uami`, `az-radar-dispatch-worker-uami`,
  `az-radar-bot-gateway-uami`
- **Role confirmed:** Azure Container Registry `AcrPull`
  (`7f951dda-4ed3-4680-a7ca-43fe172d538d`)
- **Scope:** The new ACR resource only
- **Operations covered:** Runtime manifest and layer pulls for API, JobHost, dispatch worker, and
  bot gateway
- **Write access:** Not granted to runtime identities; image publication uses the authenticated
  Azure CLI operator through ACR Tasks
- **Issues:** None

---

## 10. Files to Generate or Modify

| File | Purpose | Status |
|------|---------|--------|
| `prds/private-azure-container-registry.md` | Product and technical requirements | Complete |
| `.azure/deployment-plan.md` | Deployment source of truth | Complete |
| `infra/modules/container-registry.bicep` | ACR, private endpoint, DNS, and RBAC | Complete |
| `infra/acr-rollout.bicep` | Isolated existing-environment ACR rollout | Complete |
| `infra/modules/web-app.bicep` | Managed-identity ACR pull over VNet | Complete |
| `infra/main.bicep` | Registry and image wiring | Complete |
| `infra/main.bicepparam` | ACR-based image parameters | Complete |
| `src/Dispatching/infra/main.bicep` | Dispatch App Service ACR pull configuration | Complete |
| `infra/deploy-private-acr.ps1` | Staged private ACR deployment workflow | Complete |
| `infra/publish-acr-images.ps1` | Credential-free ACR Tasks image publication | Complete |
| `infra/deploy.ps1` | Full-platform validation and deployment workflow | Complete |
| `infra/README.md` | Deployment and verification documentation | Complete |

---

## 11. Deployment Results

- **Completed:** 2026-09-24T11:24:58+02:00
- **Registry:** `azrxon32oitl5v66.azurecr.io`
- **SKU:** Premium
- **Public network access:** Disabled
- **Administrator account:** Disabled
- **Anonymous pull:** Disabled
- **Private endpoint:** `pe-azrxon32oitl5v66`, approved in `snet-private-endpoints`
- **Private DNS:** `privatelink.azurecr.io` linked to `az-radar-vnet`
- **Images:**
  - `az-radar-api:blue` → `sha256:6d5f1cfaeaa2c2bdb605665697938e7c69ba3baba9a46e2fd228f8f68aeeb356`
  - `az-radar-jobhost:green` → `sha256:6887f28e54a057e08d8669281288316a0f32af9f41bd779d21251897b27e2550`
  - `az-radar-bot-gateway:blue` → `sha256:89867f64cefdb71396c0fd4779adb05861144a910e1a21d878bae85e09ec2cc9`
  - `az-radar-dispatch-worker:blue` → `sha256:aaea633d6df65548c1bd1527d78db781cb9b729a47521741e295fb4785111abc`
- **App Services:** All four are running from ACR with managed-identity credentials,
  `imagePullTraffic=true`, `allTraffic=true`, and Always On enabled.
- **API health:** `https://azr-api-x8c5i2.azurewebsites.net/api/health` returned `healthy`.
- **UI:** `https://azr-api-x8c5i2.azurewebsites.net/` returned HTTP 200.
- **Bot endpoint:** `https://az-radar-bot-ay637nckh3ebc.azurewebsites.net/api/messages`
  returned HTTP 405 for GET, confirming the POST-only endpoint is reachable.
- **Workers:** JobHost and dispatch worker each report one active App Service instance.
- **Live RBAC:** All three expected identities have `AcrPull` scoped to the ACR.
- **Resolved deployment issue:** The initial bootstrap attempt failed because ACR does not allow
  export policy to be disabled while public access is temporarily enabled. The template now
  enables export only during bootstrap and disables it in the final private state.

Current phase: Deployed and verified.

---

## 12. Application Release — CloudLens Wiki and Subscription Navigation

**Prepared:** 2026-09-25T06:47:04+02:00

**Goal:** Deploy an application-only update that:

- makes subscription IDs in Impact Analysis open the matching Azure portal subscription in a new tab;
- converts Azure Service Health source HTML into safe Azure DevOps Wiki Markdown;
- refreshes the Wiki dashboard headings and visual hierarchy;
- adds a CloudLens-branded logo served by the dispatch worker and rendered in the Wiki page.

### Azure Context

- **Subscription:** `MOHOSSA-M365CPI50986977`
  (`5e22addc-6168-4683-afd0-789a121ca5d3`)
- **Resource group:** `az-radar-vnet-rg`
- **Location:** Central US (`centralus`)
- **User confirmation:** 2026-09-25

### Deployment Scope

| Component | Existing App Service | Image repository | Deployment action |
|-----------|----------------------|------------------|-------------------|
| API + React UI | `azr-api-x8c5i2` | `az-radar-api` | Build opposite blue/green tag and switch image |
| Service Health dispatch worker | `az-radar-dispatch-ay637nckh3ebc` | `az-radar-dispatch-worker` | Build opposite blue/green tag and switch image |

No infrastructure resources, role assignments, secrets, network settings, databases, or managed
identities are created or changed by this release.

### Validation Completed During Preparation

- [x] Frontend TypeScript type-check
- [x] Dispatch renderer tests (11/11)
- [x] Git whitespace validation
- [x] Subscription and location confirmed
- [x] All validation checks pass
  - [x] Azure CLI authentication and target context
  - [x] API container build
  - [x] Dispatch-worker container build
  - [x] Azure Policy validation
  - [x] Static RBAC review (no role changes)
- [x] Build and publish opposite blue/green images
- [x] Switch App Service image references
- [x] Verify UI, API health, dispatch worker, logo endpoint, and Wiki reconciliation

### Rollback

Restore each App Service to its currently running ACR image tag and restart it. Existing blue/green
tags remain available in ACR.

Current phase: Deployed and verified.

### Release Validation Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Azure context | `az account show` / `az account set` | Pass; confirmed subscription `5e22addc-6168-4683-afd0-789a121ca5d3` | 2026-09-25T07:28:00+02:00 |
| Frontend type-check | `cd src\az-radar-ui; npx tsc --noEmit` | Pass | 2026-09-25T07:22:00+02:00 |
| Dispatch tests | `dotnet test src\Dispatching\AzRadar.Dispatching.Tests\AzRadar.Dispatching.Tests.csproj --no-restore` | Pass; 11/11 | 2026-09-25T07:24:00+02:00 |
| Solution build | `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore` | Pass; 0 warnings, 0 errors | 2026-09-25T07:38:00+02:00 |
| API image build | `docker build --no-cache --build-arg NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json -f Dockerfile.api -t az-radar-api:validation .` | Pass; image `sha256:92b3e9736ce0...` | 2026-09-25T07:35:00+02:00 |
| Dispatch image build | `docker build --no-cache --build-arg NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json -f src\Dispatching\Dockerfile.worker -t az-radar-dispatch-worker:validation .` | Pass; image `sha256:a7396ae10eff...` | 2026-09-25T07:35:00+02:00 |
| Azure Policy | `az policy assignment list --scope ...\resourceGroups\az-radar-vnet-rg` | Pass; no assignments at target resource-group scope | 2026-09-25T07:29:00+02:00 |
| Static RBAC | Review release diff and existing deployment plan | Pass; application files only, no IaC or role changes | 2026-09-25T07:35:00+02:00 |

### Release Deployment Results

- **Completed:** 2026-09-25T08:03:00+02:00
- **API/UI image:** `azrxon32oitl5v66.azurecr.io/az-radar-api:green`
- **Dispatch image:** `azrxon32oitl5v66.azurecr.io/az-radar-dispatch-worker:green`
- **ACR Tasks:** API run `cj7`; dispatch run `cj8`; both succeeded
- **API health:** `https://azr-api-x8c5i2.azurewebsites.net/api/health` returned `healthy`
- **Brand asset:** `https://azr-api-x8c5i2.azurewebsites.net/cloudlens-logo.svg` returned HTTP 200
- **Wiki target:** `Azure Service Health`
- **Wiki publish intent:** `f04c58f6b46d54f339e2ff0a8aedcc4c6c2c1472a1dbd803122c2fcf7da64bbd`
- **Wiki publish status:** Delivered
- **Wiki version:** `"a00ba9fd55238ff2737f92f0b264a424a75b32a2"`
- **ACR final state:** public network disabled; admin and anonymous access disabled
- **App Service liveness:** Always On enabled for API and dispatch worker
- **Live RBAC:** Three `AcrPull` assignments remain scoped to the private ACR

---

## 13. Wiki Executive Dashboard Hotfix

**Prepared:** 2026-09-25T08:25:32+02:00

**Goal:** Replace the Azure DevOps-incompatible SVG logo with a public PNG and upgrade the Service
Health Wiki into an executive dashboard with:

- at-a-glance KPI tiles for all four event families;
- an executive status and decision brief;
- a prioritized action queue;
- compact, business-oriented event cards;
- a concise operating and governance guide.

### Deployment Scope

| Component | Current tag | Target tag |
|-----------|-------------|------------|
| API + React UI | `green` | `blue` |
| Service Health dispatch worker | `green` | `blue` |

No infrastructure, identity, RBAC, network, database, or secret changes are included.

### Validation Checklist

- [x] All validation checks pass
  - [x] Azure CLI authentication and existing target context
  - [x] API production container build
  - [x] Dispatch-worker production container build
  - [x] Azure Policy validation unchanged from application release
  - [x] Static RBAC review (no role changes)
- [x] Wiki renderer tests (11/11)
- [x] Frontend TypeScript type-check
- [x] API production container build
- [x] Dispatch-worker production container build
- [x] PNG logo visual inspection
- [x] Azure validation workflow
- [x] Publish and deploy `blue` images
- [x] Configure `CLOUDLENS_LOGO_URL` to the PNG endpoint
- [x] Publish and verify the Azure DevOps Wiki page

Current phase: Deployed and verified.

### Hotfix Validation Proof

| Check | Result |
|-------|--------|
| Renderer tests | Pass; 11/11 |
| Frontend type-check | Pass |
| Solution build | Pass; 0 warnings, 0 errors |
| API container | Pass; `sha256:fc6b3dba3c8...` |
| Dispatch container | Pass; `sha256:09a72333542e...` |
| Azure Policy | Pass; no assignments at the target resource-group scope |
| Static RBAC | Pass; no infrastructure or role changes |

### Hotfix Deployment Results

- **Completed:** 2026-09-25T08:50:20+02:00
- **API/UI image:** `azrxon32oitl5v66.azurecr.io/az-radar-api:blue` (ACR run `cj9`)
- **Dispatch image:** `azrxon32oitl5v66.azurecr.io/az-radar-dispatch-worker:blue` (ACR run `cja`)
- **PNG logo:** `https://azr-api-x8c5i2.azurewebsites.net/cloudlens-logo.png` returned HTTP 200 and `image/png`
- **API health:** `https://azr-api-x8c5i2.azurewebsites.net/api/health` returned `healthy`
- **Wiki publish intent:** `0a5e2e313547c8a86a8cfe725f80455e9d07d0069ffae79beda9afe97c57d11b`
- **Wiki publish status:** Delivered
- **Wiki version:** `"aa078672478619ffe4201467bcaa1245763c9662"`
- **ACR final state:** Public access, admin credentials, and anonymous pull disabled
- **Live RBAC:** Three `AcrPull` assignments remain on the private registry
- **Secondary page read:** Blocked by the expired local Azure DevOps CLI PAT; the worker delivery used its valid Key Vault credential and succeeded

---

## 14. Teams Routing Destination Deletion

**Prepared:** 2026-09-25T12:48:05+02:00

**Goal:** Allow administrators to remove stale Teams destinations from CloudLens without deleting
the actual Teams channel.

### Behavior

- Add **Remove from CloudLens** to each Teams routing destination.
- Clearly state that the Teams channel itself remains managed in Microsoft Teams.
- Disable routing before cleanup to make retries safe.
- Delete the CloudLens channel configuration and Teams conversation reference.
- Delete related delivery intents and delivery-attempt history.
- Remove the channel ID from stored event routing references.
- Complete stale queued Service Bus messages for removed targets instead of dead-lettering them.

### Validation

- [x] Shared tests: 116/116
- [x] Dispatch tests: 15/15
- [x] Frontend TypeScript type-check
- [x] Full solution build: 0 warnings, 0 errors
- [x] API production container build
- [x] Dispatch-worker production container build
- [x] Azure validation workflow
- [x] Deploy opposite `green` tags
- [x] Delete `Operations Department / Test`
- [x] Verify channel and related delivery records are removed

### Release Validation Proof

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| Azure context | `az account set` / `az account show` | Pass; subscription `5e22addc-6168-4683-afd0-789a121ca5d3` enabled | 2026-09-25 |
| Azure Policy | `az policy assignment list --scope ...\resourceGroups\az-radar-vnet-rg` | Pass; no assignments at target resource-group scope | 2026-09-25 |
| Solution build | `dotnet build AzRadar.slnx -p:Platform="Any CPU" --no-restore` | Pass; 0 warnings, 0 errors | 2026-09-25 |
| Shared tests | `dotnet test tests\AzRadar.Shared.Tests --no-build` | Pass; 116/116 | 2026-09-25 |
| Dispatch tests | `dotnet test src\Dispatching\AzRadar.Dispatching.Tests\AzRadar.Dispatching.Tests.csproj --no-build` | Pass; 15/15 | 2026-09-25 |
| Frontend type-check | `cd src\az-radar-ui; npx tsc --noEmit` | Pass | 2026-09-25 |
| API container | `docker build --no-cache ... -f Dockerfile.api -t az-radar-api:delete-validation .` | Pass; `sha256:2d44e40ae669...` | 2026-09-25 |
| Dispatch container | `docker build --no-cache ... -f src\Dispatching\Dockerfile.worker -t az-radar-dispatch-worker:delete-validation .` | Pass; `sha256:4c514268be44...` | 2026-09-25 |
| Static RBAC | Review unstaged and staged diffs under `infra` and `src\Dispatching\infra` | Pass; no infrastructure or role-assignment changes | 2026-09-25 |

**Validated by:** azure-validate skill

### Release Deployment Results

- **Completed:** 2026-09-25
- **API/UI image:** `azrxon32oitl5v66.azurecr.io/az-radar-api:green`
  - ACR run `cjb`
  - Digest `sha256:df2305b0c9ab131c24656b4ef1e00b8ee1adc08ddc269fcdb99242cc561399f3`
- **Dispatch image:** `azrxon32oitl5v66.azurecr.io/az-radar-dispatch-worker:green`
  - ACR run `cjc`
  - Digest `sha256:9f87178fdb64ccf4a0e88937baad3b151580397a8a050a2f480a6e2f31903529`
- **API health:** `https://azr-api-x8c5i2.azurewebsites.net/api/health` returned `healthy`.
- **Deleted destination:** `Operations Department / Test`
  (`b1eb48b6c20bff339cd9536c93969d57ba51ce392b6d905fe4dc148246a46171`)
- **Delete result:** HTTP 204; the destination is absent from
  `/api/service-health/channels`.
- **Cascading cleanup:** No matching delivery intents or event routing references remain in the
  API-visible Cosmos records. Successful completion of the delete endpoint also confirms the
  conversation-reference and delivery-attempt cleanup sequence completed before the channel record
  was deleted.
- **Service Bus:** `teams-realtime` remained active with zero active messages; the pre-existing
  dead-letter count remained stable at 9 across the post-delete verification window.
- **App Service liveness:** API and dispatch worker are running with Always On enabled.
- **ACR final state:** Public access disabled; admin credentials disabled; anonymous pull disabled;
  export policy disabled.
- **Live RBAC:** Three `AcrPull` assignments remain scoped to the private ACR.

Current phase: Deployed and verified.

## 15. Lifecycle Calendar Horizon Completeness

**Completed:** 2026-10-01 · PRD: `prds/lifecycle-calendar-horizon-completeness.md`

- **API/UI image:** `azrxon32oitl5v66.azurecr.io/az-radar-api:green` (ACR run `cjj`; previous `blue` run `cjg`)
- **JobHost image:** `azrxon32oitl5v66.azurecr.io/az-radar-jobhost:green` (ACR run `cjk`; previous `blue` run `cjh`)
- **ACR publication:** Temporarily enabled the export policy, public access and `defaultAction=Allow`
  for ACR Tasks, then restored them: public access disabled, `defaultAction=Deny`, export disabled,
  admin and anonymous pull disabled.
- **App Service liveness:** Always On had drifted to `false` on both apps and was re-enabled.
- **Validation:** health `healthy`. `/api/calendar` went from 30 to 42 items. 55 undated lifecycle
  items are visible. Stats and calendar counts agree. The deadline backfill patched 17 feed items
  and 616 doc insights. Two crawls succeeded with discard diagnostics.
- **Rollback:** Switch both apps back to `blue`. The new Cosmos fields are additive and ignored by
  older images.

## 16. Three-Year Quarter Grid and Horizon Filter

**Completed:** 2026-10-01 · PRD: `prds/lifecycle-calendar-horizon-completeness.md` (R5)

- **API/UI image:** `azrxon32oitl5v66.azurecr.io/az-radar-api:blue` (ACR run `cjm`; previous `green` run `cjj`)
- **JobHost image:** Unchanged at `az-radar-jobhost:green`. This release changes the UI only.
- **ACR publication:** The same temporary opening for ACR Tasks, then restored: public access
  disabled, `defaultAction=Deny`, export disabled.
- **App Service liveness:** Always On confirmed `true` on the API app.
- **Validation:** health `healthy`. The UI bundle contains the new quarter filter strings. Live data
  gives 12 quarter blocks (Q4 2026 to Q3 2029), and the filter counts (12 + 5 + 9 + 6 + 10) equal All (42).
- **Rollback:** Switch `azr-api-x8c5i2` back to `az-radar-api:green`.

Current phase: Deployed and verified.
