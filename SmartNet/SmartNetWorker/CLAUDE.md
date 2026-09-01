# SmartNetWorker — worker Python de ingesta y publicación

> El mapa del ecosistema (contratos entre repos, partición de datos, despliegue) vive en
> `../CLAUDE.md`. Aquí solo lo propio de este repo. Detalle operativo completo en `README.md`.

## Qué hace

Paquete Python (`src/` layout, `pyproject.toml` como único empaquetado — nunca `requirements.txt`).
Seis entry points, todos **single-run**: sin scheduler, sin polling en proceso, sin reintentos
automáticos en el mismo proceso. La recurrencia (cron / Task Scheduler) es un concern de
despliegue, fuera del código. Código de salida `0` éxito / `1` fallo.

| Entry point | Módulo CLI | Ítem | Rol |
|---|---|---|---|
| `smartnet-tipo-cambio` | `cli_tipo_cambio` | BACKLOG #4 | scrape SBS → `fact.TipoCambio` (`Origen='SBS'`) |
| `smartnet-gmail` | `cli_gmail` | BACKLOG #5 | sondeo Gmail etiquetado → `fact.Email` + `fact.DocumentoRecibido` (`DESCARGADO`) + adjuntos al volumen compartido |
| `smartnet-procesamiento` | `cli_procesamiento` | BACKLOG #6 | XML/UBL autoritativo + OCR PDF donde hace falta → `fact.Procesamiento` / `fact.DatosExtraidos` / `AfectacionMixta` |
| `smartnet-inbox` | `cli_inbox` | BACKLOG #7/#11 | publica `fact.InboxEvent` desde `fact.Procesamiento` no notificado |
| `smartnet-outbox` | `cli_outbox` | BACKLOG #14 | consume `fact.OutboxEvent` (handlers aún vacíos, se acumulan `PENDIENTE`) |
| `smartnet-command-queue` | `cli_command_queue` | BACKLOG #17 | consume `fact.CommandQueue` (handlers `NotImplementedError` explícito) |

Cada CLI habla con la base como el LOGIN `usr_worker` y registra el intento en
`fact.EstadoIntegracion` (`Nombre` ∈ `SBS` / `GMAIL` / `WORKER`; `cli_inbox` **no** escribe ahí a
propósito — no hay valor `INBOX` en `CK_EstadoIntegracion_Nombre`).

## Stack y prerequisitos de sistema

- **Python 3.13+**, **ODBC Driver 18 for SQL Server** (binario de sistema, `pip` no lo instala).
- **Tesseract** + paquete de idioma **`spa`** — solo para `cli_procesamiento`. En Linux/CI está en
  el `PATH` tras `apt-get install tesseract-ocr tesseract-ocr-spa`; en Windows instala en
  `C:\Program Files\Tesseract-OCR\` fuera del `PATH` → fijar `SMARTNET_WORKER_TESSERACT_CMD`.
  La **ausencia** de esa variable es legítima (significa "esperar `tesseract` en el PATH"), a
  diferencia de la cadena de conexión y los secretos, cuya ausencia lanza `ConfiguracionError`.
- `lxml`, `pypdf`/`pypdfium2`/`pytesseract`/`Pillow`, `google-api-python-client`, `pyodbc`.

## Levantar en local

```bash
cd SmartNet/SmartNetWorker
pip install -e .[dev]          # editable + pytest + ruff

# Variables de entorno (sin default en código — el proceso lanza si faltan):
export SMARTNET_WORKER_ODBC_CONNECTION="DRIVER={ODBC Driver 18 for SQL Server};SERVER=<host>;DATABASE=BDSmartNet;UID=usr_worker;PWD=<pwd>;TrustServerCertificate=yes;"
export SMARTNET_WORKER_GMAIL_CREDENTIALS='{"client_id":"...","client_secret":"...","refresh_token":"...","token_uri":"https://oauth2.googleapis.com/token"}'
export SMARTNET_WORKER_STORAGE_ROOT="/mnt/smartnet-adjuntos"
# opcionales según integración: SMARTNET_WORKER_TELEGRAM_CREDENTIALS, SMARTNET_WORKER_SMTP_CREDENTIALS, SMARTNET_WORKER_TESSERACT_CMD

python -m smartnet_worker.cli_tipo_cambio   # o el entry point: smartnet-tipo-cambio
```

`cli_gmail` requiere que las dos etiquetas Gmail (`INGESTA.ETIQUETA_ORIGEN`,
`INGESTA.ETIQUETA_PROCESADO` en `fact.Configuracion`) ya existan en el buzón — el worker nunca las
crea.

### Pruebas

```bash
pytest -m "not integracion and not externa and not ocr"   # unitarias puras — lo que corre en CI estático
pytest -m integracion   # requiere SQL Server desechable + dotnet en PATH (ver tests/integration/conftest.py)
pytest -m ocr           # requiere Tesseract real + 'spa'; SÍ corre en CI
pytest -m externa       # scrape real contra sbs.gob.pe; excluido de CI, solo verificación manual
ruff check src tests
```

## Decisiones de arquitectura propias

1. **ADR 0019 aplicado a Python.** IO (red, DB, reloj) vive **solo** en los módulos `cli_*.py`.
   Todo lo demás (`sbs.py`, `ubl.py`, `afectacion.py`, `errores.py`, los `*_repo.py`, …) es puro o
   recibe un cursor / objeto ya conectado. `errores.clasificar` es un lookup por tipo de excepción;
   `errores.proximo_reintento` recibe el instante como parámetro, nunca lee el reloj.
2. **`config.py` es la única fuente de variables de entorno y constantes de red.** Ningún secreto
   ni cadena de conexión con default en código (design.md Decision 5): la ausencia lanza
   `ConfiguracionError` antes de cualquier llamada de red. Credenciales multi-campo (Gmail,
   Telegram, SMTP) viajan como **un JSON atómico por variable**, nunca N variables sueltas que
   podrían rotarse inconsistentes. Los valores no secretos (chat id, destinatarios) vienen de
   `fact.Configuracion`, no del entorno.
3. **Clasificación de errores (ADR 0010).** `PERMANENTE` solo para lo que nunca produciría un
   comprobante (XML inválido, PDF corrupto/cifrado); **cualquier excepción no reconocida →
   `TRANSITORIO`** ("errar hacia transitorio ante la duda"). `DIFERIBLE`/`OBSOLETO` sin productor
   aquí — deliberadamente sin usar, no olvidados.
4. **Aislamiento por fila.** Los CLI de batch (`procesamiento`, `inbox`, `outbox`, `command-queue`)
   usan **una transacción propia por fila/evento**; un fallo en una fila no aborta el resto del
   lote. `outbox`/`command-queue` además separan reclamo (una transacción corta para todo el lote,
   libera locks de inmediato) del dispatch (transacción por evento).
5. **El esquema SQL sigue siendo del runner .NET.** `tests/integration/conftest.py` aplica el
   esquema versionado real invocando `SmartNet.Db.Runner` vía `dotnet` — nunca una
   reimplementación en Python del splitting de scripts (ADR 0016).

## Gotchas descubiertos trabajando aquí

- **`SELECT SCOPE_IDENTITY()` devuelve `NULL` con pyodbc.** Un INSERT parametrizado se envía
  envuelto en `sp_executesql`, que cierra su scope al retornar. Usar `OUTPUT INSERTED.<col>` en el
  **mismo** `execute` que el INSERT.
- **`pyodbc.ProgrammingError: No results. Previous SQL was not a query.`** en repos que hacen
  UPDATE...OUTPUT o multi-statement contra un driver ODBC real (el fake-cursor unitario nunca lo
  reproduce). Fix: `SET NOCOUNT ON;` al inicio de la plantilla SQL.
- **Permiso sobre SEQUENCE ≠ permiso sobre la tabla.** `NEXT VALUE FOR fact.SeqOutbox` exige
  `GRANT UPDATE ON OBJECT::fact.SeqOutbox`, distinto del GRANT sobre `fact.OutboxEvent`. Se cerró
  con migración nueva (`019_...`), nunca editando `008` in-place (ADR 0016).
- **`usr_worker` huérfano → SQL error 4060.** `008` es *create-if-absent*, no re-vincula un user
  huérfano. Fix: `ALTER USER usr_worker WITH LOGIN = usr_worker;` en `BDSmartNet`. No correr
  scripts de esquema sueltos fuera de orden — usar el runner.
- **Scraper SBS.** La URL correcta es `SISTIP_PORTAL/.../TipoCambioPromedio.aspx`. La página no
  trae fecha por fila ni hora de consulta: ambas se derivan del span
  `#ctl00_cphContent_lblFecha` ("Tipo de Cambio al dd/mm/aaaa"), usando **medianoche** como
  convención para `fecha_consulta`. El WAF (Incapsula) bloquea `curl`/WebFetch sin motor JS; un
  navegador real la renderiza. El fixture (`tests/fixtures/sbs_tipo_cambio.html`) es captura
  literal del subárbol real.
- **Dinero y tipo de cambio: `Decimal(str(...))`, nunca `float`** (CONVENTIONS.md).
- **Dos dialectos de cadena de conexión conviven en las pruebas de integración** y no son
  intercambiables: `SmartNet.Db.Runner` usa `Microsoft.Data.SqlClient` (ADO.NET: `Server=`,
  `Integrated Security=`); el arnés pyodbc usa ODBC (`DRIVER={...}`, `Trusted_Connection=`). Se
  construyen por separado del mismo host/base.
- **`cli_procesamiento` corre una preflight de Tesseract** (`get_tesseract_version()`) una vez
  antes del primer documento: si no responde, el run entero aborta con **cero filas escritas** —
  nunca un `PERMANENTE` por documento causado por mala config del host.

## Relación con el ecosistema

Depende de `SmartNetBD` (mismo esquema versionado, tablas de ingesta/procesamiento propiedad de
`usr_worker` con `DENY` cruzado sobre las de `usr_api`). Es **par asíncrono** de `SmartNetApi` — no
se llaman por HTTP: se comunican por tablas compartidas (`CommandQueue` que la API encola y el
worker consume; `Outbox`/`EstadoIntegracion` que el worker escribe y la API lee). Comparte con la
API un volumen de archivos: el worker escribe los adjuntos descargados, la API los lee para servir
la descarga. Nadie depende de este repo por código.
