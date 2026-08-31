# Proposal: Registro de compra en la SPA (solo lectura)

BACKLOG #23 — *Depende de* #12 (detalle/validación), #21 (shell + contrato bandeja).
*Contexto extra requerido* ⚠ `handoff/DESIGN_BRIEF.md` §4. Reglas contables normativas:
`Documentación del negocio/REGLAS.md` §5 / §6 / §7 / §10.

## Intent

La compañía necesita ver su **libro de compras / registro de compras** (un reporte fiscal:
comprobantes de compra `01`/`03`/`07` con origen de libro `02`) sin abrir cada asiento uno por
uno. Hoy la única lectura es `GET /api/asientos/{id}` y la entrada `Registro de compra` del sidebar
es inerte desde #21/#22. Esta pantalla da una vista de período en formato cabecera + detalle de
líneas en solo lectura, con una marca visual de descuadre cabecera↔detalle.

## Scope

### In Scope
- **Endpoint listado** `GET /api/registro-compra?periodo=YYYY-MM&pagina=&tamanioPagina=` →
  `PaginaBandeja<RegistroCompraCabecera>`. `periodo` obligatorio (`YYYY-MM`), filtra sobre
  `fact.AsientoContable.FechaContable`, default = mes contable actual (fecha local).
- **Predicado de fila**: `fact.Factura.Estado = 'VALIDADA'` **y** su asiento vigente no `ANULADO`.
  Los anulados no aparecen.
- **Endpoint detalle** `GET /api/registro-compra/{asientoId}` → cabecera
  (`NumeroComprobante`, `OrigenLibro` verbatim, `NumeroAsiento`, `ProveedorCodigo` + nombre,
  `Glosa`, `FechaContable`, `TipoCambioVenta`, `BasePEN`, `IgvPEN`, `NetoPEN`) + `lineas[]`
  (`Orden`, `Bloque`, `Tipo`, `Debe`, `Haber`, `CuentaCodigo`, `CuentaDescripcion`).
- **Export Excel** `GET /api/registro-compra/export?periodo=YYYY-MM` → `.xlsx` generado **en la
  API** (precedente ADR 0021 + #22), consumido por `ui/boton-exportar` / `data-access/descarga-xlsx.ts`.
- **Repositorio de lectura**: nuevo puerto Core `IRegistroCompraRepository` +
  adaptador `SqlRegistroCompraRepository` (`AddSingleton`, connection string), `SELECT` con
  `COUNT(*) OVER()` uniendo `fact.AsientoContable + fact.Factura + LEFT JOIN dbo.Proveedor`
  (patrón `SqlBandejaRepository` del módulo inbox). PurityScan-guarded, sin regla en el endpoint.
- **SPA**: feature `registro-compra/` copiada de la estructura `catalogos/` (`feature/registro-compra-page`
  server-side paginado, `ui/registro-compra-tabla` con badge de inconsistencia,
  `ui/asiento-detalle` líneas en lectura, `data-access/registro-compra.service.ts`, `models/`).
- **Ruteo + nav**: ruta lazy hija de `ShellLayout` con `authGuard`; `Registro de compra` inerte → ruteado.

### Out of Scope / Non-goals
- **Editar, anular, reactivar** el asiento y su historial trazable — siguen siendo del #12.
- Núcleo contable / `SmartNet.Contable.Core` (ADR 0019): la marca de inconsistencia es
  presentación pura sobre importes ya devueltos.
- SQL versionado nuevo y `GRANT` nuevo — `fact_api` ya tiene `SELECT` sobre `fact.AsientoContable`,
  `fact.AsientoContableDetalle`, `fact.Factura` y `dbo.Proveedor` (`008`).
- Cambios en Python / tablas `dbo.*` de escritura (ADR 0003).
- Modificar el contrato de `GET /api/asientos/{id}` (ver Decisiones).
- Rango de fechas `desde`/`hasta`; solo `periodo` mensual.

## Decisiones resueltas en esta propuesta

1. **Fórmula de inconsistencia** (§6 / §7.1 / §10). Marca de presentación, computada en la SPA
   (`computed()`) sobre importes devueltos, **exacta al céntimo, sin epsilon** (§6: la identidad se
   cumple por construcción, "no hay tolerancia"):
   - **(a) cabecera**: `round(BasePEN + IgvPEN, 2) != round(NetoPEN, 2)` — es el ejemplo literal de
     DESIGN_BRIEF §4 ("base + IGV vs. neto").
   - **(b) cabecera↔detalle**: `round(SUM(Debe), 2) != round(SUM(Haber), 2)` sobre todas las
     `lineas[]` devueltas (invariante global §7.1).
   - **Percepción NO participa** en la fórmula (a): `401131` solo afecta el abono al proveedor
     (§5, §10.4); `NetoPEN` es base+IGV del comprobante, no incluye percepción. En (b) la percepción
     aparece en Debe y Haber y se cancela sola.
   - Se aplica igual a boleta / no gravada: `IgvPEN = 0`, `BasePEN = NetoPEN = total` (§10.2).
   - **No toca `nucleo-contable`**: ninguna llamada a `SmartNet.Contable.Core`, sin reevaluar reglas.
2. **Forma del endpoint — Approach A** (repositorio de lectura dedicado). Se descarta **B**
   (sobrecargar `IUnidadDeTrabajo`): es una transacción de escritura por comando, abriría una `tx`
   por request de lista y obliga a tocar 25+ implementadores/fakes; una proyección de reporte no
   pertenece ahí. Se descarta **extender `api-asientos`**: el registro de compra es una proyección
   de reporte (libro de compras), no el agregado editable ADR 0008. → **nueva capability
   `registro-compra-api`**, ruta propia `/api/registro-compra`.
3. **Entrega del detalle de líneas — endpoint separado** `GET /api/registro-compra/{asientoId}`.
   Alternativas: embeber `lineas[]` por fila (infla el listado de 200–1000 filas/mes) o reusar
   `GET /api/asientos/{id}` (hoy sin `NumeroComprobante`/`OrigenLibro`/`Glosa`/`NetoPEN`; obligaría
   a MODIFICAR `api-asientos` y su store transaccional). El endpoint separado, servido por el mismo
   repositorio de lectura, mantiene el listado liviano y **no toca `api-asientos`**. Tradeoff: un
   round-trip extra al expandir fila y un `SELECT` de líneas duplicado respecto de
   `CargarLineasPersistidasAsync` (aceptable, es lectura pura).
4. **Columna "Estado del asiento"**. Dado el predicado (factura `VALIDADA` + asiento vigente no
   `ANULADO`), el estado es efectivamente constante `CONFIRMADO`. → **se omite como columna**; si el
   diseño lo pide, va como leyenda única de la vista, no por fila.
5. **Proveedor**. `LEFT JOIN dbo.Proveedor ON ProveedorCodigo`. Fila ausente → se muestra solo el
   código. `P00000 (Varios)` → se muestra su nombre "Varios" (aunque §7 invariante 4 impide `P00000`
   en un asiento confirmado, se maneja sin romper).
6. **Paginación**: server-side `PaginaBandeja<T>` (`{ items, pagina, tamanioPagina, totalRegistros,
   totalPaginas }`), estándar del proyecto.
7. **`OrigenLibro`**: se expone el **valor de la columna verbatim**, nunca hard-code `'02'`.
8. **Estados vacíos**: período sin filas → tabla vacía con mensaje; asiento sin líneas → detalle
   "sin líneas contables"; proveedor no encontrado → código sin nombre.
9. **Export**: generado en la API (ADR 0021), no client-side.

## Capabilities

### New Capabilities
- `registro-compra-api`: endpoints `GET` de solo lectura del libro de compras — listado paginado
  por período, detalle de líneas por asiento, export `.xlsx`; puerto Core `IRegistroCompraRepository`
  + adaptador SQL, sin regla contable en el borde.
- `registro-compra-spa`: pantalla Angular de consulta — tabla cabecera con filtro de período y
  badge de inconsistencia, detalle de líneas en lectura, botón exportar a Excel.

### Modified Capabilities
- `spa-shell-nav`: `Registro de compra` pasa de inerte a link ruteado (→ 6 links, 2 inertes);
  actualiza los 2 escenarios de "Sidebar mirrors the handoff navigation" + el escenario de
  `sidebar.spec.ts` (mismo patrón que #22 con Proveedores/Plan contable). El glifo ya existe.

*(No se modifica `api-asientos` — ver Decisión 3.)*

## Approach

Backend: puerto Core `IRegistroCompraRepository` + `SqlRegistroCompraRepository` (ADO puro,
patrón inbox `SqlBandejaRepository`), `AddSingleton` en `Program.cs`. Endpoints delgados en
`AsientoEndpoints.cs` (o `RegistroCompraEndpoints.cs`) con `.RequireAuthorization()`, respuestas
camelCase, problem-details RFC 9457 existente. Excel con el helper de generación server-side de
ADR 0021. Tests de contrato estilo `CatalogoEndpointsTests` (DB real, cookie real): 401, camelCase,
filtro de período, envelope de paginación, período vacío, asiento sin líneas.

Frontend: feature `registro-compra/` calcada de `catalogos/` — `*-page` container OnPush con
signals de filtro/paginación, `data-access` service `providedIn:'root'` con signal privada +
`asReadonly()` + `firstValueFrom`, `cargando`/`error`. Filtro `periodo` mes-actual como
`tipo-cambio-page`. Fila → expande `ui/asiento-detalle`. Badge de inconsistencia por `computed()`.
Reusa `ui/tabla-paginador`, `ui/boton-exportar`, `data-access/descarga-xlsx.ts`. Ruta lazy
`loadComponent` hija de `ShellLayout` con `canActivate: [authGuard]`; `app.routes.spec.ts` aditivo.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core/` | New | Puerto `IRegistroCompraRepository` (PurityScan-guarded) |
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Infrastructure/` | New | `SqlRegistroCompraRepository` — `SELECT` join + `COUNT(*) OVER()` |
| `SmartNet/SmartNetApi/api/SmartNet.Api/AsientoEndpoints.cs` (o nuevo `RegistroCompraEndpoints.cs`) | Modified/New | 3 rutas `GET` + records de respuesta |
| `SmartNet/SmartNetApi/api/SmartNet.Api/Program.cs` | Modified | DI `AddSingleton` |
| `SmartNet.Api.Tests` | New | Tests de contrato del endpoint |
| `SmartNet/SmartNetWeb/src/app/registro-compra/**` | New | Feature Angular completa |
| `SmartNet/SmartNetWeb/src/app/app.routes.ts` (+ `.spec.ts`) | Modified | Ruta lazy + guard |
| `SmartNet/SmartNetWeb/src/app/shared/shell-layout/` + `sidebar` + `sidebar.spec.ts` | Modified | Link inerte → ruteado |
| `openspec/specs/spa-shell-nav/spec.md` | Modified | "Sidebar mirrors the handoff navigation" |
| `.claude/skills/integration-spa-api` README | Modified | Nuevo flujo registrado manualmente (precedente #22) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| La marca de inconsistencia se implementa como regla y cruza a `nucleo-contable` (ADR 0019) | Med | Decisión 1 la fija como `computed()` de presentación; test de pureza asegura que Core no se referencia |
| Reglas §12 ⚠ no ratificadas (TC venta, herencia de TC en NC): la pantalla solo *muestra* importes ya congelados; no recalcula | Low | Pantalla es solo lectura sobre `BasePEN/IgvPEN/NetoPEN` persistidos; no reprocesa |
| El `SELECT` de líneas duplica `CargarLineasPersistidasAsync` y diverge | Low | Mismo esquema `fact.AsientoContableDetalle`; test de contrato compara contra asiento conocido |
| Presupuesto de revisión: repo + 3 endpoints + tests + feature SPA + enmienda nav > 800 líneas | Med | `sdd-tasks` pronostica slices encadenados: (1) API + tests, (2) feature SPA, (3) enmienda `spa-shell-nav` |
| `dbo.Proveedor` sin fila para un `ProveedorCodigo` histórico | Low | `LEFT JOIN` + fallback a código (Decisión 5) |
| `periodo` malformado | Low | 400 problem-details; SPA valida `YYYY-MM` antes de llamar (patrón `tipo-cambio-page`) |

## Rollback Plan

Sin rollback de base de datos: no hay esquema nuevo ni `GRANT` nuevo.
1. Revertir la enmienda de `spa-shell-nav` y `sidebar` → `Registro de compra` vuelve a inerte.
2. Quitar la ruta lazy de `app.routes.ts` y borrar `src/app/registro-compra/`.
3. Quitar las 3 rutas `GET`, el `AddSingleton`, `SqlRegistroCompraRepository` y el puerto
   `IRegistroCompraRepository`.
Todo es aditivo (endpoints y feature net-new); `git revert` de los commits deja el sistema en
el estado #22 sin impacto downstream.

## Dependencies

- #12 (done) — detalle/validación produce los asientos `CONFIRMADO` que este libro lista.
- #21 (done) — shell/sidebar y patrón `SqlBandejaRepository` + `PaginaBandeja<T>`.
- #22 (done) — precedente de pantalla de consulta, `ui/boton-exportar`, `descarga-xlsx.ts`,
  patrón de enmienda a `spa-shell-nav`.
- ADR 0003 (partición de datos), ADR 0016 (SQL versionado — sin cambios), ADR 0019 (pureza del
  núcleo), ADR 0021 (generación de Excel en la API), ADR 0008 (agregado del asiento).

## Success Criteria

- [ ] `GET /api/registro-compra?periodo=YYYY-MM` devuelve `PaginaBandeja` con solo facturas
      `VALIDADA` cuyo asiento vigente no está `ANULADO`, filtradas por `FechaContable`.
- [ ] Abrir una fila muestra las líneas contables del asiento en modo lectura.
- [ ] El badge de inconsistencia se enciende cuando `round(BasePEN+IgvPEN,2) != round(NetoPEN,2)`
      o `round(SUM(Debe),2) != round(SUM(Haber),2)`, y solo entonces.
- [ ] "Exportar a Excel" descarga un `.xlsx` del período generado por la API.
- [ ] `Registro de compra` navega a la pantalla; los otros 2 destinos siguen inertes.
- [ ] Sin SQL versionado nuevo, sin `GRANT` nuevo, sin referencia a `SmartNet.Contable.Core`.
- [ ] `dotnet test` (API), `ng test` (SPA), PurityScanTests en verde.
