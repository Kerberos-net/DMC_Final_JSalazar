# Deploy Plan — Gestor de Facturas de Compra (SmartNet)

Fecha: 2026-08-31
Estado: VERIFICADO (demo, host único Windows Server sobre Proxmox)

## Resumen del proyecto

Ecosistema de cuatro piezas que hoy conviven en un repo git pero se tratan como repos
independientes:

| Pieza | Rol | Stack | Artefacto desplegable |
|---|---|---|---|
| `SmartNetBD` | Contrato de datos: esquema SQL versionado (`schema/001..021`), permisos por rol, fixtures de catálogo externo | Scripts SQL Server + manifiesto de checksums | Los `.sql` (los aplica el runner de la API) |
| `SmartNetApi` | Backend HTTP transaccional: dominio contable, auth por sesión, endpoints `/api/*`, y **el aplicador del esquema** (`SmartNet.Db.Runner`) | .NET 10, ASP.NET Core Minimal API, ADO puro, DbUp | `SmartNet.Api` (servicio HTTP) + `SmartNet.Db.Runner` (CLI de una corrida) + `SmartNet.Admin` (CLI) |
| `SmartNetWorker` | Ingesta y publicación asíncrona: tipo de cambio (SBS), correo (Gmail), extracción XML/OCR | Python 3.13, `pyodbc`, `pytesseract`, `google-api-python-client` | Paquete Python (`pyproject.toml`), 6 entry points single-run, sin scheduler propio |
| `SmartNetWeb` | SPA de bandeja, detalle de factura, configuración | Angular 22 (signals, sin librería de estado) | Build estático (`dist/spa/browser/`) |

**Contratos de integración:**
- API ↔ Worker: **nunca por HTTP**. Se comunican por tablas compartidas (`CommandQueue`,
  `OutboxEvent`/`EstadoIntegracion`) y por un **volumen de archivos compartido** (el worker escribe
  adjuntos, la API los sirve).
- SPA ↔ API: único cliente HTTP, prefijo `/api/*`, cookie de sesión server-side, **sin CORS**
  (mismo origen tras proxy inverso es precondición de seguridad — ADR 0012).
- Partición de datos: `usr_api` / `usr_worker`, cada uno con GRANT sobre su porción del esquema y
  **DENY explícito cruzado** sobre la del otro (ADR 0003).

**CI/CD existente:** `.github/workflows/ci.yml` — 4 jobs, **solo pruebas** (estáticas sin DB, suite
completa contra SQL Server, worker Python contra SQL real, frontend). **No hay ninguna etapa de
despliegue ni artefactos publicados.**

**Infraestructura existente:** ninguna. El host físico/VM, el proxy inverso concreto y el origen
del certificado TLS **no están decididos** en ningún ADR. Contenedores descartados deliberadamente
(ADR 0012, desproporcionados para 3 artefactos y 1 usuario, más la licencia de SQL Server).

**Marco de esta demo (ADR 0014 rev. 3):** es una demostración académica, sin contabilidad real.
El plan de respaldo, el gestor de secretos (Vault) y el agregador de logs con alertas son
**condiciones de puesta en producción**, no requisitos de hoy. Este pase adopta el **mínimo
pragmático** para secretos y observabilidad (decisión del usuario, 2026-08-31) y **declara la
brecha** frente a ADR 0015.

---

## Sistema de deployment propuesto

### Topología objetivo (host único Windows Server)

```
                    facturas.empresa.local   (HTTPS :443, TLS terminado aquí)
                              │
                    ┌─────────┴─────────┐  Proxy inverso (Caddy)
                    │                   │
              /  → SPA estática   /api/* → 127.0.0.1:5080 (Kestrel, solo loopback)
                                          │
                                   SmartNet.Api  (Servicio de Windows)
                                          │
   ┌──────────────────────────────────────┼───────────────────────────────┐
   │                                      │                               │
 SQL Server (instancia compartida)   Volumen compartido            Task Scheduler
 BDSmartNet, esquema `fact`          C:\SmartNet\adjuntos           6 tareas → entry points
   ▲                                      ▲                          del worker Python
   │ usr_api / usr_worker                 │ lee API / escribe worker
   └── SmartNet.Db.Runner (una corrida por deploy, principal de despliegue, NO usr_api)
```

### Build

Todo el build ocurre en **GitHub Actions** (runner limpio, determinista), disparado por un tag
`deploy/vX.Y.Z` sobre `main`. Nada se compila en el host de producción.

| Artefacto | Comando | Salida |
|---|---|---|
| Esquema SQL | *(no compila)* copiar `SmartNet/SmartNetBD/schema/**` + verificar `checksums.txt` con `generate-checksums.ps1 -Verify` | `schema/` tal cual, con manifiesto validado |
| `SmartNet.Db.Runner` | `dotnet publish -c Release -r win-x64 --self-contained false` | `runner/` (framework-dependent, requiere .NET 10 Runtime en el host) |
| `SmartNet.Api` | `dotnet publish -c Release -r win-x64 --self-contained false` | `api/` |
| `SmartNet.Admin` | `dotnet publish -c Release -r win-x64 --self-contained false` | `admin/` |
| SPA | `npm ci && npm run build` en `SmartNet/SmartNetWeb` | `web/` (contenido de `dist/spa/browser/`) |
| Worker | `python -m build` (sdist + wheel) en `SmartNet/SmartNetWorker` | `worker/smartnet_worker-X.Y.Z-py3-none-any.whl` |

Determinismo: `dotnet publish` sobre lockfiles de NuGet, `npm ci` sobre `package-lock.json`,
`pip` sobre `pyproject.toml` con versiones fijadas. El TFM es `net10.0` en los tres ejecutables.

### Artifact

Un único **paquete de release** versionado y trazable:

```
smartnet-vX.Y.Z.zip
  ├─ VERSION            # "vX.Y.Z  <git-sha-corto>  <fecha ISO>"
  ├─ schema/            # scripts .sql + checksums.txt (validado en build)
  ├─ runner/            # SmartNet.Db.Runner publicado
  ├─ api/               # SmartNet.Api publicado
  ├─ admin/             # SmartNet.Admin publicado
  ├─ web/               # SPA estática compilada
  ├─ worker/            # wheel del worker Python
  └─ deploy/            # scripts de despliegue (ver GENERATE), Caddyfile de ejemplo
```

- Trazabilidad: el tag `deploy/vX.Y.Z` apunta a un commit; el `.zip` lleva el SHA en `VERSION`;
  GitHub lo publica como **artefacto del workflow** y como **GitHub Release** adjunto al tag.
- El host descarga ese `.zip` por número de versión — **nunca hace `git pull` ni compila**.
- Se conservan los **últimos 3** `.zip` en el host (`C:\SmartNet\releases\`) para poder revertir.

### Config & Secrets

**Regla del proyecto:** ni la API ni el worker tienen `appsettings.json` / `requirements.txt` —
config ausente = fallo de arranque ruidoso, deliberado. Variables de entorno **distintas por
principal** (reusar un nombre daría rights de más al proceso equivocado).

| Variable | Consumidor | Tipo | Dónde vive |
|---|---|---|---|
| `SMARTNET_DB_CONNECTION` | `SmartNet.Db.Runner` | secret (principal de despliegue) | Config protegida del servicio de despliegue (solo durante el deploy) |
| `SMARTNET_API_DB_CONNECTION` | `SmartNet.Api` | secret (`usr_api`) | Entorno del Servicio de Windows `SmartNetApi` (ACL: solo la cuenta del servicio + admin) |
| `SMARTNET_API_STORAGE_ROOT` | `SmartNet.Api` | config | ídem |
| `SMARTNET_API_KEYRING_PATH` | `SmartNet.Api` | config | ídem → `C:\ProgramData\SmartNet\dataprotection-keys` |
| `SMARTNET_WORKER_ODBC_CONNECTION` | worker | secret (`usr_worker`) | Config de cada tarea de Task Scheduler (cuenta de servicio del worker) o archivo `.env` con ACL restringida fuera del checkout |
| `SMARTNET_WORKER_GMAIL_CREDENTIALS` | worker | secret (JSON atómico) | ídem |
| `SMARTNET_WORKER_STORAGE_ROOT` | worker | config | ídem (mismo disco físico que la API) |
| `SMARTNET_WORKER_TELEGRAM_CREDENTIALS` / `_SMTP_CREDENTIALS` | worker | secret (JSON atómico, opcionales) | ídem |
| `SMARTNET_WORKER_TESSERACT_CMD` | worker | config (opcional) | `C:\Program Files\Tesseract-OCR\tesseract.exe` |

**Mínimo pragmático (decisión 2026-08-31):**
- Secretos vía **configuración protegida del servicio**: variables de entorno del Servicio de
  Windows y de las tareas programadas, más un archivo `secrets\worker.env` con ACL `Administrators`
  + cuenta de servicio, **fuera del árbol de trabajo de git**.
- El token de Telegram se captura desde la UI (ADR 0015): la API necesita **escribir** su secreto
  en caliente. En el mínimo pragmático esto se resuelve escribiendo a `fact.Configuracion` /
  archivo protegido, **no** a un gestor con rotación.
- **Brecha declarada frente a ADR 0015** (ver "Deuda de puesta en producción" al final).

**Nunca en el repo, nunca en logs.** El `.gitignore` ya excluye artefactos; añadir `secrets/` y
`*.env` explícitamente (GENERATE).

### Infraestructura

**Elección para este proyecto:** un **host único Windows Server** con:

1. **Proxy inverso: Caddy** (binario único, `Caddyfile` de ~10 líneas, corre como Servicio de
   Windows). Termina TLS, sirve `web/` en `/`, hace `reverse_proxy /api/* 127.0.0.1:5080`.
   Añade compresión, cabeceras comunes y límite de tasa en un solo lugar (lo que ADR 0012 pide de
   un proxy separado).
   - *Alternativa considerada:* IIS + ARR (Application Request Routing). Más "esperado" en Windows
     Server pero mucha más configuración (módulo ANCM, web.config, reglas de reescritura). Caddy
     da el mismo resultado con un archivo. **Recomendación: Caddy.**
2. **`SmartNet.Api` como Servicio de Windows.** Kestrel enlazado **solo a `127.0.0.1:5080`**
   (`ASPNETCORE_URLS`). Requiere añadir `Microsoft.Extensions.Hosting.WindowsServices` +
   `builder.Host.UseWindowsService()` (GENERATE — cambio mínimo, guardado por el hecho de que hoy
   no hay binding explícito).
3. **Worker: Task Scheduler.** El worker no tiene proceso propio (por diseño). Seis tareas, una
   por entry point, con cadencias distintas (ver Verify & Observe).
4. **SQL Server:** instancia **compartida** ya existente. Este proyecto **no** la administra, no
   fija su modelo de recuperación ni su política de respaldo (ADR 0014). Los LOGIN `usr_api` /
   `usr_worker` y la base `BDSmartNet` los crea **el administrador de la instancia** antes del
   primer deploy (`008` hace `THROW` si faltan).
5. **Volumen compartido:** carpeta local `C:\SmartNet\adjuntos` (mismo host → mismo disco). La API
   y el worker reciben la raíz por variable; la base guarda la parte relativa.
6. **.NET 10 Runtime (ASP.NET Core)** y **Python 3.13** + **ODBC Driver 18 for SQL Server** +
   **Tesseract OCR + idioma `spa`** instalados en el host como prerequisitos de sistema (no los
   instala el deploy; los verifica).

### Entornos

ADR 0012 define **producción** + un **entorno de pruebas** con su propia cuenta de Google, carpeta
de Drive y hoja de cálculo.

| | Producción | Pruebas |
|---|---|---|
| Qué cambia | — | Credenciales de Google, `STORAGE_ROOT`, base de datos, host name |
| Qué NO cambia | El código. El artefacto es **idéntico**; solo cambia la config/secretos por entorno | |

Para este pase: **solo se levanta producción**. El entorno de pruebas queda documentado como
parametrización (`deploy/config.<entorno>.ps1`), no se stand-up ahora.

### Estrategia de release

**Direct (reemplazo in situ), una sola instancia.** Justificación para este proyecto:

- Un usuario, un host, sin requisito de disponibilidad continua (ADR 0014: "esto es recuperación
  ante pérdida, no continuidad de servicio").
- Blue/green y canary son desproporcionados — el mismo razonamiento por el que se descartaron los
  contenedores (ADR 0012).
- Ventana de indisponibilidad de la API: **segundos** (parar servicio → copiar `api/` → arrancar).
  El worker es single-run: una tarea en vuelo termina o falla con exit 1 y Task Scheduler la
  reintenta en la siguiente ventana; ninguna corrida nueva arranca durante el deploy porque el
  script deshabilita las tareas primero.
- La SPA se puede publicar **sin tocar el backend** (copiar `web/` y recargar Caddy) — una de las
  razones de ADR 0012 para tener proxy separado.

**Orden de despliegue (ADR 0012, no negociable):**

```
1. Deshabilitar las 6 tareas del worker en Task Scheduler
2. SmartNet.Db.Runner  → aplica schema/001..021 (idempotente; no-op si ya está al día)
3. Parar servicio SmartNetApi → desplegar api/ → arrancar servicio
4. Desplegar web/ → recargar Caddy
5. Actualizar el venv del worker con el wheel nuevo
6. Re-habilitar las 6 tareas del worker
```

### Data & Migrations

- **Forward-only, SQL versionado, aplicado por DbUp** (ADR 0016). Nunca EF Core, nunca Alembic.
- Journal en `fact.SchemaVersions` (no `dbo.*`). DbUp anota el **nombre** del script, nunca
  re-lee su contenido → `checksums.txt` + `ChecksumManifestTests` es lo único que detecta que un
  script aplicado fue editado. **Gate de deploy:** el build falla si el manifiesto no cuadra.
- Todos los scripts son **idempotentes / convergentes**: re-aplicar contra una base ya migrada es
  un no-op. El runner es seguro de correr en cada deploy.
- **`schema/rollback/NNN_down.sql` es advisory — el runner NUNCA lo ejecuta.** Solo sirve para
  revertir a mano y solo dentro de la ventana de bootstrap (`fact` aún vacío).
- **Un rollback de código NO es un rollback de datos.** Si `vX.Y.Z` añadió una columna y se
  revierte el código a `vX.Y.(Z-1)`, la columna **se queda**. El código viejo debe seguir
  funcionando contra el esquema nuevo (los scripts son aditivos: `ADD COLUMN` nullable, nuevas
  tablas, `GRANT`). Revertir un cambio de esquema destructivo requiere un **script forward nuevo**
  (numerado), discutido, nunca editar uno aplicado ni correr un `_down` en producción.
- Migración de datos entre sistemas: **fuera de alcance por decisión de producto** (regla 5 del
  proyecto).
- **Catálogo externo `dbo.*` (5 tablas):** en producción real las mantiene el sistema contable y
  este proyecto solo tiene `SELECT` (ADR 0003); `008` hace `GRANT SELECT` sobre ellas. En la demo
  la base está vacía, así que `deploy.ps1` aplica `fixtures/010` (DDL) + `fixtures/020` (datos desde
  los CSV del paquete) **antes** del runner, vía `apply-catalog-fixtures.ps1`, gated por
  `$AplicarFixturesCatalogoDemo`. En un deploy real ese flag va en `$false` — `020` hace
  `DELETE FROM dbo.Proveedor / CuentaContable / …`.

### Deploy gates

Antes de que un release proceda:

1. **CI verde** en el commit del tag — los 4 jobs (`ci.yml`).
2. **Manifiesto de checksums del esquema válido** (`generate-checksums.ps1 -Verify` en el build).
3. El tag `deploy/vX.Y.Z` está **sobre `main`** (no sobre una rama).
4. **Prerequisitos de sistema del host verificados** por `deploy/preflight.ps1`: .NET 10 Runtime,
   Python 3.13, ODBC Driver 18, Tesseract + `spa`, base `BDSmartNet` alcanzable, LOGIN
   `usr_api`/`usr_worker` existen, carpeta de keyring escribible, volumen de adjuntos escribible.
5. **Autorización explícita del usuario** para cada acción de EXECUTE que toque el host, la base o
   los secretos (ver "Autorizaciones pendientes").

### Verify & Observe

**Verificación post-deploy** (`deploy/verify.ps1`, se ejecuta al final y su resultado se registra):

| Paso | Comprobación | Éxito |
|---|---|---|
| Esquema | `SmartNet.Db.Runner` exit code + `SELECT COUNT(*) FROM fact.SchemaVersions` | exit 0; ≥ 21 filas |
| API viva | `GET https://facturas.empresa.local/api/sesion` (sin cookie) | **401** con cuerpo plano (es el sano-no-autenticado; la API nunca redirige) |
| TLS | El certificado de `facturas.empresa.local` valida contra la CA esperada | sin warning |
| SPA servida | `GET https://facturas.empresa.local/` | 200, HTML del `index.html` de Angular |
| SPA↔API real | Correr el harness `integration-spa-api` contra la instancia, o login manual + carga de la bandeja | bandeja responde 200 |
| Worker | Correr `smartnet-tipo-cambio` una vez a mano | exit 0; fila `SBS` y `WORKER` de `fact.EstadoIntegracion` con `UltimoExito` reciente |
| Sin huérfanos | 0 bases `fact_test_*`, 0 LOGIN `usr_worker` efímeros (si el host se usó para pruebas) | 0 / 0 |

**Observabilidad (mínimo pragmático):**
- API: al correr como Servicio de Windows, `UseWindowsService()` registra el logger de **Windows
  Event Log** (fallos de arranque y errores del host quedan en el Registro de eventos). Un logger a
  **archivo con rotación** para la API es un cambio de código pendiente (§ Deuda de puesta en
  producción) — hoy no está.
- Worker: `deploy/run-worker-entry.ps1` escribe a `C:\SmartNet\logs\worker\<cli>.log` con rotación
  simple (10 archivos, 5 MB); exit code 0/1 lo registra Task Scheduler (historial de la tarea).
- Caddy: access log a `C:\SmartNet\logs\caddy\access.log` con rotación (`roll_size`/`roll_keep`).
- **Latido del worker:** consulta manual periódica de `fact.EstadoIntegracion` fila `WORKER`
  (`UltimoExito`). La **alerta automática por ausencia de latido** (ADR 0015 rev. 2) queda como
  deuda de producción.

**Cadencia de las tareas del worker (punto de partida, ajustable):**

| Tarea | Entry point | Cadencia sugerida |
|---|---|---|
| Tipo de cambio | `smartnet-tipo-cambio` | Diaria, 08:00 (días hábiles) |
| Ingesta Gmail | `smartnet-gmail` | Cada 15 min |
| Procesamiento | `smartnet-procesamiento` | Cada 15 min (desfasado 5 min de Gmail) |
| Inbox | `smartnet-inbox` | Cada 15 min |
| Outbox | `smartnet-outbox` | Cada 30 min |
| Command queue | `smartnet-command-queue` | Cada 5 min |

### Recovery

| Falla | Recuperación |
|---|---|
| El runner falla a mitad | DbUp usa **transacción por script**: el script que falló se revierte entero, los previos quedaron aplicados. Corregir con un **script forward nuevo** y re-desplegar. Nunca editar el script aplicado. |
| La API no arranca tras el deploy | Parar servicio → restaurar `api/` desde `C:\SmartNet\releases\smartnet-v(anterior).zip` → arrancar. El esquema aditivo garantiza que el código anterior funciona. |
| La SPA nueva está rota | Copiar `web/` del release anterior + recargar Caddy. No toca el backend. |
| Una corrida del worker falla | Exit 1; la fila queda sin procesar (aislamiento por fila). Task Scheduler la reintenta en la siguiente ventana. Corridas idempotentes (`insert-if-absent`). |
| Se pierde el keyring de Data Protection | Toda cookie de sesión viva deja de descifrar en el próximo reinicio → los usuarios vuelven a loguearse. **Mitigación:** el deploy **preserva** `C:\ProgramData\SmartNet\dataprotection-keys` (nunca lo sobrescribe) y `deploy/backup.ps1` lo copia antes de cada release (ADR 0014, ítem #2). |
| `usr_api` / `usr_worker` huérfano → error 4060 | `ALTER USER usr_api WITH LOGIN = usr_api;` en `BDSmartNet` (documentado en memoria del proyecto). |
| Error 229 en `/api/bandeja` (DENY sobre `ProcesamientoError`) | Alguien corrió `008` suelto. Re-correr el runner completo (re-aplica `018`, idempotente). **Nunca correr scripts sueltos.** |
| Pérdida de datos de la base | **No es una operación de este proyecto** (ADR 0014): la base es compartida con el sistema contable de la empresa; restaurarla revierte también su contabilidad. El procedimiento y la decisión de ejecutarlo son del administrador de la instancia. |
| Pérdida de secretos | En el mínimo pragmático, los secretos viven en config de servicio + `secrets\worker.env`. `deploy/backup.ps1` los copia a `C:\SmartNet\backups\`. Recuperación = re-poblar desde esa copia o re-emitir credenciales de Google/Telegram. |

---

## Deuda de puesta en producción (brecha declarada vs. ADRs)

Este pase adopta el mínimo pragmático de la demo. Antes de registrar facturas reales:

1. **ADR 0015 — gestor de secretos dedicado** (Vault candidato) con escritura en caliente,
   rotación y auditoría de acceso. Hoy: config de servicio + archivo con ACL.
2. **ADR 0015 — agregador de logs** (Seq / Loki) con búsqueda, retención y **alertas por patrón**,
   `CorrelationId` propagado a los 3 artefactos, **alerta por ausencia de latido del worker** y
   **alerta por espacio libre**. Hoy: archivos con rotación + consulta manual.
3. **ADR 0014 — respaldo:** responder las 3 preguntas al administrador de la instancia (modelo de
   recuperación `SIMPLE`/`FULL`, cadena de `LOG BACKUP` existente, RPO efectivo vs. 15 min).
   Montar la copia diaria del **volumen de adjuntos antes** que la de la base. Prueba de
   restauración en entorno de prueba.
4. **ADR 0012 — certificado TLS:** decidir entre **autoridad interna** (cert propio para
   `facturas.empresa.local`, distribuir la raíz a los equipos) y **dominio público con DNS
   interno** (subdominio real resuelto internamente, habilita Let's Encrypt / cert comprado).
   Let's Encrypt sobre `.local` **no es viable** (RFC 6762). Hasta decidirlo, `deploy/` usa
   `tls internal` de Caddy (CA local de Caddy) para no bloquear la demo.
5. **ADR 0012 — entorno de pruebas** con su propia cuenta de Google / Drive / Sheet, realmente
   stood-up.
6. **Caddy como Servicio de Windows real** vía WinSW o NSSM, en lugar de la tarea programada
   AtStartup del mínimo pragmático (Caddy no implementa el protocolo del SCM por sí solo).
7. **Logger a archivo con rotación para la API** (hoy solo Event Log al correr como servicio).

---

## Autorizaciones pendientes

Ninguna acción de EXECUTE se corre sin tu OK explícito para esa acción concreta, en ese momento.
Cuando lleguemos a EXECUTE necesitaré autorización, una por una, para:

1. Correr `deploy/preflight.ps1` en el host (solo lectura, pero toca el host).
2. Correr `SmartNet.Db.Runner` contra `BDSmartNet` (modifica el esquema de una base **compartida**).
3. Instalar/configurar el Servicio de Windows `SmartNetApi`.
4. Instalar/configurar Caddy como Servicio de Windows y su `Caddyfile`.
5. Crear las 6 tareas en Task Scheduler.
6. Escribir los secretos en la config de servicio / `secrets\worker.env`.
7. Correr `deploy/verify.ps1`.

Ninguna está aprobada de antemano.

---

## Artefactos generados (fase GENERATE, 2026-08-31)

| Archivo | Qué es |
|---|---|
| `.github/workflows/deploy-build.yml` | Workflow de build+empaquetado, disparado por tag `deploy/v*`. Gates: tag sobre `main` + manifiesto de checksums. Publica `smartnet-vX.Y.Z.zip` como GitHub Release. |
| `deploy/README.md` | Runbook del operador. |
| `deploy/config.example.ps1` | Plantilla de config/secretos por entorno (`config.*.ps1` en `.gitignore`). |
| `deploy/_common.ps1` | Helpers compartidos (logging, `Assert-Admin`, `Invoke-Sql`, `Restrict-Acl`). |
| `deploy/preflight.ps1` | Verificación de prerequisitos del host (solo lectura). |
| `deploy/install-services.ps1` | Crea los Servicios de Windows `SmartNetApi` + `SmartNetCaddy`. |
| `deploy/register-worker-tasks.ps1` | Registra las 6 tareas de Task Scheduler. |
| `deploy/run-worker-entry.ps1` | Wrapper que Task Scheduler invoca por entry point (carga secretos, rota logs). |
| `deploy/deploy.ps1` | Orquestador del deploy en el orden de ADR 0012. |
| `deploy/verify.ps1` | Verificación post-deploy. |
| `deploy/backup.ps1` | Copia keyring + secretos antes de cada release. |
| `deploy/Caddyfile.example` | Config del proxy inverso (mismo origen, `tls internal` provisional). |

**Cambio de código (mínimo, requerido por la topología):**

| Archivo | Cambio |
|---|---|
| `SmartNet/SmartNetApi/api/SmartNet.Api/Program.cs` | `builder.Host.UseWindowsService();` — no-op fuera del SCM de Windows; `dotnet run`/`dotnet test`/`WebApplicationFactory` no se ven afectados. |
| `SmartNet/SmartNetApi/api/SmartNet.Api/SmartNet.Api.csproj` | `PackageReference` a `Microsoft.Extensions.Hosting.WindowsServices` 10.0.0. |
| `.gitignore` | Excluye `deploy/config.*.ps1` (salvo el ejemplo) y `*.env`. |

Verificado en GENERATE: `dotnet build SmartNet.Api` en Release compila limpio (0 warnings, 0
errores) con el paquete nuevo; los 9 scripts de `deploy/` parsean sin error; el YAML del workflow
es válido. **Pendiente de CI:** la suite de `SmartNet.Api.Tests` (necesita SQL Server) confirma que
`StartupFailsFastTests` / `SmartNetApiFactory` siguen levantando el host con el cambio.

## Registro de ejecución y verificación

### 2026-08-31 — Build local del paquete (ruta "build local + copiar", decisión del usuario)

En vez del flujo CI (tag → Release), se compiló el paquete en la máquina de desarrollo con los
mismos comandos del workflow:

- `dotnet publish -c Release -r win-x64 --self-contained false` → `runner/`, `api/`, `admin/` — OK.
- `npm ci && npm run build` en `SmartNetWeb` → `web/` (bundle generado en 15.7 s) — OK.
- `python -m build --wheel` → `worker/smartnet_worker-0.1.0-py3-none-any.whl` — OK.
- `schema/` (21 scripts + `checksums.txt`, manifiesto verificado al día) y `deploy/` copiados.
- Empaquetado: `smartnet-v1.0.0.zip` (~10.3 MB) en el scratchpad de la sesión.

**Salvedad de trazabilidad:** el `VERSION` del paquete referencia el commit `480d5bc`, pero el
cambio de código `builder.Host.UseWindowsService()` + el `PackageReference` **todavía no están
commiteados**, así que el `SmartNet.Api.exe` empaquetado no corresponde 1:1 a ese SHA. Se
recomienda commitear `deploy/`, el workflow y el cambio de código antes de seguir, y en el próximo
release usar el flujo CI para cerrar esta brecha.

### EXECUTE en la VM (Windows Server sobre Proxmox) — modo Opción A

El usuario ejecuta cada script en la VM como Administrador, con guía paso a paso.

- **2026-08-31** — Prerrequisitos + `instance-bootstrap.example.sql` + `config.prod.ps1`: el usuario
  los preparó en la VM (`C:\SmartNet\deploy-kit`).
- **Bug encontrado al correr `preflight.ps1`:** `Import-DeployConfig` en `_common.ps1` hacía `. $cfg`
  dentro de la función → las variables de config quedaban en el scope de la función, no del script;
  con `Set-StrictMode` reventaba con *"The variable '$TesseractCmd' cannot be retrieved"*.
  **Fix:** `Import-DeployConfig` ahora promueve cada variable conocida de config al scope del
  llamador (`Set-Variable -Scope 1`). Verificado localmente. `smartnet-v1.0.0.zip` regenerado.
  Los call sites de los 7 scripts no cambian.
- **Bug 2 encontrado al correr `preflight.ps1`:** `Invoke-Sql` llamaba a `sqlcmd` sin `-C`; ODBC
  Driver 18 cifra y valida la cadena de certificación por defecto, y el cert de la instancia local
  es autofirmado → *"La cadena de certificación fue emitida por una entidad en la que no se
  confía"*. **Fix:** `-C` (trust server certificate) agregado a `Invoke-Sql` y al ejemplo de
  `instance-bootstrap.example.sql`. Mismo criterio que `TrustServerCertificate=True` en las cadenas
  de config. `smartnet-v1.0.0.zip` regenerado.
- **Bug 3 encontrado al correr `preflight.ps1`:** la check "usr_api no huérfano" hacía un JOIN
  `database_principals`⋈`server_principals` esperando 1 fila, pero antes del primer deploy el USER
  de BD `usr_api` todavía no existe (lo crea `008`), así que daba 0 y fallaba. **Fix:** la check
  ahora cuenta USERS *huérfanos* (existen pero sin LOGIN con SID coincidente) y pasa con 0 — no
  present o present+linked. Se agregó la check simétrica para `usr_worker`. `smartnet-v1.0.0.zip`
  regenerado.
- **2026-08-31 — Preflight:** todos los runtimes OK (.NET 10, Python 3.13, ODBC 18, Tesseract+spa,
  sqlcmd, Caddy), `BDSmartNet` alcanzable, LOGIN `usr_api`/`usr_worker` existen, rutas escribibles.
- **2026-08-31 — `deploy.ps1` cortó en el paso 1** por dos bugs (ver arriba: `Get-ScheduledTask`
  sin tareas + `icacls` con nombre en inglés). Nada se aplicó a la base.
- **Pase de endurecimiento de `deploy/` (2026-08-31):** además de los 5 bugs previos —
  1. `Restrict-Acl`: `icacls` ahora por SID (`*S-1-5-32-544` / `*S-1-5-18`), no por nombre en
     inglés.
  2. `deploy.ps1` paso 1: guarda cuando no hay tareas `\SmartNet\` (primer deploy).
  3. **Caddy ya no se instala como Servicio de Windows** (`caddy run` no habla el protocolo del
     SCM → error 1053). Ahora es una **tarea programada `\SmartNet\SmartNet-Caddy`** AtStartup,
     como SYSTEM, con reinicio automático. `install-services.ps1`, `deploy.ps1` (paso 4) y
     `config.*.ps1` (`$CaddyTaskName`) actualizados. Un wrapper de servicio (WinSW / NSSM) es la
     mejora de producción — se suma a la § Deuda de puesta en producción.
  4. Tareas del worker y de Caddy corren como **SYSTEM** (`New-ScheduledTaskPrincipal -UserId
     S-1-5-18`); antes corrían como el usuario invocante sin credenciales guardadas.
  5. Triggers `-Once` con repetición: se añadió `-RepetitionDuration 3650d` (sin ella la
     repetición no persiste en varias versiones de Windows).
  6. `run-worker-entry.ps1`: parsing de `worker.env` sin `Set-Item Env:` frágil; `Join-Path` del
     exe corregido; falla explícita si el entry point no existe.
  7. `preflight.ps1`: checks nuevas — PowerShell 7+, `$PublicHost` resuelve (entrada en `hosts`),
     puerto 443 libre.
  `smartnet-v1.0.0.zip` regenerado con todo esto.
- **2026-08-31 — `deploy.ps1` corrió; runner falló en `008`** (`GRANT SELECT ON dbo.DocumentoIdentidad`
  / `Proveedor` / `CuentaContable` / `Motivo` / `Origen` → *"Cannot find the object"*). Scripts
  `001`-`007` aplicados (transacción por script → `008` revertido). **Causa:** las 5 tablas del
  catálogo externo `dbo.*` no existen en la base demo; en producción real las mantiene el sistema
  contable. `deploy.ps1` no aplicaba los fixtures. **Fix:** `apply-catalog-fixtures.ps1` nuevo
  (010 DDL + copia de CSV a `$FixturesDataDir` con lectura para SQL Server + 020 con `@ruta`
  reescrito); `deploy.ps1` paso 2(a) lo invoca si `$AplicarFixturesCatalogoDemo`; `fixtures/`
  ahora va en el paquete (workflow + build local). `smartnet-v1.0.0.zip` regenerado.
- **2026-08-31 — `deploy.ps1 -SkipSchema` completó** (pasos 3-6): binarios en `C:\SmartNet\current\`,
  venv del worker + wheel (pip con internet OK), secretos en `C:\SmartNet\secrets\` con ACL.
- **2026-08-31 — `install-services.ps1` OK:** servicio `SmartNetApi` creado, tarea `\SmartNet\SmartNet-Caddy`
  registrada, `Caddyfile` copiado del ejemplo y validado.
- **2026-08-31 — `register-worker-tasks.ps1` OK:** 6 tareas `\SmartNet\SmartNet-*` registradas
  (SYSTEM, repetición 5-30 min según entry point).
- **`Start-Service SmartNetApi` → Running.** ✅
- **Caddy no arrancaba:** `listening on :80: bind: socket no permitido` — Caddy abre el :80 para el
  redirect HTTP→HTTPS y http.sys/IIS lo tiene reservado en Windows Server. El :443 lo abre bien.
  **Fix:** bloque de opciones globales `{ auto_https disable_redirects }` al inicio del `Caddyfile`
  (el redirect no hace falta: los clientes entran directo por https). Actualizado en
  `Caddyfile.example` + `smartnet-v1.0.0.zip` regenerado.
- **Caddy `tls internal` — CA no confiable:** el stack funcionaba entero detrás del cert
  (`/api/sesion`→401, `/`→200 con `-SkipCertificateCheck`), pero la CA local de Caddy no estaba en
  `LocalMachine\Root`. Causa: `caddy run` en primer plano (admin) y como tarea (SYSTEM) usaban
  data dirs distintos → CAs distintas. **Fix:** `storage file_system { root C:\SmartNet\caddy\data }`
  en el bloque global del `Caddyfile` (todas las invocaciones comparten CA) + `caddy trust`
  (*"certificate installed properly in windows trusts"*). Actualizado en `Caddyfile.example`.
- **Bugs en `verify.ps1` (míos):** el check de `/api/sesion` reventaba en el `catch` ante un error
  de SSL (`$_.Exception.Response` inexistente). **Fix:** checks funcionales (`/api/sesion`, SPA)
  con `-SkipCertificateCheck`; el check de validación de TLS se aísla aparte (sin el flag).
  `smartnet-v1.0.0.zip` regenerado.

### 2026-09-01 — VERIFICADO

`verify.ps1 -Environment prod` — **todas las comprobaciones en verde**:

| Check | Resultado |
|---|---|
| `fact.SchemaVersions` ≥ 21 filas | OK (21 scripts + fixtures del catálogo `dbo.*`) |
| `GET /api/sesion` → 401 (Caddy → Kestrel) | OK |
| Kestrel solo en `127.0.0.1:5080` | OK |
| `GET /` sirve el `index.html` de la SPA | OK (3166 bytes) |
| Certificado de `facturas.empresa.local` valida contra el almacén de confianza | OK (CA de Caddy instalada) |
| `smartnet-tipo-cambio` corre y actualiza `fact.EstadoIntegracion` (scrape real SBS) | OK |
| 0 bases `fact_test_*` huérfanas | OK |

**Estado del sistema en la VM:**
- Servicio `SmartNetApi` — Running (automático).
- Tarea `\SmartNet\SmartNet-Caddy` — Running (AtStartup, SYSTEM).
- 6 tareas `\SmartNet\SmartNet-*` del worker — habilitadas.
- `C:\SmartNet\` : `current/` (release activo), `releases/` (retención 3), `backups/`, `secrets/`
  (ACL Administradores+SYSTEM), `logs/`, `adjuntos/` (volumen compartido), `caddy/data/` (CA).
- Keyring de Data Protection en `C:\ProgramData\SmartNet\dataprotection-keys`.

### Pendiente (no bloquea la demo)

1. ~~Crear un usuario para la SPA~~ — **hecho 2026-09-01**: `smartnet-admin.exe usuario crear`
   corrió OK y el login desde la SPA (`https://facturas.empresa.local/`, navegador en la VM)
   funciona end-to-end. El deploy queda **confirmado con un flujo real de usuario**.
2. **Commitear los artefactos de deploy** (`deploy/`, workflow, cambio de código, `.gitignore`) —
   siguen sin commitear; el `VERSION` del paquete apunta a `480d5bc` que no incluye
   `UseWindowsService()`.
3. **CA de Caddy en las máquinas cliente** (o cert real — ADR 0012): un navegador fuera de la VM
   verá advertencia hasta importar la raíz o resolver el cert. Herramientas:
   `deploy/export-ca.ps1` (en la VM: exporta `smartnet-root-ca.crt`, abre firewall :443, imprime
   la IP) + `deploy/trust-ca-client.ps1` (en cada cliente: `hosts` → IP de la VM + importa la CA).
4. Todo lo de la sección "Deuda de puesta en producción" (Vault, agregador con alertas, respaldo,
   entorno de pruebas, Caddy como servicio real).
