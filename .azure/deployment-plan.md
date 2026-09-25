# Azure Deployment Plan

> **Status:** Deployed

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
