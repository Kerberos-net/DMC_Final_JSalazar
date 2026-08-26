-- rollback/008_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 008_usuarios_y_permisos.sql: drops the two database users and the two roles. Never
-- drops the server LOGIN -- that is instance-level, created by the DBA outside versioned SQL
-- (design.md, Decision 3), and this project has no business touching it even in reverse.
--
-- Deliberately touches dbo NOWHERE, not even to REVOKE the five SELECT grants 008 gave fact_api/
-- fact_worker on dbo.Proveedor/CuentaContable/Motivo/Origen/DocumentoIdentidad -- the hard
-- constraint is "no down script may touch dbo in any way", and an explicit REVOKE would still be
-- touching it. This is not a gap: dropping a database principal automatically removes every
-- GRANT/DENY/permission entry that named it, dbo's included, as ordinary SQL Server behavior. No
-- statement below mentions the word `dbo` at all (see DboWriteLintTests.cs, which scans this file
-- like every other).
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration.
--
-- CANNOT UNDO safely while the application is live: dropping usr_api/usr_worker while the API or
-- the worker holds an open connection under either identity breaks them instantly and mid-
-- transaction. This is an operational risk, not a data-loss one -- no fact.* row is touched -- but
-- it belongs in the same review as every other promotion of a down script.
IF DATABASE_PRINCIPAL_ID('usr_api') IS NOT NULL
    DROP USER usr_api;
IF DATABASE_PRINCIPAL_ID('usr_worker') IS NOT NULL
    DROP USER usr_worker;
IF DATABASE_PRINCIPAL_ID('fact_api') IS NOT NULL
    DROP ROLE fact_api;
IF DATABASE_PRINCIPAL_ID('fact_worker') IS NOT NULL
    DROP ROLE fact_worker;
