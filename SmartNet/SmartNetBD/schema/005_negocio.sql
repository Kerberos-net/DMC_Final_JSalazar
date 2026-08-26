-- 005_negocio.sql
-- Contexto de negocio (.NET) — TECH-DESIGN.md, ADR 0005, ADR 0006, ADR 0011, ADR 0013, ADR 0017,
-- ADR 0018. Factura, FacturaExtraccion, AsientoContable, AsientoContableDetalle,
-- CorrelativoAsiento, AdjuntoManual, AuditoriaCorreccion.
--
-- Includes, in this same file (tasks 2.6-2.8 combined per design.md's numbering rationale — "a new
-- table's grants ship in the same numbered file as its DDL" extended here to indexes/constraints
-- that are part of the table's own shape, not a later migration): the two filtered indexes on
-- Factura, the filtered unique index on AsientoContable, and CK_Linea_Tipo on
-- AsientoContableDetalle.

CREATE TABLE fact.Factura
(
    FacturaId              BIGINT IDENTITY(1,1) NOT NULL,
    -- Idempotencia de la promoción (ADR 0005) — nullable because a manually-entered invoice may
    -- not originate from a Procesamiento.
    ProcesamientoId        BIGINT               NULL,
    -- Business code (dbo.Proveedor), NOT frozen — the live reference. No FK: see 004's note; the
    -- same reasoning (ADR 0003 "nadie escribe una tabla externa"; no REFERENCES grant on dbo)
    -- applies here as much as to CuentaCodigo/RucProveedor.
    ProveedorCodigo        CHAR(6)              NOT NULL CONSTRAINT DF_Factura_ProveedorCodigo DEFAULT ('P00000'),
    -- design.md item 1: frozen copy, never an FK. WIDENED to VARCHAR(8-11): the emitter is not
    -- always a RUC -- 124 of the 6600 suppliers carry a DNI or a carne de extranjeria instead.
    -- VARCHAR and not CHAR because padding an 8-digit DNI to 11 would poison IX_Factura_Identidad
    -- and every join to dbo.Proveedor.rucpro, which is itself VARCHAR.
    RucProveedor           VARCHAR(11)          NULL,
    -- design.md item 4.
    TipoComprobante        CHAR(2)              NOT NULL,
    -- design.md item 2: nullable — "el caso del número no extraído es normativo".
    Numero                 VARCHAR(20)          NULL,
    -- design.md item 16: TotalOrig/IgvOrig/PercepcionOrig DECIMAL(18,2); TipoCambioAplicado
    -- DECIMAL(12,6).
    TotalOrig              DECIMAL(18,2)        NOT NULL,
    IgvOrig                DECIMAL(18,2)        NULL,
    -- design.md item 15: an amount (REGLAS.md §10.4), not a rate.
    PercepcionOrig         DECIMAL(18,2)        NULL,
    -- design.md item 5.
    Moneda                 CHAR(3)              NOT NULL,
    FechaEmision           DATE                 NOT NULL,
    TipoCambioAplicado     DECIMAL(12,6)        NULL,
    -- Motivo de compra (ADR 0011) — an INT referencing dbo.Motivo's own key; not all 90 motivos
    -- are present in fact.MotivoAtributo (it is an override table), so no FK here either.
    Motivo                 INT                  NULL,
    Afectacion              VARCHAR(20)         NULL,
    -- design.md item 8: three-state BIT, no DEFAULT — NULL is "no XML, unverified" (ADR 0017).
    AfectacionMixta         BIT                 NULL,
    -- design.md item 7: five BIT NOT NULL DEFAULT 0 indicators.
    EsProveedorGenerico      BIT                NOT NULL CONSTRAINT DF_Factura_EsProveedorGenerico DEFAULT (0),
    PosibleDuplicado         BIT                NOT NULL CONSTRAINT DF_Factura_PosibleDuplicado DEFAULT (0),
    TieneCamposNoExtraidos   BIT                NOT NULL CONSTRAINT DF_Factura_TieneCamposNoExtraidos DEFAULT (0),
    FechaEnDomingo           BIT                NOT NULL CONSTRAINT DF_Factura_FechaEnDomingo DEFAULT (0),
    EsReferenciaExterna      BIT                NOT NULL CONSTRAINT DF_Factura_EsReferenciaExterna DEFAULT (0),
    -- Referencia al comprobante rectificado: interna o externa (TECH-DESIGN), exactamente una para
    -- el tipo 07 — a business rule enforced by the application, not by CHECK (it depends on
    -- TipoComprobante and is validated together with EsReferenciaExterna at the API boundary).
    FacturaReferenciaId      BIGINT             NULL,
    -- design.md item 3: the asymmetric external reference.
    RefExternaSerie          VARCHAR(4)         NULL,
    RefExternaNumero         VARCHAR(15)        NULL,
    RefExternaFecha          DATE               NULL,
    Estado                   VARCHAR(20)        NOT NULL CONSTRAINT DF_Factura_Estado DEFAULT ('PENDIENTE_VALIDACION'),
    -- design.md item 17: rowversion, excluded from every column list, cannot be inserted/updated.
    Version                  ROWVERSION         NOT NULL,
    CreadoEn                 DATETIME2(3)       NOT NULL CONSTRAINT DF_Factura_CreadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Factura PRIMARY KEY (FacturaId),
    CONSTRAINT FK_Factura_Procesamiento
        FOREIGN KEY (ProcesamientoId) REFERENCES fact.Procesamiento (ProcesamientoId),
    CONSTRAINT FK_Factura_FacturaReferencia
        FOREIGN KEY (FacturaReferenciaId) REFERENCES fact.Factura (FacturaId),
    CONSTRAINT CK_Factura_RucProveedor
        CHECK (RucProveedor IS NULL
               OR (LEN(RucProveedor) BETWEEN 8 AND 11 AND RucProveedor NOT LIKE '%[^0-9]%')),
    CONSTRAINT CK_Factura_Moneda CHECK (Moneda LIKE '[A-Z][A-Z][A-Z]'),
    CONSTRAINT CK_Factura_Afectacion CHECK (Afectacion IS NULL OR Afectacion IN ('GRAVADA', 'EXONERADA', 'INAFECTA')),
    CONSTRAINT CK_Factura_Estado CHECK (Estado IN ('PENDIENTE_VALIDACION', 'VALIDADA', 'DESCARTADA'))
);

-- Detección, NO bloqueo (TECH-DESIGN.md, "Contexto de negocio"). Deliberately non-unique: see
-- spec.md's "IX_Factura_Identidad is a non-unique filtered index" requirement.
CREATE INDEX IX_Factura_Identidad
    ON fact.Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';

-- Idempotencia de la promoción: invariante del motor (TECH-DESIGN.md, ADR 0005).
CREATE UNIQUE INDEX UQ_Factura_Procesamiento
    ON fact.Factura (ProcesamientoId)
    WHERE ProcesamientoId IS NOT NULL;

CREATE TABLE fact.FacturaExtraccion
(
    FacturaExtraccionId BIGINT IDENTITY(1,1) NOT NULL,
    FacturaId            BIGINT               NOT NULL,
    -- ADR 0017: field-name set the extraction can attribute a source to. Not exhaustively stated
    -- by any document ("..." in ADR 0017's example); this is the canonical set derivable from
    -- DatosExtraidos' own columns (TECH-DESIGN).
    CampoNombre           VARCHAR(30)          NOT NULL,
    ValorExtraido          NVARCHAR(500)       NOT NULL,
    Fuente                 VARCHAR(10)         NOT NULL,
    CreadoEn               DATETIME2(3)        NOT NULL CONSTRAINT DF_FacturaExtraccion_CreadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_FacturaExtraccion PRIMARY KEY (FacturaExtraccionId),
    CONSTRAINT FK_FacturaExtraccion_Factura FOREIGN KEY (FacturaId) REFERENCES fact.Factura (FacturaId),
    CONSTRAINT CK_FacturaExtraccion_CampoNombre
        CHECK (CampoNombre IN ('tipoComprobante', 'numero', 'ruc', 'nombreProveedor', 'total', 'igv',
                                'moneda', 'fechaEmision')),
    CONSTRAINT CK_FacturaExtraccion_Fuente CHECK (Fuente IN ('XML', 'PDF'))
);

CREATE TABLE fact.AsientoContable
(
    AsientoContableId  BIGINT IDENTITY(1,1) NOT NULL,
    FacturaId           BIGINT               NOT NULL,
    -- design.md item 2: same VARCHAR(20) rule as Factura.Numero — the fiscal number.
    NumeroComprobante    VARCHAR(20)         NULL,
    -- El correlativo propio, asignado al confirmar (TECH-DESIGN): '02-2026-08-000123'.
    NumeroAsiento         VARCHAR(20)        NULL,
    -- design.md item 4.
    OrigenLibro           CHAR(2)            NOT NULL CONSTRAINT DF_AsientoContable_OrigenLibro DEFAULT ('02'),
    -- Frozen at confirm (TECH-DESIGN: "todo lo que viene de fuera se congela al confirmar"); no FK,
    -- same reasoning as 004/Factura.ProveedorCodigo.
    ProveedorCodigo       CHAR(6)            NOT NULL,
    -- design.md item 28.
    Glosa                 NVARCHAR(250)      NULL,
    FechaContable          DATE              NOT NULL,
    -- ADR 0018: tipo de cambio VENTA, congelado al confirmar. design.md's exchange-rate rule:
    -- DECIMAL(12,6).
    TipoCambioVenta        DECIMAL(12,6)     NULL,
    BasePEN                DECIMAL(18,2)     NULL,
    IgvPEN                 DECIMAL(18,2)     NULL,
    NetoPEN                DECIMAL(18,2)     NULL,
    -- design.md item 28.
    MotivoDescripcion       NVARCHAR(120)    NULL,
    Estado                  VARCHAR(20)      NOT NULL CONSTRAINT DF_AsientoContable_Estado DEFAULT ('BORRADOR'),
    -- design.md item 17: rowversion.
    Version                 ROWVERSION       NOT NULL,
    CreadoEn                DATETIME2(3)     NOT NULL CONSTRAINT DF_AsientoContable_CreadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_AsientoContable PRIMARY KEY (AsientoContableId),
    CONSTRAINT FK_AsientoContable_Factura FOREIGN KEY (FacturaId) REFERENCES fact.Factura (FacturaId),
    CONSTRAINT CK_AsientoContable_Estado CHECK (Estado IN ('BORRADOR', 'CONFIRMADO', 'ANULADO'))
);

-- A lo sumo un asiento no anulado por factura (TECH-DESIGN.md, spec.md "UQ_Asiento_Vigente").
CREATE UNIQUE INDEX UQ_Asiento_Vigente
    ON fact.AsientoContable (FacturaId)
    WHERE Estado <> 'ANULADO';

CREATE TABLE fact.AsientoContableDetalle
(
    -- "LineaId (identificador estable)" — a stable surrogate key, not renumbered on edit.
    LineaId             BIGINT IDENTITY(1,1) NOT NULL,
    AsientoContableId    BIGINT               NOT NULL,
    -- design.md item 20: SMALLINT, presentation-only ordering.
    Orden                 SMALLINT            NOT NULL,
    Bloque                VARCHAR(20)         NOT NULL,
    Tipo                  CHAR(1)             NOT NULL,
    -- design.md item 19: NOT NULL DEFAULT 0 is REQUIRED for CK_Linea_Tipo's predicate to reject a
    -- NULL cleanly instead of evaluating to UNKNOWN.
    Debe                  DECIMAL(18,2)       NOT NULL CONSTRAINT DF_AsientoContableDetalle_Debe DEFAULT (0),
    Haber                 DECIMAL(18,2)       NOT NULL CONSTRAINT DF_AsientoContableDetalle_Haber DEFAULT (0),
    -- design.md item 6: VARCHAR(10), never CHAR; nullable for "línea sin cuenta" (SinCuenta below).
    CuentaCodigo           VARCHAR(10)        NULL,
    CuentaDescripcion       NVARCHAR(200)     NULL,
    CtaReflejaCodigo        VARCHAR(10)       NULL,
    CtaPuenteCodigo         VARCHAR(10)       NULL,
    SinCuenta                BIT              NOT NULL CONSTRAINT DF_AsientoContableDetalle_SinCuenta DEFAULT (0),
    CONSTRAINT PK_AsientoContableDetalle PRIMARY KEY (LineaId),
    CONSTRAINT FK_AsientoContableDetalle_AsientoContable
        FOREIGN KEY (AsientoContableId) REFERENCES fact.AsientoContable (AsientoContableId),
    CONSTRAINT CK_AsientoContableDetalle_Bloque CHECK (Bloque IN ('PRINCIPAL', 'DESTINO')),
    CONSTRAINT CK_AsientoContableDetalle_Tipo CHECK (Tipo IN ('D', 'H')),
    -- spec.md "CK_Linea_Tipo enforces the debit/credit shape per line" — normative, TECH-DESIGN.md.
    CONSTRAINT CK_Linea_Tipo CHECK (
        (Tipo = 'D' AND Debe > 0 AND Haber = 0) OR
        (Tipo = 'H' AND Haber > 0 AND Debe = 0)
    )
);

CREATE TABLE fact.CorrelativoAsiento
(
    -- spec.md: plain counter table, never a SEQUENCE or IDENTITY object (TECH-DESIGN.md: "UPDLOCK
    -- dentro de la transacción que confirma, para que una transacción revertida devuelva el
    -- número" — a SEQUENCE/IDENTITY would burn the number on rollback).
    Anio   SMALLINT NOT NULL,
    Mes    TINYINT  NOT NULL,
    Origen CHAR(2)  NOT NULL,
    Ultimo INT      NOT NULL CONSTRAINT DF_CorrelativoAsiento_Ultimo DEFAULT (0),
    CONSTRAINT PK_CorrelativoAsiento PRIMARY KEY (Anio, Mes, Origen),
    CONSTRAINT CK_CorrelativoAsiento_Mes CHECK (Mes BETWEEN 1 AND 12)
);

CREATE TABLE fact.AdjuntoManual
(
    AdjuntoManualId        BIGINT IDENTITY(1,1) NOT NULL,
    FacturaId               BIGINT               NOT NULL,
    NombreArchivo            NVARCHAR(255)       NOT NULL,
    RutaRelativa             NVARCHAR(400)       NOT NULL,
    MimeType                 VARCHAR(100)        NOT NULL,
    TamanoBytes              BIGINT              NOT NULL,
    SubidoPorUsuarioId       BIGINT              NOT NULL,
    SubidoEn                 DATETIME2(3)        NOT NULL,
    EliminadoEn               DATETIME2(3)       NULL,
    EliminadoPorUsuarioId      BIGINT            NULL,
    MotivoEliminacion          NVARCHAR(300)     NULL,
    CONSTRAINT PK_AdjuntoManual PRIMARY KEY (AdjuntoManualId),
    CONSTRAINT FK_AdjuntoManual_Factura FOREIGN KEY (FacturaId) REFERENCES fact.Factura (FacturaId),
    CONSTRAINT FK_AdjuntoManual_SubidoPor FOREIGN KEY (SubidoPorUsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT FK_AdjuntoManual_EliminadoPor FOREIGN KEY (EliminadoPorUsuarioId) REFERENCES fact.Usuario (UsuarioId),
    -- design.md: "borrado lógico auditado" is a property of the row — all three NULL, or all three
    -- NOT NULL.
    CONSTRAINT CK_AdjuntoManual_Eliminacion CHECK (
        (EliminadoEn IS NULL AND EliminadoPorUsuarioId IS NULL AND MotivoEliminacion IS NULL) OR
        (EliminadoEn IS NOT NULL AND EliminadoPorUsuarioId IS NOT NULL AND MotivoEliminacion IS NOT NULL)
    )
);

CREATE TABLE fact.AuditoriaCorreccion
(
    AuditoriaCorreccionId BIGINT IDENTITY(1,1) NOT NULL,
    EntidadTipo            VARCHAR(20)         NOT NULL,
    -- Deliberately NOT a foreign key: polymorphic across three entities (design.md).
    EntidadId               BIGINT             NOT NULL,
    Accion                   VARCHAR(30)       NOT NULL,
    Campo                     NVARCHAR(60)     NULL,
    -- design.md: 1000, wider than any value it holds — truncation here would be a silent
    -- falsification of the audit trail, the opposite rule from Asunto/Mensaje.
    ValorOriginal              NVARCHAR(1000)  NULL,
    ValorNuevo                  NVARCHAR(1000) NULL,
    Motivo                       NVARCHAR(300) NULL,
    UsuarioId                     BIGINT       NOT NULL,
    OcurridoEn                     DATETIME2(3) NOT NULL,
    CONSTRAINT PK_AuditoriaCorreccion PRIMARY KEY (AuditoriaCorreccionId),
    CONSTRAINT FK_AuditoriaCorreccion_Usuario FOREIGN KEY (UsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT CK_AuditoriaCorreccion_EntidadTipo CHECK (EntidadTipo IN ('FACTURA', 'ASIENTO', 'ADJUNTO')),
    CONSTRAINT CK_AuditoriaCorreccion_Accion CHECK (Accion IN (
        'CORRECCION', 'REAPERTURA', 'ANULACION', 'TRASLADO_PERIODO', 'CONFIRMACION_AFECTACION',
        'ELIMINACION_ADJUNTO', 'REPARTO_MANUAL'
    ))
);
