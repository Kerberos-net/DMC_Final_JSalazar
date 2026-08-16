# ADR 0006: El asiento contable — ciclo de vida, estructura e invariantes

## Estado

Aceptado. Revisión 4. `ANULADO` pasa a ser **terminal** y la relación con `Factura` deja de ser 1:1;
el correlativo gana mecanismo y una regla de cambio de periodo; se congela al confirmar todo lo que
viene de catálogos externos, no solo los importes (revisión adversarial v2: C4, A1, A6, A14).

La revisión 3 cerró las reglas contables con los datos maestros reales.

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

### Entidad propia, un asiento **vigente** por factura

Una factura puede tener **varios** asientos a lo largo del tiempo, pero **a lo sumo uno no anulado**:

```
Factura 1 ─── N ─▶ AsientoContable
                   (a lo sumo UNO en estado distinto de ANULADO)
```

```sql
CREATE UNIQUE INDEX UQ_Asiento_Vigente
    ON AsientoContable (FacturaId)
    WHERE Estado <> 'ANULADO';
```

Lo que se congela al confirmar es **todo lo que viene de fuera**, no solo los importes. Ver más
abajo.

### Ciclo de vida

```
BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO  (terminal)
    ▲                      │
    └──────reabrir─────────┘
```

El asiento se genera en `BORRADOR` **al abrir la factura**, si no existe. La pantalla de detalle es
el **espacio de trabajo único**.

| Acción | Efecto |
|---|---|
| **Guardar avance** | Persiste factura y asiento. Ambos siguen editables. |
| **Validar** | En **una transacción**: factura a `VALIDADA`, asiento a `CONFIRMADO`, correlativo asignado, contador de sugerencia incrementado y evento emitido. |
| **Reabrir** | Vuelve a `BORRADOR` con motivo obligatorio y auditoría. **Conserva su número.** |
| **Anular** | Estado terminal. **Libera la factura**, que vuelve a admitir un asiento en `BORRADOR`. |

**De `ANULADO` no sale ninguna flecha.** Un asiento confirmado es un hecho, y un hecho no se deshace:
se anula y se emite otro. Dos asientos, dos números, ninguno reutilizado.

#### Reabrir y anular no son intercambiables

| | Cuándo | Qué pasa con el número |
|---|---|---|
| **Reabrir** | Corregir un asiento **dentro** de su periodo | Lo conserva |
| **Anular** | Sacarlo del libro; obliga a uno nuevo | Lo conserva el anulado; el nuevo toma otro |

El tope de reapertura por cierre de periodo se aplica **solo a reabrir**.

> **Por qué `ANULADO` es terminal.** La versión anterior no tenía transición de salida y aun así ADR
> 0008 exponía `POST /asientos/{id}/reactivar`, un endpoint sin respaldo en ninguna decisión: no
> estaba en este ciclo de vida, no tenía evento en ADR 0004 y no aparecía en `REGLAS.md`.
> Implementado tal cual, habría devuelto el asiento a `CONFIRMADO` en la base **sin emitir nada**,
> dejando el importe descontado del dashboard para siempre. El endpoint se retira.
>
> Con `ANULADO` terminal y 1:1 estricto, anular dejaba la factura **irrectificable para siempre**:
> sus notas de crédito se rechazaban por precondición, la factura seguía `VALIDADA` y no había vuelta
> atrás. El asiento vigente resuelve las dos cosas a la vez.

### Dos números, dos propósitos

| Campo | Ejemplo | Origen |
|---|---|---|
| `NumeroComprobante` | `F001-00234` | Número fiscal de la factura |
| `NumeroAsiento` | `02-2026-08-000123` | Correlativo propio, por periodo y origen |

`NumeroAsiento` se asigna **al confirmar**, nunca al abrir el borrador: si se reservara antes, cada
factura abandonada quemaría un número y el libro quedaría con huecos que en una revisión hay que
justificar.

#### Cómo se genera

Con una **tabla contador**, actualizada dentro de la misma transacción que confirma:

```sql
CREATE TABLE CorrelativoAsiento (
    Anio    SMALLINT NOT NULL,
    Mes     TINYINT  NOT NULL,
    Origen  CHAR(2)  NOT NULL,      -- '02' Compras
    Ultimo  INT      NOT NULL,
    CONSTRAINT PK_CorrelativoAsiento PRIMARY KEY (Anio, Mes, Origen)
);
```

```sql
-- Dentro de la transacción de validar
UPDATE CorrelativoAsiento WITH (UPDLOCK)
   SET Ultimo = Ultimo + 1
 OUTPUT inserted.Ultimo
 WHERE Anio = @anio AND Mes = @mes AND Origen = @origen;
```

**No sirve `SEQUENCE` ni `IDENTITY`.** Están diseñadas precisamente para no bloquear, y por eso una
transacción revertida **quema el número igual**. Y hay al menos una vía de reversión tardía: las
invariantes de confirmación se evalúan dentro de la transacción de validación. Solo la tabla contador
cumple la promesa, porque si la transacción revierte el contador revierte con ella.

El **reinicio mensual es por fila**: cada `(año, mes, origen)` arranca en cero al insertarse. No hay
proceso de cierre que reiniciar, ni tarea programada que pueda no ejecutarse.

**Costo aceptado:** serializa las confirmaciones del mismo periodo y origen. Con un solo usuario no
cuesta nada, y es el precio inherente de un correlativo sin huecos.

#### Cambio de periodo, y el único hueco posible

Si un asiento reabierto cambia su `FechaContable` a otro mes, **devuelve su número y toma uno nuevo
de la serie del mes destino**. El número devuelto **no se reutiliza**: reasignarlo a otro asiento
sería peor que el hueco, porque dos documentos distintos habrían llevado el mismo número.

Eso deja un hueco en el mes de origen, así que la promesa se enuncia con precisión:

> El correlativo no tiene huecos por **facturas abandonadas ni por validaciones fallidas**, que era
> el riesgo que motivó asignarlo al confirmar. Sí puede tenerlos por **traslado deliberado de un
> asiento a otro periodo**, que es un acto explícito del asistente, queda registrado en
> `AuditoriaCorreccion` con su motivo y por tanto **es justificable en una revisión**.

Esa es la diferencia que sostiene la decisión: un hueco por accidente no se puede explicar; uno por
traslado deliberado sí, y tiene su rastro. Trasladar a un periodo **cerrado** se rechaza, igual que
reabrir en él.

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
| `CuentaCodigo` | Código de la cuenta |
| `CuentaDescripcion` | **Congelada al confirmar** |
| `CtaReflejaCodigo`, `CtaPuenteCodigo` | **Congeladas al confirmar** |

### Qué se congela al confirmar

Todo lo que viene de **catálogos externos**, no solo los importes:

```
AsientoContableDetalle
    CuentaCodigo          '631101'
    CuentaDescripcion     'Transporte de carga'
    CtaReflejaCodigo      '791101'
    CtaPuenteCodigo       '941101'

AsientoContable
    MotivoDescripcion     'Flete de mercadería'
    TipoCambio            la cifra aplicada (ADR 0018)
```

Mientras el asiento está en `BORRADOR` todo se resuelve **en vivo** contra el catálogo, que es lo
correcto: ahí todavía se está decidiendo. Al confirmar, se fija.

> **Por qué.** Congelar los importes y dejar vivo el mapeo que los distribuye era una media medida.
> `ctarefleja` y `ctapuente` son **columnas de un catálogo externo** sobre el que esta aplicación solo
> tiene `SELECT` (ADR 0003), y de ellas sale el bloque destino entero. Dos consecuencias reales:
>
> - Si `ctarefleja` cambia entre la confirmación de una factura y la de su nota de crédito, **el
>   espejo revierte contra una cuenta distinta de la que cargó**. Las dos quedan con saldo y nada lo
>   señala, porque cada asiento cuadra por separado.
> - Si una cuenta deja de declarar `ctarefleja`, un asiento reabierto y reconfirmado **pierde su
>   bloque destino** sin que ninguna invariante lo note, porque la comprobación se enuncia sobre las
>   líneas *cuya cuenta declare* `ctarefleja` y se satisface vacía.
>
> Con esto el asiento es el **documento autocontenido** que congelar los importes quiso conseguir: se
> imprime, se exporta y se audita sin consultar nada externo. Cierra además el riesgo heredado que
> ADR 0003 dejaba abierto sobre las descripciones que dejan de resolver.

Reabrir y reconfirmar **vuelve a congelar** con los valores del momento. Si el catálogo cambió, el
asiento reconfirmado refleja el catálogo nuevo, y su nota de crédito heredará eso. Es correcto: la
reconfirmación es una decisión deliberada.

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

Para la nota de crédito, la estructura es el **espejo del documento que rectifica**, y por tanto son
dos invariantes, no una:

| Documento rectificado | Estructura de la nota |
|---|---|
| Factura `01` gravada | **Tres líneas.** Cargos al Haber = base · `401111` al Haber = IGV · proveedor al Debe = total |
| Boleta `03`, o factura `EXONERADA` / `INAFECTA` | **Dos líneas.** Cargos al Haber = **total** · proveedor al Debe = total. **Sin `401111`** |

La segunda fila faltaba. Sin ella, quien implemente la invariante literalmente hará una de dos cosas,
ambas malas: **rechazar una nota de crédito legítima**, o **generar una línea de IGV que revierte un
crédito fiscal que nunca se tomó**. La afectación del documento rectificado viaja congelada en el
asiento de la factura, así que no se vuelve a resolver.

**Del bloque `DESTINO`**: para cada línea principal con `CtaReflejaCodigo` no nulo, existe su par
reflejo/puente por el mismo importe. Se evalúa sobre el **dato congelado del asiento**, no contra el
catálogo vivo: un cambio externo ya no puede satisfacerla vacía.

**Notas de crédito, tope acumulado**: la suma de las notas **vigentes** sobre una factura no puede
exceder su monto total. Se evalúa dentro de la transacción, y **vigente significa con asiento no
anulado**:

```sql
SELECT COALESCE(SUM(f.MontoTotal), 0)
  FROM Factura f
  JOIN AsientoContable a
    ON a.FacturaId = f.FacturaId
   AND a.Estado   <> 'ANULADO'          -- el asiento vigente
 WHERE f.FacturaReferenciaId = @facturaOriginalId
   AND f.Estado              = 'VALIDADA'
   AND f.FacturaId          <> @notaActual;
```

> La versión anterior filtraba solo por `Factura.Estado = 'VALIDADA'` y afirmaba, en la misma línea,
> que *"una nota anulada libera su importe"*. **La anulación aplica al asiento, no a la factura**: una
> nota anulada conservaba `VALIDADA` y seguía sumando, de modo que la capacidad no se liberaba nunca
> y el sistema rechazaba notas legítimas con `409` sin que nadie entendiera por qué. Era la única
> consulta escrita de la única invariante que depende del estado de otras filas, y estaba mal.

`Factura.Estado` conserva sus tres valores. **No se le añade uno que refleje la anulación de su
asiento**: duplicaría una verdad que ya vive en un solo sitio.

Sin tolerancia. La igualdad exacta se garantiza por la política de conversión, no por un margen.

### Conversión a soles

Las cuatro reglas cambiarias viven en **ADR 0018**. Lo que este ADR necesita de ellas:

```
totalPEN = round(totalOrig * TCventa, 2)     ← anclado
igvPEN   = round(igvOrig   * TCventa, 2)     ← anclado
basePEN  = totalPEN - igvPEN                 ← derivado
```

La identidad se cumple **por construcción**. El IGV nunca se deriva porque sustenta el crédito
fiscal; el céntimo lo absorbe la cuenta de cargo. No hay línea de ajuste.

El tipo de cambio es el **venta** de la fecha de emisión, y se congela en el asiento al confirmar.

### Notas de crédito

Espejo de la factura que modifican. **Heredan cuatro cosas** y el asistente no vuelve a elegir
ninguna:

| Hereda | De dónde |
|---|---|
| Motivo | La factura referenciada |
| Cuenta o cuentas de cargo | La factura referenciada |
| Cuentas de destino congeladas | El asiento de la factura |
| **Tipo de cambio** | El congelado en el asiento de la factura (ADR 0018) |

#### Reparto de una nota parcial

Cuando la factura repartió el cargo entre N cuentas del motivo (ADR 0011) y la nota es **parcial**,
"hereda la cuenta" no designaba nada. La nota reparte su base entre **las mismas N cuentas, en la
misma proporción**, y el céntimo residual lo absorbe la de mayor importe:

```
Factura F, base 1000.00        Nota parcial del 40%, base 400.00
   631101   700.00  (70%)         631101   280.00  (70%)
   656101   300.00  (30%)         656101   120.00  (30%)
```

**Costo declarado:** si la devolución corresponde a una línea concreta —lo habitual al devolver
mercadería—, el reparto proporcional no representa el hecho económico exacto. Se acepta: el asiento
cuadra y el saldo por cuenta es correcto en el agregado, y la alternativa exige levantar la
prohibición de elegir.

#### Referencia interna o externa

`FacturaReferenciaId` **deja de ser obligatorio** para el tipo `07`. Lo obligatorio es **una de las
dos**:

| | Cuándo | Cómo se comporta |
|---|---|---|
| **Referencia interna** | La factura está en el sistema | Hereda las cuatro cosas. Entra en el tope acumulado. |
| **Referencia externa** | La factura es anterior al sistema | El asistente elige motivo y cuenta. **No entra en el tope.** Tipo de cambio de su propia fecha. |

La referencia externa guarda serie, número y fecha del comprobante rectificado, que el XML trae
siempre, y enciende un indicador propio.

Existe porque en el arranque —y durante meses después— llegarán notas contra facturas emitidas antes
de que el sistema existiera. Sin este camino, la única acción disponible sería descartarlas: **perder
un documento fiscal real**.

**Límite intrínseco, y hay que escribirlo:** la nota con referencia externa **no tiene control de
tope**. Nada impide registrar notas por encima del total de una factura que el sistema no conoce. El
indicador las hace visibles en la bandeja para poder revisarlas aparte.

#### Precondiciones

Validar una nota de crédito **con referencia interna** se rechaza con `409` si la factura referenciada
no existe, sigue en `PENDIENTE_VALIDACION`, está `DESCARTADA`, o **su asiento vigente** está
`ANULADO`. Las cuatro no aplican a la nota con referencia externa.

## Alternativas consideradas

- **Sin borrador: componer en el cliente y crear al validar.** Es lo que hace el prototipo. Se
  descartó porque "Guardar avance" no podría guardar el asiento, y el servidor confiaría en líneas
  que llegan del cliente.
- **Anular y emitir un asiento nuevo en vez de reabrir.** Ortodoxia contable con el rastro más
  limpio. Se descartó como sustituto de `reabrir` porque el criterio *"toda factura validada tiene
  exactamente un asiento"* dejaría de poder enunciarse. **La revisión 4 la adopta parcialmente**: no
  reemplaza a `reabrir`, pero es lo que ocurre tras anular, y el criterio se reformula como *"toda
  factura validada tiene exactamente un asiento vigente"*, que dice lo mismo y admite el histórico.
- **Añadir `reactivar` con su evento `ASIENTO_REACTIVADO`.** Cerraría el hueco documental del
  endpoint de ADR 0008 conservando el 1:1 estricto. Se descartó porque un asiento que muere y revive
  no tiene lectura contable defendible ante una revisión.
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
  caminos que probar, no uno — y con la nota de crédito sobre boleta, cuatro. ADR 0019 recoge los
  casos.
- **Costo:** toda consulta que asumiera un asiento por factura debe filtrar ahora por vigencia. Es el
  cambio de modelo más profundo de la revisión 4.
- **Costo:** cuatro columnas de texto congeladas por asiento y línea. El precio es de esquema, no de
  rendimiento, y a cambio el asiento deja de depender de un catálogo que otro sistema puede cambiar
  mañana.
- **Premisa verificada:** la invariante global 4 —el proveedor no es `P0000`— se apoya en que el
  asistente puede dar de alta un proveedor en el sistema contable **de inmediato**. Confirmado (ADR
  0003). El bloqueo con `409` no deja la factura esperando: el asistente sale, registra al proveedor,
  vuelve y lo selecciona.
- **Reversión al PRD, declarada:** el PRD pide que el asiento **se genere** con `P0000` y se corrija
  después. Este ADR lo invierte, con el fundamento de que `421211` es una cuenta por pagar por
  proveedor y un saldo contra "Varios" no se puede conciliar ni pagar. Consta en la tabla de
  reversiones del PRD.
- **Fuera de alcance, declarado:** detracción, retención y diferencia de cambio nacen al pagar o al
  cierre, y este sistema registra comprobantes. El saldo de `421212` que produce **no está ajustado
  a la fecha de cierre**, y la detracción condiciona **cuándo** se puede tomar el crédito fiscal:
  este libro no basta por sí solo para determinarlo.
