namespace SupplyForge.PlatformCore.Application.Email;

/// <summary>
/// ADR-008 config switch for the email side-channel: <c>Email:Mode = Fake |
/// Smtp | Disabled</c> (default Fake in dev; Disabled means pending-only),
/// plus the PLT-052 / INT-003 retry knobs. ADR-008 states the exact keys
/// and max-attempt counts are recommendations. This layer has no
/// configuration reference, so the Host binds the section and calls
/// <see cref="Validate"/> at startup — an unsupported mode fails closed
/// before any traffic is served.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>See <see cref="EmailMode"/>; Fake | Disabled in A0.</summary>
    public string Mode { get; set; } = EmailMode.Fake;

    /// <summary>Fake-adapter failure injection: every SendAsync throws
    /// (ADR-008: the fake must exercise the queue/retry/status paths).
    /// Tests only — never enable against real traffic.</summary>
    public bool SimulateFailures { get; set; }

    /// <summary>Total attempts including the inline first one; at the limit
    /// the row stays Failed — dead-letter, visible in the read API
    /// (INT-003, PLT-052).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>First retry delay; doubles per attempt (INT-003 backoff).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Backoff ceiling, applied before jitter.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Delivery-worker sweep interval.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>INT-004: upper bound for one adapter call. The attempt is
    /// cancelled at this limit and the timeout is recorded as a normal
    /// failure, so a hung send can never stall the retry worker.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>ADR-008: Disabled = pending-only — the adapter is never
    /// attempted, so the row stays visible pending (PLT-051).</summary>
    public bool AttemptsDelivery =>
        !string.Equals(Mode, EmailMode.Disabled, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        var isFake = string.Equals(Mode, EmailMode.Fake, StringComparison.OrdinalIgnoreCase);
        var isDisabled = string.Equals(Mode, EmailMode.Disabled, StringComparison.OrdinalIgnoreCase);
        if (!isFake && !isDisabled)
        {
            var hint = string.Equals(Mode, "Smtp", StringComparison.OrdinalIgnoreCase)
                ? " The SMTP adapter is an ADR-008 follow-up and does not exist yet."
                : string.Empty;
            throw new InvalidOperationException(
                $"Email:Mode '{Mode}' is not supported; use '{EmailMode.Fake}' or " +
                $"'{EmailMode.Disabled}'.{hint}");
        }

        if (MaxAttempts < 1)
        {
            throw new InvalidOperationException("Email:MaxAttempts must be at least 1.");
        }

        if (RetryBaseDelay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Email:RetryBaseDelay must be greater than zero.");
        }

        if (RetryMaxDelay < RetryBaseDelay)
        {
            throw new InvalidOperationException(
                "Email:RetryMaxDelay must be at least Email:RetryBaseDelay.");
        }

        if (RetryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Email:RetryInterval must be greater than zero.");
        }

        if (SendTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Email:SendTimeout must be greater than zero.");
        }
    }
}

/// <summary>Supported <see cref="EmailOptions.Mode"/> values (ADR-008).
/// "Smtp" is ADR-008's future value; rejected by Validate until the real
/// adapter exists rather than silently degrading to pending-only.</summary>
public static class EmailMode
{
    public const string Fake = "Fake";
    public const string Disabled = "Disabled";
}
