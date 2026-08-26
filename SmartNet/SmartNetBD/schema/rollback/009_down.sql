-- rollback/009_down.sql -- ADVISORY, never executed by the runner (design.md, Decision 4).
-- Reverses 009_datos_base.sql: DELETEs, never DROPs (the tables are shared with other seeds/usage,
-- design.md Decision 4's "no DROP/TRUNCATE on data that may be real" rule), exactly the rows 009
-- inserted -- the five fact.EstadoIntegracion names and the fifteen fact.Configuracion keys, by
-- their exact (Seccion, Clave) / Nombre, never by a blanket DELETE of the whole table.
--
-- Ordering: promote/apply rollback scripts in DESCENDING numeric order (010 down to 001) if
-- reverting more than one migration.
--
-- CANNOT UNDO: any value an operator has set since 009 seeded these rows. This DELETE removes the
-- CURRENT row for each key regardless of whether Valor has since been filled in through the
-- Configuracion screen, or FallosSeguidos/UltimoExito/UltimoError have since recorded a real
-- integration run. Promoting this script is a deliberate choice to discard that history along with
-- the seed -- it does not distinguish "still exactly as seeded" from "customized since". Whoever
-- promotes it must confirm that loss is acceptable first; this script does not, and structurally
-- cannot, make that judgment call for them.
DELETE FROM fact.EstadoIntegracion WHERE Nombre IN ('GMAIL', 'DRIVE', 'SHEETS', 'SBS', 'WORKER');

-- T-SQL has no row-value IN (( ), ( )) constructor; a VALUES-derived table joined on both columns
-- is the equivalent that actually compiles.
DELETE c
FROM fact.Configuracion c
JOIN (VALUES
    ('INGESTA', 'ETIQUETA_ORIGEN'),
    ('INGESTA', 'EXTENSIONES_PERMITIDAS'),
    ('INGESTA', 'FRECUENCIA_SONDEO_MINUTOS'),
    ('INGESTA', 'FECHA_INICIO'),
    ('ADJUNTOS', 'TIPOS_PERMITIDOS'),
    ('ADJUNTOS', 'TAMANO_MAXIMO_BYTES'),
    ('TELEGRAM', 'DESTINO_CHAT_ID'),
    ('NOTIFICACIONES', 'CANAL_ALERTA_FALLBACK'),
    ('NOTIFICACIONES', 'PREFERENCIA_PRESENTACION'),
    ('INTEGRACIONES', 'INTERVALO_ESPERADO_WORKER'),
    ('INTEGRACIONES', 'INTERVALO_ESPERADO_GMAIL'),
    ('INTEGRACIONES', 'INTERVALO_ESPERADO_DRIVE'),
    ('INTEGRACIONES', 'INTERVALO_ESPERADO_SHEETS'),
    ('INTEGRACIONES', 'INTERVALO_ESPERADO_SBS'),
    ('CONTABILIDAD', 'FECHA_CORTE_CONTABLE')
) AS Sembrado(Seccion, Clave) ON c.Seccion = Sembrado.Seccion AND c.Clave = Sembrado.Clave;
