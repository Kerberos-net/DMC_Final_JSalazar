-- rollback/003_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 003_ingesta_y_procesamiento.sql: drops the six Python-private tables, in reverse FK
-- order (children before the parents they reference).
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001)
-- if reverting more than one migration -- that order is exactly FK-safe, since a higher-numbered
-- script was always created after, and sometimes depends on, a lower-numbered one.
--
-- CANNOT UNDO: every email the worker ever ingested, every attachment it downloaded, every
-- extraction attempt and result, and every diagnostic error it recorded. These are Python's own
-- operational history (ADR 0003, clase "privada"); once the worker has run in a real environment
-- this table set holds the only record of what it saw and did. There is no compensating recovery
-- inside this project for that history once it is dropped (design.md Decision 4).
IF OBJECT_ID('fact.ProcesamientoIntentos', 'U') IS NOT NULL
    DROP TABLE fact.ProcesamientoIntentos;
IF OBJECT_ID('fact.ProcesamientoError', 'U') IS NOT NULL
    DROP TABLE fact.ProcesamientoError;
IF OBJECT_ID('fact.DatosExtraidos', 'U') IS NOT NULL
    DROP TABLE fact.DatosExtraidos;
IF OBJECT_ID('fact.Procesamiento', 'U') IS NOT NULL
    DROP TABLE fact.Procesamiento;
IF OBJECT_ID('fact.DocumentoRecibido', 'U') IS NOT NULL
    DROP TABLE fact.DocumentoRecibido;
IF OBJECT_ID('fact.Email', 'U') IS NOT NULL
    DROP TABLE fact.Email;
