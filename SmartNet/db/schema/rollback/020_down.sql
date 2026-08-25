-- rollback/020_down.sql -- ADVISORY, never executed by the runner (design.md, item #1 Decision 4,
-- same as 019_down). Reverses 020_outbox_clasificacion.sql: DROP CHECK -> DROP COLUMN -> DELETE
-- seed row, in that order (the CHECK must go before the column, or DROP COLUMN fails), each
-- IF EXISTS-guarded.

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_OutboxEventIntegracion_Clasificacion')
    ALTER TABLE fact.OutboxEventIntegracion DROP CONSTRAINT CK_OutboxEventIntegracion_Clasificacion;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE name = 'Clasificacion'
             AND object_id = OBJECT_ID('fact.OutboxEventIntegracion'))
    ALTER TABLE fact.OutboxEventIntegracion DROP COLUMN Clasificacion;

DELETE FROM fact.Configuracion WHERE Seccion = 'CORREO' AND Clave = 'DESTINATARIOS';
