namespace SupplyForge.PlatformCore.Application.Security;

/// <summary>
/// Answers whether an interactive user currently holds a permission code from
/// the PLT-020 catalogue (role bundles, multi-role union). Evaluated
/// server-side on every authorized request (SEC-10); the permission list never
/// travels in the access token (ADR-004). Deny by default: unknown code, no
/// roles, or unknown user resolves to false (PLT-022).
/// </summary>
public interface IPermissionResolver
{
    Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default);
}
