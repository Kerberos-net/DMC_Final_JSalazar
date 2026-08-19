# SmartNet Worker (Python)

Primer paquete Python del repositorio (BACKLOG #4). Un script de un solo run que scrapea el tipo
de cambio publicado por la SBS, inserta una fila `Origen='SBS'` en `fact.TipoCambio` y deja
registro del intento (exito o fallo) en `fact.EstadoIntegracion`. Sin scheduler, sin polling, sin
reintentos automaticos — la ejecucion recurrente queda para el ítem #5.

Ver `openspec/changes/tipos-de-cambio/design.md` para las decisiones de diseño (Decisiones 4-7) y
`ADR 0003` para la particion de datos entre .NET y Python.

## Instalacion

Requiere **Python 3.13+** y el **ODBC Driver 18 for SQL Server** instalado a nivel de sistema
(design.md, Open Questions — footnote de despliegue, no confirmado por ADR 0012).

```bash
cd SmartNet/worker
pip install -e .[dev]
```

Esto instala el paquete en modo editable junto con `pytest` y `ruff` (dependencias de desarrollo).

## Variable de entorno requerida

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

## Correr el scraper

```bash
python -m smartnet_worker.cli_tipo_cambio
```

O, tras la instalacion, mediante el entry point declarado en `pyproject.toml`:

```bash
smartnet-tipo-cambio
```

Codigo de salida `0` en exito, `1` en fallo — pensado para invocarse desde un scheduler externo
(fuera del alcance de este ítem) o a mano.

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
