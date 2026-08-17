namespace SmartNet.Auth.Core;

/// <summary>
/// Matches <c>fact.Sesion.MotivoRevocacion</c>'s value list exactly (schema 011, design.md
/// Decision 2/3). Schema noun, Spanish, per CONVENTIONS.md 1:1-with-the-normative-source rule.
/// </summary>
public enum MotivoRevocacion
{
    CierreSesion,
    Restablecimiento,
    Admin,
}
