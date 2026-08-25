# Exploration: Item #17 — Errores, notificaciones y operación

## Current State

**#14 (Outbox y mensajería, ya en main)** — `despacho_outbox.py` hace guarda de obsolescencia →
handler → marcado de estado terminal, con **sin try/except, sin clasificación de error, sin
escrituras a `ProcesamientoError`, sin agenda de reintentos**. `REGISTRO_HANDLERS` está
intencionalmente vacío (reservado para #15/#16). Este es el punto de extensión real de #17 del
lado del outbox.

**Clasificación de error (ADR 0010)** ya existe pero **solo para ingesta (#6)**:
`SmartNet/worker/src/smartnet_worker/errores.py` tiene un enum puro `Clasificacion`
(TRANSITORIO/DIFERIBLE/PERMANENTE/OBSOLETO), `clasificar()`, `proximo_reintento()`. `DIFERIBLE`
está deliberadamente sin uso ahí (no hay llamada de API con cuota en #6) — su productor
(manejo de 429/cuota) es tarea de #17.

**`EstadoIntegracion`** está mucho más construido de lo que sugiere el backlog: tabla SQL
sembrada en `009_datos_base.sql`, Python `estado_integracion.py`
(`registrar_exito`/`registrar_fallo`, solo UPDATE, escrito fuera de la transacción de negocio), y
plumbing .NET completo — `IntegracionEndpoints.cs` + `ServicioDeIntegraciones.cs` con rutas
`estado`/`reprocesar`/`reconectar`/`sincronizar` — **ya construido en el ítem #11**, no en #14 ni
#17. La derivación del chip ("Conectado"/"Con error") ya vive correctamente en `SmartNet.Api`.

**Hueco crítico encontrado**: las tres rutas de comando (`reprocesar`, `reconectar`,
`sincronizar/{nombre}`) solo encolan en `CommandQueue`, pero **no existe ningún consumidor
Python de `CommandQueue`** en el worker. El camino de recuperación de ADR 0010
(`POST /api/incidencias/{id}/reprocesar` → Python re-ejecuta) es hoy un callejón sin salida.
Esto probablemente pertenece a #17 aunque la línea del backlog no lo nombre explícitamente.

**Latido ("heartbeat")** ya existe y funciona: `cli_procesamiento.py` escribe
`registrar_exito`/`registrar_fallo` para `Nombre='WORKER'` incondicionalmente al final de cada
ciclo, satisfaciendo la semántica de ADR 0015.

**Notificación Telegram + respaldo por correo**: no existe nada en código (`.cs`/`.py`) — este es
el núcleo neto nuevo de #17.

**Panel de errores (SPA, de #13)** ya modela un campo `clasificacion: string` en
`ErrorProcesamiento`; sin confirmar si distingue visualmente las tres clases o expone una acción
de reprocesar (TECH-DESIGN.md línea 663 exige ambas).

**Pantalla de Configuración (SPA)** no existe — trabajo genuinamente nuevo. El modelo de datos de
backend ya está sembrado: `fact.Configuracion` (`009_datos_base.sql`) tiene `INGESTA`,
`ADJUNTOS`, `TELEGRAM.DESTINO_CHAT_ID`, `NOTIFICACIONES.CANAL_ALERTA_FALLBACK` (ya por defecto en
`CORREO` según ADR 0015), `NOTIFICACIONES.PREFERENCIA_PRESENTACION`, e
`INTEGRACIONES.INTERVALO_ESPERADO_*` por integración. **No existen endpoints .NET para leer o
escribir `Configuracion`.**

## Affected Areas

- `SmartNet/worker/src/smartnet_worker/despacho_outbox.py` — necesita envolver clasificación de
  error, escrituras `ProcesamientoError`/`ProcesamientoIntentos`, agenda de reintentos.
- `SmartNet/worker/src/smartnet_worker/errores.py` — probablemente necesita una clasificación
  generalizada/paralela para fallos de integración y un productor real de `DIFERIBLE`.
- Módulo Python nuevo — cliente Telegram + respaldo SMTP, log de doble intento, temporización de
  disparo por clase.
- Consumidor Python de `CommandQueue` nuevo — hoy no existe.
- `SmartNet/api/SmartNet.Api/IntegracionEndpoints.cs` / `ServicioDeIntegraciones.cs` — ya cubren
  estado/reprocesar/reconectar/sincronizar (de #11); probablemente sin tocar en #17.
- Endpoints .NET nuevos para CRUD de `fact.Configuracion` por sección/clave — no existen aún.
- `SmartNet/spa/src/app/inbox/ui/panel-errores/` — confirmar/extender la distinción
  TRANSITORIO/DIFERIBLE/PERMANENTE + acción de reprocesar.
- Módulo SPA `configuracion/` nuevo — no existe.

## Approaches

1. **Wrapper genérico `notificar_por_clase()` invocado directamente desde
   `despacho_outbox.py`/futuro consumidor de `CommandQueue`** — Pros: punto único de despacho,
   reutilizable por #15/#16. Contras: acopla el módulo de despacho a un módulo de notificación
   aún no diseñado, riesgo de romper la pureza de `errores.py`. Esfuerzo: Medio.
2. **Notificación como efecto secundario inyectado, desacoplado de la clasificación pura** (replica
   el patrón ya usado en el código: `errores.py` puro + `estado_integracion.py` efecto +
   `despacho_outbox.py` orquestación) — Pros: mantiene la pureza de ADR 0019, fácil de mockear en
   pruebas, no anticipa la forma del handler de #15/#16. Contras: más piezas móviles.
   Esfuerzo: Medio-Alto.

## Recommendation

Enfoque 2 — coincide con el patrón que el código ya usa en todas partes (clasificación pura +
efectos inyectados por separado), mantiene el núcleo testeable sin I/O según ADR 0019, y evita
comprometerse de más con una interfaz de handler que #15/#16 aún no han diseñado.

## Risks

- Ambigüedad de alcance: el consumo de `CommandQueue` (necesario para que `reprocesar` funcione
  de verdad) no está nombrado en la línea del backlog de #17 pero es un requisito duro de ADR 0010
  sin otro lugar donde vivir.
- `REGISTRO_HANDLERS` vacío en `despacho_outbox.py` implica que la interfaz de envoltura de error
  de #17 debe diseñarse de forma genérica, sin handlers de Drive/Sheets contra los cuales
  validarla — riesgo de una interfaz mal ajustada para #15/#16 después.
- El token de bot de Telegram requiere el gestor de secretos con escritura en caliente de
  ADR 0015; el estado de implementación del gestor de secretos de este repo no se verificó en
  esta pasada — podría ser una dependencia bloqueante no declarada.
- No existen endpoints .NET para `Configuracion`; hay que decidir si extienden
  `IntegracionEndpoints.cs` o si nace un `ConfiguracionEndpoints.cs` nuevo, más reglas de
  validación por `Tipo`.
- `DIFERIBLE` sigue sin productor real (interpretación de cabecera 429/Retry-After entre las APIs
  de Google) — ADR 0010 marca esto como un costo aceptado explícito.
- El DDL de `ProcesamientoError`/`ProcesamientoIntentos` no se inspeccionó en esta pasada —
  confirmar el esquema existente antes de proponer migraciones.

## Ready for Proposal

Sí, una vez que el dueño del proyecto resuelva o difiera explícitamente las preguntas abiertas
anteriores — especialmente el alcance del consumidor de `CommandQueue` y la dependencia del
gestor de secretos, ya que cualquiera de las dos podría hacer crecer #17 igual que #14 creció más
allá de su estimado original.
