# Backlog: Gestor de Facturas de Compra

Despiece de `PRD.md` + `TECH-DESIGN.md` v4 + los 19 ADRs + `REGLAS.md` v2 en specs
implementables. Cada ítem es un ciclo de desarrollo completo: lo bastante grande para entregar
algo coherente, lo bastante acotado para especificarse y construirse sin convertirse en un
proyecto propio.

Los ítems #18 y #19 nacieron después del despiece inicial, al implementar el #12: el #18 aisló el
retrabajo visual y el #19 recoge los campos del detalle que quedaron en solo lectura porque
hacerlos editables cruza al núcleo contable.

El diseño está cerrado: los 30 hallazgos de la segunda revisión adversarial están resueltos, las
cinco premisas externas verificadas y ningún ADR queda condicionado.

## El backlog

| # | Ítem | Alcance | Depende de | Contexto extra requerido |
|---|---|---|---|---|
| 1 | **Esquema y permisos** | SQL versionado, esquema `fact`, tablas, índices, restricciones y los `GRANT` de los dos usuarios de base de datos | — | — |
| 2 | **Autenticación y sesión** | Login, cookie `__Host-` con `SameSite=Lax`, bloqueo por intentos, comando de restablecimiento | #1 | — |
| 3 | **Catálogos y satélites** | Lectura de los cinco catálogos externos, satélites propios, resolución de prefijos contra las 907 hojas | #1 | ⚠ Plan de cuentas (`Cuentas.xlsx`) |
| 4 | **Tipos de cambio** | Tabla, carga manual con `Origen='MANUAL'`, *scraping* SBS, bloqueo si no hay dato para la fecha | #1 | — |
| 5 | **Ingesta Gmail** | Candidatura por etiqueta y extensión, descarga de adjuntos, hash, etiquetado, consulta de sondeo acotada | #1 | — |
| 6 | **Extracción y asociación** | XML como fuente prioritaria, OCR del PDF, asociación por los cuatro componentes normalizados, `AfectacionMixta` | #5 | — |
| 7 | **Inbox y promoción** | Consumo del inbox con resultado persistido, decisión de promover, `FacturaExtraccion`, indicadores de la factura | #6, #3 | — |
| 8 | **Núcleo contable** | Generación del asiento, bloques `PRINCIPAL` y `DESTINO`, invariantes, conversión de moneda. **Sin base de datos ni HTTP** | #3 | ⚠ **`REGLAS.md` §5–§10** |
| 9 | **Sugerencia de cuenta** | Cascada por frecuencia, desempate determinista, orden determinista del último escalón | #8 | ⚠ `REGLAS.md` §3 |
| 10 | **Notas de crédito** | Referencia interna y externa, herencia de los cuatro atributos, reparto proporcional, tope acumulado | #8 | ⚠ `REGLAS.md` §5, §7 |
| 11 | **API de facturas y asientos** | `PATCH` con `If-Match`, endpoints de comando, correlativo, `409`/`412`/`422`, `AuditoriaCorreccion` | #7, #8 | — |
| 12 | **Detalle y validación** | Pantalla lado a lado, edición del asiento, guardar avance, validar, visor de documentos | #11 | — |
| 13 | **Bandeja e incidencias** | Vista lógica combinada, chip derivado de los seis indicadores, filtros, panel de errores, reprocesar | #11 | — |
| 14 | **Outbox y mensajería** | `OutboxEvent` con `Secuencia`, `CommandQueue`, reclamo de lote, guarda de obsolescencia, clase `OBSOLETO` | #11 | — |
| 15 | **Publicación a Drive** | Empaquetado desde el *payload*, `appProperties` como clave, adjuntos manuales y `DOCUMENTACION_ACTUALIZADA` | #14 | — |
| 16 | **Publicación a Sheets** | *Upsert* por `FacturaId`, columna de secuencia, corrección y anulación reflejadas | #14 | — |
| 17 | **Errores, notificaciones y operación** | Tres clases de error, notificación por clase, Telegram con respaldo por correo, `EstadoIntegracion`, latido, pantalla de configuración | #14 | — |
| 18 | **Ajuste visual del diseño SPA** | Correcciones sobre la capa visual (tokens/tema, `login-page`, `detalle-page` y sus componentes) para conformar el resultado al *handoff* de diseño; separado del #12 para no bloquear la lógica de negocio con retrabajo visual | #12 | ⚠ *Handoff* de diseño |
| 19 | **Campos contables editables y resaltado OCR por campo** | Lo que el #18 dejó fuera por requerir trabajo de servidor coordinado: hacer editables en el detalle `base imponible`, `IGV` y `tipo de cambio` (proyección y contrato de escritura nuevos, impacto sobre el asiento revisado contra `REGLAS.md`); columna `glosa` y su edición (SQL versionado); resaltado OCR **por campo** vía una lista `camposNoExtraidos` en la extracción —hoy solo existe un booleano por factura—; recálculo de `PosibleDuplicado` al cambiar el triple de identidad (`tipoComprobante`/`numero`) | #12, #18 | ⚠ **`REGLAS.md` §5–§10** |

## Cómo usar este backlog

Cada ítem es una spec independiente. Al implementarlo, arranca un ciclo de Spec-Driven Development
usando **ese ítem** como el cambio, no el proyecto completo.

Donde la columna de contexto tiene algo, hay que **pasarlo al generar la spec**. Los cuatro casos
marcados no requieren producir documentación nueva: `REGLAS.md` v2 es el documento normativo y
`Cuentas.xlsx` el plan de cuentas real de la compañía. Sin ellos, esas specs se generarían con
reglas contables inventadas, que es exactamente el fallo que dos rondas de revisión adversarial
costó evitar.

## Decisiones de despiece

**El núcleo contable está separado de la API.** ADR 0019 exige que la lógica contable viva sin
dependencias de base de datos, HTTP ni reloj. Son dos ciclos distintos: el #8 se prueba con los
siete ejemplos numéricos de `REGLAS.md` §10 y las invariantes de §7, sin infraestructura; el #11
prueba transacciones, concurrencia y contratos. Mezclarlos haría imposible el primer nivel de la
estrategia de verificación.

**El outbox está separado de sus dos destinos.** El #14 entrega infraestructura sin efecto visible
—es su costo declarado— pero permite que Drive y Sheets se construyan y se prueben por separado
sobre una guarda de obsolescencia y una clave de sincronización que ya funcionan. Implementar media
infraestructura dos veces habría sido peor.

**Los catálogos van antes que el núcleo contable.** El #8 necesita el plan de cuentas resuelto por
prefijos, incluidas `ctarefleja` y `ctapuente`. Sin el #3, el núcleo se construiría contra un
catálogo simulado y la primera prueba real aparecería tarde.

## Lo que deliberadamente no está aquí

| Tema | Dónde vive | Por qué no es un ítem del backlog |
|---|---|---|
| Topología, proxy inverso, TLS y entornos | ADR 0012 | Infraestructura de despliegue, no producto |
| Secretos y agregador de logs | ADR 0015 | Ídem, salvo la parte de `EstadoIntegracion`, que sí está en el #17 |
| Respaldo y continuidad | ADR 0014 | **Condición de puesta en producción**, no de construcción |

Los tres hay que resolverlos antes de operar con datos reales. Ninguno bloquea empezar a construir.

## Dos condiciones que este backlog no puede cerrar

Están escritas en los documentos donde corresponde, y se repiten aquí porque es fácil que se
pierdan entre diecisiete ítems:

1. **Las tres preguntas de respaldo de ADR 0014** — modelo de recuperación, cadena de `LOG BACKUP` y
   RPO efectivo de la instancia compartida.
2. **Las seis reglas de `REGLAS.md` §12** no están ratificadas por un contador. El sistema no debe
   operar con contabilidad real sin esa revisión: los puntos 1 y 5 afectan a **todo asiento en
   moneda extranjera ya confirmado**, y corregirlos después es reprocesar el libro.
