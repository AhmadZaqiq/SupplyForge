namespace SupplyForge.Inventory.Contracts.Availability;

/// <summary>
/// Result of an availability query (INV-100).
/// All quantities are in base units (qty scale 4, DM-2).
/// </summary>
public record AvailabilityResultDto(
    Guid ProductId,
    Guid WarehouseId,
    decimal OnHand,
    decimal Available,
    decimal Reserved,
    decimal Blocked,
    decimal InTransit,
    decimal IncomingQty,
    DateTimeOffset? IncomingDate);

/// <summary>
/// A single stock bucket within an availability result.
/// </summary>
public record StockBucketDto(
    string LocationId,
    string LocationType,
    decimal OnHand,
    bool IsEligible);
