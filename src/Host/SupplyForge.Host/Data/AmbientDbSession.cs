using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.Shared.Data;

namespace SupplyForge.Host.Data;

/// <summary>
/// ADR-003 ambient session (Option B): one shared <see cref="DbConnection"/>
/// and <see cref="DbTransaction"/> join the PlatformCore and Integrations
/// DbContexts so a multi-module write is a single SQL Server transaction
/// (MOD-TXN-01/04 — no distributed transactions). The factory enlists both
/// contexts when the session begins; the owner commits or rolls back once;
/// every completion path — commit, rollback, and dispose-without-commit —
/// funnels into one idempotent cleanup.
/// </summary>
public sealed class AmbientDbSession : IDbSession
{
    private readonly PlatformCoreDbContext _platform;
    private readonly IntegrationsDbContext _integrations;
    private readonly string _platformConnectionString;
    private readonly string _integrationsConnectionString;
    private readonly DbConnection _shared;
    private readonly DbTransaction _transaction;
    private IDbContextTransaction? _platformEnlistment;
    private IDbContextTransaction? _integrationsEnlistment;
    private bool _completed;
    private bool _cleaned;

    internal AmbientDbSession(
        PlatformCoreDbContext platform,
        IntegrationsDbContext integrations,
        string platformConnectionString,
        string integrationsConnectionString,
        DbConnection shared,
        DbTransaction transaction)
    {
        _platform = platform;
        _integrations = integrations;
        _platformConnectionString = platformConnectionString;
        _integrationsConnectionString = integrationsConnectionString;
        _shared = shared;
        _transaction = transaction;
    }

    public bool IsCompleted => _completed;

    /// <summary>
    /// Attaches both contexts to the shared connection and transaction
    /// (spike case B: SetDbConnection + UseTransaction on each context —
    /// a transaction from a different connection is rejected by ADO/EF).
    /// </summary>
    internal async Task JoinAsync(CancellationToken cancellationToken)
    {
        _platform.Database.SetDbConnection(_shared);
        _integrations.Database.SetDbConnection(_shared);
        _platformEnlistment = await _platform.Database.UseTransactionAsync(_transaction, cancellationToken);
        _integrationsEnlistment = await _integrations.Database.UseTransactionAsync(_transaction, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
        await CleanupAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
        await CleanupAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                // Dispose-without-commit ⇒ rollback (ADR-003: the session
                // owner commits or rolls back once). Roll back explicitly so
                // a dead transaction surfaces here instead of silently.
                await _transaction.RollbackAsync();
            }
            finally
            {
                _completed = true;
                await CleanupAsync();
            }
        }
        else
        {
            // Committed/rolled back already — cleanup is idempotent, so a
            // repeated dispose (or dispose right after commit) is a no-op.
            await CleanupAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// One idempotent unwind, in the order proven against real SQL Server by
    /// the A0-6 spike: disposing the UseTransaction enlistments disposes the
    /// raw transaction (double-dispose is idempotent); each context then
    /// gets a fresh connection built from its captured connection string —
    /// the spike proved <c>SetDbConnection</c> disposes EF's own original
    /// connection and that <c>SetDbConnection(null)</c> does NOT fall back
    /// to the connection string, so a fresh connection is the only working
    /// restore — and finally the shared connection returns to the pool.
    /// </summary>
    private async Task CleanupAsync()
    {
        if (_cleaned)
        {
            return;
        }

        _cleaned = true;

        if (_platformEnlistment is not null)
        {
            await _platformEnlistment.DisposeAsync();
        }

        if (_integrationsEnlistment is not null)
        {
            await _integrationsEnlistment.DisposeAsync();
        }

        await _transaction.DisposeAsync();

        _platform.Database.SetDbConnection(new SqlConnection(_platformConnectionString));
        _integrations.Database.SetDbConnection(new SqlConnection(_integrationsConnectionString));

        await _shared.DisposeAsync();
    }
}

/// <summary>
/// Starts the ambient session (ADR-003, NFR-043 item 5). Host owns this
/// composition because it is the only project that may reference both
/// module DbContexts (MOD-1 acyclic module graph — PlatformCore must not
/// reference Integrations). Both contexts are configured with the same
/// connection string (one SQL Server, per-module schemas), so a single
/// shared connection and transaction cover both.
/// </summary>
public sealed class AmbientDbSessionFactory : IDbSessionFactory
{
    private readonly PlatformCoreDbContext _platform;
    private readonly IntegrationsDbContext _integrations;

    public AmbientDbSessionFactory(PlatformCoreDbContext platform, IntegrationsDbContext integrations)
    {
        _platform = platform;
        _integrations = integrations;
    }

    public async Task<IDbSession> BeginAsync(CancellationToken cancellationToken = default)
    {
        var platformConnectionString = RequireConnectionString(_platform.Database.GetDbConnection());
        var integrationsConnectionString = RequireConnectionString(_integrations.Database.GetDbConnection());

        var shared = new SqlConnection(platformConnectionString);
        AmbientDbSession? session = null;
        try
        {
            await shared.OpenAsync(cancellationToken);
            var transaction = await shared.BeginTransactionAsync(cancellationToken);
            session = new AmbientDbSession(
                _platform,
                _integrations,
                platformConnectionString,
                integrationsConnectionString,
                shared,
                transaction);
            await session.JoinAsync(cancellationToken);
            return session;
        }
        catch
        {
            // Unwind whatever was created so nothing leaks — an undisposed
            // open SqlConnection would leak a pooled physical connection.
            // DisposeAsync is idempotent and safe on a partially joined
            // session; if unwinding itself fails, its exception replaces
            // this one (both are fatal to the request).
            if (session is null)
            {
                await shared.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
            }

            throw;
        }
    }

    private static string RequireConnectionString(DbConnection connection) =>
        !string.IsNullOrEmpty(connection.ConnectionString)
            ? connection.ConnectionString
            : throw new InvalidOperationException(
                "The ambient session requires a connection string on every joined DbContext.");
}
