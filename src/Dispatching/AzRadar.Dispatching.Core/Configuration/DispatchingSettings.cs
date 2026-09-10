namespace AzRadar.Dispatching.Core.Configuration;

public sealed class DispatchingCosmosSettings
{
    public const string SectionName = "DispatchingCosmos";

    public string Endpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "az-radar-db";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string EventsContainer { get; set; } = "service-health-events";
    public string ChannelsContainer { get; set; } = "service-health-channels";
    public string DeliveryIntentsContainer { get; set; } = "service-health-delivery-intents";
    public string ConversationReferencesContainer { get; set; } = "teams-conversation-references";
    public string DeliveryAttemptsContainer { get; set; } = "teams-delivery-attempts";
}

public sealed class DispatchingServiceBusSettings
{
    public const string SectionName = "DispatchingServiceBus";

    public string FullyQualifiedNamespace { get; set; } = string.Empty;
    public string TopicName { get; set; } = "service-health-delivery";
    public string TeamsSubscriptionName { get; set; } = "teams-realtime";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public int OutboxBatchSize { get; set; } = 50;
    public int OutboxPollSeconds { get; set; } = 5;
    public int MaxConcurrentCalls { get; set; } = 8;
    public int MaxDeliveryAttempts { get; set; } = 8;
}

public sealed class TeamsBotSettings
{
    public const string SectionName = "TeamsBot";

    public string MicrosoftAppType { get; set; } = "UserAssignedMSI";
    public string MicrosoftAppId { get; set; } = string.Empty;
    public string MicrosoftAppTenantId { get; set; } = string.Empty;
    public string MicrosoftAppPassword { get; set; } = string.Empty;
}
