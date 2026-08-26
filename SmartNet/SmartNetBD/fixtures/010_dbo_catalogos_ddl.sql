/*  FIXTURE DE ENTORNO — NO ES PARTE DEL ESQUEMA VERSIONADO
    ============================================================================
    Crea los cinco catalogos maestros en dbo.

    En produccion este script NO se ejecuta: esas tablas las mantiene el sistema
    contable de la compania y este proyecto solo tiene SELECT sobre ellas
    (ADR 0003, clase "externa"). Existe unicamente porque la base asignada esta
    vacia y sin estas tablas no se puede construir ni probar nada.

    El esquema versionado de SmartNet/db/schema/ NUNCA crea ni escribe un objeto
    en dbo. Esa invariante se conserva intacta manteniendo este archivo aqui.
    ============================================================================ */

SET NOCOUNT ON;
GO

/*  Tipos de documento de identidad.
    Codigos de SUNAT: 00 otros, 01 DNI, 04 carne de extranjeria,
    06 RUC, 07 pasaporte.                                        6 filas  */
IF OBJECT_ID('dbo.DocumentoIdentidad', 'U') IS NULL
CREATE TABLE dbo.DocumentoIdentidad (
    coddocide CHAR(2)      NOT NULL,
    nomdocide NVARCHAR(60) NOT NULL,
    CONSTRAINT PK_DocumentoIdentidad PRIMARY KEY CLUSTERED (coddocide)
);
GO

/*  Origenes de libro contable.                                 13 filas  */
IF OBJECT_ID('dbo.Origen', 'U') IS NULL
CREATE TABLE dbo.Origen (
    codigo CHAR(2)      NOT NULL,
    origen NVARCHAR(40) NOT NULL,
    CONSTRAINT PK_Origen PRIMARY KEY CLUSTERED (codigo)
);
GO

/*  Motivos de compra.
    'cuenta' guarda PREFIJOS separados por coma, no cuentas completas:
    el motivo 3 declara '141301,141302,1424,169103,169104'.     90 filas  */
IF OBJECT_ID('dbo.Motivo', 'U') IS NULL
CREATE TABLE dbo.Motivo (
    codigo INT          NOT NULL,
    motivo NVARCHAR(60) NOT NULL,
    cuenta VARCHAR(120) NULL,
    CONSTRAINT PK_Motivo PRIMARY KEY CLUSTERED (codigo)
);
GO

/*  Plan contable de la compania.                            1650 filas

    'cuenta' es VARCHAR y no CHAR de forma deliberada: los motivos declaran
    prefijos de 2 a 6 digitos y la resolucion de candidatas se hace con
    LIKE prefijo + '%'. Un tipo de longitud fija rellenaria con espacios y
    romperia esa comparacion.

    'nivel' viene VACIO exactamente en las 907 cuentas imputables de 6 digitos;
    las 743 restantes son nodos de jerarquia y si lo traen. Es el propio
    catalogo el que distingue la hoja del nodo.

    'ctarefleja' y 'ctapuente' sostienen la contabilidad por destino y solo
    estan presentes en 283 cuentas (ADR 0006).                            */
IF OBJECT_ID('dbo.CuentaContable', 'U') IS NULL
CREATE TABLE dbo.CuentaContable (
    cuenta      VARCHAR(10)  NOT NULL,
    descripcion NVARCHAR(60) NOT NULL,
    nivel       TINYINT      NULL,
    ctarefleja  VARCHAR(10)  NULL,
    ctapuente   VARCHAR(10)  NULL,
    CONSTRAINT PK_CuentaContable PRIMARY KEY CLUSTERED (cuenta)
);
GO

/*  Catalogo de proveedores.                                 6600 filas

    'codpro' es CHAR(6): las 6600 filas miden exactamente seis caracteres y el
    generico es 'P00000'. No es un identificador subrogado, es el codigo.

    'rucpro' es VARCHAR y nunca numerico: los ceros a la izquierda son datos.
    Tras normalizar contiene solo digitos, de 8 a 11 caracteres segun el tipo
    que declare coddocide -- 8 para DNI, 11 para RUC, 9 o 10 para carne de
    extranjeria. El prefijo 'DNI' que traian 118 filas se retira al exportar,
    porque el tipo ya vive en coddocide.                                  */
IF OBJECT_ID('dbo.Proveedor', 'U') IS NULL
CREATE TABLE dbo.Proveedor (
    codpro    CHAR(6)      NOT NULL,
    proveedor NVARCHAR(80) NOT NULL,
    coddocide CHAR(2)      NULL,
    rucpro    VARCHAR(11)  NULL,
    CONSTRAINT PK_Proveedor PRIMARY KEY CLUSTERED (codpro),
    CONSTRAINT FK_Proveedor_DocumentoIdentidad
        FOREIGN KEY (coddocide) REFERENCES dbo.DocumentoIdentidad (coddocide)
);
GO

CREATE NONCLUSTERED INDEX IX_Proveedor_Ruc ON dbo.Proveedor (rucpro);
GO

PRINT 'Catalogos dbo creados.';
GO
