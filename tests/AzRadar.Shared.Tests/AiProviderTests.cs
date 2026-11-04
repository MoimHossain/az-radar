using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI.Chat;

namespace AzRadar.Shared.Tests;

public class AiProviderTests
{
    [Theory]
    [InlineData("ghp_classic")]
    [InlineData("github_pat_ contains whitespace")]
    public void RejectsUnsupportedCredential(string token)
    {
        Assert.NotNull(AiSettingsService.Validate(new()
        {
            Provider = AiProviderProfile.Copilot,
            Pat = token
        }, false));
    }

    [Fact]
    public async Task TestUsesSuppliedPatWithoutSavingOrActivatingIt()
    {
        var fixture = new Fixture();
        await fixture.Completion.TestAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Pat = "github_pat_test_only",
            Prompt = "Hello"
        });
        fixture.Copilot.Verify(x => x.CompleteAsync("github_pat_test_only", "gpt-5.4",
            It.IsAny<string>(), "Hello", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Credentials.Verify(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Profiles.Verify(x => x.SaveAsync(It.IsAny<AiProviderProfile>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FoundryTestSupportsModelsThatRejectLegacyMaxTokens()
    {
        var fixture = new Fixture();
        var credential = new Mock<TokenCredential>();
        credential.Setup(x => x.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessToken("test-only", DateTimeOffset.UtcNow.AddHours(1)));
        using var http = new HttpClient(new FoundryHandler());
        var client = new AzureOpenAIClient(new Uri("https://foundry.example.invalid"), credential.Object,
            new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) });
        var completion = new AiCompletionService(fixture.Profiles.Object, fixture.Credentials.Object,
            fixture.Copilot.Object, new Lazy<ChatClient>(() => client.GetChatClient("gpt-5.1")),
            Options.Create(new OpenAiSettings()), NullLogger<AiCompletionService>.Instance);

        var result = await completion.TestAsync(new() { Prompt = "Reply OK" });

        Assert.Equal("OK", result.Reply);
    }

    [Fact]
    public async Task UsesSavedSecretOnlyServerSideAndRedactsResponse()
    {
        var fixture = new Fixture();
        fixture.Active.PatVersion = "version-one";
        fixture.Credentials.Setup(x => x.GetAsync("version-one", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github_pat_saved_secret");
        fixture.Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("github_pat_saved_secret");
        var result = await fixture.Completion.TestAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Prompt = "Hello"
        });
        Assert.Equal("[REDACTED]", result.Reply);
        var view = await fixture.Settings.GetAsync();
        Assert.True(view.PatConfigured);
        Assert.DoesNotContain("github_pat_", JsonSerializer.Serialize(view));
        Assert.DoesNotContain("version-one", JsonSerializer.Serialize(view));
    }

    [Fact]
    public async Task PinnedExecutionDoesNotUseNewlySelectedProfile()
    {
        var fixture = new Fixture();
        fixture.Active.CopilotModel = "new-model";
        fixture.Credentials.Setup(x => x.GetAsync("old-version", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github_pat_old");
        fixture.Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("{}");
        using var context = new JobExecutionContext(true, new()
        {
            Provider = AiProviderProfile.Copilot,
            CopilotModel = "old-model",
            PatVersion = "old-version"
        });
        await fixture.Completion.CompleteAsync("system", "prompt", true);
        fixture.Copilot.Verify(x => x.CompleteAsync("github_pat_old", "old-model", It.IsAny<string>(),
            "prompt", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingCredentialFailsWithoutFoundryFallback()
    {
        var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<AiProviderException>(() => fixture.Completion.TestAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Prompt = "Hello"
        }));
        Assert.Equal("pat_required", error.Code);
        fixture.Copilot.Verify(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Explanation before JSON\n{\"query\":null}")]
    [InlineData("```json\n{\"query\":null}\n```")]
    [InlineData("[]")]
    public async Task CopilotRepairsInvalidJsonWithoutAcceptingProse(string invalid)
    {
        var fixture = new Fixture();
        using var context = new JobExecutionContext(true, new()
        {
            Provider = AiProviderProfile.Copilot,
            PatVersion = "version"
        });
        fixture.Credentials.Setup(x => x.GetAsync("version", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github_pat_test");
        fixture.Copilot.SetupSequence(x => x.CompleteAsync("github_pat_test", "gpt-5.4", It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalid)
            .ReturnsAsync("""{"query":null}""");

        var response = await fixture.Completion.CompleteAsync("system", "original prompt", true);

        Assert.Equal("""{"query":null}""", response);
        fixture.Copilot.Verify(x => x.CompleteAsync("github_pat_test", "gpt-5.4", It.IsAny<string>(),
            It.Is<string>(p => p.Contains("original prompt") && p.Contains(invalid)),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, fixture.Copilot.Invocations.Count);
    }

    [Fact]
    public async Task CopilotFormatRepairIsBoundedAndNeverBecomesSuccessfulSkip()
    {
        var fixture = new Fixture();
        using var context = new JobExecutionContext(true, new()
        {
            Provider = AiProviderProfile.Copilot,
            PatVersion = "version"
        });
        fixture.Credentials.Setup(x => x.GetAsync("version", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github_pat_test");
        fixture.Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("SKIP");

        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            fixture.Completion.CompleteAsync("system", "prompt", true));

        Assert.Equal("invalid_response", error.Code);
        Assert.Equal(3, fixture.Copilot.Invocations.Count);
    }

    [Fact]
    public async Task CopilotOperationalFailureDoesNotTriggerFormatRepair()
    {
        var fixture = new Fixture();
        using var context = new JobExecutionContext(true, new()
        {
            Provider = AiProviderProfile.Copilot,
            PatVersion = "version"
        });
        fixture.Credentials.Setup(x => x.GetAsync("version", It.IsAny<CancellationToken>()))
            .ReturnsAsync("github_pat_test");
        fixture.Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiProviderException("inference_failed", "Unavailable"));

        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            fixture.Completion.CompleteAsync("system", "prompt", true));

        Assert.Equal("inference_failed", error.Code);
        Assert.Single(fixture.Copilot.Invocations);
    }

    [Fact]
    public async Task RuntimeErrorsCannotEchoCredential()
    {
        var fixture = new Fixture();
        fixture.Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("github_pat_must_not_escape"));
        var error = await Assert.ThrowsAsync<AiProviderException>(() => fixture.Completion.TestAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Pat = "github_pat_must_not_escape",
            Prompt = "Hello"
        }));
        Assert.DoesNotContain("github_pat_", error.ToString());
    }

    [Fact]
    public async Task FailedVerificationNeverPersistsTokenOrProfile()
    {
        var fixture = new Fixture();
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.TestAsync(It.IsAny<AiSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiProviderException("inference_failed", "Denied"));
        var settings = new AiSettingsService(fixture.Profiles.Object, fixture.Credentials.Object,
            completion.Object, Options.Create(new OpenAiSettings()));
        await Assert.ThrowsAsync<AiProviderException>(() => settings.SaveAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Pat = "github_pat_test",
            ETag = "etag"
        }));
        fixture.Credentials.Verify(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Profiles.Verify(x => x.SaveAsync(It.IsAny<AiProviderProfile>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveUsesVersionedSecretReferenceNotToken()
    {
        var fixture = new Fixture();
        fixture.Credentials.Setup(x => x.SaveAsync("github_pat_test", It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-version");
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.TestAsync(It.IsAny<AiSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiTestResult(AiProviderProfile.Copilot, "gpt-5.4", "OK", 10));
        var settings = new AiSettingsService(fixture.Profiles.Object, fixture.Credentials.Object,
            completion.Object, Options.Create(new OpenAiSettings()));
        await settings.SaveAsync(new()
        {
            Provider = AiProviderProfile.Copilot,
            Pat = "github_pat_test",
            ETag = "etag"
        });
        fixture.Profiles.Verify(x => x.SaveAsync(It.Is<AiProviderProfile>(p =>
            p.PatVersion == "new-version" && p.Provider == AiProviderProfile.Copilot &&
            !JsonSerializer.Serialize(p, (JsonSerializerOptions?)null).Contains("github_pat_")),
            "etag", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaleSettingsFailBeforeCredentialOrInferenceUse()
    {
        var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<AiProviderException>(() => fixture.Settings.SaveAsync(new() { ETag = "stale" }));
        Assert.Equal("configuration_changed", error.Code);
    }

    [Fact]
    public async Task InvalidAnalysisCannotBecomeSuccessfulEmptyOutput()
    {
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");
        var analyzer = new LlmAnalyzerService(completion.Object, NullLogger<LlmAnalyzerService>.Instance);
        await Assert.ThrowsAsync<JsonException>(() => analyzer.AnalyzeFeedItemAsync(new FeedItem { Title = "Test" }));
    }

    [Theory]
    [InlineData("""{"query":"resources | take 200"}""", "resources | take 200")]
    [InlineData("""{"query":null}""", null)]
    public async Task ResourceGraphGenerationUsesExplicitJsonQueryOrSkip(string response, string? expected)
    {
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var analyzer = new LlmAnalyzerService(completion.Object, NullLogger<LlmAnalyzerService>.Instance);

        var query = await analyzer.GenerateResourceGraphQueryAsync("Retirement", "Details", [], [], "");

        Assert.Equal(expected, query);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":123}""")]
    [InlineData("""{"query":"Explanation before KQL\nresources | take 200"}""")]
    [InlineData("""{"query":"SKIP"}""")]
    public async Task InvalidResourceGraphOutputCannotBecomeAQueryOrSilentSkip(string response)
    {
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var analyzer = new LlmAnalyzerService(completion.Object, NullLogger<LlmAnalyzerService>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            analyzer.GenerateResourceGraphQueryAsync("Retirement", "Details", [], [], ""));

        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task ServiceHealthPreservesIncidentOnProviderFailure()
    {
        var completion = new Mock<IAiCompletionService>();
        completion.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiProviderException("inference_failed", "Denied"));
        var analyzer = new LlmAnalyzerService(completion.Object, NullLogger<LlmAnalyzerService>.Instance);
        var analysis = await analyzer.AnalyzeServiceHealthEventAsync(new ServiceHealthEvent
        {
            EventType = ServiceHealthEventTypes.ServiceIssue,
            Title = "Active outage",
            Status = "Active"
        });
        Assert.Equal("service-incident", analysis.ChangeType);
        Assert.Equal(0, analysis.AiConfidence);
    }

    private sealed class FoundryHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var legacyLimit = body.RootElement.TryGetProperty("max_tokens", out _);
            return new HttpResponseMessage(legacyLimit ? System.Net.HttpStatusCode.BadRequest : System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(legacyLimit
                    ? """{"error":{"code":"unsupported_parameter","message":"max_tokens is not supported"}}"""
                    : """{"id":"chatcmpl-test","object":"chat.completion","created":0,"model":"gpt-5.1","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class Fixture
    {
        public readonly AiProviderProfile Active = new();
        public readonly Mock<IAiProfileStore> Profiles = new();
        public readonly Mock<ICopilotCredentialStore> Credentials = new();
        public readonly Mock<ICopilotCompletion> Copilot = new();
        public AiCompletionService Completion { get; }
        public AiSettingsService Settings { get; }
        public Fixture()
        {
            Profiles.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AiProfileState(Active, "etag"));
            Credentials.SetupGet(x => x.Configured).Returns(true);
            Copilot.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("OK");
            Completion = new(Profiles.Object, Credentials.Object, Copilot.Object,
                new Lazy<ChatClient>(() => throw new InvalidOperationException("Foundry must not be used.")),
                Options.Create(new OpenAiSettings()), NullLogger<AiCompletionService>.Instance);
            Settings = new(Profiles.Object, Credentials.Object, Completion, Options.Create(new OpenAiSettings()));
        }
    }
}
