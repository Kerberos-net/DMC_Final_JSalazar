namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D6 — espejo read/write de una fila de <c>fact.Configuracion</c>
/// (007_publicacion.sql:24-40). <c>Valor</c> es la forma canónica de texto que ya persiste la
/// tabla (<c>NVARCHAR(400) NULL</c>) — la validación tipada vive en
/// <see cref="ValorDeConfiguracion"/>, nunca aquí.
/// </summary>
public sealed record ConfiguracionEntrada(
    string Seccion,
    string Clave,
    string Tipo,
    string? Valor,
    string? ValorPorDefecto,
    string Descripcion);

/// <summary>
/// design D6 — puerto GET/PUT de <c>ConfiguracionEndpoints</c> (SmartNet.Api) sobre
/// <c>fact.Configuracion</c>. <see cref="ActualizarAsync"/> es UPDATE-only: una <c>Clave</c>
/// desconocida devuelve <see cref="ResultadoActualizacionConfiguracion.NoEncontrado"/>, nunca
/// inserta una fila (las claves se siembran por 009/013/020, spec.md configuracion-api-spa).
/// </summary>
public interface IConfiguracionRepository
{
    /// <summary><paramref name="seccion"/> nulo/vacío -&gt; todas las secciones (spec.md "GET
    /// /api/configuracion[?seccion=]").</summary>
    Task<IReadOnlyList<ConfiguracionEntrada>> ListarAsync(string? seccion, CancellationToken ct);

    /// <summary>Valida <paramref name="valor"/> contra el <c>Tipo</c> declarado de la clave
    /// (<see cref="ValorDeConfiguracion.Validar"/>) ANTES de escribir; una fila inexistente o un
    /// valor inválido no tocan ninguna columna (design D6).</summary>
    Task<ResultadoActualizacionConfiguracion> ActualizarAsync(
        string seccion, string clave, string? valor, long? actualizadoPorUsuarioId, CancellationToken ct);
}
