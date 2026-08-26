-- 017_indice_auditoria_por_entidad.sql
-- BACKLOG #12 (reabierto) — design.md D8. `fact.AuditoriaCorreccion` only has `PK
-- (AuditoriaCorreccionId)` today (005_negocio.sql); the historial de corrección read (D7's
-- `SqlAuditoriaRepository`) filters on `(EntidadTipo, EntidadId)` for three entity kinds
-- (FACTURA/ASIENTO/ADJUNTO) per factura, which would table-scan without this index.
--
-- Additive only, no data change: a plain nonclustered index with an INCLUDE list covering every
-- projected column so the read never needs a key/RID lookup back to the base table. Not EF Core,
-- not Alembic (ADR 0016) -- create-if-absent (design.md Decision 3/4), safe to re-run.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditoriaCorreccion_Entidad' AND object_id = OBJECT_ID('fact.AuditoriaCorreccion')
)
BEGIN
    CREATE INDEX IX_AuditoriaCorreccion_Entidad
        ON fact.AuditoriaCorreccion (EntidadTipo, EntidadId)
        INCLUDE (Accion, Campo, ValorOriginal, ValorNuevo, Motivo, UsuarioId, OcurridoEn);
END
