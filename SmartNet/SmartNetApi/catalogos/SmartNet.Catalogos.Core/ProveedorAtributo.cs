namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>fact.ProveedorAtributo</c> (`004_satelites_datos_maestros.sql`). Keyed on
/// <c>dbo.Proveedor</c>'s own business code — no FK (ADR 0003, design.md Decision 2).
/// </summary>
public sealed record ProveedorAtributo(string ProveedorCodigo, bool EsRelacionada);
