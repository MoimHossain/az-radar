using System.Text.Json;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Client;

namespace AzRadar.Shared.Services;

/// <summary>
/// Client for the Microsoft Release Communications MCP Server.
/// Endpoint: https://www.microsoft.com/releasecommunications/mcp
/// </summary>
public class MrcMcpClient : IMrcMcpClient, IAsyncDisposable
{
    private const string MrcEndpoint = "https://www.microsoft.com/releasecommunications/mcp";
    private readonly ILogger<MrcMcpClient> _logger;
    private McpClient? _client;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public MrcMcpClient(ILogger<MrcMcpClient> logger)
    {
        _logger = logger;
    }

    private async Task<McpClient> GetClientAsync(CancellationToken ct)
    {
        if (_client != null) return _client;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_client != null) return _client;

            _logger.LogInformation("Connecting to MRC MCP Server at {Endpoint}", MrcEndpoint);

            _client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(MrcEndpoint),
                    Name = "az-radar-mrc",
                    AdditionalHeaders = new Dictionary<string, string>
                    {
                        // MRC MCP is fronted by Akamai which blocks requests lacking a
                        // browser-like User-Agent (returns 400 from edgesuite.net). Send a
                        // realistic UA plus an Accept header that allows JSON + SSE which
                        // is required by the Streamable HTTP MCP transport.
                        ["User-Agent"] = "Mozilla/5.0 (compatible; AzRadar/1.0; +https://az-radar-api.azurewebsites.net) ModelContextProtocol-DotNet/1.3.0",
                        ["Accept"] = "application/json, text/event-stream",
                        ["Accept-Language"] = "en-US,en;q=0.9"
                    }
                }),
                cancellationToken: ct);

            _logger.LogInformation("Connected to MRC MCP Server");
            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<AzureUpdateItem>> GetRecentAzureUpdatesAsync(
        int top = 50, string? search = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MRC: getting recent Azure updates (top={Top}, search={Search})", top, search);

        try
        {
            var client = await GetClientAsync(cancellationToken);

            var args = new Dictionary<string, object?> { ["top"] = top };
            if (!string.IsNullOrEmpty(search))
                args["search"] = search;

            var result = await client.CallToolAsync("get_recent_azure_updates",
                args, cancellationToken: cancellationToken);

            var items = ParseUpdateItems(result);
            _logger.LogInformation("MRC: returned {Count} Azure updates", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MRC: get_recent_azure_updates failed");
            return [];
        }
    }

    public async Task<AzureUpdateItem?> GetAzureUpdateByIdAsync(
        string id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MRC: getting Azure update by ID: {Id}", id);

        try
        {
            var client = await GetClientAsync(cancellationToken);

            var result = await client.CallToolAsync("get_azure_update_by_id",
                new Dictionary<string, object?> { ["id"] = id },
                cancellationToken: cancellationToken);

            var items = ParseUpdateItems(result);
            return items.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MRC: get_azure_update_by_id failed for {Id}", id);
            return null;
        }
    }

    private List<AzureUpdateItem> ParseUpdateItems(CallToolResult result)
    {
        var items = new List<AzureUpdateItem>();

        var aiContents = result.Content.ToAIContents();
        foreach (var content in aiContents)
        {
            if (content is not TextContent textContent || string.IsNullOrEmpty(textContent.Text))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(textContent.Text);
                var root = doc.RootElement;

                // Response can be {"items": [...]} or a single item object
                if (root.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsArray.EnumerateArray())
                        items.Add(ParseSingleItem(el));
                }
                else if (root.TryGetProperty("id", out _))
                {
                    items.Add(ParseSingleItem(root));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "MRC: failed to parse response text");
            }
        }

        return items;
    }

    private static AzureUpdateItem ParseSingleItem(JsonElement el)
    {
        return new AzureUpdateItem
        {
            Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Title = el.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            Description = el.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
            Status = el.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
            Tags = ParseStringArray(el, "tags"),
            Products = ParseStringArray(el, "products"),
            ProductCategories = ParseStringArray(el, "productCategories"),
            Created = el.TryGetProperty("created", out var created) ? created.GetString() ?? "" : "",
            Modified = el.TryGetProperty("modified", out var modified) ? modified.GetString() ?? "" : "",
            GeneralAvailabilityDate = el.TryGetProperty("generalAvailabilityDate", out var ga)
                ? ga.GetString() : null,
        };
    }

    private static List<string> ParseStringArray(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.String)
            .Select(a => a.GetString()!)
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
        _initLock.Dispose();
    }
}
