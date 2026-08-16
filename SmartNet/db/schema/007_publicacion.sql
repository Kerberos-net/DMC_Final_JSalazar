-- 007_publicacion.sql
-- Tablas de publicación con múltiples orígenes — TECH-DESIGN.md, ADR 0003, ADR 0018.
-- TipoCambio, Configuracion, EstadoIntegracion.

CREATE TABLE fact.TipoCambio
(
    -- One row per (Fecha, Origen): "si la SBS publica después para una fecha con fila MANUAL, no
    -- la pisa en silencio: registra la discrepancia" (TECH-DESIGN) — both rows must be able to
    -- coexist, so (Fecha, Origen) is the natural key, not Fecha alone.
    Fecha               DATE          NOT NULL,
    Origen               VARCHAR(10)  NOT NULL,
    -- design.md's exchange-rate rule: DECIMAL(12,6).
    Compra                DECIMAL(12,6) NOT NULL,
    Venta                  DECIMAL(12,6) NOT NULL,
    FechaConsulta            DATETIME2(3) NOT NULL,
    CargadoPorUsuarioId       BIGINT      NULL,
    CargadoEn                  DATETIME2(3) NOT NULL CONSTRAINT DF_TipoCambio_CargadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_TipoCambio PRIMARY KEY (Fecha, Origen),
    CONSTRAINT FK_TipoCambio_CargadoPor FOREIGN KEY (CargadoPorUsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT CK_TipoCambio_Origen CHECK (Origen IN ('SBS', 'MANUAL'))
);

-- Designed here (no schema existed) — design.md, "Configuracion — designed here".
CREATE TABLE fact.Configuracion
(
    Seccion                  VARCHAR(30)   NOT NULL,
    Clave                     VARCHAR(60)  NOT NULL,
    Tipo                       VARCHAR(20) NOT NULL,
    -- Canonical text form; NULL = use default.
    Valor                       NVARCHAR(400) NULL,
    ValorPorDefecto              NVARCHAR(400) NULL,
    Descripcion                   NVARCHAR(200) NOT NULL,
    ActualizadoPorUsuarioId         BIGINT      NULL,
    ActualizadoEn                    DATETIME2(3) NULL,
    CONSTRAINT PK_Configuracion PRIMARY KEY (Seccion, Clave),
    CONSTRAINT FK_Configuracion_ActualizadoPor
        FOREIGN KEY (ActualizadoPorUsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT CK_Configuracion_Tipo
        CHECK (Tipo IN ('TEXTO', 'ENTERO', 'DECIMAL', 'BOOLEANO', 'FECHA', 'LISTA'))
);

CREATE TABLE fact.EstadoIntegracion
(
    -- design.md: seven rows — TECH-DESIGN lists five, ADR 0003 rev 4 adds TELEGRAM and CORREO; the
    -- later document wins (base-data seeding, Unit 4, will insert exactly these seven).
    Nombre           VARCHAR(20)  NOT NULL,
    UltimoIntento     DATETIME2(3) NULL,
    UltimoExito        DATETIME2(3) NULL,
    UltimoError          NVARCHAR(2000) NULL,
    FallosSeguidos         INT      NOT NULL CONSTRAINT DF_EstadoIntegracion_FallosSeguidos DEFAULT (0),
    CONSTRAINT PK_EstadoIntegracion PRIMARY KEY (Nombre),
    CONSTRAINT CK_EstadoIntegracion_Nombre
        CHECK (Nombre IN ('GMAIL', 'DRIVE', 'SHEETS', 'SBS', 'WORKER', 'TELEGRAM', 'CORREO'))
);
