-- rollback/015_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 015_commandqueue_reconectar_google.sql: restores CK_CommandQueue_Tipo to its
-- three-value pre-#11 shape.
--
-- CANNOT UNDO SAFELY if any fact.CommandQueue row already has Tipo = 'RECONECTAR_GOOGLE': re-adding
-- the narrower CHECK would either fail (if SQL Server validates existing rows, which it does by
-- default) or, if run WITH NOCHECK, leave the table in a state the CHECK can no longer vouch for.
-- This script does not attempt to migrate or delete those rows -- an operator must decide their
-- fate first (ADR 0003: fact.CommandQueue rows are contract data Python also reads).
IF EXISTS (SELECT 1 FROM fact.CommandQueue WHERE Tipo = 'RECONECTAR_GOOGLE')
    THROW 50002, 'No se puede revertir 015: existen filas fact.CommandQueue con Tipo=RECONECTAR_GOOGLE.', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = 'CK_CommandQueue_Tipo'
      AND definition LIKE '%RECONECTAR_GOOGLE%'
)
BEGIN
    ALTER TABLE fact.CommandQueue DROP CONSTRAINT CK_CommandQueue_Tipo;

    ALTER TABLE fact.CommandQueue
        ADD CONSTRAINT CK_CommandQueue_Tipo
            CHECK (Tipo IN ('REPROCESAR_DOCUMENTO', 'SINCRONIZAR_GMAIL', 'SINCRONIZAR_SBS'));
END
