using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public sealed class AzureDevOpsWikiService : IAzureDevOpsWikiService
{
    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsWikiSettings _settings;
    private readonly SecretClient? _secretClient;

    public AzureDevOpsWikiService(
        HttpClient httpClient,
        IOptions<AzureDevOpsWikiSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(
            Math.Clamp(_settings.RequestTimeoutSeconds, 5, 120));

        if (!string.IsNullOrWhiteSpace(_settings.KeyVaultUri))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = _settings.ManagedIdentityClientId
            });
            _secretClient = new SecretClient(new Uri(_settings.KeyVaultUri), credential);
        }
    }

    public async Task StorePatAsync(
        string secretName,
        string personalAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (_secretClient == null)
            throw new InvalidOperationException("Azure DevOps Wiki Key Vault settings are incomplete.");
        if (string.IsNullOrWhiteSpace(personalAccessToken))
            throw new ArgumentException("Personal Access Token is required.", nameof(personalAccessToken));

        await _secretClient.SetSecretAsync(
            new KeyVaultSecret(secretName, personalAccessToken),
            cancellationToken);
    }

    public async Task DeletePatAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        if (_secretClient == null || string.IsNullOrWhiteSpace(secretName))
            return;

        try
        {
            await _secretClient.StartDeleteSecretAsync(secretName, cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
        }
    }

    public async Task<AzureDevOpsWikiPageSnapshot> GetPageAsync(
        ServiceHealthNotificationChannel target,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildGetPageUri(target));
        await AuthorizeAsync(request, target, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var content = document.RootElement.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;
        var pagePath = document.RootElement.TryGetProperty("path", out var pathElement)
            ? pathElement.GetString() ?? target.AzureDevOpsPagePath
            : target.AzureDevOpsPagePath;
        var eTag = response.Headers.ETag?.Tag ??
            (response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null) ??
            string.Empty;
        if (string.IsNullOrWhiteSpace(eTag))
            throw new AzureDevOpsWikiException("etag-missing", "Azure DevOps did not return a page ETag.");

        return new AzureDevOpsWikiPageSnapshot(content, eTag, pagePath);
    }

    public async Task<AzureDevOpsWikiUpdateResult> UpdatePageAsync(
        ServiceHealthNotificationChannel target,
        string content,
        string eTag,
        CancellationToken cancellationToken = default)
    {
        if (content.Length > _settings.MaximumPageCharacters)
        {
            throw new AzureDevOpsWikiException(
                "rendered-page-too-large",
                $"Rendered wiki page has {content.Length} characters; the configured limit is {_settings.MaximumPageCharacters}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUpdatePageUri(target));
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { content }),
            Encoding.UTF8,
            "application/json");
        await AuthorizeAsync(request, target, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var updatedETag = response.Headers.ETag?.Tag ??
            (response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null) ??
            string.Empty;
        return new AzureDevOpsWikiUpdateResult(updatedETag);
    }

    private async Task AuthorizeAsync(
        HttpRequestMessage request,
        ServiceHealthNotificationChannel target,
        CancellationToken cancellationToken)
    {
        if (target.AuthenticationType == AzureDevOpsAuthenticationTypes.PersonalAccessToken)
        {
            if (_secretClient == null || string.IsNullOrWhiteSpace(target.CredentialSecretName))
                throw new AzureDevOpsWikiException("credential-missing", "The wiki PAT is not configured.");

            var secret = await _secretClient.GetSecretAsync(
                target.CredentialSecretName,
                cancellationToken: cancellationToken);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{secret.Value.Value}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            return;
        }

        if (target.AuthenticationType == AzureDevOpsAuthenticationTypes.ManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(target.ManagedIdentityClientId))
            {
                throw new AzureDevOpsWikiException(
                    "identity-not-configured",
                    "The managed identity client ID is not configured.");
            }

            var credential = new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(target.ManagedIdentityClientId));
            AccessToken token;
            try
            {
                token = await credential.GetTokenAsync(
                    new TokenRequestContext([_settings.AzureDevOpsTokenScope]),
                    cancellationToken);
            }
            catch (AuthenticationFailedException ex)
            {
                throw new AzureDevOpsWikiException(
                    "identity-authentication-failed",
                    "CloudLens could not acquire an Azure DevOps token with the configured managed identity.",
                    ex);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return;
        }

        throw new AzureDevOpsWikiException(
            "authentication-type-unsupported",
            $"Unsupported Azure DevOps authentication type '{target.AuthenticationType}'.");
    }

    private static Uri BuildGetPageUri(ServiceHealthNotificationChannel target)
    {
        if (target.AzureDevOpsPageId is > 0)
        {
            return new Uri(
                $"https://dev.azure.com/{Uri.EscapeDataString(target.AzureDevOpsOrganization)}/" +
                $"{Uri.EscapeDataString(target.AzureDevOpsProject)}/_apis/wiki/wikis/" +
                $"{Uri.EscapeDataString(target.AzureDevOpsWikiIdentifier)}/pages/" +
                $"{target.AzureDevOpsPageId}?includeContent=true&api-version=7.1");
        }

        return new Uri(
            $"https://dev.azure.com/{Uri.EscapeDataString(target.AzureDevOpsOrganization)}/" +
            $"{Uri.EscapeDataString(target.AzureDevOpsProject)}/_apis/wiki/wikis/" +
            $"{Uri.EscapeDataString(target.AzureDevOpsWikiIdentifier)}/pages?" +
            $"path={Uri.EscapeDataString(target.AzureDevOpsPagePath)}&includeContent=true&api-version=7.1");
    }

    private static Uri BuildUpdatePageUri(ServiceHealthNotificationChannel target)
    {
        var query = $"path={Uri.EscapeDataString(target.AzureDevOpsPagePath)}" +
            "&api-version=7.1";
        return new Uri(
            $"https://dev.azure.com/{Uri.EscapeDataString(target.AzureDevOpsOrganization)}/" +
            $"{Uri.EscapeDataString(target.AzureDevOpsProject)}/_apis/wiki/wikis/" +
            $"{Uri.EscapeDataString(target.AzureDevOpsWikiIdentifier)}/pages?{query}");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "authentication-failed",
            HttpStatusCode.Forbidden => "permission-required",
            HttpStatusCode.NotFound => "page-not-found",
            HttpStatusCode.PreconditionFailed => "etag-conflict",
            HttpStatusCode.TooManyRequests => "rate-limited",
            _ when (int)response.StatusCode >= 500 => "azure-devops-unavailable",
            _ => $"azure-devops-http-{(int)response.StatusCode}"
        };
        var message = string.IsNullOrWhiteSpace(responseBody)
            ? $"Azure DevOps returned HTTP {(int)response.StatusCode}."
            : $"Azure DevOps returned HTTP {(int)response.StatusCode}: {Truncate(responseBody, 1000)}";
        throw new AzureDevOpsWikiException(code, message, response.StatusCode);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

public sealed class AzureDevOpsWikiException : Exception
{
    public AzureDevOpsWikiException(
        string code,
        string message,
        HttpStatusCode? statusCode = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public AzureDevOpsWikiException(
        string code,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
    public HttpStatusCode? StatusCode { get; }
}
