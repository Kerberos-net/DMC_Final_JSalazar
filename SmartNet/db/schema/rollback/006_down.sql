-- rollback/006_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 006_contratos.sql: drops the four contract tables (child before parent) and the
-- SEQUENCE that fed OutboxEvent.Secuencia.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration. This script must run before 005_down.sql and 003_down.sql,
-- since OutboxEvent/InboxEvent hold live foreign keys into fact.Factura and fact.Procesamiento.
--
-- CANNOT UNDO: any coescritura in flight. ADR 0003's contract tables are asymmetric by design --
-- .NET produces OutboxEvent/CommandQueue and Python produces InboxEvent, and the other side
-- consumes by updating status, never by re-deriving it from anywhere else. Dropping these tables
-- destroys events neither runtime has necessarily finished consuming yet: an OutboxEvent Python
-- has not yet published to Drive/Sheets/Telegram/correo, or an InboxEvent .NET has not yet
-- promoted or discarded. There is no compensating recovery for an event lost mid-flight.
IF OBJECT_ID('fact.OutboxEventIntegracion', 'U') IS NOT NULL
    DROP TABLE fact.OutboxEventIntegracion;
IF OBJECT_ID('fact.OutboxEvent', 'U') IS NOT NULL
    DROP TABLE fact.OutboxEvent;
IF OBJECT_ID('fact.InboxEvent', 'U') IS NOT NULL
    DROP TABLE fact.InboxEvent;
IF OBJECT_ID('fact.CommandQueue', 'U') IS NOT NULL
    DROP TABLE fact.CommandQueue;
IF OBJECT_ID('fact.SeqOutbox', 'SO') IS NOT NULL
    DROP SEQUENCE fact.SeqOutbox;
