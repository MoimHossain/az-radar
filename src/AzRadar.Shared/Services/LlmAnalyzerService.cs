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
            You are an Azure Resource Graph (ARG) KQL expert. Generate a query to find resources
            impacted by this retirement/deprecation.

            CRITICAL ARG SYNTAX RULES (violations cause query failures):
            - Use the `resources` table
            - `kind` is a TOP-LEVEL column (NOT `properties.kind`)
            - `sku.name` and `sku.tier` are TOP-LEVEL (NOT `properties.sku.name`)
            - `properties` contains service-specific configs (e.g., `properties.minimumTlsVersion`)
            - Do NOT use `array_contains()`, `any()`, `all()` — ARG does not support these
            - For arrays in properties, use `mv-expand` then filter, or use `contains()` on string representation
            - Use `=~` for case-insensitive type matching
            - Use `in~` for multiple values
            - Always wrap compound conditions in parentheses
            - Always end with `| take 200`

            EXAMPLE VALID QUERIES:
            
            1. Find Redis instances with old TLS:
               resources | where type =~ "Microsoft.Cache/redis" | where properties.minimumTlsVersion in ("1.0", "1.1") | project subscriptionId, resourceGroup, name, type, location, properties.minimumTlsVersion | take 200

            2. Find Storage accounts with GPv1:
               resources | where type =~ "Microsoft.Storage/storageAccounts" | where kind =~ "Storage" | project subscriptionId, resourceGroup, name, type, location, kind, sku.name | take 200

            3. Find VMs with specific SKU:
               resources | where type =~ "Microsoft.Compute/virtualMachines" | where properties.hardwareProfile.vmSize in~ ("Standard_A0", "Standard_A1") | project subscriptionId, resourceGroup, name, type, location, properties.hardwareProfile.vmSize | take 200

            4. Find App Services with old .NET:
               resources | where type =~ "Microsoft.Web/sites" | where properties.siteConfig.netFrameworkVersion == "v2.0" | project subscriptionId, resourceGroup, name, type, location, properties.siteConfig.netFrameworkVersion | take 200

            WHEN TO RETURN "SKIP":
            - SDK/client library version retirements (ARG cannot detect)
            - API version retirements (ARG cannot detect calling API version)
            - Feature flag or preview feature retirements
            - Changes that require checking application code
            - Broad announcements listing many unrelated services
            - If you cannot write a precise, non-trivial filter

            Return ONLY the KQL query or the word SKIP. No explanations.

            Retirement/Deprecation Details:
            Title: {title}
            Description: {description}
            Affected Services: {string.Join(", ", affectedServices)}
            Affected Resource Types: {string.Join(", ", affectedResourceTypes)}
            Action Required: {actionRequired}
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
