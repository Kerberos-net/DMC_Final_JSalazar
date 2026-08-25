using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// design D3 — el ÚNICO lugar de <c>SmartNet.Api</c> que traduce <see cref="ResultadoComando"/> a
/// HTTP (ADR 0019: la lógica de mapeo vive en el host delgado, nunca en Core). Cierra
/// exhaustivamente los cuatro casos de fallo de la jerarquía cerrada de <see cref="ResultadoComando"/>
/// (<c>Aplicado</c> nunca llega aquí -- cada endpoint construye su propia respuesta de éxito, que
/// varía por ruta) y los nueve valores de <see cref="CasoConflicto"/> (409, tabla de ADR 0008) y
/// los siete de <see cref="InvarianteContable"/> (422, salvo Global 3/4 -&gt; 409 por D3/obs #138).
/// </summary>
internal static class ProblemasDeNegocio
{
    private const string Base = "https://smartnet.local/problemas/";

    /// <summary>Traduce el resultado de FALLO de un comando. Llamar con <c>Aplicado</c> es un error
    /// del llamador -- cada endpoint construye su propia respuesta 200/202/204 de éxito.</summary>
    public static IResult Map(ResultadoComando resultado) => resultado switch
    {
        ResultadoComando.NoEncontrado => Results.NotFound(),
        ResultadoComando.VersionEnConflicto => VersionEnConflicto(),
        ResultadoComando.Conflicto c => Conflicto(c),
        ResultadoComando.InvariantesIncumplidas inv => Invariantes(inv),
        ResultadoComando.Aplicado => throw new InvalidOperationException(
            "ProblemasDeNegocio.Map nunca se llama con Aplicado -- cada endpoint construye su propia respuesta de éxito."),
        _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
    };

    /// <summary>design D2 -- <c>If-Match</c> ausente, <c>*</c> o no decodificable como
    /// <see cref="TokenDeConcurrencia"/> válido: 428 Precondition Required (addendum a ADR 0008,
    /// obs #138, ratificado por el dueño del producto -- ADR 0008 solo documenta 409/412/422/400).</summary>
    public static IResult PreconditionRequerida() =>
        TypedResults.Json(
            new ProblemaGenerico(
                Base + "if-match-requerido", "Encabezado If-Match requerido", StatusCodes.Status428PreconditionRequired,
                "Esta operación exige un encabezado If-Match válido con el ETag actual del recurso."),
            statusCode: StatusCodes.Status428PreconditionRequired,
            contentType: "application/problem+json");

    /// <summary>spec.md tipos-de-cambio — "Loading MANUAL for a date that already has a MANUAL row
    /// returns 409": traduce <c>ResultadoCargaManual.YaExistia</c> (composite PK violation), nunca
    /// un silent overwrite.</summary>
    public static IResult TipoCambioManualYaExistente() =>
        TypedResults.Json(
            new ProblemaGenerico(
                Base + "tipo-cambio-ya-existe", "Ya existe un tipo de cambio MANUAL para esa fecha", StatusCodes.Status409Conflict,
                "Ya se cargó un tipo de cambio MANUAL para esta fecha; no se sobrescribe en silencio."),
            statusCode: StatusCodes.Status409Conflict,
            contentType: "application/problem+json");

    /// <summary>spec.md configuracion-api-spa — "Invalid value rejected... the previously stored
    /// value remains unchanged": traduce <see cref="ResultadoActualizacionConfiguracion.ValorInvalido"/>
    /// (design D6, <see cref="ValorDeConfiguracion.Validar"/>) a 422 -- nunca 400: el valor tiene la
    /// forma correcta de un PUT, pero incumple la regla declarada por el <c>Tipo</c> de la clave,
    /// igual que las <see cref="InvarianteContable"/> de abajo.</summary>
    public static IResult ValorDeConfiguracionInvalido() =>
        TypedResults.Json(
            new ProblemaGenerico(
                Base + "configuracion-valor-invalido", "El valor no cumple el tipo declarado de la clave",
                StatusCodes.Status422UnprocessableEntity,
                "El valor enviado no pasó la validación de su Tipo (TEXTO/ENTERO/DECIMAL/BOOLEANO/FECHA/LISTA); el valor previo se conserva."),
            statusCode: StatusCodes.Status422UnprocessableEntity,
            contentType: "application/problem+json");

    private static IResult VersionEnConflicto() =>
        TypedResults.Json(
            new ProblemaGenerico(
                Base + "precondicion-fallida", "El recurso fue modificado por otro cliente", StatusCodes.Status412PreconditionFailed,
                "El If-Match enviado no coincide con la versión actual del recurso; recargue e inténtelo de nuevo."),
            statusCode: StatusCodes.Status412PreconditionFailed,
            contentType: "application/problem+json");

    private static IResult Conflicto(ResultadoComando.Conflicto conflicto)
    {
        var (type, title) = DescribirCaso(conflicto.Caso);
        return TypedResults.Json(
            new ProblemaGenerico(type, title, StatusCodes.Status409Conflict, conflicto.Detalle),
            statusCode: StatusCodes.Status409Conflict,
            contentType: "application/problem+json");
    }

    private static IResult Invariantes(ResultadoComando.InvariantesIncumplidas invariantes)
    {
        if (invariantes.Fallos.Count == 1)
        {
            var problema = DescribirFallo(invariantes.Fallos[0]);
            return TypedResults.Json(problema, statusCode: problema.Status, contentType: "application/problem+json");
        }

        var errores = invariantes.Fallos.Select(DescribirFallo).ToArray();
        return TypedResults.Json(
            new ProblemaAsientoInvalido(
                Base + "asiento-invalido", "El asiento tiene múltiples problemas", StatusCodes.Status422UnprocessableEntity,
                "Ver 'errors' para el detalle de cada invariante incumplida.", errores),
            statusCode: StatusCodes.Status422UnprocessableEntity,
            contentType: "application/problem+json");
    }

    private static ProblemaInvariante DescribirFallo(InvarianteIncumplida fallo)
    {
        var (type, title) = DescribirInvariante(fallo.Invariante);
        var status = EsPrecondicionDeNegocio(fallo.Invariante)
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status422UnprocessableEntity;
        return new ProblemaInvariante(type, title, status, fallo.Detalle, fallo.ImporteEsperado, fallo.ImporteReal);
    }

    // design D3: Global 3 (FechaAnteriorAlCorte) y Global 4 (ProveedorVarios) son precondiciones de
    // negocio de ADR 0008 (409), no invariantes 422 -- SmartNet.Facturacion.Core ya las remapea a
    // ResultadoComando.Conflicto antes de llegar aquí (obs #138); este switch se mantiene
    // exhaustivo por defensividad, no porque el camino normal las alcance.
    private static bool EsPrecondicionDeNegocio(InvarianteContable invariante) => invariante switch
    {
        InvarianteContable.FechaAnteriorAlCorte or InvarianteContable.ProveedorVarios => true,
        _ => false,
    };

    private static (string Type, string Title) DescribirInvariante(InvarianteContable invariante) => invariante switch
    {
        InvarianteContable.SumaDebeIgualHaber => (Base + "asiento-descuadrado", "El asiento no cuadra"),
        InvarianteContable.LineaSinCuenta => (Base + "linea-sin-cuenta", "Línea sin cuenta contable"),
        InvarianteContable.FechaAnteriorAlCorte => (Base + "fecha-anterior-al-corte", "Fecha contable anterior al corte"),
        InvarianteContable.ProveedorVarios => (Base + "proveedor-generico-sin-resolver", "Proveedor genérico sin resolver"),
        InvarianteContable.TipoLineaInconsistente => (Base + "linea-inconsistente", "Línea con tipo/debe/haber inconsistente"),
        InvarianteContable.Principal => (Base + "bloque-principal-invalido", "Bloque principal inválido"),
        InvarianteContable.Destino => (Base + "bloque-destino-incompleto", "Bloque destino incompleto"),
        _ => throw new ArgumentOutOfRangeException(nameof(invariante)),
    };

    private static (string Type, string Title) DescribirCaso(CasoConflicto caso) => caso switch
    {
        CasoConflicto.DuplicadoNoResuelto => (Base + "duplicado-no-resuelto", "Duplicado sin resolver"),
        CasoConflicto.ComprobanteEmitidoDomingo => (Base + "comprobante-domingo", "Comprobante emitido en domingo"),
        CasoConflicto.SinTipoCambio => (Base + "sin-tipo-cambio", "Sin tipo de cambio vigente"),
        CasoConflicto.ProveedorGenericoNoResuelto => (Base + "proveedor-generico-sin-resolver", "Proveedor genérico sin resolver"),
        CasoConflicto.FechaAnteriorAlCorte => (Base + "fecha-anterior-al-corte", "Fecha contable anterior al corte"),
        CasoConflicto.NotaCreditoReferenciaIrresoluble => (Base + "nc-referencia-irresoluble", "Referencia de nota de crédito irresoluble"),
        CasoConflicto.AsientoYaConfirmado => (Base + "asiento-ya-confirmado", "El asiento ya fue confirmado o anulado"),
        CasoConflicto.AfectacionMixta => (Base + "afectacion-mixta", "El comprobante declara más de un código de afectación"),
        CasoConflicto.AfectacionNoVerificada => (Base + "afectacion-no-verificada", "Afectación tributaria no verificada"),
        // outbox-mensajeria (BACKLOG #14, OQ5/ADR 0020 decisión 5)
        CasoConflicto.FacturaDescartada => (Base + "factura-descartada", "Factura descartada"),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };
}

/// <summary>RFC 9457 mínimo -- <c>type</c>/<c>title</c>/<c>status</c>/<c>detail</c>, sin <c>instance</c>.</summary>
internal sealed record ProblemaGenerico(string Type, string Title, int Status, string Detail);

/// <summary>ADR 0008 ejemplo de asiento-descuadrado: <c>importeEsperado</c>/<c>importeReal</c> solo
/// cuando <see cref="InvarianteIncumplida"/> los trae (algunas invariantes, ej. LineaSinCuenta, no
/// llevan importes -- serializan como <c>null</c>, nunca se omiten para mantener la forma estable).</summary>
internal sealed record ProblemaInvariante(
    string Type, string Title, int Status, string Detail, decimal? ImporteEsperado, decimal? ImporteReal);

/// <summary>Dos o más invariantes incumplidas -- design D3: <c>type=.../asiento-invalido</c> con
/// <c>errors[]</c> de la misma forma que <see cref="ProblemaInvariante"/>.</summary>
internal sealed record ProblemaAsientoInvalido(
    string Type, string Title, int Status, string Detail, ProblemaInvariante[] Errors);
