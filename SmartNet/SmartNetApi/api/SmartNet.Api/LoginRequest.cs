namespace SmartNet.Api;

/// <summary>
/// <c>POST /api/sesion</c> request body (design.md Decision 6). Field names are the schema's own
/// nouns (<c>NombreUsuario</c>), per CONVENTIONS.md's schema-noun rule.
/// </summary>
public sealed record LoginRequest(string NombreUsuario, string Clave);
