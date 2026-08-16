# ADR 0006: El asiento contable — ciclo de vida, estructura e invariantes

## Estado

Aceptado. Revisión 3, tras cerrarse las reglas contables con los datos maestros reales.

## Contexto

El diseño original creaba el asiento en la misma transacción que la validación y a la vez exigía
*"impedir confirmar el asiento mientras exista alguna línea sin cuenta"*. No se puede impedir la
confirmación de algo ya creado atómicamente: faltaba un estado de borrador.

Al recibirse los datos maestros aparecieron tres hechos que el diseño desconocía:

- El plan de cuentas trae `ctarefleja` y `ctapuente` para 283 cuentas: la compañía lleva
  **contabilidad por destino**, y el mapeo ya existe.
- El plan distingue **terceros** (`4212`) de **partes relacionadas** (`4312`), con estructura espejo.
- El comprobante puede traer **percepción**, que aumenta lo que se debe al proveedor.

## Decisión

### Entidad propia, relación 1:1 con la factura

Importes **congelados al confirmar**, no referencias vivas a la factura.

### Ciclo de vida

```
BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO
    ▲                      │
    └──────reabrir─────────┘
```

El asiento se genera en `BORRADOR` **al abrir la factura**, si no existe. La pantalla de detalle es
el **espacio de trabajo único**.

| Acción | Efecto |
|---|---|
| **Guardar avance** | Persiste factura y asiento. Ambos siguen editables. |
| **Validar** | En **una transacción**: factura a `VALIDADA`, asiento a `CONFIRMADO`, correlativo asignado, contador de sugerencia incrementado y evento emitido. |
| **Reabrir** | Vuelve a `BORRADOR` con motivo obligatorio y auditoría. |

### Dos números, dos propósitos

| Campo | Ejemplo | Origen |
|---|---|---|
| `NumeroComprobante` | `F001-00234` | Número fiscal de la factura |
| `NumeroAsiento` | `02-2026-08-000123` | Correlativo propio, por periodo y origen |

`NumeroAsiento` se asigna **al confirmar**, nunca al abrir el borrador: si se reservara antes, cada
factura abandonada quemaría un número y el libro quedaría con huecos que en una revisión hay que
justificar.

### Estructura: dos bloques

```
FACTURA gravada · base 1,000 · IGV 180 · Motivo 22 → 631111

BLOQUE PRINCIPAL
  631111  Flete traslado de mercadería   Debe   1000.00
  401111  IGV cuenta propia              Debe    180.00
  421211  Facturas y boletas en soles    Haber  1180.00

BLOQUE DESTINO (derivado del plan de cuentas)
  946311  ctarefleja de 631111           Debe   1000.00
  791111  ctapuente de 631111            Haber  1000.00
```

`AsientoContableDetalle` lleva un discriminador de **`Bloque`**: `PRINCIPAL` o `DESTINO`.

El bloque de destino se genera **automáticamente** para cada línea de cargo cuya cuenta declare
`ctarefleja`: reflejo al Debe, puente al Haber, por el importe de la línea. Es mecánico y no
requiere criterio del asistente.

| Cuenta principal | `ctarefleja` | `ctapuente` |
|---|---|---|
| Compras de existencias (clase 60) | `20x`, `24x`, `25x` | `61x` variación |
| Gastos (clases 62-68) | `94x`, `95x`, `97x` | `791111` cargas imputables |

### Estructura del bloque principal por tipo de comprobante

| Caso | Cargo | IGV | Percepción | Abono |
|---|---|---|---|---|
| Factura gravada (`01`) | base | `401111` | si aplica, `401131` | total + percepción |
| Boleta (`03`) o factura no gravada | **total** | — | — | total |
| Nota de crédito (`07`) | **al Haber**, heredado | al Haber | — | **al Debe** |

Una boleta no otorga crédito fiscal, de modo que el IGV **se incorpora al costo**. Lo mismo aplica a
las operaciones `EXONERADA` e `INAFECTA`.

### Cuenta de proveedor: moneda × relación

| Relación | Moneda | Cuenta |
|---|---|---|
| Tercero | Soles | `421211` |
| Tercero | Dólares | `421212` |
| Relacionada | Soles | `431211` |
| Relacionada | Dólares | `431212` |

Se resuelve de forma **determinista**, sin sugerencia. `EsRelacionada` es atributo estable del
proveedor (ADR 0003), no una decisión por factura.

### Modelo de la línea

| Campo | Propósito |
|---|---|
| `LineaId` | Identificador **estable**. El contrato opera sobre él, nunca sobre la posición. |
| `Orden` | Presentación. No identifica. |
| `Bloque` | `PRINCIPAL` o `DESTINO` |
| `Tipo` | `D` o `H` |
| `Debe`, `Haber` | `DECIMAL(18,2)`. Nunca `float`. |

```sql
CONSTRAINT CK_Linea_Tipo CHECK (
    (Tipo = 'D' AND Debe > 0 AND Haber = 0) OR
    (Tipo = 'H' AND Haber > 0 AND Debe = 0)
)
```

### Invariantes de confirmación

**Globales**

1. `SUM(Debe) = SUM(Haber)` sobre todo el asiento.
2. Ninguna línea sin cuenta contable.
3. `FechaContable` **no anterior** a `Configuracion.FechaCorteContable`.
4. El proveedor **no es** `P0000 (Varios)`.

**Del bloque `PRINCIPAL`** — dependen del tipo de comprobante:

| Comprobante | Cargos `6x`/`1x` igualan | Cargo a `401111` | Abono al proveedor |
|---|---|---|---|
| Factura gravada | base imponible | el IGV | total + percepción |
| Boleta o no gravada | **monto total** | no aplica | monto total |

**Del bloque `DESTINO`**: para cada línea principal con `ctarefleja`, existe su par reflejo/puente
por el mismo importe.

**Notas de crédito**: la suma de las notas **vigentes** sobre una factura no puede exceder su monto
total. Se evalúa dentro de la transacción; una nota anulada **libera** su importe.

Sin tolerancia. La igualdad exacta se garantiza por la política de conversión, no por un margen.

### Conversión a soles

Tipo de cambio **venta** —una compra genera un pasivo en moneda extranjera—, congelado al confirmar.

```
totalPEN = round(totalOrig * TCventa, 2)     ← anclado
igvPEN   = round(igvOrig   * TCventa, 2)     ← anclado
basePEN  = totalPEN - igvPEN                 ← derivado
```

La identidad se cumple **por construcción**. El IGV nunca se deriva porque sustenta el crédito
fiscal; el céntimo lo absorbe la cuenta de cargo. No hay línea de ajuste.

### Notas de crédito

Espejo de la factura que modifican, con **`FacturaReferenciaId` obligatorio**. Heredan motivo y
cuenta de cargo: el asistente no vuelve a elegir. El dato existe en el XML UBL.

Validar una nota de crédito se rechaza con `409` si la factura referenciada no existe, sigue en
`PENDIENTE_VALIDACION`, está `DESCARTADA`, o su asiento está `ANULADO`.

## Alternativas consideradas

- **Sin borrador: componer en el cliente y crear al validar.** Es lo que hace el prototipo. Se
  descartó porque "Guardar avance" no podría guardar el asiento, y el servidor confiaría en líneas
  que llegan del cliente.
- **Anular y emitir un asiento nuevo en vez de reabrir.** Ortodoxia contable con el rastro más
  limpio. Se descartó porque rompe la relación 1:1 y el criterio *"toda factura validada tiene
  exactamente un asiento"* dejaría de poder enunciarse.
- **Tolerancia de un céntimo en el cuadre.** Es lo que implementa el prototipo. Se descartó: un
  asiento descuadrado por 0.01 sigue descuadrado, y el libro acumularía céntimos que nadie cuadra.
- **Generar el destino como asiento separado.** Se descartó porque rompería la relación 1:1 y
  obligaría a decidir su origen y su anulación conjunta.
- **Entidad `PeriodoContable` con estados.** Permitiría reabrir un mes concreto y dejaría historial
  de cierres. Se descartó por ser una segunda fuente de verdad sobre qué está cerrado, cuando el
  cierre real ocurre en el sistema contable de la compañía.

## Consecuencias

- El asistente compone el asiento con persistencia real y lo confirma cuando está correcto.
- La contabilidad por destino se genera sola, desde el propio plan de cuentas.
- La corrección posterior tiene camino definido, auditado, y **propaga a Drive y Sheets**.
- Una sola `FechaCorteContable` cierra dos cosas: contabilizar en periodo cerrado y el tope de
  reapertura.
- **Costo:** el asiento pasa de 3 líneas a 5 o más. La pantalla debe distinguir los bloques con
  claridad, o el asistente creerá que el asiento está duplicado.
- **Costo:** importes duplicados entre `Factura` y `AsientoContable`, con semántica distinta —dato
  del documento frente a dato contabilizado—. No deben "sincronizarse".
- **Costo:** las invariantes del bloque principal **dependen del tipo de comprobante**. Son tres
  caminos que probar, no uno.
- **Fuera de alcance, declarado:** detracción, retención y diferencia de cambio nacen al pagar o al
  cierre, y este sistema registra comprobantes. El saldo de `421212` que produce **no está ajustado
  a la fecha de cierre**, y la detracción condiciona **cuándo** se puede tomar el crédito fiscal:
  este libro no basta por sí solo para determinarlo.
