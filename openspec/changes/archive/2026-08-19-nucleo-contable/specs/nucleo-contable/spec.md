# Núcleo Contable Specification

## Purpose

Motor puro (sin BD/HTTP/reloj, ADR 0019) que genera un `AsientoContable` desde catálogos (#3) y
tipos de cambio (#4) ya resueltos, y evalúa las invariantes de confirmación `REGLAS.md` §7 que son
evaluables desde un único asiento ya congelado (globales 1–5, PRINCIPAL, DESTINO). No persiste, no
expone HTTP, no sugiere cuenta (#9), no implementa el catálogo de rechazo §8 ni la precondición de
NC "factura original validada" (§8/§12.4) — relajada por decisión del dueño del proyecto; #8 no
debe codificarla ni como placeholder.

**Fuera de alcance — tope acumulado de NC (§7):** exige `SELECT` sobre otros asientos (suma de NC
vigentes contra la misma factura), lo que rompe la pureza de #8 (ADR 0019: sin base de datos). Esa
evaluación pertenece a #10, que sí tiene acceso a los asientos persistidos.

## Requirements

### Requirement: Bloque PRINCIPAL — factura gravada
Para factura `01` `GRAVADA` el sistema MUST generar: cargo cuenta-motivo=base, cargo `401111`=IGV,
cargo `401131`=percepción si aplica, abono proveedor(moneda×EsRelacionada)=total+percepción.

#### Scenario: Factura gravada en soles (§10.1)
- GIVEN base 1000.00, IGV 180.00, motivo 22→631111, tercero, PEN
- WHEN se genera PRINCIPAL
- THEN 631111 D 1000.00, 401111 D 180.00, 421211 H 1180.00

#### Scenario: Factura con percepción (§10.4)
- GIVEN base 1000.00, IGV 180.00, percepción 23.60, tercero, PEN
- WHEN se genera PRINCIPAL
- THEN 401131 D 23.60, abono 1203.60; 401131 sin bloque DESTINO

### Requirement: Bloque PRINCIPAL — boleta o no gravada
Para boleta `03` o factura `EXONERADA`/`INAFECTA` el sistema MUST generar dos líneas: cargo
cuenta-motivo=monto total (sin `401111`), abono proveedor=monto total.

#### Scenario: Boleta — IGV al costo (§10.2)
- GIVEN total 1180.00 PEN, motivo 19→656111, tercero
- WHEN se genera PRINCIPAL
- THEN 656111 D 1180.00, 421211 H 1180.00, sin línea 401111

### Requirement: Bloque PRINCIPAL — NC sobre factura gravada
Para NC `07` con referencia interna a factura gravada, el sistema MUST generar el espejo invertido
(3 líneas), heredando motivo, cuenta de cargo y cuentas de destino congeladas de la factura.

#### Scenario: Nota de crédito sobre factura gravada (§10.5)
- GIVEN NC sobre F001-00234, base 200.00, IGV 36.00, total 236.00
- WHEN se genera PRINCIPAL
- THEN proveedor D 236.00, 631111 H 200.00, 401111 H 36.00

### Requirement: Bloque PRINCIPAL — NC sobre boleta o no gravada
Para NC con referencia interna a boleta/no-gravada (afectación congelada en la factura), el sistema
MUST generar el espejo invertido de 2 líneas, sin `401111`.

#### Scenario: Nota de crédito sobre boleta (§10.6)
- GIVEN NC sobre B001-00512, total 118.00, motivo heredado→656101
- WHEN se genera PRINCIPAL
- THEN proveedor D 118.00, 656101 H 118.00, sin línea 401111

### Requirement: Bloque DESTINO automático
Para cada línea de cargo con `CtaReflejaCodigo` congelado, el sistema MUST generar el par
reflejo(D)/puente(H) por el mismo importe; en NC el par también se invierte. Sin `CtaReflejaCodigo`
no hay bloque DESTINO.

#### Scenario: Destino de un flete (§10.1)
- GIVEN 631111→ctarefleja 946311/ctapuente 791111, importe 1000.00
- WHEN se genera DESTINO
- THEN 946311 D 1000.00, 791111 H 1000.00

#### Scenario: Destino invertido en NC (§10.5)
- GIVEN NC con destino congelado 946311/791111, importe 200.00
- WHEN se genera DESTINO de la NC
- THEN 946311 H 200.00, 791111 D 200.00

### Requirement: Conversión de moneda ancla/deriva
El sistema MUST calcular `totalPEN`/`igvPEN` como `redondear(monto×TCventa,2)` (anclados) y
`basePEN=totalPEN−igvPEN` (DERIVADO). MUST NOT crear línea de ajuste; el céntimo lo absorbe el cargo.

#### Scenario: Factura en dólares con redondeo derivado (§10.3)
- GIVEN USD base 1000.00, IGV 180.00, total 1180.00, TC venta 3.7895, relacionado
- WHEN se convierte a PEN
- THEN totalPEN 4471.61, igvPEN 682.11, basePEN 3789.50, cuenta 431212

### Requirement: NC hereda el tipo de cambio de su factura
Una NC `07` con referencia interna MUST usar el `TCventa` congelado en el asiento de la factura
referenciada, no el de su propia fecha.

#### Scenario: NC del 100% en dólares deja el pasivo en cero (§10.7)
- GIVEN factura USD 11525.42, TC congelado 3.712000; NC con TC propio 3.715000
- WHEN se genera NC por el 100%
- THEN se usa 3.712000; saldo 421212 del proveedor = 0.00

### Requirement: Invariantes globales de confirmación
El sistema MUST evaluar sobre el asiento: (1) `SUM(Debe)=SUM(Haber)`; (2) toda línea con cuenta;
(3) `FechaContable>=FechaCorteContable`; (4) proveedor≠`P00000`; (5) `Tipo=D`⇒`Debe>0,Haber=0` (e
inverso). MUST rechazar si alguna falla.

#### Scenario: Asiento válido pasa a CONFIRMADO
- GIVEN Debe=Haber, toda línea con cuenta, fecha≥corte, proveedor válido, D/H consistentes
- WHEN se evalúan las invariantes globales
- THEN apto para CONFIRMADO

#### Scenario: Descuadre o línea sin cuenta rechaza
- GIVEN `SUM(Debe)≠SUM(Haber)` o línea sin `CuentaCodigo`
- WHEN se evalúan las invariantes globales
- THEN se rechaza indicando la invariante incumplida

### Requirement: Invariante del bloque PRINCIPAL por tipo de comprobante
Según la afectación congelada, el sistema MUST verificar que cargos `6x`/`1x` igualen base (gravada)
o monto total (boleta/no-gravada/NC-sobre-boleta), `401111`=IGV cuando aplica, proveedor=total(+percepción).

#### Scenario: Factura gravada consistente
- GIVEN cargos 6x/1x=base, 401111=IGV
- WHEN se evalúa la invariante PRINCIPAL
- THEN pasa

#### Scenario: 401111 indebido en NC sobre boleta rechaza
- GIVEN NC sobre boleta con línea 401111
- WHEN se evalúa la invariante PRINCIPAL
- THEN se rechaza — la boleta no otorgó crédito fiscal

### Requirement: Invariante del bloque DESTINO sobre datos congelados
Para cada línea PRINCIPAL con `CtaReflejaCodigo` congelado, el sistema MUST verificar su par
reflejo/puente por el mismo importe, contra el dato congelado, nunca el catálogo vivo.

#### Scenario: Par reflejo/puente presente
- GIVEN línea con CtaReflejaCodigo congelado, importe 1000.00, con su par DESTINO 1000.00
- WHEN se evalúa la invariante DESTINO
- THEN pasa

#### Scenario: Falta el par aunque el catálogo ya no declare ctarefleja
- GIVEN línea con CtaReflejaCodigo congelado, sin su par reflejo/puente
- WHEN se evalúa la invariante DESTINO
- THEN se rechaza, aunque el catálogo vivo ya no lo declare

### Requirement: Consumo read-only de catálogos y tipos de cambio
El sistema MUST consumir `CuentaContable`/`ResolucionDePrefijos` (#3) y `TipoCambio`/
`ResultadoTipoCambio` (#4) como entradas ya resueltas, sin modificarlos ni re-resolverlos.

#### Scenario: El motor no re-resuelve el tipo de cambio
- GIVEN un `TipoCambio.Venta` ya seleccionado por #4
- WHEN el motor genera el asiento
- THEN lo usa tal cual, sin volver a consultar SBS/MANUAL
