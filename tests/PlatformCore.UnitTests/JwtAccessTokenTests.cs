using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using SupplyForge.Host.Auth;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A0-1: JWT access tokens per ADR-004 (PLT-011, SEC-03-adjacent token rules).
/// Validation is done with the standard JsonWebTokenHandler using the exact
/// TokenValidationParameters the JwtBearer pipeline uses — no custom parsing.
/// </summary>
public class JwtAccessTokenTests
{
    private const string Secret = "unit-test-signing-secret-0123456789abcdef!";
    private const string Issuer = "supplyforge";
    private const string Audience = "supplyforge-api";

    private static JwtAccessTokenIssuer CreateIssuer(string? issuer = null, string? audience = null, string? secret = null) =>
        new(issuer ?? Issuer, audience ?? Audience, secret ?? Secret, TimeProvider.System);

    private static TokenValidationParameters Params(string? issuer = null, string? audience = null, string? secret = null) =>
        JwtAuthDefaults.CreateValidationParameters(secret ?? Secret, issuer ?? Issuer, audience ?? Audience);

    private static async Task<TokenValidationResult> ValidateAsync(string token, TokenValidationParameters? parameters = null) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters ?? Params());

    private static string Mint(
        string issuer,
        string audience,
        string secret,
        DateTimeOffset notBefore,
        DateTimeOffset expires)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer,
            audience,
            new[]
            {
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            },
            notBefore.UtcDateTime,
            expires.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(token);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s) =>
        Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/')
            .PadRight(s.Length + (4 - s.Length % 4) % 4, '='));

    [Fact]
    public async Task PLT_011_Jwt_Issue_ProducesHs256Token_WithExpectedClaims()
    {
        var userId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;
        var issued = CreateIssuer().Issue(userId, "user@test.local");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(3, issued.Token.Split('.').Length);

        var result = await ValidateAsync(issued.Token);
        Assert.True(result.IsValid, result.Exception?.ToString());

        var jwt = Assert.IsType<JsonWebToken>(result.SecurityToken);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.False(string.IsNullOrEmpty(jwt.Id)); // jti
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(Audience, jwt.Audiences.Single());
        Assert.Equal("user@test.local", jwt.GetClaim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email).Value);

        // TTL = 15 minutes (ADR-004), issued "now".
        Assert.InRange(jwt.ValidFrom, before.AddSeconds(-5), after.AddSeconds(5));
        Assert.InRange(jwt.ValidTo, before.AddMinutes(15).AddSeconds(-5), after.AddMinutes(15).AddSeconds(5));
        Assert.InRange(issued.ExpiresAt, before.AddMinutes(15).AddSeconds(-5), after.AddMinutes(15).AddSeconds(5));
    }

    [Fact]
    public async Task PLT_011_Jwt_TamperedToken_Rejected()
    {
        var token = CreateIssuer().Issue(Guid.NewGuid(), "user@test.local").Token;
        var parts = token.Split('.');

        // Keep the payload valid JSON but different bytes → signature mismatch.
        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1])) + " ";
        var tampered = parts[0] + "." + Base64Url(Encoding.UTF8.GetBytes(json)) + "." + parts[2];

        var result = await ValidateAsync(tampered);
        Assert.False(result.IsValid);
        // 8.x surfaces signature failures as invalid-signature or key-lookup
        // exceptions depending on the kid path; both reject the token.
        Assert.IsAssignableFrom<SecurityTokenException>(result.Exception);
    }

    [Fact]
    public async Task PLT_011_Jwt_ExpiredBeyondSkew_Rejected()
    {
        // Expired 5 minutes ago — outside the 2-minute skew.
        var token = Mint(Issuer, Audience, Secret,
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-10),
            expires: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await ValidateAsync(token);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task PLT_011_Jwt_ExpiredWithinTwoMinuteSkew_Allowed()
    {
        // Expired 1 minute ago — inside the explicit 2-minute skew (ADR-004).
        var token = Mint(Issuer, Audience, Secret,
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-10),
            expires: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await ValidateAsync(token);
        Assert.True(result.IsValid, result.Exception?.ToString());
    }

    [Fact]
    public async Task PLT_011_Jwt_WrongIssuer_Rejected()
    {
        var token = Mint("someone-else", Audience, Secret,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14));

        var result = await ValidateAsync(token);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidIssuerException>(result.Exception);
    }

    [Fact]
    public async Task PLT_011_Jwt_WrongAudience_Rejected()
    {
        var token = Mint(Issuer, "wrong-audience", Secret,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14));

        var result = await ValidateAsync(token);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    [Fact]
    public async Task PLT_011_Jwt_WrongSigningKey_Rejected()
    {
        var token = Mint(Issuer, Audience, "another-secret-0123456789-abcdefghijklmnop!",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14));

        var result = await ValidateAsync(token);
        Assert.False(result.IsValid);
        // Key mismatch surfaces as key-not-found (kid) or invalid signature depending on lookup path.
        Assert.IsAssignableFrom<SecurityTokenException>(result.Exception);
    }

    [Fact]
    public async Task PLT_011_Jwt_UnsupportedAlgorithm_AlgNone_Rejected()
    {
        // Crafted attack token: alg=none, no signature. Built only as a test
        // input; the standard handler must reject it (RequireSignedTokens + alg allow-list).
        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":{exp}}}"));
        var unsigned = header + "." + payload + ".";

        var result = await ValidateAsync(unsigned);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void PLT_011_Jwt_ValidationParameters_EnforceAdr004()
    {
        var p = Params();

        Assert.True(p.ValidateIssuer);
        Assert.True(p.ValidateAudience);
        Assert.True(p.ValidateLifetime);
        Assert.True(p.ValidateIssuerSigningKey);
        Assert.True(p.RequireSignedTokens);
        Assert.NotNull(p.IssuerSigningKey);
        Assert.Equal(new[] { SecurityAlgorithms.HmacSha256 }, p.ValidAlgorithms);
        Assert.Equal(TimeSpan.FromMinutes(2), p.ClockSkew);
        Assert.Equal(TimeSpan.FromMinutes(15), JwtAuthDefaults.AccessTokenLifetime);
    }
}
