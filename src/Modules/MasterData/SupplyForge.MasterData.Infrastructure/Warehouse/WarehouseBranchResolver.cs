using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.MasterData.Infrastructure.Data;

namespace SupplyForge.MasterData.Infrastructure.Warehouse;

/// <summary>
/// Implementation of <see cref="IWarehouseBranchResolver"/> that reads the branch
/// from the Master Data warehouse table. This runs in the same ambient transaction
/// as the caller (A1-5 Increment 2).
/// </summary>
public sealed class WarehouseBranchResolver : IWarehouseBranchResolver
{
    private readonly MasterDataDbContext _db;

    public WarehouseBranchResolver(MasterDataDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> GetBranchIdAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        Guid? branchId = await _db.Warehouses
            .Where(w => w.Id == warehouseId)
            .Select(w => (Guid?)w.BranchId)
            .FirstOrDefaultAsync(cancellationToken);

        return branchId;
    }
}