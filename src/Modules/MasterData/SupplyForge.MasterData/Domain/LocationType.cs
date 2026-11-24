namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Location type codes (MD-041, D-16). Stored as string status codes (DM-5).
/// </summary>
public static class LocationType
{
    public const string Receiving = "Receiving";
    public const string Quarantine = "Quarantine";
    public const string Storage = "Storage";
    public const string Returns = "Returns";
    public const string Staging = "Staging";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Receiving, Quarantine, Storage, Returns, Staging
    };

    public static bool IsValid(string type) => All.Contains(type);
}