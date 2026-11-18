using System.Text.Json;
using SupplyForge.Shared.Auditing;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// PLT-044: the configured sensitive-field redaction list and its behavior —
/// passwords, token hashes, webhook/API-key secrets, full payment instrument
/// numbers (last 4 kept), and national ids, in JSON audit payloads and in
/// free-text log/detail strings.
/// </summary>
public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("passwordHash")]
    [InlineData("accessToken")]
    [InlineData("refreshToken")]
    [InlineData("tokenHash")]
    [InlineData("webhookSecret")]
    [InlineData("apiKeySecret")]
    [InlineData("secret")]
    [InlineData("nationalId")]
    public void PLT_044_ConfiguredSensitiveField_IsRedactedInJsonPayload(string field)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [field] = "super-secret-value",
            ["title"] = "kept-visible",
        });

        var redacted = SensitiveDataRedactor.RedactDetails(json);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("super-secret-value", redacted, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataRedactor.RedactedValue, redacted, StringComparison.Ordinal);
        Assert.Contains("kept-visible", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void PLT_044_PaymentInstrumentNumber_KeepsLast4Only()
    {
        var json = """{"cardNumber":"4111111111111234","note":"kept"}""";

        var redacted = SensitiveDataRedactor.RedactDetails(json);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("4111111111111234", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("411111111111", redacted, StringComparison.Ordinal);
        Assert.Contains("1234", redacted, StringComparison.Ordinal);
        Assert.Contains("kept", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void PLT_044_NestedObjectsAndArrays_AreRedactedRecursively()
    {
        var json = """
            {"before":{"password":"hidden-pw","count":3},"items":[{"apiKeySecret":"hidden-key"},{"nationalId":"1234567890"}]}
            """;

        var redacted = SensitiveDataRedactor.RedactDetails(json);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("hidden-pw", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-key", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("1234567890", redacted, StringComparison.Ordinal);
        Assert.Contains("3", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void PLT_044_NonJsonObjectKeyValueDetailString_IsRedacted()
    {
        // Matches the free-text detail shape some callers already use
        // (e.g. "name={name}") but with a secret present.
        var redacted = SensitiveDataRedactor.RedactDetails("password=Passw0rd!x; user=bob");

        Assert.NotNull(redacted);
        Assert.DoesNotContain("Passw0rd!x", redacted, StringComparison.Ordinal);
        Assert.Contains("user=bob", redacted, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataRedactor.RedactedValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void PLT_044_UnknownFields_PassThroughUnchanged()
    {
        var json = """{"title":"Quarterly report","status":"draft"}""";

        var redacted = SensitiveDataRedactor.RedactDetails(json);

        Assert.Equal(json, redacted);
    }

    [Fact]
    public void PLT_044_NullDetails_PassThrough()
    {
        Assert.Null(SensitiveDataRedactor.RedactDetails(null));
    }

    [Fact]
    public void PLT_044_RedactionList_CoversEveryPrdCategory()
    {
        // Pins the configured list against the PRD wording (PLT-044):
        // passwords, token hashes, webhook secrets, API key secrets, full
        // payment instrument numbers (last4), national ids.
        string[] prdCategories = ["password", "token", "secret", "cardnumber", "paymentinstrument", "nationalid"];

        foreach (var category in prdCategories)
        {
            Assert.Contains(
                SensitiveDataRedactor.RedactionMarkers,
                marker => marker.Contains(category, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void PLT_044_LogTextStyle_RedactsConfiguredPairs_KeepsRest()
    {
        // Log-call-site usage (RedactText): message text around the pair is
        // preserved verbatim.
        var logLine = "login failed for apiKeySecret=sfk_live_abc123 user=carol";

        var redacted = SensitiveDataRedactor.RedactText(logLine);

        Assert.DoesNotContain("sfk_live_abc123", redacted, StringComparison.Ordinal);
        Assert.Contains("login failed for", redacted, StringComparison.Ordinal);
        Assert.Contains("user=carol", redacted, StringComparison.Ordinal);
    }
}
