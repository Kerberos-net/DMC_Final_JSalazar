# Drift Report: Gestor de Facturas de Compra

**Fecha:** 2026-09-01
**Comparado contra:** PRD.md + REGLAS.md v2 + BACKLOG.md/SPRINT.md + 21 ADRs (`adrs/0001`–`adrs/0021`)

## Resumen ejecutivo

Se revisaron ~55 promesas concretas del PRD (Alcance, No alcance, Criterios de éxito, Casos borde) y
de las Decisiones de los 21 ADRs contra el código real (API .NET en `SmartNet/SmartNetApi`, worker
Python en `SmartNet/SmartNetWorker`, SPA en `SmartNet/SmartNetWeb`, esquema en
`SmartNet/SmartNetBD/schema`). La disciplina de arquitectura es alta: la partición de datos de ADR
0003 está impuesta en el motor y verificada, el núcleo contable de ADR 0019 está aislado y
golden-testeado contra los siete ejemplos de `REGLAS.md` §10, y no se encontró **ninguna decisión de
arquitectura de ADR violada por el código**. El drift real está en **alcance de producto no
entregado**: tres bloques del "Alcance" del PRD —publicación a Drive, publicación a Google
Sheets/Looker Studio, y el flujo completo de notas de crédito— no están implementados, y el flujo de
selección de motivo/cuenta del asistente está a medias. A eso se suma deuda técnica declarada que
toca promesas (23 motivos de caja chica reclasificados, sin guarda de unicidad de factura, suite de
integración del worker que no corre).

| Severidad   | Cantidad |
| ----------- | -------- |
| Crítico     | 4        |
| Advertencia | 6        |
| Sugerencia  | 3        |

**Resúmen de hallazgos**

| Código | Descripción                                                                   | Observaciones                                                                                                                                                                                                                                                                                  |
| ------ | ----------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1     | Publicación a Google Drive no implementada                                    | Se dejará para una segunda versión, no es necesaria en el MVP                                                                                                                                                                                                                                  |
| D2     | Publicación a Google Sheets y dashboard de Looker Studio no implementados     | Se dejará para una segunda versión, no es necesaria en el MVP                                                                                                                                                                                                                                  |
| D3     | Notas de crédito (tipo 07): sin flujo de negocio ni persistencia              | No se implemetó porque se dió prioridad a la funcionalidad de las facturas                                                                                                                                                                                                                     |
| D4     | Selección de motivo y de cuenta por el asistente: endpoints y UI ausentes     | Se dejará para una segunda versión, no es necesaria en el MVP. Actualmente si permite editar la cuenta pero no seleccionar con una lista desglosable                                                                                                                                           |
| D5     | Carga manual de adjuntos: solo metadata, sin subida de bytes ni UI            | Se dejará para una segunda versión, no es necesaria en el MVP. Actualmente enm sistema ingesta los documentos que llegan en el correo, pero el requerimiento indica que se pueda adjuntar manualmente los medios probatorios                                                                   |
| D6     | Percepción (`401131`) no cableada al pipeline                                 | Se dejará para una segunda versión, no es necesaria en el MVP                                                                                                                                                                                                                                  |
| D7     | Métrica de precisión de extracción (≥90%) sin superficie de reporte           | No se implemetó porque se dió prioridad a la funcionalidad de las facturas                                                                                                                                                                                                                     |
| D8     | Sincronización manual de Gmail/SBS                                            | Actualmente se realiza mediante tareas colocadas en el Programador de tareas de windows server. Lo ideal es que halla un botón en la web para llamar la sincrización de GMail. Respecto a la SBS tiene otro flujo ya que si no se carga, se debne de registrar manualmente desde otro sistema. |
| D9     | Sin guarda de unicidad de factura por identidad de comprobante                | Actualmente el sistema marca con la etiqueta "Procesado" en la bandeja de correos. Si llega otro correo con la misma factura se ingesta pero no se convierte a factura (debido a la llave principal de la tabla).                                                                              |
| D10    | 23 motivos de caja chica reclasificados a origen `02` en el *seed*            | No es un error, se reclasificó los motivos de la caja chica, pero al final no se usó en el proyecto                                                                                                                                                                                            |
| D11    | Suite de integración del worker Python no se ejecuta (ruta del *runner* rota) | Debido a un problema fue necesario que .net leyera las tablas generadas por Python, es un riesgo que asumo.                                                                                                                                                                                    |
| D12    | ADR 0021 en estado "Propuesto" vs. dependencia ya en producción               | Se debe de corregir los ADRs                                                                                                                                                                                                                                                                   |
| D13    | El PRD sigue diciendo "tipo de cambio compra" en el cuerpo                    | Faltó corregir el prd, sobre el tipo de cambio que se usará para hacer la conversión de dólares a soles.                                                                                                                                                                                       |

## Hallazgos

### D1 — Publicación a Google Drive no implementada

- **Severidad:** Crítico
- **Tipo:** Feature fantasma
- **Prometido:** PRD.md:36 — *"Creación automática de una carpeta en Google Drive por factura
  validada, con la factura y los medios probatorios correspondientes."* Criterio de éxito PRD.md:57
  (*"El 100% de las facturas marcadas como Validadas cuentan con su carpeta en Drive creada
  automáticamente…"*). ADR 0013 §"Empaquetado hacia Drive"; ADR 0004 (`FACTURA_VALIDADA`,
  `DOCUMENTACION_ACTUALIZADA`).
- **Real:** No existe módulo de Drive en el worker: `SmartNet/SmartNetWorker/src/smartnet_worker/`
  no tiene `drive.py` ni cliente de Google Drive; las únicas menciones de "drive" están en el
  **mapa** `Tipo → Integración` de `despacho_outbox.py` / `payload_inbox.py`, sin *handler*. El
  fan-out a `fact.OutboxEventIntegracion` con destino `DRIVE` se escribe (ítem #14) pero nadie lo
  consume. `SPRINT.md:2371` marca el ítem #15 como `⬜` sin ciclo SDD abierto. ADR 0020 §Consecuencias
  lo dice explícitamente: *"El consumidor del ítem #14 es inerte… las filas se acumulan `PENDIENTE`,
  listas para #15/#16."*
- **Por qué importa:** Es la mitad del valor del producto según el PRD (*"se genera automáticamente
  el archivo en Drive"*). Sin esto, cada factura validada queda sin su carpeta de respaldo y el
  criterio de éxito del 100% es inalcanzable por construcción, no por defecto.
- **Opciones:**
  - `CORREGIR CÓDIGO` — implementar el ítem #15: *handler* Python que consume
    `fact.OutboxEventIntegracion` destino `DRIVE`, empaqueta desde el *payload* (ADR 0013), usa
    `appProperties`/`FacturaId` como clave (ADR 0004), con idempotencia buscar-antes-de-crear.
  - `ACTUALIZAR PRD/ADR` — si la publicación a Drive se declara fuera de alcance de esta entrega
    (p. ej. demo académica sin cuenta Workspace real), moverla al "No alcance" del PRD con la razón,
    y anotar en ADR 0013/0004 que el mecanismo queda diseñado pero no construido (como ya se hizo con
    ADR 0014 para respaldo).

### D2 — Publicación a Google Sheets y dashboard de Looker Studio no implementados

- **Severidad:** Crítico
- **Tipo:** Feature fantasma
- **Prometido:** PRD.md:39 — *"la información se carga automáticamente desde la base de datos del
  software hacia una hoja de cálculo de Google Drive (Google Sheets), que sirve como fuente de datos
  para Looker Studio."* PRD.md:40 (*"Plantilla base de dashboard en Looker Studio…"*). Criterio de
  éxito PRD.md:61. ADR 0004 §"Clave de sincronización" (`FacturaId` como clave de *upsert* en Sheets).
- **Real:** No existe módulo de Sheets en el worker (`SmartNet/SmartNetWorker/src/smartnet_worker/`
  sin `sheets.py`). Destino `SHEETS` en `fact.OutboxEventIntegracion` sin consumidor. `SPRINT.md:2372`
  marca el ítem #16 como `⬜`. No hay artefacto de plantilla de Looker Studio en el repo.
- **Por qué importa:** El PRD define "visibilidad consolidada en un dashboard de gastos" como parte
  del objetivo. Sin la sincronización, no hay fuente de datos y el dashboard no existe.
- **Opciones:**
  - `CORREGIR CÓDIGO` — implementar el ítem #16: *upsert* por `FacturaId` con columna de secuencia,
    reflejo de corrección/anulación (ADR 0004), más la plantilla base del dashboard.
  - `ACTUALIZAR PRD/ADR` — declarar la integración con Sheets/Looker fuera del alcance de esta
    entrega y moverla al "No alcance", con la misma justificación de demo académica que usa ADR 0014.

### D3 — Notas de crédito (tipo 07): sin flujo de negocio ni persistencia

- **Severidad:** Crítico
- **Tipo:** Feature fantasma / Regla omitida
- **Prometido:** PRD.md:23 (*"tipo de comprobante (01 Factura, 03 Boleta, 07 Nota de Crédito)"*),
  PRD.md:77 (caso borde de corrección de proveedor sobre asiento). `REGLAS.md` §5 (estructura de la
  NC, referencia interna/externa, reparto proporcional), §7 (tope acumulado de notas vigentes), §8
  (precondiciones de la NC). ADR 0006 §"Notas de crédito"; ADR 0018 §4 (herencia de tipo de cambio);
  ADR 0008 (`409` para NC con referencia interna irresoluble).
- **Real:** La lógica **pura** existe y está golden-testeada:
  `SmartNet/SmartNetApi/contable/SmartNet.Contable.Core/HerenciaNotaCredito.cs`,
  `ComposicionDeAsiento.cs` (casos NC), golden §10.5/§10.6/§10.7 en `ComponerGoldenTests.cs`. Pero
  **no está cableada al pipeline**: `fact.Factura.FacturaReferenciaId`
  (`SmartNet/SmartNetBD/schema/005_negocio.sql:56`) nunca se puebla; `EsReferenciaExterna` queda con
  su valor DDL por defecto sin calcularse (SPRINT.md WU6 del ítem #7: *"`EsReferenciaExterna` queda
  con su default de DDL: notas de crédito es el ítem #10"*); no hay endpoint ni PATCH para asignar la
  referencia; la invariante del tope acumulado de §7 no se evalúa contra datos reales en ninguna
  transacción. `SPRINT.md:2370` marca el ítem #10 como `⬜`; `SPRINT.md` ítem #24 declara *"NC y
  percepción (§10.4) quedan fuera de alcance"*. El único gancho es
  `CasoConflicto.NotaCreditoReferenciaIrresoluble` en `ServicioDeFacturas.cs:158`, sin productor real
  del hecho.
- **Por qué importa:** Las notas de crédito llegan a diario en un libro de compras real, y `REGLAS.md`
  §5 fue reescrito en la v2 precisamente para cubrirlas. Sin el flujo, una NC solo puede descartarse
  —*"perder un documento fiscal real"* (ADR 0006)—.
- **Opciones:**
  - `CORREGIR CÓDIGO` — implementar el ítem #10: extracción del comprobante rectificado desde el XML,
    asignación de `FacturaReferenciaId` / referencia externa, herencia de los cuatro atributos,
    invariante de tope acumulado en la transacción de validación, precondiciones `409`.
  - `ACTUALIZAR PRD/ADR` — si esta entrega registra solo facturas y boletas, quitar "07 Nota de
    Crédito" del Alcance del PRD y anotar en `REGLAS.md` §5 y ADR 0006 que la NC queda diseñada pero
    no construida.

### D4 — Selección de motivo y de cuenta por el asistente: endpoints y UI ausentes

- **Severidad:** Crítico
- **Tipo:** Feature fantasma / Decisión de contrato incompleta
- **Prometido:** ADR 0008 §"Consultas y edición de borrador" lista `GET /api/motivos`,
  `GET /api/motivos/{id}/cuentas` y `GET /api/cuentas`. ADR 0011 §Decisión: *"Al abrir la factura, el
  asistente selecciona un **motivo**. Es **obligatorio**… El sistema **sugiere** la candidata más
  probable. El asistente confirma o cambia por otra del mismo motivo… puede **dividir el cargo**."*
  PRD.md:28. `REGLAS.md` §3.
- **Real:** Los tres endpoints **no existen**: `grep` sobre `SmartNet/SmartNetApi/api/` no encuentra
  `/api/motivos` ni `/api/cuentas` (los `*Endpoints.cs` presentes son bandeja, catálogos
  proveedores/plan-contable, documentos, asientos, facturas, tipos-cambio, integraciones,
  configuración, registro-compra, auditoría, sesión). La SPA de detalle no tiene componente de
  selección de motivo (`SmartNet/SmartNetWeb/src/app/detalle/` sin archivo `motivo*`). `fact.Factura.Motivo`
  **sí** es asignable por `PATCH /api/facturas/{id}` (`ServicioDeFacturas.cs:582`) y la cascada de
  sugerencia corre *server-side* al componer (`SqlUnidadDeTrabajo.cs:617`), pero el asistente no
  tiene forma de ver la lista de motivos activos de origen `02`, ni las cuentas candidatas, ni de
  repartir el cargo entre varias.
- **Por qué importa:** El motivo es el insumo que hace que el asiento sea implementable (ADR 0011).
  Sin la pantalla, el `fact.Factura.Motivo` queda `NULL` para toda factura ingerida y la composición
  cae al *placeholder* sin cuenta (ADR 0024 A2), bloqueando `validar` para todo el volumen real.
- **Opciones:**
  - `CORREGIR CÓDIGO` — añadir `GET /api/motivos` (activos, origen `02`), `GET /api/motivos/{id}/cuentas`
    (candidatas ordenadas por frecuencia), `GET /api/cuentas`, y el selector de motivo + reparto en
    la pantalla de detalle.
  - `ACTUALIZAR PRD/ADR` — si el motivo se va a resolver siempre automáticamente (p. ej. un motivo
    por proveedor sin intervención), documentarlo en ADR 0011 y ADR 0008 y retirar los tres endpoints
    del contrato; pero esto contradice *"la sugerencia nunca decide sola"* de `REGLAS.md` §3.

### D5 — Carga manual de adjuntos: solo metadata, sin subida de bytes ni UI

- **Severidad:** Advertencia
- **Tipo:** Feature fantasma
- **Prometido:** ADR 0013 §Contexto y §"Dos tablas": *"la interfaz permite **adjuntar y eliminar
  archivos a mano**"*, con `AdjuntoManual` y borrado lógico con auditoría. PRD.md:66 (caso borde:
  correo sin medios probatorios). Criterio de éxito PRD.md:57 (100% archivadas con sus medios
  probatorios).
- **Real:** `POST /api/facturas/{id}/adjuntos` (`FacturaEndpoints.cs:28`, `RegistrarAdjuntoAsync`)
  registra **metadata** de un `AdjuntoManual` pero no recibe ni almacena los bytes del archivo; no
  hay endpoint de subida de contenido (`DocumentoEndpoints.cs` solo expone `GET .../contenido`). La
  SPA no tiene UI para adjuntar. `DELETE .../adjuntos/{adjuntoId}` existe.
- **Por qué importa:** El caso que motivó `AdjuntoManual` —el medio probatorio que llega tarde— no
  tiene salida real: se puede registrar la fila pero el archivo no está en el volumen, así que el
  empaquetado a Drive (cuando exista) no tendría qué subir.
- **Opciones:**
  - `CORREGIR CÓDIGO` — endpoint de subida multipart que escribe el archivo en el volumen compartido
    bajo la raíz configurable (ADR 0012/0013) y crea la fila `AdjuntoManual` con su `RutaRelativa`;
    UI de adjuntar/eliminar en detalle.
  - `ACTUALIZAR PRD/ADR` — declarar en ADR 0013 que la carga manual de adjuntos queda fuera de esta
    entrega y que el 100%-archivado del PRD depende de que el proveedor reenvíe.

### D6 — Percepción (`401131`) no cableada al pipeline

- **Severidad:** Advertencia
- **Tipo:** Feature fantasma
- **Prometido:** `REGLAS.md` §4 (cuenta `401131` IGV – Régimen de Percepciones), §5 (línea de
  percepción en el bloque principal), §10.4 (ejemplo completo con percepción). ADR 0006 §"Estructura
  del bloque principal" (columna Percepción). PRD.md:17 (asiento con IGV y neto).
- **Real:** La columna `fact.Factura.PercepcionOrig` existe
  (`SmartNet/SmartNetBD/schema/005_negocio.sql:36`) y `ComposicionDeAsiento` cubre el caso en el
  núcleo puro (golden §10.4 en el ítem #8), pero `SPRINT.md` ítem #24 declara explícitamente
  *"percepción (§10.4) quedan fuera de alcance"* y no hay ruta que pueble `PercepcionOrig` ni la
  proyecte al asiento sembrado.
- **Por qué importa:** Un comprobante con percepción registrado sin ella deja el abono al proveedor
  corto (total en vez de total + percepción) y el pasivo mal.
- **Opciones:**
  - `CORREGIR CÓDIGO` — extraer la percepción del XML, exponerla en el contrato de la factura y
    proyectarla en el sembrado del asiento (ítem seguimiento de #24).
  - `ACTUALIZAR PRD/ADR` — anotar en `REGLAS.md` §11 que la percepción queda fuera de esta entrega,
    junto a los otros puntos ⏳.

### D7 — Métrica de precisión de extracción (≥90%) sin superficie de reporte

- **Severidad:** Advertencia
- **Tipo:** Regla omitida
- **Prometido:** PRD.md:62 (*"La extracción automática por OCR/IA logra un mínimo del 90% de campos
  correctamente extraídos"*). ADR 0017 §"La métrica de precisión" (comparación partida por fuente
  XML/PDF, contra `FacturaExtraccion`).
- **Real:** `fact.FacturaExtraccion` se persiste al promover (ítem #7), lo que provee la evidencia
  por campo y fuente. No se encontró ningún cálculo ni endpoint ni pantalla que compute la métrica
  (agregada o partida) contra la factura corregida. Ningún ítem del backlog la reclama.
- **Por qué importa:** El PRD la fija como criterio de éxito medible; sin superficie de cálculo no
  se puede saber si se cumple, y ADR 0017 advierte que el agregado engaña si no se parte por fuente.
- **Opciones:**
  - `CORREGIR CÓDIGO` — un reporte (endpoint + pantalla, o consulta operativa) que compare
    `FacturaExtraccion` contra los valores corregidos, partido XML/PDF.
  - `ACTUALIZAR PRD/ADR` — degradar el criterio de éxito a "la evidencia por campo queda persistida
    para medición posterior manual" si no se va a construir el cálculo en esta entrega.

### D8 — Sincronización manual de Gmail/SBS: `NotImplementedError`

- **Severidad:** Advertencia
- **Tipo:** Deuda técnica ligada a una promesa
- **Prometido:** ADR 0004 §`CommandQueue` (`SINCRONIZAR_GMAIL`, `SINCRONIZAR_SBS`), ADR 0008
  (`POST /api/integraciones/{nombre}/sincronizar`). El *handoff* de Configuración muestra
  "Sincronizar ahora".
- **Real:** El endpoint `POST /api/integraciones/{nombre}/sincronizar` existe
  (`IntegracionEndpoints.cs:21`) y encola el comando, pero el consumidor Python de `CommandQueue`
  para `SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS` lanza `NotImplementedError` explícito (SPRINT.md ítem
  #17, SUGGESTIONS: *"`SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS` sin *wiring*… depende de #15/#16"*).
  `REPROCESAR` y `RECONECTAR` sí están conectados.
- **Por qué importa:** El botón "Sincronizar ahora" encola un comando que el worker rechaza; el
  usuario no recibe señal de que no pasó nada.
- **Opciones:**
  - `CORREGIR CÓDIGO` — cablear los dos comandos a los CLIs de ingesta (`cli_gmail`) y de tipo de
    cambio (`cli_tipo_cambio`) que ya existen.
  - `ACTUALIZAR PRD/ADR` — quitar "Sincronizar ahora" de la pantalla de Configuración hasta que los
    *handlers* existan.

### D9 — Sin guarda de unicidad de factura por identidad de comprobante

- **Severidad:** Advertencia
- **Tipo:** Deuda técnica ligada a una promesa
- **Prometido:** PRD.md:59 — *"Ante una factura duplicada (mismo ruc del proveedor + tipo de
  comprobante + número), el sistema la detecta y alerta antes de permitir un nuevo registro, **sin
  excepciones**."* PRD.md:35, PRD.md:69. ADR 0008 §"Casos de `409`" (duplicado sin resolver).
- **Real:** El índice `IX_Factura_Identidad` **no es único** (deliberado, para que el flujo de
  resolución de duplicados sea alcanzable — ADR 0005). La detección es un indicador `PosibleDuplicado`
  recalculado en ingesta y en `PATCH` (ítem #19), y un `409` al validar. Pero `SPRINT.md` ítems #26
  y #24 declaran un hueco abierto (#27): `ExisteIdentidadPreviaAsync` *"solo fija un indicador, nada
  impide una 2ª `fact.Factura` con el mismo RUC+tipo+serie+número"*, y el ítem #25 documenta una
  `fact.Factura` duplicada real ya creada (F96X-1230). Un par XML+PDF mal asociado puede además
  generar 2–3 filas de `InboxEvent` → varias facturas (SPRINT.md ítem #24 *follow-up* #28).
- **Por qué importa:** El "sin excepciones" del PRD no se cumple: hay caminos por los que una segunda
  factura con la misma identidad se crea sin bloqueo, solo con un chip de alerta.
- **Opciones:**
  - `CORREGIR CÓDIGO` — implementar el ítem #27 (guarda de unicidad al promover/validar) y la
    deduplicación de `InboxEvent` del par (ítem #28).
  - `ACTUALIZAR PRD/ADR` — matizar el criterio de éxito del PRD a *"detecta y alerta"* (indicador +
    `409` al validar) en vez de *"sin excepciones"*, si se acepta que la detección es por indicador.

### D10 — 23 motivos de caja chica reclasificados a origen `02` en el *seed*

- **Severidad:** Advertencia
- **Tipo:** Deuda técnica ligada a una promesa
- **Prometido:** `REGLAS.md` §3 y §11 punto 1 — *"23 fueron reclasificados desde `07 CAJA CHICA`
  por necesidad de la demostración. Contablemente son de caja chica y **debe revertirse antes de
  producción**."* ADR 0011 §"Alcance por origen de libro" (el origen del asiento siempre es `02`).
- **Real:** `SmartNet/SmartNetBD/schema/010_motivo_atributo_demo.sql` inserta incondicionalmente 23
  filas de demo en `fact.MotivoAtributo` al migrar (SPRINT.md fase 4 del ítem #1, y desviaciones
  documentadas en los ítems #3 y otros). La migración va en el mismo tren de esquema versionado que
  todo lo demás; no hay marca ni *feature flag* que la aísle de un despliegue de producción.
- **Por qué importa:** Un despliegue real arrastraría motivos contablemente incorrectos como
  seleccionables para el asiento de compras.
- **Opciones:**
  - `CORREGIR CÓDIGO` — mover el *seed* de demo a un script de *fixtures* fuera del tren de esquema
    versionado (como `SmartNet/SmartNetBD/fixtures/`), o gate por variable de entorno de entorno.
  - `ACTUALIZAR PRD/ADR` — si la demo es el único destino previsto, dejar constancia en ADR 0016 de
    que `010_*_demo.sql` es parte del esquema y que producción exige un script de reversión previo.

### D11 — Suite de integración del worker Python no se ejecuta (ruta del *runner* rota)

- **Severidad:** Sugerencia
- **Tipo:** Deuda técnica ligada a una promesa
- **Prometido:** ADR 0019 nivel 2 (pruebas de contrato de frontera contra el esquema versionado,
  matriz de permisos desde ambos lados) y nivel 3 (un extremo a extremo). ADR 0016 (el *runner* es
  la definición autoritativa).
- **Real:** `SPRINT.md` ítem #26 WARNING-1: *"`tests/integration/conftest.py` tiene `_RUNNER_PROJECT`
  apuntando a `SmartNet/db/runner/` cuando el runner vive en `SmartNet/SmartNetApi/db/runner/`, así
  que **toda** la suite `-m integracion` del worker se saltea."* Bug preexistente del arnés. La ruta
  documentada en ADR 0016 (`SmartNet/db/schema/`) tampoco coincide con la real
  (`SmartNet/SmartNetBD/schema/`).
- **Por qué importa:** El nivel 2 de ADR 0019 —la única mitigación declarada del riesgo de
  divergencia de tipos C#/Python— no se está verificando en la suite del worker; los tests existen
  pero no corren.
- **Opciones:**
  - `CORREGIR CÓDIGO` — corregir `_RUNNER_PROJECT` en `conftest.py` y correr `-m integracion`;
    actualizar las rutas de ejemplo en ADR 0016.
  - `ACTUALIZAR PRD/ADR` — n/a (es un defecto, no una decisión).

### D12 — ADR 0021 en estado "Propuesto" vs. dependencia ya en producción

- **Severidad:** Sugerencia
- **Tipo:** Deuda técnica ligada a una promesa
- **Prometido:** Consistencia entre el estado de un ADR y el código que lo implementa.
- **Real:** `adrs/0021-generacion-de-archivos-excel-en-la-api.md:5` dice *"Aceptado. Revisión 1.
  Ratificado por el dueño del proyecto el 2026-08-30"*, pero `SPRINT.md` ítem #22 *follow-ups* dice
  *"**ADR 0021 sigue en estado "Propuesto"** — el dueño debe pasarlo a "Aceptado" al ratificar el
  cambio."* El código (`SmartNet.Exportacion.Infrastructure`, `DocumentFormat.OpenXml`) ya está en
  `main`. Además ADR 0020 está marcado *"Propuesto"* aunque el ítem #14 que lo implementa está
  cerrado y verificado.
- **Por qué importa:** Un lector no puede distinguir qué ADRs son vinculantes y cuáles están en
  discusión.
- **Opciones:**
  - `ACTUALIZAR PRD/ADR` — conciliar el estado de ADR 0020 y ADR 0021 con la realidad (ambos
    implementados y verificados → "Aceptado"), o marcar el código como provisional si la decisión
    sigue realmente abierta.
  - `CORREGIR CÓDIGO` — n/a.

### D13 — El PRD sigue diciendo "tipo de cambio compra" en el cuerpo

- **Severidad:** Sugerencia
- **Tipo:** Feature no documentada (drift inverso, ya rastreado)
- **Prometido:** PRD.md:26, :32, :33, :95, :96 dicen "tipo de cambio compra" (dos veces bajo
  "Confirmado"). PRD.md:33 pide registrar `0.00` con observación si no hay tipo de cambio.
- **Real:** El código usa **tipo de cambio venta** (`ConversionDeMoneda`, golden §10.3/§10.7 con
  `TCventa`) y **rechaza con `409`** si no hay tipo de cambio, sin registrar `0.00`
  (`ServicioDeFacturas` gate `SinTipoCambio`, ADR 0018 puntos 1–3). Esto **es una reversión
  deliberada y está registrada** en la tabla de reversiones del propio PRD (PRD.md:123-129) y en ADR
  0018, con la ratificación pendiente en `REGLAS.md` §12. No es drift silencioso.
- **Por qué importa:** El cuerpo del PRD y su tabla de reversiones se contradicen; alguien que lea
  solo el cuerpo implementaría lo contrario de lo construido. La tabla de reversiones ya mitiga esto,
  pero el riesgo persiste mientras el cuerpo no lleve una marca.
- **Opciones:**
  - `ACTUALIZAR PRD/ADR` — añadir una nota al margen en las líneas afectadas del cuerpo del PRD que
    apunte a la tabla de reversiones (sin reescribir el texto original, para conservar el rastro).
  - `CORREGIR CÓDIGO` — solo si la ratificación del contador (`REGLAS.md` §12 puntos 1 y 5) resuelve
    a favor del PRD; en ese caso es reprocesar todo asiento en moneda extranjera, no un cambio de
    código.

## Deuda técnica detectada

- `SmartNet/SmartNetBD/schema/010_motivo_atributo_demo.sql` — *seed* de demo (23 motivos de caja
  chica reclasificados) en el tren de esquema versionado; `REGLAS.md` §11 punto 1 exige revertirlo
  antes de producción (D10).
- `SmartNet/SmartNetWorker/tests/integration/conftest.py` — `_RUNNER_PROJECT` con ruta obsoleta;
  toda la suite `-m integracion` del worker se saltea en silencio (D11).
- `fact.OutboxEventIntegracion` destinos `DRIVE`/`SHEETS` — filas que se acumulan `PENDIENTE` sin
  consumidor (ADR 0020 §Consecuencias lo declara); deja de ser inocuo en cuanto exista un destino
  real (D1, D2).
- `SINCRONIZAR_GMAIL` / `SINCRONIZAR_SBS` — `NotImplementedError` en el consumidor de `CommandQueue`
  (D8).
- `#27` (SPRINT.md) — sin guarda de unicidad de `fact.Factura` por identidad; ya existe una fila
  duplicada real (F96X-1230) pendiente de limpieza (D9).
- `#28` (SPRINT.md) — un comprobante XML+PDF genera 2–3 filas de `InboxEvent`; deduplicación de
  bandeja sin arrancar (D9).
- `SembradorDeAsientoAdapter` (ítem #24) — la lógica de "tragar fallos"
  (`SinTipoCambio`/`NoEncontrado`) no tiene test directo (SPRINT.md ítem #24 W1).
- Fixture sintética del *scraper* SBS (`tests/fixtures/sbs_tipo_cambio.html`) — el *parser* puede no
  coincidir con la página real (SPRINT.md ítem #4 WARNING 2); `sbs.gob.pe` está tras WAF.
- Respaldo y continuidad (ADR 0014) — no implementado; **declarado explícitamente como condición de
  puesta en producción, no como parte de la construcción**. No es drift, pero las tres preguntas de
  ADR 0014 §"Condiciones de puesta en producción" siguen sin responder.

## Features no documentadas (drift inverso)

- **Exportación a `.xlsx`** de las pantallas de catálogo (`SmartNet.Exportacion.Infrastructure`,
  `/api/catalogos/*/exportacion`, `/api/tipos-cambio/exportacion`,
  `/api/registro-compra/export`) — no está en el PRD; nace del *handoff* de diseño y está cubierta
  por ADR 0021 (cuyo estado hay que conciliar, D12). Vale la pena documentarla en el PRD como
  capacidad entregada.
- **Shell de navegación lateral** con 8 destinos (5 inertes) y pantallas de consulta de
  **Proveedores**, **Plan contable**, **Tipo de cambio** y **Registro de compra**
  (`SmartNet/SmartNetWeb/src/app/catalogos/`, `.../registro-compra/`, `.../shell/`) — nacen del
  *handoff*/`DESIGN.md`, no del PRD. Encajan en *"un software web único"* pero no son promesas
  explícitas; conviene reflejarlas en el Alcance del PRD.
- **Pantalla `/login`, `SmartNet.Admin` (CLI de restablecimiento), `fact.Sesion` como almacén de
  sesión, anillo de claves de Data Protection** — implementan ADR 0007/0014; son detalle de
  implementación de una promesa documentada, no drift.
- **`AfectacionMixta` / indicador "afectación no verificada" / `POST /confirmar-afectacion`** —
  cubiertos por `REGLAS.md` §8 y ADR 0017; no están en el PRD pero sí en documentos normativos
  posteriores. No es drift.
- **BACKLOG.md desactualizado** — los ítems #24, #25 y #26 están cerrados y verificados pero no
  añadidos a `BACKLOG.md` (SPRINT.md los lista como *follow-up* pendiente en varios ítems).

## Próximos pasos

- **Decisión de producto (dueño del proyecto):** ¿esta entrega incluye Drive (#15), Sheets/Looker
  (#16) y notas de crédito (#10), o se declaran fuera de alcance? De la respuesta dependen D1, D2,
  D3 y buena parte del "Objetivo" del PRD. Es la decisión que desbloquea el resto.
- **Decisión de producto:** ¿el asistente selecciona el motivo y reparte el cargo (ADR 0011,
  `REGLAS.md` §3), o se resuelve siempre automáticamente? De aquí sale si D4 es `CORREGIR CÓDIGO`
  (endpoints + UI de motivo) o `ACTUALIZAR ADR`.
- **Corrección de código (sin decisión pendiente):** D8 (cablear `SINCRONIZAR_*`), D11 (ruta del
  *runner* en `conftest.py`), D12 (estado de ADR 0020/0021), y la limpieza de la `fact.Factura`
  duplicada F96X-1230.
- **Decisión de arquitectura (dueño + criterio contable):** D9 — ¿se acepta "detecta y alerta" por
  indicador, o se exige la guarda de unicidad dura del ítem #27 para cumplir el "sin excepciones"
  del PRD?
- **Condición de producción (administrador de la instancia):** responder las tres preguntas de ADR
  0014 y planificar la reversión del *seed* de demo (D10) antes de cualquier operación con datos
  reales; ratificar las seis reglas de `REGLAS.md` §12 (D13 incluida).
- **Actualización de documentación:** añadir #24–#26 a `BACKLOG.md`; reflejar en el Alcance del PRD
  las pantallas de catálogo y la exportación `.xlsx` ya entregadas; anotar en el cuerpo del PRD la
  referencia a la tabla de reversiones (D13).
