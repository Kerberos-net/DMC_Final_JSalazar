-- 016_documento_factura.sql
-- BACKLOG #12 (design.md, Blocking Architecture Finding / Decision D1). `fact.DocumentoFactura` is
-- the .NET-owned projection of a document's identity/location metadata (`NombreArchivo`,
-- `MimeType`, `RutaRelativa`, `TamanoBytes`) -- populated asynchronously by SmartNet.Inbox.
-- Infrastructure at promoción, from the `InboxEvent` payload's `documento` object (task 1.4/1.6),
-- NEVER by a SELECT against `fact.DocumentoRecibido` (Python-owned, DENY unchanged since 008 --
-- ADR 0003 §Privadas, invariant 3: "reading is the violation, not just writing").
--
-- `DocumentoRecibidoId` is stored here as a plain BIGINT, NOT a FOREIGN KEY to
-- `fact.DocumentoRecibido`: an FK would couple this .NET-owned table's DDL to a Python-owned
-- table's row lifecycle for no behavioral gain (this column exists only so a re-processed
-- InboxEvent projects the same document at most once, task 2.x) -- the constraint would encode a
-- cross-partition dependency ADR 0003 exists specifically to prevent, even though FK enforcement
-- itself does not require a runtime SELECT grant.
--
-- create-if-absent (same idempotent-reapply discipline as every other script here, design.md
-- Decision 3/4): safe to run again against a database where 016 already applied.
IF OBJECT_ID('fact.DocumentoFactura', 'U') IS NULL
BEGIN
    CREATE TABLE fact.DocumentoFactura
    (
        DocumentoFacturaId  BIGINT IDENTITY(1,1) NOT NULL,
        FacturaId            BIGINT               NOT NULL,
        -- Provenance only (never joined to fact.DocumentoRecibido, see header) and the idempotency
        -- key: a re-processed InboxEvent for the same ingested document must project at most one
        -- row (UQ_DocumentoFactura_DocumentoRecibidoId below), same anti-duplicate discipline as
        -- UQ_Factura_Procesamiento.
        DocumentoRecibidoId  BIGINT               NOT NULL,
        -- Mirrors fact.AdjuntoManual's column shapes exactly (005_negocio.sql) -- the unified
        -- documents list (#12 Phase 3) reads both tables through the same shape.
        NombreArchivo        NVARCHAR(255)        NOT NULL,
        MimeType              VARCHAR(100)        NOT NULL,
        RutaRelativa          NVARCHAR(400)        NOT NULL,
        TamanoBytes           BIGINT               NOT NULL,
        CreadoEn              DATETIME2(3)         NOT NULL CONSTRAINT DF_DocumentoFactura_CreadoEn DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_DocumentoFactura PRIMARY KEY (DocumentoFacturaId),
        CONSTRAINT FK_DocumentoFactura_Factura FOREIGN KEY (FacturaId) REFERENCES fact.Factura (FacturaId),
        CONSTRAINT UQ_DocumentoFactura_DocumentoRecibidoId UNIQUE (DocumentoRecibidoId)
    );
END

-- Same "Privadas propias de .NET" bucket shape as 008 (task 1.1/1.2): fact_api gets SELECT/INSERT
-- only -- this projection is write-once at promoción, never UPDATEd afterwards, so no UPDATE grant
-- is needed (unlike fact.Factura/AsientoContable, which the API does mutate post-insert).
GRANT SELECT, INSERT ON OBJECT::fact.DocumentoFactura TO fact_api;

-- DENY beats GRANT (design.md Decision 3, same reasoning as 008): protects the boundary against a
-- future accidental GRANT SELECT ON SCHEMA::fact reaching fact_worker.
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.DocumentoFactura TO fact_worker;
