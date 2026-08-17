namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>dbo.Proveedor</c>. <see cref="Codigo"/> ('codpro') is the business key, `CHAR(6)`,
/// never a surrogate id. <see cref="Ruc"/> is nullable and never numeric-typed — leading zeros are
/// data (`SmartNet/db/fixtures/010_dbo_catalogos_ddl.sql`).
/// </summary>
public sealed record Proveedor(string Codigo, string Nombre, string? CodigoTipoDocumento, string? Ruc);
