namespace SupplyForge.Shared.Jobs;

/// <summary>
/// MOD-1a: JobRun is a shared table owned by Platform Core infrastructure.
/// </summary>
public interface IJobRunStore
{
    Task<Guid> StartAsync(string jobName, string? correlationId, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid jobRunId, CancellationToken cancellationToken = default);

    Task FailAsync(Guid jobRunId, string error, CancellationToken cancellationToken = default);
}
