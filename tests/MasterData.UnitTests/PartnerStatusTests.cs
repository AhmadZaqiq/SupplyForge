using SupplyForge.MasterData.Domain;

namespace SupplyForge.MasterData.UnitTests;

/// <summary>
/// A1-3 status codes and credit-limit default (MD-020, MD-030, DM-5, and the
/// owner's Q1 answer). Pure domain rules — no database, no SQL Server.
/// </summary>
public class PartnerStatusTests
{
    [Fact]
    public void MD_020_SupplierStatuses_AreExactlyTheThreeDocumentedCodes()
    {
        Assert.Equal(
            new[] { SupplierStatus.Active, SupplierStatus.Inactive, SupplierStatus.Blocked },
            SupplierStatus.All);

        Assert.True(SupplierStatus.IsValid(SupplierStatus.Active));
        Assert.True(SupplierStatus.IsValid(SupplierStatus.Inactive));
        Assert.True(SupplierStatus.IsValid(SupplierStatus.Blocked));
    }

    [Fact]
    public void MD_020_UnknownSupplierStatusCode_IsNotValid()
    {
        // DM-5: an unrecognised value must never pass as a status, or a typo
        // would become a silent fourth state.
        Assert.False(SupplierStatus.IsValid("Blocked "));
        Assert.False(SupplierStatus.IsValid("pending"));
        Assert.False(SupplierStatus.IsValid(string.Empty));
        Assert.False(SupplierStatus.IsValid("ACTIVE"), "status codes are ordinal, not case-insensitive");
    }

    [Fact]
    public void MD_020_SupplierStatusTransitions_AreTested_ForEveryDistinctValidPair()
    {
        // DM-5 "transitions tested": MD-020 states no restriction, so every
        // distinct pair of recognised codes must be allowed.
        foreach (var from in SupplierStatus.All)
        {
            foreach (var to in SupplierStatus.All)
            {
                if (from == to)
                {
                    Assert.False(
                        SupplierStatus.CanTransition(from, to),
                        $"'{from}' → '{to}' is not a transition");
                    continue;
                }

                Assert.True(
                    SupplierStatus.CanTransition(from, to),
                    $"'{from}' → '{to}' must be an allowed transition");
            }
        }
    }

    [Fact]
    public void MD_020_SupplierStatusTransition_FromUnknownCode_IsRejected()
    {
        Assert.False(SupplierStatus.CanTransition("bogus", SupplierStatus.Blocked));
        Assert.False(SupplierStatus.CanTransition(SupplierStatus.Active, "bogus"));
        Assert.False(SupplierStatus.CanTransition(string.Empty, SupplierStatus.Active));
    }

    [Fact]
    public void MD_020_Supplier_DefaultsToTheActiveStatusCode()
    {
        var supplier = new Supplier();
        Assert.Equal(SupplierStatus.Active, supplier.Status);
        Assert.True(SupplierStatus.IsValid(supplier.Status));
    }

    [Fact]
    public void MD_030_CustomerStatuses_AreExactlyTheFourDocumentedCodes()
    {
        Assert.Equal(
            new[]
            {
                CustomerStatus.Active,
                CustomerStatus.Inactive,
                CustomerStatus.Blocked,
                CustomerStatus.OnHold,
            },
            CustomerStatus.All);

        Assert.True(CustomerStatus.IsValid(CustomerStatus.Active));
        Assert.True(CustomerStatus.IsValid(CustomerStatus.Inactive));
        Assert.True(CustomerStatus.IsValid(CustomerStatus.Blocked));
        Assert.True(CustomerStatus.IsValid(CustomerStatus.OnHold));
    }

    [Fact]
    public void MD_030_UnknownCustomerStatusCode_IsNotValid()
    {
        Assert.False(CustomerStatus.IsValid("On Hold"), "two words are stored as one code: OnHold");
        Assert.False(CustomerStatus.IsValid("hold"));
        Assert.False(CustomerStatus.IsValid(string.Empty));
        Assert.False(CustomerStatus.IsValid("BLOCKED"), "status codes are ordinal, not case-insensitive");
    }

    [Fact]
    public void MD_030_CustomerStatusTransitions_AreTested_ForEveryDistinctValidPair()
    {
        // DM-5 "transitions tested": SAL's rule is about which statuses may
        // confirm an order, not about which transitions are legal — so every
        // distinct pair of recognised codes must be allowed.
        foreach (var from in CustomerStatus.All)
        {
            foreach (var to in CustomerStatus.All)
            {
                if (from == to)
                {
                    Assert.False(
                        CustomerStatus.CanTransition(from, to),
                        $"'{from}' → '{to}' is not a transition");
                    continue;
                }

                Assert.True(
                    CustomerStatus.CanTransition(from, to),
                    $"'{from}' → '{to}' must be an allowed transition");
            }
        }
    }

    [Fact]
    public void MD_030_CustomerStatusTransition_FromUnknownCode_IsRejected()
    {
        Assert.False(CustomerStatus.CanTransition("bogus", CustomerStatus.OnHold));
        Assert.False(CustomerStatus.CanTransition(CustomerStatus.Active, "bogus"));
        Assert.False(CustomerStatus.CanTransition(string.Empty, CustomerStatus.Active));
    }

    [Fact]
    public void MD_030_Customer_DefaultsToTheActiveStatusCode()
    {
        var customer = new Customer();
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.True(CustomerStatus.IsValid(customer.Status));
    }

    [Fact]
    public void MD_030_CustomerCreditLimit_DefaultsToZero_NoUnlimitedValueExists()
    {
        // Owner answer Q1: NOT NULL with default 0; 0 means "no credit
        // allowed" and P1 has no unlimited value (TD-3 fails closed). The
        // NOT NULL + database default are asserted against the live schema in
        // the integration test of the same requirement.
        var customer = new Customer();
        Assert.Equal(0m, customer.CreditLimit);
    }
}
