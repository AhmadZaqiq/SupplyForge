using System.Net;
using Microsoft.Extensions.Configuration;

namespace SupplyForge.Host.RateLimiting;

/// <summary>
/// SEC-04 / PLT-012 (A0-2): configuration for the auth rate-limit policy.
/// Bound from the "RateLimit" section; override per value with environment
/// variables (RateLimit__AuthIpLimit, RateLimit__AuthWindow, ...).
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Fixed window length (ADR-004 example window: 5 minutes).</summary>
    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Attempts per window per remote IP. Higher than the account
    /// limit so one NAT'd office is not locked out by a single user.</summary>
    public int AuthIpLimit { get; set; } = 30;

    /// <summary>Attempts per window per account (ADR-004 example: 10).</summary>
    public int AuthAccountLimit { get; set; } = 10;

    /// <summary>Opt-in X-Forwarded-For handling. Off by default: the A0
    /// topology has no reverse proxy, so the socket peer is the real client.
    /// Enabling it without a trusted proxy in front would let callers spoof
    /// their own IP partition.</summary>
    public bool EnableForwardedHeaders { get; set; }

    /// <summary>Maximum forwarded hops taken from X-Forwarded-For. 1 means only
    /// the entry appended by the trusted proxy itself is honored.</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>Proxies allowed to set X-Forwarded-For. Only consulted when
    /// <see cref="EnableForwardedHeaders"/> is true.</summary>
    public string[] TrustedProxies { get; set; } = Array.Empty<string>();

    public static AuthRateLimitOptions Bind(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<AuthRateLimitOptions>()
            ?? new AuthRateLimitOptions();
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (AuthWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("RateLimit:AuthWindow must be greater than zero.");
        }

        if (AuthIpLimit < 1)
        {
            throw new InvalidOperationException("RateLimit:AuthIpLimit must be at least 1.");
        }

        if (AuthAccountLimit < 1)
        {
            throw new InvalidOperationException("RateLimit:AuthAccountLimit must be at least 1.");
        }

        if (ForwardLimit < 1)
        {
            throw new InvalidOperationException("RateLimit:ForwardLimit must be at least 1.");
        }

        foreach (var proxy in TrustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                throw new InvalidOperationException(
                    $"RateLimit:TrustedProxies contains '{proxy}', which is not a valid IP address.");
            }
        }
    }
}
