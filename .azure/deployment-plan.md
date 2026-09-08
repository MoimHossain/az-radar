# Temporary Azure Updates AI analysis and restoration

Status: Validated
Recipe: AZCLI (JobHost image updates only; no infrastructure provisioning)

## 1. Scope and approval

The user requested a temporary production JobHost behavior that analyzes a
bounded sample of previously imported Azure Updates records whose LLM analysis
was intentionally skipped, followed by restoration of the normal code and
container.

Hard boundary:

- Do not build, deploy, restart, configure, or change networking/access for
  `azr-api-x8c5i2`.
- Do not directly access or modify Cosmos DB.
- Only the source code used by `Dockerfile.jobhost`, the JobHost container
  image, and `azr-job-x8c5i2` may change.

The user's request and repeated caution constitute approval for this bounded
JobHost-only workflow.

## 2. Azure context

- Subscription: `5e22addc-6168-4683-afd0-789a121ca5d3`
- Tenant: `ec50add3-793a-47aa-a3f9-727a4398faa3`
- Resource group: `az-radar-vnet-rg`
- Region: Central US
- JobHost: `azr-job-x8c5i2`
- JobHost plan: `az-radar-job-plan`
- Protected API/UI app: `azr-api-x8c5i2` (strictly out of scope)
- API endpoint used only to submit/read jobs:
  `https://azr-api-x8c5i2.azurewebsites.net`

## 3. Temporary implementation

Temporarily change `AzureUpdatesJobHandler` so one normal AI-enabled
`azure-updates` crawl:

- bypasses the unchanged-item skip only for the first 40 records where
  `llmAnalysisSkipped=true`;
- calls the existing managed-identity Azure OpenAI analyzer;
- replaces each record through the existing ETag-protected Cosmos service;
- persists `llmAnalysis`, sets `llmAnalysisSkipped=false`, and reports the
  records as updated;
- leaves all other unchanged records on the normal skip path.

No database endpoint, credentials, firewall, role assignment, or direct data
operation is introduced.

## 4. Validation

Before temporary deployment:

- Run targeted Azure Updates tests.
- Build the JobHost project.
- Build the temporary JobHost Docker image locally.
- Confirm the API/UI App Service configuration and image are not changed.

After running the temporary crawl:

- Confirm the job completes without error.
- Confirm `updatedItems=40`.
- Read the updated feed items through the existing API and confirm the sample
  contains persisted LLM analysis with positive confidence.

After restoration:

- Restore the source file exactly to normal behavior.
- Re-run targeted tests and JobHost build.
- Build and deploy the normal JobHost container using the opposite blue/green
  tag.
- Run a normal AI-enabled crawl and confirm the 40 unchanged analyzed records
  are skipped rather than analyzed again.

## 5. Blue-green deployment

Current JobHost image: `moimhossain/az-radar-jobhost:green`.

1. Build and push temporary image
   `moimhossain/az-radar-jobhost:blue`.
2. Point only `azr-job-x8c5i2` to `blue`, restart/warm only JobHost, and run the
   bounded crawl.
3. Restore normal source.
4. Build and push normal image
   `moimhossain/az-radar-jobhost:green`.
5. Point only `azr-job-x8c5i2` back to `green`, restart/warm only JobHost.

## 6. Rollback

- If the temporary image fails before processing data, point JobHost back to
  the existing normal `green` image.
- If the bounded job partially succeeds, restore the normal image; already
  analyzed records remain valid and no direct data rollback is required.
- Never use the API/UI App Service as a rollback or maintenance surface.

## 7. Validation proof

All validation checks pass:

- [x] 1. Core validation: Azure CLI authentication, target resource existence,
  targeted tests, and JobHost build. Infrastructure validate/what-if is not
  applicable because this plan changes no IaC or Azure resource properties.
- [x] 2. Docker build: build the temporary JobHost image from
  `Dockerfile.jobhost`.
- [x] 3. Azure policy validation: confirmed the operation changes only the
  existing JobHost container image and introduces no resource, identity,
  networking, SKU, location, or policy-scope changes.

Preparation checks completed on 2026-09-08:

- `dotnet test .\tests\AzRadar.Shared.Tests --no-restore --filter
  "FullyQualifiedName~AzureUpdates" --verbosity quiet`: 58 passed.
- `dotnet build .\src\AzRadar.JobHost\AzRadar.JobHost.csproj --no-restore
  --verbosity quiet`: succeeded with 0 warnings and 0 errors.
- `docker build --no-cache --build-arg
  NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json -f
  Dockerfile.jobhost -t moimhossain/az-radar-jobhost:blue .`: succeeded.
- Protected API/UI baseline captured as running on
  `moimhossain/az-radar-api:green`; no API/UI operation is included in this
  plan.

Awaiting independent `azure-validate` completion.

Azure validation rerun on 2026-09-08:

- Subscription authentication and existing JobHost target confirmed.
- JobHost retains its UAMI and private-only network configuration.
- 58 targeted Azure Updates tests passed.
- JobHost build succeeded with 0 warnings and 0 errors.
- Temporary blue image exists locally as
  `sha256:2781012d5ccdf41fef8a79c3fcbb394f9874094a73db73cda34aa20c6fd77310`.
- Assigned policies were reviewed. The plan creates no resources and changes no
  identity, network, location, SKU, or policy scope.
- Static RBAC review confirmed the existing UAMI is attached to JobHost and the
  Cosmos DB Built-in Data Contributor assignment is deterministic and
  keyless. This deployment does not alter either definition.
