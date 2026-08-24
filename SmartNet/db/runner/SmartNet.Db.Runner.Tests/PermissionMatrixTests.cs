using Microsoft.Data.SqlClient;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Phase 3 (task 3.1/3.2/3.3) — ADR 0019 level-2 matrix suite. One test per spec.md requirement
/// under capability `db-permissions`, run against the real `008_usuarios_y_permisos.sql` applied
/// by the real runner over a throwaway `fact_test_&lt;id&gt;` database, evaluated via
/// `EXECUTE AS USER` / `REVERT` (design.md, "How the ADR 0019 level-2 tests reach a database").
///
/// RED/GREEN granularity actually executed (documented per the hard constraint on TDD
/// compression): all 18 tests below were written together against a database with 001-007 applied
/// but no `008` — every "denied" assertion trivially passed (no GRANT exists without `008`, so the
/// engine already denies everything by default) while every "succeeds" assertion failed for the
/// right reason (permission denied, error 229, because no GRANT existed yet). That is RED. `008`
/// was then authored once and the same 18 tests re-run to GREEN. This mirrors the same
/// one-round-trip-per-direction compression already used and documented for Phase 2 (task 2.13's
/// note), for the same real-SQL-Server round-trip cost reason.
/// </summary>
public sealed class PermissionMatrixTests
{
    private const string UsrApi = "usr_api";
    private const string UsrWorker = "usr_worker";

    // ---------------------------------------------------------------------------------------
    // usr_api is denied SELECT on the Python-private tables.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrApi_IsDenied_SelectOnProcesamientoAndDatosExtraidos()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrApi, "SELECT COUNT(*) FROM fact.Procesamiento;");
        await AssertDenied(db, UsrApi, "SELECT COUNT(*) FROM fact.DatosExtraidos;");
    }

    // ---------------------------------------------------------------------------------------
    // usr_worker is denied writes to Factura and reads of the four cross-boundary tables.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrWorker_IsDenied_InsertAndUpdateOnFactura()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrWorker,
            "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) " +
            "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');");
        await AssertDenied(db, UsrWorker, "UPDATE fact.Factura SET Numero = Numero WHERE 1 = 0;");
    }

    [Fact]
    public async Task UsrWorker_IsDenied_AnyAccess_OnAsientoContableAsientoContableDetalleAdjuntoManualUsuario()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.AsientoContable;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.AsientoContableDetalle;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.AdjuntoManual;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.Usuario;");
    }

    // Coordinator-directed follow-up (item 1): the matrix suite had positive usr_api coverage on
    // the full eleven-table .NET-private bucket (ADR 0003: "negocio" + "satélites de datos
    // maestros" + "seguridad") but no negative usr_worker coverage on the six tables design.md's
    // first draft did not name for explicit DENY (AuditoriaCorreccion, FacturaExtraccion,
    // CorrelativoAsiento, and the three satellites). Behavioral denial already held for these
    // (absence of GRANT denies by default), so a plain EXECUTE AS USER assertion alone would not be
    // RED against the un-widened 008 — the first assertion below (explicit DENY state in
    // sys.database_permissions) is the one that actually flips from failing to passing when 008 is
    // widened; the second (behavioral EXECUTE AS USER) documents the same property the rest of this
    // suite already uses, for consistency, even though it does not by itself distinguish
    // absence-of-GRANT from explicit DENY.
    [Fact]
    public async Task UsrWorker_HasExplicitDeny_OnFullDotNetPrivateBucket()
    {
        await using var db = await MigratedDatabaseWithUsers();

        string[] fullDotNetPrivateBucket =
        [
            "Factura", "AsientoContable", "AsientoContableDetalle", "AdjuntoManual",
            "AuditoriaCorreccion", "FacturaExtraccion", "CorrelativoAsiento",
            "ProveedorAtributo", "MotivoAtributo", "SugerenciaCuenta", "Usuario"
        ];

        foreach (var table in fullDotNetPrivateBucket)
        {
            var denyCount = await db.ExecuteScalarAsync<int>(
                $"""
                 SELECT COUNT(*)
                 FROM sys.database_permissions perm
                 JOIN sys.database_principals prin ON perm.grantee_principal_id = prin.principal_id
                 JOIN sys.objects obj ON perm.major_id = obj.object_id AND perm.class = 1
                 WHERE prin.name = 'fact_worker' AND obj.name = '{table}' AND perm.state_desc = 'DENY';
                 """);
            Assert.True(denyCount > 0, $"Expected an explicit DENY for fact_worker on fact.{table}, found none.");
        }

        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.AuditoriaCorreccion;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.FacturaExtraccion;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.CorrelativoAsiento;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.ProveedorAtributo;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.MotivoAtributo;");
        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.SugerenciaCuenta;");
    }

    // ---------------------------------------------------------------------------------------
    // usr_api has full SELECT/INSERT/UPDATE on all eleven of its own private tables.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrApi_HasFullAccess_OnItsOwnPrivateTables()
    {
        await using var db = await MigratedDatabaseWithUsers();

        var usuarioId = await SeedUsuario(db);
        var facturaId = await SeedFactura(db);
        var asientoId = await SeedAsientoContable(db, facturaId);

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) " +
            "VALUES ('P00001', '01', 50.00, 'PEN', '2026-01-02');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.Factura;");
        await AssertSucceedsWrite(db, UsrApi, "UPDATE fact.Factura SET Numero = '0001' WHERE Numero IS NULL;");

        // A second, distinct Factura for the AsientoContable insert below: seeding a second
        // non-ANULADO AsientoContable for the SAME facturaId used by SeedAsientoContable above
        // would collide with UQ_Asiento_Vigente (spec.md, "at most one non-ANULADO entry per
        // invoice") — a genuine engine invariant, not a permission concern this test is about.
        var segundaFacturaId = await SeedFactura(db);
        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.AsientoContable (FacturaId, ProveedorCodigo, FechaContable) VALUES ({segundaFacturaId}, 'P00000', '2026-01-01');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.AsientoContable;");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.AsientoContable SET Glosa = 'test' WHERE AsientoContableId = {asientoId};");

        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe) " +
            $"VALUES ({asientoId}, 1, 'PRINCIPAL', 'D', 100.00);");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.AsientoContableDetalle;");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.AsientoContableDetalle SET Orden = 2 WHERE AsientoContableId = {asientoId};");

        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.AdjuntoManual (FacturaId, NombreArchivo, RutaRelativa, MimeType, TamanoBytes, " +
            $"SubidoPorUsuarioId, SubidoEn) VALUES ({facturaId}, 'a.pdf', '/a.pdf', 'application/pdf', 100, " +
            $"{usuarioId}, SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.AdjuntoManual;");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.AdjuntoManual SET NombreArchivo = 'b.pdf' WHERE FacturaId = {facturaId};");

        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.AuditoriaCorreccion (EntidadTipo, EntidadId, Accion, UsuarioId, OcurridoEn) " +
            $"VALUES ('FACTURA', {facturaId}, 'CORRECCION', {usuarioId}, SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.AuditoriaCorreccion;");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.AuditoriaCorreccion SET Motivo = 'x' WHERE EntidadId = {facturaId};");

        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.FacturaExtraccion (FacturaId, CampoNombre, ValorExtraido, Fuente) " +
            $"VALUES ({facturaId}, 'total', '50.00', 'XML');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.FacturaExtraccion;");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.FacturaExtraccion SET ValorExtraido = '51.00' WHERE FacturaId = {facturaId};");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.CorrelativoAsiento (Anio, Mes, Origen) VALUES (2026, 1, '02');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.CorrelativoAsiento;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.CorrelativoAsiento SET Ultimo = 1 WHERE Anio = 2026 AND Mes = 1 AND Origen = '02';");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.ProveedorAtributo (ProveedorCodigo) VALUES ('P00002');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.ProveedorAtributo;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.ProveedorAtributo SET EsRelacionada = 1 WHERE ProveedorCodigo = 'P00002';");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.MotivoAtributo (Motivo, OrigenLibro) VALUES (999, '02');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.MotivoAtributo;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.MotivoAtributo SET Activo = 0 WHERE Motivo = 999;");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.SugerenciaCuenta (ProveedorCodigo, Motivo, CuentaCodigo, UltimoUso) " +
            "VALUES ('P00002', 999, '601', SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.SugerenciaCuenta;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.SugerenciaCuenta SET Veces = 1 WHERE ProveedorCodigo = 'P00002';");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usuario.prueba', 'hash');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.Usuario;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.Usuario SET Activo = 0 WHERE NombreUsuario = 'usuario.prueba';");
    }

    // ---------------------------------------------------------------------------------------
    // usr_worker has full SELECT/INSERT/UPDATE on all six of its own private tables.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrWorker_HasFullAccess_OnItsOwnPrivateTables()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertSucceedsWrite(db, UsrWorker,
            "INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado) " +
            "VALUES ('msg-1', 'a@b.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.Email;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.Email SET Estado = 'PROCESADO' WHERE GmailMessageId = 'msg-1';");
        var emailId = await db.ExecuteScalarAsync<long>("SELECT MAX(EmailId) FROM fact.Email;");

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.DocumentoRecibido (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType, " +
            $"TamanoBytes, HashContenido, RutaRelativa, Estado) VALUES ({emailId}, 'msg-1', 'f.pdf', 'pdf', " +
            $"'application/pdf', 10, REPLICATE('a', 64), '/f.pdf', 'DESCARGADO');");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.DocumentoRecibido;");
        await AssertSucceedsWrite(db, UsrWorker,
            "UPDATE fact.DocumentoRecibido SET Estado = 'PROCESADO' WHERE GmailMessageId = 'msg-1';");
        var documentoId = await db.ExecuteScalarAsync<long>("SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido;");

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado) VALUES ({documentoId}, 'PENDIENTE');");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.Procesamiento;");
        await AssertSucceedsWrite(db, UsrWorker,
            $"UPDATE fact.Procesamiento SET Estado = 'EN_PROCESO' WHERE DocumentoRecibidoId = {documentoId};");
        var procesamientoId = await db.ExecuteScalarAsync<long>("SELECT MAX(ProcesamientoId) FROM fact.Procesamiento;");

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.DatosExtraidos (ProcesamientoId, Monto) VALUES ({procesamientoId}, 50.00);");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.DatosExtraidos;");
        await AssertSucceedsWrite(db, UsrWorker,
            $"UPDATE fact.DatosExtraidos SET Monto = 55.00 WHERE ProcesamientoId = {procesamientoId};");

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.ProcesamientoError (ProcesamientoId, Integracion, Mensaje, Clasificacion, OcurridoEn) " +
            $"VALUES ({procesamientoId}, 'GMAIL', 'error', 'TRANSITORIO', SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.ProcesamientoError;");
        await AssertSucceedsWrite(db, UsrWorker,
            $"UPDATE fact.ProcesamientoError SET Mensaje = 'otro' WHERE ProcesamientoId = {procesamientoId};");

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.ProcesamientoIntentos (ProcesamientoId, NumeroIntento, Resultado, OcurridoEn) " +
            $"VALUES ({procesamientoId}, 1, 'EXITO', SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.ProcesamientoIntentos;");
        await AssertSucceedsWrite(db, UsrWorker,
            $"UPDATE fact.ProcesamientoIntentos SET Resultado = 'FALLO' WHERE ProcesamientoId = {procesamientoId};");
    }

    // ---------------------------------------------------------------------------------------
    // Contract tables: asymmetric split grants (ADR 0003 / design.md item "Permission
    // consequence").
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrApi_CanInsertAndSelect_OutboxEvent_ButNotUpdate()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var facturaId = await SeedFactura(db);

        // A literal Secuencia, not NEXT VALUE FOR fact.SeqOutbox: advancing a SEQUENCE needs its
        // own UPDATE grant, which is outside ADR 0003's matrix and not this test's concern — the
        // column itself is a plain BIGINT with no uniqueness constraint.
        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) " +
            $"VALUES ('FACTURA_VALIDADA', {facturaId}, '{{}}', 1);");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.OutboxEvent;");
        await AssertDenied(db, UsrApi, "UPDATE fact.OutboxEvent SET Estado = 'COMPLETADO' WHERE 1 = 0;");
    }

    [Fact]
    public async Task UsrWorker_CanSelectAndUpdate_OutboxEvent_ButNotInsert()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var facturaId = await SeedFactura(db);
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) " +
            $"VALUES ('FACTURA_VALIDADA', {facturaId}, '{{}}', NEXT VALUE FOR fact.SeqOutbox);");

        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.OutboxEvent;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.OutboxEvent SET Estado = 'COMPLETADO';");
        await AssertDenied(db, UsrWorker,
            $"INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) " +
            $"VALUES ('FACTURA_VALIDADA', {facturaId}, '{{}}', 2);");
    }

    // Task 3.3 — OutboxEventIntegracion child-table grants.
    [Fact]
    public async Task UsrApi_CanInsertAndSelect_OutboxEventIntegracion_ButNotUpdate()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var outboxEventId = await SeedOutboxEvent(db);

        await AssertSucceedsWrite(db, UsrApi,
            $"INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion) VALUES ({outboxEventId}, 'DRIVE');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.OutboxEventIntegracion;");
        await AssertDenied(db, UsrApi, "UPDATE fact.OutboxEventIntegracion SET Estado = 'COMPLETADO' WHERE 1 = 0;");
    }

    [Fact]
    public async Task UsrWorker_CanSelectAndUpdate_OutboxEventIntegracion_ButNotInsert()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var outboxEventId = await SeedOutboxEvent(db);
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion) VALUES ({outboxEventId}, 'DRIVE');");

        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.OutboxEventIntegracion;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.OutboxEventIntegracion SET Estado = 'COMPLETADO';");
        await AssertDenied(db, UsrWorker,
            $"INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion) VALUES ({outboxEventId}, 'SHEETS');");
    }

    [Fact]
    public async Task UsrWorker_CanInsertAndSelect_InboxEvent_ButNotUpdate()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var procesamientoId = await SeedProcesamiento(db);

        await AssertSucceedsWrite(db, UsrWorker,
            $"INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload) " +
            $"VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, '{{}}');");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.InboxEvent;");
        await AssertDenied(db, UsrWorker, "UPDATE fact.InboxEvent SET EstadoConsumo = 'PROMOVIDO' WHERE 1 = 0;");
    }

    [Fact]
    public async Task UsrApi_CanSelectAndUpdate_InboxEvent_ButNotInsert()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var procesamientoId = await SeedProcesamiento(db);
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload) " +
            $"VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, '{{}}');");

        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.InboxEvent;");
        await AssertSucceedsWrite(db, UsrApi, "UPDATE fact.InboxEvent SET EstadoConsumo = 'PROMOVIDO';");
        await AssertDenied(db, UsrApi,
            $"INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload) " +
            $"VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, '{{}}');");
    }

    [Fact]
    public async Task UsrApi_CanInsertAndSelect_CommandQueue_ButNotUpdate()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.CommandQueue (Tipo, Payload, CorrelationId) " +
            "VALUES ('SINCRONIZAR_GMAIL', '{}', NEWID());");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.CommandQueue;");
        await AssertDenied(db, UsrApi, "UPDATE fact.CommandQueue SET Estado = 'COMPLETADO' WHERE 1 = 0;");
    }

    [Fact]
    public async Task UsrWorker_CanSelectAndUpdate_CommandQueue_ButNotInsert()
    {
        await using var db = await MigratedDatabaseWithUsers();
        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.CommandQueue (Tipo, Payload, CorrelationId) " +
            "VALUES ('SINCRONIZAR_GMAIL', '{}', NEWID());");

        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.CommandQueue;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.CommandQueue SET Estado = 'COMPLETADO';");
        await AssertDenied(db, UsrWorker,
            "INSERT INTO fact.CommandQueue (Tipo, Payload, CorrelationId) " +
            "VALUES ('SINCRONIZAR_GMAIL', '{}', NEWID());");
    }

    // ---------------------------------------------------------------------------------------
    // Publication tables with multiple origins.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task BothUsers_CanInsertUpdateSelect_TipoCambio()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta) " +
            "VALUES ('2026-01-01', 'MANUAL', 3.7, 3.8, SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.TipoCambio;");
        await AssertSucceedsWrite(db, UsrApi, "UPDATE fact.TipoCambio SET Venta = 3.9 WHERE Origen = 'MANUAL';");

        await AssertSucceedsWrite(db, UsrWorker,
            "INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta) " +
            "VALUES ('2026-01-01', 'SBS', 3.7, 3.8, SYSUTCDATETIME());");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.TipoCambio;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.TipoCambio SET Venta = 3.9 WHERE Origen = 'SBS';");
    }

    [Fact]
    public async Task BothUsers_CanSelect_Configuracion_OnlyApiCanInsertUpdate()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.Configuracion;");
        await AssertDenied(db, UsrWorker,
            "INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Descripcion) " +
            "VALUES ('CORREO', 'CARPETA', 'TEXTO', 'x');");
        await AssertDenied(db, UsrWorker, "UPDATE fact.Configuracion SET Valor = 'x' WHERE 1 = 0;");

        await AssertSucceedsWrite(db, UsrApi,
            "INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Descripcion) " +
            "VALUES ('CORREO', 'CARPETA', 'TEXTO', 'x');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.Configuracion;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.Configuracion SET Valor = 'y' WHERE Seccion = 'CORREO' AND Clave = 'CARPETA';");
    }

    [Fact]
    public async Task BothUsers_CanInsertUpdateSelect_EstadoIntegracion()
    {
        await using var db = await MigratedDatabaseWithUsers();

        // 'GMAIL' is no longer a free value: 009_datos_base.sql (Phase 4) now seeds it as base
        // data. 'CORREO' is CHECK-permitted (007) and not one of the five names 009 seeds, so the
        // INSERT below tests the same permission property without colliding with real base data.
        await AssertSucceedsWrite(db, UsrWorker, "INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('CORREO');");
        await AssertSucceedsRead(db, UsrWorker, "SELECT COUNT(*) FROM fact.EstadoIntegracion;");
        await AssertSucceedsWrite(db, UsrWorker, "UPDATE fact.EstadoIntegracion SET FallosSeguidos = 1 WHERE Nombre = 'CORREO';");

        await AssertSucceedsWrite(db, UsrApi, "INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('TELEGRAM');");
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.EstadoIntegracion;");
        await AssertSucceedsWrite(db, UsrApi, "UPDATE fact.EstadoIntegracion SET FallosSeguidos = 1 WHERE Nombre = 'TELEGRAM';");
    }

    // ---------------------------------------------------------------------------------------
    // External dbo catalogs — SELECT only, object-level.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task BothUsers_CanSelect_FiveExternalDboTables_NeitherCanWrite()
    {
        await using var db = await MigratedDatabaseWithUsers();

        foreach (var user in new[] { UsrApi, UsrWorker })
        {
            await AssertSucceedsRead(db, user, "SELECT COUNT(*) FROM dbo.Proveedor;");
            await AssertSucceedsRead(db, user, "SELECT COUNT(*) FROM dbo.CuentaContable;");
            await AssertSucceedsRead(db, user, "SELECT COUNT(*) FROM dbo.Motivo;");
            await AssertSucceedsRead(db, user, "SELECT COUNT(*) FROM dbo.Origen;");
            // Coordinator-directed follow-up (item 2): a fifth external catalog, the FK target of
            // dbo.Proveedor.coddocide, loaded after this suite's first draft was written.
            await AssertSucceedsRead(db, user, "SELECT COUNT(*) FROM dbo.DocumentoIdentidad;");

            await AssertDenied(db, user, "INSERT INTO dbo.Proveedor (codpro) VALUES ('X99999');");
            await AssertDenied(db, user, "UPDATE dbo.Proveedor SET codpro = codpro WHERE 1 = 0;");
            await AssertDenied(db, user, "DELETE FROM dbo.Proveedor WHERE 1 = 0;");
            await AssertDenied(db, user, "INSERT INTO dbo.DocumentoIdentidad (coddocide) VALUES ('99');");
        }
    }

    [Fact]
    public async Task NeitherUser_HasAnyGrant_OnAnyOtherDboTable()
    {
        await using var db = await MigratedDatabaseWithUsers();

        foreach (var user in new[] { UsrApi, UsrWorker })
        {
            var strayObjects = await db.ExecuteAsUserAsync(user, async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM sys.database_permissions perm
                    JOIN sys.objects obj ON perm.major_id = obj.object_id
                    JOIN sys.schemas sch ON obj.schema_id = sch.schema_id
                    WHERE perm.class = 1
                      AND sch.name = 'dbo'
                      AND perm.grantee_principal_id = DATABASE_PRINCIPAL_ID()
                      AND obj.name NOT IN ('Proveedor', 'CuentaContable', 'Motivo', 'Origen', 'DocumentoIdentidad');
                    """;
                return (int)(await command.ExecuteScalarAsync())!;
            });

            Assert.Equal(0, strayObjects);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Work Unit 2 (Phase 1, tasks 1.3/1.4, 1.7/1.8) — fact.Sesion grants (011_sesion.sql) and
    // NivelBloqueo column-grant inheritance (012_usuario_nivel_bloqueo.sql), design.md item #2
    // Decision 2/3/8.
    // ---------------------------------------------------------------------------------------

    // Task 1.3/1.4 — usr_api holds full SELECT/INSERT/UPDATE/DELETE on fact.Sesion. DELETE is the
    // one grant this test exercises that no other table in the matrix has (design.md Decision 3).
    [Fact]
    public async Task UsrApi_HasFullAccess_OnFactSesion_IncludingDelete()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var usuarioId = await SeedUsuario(db);

        await AssertSucceedsWrite(db, UsrApi,
            $"""
             INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
             VALUES (REPLICATE('a', 64), {usuarioId}, DATEADD(HOUR, 8, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');
             """);
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.Sesion;");
        await AssertSucceedsWrite(db, UsrApi,
            "UPDATE fact.Sesion SET UltimaActividadEn = SYSUTCDATETIME() WHERE TokenHash = REPLICATE('a', 64);");
        await AssertSucceedsWrite(db, UsrApi, "DELETE FROM fact.Sesion WHERE TokenHash = REPLICATE('a', 64);");
    }

    // Task 1.3/1.4 — usr_worker is denied all four verbs on fact.Sesion, mirroring the existing
    // fact.Usuario DENY (008_usuarios_y_permisos.sql).
    [Fact]
    public async Task UsrWorker_IsDenied_AllFourVerbs_OnFactSesion()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.Sesion;");
        await AssertDenied(db, UsrWorker,
            "INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket) " +
            "VALUES (REPLICATE('b', 64), 1, DATEADD(HOUR, 8, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');");
        await AssertDenied(db, UsrWorker, "UPDATE fact.Sesion SET UltimaActividadEn = SYSUTCDATETIME() WHERE 1 = 0;");
        await AssertDenied(db, UsrWorker, "DELETE FROM fact.Sesion WHERE 1 = 0;");
    }

    // Task 1.7/1.8 — the test that actually proves design.md Decision 8's claim rather than trusting
    // it: NivelBloqueo carries no column-level grant of its own, so it must be covered end to end by
    // 008's existing OBJECT-level GRANT/DENY on fact.Usuario, with no change to 008 or a new grant
    // statement in 012. Both directions checked against the real engine: usr_api can SELECT/UPDATE
    // the new column; usr_worker is denied it exactly like the rest of the table.
    [Fact]
    public async Task NivelBloqueo_InheritsObjectLevelGrant_FromExistingUsuarioPermissions()
    {
        await using var db = await MigratedDatabaseWithUsers();

        // (a) No column-level permission exists anywhere for NivelBloqueo — if design.md's claim
        // were false, a column-level GRANT/DENY overriding the object-level one would show up here.
        var columnLevelPermissionCount = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.database_permissions perm
            JOIN sys.columns c ON perm.major_id = c.object_id AND perm.minor_id = c.column_id
            WHERE perm.class = 1 AND c.object_id = OBJECT_ID('fact.Usuario') AND c.name = 'NivelBloqueo';
            """);
        Assert.Equal(0, columnLevelPermissionCount);

        var usuarioId = await SeedUsuario(db);

        // (b) usr_api's existing object-level GRANT SELECT, INSERT, UPDATE covers the new column,
        // with zero grant changes in 012_usuario_nivel_bloqueo.sql.
        await AssertSucceedsRead(db, UsrApi,
            $"SELECT NivelBloqueo FROM fact.Usuario WHERE UsuarioId = {usuarioId};");
        await AssertSucceedsWrite(db, UsrApi,
            $"UPDATE fact.Usuario SET NivelBloqueo = 1 WHERE UsuarioId = {usuarioId};");

        // (c) usr_worker's existing object-level DENY covers the new column too — denied at the
        // statement level even though NivelBloqueo is not named in the SELECT list, because
        // fact_worker has no SELECT permission on fact.Usuario at all.
        await AssertDenied(db, UsrWorker, $"SELECT NivelBloqueo FROM fact.Usuario WHERE UsuarioId = {usuarioId};");
        await AssertDenied(db, UsrWorker, $"UPDATE fact.Usuario SET NivelBloqueo = 2 WHERE UsuarioId = {usuarioId};");
    }

    // ---------------------------------------------------------------------------------------
    // BACKLOG #12, Phase 1 (task 1.1/1.2) — fact.DocumentoFactura (016), the .NET-owned
    // projection of ingested-document metadata (design.md D1, Blocking Architecture Finding):
    // fact_api gets SELECT/INSERT (write-once projection, populated at promoción, never
    // updated), fact_worker is explicitly DENIED — same "Privadas propias de .NET" bucket shape
    // as the rest of 008, extended by 016.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task UsrApi_CanInsertAndSelect_DocumentoFactura()
    {
        await using var db = await MigratedDatabaseWithUsers();
        var facturaId = await SeedFactura(db);

        await AssertSucceedsWrite(db, UsrApi,
            $"""
             INSERT INTO fact.DocumentoFactura
                 (FacturaId, DocumentoRecibidoId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes)
             VALUES
                 ({facturaId}, 1, 'factura.xml', 'application/xml', '2026/08/factura.xml', 2048);
             """);
        await AssertSucceedsRead(db, UsrApi, "SELECT COUNT(*) FROM fact.DocumentoFactura;");
    }

    [Fact]
    public async Task UsrWorker_IsDenied_DocumentoFactura()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrWorker, "SELECT COUNT(*) FROM fact.DocumentoFactura;");
        await AssertDenied(db, UsrWorker,
            "INSERT INTO fact.DocumentoFactura " +
            "(FacturaId, DocumentoRecibidoId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes) " +
            "VALUES (1, 1, 'factura.xml', 'application/xml', '2026/08/factura.xml', 2048);");
    }

    // DocumentoRecibido's own DENY (008) is unchanged by 016 — 016 only adds a new table and its
    // own grants, it does not touch fact.DocumentoRecibido's existing GRANT/DENY statements.
    [Fact]
    public async Task UsrApi_IsStillDenied_DocumentoRecibido_AfterSchema016()
    {
        await using var db = await MigratedDatabaseWithUsers();

        await AssertDenied(db, UsrApi, "SELECT COUNT(*) FROM fact.DocumentoRecibido;");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------
    private static async Task<TestDatabaseFixture> MigratedDatabaseWithUsers()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        // See SchemaShapeTests.MigratedDatabase() for why this must be try/catch, not a bare local:
        // a throw before `return db;` here would otherwise leak the already-created database (the
        // confirmed root cause of the Work Unit 3 test-database leak).
        try
        {
            await db.CreateWithoutLoginUserAsync(UsrApi);
            await db.CreateWithoutLoginUserAsync(UsrWorker);
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            var exitCode = db.RunMigrations();
            Assert.Equal(0, exitCode);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static async Task<long> SeedUsuario(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usuario.seed', 'hash-seed');");
        return await db.ExecuteScalarAsync<long>("SELECT MAX(UsuarioId) FROM fact.Usuario;");
    }

    private static async Task<long> SeedFactura(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) " +
            "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');");
        return await db.ExecuteScalarAsync<long>("SELECT MAX(FacturaId) FROM fact.Factura;");
    }

    private static async Task<long> SeedAsientoContable(TestDatabaseFixture db, long facturaId)
    {
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.AsientoContable (FacturaId, ProveedorCodigo, FechaContable) VALUES ({facturaId}, 'P00000', '2026-01-01');");
        return await db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");
    }

    private static async Task<long> SeedOutboxEvent(TestDatabaseFixture db)
    {
        var facturaId = await SeedFactura(db);
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) " +
            $"VALUES ('FACTURA_VALIDADA', {facturaId}, '{{}}', NEXT VALUE FOR fact.SeqOutbox);");
        return await db.ExecuteScalarAsync<long>("SELECT MAX(OutboxEventId) FROM fact.OutboxEvent;");
    }

    private static async Task<long> SeedProcesamiento(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado) " +
            "VALUES ('msg-seed', 'a@b.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');");
        var emailId = await db.ExecuteScalarAsync<long>("SELECT MAX(EmailId) FROM fact.Email;");

        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.DocumentoRecibido (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType, " +
            $"TamanoBytes, HashContenido, RutaRelativa, Estado) VALUES ({emailId}, 'msg-seed', 'f.pdf', 'pdf', " +
            $"'application/pdf', 10, REPLICATE('a', 64), '/f.pdf', 'DESCARGADO');");
        var documentoId = await db.ExecuteScalarAsync<long>("SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido;");

        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado) VALUES ({documentoId}, 'PENDIENTE');");
        return await db.ExecuteScalarAsync<long>("SELECT MAX(ProcesamientoId) FROM fact.Procesamiento;");
    }

    private static async Task AssertDenied(TestDatabaseFixture db, string user, string sql)
    {
        var exception = await Record.ExceptionAsync(() => db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            return 0;
        }));

        Assert.NotNull(exception);
        var sqlException = Assert.IsType<SqlException>(exception);
        // Error 229: SELECT/INSERT/UPDATE/DELETE permission denied on the object.
        Assert.Equal(229, sqlException.Number);
    }

    private static async Task AssertSucceedsRead(TestDatabaseFixture db, string user, string sql)
    {
        var exception = await Record.ExceptionAsync(() => db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteScalarAsync();
            return 0;
        }));

        Assert.Null(exception);
    }

    private static async Task AssertSucceedsWrite(TestDatabaseFixture db, string user, string sql)
    {
        var exception = await Record.ExceptionAsync(() => db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            return 0;
        }));

        Assert.Null(exception);
    }
}
