using SupplyForge.MasterData.Domain;

namespace SupplyForge.MasterData.UnitTests;

/// <summary>
/// MD-013 (product statuses) and DM-5 (string status codes, transitions
/// tested). These are pure domain rules, so they live in the unit project —
/// no database, no SQL Server. The archive guard's real data source
/// (on-hand from the inventory ledger) arrives in A1-5; the rule itself is
/// written and pinned here first (decision D-d, A1-PROGRESS §1a).
/// </summary>
public class ProductStatusTests
{
    [Fact]
    public void MD_013_ProductStatuses_AreExactlyTheThreeDocumentedCodes()
    {
        Assert.Equal(
            new[] { ProductStatus.Active, ProductStatus.Inactive, ProductStatus.Archived },
            ProductStatus.All);

        Assert.True(ProductStatus.IsValid(ProductStatus.Active));
        Assert.True(ProductStatus.IsValid(ProductStatus.Inactive));
        Assert.True(ProductStatus.IsValid(ProductStatus.Archived));
    }

    [Fact]
    public void MD_013_UnknownStatusCode_IsNotValid()
    {
        // DM-5 stores string status codes: an unrecognised value must never
        // pass as a status, or a typo would become a silent fourth state.
        Assert.False(ProductStatus.IsValid("Archived "));
        Assert.False(ProductStatus.IsValid("discontinued"));
        Assert.False(ProductStatus.IsValid(string.Empty));
        Assert.False(ProductStatus.IsValid("ACTIVE"), "status codes are ordinal, not case-insensitive");
    }

    [Fact]
    public void MD_013_StatusTransitions_AreTested_ForEveryDistinctValidPair()
    {
        // DM-5 "transitions tested". MD-013 restricts exactly one move (see
        // CanArchive), so every other distinct pair must be allowed.
        foreach (var from in ProductStatus.All)
        {
            foreach (var to in ProductStatus.All)
            {
                if (from == to)
                {
                    Assert.False(
                        ProductStatus.CanTransition(from, to),
                        $"'{from}' → '{to}' is not a transition");
                    continue;
                }

                Assert.True(
                    ProductStatus.CanTransition(from, to),
                    $"'{from}' → '{to}' must be an allowed transition");
            }
        }
    }

    [Fact]
    public void MD_013_StatusTransition_FromUnknownCode_IsRejected()
    {
        Assert.False(ProductStatus.CanTransition("bogus", ProductStatus.Archived));
        Assert.False(ProductStatus.CanTransition(ProductStatus.Active, "bogus"));
        Assert.False(ProductStatus.CanTransition(string.Empty, ProductStatus.Active));
    }

    [Fact]
    public void MD_013_Archive_IsAllowedOnlyWhenOnHandIsNotPositive()
    {
        // MD-013: "no archive with on-hand > 0".
        Assert.True(ProductStatus.CanArchive(0m), "no stock ⇒ archivable");
        Assert.True(ProductStatus.CanArchive(0.0000m));

        Assert.False(ProductStatus.CanArchive(0.0001m), "any stock at all blocks archiving");
        Assert.False(ProductStatus.CanArchive(1m));
        Assert.False(ProductStatus.CanArchive(1250.5m));
    }

    [Fact]
    public void MD_010_Product_DefaultsToTheActiveStatusCode()
    {
        var product = new Product();
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.True(ProductStatus.IsValid(product.Status));
    }
}
