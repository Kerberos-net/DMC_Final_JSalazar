-- 002_seguridad.sql
-- Contexto de seguridad (.NET) — ADR 0007. Tabla `Usuario`.
--
-- No INSERT anywhere in versioned SQL (design.md Decision 2, item 27; spec.md "No row in Usuario
-- and no credential of any kind"). The first user is created later by the application's
-- administration command, never by migration.
CREATE TABLE fact.Usuario
(
    UsuarioId        BIGINT IDENTITY(1,1) NOT NULL,
    NombreUsuario    VARCHAR(50)          NOT NULL,
    -- PHC-encoded string ($argon2id$... or $pbkdf2-sha256$...): algorithm, parameters and salt
    -- travel with the hash. ADR 0007 permits Argon2id or PBKDF2 without choosing; design.md item 27.
    ClaveHash        NVARCHAR(200)        NOT NULL,
    IntentosFallidos INT                  NOT NULL CONSTRAINT DF_Usuario_IntentosFallidos DEFAULT (0),
    -- DATETIME2(3): a precision-only deviation from ADR 0007's unqualified DATETIME2 snippet,
    -- consistent with design.md's global rule (technical timestamps are DATETIME2(3) UTC).
    BloqueadoHasta   DATETIME2(3)         NULL,
    Activo           BIT                  NOT NULL CONSTRAINT DF_Usuario_Activo DEFAULT (1),
    CreadoEn         DATETIME2(3)         NOT NULL CONSTRAINT DF_Usuario_CreadoEn DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Usuario PRIMARY KEY (UsuarioId),
    CONSTRAINT UQ_Usuario_NombreUsuario UNIQUE (NombreUsuario)
);
