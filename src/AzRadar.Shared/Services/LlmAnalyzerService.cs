using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AzRadar.Shared.Services;

public class LlmAnalyzerService : ILlmAnalyzer
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<LlmAnalyzerService> _logger;

    public LlmAnalyzerService(
        ChatClient chatClient,
        ILogger<LlmAnalyzerService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<LlmAnalysis> AnalyzeFeedItemAsync(
        FeedItem item, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing feed item: {Title}", item.Title);

        var systemPrompt = GetSystemPrompt();
        var userPrompt = GetUserPrompt(item);

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.1f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var response = await _chatClient.CompleteChatAsync(
                messages, options, cancellationToken);

            var content = response.Value.Content[0].Text;
            var analysis = JsonSerializer.Deserialize<LlmAnalysis>(content);

            if (analysis == null)
            {
                _logger.LogWarning("LLM returned null analysis for {Title}", item.Title);
                return CreateFallbackAnalysis(item);
            }

            _logger.LogInformation(
                "Analysis complete: type={ChangeType}, severity={Severity}, confidence={Confidence}",
                analysis.ChangeType, analysis.Severity, analysis.AiConfidence);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM analysis failed for {Title}", item.Title);
            return CreateFallbackAnalysis(item);
        }
    }

    private static string GetSystemPrompt() => """
        You are an Azure lifecycle intelligence analyst. Your job is to analyze Azure update announcements 
        and extract structured metadata that helps enterprise platform teams understand the impact.
        
        Always respond with valid JSON matching this schema:
        {
          "changeType": "retirement | deprecation | breaking-change | security-advisory | new-feature | migration-required | preview | general-availability | update",
          "severity": "critical | high | medium | low | informational",
          "affectedServices": ["list of Azure service names affected"],
          "affectedResourceTypes": ["list of Azure resource types, e.g. Microsoft.Cache/redis"],
          "actionRequired": "description of what action is needed, or empty string if none",
          "deadline": "YYYY-MM-DD format if a deadline is mentioned, or null",
          "effortEstimate": "low | medium | high | very-high",
          "migrationPath": "brief description of migration steps if applicable",
          "microsoftDocLinks": ["any Microsoft documentation links mentioned"],
          "aiConfidence": 0.0 to 1.0,
          "briefSummary": "2-3 sentence plain-language summary of what this change means for platform teams"
        }
        
        Guidelines:
        - Be precise about affected resource types (use ARM resource type format when possible)
        - Rate severity based on operational impact: retirements/breaking changes are high/critical, new features are informational/low
        - Set aiConfidence lower when the announcement is vague or ambiguous
        - For new features and previews, effortEstimate should be "low" and severity "informational"
        - Extract any deadlines mentioned in the text
        """;

    private static string GetUserPrompt(FeedItem item) => $"""
        Analyze this Azure update announcement:

        Title: {item.Title}
        Published: {item.PublishDate:yyyy-MM-dd}
        Categories: {string.Join(", ", item.Categories)}
        Link: {item.Link}

        Content:
        {item.RawContent}
        
        Summary:
        {item.Summary}
        """;

    private static LlmAnalysis CreateFallbackAnalysis(FeedItem item) => new()
    {
        ChangeType = ChangeTypes.Update,
        Severity = SeverityLevels.Informational,
        AffectedServices = [],
        AffectedResourceTypes = [],
        ActionRequired = string.Empty,
        EffortEstimate = "low",
        MigrationPath = string.Empty,
        MicrosoftDocLinks = [item.Link],
        AiConfidence = 0.0,
        BriefSummary = $"Unable to analyze: {item.Title}"
    };
}
