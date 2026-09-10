using Azure.Identity;
using Azure.Messaging.ServiceBus;
using AzRadar.Dispatching.Core.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AzRadar.Dispatching.Core;

public static class DispatchingServiceCollectionExtensions
{
    public static IServiceCollection AddDispatchingPersistence(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DispatchingCosmosSettings>>().Value;
            var credential = CreateCredential(settings.ManagedIdentityClientId);
            return new CosmosClient(
                settings.Endpoint,
                credential,
                new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                });
        });
        services.AddSingleton<DispatchingRepository>();
        services.AddSingleton<ServiceHealthAdaptiveCardRenderer>();
        return services;
    }

    public static IServiceCollection AddDispatchingServiceBus(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DispatchingServiceBusSettings>>().Value;
            return new ServiceBusClient(
                settings.FullyQualifiedNamespace,
                CreateCredential(settings.ManagedIdentityClientId));
        });
        return services;
    }

    private static DefaultAzureCredential CreateCredential(string managedIdentityClientId) =>
        new(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
}
