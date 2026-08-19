-- 013_configuracion_etiqueta_procesado.sql
-- Una sola clave nueva en fact.Configuracion (BACKLOG #5). 009_datos_base.sql sembro las otras
-- cuatro claves INGESTA a partir de TECH-DESIGN.md linea 307, que nombra "carpeta o etiqueta
-- monitoreada, extensiones permitidas, frecuencia de sondeo y fecha de inicio" -- pero no la
-- etiqueta propia que ADR 0017 ("Escritura en Gmail") manda aplicar al correo ya ingestado y que
-- el tercer termino de su consulta acotada excluye. Es un hueco real del esquema, no una decision
-- de este item. NOT EXISTS-guardado, igual que 009: reaplicar es un no-op.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'ETIQUETA_PROCESADO')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'ETIQUETA_PROCESADO', 'TEXTO', NULL, NULL,
            N'Etiqueta propia que el worker aplica al correo ya ingestado y que la consulta acotada excluye (ADR 0017). Ningun documento fija su nombre; debe existir en Gmail antes del primer sondeo.');

-- Indice unico de identidad que ADR 0010 da por existente para la reproceso idempotente de
-- adjuntos (por decision explicita del usuario, agregado aqui en vez de diferirse al item #6, para
-- no dejarle una dependencia de esquema no declarada). No aplica a Email: ese ya tiene
-- UQ_Email_GmailMessageId desde 003_ingesta_y_procesamiento.sql.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_DocumentoRecibido_Email_Hash' AND object_id = OBJECT_ID('fact.DocumentoRecibido')
)
    ALTER TABLE fact.DocumentoRecibido
        ADD CONSTRAINT UQ_DocumentoRecibido_Email_Hash UNIQUE (EmailId, HashContenido);
