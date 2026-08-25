-- 018_permiso_lectura_procesamiento_error.sql
-- BACKLOG #13, design.md D1 (BLOCKING precondition, ratified). `008_usuarios_y_permisos.sql:85`
-- carries `DENY SELECT, INSERT, UPDATE, DELETE ON fact.ProcesamientoError TO fact_api`. DENY beats
-- GRANT: .NET could not read the error table at all, which blocks the bandeja's panel de errores
-- (fact.ProcesamientoError history for INCIDENCIA and reprocessed FACTURA rows). ADR 0003 has been
-- amended (revision 6, this change) to reclassify fact.ProcesamientoError from class 1 (Privada de
-- Python) to asymmetric-read: Python still writes it exclusively, both runtimes may read it — the
-- same shape ADR 0003 already documents for fact.Configuracion.
--
-- REVOKE DENY, then GRANT SELECT: SQL Server has no "downgrade DENY to GRANT" verb — REVOKE removes
-- the existing DENY entry entirely (to NULL/no explicit permission), then GRANT SELECT adds the new
-- explicit permission. INSERT/UPDATE/DELETE are re-DENY'd explicitly right after so the write
-- boundary stays engine-enforced (Python remains the only writer) even though REVOKE cleared all
-- four verbs at once.
--
-- REVOKE and GRANT/DENY are idempotent by themselves (008's header); reapplying this script against
-- an already-migrated database converges, it does not fail.
REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoError FROM fact_api;
GRANT SELECT ON OBJECT::fact.ProcesamientoError TO fact_api;
DENY INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoError TO fact_api;

-- Recommended in the same script (design.md File Changes): three indexes that support exactly the
-- queries this permission change unblocks. REGLAS del proyecto trata el esquema como SQL versionado
-- (ADR 0016); no hay razon para partir esto en una migracion de performance aparte. create-if-absent
-- (same idempotent-reapply discipline as 017).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ProcesamientoError_ProcesamientoId' AND object_id = OBJECT_ID('fact.ProcesamientoError')
)
BEGIN
    -- D3/D4: the bandeja's second result set joins error rows to the current page keyed by
    -- ProcesamientoId; without this index that join table-scans fact.ProcesamientoError.
    CREATE INDEX IX_ProcesamientoError_ProcesamientoId
        ON fact.ProcesamientoError (ProcesamientoId)
        INCLUDE (Integracion, Mensaje, Clasificacion, OcurridoEn);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_InboxEvent_CreadoEn' AND object_id = OBJECT_ID('fact.InboxEvent')
)
BEGIN
    -- D4: OFFSET/FETCH pagination orders by (CreadoEn, InboxEventId) — this index carries the sort
    -- order so the bandeja's page query does not sort the whole table on every request.
    CREATE INDEX IX_InboxEvent_CreadoEn
        ON fact.InboxEvent (CreadoEn, InboxEventId);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_CommandQueue_Referencia' AND object_id = OBJECT_ID('fact.CommandQueue')
)
BEGIN
    -- D5: reprocesarDisponibleEn looks up pending/en-proceso REPROCESAR_DOCUMENTO commands by
    -- (Tipo, Estado) filtered further by Referencia — without this index that scans the full queue.
    CREATE INDEX IX_CommandQueue_Referencia
        ON fact.CommandQueue (Tipo, Estado)
        INCLUDE (Referencia, CreadoEn);
END
