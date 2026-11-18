namespace SupplyForge.Shared.Sequencing;

/// <summary>
/// MOD-1a: DocumentSequence is a shared table owned by Platform Core infrastructure (ADR-005).
/// </summary>
public interface IDocumentSequenceService
{
    /// <summary>
    /// Atomically allocate the next sequence value and return the formatted number.
    /// </summary>
    Task<string> NextAsync(
        string scope,
        string prefix,
        int padWidth,
        CancellationToken cancellationToken = default);
}
