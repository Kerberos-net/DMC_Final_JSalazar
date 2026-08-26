-- rollback/013_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4).
-- Reverses 013_configuracion_etiqueta_procesado.sql: deletes the seeded INGESTA.ETIQUETA_PROCESADO
-- key, then drops the UQ_DocumentoRecibido_Email_Hash unique constraint.
--
-- CANNOT UNDO: dropping UQ_DocumentoRecibido_Email_Hash after real ingestion has run does not
-- restore the previous absence of the constraint's protection -- any duplicate (EmailId,
-- HashContenido) row written while the constraint was gone (there won't be any, since it never
-- existed before this migration) remains theoretical, but the DELETE below is destructive in the
-- ordinary sense: it is data loss for the seeded row, and it removes an identity guarantee item #6
-- was told it could assume (design.md, ADR 0010). Deliberately touches dbo NOWHERE -- this
-- migration never referenced dbo in either direction (see DboWriteLintTests.cs, which scans this
-- file like every other).
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_DocumentoRecibido_Email_Hash' AND object_id = OBJECT_ID('fact.DocumentoRecibido')
)
    ALTER TABLE fact.DocumentoRecibido DROP CONSTRAINT UQ_DocumentoRecibido_Email_Hash;

DELETE FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'ETIQUETA_PROCESADO';
