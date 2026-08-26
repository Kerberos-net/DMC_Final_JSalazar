-- 011_sesion.sql
-- fact.Sesion -- registro de sesiones del lado del servidor (ADR 0007, design.md item #2 Decision
-- 2). Cada cookie __Host-session emitida por SmartNet.Api tiene aqui su fila: sin una fila valida
-- (no revocada, no expirada), ninguna cookie autentica una peticion. Nunca es una edicion de
-- 008_usuarios_y_permisos.sql ni de 002_seguridad.sql -- DbUp journala por NOMBRE, asi que editar un
-- script ya aplicado se omite en silencio en toda base que ya lo corrio (design.md Decision 2).
--
-- Grants y DENY viajan en este mismo archivo, no en 008: la regla que el item #1 ya fijo -- "una
-- tabla nueva nunca existe sin sus permisos" (design.md, item #1 Decision 2) -- para que fact.Sesion
-- nunca quede sin su matriz aplicada.
CREATE TABLE fact.Sesion
(
    SesionId          BIGINT IDENTITY(1,1)                     NOT NULL,
    -- SHA-256 hex (minuscula) del token que viaja en la cookie. El token en claro NUNCA se
    -- almacena: misma disciplina que ClaveHash. CHAR(64) + BIN2 replica design.md item 14
    -- (HashContenido) -- busqueda byte-exacta y sin dependencia de la collation compartida.
    TokenHash         CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    UsuarioId         BIGINT                                   NOT NULL,
    CreadaEn          DATETIME2(3) NOT NULL CONSTRAINT DF_Sesion_CreadaEn DEFAULT (SYSUTCDATETIME()),
    ExpiraEn          DATETIME2(3)                             NOT NULL,
    UltimaActividadEn DATETIME2(3)                             NOT NULL,
    RevocadaEn        DATETIME2(3)                             NULL,
    MotivoRevocacion  VARCHAR(20)                              NULL,
    -- Ticket de autenticacion serializado (ITicketStore), en Base64. NVARCHAR y no VARBINARY:
    -- design.md item 9, legibilidad en una ventana de consulta.
    Ticket            NVARCHAR(MAX)                            NOT NULL,
    CONSTRAINT PK_Sesion PRIMARY KEY (SesionId),
    CONSTRAINT UQ_Sesion_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_Sesion_Usuario FOREIGN KEY (UsuarioId) REFERENCES fact.Usuario (UsuarioId),
    CONSTRAINT CK_Sesion_Revocacion CHECK
        ((RevocadaEn IS NULL AND MotivoRevocacion IS NULL)
         OR (RevocadaEn IS NOT NULL AND MotivoRevocacion IS NOT NULL)),
    CONSTRAINT CK_Sesion_MotivoRevocacion CHECK
        (MotivoRevocacion IS NULL
         OR MotivoRevocacion IN ('CIERRE_SESION', 'RESTABLECIMIENTO', 'ADMIN'))
);

-- UQ_Sesion_TokenHash ES el indice de la ruta caliente (todo request autenticado es un seek por su
-- hash). Este filtrado sirve la otra consulta que existe -- "revocar todas las sesiones vivas de
-- este usuario" -- que el comando de restablecimiento necesita ejecutar.
CREATE INDEX IX_Sesion_UsuarioId_Activa
    ON fact.Sesion (UsuarioId, ExpiraEn)
    WHERE RevocadaEn IS NULL;

-- fact_api: SELECT/INSERT/UPDATE/DELETE completo. DELETE es el UNICO grant DELETE de toda la
-- matriz (design.md, item #2 Decision 3) -- reservado exclusivamente para el verbo
-- "sesion purgar" de SmartNet.Admin; ningun endpoint HTTP emite un DELETE, propiedad de revision,
-- no del motor.
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Sesion TO fact_api;

-- fact_worker: el mismo DENY explicito de cuatro verbos que ya protege fact.Usuario
-- (008_usuarios_y_permisos.sql) -- fact.Sesion pertenece al mismo bucket privado de .NET
-- (ADR 0003, "Privadas propias").
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Sesion TO fact_worker;
