# PRD: Reliable JobHost Schedules and Success-Only Post-Jobs

> **Status:** Implemented, deployed, and live smoke-tested
> **Date:** 2026-11-03
> **Product:** CloudLens / AzRadar
> **Decision:** Reuse JobHost and Cosmos DB. No Function App or new hosting resources.

## 1. Summary

Add **Schedules** immediately below **Crawling Jobs** in Services. Users can run
any of the four existing job types once, daily, or weekly, with an optional
post-job that starts only after the primary succeeds.

Example: daily at 00:00 Europe/Amsterdam, run Azure Updates, then Blast Radius
Scan after successful completion.

The existing JobHost runs a new `ScheduleWorker` background service. Every
30 seconds it queries Cosmos for due schedules and unfinished chains.
**Cosmos is the alarm list; the service is only the clock.** Restarting the process
does not erase definitions, admitted runs, or pending dispatch.

The user selected this design instead of the earlier independent Functions
proposal, to avoid another hosting resource. Scheduling therefore shares
JobHost's availability: it cannot start work while JobHost is down, but catches
up according to an explicit policy when it returns.

## 2. Scope and UX

Supported primary and post-job types:

- `azure-updates`
- `ms-learn-intelligence`
- `github-crawl`
- `blast-radius-scan`

The `/schedules` page provides creation/editing, pause/resume, deletion, Run now,
next-occurrence preview, paginated history, and retry of a failed chain/step.
Forms include name, primary job, Once/Daily/Weekly, start date, local time,
time zone, weekdays, and optional post-job. Azure Updates supports its existing
skip-LLM option independently on either step.

History shows scheduled/admitted/completed times, configuration revision, state,
errors, and links to the actual primary and post-job diagnostics.
Partial completion is not full success.

Non-goals: arbitrary cron, monthly/holiday calendars, workflow DAGs, parallel
branches, scripts, hard real-time deadlines, multi-region HA, or exactly-once
external side effects.

## 3. Architecture

```text
Schedules UI -> Minimal API -> Cosmos schedule-control container
                                      ^
                                      |
Existing JobHost: ScheduleWorker (every 30 seconds)
    |
    +-- admit due occurrence atomically with next due date
    +-- reconcile unfinished run / durable pending step
    +-- create crawl job with deterministic ID
                  |
                  v
Existing crawl-jobs container
    |
    +-- Change Feed hints
    +-- startup + periodic durable pending sweep
                  |
                  v
Existing job executors + existing IJobHandler implementations
    |
    +-- durable terminal outcome and completion/read barrier
                  |
                  v
ScheduleWorker observes success -> prepares post-step -> same dispatch path
```

No HTTP callback to the API is needed. The API can be down while existing
schedules continue. No separate scheduler infrastructure, Storage account, broker,
or credentials are introduced.

The clock uses UTC. Application recurrence calculations resolve each schedule's
local calendar and time zone; do not change the process-wide time zone.

## 4. Persistence and coordination

Add `schedule-control`, partition key `/scheduleId`. This is a deliberate new
container exception to the application's common `/id` convention. Existing
containers and partition keys remain unchanged.

| Record | Purpose |
|---|---|
| Definition (`kind=schedule`, `id=definition`) | Revision, configuration, enabled/deleted flags, next due time, indexed numeric wake time, active run, latest outcome, skipped/missed counters |
| Run (`kind=run`) | Immutable configuration snapshot, due/admitted times, primary/post IDs, current step, execution number, retry time, terminal result |
| Completion barrier (`kind=barrier`) | Bounded pages of opaque Cosmos session tokens and touched item references, partitioned by job ID |
| Execution gate (`kind=execution-lock`) | Durable ownership of crawling execution, preventing overlapping shared cursor/result mutations |

Pending step state in the run record is the durable **outbox intent**; separate
dispatch documents are unnecessary for a fixed two-step chain.

### Admission transaction

Within one schedule partition, a conditional Cosmos transactional batch:

1. Replaces the definition using its current ETag.
2. Creates the occurrence with its frozen configuration and pending primary step.
3. Advances the next due time and sets the active run reference.

All changes succeed or none do. A concurrent pause, edit, or second scheduler
loses the ETag race and re-reads on the next pass.

### Identities and cross-container dispatch

Occurrence identity is derived from schedule ID, revision, and resolved due UTC
instant. Run now and operator retries use persisted request idempotency keys.
Job identity is derived from run, step, and execution number.

Creating the crawl job and updating the schedule container are not one
transaction. Dispatch is deliberately repeatable:

- Create the deterministic job ID.
- If it already exists, verify its schedule/run, job type, options, and parent.
- Observe its status and persist the step state.

Lost acknowledgement after job creation causes another read of the same job, not
another job. A missing previously acknowledged job becomes attention-required,
not silently recreated. API deletion of scheduled or executing jobs is blocked.

List/history queries use pagination. Scheduler queries stream due definitions
using an indexed Unix-millisecond `wakeAt`, without loading all schedules/history.
Individual schedule failures are logged and do not stop the rest of the pass.
Definitions with active runs remain wakeable even if paused or deleted.

## 5. Recurrence, missed runs, and overlap

- Once, daily, weekly; minute-precision local time and explicit time zone.
- Daily means a local calendar time, not every 24 elapsed hours.
- Calculate from scheduled time, never from completion time.
- Spring-forward gap: move to the first valid minute. Fall-back fold: use the
  earlier UTC instant once. Preview shows resolved local/UTC timestamps.
- Reject invalid job types/options, dates, time zones, weekdays, and past-only
  one-time schedules.
- Downtime recovery coalesces to the **latest due occurrence within 24 hours**.
  Earlier missed dates increment the missed counter; no catch-up storm.
- A one-time schedule older than 24 hours becomes visibly missed.
- At most one active chain per schedule, including its post-job and retry waits.
  Due occurrences crossed while that chain is active are skipped and counted.
- Pause prevents future admissions but does not cancel an admitted chain.
- Resume starts with the next future occurrence; paused dates do not catch up.
- Edits increment revision and affect future admissions only.
- Delete is a soft delete; an already admitted chain still finishes.
- Run now does not move the next scheduled date and respects the active-chain gate.

## 6. Worker reliability and shared-data safety

Change Feed remains a fast hint mechanism, not the durable queue of record.
JobHost adds a startup and 30-second paginated pending-document sweep.
The in-memory channel is bounded and deduplicated; dropped hints are rediscovered
from Cosmos. Fresh-read ETag claims and attempt-fenced progress/heartbeat patches
remain in place.

For the first release, **one durable execution gate serializes all crawling jobs
across JobHost instances, including manual jobs**. Existing handlers share
watchlist cursors and result containers, so adding replicas must not introduce
unsafe concurrent execution. The scheduling/control plane can handle many
definitions independently; expensive crawling throughput is intentionally bounded.
This trades parallel crawl throughput for predictable data safety.

The gate has no automatic expiry: a stale heartbeat does not prove a worker has
stopped. Normal handler exit releases it; terminal owner records permit safe
cleanup after a release acknowledgement was lost.

A stale processing job (five minutes without heartbeat) blocks its chain and
surfaces attention-required. Operators must stop the owning worker, then explicitly
resolve the job as interrupted via `POST /api/crawl-jobs/{id}/resolve-stalled`
with `workerStopped=true`. This attempt-fenced transition never implies success.
After resolution the chain fails, the gate releases, and a new explicit recovery
run may be admitted. Running-job cancellation is not implied by pause/delete.

Increasing crawling parallelism requires audited resource-scoped locks and
side-effect fencing; do not simply add more executors or an expiring global lease.

## 7. Success-only chaining and read visibility

Persist outcome separately from existing job status:

- `completed + succeeded`: all required work successfully finished.
- `completed + partial`: some required work failed or coverage was incomplete.
- `failed`: execution/configuration failure, including a confirmed interruption.

Zero new findings is valid success. Missing Blast Radius configuration is failure.
GitHub repository errors/rate limits/caps and failed Resource Graph queries are
partial, not full success. Scheduled LLM fallbacks must fail or mark partial rather
than allowing a success-shaped fallback to start the post-job.

The reconciler observes the **specific primary job referenced by the run**.
Only explicit `succeeded` with no error admits the post-step. Legacy outcome-less,
partial, failed, missing, or stalled jobs cannot trigger it.
Persisted pending post-step state survives a crash between primary completion and
post-job creation. Completing all configured steps atomically clears the active
run reference; a post-job failure never rewrites primary success.

Cosmos uses session consistency. A new post-job process does not automatically
inherit the predecessor's read-your-writes guarantees. Scheduled execution captures
opaque session tokens from acknowledged feed/doc/cursor/result writes, persists
bounded manifest pages before terminal success, and records the page count.
The post-job warms its Cosmos client with token-bound item reads before querying
the current dataset. Missing barriers block execution. Tokens are never rewritten.

This is causal visibility, not snapshot isolation: other work may change the
dataset, and existing handler query filters/limits still apply.
Test a fresh-client handoff; do not rely on an arbitrary sleep for consistency.

## 8. Retry policy

Dispatch/storage failures retain durable intent and are reconsidered on the next
pass; they do not get a fresh job ID.

Safe classified **pre-handler** transient failures can retry at most three total
executions, waiting one and five minutes. Each execution has a distinct job ID.
Once a handler has started, automatic full replay is disabled unless its replay
safety is established; configuration, partial, unknown, and interrupted work fail
closed. This prevents duplicated LLM costs and cursor/result mutations.

An operator can retry a terminal failed run with an idempotency key. A new recovery
occurrence preserves the original configuration. If the primary succeeded, only
the post-step is rerun, retaining the primary's success/barrier evidence.
Retries never reopen an immutable terminal run.

## 9. API

All endpoints use existing Minimal API conventions and access boundaries.

| Route | Purpose |
|---|---|
| `GET /api/job-types` | Shared allowed job catalog |
| `POST /api/schedules/preview` | Resolve next five occurrences without saving |
| `GET /api/schedules` | Paginated definitions |
| `POST /api/schedules` | Idempotent creation; UUID Idempotency-Key required |
| `GET /api/schedules/{id}` | Definition and current ETag |
| `PUT /api/schedules/{id}` | Edit using If-Match |
| `POST /api/schedules/{id}/pause` or `/resume` | Conditional state change |
| `DELETE /api/schedules/{id}` | Conditional soft delete |
| `POST /api/schedules/{id}/run-now` | Idempotent manual admission |
| `GET /api/schedules/{id}/runs` | Paginated history |
| `GET /api/schedules/{id}/runs/{runId}` | Individual chain |
| `POST /api/schedules/{id}/runs/{runId}/retry` | Idempotent recovery run |
| `POST /api/crawl-jobs/{id}/resolve-stalled` | Explicit operator-confirmed interrupted execution |
| `GET JobHost /health/schedules` | Scan heartbeat, errors, gate owner/stall information |

Stale If-Match returns 412; invalid state/overlap returns 409. Invalid input is
explicit. HTTP success means durably recorded, not execution completed.
Never return an empty list to conceal failed persistence.

## 10. Infrastructure and operations

Only add the Cosmos control container and update the existing API/JobHost images.
Precreate the container using Azure management-plane permissions; runtime retains
its existing managed identity. Include the container in canonical Cosmos Bicep.
No new Azure AI resources, keys, connection-string credentials, or Service Bus
changes. Do not redeploy unrelated dispatch infrastructure.

Always On is mandatory for JobHost. The live preflight found it disabled on both
API and worker; restore it during rollout and verify afterward.
`/health/schedules` must report degraded on stale scheduling progress, scan errors,
or a stalled execution gate. External monitoring should poll it independently;
endpoint availability alone does not provision an alert/action group.
The deployed JobHost has public network access disabled; monitor its health from
an authorized private network. Do not enable public ingress merely to probe it
from an operator workstation.

History, terminal jobs, and completion barriers are retained conservatively in the
initial implementation; no active/unresolved record expires. A coordinated 90-day
cleanup policy is a follow-up operational requirement: never enable independent
job/barrier TTL while a chain or recovery run can still reference them.

Costs are existing JobHost CPU/memory, additional Cosmos RUs/storage, and crawling/
LLM usage driven by frequency. No additional hosting charge. Busy schedules can
still require larger compute/Cosmos capacity; no unmeasured scale/SLA claim.

## 11. Failure behavior

| Failure | Expected result |
|---|---|
| API unavailable | Existing schedules proceed in JobHost |
| JobHost unavailable at due time | Durable schedule catches up on return |
| Two scheduling instances race | One atomic admission wins |
| Crash after admission before job creation | Pending step is rediscovered |
| Job created but acknowledgement lost | Re-read the same deterministic ID |
| Change Feed checkpoint precedes worker crash | Pending sweep rediscovers unclaimed work |
| Primary completes before scheduler restart | Reconcile terminal primary and prepare post-step |
| Primary fails/partial | Post-job never starts |
| Worker stops mid-handler | Stale chain/gate blocks; explicit operator recovery |
| Post-job succeeds before chain update | Reconcile its terminal state and close once |
| Container/network/RBAC failure | Visible degraded health, durable intent remains |

## 12. Acceptance, rollout, and limitations

Automated tests cover recurrence/DST/offsets, invalid input, durable admission,
conditional-write races, duplicate dispatch, lost acknowledgements, success-only
gating, missing jobs, stale execution, pause/delete, misfires, retries, and read
barrier capture. Integration/live tests must validate actual Cosmos transaction
semantics, session-token handoff, restart behavior, and deployed API/UI routes.

Run backend tests/build, frontend type-check/build/lint, and deployment preflight.
Deploy the opposite blue/green **worker** image first, with no active old execution,
then API/UI. Preserve rollback image references. Enable Always On, verify scheduler
health, create a controlled one-time chain, verify its correlated primary/post jobs,
exercise pause/edit/idempotency and negative gating, and clean up smoke schedules.
Do not inject Service Health/Teams/Wiki notifications.

Healthy-operation target: due admission and success-to-post admission within about
two minutes, separately from executor queue delay and crawl duration. These are
design objectives to measure, not a contractual SLA.

The initial release is single-region, serializes crawling, and requires operator
intervention for genuinely ambiguous execution. It does not claim that existing
B1 hosting is zone redundant. Further scale testing and external alert provisioning
must be recorded separately from successful smoke testing.

## 13. Research and alternatives

The accepted JobHost approach avoids new infrastructure while retaining durable
coordination. Its explicit downside is sharing worker downtime. Functions with a
Cosmos ledger would provide a separate clock but was rejected by the user.
Service Bus scheduled messages do not provide recurrence; Durable Functions adds
unnecessary orchestration infrastructure for the current two-step requirement.
An in-memory timer without a persisted ledger is insufficient.

First-party references reviewed 2026-11-03:

- Cosmos transactional batches: same-container, same-partition atomicity:
  https://learn.microsoft.com/en-us/azure/cosmos-db/transactional-batch
- Change Feed Processor: at-least-once delivery and delegate checkpointing:
  https://learn.microsoft.com/en-us/azure/cosmos-db/change-feed-processor
- Transactional outbox pattern:
  https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos
- Cosmos session consistency and token propagation:
  https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels
  and https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-manage-consistency
- Service Bus scheduling limitations:
  https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sequencing
- Alternative timer-host behavior:
  https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer
- Alternative durable recurrence:
  https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-eternal-orchestrations

## 14. Implementation and rollout verification (2026-11-03)

Deployed to the existing `az-radar-vnet-rg` API/UI and JobHost:
https://azr-api-x8c5i2.azurewebsites.net/schedules

- Updated PRD and implementation use JobHost only; no Function App or new hosting.
- 194 backend tests passed, including 21 scheduling cases. Solution build had zero
  warnings/errors; frontend type-check, production build and changed-file ESLint passed.
- Isolated infrastructure rollout created only `schedule-control`. Fresh API green
  and JobHost blue images were deployed; Always On was restored on both apps.
  Registry private access and managed identity were preserved.
- Live CRUD, UUID idempotency/conflict, ETag concurrency, pause/edit/resume,
  overlap rejection, history, and IANA/DST preview checks passed.
- Stopped JobHost before a one-time Azure Updates -> Blast Radius due time, then
  started it afterward. The durable ledger admitted exactly one occurrence.
  Primary checked 10,016 updates and succeeded; only then did Blast Radius start,
  check 73 retirement items, and succeed. Each job executed once. Completion
  barriers and parent/run correlations were persisted and consumed successfully.
- Scheduled-job deletion was rejected. The smoke schedule was soft-deleted after
  successful completion; diagnostic history remains, with no recurring smoke work.

This verifies the deployed happy path and restart-before-admission recovery,
not multi-replica load, interruption during a handler, or a fresh-process
primary/post barrier handoff. Negative transitions are covered by unit tests.
Independent private-network monitoring and coordinated retention cleanup remain
operational follow-ups as described above.
