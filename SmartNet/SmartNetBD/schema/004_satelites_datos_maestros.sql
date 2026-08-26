-- 004_satelites_datos_maestros.sql
-- Satélites de datos maestros (.NET) — TECH-DESIGN.md, ADR 0011. ProveedorAtributo,
-- MotivoAtributo, SugerenciaCuenta. All three key on the external catalogs' own business codes
-- (design.md Open Questions — RESOLVED), never on a surrogate id.
--
-- Decision (this work unit, not explicitly closed by design.md for these three tables — see
-- apply-progress and the return summary for the full reasoning): NO foreign key from any of these
-- columns to dbo.Proveedor or dbo.Motivo. design.md item 1 (RucProveedor) and item 6 (CuentaCodigo)
-- both reject an FK to dbo for the same two reasons — ADR 0003's "nadie escribe una tabla externa"
-- invariant, and a FOREIGN KEY on this project's tables would constrain the accounting system's own
-- DELETEs on a table this project only reads. That reasoning is not specific to the columns it was
-- written for: it applies identically to ProveedorCodigo and Motivo wherever they appear, including
-- here. The four dbo.* tables are granted `SELECT` only (008, Unit 3) — never `REFERENCES` — which
-- would make declaring the FK impossible under the deploy principal's own permission boundary even
-- if it were otherwise desired.
CREATE TABLE fact.ProveedorAtributo
(
    -- dbo.Proveedor's own key: CHAR(6), 'P00000' is literally the generic supplier's code.
    ProveedorCodigo CHAR(6) NOT NULL,
    -- "EsRelacionada, que elige entre 4212 y 4312" (TECH-DESIGN).
    EsRelacionada   BIT     NOT NULL CONSTRAINT DF_ProveedorAtributo_EsRelacionada DEFAULT (0),
    CONSTRAINT PK_ProveedorAtributo PRIMARY KEY (ProveedorCodigo)
);

CREATE TABLE fact.MotivoAtributo
(
    -- dbo.Motivo's own key: INT.
    Motivo      INT     NOT NULL,
    Activo      BIT     NOT NULL CONSTRAINT DF_MotivoAtributo_Activo DEFAULT (1),
    -- design.md item 4: CHAR(2), SUNAT libro-origin codes.
    OrigenLibro CHAR(2) NOT NULL,
    CONSTRAINT PK_MotivoAtributo PRIMARY KEY (Motivo)
);

CREATE TABLE fact.SugerenciaCuenta
(
    ProveedorCodigo CHAR(6)      NOT NULL,
    Motivo          INT          NOT NULL,
    -- design.md item 6: VARCHAR, NEVER CHAR — motives store 2-to-6-digit prefixes, CHAR would pad
    -- and break `LIKE prefix + '%'`.
    CuentaCodigo    VARCHAR(10)  NOT NULL,
    Veces           INT          NOT NULL CONSTRAINT DF_SugerenciaCuenta_Veces DEFAULT (0),
    UltimoUso       DATETIME2(3) NOT NULL,
    CONSTRAINT PK_SugerenciaCuenta PRIMARY KEY (ProveedorCodigo, Motivo, CuentaCodigo)
);
