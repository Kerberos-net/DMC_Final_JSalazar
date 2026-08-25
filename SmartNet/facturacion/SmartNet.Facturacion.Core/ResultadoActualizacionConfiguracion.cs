namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D6 — jerarquía cerrada del resultado de <c>IConfiguracionRepository.ActualizarAsync</c>
/// (mismo patrón que <see cref="ResultadoComando"/>): "unknown key -&gt; 404, never INSERT" y
/// "invalid value rejected, prior value retained" son casos DISTINTOS -- <c>ConfiguracionEndpoints</c>
/// (SmartNet.Api) los mapea a 404 y a un <c>ProblemaGenerico</c> 422 respectivamente, vía
/// <c>ProblemasDeNegocio</c>.
/// </summary>
public abstract record ResultadoActualizacionConfiguracion
{
    private protected ResultadoActualizacionConfiguracion() { }

    /// <summary>La fila existía, el valor pasó <see cref="ValorDeConfiguracion.Validar"/> y se
    /// actualizó (Valor + ActualizadoPorUsuarioId + ActualizadoEn).</summary>
    public sealed record Actualizado : ResultadoActualizacionConfiguracion;

    /// <summary>No existe fila para <c>(Seccion, Clave)</c> — UPDATE-only, NUNCA se inserta una
    /// clave desconocida (spec.md configuracion-api-spa, tasks.md 5.3).</summary>
    public sealed record NoEncontrado : ResultadoActualizacionConfiguracion;

    /// <summary><c>valor</c> no pasó <see cref="ValorDeConfiguracion.Validar"/> para el <c>Tipo</c>
    /// declarado de la clave — el valor previo NO se toca (spec.md "the previously stored value
    /// remains unchanged").</summary>
    public sealed record ValorInvalido : ResultadoActualizacionConfiguracion;
}
