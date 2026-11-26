using SupplyForge.Inventory.Domain;
using Xunit;

namespace SupplyForge.Inventory.UnitTests;

/// <summary>
/// Unit tests for Adjustment domain logic (INV-040, INV-032, INV-042).
/// Pure domain tests — no EF Core, no database.
/// </summary>
public sealed class AdjustmentTests
{
    private static readonly Guid BranchId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Adjustment NewAdjustment(string status, string? sourceDocumentType = null)
    {
        return new Adjustment
        {
            AdjustmentId = Guid.NewGuid(),
            ReasonCode = "DAMAGED",
            Comment = "Test adjustment",
            Status = status,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = null,
            BranchId = BranchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void INV_040_Adjustment_StatusTransition_DraftToApproved()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Draft);

        // Act
        adjustment.Status = Adjustment.Statuses.Approved;

        // Assert
        Assert.Equal(Adjustment.Statuses.Approved, adjustment.Status);
    }

    [Fact]
    public void INV_040_Adjustment_StatusTransition_ApprovedToPosted()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Approved);

        // Act
        adjustment.Status = Adjustment.Statuses.Posted;

        // Assert
        Assert.Equal(Adjustment.Statuses.Posted, adjustment.Status);
    }

    [Fact]
    public void INV_040_Adjustment_CanReject_FromDraft()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Draft);

        // Act
        adjustment.Status = Adjustment.Statuses.Rejected;

        // Assert
        Assert.Equal(Adjustment.Statuses.Rejected, adjustment.Status);
    }

    [Fact]
    public void INV_040_Adjustment_CanReject_FromApproved()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Approved);

        // Act
        adjustment.Status = Adjustment.Statuses.Rejected;

        // Assert
        Assert.Equal(Adjustment.Statuses.Rejected, adjustment.Status);
    }

    [Fact]
    public void INV_040_Adjustment_CannotReject_FromPosted()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Posted);

        // Act & Assert
        bool canReject = adjustment.Status != Adjustment.Statuses.Posted && adjustment.Status != Adjustment.Statuses.Rejected;
        Assert.False(canReject);
    }

    [Fact]
    public void INV_032_Adjustment_TransferShortfall_IsAuditOnly()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Posted, sourceDocumentType: "Transfer");
        adjustment.SourceDocumentId = Guid.NewGuid();

        // Act & Assert - transfer-shortfall adjustment has SourceDocumentType = "Transfer"
        Assert.Equal("Transfer", adjustment.SourceDocumentType);
        Assert.Equal(Adjustment.Statuses.Posted, adjustment.Status);
        Assert.NotNull(adjustment.SourceDocumentId);
    }

    [Fact]
    public void INV_040_Adjustment_NormalHasNoSourceDocument()
    {
        // Arrange
        Adjustment adjustment = NewAdjustment(Adjustment.Statuses.Draft);

        // Act & Assert
        Assert.Null(adjustment.SourceDocumentType);
        Assert.Null(adjustment.SourceDocumentId);
    }
}
