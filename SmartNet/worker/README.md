# SmartNet Worker (Python)

Paquete Python del repositorio con dos scripts de un solo run:

- **`cli_tipo_cambio.py`** (BACKLOG #4): scrapea el tipo de cambio publicado por la SBS, inserta
  una fila `Origen='SBS'` en `fact.TipoCambio` y deja registro del intento en
  `fact.EstadoIntegracion` (`Nombre='SBS'`).
- **`cli_gmail.py`** (BACKLOG #5): sondea un buzon Gmail etiquetado, descarga los adjuntos
  candidatos (etiqueta + extension permitida, ADR 0017), los persiste como `fact.Email` +
  `fact.DocumentoRecibido` (`Estado='DESCARGADO'`), aplica su propia etiqueta de "procesado" y deja
  registro del intento en `fact.EstadoIntegracion` (`Nombre='GMAIL'`).

Ambos sin scheduler, sin polling en proceso, sin reintentos automaticos — la ejecucion recurrente
es un concern de despliegue (cron/Task Scheduler), no de este codigo.

Ver `openspec/changes/tipos-de-cambio/design.md` (Decisiones 4-7) y
`openspec/changes/ingesta-gmail/design.md` (Decisiones 1-7) para las decisiones de diseño, y
`ADR 0003`/`ADR 0017` para la particion de datos y las reglas de Gmail.

## Instalacion

Requiere **Python 3.13+** y el **ODBC Driver 18 for SQL Server** instalado a nivel de sistema
(design.md, Open Questions — footnote de despliegue, no confirmado por ADR 0012).

```bash
cd SmartNet/worker
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
```

O, tras la instalacion, mediante los entry points declarados en `pyproject.toml`:

```bash
smartnet-tipo-cambio
smartnet-gmail
```

Codigo de salida `0` en exito, `1` en fallo — pensados para invocarse desde un scheduler externo
(fuera del alcance de estos ítems) o a mano. `cli_gmail.py` requiere ademas que las dos etiquetas
Gmail (`INGESTA.ETIQUETA_ORIGEN` e `INGESTA.ETIQUETA_PROCESADO`) ya existan en el buzon antes del
primer sondeo — el worker nunca las crea (design.md, Decision 3).

## Pruebas

Markers definidos en `pyproject.toml`: `integracion` (requiere SQL Server real), `externa`
(requiere red real hacia `sbs.gob.pe`, excluida de CI).

```bash
# Solo unitarias, sin red ni DB — lo que corre en verificaciones-estaticas de CI
pytest -m "not integracion and not externa"

# Con integracion real (requiere una instancia SQL Server desechable + el SDK de .NET en PATH —
# ver tests/integration/conftest.py para el detalle completo)
pytest -m integracion

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
