-- 009_datos_base.sql
-- Datos base (Work Unit 4, Phase 4). EstadoIntegracion y Configuracion, ambos NOT EXISTS-guardados
-- para que la reaplicacion sea un no-op, igual que 001-008 (design.md, "create-if-absent, always-
-- grant" extendido aqui a "insert-if-absent").
--
-- No escribe fact.Usuario -- spec.md, "fact.Usuario exists and is empty after migration": ninguna
-- fila, ninguna credencial, en ningun script versionado.

-- ============================================================================================
-- EstadoIntegracion: exactamente los cinco nombres que TECH-DESIGN.md nombra explicitamente
-- (GMAIL, DRIVE, SHEETS, SBS, WORKER) -- spec.md lo deja escrito de forma literal ("cinco filas, ni
-- una mas ni una menos"), resolviendo a favor de TECH-DESIGN el Open Question que design.md dejo
-- abierto sobre si sembrar cinco o siete (ADR 0003 revision 4 nombra tambien TELEGRAM y CORREO,
-- pero como clase de origen posible, no como fila a sembrar aqui; el CHECK de 007 ya admite las
-- siete, la siembra solo cubre las cinco que TECH-DESIGN pide). Ver tasks.md, nota bajo 4.5.
-- ============================================================================================
IF NOT EXISTS (SELECT 1 FROM fact.EstadoIntegracion WHERE Nombre = 'GMAIL')
    INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('GMAIL');
IF NOT EXISTS (SELECT 1 FROM fact.EstadoIntegracion WHERE Nombre = 'DRIVE')
    INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('DRIVE');
IF NOT EXISTS (SELECT 1 FROM fact.EstadoIntegracion WHERE Nombre = 'SHEETS')
    INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('SHEETS');
IF NOT EXISTS (SELECT 1 FROM fact.EstadoIntegracion WHERE Nombre = 'SBS')
    INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('SBS');
-- WORKER: FallosSeguidos toma su DEFAULT (0) -- spec.md: "no debe disparar una falsa alerta antes
-- de que el worker haya corrido nunca".
IF NOT EXISTS (SELECT 1 FROM fact.EstadoIntegracion WHERE Nombre = 'WORKER')
    INSERT INTO fact.EstadoIntegracion (Nombre) VALUES ('WORKER');

-- ============================================================================================
-- Configuracion: una fila por clave nombrada en TECH-DESIGN.md ("Carpeta o etiqueta monitoreada,
-- extensiones permitidas, frecuencia de sondeo, fecha de inicio, tipos y tamano maximo de
-- adjuntos, destino de Telegram, preferencias de notificacion y de presentacion, e intervalo
-- esperado por integracion"), agrupadas en seis secciones. `ValorPorDefecto` se llena UNICAMENTE
-- donde algun documento declara un valor; donde ningun documento lo hace, la fila se siembra con
-- `Valor` y `ValorPorDefecto` ambos NULL a proposito (design.md: "para que un sistema sin
-- configurar falle de forma visible en la pantalla de Configuracion, en vez de correr en silencio
-- sobre un valor inventado"). Cada clave documenta, en su propio comentario, la fuente exacta de
-- su valor o la ausencia de una.
-- ============================================================================================

-- --- INGESTA: carpeta/etiqueta monitoreada, extensiones permitidas, frecuencia de sondeo, fecha
-- de inicio (TECH-DESIGN.md linea 307; ADR 0017 nombra los tres primeros conceptos sin darles un
-- valor concreto).
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'ETIQUETA_ORIGEN')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'ETIQUETA_ORIGEN', 'TEXTO', NULL, NULL,
            N'Etiqueta de Gmail que marca un correo como candidato a factura (ADR 0017). Ningun documento fija su nombre.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'EXTENSIONES_PERMITIDAS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'EXTENSIONES_PERMITIDAS', 'LISTA', NULL, NULL,
            N'Extensiones de adjunto que la ingesta admite. TECH-DESIGN.md las nombra sin fijar la lista; no se infiere del TipoDocumento ya identificado tras la descarga.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'FRECUENCIA_SONDEO_MINUTOS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'FRECUENCIA_SONDEO_MINUTOS', 'ENTERO', NULL, NULL,
            N'Minutos entre sondeos de Gmail (ADR 0017: "se concilia con la cuota de la API"). Ningun documento fija un numero.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'FECHA_INICIO')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'FECHA_INICIO', 'FECHA', NULL, NULL,
            N'Fecha a partir de la cual el sondeo de Gmail busca correo (ADR 0017: "after:<fecha-inicio-configurada>"). Es propia de cada despliegue; ningun documento fija una.');

-- --- ADJUNTOS: tipos permitidos y tamano maximo de adjuntos MANUALES -- ADR 0013 los deja
-- EXPLICITAMENTE pendientes ("Pendiente: tipos permitidos y tamano maximo de los adjuntos
-- manuales, que se configuran desde la pantalla de Configuracion").
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'ADJUNTOS' AND Clave = 'TIPOS_PERMITIDOS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('ADJUNTOS', 'TIPOS_PERMITIDOS', 'LISTA', NULL, NULL,
            N'Tipos de archivo admitidos como adjunto manual (AdjuntoManual). ADR 0013 lo deja pendiente explicitamente.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'ADJUNTOS' AND Clave = 'TAMANO_MAXIMO_BYTES')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('ADJUNTOS', 'TAMANO_MAXIMO_BYTES', 'ENTERO', NULL, NULL,
            N'Tamano maximo de un adjunto manual, en bytes. ADR 0013 lo deja pendiente explicitamente.');

-- --- TELEGRAM: destino de las alertas.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'TELEGRAM' AND Clave = 'DESTINO_CHAT_ID')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('TELEGRAM', 'DESTINO_CHAT_ID', 'TEXTO', NULL, NULL,
            N'Chat de Telegram al que se envian las alertas (TECH-DESIGN.md). Ningun documento fija un destino: es un dato de despliegue, no de diseno.');

-- --- NOTIFICACIONES: preferencias de notificacion y de presentacion.
-- CANAL_ALERTA_FALLBACK SI tiene un valor documentado: TECH-DESIGN.md linea 634 y ADR 0015 dicen
-- explicitamente "si Telegram falla, la alerta se envia por correo" -- el unico valor de esta
-- migracion que no es 'pendiente', porque un documento normativo lo decide, no porque se haya
-- inventado.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'NOTIFICACIONES' AND Clave = 'CANAL_ALERTA_FALLBACK')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('NOTIFICACIONES', 'CANAL_ALERTA_FALLBACK', 'TEXTO', NULL, 'CORREO',
            N'Canal de respaldo cuando Telegram falla al enviar una alerta (TECH-DESIGN.md, ADR 0015: "fallos por Telegram con respaldo por correo").');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'NOTIFICACIONES' AND Clave = 'PREFERENCIA_PRESENTACION')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('NOTIFICACIONES', 'PREFERENCIA_PRESENTACION', 'TEXTO', NULL, NULL,
            N'Preferencia de presentacion de la interfaz (TECH-DESIGN.md la nombra junto a las de notificacion, sin detallarla).');

-- --- INTEGRACIONES: intervalo esperado por integracion, el que deriva la pildora "Conectado / Con
-- error" (TECH-DESIGN.md). Solo WORKER tiene un valor documentado: ADR 0015 dice "30 minutos como
-- punto de partida" para el latido del worker. Las otras cuatro integraciones no tienen un
-- intervalo declarado en ningun documento.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_WORKER')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INTEGRACIONES', 'INTERVALO_ESPERADO_WORKER', 'ENTERO', NULL, '30',
            N'Minutos entre latidos esperados de WORKER antes de marcar "Con error" (ADR 0015: "30 minutos como punto de partida").');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_GMAIL')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INTEGRACIONES', 'INTERVALO_ESPERADO_GMAIL', 'ENTERO', NULL, NULL,
            N'Minutos entre ejecuciones esperadas de GMAIL antes de marcar "Con error". Ningun documento fija un numero para esta integracion.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_DRIVE')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INTEGRACIONES', 'INTERVALO_ESPERADO_DRIVE', 'ENTERO', NULL, NULL,
            N'Minutos entre ejecuciones esperadas de DRIVE antes de marcar "Con error". Ningun documento fija un numero para esta integracion.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_SHEETS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INTEGRACIONES', 'INTERVALO_ESPERADO_SHEETS', 'ENTERO', NULL, NULL,
            N'Minutos entre ejecuciones esperadas de SHEETS antes de marcar "Con error". Ningun documento fija un numero para esta integracion.');

IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_SBS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INTEGRACIONES', 'INTERVALO_ESPERADO_SBS', 'ENTERO', NULL, NULL,
            N'Minutos entre ejecuciones esperadas de SBS antes de marcar "Con error". Ningun documento fija un numero para esta integracion.');

-- --- CONTABILIDAD: no es una de las ocho de TECH-DESIGN.md -- se agrega aqui porque REGLAS.md §7
-- (invariante global 3) y TECH-DESIGN.md (criterio de aceptacion del Flujo 3) referencian
-- literalmente `Configuracion.FechaCorteContable` como una regla real de validacion: "FechaContable
-- no anterior a Configuracion.FechaCorteContable". Sin esta fila, la invariante no tendria donde
-- leer su propio valor. Ningun documento fija la fecha de corte en si (no es uno de los seis puntos
-- "pendientes de ratificacion" de REGLAS.md §12 -- esos son de criterio contable, este es un dato
-- operativo), asi que se siembra pendiente, igual que ADJUNTOS: Valor y ValorPorDefecto ambos NULL.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'CONTABILIDAD' AND Clave = 'FECHA_CORTE_CONTABLE')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('CONTABILIDAD', 'FECHA_CORTE_CONTABLE', 'FECHA', NULL, NULL,
            N'Fecha contable minima aceptada al confirmar un asiento (REGLAS.md §7.3, TECH-DESIGN.md). Ningun documento fija la fecha misma.');
