# Delta for Factura Promotion

## ADDED Requirements

### Requirement: Associated-document event projects onto the partner factura

When a `PENDIENTE` `InboxEvent` carries a non-null `documentoAsociadoId`, the
system MUST NOT run the structural sufficiency check (`PoliticaDePromocion`) for
that event and MUST NOT create a second `Factura`. Instead it MUST resolve the
partner's already-promoted, non-`DESCARTADA` `Factura` by joining
`fact.DocumentoFactura` (on `DocumentoRecibidoId = documentoAsociadoId`) to
`fact.Factura`, using only tables granted to `usr_api` (ADR 0003). If found, it
MUST insert this event's single `fact.DocumentoFactura` row onto that
`FacturaId` in one transaction and mark the event `EstadoConsumo='PROMOVIDO'`
with that `FacturaId`. If not found, it MUST leave the event `PENDIENTE` for a
later cycle (self-heal). This branch never fires when `documentoAsociadoId` is
null. (New behavior — ADR 0019 level 1 unit for the branch decision; level 2
boundary contract for the merge insert.)

#### Scenario: Partner factura found — PDF projects, no second factura

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND a non-`DESCARTADA` `Factura` exists for that partner `DocumentoRecibidoId`
- WHEN the consumer processes the event
- THEN a `fact.DocumentoFactura` row for this event is inserted on the partner `FacturaId`
- AND no new `Factura` row is created and no sufficiency check runs
- AND the event ends `EstadoConsumo='PROMOVIDO'` with that `FacturaId`

#### Scenario: Partner not yet promoted — defer and self-heal

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND no non-`DESCARTADA` `Factura` is resolvable for the partner yet
- WHEN the consumer processes the event
- THEN no `Factura` and no `fact.DocumentoFactura` row is created
- AND the event stays `EstadoConsumo='PENDIENTE'`
- AND a later cycle promotes it once the partner factura exists

#### Scenario: Order independence

- GIVEN an XML event and its associated PDF event both `PENDIENTE`
- WHEN they are processed in either order across cycles
- THEN the end state is exactly one `Factura` with two `fact.DocumentoFactura` rows

#### Scenario: Paired XML discarded — associated PDF does not self-promote

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND the paired XML event was discarded (`EstadoConsumo='DESCARTADO'`, no non-`DESCARTADA` `Factura`)
- WHEN the consumer processes the associated PDF event
- THEN the lookup resolves nothing and no `Factura` is created for the PDF
- AND the PDF event does not promote on its own (it defers with the XML)

#### Scenario: Unassociated PDF still promotes on its own (regression guard)

- GIVEN a `PENDIENTE` PDF event with `documentoAsociadoId` null and all 4 required fields present
- WHEN the consumer processes it
- THEN the normal sufficiency path runs and creates its own `Factura` as today

#### Scenario: Re-emitted associated event is an idempotent no-op

- GIVEN an associated PDF event already projected onto a partner `FacturaId`
- AND `reprocesar` re-emits an event for the same `DocumentoRecibidoId`
- WHEN the consumer processes the re-emitted event
- THEN the merge insert hits `UQ_DocumentoFactura_DocumentoRecibidoId` (SQL 2601/2627), is caught, and no duplicate row is written
- AND no second `Factura` is created

#### Scenario: No spurious PosibleDuplicado from the paired PDF

- GIVEN an XML+PDF pair promoted via this branch
- WHEN promotion completes
- THEN only the XML's `Factura` exists and no second `Factura` with `PosibleDuplicado=1` is produced by the PDF event
