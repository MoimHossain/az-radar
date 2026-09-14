# Service Health identity and authentication flow

## Short answer

AzRadar does **not** use the person signed in to the UI to configure or run the Service Health pipeline. The UI sends a subscription ID to the API, and the backend uses Azure managed identities.

The current design is easiest and safest to understand as **one AzRadar deployment per customer tenant**:

- Managed identities are created in the tenant where AzRadar is deployed.
- The subscription provisioning identity must have permission on every monitored subscription.
- Azure Monitor sends Service Health records to the deployment's Event Hub.
- The JobHost reads and analyzes those records.
- A separate dispatch worker moves notifications through Service Bus and posts them to Teams.
- The Teams app identifies the CloudLens bot by its bot client ID; it does not connect to Service Bus.

There is no literal customer tenant ID in the application source or Teams manifest. Bicep uses `tenant().tenantId`, which resolves to the tenant used for that deployment. However, the current routing code is **not safe for one shared deployment serving multiple unrelated customer tenants**, as explained below.

## End-to-end flow

```text
Administrator enters subscription ID in AzRadar UI
        |
        v
AzRadar API
  Uses: Service Health provisioning UAMI
  Auth: Microsoft Entra token for Azure Resource Manager
        |
        v
Subscription Activity Log diagnostic setting
  Sends category: ServiceHealth
  Destination: AzRadar Event Hub
        |
        v
AzRadar JobHost App Service
  Uses: AzRadar runtime UAMI
  Reads: Event Hub
  Calls: Azure OpenAI
  Writes: event + delivery intent to Cosmos DB
        |
        v
AzRadar Dispatch Worker App Service
  Uses: dispatch worker UAMI
  Reads pending intents from Cosmos DB
  Publishes to and consumes from Service Bus
        |
        v
Microsoft Bot Framework / Teams
  Uses: CloudLens bot UAMI
  Routes with the stored tenant, team, channel, and conversation reference
```

## Identity used at each step

| Step | Component | Identity and authentication |
|---|---|---|
| Register a subscription from the UI | `AzRadar.Api` | The API uses the dedicated **Service Health provisioning UAMI** through `DefaultAzureCredential`. The signed-in UI user's Azure identity is not used for the ARM operation. |
| Read subscription metadata | `AzRadar.Api` | The same provisioning UAMI requests an Azure Resource Manager token for `https://management.azure.com/.default`. |
| Create the Activity Log diagnostic setting | `AzRadar.Api` | The provisioning UAMI calls ARM to create `az-radar-service-health` on the submitted subscription. It needs subscription read and diagnostic-setting read/write/delete permissions. |
| Validate the Event Hub destination | `AzRadar.Api` | The provisioning UAMI has a custom role allowing it to read the Event Hub and authorization rule and call `listKeys`. |
| Send Service Health records to Event Hub | Azure Monitor | This is performed by the Azure Monitor platform, not by an AzRadar App Service identity. The diagnostic setting references the Event Hub authorization rule. The Event Hub permits trusted Microsoft services through its network boundary. |
| Receive Event Hub records | `AzRadar.JobHost` / `ServiceHealthIngressWorker` | The **AzRadar runtime UAMI** authenticates with `DefaultAzureCredential` and requires Azure Event Hubs Data Receiver. |
| Run LLM analysis | `AzRadar.JobHost` / `ServiceHealthEventProcessor` / `LlmAnalyzerService` | The **same AzRadar runtime UAMI** authenticates to the configured Azure OpenAI endpoint. The analysis happens inline in JobHost while processing the Event Hub record. |
| Store event and delivery intent | `AzRadar.JobHost` | The runtime UAMI writes to Cosmos DB using Microsoft Entra authentication. |
| Publish to Service Bus | `AzRadar.Dispatching.Worker` / `DeliveryIntentOutboxWorker` | The **dispatch worker UAMI** reads pending delivery intents from Cosmos DB and sends messages to the Service Bus topic using Azure Service Bus Data Sender. |
| Consume from Service Bus | `AzRadar.Dispatching.Worker` / `TeamsDeliveryWorker` | The same dispatch worker UAMI receives from the Teams subscription using Azure Service Bus Data Receiver. |
| Post proactively to Teams | `AzRadar.Dispatching.Worker` | The worker uses the **CloudLens bot gateway UAMI** as the Bot Framework identity. Both the dispatch worker UAMI and bot UAMI are attached to this App Service for their separate purposes. |
| Receive installation or registration activity from Teams | `AzRadar.Dispatching.BotGateway` | Teams/Bot Service calls the public bot endpoint with a signed JWT. The gateway validates the Bot Framework issuer and the bot client ID as the token audience. |
| Store the Teams destination | `AzRadar.Dispatching.BotGateway` | The **bot gateway UAMI** writes the tenant ID, team ID, channel ID, and serialized conversation reference to Cosmos DB. |

## What happens when a Teams app is installed

The Teams app does **not** identify itself to Service Bus and contains no Service Bus namespace, topic, subscription, key, or managed identity.

The source manifest contains placeholder GUIDs. During packaging, `package.ps1` replaces those placeholders with the deployed CloudLens bot UAMI client ID:

```powershell
.\package.ps1 -BotClientId '<bot-uami-client-id>'
```

That bot client ID connects the Teams package to the Azure Bot resource. It is deployment-specific, but it is not a hardcoded tenant ID.

When the bot is installed or mentioned in a channel, Teams supplies these values in the activity payload:

- Tenant ID
- Team ID
- Channel ID
- Bot Framework service URL and conversation information

The gateway stores them in Cosmos DB. Later, the dispatch worker loads that conversation reference and explicitly supplies the stored tenant ID and channel ID when creating the proactive Teams conversation. There is no hardcoded channel or customer tenant in this process.

## How a Service Health event is associated with a tenant

The Activity Log record contains a **subscription ID**, not a Teams tenant configuration. During subscription registration, AzRadar reads and stores the subscription's tenant ID. The Teams registration separately stores the tenant ID supplied by Teams.

The intended association should therefore be:

```text
Service Health event subscription ID
    -> registered subscription
    -> subscription tenant ID
    -> Teams channels with the same tenant ID
```

### Current tenant-isolation gap

The current `ServiceHealthEventProcessor` does not perform that association. It selects every registered Teams channel that subscribes to the event family, without comparing:

- The event's subscription ID with a registered subscription
- The registered subscription's tenant ID with the Teams channel's tenant ID

Therefore:

- A dedicated deployment containing only one customer's subscriptions and Teams channels is naturally tenant-isolated.
- A shared deployment containing subscriptions and Teams channels from multiple customer tenants could route one tenant's Service Health alert to another tenant's channel.

This must be corrected before treating the current deployment as a multi-customer SaaS service. Routing should resolve the event's registered subscription and permit only channels whose `TenantId` matches that subscription's `TenantId`.

## Is anything tenant-specific hardcoded?

### Runtime application configuration

No literal tenant GUID is hardcoded in the runtime application code or source Teams manifest.

- The Azure Bot resource uses `tenant().tenantId`, resolved from the deployment context.
- Bot gateway token settings use the deployment tenant and deployed bot client ID.
- Managed identity client IDs and resource endpoints are injected through App Service settings.
- The Teams package receives the deployed bot client ID when it is built.
- Tenant, team, channel, and conversation identifiers are captured dynamically from Teams activities.

The repository's deployment records may contain IDs from a previous deployment for operational documentation, but those records are not runtime configuration.

### Deployment boundary

Although the tenant is not hardcoded, managed identities are **tenant-bound**. A UAMI created in one Microsoft Entra tenant is not automatically authorized in another customer's tenant.

For a new customer, use one of these models:

1. **Recommended current model: deploy AzRadar in the customer's tenant.** Create new managed identities, Event Hub, Service Bus, Azure Bot, Cosmos configuration, and Teams package in that tenant. Grant the provisioning UAMI access to the customer's monitored subscriptions.
2. **Central multi-customer service:** requires an explicit cross-tenant design, such as Azure Lighthouse for Azure resource management, supported Teams cross-tenant bot distribution, and strict tenant filtering in storage, APIs, processing, and dispatch. The current implementation is not yet complete for this model.

## Customer deployment checklist

1. Deploy the Bicep templates while authenticated to the customer's intended Azure/Entra tenant.
2. Confirm the dedicated provisioning UAMI has the diagnostic-setting role on each monitored subscription.
3. Confirm the runtime UAMI has Event Hubs Data Receiver, Cosmos DB data access, and Azure OpenAI access.
4. Confirm the dispatch worker UAMI has Cosmos DB access and Service Bus Data Sender and Data Receiver.
5. Confirm the bot gateway UAMI is configured as the Azure Bot identity and is attached to both the gateway and dispatch worker where required.
6. Build a new Teams package using that deployment's bot client ID.
7. Upload and install that package in the customer's Teams tenant, then register each target channel.
8. Do not mix unrelated tenants in one deployment until tenant-aware event routing is implemented and tested.

## Main implementation references

- `src/AzRadar.Shared/Services/ServiceHealthSubscriptionProvisioner.cs`
- `src/AzRadar.JobHost/ServiceHealthIngressWorker.cs`
- `src/AzRadar.Shared/Services/ServiceHealthEventProcessor.cs`
- `src/AzRadar.Shared/Services/LlmAnalyzerService.cs`
- `src/Dispatching/AzRadar.Dispatching.Worker/DeliveryIntentOutboxWorker.cs`
- `src/Dispatching/AzRadar.Dispatching.Worker/TeamsDeliveryWorker.cs`
- `src/Dispatching/AzRadar.Dispatching.BotGateway/AzRadarNotificationAgent.cs`
- `src/Dispatching/AzRadar.Dispatching.BotGateway/AzureBotAuthenticationExtensions.cs`
- `infra/main.bicep`
- `infra/modules/event-hubs.bicep`
- `infra/modules/service-health-subscription.bicep`
- `src/Dispatching/infra/main.bicep`
- `src/Dispatching/TeamsApp/manifest.json`
- `src/Dispatching/TeamsApp/package.ps1`

Microsoft reference: [Diagnostic settings in Azure Monitor](https://learn.microsoft.com/azure/azure-monitor/platform/diagnostic-settings#destinations).
