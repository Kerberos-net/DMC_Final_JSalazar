namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 3 (Phase 3) — resultado puntual de <see cref="IUnidadDeTrabajo.AgregarLineaAsync"/>: igual que
/// <see cref="ResultadoEscritura"/> para las demás escrituras CAS, pero además devuelve el
/// <see cref="LineaId"/> nuevo cuando <see cref="Resultado"/> es <see cref="ResultadoEscritura.Aplicado"/>
/// (nulo en cualquier otro caso -- no hay línea nueva que reportar).
/// </summary>
public sealed record ResultadoLinea(ResultadoEscritura Resultado, long? LineaId);
