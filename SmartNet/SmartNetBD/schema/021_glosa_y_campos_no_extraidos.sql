-- 021_glosa_y_campos_no_extraidos.sql
-- BACKLOG #19 (design.md, "File Changes"). Dos columnas nuevas nullable en fact.Factura:
--
--   Glosa             NVARCHAR(250) NULL -- glosa contable editable antes de validar (REGLAS §9).
--                                           Espeja el ancho de fact.AsientoContable.Glosa (005:122).
--   CamposNoExtraidos NVARCHAR(500) NULL -- lista (CSV) de campos que la ingesta NO pudo extraer del
--                                           documento, promovida tal cual desde
--                                           fact.InboxEvent.CamposNoExtraidos (D8: hecho de
--                                           extraccion inmutable, nunca derivado del lado del API).
--                                           NULL = factura anterior a 021 -> la SPA cae al highlight
--                                           grueso por fact.Factura.TieneCamposNoExtraidos (005:50).
--
-- Ambas ADD van en un unico lote sin GO: solo se agregan columnas, no se referencian mas adelante
-- en el script, asi que no aplica la restriccion de "columna recien agregada en el mismo lote" que
-- obliga al GO en 015/020.
--
-- Sin GRANT nuevo (ADR 0003): fact_api ya tiene GRANT SELECT, INSERT, UPDATE a nivel de OBJETO
-- sobre fact.Factura (008); los GRANT son por objeto, no por columna, asi que las dos columnas
-- nuevas quedan cubiertas end-to-end. fact_worker conserva su DENY cruzado sobre fact.Factura.
--
-- Convergente / idempotente (patron 009/013/015/020): cada ADD esta guardado por NOT EXISTS sobre
-- sys.columns, reaplicar el script contra una base ya migrada es un no-op, no un error. No hay
-- migracion de EF Core ni Alembic (ADR 0016): este archivo SQL versionado ES el cambio completo.

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'Glosa'
                 AND object_id = OBJECT_ID('fact.Factura'))
    ALTER TABLE fact.Factura ADD Glosa NVARCHAR(250) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'CamposNoExtraidos'
                 AND object_id = OBJECT_ID('fact.Factura'))
    ALTER TABLE fact.Factura ADD CamposNoExtraidos NVARCHAR(500) NULL;
