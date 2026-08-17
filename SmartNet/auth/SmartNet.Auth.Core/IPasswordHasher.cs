namespace SmartNet.Auth.Core;

/// <summary>
/// Port over Argon2id (design.md Decision 5). Encoding/decoding of the PHC string is
/// <see cref="PhcCodec"/>'s job — an implementation of this port delegates to it rather than
/// reimplementing the format (design.md Decision 1).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string clave); // → PHC string

    PasswordVerification Verify(string clave, string phc);
}
