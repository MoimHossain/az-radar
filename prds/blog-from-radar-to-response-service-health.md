# From Radar to Response: How CloudLens Learned to Deliver Azure Service Health into Teams

> _“Knowing that Azure changed is useful. Knowing that Azure is currently having a bad day — and
> getting that information to the right people — is operationally necessary.”_

In my last CloudLens article,
[From RSS Scraper to MCP-Powered Radar](https://moimhossain.com/2026/05/20/from-rss-scraper-to-mcp-powered-radar-how-azradar-grew-up/),
I wrote about how the project evolved from a slightly overconfident RSS scraper into a proper
Azure lifecycle intelligence platform.

MCP gave CloudLens better source data. Azure OpenAI turned that data into useful judgement. Azure
Resource Graph gave us blast radius. The lifecycle calendar gave us a way to answer the immortal
platform-engineering question:

> _“What is going to break next quarter, and who forgot to budget for it?”_

At the end of that article I mentioned the next major capability: a **Smart Notification Router**.

It turns out that sentence was hiding quite a lot of architecture.

The new capability is now running. CloudLens can register Azure subscriptions, ingest Azure Service
Health notifications through a private event pipeline, enrich and classify them, and send them as
standalone posts to independently configured Microsoft Teams channels through a tenant-managed app
called **CloudLens**.

This is the story of how we moved from **radar** to **response**.

> **Suggested hero image:** CloudLens Service Health Dispatch page beside a CloudLens notification in
> Microsoft Teams.

---

## Azure already had the signal. The enterprise did not have the operating model.

Azure Service Health is one of those services that looks simple until you try to operationalise it
across a large organisation.

Azure publishes personalised notifications into each affected subscription. A portal user can see
service incidents, planned maintenance, health advisories, and security advisories. Technically,
the information is there.

But at enterprise scale, “the information exists somewhere in the portal” is not the same as
“the right team has received it and is doing something about it.”

The obvious approach is to ask every workload team to create a Service Health alert:

1. Pick the correct subscription.
2. Choose the correct event types.
3. Create an action group.
4. Point it at email, a webhook, a workflow, or something somebody promises to keep alive.
5. Repeat this for every subscription.
6. Hope nobody changes teams.

That model works beautifully in diagrams.

In real organisations, it produces 83 slightly different alert rules, three expired webhook
owners, one channel called `Azure Alerts FINAL v2`, and a spreadsheet explaining which
subscriptions might be covered.

I wanted the opposite model:

> **The platform team owns collection once. Workload teams should not need to understand Azure
> Monitor wiring to receive a useful Service Health notification.**

---

## The ingestion decision: do not build another public webhook

The first design question was how Azure should send Service Health data to CloudLens.

A webhook sounds easy. It also creates exactly the conversation I did not want to have in a
regulated enterprise:

- Is the endpoint public?
- Which WAF protects it?
- Who rotates the secret?
- What happens while the receiver is down?
- How many alert rules and action groups do we need?
- Can we replay yesterday's events?

So CloudLens does not expose a new public Service Health webhook.

Instead, registering a subscription causes CloudLens to configure a subscription-level
**diagnostic setting** that exports only the Activity Log's `ServiceHealth` category to a central
**Azure Event Hub**.

```text
Registered subscription
    -> Activity Log: ServiceHealth
    -> Azure Event Hubs
    -> CloudLens ingress worker
```

This has a few useful consequences:

- Azure can continue producing events while an CloudLens worker is restarting.
- Event Hubs gives us a replay window.
- The platform team can verify whether each subscription is configured correctly.
- Workload teams do not create alert rules.
- The CloudLens consumer reaches Event Hubs through private networking.

Subscription onboarding still requires explicit permission. A dedicated managed identity is given
the narrow rights needed to create and verify the CloudLens diagnostic setting. CloudLens does not
quietly give itself tenant-wide Contributor and wander around the estate configuring things.

That is less magical than “click once and monitor everything.”

It is also much more acceptable to the people whose job involves asking what “everything” means.

> **Suggested screenshot:** Register subscription tab showing an active diagnostic setting and the
> synthetic event selector.

---

## One event stream in, a stable event model out

Azure Activity Log payloads are flexible. “Flexible” is a polite architecture word meaning
“different event shapes will eventually arrive at 2am.”

The ingress worker therefore does not send the raw payload directly to Teams. It:

1. Validates and normalises the Azure event.
2. Classifies it as a Service Issue, Planned Maintenance, Health Advisory, or Security Advisory.
3. Generates a stable identifier and content hash.
4. Rejects duplicate event versions.
5. Persists the event in Cosmos DB.
6. Asks Azure OpenAI for a concise operational summary and recommended action.
7. Evaluates the configured channel routes.
8. Creates one durable delivery intent per matching destination.

The important bit is step six **and** step seven.

The LLM improves communication, but it is not the bouncer at the door.

An active Service Issue does not disappear because a model timed out. A security advisory does not
get downgraded because a generated summary sounded calm. Azure facts remain facts; AI output is
additive and labelled.

This follows the same principle as the rest of CloudLens:

> **Use AI for judgement where judgement helps. Do not use AI to replace the controls that keep the
> system safe.**

---

## Routing that a human can explain

I considered building a flexible rule engine immediately:

```text
IF service = App Service
AND region IN (West Europe, North Europe)
AND subscriptionTag.businessCriticality = high
AND localTime NOT BETWEEN ...
THEN ...
```

That would have been impressive.

It would also have delayed the useful version by several months and created a new domain-specific
language for somebody to debug during an incident.

The implemented routing model is deliberately straightforward. Every registered CloudLens channel
selects any combination of:

- Service Issues
- Planned Maintenance
- Health Advisories
- Security Advisories

That is enough to create practical operating channels:

| Teams channel | Receives |
|---|---|
| Azure Service Incidents | Service Issues |
| Platform Maintenance | Planned Maintenance |
| Azure Governance | Health Advisories and Planned Maintenance |
| Restricted Security Operations | Security Advisories |

There is no separate “enabled” switch fighting with the checkboxes. A registered channel with at
least one selected event family receives notifications. Selecting none pauses it.

Simple rules are not a lack of ambition. They are a product decision: the first version should be
easy to operate and impossible to misunderstand.

> **Suggested screenshot:** CloudLens channel routing showing different event-family combinations.

---

## Why CloudLens is a Teams app, not a workflow URL

The earliest design explored Teams Workflows and Key Vault references. Technically, a workflow can
accept an HTTP request and post a card.

Operationally, that would make the notification path depend on:

- A URL that behaves like a secret.
- A workflow owned by one or more users.
- Ownership and lifecycle rules outside CloudLens.
- A configuration page full of secret references that no product owner wants to explain.

We removed that model.

CloudLens is now a tenant-managed Teams application backed by Azure Bot Service. Install it in a
channel, send the registration message, and the Bot Gateway captures the stable Team and channel
identity.

CloudLens then resolves the friendly names so the routing page can display:

```text
Cloud Platform / Planned Maintenance
Channel ID: 19:abc...xyz
```

The name helps the human. The stable ID protects the system when somebody renames the channel to
`Planned Maintenance - NEW`.

The app says only that the channel has been registered. The actual event-family policy remains in
CloudLens, where it can be reviewed and changed centrally.

---

## The “why are all alerts replies?” incident

Building proactive Teams notifications gave me one of my favourite examples of a feature being
technically correct and visibly wrong.

The first CloudLens notifications arrived. The cards looked good. Delivery was successful.

They were also all replies underneath the old message used to register the channel.

From the bot's perspective this made sense: we had stored a conversation reference, so continuing
that conversation meant replying to the registration thread.

From an operator's perspective it was terrible. A new Azure incident looked like comment number
six under a forgotten setup message.

The fix was not CSS, Adaptive Card formatting, or a Teams setting. The dispatch worker had to
create a **new channel conversation** for every notification, using the stored channel identity but
not the registration message ID.

Now every Service Health notification appears as a standalone channel post.

It is a small implementation detail with a large product effect:

> **Operational events must look like operational events.**

> **Suggested screenshot:** Before-and-after Teams view showing a threaded registration reply and
> the final standalone CloudLens post.

---

## Durable delivery: “queued” is not the same as “delivered”

Another tempting shortcut was to let the ingestion worker call Teams directly.

That works until Teams returns a 429, a channel has been removed, the bot reference is stale, or
Microsoft has a transient outage at exactly the same time Azure is having an incident.

Instead, CloudLens writes a **delivery intent** to Cosmos DB. An outbox worker publishes the intent to
Azure Service Bus. A separate Teams delivery worker claims it, renders the card, sends it, and
records the outcome.

```text
Service Health event
    -> delivery intent
    -> Service Bus
    -> Teams delivery worker
    -> CloudLens channel post
    -> persisted delivery result
```

The distinction matters:

- `ready-for-dispatch` means the event matched at least one route.
- `queued` means delivery work reached Service Bus.
- `dispatching` means a worker owns the attempt.
- `delivered` means Teams accepted it.
- `retry-scheduled` means the failure may recover.
- `dead-lettered` means the failure is terminal or retries were exhausted.

One event can have multiple intents. The incident channel might succeed while the security channel
fails because CloudLens was removed. CloudLens keeps those outcomes separate.

That is the difference between a notification demo and a notification service.

> **Suggested screenshot:** Pending delivery intents tab with status and failure information.

---

## The six dead letters that were not a production incident

During testing, the Service Bus dead-letter queue contained several messages. At first glance that
sounds worrying, especially when you are building an incident notification system.

The forensic trail told a much less dramatic story:

- All six records were synthetic test events.
- Five targeted an abandoned pilot workflow destination that the bot-only dispatcher could never
  deliver to.
- One older test record had retried before a Teams conversation deserialisation fix.
- The real Planned Maintenance event visible in the UI had not dead-lettered; it simply had no
  matching route.

That investigation reinforced why delivery state must be durable and why test events must be
clearly marked.

If all you have is a Teams channel, you know only what arrived.

If you have an event record, route decision, delivery intent, attempt history, and dead-letter
reason, you can explain what happened.

We subsequently removed the abandoned workflow model and the old pilot configuration so it could
not create more false failures.

---

## The boring App Service setting that stopped ingestion

At one point I sent a synthetic event and received the reassuring message:

```text
Synthetic ServiceIssue event ... was published.
```

Then nothing appeared in recent events, and nothing arrived in Teams.

The event had reached Event Hubs. The code was fine. The credentials were fine.

The JobHost App Service had **Always On disabled**.

An HTTP application can tolerate being unloaded while idle. A continuous Event Hub listener
cannot. The worker had quietly gone to sleep because nobody was browsing it.

Enabling Always On and restarting the worker was enough. Event Hubs still had the event, so the
listener resumed and processed it.

This is not the glamorous part of cloud architecture, but it is an important one:

> **Durable messaging can save an event, but only if the consumer eventually wakes up.**

Always On is now enforced in the infrastructure definition for every continuously running App
Service.

---

## The administration experience

The Service Health Dispatch page ended up with three tabs:

### Register subscription

This is where an operator adds a subscription, verifies its diagnostic setting, sends a synthetic
event, unregisters watching, and configures CloudLens channel routing.

### Recent ingested events

This shows what CloudLens actually received and understood:

- Event family
- Azure service and region
- Tracking ID
- Synthetic marker
- AI summary
- `ready-for-dispatch` or `no-route`

### Pending delivery intents

This shows the external delivery work and its failures. Terminal records can be removed
permanently, but active in-flight delivery cannot be deleted from underneath a worker.

The distinction between these tabs turned out to be important. An event is not a delivery, and a
route is not a receipt.

Putting everything into one table would have made the UI shorter and the operating model much more
confusing.

> **Suggested screenshot:** The three-tab Service Health Dispatch page.

---

## The architecture, in one picture

```text
Azure subscriptions
    -> Activity Log ServiceHealth
    -> Event Hubs
    -> CloudLens ingress + AI enrichment
    -> Cosmos DB event and delivery ledger
    -> Service Bus
    -> Teams delivery worker
    -> Azure Bot Service
    -> CloudLens in Microsoft Teams
```

A few principles survived contact with reality:

- **Zero application keys.** Managed identities are used across Azure services.
- **Private core, minimal public edge.** Event processing and data services remain private. Only
  the Bot Gateway needs a public callback surface for Azure Bot Service.
- **At-least-once means idempotent.** Stable IDs and content hashes protect every stage.
- **AI is additive.** An LLM failure does not suppress an incident.
- **Delivery is a state machine.** “I put something on a queue” is not a success metric.
- **Names are for people; IDs are for routing.**
- **No `latest` Docker tag.** Past me remains banned from making that decision.

---

## What this capability changes

The previous version of CloudLens answered:

> _“What is changing in Azure, and does it affect our estate?”_

Service Health adds a different time horizon:

> _“What is Azure telling us right now, which subscriptions are involved, and did the right
> operational channel receive it?”_

Those are complementary signals.

Azure Updates and Microsoft Learn help a platform team prepare for lifecycle change. Resource
Graph measures the estate impact. Service Health covers active incidents, planned platform work,
and advisories. CloudLens closes the loop by putting the signal where teams already collaborate.

The result is no longer just a radar screen. It is the beginning of an operational control plane.

---

## What comes next

The current routing model is intentionally central. A channel chooses event families, and those
families apply across registered subscriptions.

The next useful step is ownership-aware routing:

- Map subscriptions and resources to accountable teams.
- Use service and region context.
- Route critical events immediately.
- Coalesce low-urgency advisory updates into digests.
- Update or resolve an existing Teams activity as the Azure incident evolves.
- Reconcile the event stream with Azure Resource Graph.

The durable event and delivery model is already in place, so those features do not require another
notification architecture. They are policy and product layers on top of the same foundation.

The lesson from this iteration is similar to the MCP lesson from the previous article:

> The answer was not a bigger prompt or another clever card template. The answer was putting the
> right durable boundaries around the AI.

Event Hubs protects ingestion. Cosmos DB protects state. Service Bus protects delivery. Managed
identity protects credentials. CloudLens gives Teams a governed destination. Azure OpenAI makes
the message easier to understand.

Each component has one job, which is probably why the whole thing finally works.

If you are building a central Azure platform and have ever discovered a Service Health incident
because somebody pasted a portal screenshot into a chat, the source is on GitHub:
[`MoimHossain/az-radar`](https://github.com/MoimHossain/az-radar).

And if the last version of CloudLens grew teeth, this version learned when — and where — to bite.
