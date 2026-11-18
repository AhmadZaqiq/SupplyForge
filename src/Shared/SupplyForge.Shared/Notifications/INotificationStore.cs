namespace SupplyForge.Shared.Notifications;

/// <summary>
/// MOD-1a: Notification is a shared table owned by Platform Core infrastructure (ADR-008).
/// </summary>
public interface INotificationStore
{
    Task<Guid> RaiseAsync(
        string notificationType,
        string? userId,
        string? branchId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default);
}
