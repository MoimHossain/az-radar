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

    public async Task<string?> GenerateResourceGraphQueryAsync(
        string title,
        string description,
        List<string> affectedServices,
        List<string> affectedResourceTypes,
        string actionRequired,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating ARG query for: {Title}", title);

        var prompt = $"""
            You are an Azure Resource Graph (ARG) expert. Generate a KQL query for Azure Resource Graph
            that will find resources specifically impacted by this retirement/deprecation.

            IMPORTANT RULES:
            - Return ONLY the KQL query, no explanation, no markdown fences
            - The query must be valid Azure Resource Graph KQL
            - Use the `resources` table
            - Be SPECIFIC: filter by actual properties that indicate the resource is affected
              (e.g., specific SKU, TLS version, API version, OS version, deprecated configuration)
            - Do NOT simply query all resources of a type — that is too broad
            - If the retirement is about a specific SDK version or client library, return "SKIP" since ARG cannot detect SDK usage
            - If the retirement is about a deprecated feature that cannot be detected via resource properties, return "SKIP"
            - Always include: subscriptionId, resourceGroup, name, type, location, and relevant properties
            - Always use `project` to limit output columns
            - Always add `| take 200` at the end
            - If filtering by a property, use `properties.` prefix (e.g., `properties.minimumTlsVersion`)

            Retirement/Deprecation Details:
            Title: {title}
            Description: {description}
            Affected Services: {string.Join(", ", affectedServices)}
            Affected Resource Types: {string.Join(", ", affectedResourceTypes)}
            Action Required: {actionRequired}

            Generate the ARG KQL query:
            """;

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an Azure Resource Graph KQL query generator. Return only valid KQL or the word SKIP."),
                new UserChatMessage(prompt)
            };

            var options = new ChatCompletionOptions { Temperature = 0.1f };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var query = response.Value.Content[0].Text.Trim();

            // Clean up markdown fences if present
            query = query.Replace("```kql", "").Replace("```kusto", "").Replace("```", "").Trim();

            if (string.IsNullOrEmpty(query) || query.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("LLM returned SKIP for: {Title}", title);
                return null;
            }

            _logger.LogInformation("Generated ARG query for {Title}: {Query}", title, query[..Math.Min(100, query.Length)]);
            return query;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate ARG query for: {Title}", title);
            return null;
        }
    }
}
