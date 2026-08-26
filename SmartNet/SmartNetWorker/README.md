# SmartNet Worker (Python)

Paquete Python del repositorio con tres scripts de un solo run:

- **`cli_tipo_cambio.py`** (BACKLOG #4): scrapea el tipo de cambio publicado por la SBS, inserta
  una fila `Origen='SBS'` en `fact.TipoCambio` y deja registro del intento en
  `fact.EstadoIntegracion` (`Nombre='SBS'`).
- **`cli_gmail.py`** (BACKLOG #5): sondea un buzon Gmail etiquetado, descarga los adjuntos
  candidatos (etiqueta + extension permitida, ADR 0017), los persiste como `fact.Email` +
  `fact.DocumentoRecibido` (`Estado='DESCARGADO'`), aplica su propia etiqueta de "procesado" y deja
  registro del intento en `fact.EstadoIntegracion` (`Nombre='GMAIL'`).
- **`cli_procesamiento.py`** (BACKLOG #6): procesa `fact.DocumentoRecibido` pendiente
  (`Estado='DESCARGADO'` o un reintento vencido) — parsea XML/UBL como fuente autoritativa,
  extrae texto de PDF con OCR local (Tesseract) solo donde hace falta, asocia pares XML<->PDF por
  el RUC/tipo/serie/numero (ADR 0017), calcula `AfectacionMixta` (REGLAS.md §8) y deja registro del
  intento en `fact.EstadoIntegracion` (`Nombre='WORKER'`).

Los tres sin scheduler, sin polling en proceso, sin reintentos automaticos en el mismo proceso — la
ejecucion recurrente es un concern de despliegue (cron/Task Scheduler), no de este codigo.
`cli_procesamiento.py` se agenda **despues** de `cli_gmail.py`: solo tiene sentido correr sobre lo
que #5 ya descargo.

Ver `openspec/changes/tipos-de-cambio/design.md` (Decisiones 4-7),
`openspec/changes/ingesta-gmail/design.md` (Decisiones 1-7) y
`openspec/changes/extraccion-y-asociacion/design.md` (Decisiones 1-9) para las decisiones de
diseño, y `ADR 0003`/`ADR 0010`/`ADR 0017` para la particion de datos, la clasificacion de errores
y las reglas de Gmail/extraccion.

## Prerequisitos de sistema

Ademas de Python y el driver ODBC (ver "Instalacion" abajo), `cli_procesamiento.py` (BACKLOG #6)
necesita el binario **Tesseract** con el paquete de idioma **`spa`** instalados a nivel de sistema
— igual clase de prerequisito que el ODBC Driver 18: un binario que `pip` no puede instalar por
si solo (design.md, Decision 3/7).

- **Debian/Ubuntu** (y el job de CI `pruebas-de-worker-python`):

  ```bash
  sudo apt-get update
  sudo apt-get install -y tesseract-ocr tesseract-ocr-spa
  ```

- **Windows**:

  ```powershell
  winget install UB-Mannheim.TesseractOCR
  ```

  El instalador de Windows NO agrega el binario al `PATH` por defecto (se instala en
  `C:\Program Files\Tesseract-OCR\tesseract.exe`). Fijar la ruta completa via la variable de
  entorno opcional:

  ```
  SMARTNET_WORKER_TESSERACT_CMD
  ```

  ```powershell
  $env:SMARTNET_WORKER_TESSERACT_CMD = "C:\Program Files\Tesseract-OCR\tesseract.exe"
  ```

  A diferencia de la cadena de conexion, las credenciales de Gmail y la raiz de almacenamiento, la
  **ausencia** de esta variable es legitima (design.md, Decision 7): significa "esperar `tesseract`
  en el `PATH`", que es el caso normal en Linux/CI tras `apt-get install`.

`cli_procesamiento.py` corre una **preflight** (`pytesseract.get_tesseract_version()`) una unica
vez, antes del primer documento del run: si Tesseract no responde, el run entero aborta con **cero
filas escritas** — nunca un `PERMANENTE` por documento causado por una mala configuracion del host
(design.md, Decision 7).

## Instalacion

Requiere **Python 3.13+** y el **ODBC Driver 18 for SQL Server** instalado a nivel de sistema
(design.md, Open Questions — footnote de despliegue, no confirmado por ADR 0012).

```bash
cd SmartNet/SmartNetWorker
pip install -e .[dev]
```

Esto instala el paquete en modo editable junto con `pytest` y `ruff` (dependencias de desarrollo).

## Variables de entorno requeridas

```
SMARTNET_WORKER_ODBC_CONNECTION
```

Cadena de conexion ODBC completa hacia la base de datos, con las credenciales de `usr_worker`
(`008_usuarios_y_permisos.sql`). **Sin valor por defecto en el codigo** (design.md, Decision 5) —
el proceso lanza `ConfiguracionError` si esta variable no esta definida. Nunca se comitea un
secreto en este repositorio.

Ejemplo (no un valor real):

```bash
export SMARTNET_WORKER_ODBC_CONNECTION="DRIVER={ODBC Driver 18 for SQL Server};SERVER=<host>;DATABASE=BDSmartNet;UID=usr_worker;PWD=<password>;TrustServerCertificate=yes;"
```

Dos variables adicionales, requeridas solo por `cli_gmail.py` (BACKLOG #5, sin valor por defecto en
codigo, mismo principio que la cadena de conexion — ver `openspec/changes/ingesta-gmail/design.md`,
Decision 2):

```
SMARTNET_WORKER_GMAIL_CREDENTIALS
SMARTNET_WORKER_STORAGE_ROOT
```

- `SMARTNET_WORKER_GMAIL_CREDENTIALS`: el JSON completo `authorized_user` de OAuth
  (`client_id`, `client_secret`, `refresh_token`, `token_uri`) como **un unico secreto atomico** —
  nunca tres variables separadas que podrian rotarse de forma mutuamente inconsistente. Alcance
  `gmail.modify` unicamente; el flujo de consentimiento interactivo que genera este JSON es
  responsabilidad del lado .NET (`POST /api/integraciones/google/reconectar`, ADR 0015), fuera de
  alcance de este paquete.
- `SMARTNET_WORKER_STORAGE_ROOT`: raiz del volumen compartido (ADR 0013) donde el worker escribe
  los adjuntos descargados. El lado .NET la lee para servir la descarga; este proceso solo escribe.

Ejemplo (no un valor real):

```bash
export SMARTNET_WORKER_GMAIL_CREDENTIALS='{"client_id":"...","client_secret":"...","refresh_token":"...","token_uri":"https://oauth2.googleapis.com/token"}'
export SMARTNET_WORKER_STORAGE_ROOT="/mnt/smartnet-adjuntos"
```

## Correr los workers

```bash
python -m smartnet_worker.cli_tipo_cambio
python -m smartnet_worker.cli_gmail
python -m smartnet_worker.cli_procesamiento
```

O, tras la instalacion, mediante los entry points declarados en `pyproject.toml`:

```bash
smartnet-tipo-cambio
smartnet-gmail
smartnet-procesamiento
```

Codigo de salida `0` en exito, `1` en fallo — pensados para invocarse desde un scheduler externo
(fuera del alcance de estos ítems) o a mano. `cli_gmail.py` requiere ademas que las dos etiquetas
Gmail (`INGESTA.ETIQUETA_ORIGEN` e `INGESTA.ETIQUETA_PROCESADO`) ya existan en el buzon antes del
primer sondeo — el worker nunca las crea (design.md, Decision 3).

## Pruebas

Markers definidos en `pyproject.toml`: `integracion` (requiere SQL Server real), `externa`
(requiere red real hacia `sbs.gob.pe`, excluida de CI), `ocr` (requiere el binario Tesseract real —
a diferencia de `externa`, SI corre en CI, ver "Prerequisitos de sistema").

```bash
# Solo unitarias, sin red ni DB ni Tesseract — lo que corre en verificaciones-estaticas de CI
pytest -m "not integracion and not externa and not ocr"

# Con integracion real (requiere una instancia SQL Server desechable + el SDK de .NET en PATH —
# ver tests/integration/conftest.py para el detalle completo)
pytest -m integracion

# Con OCR real (requiere el binario Tesseract + el paquete de idioma 'spa' instalados —
# ver "Prerequisitos de sistema"). Corre en el job pruebas-de-worker-python de CI junto con
# `integracion`.
pytest -m ocr

# Scrape real contra sbs.gob.pe — deseleccionado por defecto, solo para verificacion manual
# tras un incidente (design.md, Testing Strategy, fila "Excluido de CI")
pytest -m externa
```

## Convencion (item #5 la reutiliza)

- `pyproject.toml` con `src/` layout es el unico empaquetado Python del repositorio — nunca un
  `requirements.txt` como segunda fuente de verdad de las versiones.
- IO (red, DB, reloj) vive **solo** en `cli_tipo_cambio.py`. Todo lo demas (`sbs.py`,
  `tipo_cambio_repo.py`, `estado_integracion.py`) es puro o recibe un cursor/objeto ya conectado —
  ADR 0019 aplicado a Python.
- Dinero y tipo de cambio: `Decimal(str(...))`, nunca `float` (CONVENTIONS.md).
- `ruff` para lint, `pytest` para pruebas — ya declarados en `config.yaml`
  (`per_runtime_commands_expected.python`).

## Limitaciones conocidas de esta implementacion

- **Fixture de la SBS ahora real** (actualizado 18/08/2026): la pagina real de `sbs.gob.pe` sigue
  detras de un WAF (Incapsula) que bloquea `curl`/WebFetch sin motor JS, pero un navegador real
  (Claude in Chrome) la renderiza sin problema. `tests/fixtures/sbs_tipo_cambio.html` es ahora una
  captura literal del subarbol real (tabla Telerik RadGrid + span de fecha) — ver
  `tests/fixtures/README.md` para el detalle. `sbs.py` fue ajustado contra esa estructura real: la
  URL correcta es `SISTIP_PORTAL/Paginas/Publicacion/TipoCambioPromedio.aspx` (la
  `EstadisticasSAEEPortal/...` original era incorrecta), y la pagina no tiene columna de fecha por
  fila ni hora de consulta — ambas se derivan del span `#ctl00_cphContent_lblFecha`
  ("Tipo de Cambio al dd/mm/aaaa"), usando medianoche como convencion explicita para
  `fecha_consulta` (`DATETIME2(3) NOT NULL` en el esquema).
- **BACKLOG #5, WU4 (cli_gmail.py + integracion real)**: a diferencia de WU1-WU3 de este mismo
  ítem, esta tanda SI tuvo una instancia SQL Server real y `dotnet` alcanzables en el entorno de
  implementacion. Se corrio la suite de integracion completa (`pytest -m integracion`) contra una
  base `fact_test_worker_<id>` efimera real, aplicando el esquema versionado completo via
  `SmartNet.Db.Runner` — **7/7 passed**, cero bases/logins huerfanos despues del run (verificado con
  `sqlcmd`). Esa corrida encontro y corrigio un bug real: `insertar_email` leia el `EmailId`
  generado con un `SELECT SCOPE_IDENTITY()` en un `execute()` separado del INSERT, que devuelve
  `NULL` con pyodbc porque un INSERT parametrizado se envia envuelto en `sp_executesql` — ese
  wrapper cierra su propio scope al retornar. El fix usa `OUTPUT INSERTED.EmailId` en el MISMO
  `execute` que el INSERT, que no tiene ese problema porque lee el valor dentro del mismo
  statement/scope. Suite unitaria completa: 81/81 passed; `ruff check src tests`: limpio.
- **BACKLOG #6, WU4 (cli_procesamiento.py + integracion real + `ocr`)**: la misma instancia SQL
  Server 2025 local real de WU4/#5 estaba alcanzable — se corrio `dotnet test
  SmartNet.Db.Runner.Tests --filter "FullyQualifiedName~BaseDataTests"` (33/33 passed, incluye
  `Configuracion_PendienteKeys_HaveValorAndValorPorDefectoBothNull("EMPRESA","RUC")`) y `pytest -m
  integracion` completo (11/11 passed, incluye las 4 pruebas nuevas de este item: `Procesamiento`+
  `DatosExtraidos`+`AfectacionMixta` reales, FK de asociacion en ambos lados,
  `CK_Procesamiento_NoAutoAsociacion` rechaza la auto-asociacion, `INSERT fact.FacturaExtraccion`
  falla por DENY) — cero bases/logins huerfanos despues (verificado con `sqlcmd`). Suite unitaria
  completa: `pytest -q -m "not integracion and not ocr"` → 163/163 passed; `ruff check src tests`:
  limpio.
  El marker **`ocr`** (nuevo, `tests/integration/test_ocr_real.py`) inicialmente no se pudo correr
  contra Tesseract real en este entorno de WU4: `winget install UB-Mannheim.TesseractOCR` fallo dos
  veces con `0x800704c7` (requiere elevacion UAC interactiva). La prueba quedo escrita con un
  fixture regenerado con texto real renderizado (`comprobante_escaneado.pdf`, imagen 1600x900 con
  RUC/serie/numero/monto reales via Pillow+Arial, sin capa de texto embebida) y un skip explicito y
  honesto (`TesseractNotFoundError`) cuando el binario falta.
  **Post-verify (sesion sdd-verify de BACKLOG #6): el usuario instalo Tesseract con permisos de
  administrador** (`winget install UB-Mannheim.TesseractOCR`), y se descargo `spa.traineddata`
  (paquete oficial de idioma espanol, `tesseract-ocr/tessdata` en GitHub — el instalador solo trae
  `eng`) a una carpeta local via `TESSDATA_PREFIX`. Con `SMARTNET_WORKER_TESSERACT_CMD` y
  `TESSDATA_PREFIX` fijados, `pytest -m ocr` **corrio de verdad contra Tesseract 5.4.0 real: 1
  passed**, y la suite completa `pytest -q -m "not integracion"` → **164/164 passed** (163 previos +
  el `ocr` real, ya no skippeado). El job de CI (`pruebas-de-worker-python`, `apt-get install
  tesseract-ocr tesseract-ocr-spa`) tambien lo corre en cada push.
- **Entorno de implementacion sin interprete de Python al empezar**: este Work Unit empezo en un
  entorno donde solo existia el stub de Microsoft Store para `python`/`py` (sin instalacion real,
  sin `pip`, sin `pytest`, sin `ruff`). Se instalo Python 3.13.15 con `winget install
  Python.Python.3.13` durante la implementacion — desviacion respecto al plan original de asumir
  el interprete disponible, documentada aqui en vez de dejarse en silencio. Con Python instalado
  se corrieron realmente: 17/17 pruebas unitarias en verde (`pytest -m "not integracion and not
  externa"`), `ruff check src tests` limpio, y **las 3 pruebas de integracion tambien en verde**
  contra una instancia SQL Server 2025 local real, aplicando el esquema versionado completo via
  `SmartNet.Db.Runner` y conectando como un LOGIN `usr_worker` efimero real — sin bases ni logins
  huerfanos despues del run (verificado con `sqlcmd`).
- **BACKLOG #14, Fase 5 (contrato bidireccional N2)**: la misma instancia SQL Server local real
  estaba alcanzable. `conftest.py::worker_db` gano un segundo LOGIN de instancia efimero real,
  `usr_api` (antes era `WITHOUT LOGIN`, tasks.md 5.2) — la primera vez que una prueba de #14 ejerce
  el GRANT/DENY real bajo ESE login, no solo bajo una conexion de confianza/sysadmin. Esa primera
  ejecucion real encontro y corrigio DOS bugs de produccion genuinos (detalle completo en
  `tasks.md`, Fase 5): (1) faltaba `GRANT UPDATE ON OBJECT::fact.SeqOutbox TO fact_api` — SQL Server
  exige permiso `UPDATE` sobre el objeto SEQUENCE para `NEXT VALUE FOR`, un permiso distinto del que
  008 ya da sobre la TABLA `fact.OutboxEvent`; sin el, el INSERT real de
  `SqlUnidadDeTrabajo.EmitirOutboxAsync` fallaba bajo el login `usr_api` real (error 229). Cerrado
  con una migracion NUEVA, `019_permiso_secuencia_seqoutbox.sql` (nunca se edito 008 in-place, ADR
  0016). (2) `outbox_repo.OutboxRepo.reclamar` fallaba con
  `pyodbc.ProgrammingError: No results.  Previous SQL was not a query.` contra un driver ODBC real
  (el fake-cursor unitario nunca reproduce el comportamiento de multiples result-sets) — corregido
  con `SET NOCOUNT ON;` al inicio de su plantilla SQL. Con ambos fijos: `pytest -m integracion`
  completo — **19/19 passed** (13 previos + 6 nuevos de Fase 5), cero bases/logins huerfanos despues
  (verificado con `sqlcmd`); suite unitaria completa `pytest tests/unit -q` → **210/210 passed**
  (208 previos + 1 contrato de payload + 1 pin de `SET NOCOUNT ON`); `dotnet build SmartNet.sln` →
  compilacion limpia; `dotnet test` de `SmartNet.Db.Runner.Tests` (134/134, incluye
  `PermissionMatrixTests` 27/27 sin duplicar ninguna) y `SmartNet.Facturacion.Infrastructure.Tests`
  (46/46) tambien en verde tras el fix.
