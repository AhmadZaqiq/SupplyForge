using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SupplyForge.Host.Auth;

/// <summary>
/// Single source of truth for JWT parameters decided in ADR-004:
/// HS256 only, 15-minute access-token lifetime, 2-minute clock skew.
/// Used by both token issuance and the JwtBearer validation pipeline.
/// </summary>
public static class JwtAuthDefaults
{
    public const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public static TokenValidationParameters CreateValidationParameters(
        string secret,
        string issuer,
        string audience)
    {
        return new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidAlgorithms = new[] { SigningAlgorithm },
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = ClockSkew,
        };
    }
}
