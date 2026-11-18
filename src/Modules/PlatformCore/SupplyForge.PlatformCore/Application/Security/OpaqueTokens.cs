namespace SupplyForge.PlatformCore.Application.Security;

/// <summary>
/// Opaque refresh-token material (ADR-004). Store only the hash.
/// </summary>
public static class OpaqueTokens
{
    public static (string Token, string Hash) Create(int byteLength = 32)
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(byteLength));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));
}
