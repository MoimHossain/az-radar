using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace AzRadar.Shared.Services;

public sealed class CopilotCompletion(ILogger<CopilotCompletion> logger) : ICopilotCompletion
{
    private readonly SemaphoreSlim _capacity = new(1, 1);

    public async Task<string> CompleteAsync(string pat, string model, string system, string prompt, CancellationToken ct)
    {
        if (system.Length + prompt.Length > 128_000)
            throw new AiProviderException("prompt_too_large", "The Copilot analysis input exceeds 128,000 characters.");
        await _capacity.WaitAsync(ct);
        var directory = Path.Combine(Path.GetTempPath(), $"azradar-copilot-{Guid.NewGuid():N}");
        CopilotClient? client = null;
        try
        {
            Directory.CreateDirectory(directory);
            var environment = new Dictionary<string, string>();
            foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "TMP", "TEMP", "TMPDIR",
                "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "SSL_CERT_FILE", "NODE_EXTRA_CA_CERTS" })
                if (Environment.GetEnvironmentVariable(key) is { } value) environment[key] = value;
            environment["HOME"] = directory;
            environment["USERPROFILE"] = directory;
            environment["COPILOT_HOME"] = directory;
            client = new CopilotClient(new CopilotClientOptions
            {
                Mode = CopilotClientMode.Empty,
                BaseDirectory = directory,
                GitHubToken = pat,
                UseLoggedInUser = false,
                Environment = environment
            });
            await client.StartAsync(ct);
            logger.LogInformation("Copilot runtime started for bounded inference using model {Model}", model);
            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = model,
                AvailableTools = [],
                ConfigDirectory = directory,
                SkipCustomInstructions = true,
                ManageScheduleEnabled = false,
                EnableExperimentalMode = false,
                EnableSessionTelemetry = false,
                SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = system },
#pragma warning disable GHCP001 // The pinned SDK marks even deny-only permission decisions experimental.
                OnPermissionRequest = (_, _) => Task.FromResult(PermissionDecision.Reject("Inference only: tools are disabled."))
#pragma warning restore GHCP001
            }, ct);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt },
                TimeSpan.FromSeconds(90), ct);
            return response?.Data.Content ?? "";
        }
        finally
        {
            try
            {
                if (client is not null)
                {
                    // A cancelled wait does not stop inference. Stop the owned runtime before releasing capacity.
                    await client.ForceStopAsync();
                    await client.DisposeAsync();
                }
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Could not remove isolated Copilot session directory {Directory}: {ErrorType}",
                        directory, ex.GetType().Name);
                }
                _capacity.Release();
            }
        }
    }
}
