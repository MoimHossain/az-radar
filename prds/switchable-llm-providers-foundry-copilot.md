# PRD and Research Assessment: Switchable Foundry / Copilot Intelligence

> **Status:** Implemented and deployed; final Copilot scheduled-job verification in progress (2026-11-03)
> **Research date:** 2026-11-03
> **Product:** AzRadar
> **Scope:** UI-selectable LLM provider for existing intelligence features
> **Implementation/deployment:** See `.azure\deployment-plan.md` for current rollout evidence.

## Approved initial scope (supersedes research alternatives below)

The user approved Settings -> **AI settings**, with an **AI provider** dropdown:
**Foundry Endpoint** (existing default) or **GitHub Copilot**. Both show a prompt
textbox, an explicit Test button, and the actual inference result. Testing a
candidate must not activate it; Save / Apply changes future executions.

Copilot onboarding uses a **user-owned fine-grained PAT** (`github_pat_`) with
the **Copilot Requests account permission**, not a classic PAT, GitHub App,
OAuth redirect, or organization-installation preview. No extra repository/write
permissions are required merely for inference. The token owner needs permitted
Copilot access/model usage; scheduled execution uses that same entitlement and
budget without requiring a browser session. PAT expiry/revocation requires
manual replacement, not automatic refresh.

PAT submission is write-only. Store it in Azure Key Vault via UAMI and keep
only secret references/nonsecret configuration in Cosmos. No API, including
the generic config API, can retrieve the PAT. Never echo it in save/test
responses, logs, images, browser storage, or jobs.

The user explicitly accepted **no authentication/authorization for this initial
round**; customers will restrict/disable dashboard access using network settings.
Implement global request limits and sanitized errors. This is a deliberate demo
limitation: write-only storage alone does not stop unauthorized configuration
changes or billable tests using a saved credential. Production must establish
an access boundary before exposing management to untrusted networks.

Preserve Foundry/UAMI as default, all four analyzer operations, Service Health
fallback delivery, and success-only scheduled post-jobs. Pin the selected profile
for an executing job/chain; no silent account/model/provider fallback.
Use SDK/runtime versions satisfying organizational package-age/feed approval.
Live successful Copilot testing needs an eligible PAT supplied through the UI
or another approved secret channel, never borrowed ambient operator credentials.
The pinned SDK labels even its deny-only permission-decision factory experimental.
The implementation scopes this diagnostic suppression to that rejection handler;
it does not enable experimental structured output or organization authentication.

The following sections retain the broader source-backed research. OAuth and
organization-owned GitHub Apps are future alternatives, not requirements for
this release; their proposed authenticated UI is superseded by this explicit
initial-scope decision.

## Implementation and live verification

The initial release implements the AI settings page, dedicated settings/test
API, versioned Key Vault credentials through UAMI, immutable Cosmos profiles,
and profile snapshots for manual jobs, scheduled occurrences, post-jobs and
retries. Test does not save credentials or activate a candidate. Save / Apply
verifies inference before activation. Settings responses never expose the PAT
or its vault version reference; the generic configuration API rejects AI keys.

Copilot runs inside the existing API and JobHost containers with pinned
`GitHub.Copilot.SDK` 1.0.22 and runtime 1.0.98. Each request owns an isolated
runtime/session with explicit credentials, no ambient operator login, no tools,
and denied permission requests. A single-flight limiter and request deadlines
bound inference; cancellation forcibly stops the owned runtime before releasing
capacity. Native Linux assets and third-party notices are verified during builds.

Foundry retains its existing managed-identity client and request options.
Live testing caught an Azure.AI.OpenAI 2.1 compatibility issue: adding
`MaxOutputTokenCount` causes that SDK to serialize legacy `max_tokens`, rejected
by the deployed GPT-5.1 model. The implementation therefore preserves the prior
Foundry options rather than adding that cap; a wire-level regression test covers
the behavior, and the two-minute request deadline still applies.

Verification completed on the deployed application:

- Shared suite: 214 passed; solution build: zero warnings/errors; frontend
  type-check, changed-file lint, and production build passed.
- Real Foundry inference passed through both the API and rendered UI. Test-only
  semantics, verified settings persistence, stale-update rejection, request-size
  limits, input validation, foreign-origin rejection, and rate limits passed.
- A scheduled Foundry job and success-only post-job both completed with real
  inference, retaining the same immutable AI profile and correct parent linkage.
- Real Copilot GPT-5.4 inference passed through the API. After explicit user
  approval, the PAT was saved to Key Vault and inference succeeded without
  resubmitting it. The rendered UI reloaded the Copilot selection and showed only
  a configured marker with an empty password field.
- Copilot is now active at the user's explicit request. One scheduled Copilot
  Blast Radius scan checked all 73 items but finished partial because verbose
  model output was submitted as KQL. Query generation now requires a validated
  JSON envelope with an executable query or explicit null; invalid output is
  never silently treated as a skip. Six regression cases cover this contract.
  The correction is deployed and an explicitly approved second scheduled scan
  is being verified; its final outcome is recorded in the deployment plan.
  No PAT values are included in this repository.

## 1. Executive assessment

**Yes, this is technically feasible.** AzRadar can retain its current Microsoft
Foundry / Azure OpenAI integration and offer a Copilot-backed provider. The
Copilot SDK has a .NET implementation compatible with our .NET 8 application,
supports backend workers, and can run its runtime inside a container. A customer
does not need to provision their own Foundry resource to use Copilot-hosted
inference. [S2][S3][S4]

However, this is **not just changing an endpoint**, and a person's Copilot
subscription is **not automatically an organization-wide unattended inference
account**. Three separate decisions must be made:

1. **Inference provider:** Foundry or Copilot.
2. **Execution identity and payer:** an authorizing user, or an organization-owned
   GitHub App installation.
3. **Customer approval:** permitted data, models/hosting, automation, budgets,
   preview features, and redistribution.

**Recommended decision:** approve a bounded feasibility prototype and a
provider-neutral design. Keep Foundry as the existing default. For customers
without Foundry, support explicitly authorized delegated Copilot connections
where appropriate; evaluate organization-owned Copilot authentication as the
better architectural fit for nightly shared workloads.

**Important release gate:** organization installation-token authentication is
currently documented as **public preview**, even though the SDK itself is
documented as generally available. A customer requiring GA-only dependencies
cannot be promised that this unattended organization-authentication path is
production-approved. [S2][S7]

Do not silently fall back to Foundry when Copilot fails: that could violate the
very policy this feature is intended to respect.

## 2. What the research verified

| Question | Finding as of the research date | Consequence |
|---|---|---|
| Was the SDK renamed? | GitHub announced the transition to **Microsoft Copilot SDK** on October 28, 2026. Package names/namespaces and repository migration are phased. [S1] | The user's recollection is correct. Use a neutral product label, but install verified existing packages, not an assumed renamed package. |
| Is this the Microsoft 365 Copilot SDK? | The announcement describes a shared Copilot harness direction; the inspected package remains `GitHub.Copilot.SDK` with `GitHub.Copilot` namespace. [S1][S3] | Do not assume GitHub and Microsoft 365 entitlements, authentication, or billing are interchangeable. |
| Is a .NET rewrite necessary? | No. The published 1.0.22 package and source target .NET 8 / .NET Standard 2.0. [S3] | No Python or Node service is required solely for SDK language support. |
| Is the SDK still only a technical preview? | The inspected README states GA and semantic versioning. The latest stable release observed was 1.0.28. Some individual APIs and authentication paths remain experimental/preview. [S2][S7][S8][S9] | Assess maturity per feature, not by the SDK's top-level label. |
| Can it run without a desktop session? | Yes. Official guides cover backend APIs, background workers, a managed child process, and externally hosted headless runtimes. [S4] | Container hosting is a supported architecture, not UI automation. |
| Does containerizing it host the LLM locally? | No. The container runs the agent runtime; Copilot-hosted models are still remote. Local-model capabilities are a separate option, not provided by a Copilot subscription alone. [S2][S12] | No air-gap or customer-VNet-only inference claim. |
| Must the customer buy Foundry? | Not for the standard Copilot authentication path. BYOK is a different path using an independently supplied model provider. [S5][S6] | Use Copilot credentials, not Foundry BYOK, for the customer's stated scenario. |
| Is all Copilot inference Foundry-free? | No such blanket guarantee. GitHub explicitly documents cloud-hosted MAI Code 1.1 Flash as served on Azure AI Foundry in GitHub's tenant. Other models have different hosts. [S12] | Clarify what the customer's Foundry prohibition actually covers. |
| Can unattended work avoid an employee's identity? | Official server-to-server docs describe organization-installed GitHub Apps using short-lived installation tokens and organization billing. This authentication is public preview and subject to enablement/policy. [S6][S7] | Preferred service identity in principle, conditional production eligibility in practice. |
| Is usage free because the customer has Copilot? | No. Usage is metered; allowances, model prices, organization pools, spending controls, and throttling apply. [S10][S11] | Budget and observe actual inference, not just subscription seats. |
| Can we give each GitHub App its own GitHub hard budget? | Current billing/admin docs say no individual-app or installation budget. Organization controls apply. [S7][S11] | AzRadar guards are necessary, but must not be represented as an authoritative per-app GitHub billing cap. |
| Is GitHub Models a simpler alternative? | No for a new implementation: official docs say GitHub Models and its inference API were fully retired July 30, 2026. [S18] | Do not propose old `models.github.ai` tutorials as a supported solution. |

### Evidence quality and freshness

This assessment used first-party GitHub/Microsoft documentation, the published
NuGet package page, release metadata, and SDK source. Search snippets were used
for discovery, not as the final evidence where canonical documents were
available. SDK `main` was inspected at revision
`da61ce3f0cd3e31713f3f7e0f62b37a29d25f448`.

This was a documentation/source assessment, not an authenticated SDK prototype.
No customer entitlement, organization enablement, real inference quality,
throughput, or cost was tested. Those remain explicit prototype acceptance gates.

There is a material documentation/version difference:

| SDK documentation | Organization-app permission and token transport described |
|---|---|
| **v1.0.22** | Repository `Copilot Requests: Read & write`, `copilot_requests: write`, repository ID, All repositories installation access, installation token through runtime environment; warns against explicit SDK user-token option. [S20] |
| **v1.0.28 / current admin docs** | Organization `Copilot Requests: Read-only`, `organization_copilot_requests: read`; current SDK guide uses the explicit SDK token option for managed runtimes. [S6][S7] |

These are **not interchangeable recipes**. Do not copy newer authentication code
into an older approved package, request broad repository access merely to make
an old example work, or interpret current "Read-only" permission as free of
spending authority. Validate the selected SDK/runtime/service combination.

## 3. Clarify the customer's restriction before choosing Copilot

There are at least three different requirements hidden in "not allowed to use
Foundry":

| Actual restriction | Does Copilot address it? |
|---|---|
| Cannot create, pay for, or administer Foundry resources in their Azure subscription | Usually the intended fit: Copilot manages model access; no customer-owned Foundry deployment is needed. |
| Cannot send these workload inputs to Foundry or certain underlying model hosts, even indirectly | Not established by selecting Copilot. Inspect hosting for the chosen model and obtain explicit approval. [S12] |
| Cannot send operational/customer data to any external inference service | Standard Copilot-hosted inference does not solve this. A separately approved self-hosted model path would need separate design and capacity. |

Having approval to use Copilot in an IDE does not by itself prove approval to
send Service Health communications, repository diffs, subscription context, or
other operational data through an automated application.

**No implementation should market this as a compliance bypass.** The UI should
say "No customer-owned Foundry resource required", not "Foundry is never used".
Avoid unapproved automatic model routing: different models can imply different
hosts, regions, retention terms, and usage costs. [S12][S13]

## 4. Current AzRadar integration and affected surfaces

The existing application already has a useful domain boundary:

| Existing code | Finding / implication |
|---|---|
| `src\AzRadar.Shared\Interfaces\ILlmAnalyzer.cs` | Four operations: feed analysis, documentation-change analysis, Service Health analysis, and ARG KQL generation. All must remain provider-switchable. |
| `src\AzRadar.Shared\Services\LlmAnalyzerService.cs:13-20` | Concrete dependency on `OpenAI.Chat.ChatClient`; prompts, deserialization, and domain fallbacks are combined with transport. |
| `src\AzRadar.Shared\ServiceCollectionExtensions.cs:50-62` | Singleton Azure OpenAI client authenticates through UAMI and resolves the configured deployment. This must not be required when running Copilot-only. |
| `src\AzRadar.Shared\Configuration\OpenAiSettings.cs` | Endpoint, deployment name, and UAMI client ID are deployment settings, not a dynamic provider profile. |
| `src\AzRadar.Api\Program.cs:81-119` | LLM health check directly resolves an OpenAI client and calls Foundry. It must become provider-aware. |
| `src\AzRadar.Api\Program.cs:1139-1177,1252` | Generic app-config endpoints persist values and only specially mask the existing GitHub PAT key. They are not a safe OAuth-token or GitHub App-key vault. |
| `src\AzRadar.Shared\Services\ServiceHealthEventProcessor.cs:30-34` | Service Health also calls the analyzer. A "crawling jobs only" switch would be incomplete. |
| `src\AzRadar.Shared\Services\AzureDevOpsWikiService.cs:28-50` | Existing prior art accesses Key Vault via managed identity for external-service credentials. Reuse the mechanism/pattern, not Wiki-specific token storage. |
| `src\az-radar-ui\src\pages\BlastRadiusConfigPage.tsx` | Existing FluentUI configuration page pattern, but provider connection/authentication needs a richer flow. |
| `prds\job-schedules.md` | Durable primary/post chains, explicit success outcomes, global crawl execution gate, and operator recovery must remain intact. |

Existing intelligence expects constrained structured output, not autonomous
repository editing. The Foundry implementation requests JSON for analysis and
plain KQL or `SKIP` for ARG generation. Provider switching must preserve these
contracts, watchlist semantics, and success-only chaining.

The SDK can do substantially more than we need. We should deliberately expose
**less**: bounded inference with no shell, file modification, browsing, MCP,
plugins, autonomous sub-agents, or runtime-owned scheduling.

## 5. Options assessment

### A. Keep direct Foundry with UAMI

Keep the current Azure OpenAI chat-completions path as a provider adapter.
Preserve managed identity, private networking where configured, and the
existing deployment name. Do not route Foundry through Copilot BYOK merely to
unify the implementation: that adds a runtime and may change authentication
without solving a customer requirement.

**Best for:** existing deployments with approved Foundry infrastructure.

### B. Copilot using an authorizing user's access

Official SDK documentation supports GitHub OAuth/user tokens, fine-grained PATs,
and stored CLI sign-in. A customer can authorize an application to use Copilot
as that user. A browser-based connection flow is the preferred product UX; a
developer's locally logged-in CLI is only a development convenience. [S5]

**Advantages:** directly addresses customers who currently have individual
Copilot access; no customer-owned Foundry resource.

**Constraints:**

- License/access, permitted models, budget, identity, and organization policy are
  those of the authorizing user; GitHub login alone is not proof of usable Copilot.
- Explicitly authorize unattended use if this connection will own nightly jobs.
  Tokens must outlive browser sessions through an approved stored/refreshable
  grant. Lifetimes and refresh support depend on the grant; do not assume all
  OAuth tokens are permanent or all expire after the same interval.
- Revocation, seat removal, account departure, organization access changes, and
  disabled models can stop scheduled inference. Surface "Reconnect required".
- A connection owned by one employee is not a license pool for arbitrary users
  or tenants. Do not rotate through employees' tokens to bypass limits.
- Prefer customer-managed Business/Enterprise access for organizational data.
  Personal-plan data policies can differ, including model-training settings.
  Do not assume Business/Enterprise protections apply to a personal subscription.
  [S12][S16]

**Assessment:** viable for explicitly delegated use and a bounded prototype.
Conditional fit for shared unattended production; identity ownership and
offboarding are substantive operational dependencies.

### C. Copilot using an organization-installed GitHub App

For service workloads, the current official design supports a GitHub App
installed on an organization, using installation access tokens and organization
billing without a user signing in. Current docs specify organization
`Copilot Requests: Read-only`; despite its name it authorizes spend. Installation
tokens expire after one hour. [S6][S7]

**Advantages:** independent of an employee's browser session; clear customer-owned
organization, installation, model policy, and payer.

**Constraints / gates:**

- This authentication is **public preview**. Require customer preview approval.
- Current guidance applies to organization installations, not personal-account
  or enterprise installations. An enterprise-managed deployment must select the
  appropriate organization and have its administrators approve the installation.
- Organization/service enablement must be verified, including any documented
  "not enabled" response; token creation alone does not establish entitlement.
- The GitHub Actions **Allow use of Copilot CLI billed to the organization**
  policy is a different authentication path. Do not require it merely because
  AzRadar runs in an App Service container. [S7][S11]
- Mint/renew short-lived tokens using a customer-approved GitHub App credential.
  A GitHub App signing credential is still an external-service secret, even if
  managed identity securely accesses it from Key Vault.
- Current server-to-server docs prescribe restarting the managed client/runtime
  to replace its installation token. Rotate before expiry at safe session
  boundaries; do not assume a token-refresh callback works identically for this
  token type just because it exists for user sessions.
- No individual-app GitHub budget exists. Apply organization spending controls
  and AzRadar admission limits; reconcile usage using the documented app
  attribution in CSV exports. [S7][S11]

**Assessment:** preferred identity architecture for shared nightly jobs, but
**conditional go**, not an unconditional production recommendation.

### D. Alternatives that are not substitutes for this requirement

| Option | Assessment |
|---|---|
| Copilot SDK BYOK | A runtime using another approved model provider, not a way to spend existing Copilot entitlement on that provider. Provider credentials/billing remain separate. [S2][S5] |
| Shared headless runtime with per-user sessions | Supported topology, not a replacement for authentication or isolation. More infrastructure and authorization than the first iteration needs. [S4][S14] |
| Experimental in-process Copilot runtime | Avoid initially. It removes the child process but adds another experimental integration surface without addressing entitlement. [S2] |
| Microsoft Agent Framework | An orchestration framework with model/agent integrations, not a Copilot entitlement or an inference service. No requirement to adopt it for four bounded analyzer operations. [S19] |
| Microsoft 365 Copilot / Copilot Studio | Separate product/authentication/licensing questions. The SDK branding transition alone does not make these interchangeable with GitHub Copilot. Out of initial scope. |
| GitHub Models API | Retired, not a supported new fallback. [S18] |
| Self-hosted/local model | Potential separate option if external inference is prohibited; needs approved weights/license, suitable hardware, inference serving, and quality evaluation. Not "free compute included with Copilot". |
| Calling undocumented Copilot inference endpoints directly | Reject. Use supported SDK/runtime contracts rather than extracting internal tokens or imitating an IDE client. |
| Moving AzRadar scheduling into GitHub Actions | Unnecessary for this feature. Authentication examples for Actions are not a reason to replace the durable JobHost scheduler. |

## 6. Proposed application architecture

### 6.1 Preserve the domain analyzer; replace the completion boundary

Keep `ILlmAnalyzer` as the contract used by the four job types and Service Health.
Extract a small provider-neutral completion interface beneath the shared prompts,
parsing, and domain validation.

Proposed responsibilities, with names illustrative rather than an implementation
commitment:

| Component | Responsibility |
|---|---|
| Shared analyzer | Build the existing prompts; validate domain JSON/KQL; apply explicit existing non-LLM fallbacks where appropriate. |
| Foundry adapter | Existing Azure OpenAI completion semantics and UAMI authentication. |
| Copilot adapter | Create a bounded, isolated runtime session; send the prompt; collect the final response and usage; cancel/drain/clean up. |
| Provider-profile resolver | Resolve an immutable approved profile and authorized connection for an execution. |
| Credential service | Resolve/renew credentials server-side without exposing them in configuration or job documents. |
| Runtime supervisor | Manage process readiness, token/client rotation, resource limits, shutdown, and recovery. |

Avoid duplicating the full analyzer and its prompts for each provider. Do not
force SDK output into an `OpenAI.Chat.ChatClient` by pretending Copilot is an
OpenAI-compatible REST endpoint.

Foundry initialization must be lazy/conditional: a Copilot-only deployment with
no Foundry endpoint must start, perform its selected-provider health check, and
run all supported intelligence operations successfully.

### 6.2 Recommended container topology

**Prototype / initial single-customer design:** use the .NET SDK in the existing
JobHost container and let it supervise a child runtime over stdio. This avoids
a public RPC port, a Python/Node bridge, and a new shared-runtime service.
Create a separate runtime in API only for API-side operations that genuinely
need inference or connection validation; do not share an in-memory object
between API and JobHost or make the worker call the public API to analyze items.

The .NET package can acquire its pinned runtime at build time. Target the actual
Linux runtime identifier, verify checksums, and include the runtime artifacts
in the final image. Do not rely on a workstation binary, login cache, runtime
downloads during container startup, or unpinned global installation. [S3][S4]

**Security boundary:** a child process in JobHost is not a hardened isolation
boundary from JobHost's managed identity, network, or filesystem. Empty mode and
tool denial restrict agent behavior, not arbitrary native-code execution.
Customers requiring a stronger boundary should use a separately deployed,
private inference broker/runtime with no Cosmos/ARG permissions, authenticated
application transport, and an explicitly reviewed network policy.

**Later scaling:** use a bounded pool of per-worker/customer runtimes before
considering a shared cross-customer runtime. A remote headless runtime needs
protected transport/admission and strong tenant authorization; private address
space alone is not authentication. A single shared runtime also creates a
failure domain. [S4][S14]

No evidence gathered here establishes sufficient spare memory/CPU on the
existing B1 plans. Measure startup, resident memory, inference concurrency, and
Service Health latency before promising unchanged hosting cost.

### 6.3 Inference-only session policy

For every Copilot session:

- Use `CopilotClientMode.Empty` or the verified equivalent for the selected
  version; isolated base directory and unique execution/session IDs.
- Explicit empty tool allowlist and deny-by-default permission handling.
- Disable ambient workspace instructions, plugins, skills, MCP, custom agents,
  shell/file/network tools, remote session sharing, and runtime scheduling.
- No `ApproveAll`, "allow all tools", or SDK defaults copied from tutorial agents.
- No interactive permission, question, or budget-extension prompts in a worker:
  reject and produce a visible typed failure.
- Provide only the source content required for the analyzer operation. Do not
  attach a repository checkout or mount application configuration/secrets as
  model context.
- Fresh session per independent analysis initially; no session history reused
  across customers, owners, or unrelated feed items.
- Deny unknown tools even if the model requests them. A malicious feed entry
  cannot authorize a command or change provider.

`Empty` mode, explicit tool selection, session identity, and state isolation
are documented SDK concepts. Specific behavior must be tested on the pinned
version. [S14][S20]

### 6.4 Output contract and model selection

Do not assume the Copilot runtime is a drop-in equivalent for Foundry's JSON mode,
temperature parameter, latency, or token limit behavior.

Analysis must validate required fields, enums, dates, confidence bounds, and
collection shapes with `System.Text.Json` plus domain validation. JSON that
deserializes is not necessarily a correct analysis.

The .NET SDK has a typed structured-output helper, but the inspected 1.0.28
source marks it experimental; it also states that deserialization is not full
JSON Schema validation. A normal prompt is not a guarantee of JSON output.
Evaluate explicit response schemas and the chosen model, and either approve the
experimental API or use the verified non-experimental surface with strict
application validation. Never hide the trade-off. [S8]

Preserve `SKIP` as a valid ARG-generation result, separate from model failure.
Copilot only generates the query; the existing managed-identity ARG client runs
it. Do not give the Copilot agent Azure credentials or direct ARG execution tools.

Discover model availability using the correct connection's authenticated catalog;
intersect it with the customer-approved allowlist. Do not assume Foundry
deployment name `gpt-4o` is an available Copilot model ID. Pin the approved model
in each execution; no automatic upgrade, Fusion/auto routing, or fallback to an
unapproved model/host.

## 7. UI and configuration proposal

Add **Settings -> AI Provider** using FluentUI v9.

### 7.1 Configuration UX

| UI area | Required behavior |
|---|---|
| Provider | Microsoft Foundry / Azure OpenAI, or Copilot. Show policy-disabled choices with an explanation. |
| Foundry settings | Existing endpoint/deployment/UAMI configuration; no model API keys. |
| Copilot connection | Clearly distinguish "Use an authorized user's Copilot access" and "Organization-owned automation (preview)". |
| Connect / reconnect | Server-side GitHub authorization flow or administrator-managed installation connection, not a request to paste a developer's CLI cache. |
| Ownership and payer | Display authorized owner/organization and the documented billing route. Never infer the payer solely from a username or token prefix. |
| Model | Available and approved models for this connection; show relevant hosting/region policy. |
| Connection status | Not configured, connected, test failed, reconnect required, organization not enabled, model blocked, budget blocked, or policy blocked. |
| Test connection | Explicit, authorized, rate-limited POST: small real inference with a disclosed possible charge, plus credential/model/output validation. |
| Apply | Validate and conditionally activate a configuration revision; explain which future executions will use it. |
| Operational status | Last successful check, sanitized last error, SDK/runtime versions, active profile revision, recent usage and unknown/unavailable quota distinctly. |
| Disconnect / revoke | Stop future admission for this connection; explain treatment of active work and customer-side revocation. |

A successful GitHub login or `/user` request is not a successful Copilot
inference test. Test the actual selected account, model, and transport from
both relevant application processes.

### 7.2 Safe configuration storage

Persist only nonsecret profile metadata in Cosmos: provider, profile revision,
model, logical connection ID, owner/organization binding, allowed usage, and
policy/budget settings.

Store OAuth/refresh credentials and any GitHub App signing credential in a
customer-approved secret store; access Azure Key Vault through UAMI. Do not put
them into the generic app-config API, browser storage, images, logs, prompts,
schedule snapshots, or job records. Do not reuse the existing repository-crawl
PAT as a Copilot connection; supported token types and permissions differ.

This preserves **no Azure API keys**. It does **not** make GitHub authentication
secretless. If a customer requires no stored external credential of any kind,
that is an additional feasibility gate, not something Azure UAMI alone solves.

Proposed dedicated management endpoints should cover profile get/update,
connection initiation/callback/disconnect, model discovery, and explicit test.
They must require proven administrator authorization, anti-CSRF/replay
protection where applicable, ETags, audit logging, and secret-redacted responses.
The inspected `Program.cs` does not establish an in-app authorization policy for
this new management surface; verify any platform authentication rather than
assuming the existing generic settings endpoints are sufficient.

### 7.3 Profile activation and scheduled chains

Use immutable revisions. Resolve and persist provider/model/connection metadata
for a manual job before inference, and for a scheduled occurrence at admission.
The primary and post-job use the **same occurrence profile** unless a future
explicit product option says otherwise.

Changing the UI default must not switch an executing job halfway through a feed,
or change the provider between successful primary and Blast Radius post-job.
API and worker must read the authoritative revision, not independently apply
unbounded stale singleton caches. A test result must be tied to the candidate
revision and invalidated by relevant edits.

Pin a logical identity/connection, **not an expiring access token**. Renewal may
replace a token for the same authorized identity; it must not replace the
owner/payer silently. Revocation or deletion must fail the pinned execution
visibly rather than fall through to another account.

For legacy jobs without provider metadata, preserve existing Foundry behavior
through an explicit migration/default policy. Retry and reanalysis must record
whether they retain the original revision or intentionally create a new execution
with a newly approved one.

## 8. Reliability and failure semantics

Keep Cosmos admission, deterministic job IDs, progress/heartbeat fencing, and
the current execution gate. Copilot is an inference dependency, not a new job
scheduler or the owner of Azure side effects.

| Failure | Required behavior |
|---|---|
| Token expired | Renew through the approved path before new work; at most a bounded same-identity recovery. If renewal fails, reconnect-required. |
| Access revoked, no entitlement, organization not enabled, model denied | Visible non-success; no repeated automatic login, account substitution, or Foundry fallback. |
| 429 / transient network or provider outage | Bounded retry with backoff/jitter and provider retry hints where available; checkpoint work and respect the execution deadline. |
| Credits/spending blocked | Stop admission for the connection and mark the affected execution explicitly; do not keep retrying until the billing cycle changes. |
| Missing/invalid structured response | Bounded repair only within the configured budget; otherwise explicit analysis failure, not a successful empty analysis. |
| SDK wait timeout / cancellation | Explicitly abort and drain the runtime turn; waiting cancellation alone may not stop inference. Do not release ownership while the old turn can still produce effects. [S4][S8] |
| Runtime exits / token-rotation restart | Supervised recovery; new sessions only after the previous instance is stopped. Preserve the existing job's outcome rules. |
| Ambiguous completion | Do not claim exactly-once inference or billing. A retried remote request can consume credits twice even if Cosmos result persistence is idempotent. |
| Provider change during a chain | Current execution retains its pinned revision. Policy revocation overrides continued use and causes explicit failure. |
| Primary fails or only partially analyzes | Preserve success-only chaining; do not dispatch the post-job. |
| Service Health enrichment fails | Preserve the authoritative event and existing non-LLM operational fallback/delivery semantics; mark enrichment degraded. Never downgrade or discard an incident because Copilot authentication failed. |

Existing analyzer fallback behavior needs explicit treatment during the refactor.
Returning zero-confidence/empty "success-shaped" output must not turn a provider
outage into a successful scheduled crawl or a silent watchlist discard.

Inference concurrency must be bounded across crawl and Service Health callers,
not only per job. Long crawls must not starve incident enrichment. Begin with a
small configured capacity and measure; SDK support for multiple sessions is not
a throughput reservation or permission to evade rate limits. [S10]

## 9. Security, privacy, licensing, and network findings

### 9.1 Data boundary

Data sent today includes public update text, repository-change context/diffs,
and potentially nonpublic Service Health communications. Error-driven ARG
regeneration can also include query/error details. Document and minimize these
payloads separately; do not label the entire dataset "public".

The application must enforce source authorization and data minimization before
it constructs the prompt. Do not assume IDE content-exclusion rules will remove
arbitrary text that AzRadar has already supplied as a prompt. [S15]

Copilot's model hosting, caching, retention, and training commitments vary by
model and plan. For example, current hosting docs contain model-specific
exceptions to general zero-retention descriptions. A blanket "Copilot stores
nothing" or "all users have enterprise data protection" statement is incorrect.
Review the selected model and the customer's actual agreement. [S12][S16]

Data-residency controls do exist for qualifying GitHub Enterprise Cloud
deployments, including documented regional model enforcement, but do not arise
merely because our container runs in an Azure region. Validate the customer's
enterprise, domain, account, model, runtime, and policy together. [S13]

Copilot sessions/transcripts can contain prompts and outputs on local disk.
Use isolated writable directories, explicit cleanup/retention, redacted logs,
and no remote-session publication. Session deletion is a local/product operation,
not proof that an upstream service retained no data.

### 9.2 Network

The runtime needs outbound access to GitHub authentication/API/Copilot endpoints
and applicable enterprise/residency routes. Use the official allowlist and test
the actual customer's proxy, TLS inspection/certificates, DNS, and plan-specific
routing. No Foundry resource does not mean no external egress. [S17]

Use stdio for the same-container initial implementation. Do not expose a
headless JSON-RPC port to the internet or assume a container image adds transport
authentication. If a broker is introduced, authenticate it and authorize requests
before they can select a connection, model, session, or secret reference.

### 9.3 Licensing and redistribution

The SDK source is MIT licensed; that is **not the license for every bundled
component or for Copilot service usage**. The Copilot CLI has a separate license.
It permits redistribution of unmodified copies as part of an application/service
with material functionality beyond the CLI, subject to retaining the license
and notices; it is not permission to redistribute the CLI as a standalone
primary product. Include these artifacts in the image's third-party notices
and review the exact pinned runtime license. [S2][S21]

Service access still requires the applicable entitlement and terms. GitHub-
purchased business service terms, Microsoft-purchased terms, and personal terms
differ. The inspected SDK support policy directs users to project issues and
does not itself establish an enterprise application SLA. [S16][S22]

Obtain customer/licensing review for unattended delegated use, shared outputs,
external-facing distribution, and any model-specific restrictions. This PRD
establishes technical support patterns, not a legal opinion or a blanket right
to resell another person's Copilot access.

## 10. Cost and operational controls

Current GitHub documentation uses token-based **AI credits**. Do not size the
feature using older blanket claims such as "one premium request per prompt",
"unlimited inference", or a historical monthly request count. SDK events can
report token/model/usage information, but some metrics/quota APIs are
experimental. [S9][S11]

Estimate consumption as:

`analyses requiring inference x measured tokens/analysis x approved model rates`

Then include retries, ARG-generation attempts, runtime/context overhead, explicit
connection tests, and any model/region adjustments. Enumerating 10,000 catalog
items does not necessarily mean 10,000 LLM calls: existing dedup/hash checks must
remain in front of inference.

Requirements:

- Per-execution call/input/output/deadline limits where the selected API supports
  them; connection-level admission controls and daily/monthly usage warnings.
- Record provider, model, connection/profile revision, prompt schema version,
  duration, retry count, and reported usage with results/diagnostics.
- Do not log full sensitive prompts or tokens by default.
- Distinguish unavailable quota/usage from zero remaining balance.
- Treat SDK session-credit limits as **soft**: the documented check occurs after
  a response and may overshoot by a call. Never advertise an exact hard currency
  ceiling from this mechanism. [S9]
- Configure appropriate GitHub organization/user spending controls, including
  hard-stop settings where applicable. Organization-app authentication has no
  dedicated per-app GitHub budget; app guards do not change that. [S7][S11]
- No quota splitting across users/accounts, unapproved model substitutions, or
  runtime fleet growth intended to defeat throttling.

## 11. Version and supply-chain proposal

Respect this repository's requirement to use approved packages with at least
two weeks of release age, including relevant transitive/runtime artifacts.

At research time:

| Candidate | Observed release | Assessment |
|---|---|---|
| SDK 1.0.28 | 2026-10-28; latest stable observed | Less than 14 days old. Its release references runtime 1.0.105-1, whose GitHub release is marked prerelease. Not an automatic production choice. |
| SDK 1.0.22 | GitHub release 2026-10-14; published NuGet package supports .NET 8 | GitHub release age qualifies as a candidate; its pinned CLI is 1.0.98, released 2026-10-14. Verify actual NuGet/dependency publication dates, organizational approval, and live compatibility before selection. |
| Current `main` / 1.0.29 previews | Actively changing | Research evidence, not a production dependency pin. |

The older candidate's organization-authentication recipe has different
permissions and token transport from current guidance. **Do not choose it solely
because its GitHub tag passes the age threshold**, and do not weaken permissions to fit it.
Prefer waiting for an aged, approved compatible release or using the
organization's approved feed/exception process. A feed proxy is not permission
to bypass approval.

Pin SDK + runtime together; verify Linux RID, checksums, protocol compatibility,
package license, and all required features. The .NET SDK supports a release mirror
or preinstalled runtime path for controlled builds. This matters because a NuGet
proxy alone may not approve the additional runtime archive fetched by MSBuild.
[S3][S20]

Use our existing private-image and blue/green deployment practices if this is
implemented later. No changes to the deployment are authorized by this PRD.

## 12. Proposed staged delivery and acceptance gates

### Phase 0: Customer feasibility decision

Record, without placing customer identities or secrets in this public repository:

1. Meaning of the Foundry prohibition, permitted payloads and model hosts.
2. Personal/delegated access versus approved organization-owned automation.
3. Organization-app preview eligibility/approval and selected billing entity.
4. App permissions, token storage/renewal, offboarding and security owners.
5. Approved model list, residency/network policy, budget and throughput target.
6. Accepted SDK/runtime versions, component licenses, support expectations.

**No-go:** external inference prohibited; no approved execution identity; no
approved model/hosting path; or required preview/package permissions rejected.
Do not work around any of these with a different token or secret endpoint.

### Phase 1: Isolated prototype, no production data

- .NET 8 Linux container, pinned approved SDK/runtime.
- Foundry unconfigured; exercise one synthetic analysis and one KQL request
  through Copilot. Prove no Foundry resource or credential is needed.
- Prove empty tool set, no ambient instructions, no external tools, no user
  prompts, isolated storage, and explicit abort/drain.
- Test authorized user authentication separately from organization-app
  authentication; verify actual organization enablement and billing attribution.
- Test credential expiry/renewal/revocation, denied models, proxy/egress, runtime
  termination, timeout, malformed JSON, and spending-block responses.
- Measure output validity, semantic quality, latency, resource footprint, and
  usage. A successful "hello" response is not enough.

**Exit:** actual supported-version evidence, customer approval, and sufficient
quality/performance. If organization-app preview is rejected, document whether
delegated unattended ownership is acceptable; otherwise stop.

### Phase 2: Provider abstraction and configuration

- Extract shared completion transport without duplicating domain prompts.
- Dedicated secured provider/connection management UI and API.
- Foundry remains backward-compatible; Copilot-only starts with no Foundry.
- Revisioned profile activation and job/chain provenance; no raw credential data
  in Cosmos or client responses.
- Provider-aware passive readiness plus explicit paid connection test; no public
  unauthenticated health route repeatedly triggering billable inference.

### Phase 3: End-to-end job and operational validation

| Test | Acceptance |
|---|---|
| Existing Foundry deployments | Same UAMI authentication and domain behavior; no new API keys. |
| Copilot-only deployment | No Foundry endpoint/client initialization, provisioning, or fallback calls. |
| Four analyzer operations | Feed, doc change, ARG generation, Service Health all use the selected provider. |
| Scheduled primary/post chain | Profile is pinned; post runs only on complete primary success. |
| Switch during execution | Current chain does not change model/owner/provider; future admission uses new approved revision. |
| Credential expires mid-crawl | Same-identity renewal or explicit safe failure; no lost heartbeat or silent data discard. |
| Provider/credit failure | Visible non-success and blocked post-job; existing Service Health non-LLM delivery preserved. |
| Timeout/runtime death | Turn is stopped/drained or quarantined before ownership is released; no orphan processing or claimed exactly-once billing. |
| User/tenant isolation | Cannot resume another owner's session or select another customer's connection; no shared prompt history. |
| Malicious source text | Cannot invoke tools, access secrets, change policy, or enable remote sharing. |
| Large crawl with concurrent events | Bounded resource use and measured Service Health enrichment delay; no invented QPS/SLA promise. |
| Restart/rotation | Durable jobs and schedule records remain authoritative; credentials are re-resolved, not embedded in snapshots. |
| Disconnect/offboarding | Future work stops for the revoked connection; status and operator remediation are clear. |

Use a representative approved evaluation set. Require schema validity on every
accepted result and zero tool execution; agree semantic-quality tolerances
against the current Foundry baseline before release rather than inventing a
universal accuracy percentage here.

### Phase 4: Controlled rollout

Feature-flag Copilot by deployment/customer. Pilot with bounded workloads and
explicit budget approval; observe before permitting nightly volume. Roll back
configuration for new work only to an **approved** provider. For a Foundry-banned
customer, rollback can mean disabling AI execution, not secretly restoring
Foundry. Do not redeploy unrelated dispatch infrastructure.

## 13. Open decisions and final recommendation

| Decision | Suggested position |
|---|---|
| Is the UI switch worth building? | Yes, after the prototype proves an approved auth/model path. |
| Default provider | Existing Foundry, unchanged for current customers. |
| Initial runtime | .NET SDK with supervised stdio child process; separate private broker if stronger isolation is required. |
| Customers with only personal Copilot access | Explicit delegated connection; no promise that one subscription powers an unlimited shared service. |
| Shared nightly customer automation | Prefer customer-owned organization installation identity, conditional on preview approval and actual enablement. |
| GA-only customer rejects organization-app preview | Evaluate approved delegated ownership; otherwise no-go until a suitable supported path is available. |
| Failure fallback | Never automatic across providers, identities, or unapproved models. |
| New orchestration framework / Function App | Not needed for this feature. |
| Immediate implementation or deployment | Not proposed by this research request. Review these gates first. |

**Bottom line:** containerized Copilot inference is a real, documented option,
and a provider selector can be added cleanly. The difficult part is establishing
a durable, approved identity/payer and preserving AzRadar's bounded,
success-aware processing semantics. Treat that as the first prototype milestone,
not an implementation detail deferred until after adding a dropdown.

## 14. Source register

All sources accessed 2026-11-03. Live pages can change; release-pinned examples
below deliberately preserve the authentication differences found in this review.

[S1]: https://github.blog/changelog/2026-10-28-github-copilot-sdk-is-becoming-microsoft-copilot-sdk/
[S2]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/README.md
[S3]: https://www.nuget.org/packages/GitHub.Copilot.SDK/1.0.22
[S4]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/docs/setup/backend-services.md
[S5]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/docs/auth/authenticate.md
[S6]: https://github.com/github/copilot-sdk/blob/v1.0.28/docs/auth/server-to-server-tokens.md
[S7]: https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-app-copilot-access
[S8]: https://github.com/github/copilot-sdk/blob/v1.0.28/dotnet/src/Session.StructuredOutput.cs
[S9]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/docs/features/session-limits.md
[S10]: https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/usage-limits
[S11]: https://docs.github.com/en/copilot/concepts/billing-and-usage/organizations-and-enterprises/billing
[S12]: https://docs.github.com/en/copilot/reference/ai-models/model-hosting
[S13]: https://docs.github.com/en/enterprise-cloud@latest/admin/data-residency/github-copilot-with-data-residency
[S14]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/docs/setup/multi-tenancy.md
[S15]: https://docs.github.com/en/copilot/concepts/security-governance-and-network-settings/content-exclusion
[S16]: https://docs.github.com/en/site-policy/github-terms/github-terms-for-additional-products-and-features#github-copilot
[S17]: https://docs.github.com/en/copilot/reference/copilot-allowlist-reference
[S18]: https://docs.github.com/en/github-models
[S19]: https://learn.microsoft.com/en-us/agent-framework/overview/
[S20]: https://github.com/github/copilot-sdk/blob/v1.0.22/docs/auth/server-to-server-tokens.md
[S21]: https://github.com/github/copilot-cli/blob/main/LICENSE.md
[S22]: https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/SUPPORT.md

Additional inspected first-party evidence:

- [GitHub Generative AI Services Terms](https://github.com/customer-terms/github-generative-ai-services-terms)
- [Current per-model pricing and billing units](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing)
- [SDK usage and billing metrics](https://github.com/github/copilot-sdk/blob/da61ce3f0cd3e31713f3f7e0f62b37a29d25f448/docs/features/usage-and-billing.md)
- [SDK release 1.0.28](https://github.com/github/copilot-sdk/releases/tag/v1.0.28)
- [SDK release 1.0.22](https://github.com/github/copilot-sdk/releases/tag/v1.0.22)
- [CLI release 1.0.98](https://github.com/github/copilot-cli/releases/tag/v1.0.98)
- [CLI release 1.0.105-1](https://github.com/github/copilot-cli/releases/tag/v1.0.105-1)
- [.NET 1.0.22 project target frameworks](https://github.com/github/copilot-sdk/blob/v1.0.22/dotnet/src/GitHub.Copilot.SDK.csproj)
- [1.0.22 pinned runtime version](https://github.com/github/copilot-sdk/blob/v1.0.22/nodejs/package.json)
- [.NET runtime download/build targets](https://github.com/github/copilot-sdk/blob/v1.0.22/dotnet/src/build/GitHub.Copilot.SDK.targets)
