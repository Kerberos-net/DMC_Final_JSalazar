-- rollback/005_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 005_negocio.sql: drops the seven .NET-private "negocio" tables and the two indexes/one
-- CHECK that live only as metadata on them (dropped automatically with their table), in reverse FK
-- order.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration. 006_down.sql (OutboxEvent/InboxEvent, both FK to Factura;
-- InboxEvent also FK to Procesamiento) must run before this script, or its own DROP would fail
-- against a table still referenced by a live foreign key.
--
-- CANNOT UNDO: this is the highest-risk table set in the project to ever actually promote. It is
-- the accounting book itself -- every AsientoContable, every line, every manual attachment, every
-- correction audited in AuditoriaCorreccion, and the CorrelativoAsiento counters that guarantee no
-- fiscal number is ever reused. Dropping it while it holds a single confirmed asiento is
-- irreversible data loss with no compensating recovery inside this project (design.md Decision 4)
-- and would corrupt the company's own books. This script exists to be reviewed, never to be run
-- against a database that has ever validated a real invoice.
IF OBJECT_ID('fact.AuditoriaCorreccion', 'U') IS NOT NULL
    DROP TABLE fact.AuditoriaCorreccion;
IF OBJECT_ID('fact.AdjuntoManual', 'U') IS NOT NULL
    DROP TABLE fact.AdjuntoManual;
IF OBJECT_ID('fact.CorrelativoAsiento', 'U') IS NOT NULL
    DROP TABLE fact.CorrelativoAsiento;
IF OBJECT_ID('fact.AsientoContableDetalle', 'U') IS NOT NULL
    DROP TABLE fact.AsientoContableDetalle;
IF OBJECT_ID('fact.AsientoContable', 'U') IS NOT NULL
    DROP TABLE fact.AsientoContable;
IF OBJECT_ID('fact.FacturaExtraccion', 'U') IS NOT NULL
    DROP TABLE fact.FacturaExtraccion;
IF OBJECT_ID('fact.Factura', 'U') IS NOT NULL
    DROP TABLE fact.Factura;
