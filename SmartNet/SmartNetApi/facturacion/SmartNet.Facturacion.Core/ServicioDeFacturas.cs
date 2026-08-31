using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D1/D5 — orquesta <c>validar</c> (= ADR 0006 "confirmar") a través de
/// <see cref="IUnidadDeTrabajo"/>: CAS de estado -&gt; gate D4 -&gt; invariantes (puras, #8) -&gt;
/// correlativo (UPDLOCK, gapless) -&gt; escritura CAS -&gt; outbox -&gt; commit, todo dentro de UNA
/// transacción. Nunca toca SQL directamente (ADR 0019).
///
/// DESVIACIÓN DOCUMENTADA (para revisión en PR 2): design.md solo fijó la forma de
/// <see cref="IUnidadDeTrabajo"/> alrededor de un asiento (<c>CargarAsientoAsync</c>/
/// <c>GuardarAsientoAsync</c> reciben un id de asiento, no de factura). <see cref="ValidarAsync"/>
/// recibe por tanto <c>asientoId</c> directamente — el endpoint <c>POST /api/facturas/{id}/validar</c>
/// (Phase 2) deberá resolver factura-&gt;asiento BORRADOR vigente (UQ_Asiento_Vigente) antes de
/// llamar aquí, o el puerto se ampliará entonces. No se inventó ningún miembro nuevo en
/// <see cref="IUnidadDeTrabajo"/> para evitar apartarse del contrato ya fijado por design.md.
/// </summary>
public sealed class ServicioDeFacturas
{
    private const string OrigenLibro = "02";

    // CK_OutboxEvent_Tipo (006_contratos.sql) NO tiene un valor "ASIENTO_CONFIRMADO" — los cinco
    // valores válidos son FACTURA_VALIDADA/FACTURA_CORREGIDA/ASIENTO_CORREGIDO/ASIENTO_ANULADO/
    // DOCUMENTACION_ACTUALIZADA. "validar" es, desde la tabla de contrato, un FACTURA_VALIDADA.
    private const string TipoEventoFacturaValidada = "FACTURA_VALIDADA";

    // outbox-mensajeria (BACKLOG #14, design D1) -- reconfirmación de un asiento reabierto (D1:
    // persistido.NumeroAsiento ya existe antes de la escritura de validar).
    private const string TipoEventoAsientoCorregido = "ASIENTO_CORREGIDO";

    // outbox-mensajeria (BACKLOG #14, design D8) -- PatchAsync y ConfirmarAfectacionAsync, a lo
    // sumo un evento por transacción cada uno (nunca ambos a la vez -- dos rutas, dos transacciones).
    private const string TipoEventoFacturaCorregida = "FACTURA_CORREGIDA";

    private readonly IFacturacionStore _store;

    public ServicioDeFacturas(IFacturacionStore store) => _store = store;

    public async Task<ResultadoComando> ValidarAsync(
        long asientoId, DateOnly fechaCorteContable, DateTimeOffset ahora, long usuarioId, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);
        return await ValidarInternoAsync(uow, asientoId, fechaCorteContable, ct);
    }

    /// <summary>
    /// PR 2 — envoltorio factura-shaped de <see cref="ValidarAsync"/> (deviación #1 de PR 1):
    /// <c>POST /api/facturas/{id}/validar</c> solo conoce el id de FACTURA, nunca el de asiento.
    /// Resuelve el asiento BORRADOR vigente (<c>UQ_Asiento_Vigente</c>) DENTRO de la misma
    /// transacción antes de reutilizar exactamente la misma lógica de <see cref="ValidarAsync"/> —
    /// ningún comportamiento de D4/D5 se duplica ni se reinterpreta.
    /// </summary>
    public async Task<ResultadoComando> ValidarPorFacturaAsync(
        long facturaId, DateOnly fechaCorteContable, DateTimeOffset ahora, long usuarioId, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var asientoId = await uow.ObtenerAsientoVigenteIdAsync(facturaId, ct);
        if (asientoId is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        return await ValidarInternoAsync(uow, asientoId.Value, fechaCorteContable, ct);
    }

    private static async Task<ResultadoComando> ValidarInternoAsync(
        IUnidadDeTrabajo uow, long asientoId, DateOnly fechaCorteContable, CancellationToken ct)
    {
        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        if (persistido.Estado != AsientoPersistido.Borrador)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado o anulado.");
        }

        var casoPreGate = EvaluarHechosDeConflicto(persistido.Hechos);
        if (casoPreGate is not null)
        {
            return new ResultadoComando.Conflicto(casoPreGate.Value, DescribirCaso(casoPreGate.Value));
        }

        var resultadoInvariantes = InvariantesDeConfirmacion.Evaluar(persistido.Asiento, fechaCorteContable);
        if (resultadoInvariantes is ResultadoConfirmacion.InvariantesIncumplidas incumplidas)
        {
            // obs #138 (D3 ratificado): Global 3 (FechaAnteriorAlCorte) y Global 4 (ProveedorVarios)
            // son precondiciones de negocio de ADR 0008, no invariantes 422 -- se re-mapean a 409
            // aquí, ANTES de exponer InvariantesIncumplidas a la capa HTTP.
            var casoDeNegocio = incumplidas.Fallos.Select(MapearAConflictoDeNegocio).FirstOrDefault(c => c is not null);
            if (casoDeNegocio is not null)
            {
                return new ResultadoComando.Conflicto(casoDeNegocio.Value, DescribirCaso(casoDeNegocio.Value));
            }

            return new ResultadoComando.InvariantesIncumplidas(incumplidas.Fallos);
        }

        // design D1 -- calculado ANTES de sobrescribir NumeroAsiento: un NumeroAsiento ya presente
        // en este punto es, por construcción, "reconfirmado tras reapertura" (ReabrirAsync es la
        // única ruta de vuelta a BORRADOR y no lo limpia).
        var esReconfirmacion = persistido.NumeroAsiento is not null;

        var fechaContable = persistido.Asiento.FechaContable;
        var correlativo = await uow.AsignarCorrelativoAsync(
            (short)fechaContable.Year, (byte)fechaContable.Month, OrigenLibro, ct);
        var numeroAsiento = $"{OrigenLibro}-{fechaContable.Year}-{fechaContable.Month:00}-{correlativo:000000}";

        var confirmado = persistido with { Estado = AsientoPersistido.Confirmado, NumeroAsiento = numeroAsiento };
        var escritura = await uow.GuardarAsientoAsync(asientoId, persistido.Version, confirmado, ct);

        var resultadoEscritura = TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        // design D10/OQ5 -- state-CAS de fact.Factura.Estado. NoTransicionable (hoy: DESCARTADA) es
        // terminal: 409 y rollback -- el "await using var uow" del caller ya deshace todo porque
        // nunca se llega a CommitAsync (ni el asiento CONFIRMADO ni el FACTURA_VALIDADA quedan).
        var transicion = await uow.MarcarFacturaValidadaAsync(persistido.FacturaId, ct);
        if (transicion == TransicionEstadoFactura.NoTransicionable)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.FacturaDescartada, DescribirCaso(CasoConflicto.FacturaDescartada));
        }

        // design D1 -- reconfirmación tras reabrir emite ASIENTO_CORREGIDO, no FACTURA_VALIDADA.
        var tipoEvento = esReconfirmacion ? TipoEventoAsientoCorregido : TipoEventoFacturaValidada;
        var payload = await PayloadOutbox.ConstruirAsync(uow, tipoEvento, persistido.FacturaId, asientoId, ct);
        await uow.EmitirOutboxAsync(tipoEvento, persistido.FacturaId, payload, ct);
        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    private static CasoConflicto? EvaluarHechosDeConflicto(HechosDeConflicto hechos)
    {
        if (hechos.DuplicadoNoResuelto)
        {
            return CasoConflicto.DuplicadoNoResuelto;
        }

        if (hechos.ComprobanteEmitidoDomingo)
        {
            return CasoConflicto.ComprobanteEmitidoDomingo;
        }

        if (hechos.SinTipoCambio)
        {
            return CasoConflicto.SinTipoCambio;
        }

        if (hechos.NotaCreditoReferenciaIrresoluble)
        {
            return CasoConflicto.NotaCreditoReferenciaIrresoluble;
        }

        if (hechos.AfectacionMixta)
        {
            return CasoConflicto.AfectacionMixta;
        }

        if (hechos.AfectacionNoVerificada)
        {
            return CasoConflicto.AfectacionNoVerificada;
        }

        return null;
    }

    private static CasoConflicto? MapearAConflictoDeNegocio(InvarianteIncumplida fallo) => fallo.Invariante switch
    {
        InvarianteContable.FechaAnteriorAlCorte => CasoConflicto.FechaAnteriorAlCorte,
        InvarianteContable.ProveedorVarios => CasoConflicto.ProveedorGenericoNoResuelto,
        _ => null,
    };

    private static string DescribirCaso(CasoConflicto caso) => caso switch
    {
        CasoConflicto.DuplicadoNoResuelto => "Existe una identidad duplicada sin resolver.",
        CasoConflicto.ComprobanteEmitidoDomingo => "El comprobante fue emitido un domingo.",
        CasoConflicto.SinTipoCambio => "No hay tipo de cambio vigente para la fecha de emisión.",
        CasoConflicto.ProveedorGenericoNoResuelto => "El proveedor es P00000 (Varios), no permitido para confirmar.",
        CasoConflicto.FechaAnteriorAlCorte => "La fecha contable es anterior a la fecha de corte.",
        CasoConflicto.NotaCreditoReferenciaIrresoluble => "La referencia interna de la nota de crédito no se pudo resolver.",
        CasoConflicto.AsientoYaConfirmado => "El asiento ya fue confirmado o anulado.",
        CasoConflicto.AfectacionMixta => "El comprobante declara más de un código de afectación.",
        CasoConflicto.AfectacionNoVerificada => "La afectación tributaria aún no fue confirmada.",
        CasoConflicto.FacturaDescartada => "La factura fue descartada; no puede validarse.",
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    internal static ResultadoComando? TraducirResultadoEscritura(ResultadoEscritura escritura) => escritura switch
    {
        ResultadoEscritura.Aplicado => null,
        ResultadoEscritura.VersionEnConflicto => new ResultadoComando.VersionEnConflicto(),
        ResultadoEscritura.NoEncontrado => new ResultadoComando.NoEncontrado(),
        ResultadoEscritura.EstadoInvalido => new ResultadoComando.Conflicto(
            CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado o anulado."),
        _ => throw new ArgumentOutOfRangeException(nameof(escritura)),
    };

    // ------------------------------------------------------------------------------------------
    // PR 2 (Phase 2): PATCH, abrir, validar (arriba), descartar, adjuntos. Todos siguen D1/D5 --
    // una transacción, cargar -> decidir (puro donde aplica) -> escribir -> commit.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// design D6 — <c>PATCH /api/facturas/{id}</c>: CAS sobre <c>fact.Factura.Version</c>, UNA fila
    /// de <see cref="EntradaAuditoria"/>(<c>Accion=CORRECCION</c>) POR CAMPO que de verdad cambió de
    /// valor (comparado contra lo cargado, no contra lo enviado -- reenviar el mismo valor no
    /// audita nada). Permitido en cualquier <c>Estado</c> (spec.md: "correction to already-validated
    /// factura" es un escenario explícito, no un 409).
    /// </summary>
    public async Task<ResultadoComando> PatchAsync(
        long facturaId, byte[] versionEsperada, CorreccionFactura cambios, long usuarioId, DateTimeOffset ahora,
        CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistida = await uow.CargarFacturaAsync(facturaId, ct);
        if (persistida is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        // BACKLOG #18 PR5 (api-facturas delta) -- guarda pura ANTES de escribir: una corrección
        // inválida (numero en blanco / muy largo, tipo de comprobante desconocido) -> 422 sin tocar
        // ninguna fila ni hacer commit.
        var invalida = ValidacionDeCorreccion.Validar(persistida, cambios);
        if (invalida is not null)
        {
            return invalida;
        }

        var (actualizada, entradas) = AplicarCorreccion(persistida, cambios, usuarioId, ahora);

        var escritura = await uow.GuardarFacturaAsync(facturaId, versionEsperada, actualizada, ct);
        var resultadoEscritura = TraducirResultadoEscrituraFactura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        foreach (var entrada in entradas)
        {
            await uow.RegistrarAuditoriaAsync(entrada, ct);
        }

        // BACKLOG #19 (design D6) -- si la tripleta de identidad (RucProveedor, TipoComprobante,
        // Numero -- IX_Factura_Identidad) cambió, recomputar fact.Factura.PosibleDuplicado DENTRO de
        // esta transacción (excluye la propia factura y las DESCARTADA). Sin fila de auditoría: es un
        // indicador derivado, no una corrección del usuario.
        var identidadCambio =
            actualizada.RucProveedor != persistida.RucProveedor
            || actualizada.TipoComprobante != persistida.TipoComprobante
            || actualizada.Numero != persistida.Numero;
        if (identidadCambio)
        {
            var hayDuplicado = await uow.ExisteIdentidadPreviaAsync(
                facturaId, actualizada.RucProveedor, actualizada.TipoComprobante, actualizada.Numero, ct);
            await uow.ActualizarPosibleDuplicadoAsync(facturaId, hayDuplicado, ct);
        }

        // BACKLOG #19 (design D4) -- si cambió el par original (TotalOrig / IgvOrig) o la moneda,
        // re-derivar los tres escalares (BasePEN / IgvPEN / NetoPEN, REGLAS.md §5/§6 vía
        // ProyeccionDeImportes) y escribirlos sobre el asiento BORRADOR vigente en la MISMA
        // transacción. Sin tipo de cambio aplicable (moneda extranjera, asiento sin TipoCambioVenta
        // congelado) -> se omite la escritura, el PATCH igual responde 200 y el gate SinTipoCambio ya
        // existente bloqueará validar.
        var importesCambiaron =
            actualizada.TotalOrig != persistida.TotalOrig
            || actualizada.IgvOrig != persistida.IgvOrig
            || actualizada.Moneda != persistida.Moneda;
        if (importesCambiaron)
        {
            var asientoId = await uow.ObtenerAsientoVigenteIdAsync(facturaId, ct);
            if (asientoId is not null)
            {
                var asiento = await uow.CargarAsientoAsync(asientoId.Value, ct);
                if (asiento is not null && asiento.Estado == AsientoPersistido.Borrador)
                {
                    var tcVenta = actualizada.Moneda == MonedaLocal ? 1m : asiento.Asiento.TipoCambioVenta;
                    if (tcVenta is not null)
                    {
                        var igvOrig = actualizada.IgvOrig ?? 0m;
                        var baseOrig = actualizada.TotalOrig - igvOrig;
                        var proyeccion = ProyeccionDeImportes.Derivar(
                            CodigoComprobante.Convertir(actualizada.TipoComprobante),
                            MapearAfectacion(actualizada.Afectacion),
                            baseOrig, igvOrig, tcVenta.Value);
                        await uow.ActualizarProyeccionEscalarAsync(
                            asientoId.Value, proyeccion.BasePEN, proyeccion.IgvPEN, proyeccion.NetoPEN, ct);
                    }
                }
            }
        }

        // outbox-mensajeria (BACKLOG #14, design D8) -- FACTURA_CORREGIDA iff algo cambió de verdad
        // (entradas.Count > 0 -- Auditar ya descarta reenvíos del mismo valor) y la factura ya está
        // VALIDADA (spec.md: "update to a non-validated invoice emits no FACTURA_CORREGIDA").
        if (entradas.Count > 0 && persistida.Estado == FacturaPersistida.Validada)
        {
            var payload = await PayloadOutbox.ConstruirAsync(uow, TipoEventoFacturaCorregida, facturaId, null, ct);
            await uow.EmitirOutboxAsync(TipoEventoFacturaCorregida, facturaId, payload, ct);
        }

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>Código ISO 4217 de la moneda local (soles) -- <c>fact.Factura.Moneda</c> solo
    /// impone <c>CHAR(3)</c> mayúsculas (CK_Factura_Moneda), ninguna otra restricción; "distinta de
    /// PEN" es la definición de "moneda extranjera" en este motor (spec.md, ADR 0018 pt. 3).</summary>
    private const string MonedaLocal = "PEN";

    /// <summary>
    /// design D1/ADR 0006 -- <c>POST /api/facturas/{id}/abrir</c>: crea el asiento BORRADOR si la
    /// factura no tiene ninguno vigente (idempotente -- si ya existe, no crea un segundo, nunca
    /// falla, incluso si la factura está en moneda extranjera sin tipo de cambio -- el gate D4 solo
    /// se evalúa cuando <c>abrir</c> va a CREAR un asiento). Nunca escribe <see cref="EntradaAuditoria"/>
    /// (D6: <c>abrir</c> no está en el enum <c>Accion</c>).
    ///
    /// PR 5 (Phase 5, verify-report.md CRITICAL finding) -- cierra la deviación continuada de PR 1
    /// (#4) y PR 2 (#8): factura en moneda extranjera (<c>Moneda != PEN</c>) sin tipo de cambio
    /// vigente para su <c>FechaEmision</c> -&gt; 409 <see cref="CasoConflicto.SinTipoCambio"/>, ANTES
    /// de crear el asiento. Usa <see cref="IUnidadDeTrabajo.ExisteTipoCambioVigenteAsync"/> (no
    /// <see cref="HechosDeConflicto"/>: ese record depende de un asiento ya existente vía
    /// <c>CargarAsientoAsync</c>, que <c>abrir</c> todavía no tiene cuando decide crear uno).
    /// </summary>
    public async Task<ResultadoComando> AbrirAsync(long facturaId, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var factura = await uow.CargarFacturaAsync(facturaId, ct);
        if (factura is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        var asientoExistenteId = await uow.ObtenerAsientoVigenteIdAsync(facturaId, ct);
        if (asientoExistenteId is not null)
        {
            // Idempotente: ya existe un asiento vigente -- abrir no crea uno segundo ni falla.
            await uow.CommitAsync(ct);
            return new ResultadoComando.Aplicado();
        }

        if (factura.Moneda != MonedaLocal)
        {
            var tieneTipoCambio = await uow.ExisteTipoCambioVigenteAsync(factura.FechaEmision, ct);
            if (!tieneTipoCambio)
            {
                return new ResultadoComando.Conflicto(
                    CasoConflicto.SinTipoCambio, DescribirCaso(CasoConflicto.SinTipoCambio));
            }
        }

        await uow.CrearAsientoBorradorAsync(facturaId, factura.ProveedorCodigo, factura.FechaEmision, ct);
        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>
    /// design D6 -- <c>POST /api/facturas/{id}/descartar</c>: <c>Estado -&gt; DESCARTADA</c>, CAS,
    /// SIN auditoría (D6: <c>descartar</c> no está en el enum <c>Accion</c>). Una factura ya
    /// VALIDADA no puede descartarse (reusa <see cref="CasoConflicto.AsientoYaConfirmado"/> --
    /// mismo patrón de reutilización de PR 1 deviación #2, documentado aquí también: no hay un
    /// caso de la tabla 409 de ADR 0008 específico para "factura ya validada").
    /// </summary>
    public async Task<ResultadoComando> DescartarAsync(long facturaId, byte[] versionEsperada, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var factura = await uow.CargarFacturaAsync(facturaId, ct);
        if (factura is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        if (factura.Estado == FacturaPersistida.Validada)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "La factura ya fue validada; no puede descartarse.");
        }

        var descartada = factura with { Estado = FacturaPersistida.Descartada };
        var escritura = await uow.GuardarFacturaAsync(facturaId, versionEsperada, descartada, ct);
        var resultadoEscritura = TraducirResultadoEscrituraFactura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>
    /// design D6/ADR 0008 -- <c>POST /api/facturas/{id}/adjuntos</c>: SIN auditoría al agregar
    /// (D6 no lista "agregar adjunto" en el enum <c>Accion</c>); emite <c>DOCUMENTACION_ACTUALIZADA</c>
    /// solo cuando la factura ya está VALIDADA (spec.md).
    /// </summary>
    public async Task<ResultadoComando> RegistrarAdjuntoAsync(long facturaId, AdjuntoManual adjunto, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var factura = await uow.CargarFacturaAsync(facturaId, ct);
        if (factura is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        await uow.RegistrarAdjuntoAsync(adjunto, ct);

        if (factura.Estado == FacturaPersistida.Validada)
        {
            // outbox-mensajeria (BACKLOG #14, design D2) -- retrofit: sobre completo vía
            // PayloadOutbox en vez del escalar suelto NombreArchivo.
            var payload = await PayloadOutbox.ConstruirAsync(uow, TipoEventoDocumentacionActualizada, facturaId, null, ct);
            await uow.EmitirOutboxAsync(TipoEventoDocumentacionActualizada, facturaId, payload, ct);
        }

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>
    /// design D6/ADR 0008 -- <c>DELETE /api/facturas/{id}/adjuntos/{adjuntoId}</c>: borrado lógico +
    /// SIEMPRE <see cref="EntradaAuditoria"/>(<c>Accion=ELIMINACION_ADJUNTO</c>) + emite
    /// <c>DOCUMENTACION_ACTUALIZADA</c> cuando la factura ya está VALIDADA.
    /// </summary>
    public async Task<ResultadoComando> EliminarAdjuntoAsync(
        long facturaId, long adjuntoManualId, long usuarioId, string motivo, DateTimeOffset ahora,
        CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var factura = await uow.CargarFacturaAsync(facturaId, ct);
        if (factura is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        var escritura = await uow.EliminarAdjuntoAsync(adjuntoManualId, facturaId, ahora, usuarioId, motivo, ct);
        if (escritura == ResultadoEscritura.NoEncontrado)
        {
            return new ResultadoComando.NoEncontrado();
        }

        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Adjunto, adjuntoManualId, EntradaAuditoria.Acciones.EliminacionAdjunto,
                Campo: "EliminadoEn", ValorOriginal: null, ValorNuevo: ahora.UtcDateTime.ToString("O"),
                Motivo: motivo, UsuarioId: usuarioId, OcurridoEn: ahora),
            ct);

        if (factura.Estado == FacturaPersistida.Validada)
        {
            // outbox-mensajeria (BACKLOG #14, design D2) -- retrofit: sobre completo vía
            // PayloadOutbox en vez del escalar suelto adjuntoId.ToString().
            var payload = await PayloadOutbox.ConstruirAsync(uow, TipoEventoDocumentacionActualizada, facturaId, null, ct);
            await uow.EmitirOutboxAsync(TipoEventoDocumentacionActualizada, facturaId, payload, ct);
        }

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>
    /// diseno-visual-spa-item-12 (design D10) -- <c>POST /api/facturas/{id}/confirmar-afectacion</c>:
    /// CAS DEDICADO sobre <c>fact.Factura.AfectacionMixta</c> (<see cref="IUnidadDeTrabajo.
    /// ConfirmarAfectacionAsync"/>, no <see cref="GuardarFacturaAsync"/> -- ese UPDATE nunca toca esa
    /// columna, design D9), SIEMPRE audita <c>CONFIRMACION_AFECTACION</c> (D6: es una de las siete
    /// <c>Accion</c> de la tabla, no una excepción). Deliberadamente NO evalúa ni conecta
    /// <see cref="CasoConflicto.AfectacionNoVerificada"/> -- el gate permanece dormido en esta
    /// entrega (ver Open Questions de design.md); esto solo registra la afirmación del asistente.
    /// </summary>
    public async Task<ResultadoComando> ConfirmarAfectacionAsync(
        long facturaId, byte[] versionEsperada, bool esMixta, long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var factura = await uow.CargarFacturaAsync(facturaId, ct);
        if (factura is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        var escritura = await uow.ConfirmarAfectacionAsync(facturaId, versionEsperada, esMixta, ct);
        var resultadoEscritura = TraducirResultadoEscrituraFactura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Factura, facturaId, EntradaAuditoria.Acciones.ConfirmacionAfectacion,
                Campo: nameof(FacturaPersistida.AfectacionMixta), ValorOriginal: factura.AfectacionMixta?.ToString(),
                ValorNuevo: esMixta.ToString(), Motivo: null, usuarioId, ahora),
            ct);

        // outbox-mensajeria (BACKLOG #14, design D8) -- FACTURA_CORREGIDA iff AfectacionMixta
        // realmente cambió (un valor reenviado idéntico no es un hecho de negocio nuevo) y la
        // factura ya está VALIDADA (spec.md: "on any accepted update to a validated invoice").
        if (factura.AfectacionMixta != esMixta && factura.Estado == FacturaPersistida.Validada)
        {
            var payload = await PayloadOutbox.ConstruirAsync(uow, TipoEventoFacturaCorregida, facturaId, null, ct);
            await uow.EmitirOutboxAsync(TipoEventoFacturaCorregida, facturaId, payload, ct);
        }

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    private static (FacturaPersistida Actualizada, IReadOnlyList<EntradaAuditoria> Entradas) AplicarCorreccion(
        FacturaPersistida original, CorreccionFactura cambios, long usuarioId, DateTimeOffset ahora)
    {
        var entradas = new List<EntradaAuditoria>();
        var actualizada = original;

        void Auditar(string campo, string? valorOriginal, string? valorNuevo)
        {
            if (valorOriginal == valorNuevo)
            {
                return;
            }

            entradas.Add(new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Factura, original.FacturaId, EntradaAuditoria.Acciones.Correccion,
                campo, valorOriginal, valorNuevo, Motivo: null, usuarioId, ahora));
        }

        if (cambios.ProveedorCodigo is not null)
        {
            Auditar(nameof(FacturaPersistida.ProveedorCodigo), original.ProveedorCodigo, cambios.ProveedorCodigo);
            actualizada = actualizada with { ProveedorCodigo = cambios.ProveedorCodigo };
        }

        if (cambios.RucProveedor is not null)
        {
            Auditar(nameof(FacturaPersistida.RucProveedor), original.RucProveedor, cambios.RucProveedor);
            actualizada = actualizada with { RucProveedor = cambios.RucProveedor };
        }

        if (cambios.Moneda is not null)
        {
            Auditar(nameof(FacturaPersistida.Moneda), original.Moneda, cambios.Moneda);
            actualizada = actualizada with { Moneda = cambios.Moneda };
        }

        if (cambios.TotalOrig is not null)
        {
            Auditar(
                nameof(FacturaPersistida.TotalOrig),
                original.TotalOrig.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cambios.TotalOrig.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            actualizada = actualizada with { TotalOrig = cambios.TotalOrig.Value };
        }

        if (cambios.FechaEmision is not null)
        {
            Auditar(
                nameof(FacturaPersistida.FechaEmision),
                original.FechaEmision.ToString("O"),
                cambios.FechaEmision.Value.ToString("O"));
            actualizada = actualizada with { FechaEmision = cambios.FechaEmision.Value };
        }

        if (cambios.Motivo is not null)
        {
            Auditar(nameof(FacturaPersistida.Motivo), original.Motivo?.ToString(), cambios.Motivo.Value.ToString());
            actualizada = actualizada with { Motivo = cambios.Motivo.Value };
        }

        if (cambios.Afectacion is not null)
        {
            Auditar(nameof(FacturaPersistida.Afectacion), original.Afectacion, cambios.Afectacion);
            actualizada = actualizada with { Afectacion = cambios.Afectacion };
        }

        // BACKLOG #18 PR5 (api-facturas delta) -- mismos dos campos que la SPA ahora edita; el
        // conjunto de valores aceptados de TipoComprobante ya lo validó ValidacionDeCorreccion.
        if (cambios.TipoComprobante is not null)
        {
            Auditar(nameof(FacturaPersistida.TipoComprobante), original.TipoComprobante, cambios.TipoComprobante);
            actualizada = actualizada with { TipoComprobante = cambios.TipoComprobante };
        }

        if (cambios.Numero is not null)
        {
            Auditar(nameof(FacturaPersistida.Numero), original.Numero, cambios.Numero);
            actualizada = actualizada with { Numero = cambios.Numero };
        }

        // BACKLOG #19 (design D1/D7) -- base imponible + IGV son un PAR ATOMICO; la base es DERIVADA
        // (REGLAS.md §6), no una columna. El ladder escribe TotalOrig = base + IGV e IgvOrig = IGV, y
        // audita UNA fila por columna persistida que de verdad cambio (TotalOrig, IgvOrig) -- NUNCA
        // una fila sintetica "BaseImponible" (D7). La atomicidad del par y el choque con totalOrig
        // ya los rechazo ValidacionDeCorreccion (422) antes de llegar aca.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (cambios.BaseImponible is not null && cambios.Igv is not null)
        {
            var nuevoTotal = cambios.BaseImponible.Value + cambios.Igv.Value;
            Auditar(
                nameof(FacturaPersistida.TotalOrig),
                original.TotalOrig.ToString(inv),
                nuevoTotal.ToString(inv));
            Auditar(
                nameof(FacturaPersistida.IgvOrig),
                original.IgvOrig?.ToString(inv),
                cambios.Igv.Value.ToString(inv));
            actualizada = actualizada with { TotalOrig = nuevoTotal, IgvOrig = cambios.Igv.Value };
        }

        if (cambios.Glosa is not null)
        {
            Auditar(nameof(FacturaPersistida.Glosa), original.Glosa, cambios.Glosa);
            actualizada = actualizada with { Glosa = cambios.Glosa };
        }

        return (actualizada, entradas);
    }

    /// <summary>BACKLOG #19 (design D4) -- mapeo del codigo textual de <c>fact.Factura.Afectacion</c>
    /// (<c>GRAVADA</c> / <c>EXONERADA</c> / <c>INAFECTA</c>, o <c>null</c>) al enum de dominio. Mismo
    /// criterio que <c>SqlUnidadDeTrabajo.MapearAfectacion</c>: un valor ausente o desconocido cae a
    /// GRAVADA (el asiento se compone como gravado salvo prueba en contrario).</summary>
    private static Afectacion MapearAfectacion(string? codigo) => codigo switch
    {
        "EXONERADA" => Afectacion.Exonerada,
        "INAFECTA" => Afectacion.Inafecta,
        _ => Afectacion.Gravada,
    };

    // CK_OutboxEvent_Tipo (006_contratos.sql) -- el quinto valor válido, para adjuntos post-validar.
    private const string TipoEventoDocumentacionActualizada = "DOCUMENTACION_ACTUALIZADA";

    private static ResultadoComando? TraducirResultadoEscrituraFactura(ResultadoEscritura escritura) => escritura switch
    {
        ResultadoEscritura.Aplicado => null,
        ResultadoEscritura.VersionEnConflicto => new ResultadoComando.VersionEnConflicto(),
        ResultadoEscritura.NoEncontrado => new ResultadoComando.NoEncontrado(),
        ResultadoEscritura.EstadoInvalido => new ResultadoComando.Conflicto(
            CasoConflicto.AsientoYaConfirmado, "La factura ya fue validada o descartada."),
        _ => throw new ArgumentOutOfRangeException(nameof(escritura)),
    };
}
