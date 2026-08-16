# Preguntas para el contador

Insumo para elaborar `REGLAS.md`. Cada pregunta indica **qué cambia en el sistema** según la
respuesta, para que se entienda por qué se pregunta y no quede como un trámite.

Ordenadas por impacto: primero lo que bloquea la implementación del asiento, después lo que altera
el modelo de datos, luego reglas de operación, y al final confirmaciones de decisiones ya tomadas.

**Cómo usar este documento:** responder bajo cada pregunta, en el bloque `Respuesta:`.

> **Las diecisiete preguntas ya están respondidas.** Los bloques `Respuesta:` de este archivo están
> vacíos porque las respuestas se registraron directamente en **`DECISIONES-REVISION.md`**, con su
> fundamento y sus alternativas, y de ahí pasaron a **`REGLAS.md`**, que es el documento normativo.
> Este cuestionario se conserva como el insumo que las originó, no como un pendiente.
>
> Lo que **sí** sigue abierto son los **seis puntos de ratificación formal por un contador**, listados
> en `REGLAS.md` §12. Son seis y no cuatro: la segunda revisión adversarial añadió el tipo de cambio
> de la nota de crédito y la estructura de la nota sobre boleta.

---

# Bloque A — Bloquean la generación del asiento

Sin estas respuestas, el sistema se puede construir pero no se puede poner a producir.

## A1. Catálogo de motivos de compra y sus cuentas

El sistema ya no deduce la cuenta a partir del producto: **el asistente selecciona un motivo de
compra** y ese motivo determina la cuenta de cargo. Cada motivo se enlaza a una o más cuentas
candidatas de 6 dígitos de las clases `6x` y `1x`.

Se necesita el catálogo inicial:

- ¿Qué motivos existen? (nombre y descripción)
- ¿Qué cuenta o cuentas de 6 dígitos corresponde a cada motivo?
- Cuando un motivo tiene varias, ¿cuál es la **predeterminada**?

> **Qué cambia:** sin este catálogo el sistema no genera ninguna línea de cargo. Es dato maestro
> obligatorio, no configuración opcional.

**Respuesta:**

## A2. Boletas (03) y crédito fiscal

Una boleta no otorga crédito fiscal como una factura.

- ¿El asiento de una boleta es **distinto** al de una factura, o solo cambia el reporte?
- Si es distinto: ¿el IGV va igualmente a `401111`, o se incorpora al **costo** en la cuenta de
  cargo, sin desagregar?

> **Qué cambia:** si el IGV de una boleta no va a `401111`, el asiento pasa de tres líneas a dos, y
> la invariante de confirmación que exige que el cargo a IGV iguale el IGV de la factura deja de
> aplicar para ese tipo de comprobante.

**Respuesta:**

## A3. IGV: exonerados, inafectos y no gravados

El sistema asume hoy que existe una base imponible y un IGV.

- ¿Siempre 18%?
- ¿Qué tratamiento reciben las operaciones **exoneradas**, **inafectas** y **no gravadas**?
- ¿Puede una misma factura mezclar líneas gravadas y no gravadas? Si sí, ¿cómo se reparte la base
  entre las cuentas de cargo?

> **Qué cambia:** una factura sin IGV rompe la invariante de tres líneas. Y una factura mixta obliga
> a que el asistente divida el cargo distinguiendo la porción gravada de la que no lo está, lo que
> es una regla de captura, no solo un cálculo.

**Respuesta:**

## A4. Notas de crédito (07)

El PRD admite la nota de crédito como tipo de comprobante, pero no define su asiento.

- ¿El asiento **invierte los signos** respecto de la factura original, o es un asiento propio con su
  propia estructura de cuentas?
- ¿Debe **vincularse al comprobante que modifica**?
- Si se vincula: ¿qué pasa si la factura original todavía no está validada, o ya fue anulada?

> **Qué cambia:** hoy `AsientoContable` es 1:1 con `Factura` y **no tiene referencia a una factura
> anterior**. Si la nota de crédito debe vincularse, el modelo de datos cambia.

**Respuesta:**

---

# Bloque B — Alteran el modelo de datos

## B1. Contabilidad por destino (cuentas de la clase 9)

- ¿La compañía lleva contabilidad analítica por destino, con cuentas de la clase 9?
- Si sí: ¿se genera **automáticamente** a partir del motivo, o la captura el asistente?

> **Qué cambia:** si la respuesta es sí, **cada asiento se duplica**: al asiento financiero se le
> suma el de destino, con su propio cuadre. `AsientoContableDetalle` necesita distinguir a qué
> bloque pertenece cada línea, y las invariantes de confirmación se aplican por bloque.

**Respuesta:**

## B2. Detracciones, retenciones y percepciones

Ninguna aparece en el PRD ni en el diseño actual.

- ¿Aplican a las compras de la compañía? ¿Cuáles y en qué supuestos?
- ¿Modifican el asiento, el importe por pagar al proveedor, o ambos?
- ¿Qué cuentas intervienen?

> **Qué cambia:** modifican el abono a la cuenta de proveedor, de modo que la invariante *"el abono a
> `421211`/`421212` iguala el monto total de la factura"* dejaría de ser cierta. Además el sistema
> necesitaría capturar el dato del porcentaje y del código de detracción.

**Respuesta:**

## B3. Diferencia de cambio

Además de convertir a soles con el tipo de cambio de la fecha de emisión:

- ¿Se registra **diferencia de cambio**?
- ¿En qué momento: al pagar, al cierre de mes, ambos?
- ¿Contra qué cuenta?

> **Qué cambia:** el sistema hoy congela los importes al confirmar el asiento y no vuelve a tocarlos.
> Registrar diferencia de cambio implicaría un asiento posterior que el diseño no contempla, y
> probablemente un momento del ciclo —el pago— que el sistema no modela.

**Respuesta:**

## B4. Cuenta de proveedor: ¿solo por moneda?

El sistema resuelve hoy la cuenta de proveedor **de forma automática por la moneda**:

| Moneda | Cuenta |
|---|---|
| Soles | `421211` Facturas y boletas en soles |
| Dólares | `421212` Facturas y boletas emitidas en dólares |

- ¿Es correcto y suficiente?
- ¿Se distingue entre **terceros** y **partes relacionadas**? Si sí, ¿con qué criterio, y de dónde
  sale ese dato?

> **Qué cambia:** si hay que distinguir relacionadas, la cuenta de proveedor deja de ser automática
> por moneda y pasa a depender de un atributo del proveedor que el catálogo maestro hoy no tiene.

**Respuesta:**

---

# Bloque C — Reglas de operación

## C1. Periodo contable cerrado

La fecha contable del asiento es hoy **libre y editable**, y nada impide contabilizar en un mes ya
cerrado.

- ¿Existe el concepto de **cierre de periodo**?
- ¿Se puede contabilizar en un mes cerrado? ¿Bajo qué condición o autorización?
- ¿Quién y cuándo cierra un periodo?

> **Qué cambia:** de esta respuesta depende **C2**, y también si el sistema necesita una entidad de
> periodo contable con su propio estado, que hoy no existe.

**Respuesta:**

## C2. Hasta cuándo se puede reabrir un asiento

Se decidió que un asiento `CONFIRMADO` puede **reabrirse** a borrador, corregirse y volver a
confirmarse, con motivo obligatorio y todo auditado. El caso de uso es el que exige el PRD: corregir
el proveedor genérico por el real.

- ¿Hay un **límite temporal** para reabrir? (mismo día, mismo mes, hasta el cierre de periodo)
- ¿Hay operaciones que **no** deban poder corregirse así, y que exijan anular y volver a emitir?

> **Qué cambia:** hoy la reapertura no tiene tope. Sin regla, un asiento de hace ocho meses se puede
> reabrir.

**Respuesta:**

## C3. Asiento contra el proveedor genérico

Cuando el sistema no identifica al proveedor, asigna `P0000 (Varios)`.

- ¿Es admisible **confirmar un asiento** contra `P0000`?
- ¿O el asiento debe quedar retenido hasta identificar al proveedor real?

> **Qué cambia:** el PRD asume lo primero, pero es una decisión contable, no de producto. Si la
> respuesta es que no se admite, hay que agregar una invariante de confirmación más.

**Respuesta:**

## C4. Correlativo del asiento

Hay una contradicción sin resolver entre el PRD y el prototipo:

- El **PRD** dice que el número de comprobante de la cabecera del asiento es **el mismo número
  extraído de la factura**, no un correlativo propio del software.
- El **prototipo** muestra además un campo `compro` (`CP-000112`), un correlativo interno distinto
  del número fiscal (`F001-00234`).

- ¿El libro de compras necesita un **correlativo propio** del sistema?
- Si sí: ¿cuál es su formato y cuándo se asigna —al crear el borrador o al confirmar—?

> **Qué cambia:** si hace falta correlativo propio, es un campo nuevo con generación secuencial sin
> huecos, que es un requisito distinto de un identificador cualquiera.

**Respuesta:**

## C5. Rechazo de documentos emitidos en domingo

Se registró como política de la compañía que **no se aceptan documentos emitidos en domingo**, y el
sistema rechaza su validación.

- ¿Es correcto y aplica **sin excepciones**?
- ¿Aplica también a **sábados** o a feriados?
- ¿Aplica a todos los tipos de comprobante, incluidas las notas de crédito?

> **Qué cambia:** hoy está implementado como bloqueo duro sobre la fecha de emisión, con dos salidas:
> corregir la fecha si la extracción leyó mal, o descartar la factura.

**Respuesta:**

---

# Bloque D — Confirmaciones de decisiones ya tomadas

Estas ya se decidieron durante el diseño. Se piden por escrito para dejar constancia.

## D1. Tipo de cambio venta

El diseño usa el **tipo de cambio venta** para convertir a soles, por tratarse de un pasivo en
moneda extranjera. (La versión anterior del documento decía "compra"; se corrigió.)

- ¿Se confirma que es **venta**?
- ¿Aplica igual a todos los tipos de comprobante?

**Respuesta:**

## D2. Absorción del redondeo

Al convertir a soles se anclan el **total** y el **IGV**, y la **base imponible se deriva** como su
diferencia:

```
totalPEN = redondear(total * TCventa, 2)
igvPEN   = redondear(igv   * TCventa, 2)
basePEN  = totalPEN - igvPEN
```

Así `base + IGV = total` se cumple siempre, sin líneas de ajuste. El criterio fue **proteger la
exactitud del IGV** por el crédito fiscal, dejando que la diferencia de céntimos la absorba la
cuenta de cargo.

- ¿Se confirma que la **cuenta de cargo** es la que debe absorber esa diferencia?
- ¿O prefiere que se genere una **línea de ajuste** explícita? Si sí, ¿contra qué cuenta?

**Respuesta:**

## D3. Cuentas fijas

| Concepto | Cuenta asumida |
|---|---|
| IGV | `401111` IGV cuenta propia |
| Proveedor, soles | `421211` Facturas y boletas en soles |
| Proveedor, dólares | `421212` Facturas y boletas emitidas en dólares |

- ¿Se confirman las tres?

**Respuesta:**

## D4. Origen del libro

El asiento se genera con origen del libro `02 Compras` por defecto.

- ¿Es siempre `02`, o hay comprobantes que van a otro libro?

**Respuesta:**
