using SupplyForge.Shared.Data;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Notifications;

/// <summary>
/// PLT-050 raise use case. The in-app store write runs inside the ADR-003
/// ambient session and commits <b>before</b> the email side-channel fires —
/// MOD-TXN-02 (never call external network inside a DB transaction) and the
/// ADR-003 "no external call in TXN" test row made structural: the fake
/// adapter can never observe an open transaction. The first delivery attempt
/// is inline after the commit (ADR-008 round-trip); its outcome is recorded
/// on the row by <see cref="NotificationDeliveryService"/> — an adapter
/// failure leaves the row pending for the retry worker instead of failing
/// the request (PLT-052).
/// </summary>
public sealed class NotificationRaiseService
{
    private readonly IDbSessionFactory _sessionFactory;
    private readonly INotificationStore _store;
    private readonly NotificationDeliveryService _delivery;

    public NotificationRaiseService(
        IDbSessionFactory sessionFactory,
        INotificationStore store,
        NotificationDeliveryService delivery)
    {
        _sessionFactory = sessionFactory;
        _store = store;
        _delivery = delivery;
    }

    public async Task<Guid> RaiseAsync(
        string notificationType,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken = default)
    {
        Guid id;
        await using (var session = await _sessionFactory.BeginAsync(cancellationToken))
        {
            id = await _store.RaiseAsync(
                notificationType,
                userId: null,
                branchId: null,
                title,
                body,
                link,
                cancellationToken);
            await session.CommitAsync(cancellationToken);
        }

        // MOD-TXN-02: outside the session — the commit above has completed
        // (IsCompleted = true) before any external call is made.
        await _delivery.TryDeliverAsync(
            id,
            notificationType,
            title,
            body,
            attemptsSoFar: 0,
            cancellationToken);

        return id;
    }
}
