using System.Security.Cryptography;
using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// Adapter over <see cref="RandomNumberGenerator"/> for <see cref="ISessionTokenFactory"/>
/// (design.md Decision 4). 256 random bits, Base64Url-encoded (43 chars, no padding) travel in
/// the cookie; the database stores <c>SHA256(token)</c> as lowercase hex, never the token itself.
/// </summary>
public sealed class CsprngSessionTokenFactory : ISessionTokenFactory
{
    private const int TokenSizeBytes = 32; // 256 bits

    public (string Token, string TokenHash) Create()
    {
        var raw = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        var token = Base64UrlEncode(raw);
        return (token, HashOf(token));
    }

    public string HashOf(string token)
    {
        var raw = Base64UrlDecode(token);
        var digest = SHA256.HashData(raw);
        return Convert.ToHexStringLower(digest);
    }

    private static string Base64UrlEncode(byte[] raw) =>
        Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
