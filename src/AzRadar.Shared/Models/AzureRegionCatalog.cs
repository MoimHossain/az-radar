namespace AzRadar.Shared.Models;

public static class AzureRegionCatalog
{
    public static readonly IReadOnlyList<string> PublicRegions =
    [
        "Australia Central",
        "Australia Central 2",
        "Australia East",
        "Australia Southeast",
        "Brazil South",
        "Brazil Southeast",
        "Canada Central",
        "Canada East",
        "Central India",
        "Central US",
        "Central US EUAP",
        "Chile Central",
        "East Asia",
        "East US",
        "East US 2",
        "East US 2 EUAP",
        "France Central",
        "France South",
        "Germany North",
        "Germany West Central",
        "Indonesia Central",
        "Israel Central",
        "Italy North",
        "Japan East",
        "Japan West",
        "Jio India Central",
        "Jio India West",
        "Korea Central",
        "Korea South",
        "Malaysia West",
        "Mexico Central",
        "New Zealand North",
        "North Central US",
        "North Europe",
        "Norway East",
        "Norway West",
        "Poland Central",
        "Qatar Central",
        "South Africa North",
        "South Africa West",
        "South Central US",
        "South India",
        "Southeast Asia",
        "Spain Central",
        "Sweden Central",
        "Sweden South",
        "Switzerland North",
        "Switzerland West",
        "UAE Central",
        "UAE North",
        "UK South",
        "UK West",
        "West Central US",
        "West Europe",
        "West India",
        "West US",
        "West US 2",
        "West US 3"
    ];

    public static bool TryGetCanonicalName(string value, out string canonicalName)
    {
        canonicalName = PublicRegions.FirstOrDefault(
            region => string.Equals(region, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return canonicalName.Length > 0;
    }
}
