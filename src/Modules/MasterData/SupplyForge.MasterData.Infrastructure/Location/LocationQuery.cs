using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Contracts.Location;
using SupplyForge.MasterData.Infrastructure.Data;

namespace SupplyForge.MasterData.Infrastructure.Location;

/// <summary>
/// Query contract implementation for location eligibility information.
/// Reads from <see cref="MasterDataDbContext.StorageLocations"/> (master_data schema).
/// Lives in Master Data to avoid Inventory → MasterData implementation dependency.
/// </summary>
public sealed class LocationQuery : ILocationQuery
{
    private readonly MasterDataDbContext _masterData;

    public LocationQuery(MasterDataDbContext masterData)
    {
        _masterData = masterData;
    }

    public async Task<IReadOnlyDictionary<Guid, LocationInfo>> GetLocationInfosAsync(
        IEnumerable<Guid> locationIds,
        CancellationToken cancellationToken = default)
    {
        var ids = locationIds as Guid[] ?? locationIds.ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, LocationInfo>();
        }

        var locations = await _masterData.StorageLocations
            .Where(l => ids.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                l.WarehouseId,
                l.Type,
                l.IsBlocked,
                l.IsActive
            })
            .ToListAsync(cancellationToken);

        return locations.ToDictionary(
            l => l.Id,
            l => new LocationInfo(l.Id, l.WarehouseId, l.Type, l.IsBlocked, l.IsActive));
    }
}
