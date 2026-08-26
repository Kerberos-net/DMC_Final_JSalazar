-- 012_usuario_nivel_bloqueo.sql
-- fact.Usuario.NivelBloqueo -- design.md item #2 Decision 8. Un segundo contador de bloqueo,
-- distinto de IntentosFallidos: IntentosFallidos responde "cuantos fallos faltan para el proximo
-- bloqueo" y se resetea al ARMAR un bloqueo; NivelBloqueo responde "cuanto durara el proximo
-- bloqueo" y solo se resetea con un exito o con el comando de restablecimiento -- nunca por el paso
-- del tiempo. Un solo entero no puede cargar ambas preguntas a la vez porque tienen eventos de
-- reseteo distintos (Decision 8, "The conflict revision 1 could not see").
--
-- Script propio, nunca una edicion de 002_seguridad.sql: la misma razon que 011_sesion.sql frente a
-- 008 -- 002 ya fue aplicado y journalado por nombre, asi que una edicion se omite en silencio en
-- toda base que ya lo corrio. Tampoco se pliega dentro de 011: 011 es la DDL de fact.Sesion y sus
-- grants, una tabla por archivo; un archivo llamado 011_sesion.sql que ademas altera fact.Usuario
-- seria imposible de buscar y estaria mal nombrado.
-- GO separa las dos ALTER TABLE en lotes distintos: SQL Server compila un lote entero antes de
-- ejecutarlo, asi que la segunda sentencia (el CHECK sobre NivelBloqueo) no veria la columna que la
-- primera sentencia todavia no ha comprometido si compartieran lote (error 207, "Invalid column
-- name"). Verificado contra el motor real -- design.md ya trae este mismo GO en su bloque de
-- Decision 8.
ALTER TABLE fact.Usuario
    ADD NivelBloqueo INT NOT NULL
        CONSTRAINT DF_Usuario_NivelBloqueo DEFAULT (0);
GO
ALTER TABLE fact.Usuario
    ADD CONSTRAINT CK_Usuario_NivelBloqueo CHECK (NivelBloqueo >= 0);

-- Sin cambios de grants: 008_usuarios_y_permisos.sql otorga y deniega a nivel de OBJETO
-- (GRANT SELECT, INSERT, UPDATE ON OBJECT::fact.Usuario TO fact_api / DENY ... TO fact_worker), asi
-- que la columna nueva hereda ambos automaticamente -- no existe una nocion de grant a nivel de
-- columna en 008. Verificado contra el motor real, no asumido (task 1.7/1.8 de tasks.md).
