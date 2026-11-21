using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupplyForge.Shared.Data;

namespace SupplyForge.Host.Data;

/// <summary>
/// ADR-003 ambient session (Option B): one shared <see cref="DbConnection"/>
/// and <see cref="DbTransaction"/> join every module DbContext into a single
/// SQL Server transaction (MOD-TXN-01/04 — no distributed transactions). The
/// factory enlists all contexts when the session begins; the owner commits or
/// rolls back once; every completion path — commit, rollback, and
/// dispose-without-commit — funnels into one idempotent cleanup.
///
/// <para>
/// A1-1 generalized this from the original two contexts to N: a module joins
/// simply by being handed to <see cref="AmbientDbSessionFactory"/>. The Host
/// stays the only composition root (MOD-1), so the list of joined contexts is
/// declared there. Every joined context must share one connection string —
/// there is exactly one shared connection, so two different target databases
/// could not both be reached through it.
/// </para>
/// </summary>
public sealed class AmbientDbSession : IDbSession
{
    private readonly DbContext[] _contexts;
    private readonly string[] _connectionStrings;
    private readonly DbConnection _shared;
    private readonly DbTransaction _transaction;
    private readonly IDbContextTransaction?[] _enlistments;
    private bool _completed;
    private bool _cleaned;

    internal AmbientDbSession(
        DbContext[] contexts,
        string[] connectionStrings,
        DbConnection shared,
        DbTransaction transaction)
    {
        _contexts = contexts;
        _connectionStrings = connectionStrings;
        _shared = shared;
        _transaction = transaction;
        _enlistments = new IDbContextTransaction?[contexts.Length];
    }

    public bool IsCompleted => _completed;

    /// <summary>
    /// Attaches every context to the shared connection and transaction
    /// (spike case B: SetDbConnection + UseTransaction on each context —
    /// a transaction from a different connection is rejected by ADO/EF).
    /// </summary>
    internal async Task JoinAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _contexts.Length; i++)
        {
            _contexts[i].Database.SetDbConnection(_shared);
            _enlistments[i] = await _contexts[i].Database.UseTransactionAsync(_transaction, cancellationToken);
        }
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

        for (var i = 0; i < _enlistments.Length; i++)
        {
            if (_enlistments[i] is not null)
            {
                await _enlistments[i]!.DisposeAsync();
            }
        }

        await _transaction.DisposeAsync();

        for (var i = 0; i < _contexts.Length; i++)
        {
            _contexts[i].Database.SetDbConnection(new SqlConnection(_connectionStrings[i]));
        }

        await _shared.DisposeAsync();
    }
}

/// <summary>
/// Starts the ambient session (ADR-003, NFR-043 item 5). Host owns this
/// composition because it is the only project that may reference the module
/// DbContexts (MOD-1 acyclic module graph — PlatformCore must not reference
/// Integrations, MasterData must not reference either). All contexts are
/// configured with the same connection string (one SQL Server, per-module
/// schemas), so a single shared connection and transaction cover all of them.
/// </summary>
public sealed class AmbientDbSessionFactory : IDbSessionFactory
{
    private readonly DbContext[] _contexts;

    /// <param name="contexts">
    /// Every module DbContext to enlist, in any order. The Host declares the
    /// list; tests may pass the subset they exercise.
    /// </param>
    public AmbientDbSessionFactory(params DbContext[] contexts)
    {
        if (contexts.Length == 0)
        {
            throw new ArgumentException(
                "The ambient session needs at least one DbContext to enlist.",
                nameof(contexts));
        }

        _contexts = contexts;
    }

    public async Task<IDbSession> BeginAsync(CancellationToken cancellationToken = default)
    {
        // Configured string from EF options — deliberately NOT
        // GetDbConnection().ConnectionString: SqlClient redacts the password
        // from a connection's ConnectionString once it has been opened, and
        // in the Host the scoped context has normally already executed the
        // request's queries (permission resolution) before a session begins,
        // so the live connection's string would be credential-less ("Login
        // failed for user 'sa'" — found by the A0-6 HTTP round-trip test).
        // Database.GetConnectionString() reads the unchanged configured
        // value, which is also what the post-session restore needs.
        var connectionStrings = new string[_contexts.Length];
        for (var i = 0; i < _contexts.Length; i++)
        {
            connectionStrings[i] = RequireConnectionString(_contexts[i].Database.GetConnectionString());
        }

        // One shared connection ⇒ one target database. Two different strings
        // would silently pin every context to the first one's database.
        for (var i = 1; i < _contexts.Length; i++)
        {
            if (!string.Equals(connectionStrings[i], connectionStrings[0], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The ambient session requires every joined DbContext to use the same connection string, " +
                    $"but {_contexts[i].GetType().Name} targets a different database than {_contexts[0].GetType().Name}.");
            }
        }

        var shared = new SqlConnection(connectionStrings[0]);
        AmbientDbSession? session = null;
        try
        {
            await shared.OpenAsync(cancellationToken);
            var transaction = await shared.BeginTransactionAsync(cancellationToken);
            session = new AmbientDbSession(_contexts, connectionStrings, shared, transaction);
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

    private static string RequireConnectionString(string? connectionString) =>
        !string.IsNullOrEmpty(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                "The ambient session requires a configured connection string on every joined DbContext.");
}
