namespace SupplyForge.PlatformCore.Application.Security;

/// <summary>
/// Read-only user-account presence behind the create-user bootstrap window
/// (PLT-010, PLT-022, A0-3 Step 4): the first account may be created only
/// while no user exists; afterwards POST /users requires an authenticated
/// caller holding <see cref="PermissionCodes.PlatformUserManage"/>. Lives in
/// Application so the Host authorization handler never touches the DbContext.
/// </summary>
public interface IUserAccountState
{
    /// <summary>True when at least one user account exists (bootstrap window closed).</summary>
    Task<bool> HasAnyUserAsync(CancellationToken cancellationToken = default);
}
