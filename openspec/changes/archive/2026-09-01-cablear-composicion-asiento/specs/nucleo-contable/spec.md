# Delta for Núcleo Contable

Change: `cablear-composicion-asiento` (BACKLOG #24). Wires `ComposicionDeAsiento.Componer`
(§5–§6) and `InvariantesDeConfirmacion` (§7) into production. No REGLAS rule is changed —
only made executable against real persisted data.

## MODIFIED Requirements

### Requirement: Invariantes globales de confirmación

El sistema MUST evaluar sobre el asiento: (1) `SUM(Debe)=SUM(Haber)`; (2) toda línea con
cuenta; (3) `FechaContable>=FechaCorteContable`; (4) proveedor≠`P00000`; (5) `Tipo=D`⇒
`Debe>0,Haber=0` (e inverso). MUST rechazar si alguna falla.

El sistema MUST además rechazar un asiento **sin bloque PRINCIPAL** — cero líneas, o sin
al menos un cargo `6x`/`1x` y su abono al proveedor. Un asiento vacío NO satisface las
invariantes de forma vacua y NO puede pasar a `CONFIRMADO`.
(Previously: the globals evaluated on the líneas present; a zero-línea asiento passed
globals 1/2/5 vacuously — `0=0`, no línea sin cuenta — and could reach `CONFIRMADO`.)

#### Scenario: Asiento válido pasa a CONFIRMADO — [unchanged]
- GIVEN Debe=Haber, toda línea con cuenta, fecha≥corte, proveedor válido, D/H consistentes
- WHEN se evalúan las invariantes globales
- THEN apto para CONFIRMADO
- (test: pure Core / ADR 0019 nivel 1)

#### Scenario: Descuadre o línea sin cuenta rechaza — [unchanged]
- GIVEN `SUM(Debe)≠SUM(Haber)` o línea sin `CuentaCodigo`
- WHEN se evalúan las invariantes globales
- THEN se rechaza indicando la invariante incumplida
- (test: pure Core / ADR 0019 nivel 1)

#### Scenario: Asiento sin líneas ya no pasa vacuamente — [new]
- GIVEN un `AsientoContable` con encabezado poblado y cero líneas
- WHEN se evalúan las invariantes de confirmación
- THEN se rechaza nombrando la ausencia del bloque PRINCIPAL — nunca resulta apto para
  `CONFIRMADO`
- (test: pure Core / ADR 0019 nivel 1 — de-vacuuming regression fixture)

## ADDED Requirements

### Requirement: `Componer` produce la semilla PRINCIPAL + DESTINO desde una `EntradaAsiento` resuelta

Dada una `EntradaAsiento` con catálogo, `ProveedorAtributo`, motivo y `TipoCambioVenta` ya
resueltos por puertos (ADR 0019 — el motor no toca BD/HTTP/reloj), el sistema MUST producir
un `AsientoContable` en fase BORRADOR con: encabezado `BasePEN`/`IgvPEN`/`NetoPEN` calculados
(§6), bloque PRINCIPAL (§5, los cuatro casos factura/boleta/gravada/no gravada) y bloque
DESTINO automático para cada cargo con `CtaReflejaCodigo` congelado. `Componer` es función
total — nunca rechaza por motivos contables en fase BORRADOR (ADR 0006). La rama de herencia
de nota de crédito queda **fuera de alcance** en este cambio (dormida hasta #10/#11).

#### Scenario: Semilla de factura gravada en soles (§10.1) — [new]
- GIVEN base 1000.00, IGV 180.00, motivo→631111 con ctarefleja 946311 / ctapuente 791111, tercero, PEN
- WHEN se compone la semilla
- THEN PRINCIPAL: 631111 D 1000.00, 401111 D 180.00, 421211 H 1180.00; DESTINO: 946311 D 1000.00, 791111 H 1000.00
- (test: pure Core golden / ADR 0019 nivel 1 — reusa §10.1)

#### Scenario: Semilla de boleta con IGV al costo (§10.2) — [new]
- GIVEN total 1180.00 PEN, motivo→656111, tercero
- WHEN se compone la semilla
- THEN 656111 D 1180.00, 421211 H 1180.00, sin línea 401111
- (test: pure Core golden / ADR 0019 nivel 1 — reusa §10.2)

#### Scenario: Semilla en dólares con redondeo derivado (§10.3) — [new]
- GIVEN USD base 1000.00, IGV 180.00, total 1180.00, TC venta 3.7895, relacionado
- WHEN se compone la semilla
- THEN totalPEN 4471.61, igvPEN 682.11, basePEN 3789.50 (derivado), cuenta de proveedor 431212
- (test: pure Core golden / ADR 0019 nivel 1 — reusa §10.3)

#### Scenario: Percepción / §10.4 no cubierta — [new / deferred gap]
- GIVEN una factura con percepción
- WHEN se intenta componer con datos de percepción
- THEN este cambio NO cubre §10.4 — no existe columna `fact.Factura.PercepcionOrig`; el
  ejemplo §10.4 permanece inalcanzable hasta un ciclo futuro (owner decision 2)
