namespace SupplyForge.MasterData.Contracts.Location;

/// <summary>
/// Port interface for guarding location deactivation (MD-042).
/// Master Data defines the port; Inventory implements it in A1-5 when
/// StockBalance exists. The Host wires the implementation.
/// This avoids a compile-time cycle: MasterData → PlatformCore only;
/// Inventory → MasterData (not the reverse).
/// </summary>
public interface ILocationDeactivationGuard
{
    /// <summary>
    /// Returns true if the location can be deactivated (no stock holding).
    /// In A1-4 this always returns true (placeholder).
    /// In A1-5 Inventory implements this to check StockBalance.
    /// </summary>
    /// <param name="locationId">The location to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deactivation is allowed.</returns>
    Task<bool> CanDeactivateAsync(Guid locationId, CancellationToken cancellationToken = default);
}