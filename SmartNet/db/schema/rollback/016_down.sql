-- rollback/016_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 016_documento_factura.sql: drops fact.DocumentoFactura and its grants.
--
-- GRANT/DENY need no explicit REVOKE before the DROP TABLE -- dropping the object removes every
-- permission recorded against it (same reasoning already documented in rollback/015_down.sql for
-- CHECK constraints, applied here to object-level GRANT/DENY).
--
-- CANNOT UNDO SAFELY if any row already exists: dropping the table destroys the projection
-- promoción already wrote, and Phase 1's premise (task 1.4/1.6) is that the source
-- (fact.DocumentoRecibido) cannot be re-read to rebuild it (ADR 0003). An operator must accept
-- that loss before running this script, same posture as every other _down.sql in this directory.
IF OBJECT_ID('fact.DocumentoFactura', 'U') IS NOT NULL
    DROP TABLE fact.DocumentoFactura;
