-- 008_usuarios_y_permisos.sql
-- Matriz de permisos (ADR 0003, revision 4) expresada como SQL versionado (ADR 0016). Dos usuarios
-- de base de datos, uno por runtime, con GRANT/DENY por tabla, aplicados a roles, nunca a los
-- usuarios directamente (design.md, Decision 3).
--
-- create-if-absent, always-grant (design.md): reaplicar este script contra una base donde
-- usr_api/usr_worker ya existen debe converger, no fallar. Los GRANT y DENY de SQL Server ya son
-- idempotentes por si mismos (repetir el mismo GRANT/DENY no produce error); lo que necesita guarda
-- explicita es la CREATE de los principales (usuarios y roles) y la membresia de rol.

-- Falla en voz alta si la premisa del entorno no se cumple (design.md, Decision 3, punto 3): en
-- despliegue real el administrador de instancia debe haber creado los LOGIN antes de aplicar el
-- esquema. En el arnes de pruebas (SmartNet/db/test-bootstrap) los usuarios ya existen como
-- WITHOUT LOGIN antes de que este script corra, asi que DATABASE_PRINCIPAL_ID no es NULL y el
-- THROW nunca se dispara alli -- la misma guarda sirve a los dos caminos sin bifurcar el script.
IF DATABASE_PRINCIPAL_ID('usr_api') IS NULL AND SUSER_ID('usr_api') IS NULL
    THROW 50001, 'Login usr_api no existe: debe crearlo el administrador de la instancia antes de aplicar el esquema.', 1;
IF DATABASE_PRINCIPAL_ID('usr_worker') IS NULL AND SUSER_ID('usr_worker') IS NULL
    THROW 50001, 'Login usr_worker no existe: debe crearlo el administrador de la instancia antes de aplicar el esquema.', 1;

-- CREATE USER ... FOR LOGIN es el camino de despliegue real (design.md, Decision 3). Si el
-- principal de base de datos ya existe -- porque el arnes de pruebas lo creo WITHOUT LOGIN, o
-- porque este script ya se aplico antes -- se omite: create-if-absent.
IF DATABASE_PRINCIPAL_ID('usr_api') IS NULL
    CREATE USER usr_api FOR LOGIN usr_api;
IF DATABASE_PRINCIPAL_ID('usr_worker') IS NULL
    CREATE USER usr_worker FOR LOGIN usr_worker;

-- Nombres unqualified resuelven a fact primero (ADR 0003).
ALTER USER usr_api WITH DEFAULT_SCHEMA = fact;
ALTER USER usr_worker WITH DEFAULT_SCHEMA = fact;

-- Los GRANT/DENY viajan a roles, nunca a los usuarios (design.md, Decision 3): la matriz queda como
-- un solo objeto revisable, un entorno puede usar otros nombres de login sin tocar este script, y
-- la prueba de nivel 2 de ADR 0019 puede sumar un principal desechable al rol.
IF DATABASE_PRINCIPAL_ID('fact_api') IS NULL
    CREATE ROLE fact_api;
IF DATABASE_PRINCIPAL_ID('fact_worker') IS NULL
    CREATE ROLE fact_worker;

IF IS_ROLEMEMBER('fact_api', 'usr_api') = 0
    ALTER ROLE fact_api ADD MEMBER usr_api;
IF IS_ROLEMEMBER('fact_worker', 'usr_worker') = 0
    ALTER ROLE fact_worker ADD MEMBER usr_worker;

-- ============================================================================================
-- Privadas propias de .NET (negocio + satelites + seguridad + CorrelativoAsiento) -- fact_api
-- SELECT/INSERT/UPDATE, ADR 0003 "Privadas propias".
-- ============================================================================================
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Factura TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.AsientoContable TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.AsientoContableDetalle TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.AdjuntoManual TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.AuditoriaCorreccion TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.FacturaExtraccion TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.CorrelativoAsiento TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.ProveedorAtributo TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.MotivoAtributo TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.SugerenciaCuenta TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Usuario TO fact_api;

-- ============================================================================================
-- Privadas propias de Python (ingesta y procesamiento) -- fact_worker SELECT/INSERT/UPDATE.
-- ============================================================================================
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Email TO fact_worker;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.DocumentoRecibido TO fact_worker;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Procesamiento TO fact_worker;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.DatosExtraidos TO fact_worker;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.ProcesamientoError TO fact_worker;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.ProcesamientoIntentos TO fact_worker;

-- ============================================================================================
-- DENY explicito sobre las fronteras mas fuertes (design.md, Decision 3: "DENY beats GRANT" --
-- protege la afirmacion mas fuerte de ADR 0003 contra un GRANT SELECT ON SCHEMA::fact futuro por
-- error). Widened (Work Unit 3, coordinator-directed follow-up item 1): el primer borrador solo
-- nombraba cuatro tablas para fact_worker; se amplia aqui al bucket completo "Privadas propias de
-- .NET" de ADR 0003 -- negocio + satelites de datos maestros + seguridad -- once tablas en total,
-- para que el DENY explicito sobreviva un futuro GRANT accidental tambien en esas seis. design.md
-- se actualizo junto con este script para que ambos coincidan.
-- ============================================================================================
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Procesamiento TO fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.DatosExtraidos TO fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Email TO fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.DocumentoRecibido TO fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoError TO fact_api;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProcesamientoIntentos TO fact_api;

DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Factura TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.AsientoContable TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.AsientoContableDetalle TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.AdjuntoManual TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.AuditoriaCorreccion TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.FacturaExtraccion TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.CorrelativoAsiento TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.ProveedorAtributo TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.MotivoAtributo TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.SugerenciaCuenta TO fact_worker;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Usuario TO fact_worker;

-- ============================================================================================
-- De contrato (coescritas asimetricas, ADR 0003): quien produce inserta, quien consume actualiza.
-- ============================================================================================
-- OutboxEvent: .NET produce (INSERT/SELECT), Python consume (SELECT/UPDATE del estado global).
GRANT SELECT, INSERT ON OBJECT::fact.OutboxEvent TO fact_api;
GRANT SELECT, UPDATE ON OBJECT::fact.OutboxEvent TO fact_worker;

-- OutboxEventIntegracion (task 3.3, design.md "Permission consequence"): fact_api inserta una fila
-- hija por integracion configurada dentro de la misma transaccion; fact_worker mantiene el estado
-- por integracion.
GRANT SELECT, INSERT ON OBJECT::fact.OutboxEventIntegracion TO fact_api;
GRANT SELECT, UPDATE ON OBJECT::fact.OutboxEventIntegracion TO fact_worker;

-- InboxEvent: Python produce (INSERT/SELECT), .NET consume y marca el resultado (SELECT/UPDATE).
GRANT SELECT, INSERT ON OBJECT::fact.InboxEvent TO fact_worker;
GRANT SELECT, UPDATE ON OBJECT::fact.InboxEvent TO fact_api;

-- CommandQueue: .NET produce ordenes (INSERT/SELECT), Python las consume (SELECT/UPDATE).
GRANT SELECT, INSERT ON OBJECT::fact.CommandQueue TO fact_api;
GRANT SELECT, UPDATE ON OBJECT::fact.CommandQueue TO fact_worker;

-- ============================================================================================
-- Publicacion con multiples origenes (ADR 0003).
-- ============================================================================================
-- TipoCambio: origen SBS (Python) y MANUAL (.NET), ambos leen -- ambos runtimes escriben segun su
-- propio origen; el discriminador es de fila, no de permiso (design.md Open Questions).
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.TipoCambio TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.TipoCambio TO fact_worker;

-- Configuracion: solo .NET escribe; ambos leen.
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Configuracion TO fact_api;
GRANT SELECT ON OBJECT::fact.Configuracion TO fact_worker;

-- EstadoIntegracion: filas de ambos origenes (GMAIL/SBS/WORKER/DRIVE/SHEETS de Python;
-- TELEGRAM/CORREO de .NET si la API los ejecuta); el motor no puede partir el permiso por valor de
-- fila (design.md Open Questions), asi que ambos runtimes reciben SELECT/INSERT/UPDATE completo.
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.EstadoIntegracion TO fact_api;
GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.EstadoIntegracion TO fact_worker;

-- ============================================================================================
-- Externas (ADR 0003, clase 4): SELECT unicamente, a nivel de objeto -- nunca
-- GRANT SELECT ON SCHEMA::dbo, que expondria el resto del sistema contable. Ningun INSERT,
-- UPDATE, DELETE ni EXECUTE, para ninguno de los dos usuarios, en ninguna tabla de dbo.
-- Cinco catalogos, no cuatro (Work Unit 3, coordinator-directed follow-up item 2):
-- dbo.DocumentoIdentidad se sumo despues del primer borrador -- es el destino de la FK
-- dbo.Proveedor.coddocide, y un catalogo externo real que el usuario cargo.
-- ============================================================================================
GRANT SELECT ON OBJECT::dbo.DocumentoIdentidad TO fact_api;
GRANT SELECT ON OBJECT::dbo.DocumentoIdentidad TO fact_worker;
GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_api;
GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_worker;
GRANT SELECT ON OBJECT::dbo.CuentaContable TO fact_api;
GRANT SELECT ON OBJECT::dbo.CuentaContable TO fact_worker;
GRANT SELECT ON OBJECT::dbo.Motivo TO fact_api;
GRANT SELECT ON OBJECT::dbo.Motivo TO fact_worker;
GRANT SELECT ON OBJECT::dbo.Origen TO fact_api;
GRANT SELECT ON OBJECT::dbo.Origen TO fact_worker;
