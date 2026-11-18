namespace SupplyForge.PlatformCore.Application.Security;

/// <summary>
/// PLT-020 permission catalogue codes referenced by Host <c>perm:*</c> policies
/// and by the platform catalogue seed (A0-3 Step 3). Single source of truth so
/// a policy name can never drift from the catalogue row it resolves against.
/// Codes are lowercase and stable: the unique Permission.Code index sits on a
/// case-insensitive collation, so a case variant would collide with (or
/// silently resolve to) the canonical row. Module codes join with their
/// module permission matrix (PRD §6.4.1), never before.
/// </summary>
public static class PermissionCodes
{
    public const string PlatformNotificationRead = "platform.notification.read";
    public const string PlatformNotificationRaise = "platform.notification.raise";
    public const string PlatformApiKeyManage = "platform.apikey.manage";
    public const string PlatformUserManage = "platform.user.manage";
}
