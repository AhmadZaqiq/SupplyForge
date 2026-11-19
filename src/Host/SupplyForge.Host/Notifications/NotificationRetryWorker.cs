using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.Host.Notifications;

/// <summary>
/// PLT-052 / INT-003: background retry of pending notification email. The
/// inline first attempt (NotificationRaiseService) schedules the next try on
/// failure; this worker sweeps rows whose <c>NextAttemptAt</c> is due and
/// re-attempts through the same delivery path until <c>Email:MaxAttempts</c>,
/// where the row stays Failed — dead-letter, visible in the in-app read API.
/// With <c>Email:Mode=Disabled</c> nothing is ever scheduled, so the loop
/// only pays a timer tick (PLT-051 visible pending).
/// </summary>
public sealed class NotificationRetryWorker : BackgroundService
{
    private const int SweepBatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<NotificationRetryWorker> _logger;

    public NotificationRetryWorker(
        IServiceScopeFactory scopeFactory,
        EmailOptions options,
        ILogger<NotificationRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RetryInterval, stoppingToken);
                if (!_options.AttemptsDelivery)
                {
                    continue;
                }

                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Host shutdown.
            }
            catch (Exception ex)
            {
                // A sweep failure must not kill the loop — a dead retry loop
                // would silently strand Pending rows (PLT-052).
                _logger.LogError(ex, "Notification delivery sweep failed.");
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
        var delivery = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();

        var due = await store.ListDueForDeliveryAsync(SweepBatchSize, cancellationToken);
        foreach (var item in due)
        {
            try
            {
                await delivery.TryDeliverAsync(
                    item.Id,
                    item.NotificationType,
                    item.Title,
                    item.Body,
                    item.DeliveryAttempts,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Host shutdown — handled by the outer catch.
            }
            catch (Exception ex)
            {
                // One bad row (e.g. deleted mid-sweep) must not stall the
                // rest of the batch.
                _logger.LogError(
                    ex, "Notification delivery attempt failed for {NotificationId}.", item.Id);
            }
        }
    }
}
