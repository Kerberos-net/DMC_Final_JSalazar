# REGLAS.md — Reglas contables del Gestor de Facturas de Compra

**Versión 2.**

Documento de referencia para implementar la generación del asiento contable. Recoge las reglas
cerradas con los datos maestros reales de la compañía.

### Qué cambió en la v2

Seis secciones, tras la segunda revisión adversarial. Si ya leíste la v1, esto es lo nuevo:

| § | Cambio | Por qué |
|---|---|---|
| **5** | La nota de crédito sobre **boleta** tiene dos líneas y **sin `401111`** | Aplicar la estructura de tres revertía un crédito fiscal que nunca se tomó |
| **5** | La nota **parcial** reparte en la misma proporción que la factura | "Hereda la cuenta" no designaba nada cuando el cargo estaba repartido en N cuentas |
| **5** | Existe la nota con **referencia externa** | Las notas contra facturas anteriores al sistema no tenían más salida que descartarlas: perder un documento fiscal |
| **5** | El bloque destino usa las cuentas **congeladas** en el asiento | Un cambio en el catálogo externo hacía que el espejo revirtiera contra otra cuenta, sin que nada lo señalara |
| **6** | La nota de crédito **hereda el tipo de cambio** de su factura | Una nota del 100% en dólares no dejaba el pasivo en cero: dejaba un residuo cambiario permanente |
| **7** | El tope de notas se evalúa contra el **asiento vigente** | La consulta filtraba por el estado de la factura, que la anulación no toca: la capacidad no se liberaba nunca |
| **7** | Invariante del tipo `07` **partida en dos**, por afectación | Faltaba el caso de la boleta |
| **8** | La **factura mixta** se detecta desde el XML, con cobertura declarada | La regla existía y el sistema no tenía con qué cumplirla |
| **9** | `ANULADO` es **terminal**; anular libera la factura | Anular dejaba la factura irrectificable para siempre |
| **9** | El correlativo tiene **mecanismo**, y su promesa es condicionada | `SEQUENCE` quema el número en una transacción revertida |
| **12** | **Seis** puntos pendientes de ratificación, no cuatro | El §6 y el §5 nuevos son del mismo capítulo |

**Origen de las reglas:** las diecisiete preguntas de `PREGUNTAS-CONTABLES.md`, respondidas y
registradas en `DECISIONES-REVISION.md`. La clasificación de motivos está en
`MOTIVOS-CLASIFICACION.md`.

**Cómo leer este documento:** es normativo. Donde dice "se rechaza", el sistema **debe** rechazar.
Donde una regla depende de una condición que puede cambiar, se marca con ⏳ y se explica cuándo
revisarla.

---

## 1. Alcance

### Lo que este sistema hace

Registra **comprobantes de compra recibidos** —facturas `01`, boletas `03` y notas de crédito
`07`— y genera su asiento contable con origen de libro `02 COMPRAS`.

### Lo que este sistema NO hace

| Fuera de alcance | Dónde vive |
|---|---|
| **Detracciones** | Ocurren al pagar. Origen `06 BANCOS` |
| **Retenciones** | Ocurren al pagar. Origen `06 BANCOS` |
| **Diferencia de cambio** | Nace al pagar o al cierre |
| **Gasto sin comprobante** | No entra por aquí: el sistema parte de una factura recibida por correo |
| **Alta de proveedores** | Otro sistema, otro flujo |
| **Cierre de periodo** | Sistema contable de la compañía |

### Dos consecuencias que el usuario de la información debe conocer

1. El saldo de `421212` / `431212` que produce este sistema **no está ajustado a la fecha de
   cierre**. Quien realice el cierre mensual debe revaluar los pasivos en moneda extranjera por su
   cuenta.
2. La detracción condiciona **cuándo** puede tomarse el crédito fiscal. **Este libro no basta por sí
   solo** para determinar el crédito fiscal del periodo.

---

## 2. Datos maestros

Los mantiene el **sistema contable de la compañía**. Esta aplicación **solo lee**.

| Catálogo | Contenido |
|---|---|
| `CuentaContable` | 1650 cuentas. **Solo las de 6 dígitos son imputables** (907). Los niveles 2 a 5 son jerarquía. |
| `Motivo` | 90 motivos, cada uno con uno o varios **prefijos** de cuenta separados por coma |
| `Proveedor` | Incluye el genérico `P0000 (Varios)` |
| `Origen` | 13 orígenes de libro |

Los atributos que este proyecto necesita y el catálogo no aporta viven en **tablas satélite**:
`MotivoAtributo` (activo, origen), `ProveedorAtributo` (relacionada) y `SugerenciaCuenta`.

---

## 3. Motivo de compra

### El motivo determina la cuenta de cargo

Es **obligatorio**. Sin motivo no hay cuenta de cargo y la validación se rechaza.

### Resolución de candidatas por prefijo

El motivo declara **prefijos**, no cuentas. Las candidatas son todas las **hojas de 6 dígitos** cuyo
código empieza por alguno de esos prefijos.

```
Motivo 22  Fletes traslado de mercadería  →  631111                    →   1 candidata
Motivo 48  Gastos de representación       →  6373                      →   6 candidatas
Motivo  6  Transferencia entre Bancos     →  104                       →  20 candidatas
Motivo 70  Préstamos a terceros           →  16                        →  34 candidatas
Motivo  8  Tributos por pagar             →  4011,4017,4018,403,417,…  →  22 candidatas
```

Una cuenta nueva creada bajo un prefijo ya declarado **aparece sola**, sin tocar nada.

### Alcance: solo motivos de origen 02

La pantalla de validación ofrece **únicamente los motivos activos de origen `02` COMPRAS**. De ahí
se deriva que el origen del asiento sea siempre `02`.

⏳ **Estado actual: 50 motivos disponibles.** De ellos, **22 fueron reclasificados desde
`07 CAJA CHICA` por necesidad de la demostración**. Contablemente son de caja chica y **debe
revertirse antes de producción**. Están marcados con `†` en `MOTIVOS-CLASIFICACION.md`.

### Motivos dados de baja

`1`, `28`, `39`, `44`, `76` y `83`. **No se borran**: los asientos históricos los referencian.
`MotivoAtributo.Activo` los retira del selector.

### Sugerencia de cuenta

Cuando el motivo tiene varias candidatas, el sistema sugiere una por **frecuencia histórica**:

1. Cuenta más usada para el par `(proveedor, motivo)`.
2. Si no hay historial, la más usada para el `motivo` a nivel global.
3. Si tampoco, la primera candidata del motivo.

El contador de frecuencia se incrementa **al confirmar el asiento**, no al sugerir.

**La sugerencia nunca decide sola.** El asistente confirma o corrige, y la interfaz muestra el
fundamento: *"usado 14 de 15 veces con este proveedor"*.

El mismo mecanismo, considerando solo el proveedor, sugiere el **motivo**.

---

## 4. Cuentas fijas

### IGV

| Concepto | Cuenta |
|---|---|
| IGV de la compra | `401111` IGV – CUENTA PROPIA |
| Percepción | `401131` IGV – RÉGIMEN DE PERCEPCIONES |

⏳ **`401111` vale mientras la compañía tome el crédito fiscal íntegro.** Si aparecen ventas no
gravadas y hay que prorratear, la cuenta pasaría a depender del destino de la compra: `401161`
(operaciones gravadas) y `401171` (operaciones comunes) ya existen en el plan.

### Cuenta de proveedor

Se resuelve de forma **determinista**, cruzando moneda con la marca `EsRelacionada` del proveedor.

| Relación | Moneda | Cuenta |
|---|---|---|
| Tercero | Soles | `421211` FACTURAS Y BOLETAS EN SOLES |
| Tercero | Dólares | `421212` FACTURAS Y BOLETAS EN DOLARES |
| Relacionada | Soles | `431211` FACTURAS Y BOLETAS RELAC. EN SOLES |
| Relacionada | Dólares | `431212` FACTURAS Y BOLETAS RELAC. EN DOLARES |

Siempre bajo `4212` / `4312` **EMITIDAS**. Las subcuentas `4211` / `4311` **NO EMITIDAS** son para
provisiones sin comprobante, y este sistema parte siempre de un comprobante recibido.

`EsRelacionada` es un atributo **estable del proveedor**, no una decisión por factura.
`P0000 (Varios)` es siempre **tercero**.

---

## 5. Estructura del asiento

El asiento tiene **dos bloques**.

### Bloque PRINCIPAL — según el tipo de comprobante

**Factura gravada (`01`, `Afectacion = GRAVADA`)**

```
  6xxxxx / 1xxxxx   cuenta del motivo       Debe    base imponible
  401111            IGV cuenta propia       Debe    IGV
  401131            percepción (si aplica)  Debe    percepción
  4212xx / 4312xx   proveedor               Haber   total + percepción
```

**Boleta (`03`) o factura `EXONERADA` / `INAFECTA`**

No otorgan crédito fiscal, de modo que **el IGV se incorpora al costo**.

```
  6xxxxx / 1xxxxx   cuenta del motivo       Debe    monto total
  4212xx / 4312xx   proveedor               Haber   monto total
```

**Nota de crédito (`07`)**

Espejo de la factura que modifica. Hereda **cuatro cosas** y el asistente no elige ninguna:

| Hereda | De dónde |
|---|---|
| Motivo | La factura referenciada |
| Cuenta o cuentas de cargo | La factura referenciada |
| Cuentas de destino congeladas | El asiento de la factura |
| **Tipo de cambio** | El congelado en el asiento de la factura (§6) |

**La estructura es el espejo del documento que rectifica**, no siempre la de una factura gravada:

*Sobre factura `01` gravada — tres líneas*

```
  4212xx / 4312xx   proveedor               Debe    total de la nota
  6xxxxx / 1xxxxx   cuenta heredada         Haber   base
  401111            IGV cuenta propia       Haber   IGV
```

*Sobre boleta `03`, o factura `EXONERADA` / `INAFECTA` — dos líneas, sin IGV*

```
  4212xx / 4312xx   proveedor               Debe    total de la nota
  6xxxxx / 1xxxxx   cuenta heredada         Haber   total de la nota
```

La segunda estructura faltaba. Aplicar la primera a una boleta **generaría una línea de IGV que
revierte un crédito fiscal que nunca se tomó**; rechazarla dejaría sin registrar una nota de crédito
legítima. La afectación del documento rectificado viaja **congelada en el asiento de la factura**, de
modo que no se vuelve a resolver contra el catálogo.

**Reparto de una nota parcial.** Cuando la factura repartió el cargo entre N cuentas (§5, "División
del cargo") y la nota es parcial, se reparte entre **las mismas N cuentas, en la misma proporción**.
El céntimo residual lo absorbe la cuenta de mayor importe.

```
Factura F, base 1000.00        Nota parcial del 40%, base 400.00
   631101   700.00  (70%)         631101   280.00  (70%)
   656101   300.00  (30%)         656101   120.00  (30%)
```

*Costo declarado:* si la devolución corresponde a una línea concreta —lo habitual al devolver
mercadería—, el reparto proporcional no representa el hecho económico exacto. Se acepta: el asiento
cuadra y el saldo por cuenta es correcto en el agregado.

**Nota de crédito con referencia externa.** Contra una factura **anterior al sistema**, que por tanto
no existe aquí. No hay de quién heredar:

| | Referencia interna | Referencia externa |
|---|---|---|
| Motivo y cuenta | Heredados | **Los elige el asistente** |
| Tipo de cambio | Heredado de la factura | El de su **propia** fecha (§6) |
| Tope acumulado (§7) | Se aplica | **No se aplica** |
| Precondiciones (§8) | Se aplican | No se aplican |

Guarda serie, número y fecha del comprobante rectificado —el XML los trae siempre— y enciende un
indicador propio.

Existe porque en el arranque, y durante meses después, llegarán notas contra facturas emitidas antes
de que el sistema existiera. Sin este camino, la única acción disponible sería descartarlas: **perder
un documento fiscal real**.

> **Límite intrínseco, no descuido.** La nota con referencia externa **no tiene control de tope**:
> nada impide registrar notas por encima del total de una factura que el sistema no conoce. El
> indicador las hace visibles en la bandeja para poder revisarlas aparte.

### Bloque DESTINO — contabilidad analítica

Se genera **automáticamente** desde el plan de cuentas. Para **cada línea de cargo** cuya cuenta
declare `ctarefleja`:

```
  ctarefleja        Debe    importe de la línea principal
  ctapuente         Haber   importe de la línea principal
```

En una nota de crédito, el bloque de destino **también se invierte**, y usa las **cuentas congeladas
en el asiento de la factura**, no las que el catálogo declare hoy.

> **Por qué congeladas.** `ctarefleja` y `ctapuente` son columnas de un catálogo **externo**, que
> mantiene el sistema contable de la compañía. Si cambian entre la confirmación de una factura y la
> de su nota de crédito, el espejo revertiría **contra una cuenta distinta de la que cargó**: las dos
> quedarían con saldo y nada lo señalaría, porque cada asiento cuadra por separado.
>
> Al confirmar, la línea guarda `CuentaCodigo`, `CuentaDescripcion`, `CtaReflejaCodigo` y
> `CtaPuenteCodigo`. El asiento pasa a ser **autocontenido**: se imprime, se exporta y se audita sin
> consultar ningún catálogo externo.

El mapeo ya existe en el plan, de modo que es mecánico y no requiere criterio del asistente:

| Cuenta principal | `ctarefleja` | `ctapuente` | Cuentas afectadas |
|---|---|---|---|
| Compras de existencias (clase 60) | `20x`, `24x`, `25x` | `61x` variación de existencias | 16 |
| Gastos (clases 62-68) | `94x`, `95x`, `97x` | `791111` cargas imputables | 267 |

Una cuenta **sin `ctarefleja` no genera bloque de destino**. Es el caso de las cuentas de clase 1
y 4.

### División del cargo

El asistente puede repartir el cargo entre **varias cuentas del mismo motivo**. La suma de esas
líneas debe igualar la base imponible —o el monto total en los casos sin IGV—.

---

## 6. Conversión de moneda

Las cuatro reglas cambiarias están decididas en **ADR 0018**, que es donde viven con su fundamento y
sus alternativas. Esta sección recoge lo que hace falta para implementar.

Se usa el **tipo de cambio VENTA**: una compra genera un pasivo en moneda extranjera, y los pasivos
se convierten a venta.

```
totalPEN = redondear(totalOrig × TCventa, 2)     ← anclado
igvPEN   = redondear(igvOrig   × TCventa, 2)     ← anclado
basePEN  = totalPEN − igvPEN                     ← DERIVADO
```

`basePEN + igvPEN = totalPEN` se cumple **por construcción**. No hay tolerancia ni línea de ajuste.

**Por qué se deriva la base y no el IGV:** el IGV sustenta el crédito fiscal y debe ser exacto
respecto del comprobante. La diferencia de redondeo la absorbe la cuenta de cargo, donde un céntimo
no tiene consecuencia tributaria.

### Tipo de cambio disponible

La SBS publica por las noches, y lo publicado el viernes cubre **sábado, domingo y lunes**.

Si **no hay tipo de cambio** para la fecha de emisión, la factura en moneda extranjera **no se abre
para edición**. Primero se carga el tipo de cambio, y entonces puede trabajarse.

La carga manual queda marcada con `Origen = 'MANUAL'`. Si la SBS publica después para esa fecha,
**no pisa la fila manual en silencio**: registra la discrepancia.

### La nota de crédito hereda el tipo de cambio de su factura

Una nota de crédito `07` **con referencia interna** no calcula su tipo de cambio: **copia el
congelado de la factura referenciada**, no el de su propia fecha de emisión.

```
Nota de crédito sobre la factura F:

    TC aplicado = F.TipoCambio        (el congelado al confirmar F)
```

> **Por qué.** Sin esta regla, aplicando la general, una nota que anula el **100%** de una factura en
> dólares **no deja el pasivo en cero**. Deja
>
> ```
> residuo = totalOrig × (TCventa_NC − TCventa_factura)
> ```
>
> repartido entre `421212`/`431212`, la cuenta de cargo heredada y las cuentas de destino. Con tres
> milésimas de movimiento sobre USD 10 000 son **S/ 30 colgados en una cuenta por pagar, por
> proveedor, para siempre**.
>
> Y nada lo atrapa: el asiento de la nota **cuadra perfectamente consigo mismo**. El descuadre es
> entre dos asientos, y ninguna invariante mira ese par. Tampoco lo cubre §1, que deja la diferencia
> de cambio fuera de alcance porque *"nace al pagar o al cierre"*: este residuo **lo genera este
> sistema**, dentro del libro de compras, sin que intervenga ningún pago.
>
> Heredar el tipo de cambio es coherente con llamar **espejo** a la nota: ya hereda el motivo, la
> cuenta de cargo y las cuentas de destino. Es el cuarto atributo heredado, no una excepción.

**Consecuencias operativas:**

- La regla de rechazo por falta de tipo de cambio **no aplica al tipo `07`** con referencia interna:
  hereda uno que ya existe. Una nota emitida un día sin publicación de la SBS se registra sin
  problema.
- La nota con **referencia externa** no tiene de quién heredar y aplica la regla general.
- ⏳ Esta regla está **pendiente de ratificación** por un contador (§12).

### Tipos de dato

| Dato | Tipo |
|---|---|
| Importes | `DECIMAL(18,2)` |
| Tipo de cambio | `DECIMAL(12,6)` |

**Nunca `float` ni `real`** en una columna monetaria.

---

## 7. Invariantes de confirmación

Un asiento **no puede pasar a `CONFIRMADO`** si falla cualquiera de estas.

### Globales

1. `SUM(Debe) = SUM(Haber)` sobre el asiento completo, ambos bloques.
2. Ninguna línea sin cuenta contable asignada.
3. `FechaContable` no anterior a `Configuracion.FechaCorteContable`.
4. El proveedor no es `P0000 (Varios)`.
5. `Tipo = 'D'` exige `Debe > 0` y `Haber = 0`; `Tipo = 'H'`, lo contrario.

### Del bloque PRINCIPAL

| Comprobante | Cargos `6x`/`1x` igualan | Cargo a `401111` | Abono al proveedor |
|---|---|---|---|
| Factura gravada | base imponible | el IGV | total + percepción |
| Boleta o no gravada | **monto total** | no aplica | monto total |
| Nota de crédito **sobre factura gravada** | *(al Haber)* base | *(al Haber)* IGV | *(al Debe)* total |
| Nota de crédito **sobre boleta o no gravada** | *(al Haber)* **monto total** | **no aplica** | *(al Debe)* total |

La cuarta fila es de la revisión v2 y faltaba. Se elige por la **afectación congelada en el asiento
de la factura rectificada**, no por la del catálogo ni por la de la propia nota.

### Del bloque DESTINO

Para cada línea principal con **`CtaReflejaCodigo` no nulo**, existe su par reflejo/puente por el
mismo importe.

Se evalúa sobre el **dato congelado en la línea**, no contra el catálogo vivo. Enunciada sobre el
catálogo —*"cuya cuenta declare `ctarefleja`"*—, una cuenta que dejara de declararlo haría que la
comprobación **se satisficiera vacía**: un asiento reabierto y reconfirmado perdería su bloque de
destino sin que nada lo notara.

### De notas de crédito

La suma de las notas de crédito **vigentes** sobre una factura **no puede exceder su monto total**.
Se evalúa dentro de la transacción de confirmación.

**Vigente significa con asiento no anulado.** La anulación se aplica al asiento, no a la factura: una
nota anulada conserva `Estado = 'VALIDADA'`, de modo que filtrar solo por el estado de la factura
haría que **siguiera sumando** y la capacidad nunca se liberara.

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

Anular el asiento de una nota **libera** su importe y permite registrar otra que antes se rechazaba.
Las notas con **referencia externa** no entran en este cálculo: no hay factura contra la que topar.

**La moneda del tope no importa.** Factura y notas comparten el tipo de cambio congelado (§6), de
modo que comparar en soles o en moneda original da exactamente la misma proporción.

---

## 8. Reglas de rechazo

El sistema rechaza la validación con `409` en estos casos. Cada uno tiene salida:

| Situación | Salida |
|---|---|
| Duplicado `(RUC, tipo, número)` sin resolver | Corregir el número, o **descartar** la factura |
| Comprobante emitido en **domingo** | Corregir la fecha, o descartar |
| Factura en moneda extranjera sin tipo de cambio | Cargar el tipo de cambio |
| Proveedor `P0000 (Varios)` | Registrarlo en el sistema externo y seleccionarlo |
| `FechaContable` anterior a la fecha de corte | Ajustar la fecha contable |
| Nota de crédito con **referencia interna** sin factura válida | Resolver primero la factura original |
| Factura con líneas gravadas y no gravadas mezcladas | Fuera de alcance: registrar por otra vía |
| **Afectación no verificada** sin confirmar | Confirmar la afectación del comprobante |

### La factura mixta: cómo se detecta y hasta dónde llega la regla

`FacturaDetalle` no existe y `Afectacion` es **un solo campo de cabecera** con tres valores, así que
una factura mixta no tiene representación posible: el extractor elegiría uno de los tres, el
comprobante parecería homogéneo y **pasaría todas las reglas**. El modo de fallo va en la peor
dirección: una mixta registrada como `GRAVADA` **toma crédito fiscal sobre la porción que no lo
genera**.

La detección no necesita revivir el detalle. El extractor recorre las líneas del XML UBL y calcula:

```
AfectacionMixta

    true   → el XML declara más de un código de afectación  → rechazo (409)
    false  → el XML declara uno solo: afectación verificada
    NULL   → no hay XML: afectación NO verificada
```

**Cobertura declarada de la regla:**

| Comprobante | Cómo se comprueba |
|---|---|
| Con XML | **Automática**, y bloquea |
| Solo PDF | **Por confirmación del asistente**, que sí puede mirar el documento |

El `NULL` no bloquea: enciende un indicador y exige confirmación antes de validar. Sobre un PDF
escaneado la mezcla **no es detectable de forma fiable por ningún medio**, y dejarlo pasar en
silencio pondría el modo de fallo justo donde el OCR ya es menos fiable. La confirmación es una
afirmación del asistente sobre un documento fiscal, y queda en `AuditoriaCorreccion`.

### Regla de domingos

**Sin excepciones por tipo de comprobante.** Aplica a `01`, `03` **y** `07`.

Los **sábados se permiten**. Los feriados **no se controlan**.

> **Consecuencia conocida:** si un proveedor emite una nota de crédito en domingo, no se puede
> registrar. La factura original queda registrada y su rectificación bloqueada hasta que el
> proveedor reemita el documento.

### Precondiciones de una nota de crédito

Se aplican **solo a las notas con referencia interna**. Se rechaza si la factura referenciada no
existe, está en `PENDIENTE_VALIDACION`, está `DESCARTADA`, o **su asiento vigente** está `ANULADO`.

Una factura cuyo asiento se anuló y **se rehízo** vuelve a admitir notas de crédito: tiene un asiento
vigente otra vez. Es el comportamiento correcto — el documento fiscal nunca dejó de existir.

Las notas con **referencia externa** no tienen precondiciones: no hay factura que comprobar.

---

## 9. Numeración y ciclo de vida

### Dos números

| Campo | Ejemplo | Qué es |
|---|---|---|
| `NumeroComprobante` | `F001-00234` | Número fiscal del comprobante del proveedor |
| `NumeroAsiento` | `02-2026-08-000123` | Correlativo propio del libro, por periodo y origen |

`NumeroAsiento` se asigna **al confirmar**, nunca al crear el borrador, y se genera con una **tabla
contador** actualizada dentro de la misma transacción. `SEQUENCE` e `IDENTITY` no sirven: están
diseñadas para no bloquear, y por eso una transacción revertida **quema el número igual**.

El contador **se reinicia cada mes**, por fila `(año, mes, origen)`. No hay proceso de cierre que
reiniciar ni tarea programada que pueda no ejecutarse.

**Qué promete exactamente la secuencia:**

> No tiene huecos por **facturas abandonadas ni por validaciones fallidas**, que es el riesgo que
> motivó asignar el número al confirmar. **Sí puede tenerlos por traslado deliberado de un asiento a
> otro periodo**, que es un acto explícito del asistente, queda en `AuditoriaCorreccion` con su
> motivo y por tanto **es justificable en una revisión**.

Esa es la diferencia que sostiene la regla: un hueco por accidente no se puede explicar; uno por
traslado deliberado sí, y tiene su rastro. Un asiento trasladado devuelve su número y toma uno de la
serie del mes destino; **el devuelto no se reutiliza**, porque reasignarlo haría que dos documentos
distintos hubieran llevado el mismo número.

Trasladar a un periodo **cerrado** se rechaza, igual que reabrir en él.

### Ciclo de vida

```
BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO  (terminal)
    ▲                      │
    └──────reabrir─────────┘
```

**De `ANULADO` no sale ninguna flecha.** Un asiento confirmado es un hecho, y un hecho no se deshace:
se anula y se emite otro. Anular **libera la factura**, que vuelve a admitir un asiento en `BORRADOR`
con su propio correlativo. El anulado permanece en el libro con el suyo.

Una factura tiene, a lo largo del tiempo, **varios asientos posibles y a lo sumo uno vigente**.

| | Cuándo se usa | Qué pasa con el número |
|---|---|---|
| **Reabrir** | Corregir el asiento **dentro** de su periodo | Lo conserva |
| **Anular** | Sacarlo del libro; obliga a emitir uno nuevo | Lo conserva el anulado; el nuevo toma otro |

Sin la liberación de la factura, anular la dejaría **irrectificable para siempre**: sus notas de
crédito se rechazarían por precondición y no habría vuelta atrás.

La **reapertura** exige motivo, devuelve la factura a `PENDIENTE_VALIDACION` y queda auditada. Un
asiento cuya fecha contable sea anterior a la **fecha de corte no se puede reabrir**. El tope de
reapertura se aplica **solo a reabrir**, no a anular.

Al reconfirmar se emite un evento que **corrige Drive y Google Sheets**.

---

## 10. Ejemplos completos

### 10.1 · Factura gravada en soles, con destino

```
Factura F001-00234 · Ferretería San Martín · PEN
Base 1,000.00 · IGV 180.00 · Total 1,180.00
Motivo 22 Fletes traslado de mercadería → 631111
Proveedor: tercero

BLOQUE PRINCIPAL
  631111  FLETE TRASLADO DE MERCADERIA        Debe    1000.00
  401111  IGV – CUENTA PROPIA                 Debe     180.00
  421211  FACTURAS Y BOLETAS EN SOLES         Haber   1180.00

BLOQUE DESTINO   (631111 → ctarefleja 946311, ctapuente 791111)
  946311  (destino del flete)                 Debe    1000.00
  791111  CARGAS IMPUTABLES A CTA DE COSTOS   Haber   1000.00

Cuadre global: Debe 2,180.00 = Haber 2,180.00   ✓
Cargos 6x del bloque principal: 1,000.00 = base  ✓
Cargo a 401111: 180.00 = IGV                     ✓
Abono al proveedor: 1,180.00 = total             ✓
```

### 10.2 · Boleta: el IGV va al costo

```
Boleta B004-00456 · PEN · Total 1,180.00
Motivo 19 Útiles de escritorio → 656111
Proveedor: tercero

BLOQUE PRINCIPAL
  656111  UTILES DE ESCRITORIO                Debe    1180.00
  421211  FACTURAS Y BOLETAS EN SOLES         Haber   1180.00

BLOQUE DESTINO   (si 656111 declara ctarefleja)
  94xxxx  (destino)                           Debe    1180.00
  791111  CARGAS IMPUTABLES                   Haber   1180.00

SIN línea de 401111: la boleta no otorga crédito fiscal.
Cargos del bloque principal: 1,180.00 = TOTAL (no base)  ✓
```

### 10.3 · Factura en dólares, con redondeo derivado

```
Factura F045-01210 · USD · TC venta 3.7895
Base 1,000.00 · IGV 180.00 · Total 1,180.00 (USD)
Proveedor: parte relacionada

CONVERSIÓN
  totalPEN = redondear(1180.00 × 3.7895, 2) = 4471.61   ← anclado
  igvPEN   = redondear( 180.00 × 3.7895, 2) =  682.11   ← anclado
  basePEN  = 4471.61 − 682.11               = 3789.50   ← DERIVADO

BLOQUE PRINCIPAL
  6xxxxx  cuenta del motivo                   Debe    3789.50
  401111  IGV – CUENTA PROPIA                 Debe     682.11
  431212  FACTURAS Y BOLETAS RELAC. EN DOLARES Haber   4471.61

Proveedor RELACIONADO + DÓLARES → 431212, no 421212.
base + IGV = total, exacto por construcción.  ✓
```

### 10.4 · Factura con percepción

```
Factura F007-00312 · PEN
Base 1,000.00 · IGV 180.00 · Total 1,180.00 · Percepción 23.60
Proveedor: tercero

BLOQUE PRINCIPAL
  6xxxxx  cuenta del motivo                   Debe    1000.00
  401111  IGV – CUENTA PROPIA                 Debe     180.00
  401131  IGV – RÉGIMEN DE PERCEPCIONES       Debe      23.60
  421211  FACTURAS Y BOLETAS EN SOLES         Haber   1203.60

El abono al proveedor iguala TOTAL + PERCEPCIÓN.
La percepción NO genera bloque de destino: 401131 no declara ctarefleja.
```

### 10.5 · Nota de crédito

```
Nota de crédito NC01-00089 sobre F001-00234
Base 200.00 · IGV 36.00 · Total 236.00 · PEN
Motivo y cuenta HEREDADOS de F001-00234 → 631111

BLOQUE PRINCIPAL   (signos invertidos)
  421211  FACTURAS Y BOLETAS EN SOLES         Debe     236.00
  631111  FLETE TRASLADO DE MERCADERIA        Haber    200.00
  401111  IGV – CUENTA PROPIA                 Haber     36.00

BLOQUE DESTINO     (también invertido)
  946311  (destino del flete)                 Haber    200.00
  791111  CARGAS IMPUTABLES                   Debe     200.00

Control acumulado:
  notas vigentes sobre F001-00234 ≤ 1,180.00   ✓ (236.00)
```

### 10.6 · Nota de crédito sobre boleta — dos líneas, sin IGV

```
Nota de crédito NC01-00090 sobre B001-00512
Total 118.00 · PEN
Afectación de la boleta rectificada: el IGV fue al costo
Motivo y cuenta HEREDADOS de B001-00512 → 656101

BLOQUE PRINCIPAL   (signos invertidos)
  421211  FACTURAS Y BOLETAS EN SOLES         Debe     118.00
  656101  SUMINISTROS                         Haber    118.00

  ← NO hay línea 401111. Revertirla devolvería un crédito
    fiscal que la boleta nunca otorgó.

BLOQUE DESTINO     (también invertido)
  946561  (destino de suministros)            Haber    118.00
  791111  CARGAS IMPUTABLES                   Debe     118.00

Invariante aplicada: la del tipo 07 SOBRE BOLETA
  cargos al Haber = monto total    ✓ 118.00 = 118.00
  401111                            no aplica
  proveedor al Debe = total         ✓ 118.00
```

### 10.7 · Nota de crédito del 100% en dólares — el pasivo queda en cero exacto

```
Factura F001-00301   USD 10,000.00 · IGV USD 1,525.42 · Total USD 11,525.42
  Emitida el 12/08/2026 · TC venta 3.712000  ← CONGELADO en el asiento

  totalPEN = redondear(11,525.42 × 3.712000, 2) = 42,782.36
  igvPEN   = redondear( 1,525.42 × 3.712000, 2) =  5,662.36
  basePEN  = 42,782.36 − 5,662.36               = 37,120.00

  631111  FLETE TRASLADO DE MERCADERIA        Debe   37,120.00
  401111  IGV – CUENTA PROPIA                 Debe    5,662.36
  421212  FACTURAS Y BOLETAS EN DOLARES       Haber  42,782.36


Nota de crédito NC01-00091 sobre F001-00301, por el 100%
  Emitida el 03/09/2026 · TC venta de ESA fecha: 3.715000
  TC APLICADO: 3.712000   ← HEREDADO de F001-00301, no el de su fecha

  421212  FACTURAS Y BOLETAS EN DOLARES       Debe   42,782.36
  631111  FLETE TRASLADO DE MERCADERIA        Haber  37,120.00
  401111  IGV – CUENTA PROPIA                 Haber   5,662.36

Saldo de 421212 para este proveedor:  42,782.36 − 42,782.36 = 0.00  ✓
```

> **Qué habría pasado con el tipo de cambio propio (3.715000).** El total de la nota sería
> `redondear(11,525.42 × 3.715000, 2) = 42,816.94`, y el saldo de `421212` quedaría en **−34.58**
> soles: un pasivo negativo contra un proveedor al que ya no se le debe nada, colgado para siempre.
> El asiento de la nota **cuadraría igual**, porque el descuadre es entre dos asientos y ninguna
> invariante mira ese par.

---

## 11. Reglas que hay que revisar

| ⏳ | Regla | Cuándo revisarla |
|---|---|---|
| 1 | **22 motivos de caja chica reclasificados a `02`** | **Antes de producción.** Es una alteración hecha para la demostración. Marcados con `†`. |
| 2 | IGV siempre a `401111` | Si aparecen ventas no gravadas y hay que prorratear el crédito fiscal |
| 3 | Facturas mixtas fuera de alcance | Si empiezan a llegar comprobantes con líneas gravadas y no gravadas |
| 4 | Feriados no controlados | Si la política de la compañía se extiende más allá de los domingos |
| 5 | Detracción, retención y diferencia de cambio fuera de alcance | Si el sistema llegara a modelar pagos o cierres |
| 6 | **Reparto proporcional** de la nota de crédito parcial | Si las devoluciones parciales empiezan a corresponder sistemáticamente a una línea concreta |
| 7 | **Nota con referencia externa sin tope** | Cuando dejen de llegar notas contra facturas anteriores al sistema |

---

## 12. Validación pendiente

Este documento recoge decisiones tomadas por el responsable del proyecto sobre la base de los datos
maestros reales de la compañía. Son **seis** puntos, no cuatro: la revisión adversarial v2 añadió los
dos últimos, que son del mismo capítulo.

> ## ⚠ Estas seis reglas NO están ratificadas por un contador
>
> El proyecto es una **demostración académica** y no habrá revisión contable formal. Se asume el
> riesgo de forma deliberada, y por eso queda escrito aquí y no en una nota al pie.
>
> **Las seis son decisiones de diseño con fundamento, no criterios normativos verificados.** Están
> razonadas —cada una tiene su porqué en este documento y en su ADR— y son internamente coherentes:
> el sistema construido sobre ellas cuadra, es explicable y produce asientos consistentes. Lo que
> **no** está comprobado es que coincidan con lo que exige la norma tributaria peruana ni con el
> criterio del contador de la compañía.
>
> **Este sistema no debe operar con contabilidad real sin esa revisión.** No porque probablemente
> estén mal, sino porque el costo de equivocarse no es simétrico: los puntos 1 y 5 afectan a **todo
> asiento en moneda extranjera ya confirmado**, y corregirlos después significa reprocesar el libro,
> no cambiar una línea de código.
>
> El punto 5 es el más discutible de los seis y el que más conviene señalar el día que alguien lo
> revise.

| # | Punto | Dónde | Qué pasa si el criterio correcto es otro |
|---|---|---|---|
| 1 | Tipo de cambio **venta** para pasivos | §6, ADR 0018 | **Reprocesar todo asiento en moneda extranjera ya confirmado** |
| 2 | La **cuenta de cargo** absorbe la diferencia de redondeo, sin línea de ajuste | §6 | Aparece una línea de ajuste y su cuenta |
| 3 | La **boleta** incorpora el IGV al costo | §5 | Cambia la estructura de todo asiento de boleta |
| 4 | **Precondiciones de la nota de crédito**, en especial exigir que la factura original esté validada | §8 | Se admiten notas que hoy se rechazan con `409` |
| 5 | **La nota de crédito hereda el tipo de cambio de su factura** | §6 | La diferencia de cambio **deja de estar fuera de alcance** y hace falta la línea de ajuste que §1 declara inexistente |
| 6 | **Estructura de la nota sobre boleta**: dos líneas, sin `401111` | §5, §7 | Cambia la invariante de confirmación del tipo `07` |

Los puntos 1 y 2 son además **reversiones al PRD**, que pedía por escrito lo contrario. Constan en la
tabla de reversiones del propio PRD.

**Sobre el punto 5, el más discutible.** La regla general —y probablemente la norma, si la nota se
declara como comprobante propio— diría que use el tipo de cambio de **su propia fecha**. Se eligió
heredarlo por dos razones: la alternativa deja un **residuo cambiario permanente** en la cuenta por
pagar que ningún control detecta (el ejemplo 10.7 lo muestra con números), y arrastra consigo la
línea de ajuste por diferencia de cambio, es decir, reabre un alcance que §1 cerró deliberadamente.

Si el criterio correcto fuera el contrario, **hay que aceptar las dos cosas a la vez**: el residuo
deja de ser un defecto y pasa a ser diferencia de cambio legítima, y el sistema necesita la línea de
ajuste y su cuenta. No se puede adoptar el tipo de cambio propio y seguir declarando la diferencia de
cambio fuera de alcance.
