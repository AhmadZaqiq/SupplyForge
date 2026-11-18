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
    private readonly List<(string To, string Subject, string Body)> _sent = new();
    private readonly object _gate = new();

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
        lock (_gate)
        {
            _sent.Add((to, subject, body));
        }

        return Task.CompletedTask;
    }
}
