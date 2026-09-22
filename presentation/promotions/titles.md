# CloudLens promo - titles and descriptions

Video: 1:39 | Repo: github.com/MoimHossain/az-radar
Pair each set with its matching thumbnail. Do not mix - the title is written
to add the keywords the thumbnail deliberately leaves out.

---

## A - pairs with `cloudlens-thumb-a` ("WHAT'S YOUR BLAST RADIUS")

### Title
Azure Is Retiring 63 Services. Are Any of Them Yours? (52)

**Alternates for A/B testing**
- Your Azure Estate Is Already Breaking. You Just Don't Know Yet. (62)
- I Built an AI That Finds Which Azure Retirements Kill My Estate (63)
- Azure Retirement Emails Are Useless. So I Replaced Them. (56)

### Description
Azure retires services constantly. The hard part was never the announcement -
it is working out whether it touches anything you actually run.

CloudLens ingests Azure's entire change landscape, classifies every item with
GPT-4o, then uses Azure Resource Graph to map each retirement onto the
resources deployed in your own subscriptions. Not "this service is retiring",
but "7 of your clusters, across 4 regions, in 1 subscription".

Built on .NET 8, Cosmos DB change feed, Azure OpenAI and Azure Resource Graph.
Zero keys anywhere - managed identity end to end.

CHAPTERS
0:00 Azure ships faster than you can track
0:22 What CloudLens does
0:38 The lifecycle calendar
0:52 Blast radius: which of YOUR resources are hit
1:05 Service Health, straight into Teams
1:18 Auto-published to the Azure DevOps wiki
1:28 Zero keys, end to end

LINKS
Source: https://github.com/MoimHossain/az-radar
Deep dive: https://moimhossain.com/2026/09/11/from-radar-to-response-how-cloudlens-learned-to-deliver-azure-service-health-into-teams/

#Azure #CloudComputing #PlatformEngineering #DevOps #AzureOpenAI #dotnet

---

## B - pairs with `cloudlens-thumb-b` ("63 RETIRING. 21 HIT YOURS. 1 DASHBOARD")

### Title
I Tracked 499 Azure Changes. 21 Were About to Break Me. (54)

**Alternates for A/B testing**
- The Azure Dashboard Microsoft Should Have Shipped (48)
- 499 Azure Changes This Week. Only 21 Actually Matter. (52)
- I Stopped Reading Azure Update Emails. I Built This Instead. (60)

### Description
Every week Azure ships hundreds of changes. Retirements, deprecations,
breaking API versions. Somewhere in that noise is the one that takes down
your estate.

CloudLens watches the whole landscape, deduplicates it, and has GPT-4o
classify each item by change type, severity, deadline, migration path and
effort. Every dated change lands on a lifecycle calendar, and Azure Resource
Graph tells you which of the changes actually touch resources you have
deployed.

The numbers on screen are live: 499 changes tracked, 63 retirements,
15 urgent, 21 of my own resources affected.

Built on .NET 8 Minimal API, React and Fluent UI, Cosmos DB change feed,
Azure OpenAI and Azure Resource Graph. No API keys - managed identity
everywhere.

CHAPTERS
0:00 Azure ships faster than you can track
0:22 What CloudLens does
0:38 The lifecycle calendar
0:52 Blast radius: which of YOUR resources are hit
1:05 Service Health, straight into Teams
1:18 Auto-published to the Azure DevOps wiki
1:28 Zero keys, end to end

LINKS
Source: https://github.com/MoimHossain/az-radar
Deep dive: https://moimhossain.com/2026/09/11/from-radar-to-response-how-cloudlens-learned-to-deliver-azure-service-health-into-teams/

#Azure #PlatformEngineering #CloudComputing #DevOps #AzureOpenAI #dotnet

---

## C - pairs with `cloudlens-thumb-c` ("STOP FINDING OUT LAST. IT'S IN TEAMS")

### Title
Azure Went Down. My Team Knew Before the Portal Did. (51)

**Alternates for A/B testing**
- Stop Finding Out About Azure Outages Last (41)
- Azure Outages Now Land in Teams Before Anyone Notices (52)
- I Wired Azure Service Health Into Teams With Zero Keys (53)

### Description
Most platform teams find out about an Azure incident from a screenshot pasted
into a chat, long after it mattered.

CloudLens routes Service Health events through a private Event Hub pipeline
and posts each one as a standalone message in the exact Teams channel that
owns that service. Not an email. Not a webhook somebody forgot to renew. The
same events publish to your Azure DevOps wiki, so the whole organisation
reads one hub.

Delivery is modelled as a state machine, so it is idempotent and durable -
at-least-once, with no duplicate posts. Managed identity throughout, no keys
or connection strings anywhere in the system.

CHAPTERS
0:00 Azure ships faster than you can track
0:22 What CloudLens does
0:38 The lifecycle calendar
0:52 Blast radius: which of YOUR resources are hit
1:05 Service Health, straight into Teams
1:18 Auto-published to the Azure DevOps wiki
1:28 Zero keys, end to end

LINKS
Source: https://github.com/MoimHossain/az-radar
Deep dive: https://moimhossain.com/2026/09/11/from-radar-to-response-how-cloudlens-learned-to-deliver-azure-service-health-into-teams/

#Azure #MicrosoftTeams #PlatformEngineering #DevOps #SRE #dotnet
