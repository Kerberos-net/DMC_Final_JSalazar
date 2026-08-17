namespace SmartNet.Auth.Core;

/// <summary>
/// Port over the CSPRNG session token (design.md Decision 4/5): 256 random bits in the cookie,
/// SHA-256 at rest — the token itself never touches storage, only its hash does.
/// </summary>
public interface ISessionTokenFactory
{
    (string Token, string TokenHash) Create();

    string HashOf(string token);
}
