-- 020_outbox_clasificacion.sql
-- BACKLOG #17 (design.md D1/D1b, ratificado). Corrige la ubicacion original propuesta para los
-- errores de despacho del outbox: fact.ProcesamientoError.ProcesamientoId es NOT NULL FK ->
-- fact.Procesamiento (003_ingesta_y_procesamiento.sql:100-115); el camino del outbox solo conoce
-- FacturaId, EnvolturaOutbox no carga procesamientoId, fact.Factura esta DENY-ada a fact_worker
-- (008_usuarios_y_permisos.sql:88) y Factura.ProcesamientoId es nullable -- ProcesamientoError NO
-- puede recibir errores de outbox. Se persiste en fact.OutboxEventIntegracion (Estado='ERROR',
-- Intentos, UltimoError, ProximoIntentoEn ya existen, ya GRANT UPDATE a fact_worker), con UNA
-- columna nueva nullable Clasificacion + CHECK ADR-0010 (mismo vocabulario que
-- CK_ProcesamientoError_Clasificacion, 003:113-114).
--
-- Dos efectos independientes, ambos NOT EXISTS-guardados (reaplicar converge, no falla -- patron
-- 009/013/015). No hay migracion de EF Core ni Alembic (ADR 0016): este archivo SQL versionado ES
-- el cambio de esquema completo.

-- (1) Columna + CHECK en fact.OutboxEventIntegracion (006_contratos.sql:32).
-- ALTER...ADD va en su propio lote (GO) porque SQL Server no permite referenciar en el mismo lote
-- una columna recien agregada -- mismo motivo por el que 015 hace DROP+ADD en vez de ALTER de un
-- CHECK existente.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'Clasificacion'
                 AND object_id = OBJECT_ID('fact.OutboxEventIntegracion'))
    ALTER TABLE fact.OutboxEventIntegracion ADD Clasificacion VARCHAR(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_OutboxEventIntegracion_Clasificacion')
    ALTER TABLE fact.OutboxEventIntegracion
        ADD CONSTRAINT CK_OutboxEventIntegracion_Clasificacion
            CHECK (Clasificacion IN ('TRANSITORIO', 'DIFERIBLE', 'PERMANENTE', 'OBSOLETO'));

-- (2) Fila semilla CORREO.DESTINATARIOS en fact.Configuracion (007_publicacion.sql:24).
-- Valor y ValorPorDefecto quedan NULL a proposito -- ningun documento normativo fija destinatarios,
-- asi que se siembra pendiente (igual que TELEGRAM.DESTINO_CHAT_ID y
-- CONTABILIDAD.FECHA_CORTE_CONTABLE en 009); el notificador falla con ConfiguracionError explicito
-- al arrancar en vez de "enviar a nadie" en silencio. Tipo = 'LISTA' (no TEXTO): son N destinatarios
-- separados por coma (regla LISTA de D6). Sin GRANT nuevo: fact_worker ya tiene SELECT sobre
-- fact.Configuracion (008:131) y UPDATE sobre fact.OutboxEventIntegracion; los GRANT son a nivel de
-- objeto, no de columna ni de fila (mismo razonamiento que 015:24-27).
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion
               WHERE Seccion = 'CORREO' AND Clave = 'DESTINATARIOS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('CORREO', 'DESTINATARIOS', 'LISTA', NULL, NULL,
            -- <= 200 chars (Descripcion NVARCHAR(200), 007_publicacion.sql:32) -- la version
            -- original de esta fila (253 chars) truncaba con error 2628 al aplicar el script;
            -- descubierto en verificacion Fase 5/7 (sdd-apply), corregido aqui, no en silencio.
            N'Direcciones de correo para la alerta de respaldo cuando Telegram falla (ADR 0015). Ningun documento fija destinatarios; se configura desde la pantalla de Configuracion.');
