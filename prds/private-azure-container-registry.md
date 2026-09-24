# PRD - Private Azure Container Registry Deployment

> **Status:** Implemented
> **Feature area:** Infrastructure, container supply chain, and deployment
> **Date:** 2026-09-24

## 1. Summary

Move AzRadar container distribution from public Docker Hub repositories to an Azure Container
Registry (ACR) provisioned in the existing `az-radar-vnet-rg` test resource group. The registry
must use private networking, disable anonymous and administrator credentials, and allow each
containerized App Service to pull its image with a user-assigned managed identity.

The current implementation does not use ACR. `infra/main.bicep`, the dispatching Bicep template,
and `infra/main.bicepparam` reference public `moimhossain/*` Docker Hub images, while the shared
App Service module explicitly assumes a public registry without credentials.

## 2. Goals

1. Provision one Azure Container Registry in `az-radar-vnet-rg`.
2. Use the Premium SKU required for Azure Private Link.
3. Add an ACR private endpoint to the existing private-endpoint subnet.
4. Create and link the `privatelink.azurecr.io` private DNS zone to the existing VNet.
5. Disable ACR public network access after images can be built and imported through an approved
   deployment path.
6. Disable the ACR administrator account and anonymous pull.
7. Grant `AcrPull` only to the managed identities used by the AzRadar App Services.
8. Configure every deployed App Service container to authenticate to ACR with its assigned
   user-managed identity and pull the image through VNet integration.
9. Publish newly built AzRadar images to ACR and update the deployed applications to use them.
10. Preserve the existing App Service endpoints, data, managed identities, and private Cosmos,
    Service Bus, Event Hubs, Key Vault, and Azure OpenAI connectivity.

## 3. Non-goals

- Creating a new resource group, subscription, VNet, or App Service.
- Using ACR access keys, registry passwords, service-principal secrets, or Docker Hub credentials.
- Making the App Service front ends private.
- Introducing Azure Kubernetes Service, Azure Container Apps, or a new CI/CD platform.
- Deleting the existing Docker Hub repositories or tags during this rollout.
- Changing application behavior or data schemas.

## 4. Existing state

The repository currently:

- Supplies Docker Hub image references through `apiImage`, `jobImage`, `botGatewayImage`, and
  `dispatchWorkerImage`.
- Configures App Services with `linuxFxVersion: DOCKER|<public-image>`.
- Attaches user-assigned managed identities to the API and JobHost.
- Creates separate user-assigned identities for the dispatch worker and optional Teams bot.
- Integrates App Services with delegated VNet subnets and routes application traffic through the
  VNet.
- Provides a dedicated subnet for private endpoints.
- Does not provision an ACR, ACR private endpoint, ACR private DNS zone, or `AcrPull` assignments.

## 5. Target architecture

### 5.1 Registry

Provision a single Premium ACR with a deterministic, globally unique name derived from the
deployment prefix and resource-group identity.

Registry controls:

- `adminUserEnabled: false`
- `anonymousPullEnabled: false`
- `publicNetworkAccess: Disabled`
- HTTPS-only Azure control-plane and data-plane usage
- Private endpoint group `registry`
- Private DNS zone `privatelink.azurecr.io`

### 5.2 Image repositories

Use four repositories when their corresponding runtime is deployed:

| Runtime | ACR repository |
|---------|----------------|
| API and React UI | `az-radar-api` |
| Crawl JobHost | `az-radar-jobhost` |
| CloudLens Teams bot gateway | `az-radar-bot-gateway` |
| Service Health dispatch worker | `az-radar-dispatch-worker` |

Tags remain immutable deployment identifiers. The first migration uses a timestamped or commit
tag rather than `latest`. Subsequent deployments may retain the repository's blue/green
alternation, but the image hostname changes from Docker Hub to the private ACR login server.

### 5.3 Authentication and authorization

- Build/push uses the signed-in Azure CLI identity through `az acr build` or `az acr login`; no
  registry password is stored.
- The shared AzRadar UAMI receives `AcrPull` for the API and JobHost.
- The dispatch worker UAMI receives `AcrPull`.
- The optional Teams bot gateway UAMI receives `AcrPull` only when Teams dispatch is provisioned.
- Role assignments are scoped to the ACR resource.
- App Service is configured with managed-identity registry credentials and the client ID of the
  identity that owns `AcrPull`.

### 5.4 Network path

1. App Service resolves `<registry>.azurecr.io`.
2. The linked private DNS zone resolves the registry endpoints to the ACR private endpoint.
3. App Service pulls over its regional VNet integration.
4. Image-pull-over-VNet is explicitly enabled on each App Service.
5. ACR public network access remains disabled after image publication and deployment.

The existing App Service integration subnets and private-endpoint subnet are reused. No new
address range is required.

## 6. Infrastructure changes

1. Add an ACR Bicep module that provisions:
   - Premium registry.
   - Private DNS zone and VNet link.
   - Private endpoint and DNS zone group.
   - Managed-identity `AcrPull` role assignments.
2. Extend the network module to expose the VNet resource ID already required for DNS linking.
3. Change image parameters from Docker Hub defaults to ACR repository/tag inputs derived from the
   registry login server.
4. Extend the App Service module with:
   - The registry-pull UAMI client ID.
   - Managed-identity ACR authentication.
   - Image pull over VNet.
5. Apply the same settings to dispatching App Services.
6. Update deployment documentation and validation commands.

## 7. Deployment workflow

1. Validate the updated Bicep templates.
2. Provision ACR, private endpoint, DNS, and RBAC without changing the running image references.
3. Build and push/import all required images into ACR using unique tags.
4. Verify repositories and manifests exist in ACR.
5. Deploy App Service image references and managed-identity pull settings.
6. Restart only the affected App Services if the platform does not recycle them automatically.
7. Verify application health, worker liveness, image source, DNS, RBAC, and registry networking.

The sequence avoids configuring an App Service with an image that has not yet been published.

## 8. Security requirements

- No registry keys, passwords, access tokens, or connection strings in Bicep, parameter files,
  App Service settings, scripts, logs, or source control.
- ACR administrator credentials remain disabled.
- Public network access is disabled for the final state.
- `AcrPull` is the only registry data-plane role granted to runtime identities.
- Build/push permissions are not granted to runtime identities.
- Role assignment names are deterministic and deployment-idempotent.
- Private DNS is linked only to the AzRadar VNet.
- Existing zero-key Cosmos, Azure OpenAI, Event Hubs, Service Bus, and Key Vault authentication is
  unchanged.

## 9. Failure handling

- Bicep validation failures stop before provisioning.
- A failed image build or push stops before App Service image references are changed.
- Missing `AcrPull` propagation stops deployment before application restart.
- An image-pull failure is surfaced through App Service container logs and deployment validation.
- Rollback changes each App Service back to its prior Docker Hub image or previous known-good ACR
  tag; no data rollback is required.
- ACR and its private endpoint remain additive infrastructure during rollback.

## 10. Acceptance criteria

1. A Premium ACR exists in `az-radar-vnet-rg` in Central US.
2. ACR administrator access and anonymous pull are disabled.
3. ACR public network access is disabled in the final deployed state.
4. A private endpoint exists in the existing AzRadar private-endpoint subnet.
5. `privatelink.azurecr.io` is linked to the AzRadar VNet and the private endpoint has a DNS zone
   group.
6. Required AzRadar repositories and the newly built tags exist in ACR.
7. API, JobHost, dispatch worker, and the optional bot gateway use ACR image references whenever
   those applications are deployed.
8. Each runtime App Service uses a user-assigned managed identity with `AcrPull`; no registry
   credential settings exist.
9. Image pull over VNet is enabled for every runtime App Service.
10. The API and UI return successful health responses from their existing public endpoint.
11. JobHost and dispatch worker remain running with Always On enabled and no image-pull errors.
12. Cosmos, Service Health ingestion, Wiki dispatch, and configured Teams dispatch behavior remain
    operational.
13. Bicep build/validation, .NET tests, frontend type-check, and post-deployment smoke tests pass.

## 11. Rollout and rollback

Roll out in two infrastructure-safe stages: provision the private registry and publish images,
then switch App Services to ACR. Keep the previous image references and tags recorded before the
switch.

Rollback is an App Service image change to the previous known-good image references, followed by
an application restart and health verification. The ACR, private endpoint, DNS zone, and role
assignments can remain deployed because they do not alter application data or external endpoints.

## 12. Operational notes

- ACR Private Link requires Premium SKU and adds ongoing cost compared with Basic or Standard.
- Private ACR builds may require a trusted-services exception or a temporary controlled public
  access window depending on the selected build mechanism. Any temporary exposure must be
  explicitly reverted and verified before completion.
- App Service image pulls need both managed-identity registry configuration and VNet image-pull
  routing; either setting alone is insufficient for a private registry.
- Existing Always On settings must remain enabled for continuous workers.
