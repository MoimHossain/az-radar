using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public static class AzureDevOpsWikiUriParser
{
    public static bool TryParse(
        string? value,
        out AzureDevOpsWikiPageAddress? address,
        out string? error)
    {
        address = null;
        error = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Wiki page URI must be a valid HTTPS URI.";
            return false;
        }

        if (!string.Equals(uri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only Azure DevOps Services URIs on dev.azure.com are supported.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            error = "Credentials must not be embedded in the wiki URI.";
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var wikiMarker = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "_wiki", StringComparison.OrdinalIgnoreCase));
        if (wikiMarker != 2 ||
            segments.Length < 7 ||
            !string.Equals(segments[3], "wikis", StringComparison.OrdinalIgnoreCase))
        {
            error = "Wiki URI must identify an organization, project, wiki, and existing page.";
            return false;
        }

        if (!int.TryParse(segments[5], out var pageId) || pageId <= 0)
        {
            error = "Wiki URI must contain the numeric ID of an existing page.";
            return false;
        }

        var pageSegments = segments.Skip(6).ToArray();
        if (pageSegments.Length == 0)
        {
            error = "Wiki URI must identify an existing page.";
            return false;
        }

        var pagePath = "/" + string.Join("/", pageSegments);
        address = new AzureDevOpsWikiPageAddress(
            segments[0],
            segments[1],
            segments[4],
            pageId,
            pagePath,
            uri.GetLeftPart(UriPartial.Path));
        return true;
    }
}
