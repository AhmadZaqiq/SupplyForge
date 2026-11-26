namespace SupplyForge.MasterData.Contracts.Warehouse;

/// <summary>
/// Port interface for resolving the branch that owns a warehouse.
/// Master Data defines the port; MasterData.Infrastructure implements it.
/// Inventory depends on this contract for branch isolation (BRN-012).
/// </summary>
public interface IWarehouseBranchResolver
{
    /// <summary>
    /// Returns the BranchId that owns the given warehouse, or null if the warehouse does not exist.
    /// </summary>
    /// <param name="warehouseId">The warehouse to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The owning BranchId, or null if not found.</returns>
    Task<Guid?> GetBranchIdAsync(Guid warehouseId, CancellationToken cancellationToken = default);
}