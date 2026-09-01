# SmartNetBD

Contrato de datos del ecosistema SmartNet: el esquema SQL versionado, la matriz de permisos por
rol y los fixtures del catálogo externo. Es la base sobre la que se apoyan `SmartNetApi` y
`SmartNetWorker`; no depende de ningún otro repo. El contexto global (partición de propiedad de
datos, contratos entre repos, orden de despliegue) está en `../CLAUDE.md` — no se repite aquí.

## Qué hace y stack

- **Scripts SQL Server numerados** en `schema/001..020`, aplicados en orden léxico (== numérico
  por el zero-padding) por **DbUp**.
- El aplicador NO vive aquí: es `SmartNet.Db.Runner`, un ejecutable .NET dentro de
  `../SmartNetApi/db/runner/`. Este repo es solo los `.sql` + el manifiesto de checksums + los
  fixtures.
- Nunca EF Core, nunca Alembic (ADR 0016). El archivo SQL versionado **es** el cambio de esquema
  completo.
- Todo objeto vive en el esquema `fact`. Ningún script crea ni escribe en `dbo` (ADR 0003) — esa
  invariante es lo más fuerte que el repo reivindica.

### Estructura

| Carpeta | Contenido |
|---|---|
| `schema/*.sql` | Los 20 scripts forward que el runner aplica. |
| `schema/checksums.txt` | Manifiesto SHA-256 de cada script forward (ver gotcha abajo). |
| `schema/generate-checksums.ps1` | Regenera el manifiesto. Se corre **a mano** tras tocar un script. |
| `schema/rollback/NNN_down.sql` | **Advisory, nunca ejecutado por el runner** (design.md Decisión 4). Documentación de cómo revertir. |
| `fixtures/` | **No es esquema versionado, no corre en producción.** Crea y carga los 5 catálogos `dbo.*` que en real mantiene el sistema contable de la empresa. |

Tablas (todas en `fact`): `Usuario`, `Sesion`; ingesta/procesamiento (`Email`,
`DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`,
`ProcesamientoIntentos`, `DocumentoFactura`); negocio/contable (`Factura`, `FacturaExtraccion`,
`AsientoContable`, `AsientoContableDetalle`, `CorrelativoAsiento`, `AdjuntoManual`,
`AuditoriaCorreccion`, `SugerenciaCuenta`, `ProveedorAtributo`, `MotivoAtributo`,
`Configuracion`, `TipoCambio`); contratos entre runtimes (`CommandQueue`, `EstadoIntegracion`,
`OutboxEvent`, `OutboxEventIntegracion`, `InboxEvent`).

## Cómo se levanta en local

No hay comando propio de este repo. El esquema se aplica desde `SmartNetApi`:

```powershell
# 1. La base compartida y los LOGIN usr_api / usr_worker deben existir ya en la instancia
#    (los crea el administrador de la instancia; 008 hace THROW si faltan).

# 2. Aplicar todos los scripts en orden, con journal en fact.SchemaVersions:
cd ..\SmartNetApi\db\runner\SmartNet.Db.Runner
dotnet run -- --connection "Server=localhost;Database=BDSmartNet;Integrated Security=True;TrustServerCertificate=True;Encrypt=False"
#   (o exportar SMARTNET_DB_CONNECTION en vez del flag --connection)
#   --scripts-path por defecto se resuelve caminando hacia arriba hasta el .git y bajando a
#   SmartNet/SmartNetBD/schema — funciona igual desde cualquier cwd.

# 3. Fixtures del catálogo externo (solo entorno local/demo, la base asignada está vacía):
cd ..\..\..\SmartNetBD\fixtures
sqlcmd -S localhost -E -d BDSmartNet -i .\010_dbo_catalogos_ddl.sql
sqlcmd -S localhost -E -d BDSmartNet -i .\020_dbo_catalogos_datos.sql
#   Regenerar los CSV desde los .xlsx solo si cambiaron: .\exportar-catalogos.ps1
```

Pruebas que ejercen el esquema real (`SchemaShapeTests`, `PermissionMatrixTests`,
`ChecksumManifestTests`, `DboWriteLintTests`, `BaseDataTests`, `RollbackAdvisoryTests`) viven en
`../SmartNetApi/db/runner/SmartNet.Db.Runner.Tests/`. Usan `TestDatabaseFixture`, que crea una
base `fact_test_<guid>` desechable por corrida y principales `WITHOUT LOGIN` — sin dependencia de
`CREATE LOGIN` a nivel de instancia. Override de instancia: `SMARTNET_TEST_MASTER_CONNECTION`.

## Decisiones de arquitectura propias

1. **`fact` es el único esquema.** Ningún script forward crea, altera ni escribe un objeto fuera
   de `fact`. `DboWriteLintTests` lo verifica estáticamente sobre los `.sql`.
2. **El journal de DbUp se fuerza a `fact.SchemaVersions`** (no el `dbo.SchemaVersions` por
   defecto), porque la base es compartida con el sistema contable.
3. **Permisos a roles, nunca a usuarios** (`008`): `fact_api` / `fact_worker` reciben los
   GRANT/DENY; `usr_api` / `usr_worker` solo son miembros. Un entorno puede usar otros nombres de
   login sin tocar el script.
4. **DENY explícito cruzado.** `fact_api` tiene DENY sobre tablas de ingesta y viceversa — el
   límite de partición se sostiene en runtime aunque el código tenga un bug.
5. **Todos los scripts son idempotentes / convergentes** (`create-if-absent, always-grant`;
   `insert-if-absent` para datos base). Reaplicar cualquier script contra una base ya migrada es
   un no-op, no un error. Patrón: guardas `IF ... IS NULL` / `IF NOT EXISTS`.
6. **`rollback/` es advisory.** Nunca lo aplica el runner. Promover en orden numérico
   DESCENDENTE (010 → 001) y solo dentro de la ventana de bootstrap (`fact` aún vacío). `001_down`
   corre el último y también dropea `fact.SchemaVersions`.
7. **Sin filas de credenciales en SQL.** `fact.Usuario` se crea vacío; el primer usuario lo crea
   el CLI `smartnet-admin`, nunca una migración. `009` siembra solo `EstadoIntegracion` (5 filas
   exactas: GMAIL, DRIVE, SHEETS, SBS, WORKER) y `Configuracion`.

## Gotchas descubiertos trabajando aquí

- **Editar un script ya aplicado NO falla en ningún lado.** DbUp anota el *nombre* del script en
  `fact.SchemaVersions` y nunca vuelve a mirar su contenido. `checksums.txt` +
  `ChecksumManifestTests` es lo único que detecta esa divergencia silenciosa. Tras tocar
  cualquier `schema/*.sql`: correr `generate-checksums.ps1` y commitear el manifiesto en el mismo
  cambio. El manifiesto cubre **solo** los scripts forward de nivel superior; `rollback/` queda
  fuera a propósito.
- **`001_esquema_fact.sql` asume que `fact` ya existe.** El runner hace
  `IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact')` *antes* de `PerformUpgrade()`, fuera
  de la transacción del journal, porque crear el esquema en el mismo lote que DbUp usa para
  journalizar da error 2760. La guarda `IF SCHEMA_ID(...) IS NULL` del propio 001 lo hace
  converger en ambos caminos.
- **Nunca correr un script suelto fuera de orden.** Reaplicar `008` en aislamiento reintroduce
  `DENY SELECT ON fact.ProcesamientoError TO fact_api`; DENY gana a GRANT y `GET /api/bandeja`
  empieza a fallar con error 229. `018` es el script que hace `REVOKE` + `GRANT` de eso. Usar
  siempre el runner. Ver `[[local-db-connection-gotchas]]`.
- **Usuario huérfano (`usr_api`) → error 4060 al arrancar la API.** `008` es *create-if-absent*:
  no re-vincula un usuario existente cuyo SID ya no coincide con el login. Fix manual:
  `ALTER USER usr_api WITH LOGIN = usr_api;` en `BDSmartNet`.
- **Una `SEQUENCE` es un asegurable distinto de la tabla.** `GRANT ... ON fact.OutboxEvent` no
  alcanza para `NEXT VALUE FOR fact.SeqOutbox`; hace falta `GRANT UPDATE` sobre la secuencia
  (`019`). Solo lo detectó el arnés de contrato N2 que corre bajo el LOGIN `usr_api` real, no las
  pruebas contra conexión sysadmin.
- **`ALTER TABLE ... ADD` va en su propio lote (`GO`)** antes de referenciar la columna nueva en
  el mismo script (ver `015`, `020`).
- **Fixtures: `BULK INSERT` lee rutas desde el servidor, no desde el cliente.** Si SQL Server no
  corre en esta máquina hay que copiar `data/` a una ruta alcanzable por el servidor y ajustar
  `@ruta` en `020`. Los CSV están forzados a `eol=crlf` en `.gitattributes` (raíz) porque el
  terminador de fila se declara literal (`0x0d0a`); los `.xlsx` son `binary`.
- **`Factura.RucProveedor` es `VARCHAR(11)` de 8 a 11 dígitos**, no `CHAR(11)`. 124 proveedores
  del catálogo emiten con DNI/carné, no RUC; un tipo de longitud fija rellenaría con espacios y
  rompería la detección de duplicados en `IX_Factura_Identidad`. El proveedor genérico es
  `P00000` (seis caracteres, no cinco).

## Relación con el ecosistema

`SmartNetBD` no depende de nadie: es el contrato que los demás consumen. `SmartNetApi` (esquema +
tablas `fact.*` de negocio, login `usr_api`) y `SmartNetWorker` (tablas de ingesta/procesamiento,
login `usr_worker`) son ambos clientes de datos, cada uno con su porción del esquema y DENY sobre
la del otro. `SmartNet.Db.Runner` (dentro de `SmartNetApi`) aplica estos scripts **antes** de
desplegar la API o el worker. La API y el worker se comunican entre sí a través de tablas de este
esquema (`CommandQueue`, `OutboxEvent`/`EstadoIntegracion`), nunca por HTTP.
