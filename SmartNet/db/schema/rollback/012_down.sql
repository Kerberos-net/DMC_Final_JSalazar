-- rollback/012_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 012_usuario_nivel_bloqueo.sql: drops CK_Usuario_NivelBloqueo, then the named DEFAULT
-- constraint DF_Usuario_NivelBloqueo, then the column itself. Verified against the real engine
-- (task 1.12): SQL Server does NOT drop a named DEFAULT constraint automatically when its column is
-- dropped -- error 5074 ("object is dependent on column") if the DROP COLUMN is attempted first,
-- corrected here rather than assumed.
--
-- Deliberately touches dbo NOWHERE -- this migration never referenced dbo in either direction (see
-- DboWriteLintTests.cs, which scans this file like every other).
--
-- Additive-in-reverse, like 011's own rollback: losing NivelBloqueo costs one account's lockout
-- escalation memory (every account reverts to "first offender" on its next lock) and nothing else --
-- no accounting data can be involved.
--
-- Order matters: the CHECK constraint must go before the column it references, or SQL Server
-- rejects the DROP COLUMN with "the column has a check constraint" in the same batch.
IF OBJECT_ID('fact.CK_Usuario_NivelBloqueo', 'C') IS NOT NULL
    ALTER TABLE fact.Usuario DROP CONSTRAINT CK_Usuario_NivelBloqueo;
IF OBJECT_ID('fact.DF_Usuario_NivelBloqueo', 'D') IS NOT NULL
    ALTER TABLE fact.Usuario DROP CONSTRAINT DF_Usuario_NivelBloqueo;
IF COL_LENGTH('fact.Usuario', 'NivelBloqueo') IS NOT NULL
    ALTER TABLE fact.Usuario DROP COLUMN NivelBloqueo;
