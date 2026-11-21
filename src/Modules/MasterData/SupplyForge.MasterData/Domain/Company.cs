namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Company singleton profile (MD-001). Exactly one active row exists; the row
/// is created only by the first-run seed, never through a tenant API.
/// <para>
/// <c>Id</c> is a fixed sentinel so the singleton is structural: a second row
/// cannot be inserted at all (MD-001 "exactly one active company row").
/// </para>
/// </summary>
public class Company
{
    /// <summary>Sentinel key — the singleton is the one row with this id.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    public string LegalName { get; set; } = string.Empty;

    public string? TaxId { get; set; }

    public string? Address { get; set; }

    /// <summary>Raw logo bytes (MD-001). Upload/retrieval surface is SEC-40.</summary>
    public byte[]? Logo { get; set; }

    public string? LogoContentType { get; set; }

    /// <summary>ISO 4217 code of the base currency (A-1 / Q-1 default: USD).</summary>
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>IANA or Windows time-zone id (A-1 demo default: UTC).</summary>
    public string Timezone { get; set; } = "UTC";

    /// <summary>Month the fiscal year starts in, 1…12.</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>BCP-47 default language tag (A-1 demo default: en-US).</summary>
    public string DefaultLanguage { get; set; } = "en-US";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
