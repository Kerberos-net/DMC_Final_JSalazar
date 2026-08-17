namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>dbo.Motivo</c> (`SmartNet/db/fixtures/010_dbo_catalogos_ddl.sql`). <see cref="Cuenta"/>
/// holds comma-separated PREFIXES, never complete account codes — the raw input to
/// <see cref="ResolucionDePrefijos"/> (REGLAS.md §3).
/// </summary>
public sealed record Motivo(int Codigo, string Descripcion, string? Cuenta);
