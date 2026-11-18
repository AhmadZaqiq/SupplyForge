namespace SupplyForge.Shared.Data;

/// <summary>
/// Ambient transaction scope for a single logical DB transaction across
/// one or more module DbContexts (ADR-003). Joiners enlist in the ambient
/// transaction; the owner commits or rolls back once.
/// </summary>
public interface IDbSession : IAsyncDisposable
{
    bool IsCompleted { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts or joins the ambient transaction (ADR-003, NFR-043(5)).
/// </summary>
public interface IDbSessionFactory
{
    Task<IDbSession> BeginAsync(CancellationToken cancellationToken = default);
}
