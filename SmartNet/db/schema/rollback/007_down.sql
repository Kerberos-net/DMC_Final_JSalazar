-- rollback/007_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 007_publicacion.sql: drops the three publication tables. None references another, and
-- none is referenced elsewhere in fact except by fact.Usuario (TipoCambio.CargadoPorUsuarioId,
-- Configuracion.ActualizadoPorUsuarioId), so this script must run before 002_down.sql.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration. In particular, 009_down.sql (which only DELETEs the rows it
-- seeded from fact.Configuracion and fact.EstadoIntegracion) must run before this script drops
-- those tables outright, or its own DELETE would fail against tables that no longer exist.
--
-- CANNOT UNDO: every exchange rate ever loaded (fact.TipoCambio -- both the SBS feed and any
-- manual entry, and the discrepancy record between them if one was ever detected), every
-- configuration value an operator set through the Configuracion screen beyond what 009 seeded (see
-- 009_down.sql for that narrower, seed-only case), and the integration heartbeat history in
-- fact.EstadoIntegracion that the "Conectado / Con error" pill depends on.
IF OBJECT_ID('fact.TipoCambio', 'U') IS NOT NULL
    DROP TABLE fact.TipoCambio;
IF OBJECT_ID('fact.Configuracion', 'U') IS NOT NULL
    DROP TABLE fact.Configuracion;
IF OBJECT_ID('fact.EstadoIntegracion', 'U') IS NOT NULL
    DROP TABLE fact.EstadoIntegracion;
