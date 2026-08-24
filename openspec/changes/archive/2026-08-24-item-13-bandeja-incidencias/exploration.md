# Exploration: Bandeja e incidencias (BACKLOG #13)

## Current State

- **#11 (API de facturas y asientos)** ya está cerrado
  (`openspec/changes/archive/2026-08-23-api-facturas-asientos/`) y entrega más superficie de la que
  #13 estrictamente necesita — incluye ambos endpoints que la línea de alcance de #13 implica:
  - `POST /api/incidencias/{id}/reprocesar`
    (`SmartNet/api/SmartNet.Api/IntegracionEndpoints.cs`) — solo encola, inserta
    `fact.CommandQueue(Tipo=REPROCESAR_DOCUMENTO)`, nunca llama a Python directamente (ADR 0003),
    nunca escribe `AuditoriaCorreccion`. Spec:
    `openspec/specs/api-incidencias-integraciones/spec.md`.
  - `GET /api/integraciones/estado`, `POST /api/integraciones/{nombre}/sincronizar`,
    `POST /api/integraciones/google/reconectar` — mismo patrón de encolar/derivar.
- **`GET /api/bandeja` ya existe** pero es explícitamente una versión parcial, con forma del ítem
  #7 — el propio comentario de `IBandejaRepository.cs`
  (`SmartNet/inbox/SmartNet.Inbox.Core/IBandejaRepository.cs`) dice "#7-shaped, widened by #13
  later". Hoy solo soporta `estado`/`orden` (`SqlBandejaRepository` en
  `SmartNet.Inbox.Infrastructure`). **ADR 0008** (`adrs/0008-contratos-de-api.md`, línea 34)
  especifica el contrato completo: `GET /api/bandeja?estado=&desde=&hasta=&proveedor=&pagina=` —
  `desde`, `hasta`, `proveedor`, `pagina` **no están implementados todavía**. ADR 0008 línea 50
  también exige: "Cada elemento declara su origen. Angular nunca combina fuentes" (combinación
  server-side solamente, ADR 0003).
- **Los "seis indicadores"** (`DESIGN_BRIEF.md`): proveedor genérico, posible duplicado, campos no
  extraídos, fecha en domingo, afectación no verificada, referencia externa.
  `SmartNet.Inbox.Core.IndicadoresFactura` solo calcula **5** — `EsReferenciaExterna` se mantiene
  deliberadamente en su default de DDL (decisión ya tomada en ADR 0005 / WU6 del ítem #7: no existe
  todavía dato de referencia de nota, notas de crédito es el ítem #10). Esto es una decisión de
  diseño ya resuelta (D5), no una discrepancia nueva. La lógica de derivación del chip ya existe en
  el cliente: `SmartNet/spa/src/app/inbox/ui/inbox-list/inbox-list.ts` (`chipsDe()`).
- **"Reprocesar"** ya tiene un significado concreto: encolar
  `fact.CommandQueue(Tipo=REPROCESAR_DOCUMENTO, Referencia={id})`. El spec nunca define qué es
  `{id}` — `ServicioDeIntegraciones.EncolarAsync` lo pasa de forma opaca. El candidato natural es
  `ProcesamientoId`, porque la tabla de log de errores `fact.ProcesamientoError`
  (`SmartNet/db/schema/003_ingesta_y_procesamiento.sql`) está indexada por `ProcesamientoId`, no por
  `InboxEventId`/`FacturaId`. Esta es una pregunta de diseño abierta que #13 debe cerrar.
- **Fuente de errores**: `fact.ProcesamientoError(ProcesamientoId FK, Integracion, Mensaje,
  Clasificacion CHECK IN ('TRANSITORIO','DIFERIBLE','PERMANENTE','OBSOLETO'), OcurridoEn)`
  implementa la clasificación de ADR 0010. Según ADR 0003 es propiedad exclusiva de Python (la
  escribe) — .NET solo puede **leerla** para el "panel de errores", nunca escribirla.
- **SPA**: `SmartNet/spa/src/app/inbox/` (del ítem #7) ya sigue el patrón
  contenedor/presentacional + signals que este ítem debe extender, no reemplazar —
  `feature/inbox-page/inbox-page.ts` (contenedor, dueño de los signals de filtro + `effect()` de
  refetch per ADR 0009), `data-access/inbox.service.ts` (`providedIn: 'root'`, signal privado +
  `asReadonly()`), `ui/inbox-filter/`, `ui/inbox-list/` (presentacional, `OnPush`). `inbox-list.ts`
  hoy declara "Read-only... the template never renders a button" — #13 debe relajar esto
  explícitamente para agregar la acción de reprocesar y el panel de errores. El ítem #18 ("Ajuste
  visual SPA") es un ítem distinto, puramente visual, que depende de #12 — no confundir con #13.

## Affected Areas

- `SmartNet/inbox/SmartNet.Inbox.Core/IBandejaRepository.cs`, `BandejaItem` — ampliar firma de
  consulta y proyección.
- `SmartNet/inbox/SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` — extender SQL, unir con
  `fact.ProcesamientoError`.
- `SmartNet/api/SmartNet.Api/BandejaEndpoints.cs` — aceptar los nuevos query params.
- `SmartNet/spa/src/app/inbox/**` (feature/data-access/ui/models) — extender filtros, agregar panel
  de errores, conectar reprocesar, relajar la restricción read-only.
- ADRs 0003, 0008, 0010, 0016, 0019 condicionan directamente este diseño.

## Approaches

1. **Ampliar `GET /api/bandeja` in place** (filtros + join `ProcesamientoError`, discriminador
   `origen` por fila) — calza con el contrato literal de ADR 0008. Esfuerzo: Medio.
2. **Nuevo endpoint separado `GET /api/incidencias`** — formas más limpias pero contradice el spec
   literal de ADR 0008 (requeriría enmienda del ADR). Esfuerzo: Medio + sobrecarga de ADR.

## Recommendation

Approach 1: ampliar `GET /api/bandeja` in place según ADR 0008, agregar un discriminador `origen`
explícito por fila, mantener reprocesar en el endpoint de comando ya entregado (solo fijar que
`{id}` = `ProcesamientoId`), y extender el módulo `inbox/` de la SPA ya existente en vez de crear
uno paralelo.

## Risks

- El diseño de la forma de respuesta al combinar filas de factura + filas de incidencia/error
  necesita un discriminador explícito — se resuelve en `sdd-propose`/`sdd-design`, no durante apply.
- La semántica de `{id}` de `reprocesar` cruza la frontera .NET/Python (ADR 0003) y hoy no está
  documentada — necesita fijarse.
- Levantar la restricción read-only de `inbox-list.ts` ("never renders a button") debe declararse
  explícitamente, no parchearse en silencio.
- `pagina=` no tiene precedente de paginación en ningún lugar del código — necesita una decisión
  explícita nueva (tamaño de página, forma del envelope).

## Ready for Proposal

Sí.
