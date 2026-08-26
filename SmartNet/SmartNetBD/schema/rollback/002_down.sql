-- rollback/002_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 002_seguridad.sql: DROP TABLE fact.Usuario.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001)
-- if reverting more than one migration -- that order is exactly FK-safe, since a higher-numbered
-- script was always created after, and sometimes depends on, a lower-numbered one.
-- CANNOT UNDO: any real account this project ever created. spec.md guarantees no versioned SQL
-- ever INSERTs a row here, but the application's own administration command (ADR 0007) is meant to
-- create the first user, and every login afterward, directly in this table -- including each
-- ClaveHash. Once that has happened even once, this DROP destroys every account and every
-- credential permanently; there is no compensating recovery for it inside this project (design.md
-- Decision 4: "the only recovery is the instance-level backup, which is not this project's to
-- invoke"). Promoting this script forward without first confirming fact.Usuario is empty violates
-- design.md's own rule that no migration may DROP a table holding data.
IF OBJECT_ID('fact.Usuario', 'U') IS NOT NULL
    DROP TABLE fact.Usuario;
