# Exploration: API de facturas y asientos (BACKLOG #11)

## Current State

Las dependencias #7 y #8 están cerradas **en código**, no solo en la prosa de `BACKLOG.md`:

- `SmartNet/inbox/SmartNet.Inbox.Core` (`PoliticaDePromocion`, `ConstruccionDeFactura`,
  `CalculoDeIndicadores`, puertos `IEventoInboxRepository`/`IPromocionRepository`) y
  `SmartNet/inbox/SmartNet.Inbox.Infrastructure` (`SqlEventoInboxRepository`,
  `SqlPromocionRepository`) están completos y conectados a `SmartNet.Api/Program.cs` vía
  `PromocionBackgroundService`.
- `SmartNet/contable/SmartNet.Contable.Core` es un motor puro completo: `AsientoContable`,
  `ComposicionDeAsiento`, `LineaAsiento`, `InvariantesDeConfirmacion`, `InvarianteContable` (enum de
  las 7 invariantes de ADR 0006) e `InvarianteIncumplida` — cuyo propio doc comment dice
  "traducir a 409/412/422 es de #11", es decir, el núcleo defiere deliberadamente el mapeo HTTP a
  este ítem. `PurityScanTests.cs` verifica que no haya DB/HTTP/reloj (ADR 0019).
- `SmartNet.Api` hoy solo tiene `SesionEndpoints.cs` (login/logout) y `BandejaEndpoints.cs`
  (`GET /api/bandeja`, delegado fino a `IBandejaRepository`). No existen `PATCH`, endpoints de
  comando, ni manejo de concurrencia — #11 es el primer lugar donde aparecen concurrencia
  optimista y endpoints de comando.

## Esquema ya disponible para #11

`SmartNet/db/schema/005_negocio.sql` / `006_contratos.sql`:

- `fact.Factura.Version` y `fact.AsientoContable.Version` son `ROWVERSION NOT NULL` — listos para
  respaldar el token `If-Match`/`ETag`.
- `fact.CorrelativoAsiento (Anio, Mes, Origen, Ultimo)` — tabla contador plana (deliberadamente no
  `SEQUENCE`/`IDENTITY` según ADR 0006, asignado con `UPDATE ... WITH (UPDLOCK) ... OUTPUT
  inserted.Ultimo` dentro de la transacción de confirmación).
- `fact.AuditoriaCorreccion` (EntidadTipo FACTURA/ASIENTO/ADJUNTO, `EntidadId` polimórfico sin FK,
  `Accion` con CORRECCION/REAPERTURA/ANULACION/TRASLADO_PERIODO/…, ValorOriginal/ValorNuevo,
  Motivo, UsuarioId, OcurridoEn) ya calza con lo que #11 necesita para auditoría de correcciones.
- `fact.AsientoContable.Estado` restringido a BORRADOR/CONFIRMADO/ANULADO; `UQ_Asiento_Vigente`
  (índice único filtrado) impone "a lo sumo un asiento no ANULADO por Factura".

## ADRs relevantes

- **ADR 0008** (Contratos de comunicación) es el contrato normativo: separación REST vs. comando
  (`PATCH /api/facturas/{id}`, `PATCH /api/asientos/{id}` para ediciones; `POST
  .../abrir|validar|descartar|reabrir|anular|lineas` etc. para comandos), la tabla completa de
  casos `409`, la forma RFC 9457 de `422`, y el requisito explícito de `If-Match`/`412` en las dos
  rutas `PATCH`. `LineaId` (no la posición) es el identificador estable para rutas a nivel de línea.
- **ADR 0006** (Asiento contable) — ciclo de vida (`BORRADOR → CONFIRMADO → ANULADO` terminal),
  mecánica de asignación del correlativo, el caso límite de cambio de período/renumeración
  registrado vía `TRASLADO_PERIODO`, y qué se congela al confirmar — todo lo que los handlers de
  comando de #11 deben orquestar transaccionalmente.
- **ADR 0019** (Estrategia de verificación) — exige la separación núcleo puro / host delgado; #11
  es explícitamente la capa autorizada a tocar DB/HTTP/reloj.
- **ADR 0003** (Partición de datos) — `/api/motivos`, `/api/cuentas`, `/api/proveedores` siguen
  siendo proyecciones de solo lectura; #11 no debe agregar endpoints de escritura para `dbo.*`.
- ADR 0004 (Outbox) es adyacente: los comandos de tipo validar/anular deberían emitir filas
  `OutboxEvent` en la misma transacción — #11 es el lado productor de la cola de #14, aunque #14 en
  sí queda fuera de este alcance.

## Affected Areas

- `SmartNet/api/SmartNet.Api/*Endpoints.cs` — nuevas clases de endpoint siguiendo el patrón de
  clase estática delgada de `BandejaEndpoints.cs`.
- Una capa de orquestación nueva entre `SmartNet.Api` y `SmartNet.Contable.Core`/`SmartNet.Inbox.Core`
  que hoy no existe: carga el agregado, invoca el motor puro, traduce `InvarianteIncumplida` a
  problem+json 409/412/422, asigna el correlativo transaccionalmente, escribe
  `AuditoriaCorreccion`, emite `OutboxEvent`. El precedente más cercano de "núcleo puro + servicio
  de orquestación delgado" es el ítem #9 (`SmartNet.Sugerencia.Core` + su servicio de orquestación,
  commit `322ee0e`).
- `SmartNet/api/SmartNet.Api/Program.cs` — registro DI siguiendo el patrón existente de resolución
  perezosa de `IConfiguration`.
- `SmartNet/api/SmartNet.Api.Tests/` — nuevas pruebas de integración para 409/412/422 e ida y vuelta
  de `If-Match`, siguiendo `BandejaEndpointsTests.cs`.

## Approaches

1. **ETag = Base64 de los bytes del `rowversion` de SQL Server**, compare-and-swap en el UPDATE
   (`WHERE Version = @expected`, 0 filas afectadas ⇒ 412).
   - Pros: calza exactamente con cómo ya está diseñado el esquema; patrón ADO.NET/EF estándar;
     esfuerzo bajo.
   - Contras: ninguno relevante, dado que el esquema ya se comprometió con `rowversion`.
   - Esfuerzo: Bajo.
2. **Helper compartido de token de concurrencia** usado por ambos endpoints `PATCH`, en vez de
   duplicar el compare-and-swap por handler.
   - Pros: evita divergencia cuando lleguen `PATCH`/`DELETE /lineas/{lineaId}` a nivel de línea.
   - Contras: abstracción prematura con solo dos llamadores hoy.
   - Esfuerzo: Bajo/Medio — decidir en la fase de diseño según cuántos endpoints `PATCH` termine
     entregando #11.
3. **Dónde vive el mapeo invariante→HTTP**: mapper dedicado en `SmartNet.Api`
   (`InvarianteIncumplida → ProblemDetails`), nunca dentro de `SmartNet.Contable.Core`.
   - Pros: mantiene intacta la frontera de ADR 0019; calza con el propio doc comment del núcleo.
   - Contras: ninguno.
   - Esfuerzo: Bajo.

## Recommendation

Avanzar directo a `sdd-propose`. Usar ETags respaldados por `rowversion` (approach 1) y mantener el
mapper de invariante a HTTP en `SmartNet.Api` (approach 3); postergar la decisión de helper
compartido vs. duplicación (approach 2) a la fase de diseño, una vez acotada la superficie exacta de
`PATCH`. La fase de propuesta debe acotar explícitamente la lista completa de comandos de ADR 0008
(11+ endpoints que abarcan facturas/asientos/tipos-de-cambio/incidencias/integraciones) al subconjunto
que declara la línea de alcance de #11 en `BACKLOG.md`, dejando el resto para los consumidores
#12/#13/#14 — de lo contrario #11 corre el riesgo de absorber en silencio todo ADR 0008 de una vez.

## Risks

- Sin riesgo bloqueante — #7 y #8 están genuinamente completos en código.
- La asignación transaccional del correlativo (contador con UPDLOCK) + las escrituras de
  `AuditoriaCorreccion` son infraestructura transaccional multi-tabla nueva, sin precedente de
  adaptador existente en el código (`SqlEventoInboxRepository`/`SqlPromocionRepository` no tienen
  esta forma de transacción) — trabajo de diseño no trivial, pero no un bloqueo.
- Riesgo de scope creep: la tabla de comandos de ADR 0008 es más amplia que la línea de alcance de
  #11 en `BACKLOG.md`; necesita acotarse explícitamente en la fase de propuesta.
- Sin contexto ⚠ requerido per `BACKLOG.md` — no se necesita `REGLAS.md` ni el plan de cuentas para
  esta spec.

## Ready for Proposal

Sí.
