using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AzRadar.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzRadarSharedServices(this IServiceCollection services)
    {
        // Cosmos DB via Managed Identity
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<CosmosDbSettings>>().Value;
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = settings.ManagedIdentityClientId
            });
            return new CosmosClient(settings.Endpoint, credential, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });
        services.AddSingleton<ICosmosDbService, CosmosDbService>();
        services.AddSingleton<IServiceHealthSubscriptionProvisioner>(sp =>
            new ServiceHealthSubscriptionProvisioner(
                new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                sp.GetRequiredService<IOptions<ServiceHealthProvisioningSettings>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceHealthSubscriptionProvisioner>>()));
        services.AddSingleton<IServiceHealthEventProcessor, ServiceHealthEventProcessor>();
        services.AddSingleton<IServiceHealthTestEventPublisher, ServiceHealthTestEventPublisher>();

        // Azure OpenAI via Managed Identity (UAMI)
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = settings.ManagedIdentityClientId
            });
            var client = new AzureOpenAIClient(new Uri(settings.Endpoint), credential);
            return client.GetChatClient(settings.DeploymentName);
        });
        services.AddSingleton<ILlmAnalyzer, LlmAnalyzerService>();
        services.AddSingleton<JobHeartbeatRunner>();

        // MCP Clients
        services.AddSingleton<IMcpDocsClient, McpDocsClient>();
        services.AddSingleton<IMrcMcpClient, MrcMcpClient>();
        services.AddSingleton<IAzureUpdatesSource>(sp => new AzureUpdatesSource(
            new HttpClient { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureUpdatesSource>>()));

        // GitHub REST client (Change Radar)
        services.AddSingleton<IGitHubClient, GitHubClient>();

        // Job Handlers
        services.AddSingleton<IJobHandler, AzureUpdatesJobHandler>();
        services.AddSingleton<IJobHandler, MsLearnIntelligenceJobHandler>();
        services.AddSingleton<IJobHandler, BlastRadiusJobHandler>();
        services.AddSingleton<IJobHandler, GitHubCrawlJobHandler>();

        // Resource Graph Client
        services.AddSingleton<IResourceGraphClient, ResourceGraphClient>();

        return services;
    }
}
