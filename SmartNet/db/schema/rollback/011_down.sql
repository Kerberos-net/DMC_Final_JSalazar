-- rollback/011_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 011_sesion.sql: drops fact.Sesion and, with it, every GRANT/DENY that named it (dropping
-- a table removes its permission entries as ordinary SQL Server behavior, the same reasoning
-- rollback/008_down.sql already documents for dropping a principal).
--
-- Deliberately touches dbo NOWHERE -- fact.Sesion has no dbo reference of any kind, forward or
-- reverse (see DboWriteLintTests.cs, which scans this file like every other).
--
-- CANNOT UNDO safely while the API is live: dropping fact.Sesion while SmartNet.Api holds an open
-- connection breaks every authenticated request instantly -- every live session is lost the moment
-- this runs, not just future ones. Operational risk, not a data-loss one against the accounting
-- record (fact.Sesion carries no accounting data), but it belongs in the same review as every other
-- promotion of a down script.
IF OBJECT_ID('fact.Sesion', 'U') IS NOT NULL
    DROP TABLE fact.Sesion;
