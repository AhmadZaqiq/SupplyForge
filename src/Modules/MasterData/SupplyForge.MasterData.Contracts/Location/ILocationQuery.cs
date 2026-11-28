namespace SupplyForge.MasterData.Contracts.Location;

/// <summary>
/// Query contract for location eligibility information (INV-100, INV-091).
/// Master Data owns location data; Inventory reads via this contract to avoid
/// a compile-time dependency on Master Data implementation (MOD-2).
/// </summary>
public interface ILocationQuery
{
    /// <summary>
    /// Returns location info for the given location IDs.
    /// Includes Type (Storage, Receiving, Quarantine, Returns, Staging),
    /// IsBlocked, and IsActive.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LocationInfo>> GetLocationInfosAsync(
        IEnumerable<Guid> locationIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only location eligibility information.
/// </summary>
public record LocationInfo(
    Guid LocationId,
    Guid WarehouseId,
    string Type,
    bool IsBlocked,
    bool IsActive);

/// <summary>
/// Location type string constants (INV-090, INV-091).
/// Defined in contracts so Inventory can reference them without depending on
/// Master Data implementation (MOD-2).
/// </summary>
public static class LocationTypes
{
    public const string Receiving = "Receiving";
    public const string Quarantine = "Quarantine";
    public const string Storage = "Storage";
    public const string Returns = "Returns";
    public const string Staging = "Staging";
}
