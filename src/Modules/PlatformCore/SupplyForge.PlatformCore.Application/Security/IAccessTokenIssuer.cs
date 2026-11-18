namespace SupplyForge.PlatformCore.Application.Security;

/// <summary>
/// Access-token issuance boundary (ADR-004: JWT HS256, 15-minute TTL).
/// The concrete JWT creation lives at the HTTP composition root so this
/// module stays free of token-format dependencies; validation is done by the
/// standard ASP.NET Core JWT Bearer handler, never by this module.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessTokenIssueResult Issue(Guid userId, string email);
}

public sealed record AccessTokenIssueResult(string Token, DateTimeOffset ExpiresAt);
