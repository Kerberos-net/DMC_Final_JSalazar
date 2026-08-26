-- rollback/004_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 004_satelites_datos_maestros.sql: drops the three satellite tables. No FK exists
-- between them, and none is referenced by another fact.* table, so order among the three does not
-- matter.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration. In particular, 010_down.sql (which only DELETEs rows from
-- fact.MotivoAtributo) must run before this script drops the table outright, or its own DELETE
-- would fail against a table that no longer exists.
--
-- CANNOT UNDO: every learned suggestion in fact.SugerenciaCuenta (ADR 0011's frequency-based
-- learning has no other record of what an accountant chose before) and any EsRelacionada override
-- in fact.ProveedorAtributo. fact.MotivoAtributo is the one exception with a documented, intended
-- recovery path -- see rollback/010_down.sql, which is meant to run first.
IF OBJECT_ID('fact.SugerenciaCuenta', 'U') IS NOT NULL
    DROP TABLE fact.SugerenciaCuenta;
IF OBJECT_ID('fact.MotivoAtributo', 'U') IS NOT NULL
    DROP TABLE fact.MotivoAtributo;
IF OBJECT_ID('fact.ProveedorAtributo', 'U') IS NOT NULL
    DROP TABLE fact.ProveedorAtributo;
