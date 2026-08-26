-- 014_asociacion_y_afectacion_mixta.sql
-- BACKLOG #6. Dos columnas aditivas y nullable: ninguna fila existente se rompe, ningun permiso
-- cambia (008 ya da SELECT/INSERT/UPDATE de estas dos tablas a fact_worker).
--
-- DocumentoAsociadoId cierra el hueco que 003 dejo: Procesamiento tenia un unico FK
-- (DocumentoRecibidoId) y NADA vinculaba el DocumentoRecibido de un XML con el de su PDF. Es la
-- decision resuelta del usuario (columna FK nullable, no una tabla fact.AsociacionDocumento).
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'DocumentoAsociadoId' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento ADD DocumentoAsociadoId BIGINT NULL;

-- AfectacionMixta: REGLAS.md §8, tres estados (true = el XML declara mas de un codigo de afectacion
-- -> rechazo 409; false = uno solo, verificada; NULL = sin XML, NO verificada). BIT NULL es el unico
-- tipo que representa los tres sin inventar un centinela.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'AfectacionMixta' AND object_id = OBJECT_ID('fact.DatosExtraidos'))
    ALTER TABLE fact.DatosExtraidos ADD AfectacionMixta BIT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Procesamiento_DocumentoAsociado')
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT FK_Procesamiento_DocumentoAsociado
            FOREIGN KEY (DocumentoAsociadoId) REFERENCES fact.DocumentoRecibido (DocumentoRecibidoId);

-- Un documento no puede ser su propia pareja. Invariante del motor, no de la disciplina del worker.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Procesamiento_NoAutoAsociacion')
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT CK_Procesamiento_NoAutoAsociacion
            CHECK (DocumentoAsociadoId IS NULL OR DocumentoAsociadoId <> DocumentoRecibidoId);

-- Indice filtrado: el conjunto candidato de la asociacion es "lo que sigue sin pareja" (Decision 6),
-- que encoge conforme se forman parejas -- no crece con el volumen historico.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Procesamiento_SinAsociar' AND object_id = OBJECT_ID('fact.Procesamiento'))
    CREATE INDEX IX_Procesamiento_SinAsociar ON fact.Procesamiento (DocumentoRecibidoId)
        WHERE DocumentoAsociadoId IS NULL;

-- Un Procesamiento por DocumentoRecibido: ProcesamientoIntentos.NumeroIntento ya presupone esto
-- (N intentos de UN procesamiento). Sin este UNIQUE, upsert_procesamiento necesitaria un
-- SELECT-then-INSERT -- la forma TOCTOU que #4/#5 rechazaron explicitamente. Con el UNIQUE, el
-- motor lo garantiza y el repo usa el mismo patron IntegrityError que insertar_email/
-- insertar_documento (decision explicita del usuario, Open Question 4).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_Procesamiento_DocumentoRecibido' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT UQ_Procesamiento_DocumentoRecibido UNIQUE (DocumentoRecibidoId);

-- RUC propio de la empresa: unica forma no-inferencial de distinguir, en un PDF sin XML que
-- declare dos RUC (proveedor y empresa propia), cual es el emisor -- ADR 0017 prohibe inferirlo por
-- proximidad de etiqueta. NULL-seeded, igual que las demas claves de Configuracion sin fijar por
-- este proyecto (decision explicita del usuario, Open Question 1).
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'EMPRESA' AND Clave = 'RUC')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('EMPRESA', 'RUC', 'TEXTO', NULL, NULL,
            N'RUC propio de la empresa (11 digitos). Usado para excluir el RUC propio al identificar el RUC emisor en un PDF sin XML que muestre ambos (ADR 0017, item #6).');
