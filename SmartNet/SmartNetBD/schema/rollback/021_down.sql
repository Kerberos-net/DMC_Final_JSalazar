-- rollback/021_down.sql -- ADVISORY, never executed by the runner (design.md item #1, Decision 4,
-- same as 019_down/020_down). Reverses 021_glosa_y_campos_no_extraidos.sql: drops the two nullable
-- columns added to fact.Factura, each IF EXISTS-guarded so promoting this in the bootstrap window
-- converges instead of failing. No GRANT to undo (021 adds none). Data loss on the two columns is
-- expected and acceptable -- both are additive nullable, no backfill, nothing else references them.

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE name = 'CamposNoExtraidos'
             AND object_id = OBJECT_ID('fact.Factura'))
    ALTER TABLE fact.Factura DROP COLUMN CamposNoExtraidos;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE name = 'Glosa'
             AND object_id = OBJECT_ID('fact.Factura'))
    ALTER TABLE fact.Factura DROP COLUMN Glosa;
