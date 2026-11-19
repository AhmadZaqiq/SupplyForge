using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// ADR-008 delivery knobs without a database: PLT-051 mode validation,
/// PLT-052 fake-adapter failure injection, and the INT-003 backoff curve
/// (exponential + jitter + cap) that the raise flow and the retry worker
/// both go through. ADR-008: exact config keys and max-attempt counts are
/// recommendations.
/// </summary>
public class EmailDeliveryStateTests
{
    [Fact]
    public void PLT_051_EmailOptions_DisabledNeverAttempts_FakeDoes_CaseInsensitive()
    {
        var disabled = new EmailOptions { Mode = "disabled" };
        Assert.False(disabled.AttemptsDelivery);
        disabled.Validate();

        var fake = new EmailOptions { Mode = "FAKE" };
        Assert.True(fake.AttemptsDelivery);
        fake.Validate();

        Assert.True(new EmailOptions().AttemptsDelivery); // default = Fake
    }

    [Fact]
    public void PLT_051_EmailOptions_SmtpMode_RejectedWithAdapterHint()
    {
        // The ADR-008 Smtp value names a future adapter: fail closed at
        // startup with a clear hint instead of silently degrading to
        // pending-only or pretending a send happened.
        var options = new EmailOptions { Mode = "Smtp" };
        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Email:Mode", ex.Message);
        Assert.Contains("SMTP adapter", ex.Message);
    }

    [Fact]
    public void PLT_052_EmailOptions_Validate_RejectsNonPositiveRetryKnobs()
    {
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { MaxAttempts = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { RetryBaseDelay = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { RetryBaseDelay = TimeSpan.FromMinutes(2), RetryMaxDelay = TimeSpan.FromMinutes(1) }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { RetryInterval = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { SendTimeout = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(
            () => new EmailOptions { SendTimeout = TimeSpan.FromSeconds(-1) }.Validate());
    }

    [Fact]
    public async Task INT_004_FakeEmailSender_CancelledToken_ThrowsWithoutRecording()
    {
        // Deterministic cancellation behavior: the fake observes the token
        // like a real adapter, which is what makes the bounded-attempt and
        // shutdown paths testable without a network.
        var sender = new FakeEmailSender(new EmailOptions());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync("dev@supplyforge.local", "subject", "body", cancelled.Token));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task PLT_052_FakeEmailSender_SimulateFailures_Throws_WithoutRecording()
    {
        var sender = new FakeEmailSender(new EmailOptions { SimulateFailures = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("dev@supplyforge.local", "subject", "body"));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task PLT_050_FakeEmailSender_WithoutFailures_RecordsSentMessage()
    {
        var sender = new FakeEmailSender(new EmailOptions());

        await sender.SendAsync("dev@supplyforge.local", "subject", "body");
        Assert.Single(sender.Sent);
    }

    [Fact]
    public void INT_003_ComputeBackoff_DoublesPerAttempt_WithinJitterBand()
    {
        var baseDelay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromMinutes(5);

        for (var attempts = 1; attempts <= 4; attempts++)
        {
            var expected = baseDelay.Ticks * Math.Pow(2, attempts - 1);
            for (var i = 0; i < 25; i++)
            {
                var delay = NotificationDeliveryService.ComputeBackoff(
                    attempts, baseDelay, maxDelay);
                Assert.InRange(delay.Ticks, (long)expected, (long)(expected * 1.2));
            }
        }
    }

    [Fact]
    public void INT_003_ComputeBackoff_CapsAtMaxDelay_ThenJittersAboveIt()
    {
        var maxDelay = TimeSpan.FromMinutes(1);
        for (var i = 0; i < 50; i++)
        {
            var delay = NotificationDeliveryService.ComputeBackoff(
                attempts: 50, baseDelay: TimeSpan.FromSeconds(10), maxDelay: maxDelay);
            Assert.InRange(delay.Ticks, maxDelay.Ticks, (long)(maxDelay.Ticks * 1.2));
        }
    }

    [Fact]
    public void INT_003_ComputeBackoff_ZeroAttempts_TreatedAsFirstAttempt()
    {
        var baseDelay = TimeSpan.FromSeconds(3);
        var delay = NotificationDeliveryService.ComputeBackoff(
            0, baseDelay, TimeSpan.FromMinutes(1));
        Assert.InRange(delay.Ticks, baseDelay.Ticks, (long)(baseDelay.Ticks * 1.2));
    }
}
