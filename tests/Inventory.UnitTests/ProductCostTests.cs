using SupplyForge.Inventory.Domain;
using Xunit;

namespace SupplyForge.Inventory.UnitTests;

/// <summary>
/// Unit tests for WAC Core domain logic in ProductCost (A1-6).
/// Pure domain tests — no EF Core, no database.
/// </summary>
public sealed class ProductCostTests
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WarehouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ProductCost NewProductCost(decimal qtyOnHand, decimal avgCost)
    {
        return new ProductCost
        {
            ProductId = ProductId,
            WarehouseId = WarehouseId,
            QtyOnHand = qtyOnHand,
            AvgCost = avgCost,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void WAC_02_OpeningBalance_SetsInitialQtyAndCost_WhenBucketEmpty()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act
        cost.ApplyOpeningBalance(100m, 15.50m);

        // Assert
        Assert.Equal(100m, cost.QtyOnHand);
        Assert.Equal(15.50m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_02_OpeningBalance_BlendsWithExistingStock()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act
        cost.ApplyOpeningBalance(50m, 16.00m);

        // Assert - (100*10 + 50*16) / 150 = 12.00
        Assert.Equal(150m, cost.QtyOnHand);
        Assert.Equal(12.00m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_02_OpeningBalance_RejectsNegativeQuantity()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyOpeningBalance(-10m, 10.00m));
    }

    [Fact]
    public void WAC_02_OpeningBalance_RejectsNegativeUnitCost()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyOpeningBalance(10m, -5.00m));
    }

    [Fact]
    public void WAC_03_Receipt_BlendsWAC_Correctly()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act
        cost.ApplyReceipt(50m, 12.00m);

        // Assert - (100*10 + 50*12) / 150 = 10.66666667
        Assert.Equal(150m, cost.QtyOnHand);
        Assert.Equal(10.66666667m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_03_Receipt_IntoZeroStock_EstablishesNewWAC()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act
        cost.ApplyReceipt(50m, 25.00m);

        // Assert
        Assert.Equal(50m, cost.QtyOnHand);
        Assert.Equal(25.00m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_03_Receipt_RejectsNonPositiveQuantity()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyReceipt(0m, 10.00m));
        Assert.Throws<ArgumentException>(() => cost.ApplyReceipt(-10m, 10.00m));
    }

    [Fact]
    public void WAC_03_Receipt_RejectsNegativeUnitCost()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyReceipt(10m, -1.00m));
    }

    [Fact]
    public void WAC_04_Issue_RecordsCurrentWAC_DoesNotReaverage()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act
        decimal costUsed = cost.ApplyIssue(30m);

        // Assert - WAC unchanged at 10.00, qty reduced
        Assert.Equal(10.00m, Math.Round(costUsed, 8));
        Assert.Equal(70m, cost.QtyOnHand);
        Assert.Equal(10.00m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_04_Issue_ToZero_ResetsWAC()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act
        decimal costUsed = cost.ApplyIssue(100m);

        // Assert - QtyOnHand = 0, AvgCost = 0, cost recorded at pre-issue WAC
        Assert.Equal(0m, cost.QtyOnHand);
        Assert.Equal(0m, cost.AvgCost);
        Assert.Equal(10.00m, Math.Round(costUsed, 8));
    }

    [Fact]
    public void WAC_04_Issue_RejectsNonPositiveQuantity()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyIssue(0m));
        Assert.Throws<ArgumentException>(() => cost.ApplyIssue(-10m));
    }

    [Fact]
    public void WAC_04_Issue_RejectsWhenZeroStock()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => cost.ApplyIssue(10m));
    }

    [Fact]
    public void WAC_04_Issue_RejectsWhenExceedsAvailableStock()
    {
        // Arrange
        ProductCost cost = NewProductCost(10m, 10.00m);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => cost.ApplyIssue(11m));
    }

    [Fact]
    public void WAC_05_TransferOut_UsesSourceWAC_DoesNotReaverage()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act
        decimal costUsed = cost.ApplyTransferOut(30m);

        // Assert - WAC unchanged at 10.00, qty reduced
        Assert.Equal(10.00m, Math.Round(costUsed, 8));
        Assert.Equal(70m, cost.QtyOnHand);
        Assert.Equal(10.00m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_05_TransferOut_ToZero_ResetsWAC()
    {
        // Arrange
        ProductCost cost = NewProductCost(50m, 15.00m);

        // Act
        decimal costUsed = cost.ApplyTransferOut(50m);

        // Assert
        Assert.Equal(0m, cost.QtyOnHand);
        Assert.Equal(0m, cost.AvgCost);
        Assert.Equal(15.00m, Math.Round(costUsed, 8));
    }

    [Fact]
    public void WAC_05_TransferOut_RejectsNonPositiveQuantity()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyTransferOut(0m));
        Assert.Throws<ArgumentException>(() => cost.ApplyTransferOut(-10m));
    }

    [Fact]
    public void WAC_05_TransferOut_RejectsWhenZeroStock()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => cost.ApplyTransferOut(10m));
    }

    [Fact]
    public void WAC_05_TransferOut_RejectsWhenExceedsAvailableStock()
    {
        // Arrange
        ProductCost cost = NewProductCost(10m, 10.00m);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => cost.ApplyTransferOut(11m));
    }

    [Fact]
    public void WAC_05_TransferIn_BlendsWithSourceWAC()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 20.00m);

        // Act - TransferIn at source WAC of 10.00
        cost.ApplyTransferIn(30m, 10.00m);

        // Assert - (100*20 + 30*10) / 130 = 17.69230769
        Assert.Equal(130m, cost.QtyOnHand);
        Assert.Equal(17.69230769m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_05_TransferIn_IntoZeroStock_EstablishesWACFromSource()
    {
        // Arrange
        ProductCost cost = NewProductCost(0m, 0m);

        // Act
        cost.ApplyTransferIn(50m, 12.50m);

        // Assert
        Assert.Equal(50m, cost.QtyOnHand);
        Assert.Equal(12.50m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_05_TransferIn_RejectsNonPositiveQuantity()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyTransferIn(0m, 10.00m));
        Assert.Throws<ArgumentException>(() => cost.ApplyTransferIn(-10m, 10.00m));
    }

    [Fact]
    public void WAC_05_TransferIn_RejectsNegativeSourceCost()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => cost.ApplyTransferIn(10m, -1.00m));
    }

    [Fact]
    public void WAC_08_ZeroStock_NextReceiptEstablishesNewWAC()
    {
        // Arrange
        ProductCost cost = NewProductCost(100m, 10.00m);
        cost.ApplyIssue(100m);

        // Assert - AvgCost reset to 0
        Assert.Equal(0m, cost.QtyOnHand);
        Assert.Equal(0m, cost.AvgCost);

        // Act - new receipt establishes fresh WAC
        cost.ApplyReceipt(50m, 25.00m);

        // Assert
        Assert.Equal(50m, cost.QtyOnHand);
        Assert.Equal(25.00m, Math.Round(cost.AvgCost, 8));
    }

    [Fact]
    public void WAC_10_NoResidualDust_OnFinalIssue_TotalRecordedEqualsImplicitValue()
    {
        // Arrange - track the sum of costs recorded across all issues
        ProductCost cost = NewProductCost(30m, 10.00000001m);

        // Act - issue all 30 units in three partial issues
        decimal totalRecordedCost = 0m;
        totalRecordedCost += cost.ApplyIssue(10m) * 10m;
        totalRecordedCost += cost.ApplyIssue(10m) * 10m;
        totalRecordedCost += cost.ApplyIssue(10m) * 10m;

        // Assert - total recorded equals implicit value (30 * 10.00000001), no dust lost
        Assert.Equal(0m, cost.QtyOnHand);
        Assert.Equal(0m, cost.AvgCost);
        Assert.Equal(30m * 10.00000001m, totalRecordedCost);
    }

    [Fact]
    public void WAC_09_Scale8_Rounding_RetainedOnReceipt()
    {
        // Arrange
        ProductCost cost = NewProductCost(3m, 10.00m);

        // Act - receipt at repeating decimal cost
        cost.ApplyReceipt(7m, 10.00m / 3.0m);

        // Assert - AvgCost stored at scale 8: (3*10 + 7*3.33333333) / 10 = 5.33333333
        Assert.Equal(10m, cost.QtyOnHand);
        Assert.Equal(5.33333333m, Math.Round(cost.AvgCost, 8));
    }
}
