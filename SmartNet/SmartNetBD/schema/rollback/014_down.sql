-- rollback/014_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 014_asociacion_y_afectacion_mixta.sql: drops the filtered index, both constraints, the
-- UNIQUE, both new columns, then deletes the seeded EMPRESA.RUC configuration row.
--
-- CANNOT UNDO: dropping AfectacionMixta destroys the only record that a comprobante's afectacion was
-- ever verified against its XML (REGLAS.md §8) -- there is no way to recompute it after the column
-- is gone, since the source XML bytes are not retained by this column. Dropping
-- DocumentoAsociadoId similarly discards every XML<->PDF pairing this item's engine ever
-- established; #13's incidencias panel would lose its "PDF sin pareja" signal along with it.
-- Deliberately touches dbo NOWHERE -- this migration never referenced dbo in either direction (see
-- DboWriteLintTests.cs, which scans this file like every other).
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_Procesamiento_SinAsociar' AND object_id = OBJECT_ID('fact.Procesamiento'))
    DROP INDEX IX_Procesamiento_SinAsociar ON fact.Procesamiento;

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UQ_Procesamiento_DocumentoRecibido' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento DROP CONSTRAINT UQ_Procesamiento_DocumentoRecibido;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Procesamiento_NoAutoAsociacion')
    ALTER TABLE fact.Procesamiento DROP CONSTRAINT CK_Procesamiento_NoAutoAsociacion;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Procesamiento_DocumentoAsociado')
    ALTER TABLE fact.Procesamiento DROP CONSTRAINT FK_Procesamiento_DocumentoAsociado;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE name = 'DocumentoAsociadoId' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento DROP COLUMN DocumentoAsociadoId;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE name = 'AfectacionMixta' AND object_id = OBJECT_ID('fact.DatosExtraidos'))
    ALTER TABLE fact.DatosExtraidos DROP COLUMN AfectacionMixta;

DELETE FROM fact.Configuracion WHERE Seccion = 'EMPRESA' AND Clave = 'RUC';
