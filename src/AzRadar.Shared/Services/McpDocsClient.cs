using System.Text.Json;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace AzRadar.Shared.Services;

/// <summary>
/// Client for the Microsoft Learn MCP Server using the official MCP .NET SDK.
/// </summary>
public class McpDocsClient : IMcpDocsClient, IAsyncDisposable
{
    private const string McpEndpoint = "https://learn.microsoft.com/api/mcp";
    private readonly ILogger<McpDocsClient> _logger;
    private McpClient? _client;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public McpDocsClient(ILogger<McpDocsClient> logger)
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

            _logger.LogInformation("Connecting to MS Learn MCP Server at {Endpoint}", McpEndpoint);

            _client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(McpEndpoint),
                    Name = "az-radar"
                }),
                cancellationToken: ct);

            _logger.LogInformation("Connected to MS Learn MCP Server");
            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<McpSearchResult>> SearchDocsAsync(
        string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP docs search: {Query}", query);

        try
        {
            var client = await GetClientAsync(cancellationToken);

            var result = await client.CallToolAsync("microsoft_docs_search",
                new Dictionary<string, object?> { ["query"] = query },
                cancellationToken: cancellationToken);

            var results = new List<McpSearchResult>();

            // Convert ContentBlocks to AIContent for text extraction
            var aiContents = result.Content.ToAIContents();
            foreach (var content in aiContents)
            {
                if (content is not TextContent textContent || string.IsNullOrEmpty(textContent.Text))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(textContent.Text);
                    var root = doc.RootElement;

                    // MCP returns {"results": [{title, content, url?}, ...]}
                    if (root.TryGetProperty("results", out var resultsArray)
                        && resultsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in resultsArray.EnumerateArray())
                        {
                            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                            var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            var itemContent = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                            var snippet = itemContent.Length > 500 ? itemContent[..500] : itemContent;
                            results.Add(new McpSearchResult(title, url, snippet) { FullContent = itemContent });
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                            var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            var itemContent = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                            var snippet = itemContent.Length > 500 ? itemContent[..500] : itemContent;
                            results.Add(new McpSearchResult(title, url, snippet) { FullContent = itemContent });
                        }
                    }
                }
                catch (JsonException)
                {
                    _logger.LogDebug("MCP search returned non-JSON text, treating as plain text");
                    results.Add(new McpSearchResult("", "", textContent.Text));
                }
            }

            _logger.LogInformation("MCP search returned {Count} results for: {Query}", results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP search failed for: {Query}", query);
            return [];
        }
    }

    public async Task<string> FetchDocAsync(
        string url, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP docs fetch: {Url}", url);

        try
        {
            var client = await GetClientAsync(cancellationToken);

            var result = await client.CallToolAsync("microsoft_docs_fetch",
                new Dictionary<string, object?> { ["url"] = url },
                cancellationToken: cancellationToken);

            var aiContents = result.Content.ToAIContents();
            var content = string.Join("\n", aiContents
                .OfType<TextContent>()
                .Where(c => !string.IsNullOrEmpty(c.Text))
                .Select(c => c.Text));

            _logger.LogInformation("MCP fetch returned {Length} chars for: {Url}", content.Length, url);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP fetch failed for: {Url}", url);
            return string.Empty;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
        _initLock.Dispose();
    }
}
