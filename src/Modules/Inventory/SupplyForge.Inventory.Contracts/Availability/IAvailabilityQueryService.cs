namespace SupplyForge.Inventory.Contracts.Availability;

/// <summary>
/// Query contract for inventory availability (INV-100).
/// Returns all stock buckets per product+warehouse: on-hand, available,
/// reserved, blocked, in-transit, incoming qty and incoming date.
/// </summary>
public interface IAvailabilityQueryService
{
    /// <summary>
    /// Returns availability for a product at a warehouse.
    /// Branch scope is enforced: the caller must have access to the warehouse's
    /// branch (BRN-012).
    /// </summary>
    /// <param name="productId">The product to query.</param>
    /// <param name="warehouseId">The warehouse to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Availability result with all stock buckets.</returns>
    Task<AvailabilityResultDto> GetAvailabilityAsync(
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken = default);
}
