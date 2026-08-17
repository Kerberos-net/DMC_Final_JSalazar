namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>fact.MotivoAtributo</c> (`004_satelites_datos_maestros.sql`). <see cref="Activo"/>/
/// <see cref="OrigenLibro"/> filtering (e.g. "origen '02'") is a Core-level concern, never an
/// adapter-level `WHERE` (design.md Interfaces/Contracts, spec's satellite scope).
/// </summary>
public sealed record MotivoAtributo(int Motivo, bool Activo, string OrigenLibro);
