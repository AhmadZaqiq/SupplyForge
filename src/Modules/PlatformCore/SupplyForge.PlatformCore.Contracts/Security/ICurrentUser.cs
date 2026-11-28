namespace SupplyForge.PlatformCore.Contracts.Security;

/// <summary>
/// Represents the current authenticated user for the request or background operation.
/// Implemented by the Host/PlatformCore.Infrastructure; consumed by modules that need
/// to record who performed an action (e.g. StockMovement.CreatedByUserId).
/// <para>
/// Fail closed by default (Q-E): if no user is authenticated, <see cref="UserId"/>
/// is null and <see cref="IsAuthenticated"/> is false.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The authenticated user's id, or null when no user is authenticated
    /// (e.g. system scope for seed/migration/background jobs).
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// True when a user is authenticated for the current request.
    /// </summary>
    bool IsAuthenticated { get; }
}
