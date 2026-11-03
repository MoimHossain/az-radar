using System.Net;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public sealed class CosmosAiProfileStore(CosmosClient cosmos, IOptions<CosmosDbSettings> settings) : IAiProfileStore
{
    public const string ActiveKey = "ai-settings";
    private Container Container => cosmos.GetContainer(settings.Value.DatabaseName, settings.Value.AppConfigContainer);

    public async Task<AiProfileState> GetActiveAsync(CancellationToken ct = default)
    {
        ItemResponse<AppConfig> pointer;
        try
        {
            pointer = await Container.ReadItemAsync<AppConfig>(ActiveKey, new PartitionKey(ActiveKey), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new(new(), null);
        }
        return new(await GetAsync(pointer.Resource.Value, ct), pointer.ETag);
    }

    public async Task<AiProviderProfile> GetAsync(string id, CancellationToken ct = default)
    {
        if (id == AiProviderProfile.LegacyId) return new();
        return (await Container.ReadItemAsync<AiProviderProfile>(id, new PartitionKey(id), cancellationToken: ct)).Resource;
    }

    public async Task SaveAsync(AiProviderProfile profile, string? etag, CancellationToken ct = default)
    {
        await Container.CreateItemAsync(profile, new PartitionKey(profile.Id), cancellationToken: ct);
        var pointer = new AppConfig { Id = ActiveKey, Value = profile.Id };
        try
        {
            if (etag is null)
                await Container.CreateItemAsync(pointer, new PartitionKey(ActiveKey), cancellationToken: ct);
            else
                await Container.ReplaceItemAsync(pointer, ActiveKey, new PartitionKey(ActiveKey),
                    new ItemRequestOptions { IfMatchEtag = etag }, ct);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw new AiProviderException("configuration_changed", "AI settings changed. Refresh and retry.");
        }
    }
}
