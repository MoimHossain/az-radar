using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public class ServiceHealthSubscriptionProvisioner : IServiceHealthSubscriptionProvisioner
{
    private const string ArmScope = "https://management.azure.com/.default";
    private const string SubscriptionApiVersion = "2020-01-01";
    private const string DiagnosticSettingsApiVersion = "2021-05-01-preview";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly ServiceHealthProvisioningSettings _settings;
    private readonly ILogger<ServiceHealthSubscriptionProvisioner> _logger;

    public ServiceHealthSubscriptionProvisioner(
        HttpClient httpClient,
        IOptions<ServiceHealthProvisioningSettings> settings,
        ILogger<ServiceHealthSubscriptionProvisioner> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _settings.ManagedIdentityClientId
        });
    }

    public async Task<ServiceHealthProvisioningResult> ProvisionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var metadata = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!metadata.Succeeded)
            return metadata;

        var body = JsonSerializer.Serialize(new
        {
            properties = new
            {
                eventHubAuthorizationRuleId = _settings.EventHubAuthorizationRuleId,
                eventHubName = _settings.EventHubName,
                logs = new[]
                {
                    new
                    {
                        category = "ServiceHealth",
                        enabled = true,
                        retentionPolicy = new { enabled = false, days = 0 }
                    }
                }
            }
        });

        var path = DiagnosticSettingPath(subscriptionId);
        using var request = await CreateRequestAsync(HttpMethod.Put, path, cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return await CreateFailureAsync(response, metadata.DisplayName, metadata.TenantId, cancellationToken);

        return await VerifyAsync(subscriptionId, cancellationToken);
    }

    public async Task<ServiceHealthProvisioningResult> VerifyAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var metadata = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!metadata.Succeeded)
            return metadata;

        using var request = await CreateRequestAsync(
            HttpMethod.Get, DiagnosticSettingPath(subscriptionId), cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return await CreateFailureAsync(response, metadata.DisplayName, metadata.TenantId, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var properties = document.RootElement.GetProperty("properties");
        var authorizationRuleId = properties.GetProperty("eventHubAuthorizationRuleId").GetString();
        var eventHubName = properties.TryGetProperty("eventHubName", out var hub) ? hub.GetString() : null;
        var hasServiceHealth = properties.GetProperty("logs").EnumerateArray().Any(log =>
            log.TryGetProperty("category", out var category) &&
            string.Equals(category.GetString(), "ServiceHealth", StringComparison.OrdinalIgnoreCase) &&
            log.TryGetProperty("enabled", out var enabled) &&
            enabled.GetBoolean());

        if (!hasServiceHealth ||
            !string.Equals(authorizationRuleId, _settings.EventHubAuthorizationRuleId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(eventHubName, _settings.EventHubName, StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceHealthProvisioningResult(
                false,
                ServiceHealthSubscriptionStatus.ConfigurationFailed,
                metadata.DisplayName,
                metadata.TenantId,
                "DiagnosticSettingMismatch",
                "The diagnostic setting exists but does not match the configured Service Health Event Hub destination.");
        }

        return new ServiceHealthProvisioningResult(
            true,
            ServiceHealthSubscriptionStatus.Active,
            metadata.DisplayName,
            metadata.TenantId);
    }

    public async Task DeleteAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = await CreateRequestAsync(
            HttpMethod.Delete, DiagnosticSettingPath(subscriptionId), cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var failure = await CreateFailureAsync(response, string.Empty, string.Empty, cancellationToken);
        throw new InvalidOperationException($"{failure.ErrorCode}: {failure.ErrorMessage}");
    }

    private async Task<ServiceHealthProvisioningResult> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"https://management.azure.com/subscriptions/{subscriptionId}?api-version={SubscriptionApiVersion}",
            cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return await CreateFailureAsync(response, subscriptionId, string.Empty, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return new ServiceHealthProvisioningResult(
            true,
            ServiceHealthSubscriptionStatus.Registering,
            root.TryGetProperty("displayName", out var name) ? name.GetString() ?? subscriptionId : subscriptionId,
            root.TryGetProperty("tenantId", out var tenant) ? tenant.GetString() ?? string.Empty : string.Empty);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([ArmScope]), cancellationToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private async Task<ServiceHealthProvisioningResult> CreateFailureAsync(
        HttpResponseMessage response,
        string displayName,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var errorCode = response.StatusCode == HttpStatusCode.Forbidden
            ? "AuthorizationFailed"
            : $"Arm{(int)response.StatusCode}";
        var status = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? ServiceHealthSubscriptionStatus.PermissionRequired
            : ServiceHealthSubscriptionStatus.ConfigurationFailed;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var code))
                    errorCode = code.GetString() ?? errorCode;
                if (error.TryGetProperty("message", out var message))
                    content = message.GetString() ?? content;
            }
        }
        catch (JsonException)
        {
            // Preserve the ARM response body when it is not JSON.
        }

        _logger.LogWarning(
            "Service Health provisioning request failed with {StatusCode} and {ErrorCode}",
            response.StatusCode, errorCode);

        return new ServiceHealthProvisioningResult(
            false, status, displayName, tenantId, errorCode, content);
    }

    private string DiagnosticSettingPath(string subscriptionId) =>
        $"https://management.azure.com/subscriptions/{subscriptionId}" +
        $"/providers/Microsoft.Insights/diagnosticSettings/{Uri.EscapeDataString(_settings.DiagnosticSettingName)}" +
        $"?api-version={DiagnosticSettingsApiVersion}";

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ManagedIdentityClientId) ||
            string.IsNullOrWhiteSpace(_settings.EventHubAuthorizationRuleId) ||
            string.IsNullOrWhiteSpace(_settings.EventHubName) ||
            string.IsNullOrWhiteSpace(_settings.DiagnosticSettingName))
        {
            throw new InvalidOperationException(
                "ServiceHealthProvisioning settings are incomplete. Configure the dedicated managed identity and Event Hub destination.");
        }
    }
}
