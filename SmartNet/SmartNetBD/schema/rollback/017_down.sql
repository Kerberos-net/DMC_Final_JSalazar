-- rollback/017_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 017_indice_auditoria_por_entidad.sql: drops IX_AuditoriaCorreccion_Entidad.
--
-- Safe to undo at any time: the index carries no data of its own, only accelerates the historial de
-- corrección read (D7). Dropping it degrades that read back to a table scan; it changes no row.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditoriaCorreccion_Entidad' AND object_id = OBJECT_ID('fact.AuditoriaCorreccion')
)
    DROP INDEX IX_AuditoriaCorreccion_Entidad ON fact.AuditoriaCorreccion;
