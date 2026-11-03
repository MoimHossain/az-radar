using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using Microsoft.Extensions.Options;

namespace AzRadar.Shared.Services;

public sealed class CopilotCredentialStore : ICopilotCredentialStore
{
    private const string SecretName = "azradar-ai-copilot-pat";
    private readonly SecretClient? _client;
    public bool Configured => _client is not null;

    public CopilotCredentialStore(IOptions<AzureDevOpsWikiSettings> vault, IOptions<CosmosDbSettings> cosmos)
    {
        if (!string.IsNullOrWhiteSpace(vault.Value.KeyVaultUri))
            _client = new SecretClient(new Uri(vault.Value.KeyVaultUri),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = cosmos.Value.ManagedIdentityClientId
                }));
    }

    public async Task<string> SaveAsync(string pat, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("The AI credential Key Vault is not configured.");
        var secret = await _client.SetSecretAsync(new KeyVaultSecret(SecretName, pat), ct);
        return secret.Value.Properties.Version;
    }

    public async Task<string> GetAsync(string version, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("The AI credential Key Vault is not configured.");
        return (await _client.GetSecretAsync(SecretName, version, ct)).Value.Value;
    }
}
