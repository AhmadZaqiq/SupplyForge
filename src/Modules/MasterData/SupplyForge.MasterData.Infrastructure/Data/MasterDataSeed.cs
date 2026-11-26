using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Domain;

namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// A1-1 (MD-001, MD-002): idempotent first-run seed of the company singleton
/// and the three v1 branches. Same get-or-insert shape as
/// <c>PlatformCatalogueSeed</c>: every row is checked against its unique key
/// first, and a lost insert race re-queries the winner's committed row instead
/// of failing startup.
///
/// <para>
/// MD-001 allows creation "only at first-run migration/seed (not a tenant
/// API)" — this is that seed. It runs at Host startup next to the platform
/// catalogue seed; when the <c>master_data</c> migration has not been applied
/// to the target database yet, it reports <c>false</c> and the Host starts
/// normally, because no A1 table exists there to seed into.
/// </para>
/// </summary>
public sealed class MasterDataSeed
{
    /// <summary>
    /// The three v1 branches (A-2: seed three, schema supports N). Codes and
    /// names are placeholders — Q-3 (branch codes/names) is Open-PO and
    /// explicitly accepts placeholders until the product owner answers.
    /// </summary>
    public static readonly (string Code, string Name)[] V1Branches =
    [
        ("BR-01", "Branch 01"),
        ("BR-02", "Branch 02"),
        ("BR-03", "Branch 03"),
    ];

    private readonly MasterDataDbContext _db;
    private readonly TimeProvider _clock;

    public MasterDataSeed(MasterDataDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
        // Q-E option a: seed runs under the explicit system scope, set by the
        // Host (the only composition root); design-time/tests set their own.
    }

    /// <summary>
    /// Creates any missing seed row. Returns <c>false</c> only when the
    /// <c>master_data</c> schema is absent from this database (the migration
    /// has not been applied), which is not an error: nothing was attempted and
    /// nothing was written. Any other failure fails the caller, because a
    /// half-seeded company/branch set would break every later slice.
    /// </summary>
    public async Task<bool> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (!await SchemaExistsAsync(cancellationToken))
        {
            return false;
        }

        await EnsureCompanyAsync(cancellationToken);

        foreach (var (code, name) in V1Branches)
        {
            await EnsureBranchAsync(code, name, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Probes for the seeded table before writing anything, so a database that
    /// predates A1 reports "not there" instead of throwing error 208 on every
    /// Host start. <c>Schema</c> and <c>Branch</c> are compile-time constants of
    /// this module — no external value reaches the SQL.
    /// </summary>
    private async Task<bool> SchemaExistsAsync(CancellationToken cancellationToken)
    {
        var tableCount = await _db.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) AS Value FROM sys.tables WHERE schema_id = SCHEMA_ID('{MasterDataDbContext.Schema}') AND name = '{nameof(Branch)}'")
            .SingleAsync(cancellationToken);

        return tableCount > 0;
    }

    private async Task EnsureCompanyAsync(CancellationToken cancellationToken)
    {
        if (await _db.Companies.AnyAsync(c => c.Id == Company.SingletonId, cancellationToken))
        {
            return;
        }

        // Placeholder legal name: MD-001 states the field but the PRD gives no
        // default value for it (Q-3 covers branch names only). Replaced by the
        // owner's real legal name without any schema change.
        _db.Companies.Add(new Company
        {
            Id = Company.SingletonId,
            LegalName = "SupplyForge Demo Company",
            CreatedAt = _clock.GetUtcNow(),
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique-company race with a parallel Host start: swallow only
            // when the singleton row really is there (MD-001: exactly one).
            _db.ChangeTracker.Clear();
            if (!await _db.Companies.AnyAsync(c => c.Id == Company.SingletonId, cancellationToken))
            {
                throw new InvalidOperationException("Company singleton seed failed.", ex);
            }
        }
    }

    private async Task EnsureBranchAsync(string code, string name, CancellationToken cancellationToken)
    {
        if (await _db.Branches.AnyAsync(b => b.Code == code, cancellationToken))
        {
            return;
        }

        _db.Branches.Add(new Branch
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            CreatedAt = _clock.GetUtcNow(),
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique Branch.Code race with a parallel Host start (see above).
            _db.ChangeTracker.Clear();
            if (!await _db.Branches.AnyAsync(b => b.Code == code, cancellationToken))
            {
                throw new InvalidOperationException($"Branch seed failed for '{code}'.", ex);
            }
        }
    }
}
