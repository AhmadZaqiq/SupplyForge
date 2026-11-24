using System.Reflection;
using SupplyForge.MasterData.Domain;

namespace SupplyForge.MasterData.UnitTests;

/// <summary>
/// A1-4 unit tests for the Warehouse / StorageLocation domain model
/// (MD-040, MD-041, MD-042, DM-1, DM-5). Pure domain — no database.
/// </summary>
public class StorageLocationTests
{
    [Fact]
    public void MD_040_Warehouse_HasBranchId_AndGuidKey()
    {
        var w = new Warehouse
        {
            Id = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Code = "WH-001",
            Name = "Main Warehouse",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.NotEqual(Guid.Empty, w.Id);
        Assert.NotEqual(Guid.Empty, w.BranchId);
        Assert.Equal("WH-001", w.Code);
        Assert.True(w.IsActive);
        Assert.Null(w.UpdatedAt);
    }

    [Fact]
    public void MD_041_StorageLocation_Type_IsOneOfFive_D16()
    {
        Assert.Equal(5, LocationType.All.Count);
        Assert.True(LocationType.All.SetEquals(new[] { "Receiving", "Quarantine", "Storage", "Returns", "Staging" }));

        foreach (var type in LocationType.All)
        {
            Assert.True(LocationType.IsValid(type));
        }
    }

    [Fact]
    public void MD_041_StorageLocation_Type_InvalidValue_IsRejectedByDomain()
    {
        Assert.False(LocationType.IsValid("InvalidType"));
        Assert.False(LocationType.IsValid(string.Empty));
        Assert.False(LocationType.IsValid("Picking"), "Picking is a workflow state, not a location type (D-16)");
    }

    [Fact]
    public void MD_042_StorageLocation_IsBlocked_DefaultsFalse()
    {
        var loc = new StorageLocation
        {
            Id = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Type = LocationType.Storage,
            Code = "LOC-001",
            Name = "Test Location",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.False(loc.IsBlocked);
    }

    [Fact]
    public void MD_042_StorageLocation_BlockedButActive_IsLegal_DoesNotImplyInactive()
    {
        // The PRD never says "blocked implies inactive" (owner, 2026-11-24):
        // a blocked Storage location can still hold stock and stays active.
        var loc = new StorageLocation
        {
            Id = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Type = LocationType.Storage,
            Code = "LOC-BLK",
            Name = "Blocked but active",
            IsBlocked = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.True(loc.IsBlocked);
        Assert.True(loc.IsActive);
    }

    [Fact]
    public void DM_1_WarehouseLocation_TransitiveBranchOwnership()
    {
        var branchId = Guid.NewGuid();
        var warehouse = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-001", Name = "Main", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location = new StorageLocation { Id = Guid.NewGuid(), WarehouseId = warehouse.Id, Type = LocationType.Storage, Code = "A-01", Name = "Aisle 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        Assert.Equal(branchId, warehouse.BranchId);
        Assert.Equal(warehouse.Id, location.WarehouseId);
    }

    [Fact]
    public void DM_5_DeactivateNotDelete_NoDeleteOperationExists()
    {
        // "deactivate not delete": the domain model exposes no delete operation.
        Assert.DoesNotContain(typeof(Warehouse).GetMethods(BindingFlags.Public | BindingFlags.Instance), m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(StorageLocation).GetMethods(BindingFlags.Public | BindingFlags.Instance), m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));

        var w = new Warehouse { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), Code = "WH-001", Name = "W", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        w.IsActive = false;
        Assert.False(w.IsActive);
    }
}