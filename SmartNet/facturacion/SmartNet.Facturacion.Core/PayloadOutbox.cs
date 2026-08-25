using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D2 — sobre único para los cinco eventos de <c>fact.OutboxEvent</c> (retrofit incluido de
/// FACTURA_VALIDADA/DOCUMENTACION_ACTUALIZADA), construido re-leyendo por <see cref="IUnidadDeTrabajo"/>
/// (la transacción ve sus propias escrituras — nunca desde records en memoria post-escritura).
/// <see cref="ConstruirAsync"/> es el único miembro que toca el puerto; <see cref="Serializar"/> es
/// pura y es lo que <c>PayloadOutboxTests</c> (golden fixtures, tasks.md 1.1) ejercita.
/// </summary>
internal static class PayloadOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// design D2 — re-lee factura, asiento (si aplica) y la lista unificada de documentos por el
    /// puerto, dentro de la transacción en curso, y arma el sobre. <paramref name="asientoContableId"/>
    /// lo pasan explícitamente los dos sitios asiento-rooted (ASIENTO_CORREGIDO/ASIENTO_ANULADO —
    /// este último porque <see cref="IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync"/> excluye ANULADO,
    /// así que resolver "vigente" DESPUÉS de anular ya no encontraría el asiento del propio evento);
    /// los demás pasan <c>null</c> y este método resuelve el vigente.
    ///
    /// Una factura <c>null</c> en este punto es un estado imposible (FK) — se lanza en vez de emitir
    /// un payload hueco; como esto corre dentro de la transacción del comando, el rollback es
    /// automático (design D2).
    /// </summary>
    internal static async Task<string> ConstruirAsync(
        IUnidadDeTrabajo uow, string tipo, long facturaId, long? asientoContableId, CancellationToken ct)
    {
        var factura = await uow.CargarFacturaAsync(facturaId, ct)
            ?? throw new InvalidOperationException(
                $"Factura {facturaId} no existe al construir el payload de outbox para '{tipo}' — estado imposible (FK).");

        var resolvedAsientoId = asientoContableId ?? await uow.ObtenerAsientoVigenteIdAsync(facturaId, ct);

        EnvolturaAsiento? envolturaAsiento = null;
        if (resolvedAsientoId is not null)
        {
            var asiento = await uow.CargarAsientoAsync(resolvedAsientoId.Value, ct);
            if (asiento is not null)
            {
                var lineas = await uow.CargarLineasPersistidasAsync(resolvedAsientoId.Value, ct);
                envolturaAsiento = new EnvolturaAsiento(
                    asiento.AsientoContableId,
                    asiento.NumeroAsiento,
                    asiento.Estado,
                    asiento.Asiento.FechaContable,
                    lineas.Select(l => new EnvolturaLinea(
                        l.LineaId,
                        l.Linea.Bloque.ToString(),
                        l.Linea.Tipo.ToString(),
                        l.Linea.Debe,
                        l.Linea.Haber,
                        l.Linea.CuentaCodigo)).ToArray());
            }
        }

        // spec.md documentos-lista-unificada-api / design D1: AMBOS orígenes completos, nunca solo uno.
        var documentos = new List<EnvolturaDocumento>();
        var documentosFactura = await uow.CargarDocumentosFacturaAsync(facturaId, ct);
        documentos.AddRange(documentosFactura.Select(d => new EnvolturaDocumento(
            "INGESTA", d.DocumentoFacturaId, d.NombreArchivo, d.RutaRelativa, d.MimeType)));
        var adjuntos = await uow.CargarAdjuntosDeFacturaAsync(facturaId, ct);
        documentos.AddRange(adjuntos.Select(a => new EnvolturaDocumento(
            "ADJUNTO", a.AdjuntoManualId, a.NombreArchivo, a.RutaRelativa, a.MimeType)));

        var envoltura = new EnvolturaOutbox(
            Version: 1,
            Evento: tipo,
            FacturaId: facturaId,
            Factura: new EnvolturaFactura(
                factura.Estado, factura.ProveedorCodigo, factura.RucProveedor, factura.TipoComprobante,
                factura.Numero, factura.TotalOrig, factura.Moneda, factura.FechaEmision, factura.Motivo,
                factura.Afectacion, factura.AfectacionMixta ?? false, factura.EsProveedorGenerico,
                factura.PosibleDuplicado, factura.TieneCamposNoExtraidos),
            Asiento: envolturaAsiento,
            Documentos: documentos);

        return Serializar(envoltura);
    }

    /// <summary>Pura, sin I/O — lo que <c>PayloadOutboxTests</c> golden-fixture ejercita directamente.</summary>
    internal static string Serializar(EnvolturaOutbox envoltura) => JsonSerializer.Serialize(envoltura, JsonOptions);
}

/// <summary>design.md Interfaces/Contracts — envelope raíz, idéntico para los 5 <c>Tipo</c> de
/// <c>fact.OutboxEvent</c>; el consumidor Python trata <c>Payload</c> como cadena opaca en #14.</summary>
internal sealed record EnvolturaOutbox(
    int Version,
    string Evento,
    long FacturaId,
    EnvolturaFactura Factura,
    EnvolturaAsiento? Asiento,
    IReadOnlyList<EnvolturaDocumento> Documentos);

internal sealed record EnvolturaFactura(
    string Estado,
    string ProveedorCodigo,
    string? RucProveedor,
    string TipoComprobante,
    string? Numero,
    decimal TotalOrig,
    string Moneda,
    DateOnly FechaEmision,
    int? Motivo,
    string? Afectacion,
    bool AfectacionMixta,
    bool EsProveedorGenerico,
    bool PosibleDuplicado,
    bool TieneCamposNoExtraidos);

/// <summary><c>null</c> únicamente cuando la factura no tiene asiento en absoluto.</summary>
internal sealed record EnvolturaAsiento(
    long AsientoContableId,
    string? NumeroAsiento,
    string Estado,
    DateOnly FechaContable,
    IReadOnlyList<EnvolturaLinea> Lineas);

/// <summary><see cref="Bloque"/>/<see cref="Tipo"/> ya vienen a texto (Bloque.ToString()/Tipo.ToString())
/// — mismo precedente que <c>ServicioDeAsientos.SerializarLineas</c> (design D2).</summary>
internal sealed record EnvolturaLinea(
    long LineaId,
    string Bloque,
    string Tipo,
    decimal Debe,
    decimal Haber,
    string? CuentaCodigo);

/// <summary><c>Origen</c> es <c>"INGESTA"</c> (<see cref="DocumentoFacturaPersistido"/>) o
/// <c>"ADJUNTO"</c> (<see cref="AdjuntoManual"/>) — spec.md documentos-lista-unificada-api.</summary>
internal sealed record EnvolturaDocumento(
    string Origen,
    long Id,
    string NombreArchivo,
    string RutaRelativa,
    string MimeType);
