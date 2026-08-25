# Design: Errores, notificaciones y operación (#17)

**Status**: `done` — ratificado por el dueño del proyecto. D1 (nueva DDL) y la siembra de
`CORREO.DESTINATARIOS` quedan confirmadas y son definitivas, no condicionales.

> **Corrección de numeración**: el dueño ratificó el script como `019`, pero `019_permiso_secuencia_seqoutbox.sql`
> ya existe (mergeado con el ítem #14, commit `840564a`). El siguiente número libre es **020**. Se conserva la
> sustancia ratificada (un único script SQL versionado con columna + CHECK + fila semilla, ADR 0016, sin
> EF/Alembic) y sólo cambia el ordinal: `020_outbox_clasificacion.sql` + `rollback/020_down.sql`.

## Technical Approach

Four independent slices on top of merged infra. Every slice keeps a pure decision core (no DB/HTTP/clock,
ADR 0019) with injected effects, mirroring `errores.py` vs `estado_integracion.py`. The outbox wrapper adds a
`try/except` around the *existing* handler callable — it commits to nothing about Drive/Sheets (#15/#16).

## Architecture Decisions

### D1 — Where outbox dispatch errors are persisted (corrects the proposal)

**Verified**: `fact.ProcesamientoError.ProcesamientoId` is `NOT NULL` FK → `fact.Procesamiento`
(`003_ingesta_y_procesamiento.sql:100-115`). The outbox path only knows `FacturaId`; `EnvolturaOutbox`
(`PayloadOutbox.cs:94`) carries **no** `procesamientoId`; `fact.Factura` is `DENY`-ed to `fact_worker`
(`008_usuarios_y_permisos.sql:88`); and `Factura.ProcesamientoId` is itself nullable (`005_negocio.sql:17`).
**So `ProcesamientoError` cannot receive outbox errors.** The proposal's "no new DDL" assumption is wrong.

**Choice (RATIFICADA)**: persist on `fact.OutboxEventIntegracion` (`Estado='ERROR'`, `Intentos`, `UltimoError`,
`ProximoIntentoEn` — all already present, all already `GRANT UPDATE TO fact_worker`), plus **one** new nullable
`Clasificacion VARCHAR(20) NULL` + ADR-0010 CHECK, shipped as versioned SQL `020_outbox_clasificacion.sql` and
`rollback/020_down.sql` (ADR 0016; create-if-absent, same shape as 013/015). Object-level grants already cover
it. `ProcesamientoError`/`ProcesamientoIntentos` stay the ingesta home (#6), untouched.

| Alternative | Rejected because |
|---|---|
| Add `procesamientoId` to the outbox payload | **Descartada por el dueño.** Bumps a co-written contract + 2 golden fixtures, and is still `NULL` for manually-entered invoices |
| `GRANT SELECT ON fact.Factura TO fact_worker` | Breaks ADR 0003's strongest boundary for a logging convenience |
| Reuse `UltimoError` with no `Clasificacion` | Operator cannot tell transitorio from permanente — defeats the item |

### D1b — Contenido de `020_outbox_clasificacion.sql` (ratificado)

Un solo script, dos efectos, ambos `NOT EXISTS`-guardados (reaplicar converge, no falla — patrón 009/013/015):

```sql
-- (1) Columna + CHECK en fact.OutboxEventIntegracion (006_contratos.sql:32).
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'Clasificacion'
                 AND object_id = OBJECT_ID('fact.OutboxEventIntegracion'))
    ALTER TABLE fact.OutboxEventIntegracion ADD Clasificacion VARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_OutboxEventIntegracion_Clasificacion')
    ALTER TABLE fact.OutboxEventIntegracion
        ADD CONSTRAINT CK_OutboxEventIntegracion_Clasificacion
            CHECK (Clasificacion IN ('TRANSITORIO', 'DIFERIBLE', 'PERMANENTE', 'OBSOLETO'));

-- (2) Fila semilla CORREO.DESTINATARIOS en fact.Configuracion (007_publicacion.sql:24).
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion
               WHERE Seccion = 'CORREO' AND Clave = 'DESTINATARIOS')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('CORREO', 'DESTINATARIOS', 'LISTA', NULL, NULL,
            N'Direcciones de correo que reciben la alerta de respaldo cuando Telegram falla (ADR 0015, NOTIFICACIONES.CANAL_ALERTA_FALLBACK). Ningun documento fija un destinatario: es dato de despliegue, se configura desde la pantalla de Configuracion.');
```

Notas de diseño, no de implementación:

- **`ALTER ... ADD` separado del CHECK por `GO`**: SQL Server no admite referenciar en el mismo lote una columna
  recién agregada. Es el mismo motivo por el que 015 hace DROP+ADD en vez de ALTER de un CHECK existente.
- **Vocabulario del CHECK idéntico a `CK_ProcesamientoError_Clasificacion`** (`003:113-114`): las mismas cuatro
  clases de ADR 0010, mismo orden, mismo `VARCHAR(20)`. Una sola verdad de vocabulario en todo el esquema.
- **Nullable, sin `DEFAULT`**: las filas ya existentes de #14 quedan en `NULL` ("nunca falló"), que es exactamente
  su semántica. Un `DEFAULT` inventaría una clasificación para eventos que jamás lanzaron.
- **`Tipo = 'LISTA'`** (no `TEXTO`): son N destinatarios separados por coma; encaja con la regla LISTA de D6
  ("ítems separados por coma, ninguno vacío") y con el precedente `INGESTA.EXTENSIONES_PERMITIDAS` (009).
- **`Valor = NULL` y `ValorPorDefecto = NULL`, explícitamente — no cadena vacía.** Ningún documento normativo fija
  destinatarios, así que se siembra *pendiente*, igual que `TELEGRAM.DESTINO_CHAT_ID` y `CONTABILIDAD.FECHA_CORTE_CONTABLE`
  (009). La cadena vacía sería peor en tres frentes: viola la propia regla LISTA de D6 (ítem vacío), es
  indistinguible de "configurado con cero destinatarios", y `007:29` documenta `NULL = usar el default`. Con `NULL`,
  el notificador falla con `ConfiguracionError` explícito al arrancar en vez de "enviar a nadie" en silencio.
- **Sin `GRANT` nuevo**: `fact_worker` ya tiene SELECT sobre `fact.Configuracion` (`008:131`) y UPDATE sobre
  `fact.OutboxEventIntegracion`; los GRANT son a nivel de objeto, no de columna ni de fila (mismo razonamiento
  escrito en 015:24-27).
- **`rollback/020_down.sql`** (advisory, nunca lo ejecuta el runner — igual que `019_down`): `DROP CONSTRAINT
  CK_OutboxEventIntegracion_Clasificacion`, `DROP COLUMN Clasificacion`, y `DELETE FROM fact.Configuracion WHERE
  Seccion='CORREO' AND Clave='DESTINATARIOS'` — los tres `IF EXISTS`-guardados, en ese orden (el CHECK antes que
  la columna, o el DROP COLUMN falla).

### D2 — Generic wrapper contract (excepción entra → clasificación sale)

`despachar_evento` keeps its signature and its `Mapping[str, Callable[[EventoReclamado], None]]` registry. The
wrapper only wraps `handler(evento)` in `except BaseException`. Handler contract stays "callable that raises on
failure" — nothing Drive/Sheets-specific. New pure module `clasificacion_despacho.py`:

```python
@dataclass(frozen=True)
class ResultadoDespacho:
    estado: str                          # 'COMPLETADO' | 'ERROR' | 'OBSOLETO'
    clasificacion: Clasificacion | None
    proximo_intento_en: datetime | None
    agotado: bool                        # TRANSITORIO en el tope -> notificar

def decidir(error: BaseException, intentos: int, instante: datetime) -> ResultadoDespacho: ...
```

Persistence is injected as a `RegistroDeFallo` Protocol (`registrar(evento_id, integracion, resultado, mensaje,
instante)`), default `None` → #14 behaviour preserved byte-for-byte.

### D3 — DIFERIBLE producer

New `CuotaExcedidaError(Exception)` carrying `retry_after: timedelta | None`, plus a **pure** helper
`retry_after_desde(cabecera: str | None, instante: datetime) -> timedelta | None` handling both the
delta-seconds and HTTP-date forms. It takes the header *string*, never a `Response` object — no HTTP type, no
clock. `decidir` maps it to `DIFERIBLE` and honours `retry_after` verbatim, else falls back to
`errores.proximo_reintento`. `OBSOLETO` still short-circuits before the handler (`despacho_outbox.py:51`).

### D4 — Notifier: Telegram primary, correo fallback, both attempts logged

Three layers: pure `politica_notificacion.py` (`debe_notificar`, `redactar`), effectful `notificaciones.py`
(`notificar(canales, mensaje, instante, cursor)`), and adapters behind a `CanalDeAviso` Protocol
(`enviar(mensaje) -> None`, raises on failure). Telegram is attempted first; **any** exception is logged and
correo is attempted. Both outcomes go to `fact.EstadoIntegracion` rows `TELEGRAM`/`CORREO` via the existing
`registrar_exito`/`registrar_fallo` (both runtimes hold SELECT/INSERT/UPDATE, `008:136-137`) — so a failed
Telegram send is visible even when correo succeeds.

| Clase | Disparo |
|---|---|
| TRANSITORIO | sólo al agotar el tope (`agotado=True`) |
| PERMANENTE | inmediato, primer fallo |
| DIFERIBLE | una vez por `(OutboxEventId, Integracion)` — dedupe leyendo la `Clasificacion` ya escrita en la fila |
| OBSOLETO | nunca |

Secrets: `config.py` gains `SMARTNET_WORKER_TELEGRAM_CREDENTIALS` and `SMARTNET_WORKER_SMTP_CREDENTIALS`, each
one atomic JSON blob, no default in code — identical rule to `obtener_credenciales_gmail_json`. Non-secret
destinations come from `fact.Configuracion`, which `fact_worker` may `SELECT` (`008:131`), through a new
read-only `configuracion_repo.py`: `TELEGRAM.DESTINO_CHAT_ID` ya sembrada en 009, y `CORREO.DESTINATARIOS`
sembrada por el 020 (D1b) — decisión ratificada, ya no es una clave que la pantalla deba crear.

### D5 — CommandQueue consumer: lease and idempotency

No DDL: `Estado`/`Intentos`/`ProximoIntentoEn` exist, `fact_worker` has SELECT+UPDATE (`008:119`), and
`IX_CommandQueue_Referencia (Tipo, Estado)` (018) already indexes the claim. `command_queue_repo.py` mirrors
`outbox_repo.py`: `SET NOCOUNT ON` first (the pyodbc multi-result-set trap documented at `outbox_repo.py:22-29`),
then `UPDATE TOP (?) ... OUTPUT ... FROM fact.CommandQueue WITH (READPAST, UPDLOCK, ROWLOCK) WHERE Tipo IN (…)
AND (Estado='PENDIENTE' OR (Estado='EN_PROCESO' AND ProximoIntentoEn <= ?))`, reusing the **imported**
`ARRENDAMIENTO` (5 min) constant — never a fresh literal.

**Semantics: at-least-once with idempotent effects, not at-most-once.** The proposal's fear ("re-sending to
Drive") does not apply — no `CK_CommandQueue_Tipo` value dispatches to Drive/Sheets:

| Tipo | Referencia | Efecto e idempotencia |
|---|---|---|
| `REPROCESAR_DOCUMENTO` | `ProcesamientoId` (confirmado: `SqlBandejaRepository.cs:72`) | `Procesamiento.Estado='PENDIENTE'`; re-ejecutar es no-op, y el reproceso ya es idempotente vía `UQ_Procesamiento_DocumentoRecibido` / `UQ_DocumentoRecibido_Email_Hash` |
| `SINCRONIZAR_GMAIL` | `NULL` | Sondeo; idempotente por `UQ_Email_GmailMessageId` |
| `SINCRONIZAR_SBS` | `NULL` | Idempotente por fecha en `fact.TipoCambio` |
| `RECONECTAR_GOOGLE` | `NULL` | Sólo limpia `EstadoIntegracion.FallosSeguidos`; el consentimiento OAuth interactivo es de .NET |

Terminal: `COMPLETADO` en éxito; `ERROR` si la clasificación es `PERMANENTE` o se agota el presupuesto
transitorio (misma política `errores.proximo_reintento`). ADR 0003: el consumidor toca sólo `CommandQueue`
(contrato), `Procesamiento` (privada de Python) y `EstadoIntegracion` (compartida). Nunca `fact.Factura`, nunca `dbo.*`.

### D6 — Configuración: API .NET + pantalla Angular

New `ConfiguracionEndpoints.cs` (owner answer #2), same thin shape as `IntegracionEndpoints.cs`:
`GET /api/configuracion[?seccion=]` and `PUT /api/configuracion/{seccion}/{clave}` with body `{ "valor": string|null }`.
Validation is a **pure** Core type `ValorDeConfiguracion.Validar(tipo, valor)` (no HTTP/DB), mapped to
ProblemDetails by the endpoint via the existing `ProblemasDeNegocio` convention:

| Tipo | Regla | Tipo | Regla |
|---|---|---|---|
| TEXTO | ≤ 400 chars | BOOLEANO | `true`/`false` canónico |
| ENTERO | `long.TryParse`, invariante | FECHA | `yyyy-MM-dd` exacto |
| DECIMAL | `decimal.TryParse` invariante, nunca `float` | LISTA | ítems separados por coma, ninguno vacío |

`valor = null` es legal → "usar `ValorPorDefecto`". La escritura sella `ActualizadoPorUsuarioId`/`ActualizadoEn`.
**UPDATE únicamente**: las claves se siembran en 009/013 y ahora 020 (`CORREO.DESTINATARIOS`); una clave
desconocida es 404, nunca un INSERT silencioso.

SPA: `spa/src/app/configuracion/` con `data-access/` + `models/` + `feature/configuracion-page/` +
`ui/configuracion-seccion/` + `ui/campo-configuracion/`, signals y `HttpClient`/`firstValueFrom` como
`inbox.service.ts`, sin librería de estado. Ruta lazy `configuracion` tras `authGuard` en `app.routes.ts`. La
validación de cliente espeja `Tipo` pero **no es autoritativa**: el error del servidor lo pinta el
`http-error.interceptor` existente.

### D7 — `panel-errores`: sin delta

`ErrorProcesamiento.clasificacion` ya viaja en el contrato (`bandeja-item.model.ts:28`), `SqlBandejaRepository`
ya lo proyecta, `panel-errores` ya lo renderiza y `confirmar-reproceso` + `reprocesarDisponibleEn` ya existen.
Como D1 manda los errores de outbox a `OutboxEventIntegracion` (nivel factura, no documento), la bandeja no
cambia. **La capacidad modificada del proposal se cierra sin cambios.** Los fallos de despacho se observan por
`GET /api/integraciones/estado` (ya construido); ampliarlo a un listado por factura queda diferido.

## Data Flow

```
   cli_outbox ──reclamar──> OutboxRepo (READPAST lease 5m)
        │
        └─> despachar_evento ──guarda obsolescencia──> [OBSOLETO] fin
                  │
                  └─> handler(evento)  ── raises ──┐
                                                   v
                            clasificacion_despacho.decidir  (PURO)
                                                   │
                            ┌──────────────────────┼──────────────────────┐
                            v                      v                      v
                RegistroDeFallo (SQL)     politica_notificacion    proximo_intento_en
                OutboxEventIntegracion         (PURO)                 (reintento)
                 Estado/Intentos/                 │
                 UltimoError/Clasificacion        v
                                          notificaciones.notificar
                                          Telegram ──falla──> Correo
                                             │                   │
                                             └── EstadoIntegracion (ambos intentos)

   .NET ConfiguracionEndpoints ──UPDATE──> fact.Configuracion ──SELECT──> worker
   .NET IntegracionEndpoints ──INSERT──> fact.CommandQueue ──READPAST──> consumidor_command_queue
```

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/db/schema/020_outbox_clasificacion.sql` | Create | `Clasificacion VARCHAR(20) NULL` + `CK_OutboxEventIntegracion_Clasificacion` en `fact.OutboxEventIntegracion`, **y** fila semilla `CORREO.DESTINATARIOS` (`LISTA`, `Valor`/`ValorPorDefecto` NULL) en `fact.Configuracion` (D1/D1b) |
| `SmartNet/db/schema/rollback/020_down.sql` | Create | Reverso del 020: DROP CHECK → DROP COLUMN → DELETE de la semilla, los tres guardados |
| `worker/.../clasificacion_despacho.py` | Create | Núcleo puro: `decidir`, `ResultadoDespacho`, `retry_after_desde` |
| `worker/.../errores.py` | Modify | `CuotaExcedidaError` → productor de `DIFERIBLE` |
| `worker/.../despacho_outbox.py` | Modify | `try/except` alrededor del handler + `RegistroDeFallo` inyectado |
| `worker/.../outbox_repo.py` | Modify | `marcar_fallo(...)` escribiendo Estado/Intentos/UltimoError/Clasificacion/ProximoIntentoEn |
| `worker/.../politica_notificacion.py` | Create | Puro: `debe_notificar`, `redactar` |
| `worker/.../notificaciones.py` | Create | `CanalDeAviso`, `TelegramCanal`, `CorreoCanal`, `notificar` con log dual |
| `worker/.../configuracion_repo.py` | Create | Lectura de `fact.Configuracion` (sólo SELECT) |
| `worker/.../command_queue_repo.py` | Create | Claim READPAST + `marcar` sobre `fact.CommandQueue` |
| `worker/.../comandos.py` | Create | Puro: `Tipo` → handler, sin SQL |
| `worker/.../cli_command_queue.py` | Create | Bucle del consumidor (entry point) |
| `worker/.../config.py` | Modify | Credenciales Telegram/SMTP por env, sin default |
| `worker/.../cli_outbox.py` | Modify | Inyecta registro + notificador |
| `api/SmartNet.Api/ConfiguracionEndpoints.cs` | Create | GET/PUT `fact.Configuracion` |
| `facturacion/.../ValorDeConfiguracion.cs` | Create | Validación pura por `Tipo` |
| `facturacion/.../Sql*ConfiguracionRepository.cs` | Create | Puerto + adaptador SQL |
| `spa/src/app/configuracion/**` | Create | data-access + models + feature + ui |
| `spa/src/app/app.routes.ts` | Modify | Ruta lazy `configuracion` tras `authGuard` |

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit (puro) | `decidir`, `retry_after_desde`, `debe_notificar`, `ValorDeConfiguracion.Validar` | Sin DB/HTTP/reloj; `instante` siempre parámetro (ADR 0019 nivel 1) |
| Unit (repos) | `marcar_fallo`, claim de CommandQueue | Fake cursor, aserción sobre SQL + parámetros (patrón `test_outbox_repo.py`) |
| Unit (notificador) | Telegram falla → correo se intenta; ambos se registran | Canales fake que lanzan |
| Integración | Claim concurrente no procesa el comando dos veces; el 020 aplica, reaplica (no-op) y revierte; el CHECK rechaza una clasificación fuera del vocabulario; la semilla `CORREO.DESTINATARIOS` existe con `Valor IS NULL` | Arnés `worker_db` real (ADR 0002/0019 nivel 2) |
| Integración .NET | PUT rechaza por `Tipo`; clave desconocida → 404, sin INSERT | `WebApplicationFactory` + BD de prueba |
| Estructural | El consumidor no toca `fact.Factura` ni `dbo.*` | Extender `test_no_dbo_structural.py` |
| SPA | Servicio + página de configuración | Jasmine/Karma + `HttpTestingController` |

## Threat Matrix

`N/A` — sin routing de CLI, sin shell, sin subproceso, sin automatización de VCS/PR y sin clasificación de
archivos ejecutables. Las fronteras reales de este ítem son egreso de red (Telegram HTTPS, SMTP) y credenciales;
se cubren por el patrón ya vigente: un secreto atómico por variable de entorno sin default en código
(`config.py`), timeout HTTP explícito (`HTTP_TIMEOUT_SECONDS`), y `Mensaje` truncado a 2000 chars antes de tocar
`UltimoError`/`ProcesamientoError` para no filtrar payload crudo en la base.

## Migration / Rollout

Un único script versionado (`020`), aditivo, con columna nullable y una fila semilla `NOT EXISTS`-guardada:
aplicarlo no rompe #14 ni #16, y reaplicarlo es no-op. Cada capacidad se despliega sola; `REGISTRO_HANDLERS`
sigue vacío, así que la envoltura de clasificación queda inerte hasta #15/#16. El notificador queda desactivado
si las variables de entorno no están definidas, y falla con `ConfiguracionError` explícito si
`CORREO.DESTINATARIOS` sigue en `NULL` al arrancar — nunca un envío silencioso a nadie.

## Decisiones ratificadas por el dueño

| # | Pregunta abierta anterior | Resolución |
|---|---|---|
| 1 | ¿D1 puede introducir DDL nueva? | **Sí.** Columna `Clasificacion` + CHECK vía script SQL versionado (ADR 0016). Se descarta versionar el payload del outbox. |
| 2 | ¿`CORREO.DESTINATARIOS` se siembra o se deja a la pantalla? | **Se siembra** en el mismo script, junto a la columna. |

## Open Questions

Ninguna que bloquee `sdd-tasks`. Riesgos abiertos (no preguntas) en la sección siguiente.

## Riesgos abiertos

- **Contrato del wrapper sin validar contra un handler real.** `REGISTRO_HANDLERS` está vacío hasta #15/#16, así
  que "el handler lanza al fallar" se verifica sólo con dobles de prueba. Si el handler real de Drive/Sheets
  devuelve un código de error en vez de lanzar, `decidir` nunca se invoca y el fallo pasa como éxito. Mitigación:
  #15/#16 deben incluir una prueba de contrato que afirme que el handler lanza.
- **El dedupe de `DIFERIBLE` es best-effort.** Se resuelve leyendo la `Clasificacion` ya escrita en la fila
  `(OutboxEventId, Integracion)`; no hay bloqueo entre el read y el write, así que dos workers concurrentes sobre
  la misma fila podrían notificar dos veces. Aceptado: el arriendo READPAST de 5 min hace la ventana improbable,
  y el coste de fallar es un aviso duplicado, no un dato incorrecto. Endurecerlo exigiría DDL adicional
  (`NotificadoEn`), fuera del alcance ratificado.
