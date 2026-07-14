using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface ILlmAnalyzer
{
    Task<LlmAnalysis> AnalyzeFeedItemAsync(
        FeedItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask the LLM to generate an Azure Resource Graph (KQL) query
    /// to detect resources impacted by a specific retirement/deprecation.
    /// Returns the KQL query string, or null if the LLM cannot generate one.
    /// </summary>
    Task<string?> GenerateResourceGraphQueryAsync(
        string title,
        string description,
        List<string> affectedServices,
        List<string> affectedResourceTypes,
        string actionRequired,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a single GitHub documentation/source change (a git diff) and decides whether it
    /// represents something an Azure platform team must know about, with a justification.
    /// </summary>
    Task<LlmAnalysis> AnalyzeDocChangeAsync(
        RepoChangeContext change,
        CancellationToken cancellationToken = default);
}
