namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md Interfaces/Contracts — resultado puntual de <see cref="IUnidadDeTrabajo.GuardarAsientoAsync"/>.
/// Distinto de <see cref="ResultadoComando"/>: este es el resultado de UNA escritura CAS dentro de la
/// transacción; el Servicio lo traduce al <see cref="ResultadoComando"/> completo del comando.
/// </summary>
public enum ResultadoEscritura
{
    /// <summary>La escritura CAS afectó exactamente la fila esperada.</summary>
    Aplicado,

    /// <summary>@@ROWCOUNT = 0 y la fila existe con otra <c>Version</c> — 412 (design D2).</summary>
    VersionEnConflicto,

    /// <summary>@@ROWCOUNT = 0 y la fila existe pero en un <c>Estado</c> que no admite el comando — 409.</summary>
    EstadoInvalido,

    /// <summary>@@ROWCOUNT = 0 y la fila no existe — 404.</summary>
    NoEncontrado,
}
