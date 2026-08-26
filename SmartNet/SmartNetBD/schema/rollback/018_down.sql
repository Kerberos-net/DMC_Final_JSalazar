-- rollback/018_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 018_permiso_lectura_procesamiento_error.sql: restores the original DENY SELECT on
-- fact.ProcesamientoError for fact_api (008's shape) and drops the three added indexes.
--
-- Safe to undo at any time: the indexes carry no data of their own, and reverting the grant only
-- removes .NET's read access to the error table again -- it changes no row.
REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoError FROM fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoError TO fact_api;

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ProcesamientoError_ProcesamientoId' AND object_id = OBJECT_ID('fact.ProcesamientoError')
)
    DROP INDEX IX_ProcesamientoError_ProcesamientoId ON fact.ProcesamientoError;

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_InboxEvent_CreadoEn' AND object_id = OBJECT_ID('fact.InboxEvent')
)
    DROP INDEX IX_InboxEvent_CreadoEn ON fact.InboxEvent;

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_CommandQueue_Referencia' AND object_id = OBJECT_ID('fact.CommandQueue')
)
    DROP INDEX IX_CommandQueue_Referencia ON fact.CommandQueue;
