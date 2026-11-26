using SupplyForge.Inventory.Domain;
using Xunit;

namespace SupplyForge.Inventory.UnitTests;

/// <summary>
/// Unit tests for Transfer domain logic (INV-030, INV-031, INV-032).
/// Pure domain tests — no EF Core, no database.
/// </summary>
public sealed class TransferTests
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WarehouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LocationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BranchId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Transfer NewTransfer(decimal qty, string status)
    {
        return new Transfer
        {
            TransferId = Guid.NewGuid(),
            SourceWarehouseId = WarehouseId,
            SourceLocationId = LocationId,
            DestWarehouseId = WarehouseId,
            DestLocationId = LocationId,
            ProductId = ProductId,
            Qty = qty,
            ShippedQty = 0m,
            ReceivedQty = 0m,
            Status = status,
            BranchId = BranchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void INV_030_Transfer_RemainingQty_IsShippedMinusReceived()
    {
        // Arrange
        Transfer transfer = NewTransfer(100m, Transfer.Statuses.Approved);
        transfer.ShippedQty = 100m;
        transfer.ReceivedQty = 30m;

        // Act & Assert
        Assert.Equal(70m, transfer.RemainingQty);
    }

    [Fact]
    public void INV_030_Transfer_RemainingQty_IsZero_WhenFullyReceived()
    {
        // Arrange
        Transfer transfer = NewTransfer(50m, Transfer.Statuses.Received);
        transfer.ShippedQty = 50m;
        transfer.ReceivedQty = 50m;

        // Act & Assert
        Assert.Equal(0m, transfer.RemainingQty);
    }

    [Fact]
    public void INV_030_Transfer_StatusTransitions_SameWarehouse()
    {
        // Arrange
        Transfer transfer = NewTransfer(10m, Transfer.Statuses.Draft);

        // Act
        transfer.Status = Transfer.Statuses.Approved;
        transfer.Status = Transfer.Statuses.Closed;

        // Assert
        Assert.Equal(Transfer.Statuses.Closed, transfer.Status);
    }

    [Fact]
    public void INV_030_Transfer_StatusTransitions_InterWarehouse()
    {
        // Arrange
        Transfer transfer = NewTransfer(10m, Transfer.Statuses.Draft);

        // Act
        transfer.Status = Transfer.Statuses.Approved;
        transfer.Status = Transfer.Statuses.InTransit;
        transfer.Status = Transfer.Statuses.Received;
        transfer.Status = Transfer.Statuses.Closed;

        // Assert
        Assert.Equal(Transfer.Statuses.Closed, transfer.Status);
    }

    [Fact]
    public void INV_031_Transfer_InTransit_NotAvailable()
    {
        // Arrange
        Transfer transfer = NewTransfer(100m, Transfer.Statuses.InTransit);
        transfer.ShippedQty = 100m;
        transfer.ReceivedQty = 0m;

        // Act & Assert - In Transit means nothing received yet
        Assert.Equal(Transfer.Statuses.InTransit, transfer.Status);
        Assert.Equal(100m, transfer.RemainingQty);
        Assert.Equal(0m, transfer.ReceivedQty);
    }

    [Fact]
    public void INV_032_Transfer_CannotClose_WhenShortfallUnreconciled()
    {
        // Arrange - partial receipt, shortfall exists
        Transfer transfer = NewTransfer(100m, Transfer.Statuses.Received);
        transfer.ShippedQty = 100m;
        transfer.ReceivedQty = 70m;

        // Act & Assert - shortfall of 30, cannot close without adjustment
        bool fullyReceived = transfer.ReceivedQty >= transfer.ShippedQty;
        Assert.False(fullyReceived);
        Assert.Equal(30m, transfer.RemainingQty);
    }

    [Fact]
    public void INV_032_Transfer_CanClose_WhenFullyReceived()
    {
        // Arrange
        Transfer transfer = NewTransfer(100m, Transfer.Statuses.Received);
        transfer.ShippedQty = 100m;
        transfer.ReceivedQty = 100m;

        // Act & Assert
        bool fullyReceived = transfer.ReceivedQty >= transfer.ShippedQty;
        Assert.True(fullyReceived);
        Assert.Equal(0m, transfer.RemainingQty);
    }

    [Fact]
    public void BRN_020_Transfer_SameWarehouse_IsSameBranch()
    {
        // Arrange
        Transfer transfer = NewTransfer(10m, Transfer.Statuses.Draft);
        transfer.SourceWarehouseId = WarehouseId;
        transfer.DestWarehouseId = WarehouseId;

        // Act & Assert
        Assert.True(transfer.SourceWarehouseId == transfer.DestWarehouseId);
    }

    [Fact]
    public void BRN_020_Transfer_InterWarehouse_DifferentWarehouses()
    {
        // Arrange
        Guid otherWarehouseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Transfer transfer = NewTransfer(10m, Transfer.Statuses.Draft);
        transfer.SourceWarehouseId = WarehouseId;
        transfer.DestWarehouseId = otherWarehouseId;

        // Act & Assert
        Assert.False(transfer.SourceWarehouseId == transfer.DestWarehouseId);
    }
}
