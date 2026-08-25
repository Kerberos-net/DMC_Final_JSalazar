-- 015_commandqueue_reconectar_google.sql
-- BACKLOG #11 (design D7). Amplía CK_CommandQueue_Tipo con RECONECTAR_GOOGLE para
-- POST /api/integraciones/google/reconectar -- reutilizar SINCRONIZAR_GMAIL con un flag de
-- payload ocultaría la intención (design.md D7, rechazado explícitamente). No hay migración de EF
-- Core (ADR 0016): este archivo SQL versionado ES el cambio de esquema completo.
--
-- create-if-absent (mismo patrón que 014): si el CHECK ya incluye RECONECTAR_GOOGLE (reaplicación
-- del script), no hace nada. SQL Server no permite ALTER un CHECK existente para añadir un valor --
-- hay que DROP + ADD con la lista completa.

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = 'CK_CommandQueue_Tipo'
      AND definition LIKE '%RECONECTAR_GOOGLE%'
)
BEGIN
    ALTER TABLE fact.CommandQueue DROP CONSTRAINT CK_CommandQueue_Tipo;

    ALTER TABLE fact.CommandQueue
        ADD CONSTRAINT CK_CommandQueue_Tipo
            CHECK (Tipo IN ('REPROCESAR_DOCUMENTO', 'SINCRONIZAR_GMAIL', 'SINCRONIZAR_SBS', 'RECONECTAR_GOOGLE'));
END

-- Los GRANT de fact.CommandQueue (fact_api INSERT/SELECT, fact_worker SELECT/UPDATE,
-- 008_usuarios_y_permisos.sql) ya cubren el valor nuevo de Tipo -- el GRANT es a nivel de objeto,
-- no de fila/valor (mismo razonamiento que TipoCambio/EstadoIntegracion en 008). Ningún GRANT
-- nuevo es necesario aquí.
