using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;

namespace SupplyForge.PlatformCore.Infrastructure.Data;

/// <summary>
/// A0-3 Step 3 (PLT-020, PLT-ROLE-1): idempotent runtime seed of the permission
/// catalogue and the eleven system role bundles from PRD 02b §2.6.1.
///
/// Runs at Host startup instead of EF <c>HasData</c> so no migration is needed
/// (migrations require owner approval). Every row is get-or-insert against the
/// unique <c>Code</c> indexes with first-writer-wins: a concurrent startup that
/// loses the insert race re-queries the winner's committed row instead of
/// failing (same pattern as the TestPermissionGrant test helper).
///
/// Bundle mappings are seeded only where the PRD states them: Company
/// Administrator manages users (02b §2.6.1). Module permission codes and their
/// bundle mappings arrive with each module's matrix (PRD §6.4.1) and are
/// deliberately not pre-seeded.
/// </summary>
public sealed class PlatformCatalogueSeed
{
    private const string ModuleName = "PlatformCore";

    private static readonly (string Code, string Name)[] CataloguePermissions =
    [
        (PermissionCodes.PlatformNotificationRead, "Read notifications"),
        (PermissionCodes.PlatformNotificationRaise, "Raise notifications"),
        (PermissionCodes.PlatformApiKeyManage, "Manage API keys"),
        (PermissionCodes.PlatformUserManage, "Manage user accounts"),
    ];

    private static readonly (string Code, string Name)[] SystemRoles =
    [
        (SystemRoleCodes.CompanyExecutive, "Company Executive"),
        (SystemRoleCodes.BranchManager, "Branch Manager"),
        (SystemRoleCodes.ProcurementManager, "Procurement Manager"),
        (SystemRoleCodes.ProcurementOfficer, "Procurement Officer"),
        (SystemRoleCodes.WarehouseManager, "Warehouse Manager"),
        (SystemRoleCodes.WarehouseOperator, "Warehouse Operator"),
        (SystemRoleCodes.QualityInspector, "Quality Inspector"),
        (SystemRoleCodes.SalesRepresentative, "Sales Representative"),
        (SystemRoleCodes.Accountant, "Accountant"),
        (SystemRoleCodes.CompanyAdministrator, "Company Administrator"),
        (SystemRoleCodes.IntegrationClient, "Integration Client"),
    ];

    private readonly PlatformCoreDbContext _db;
    private readonly TimeProvider _clock;

    public PlatformCatalogueSeed(PlatformCoreDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Creates any missing catalogue/system-role rows. Existing rows are left
    /// alone except for completing the seed's own permission rows when their
    /// module metadata is absent (see <see cref="EnsurePermissionAsync"/>).
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        foreach (var permission in CataloguePermissions)
        {
            await EnsurePermissionAsync(permission.Code, permission.Name, cancellationToken);
        }

        foreach (var role in SystemRoles)
        {
            await EnsureRoleAsync(role.Code, role.Name, cancellationToken);
        }

        // Only PRD-backed bundle mapping (02b §2.6.1: Company Administrator
        // owns users/roles/settings). Other mappings arrive with module
        // matrices (PRD §6.4.1).
        await EnsureRolePermissionAsync(
            SystemRoleCodes.CompanyAdministrator,
            PermissionCodes.PlatformUserManage,
            cancellationToken);
    }

    private async Task EnsurePermissionAsync(string code, string name, CancellationToken cancellationToken)
    {
        if (!await _db.Permissions.AnyAsync(p => p.Code == code, cancellationToken))
        {
            _db.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Module = ModuleName,
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Unique Permission.Code race with a parallel Host start: swallow
                // only when the winner's row is really there; any other failure is
                // a genuine seed error and must fail startup.
                _db.ChangeTracker.Clear();
                if (!await _db.Permissions.AnyAsync(p => p.Code == code, cancellationToken))
                {
                    throw new InvalidOperationException($"Permission seed failed for '{code}'.", ex);
                }
            }
        }

        // These codes are the seed's own catalogue rows, so complete any row
        // that predates this seed or was created without module metadata.
        // Set-based and last-writer-wins on the same value: no concurrency
        // failure is possible, and pending tracked entities are unaffected.
        await _db.Permissions
            .Where(p => p.Code == code && p.Module == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, ModuleName), cancellationToken);
    }

    private async Task EnsureRoleAsync(string code, string name, CancellationToken cancellationToken)
    {
        if (await _db.Roles.AnyAsync(r => r.Code == code, cancellationToken))
        {
            return;
        }

        _db.Roles.Add(new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsSystem = true,
            CreatedAt = _clock.GetUtcNow(),
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique Role.Code race with a parallel Host start (see above).
            _db.ChangeTracker.Clear();
            if (!await _db.Roles.AnyAsync(r => r.Code == code, cancellationToken))
            {
                throw new InvalidOperationException($"Role seed failed for '{code}'.", ex);
            }
        }
    }

    private async Task EnsureRolePermissionAsync(
        string roleCode,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var role = await _db.Roles.FirstAsync(r => r.Code == roleCode, cancellationToken);
        var permission = await _db.Permissions.FirstAsync(p => p.Code == permissionCode, cancellationToken);

        var linked = await _db.RolePermissions
            .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id, cancellationToken);
        if (linked)
        {
            return;
        }

        _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Composite PK race with a parallel Host start (see above).
            _db.ChangeTracker.Clear();
            if (!await _db.RolePermissions
                    .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Role bundle seed failed for '{roleCode}' -> '{permissionCode}'.", ex);
            }
        }
    }
}

/// <summary>
/// Stable codes of the eleven system roles from PRD 02b §2.6.1 (PLT-ROLE-1:
/// roles = bundles). Seeded by <see cref="PlatformCatalogueSeed"/>; referenced
/// by tests to pin the seeded contract.
/// </summary>
public static class SystemRoleCodes
{
    public const string CompanyExecutive = "company_executive";
    public const string BranchManager = "branch_manager";
    public const string ProcurementManager = "procurement_manager";
    public const string ProcurementOfficer = "procurement_officer";
    public const string WarehouseManager = "warehouse_manager";
    public const string WarehouseOperator = "warehouse_operator";
    public const string QualityInspector = "quality_inspector";
    public const string SalesRepresentative = "sales_representative";
    public const string Accountant = "accountant";
    public const string CompanyAdministrator = "company_administrator";
    public const string IntegrationClient = "integration_client";
}
