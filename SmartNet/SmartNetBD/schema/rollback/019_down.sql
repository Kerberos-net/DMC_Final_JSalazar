-- rollback/019_down.sql -- ADVISORY, never executed by the runner (design.md item #1, Decision 4).
-- Reverses 019_permiso_secuencia_seqoutbox.sql: removes fact_api's GRANT UPDATE on
-- fact.SeqOutbox. Safe to undo at any time -- it changes no row, only re-blocks NEXT VALUE FOR
-- fact.SeqOutbox for fact_api, which would break EmitirOutboxAsync's real INSERT again (by design,
-- if this is reverted the 008 permission gap this migration closes reopens).
REVOKE UPDATE ON OBJECT::fact.SeqOutbox FROM fact_api;
