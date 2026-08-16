-- 003_ingesta_y_procesamiento.sql
-- Contexto de ingesta y procesamiento (Python), TECH-DESIGN.md — private tables, ADR 0003.
-- Email, DocumentoRecibido, Procesamiento, DatosExtraidos, ProcesamientoError,
-- ProcesamientoIntentos.

CREATE TABLE fact.Email
(
    EmailId          BIGINT IDENTITY(1,1) NOT NULL,
    -- design.md item 22: opaque Google-controlled hex id, idempotency key of ingestion.
    GmailMessageId   VARCHAR(32)          NOT NULL,
    -- design.md item 23: RFC 5321, 64 local + '@' + 255 domain.
    Remitente        NVARCHAR(320)        NOT NULL,
    -- design.md item 24: unbounded in principle; truncation is acceptable and deliberate.
    Asunto           NVARCHAR(500)        NULL,
    FechaRecepcion   DATETIME2(3)         NOT NULL,
    FechaDeteccion   DATETIME2(3)         NOT NULL,
    -- Not stated by any source document; authored here following design.md's global rule
    -- (enum-like columns are VARCHAR(20) with a named CHECK). CANDIDATO/PROCESADO/ERROR mirror
    -- ADR 0017's candidacy pipeline (a candidate email is ingested, then processed, or fails).
    Estado           VARCHAR(20)          NOT NULL,
    CONSTRAINT PK_Email PRIMARY KEY (EmailId),
    CONSTRAINT UQ_Email_GmailMessageId UNIQUE (GmailMessageId),
    CONSTRAINT CK_Email_Estado CHECK (Estado IN ('CANDIDATO', 'PROCESADO', 'ERROR'))
);

CREATE TABLE fact.DocumentoRecibido
(
    DocumentoRecibidoId BIGINT IDENTITY(1,1) NOT NULL,
    EmailId              BIGINT               NOT NULL,
    -- ADR 0017 "Identidad del adjunto": GmailMessageId, nombre, extension, mime, hash.
    GmailMessageId       VARCHAR(32)          NOT NULL,
    -- design.md item 26: 255 is the practical single-component limit on NTFS/Drive.
    NombreArchivo        NVARCHAR(255)        NOT NULL,
    Extension            VARCHAR(10)          NOT NULL,
    MimeType             VARCHAR(100)         NOT NULL,
    TamanoBytes          BIGINT               NOT NULL,
    -- design.md item 14: SHA-256 hex, fixed 64, ASCII; BIN2 safe because this column never joins
    -- to dbo.
    HashContenido        CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    -- "tipo de documento identificado" (TECH-DESIGN): which of the two candidate formats this is.
    TipoDocumento        VARCHAR(10)          NULL,
    -- design.md item 26: NVARCHAR(400) = 800 bytes, stays under the 900-byte index key limit.
    RutaRelativa         NVARCHAR(400)        NOT NULL,
    Estado               VARCHAR(20)          NOT NULL,
    CONSTRAINT PK_DocumentoRecibido PRIMARY KEY (DocumentoRecibidoId),
    CONSTRAINT FK_DocumentoRecibido_Email FOREIGN KEY (EmailId) REFERENCES fact.Email (EmailId),
    CONSTRAINT CK_DocumentoRecibido_TipoDocumento CHECK (TipoDocumento IN ('XML', 'PDF')),
    CONSTRAINT CK_DocumentoRecibido_Estado
        CHECK (Estado IN ('DESCARGADO', 'PROCESADO', 'ERROR'))
);

CREATE TABLE fact.Procesamiento
(
    ProcesamientoId      BIGINT IDENTITY(1,1) NOT NULL,
    DocumentoRecibidoId  BIGINT               NOT NULL,
    Estado               VARCHAR(20)          NOT NULL,
    IniciadoEn           DATETIME2(3)         NULL,
    FinalizadoEn         DATETIME2(3)         NULL,
    -- "indicador de si ya originó una factura" (TECH-DESIGN).
    OriginoFactura        BIT                 NOT NULL CONSTRAINT DF_Procesamiento_OriginoFactura DEFAULT (0),
    CONSTRAINT PK_Procesamiento PRIMARY KEY (ProcesamientoId),
    CONSTRAINT FK_Procesamiento_DocumentoRecibido
        FOREIGN KEY (DocumentoRecibidoId) REFERENCES fact.DocumentoRecibido (DocumentoRecibidoId),
    CONSTRAINT CK_Procesamiento_Estado
        CHECK (Estado IN ('PENDIENTE', 'EN_PROCESO', 'COMPLETADO', 'ERROR'))
);

CREATE TABLE fact.DatosExtraidos
(
    DatosExtraidosId   BIGINT IDENTITY(1,1) NOT NULL,
    ProcesamientoId    BIGINT               NOT NULL,
    -- design.md item 4: SUNAT catalogo-01, exactly two digits, significant leading zero.
    TipoComprobante    CHAR(2)              NULL,
    -- design.md item 2: serie(4) + '-' + hasta 8 dígitos; VARCHAR because issuers do not always
    -- pad the correlativo.
    Numero             VARCHAR(20)          NULL,
    -- design.md item 1: Peruvian RUC is exactly 11 digits, an identifier (leading zeros are data).
    RucProveedor       CHAR(11)             NULL,
    NombreProveedor    NVARCHAR(200)        NULL,
    -- Money rule: DECIMAL(18,2), never float/real.
    Monto              DECIMAL(18,2)        NULL,
    -- design.md item 5: ISO 4217 alpha-3.
    Moneda             CHAR(3)              NULL,
    FechaEmision       DATE                 NULL,
    -- "qué campos no pudo extraer" (TECH-DESIGN) — a delimited list of field names.
    CamposNoExtraidos  NVARCHAR(500)        NULL,
    CreadoEn           DATETIME2(3)         NOT NULL CONSTRAINT DF_DatosExtraidos_CreadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_DatosExtraidos PRIMARY KEY (DatosExtraidosId),
    CONSTRAINT FK_DatosExtraidos_Procesamiento
        FOREIGN KEY (ProcesamientoId) REFERENCES fact.Procesamiento (ProcesamientoId),
    CONSTRAINT CK_DatosExtraidos_RucProveedor
        CHECK (RucProveedor IS NULL OR RucProveedor LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    CONSTRAINT CK_DatosExtraidos_Moneda CHECK (Moneda IS NULL OR Moneda LIKE '[A-Z][A-Z][A-Z]')
);

CREATE TABLE fact.ProcesamientoError
(
    ProcesamientoErrorId BIGINT IDENTITY(1,1) NOT NULL,
    ProcesamientoId       BIGINT               NOT NULL,
    Integracion            VARCHAR(20)         NOT NULL,
    -- design.md item 25: Google API exception text is long; 2000 stays off MAX.
    Mensaje                NVARCHAR(2000)      NOT NULL,
    -- ADR 0010: three error classes plus the terminal non-error OBSOLETO.
    Clasificacion          VARCHAR(20)         NOT NULL,
    OcurridoEn             DATETIME2(3)        NOT NULL,
    CONSTRAINT PK_ProcesamientoError PRIMARY KEY (ProcesamientoErrorId),
    CONSTRAINT FK_ProcesamientoError_Procesamiento
        FOREIGN KEY (ProcesamientoId) REFERENCES fact.Procesamiento (ProcesamientoId),
    CONSTRAINT CK_ProcesamientoError_Clasificacion
        CHECK (Clasificacion IN ('TRANSITORIO', 'DIFERIBLE', 'PERMANENTE', 'OBSOLETO'))
);

CREATE TABLE fact.ProcesamientoIntentos
(
    ProcesamientoIntentoId BIGINT IDENTITY(1,1) NOT NULL,
    ProcesamientoId          BIGINT             NOT NULL,
    -- design.md item 20: SMALLINT, not TINYINT — Python has no unsigned byte type.
    NumeroIntento             SMALLINT          NOT NULL,
    Resultado                 VARCHAR(20)       NOT NULL,
    OcurridoEn                DATETIME2(3)      NOT NULL,
    Detalle                   NVARCHAR(2000)    NULL,
    ProximoReintentoEn        DATETIME2(3)      NULL,
    CONSTRAINT PK_ProcesamientoIntentos PRIMARY KEY (ProcesamientoIntentoId),
    CONSTRAINT FK_ProcesamientoIntentos_Procesamiento
        FOREIGN KEY (ProcesamientoId) REFERENCES fact.Procesamiento (ProcesamientoId),
    CONSTRAINT CK_ProcesamientoIntentos_Resultado CHECK (Resultado IN ('EXITO', 'FALLO'))
);
