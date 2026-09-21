namespace AzRadar.Shared.Models;

public sealed record AzureDevOpsWikiPageAddress(
    string Organization,
    string Project,
    string WikiIdentifier,
    int PageId,
    string PagePath,
    string BrowserUri);

public sealed record AzureDevOpsWikiPageSnapshot(
    string Content,
    string ETag,
    string PagePath);

public sealed record AzureDevOpsWikiUpdateResult(
    string ETag);
