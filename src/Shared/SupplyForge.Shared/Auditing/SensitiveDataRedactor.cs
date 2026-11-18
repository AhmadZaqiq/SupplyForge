using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SupplyForge.Shared.Auditing;

/// <summary>
/// PLT-044: sensitive-field redaction for audit payloads and log text. The
/// redaction list is configured in code (as the PRD requires) and pinned by
/// tests: passwords, token hashes, webhook/API-key secrets, full payment
/// instrument numbers (last 4 kept), and national ids. Matching is
/// case-insensitive substring on the field name — over-redaction is the safe
/// direction. Applied at the audit writer choke point (every module writes
/// through it) and available as <see cref="RedactText"/> for log call sites.
/// </summary>
public static class SensitiveDataRedactor
{
    public const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Configured markers (PLT-044 list): a field name containing any marker
    /// is redacted. Substrings cover the PRD's categories — "password" also
    /// catches passwordHash, "token" catches tokenHash/accessToken/
    /// refreshToken, "secret" catches apiKeySecret/webhookSecret.
    /// </summary>
    public static IReadOnlyList<string> RedactionMarkers { get; } =
    [
        "password",
        "token",
        "secret",
        "cardnumber",
        "paymentinstrument",
        "nationalid",
    ];

    /// <summary>Payment categories keep their last 4 characters (PLT-044).</summary>
    private static readonly string[] KeepLast4Markers =
    [
        "cardnumber",
        "paymentinstrument",
    ];

    // No word boundary before the marker: fields like "apiKeySecret" or
    // "userPassword" must match mid-word (same substring semantics as the
    // JSON path). The marker is always followed by [A-Za-z0-9_]* then the
    // separator, so random prose cannot match without a [=:].
    private static readonly Regex KeyValuePattern = new(
        @"(?<marker>" + string.Join('|', RedactionMarkers) +
        @")[A-Za-z0-9_]*\s*[=:]\s*(?:""(?<quoted>[^""]*)""|(?<bare>[^\s,;&]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Redacts <paramref name="detailsJson"/>: structured redaction for JSON
    /// object/array payloads (recursing into nested shapes), pattern
    /// redaction for non-JSON <c>key=value</c> detail strings. Null passes
    /// through; fields without a marker are untouched.
    /// </summary>
    public static string? RedactDetails(string? detailsJson)
    {
        if (detailsJson is null)
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(detailsJson);
        }
        catch (JsonException)
        {
            // Not JSON (free-text detail strings) → text redaction below.
            return RedactText(detailsJson);
        }

        if (root is JsonObject or JsonArray)
        {
            return RedactNode(root)!.ToJsonString();
        }

        // JSON scalar: nothing structured to match, but the raw text may
        // still carry key=value secrets — fall through to text redaction.
        return RedactText(detailsJson);
    }

    /// <summary>Redacts configured <c>key=value</c> / <c>key: value</c> pairs in free text (log messages, detail strings).</summary>
    public static string RedactText(string text) =>
        KeyValuePattern.Replace(text, static match =>
        {
            var marker = match.Groups["marker"].Value;
            var valueGroup = match.Groups["quoted"].Success
                ? match.Groups["quoted"]
                : match.Groups["bare"];
            var value = valueGroup.Value;

            var redacted = IsKeepLast4(marker) ? KeepLast4(value) : RedactedValue;

            // Replace only the captured value; key, separators, and any
            // trailing text stay exactly as written.
            var valueOffset = valueGroup.Index - match.Index;
            return match.Value[..valueOffset] +
                   redacted +
                   match.Value[(valueOffset + valueGroup.Length)..];
        });

    private static JsonNode? RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    var value = obj[key];
                    if (IsSensitiveField(key))
                    {
                        obj[key] = JsonValue.Create(MakeRedacted(key, value));
                    }
                    else
                    {
                        // Mutate nested containers in place — assigning an
                        // already-parented node back to its own parent throws.
                        RedactNode(value);
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    RedactNode(array[i]);
                }

                return array;
            default:
                return node;
        }
    }

    private static bool IsSensitiveField(string fieldName) =>
        RedactionMarkers.Any(marker => fieldName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsKeepLast4(string marker) =>
        KeepLast4Markers.Contains(marker, StringComparer.OrdinalIgnoreCase);

    private static string MakeRedacted(string fieldName, JsonNode? value)
    {
        if (!IsKeepLast4(fieldName))
        {
            return RedactedValue;
        }

        var original = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)
            ? text
            : value?.ToJsonString() ?? string.Empty;
        return KeepLast4(original);
    }

    /// <summary>Masks everything but the last 4 characters; fully redacts shorter values.</summary>
    private static string KeepLast4(string value) =>
        value.Length >= 4 ? "****" + value[^4..] : RedactedValue;
}
