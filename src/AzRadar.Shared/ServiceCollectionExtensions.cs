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

        // MCP Clients
        services.AddSingleton<IMcpDocsClient, McpDocsClient>();
        services.AddSingleton<IMrcMcpClient, MrcMcpClient>();

        // Job Handlers
        services.AddSingleton<IJobHandler, AzureUpdatesJobHandler>();
        services.AddSingleton<IJobHandler, MsLearnIntelligenceJobHandler>();
        services.AddSingleton<IJobHandler, BlastRadiusJobHandler>();

        // Resource Graph Client
        services.AddSingleton<IResourceGraphClient, ResourceGraphClient>();

        return services;
    }
}
