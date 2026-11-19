namespace SupplyForge.PlatformCore.Application.Email;

/// <summary>
/// ADR-008 / TD-2: default Email:Mode = Fake. No real SMTP in A0.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed class FakeEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly List<(string To, string Subject, string Body)> _sent = new();
    private readonly object _gate = new();

    public FakeEmailSender(EmailOptions options) => _options = options;

    public IReadOnlyList<(string To, string Subject, string Body)> Sent
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToList();
            }
        }
    }

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Deterministic cancellation behavior (INT-004): the fake observes
        // the token like a real adapter would, so timeout and shutdown paths
        // are exercisable without a network.
        cancellationToken.ThrowIfCancellationRequested();

        // ADR-008: the fake must exercise the queue/retry/status paths —
        // the failure knob makes every attempt throw so the delivery state
        // machine records Pending → retry → Failed instead of succeeding.
        if (_options.SimulateFailures)
        {
            throw new InvalidOperationException(
                "Simulated delivery failure (Email:SimulateFailures).");
        }

        lock (_gate)
        {
            _sent.Add((to, subject, body));
        }

        return Task.CompletedTask;
    }
}
