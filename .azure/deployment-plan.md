# Azure Updates deployment and no-LLM backfill

Status: Validated
Recipe: AZCLI (application-image update only; no infrastructure provisioning)

## 1. Scope and approval

User requested deployment to the existing Azure environment and execution of a
historical backfill without LLM usage on 2026-09-07.
Add a per-job skip-analysis flag and a persisted intentional-skip marker, leaving
normal analysis enabled for subsequent new/revised announcements.

## 2. Azure context

- Subscription: 5e22addc-6168-4683-afd0-789a121ca5d3
- Resource group: az-radar-vnet-rg
- Existing region: Central US
- API: azr-api-x8c5i2
- Worker: azr-job-x8c5i2
- Endpoint: https://azr-api-x8c5i2.azurewebsites.net

## 3. Deployment

- Build Dockerfile.api and Dockerfile.jobhost with opposite blue tags.
- Current images on both target apps: green (confirmed 2026-09-07).
- Push moimhossain/az-radar-api:blue and moimhossain/az-radar-jobhost:blue.
- Update only these two App Services, preserving existing identity/network settings.
- Verify worker deployment before enabling creation of the no-LLM backfill job.
- No resources, keys, role assignments, or LLM deployments will be created.

## 4. Rollback

Retain green tags. If rollout fails, restore each target app to its original green
image. Do not delete data. Do not trigger backfill until the new worker is active.

## 5. Backfill

Create one azure-updates crawl with skipLlmAnalysis=true.
Confirm it completes and source IDs match the complete catalog/RSS union.
Confirm newly imported records have no LLM analysis and intentional-skip markers.
Normal crawls must skip unchanged intentionally unanalysed records.

## 6. Preparation

- [x] Implement no-LLM mode and regression coverage.
- [x] Validate build, UI, Docker images, Azure targets.
- [x] All validation checks pass
  - [x] Core validation: CLI authentication and existing targets confirmed; no IaC changes, so provisioning validate/what-if are not applicable.
  - [x] Docker build for API and JobHost.
  - [x] Azure policy: existing resources/region/SKUs/identity/network retained; no provisioning or policy scope changes.
- [x] Deploy both apps.
- [x] Execute and reconcile historical backfill.

## 7. Validation proof

2026-09-07, approximately 07:43-07:46 local:
- `dotnet test .\tests\AzRadar.Shared.Tests --no-restore --filter 'FullyQualifiedName~AzureUpdates' --verbosity quiet`: 55 passed.
- `dotnet build .\src\AzRadar.Api\AzRadar.Api.csproj --no-restore --verbosity quiet`: passed (existing CS0458 warning).
- `dotnet build .\src\AzRadar.JobHost\AzRadar.JobHost.csproj --no-restore --verbosity quiet`: passed.
- Frontend `npx tsc --noEmit`: passed.
- Docker engine 29.6.2 available; both target apps Running, green, Central US.
- Cosmos data-plane Data Contributor role confirmed for existing worker UAMI.
- Initial container restores failed because direct nuget.org is unavailable in
  this environment. Added optional NUGET_SOURCE build argument; retry uses the
  same approved packagefeedproxy source as the host's NuGet configuration.
- `docker build --no-cache --quiet --build-arg NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json -f Dockerfile.api -t moimhossain/az-radar-api:blue .`: passed, image 8c6bcd729f8d306a347debee1947ea5ce0201ae4aea214ab03e3c96487421646.
- `docker build --no-cache --quiet --build-arg NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json -f Dockerfile.jobhost -t moimhossain/az-radar-jobhost:blue .`: passed, image ccd074f8e69926284df749dea8288f804cbdb2e15964dc6ec5f494aaa136c121.
- `docker image inspect`: both images linux/amd64.
- Static role review: existing cosmos-rbac.bicep supplies account-scoped data
  contributor; no infrastructure or role changes required.
- `git diff --check`: passed.

Follow-up proof, 2026-09-07:
- `dotnet test .\tests\AzRadar.Shared.Tests --no-restore --filter 'FullyQualifiedName~AzureUpdates|FullyQualifiedName~JobHeartbeat|FullyQualifiedName~FeedItemContentCodec'`: 70 passed.
- API and JobHost builds passed; green API Docker build also ran the frontend production build.
- Both `docker build --no-cache --quiet --build-arg NUGET_SOURCE=https://packagefeedproxy.microsoft.io/nuget/v3/index.json` commands passed for green tags.
- Green API image: 38aab290d1324bcaf09a4101206e210d86df28a98dfad39ecdb24f1d7d826ea9.
- Green worker image: e1dd35e2287a8ede7e01c8a6725106fdf9fc704540d05018035457008c7338f1.
- Exact oversized upstream record: 1,020,398 source bytes encoded to a 75,356-byte
  storage document; decoded text matches the original exactly.
- Existing Cosmos Data Contributor permissions cover item patch operations;
  no additional role/network/region/tier changes.
- No active jobs found before follow-up rollout; both current images confirmed blue.

## 8. Rollout and execution

- Both apps switched from green to blue; API health confirms catalog-rss-v2
  and supportsSkipLlmAnalysis=true.
- Worker public and SCM ingress returned existing IP restrictions. Restrictions
  were preserved. ARM container diagnostics report no startup failures.
- Enabled Always On on the existing B1 worker to prevent idle shutdown during
  background processing; this restores the setting already specified by Bicep.
  No hosting tier, network, identity, or resource changes.
- Backfill job: 4f75308c-9399-4107-bd9d-8633920ab6fc
- Persisted request: azure-updates, skipLlmAnalysis=true.
- Initial job failed on an oversized archived record; its successful imports were
  retained and the follow-up below completed the remaining work.

## 9. Follow-up: liveness and oversized content

User approved heartbeat-based stale detection during the live backfill.
The first backfill subsequently failed at an archived announcement exceeding the
Cosmos document limit; imported data is retained.

- Add 30-second conditional heartbeat writes, separate progress patches, and
  API-derived stale state after two minutes without heartbeat (not job age).
- Claim queued jobs only when an executor is available; require deletion
  confirmation for all active jobs.
- Store large source bodies losslessly compressed and bound duplicate summaries.
- Validate tests and rebuild opposite green images; current live images are blue.
- Roll back this follow-up to blue if needed.
- Resume via a new no-LLM crawl; unchanged previously imported records are skipped.

Green rollout completed:
- Live API reports supportsJobHeartbeat=true.
- Resumed job: d4cd7eac-1ffc-42a0-b1e8-2da27ec05531, skipLlmAnalysis=true.
- Heartbeats observed at 08:25:53, 08:26:23, and 08:26:53 local, while catalog
  enumeration was still running; isStale=false.

## 10. Final outcome

- Resumed job completed at 08:35:56 local: 1,653 new, 8,234 skipped,
  9,887 total checked, no error.
- 21 advancing heartbeat timestamps were observed throughout the ten-minute job;
  it was never reported stale.
- Production contains 9,887 unique Azure Updates records. All 200 current RSS IDs
  are present; zero missing.
- All 9,887 backfill records have intentional analysis-skip markers; none has an
  LLM analysis. Future unchanged backfilled posts are not queued for analysis.
- The formerly oversized announcement is readable through the API with all
  1,020,398 original source characters preserved.
- Both App Services are Running on green; blue is retained as rollback.
