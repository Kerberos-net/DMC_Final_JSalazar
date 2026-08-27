using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// Jerarquía cerrada del resultado de cualquier comando de <see cref="ServicioDeFacturas"/>/
/// <see cref="ServicioDeAsientos"/> (design.md Data Flow: "ProblemasDeNegocio &lt;- ResultadoComando
/// (Ok | Conflicto | VersionEnConflicto | Invariantes)"). Copia el patrón cerrado de
/// <c>ResultadoConfirmacion</c> (#8): el ctor <c>private protected</c> impide que otro ensamblado
/// añada un quinto caso — <c>SmartNet.Api</c> (#11 Phase 2/3) debe agotar los cuatro en
/// <c>ProblemasDeNegocio.Map</c>.
/// </summary>
public abstract record ResultadoComando
{
    private protected ResultadoComando() { }

    /// <summary>El comando se aplicó — commit ya ocurrió.</summary>
    public sealed record Aplicado : ResultadoComando;

    /// <summary>CAS falló: el If-Match no coincidía con la <c>Version</c> actual — 412 (design D2).</summary>
    public sealed record VersionEnConflicto : ResultadoComando;

    /// <summary>La entidad direccionada no existe — 404.</summary>
    public sealed record NoEncontrado : ResultadoComando;

    /// <summary>Un <see cref="CasoConflicto"/> de la tabla 409 de ADR 0008 bloqueó el comando.</summary>
    public sealed record Conflicto(CasoConflicto Caso, string Detalle) : ResultadoComando;

    /// <summary>Una o más <see cref="InvarianteContable"/> (REGLAS.md §7) fallaron — 422. TODAS las
    /// fallas se devuelven, nunca solo la primera (design D3).</summary>
    public sealed record InvariantesIncumplidas(IReadOnlyList<InvarianteIncumplida> Fallos) : ResultadoComando;

    /// <summary>BACKLOG #18 PR5 (api-facturas delta) — un campo de <see cref="CorreccionFactura"/>
    /// no pasó <c>ValidacionDeCorreccion</c> (numero en blanco o de más de 20 caracteres, tipo de
    /// comprobante fuera del conjunto aceptado) — 422 <c>application/problem+json</c>, ninguna fila
    /// se toca. No es una <see cref="InvarianteContable"/>: es validación de forma del comando, no
    /// una regla contable de REGLAS.md §7.</summary>
    public sealed record CorreccionInvalida(string Detalle) : ResultadoComando;
}
