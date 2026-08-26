-- rollback/001_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 001_esquema_fact.sql: DROP SCHEMA fact.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001)
-- if reverting more than one migration -- that order is exactly FK-safe, since a higher-numbered
-- script was always created after, and sometimes depends on, a lower-numbered one.
-- Run LAST, only after every other NNN_down.sql (010 down to 002) has removed every object the
-- schema still owns -- DROP SCHEMA fails while anything remains inside it. This is by design: it
-- is the final structural proof that nothing was left behind.
--
-- fact.SchemaVersions is DbUp's own journal table (design.md, Decision 1) -- not created by any
-- numbered forward script, so no other rollback script owns it. It is dropped HERE, last, because
-- this script's whole purpose in the bootstrap-exception window is "start over": leaving the
-- journal behind would make the runner believe 001-010 are still applied and skip them on the next
-- run, against a database that no longer has anything they created.
--
-- Safe only inside design.md's "one bootstrap exception" window (fact is still empty, before the
-- first production apply). Outside that window fact holds real business, ingesta and security
-- data (Factura, AsientoContable, Usuario...), and reaching this script at all would already mean
-- every other down script destroyed that data first -- see their own files for what they admit
-- they cannot undo. This script adds no new risk beyond theirs; it only removes the now-empty
-- container and DbUp's own record of what used to be in it.
IF OBJECT_ID('fact.SchemaVersions', 'U') IS NOT NULL
    DROP TABLE fact.SchemaVersions;
IF SCHEMA_ID('fact') IS NOT NULL
    DROP SCHEMA fact;
