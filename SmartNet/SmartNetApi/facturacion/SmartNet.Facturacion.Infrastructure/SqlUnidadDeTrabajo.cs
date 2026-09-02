using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;
using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;
using SmartNet.Sugerencia.Core;
using SmartNet.TiposCambio.Core;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D1/D5 — implementación SQL de <see cref="IUnidadDeTrabajo"/>: posee la
/// <see cref="SqlConnection"/>/<see cref="SqlTransaction"/> abiertas por
/// <see cref="SqlFacturacionStore.AbrirAsync"/>; rollback salvo <see cref="CommitAsync"/> explícito
/// (<see cref="DisposeAsync"/>).
///
/// ALCANCE PHASE 1 (cerrado en PR 3): <see cref="GuardarAsientoAsync"/> sigue escribiendo solo las
/// columnas de encabezado de <c>fact.AsientoContable</c> (Estado/NumeroAsiento/campos congelados) —
/// las líneas (<c>fact.AsientoContableDetalle</c>) ahora se escriben por <see cref="AgregarLineaAsync"/>/
/// <see cref="ActualizarLineaAsync"/>/<see cref="EliminarLineaAsync"/> (PR 3, Phase 3), cada uno con
/// su propio CAS de encabezado -- ver el comentario de <see cref="TocarEncabezadoAsync"/>.
///
/// <see cref="CargarAsientoAsync"/> calcula <see cref="HechosDeConflicto.DuplicadoNoResuelto"/> vía
/// <c>IX_Factura_Identidad</c> y (PR 5, cierra el gap) <see cref="HechosDeConflicto.SinTipoCambio"/>
/// vía <see cref="ITipoCambioRepository"/> para facturas en moneda distinta de PEN; los otros tres
/// hechos (<c>ComprobanteEmitidoDomingo</c>/<c>NotaCreditoReferenciaIrresoluble</c>/
/// <c>AfectacionMixta</c>/<c>AfectacionNoVerificada</c>) siguen en <c>false</c>, fuera del alcance de
/// #11 (flujo de asociación de NC, Phase futura) — <see cref="HechosDeConflicto"/> documenta esto en
/// su propio comentario.
/// </summary>
public sealed class SqlUnidadDeTrabajo : IUnidadDeTrabajo
{
    /// <summary>Misma constante que <c>ServicioDeFacturas.MonedaLocal</c> -- "moneda extranjera" =
    /// <c>Moneda != PEN</c> (spec.md, ADR 0018 pt. 3). Duplicada deliberadamente: Core no puede
    /// referenciar Infrastructure, e Infrastructure no expone constantes de Core.</summary>
    private const string MonedaLocal = "PEN";

    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly ITipoCambioRepository _tipoCambioRepository;
    private readonly ServicioDeSugerencia? _servicioDeSugerencia;
    private bool _committed;
    private bool _disposed;

    // outbox-mensajeria (BACKLOG #14, design D8) -- a lo sumo un evento por (Tipo, FacturaId) por
    // transacción; mirror de FakeUnidadDeTrabajo.EmitirOutboxAsync (Core.Tests). Fail-loud dentro
    // de la transacción en curso -- el "await using" del caller ya deshace todo, así que este throw
    // nunca deja una fila a medio escribir.
    private readonly HashSet<(string Tipo, long FacturaId)> _emitidosEnEstaTx = new();

    internal SqlUnidadDeTrabajo(
        SqlConnection connection,
        SqlTransaction transaction,
        ITipoCambioRepository tipoCambioRepository,
        ServicioDeSugerencia? servicioDeSugerencia = null)
    {
        _connection = connection;
        _transaction = transaction;
        _tipoCambioRepository = tipoCambioRepository;
        _servicioDeSugerencia = servicioDeSugerencia;
    }

    public async Task<AsientoPersistido?> CargarAsientoAsync(long asientoId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT a.AsientoContableId, a.FacturaId, a.Estado, a.NumeroAsiento, a.Version,
                   a.ProveedorCodigo, a.FechaContable, a.MotivoDescripcion, a.TipoCambioVenta,
                   a.BasePEN, a.IgvPEN, a.NetoPEN,
                   f.Afectacion, f.TipoComprobante, f.RucProveedor, f.Numero, f.Moneda, f.FechaEmision,
                   f.EsReferenciaExterna, f.FacturaReferenciaId
            FROM fact.AsientoContable a
            JOIN fact.Factura f ON f.FacturaId = a.FacturaId
            WHERE a.AsientoContableId = @asientoId;
            """);
        command.Parameters.AddWithValue("@asientoId", asientoId);

        long facturaId;
        string estado, proveedorCodigo, tipoComprobanteCodigo, moneda;
        string? numeroAsiento, motivoDescripcion, afectacionCodigo, rucProveedor, numero;
        decimal? tipoCambioVenta, basePen, igvPen, netoPen;
        DateOnly fechaContable, fechaEmision;
        byte[] version;
        bool esReferenciaExterna;
        long? facturaReferenciaId;

        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            facturaId = reader.GetInt64(1);
            estado = reader.GetString(2).TrimEnd();
            numeroAsiento = reader.IsDBNull(3) ? null : reader.GetString(3).TrimEnd();
            version = (byte[])reader[4];
            proveedorCodigo = reader.GetString(5).TrimEnd();
            fechaContable = DateOnly.FromDateTime(reader.GetDateTime(6));
            motivoDescripcion = reader.IsDBNull(7) ? null : reader.GetString(7);
            tipoCambioVenta = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
            basePen = reader.IsDBNull(9) ? null : reader.GetDecimal(9);
            igvPen = reader.IsDBNull(10) ? null : reader.GetDecimal(10);
            netoPen = reader.IsDBNull(11) ? null : reader.GetDecimal(11);
            afectacionCodigo = reader.IsDBNull(12) ? null : reader.GetString(12).TrimEnd();
            tipoComprobanteCodigo = reader.GetString(13).TrimEnd();
            rucProveedor = reader.IsDBNull(14) ? null : reader.GetString(14).TrimEnd();
            numero = reader.IsDBNull(15) ? null : reader.GetString(15).TrimEnd();
            moneda = reader.GetString(16).TrimEnd();
            fechaEmision = DateOnly.FromDateTime(reader.GetDateTime(17));
            esReferenciaExterna = reader.GetBoolean(18);
            facturaReferenciaId = reader.IsDBNull(19) ? null : reader.GetInt64(19);
        }

        var lineas = await CargarLineasAsync(asientoId, ct);
        var duplicadoNoResuelto = await ExisteDuplicadoNoResueltoAsync(facturaId, rucProveedor, tipoComprobanteCodigo, numero, ct);

        // PR 5 -- mismo criterio que ServicioDeFacturas.AbrirAsync: moneda extranjera sin tipo de
        // cambio vigente para la FechaEmision de la factura. Solo se consulta ITipoCambioRepository
        // cuando hace falta (moneda local nunca dispara este 409, ADR 0018 pt. 3).
        //
        // BACKLOG #19 (design D, correction 2 / REGLAS.md §6) -- NARROWING: una NC 07 con referencia
        // INTERNA (EsReferenciaExterna = 0 AND FacturaReferenciaId IS NOT NULL) hereda el tipo de
        // cambio congelado del comprobante que rectifica, asi que NO necesita un TC vigente propio.
        // La NC 07 con referencia EXTERNA si lo necesita. Rama DORMIDA hoy: FacturaReferenciaId no
        // se puebla hasta el flujo de asociacion de NC (#10/#11).
        var esNc07ReferenciaInterna =
            tipoComprobanteCodigo == "07" && !esReferenciaExterna && facturaReferenciaId is not null;
        var sinTipoCambio = moneda != MonedaLocal
            && !esNc07ReferenciaInterna
            && !await ExisteTipoCambioVigenteAsync(fechaEmision, ct);

        var asiento = new AsientoContable(
            ProveedorCodigo: proveedorCodigo,
            FechaContable: fechaContable,
            MotivoDescripcion: motivoDescripcion,
            TipoCambioVenta: tipoCambioVenta,
            BasePEN: basePen ?? 0m,
            IgvPEN: igvPen ?? 0m,
            NetoPEN: netoPen ?? 0m,
            AfectacionCongelada: MapearAfectacion(afectacionCodigo),
            Comprobante: MapearTipoComprobante(tipoComprobanteCodigo),
            Lineas: lineas);

        var hechos = HechosDeConflicto.Ninguno with { DuplicadoNoResuelto = duplicadoNoResuelto, SinTipoCambio = sinTipoCambio };

        return new AsientoPersistido(asientoId, facturaId, estado, numeroAsiento, version, asiento, hechos);
    }

    public async Task<ResultadoEscritura> GuardarAsientoAsync(
        long id, byte[] versionEsperada, AsientoPersistido asiento, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            UPDATE fact.AsientoContable
            SET Estado = @estado, NumeroAsiento = @numeroAsiento, ProveedorCodigo = @proveedorCodigo,
                MotivoDescripcion = @motivoDescripcion, FechaContable = @fechaContable,
                TipoCambioVenta = @tipoCambioVenta, BasePEN = @basePen, IgvPEN = @igvPen, NetoPEN = @netoPen
            WHERE AsientoContableId = @id AND Version = @versionEsperada;
            """);
        command.Parameters.AddWithValue("@estado", asiento.Estado);
        command.Parameters.AddWithValue("@numeroAsiento", (object?)asiento.NumeroAsiento ?? DBNull.Value);
        command.Parameters.AddWithValue("@proveedorCodigo", asiento.Asiento.ProveedorCodigo);
        command.Parameters.AddWithValue("@motivoDescripcion", (object?)asiento.Asiento.MotivoDescripcion ?? DBNull.Value);
        command.Parameters.AddWithValue("@fechaContable", asiento.Asiento.FechaContable.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@tipoCambioVenta", (object?)asiento.Asiento.TipoCambioVenta ?? DBNull.Value);
        command.Parameters.AddWithValue("@basePen", asiento.Asiento.BasePEN);
        command.Parameters.AddWithValue("@igvPen", asiento.Asiento.IgvPEN);
        command.Parameters.AddWithValue("@netoPen", asiento.Asiento.NetoPEN);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@versionEsperada", versionEsperada);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        if (filasAfectadas > 0)
        {
            return ResultadoEscritura.Aplicado;
        }

        // @@ROWCOUNT = 0 -- design D2: re-SELECT para distinguir 404 de 412.
        await using var verificacion = CrearComando(
            "SELECT COUNT(*) FROM fact.AsientoContable WHERE AsientoContableId = @id;");
        verificacion.Parameters.AddWithValue("@id", id);
        var existe = (int)(await verificacion.ExecuteScalarAsync(ct))! > 0;

        return existe ? ResultadoEscritura.VersionEnConflicto : ResultadoEscritura.NoEncontrado;
    }

    public async Task<int> AsignarCorrelativoAsync(short anio, byte mes, string origen, CancellationToken ct)
    {
        // design D5 -- UPDLOCK dentro de la transacción que confirma, para que una transacción
        // revertida devuelva el número (nunca SEQUENCE/IDENTITY, ADR 0006).
        await using (var incremento = CrearComando(
            """
            UPDATE fact.CorrelativoAsiento WITH (UPDLOCK, HOLDLOCK)
            SET Ultimo = Ultimo + 1
            OUTPUT inserted.Ultimo
            WHERE Anio = @anio AND Mes = @mes AND Origen = @origen;
            """))
        {
            incremento.Parameters.AddWithValue("@anio", anio);
            incremento.Parameters.AddWithValue("@mes", mes);
            incremento.Parameters.AddWithValue("@origen", origen);

            var resultado = await incremento.ExecuteScalarAsync(ct);
            if (resultado is int ultimo)
            {
                return ultimo;
            }
        }

        // No existía fila para (Anio, Mes, Origen) -- sembrarla en Ultimo=1. Anti-TOCTOU: si otra
        // transacción concurrente ya la sembró (2627, PK_CorrelativoAsiento), reintentar el UPDATE
        // en vez de fallar (mismo idioma que SqlPromocionRepository, design D5).
        try
        {
            await using var siembra = CrearComando(
                "INSERT INTO fact.CorrelativoAsiento (Anio, Mes, Origen, Ultimo) VALUES (@anio, @mes, @origen, 1);");
            siembra.Parameters.AddWithValue("@anio", anio);
            siembra.Parameters.AddWithValue("@mes", mes);
            siembra.Parameters.AddWithValue("@origen", origen);
            await siembra.ExecuteNonQueryAsync(ct);
            return 1;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return await AsignarCorrelativoAsync(anio, mes, origen, ct);
        }
    }

    public async Task RegistrarAuditoriaAsync(EntradaAuditoria entrada, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            INSERT INTO fact.AuditoriaCorreccion
                (EntidadTipo, EntidadId, Accion, Campo, ValorOriginal, ValorNuevo, Motivo, UsuarioId, OcurridoEn)
            VALUES
                (@entidadTipo, @entidadId, @accion, @campo, @valorOriginal, @valorNuevo, @motivo, @usuarioId, @ocurridoEn);
            """);
        command.Parameters.AddWithValue("@entidadTipo", entrada.EntidadTipo);
        command.Parameters.AddWithValue("@entidadId", entrada.EntidadId);
        command.Parameters.AddWithValue("@accion", entrada.Accion);
        command.Parameters.AddWithValue("@campo", (object?)entrada.Campo ?? DBNull.Value);
        command.Parameters.AddWithValue("@valorOriginal", (object?)entrada.ValorOriginal ?? DBNull.Value);
        command.Parameters.AddWithValue("@valorNuevo", (object?)entrada.ValorNuevo ?? DBNull.Value);
        command.Parameters.AddWithValue("@motivo", (object?)entrada.Motivo ?? DBNull.Value);
        command.Parameters.AddWithValue("@usuarioId", entrada.UsuarioId);
        command.Parameters.AddWithValue("@ocurridoEn", entrada.OcurridoEn.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct);
    }

    // outbox-mensajeria (BACKLOG #14, design D3) -- mapa de aplicabilidad Tipo -> destinos,
    // fuente ADR 0004: "Solo sincroniza Drive: los adjuntos no son un dato del dashboard" (:59-60)
    // y "un evento que no aplica a un destino -DOCUMENTACION_ACTUALIZADA no toca Sheets- se marca
    // aplicado sin avanzar la secuencia de ese destino" (:177-178). TELEGRAM/CORREO (CK_...Integracion)
    // son canales de notificación de errores (#17, BACKLOG), no destinos sincronizados por FacturaId
    // (ADR 0004 "Clave de sincronización con los destinos externos" solo lista Sheets y Drive) -- este
    // fan-out no escribe filas para ellos; #17 tiene su propio punto de emisión, fuera de #14.
    // Vive en Infrastructure, no en Core: D3 rechaza explícitamente un mapa en Core porque
    // "fact_worker" no tiene INSERT en esta tabla y el mapa filtraría conocimiento de destinos hacia
    // el núcleo contable.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DestinosAplicables =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["FACTURA_VALIDADA"] = new[] { "DRIVE", "SHEETS" },
            ["FACTURA_CORREGIDA"] = new[] { "DRIVE", "SHEETS" },
            ["ASIENTO_CORREGIDO"] = new[] { "DRIVE", "SHEETS" },
            ["ASIENTO_ANULADO"] = new[] { "DRIVE", "SHEETS" },
            ["DOCUMENTACION_ACTUALIZADA"] = new[] { "DRIVE" },
        };

    public async Task EmitirOutboxAsync(string tipo, long facturaId, string payload, CancellationToken ct)
    {
        if (!_emitidosEnEstaTx.Add((tipo, facturaId)))
        {
            throw new InvalidOperationException(
                $"Ya se emitió un evento '{tipo}' para la factura {facturaId} en esta transacción.");
        }

        if (!DestinosAplicables.TryGetValue(tipo, out var destinos))
        {
            // Fail-loud: un Tipo sin entrada en el mapa es un evento nuevo que design D3 no
            // clasificó todavía -- mejor romper la transacción (rollback) que emitirlo sin fan-out.
            throw new InvalidOperationException(
                $"Tipo de evento '{tipo}' no tiene destinos aplicables definidos en el mapa D3.");
        }

        await using var command = CrearComando(
            """
            INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia)
            OUTPUT inserted.OutboxEventId
            VALUES (@tipo, @facturaId, @payload, NEXT VALUE FOR fact.SeqOutbox);
            """);
        command.Parameters.AddWithValue("@tipo", tipo);
        command.Parameters.AddWithValue("@facturaId", facturaId);
        command.Parameters.AddWithValue("@payload", payload);
        var outboxEventId = (long)(await command.ExecuteScalarAsync(ct))!;

        foreach (var destino in destinos)
        {
            await using var fanOut = CrearComando(
                """
                INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion)
                VALUES (@outboxEventId, @integracion);
                """);
            fanOut.Parameters.AddWithValue("@outboxEventId", outboxEventId);
            fanOut.Parameters.AddWithValue("@integracion", destino);
            await fanOut.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>PR 5 -- delega en el <see cref="ITipoCambioRepository"/> ya existente (item #3/#11);
    /// abre su propia <c>SqlConnection</c> (no la de esta unidad de trabajo): <c>fact.TipoCambio</c>
    /// nunca se escribe desde este flujo, así que no necesita compartir la transacción de negocio.</summary>
    public async Task<bool> ExisteTipoCambioVigenteAsync(DateOnly fecha, CancellationToken ct) =>
        await _tipoCambioRepository.ObtenerVigenteAsync(fecha, ct) is ResultadoTipoCambio.Vigente;

    public async Task CommitAsync(CancellationToken ct)
    {
        await _transaction.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_committed)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch (InvalidOperationException)
            {
                // La conexión ya se cerró/la transacción ya terminó -- nada que revertir.
            }
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // --- PR 2 additions: contrato factura-shaped (IUnidadDeTrabajo.cs) ---

    public async Task<FacturaPersistida?> CargarFacturaAsync(long facturaId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT FacturaId, Estado, ProveedorCodigo, RucProveedor, TipoComprobante, Numero, TotalOrig,
                   Moneda, FechaEmision, Motivo, Afectacion, Version,
                   EsProveedorGenerico, PosibleDuplicado, TieneCamposNoExtraidos, AfectacionMixta,
                   IgvOrig, Glosa, CamposNoExtraidos
            FROM fact.Factura
            WHERE FacturaId = @facturaId;
            """);
        command.Parameters.AddWithValue("@facturaId", facturaId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new FacturaPersistida(
            FacturaId: reader.GetInt64(0),
            Estado: reader.GetString(1).TrimEnd(),
            ProveedorCodigo: reader.GetString(2).TrimEnd(),
            RucProveedor: reader.IsDBNull(3) ? null : reader.GetString(3).TrimEnd(),
            TipoComprobante: reader.GetString(4).TrimEnd(),
            Numero: reader.IsDBNull(5) ? null : reader.GetString(5).TrimEnd(),
            TotalOrig: reader.GetDecimal(6),
            Moneda: reader.GetString(7).TrimEnd(),
            FechaEmision: DateOnly.FromDateTime(reader.GetDateTime(8)),
            Motivo: reader.IsDBNull(9) ? null : reader.GetInt32(9),
            Afectacion: reader.IsDBNull(10) ? null : reader.GetString(10).TrimEnd(),
            Version: (byte[])reader[11],
            EsProveedorGenerico: reader.GetBoolean(12),
            PosibleDuplicado: reader.GetBoolean(13),
            TieneCamposNoExtraidos: reader.GetBoolean(14),
            AfectacionMixta: reader.IsDBNull(15) ? null : reader.GetBoolean(15),
            // BACKLOG #19 (design D1/D8) -- IgvOrig nullable; Glosa (schema 021); CamposNoExtraidos
            // CSV promovido tal cual (D8: NULL = factura pre-021 -> la SPA cae al bool coarse).
            IgvOrig: reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            Glosa: reader.IsDBNull(17) ? null : reader.GetString(17),
            CamposNoExtraidos: reader.IsDBNull(18) ? null : PartirCamposNoExtraidos(reader.GetString(18)));
    }

    // BACKLOG #19 (design D8) -- fact.Factura.CamposNoExtraidos es un CSV promovido tal cual desde
    // fact.InboxEvent.CamposNoExtraidos (SqlPromocionRepository escribe string.Join(",", ...)).
    private static IReadOnlyList<string> PartirCamposNoExtraidos(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<ResultadoEscritura> GuardarFacturaAsync(
        long id, byte[] versionEsperada, FacturaPersistida factura, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            UPDATE fact.Factura
            SET Estado = @estado, ProveedorCodigo = @proveedorCodigo, RucProveedor = @rucProveedor,
                TotalOrig = @totalOrig, Moneda = @moneda, FechaEmision = @fechaEmision, Motivo = @motivo,
                Afectacion = @afectacion, TipoComprobante = @tipoComprobante, Numero = @numero,
                IgvOrig = @igvOrig, Glosa = @glosa
            WHERE FacturaId = @id AND Version = @versionEsperada;
            """);
        command.Parameters.AddWithValue("@estado", factura.Estado);
        command.Parameters.AddWithValue("@proveedorCodigo", factura.ProveedorCodigo);
        command.Parameters.AddWithValue("@rucProveedor", (object?)factura.RucProveedor ?? DBNull.Value);
        command.Parameters.AddWithValue("@totalOrig", factura.TotalOrig);
        command.Parameters.AddWithValue("@moneda", factura.Moneda);
        command.Parameters.AddWithValue("@fechaEmision", factura.FechaEmision.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@motivo", (object?)factura.Motivo ?? DBNull.Value);
        command.Parameters.AddWithValue("@afectacion", (object?)factura.Afectacion ?? DBNull.Value);
        // BACKLOG #19 (design D1/D7) -- IgvOrig es parte del par atomico base/IGV; Glosa (schema 021)
        // solo editable en PENDIENTE_VALIDACION (D2, ya validado por ValidacionDeCorreccion).
        // CamposNoExtraidos NO va aqui: es un hecho de extraccion inmutable (D8).
        command.Parameters.AddWithValue("@igvOrig", (object?)factura.IgvOrig ?? DBNull.Value);
        command.Parameters.AddWithValue("@glosa", (object?)factura.Glosa ?? DBNull.Value);
        // BACKLOG #18 PR5 (api-facturas delta) -- el SET de arriba OMITIA ambas columnas: un PATCH de
        // tipoComprobante/numero devolvia 200 y NO persistia nada. Numero NULL nunca llega aqui via
        // PATCH (CorreccionFactura.Numero == null => "no se toca", AplicarCorreccion no lo copia).
        command.Parameters.AddWithValue("@tipoComprobante", factura.TipoComprobante);
        command.Parameters.AddWithValue("@numero", (object?)factura.Numero ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@versionEsperada", versionEsperada);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        if (filasAfectadas > 0)
        {
            return ResultadoEscritura.Aplicado;
        }

        await using var verificacion = CrearComando("SELECT COUNT(*) FROM fact.Factura WHERE FacturaId = @id;");
        verificacion.Parameters.AddWithValue("@id", id);
        var existe = (int)(await verificacion.ExecuteScalarAsync(ct))! > 0;

        return existe ? ResultadoEscritura.VersionEnConflicto : ResultadoEscritura.NoEncontrado;
    }

    // --- diseno-visual-spa-item-12 (design D10) addition: escritura CAS dedicada de
    // AfectacionMixta -- GuardarFacturaAsync's UPDATE de arriba deliberadamente no la toca. ---

    public async Task<ResultadoEscritura> ConfirmarAfectacionAsync(
        long facturaId, byte[] versionEsperada, bool esMixta, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            UPDATE fact.Factura
            SET AfectacionMixta = @afectacionMixta
            WHERE FacturaId = @id AND Version = @versionEsperada;
            """);
        command.Parameters.AddWithValue("@afectacionMixta", esMixta);
        command.Parameters.AddWithValue("@id", facturaId);
        command.Parameters.AddWithValue("@versionEsperada", versionEsperada);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        if (filasAfectadas > 0)
        {
            return ResultadoEscritura.Aplicado;
        }

        await using var verificacion = CrearComando("SELECT COUNT(*) FROM fact.Factura WHERE FacturaId = @id;");
        verificacion.Parameters.AddWithValue("@id", facturaId);
        var existe = (int)(await verificacion.ExecuteScalarAsync(ct))! > 0;

        return existe ? ResultadoEscritura.VersionEnConflicto : ResultadoEscritura.NoEncontrado;
    }

    // --- outbox-mensajeria (BACKLOG #14, design D10 / ADR 0020 decisión 5) addition ---
    //
    // DEVIACIÓN DOCUMENTADA (apply batch 1 / Phase 1): esta implementación se adelanta desde
    // tasks.md 3.1 -- SIN su prueba de integración de esquema real dedicada todavía (esa prueba
    // exige SQL Server real y vive en Phase 3 de este mismo cambio). Se implementa aquí, ahora,
    // porque extender IUnidadDeTrabajo (task 1.4) es un cambio de interfaz que rompe la compilación
    // de SqlUnidadDeTrabajo en TODO el resto de la solución -- dejarlo sin cuerpo bloquearía cada
    // prueba de cada otro ítem, no solo las de #14. El SQL es literal, verbatim de design D10; la
    // prueba de integración de Phase 3 (SqlUnidadDeTrabajoFacturaTests.cs) la ejercita contra
    // esquema real cuando ese lote corra.

    public async Task<TransicionEstadoFactura> MarcarFacturaValidadaAsync(long facturaId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            UPDATE fact.Factura SET Estado = 'VALIDADA'
            WHERE FacturaId = @id AND Estado = 'PENDIENTE_VALIDACION';
            """);
        command.Parameters.AddWithValue("@id", facturaId);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        if (filasAfectadas > 0)
        {
            return TransicionEstadoFactura.Aplicada;
        }

        // @@ROWCOUNT = 0 -- design D10: re-SELECT para distinguir "ya VALIDADA" (reconfirmación
        // tras reabrir, D1) de cualquier otro estado (hoy: DESCARTADA), que es terminal (OQ5).
        await using var verificacion = CrearComando("SELECT Estado FROM fact.Factura WHERE FacturaId = @id;");
        verificacion.Parameters.AddWithValue("@id", facturaId);
        var estadoActual = (string?)await verificacion.ExecuteScalarAsync(ct);

        return estadoActual?.TrimEnd() == FacturaPersistida.Validada
            ? TransicionEstadoFactura.YaValidada
            : TransicionEstadoFactura.NoTransicionable;
    }

    // --- BACKLOG #19 (design D4/D6) additions ---

    public async Task<bool> ExisteIdentidadPreviaAsync(
        long facturaId, string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct) =>
        await ExisteDuplicadoNoResueltoAsync(facturaId, rucProveedor, tipoComprobante, numero, ct);

    public async Task ActualizarPosibleDuplicadoAsync(long facturaId, bool posibleDuplicado, CancellationToken ct)
    {
        // design D6 -- sin CAS: GuardarFacturaAsync ya hizo el CAS de Version en esta misma
        // transaccion. Recomputacion derivada de un indicador, no una correccion del usuario (D6:
        // sin fila de AuditoriaCorreccion).
        await using var command = CrearComando(
            "UPDATE fact.Factura SET PosibleDuplicado = @valor WHERE FacturaId = @id;");
        command.Parameters.AddWithValue("@valor", posibleDuplicado);
        command.Parameters.AddWithValue("@id", facturaId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ResultadoEscritura> ActualizarProyeccionEscalarAsync(
        long asientoContableId, decimal basePen, decimal igvPen, decimal netoPen, CancellationToken ct)
    {
        // design D4 -- escribe los tres escalares sobre el asiento BORRADOR vigente en la misma
        // transaccion del PATCH. Solo si sigue en BORRADOR; ROWVERSION se incrementa por el UPDATE.
        await using var command = CrearComando(
            """
            UPDATE fact.AsientoContable
            SET BasePEN = @basePen, IgvPEN = @igvPen, NetoPEN = @netoPen
            WHERE AsientoContableId = @id AND Estado = 'BORRADOR';
            """);
        command.Parameters.AddWithValue("@basePen", basePen);
        command.Parameters.AddWithValue("@igvPen", igvPen);
        command.Parameters.AddWithValue("@netoPen", netoPen);
        command.Parameters.AddWithValue("@id", asientoContableId);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        return filasAfectadas > 0 ? ResultadoEscritura.Aplicado : ResultadoEscritura.NoEncontrado;
    }

    public async Task<long?> ObtenerAsientoVigenteIdAsync(long facturaId, CancellationToken ct)
    {
        // UQ_Asiento_Vigente (005_negocio.sql): a lo sumo un asiento no ANULADO por factura.
        await using var command = CrearComando(
            """
            SELECT AsientoContableId
            FROM fact.AsientoContable
            WHERE FacturaId = @facturaId AND Estado <> 'ANULADO';
            """);
        command.Parameters.AddWithValue("@facturaId", facturaId);

        var resultado = await command.ExecuteScalarAsync(ct);
        return resultado is long id ? id : null;
    }

    // BACKLOG #24 (design A1/A3) -- hechos externos que SembradoDeAsiento (Core) necesita y que una
    // FacturaPersistida no lleva. fact.ProveedorAtributo y dbo.Motivo se leen DENTRO de la
    // transacción (una sola consulta); el tipo de cambio se resuelve en su propia conexión vía
    // ITipoCambioRepository -- mismo criterio que ExisteTipoCambioVigenteAsync (catálogo externo
    // read-only, ADR 0003, nunca escrito desde este flujo).
    public async Task<HechosDeComposicion> ResolverHechosDeComposicionAsync(long facturaId, CancellationToken ct)
    {
        bool esRelacionada;
        string? motivoDescripcion;
        string moneda;
        DateOnly fechaEmision;
        string proveedorCodigo;
        int? motivoCodigo;

        await using (var command = CrearComando(
            """
            SELECT ISNULL(pa.EsRelacionada, 0), m.motivo, f.Moneda, f.FechaEmision,
                   f.ProveedorCodigo, f.Motivo
            FROM fact.Factura f
            LEFT JOIN fact.ProveedorAtributo pa ON pa.ProveedorCodigo = f.ProveedorCodigo
            LEFT JOIN dbo.Motivo m ON m.codigo = f.Motivo
            WHERE f.FacturaId = @facturaId;
            """))
        {
            command.Parameters.AddWithValue("@facturaId", facturaId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException(
                    $"No existe fact.Factura {facturaId} al resolver los hechos de composición.");
            }

            esRelacionada = reader.GetBoolean(0);
            motivoDescripcion = reader.IsDBNull(1) ? null : reader.GetString(1).TrimEnd();
            moneda = reader.GetString(2).TrimEnd();
            fechaEmision = DateOnly.FromDateTime(reader.GetDateTime(3));
            proveedorCodigo = reader.GetString(4).TrimEnd();
            motivoCodigo = reader.IsDBNull(5) ? null : reader.GetInt32(5);
        }

        TipoCambioCongelado? tipoCambio = null;
        if (moneda != MonedaLocal
            && await _tipoCambioRepository.ObtenerVigenteAsync(fechaEmision, ct) is ResultadoTipoCambio.Vigente vigente)
        {
            tipoCambio = TipoCambioCongelado.DeTipoCambio(vigente.Valor);
        }

        // BACKLOG #24 Phase 4.1 -- si Program.cs cableó ServicioDeSugerencia, se resuelve la cuenta
        // de cargo por defecto (cascada REGLAS.md §3 sobre proveedor + motivo). Sin sugerencia o sin
        // el servicio inyectado -> null -> SembradoDeAsiento.Sembrar cae al placeholder (design A2).
        // El servicio abre sus propias conexiones (dbo.CuentaContable / dbo.Motivo /
        // fact.SugerenciaCuenta son lecturas de catálogo, nunca escritas desde esta transacción).
        CuentaContable? cuentaSugerida = null;
        if (_servicioDeSugerencia is not null)
        {
            var sugerencia = await _servicioDeSugerencia.SugerirParaFacturaAsync(proveedorCodigo, motivoCodigo, ct);
            if (sugerencia.Cuenta is not null)
            {
                cuentaSugerida = sugerencia.CandidatasVigentes
                    .FirstOrDefault(c => c.Cuenta == sugerencia.Cuenta.CuentaCodigo);
            }
        }

        return new HechosDeComposicion(esRelacionada, motivoDescripcion, tipoCambio, cuentaSugerida);
    }

    // BACKLOG #24 (design C1) -- lectura in-tx de dbo.CuentaContable por código exacto (grant
    // SELECT dbo.CuentaContable en 008). Mismo mapeo que SqlCuentaContableRepository.Map.
    public async Task<CuentaContable?> ObtenerCuentaContableAsync(string cuentaCodigo, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente
            FROM dbo.CuentaContable
            WHERE cuenta = @cuenta;
            """);
        command.Parameters.AddWithValue("@cuenta", cuentaCodigo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new CuentaContable(
            Cuenta: reader.GetString(0),
            Descripcion: reader.GetString(1),
            Nivel: reader.IsDBNull(2) ? null : reader.GetByte(2),
            CtaReflejaCodigo: reader.IsDBNull(3) ? null : reader.GetString(3),
            CtaPuenteCodigo: reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task<long> CrearAsientoBorradorAsync(
        long facturaId, AsientoContable asiento, CancellationToken ct)
    {
        // BACKLOG #24 (design B1) -- encabezado (escalares del motor) + N líneas ya compuestas, en
        // esta transacción. Las líneas NO pasan por AgregarLineaAsync (su CAS de encabezado espera
        // una Version que aquí nadie sostiene -- la fila se acaba de crear).
        long asientoId;
        await using (var command = CrearComando(
            """
            INSERT INTO fact.AsientoContable
                (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado,
                 MotivoDescripcion, TipoCambioVenta, BasePEN, IgvPEN, NetoPEN)
            OUTPUT inserted.AsientoContableId
            VALUES (@facturaId, '02', @proveedorCodigo, @fechaContable, 'BORRADOR',
                    @motivoDescripcion, @tipoCambioVenta, @basePen, @igvPen, @netoPen);
            """))
        {
            command.Parameters.AddWithValue("@facturaId", facturaId);
            command.Parameters.AddWithValue("@proveedorCodigo", asiento.ProveedorCodigo);
            command.Parameters.AddWithValue("@fechaContable", asiento.FechaContable.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@motivoDescripcion", (object?)asiento.MotivoDescripcion ?? DBNull.Value);
            command.Parameters.AddWithValue("@tipoCambioVenta", (object?)asiento.TipoCambioVenta ?? DBNull.Value);
            command.Parameters.AddWithValue("@basePen", asiento.BasePEN);
            command.Parameters.AddWithValue("@igvPen", asiento.IgvPEN);
            command.Parameters.AddWithValue("@netoPen", asiento.NetoPEN);

            asientoId = (long)(await command.ExecuteScalarAsync(ct))!;
        }

        await InsertarLineasAsync(asientoId, asiento.Lineas, ct);
        return asientoId;
    }

    public async Task<ResultadoEscritura> ReemplazarLineasAsync(
        long asientoContableId, byte[] versionEsperada, AsientoContable asiento, CancellationToken ct)
    {
        // BACKLOG #24 (design B2) -- CAS de encabezado -> DELETE detalle -> UPDATE escalares
        // (nunca Estado/NumeroAsiento) -> re-INSERT. TocarEncabezadoAsync ya incrementa Version.
        var bump = await TocarEncabezadoAsync(asientoContableId, versionEsperada, ct);
        if (bump != ResultadoEscritura.Aplicado)
        {
            return bump;
        }

        await using (var borrado = CrearComando(
            "DELETE FROM fact.AsientoContableDetalle WHERE AsientoContableId = @id;"))
        {
            borrado.Parameters.AddWithValue("@id", asientoContableId);
            await borrado.ExecuteNonQueryAsync(ct);
        }

        await using (var encabezado = CrearComando(
            """
            UPDATE fact.AsientoContable
            SET MotivoDescripcion = @motivoDescripcion, TipoCambioVenta = @tipoCambioVenta,
                BasePEN = @basePen, IgvPEN = @igvPen, NetoPEN = @netoPen, FechaContable = @fechaContable
            WHERE AsientoContableId = @id;
            """))
        {
            encabezado.Parameters.AddWithValue("@motivoDescripcion", (object?)asiento.MotivoDescripcion ?? DBNull.Value);
            encabezado.Parameters.AddWithValue("@tipoCambioVenta", (object?)asiento.TipoCambioVenta ?? DBNull.Value);
            encabezado.Parameters.AddWithValue("@basePen", asiento.BasePEN);
            encabezado.Parameters.AddWithValue("@igvPen", asiento.IgvPEN);
            encabezado.Parameters.AddWithValue("@netoPen", asiento.NetoPEN);
            encabezado.Parameters.AddWithValue("@fechaContable", asiento.FechaContable.ToDateTime(TimeOnly.MinValue));
            encabezado.Parameters.AddWithValue("@id", asientoContableId);
            await encabezado.ExecuteNonQueryAsync(ct);
        }

        await InsertarLineasAsync(asientoContableId, asiento.Lineas, ct);
        return ResultadoEscritura.Aplicado;
    }

    private async Task InsertarLineasAsync(long asientoContableId, IReadOnlyList<LineaAsiento> lineas, CancellationToken ct)
    {
        foreach (var linea in lineas)
        {
            await using var command = CrearComando(
                """
                INSERT INTO fact.AsientoContableDetalle
                    (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion,
                     CtaReflejaCodigo, CtaPuenteCodigo, SinCuenta)
                VALUES
                    (@asientoId, @orden, @bloque, @tipo, @debe, @haber, @cuentaCodigo, @cuentaDescripcion,
                     @ctaRefleja, @ctaPuente, @sinCuenta);
                """);
            AgregarParametrosDeLinea(command, asientoContableId, linea);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<long> RegistrarAdjuntoAsync(AdjuntoManual adjunto, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            INSERT INTO fact.AdjuntoManual
                (FacturaId, NombreArchivo, RutaRelativa, MimeType, TamanoBytes, SubidoPorUsuarioId, SubidoEn)
            OUTPUT inserted.AdjuntoManualId
            VALUES (@facturaId, @nombreArchivo, @rutaRelativa, @mimeType, @tamanoBytes, @subidoPor, @subidoEn);
            """);
        command.Parameters.AddWithValue("@facturaId", adjunto.FacturaId);
        command.Parameters.AddWithValue("@nombreArchivo", adjunto.NombreArchivo);
        command.Parameters.AddWithValue("@rutaRelativa", adjunto.RutaRelativa);
        command.Parameters.AddWithValue("@mimeType", adjunto.MimeType);
        command.Parameters.AddWithValue("@tamanoBytes", adjunto.TamanoBytes);
        command.Parameters.AddWithValue("@subidoPor", adjunto.SubidoPorUsuarioId);
        command.Parameters.AddWithValue("@subidoEn", adjunto.SubidoEn.UtcDateTime);

        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<ResultadoEscritura> EliminarAdjuntoAsync(
        long adjuntoManualId, long facturaId, DateTimeOffset eliminadoEn, long eliminadoPorUsuarioId,
        string motivoEliminacion, CancellationToken ct)
    {
        // CK_AdjuntoManual_Eliminacion: los tres campos de borrado lógico juntos, nunca por
        // separado -- "EliminadoEn IS NULL" en el WHERE evita un segundo borrado del mismo adjunto.
        await using var command = CrearComando(
            """
            UPDATE fact.AdjuntoManual
            SET EliminadoEn = @eliminadoEn, EliminadoPorUsuarioId = @eliminadoPor, MotivoEliminacion = @motivo
            WHERE AdjuntoManualId = @id AND FacturaId = @facturaId AND EliminadoEn IS NULL;
            """);
        command.Parameters.AddWithValue("@eliminadoEn", eliminadoEn.UtcDateTime);
        command.Parameters.AddWithValue("@eliminadoPor", eliminadoPorUsuarioId);
        command.Parameters.AddWithValue("@motivo", motivoEliminacion);
        command.Parameters.AddWithValue("@id", adjuntoManualId);
        command.Parameters.AddWithValue("@facturaId", facturaId);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        return filasAfectadas > 0 ? ResultadoEscritura.Aplicado : ResultadoEscritura.NoEncontrado;
    }

    // --- PR 3 (Phase 3, BACKLOG #12) additions: lectura read-only para lista unificada / visor
    // (IUnidadDeTrabajo.cs). Ningún SELECT contra fact.DocumentoRecibido en este archivo. ---

    public async Task<IReadOnlyList<DocumentoFacturaPersistido>> CargarDocumentosFacturaAsync(long facturaId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT DocumentoFacturaId, FacturaId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes, CreadoEn
            FROM fact.DocumentoFactura
            WHERE FacturaId = @facturaId
            ORDER BY DocumentoFacturaId;
            """);
        command.Parameters.AddWithValue("@facturaId", facturaId);

        var documentos = new List<DocumentoFacturaPersistido>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documentos.Add(MapearDocumentoFactura(reader));
        }

        return documentos;
    }

    public async Task<IReadOnlyList<AdjuntoManual>> CargarAdjuntosDeFacturaAsync(long facturaId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT AdjuntoManualId, FacturaId, NombreArchivo, RutaRelativa, MimeType, TamanoBytes,
                   SubidoPorUsuarioId, SubidoEn, EliminadoEn
            FROM fact.AdjuntoManual
            WHERE FacturaId = @facturaId AND EliminadoEn IS NULL
            ORDER BY AdjuntoManualId;
            """);
        command.Parameters.AddWithValue("@facturaId", facturaId);

        var adjuntos = new List<AdjuntoManual>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            adjuntos.Add(MapearAdjunto(reader));
        }

        return adjuntos;
    }

    public async Task<DocumentoFacturaPersistido?> CargarDocumentoFacturaPorIdAsync(long documentoFacturaId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT DocumentoFacturaId, FacturaId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes, CreadoEn
            FROM fact.DocumentoFactura
            WHERE DocumentoFacturaId = @id;
            """);
        command.Parameters.AddWithValue("@id", documentoFacturaId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapearDocumentoFactura(reader) : null;
    }

    public async Task<AdjuntoManual?> CargarAdjuntoPorIdAsync(long adjuntoManualId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT AdjuntoManualId, FacturaId, NombreArchivo, RutaRelativa, MimeType, TamanoBytes,
                   SubidoPorUsuarioId, SubidoEn, EliminadoEn
            FROM fact.AdjuntoManual
            WHERE AdjuntoManualId = @id AND EliminadoEn IS NULL;
            """);
        command.Parameters.AddWithValue("@id", adjuntoManualId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapearAdjunto(reader) : null;
    }

    private static DocumentoFacturaPersistido MapearDocumentoFactura(SqlDataReader reader) => new(
        DocumentoFacturaId: reader.GetInt64(0),
        FacturaId: reader.GetInt64(1),
        NombreArchivo: reader.GetString(2).TrimEnd(),
        MimeType: reader.GetString(3).TrimEnd(),
        RutaRelativa: reader.GetString(4).TrimEnd(),
        TamanoBytes: reader.GetInt64(5),
        CreadoEn: new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));

    private static AdjuntoManual MapearAdjunto(SqlDataReader reader) => new(
        AdjuntoManualId: reader.GetInt64(0),
        FacturaId: reader.GetInt64(1),
        NombreArchivo: reader.GetString(2).TrimEnd(),
        RutaRelativa: reader.GetString(3).TrimEnd(),
        MimeType: reader.GetString(4).TrimEnd(),
        TamanoBytes: reader.GetInt64(5),
        SubidoPorUsuarioId: reader.GetInt64(6),
        SubidoEn: new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)),
        EliminadoEn: reader.IsDBNull(8) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)));

    // --- PR 3 (Phase 3) additions: líneas por LineaId (IUnidadDeTrabajo.cs) ---

    public async Task<IReadOnlyList<LineaPersistida>> CargarLineasPersistidasAsync(long asientoContableId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT LineaId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion, CtaReflejaCodigo, CtaPuenteCodigo
            FROM fact.AsientoContableDetalle
            WHERE AsientoContableId = @asientoId
            ORDER BY Orden;
            """);
        command.Parameters.AddWithValue("@asientoId", asientoContableId);

        var lineas = new List<LineaPersistida>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lineas.Add(new LineaPersistida(
                LineaId: reader.GetInt64(0),
                Linea: new LineaAsiento(
                    Orden: reader.GetInt16(1),
                    Bloque: reader.GetString(2).TrimEnd() == "PRINCIPAL" ? Bloque.Principal : Bloque.Destino,
                    Tipo: reader.GetString(3).TrimEnd() == "D" ? TipoLinea.D : TipoLinea.H,
                    Debe: reader.GetDecimal(4),
                    Haber: reader.GetDecimal(5),
                    CuentaCodigo: reader.IsDBNull(6) ? null : reader.GetString(6).TrimEnd(),
                    CuentaDescripcion: reader.IsDBNull(7) ? null : reader.GetString(7),
                    CtaReflejaCodigo: reader.IsDBNull(8) ? null : reader.GetString(8).TrimEnd(),
                    CtaPuenteCodigo: reader.IsDBNull(9) ? null : reader.GetString(9).TrimEnd())));
        }

        return lineas;
    }

    public async Task<ResultadoLinea> AgregarLineaAsync(
        long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct)
    {
        var bump = await TocarEncabezadoAsync(asientoContableId, versionEsperada, ct);
        if (bump != ResultadoEscritura.Aplicado)
        {
            return new ResultadoLinea(bump, null);
        }

        await using var command = CrearComando(
            """
            INSERT INTO fact.AsientoContableDetalle
                (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion, CtaReflejaCodigo,
                 CtaPuenteCodigo, SinCuenta)
            OUTPUT inserted.LineaId
            VALUES
                (@asientoId, @orden, @bloque, @tipo, @debe, @haber, @cuentaCodigo, @cuentaDescripcion, @ctaRefleja,
                 @ctaPuente, @sinCuenta);
            """);
        AgregarParametrosDeLinea(command, asientoContableId, linea);

        var lineaId = (long)(await command.ExecuteScalarAsync(ct))!;
        return new ResultadoLinea(ResultadoEscritura.Aplicado, lineaId);
    }

    public async Task<ResultadoEscritura> ActualizarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct)
    {
        var bump = await TocarEncabezadoAsync(asientoContableId, versionEsperada, ct);
        if (bump != ResultadoEscritura.Aplicado)
        {
            return bump;
        }

        await using var command = CrearComando(
            """
            UPDATE fact.AsientoContableDetalle
            SET Orden = @orden, Bloque = @bloque, Tipo = @tipo, Debe = @debe, Haber = @haber,
                CuentaCodigo = @cuentaCodigo, CuentaDescripcion = @cuentaDescripcion, CtaReflejaCodigo = @ctaRefleja,
                CtaPuenteCodigo = @ctaPuente, SinCuenta = @sinCuenta
            WHERE LineaId = @lineaId AND AsientoContableId = @asientoId;
            """);
        AgregarParametrosDeLinea(command, asientoContableId, linea);
        command.Parameters.AddWithValue("@lineaId", lineaId);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        return filasAfectadas > 0 ? ResultadoEscritura.Aplicado : ResultadoEscritura.NoEncontrado;
    }

    public async Task<ResultadoEscritura> EliminarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, CancellationToken ct)
    {
        var bump = await TocarEncabezadoAsync(asientoContableId, versionEsperada, ct);
        if (bump != ResultadoEscritura.Aplicado)
        {
            return bump;
        }

        await using var command = CrearComando(
            "DELETE FROM fact.AsientoContableDetalle WHERE LineaId = @lineaId AND AsientoContableId = @asientoId;");
        command.Parameters.AddWithValue("@lineaId", lineaId);
        command.Parameters.AddWithValue("@asientoId", asientoContableId);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        return filasAfectadas > 0 ? ResultadoEscritura.Aplicado : ResultadoEscritura.NoEncontrado;
    }

    /// <summary>design D2 -- CAS contra <c>fact.AsientoContable.Version</c> compartido por los tres
    /// comandos de línea: la fila que cambia es <c>fact.AsientoContableDetalle</c>, pero el ETag que
    /// el cliente sostiene es el del ENCABEZADO (una sola superficie de concurrencia por asiento).
    /// <c>SET Glosa = Glosa</c> es un no-op deliberado -- ninguna columna de negocio cambia de valor,
    /// pero SQL Server actualiza <c>ROWVERSION</c> en cualquier <c>UPDATE</c> que toque la fila,
    /// tenga o no cambios reales (mismo idioma que <see cref="GuardarAsientoAsync"/>'s CAS, aplicado
    /// aquí sin escribir columnas de negocio -- <c>Glosa</c> no la usa ningún flujo de #11 todavía).</summary>
    private async Task<ResultadoEscritura> TocarEncabezadoAsync(long asientoContableId, byte[] versionEsperada, CancellationToken ct)
    {
        await using var command = CrearComando(
            "UPDATE fact.AsientoContable SET Glosa = Glosa WHERE AsientoContableId = @id AND Version = @versionEsperada;");
        command.Parameters.AddWithValue("@id", asientoContableId);
        command.Parameters.AddWithValue("@versionEsperada", versionEsperada);

        var filasAfectadas = await command.ExecuteNonQueryAsync(ct);
        if (filasAfectadas > 0)
        {
            return ResultadoEscritura.Aplicado;
        }

        await using var verificacion = CrearComando("SELECT COUNT(*) FROM fact.AsientoContable WHERE AsientoContableId = @id;");
        verificacion.Parameters.AddWithValue("@id", asientoContableId);
        var existe = (int)(await verificacion.ExecuteScalarAsync(ct))! > 0;

        return existe ? ResultadoEscritura.VersionEnConflicto : ResultadoEscritura.NoEncontrado;
    }

    private static void AgregarParametrosDeLinea(SqlCommand command, long asientoContableId, LineaAsiento linea)
    {
        command.Parameters.AddWithValue("@asientoId", asientoContableId);
        command.Parameters.AddWithValue("@orden", linea.Orden);
        command.Parameters.AddWithValue("@bloque", linea.Bloque == Bloque.Principal ? "PRINCIPAL" : "DESTINO");
        command.Parameters.AddWithValue("@tipo", linea.Tipo == TipoLinea.D ? "D" : "H");
        command.Parameters.AddWithValue("@debe", linea.Debe);
        command.Parameters.AddWithValue("@haber", linea.Haber);
        command.Parameters.AddWithValue("@cuentaCodigo", (object?)linea.CuentaCodigo ?? DBNull.Value);
        command.Parameters.AddWithValue("@cuentaDescripcion", (object?)linea.CuentaDescripcion ?? DBNull.Value);
        command.Parameters.AddWithValue("@ctaRefleja", (object?)linea.CtaReflejaCodigo ?? DBNull.Value);
        command.Parameters.AddWithValue("@ctaPuente", (object?)linea.CtaPuenteCodigo ?? DBNull.Value);
        command.Parameters.AddWithValue("@sinCuenta", linea.CuentaCodigo is null);
    }

    private SqlCommand CrearComando(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private async Task<IReadOnlyList<LineaAsiento>> CargarLineasAsync(long asientoId, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion, CtaReflejaCodigo, CtaPuenteCodigo
            FROM fact.AsientoContableDetalle
            WHERE AsientoContableId = @asientoId
            ORDER BY Orden;
            """);
        command.Parameters.AddWithValue("@asientoId", asientoId);

        var lineas = new List<LineaAsiento>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lineas.Add(new LineaAsiento(
                Orden: reader.GetInt16(0),
                Bloque: reader.GetString(1).TrimEnd() == "PRINCIPAL" ? Bloque.Principal : Bloque.Destino,
                Tipo: reader.GetString(2).TrimEnd() == "D" ? TipoLinea.D : TipoLinea.H,
                Debe: reader.GetDecimal(3),
                Haber: reader.GetDecimal(4),
                CuentaCodigo: reader.IsDBNull(5) ? null : reader.GetString(5).TrimEnd(),
                CuentaDescripcion: reader.IsDBNull(6) ? null : reader.GetString(6),
                CtaReflejaCodigo: reader.IsDBNull(7) ? null : reader.GetString(7).TrimEnd(),
                CtaPuenteCodigo: reader.IsDBNull(8) ? null : reader.GetString(8).TrimEnd()));
        }

        return lineas;
    }

    private async Task<bool> ExisteDuplicadoNoResueltoAsync(
        long facturaId, string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct)
    {
        await using var command = CrearComando(
            """
            SELECT COUNT(*)
            FROM fact.Factura
            WHERE FacturaId <> @facturaId
              AND Estado <> 'DESCARTADA'
              AND TipoComprobante = @tipoComprobante
              AND ((@rucProveedor IS NULL AND RucProveedor IS NULL) OR RucProveedor = @rucProveedor)
              AND ((@numero IS NULL AND Numero IS NULL) OR Numero = @numero);
            """);
        command.Parameters.AddWithValue("@facturaId", facturaId);
        command.Parameters.AddWithValue("@tipoComprobante", tipoComprobante);
        command.Parameters.AddWithValue("@rucProveedor", (object?)rucProveedor ?? DBNull.Value);
        command.Parameters.AddWithValue("@numero", (object?)numero ?? DBNull.Value);

        var count = (int)(await command.ExecuteScalarAsync(ct))!;
        return count > 0;
    }

    private static Afectacion MapearAfectacion(string? codigo) => codigo switch
    {
        "GRAVADA" => Afectacion.Gravada,
        "EXONERADA" => Afectacion.Exonerada,
        "INAFECTA" => Afectacion.Inafecta,
        _ => Afectacion.Gravada,
    };

    // BACKLOG #18 PR5 -- el conjunto de codigos {01,03,07} vive una sola vez, en
    // SmartNet.Contable.Core.CodigoComprobante; la validacion de correcciones lo comparte.
    private static TipoComprobante MapearTipoComprobante(string codigo) => CodigoComprobante.Convertir(codigo);
}
