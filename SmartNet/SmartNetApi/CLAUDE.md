# SmartNetApi

Backend HTTP transaccional del ecosistema SmartNet: dominio contable, autenticación por sesión,
endpoints que consume la SPA, y el aplicador del esquema SQL. El contexto global del ecosistema
(contratos entre repos, partición de datos, despliegue) está en `../CLAUDE.md` — no se repite aquí.

## Qué hace y stack

- **.NET 10**, ASP.NET Core Minimal API, ADO puro (`Microsoft.Data.SqlClient`), sin ORM.
- **DbUp** solo dentro de `db/runner` (aplicador de esquema), nunca EF Core, nunca migraciones.
- Pruebas: xUnit. Núcleos de dominio con `PurityScanTests` (prohíben DB/HTTP/reloj en build).
  Infra y API con `TestDatabaseFixture` (base `fact_test_<guid>` desechable) y
  `WebApplicationFactory<Program>`.

### Solución (`SmartNet.sln`, 31 proyectos)

Módulos por carpeta, cada uno par `*.Core` (dominio puro, español) + `*.Infrastructure`
(adaptadores SQL): `auth`, `catalogos`, `contable`, `facturacion`, `inbox`, `sugerencia`,
`tipos-de-cambio`. Ejecutables: `api/SmartNet.Api` (host HTTP), `admin/SmartNet.Admin`
(CLI `smartnet-admin`), `db/runner/SmartNet.Db.Runner` (aplicador DbUp).

- `contable/SmartNet.Contable.Core` llega a la API de forma transitiva vía `facturacion`.
- `sugerencia` existe pero **aún no está cableado** en `Program.cs` (sin endpoints ni DI).

## Cómo se levanta en local

Prerrequisito: SQL Server local con la base compartida ya creada y el esquema aplicable. Todas
las credenciales vienen de variables de entorno — **no hay `appsettings.json`, es deliberado**
(`ApiConnectionOptions.cs`).

```bash
# 1. Aplicar el esquema versionado (principal de despliegue, NO usr_api).
#    --scripts-path por defecto: <repo-root>/SmartNet/SmartNetBD/schema (se resuelve subiendo hasta .git)
cd db/runner/SmartNet.Db.Runner
SMARTNET_DB_CONNECTION="Server=localhost;Database=BDSmartNet;Integrated Security=True;TrustServerCertificate=True;Encrypt=False" \
  dotnet run

# 2. Levantar la API (principal usr_api; variable DISTINTA a la del runner, a propósito)
cd ../../../api/SmartNet.Api
export SMARTNET_API_DB_CONNECTION="Server=localhost;Database=BDSmartNet;User Id=usr_api;Password=...;TrustServerCertificate=True;Encrypt=False"
export SMARTNET_API_STORAGE_ROOT="D:/ruta/al/volumen/compartido"   # mismo disco físico que el worker
export SMARTNET_API_KEYRING_PATH="D:/ruta/al/keyring"              # Data Protection persistido a disco
dotnet run   # https://localhost:54848 (http://localhost:54849)

# 3. Crear un usuario para poder loguearse desde la SPA
cd ../../admin/SmartNet.Admin
SMARTNET_API_DB_CONNECTION="...usr_api..." dotnet run -- usuario crear --nombre <usuario>
```

`smartnet-admin` también: `usuario restablecer-clave --nombre <u>`, `sesion purgar --retencion-dias <n>`.

### Pruebas

```bash
dotnet test SmartNet.sln                              # todo (necesita SQL Server local)
dotnet test auth/SmartNet.Auth.Core.Tests             # un núcleo puro, sin DB
```

- El fixture usa por defecto `Server=localhost;...;Integrated Security=True` contra `master` para
  crear/borrar las bases `fact_test_<guid>`. Override: `SMARTNET_TEST_MASTER_CONNECTION`.
- CI tiene 4 jobs (`.github/workflows/ci.yml` en el repo raíz): estáticas sin DB, suite completa
  contra SQL Server, worker Python, y frontend. El worker de integración levanta su **propio**
  contenedor porque necesita `CREATE LOGIN usr_worker` server-scoped.

## Decisiones de arquitectura propias

1. **El host de la API NUNCA referencia `SmartNet.Db.Runner`** (directa ni transitivamente). Un
   runtime que puede alterar una base compartida al arrancar es justo lo que la partición de
   permisos previene. Hay un guardián estructural: `NoRunnerReferenceGuardTests`.
2. **Variables de entorno deliberadamente distintas por principal**:
   `SMARTNET_DB_CONNECTION` (runner, deploy) ≠ `SMARTNET_API_DB_CONNECTION` (API, `usr_api`);
   `SMARTNET_API_STORAGE_ROOT` (API) ≠ `SMARTNET_WORKER_STORAGE_ROOT` (worker). Reusar el nombre
   dejaría que un operador entregue rights de más al proceso equivocado.
3. **Sin `appsettings.json` ni fallback comiteado.** Config ausente = fallo de arranque ruidoso
   con mensaje de uso (`ApiConnectionOptions`/`ApiKeyRingOptions`/`DocumentoStorageOptions.Resolve`
   se validan post-`Build()` contra `app.Configuration`).
4. **Sesión server-side real**, no cookie autocontenida: `SqlSesionTicketStore` sobre `fact.Sesion`,
   cookie `__Host-session` (`HttpOnly`, `Secure`, `SameSite=Lax`, 8h sliding). Logout invalida del
   lado del servidor (ADR 0007). 401 devuelve el status plano, nunca redirige a login.
5. **Data Protection key ring persistido a disco** (`FileSystemXmlRepository` en
   `SMARTNET_API_KEYRING_PATH`): sin esto, reiniciar el host invalida toda cookie viva.
6. **Sin `app.UseCors(...)` en ningún lado** — el mismo origen tras el proxy inverso es
   precondición, no supuesto (ADR 0012). Guardado por test.
7. **DbUp journal forzado a `fact.SchemaVersions`** (no `dbo.SchemaVersions`, su default): este
   proyecto nunca crea un objeto fuera del esquema `fact`.
8. Registro DI **lazy** (factory delegate, no `builder.Configuration` eager): `WebApplicationFactory`
   inyecta su override de config durante `Build()`; código que corre antes vería la config
   pre-override. Los `*Servicio*` son `AddScoped`; los repos planos, `AddSingleton`.

## Gotchas ya descubiertos

- **Error 4060 / "Login failed for user 'usr_api'"** al arrancar: el usuario de BD `usr_api` quedó
  huérfano (SID desalineado tras drop/recreate del login). `008_usuarios_y_permisos.sql` es
  *create-if-absent*, no re-linkea. Fix: `ALTER USER usr_api WITH LOGIN = usr_api;` en la base
  compartida (ídem `usr_worker`).
- **Error 229 "SELECT permission denied on 'ProcesamientoError'"** en `GET /api/bandeja`: alguien
  re-corrió `008` aislado, que re-aplica el `DENY` (DENY gana a GRANT). Fix: re-aplicar `018`
  (idempotente). **Nunca correr scripts de esquema sueltos** — usar `SmartNet.Db.Runner`.
- El runner, en base nueva, hace `EnsureJournalSchemaExists` fuera de la transacción de DbUp:
  crear `fact.SchemaVersions` en la misma transacción que `001` da SQL error 2760 (schema no
  visible aún en ese round-trip).
- `TimeProvider.System` es DI singleton para que los tests sustituyan un `FakeTimeProvider` y
  manejen la escalada de lockout y el `PromocionBackgroundService` sin esperas reales.
- No hay ESLint en la SPA todavía; el "lint" de CI para frontend es `tsc --noEmit`.

## Relación con el ecosistema

Depende de `SmartNetBD` (esquema + tablas `fact.*`, aplicadas por su propio runner antes de
desplegar). `SmartNetWeb` es su único cliente HTTP (`/api/*`, cookie auth). Con `SmartNetWorker`
**no habla por HTTP**: se coordinan de forma asíncrona vía tablas compartidas (`CommandQueue`,
`Outbox`/`EstadoIntegracion`) y el volumen compartido de adjuntos. Detalle completo en `../CLAUDE.md`.
