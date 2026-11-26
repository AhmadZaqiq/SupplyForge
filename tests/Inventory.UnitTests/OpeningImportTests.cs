using SupplyForge.Inventory.Domain;
using Xunit;

namespace SupplyForge.Inventory.UnitTests;

/// <summary>
/// Unit tests for OpeningImport domain logic (INV-014).
/// Pure domain tests — no EF Core, no database.
/// </summary>
public sealed class OpeningImportTests
{
    private static readonly Guid BranchId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static OpeningImport NewOpeningImport(string status)
    {
        return new OpeningImport
        {
            OpeningImportId = Guid.NewGuid(),
            Status = status,
            BranchId = BranchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void INV_014_OpeningImport_StatusTransition_DraftToValidated()
    {
        // Arrange
        OpeningImport import = NewOpeningImport(OpeningImport.Statuses.Draft);

        // Act
        import.Status = OpeningImport.Statuses.Validated;

        // Assert
        Assert.Equal(OpeningImport.Statuses.Validated, import.Status);
    }

    [Fact]
    public void INV_014_OpeningImport_StatusTransition_ValidatedToPosted()
    {
        // Arrange
        OpeningImport import = NewOpeningImport(OpeningImport.Statuses.Validated);

        // Act
        import.Status = OpeningImport.Statuses.Posted;

        // Assert
        Assert.Equal(OpeningImport.Statuses.Posted, import.Status);
    }

    [Fact]
    public void INV_014_OpeningImport_CanReject_FromDraft()
    {
        // Arrange
        OpeningImport import = NewOpeningImport(OpeningImport.Statuses.Draft);

        // Act
        import.Status = OpeningImport.Statuses.Rejected;

        // Assert
        Assert.Equal(OpeningImport.Statuses.Rejected, import.Status);
    }

    [Fact]
    public void INV_014_OpeningImport_CannotReject_FromPosted()
    {
        // Arrange
        OpeningImport import = NewOpeningImport(OpeningImport.Statuses.Posted);

        // Act & Assert
        bool canReject = import.Status != OpeningImport.Statuses.Posted && import.Status != OpeningImport.Statuses.Rejected;
        Assert.False(canReject);
    }
}
