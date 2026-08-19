# Exploration: Ingesta Gmail (BACKLOG #5)

## Current State
- Todo el DDL de este ítem ya existe (ítem #1): `fact.Email`, `fact.DocumentoRecibido`
  (`SmartNet/db/schema/003_ingesta_y_procesamiento.sql`), `fact.Configuracion` con claves INGESTA
  sembradas (`ETIQUETA_ORIGEN`, `EXTENSIONES_PERMITIDAS`, `FRECUENCIA_SONDEO_MINUTOS`,
  `FECHA_INICIO` — todas sembradas con `Valor=NULL`, deliberadamente sin fijar, per
  `SmartNet/db/schema/009_datos_base.sql`), `fact.EstadoIntegracion` (fila `Nombre='GMAIL'`
  esperada), y los `GRANT` de `fact_worker` (SELECT/INSERT/UPDATE en `Email`/`DocumentoRecibido`,
  SELECT-only en `Configuracion`, SELECT/INSERT/UPDATE en `EstadoIntegracion`) todos en
  `SmartNet/db/schema/008_usuarios_y_permisos.sql`. **No se necesita DDL nueva**, misma situación
  que el ítem #4.
- `SmartNet/worker/` (Python) ya existe desde el ítem #4, cerrado. Su `cli_tipo_cambio.py` dice
  explícitamente en el docstring *"Sin scheduler, sin polling, sin reintentos — un solo run,
  deferido a #5"*, y `openspec/changes/tipos-de-cambio/proposal.md` dice *"#5 (Gmail ingestion),
  which needs a polling loop anyway and will reuse whatever tooling convention this item
  establishes."* **Esto confirma la hipótesis: #5 extiende el worker existente, no crea un tercer
  stack.** Patrones reusables: `config.py` (credenciales solo por env var, sin defaults en
  código), `estado_integracion.py` (`UPDATE ... WHERE Nombre=X`, raise si `rowcount != 1`), la
  separación `sbs.py` (parseo puro) / `cli_tipo_cambio.py` (único punto de IO), `tipo_cambio_repo.py`.
- ADR 0017 (`adrs/0017-frontera-del-motor-de-extraccion.md`) es esencialmente la spec completa de
  la porción de ingesta del #5: candidatura = etiqueta + extensión permitida solamente (ambas
  configurables, asunto/remitente nunca se usan), identidad del adjunto (`GmailMessageId`, nombre,
  extensión, MIME, `HashContenido` para idempotencia de reproceso per ADR 0010), Gmail write-back
  (el worker aplica su propia etiqueta "procesado", nunca borra, scope `gmail.modify`), y la
  consulta de sondeo acotada `label:<etiqueta-origen> -label:<etiqueta-procesado>
  after:<fecha-inicio-configurada>`.
- ADR 0013 (`adrs/0013-almacenamiento-y-entrega-de-documentos.md`): Python escribe los archivos
  descargados a un volumen compartido en una raíz configurable (ADR 0012),
  `fact.DocumentoRecibido.RutaRelativa` guarda la ruta relativa.
- ADR 0015 (`adrs/0015-secretos-credenciales-y-observabilidad.md`): Gmail usa OAuth de usuario, app
  "En producción" (nunca testing), scope `gmail.modify` solamente. El gestor de secretos dedicado
  (Vault) que prescribe está explícitamente fuera del alcance del backlog (tabla de infra/deploy).
- ADR 0003 confirma las tablas fact.* propiedad de Python para ingesta y las cuatro invariantes de
  partición.
- El alcance literal del BACKLOG #5 ("candidatura, descarga, hash, etiquetado, sondeo") mapea a
  `Email`+`DocumentoRecibido` solamente — no `Procesamiento`/`DatosExtraidos`/`InboxEvent`, que son
  de #6/#7.

## Affected Areas
- `SmartNet/worker/src/smartnet_worker/` — extender con wrapper del cliente Gmail, cómputo de
  hash, repositorio `Email`/`DocumentoRecibido` (espeja `tipo_cambio_repo.py`), registro de
  `EstadoIntegracion` generalizado más allá del módulo actual hardcodeado a SBS.
- `SmartNet/worker/src/smartnet_worker/config.py` — extender con carga de credenciales OAuth de
  Gmail y raíz del volumen compartido, ambas por env var (sin defaults), espejando
  `ODBC_CONNECTION_ENV_VAR`.
- `SmartNet/worker/pyproject.toml` — nueva dependencia para acceso a Gmail API
  (`google-api-python-client` + libs de auth).
- `fact.Configuracion` (solo lectura para el worker) — **gap encontrado**: la consulta de sondeo de
  ADR 0017 necesita un tercer término configurable, `<etiqueta-procesado>`, pero el ítem #1 solo
  sembró `ETIQUETA_ORIGEN`, no `ETIQUETA_PROCESADO`. Necesita una decisión explícita (¿migración
  SQL chica nueva vs. derivar la etiqueta por convención?).

## Approaches
1. **Punto de entrada CLI de un solo run** (`cli_gmail.py`), espeja `cli_tipo_cambio.py`
   exactamente — un ciclo de sondeo (consulta → descarga → hash → escritura → etiquetado) por
   invocación, el scheduling externo (cron/Task Scheduler) queda para deployment.
   - Pros: paridad con el patrón ya probado de #4, sin scheduler en proceso que probar/soportar,
     agnóstico de deployment.
   - Contras: no resuelve la pregunta abierta de TECH-DESIGN.md sobre "dos bucles encadenados de
     15 minutos" — solo la difiere a config de deployment.
   - Esfuerzo: Medio.
2. **Bucle de polling en proceso/daemon**, potencialmente compartido con el scraping SBS.
   - Pros: responde directamente al framing de TECH-DESIGN "bucle de sondeo"; un solo proceso
     monitoreado.
   - Contras: scope creep real (supervisión de proceso, apagado ordenado, scheduling
     cross-integración) — el ítem #4 difirió esto a #5 pero nunca exigió un proceso siempre-activo
     compartido vs. corridas disparadas externamente.
   - Esfuerzo: Alto.
3. **Enfoque 1, estrictamente acotado a `Email`/`DocumentoRecibido` solamente** (sin crear filas de
   `Procesamiento` — eso es trabajo de #6).
   - Pros: calza con la redacción del BACKLOG y con el grafo de dependencias #6-depende-de-#5
     exactamente; diff más chico y revisable, consistente con el precedente de alcance mínimo
     del #4.
   - Esfuerzo: Medio (misma forma técnica que #1, solo una aclaración de alcance).

## Recommendation
Combinar 1+3: un `cli_gmail.py` de un solo run, acotado estrictamente a poblar
`Email`+`DocumentoRecibido` (`Estado='DESCARGADO'`) y el write-back de etiqueta en Gmail, difiriendo
todo código de scheduler/daemon recurrente a deployment (mismo patrón que usó #4). Mantiene el diff
revisable y es consistente con el precedente del ítem anterior.

## Risks

- **Bloqueante — origen de las credenciales OAuth de Gmail**: no existe convención para
  obtener/refrescar un token de Gmail. ADR 0015 exige un gestor de secretos basado en Vault que
  está fuera del alcance del backlog. Alternativa propuesta: solo env vars (espeja `config.py`),
  pero es una superficie de seguridad real y debe plantearse, no asumirse en silencio.
- **Bloqueante — falta la clave `ETIQUETA_PROCESADO` en Configuracion**: ADR 0017 la necesita, el
  ítem #1 nunca la sembró.
- **Abierto — raíz del volumen compartido**: ADR 0013 exige una raíz configurable entregada a
  ambos runtimes; no existe todavía env var/config del lado del worker.
- **Abierto — forma del scheduler**: CLI de un solo run vs. bucle en proceso es un fork real
  (TECH-DESIGN.md marca explícitamente "frecuencia de sondeo sin fijar" como sin resolver);
  debería plantearse, no asumirse.
- **Pregunta de formato**: `EXTENSIONES_PERMITIDAS` es un valor de `Configuracion` tipado `LISTA`
  sin convención de delimitador establecida — el ítem #4 nunca consumió una clave `LISTA`.
- El alcance del hash (por adjunto, SHA-256) y la regla de candidatura (solo etiqueta+extensión)
  ya están resueltos por ADR 0017 — no son preguntas abiertas.

## Decisiones ya resueltas (no son preguntas abiertas)

- **Credenciales OAuth de Gmail**: solo env var (token/refresh-token), sin default en código —
  mismo patrón que `config.py` del ítem #4. Vault queda diferido, fuera de alcance.
- **`ETIQUETA_PROCESADO` faltante en `fact.Configuracion`**: se agrega vía una migración SQL chica
  nueva, consistente con cómo el ítem #1 sembró las demás claves INGESTA.
- **Forma del scheduler**: CLI de un solo run (`cli_gmail.py`), mismo patrón que `cli_tipo_cambio.py`
  del ítem #4 — un ciclo completo por invocación, scheduling recurrente diferido a deployment.
- **Raíz del volumen compartido**: env var (ej. `SMARTNET_WORKER_STORAGE_ROOT`), sin default en
  código, mismo patrón que las demás configs del worker.

## Ready for Proposal
Sí. Las cuatro preguntas bloqueantes/abiertas fueron resueltas por el usuario — ver arriba.
