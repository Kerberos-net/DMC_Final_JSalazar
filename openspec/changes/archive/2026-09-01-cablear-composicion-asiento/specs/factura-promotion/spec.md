# Delta for Factura Promotion

Change: `cablear-composicion-asiento` (BACKLOG #24). Promotion now also seeds the factura's
`BORRADOR` asiento (fires the `/abrir` compose step) so the detalle screen always has an
asiento with base/IGV populated (owner decision 1). Overlaps the shipped #25/#26 promotion
seam — the associated-document merge branch is unaffected.

## ADDED Requirements

### Requirement: Promotion seeds the factura's BORRADOR asiento

When promotion creates a new `Factura` (`Estado='PENDIENTE_VALIDACION'`), the system MUST also
run the `abrir` compose+seed step for that factura in the same flow, producing an engine-
composed `BORRADOR` asiento (header projection + PRINCIPAL/DESTINO líneas, default cargo
account from `ServicioDeSugerencia`).

If the seed cannot be produced because the factura is foreign-currency and no vigente
`fact.TipoCambio` exists, promotion MUST still succeed — the `Factura` is created without an
asiento, and the detalle screen later offers "generar asiento" once a tipo de cambio exists.
The seed failure MUST NOT roll back or block the factura promotion.

The seed step MUST NOT run on the associated-document merge branch (a `PENDIENTE` event with
non-null `documentoAsociadoId`): that branch projects a `fact.DocumentoFactura` row onto an
already-promoted partner factura and creates no `Factura`, so it also creates no asiento
(#25/#26 behavior unchanged).

#### Scenario: Complete PEN factura is promoted with a seeded asiento — [new]
- GIVEN a `PENDIENTE` `InboxEvent` whose payload promotes to a PEN `Factura`
- WHEN the hosted consumer processes it
- THEN the `Factura` is created `PENDIENTE_VALIDACION` AND a `BORRADOR` asiento is seeded with
  engine-composed header scalars and PRINCIPAL/DESTINO líneas
- (test: E2E promotion→asiento / integration)

#### Scenario: Foreign-currency factura with no rate promotes without an asiento — [new]
- GIVEN a `PENDIENTE` event that promotes to a USD `Factura` whose fecha de emisión has no
  vigente `fact.TipoCambio`
- WHEN the consumer processes it
- THEN the `Factura` is created `PENDIENTE_VALIDACION` with no asiento; the event ends
  `EstadoConsumo='PROMOVIDO'`; the detalle screen later offers "generar asiento"
- (test: E2E / integration)
- NOTE FOR DESIGN: confirm this matches owner intent. If a failed seed should instead fail the
  promotion, flag it during design.

#### Scenario: Associated-PDF merge branch seeds no asiento (regression guard) — [new]
- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId` resolving to an already-
  promoted partner `Factura`
- WHEN the consumer processes it
- THEN a `fact.DocumentoFactura` row is inserted on the partner `FacturaId`, no new `Factura`
  is created, and no asiento seed runs (#25/#26 path unchanged)
- (test: E2E / integration)

#### Scenario: Idempotent re-promotion does not re-seed — [new]
- GIVEN an `InboxEvent` whose `ProcesamientoId` already produced a `Factura` (with or without
  an asiento)
- WHEN promotion is attempted again
- THEN the unique-index violation is an idempotent no-op and no second asiento is seeded
- (test: E2E / integration)
