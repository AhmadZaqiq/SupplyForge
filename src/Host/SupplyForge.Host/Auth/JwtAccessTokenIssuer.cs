using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.Host.Auth;

/// <summary>
/// Issues ADR-004 access tokens: JWT, HS256, 15-minute TTL, claims
/// sub / jti / email / iss / aud — no permission catalogue in the token.
/// Validation happens exclusively in the standard JwtBearer handler.
/// </summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _secret;
    private readonly TimeProvider _clock;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtAccessTokenIssuer(string issuer, string audience, string secret, TimeProvider clock)
    {
        _issuer = issuer;
        _audience = audience;
        _secret = secret;
        _clock = clock;
    }

    public AccessTokenIssueResult Issue(Guid userId, string email)
    {
        var now = _clock.GetUtcNow();
        var expires = now.Add(JwtAuthDefaults.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, email),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
                JwtAuthDefaults.SigningAlgorithm));

        return new AccessTokenIssueResult(_handler.WriteToken(token), expires);
    }
}
