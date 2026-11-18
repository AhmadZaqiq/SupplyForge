using System.Security.Claims;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Single extraction of the authenticated user id from a principal. Shared by
/// the perm:*/create-user handlers and the create-user endpoint so the
/// subject the policy checks is always the same subject handed to the
/// service-layer revalidation (A0-3 Step 4) — drift between them would be a
/// fail-open gap.
/// </summary>
internal static class PrincipalSubject
{
    /// <summary>
    /// JwtBearer maps "sub" → <see cref="ClaimTypes.NameIdentifier"/>
    /// (MapInboundClaims default); raw "sub" is the fallback if inbound
    /// mapping is off.
    /// </summary>
    internal static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        if (claim is null)
        {
            userId = Guid.Empty;
            return false;
        }

        return Guid.TryParse(claim.Value, out userId);
    }
}
