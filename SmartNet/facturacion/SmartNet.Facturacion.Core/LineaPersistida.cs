using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 3 (Phase 3) — espejo de una fila de <c>fact.AsientoContableDetalle</c> con su
/// <see cref="LineaId"/> estable (identidad de la fila -- spec.md api-asientos: "Líneas addressed by
/// LineaId, never position"). <see cref="LineaAsiento"/> (#8) deliberadamente no lleva esta columna
/// -- es un campo de ciclo de vida/persistencia, igual que <see cref="AsientoPersistido"/> añade
/// <c>AsientoContableId</c>/<c>Estado</c>/<c>Version</c> sobre <c>AsientoContable</c> (#8).
/// </summary>
public sealed record LineaPersistida(long LineaId, LineaAsiento Linea);
