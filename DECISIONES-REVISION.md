# Decisiones de resolución — Revisión adversarial

Registro incremental de las decisiones tomadas para cerrar los hallazgos de
`REVISION-ADVERSARIAL.md`. Este archivo es el insumo para reescribir `TECH-DESIGN.md`
y los ADRs de `adrs/` (la copia original queda en `adrs - v1/`).

**Estado:** en curso.

---

## C1 — Origen de las líneas del asiento · CERRADO

### Decisión

El insumo del asiento **no es el producto, es el motivo de compra**. Se eliminan del modelo
`FacturaDetalle`, `Producto` y el mapeo producto→cuenta. Entran `Motivo`, `MotivoCuenta` y
`SugerenciaCuenta`.

### Flujo

1. Al validar la factura, el asistente selecciona un **motivo** de compra (obligatorio: sin
   motivo no hay cuenta de cargo y la validación se rechaza).
2. Cada motivo está enlazado a **una o más cuentas contables candidatas** de 6 dígitos,
   tomadas del plan contable personalizado de la compañía (clases `6x` y `1x`).
3. El sistema **sugiere** la cuenta candidata más probable. El asistente la confirma o la
   cambia por otra del mismo motivo.
4. Por defecto el asiento se compone de **tres líneas**: cargo (`6x`/`1x`) + IGV + proveedor.
5. El asistente puede **dividir el cargo** en varias cuentas del motivo antes de confirmar.
   Invariante: la suma de los cargos `6x`/`1x` debe igualar la base imponible.

### Cuentas fijas (no dependen del motivo)

| Concepto | Cuenta | Criterio de selección |
|---|---|---|
| IGV | `401111` IGV cuenta propia | Fija, bajo `4011x` Impuesto general a las ventas |
| Proveedor, soles | `421211` Facturas y boletas en soles | Moneda de la factura = PEN |
| Proveedor, dólares | `421212` Facturas y boletas emitidas en dólares | Moneda de la factura = USD |

La cuenta de proveedor se resuelve de forma **determinista por la moneda del comprobante**.
No requiere sugerencia ni aprendizaje.

### Corrección al prototipo

El prototipo (`handoff/Gestor de Facturas.dc.html`, `defaultLineas()` línea 1666) usa cuentas de
**5 dígitos** (`60111`, `40111`, `42011`). El plan contable real de la compañía es de
**6 dígitos**. Las cuentas del prototipo son ilustrativas y no deben tomarse como referencia.

### Ejemplo — factura de S/ 1,180.00 (IGV 18%)

```
Motivo: "Compra de mercadería"  →  candidatas: 601110, 601120, 601130
Sugerencia: 601110 (usada 14 de 15 veces con este proveedor)

  601110   Compras - Mercaderías          Debe   1000.00
  401111   IGV - Cuenta propia            Debe    180.00
  421211   Facturas y boletas en soles    Haber  1180.00
```

---

## C1b — Mecanismo de sugerencia (aprendizaje) · CERRADO

### Decisión

**Frecuencia histórica determinista.** Sin modelo de IA, sin servicio externo, sin dependencia
de red. La sugerencia es explicable ante un contador y auditable.

### Modelo

```sql
CREATE TABLE SugerenciaCuenta (
    ProveedorId   BIGINT      NOT NULL REFERENCES Proveedor(Id),
    MotivoId      BIGINT      NOT NULL REFERENCES Motivo(Id),
    CuentaCodigo  VARCHAR(10) NOT NULL REFERENCES CuentaContable(Codigo),
    Veces         INT         NOT NULL DEFAULT 0,
    UltimoUso     DATETIME2   NOT NULL,
    PRIMARY KEY (ProveedorId, MotivoId, CuentaCodigo)
);
```

El contador `Veces` se incrementa **al confirmar el asiento**, no al sugerir.

### Cascada de resolución

1. Cuenta más usada para el par `(proveedor, motivo)`.
2. Si no hay historial: cuenta más usada para el `motivo` a nivel global.
3. Si tampoco: cuenta marcada como **predeterminada** del motivo (`MotivoCuenta.EsPredeterminada`).

El mismo mecanismo, keyed **solo por proveedor**, sugiere el **motivo** de la factura.

### La sugerencia nunca decide sola

El asistente siempre confirma o corrige. La interfaz muestra el fundamento de la sugerencia
—por ejemplo *"usado 14 de 15 veces con este proveedor"*— para que la decisión sea informada
y no un autocompletado ciego.

### Costo declarado

No generaliza a proveedores nuevos: la primera factura de un proveedor cae siempre al nivel 2
o 3 de la cascada. Es un costo aceptado a cambio de que la sugerencia sea explicable.

---

---

## C3 — Ciclo de vida del asiento · CERRADO

### Estados

```
BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO
```

El estado `GENERADO` que figuraba en el TDD y en ADR 0006 se renombra a `CONFIRMADO`, y se
agrega `BORRADOR` como estado inicial.

### Creación diferida

El asiento se genera en estado `BORRADOR` **al abrir la factura para su registro o validación**,
si todavía no existe. No se genera en la promoción ni al validar.

La pantalla de detalle es el **espacio de trabajo único**: el asistente contable revisa la
factura y el asiento en el mismo lugar, no en dos pantallas separadas.

### Acciones

| Acción | Efecto |
|---|---|
| **Guardar avance** | Persiste las modificaciones de la factura **y** del asiento con sus líneas. Ambos permanecen editables. |
| **Validar** | Ejecuta las validaciones finales y, en **una única transacción**, pasa la factura a `VALIDADA` y el asiento a `CONFIRMADO`. |

Después de confirmar, **ni la factura ni el asiento pueden editarse por el flujo normal**.

Mientras el asiento permanezca en `BORRADOR`, sus líneas pueden agregarse, modificarse y
eliminarse libremente.

### Modelo de la línea

| Campo | Propósito |
|---|---|
| `LineaId` | Identificador **estable** de la línea. |
| `Orden` | Posición de presentación. No identifica. |
| `Tipo` | `D` o `H`. Representa la naturaleza contable de la línea. |
| `Debe` | Importe al debe. |
| `Haber` | Importe al haber. |

### Regla de coherencia de la línea

```
Tipo = 'D'  →  Debe > 0  AND  Haber = 0
Tipo = 'H'  →  Haber > 0  AND  Debe = 0
```

Se materializa como restricción `CHECK` en el motor, no solo como validación de aplicación.

### Invariantes de confirmación

El asiento no puede pasar a `CONFIRMADO` si falla cualquiera de estas comprobaciones:

1. **Cuadre.** `SUM(Debe) = SUM(Haber)`.
2. **Base imponible.** La suma de los cargos a cuentas `6x`/`1x` iguala la base imponible de la factura.
3. **IGV.** El cargo a `401111` iguala el IGV de la factura.
4. **Monto total.** El abono a la cuenta de proveedor (`421211`/`421212`) iguala el monto total de la factura.
5. **Cuentas asignadas.** Ninguna línea queda sin cuenta contable.

### Efecto sobre el hallazgo original

El identificador inestable que C3 señalaba —`POST /asientos/{id}/lineas/{numero}/cuenta`, que
se corría al agregar o eliminar líneas— queda resuelto por `LineaId`. El contrato pasa a
operar sobre el identificador estable, no sobre la posición.

---

## Observaciones abiertas que se derivan de C3

### O1 — Corrección posterior a la confirmación · CERRADO

**Decisión: reapertura auditada.** Un asiento `CONFIRMADO` vuelve a `BORRADOR` mediante una
acción explícita, se corrige con las mismas reglas de edición de siempre y se vuelve a validar.

```
CONFIRMADO ──reabrir──▶ BORRADOR ──validar──▶ CONFIRMADO
```

```http
POST /asientos/{id}/reabrir
{ "motivo": "Corrección de proveedor" }
```

Efectos, en una única transacción:

- El asiento pasa a `BORRADOR`.
- La factura vuelve a `PENDIENTE_VALIDACION`.
- Se registra la reapertura en `AuditoriaCorreccion`, con el motivo.

Al volver a confirmar se emite un **nuevo evento de outbox**, que corrige la carpeta de Drive y
la fila de Google Sheets. Esto cierra parcialmente C5.

Se mantiene la relación **1:1 entre factura y asiento** que establece ADR 0006.

**Costo declarado y pendiente de acotar:** un asiento contable ya cerrado vuelve a ser editable.
Falta la regla de **hasta cuándo** se puede reabrir, que depende del concepto de cierre de
periodo — hoy inexistente en el sistema (pregunta D1.10, la responde un contador).

### O2 — Crear el borrador «al abrir la factura» es una escritura disparada por una lectura

Si el asiento se crea al abrir la pantalla de detalle, un `GET` termina insertando filas. Es
idempotente —solo crea si no existe— pero rompe la semántica de HTTP: un *prefetch* del
navegador, un reintento o una precarga generan asientos. **Recomendación:** que la creación
sea una acción explícita del cliente al entrar a la pantalla, no un efecto lateral de la
consulta. Se refleja en el contrato de ADR 0008.

---

## C2 — Detección y resolución de duplicados · CERRADO

### Decisión

`(RUC del proveedor, tipo de comprobante, número)` es la **identidad del comprobante**. No puede
repetirse, y la garantía vive en el motor, no en una bandera calculada.

```sql
CREATE UNIQUE INDEX UQ_Factura_Identidad
    ON Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';
```

Esto implementa la sugerencia S2 y convierte el duplicado en una **invariante real**, no en una
advertencia de aplicación.

### Recálculo

La bandera de duplicado se recalcula **al guardar avance y al validar**, no solo en la
promoción. Es la corrección del segundo agujero de C2: el número lo puso el OCR y el usuario lo
edita, de modo que una bandera calculada una sola vez es inservible.

### Dos salidas reales

| Situación | Salida |
|---|---|
| El OCR leyó mal el número | El asistente lo corrige. La bandera desaparece sola en el siguiente recálculo. |
| Es efectivamente la misma factura repetida | `POST /facturas/{id}/descartar` |

El checkbox **"Revisé el duplicado"** del prototipo se elimina. Era estado local
(`dupAck: false`, línea 1205), nunca se persistía, y constituía exactamente la validación de
cliente que el criterio de aceptación prohíbe.

### Corrección al dato de ejemplo del prototipo

El prototipo marca la factura 5 como `duplicado:true` (RUC `20489912345`, número `F010-00567`)
frente a la factura 4 (RUC `20601122334`, mismo número). **Son proveedores distintos**, y cada
proveedor mantiene su propia numeración. Según la regla que el propio prototipo enuncia en
pantalla —*"mismo RUC, tipo de comprobante y número"*— ese caso **no es un duplicado**. El dato
de ejemplo debe corregirse para no inducir a error a quien implemente.

---

## C4 — Falsos positivos y descarte · PARCIALMENTE CERRADO

### Resuelto: existe el descarte

`Factura` gana el estado `DESCARTADA`, alcanzable por `POST /facturas/{id}/descartar` con
motivo obligatorio. Cubre los dos casos que hoy dejaban una factura atrapada en la bandeja para
siempre:

- El correo no correspondía a una factura de compra real (falso positivo de detección, caso
  borde declarado por el PRD).
- La factura ya estaba registrada (salida 2 de C2).

Una factura `DESCARTADA` sale de la bandeja activa, queda fuera del índice único de identidad y
conserva su rastro de auditoría. **No se borra.**

### Resuelto: el criterio de candidatura

Un correo es **candidato a procesamiento** cuando cumple las dos condiciones, y solo esas:

1. Pertenece a una **etiqueta o carpeta de Gmail** configurada como origen de facturas.
2. Contiene **al menos un adjunto con extensión permitida** (PDF o XML).

El asunto y el remitente **no intervienen**. La etiqueta monitoreada y las extensiones
permitidas son **configurables**, nunca fijas en el código del worker.

Los correos que no cumplen la regla **no se envían al procesamiento documental**. No consumen
OCR ni generan filas de trabajo.

> Candidatura **no** equivale a ser una factura. La regla solo selecciona qué se procesa.
> Determinar el tipo de comprobante es responsabilidad de la etapa de procesamiento en Python.

### Identidad del adjunto

Cada adjunto se identifica **individualmente**, y de cada uno se almacena:

| Campo | Propósito |
|---|---|
| `GmailMessageId` | Correo de origen |
| `NombreArchivo` | Trazabilidad |
| `Extension` | Enrutamiento a XML o a OCR |
| `MimeType` | Verificación del tipo real |
| `HashContenido` | **Idempotencia**: evita reprocesar un documento ya registrado |

### Fuente de extracción: XML prioritario

| Adjuntos del comprobante | Fuente de datos | Evidencia |
|---|---|---|
| XML + PDF | **XML** (fuente estructurada prioritaria) | PDF |
| Solo PDF | OCR / extracción documental | PDF |
| Solo XML | XML | XML |

Cuando un correo trae PDF y XML del **mismo comprobante**, ambos se asocian al mismo
documento y a la misma factura.

### Regla de asociación PDF ↔ XML

**Clave de asociación:** el identificador tributario compuesto, normalizado.

```
RUC del emisor + tipo de comprobante + serie + número
```

**Procedimiento**

1. El worker procesa **primero todos los XML** del correo y extrae de cada uno su identificador
   tributario. El XML es la autoridad.
2. Después procesa **cada PDF** para obtener los mismos cuatro datos, mediante extracción de
   texto y, cuando haga falta, OCR.
3. El PDF se asocia al XML **únicamente si los cuatro componentes normalizados coinciden de
   forma exacta**.

**Mecanismo de recuperación.** Si no es posible extraer los datos del contenido del PDF, el
nombre del archivo puede usarse como respaldo, siempre que la coincidencia sea **inequívoca**.

**Evidencia insuficiente, explícitamente.** El asunto del correo, el remitente, la fecha y la
posición del archivo entre los adjuntos **no establecen asociación** en ningún caso.

**Sin coincidencia inequívoca.** El PDF permanece **sin asociar** y se registra un evento para
revisión. Nunca se asigna a un comprobante por proximidad o descarte.

> **Costo aceptado.** El PDF pasa por extracción incluso cuando el XML ya aportaba el dato
> exacto. Se gasta presupuesto de OCR a cambio de que la asociación sea verificada y no
> inferida. Es una decisión consciente, no un descuido.

**Superficie pendiente:** el "evento registrado para revisión" necesita un lugar donde vivir y
una pantalla donde verse. Se resuelve junto con C7 y el panel de errores.

### Efecto sobre el prototipo

Los filtros `filtroRemitentes`, `filtroAsunto` y `filtroPalabras` de la pantalla de
Configuración (líneas 971-980 y 1258) **se eliminan**: la regla acordada no los usa. Sobreviven
la carpeta o etiqueta monitoreada y las extensiones permitidas (`filtroExts`).

Además, `'Bandeja de entrada'` sale de las carpetas monitoreadas por defecto (línea 1260).
Monitorear la bandeja completa contradice el criterio de candidatura por etiqueta.

---

## C7 — Estado ERROR e incidencias de procesamiento · CERRADO

### Principio

Los errores de **ingesta, asociación y extracción documental** ocurren **antes de que exista
una entidad `Factura`**. No se promueven artificialmente a `Factura.Estado = ERROR`.

`Factura` sigue siendo propiedad del dominio .NET y **solo se crea cuando el procesamiento
documental produjo datos suficientes para representar una factura**. ADR 0003 queda intacta.

### Dónde vive el error

Python registra el estado y el detalle del fallo en las entidades de ingesta y procesamiento
—`DocumentoRecibido` y `Procesamiento`—, que ya son de su propiedad.

Errores cubiertos:

| Error | Etapa |
|---|---|
| Adjunto corrupto | Ingesta |
| XML inválido | Extracción |
| OCR fallido | Extracción |
| PDF no asociado | Asociación |
| PDF ambiguo | Asociación |
| Tipo de comprobante no válido | Extracción |

### La bandeja es una vista lógica de .NET

La bandeja combina **facturas pendientes de validación** con **documentos y procesamientos que
requieren atención**. Esa combinación la hace **.NET**, y la expone como una sola vista.

> Angular **no** accede a las tablas ni es responsable de combinar las fuentes. El frontend
> consume una vista ya resuelta.

### Contrato mínimo del elemento de error

Cada incidencia expone al menos:

- Tipo de error
- Mensaje
- Fecha
- Número de intentos
- **Acciones disponibles** (`REPROCESAR`, `REVISAR`, …)

### Reproceso

Los errores recuperables pueden ser **reprocesados por Python sin crear duplicados**. La
idempotencia se apoya en el `HashContenido` del adjunto definido en C4b.

### Efecto sobre A1

`REPROCESAR` es el camino de recuperación cuya ausencia denunciaba A1 (*"sin camino de
recuperación tras agotar los reintentos"*). Con una acción explícita por incidencia en el panel
de errores, el criterio del PRD deja de ser inalcanzable por construcción y ya no hace falta
intervenir la base de datos a mano.

---

## Canal .NET → Python · CERRADO

### Decisión: dos tablas de frontera, dos semánticas

| Tabla | Semántica | Origen |
|---|---|---|
| `OutboxEvent` | **"esto ocurrió"** — eventos de negocio ya consumados | Escrito dentro de la transacción del hecho de negocio |
| `CommandQueue` | **"haz esto"** — solicitudes de ejecución | Acción del usuario o de un proceso interno |

`IntegrationOutbox` se renombra a `OutboxEvent` para que el nombre refleje el alcance real.

### Comandos

`REPROCESAR_DOCUMENTO` · `SINCRONIZAR_GMAIL` · `SINCRONIZAR_SBS`

### Flujo de un comando

1. Angular invoca la API .NET.
2. .NET **valida la solicitud** y registra el comando en `CommandQueue` con referencia,
   *payload*, estado, intentos y `CorrelationId`.
3. Python consume los comandos pendientes, ejecuta la operación y actualiza su estado.

### Garantía de entrega

Los comandos tienen semántica de **entrega al menos una vez**. Las operaciones **deben ser
idempotentes**. Se registran intentos, errores y **próxima fecha de reintento**.

### Retroalimentación

Los resultados relevantes de la ejecución de un comando **pueden generar eventos en
`OutboxEvent`**, manteniendo la separación entre comandos y eventos.

```
Usuario ──▶ .NET ──▶ CommandQueue ──▶ Python ──▶ ejecuta
                                         │
                                         └──▶ OutboxEvent ──▶ …
```

### Frontera de escritura, precisada

Python **no escribe las tablas internas de .NET**. La comunicación entre ambos componentes se
realiza mediante **contratos de integración definidos en SQL Server**: `OutboxEvent` y
`CommandQueue` son contratos, no tablas internas, y su coescritura es deliberada.

Esto corrige la redacción que A4 señalaba como incorrecta en la tabla de propiedad de datos del
TDD, donde `IntegrationOutbox` figuraba como *"escribe .NET, consume Python"* cuando consumir
un outbox exige `UPDATE`.

### Efecto sobre C6

`SINCRONIZAR_SBS` aporta el mecanismo de **re-resolución del tipo de cambio** cuya ausencia
señalaba C6: hoy nada reintenta la consulta cuando la SBS publica. Falta todavía la política
contable de qué tipo de cambio aplicar.

---

## C5 — Alcance de eventos del outbox · CERRADO

### Principio

`OutboxEvent` representa **hechos de negocio ocurridos**. No transporta comandos ni estados a
sincronizar. Se genera un **evento específico por cada operación de negocio relevante** que deba
propagarse a integraciones externas.

**Se descarta explícitamente** el evento genérico de reconciliación de estado. Cada evento
define qué información debe actualizarse.

### Garantías

- La **modificación del dominio y la creación del evento ocurren en la misma transacción** de
  SQL Server.
- Los eventos son **inmutables**. Cada operación genera un registro nuevo; ninguno se edita.
- Semántica de **entrega al menos una vez**. El consumidor implementa idempotencia.

### Catálogo inicial

| Evento | Dispara |
|---|---|
| `FACTURA_VALIDADA` | Validación de la factura y confirmación del asiento |
| `FACTURA_CORREGIDA` | Corrección de datos de la factura |
| `ASIENTO_CORREGIDO` | Corrección del asiento tras reapertura (O1) |
| `ASIENTO_ANULADO` | Anulación del asiento |
| `FACTURA_ANULADA` | Solo si la anulación de factura forma parte del dominio |

> Según lo decidido en C2 y C4, `Factura` tiene los estados `PENDIENTE_VALIDACION`, `VALIDADA` y
> `DESCARTADA`, y la anulación aplica al **asiento**, no a la factura. Una factura `DESCARTADA`
> nunca llegó a validarse, así que no existe nada que corregir en Drive ni en Sheets y **no
> requiere evento**. `FACTURA_ANULADA` queda fuera del catálogo salvo que se incorpore la
> anulación de factura como operación del dominio.

### Regla derivada: orden de consumo por factura

Con eventos granulares y entrega al menos una vez, **el orden de aplicación importa**.
`ASIENTO_ANULADO` y `ASIENTO_CORREGIDO` aplicados fuera de orden dejan la fila de Google Sheets
con el dato equivocado de forma permanente.

**Requisito del consumidor:** los eventos de una misma factura se procesan **serializados y en
orden de creación**. Eventos de facturas distintas pueden procesarse en paralelo.

```sql
-- El lote de trabajo se toma ordenado, y nunca se reclaman
-- dos eventos de la misma factura en paralelo.
ORDER BY FacturaId, Id
```

### Consecuencia para el dashboard

Google Sheets es la fuente que alimenta Looker Studio. Toda operación que afecte los datos
mostrados en el dashboard **debe** generar su evento correspondiente. Esto cierra el hallazgo
original de C5: un asiento anulado dejaba de reflejarse y seguía contando como gasto de forma
permanente.

### Efecto sobre A4

La idempotencia **se construye en cada integración** —buscar antes de crear en Drive, *upsert*
por clave en Sheets—, no se hereda del outbox. La afirmación de ADR 0004 según la cual *"el
estado independiente por integración hace que los reintentos sean seguros de repetir"* se
corrige: no cubre la ventana entre que la API externa responde con éxito y que se persiste el
estado.

---

## C6 — Tipo de cambio · CERRADO (con matices)

### Corrección 1: es el tipo de cambio VENTA

El TECH-DESIGN dice que los importes se expresan en soles *"usando el tipo de cambio **compra**
de la fecha de emisión"*. **Es incorrecto.** Se usa el tipo de cambio **venta**.

Una factura de compra genera un **pasivo** en moneda extranjera (`421211`/`421212`), y los
pasivos se convierten al tipo de cambio venta. El error afectaba a **todas** las facturas en
moneda extranjera, no solo a las de fin de semana.

Hay que corregirlo en el TDD (Flujo 4 y modelo de datos) y en ADR 0006.

### Corrección 2: el hallazgo original de C6 estaba mal planteado

La revisión adversarial afirma que *"la SBS publica tipo de cambio únicamente en días hábiles"*
y que por tanto una factura emitida un sábado **nunca** tendrá tipo de cambio.

**No es así.** La SBS publica **por las noches**, y lo publicado el viernes por la noche se
asigna a **sábado, domingo y lunes**. La tabla `TipoCambio` sí contiene filas para los días no
hábiles. El escenario permanente que describía C6 no se produce en la operación normal.

### El hueco real: días sin publicación

En algunos casos la SBS **no publica**. Ahí sí falta la fila, y la regla es:

1. Primero se **carga el tipo de cambio en la tabla** `TipoCambio`.
2. Recién entonces puede **empezar la edición de las facturas** de esa fecha.

Es un bloqueo **anterior a la edición**, no solo a la validación. Como el asiento se crea en
`BORRADOR` al abrir la factura (C3), la comprobación se sitúa en ese punto: sin tipo de cambio
no se abre la factura para trabajar.

> **Supuesto declarado:** el bloqueo aplica únicamente a facturas en **moneda extranjera**. Las
> facturas en soles no dependen de `TipoCambio` y se editan con normalidad. Si tu intención era
> bloquear todo, dímelo.

### Desaparece el 0.00

La regla *"si no hay fila, tipo de cambio 0.00 con observación"* se elimina del diseño. Producía
asientos aritméticamente consistentes y contablemente basura: base, IGV y neto en cero, con el
control de cuadre pasando porque `0 = 0`.

En su lugar: **o hay tipo de cambio válido, o la factura no se abre para edición.**

### Congelamiento

El tipo de cambio se congela **al confirmar el asiento**, no en la promoción.

### Carga manual del tipo de cambio

**Decisión:** .NET escribe las filas cargadas a mano. La invariante de `TipoCambio` pasa de
*"un solo escritor"* a **"un escritor por origen"**.

```sql
ALTER TABLE TipoCambio ADD
    Origen     VARCHAR(10) NOT NULL,  -- 'SBS' | 'MANUAL'
    CargadoPor BIGINT      NULL REFERENCES Usuario(Id),
    CargadoEn  DATETIME2   NOT NULL;
```

```http
POST /tipos-cambio
{ "fecha": "2026-08-11", "compra": 3.512, "venta": 3.519 }
```

.NET valida y escribe con `Origen = 'MANUAL'`. La carga es **inmediata**: el asistente sigue
trabajando al instante, que es lo que exige una operación que bloquea el trabajo.

**Regla de convivencia.** Si la SBS publica después para esa misma fecha, Python **no pisa una
fila `MANUAL` en silencio**: registra la discrepancia para revisión.

**Costo declarado.** ADR 0003 deja de poder describirse como partición estricta por tabla. Hay
que reescribirla como partición por tabla **con orígenes de escritura declarados**.

### Política de la empresa: domingos

**No se aceptan documentos emitidos los domingos.** Es una regla de negocio de la compañía que
el PRD no recogía y que el TDD desconocía por completo.

**Pendiente de precisar:** qué hace el sistema con una factura fechada en domingo — ¿bloquea la
validación con error de dominio, levanta una alerta que el asistente puede justificar, o la
propone para descarte?

---

## C10 / A8 — Almacenamiento de documentos · PARCIALMENTE CERRADO

### Decisión: disco compartido

Python escribe los archivos descargados en un volumen; `DocumentoRecibido` guarda la ruta;
.NET lee ese volumen y sirve los bytes a la SPA por un endpoint autenticado.

**La ruta se compone a partir de una raíz configurable**, nunca absoluta en el código. Ambos
runtimes reciben la misma raíz por configuración y `DocumentoRecibido.Ruta` almacena la parte
relativa. Es lo que evita el acuerdo tácito de rutas absolutas entre dos lenguajes.

### Costos aceptados, que ahora son requisitos

| Costo | Requisito derivado |
|---|---|
| Volumen compartido obligatorio | La topología de despliegue debe garantizarlo. Entra en A14 como restricción, no como opción. |
| Dos respaldos que coordinar | La política de C9 debe cubrir **base y volumen de forma consistente**. Un asiento cuyo documento ya no existe es pérdida de evidencia contable. |
| Riesgo de huérfanos | Fila sin archivo, o archivo sin fila. Necesita una verificación periódica de integridad que hoy no existe. |

### El documento antes de validar

Los archivos se sirven **desde el volumen, no desde Drive**. Drive recibe la documentación
**al validar**, y el visor debe funcionar **antes** de eso. Queda confirmado que Drive no es el
almacén de trabajo, sino el archivo de destino.

### Entrega y renderizado

```http
GET /api/documentos/{id}/contenido
→ 200, Content-Type según el MIME real del archivo
```

**Visor nativo del navegador** dentro de un `<iframe>`. El visor integrado maneja PDF de varias
páginas, y un adjunto JPG o PNG servido con su MIME correcto se renderiza igual sin código
adicional. Se descarta PDF.js: solo se justificaría para anotar sobre el documento, que no es un
requisito del PRD ni del prototipo.

---

## A9 / A14 / A15 — Topología: mismo origen tras proxy inverso · CERRADO

```
https://facturas.empresa.local
    /        → SPA Angular compilada (estáticos)
    /api/*   → ASP.NET Core (Kestrel)
```

### Cookie de sesión, decidida

```csharp
options.Cookie.HttpOnly     = true;
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
options.Cookie.SameSite     = SameSiteMode.Lax;
options.Cookie.Name         = "__Host-session";
```

`SameSite=Lax` **funciona** porque no hay cruce de orígenes. No se necesita CORS, ni
`AllowCredentials`, ni `withCredentials` en Angular. El criterio de aceptación del Flujo 6
—*"la cookie se emite con los atributos HttpOnly y SameSite"*— deja de ser un marcador de
posición y pasa a tener un valor concreto que se puede verificar.

### Efecto sobre el visor

```html
<iframe src="/api/documentos/12/contenido"></iframe>
```

Al ser mismo origen, **la cookie viaja**. Era exactamente el punto donde A9 y A15 se
manifestaban como un panel en blanco.

### TLS

El certificado se gestiona y termina **en el proxy inverso**, en un solo lugar. Cierra la parte
de A15 relativa a dónde ocurre la terminación; queda por decidir el origen del certificado
(autoridad interna, Let's Encrypt o comprado) y quién lo renueva.

### Contradicción a corregir

ADR 0001 declara en sus consecuencias *"autenticación por token"*, lo que contradice a ADR 0007.
Hay que corregirlo (hallazgo A9, párrafo final).

---

## C11 — Adjuntos manuales · CERRADO

### Decisión: tabla separada

| Tabla | Propietario | Contenido |
|---|---|---|
| `DocumentoRecibido` | Python | Adjuntos descargados de Gmail |
| `AdjuntoManual` | .NET | Archivos subidos por el asistente desde la SPA |

La partición de ADR 0003 queda **impecable por tabla**: ningún componente escribe donde no le
corresponde, y la mitigación de esquemas separados con permisos por usuario de base de datos
sigue siendo aplicable.

### Contrato

```http
POST   /api/facturas/{id}/adjuntos
DELETE /api/facturas/{id}/adjuntos/{adjuntoId}
```

Tipos permitidos y tamaño máximo se resuelven junto con A6: el prototipo ya expone "Adjuntos
permitidos" como ajuste de configuración.

### Borrado con rastro

Eliminar un adjunto es **borrado lógico con auditoría**. `AdjuntoManual` lleva `EliminadoEn`,
`EliminadoPor` y `MotivoEliminacion`.

`AuditoriaCorreccion` registra campos modificados, no archivos eliminados. Sin esto quedaba un
hueco de trazabilidad justo sobre el **respaldo documental de un asiento contable**, que es el
tercer punto de C11.

### Costo aceptado: la consulta es un UNION

El visor, el contador *"Medios probatorios (N)"* y el empaquetado hacia Drive operan sobre dos
tablas con formas distintas, para algo que el usuario ve como una sola lista. .NET expone una
**vista unificada de documentos de la factura**, igual que con la bandeja en C7. Angular nunca
combina fuentes.

### Requisito derivado: Python no puede leer `AdjuntoManual`

El empaquetado hacia Google Drive lo ejecuta el worker, y Python no lee tablas de .NET.

**Se resuelve con la decisión de C5:** cada evento define qué información propagar. El *payload*
de `FACTURA_VALIDADA` incluye la **lista completa de rutas de documentos, de ambos orígenes**,
resuelta por .NET al emitir el evento. Python empaqueta desde el *payload* y **no consulta
ninguna de las dos tablas**.

Es la razón concreta por la que el evento granular con *payload* explícito —y no el evento
genérico de reconciliación— era la opción compatible con esta partición.

---

## C9 — Política de respaldo · CERRADO

### Decisión

Respaldo nativo escalonado, con **orden deliberado** entre los dos almacenes.

| Frecuencia | Acción |
|---|---|
| Diario, paso 1 | Copia del **volumen de documentos** |
| Diario, paso 2 | `FULL BACKUP` de la base de datos |
| Cada 15 minutos | `LOG BACKUP` (respaldo de log de transacciones) |

### Por qué el orden importa

El volumen se copia **antes** que la base. Así toda fila que referencia un archivo tiene ese
archivo presente en la copia. En el orden inverso, la base respaldada contiene referencias a
documentos que la copia del volumen todavía no capturó: asientos contables sin su evidencia.

Es la mitigación concreta del costo que asumiste al elegir disco compartido en C10.

### Objetivos declarados

- **RPO = 15 minutos.** Pérdida máxima aceptable de trabajo.
- **RTO:** por definir.
- **Destino fuera del host.** Un respaldo en el mismo disco que protege no es un respaldo.
- **Retención:** por definir, contra el requisito de conservación indefinida del PRD.

### Prueba de restauración

Se ejecuta una **prueba de restauración periódica documentada**. Un respaldo que nunca se
restauró no es un respaldo, es una suposición — y esto es la contabilidad de compras completa
de la empresa, sin copia en ningún otro sistema desde que el PRD retiró el sistema contable
externo.

---

## C8 — Vigilancia del worker · RIESGO ACEPTADO

**Decisión del responsable del proyecto:** el reinicio del worker es **manual**. No se implementa
latido, ni supervisor automático, ni monitor externo en esta versión.

Queda documentado lo que eso implica, para que la decisión sea revisable más adelante:

- Si el worker se detiene o se cuelga, **nadie avisa**. El mecanismo de notificación vive dentro
  del propio worker.
- El fallo es silencioso: una bandeja sin facturas nuevas es indistinguible de un día sin
  facturas hasta que alguien lo note por otra vía.
- Los criterios de éxito del PRD de **visibilidad en ≤15 minutos** y **tasa de entrega de
  notificaciones ≥99%** suponen un worker vivo. Con reinicio manual, ambos dependen de la
  atención del operador.

No es bloqueante para construir el sistema. Se registra como riesgo abierto en el TDD.

---

## A5 — Redondeo y tipos decimales · CERRADO

### Tipos

| Dato | Tipo |
|---|---|
| Importes | `DECIMAL(18,2)` |
| Tipo de cambio | `DECIMAL(12,6)` |

**Nunca** `float` ni `real` en una columna monetaria. No hay alternativa razonable.

### Conversión a soles: se ancla total e IGV, se deriva la base

```
totalPEN = round(totalOrig * TCventa, 2)     ← anclado
igvPEN   = round(igvOrig   * TCventa, 2)     ← anclado
basePEN  = totalPEN - igvPEN                 ← DERIVADO
```

La identidad `basePEN + igvPEN = totalPEN` se cumple **por construcción**, no por tolerancia.
No hay caso en que falle, no hay línea de ajuste y no hay control que pueda dar falso negativo.

### Por qué se deriva la base y no el IGV

El IGV sustenta el **crédito fiscal** y debe ser exacto respecto del comprobante. La diferencia
de redondeo la absorbe la cuenta de cargo (`6x`/`1x`), donde un céntimo no tiene consecuencia
tributaria.

### Efecto sobre el cargo dividido

Cuando el asistente divide el cargo en varias cuentas del motivo (C1), la suma de esas líneas
debe igualar **`basePEN`**, el valor derivado. Es la misma invariante 2 de C3, ahora con un
valor que siempre existe y siempre cuadra.

### Se descarta la tolerancia del prototipo

El prototipo acepta una diferencia de hasta un céntimo (`Math.abs(sumDebe - sumHaber) < 0.01`,
línea 1357). Contradice lo decidido en C3 y deja que el libro de compras acumule céntimos que
nadie cuadra a fin de mes. **Se elimina.**

---

## A12 — Fechas y zonas horarias · CERRADO

### Fechas de negocio: `DATE`, sin hora

`FechaEmision`, `FechaContable` y `TipoCambio.Fecha` son fechas calendario puras. **Un día
calendario no tiene zona horaria.** Si el comprobante dice `28/07/2026`, eso es el dato, y sale
del XML o del OCR, nunca del reloj del servidor.

### Marcas técnicas: `DATETIME2` en UTC

`CreatedAt`, `UltimoIntento`, `CargadoEn`, `EliminadoEn` y demás marcas de auditoría se
almacenan en **UTC** y se convierten a hora de Lima (UTC-5) **solo al presentar**.

### El problema que esto elimina

El escenario que describe A12 —factura emitida a las 23:30 que se registra al día siguiente en
UTC, busca el tipo de cambio equivocado y, a fin de mes, cae en el periodo contable
equivocado— **desaparece**, porque la fecha de emisión nunca se deriva de una marca de tiempo.

La regla de domingos se evalúa sobre ese `DATE`, no sobre una conversión horaria.

---

## Regla de domingos · CERRADO

Política de la compañía: **no se aceptan documentos emitidos en domingo**. No figuraba en el
PRD ni en el TDD.

```http
POST /api/facturas/{id}/validar
→ 409 Conflict
  { "title": "Comprobante emitido en domingo", ... }
```

Se evalúa sobre `FechaEmision`, que es un `DATE` puro (A12).

**Dos salidas, idénticas a las del duplicado en C2:**

1. El OCR leyó mal la fecha → el asistente la corrige → la condición desaparece.
2. El comprobante es realmente de domingo → `POST /facturas/{id}/descartar`.

La política se aplica sin excepciones y sigue habiendo salida cuando el dato estaba mal leído.
No se introduce ningún mecanismo nuevo.

---

## A2 — Alcance de Python · CERRADO

### Principio de división

> **Python es el worker de integración y procesamiento asíncrono del sistema.**
> **.NET es el owner del dominio y de la API transaccional.**

Python conserva todas las integraciones externas: Gmail, Drive, Sheets, Telegram, correo, OCR y
scraping de la SBS.

### Lo que cambia en ADR 0002

La justificación deja de ser *"por coherencia con la propiedad del procesamiento y de los
reintentos"*, que era la palabra que A2 señalaba como insuficiente.

El eje real de separación es el **modelo de ejecución**:

| Componente | Naturaleza del trabajo |
|---|---|
| API .NET | Síncrono, transaccional, dueño de las invariantes del dominio |
| Worker Python | Asíncrono, tolerante a fallo, con reintentos y latencia variable frente a terceros |

Es un criterio verificable: ante una integración nueva, la pregunta es si su trabajo es
transaccional o asíncrono, no si "encaja mejor".

### Consecuencia

Las decisiones tomadas en C11 se mantienen tal cual, incluida la lista de rutas en el *payload*
de `FACTURA_VALIDADA` para que Python empaquete hacia Drive sin leer tablas de .NET.

---

## A3 — Promoción a factura · CERRADO

### Decisión: inbox de integración

Python **notifica mediante un mensaje de integración** que el procesamiento terminó. .NET lo
consume desde un **Inbox** y, **dentro de una transacción propia**, decide si corresponde
promover el resultado a una `Factura`.

```
InboxEvent   (Python escribe, .NET consume)
  PROCESAMIENTO_COMPLETADO
  PROCESAMIENTO_FALLIDO
```

### Dos reglas que la decisión fija

- Python **no accede a las tablas de dominio de .NET**.
- Python **no solicita operaciones de dominio**. No pide "crear factura": informa que el
  procesamiento terminó. **La decisión de promover es de .NET**, y puede no promover.

Es la misma disciplina de C5 —hechos, no órdenes— aplicada en la dirección Python → .NET.

### Las tres direcciones quedan simétricas

| Dirección | Contrato | Semántica |
|---|---|---|
| .NET → Python | `OutboxEvent` | Hechos de negocio |
| .NET → Python | `CommandQueue` | Órdenes de ejecución |
| Python → .NET | `InboxEvent` | Hechos de procesamiento |

**Ningún componente sondea la tabla interna del otro.** `Procesamiento` vuelve a ser privada de
Python y ADR 0003 recupera su coherencia: desaparece el contrato de facto que A3 denunciaba.

### Corrección a ADR 0004

ADR 0004 descartó una alternativa por *"poner un `BackgroundService` en .NET"*. Ese rechazo se
reescribe: la alternativa se descarta porque **no participa de la transacción del hecho de
negocio**, que es la razón real y sigue siendo válida. Que .NET tenga un servicio alojado
consumiendo su inbox es legítimo y no contradice nada.

---

## A6 — Configuración · CERRADO

### Modelo

Tabla `Configuracion` **tipada por secciones**, escrita por .NET y **leída por Python como
contrato declarado** — el patrón de `TipoCambio`, en la dirección contraria. No es clave-valor
genérico: los ajustes que el worker consume (carpeta monitoreada, extensiones permitidas,
frecuencia de sondeo, fecha de inicio) son un contrato y merecen tipo y validación.

### Ajustes que desaparecen

| Ajuste | Motivo |
|---|---|
| Filtro de asunto | La regla de candidatura de C4b no lo usa |
| Remitentes permitidos | Ídem |
| Palabras clave en el cuerpo | Ídem |
| Eliminar correos procesados | Decisión de esta sección |

### "Sincronizar ahora"

Resuelto: viaja por `CommandQueue` como `SINCRONIZAR_GMAIL` / `SINCRONIZAR_SBS`. Era el hueco
arquitectónico que A6 señalaba sin camino posible.

### Escritura en Gmail: solo etiquetar

El worker aplica una **etiqueta propia** al correo ya ingestado. **Nunca borra.**

- Alcance OAuth: `gmail.modify`. Permite etiquetar; no se usa para borrado irreversible.
- Es **reversible**: quitar la etiqueta permite reingestar.
- **No pisa el estado leído/no leído**, que pertenece al usuario.
- El correo original sobrevive como evidencia de última instancia si el volumen se pierde.

La idempotencia sigue viniendo del `HashContenido` (C4b), no de la etiqueta. La etiqueta es
señal para el humano, no mecanismo de control.

---

## A16 — Credenciales de Google · CERRADO

**Decisión:** OAuth de usuario, con la aplicación en estado **"En producción"** en Google Cloud.

> **Nunca en modo *testing*.** Una app en testing caduca sus refresh tokens **cada 7 días**: el
> sistema funcionaría una semana y se detendría. Es un requisito de configuración, no una
> recomendación.

El refresh token no caduca por tiempo, pero **sí puede revocarse** —cambio de contraseña de la
cuenta, revisión de seguridad de Google, revocación manual del usuario—, así que el camino de
reautenticación es obligatorio.

### Obliga a construir

1. **Flujo OAuth completo**, con *redirect URI* sobre HTTPS (queda cubierto por A15: TLS termina
   en el proxy inverso).
2. **Pantalla "Reconectar" funcional.** Hoy el botón del prototipo no tiene backend ni endpoint.
3. **Detección de credencial inválida** con aviso al usuario. ADR 0010 ya la clasifica bien como
   error permanente que se notifica de inmediato; lo que faltaba era la salida.

No requiere intervención del administrador de Google Workspace.

### Pendiente asociado

La cuota de almacenamiento de Drive frente al requisito de conservación indefinida sigue sin
verificar (se trata en S5).

---

## A7 — Gestión de secretos · CERRADO

### Decisión

Gestor de secretos **dedicado y desplegable en infraestructura propia**. Los tres artefactos
obtienen de él sus credenciales.

**El diseño no se acopla a ningún proveedor cloud.** La arquitectura define un **puerto de
almacén de secretos**; la implementación es sustituible. **HashiCorp Vault** se documenta como
implementación candidata, no como dependencia del diseño.

### Secretos cubiertos

Refresh token de Google · Token del bot de Telegram · Credenciales SMTP · Cadena de conexión a
SQL Server · Clave de acceso al propio gestor.

### Requisito derivado del prototipo

El token de Telegram se captura **desde la interfaz**, así que el puerto debe permitir
**escritura en caliente**, no solo lectura al arrancar.

### El secreto irreductible

Sigue existiendo un secreto fuera del gestor: el que permite acceder al gestor. Es inevitable en
cualquier diseño y se resuelve con la configuración protegida del servicio. Queda declarado, no
escondido.

### Consecuencias operativas

- El gestor entra en la política de respaldo de C9: si se pierde, se pierde el acceso a Gmail,
  Drive, Sheets y Telegram.
- Es una dependencia de **arranque** de los tres artefactos. Si no está disponible, el sistema
  no levanta. Entra en A18 como parte del procedimiento de despliegue.

---

## A17 — Observabilidad · CERRADO

### Decisión

Los tres artefactos emiten **logs estructurados** a un **agregador desplegado en infraestructura
propia**, con búsqueda, retención configurable y alertas por patrón. Coherente con la decisión
in-house de A7. Implementaciones candidatas: Seq o Grafana Loki; el diseño no se acopla a
ninguna.

### Lo que esto resuelve y el panel de errores no puede

El panel de errores lee tablas, de modo que **por construcción es ciego** a los fallos que
impidieron escribir en la base: SQL Server caído, worker que no arranca, excepción antes del
primer `INSERT`. Esos fallos ahora sí quedan registrados.

Con C8 en reinicio manual y sin vigilancia, esta es la única herramienta de diagnóstico que
sobrevive a la caída de la base.

### Correlación entre artefactos

El `CorrelationId` ya decidido para `CommandQueue` se propaga a los tres artefactos y a los
eventos de `OutboxEvent` e `InboxEvent`. Una sola búsqueda reconstruye el recorrido completo de
una factura: correo → procesamiento → promoción → validación → Drive y Sheets.

### Pendiente de fijar

Retención de logs y política de rotación, contra el costo de almacenamiento (se trata en S5).

---

## A18 / S4 — Esquema, entornos y despliegue · CERRADO

### El esquema es SQL versionado, con herramienta neutral

```
db/migrations/
  001_esquema_inicial.sql
  002_motivo_y_sugerencia_cuenta.sql
  003_commandqueue_inboxevent.sql
  ...
```

Aplicado por una herramienta **independiente de ambos runtimes** (DbUp, Flyway o equivalente).

**Razón:** el esquema **es el contrato de integración** del sistema. Sobre él se apoyan ADR 0003,
`OutboxEvent`, `CommandQueue` e `InboxEvent`. Definirlo con clases C# haría que las tablas de
Python fueran un efecto colateral del ORM de .NET, y revisar un cambio de la frontera obligaría a
leer C# en vez de SQL. En un sistema contable, un esquema legible y auditable no es un detalle
menor.

> **Aviso de terminología para los documentos.** "Migración" aquí significa **versionado del
> esquema de base de datos**. No tiene ninguna relación con la restricción del proyecto según la
> cual *no hay migración de datos hacia ningún sistema contable externo*. Los ADRs deben usar
> **"versionado del esquema"** para evitar que se lean como lo contrario de lo decidido.

### Orden de despliegue

```
1. migraciones de esquema
2. API .NET
3. worker Python
```

Resuelve el *"debe desplegarse de forma coordinada"* que ADR 0001, ADR 0003 y ADR 0008 repiten
tres veces sin definir nunca.

### Entorno de pruebas

Existe un entorno separado de producción, con **su propia cuenta de Google, su carpeta de Drive
y su hoja de cálculo**. Sin él, probar el flujo completo significa crear carpetas en el Drive
real y escribir en el Sheets que alimenta el dashboard.

---

## S1, S3, S5 — Resueltas sin decisión adicional

### S1 — `Factura.estado` mezclaba dos ejes · CERRADO por construcción

Tras C4 y C7, `Factura.Estado` contiene **solo ciclo de vida**:

```
PENDIENTE_VALIDACION | VALIDADA | DESCARTADA
```

`ERROR` se fue a las incidencias de procesamiento (C7) y `ALERTA` nunca fue un estado: es el
resultado de los indicadores —proveedor genérico, posible duplicado, campos no extraídos, fecha
en domingo—, que son campos propios. El chip de la bandeja **se deriva**; no se almacena. La
ambigüedad de los filtros y contadores del Flujo 7 desaparece.

### S3 — Cuotas como clase de error propia · CERRADO

ADR 0010 agrupa "superación de cuota" con los fallos de red: 3 intentos con espera creciente.
Las cuotas de Gmail y Drive se restablecen **por ventana** —por minuto o diaria—, así que tres
intentos cortos producen errores falsamente terminales.

Se añade una **tercera clase**: `DIFERIBLE`, con horizonte de reintento acorde a la ventana de
la cuota, no a la latencia de red.

```
TRANSITORIO  → 3 intentos, espera creciente en segundos
DIFERIBLE    → reintento al abrirse la ventana de cuota
PERMANENTE   → sin reintentos, notificación inmediata
```

### S5 — Dimensionamiento y umbral de aviso · CERRADO

No hace falta un cálculo fino; hace falta un **umbral**. El modo de fallo real es un disco lleno
que detiene la ingesta en silencio, la misma familia de problema que C8.

- **Alerta por espacio libre** del volumen de documentos, emitida desde el agregador de A17.
- Aplica a los tres consumidores de espacio: volumen de documentos, crecimiento de la base y
  retención de logs.
- La cuota de Drive frente a la conservación indefinida (A16) debe **verificarse contra el plan
  de Workspace contratado** antes de la primera factura real.

---

## Correcciones documentales, sin decisión

| # | Corrección |
|---|---|
| A10 | ADR 0003 sigue listando `Rol` y `UsuarioRol`, que ADR 0007 eliminó, y omite media docena de tablas. Se reescribe completa. |
| A13 | El TDD afirma que el prototipo *"no contempla"* el estado intermedio entre validar y archivar: **sí lo contempla** (`migracion`, `sheetsSync`). Y ADR 0006 dice que glosa y fecha contable *"se derivaron del prototipo"* como atributos del asiento: en el prototipo viven en la **factura**. Ambas se corrigen. |
| A13b | El prototipo tiene `compro` (`CP-000112`), un correlativo interno distinto del número fiscal, que el modelo omite y el PRD excluye. Hay que cerrar cuál manda (pregunta 11 de D1). |
| A9b | ADR 0001 declara *"autenticación por token"* en sus consecuencias, contradiciendo a ADR 0007. Se corrige. |
| A11 | El XML ya no es candidato a resolver C1, pero **sí es la fuente prioritaria de cabecera** (C4b). ADR nueva sobre la frontera del motor de extracción, incluida la decisión de si los documentos salen de la organización. |

---

## B1 — Contabilidad por destino · CERRADO con los datos maestros

Los datos maestros entregados (`Cuentas.xlsx`, 1650 cuentas) traen las columnas **`ctarefleja`** y
**`ctapuente`** pobladas para **283 cuentas**. La compañía **sí lleva contabilidad por destino**, y
el mapeo ya existe: no requiere criterio del asistente.

### Dos mecanismos, una sola mecánica

| Cuenta principal | `ctarefleja` | `ctapuente` | Casos |
|---|---|---|---|
| Compras de existencias (clase 60) | `20x`, `24x`, `25x` | `61x` variación de existencias | 16 |
| Gastos (clases 62-68) | `94x`, `95x`, `97x` | `791111` cargas imputables | 267 |

En ambos casos la regla es la misma: **`ctarefleja` al Debe, `ctapuente` al Haber**, por el importe
de la línea principal.

### Estructura del asiento

```
FACTURA: flete S/ 1,180 (base 1,000 + IGV 180) · Motivo 22 → 631111

BLOQUE PRINCIPAL
  631111  Flete traslado de mercadería   Debe   1000.00
  401111  IGV cuenta propia              Debe    180.00
  421211  Facturas y boletas en soles    Haber  1180.00

BLOQUE DESTINO (generado del plan de cuentas)
  946311  ctarefleja de 631111           Debe   1000.00
  791111  ctapuente de 631111            Haber  1000.00
```

El asiento cuadra globalmente porque el bloque de destino es neutro: `refleja` y `puente` se
compensan.

### Corrección obligatoria a ADR 0006

La invariante 2 —*"la suma de los cargos a cuentas `6x`/`1x` iguala la base imponible"*— **deja de
ser cierta**, porque `946311` también es un cargo. Se reescribe:

> Las invariantes 2, 3 y 4 se aplican **exclusivamente al bloque principal**. El bloque de destino
> se valida por separado: para cada línea principal con `ctarefleja`, existe su par
> reflejo/puente por el mismo importe.

`AsientoContableDetalle` gana un discriminador de **bloque** (`PRINCIPAL` / `DESTINO`).

### Corrección obligatoria a ADR 0011

`MotivoCuenta` **no almacena cuentas: almacena prefijos**. Las longitudes reales van de 2 a 6
dígitos, y las candidatas se resuelven contra las **907 hojas de 6 dígitos** del plan.

```
Motivo  6  Transferencia entre Bancos  → prefijo 104   → 20 candidatas
Motivo 70  Préstamos a terceros        → prefijo 16    → 34 candidatas
Motivo 22  Fletes traslado mercadería  → prefijo 631111 → 1 candidata
```

Un motivo puede declarar **varios prefijos separados por coma**. Los 90 motivos entregados resuelven
correctamente: **ningún prefijo queda sin cuentas**.

**Solo las cuentas de 6 dígitos son imputables.** Los niveles 2 a 5 son jerarquía.

---

## A1 — Catálogo de motivos · CERRADO en estructura

### Datos maestros recibidos

`Cuentas.xlsx` (1650 cuentas, 907 hojas de 6 dígitos) · `Motivos.xlsx` (90) · `Origen.xlsx` (13).

### Alcance por origen

Cada motivo declara su **origen de libro**, usando el catálogo de 13 códigos. La pantalla de
validación de una factura **solo ofrece los motivos de origen `02` COMPRAS**.

La clasificación **validada** está en `MOTIVOS-CLASIFICACION.md`. Reparto final: **50 motivos en
`02`**, 21 en `06`, 4 en `04`, 4 en `10`, 3 en `05`, 2 en `03`, 6 de baja.

> **Alcance de demo, no contable.** Los 23 motivos que corresponden realmente a `07` CAJA CHICA
> fueron **reclasificados a `02` COMPRAS por necesidad de la demostración**. Contablemente son de
> caja chica y la reclasificación **debe revertirse antes de producción**. Está marcada con `†` en
> el documento de clasificación para que sea reversible sin volver a analizarlos uno por uno.
>
> Tres de ellos —`5` Transferencia a Caja chica, `53` Recarga de tarjetas, `88` Devolución
> Comprobante— **no son gastos**: sus cuentas son de efectivo o por cobrar, y producirán asientos
> que cuadran pero no representan una compra.

### Seis motivos dados de baja

`1`, `28`, `39`, `44`, `76`, `83`.

`Motivo` lleva un indicador **`Activo`**. Un motivo inactivo **no se ofrece** en el selector pero
**no se borra**: los asientos históricos lo referencian y esa referencia debe seguir resolviendo.

### Límite de alcance declarado

Los motivos `39`, `44` y `76` eran de gasto **"sin sustento"**. Ese flujo —registrar gasto sin
comprobante— **queda fuera de este sistema**, que parte de una factura recibida por correo, y
seguirá llevándose por otra vía. Es un límite de alcance explícito, no una carencia.

---

## A2 — Boletas (03) y crédito fiscal · CERRADO

Una boleta **no otorga crédito fiscal**, de modo que no hay nada que cargar a `401111`. El importe
total va íntegro a la cuenta de cargo del motivo.

```
BOLETA de S/ 1,180

  656111  Útiles de escritorio        Debe   1180.00
  421211  Facturas y boletas en soles Haber  1180.00
```

**Dos líneas en el bloque principal**, no tres.

### Efecto sobre las invariantes de ADR 0006

| Invariante | Factura (01) | Boleta (03) |
|---|---|---|
| Cargos del bloque principal | igualan la **base imponible** | igualan el **monto total** |
| Cargo a `401111` | iguala el IGV | **no aplica** |
| Abono a `421211`/`421212` | iguala el monto total | iguala el monto total |

El **bloque de destino** refleja el **total**, no la base, porque el cargo es el total.

### Consecuencia para la extracción

Muchas boletas **no desglosan el IGV**. El sistema no debe exigir ese dato para el tipo `03`: si no
viene, no es un campo faltante, es que no existe.

---

## A3 — IGV: exonerados, inafectos y mixtas · CERRADO

`Factura` gana **`Afectacion`**: `GRAVADA` · `EXONERADA` · `INAFECTA`.

| Afectación | Bloque principal |
|---|---|
| `GRAVADA` | 3 líneas: cargo por la base, IGV a `401111`, abono al proveedor |
| `EXONERADA` / `INAFECTA` | **2 líneas**: cargo por el total, abono al proveedor |

Reutiliza exactamente el mecanismo de la boleta (A2). **No añade lógica nueva.**

### Las facturas mixtas quedan fuera de alcance

Una factura es gravada **o** no gravada, nunca ambas. Una factura que mezcle líneas gravadas y no
gravadas **se rechaza con `409`** y se registra por otra vía.

**Es detectable, no una suposición:** el XML UBL trae la afectación **por línea**, así que el
sistema puede comprobar que todas coinciden y rechazar explícitamente cuando no lo hacen. Para una
factura que solo llega en PDF, la afectación la confirma el asistente.

> Declarado como límite de alcance, igual que los motivos "sin sustento" de A1: el sistema no falla
> en silencio ante una mixta, la rechaza y lo dice.

---

## A4 — Notas de crédito (07) · CERRADO

### Espejo con vínculo obligatorio

El asiento de una nota de crédito **invierte los signos** respecto de la factura que modifica, y
**hereda de ella el motivo y la cuenta de cargo**. El asistente no vuelve a elegir motivo.

```
NC de S/ 236 sobre la factura F001-00234

  421211  Facturas y boletas en soles   Debe    236.00
  601110  heredada del original         Haber   200.00
  401111  IGV cuenta propia             Haber    36.00
```

El bloque de destino también se invierte: `ctarefleja` al **Haber**, `ctapuente` al **Debe**.

### Cambio en el modelo

`Factura` gana **`FacturaReferenciaId`**, obligatorio para el tipo `07`. El dato existe: el XML UBL
trae la referencia al comprobante que modifica.

ADR 0006 mantiene la relación **1:1 entre factura y asiento**. Lo que cambia es que una `Factura`
puede referenciar a otra.

### Por qué el vínculo es obligatorio

Sin él, nada garantiza que la nota de crédito revierta contra la **misma cuenta** que cargó la
factura. Si el asistente eligiera otro motivo, la cuenta original quedaría con saldo y otra quedaría
negativa, sin forma de detectarlo.

### Precondiciones de validación (supuesto declarado)

Validar una nota de crédito se rechaza con `409` si:

| Situación de la factura referenciada | Motivo |
|---|---|
| No existe en el sistema | No hay de dónde heredar el motivo y la cuenta |
| Está en `PENDIENTE_VALIDACION` | Primero se valida la original |
| Está `DESCARTADA` | No hay operación que rectificar |
| Su asiento está `ANULADO` | Ídem |

> Estas cuatro precondiciones **las asumí yo**, no las decidiste. Si alguna debe permitirse —por
> ejemplo, una nota de crédito que llega antes que su factura— hay que decirlo.

### Varias notas de crédito por factura, con tope

Una factura **admite varias** notas de crédito. La suma de todas **no puede exceder su importe**.

```
Factura 1 ──┬──▶ NC (07)
            ├──▶ NC (07)
            └──▶ NC (07)

Σ importes de NC vigentes  ≤  monto total de la factura
```

### Invariante nueva, y dónde se aplica

Es una **precondición de validación**, no una restricción de columna: depende del estado de otras
filas. Se evalúa **dentro de la misma transacción** que confirma la nota de crédito, sobre el total
acumulado de las notas ya vigentes más la que se está validando.

```sql
-- Rechaza con 409 si el acumulado supera el total de la factura original
SELECT SUM(f.MontoTotal)
  FROM Factura f
 WHERE f.FacturaReferenciaId = @facturaOriginalId
   AND f.Estado = 'VALIDADA';
```

**Solo cuentan las notas vigentes.** Una nota de crédito cuyo asiento se anuló **libera** su importe:
la capacidad disponible vuelve a subir.

> **Supuesto declarado:** el tope se compara contra el **monto total** de la factura, no contra su
> base imponible. Si el criterio contable fuera la base, dímelo — cambia el cálculo, no el mecanismo.

Con un solo usuario no hay concurrencia real, pero la comprobación va igualmente dentro de la
transacción: es la única forma de que sea una invariante y no una advertencia.

---

## B2 — Detracciones, retenciones y percepciones · CERRADO

### Solo la percepción entra al asiento

Las tres figuras no operan en el mismo momento:

| Figura | Momento | ¿Afecta al asiento de compra? |
|---|---|---|
| **Percepción** | Viene en el comprobante | **Sí.** Línea adicional al Debe en `401131` |
| **Detracción** | Al pagar | No. Origen `06` BANCOS |
| **Retención** | Al pagar | No. Origen `06` BANCOS |

```
FACTURA con percepción · base 1,000 · IGV 180 · percepción 23.60

  6xxxxx  Cuenta del motivo             Debe   1000.00
  401111  IGV cuenta propia             Debe    180.00
  401131  IGV régimen de percepciones   Debe     23.60
  421211  Facturas y boletas en soles   Haber  1203.60
```

### Efecto sobre la invariante 4 de ADR 0006

El abono a la cuenta de proveedor iguala **monto total + percepción**, no solo el monto total.

`Factura` gana un campo de **percepción**, que el XML UBL declara cuando aplica.

### Fuera de alcance, declarado

Detracción y retención ocurren al pagar, y **este sistema registra comprobantes, no pagos**. Se
llevan por su origen correspondiente.

> **Caveat que conviene conocer:** la detracción condiciona **cuándo** se puede tomar el crédito
> fiscal. Es un asunto de reporte tributario, no del asiento, pero significa que el libro que
> produce este sistema **no basta por sí solo** para determinar el crédito fiscal del periodo.

### Confirmación de cuentas contra el plan real

| Cuenta | Descripción en el plan | Uso |
|---|---|---|
| `401111` | IGV – CUENTA PROPIA | IGV de la compra |
| `401131` | IGV – RÉGIMEN DE PERCEPCIONES | Percepción |
| `421211` | FACTURAS Y BOLETAS EN SOLES | Proveedor, soles |
| `421212` | FACTURAS Y BOLETAS EN DOLARES | Proveedor, dólares |

El plan también tiene `4211 NO EMITIDAS` (`421111`, `421112`), para provisiones sin comprobante
emitido. **No aplican aquí:** este sistema arranca de un comprobante recibido, de modo que `4212
EMITIDAS` es siempre la correcta. Queda confirmado, no supuesto.

---

## Cuenta de IGV · CONFIRMADO con condición

El IGV de la compra va **siempre a `401111`** IGV cuenta propia.

El plan también contiene `401161` IGV destinado a operaciones gravadas y `401171` destinado a
operaciones comunes, que existen para la **prorrata del crédito fiscal**. **No se usan** en este
sistema.

> **Condición de revisión:** esta decisión vale mientras la compañía tome el crédito fiscal íntegro.
> Si aparecen ventas no gravadas y hay que prorratear, `401111` deja de ser correcta y la cuenta de
> IGV pasaría a depender del destino de la compra. Las cuentas ya están en el plan esperando.

---

## B3 — Diferencia de cambio · FUERA DE ALCANCE

El asiento congela el importe en soles al confirmarse y **no se vuelve a tocar**.

La diferencia de cambio nace **al pagar** o **al cierre de periodo**, y este sistema no modela
ninguno de los dos. Mismo criterio que B2: lo que ocurre después del comprobante se lleva por su
propio origen de libro.

> **Consecuencia declarada.** El saldo de `421212` / `431212` que produce este sistema **no está
> ajustado a la fecha de cierre**. Quien realice el cierre mensual debe revaluar los pasivos en
> moneda extranjera por su cuenta. No es un defecto: es el límite de alcance, y debe estar escrito.

Registrar la diferencia de cambio exigiría dos capacidades que el sistema no tiene: saber qué
facturas siguen pendientes de pago, y saber cuándo cierra el periodo.

---

## B4 — Cuenta de proveedor: terceros o relacionadas · CERRADO

`Proveedor` gana **`EsRelacionada`**. La cuenta se resuelve cruzando **moneda × relación**:

| Relación | Moneda | Cuenta |
|---|---|---|
| Tercero | Soles | `421211` FACTURAS Y BOLETAS EN SOLES |
| Tercero | Dólares | `421212` FACTURAS Y BOLETAS EN DOLARES |
| Relacionada | Soles | `431211` FACTURAS Y BOLETAS RELAC. EN SOLES |
| Relacionada | Dólares | `431212` FACTURAS Y BOLETAS RELAC. EN DOLARES |

El plan de cuentas tiene la estructura **espejo completa** bajo `43`, confirmada contra el dato
real. Todo se resuelve de forma automática: el asistente no decide nada nuevo.

**`P00000 (Varios)` es siempre tercero.** Una parte relacionada nunca sería un proveedor sin
identificar.

### Por qué es atributo del proveedor y no de la factura

Es un dato **estable**: si un proveedor es del grupo hoy, lo será en las siguientes doscientas
facturas. Preguntarlo por factura sería repetir una decisión invariable, invitando al error por
automatismo y sin dejar rastro de cuál es la clasificación correcta.

**Tarea de dato maestro:** marcar en el catálogo de proveedores cuáles son partes relacionadas.
Suelen ser pocas.

### Corrección a ADR 0011

La cuenta de proveedor deja de resolverse solo por moneda. Sigue siendo **determinista y automática**
—no requiere sugerencia ni aprendizaje— pero ahora depende de dos dimensiones.

---

## C1 / C2 — Periodo cerrado y tope de reapertura · CERRADO

### Fecha de corte configurable

Un único ajuste en `Configuracion`:

```
FechaCorteContable  DATE
```

Validar un asiento cuya `FechaContable` sea **anterior** a esa fecha se rechaza con `409`. Quien
cierra el mes **avanza la fecha de corte**. Un solo campo que mover.

No se modela una entidad `PeriodoContable` con estados: sería una segunda fuente de verdad sobre
qué está cerrado, cuando el cierre real ocurre en el software contable de la compañía.

### C2 queda resuelto por la misma regla

El **tope de reapertura** es la fecha de corte. Un asiento cuya fecha contable quede antes del corte
**no se puede reabrir**. Cero lógica adicional para el hallazgo O1.

**Limitación declarada:** no se pueden reabrir periodos individuales ni queda historial de quién
cerró qué y cuándo. La fecha de corte solo avanza.

---

## C3 — Asiento contra el proveedor genérico · CERRADO

**No se puede validar una factura con `P00000 (Varios)`.** Se rechaza con `409`.

### El alta de proveedores NO entra en el alcance

**Este sistema no crea proveedores.** El alta se realiza en **otro flujo, con otro sistema**, fuera
del alcance de este proyecto.

Flujo real del asistente contable:

1. Busca el proveedor en el catálogo.
2. Si no lo encuentra, **sale de este sistema** y ejecuta el proceso de registro de proveedores en
   el sistema correspondiente.
3. Vuelve, y **selecciona** el proveedor ya registrado.

Mientras tanto la factura permanece en `PENDIENTE_VALIDACION`, y "Guardar avance" conserva el
trabajo hecho hasta ese punto (ADR 0006).

**No existe `POST /api/proveedores`.** El catálogo es de **solo lectura** para esta aplicación, tal
como declaraba el TECH-DESIGN original. No hay ampliación de alcance.

### `Proveedor` es una tabla externa

El proveedor lo crea el otro sistema **en la misma base de datos asignada al proyecto**. Esta
aplicación **solo lee**.

Es coherente con la restricción del proyecto: todo se graba en la base asignada, sin integración por
API con ningún sistema externo.

**Ventaja operativa:** el proveedor recién registrado está disponible **de inmediato**. El asistente
sale, lo registra, vuelve y lo encuentra. No hay sincronización ni espera en el camino crítico.

### Cuarta clase de tabla en ADR 0003

La partición de propiedad de datos gana una clase que no existía:

| Clase | Definición | Permisos de esta aplicación |
|---|---|---|
| Privada | Un componente escribe y lee | Según el propietario |
| De contrato | Coescrita por diseño entre .NET y Python | Producir o consumir |
| De publicación | Un tipo de fila, varios orígenes | Según el origen |
| **Externa** | **Escrita por un sistema ajeno al proyecto** | **`SELECT` únicamente** |

Sin `INSERT`, `UPDATE` ni `DELETE`. Es la clase donde el refuerzo por permisos de base de datos
resulta más importante, porque protege datos de los que este sistema no es responsable.

### Todos los datos maestros son externos

No solo `Proveedor`. El plan de cuentas, los motivos y los orígenes se mantienen igualmente en el
sistema contable de la compañía.

| Tabla externa | Contenido | Esta aplicación |
|---|---|---|
| `Proveedor` | Catálogo de proveedores | `SELECT` |
| `CuentaContable` | Plan contable, 1650 cuentas, con `ctarefleja` y `ctapuente` | `SELECT` |
| `Motivo` | 90 motivos con sus prefijos de cuenta | `SELECT` |
| `Origen` | 13 orígenes de libro | `SELECT` |

### Tablas satélite, propiedad de .NET

Lo que **este proyecto necesita y el sistema contable no tiene** vive aparte, unido por clave. El
catálogo externo **no se toca nunca**.

| Satélite | Aporta | Para |
|---|---|---|
| `ProveedorAtributo` | `EsRelacionada` | B4: cuenta `4212` frente a `4312` |
| `MotivoAtributo` | `Activo`, `OrigenLibro` | A1: bajas y alcance por origen |
| `SugerenciaCuenta` | Frecuencia por proveedor y motivo | ADR 0011: el aprendizaje |

### Dos ventajas que esto compra

**El catálogo se mantiene solo.** Si el contador agrega una cuenta o un motivo en el sistema
contable, aparece aquí sin replicar nada. Desaparece el riesgo de dos catálogos que divergen en
silencio hasta que un asiento usa una cuenta que ya no existe.

**La reclasificación de demo es reversible sin tocar contabilidad.** Los 23 motivos de caja chica
movidos a `02` viven en `MotivoAtributo`, no en el catálogo real. Revertirlos antes de producción es
actualizar una tabla satélite, no editar el plan contable de la compañía.

### Nota sobre `MotivoCuenta`

Los prefijos de cuenta llegan como una **lista separada por comas** dentro del propio catálogo de
motivos. `MotivoCuenta` no es una tabla externa aparte: es la **interpretación** que esta aplicación
hace de ese campo, resolviendo cada prefijo contra las 907 hojas de 6 dígitos del plan.

### Por qué se rechaza P00000

`421211` es una cuenta por pagar **por proveedor**. Un saldo acumulado contra "Varios" no se puede
conciliar ni pagar: no se sabe a quién se le debe.

`P00000` conserva su papel como **marcador temporal** de la factura recién promovida, que el
asistente debe resolver antes de validar.

---

## C4 — Correlativo del asiento · CERRADO

`AsientoContable` lleva **dos números**, con propósitos distintos:

| Campo | Contenido | Origen |
|---|---|---|
| `NumeroComprobante` | `F001-00234` | Número fiscal de la factura |
| `NumeroAsiento` | `02-2026-08-000123` | Correlativo propio, por periodo y origen |

Esto resuelve el conflicto entre el PRD —que excluye un correlativo propio— y el prototipo —que
muestra `compro` (`CP-000112`)—: **conviven los dos**, cada uno con su función.

### Se asigna al confirmar, no al abrir

Si el número se reservara al crear el borrador, cada factura abierta y luego descartada **quemaría
un número** y el libro quedaría con huecos sin explicación. En una revisión, un hueco hay que
justificarlo.

Asignar al confirmar produce una **secuencia sin saltos**.

---

## C5 — Regla de domingos · CERRADO

**Política uniforme, sin excepción por tipo de comprobante.** Se rechaza con `409` cualquier
comprobante emitido en domingo: facturas `01`, boletas `03` **y notas de crédito `07`**.

Los **sábados se permiten**. Los feriados **no se controlan**: exigiría mantener un calendario que
no se pidió.

> **Costo aceptado, declarado en la decisión.** Si un proveedor emite una nota de crédito en
> domingo, no se puede registrar. La factura original queda registrada y su rectificación
> bloqueada, con el pasivo inflado hasta que el proveedor reemita el documento. Es una consecuencia
> conocida de elegir una política sin excepciones.

---

## D4 — Origen del libro · CERRADO por construcción

Siempre `02 COMPRAS`. Se deriva de la decisión de A1: la pantalla de validación **solo ofrece
motivos de origen `02`**, de modo que el origen del asiento no es un campo que alguien elija, sino
una consecuencia del motivo seleccionado.

---

## Estado de los hallazgos

### Críticos

| # | Estado |
|---|---|
| C1 | **Cerrado.** El motivo determina la cuenta; se eliminan `Producto` y `FacturaDetalle`. |
| C2 | **Cerrado.** Índice único de identidad + recálculo + descarte. |
| C3 | **Cerrado.** `BORRADOR → CONFIRMADO → ANULADO`, con reapertura auditada (O1). |
| C4 | **Cerrado.** Estado `DESCARTADA` + regla de candidatura por etiqueta y adjunto. |
| C5 | **Cerrado.** Catálogo de eventos granulares + orden de consumo por factura. |
| C6 | **Cerrado.** Tipo de cambio **venta**; carga manual bloqueante; desaparece el 0.00. |
| C7 | **Cerrado.** El error vive en las tablas de ingesta; la bandeja es vista lógica de .NET. |
| C8 | **Riesgo aceptado.** Reinicio manual; sin vigilancia automática en esta versión. |
| C9 | **Cerrado.** Respaldo escalonado, volumen antes que base, RPO 15 min, prueba de restauración. |
| C10 | **Cerrado.** Disco compartido, servido por .NET, visor nativo, mismo origen. |
| C11 | **Cerrado.** `AdjuntoManual` propiedad de .NET, con borrado lógico auditado. |

### Advertencias

| # | Estado |
|---|---|
| A1 | **Cerrado.** `REPROCESAR` por incidencia (C7). |
| A2 | **Cerrado.** Eje de separación por modelo de ejecución, no "coherencia". |
| A3 | **Cerrado.** `InboxEvent`; nadie sondea la tabla interna del otro. |
| A4 | **Cerrado.** Contratos coescritos por diseño; idempotencia en cada integración. |
| A5 | **Cerrado.** Se anclan total e IGV, se deriva la base. `DECIMAL`, nunca `float`. |
| A6 | **Cerrado.** `Configuracion` tipada; el worker etiqueta en Gmail pero nunca borra. |
| A7 | **Cerrado.** Gestor de secretos in-house, sin acoplar a proveedor cloud. |
| A8 | **Cerrado.** Disco compartido con raíz configurable. |
| A9 | **Cerrado.** Mismo origen tras proxy inverso; `SameSite=Lax`. |
| A10 | Corrección documental: ADR 0003 se reescribe completa. |
| A11 | El XML es fuente prioritaria de cabecera (C4b). Falta ADR de la frontera del motor de extracción. |
| A12 | **Cerrado.** Fechas de negocio `DATE`; marcas técnicas en UTC. |
| A13 | Corrección documental en TDD y ADR 0006. |
| A14 | **Cerrado.** Proxy inverso, volumen compartido, entorno de pruebas y orden de despliegue. |
| A15 | **Parcial.** TLS termina en el proxy; falta el origen del certificado y quién lo renueva. |
| A16 | **Cerrado.** OAuth de usuario con app en producción; reconexión obligatoria. |
| A17 | **Cerrado.** Agregador in-house con `CorrelationId` transversal. |
| A18 | **Cerrado.** SQL versionado neutral, orden de despliegue y entorno de pruebas. |

### Sugerencias

| # | Estado |
|---|---|
| S1 | **Cerrado por construcción.** `Factura.Estado` solo contiene ciclo de vida. |
| S2 | **Cerrado.** Índice único de identidad (C2). |
| S3 | **Cerrado.** Tercera clase de error: `DIFERIBLE`. |
| S4 | **Cerrado.** SQL versionado con herramienta neutral. |
| S5 | **Cerrado.** Umbral de aviso por espacio libre desde el agregador. |

### Único pendiente: D1

Las preguntas contables **las responde un contador**, no una revisión técnica. Quedan fuera del
alcance de este documento y bloquean el `REGLAS.md`.

De las doce del informe original, estas **ya no aplican** tras las decisiones tomadas:

| # | Estado |
|---|---|
| 2 — Cuenta de compra según naturaleza | Resuelta: la determina el **motivo**, no el OCR (C1). |
| 3 — Cuentas fijas | Resuelta: `401111` IGV; `421211`/`421212` proveedor por moneda (C1). |
| 9 — Redondeo | Resuelta en su parte técnica: se ancla total e IGV (A5). Falta confirmar que la base absorba la diferencia. |

Siguen abiertas: **1** (notas de crédito 07), **4** (IGV exonerados e inafectos), **5**
(contabilidad por destino, clase 9), **6** (detracciones, retenciones y percepciones), **7**
(boletas), **8** (diferencia de cambio), **10** (periodo cerrado — bloquea acotar la reapertura
de O1), **11** (correlativo propio frente al número fiscal), **12** (asiento contra proveedor
genérico).

**Nueva, derivada de esta ronda:**

| # | Pregunta |
|---|---|
| 13 | **Tipo de cambio venta.** El usuario confirmó que es venta, no compra. Requiere validación formal del contador. |
| 14 | **Umbral de reapertura.** ¿Hasta cuándo se puede reabrir un asiento confirmado? Depende del concepto de cierre de periodo, hoy inexistente (se cruza con la 10). |

---
---

# Ronda 2 — Decisiones sobre `REVISION-ADVERSARIAL-V2.md`

Segunda revisión adversarial, ejecutada por el equipo sobre TECH-DESIGN v3, los 17 ADRs, el PRD y el
corpus contable. Recuento del informe: 11 críticos (C1–C11), 15 advertencias (A1–A15) y 4
sugerencias (S1–S4). **Los identificadores de esta sección son los de la ronda 2** y no coinciden
con los de la ronda 1.

---

## R2-C1 — Índice único contra flujo de duplicados · CERRADO

### El problema, verificado

`TECH-DESIGN.md` declara el índice como **único**:

```sql
CREATE UNIQUE INDEX UQ_Factura_Identidad
    ON Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';
```

La promoción inserta en `PENDIENTE_VALIDACION`, que **está dentro del filtro**. Consecuencias
comprobadas:

- **Problema A.** SQL Server trata los `NULL` como iguales en un índice único. La segunda factura
  con número no extraído es rechazada por el motor. El caso borde que el PRD manda soportar —campos
  vacíos resaltados para carga manual— es exactamente el que el índice impide.
- **Problema B.** La fila duplicada nunca llega a existir, de modo que el indicador
  `posibleDuplicado`, el recálculo al guardar, el `409` de ADR 0008 y las dos salidas ("corregir el
  número" / "descartar") son **código inalcanzable**.

### La decisión

**Manda el flujo de resolución.** El índice pasa a ser de **detección**, no de bloqueo.

```sql
CREATE INDEX IX_Factura_Identidad
    ON Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';
```

La unicidad se aplica **al validar**, dentro de la misma transacción que confirma factura y asiento:

```sql
-- Rechaza con 409 si ya existe otra factura VALIDADA con la misma identidad
SELECT TOP 1 f.FacturaId
  FROM Factura f
 WHERE f.RucProveedor    = @ruc
   AND f.TipoComprobante = @tipo
   AND f.Numero          = @numero
   AND f.Estado          = 'VALIDADA'
   AND f.FacturaId      <> @facturaActual;
```

### Por qué

El criterio del PRD es *"detecta y alerta antes de permitir un nuevo registro"*: **alertar, no
rechazar en el motor**. Un rechazo en el `INSERT` ocurre en el worker, días antes y sin interfaz
donde mostrarlo; el asistente ve una incidencia técnica en vez de dos facturas comparables.

La comprobación al validar es el único punto de control y cubre los tres caminos: promoción de un
duplicado, edición del número hacia uno ya existente, y validación concurrente.

### Consecuencias

- `PENDIENTE_VALIDACION` **admite** varias filas con la misma identidad, y varias con número vacío.
  Es el estado de trabajo: la limpieza ocurre al validar.
- El indicador `posibleDuplicado` recupera su sentido: se calcula contra las filas existentes al
  promover, al guardar avance y al validar.
- La unicidad deja de ser invariante del motor y pasa a ser **precondición de validación**, igual
  que el tope acumulado de notas de crédito (§A4 de la ronda 1). Con un solo usuario no hay
  concurrencia real, pero la comprobación va dentro de la transacción para que sea invariante y no
  advertencia.
- Se pierde la red de seguridad del motor: si alguien escribe en `Factura` fuera de la API, puede
  crear un duplicado validado. Aceptado — ADR 0003 ya prohíbe esa escritura por partición.

### Hallazgos afectados

R2-C1 cerrado. Toca `TECH-DESIGN.md` (modelo de `Factura`, Flujo 2, Flujo 3), ADR 0005 (el índice
dejaba de ser el argumento de idempotencia) y ADR 0008 (el `409` pasa a ser el único control).

---

## R2-C2 — Idempotencia de la promoción · CERRADO

### El problema, verificado

ADR 0005 se contradice consigo mismo. En "Idempotencia": *"`Procesamiento` lleva el indicador de si
ya originó una factura"*. En "Consecuencias": *"`Procesamiento` vuelve a ser privada de Python"*. Y
en la frontera: *"la decisión de promover es de .NET"*. Las tres cosas juntas obligan a .NET a
escribir en una tabla privada de Python, que es exactamente lo que ADR 0003 prohíbe y lo que ADR
0005 existe para corregir.

**El hallazgo se agrava con R2-C1.** ADR 0005 se apoyaba en el índice único de identidad como
*"respaldo real"* de la idempotencia. Ese índice acaba de dejar de ser único. Sin esta decisión, la
promoción no tiene ninguna garantía de motor.

### La decisión

El indicador pertenece al lado de .NET, como **índice único sobre `Factura.ProcesamientoId`**.

```sql
ALTER TABLE Factura
    ADD ProcesamientoId BIGINT NULL;

CREATE UNIQUE INDEX UQ_Factura_Procesamiento
    ON Factura (ProcesamientoId)
    WHERE ProcesamientoId IS NOT NULL;
```

Reejecutar la promoción sobre el mismo procesamiento produce una violación de índice, que el
servicio de fondo captura y trata como **no-op idempotente**: el `InboxEvent` se da por consumido y
no se crea una segunda factura.

### Por qué

Es el mismo argumento que ADR 0005 ya usa para preferir el índice de identidad sobre una bandera
calculada: **una invariante del motor no se puede saltar**, una bandera sí. Con la diferencia de que
ésta vive en una tabla de dominio de .NET y no toca la partición de ADR 0003.

`ProcesamientoId` es además el eslabón de trazabilidad que faltaba: desde una factura se puede
llegar a los adjuntos que la originaron sin pasar por el correo.

### Consecuencias

- `Procesamiento` queda **estrictamente privada de Python**. La contradicción de ADR 0005
  desaparece y la invariante 1 de ADR 0003 vuelve a ser cierta sin excepciones.
- `ProcesamientoId` es `NULL` para facturas que no nacieron de una promoción. Hoy no existe ese
  camino, pero el índice filtrado lo admite sin cambios.
- **`InboxEvent` necesita igualmente su propia marca de consumo.** El índice cubre "no crear dos
  facturas", no cubre "no volver a procesar el mismo evento" en el caso en que .NET decide **no
  promover** —adjunto corrupto, XML inválido, PDF ambiguo—, que ADR 0005 contempla y hoy no
  persiste. Sin marca, ese evento se reconsume en cada ciclo para siempre. La marca vive en
  `InboxEvent`, que es **tabla de contrato** y por tanto coescribible por diseño.
- La captura de la violación de índice debe distinguirse de la violación de identidad del
  comprobante, que tras R2-C1 ya no existe como índice. Hoy solo hay una violación posible, lo que
  simplifica el manejo.

### Hallazgos afectados

R2-C2 cerrado. Toca ADR 0005 (sección "Idempotencia" y consecuencias), ADR 0004 (marca de consumo
en `InboxEvent`) y `TECH-DESIGN.md` (modelo de `Factura`).

---

## R2-C3 — Orden por agregado contra `DIFERIBLE` · CERRADO

### El problema, verificado

ADR 0004 promete: *"los eventos de una misma factura se procesan serializados y en orden de
creación"*, y nombra el daño de no cumplirlo: `ASIENTO_ANULADO` y `ASIENTO_CORREGIDO` fuera de orden
dejan Sheets mal **de forma permanente**.

ADR 0010 introduce `DIFERIBLE` —cuota de Google agotada, reintento *"al abrirse la ventana de
cuota"*, que en una cuota diaria es al día siguiente—. Escenario que el diseño permite hoy:

1. 10:00 · `ASIENTO_CORREGIDO` → cuota agotada → `DIFERIBLE`, reprogramado a mañana.
2. 11:00 · el asistente anula ese asiento → `ASIENTO_ANULADO` → se aplica sin problema.
3. Mañana · el `ASIENTO_CORREGIDO` diferido se reintenta y **resucita el importe sobre un asiento
   anulado**.

Se suma que `READPAST` —única dependencia de motor que ADR 0002 declara— **salta** las filas
bloqueadas: reclamar por fila no produce orden por agregado.

### La decisión

**Guarda de obsolescencia en el destino.** Cada evento lleva una `Secuencia` monótona por
`AgregadoId`, y cada fila del destino registra la secuencia del último evento aplicado.

```sql
ALTER TABLE OutboxEvent
    ADD Secuencia BIGINT NOT NULL;   -- monótona por AgregadoId
```

```
Al aplicar el evento e sobre la fila destino:

    SI e.Secuencia <= fila._Secuencia
        → descartar como OBSOLETO  (estado terminal, sin error, sin notificación)
    SI NO
        → upsert + escribir e.Secuencia en la fila
```

En Sheets la secuencia vive en una **columna propia** de la hoja, junto a la clave de *upsert*. En
Drive no hace falta: las operaciones de carpeta son aditivas y no se pisan.

### Por qué

Convierte el orden de aplicación de **precondición** en **resultado**. El evento diferido no
necesita llegar a tiempo: llega, comprueba que ya se aplicó uno posterior y no hace nada. El daño
que ADR 0004 describe se vuelve imposible sin serializar nada.

La alternativa —bloqueo estricto por agregado, que era la dirección del informe— cumple la garantía
literalmente pero paga un precio desproporcionado: **una cuota agotada congela todos los eventos de
esa factura hasta el día siguiente**, incluidos los que sí habrían pasado.

**No es reconciliación de estado.** Los eventos siguen siendo granulares y con nombre propio, tal
como se decidió en la ronda 1 (§C5). La secuencia solo responde a *"¿este evento sigue siendo el más
reciente?"*, no transporta el estado completo del agregado.

### Consecuencias

- **La garantía de ADR 0004 cambia de redacción.** Ya no es *"se procesan serializados y en orden"*
  sino **"el efecto final corresponde siempre al evento más reciente"**. Es una garantía más débil y
  más honesta: es la que el sistema puede sostener con reintentos diferidos.
- `OBSOLETO` es una **cuarta clase terminal**, junto a las tres de ADR 0010. No es error, no
  notifica y no cuenta como fallo en ninguna métrica.
- Los eventos de una misma factura **pueden aplicarse en paralelo** sin romper nada. `READPAST` deja
  de ser un problema.
- **Costo:** el *payload* de cada evento debe ser autosuficiente para reconstruir la fila entera.
  Un evento que solo trajera un delta no se puede saltar. Los cuatro eventos del catálogo ya lo
  cumplen.
- **Costo:** la columna de secuencia es visible en la hoja para quien la abra. Se documenta como
  columna técnica; Looker Studio no la usa.

### Hallazgos afectados

R2-C3 cerrado. Toca ADR 0004 (garantías comunes y `Secuencia`), ADR 0010 (clase terminal
`OBSOLETO`) y `TECH-DESIGN.md` (Flujo 5).

---

## R2-C4 — `reactivar` sin transición ni evento · CERRADO

### El problema, verificado

`POST /api/asientos/{id}/reactivar` aparece **una sola vez en todo el repositorio**: en ADR 0008.
No está en ADR 0006 (dueño del ciclo de vida, cuyo diagrama no tiene ninguna flecha saliendo de
`ANULADO`), no está en el catálogo de eventos de ADR 0004, no está en `REGLAS.md` §9 y la palabra
no aparece en `DECISIONES-REVISION.md`. El endpoint no tiene respaldo en ninguna decisión.

Implementado tal cual, reactivar devuelve el asiento a `CONFIRMADO` en la base y **no emite nada**:
el importe queda descontado del dashboard de forma permanente. Es el mismo bug que ADR 0004 celebra
haber corregido, con el signo invertido.

Y hay un callejón sin salida que ningún documento nombra: **anular el asiento deja la factura
irrectificable para siempre**. La precondición de nota de crédito rechaza toda NC cuya factura
referenciada tenga el asiento `ANULADO`, la factura sigue `VALIDADA`, y no hay transición de vuelta.

### La decisión

**Se retira el endpoint. `ANULADO` es terminal.** Y para que eso no produzca el callejón, la
relación entre factura y asiento deja de ser 1:1 estricta:

```
Factura 1 ─── N ─▶ AsientoContable
                   (a lo sumo UNO en estado distinto de ANULADO)
```

```sql
CREATE UNIQUE INDEX UQ_Asiento_Vigente
    ON AsientoContable (FacturaId)
    WHERE Estado <> 'ANULADO';
```

```
BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO  (terminal)
     ▲                     │
     └───────reabrir───────┘
```

Anular libera la factura: vuelve a admitir un asiento en `BORRADOR`, que al confirmarse toma **su
propio correlativo**. El asiento anulado permanece en el libro con el suyo.

### Por qué

Es la ortodoxia contable y la alternativa que **ADR 0006 ya había evaluado**: un asiento confirmado
es un hecho, y un hecho no se deshace; se anula y se emite otro. La numeración lo refleja: dos
asientos, dos números, ninguno reutilizado.

La opción de añadir `reactivar` con su evento `ASIENTO_REACTIVADO` cierra el hueco documental pero
deja un asiento que muere y revive, que no tiene lectura contable defendible ante una revisión.

### Consecuencias

- **`AsientoContable` deja de ser 1:1 con `Factura`.** Lo dice `TECH-DESIGN.md` hoy y hay que
  corregirlo. Toda consulta que asuma un solo asiento por factura debe filtrar por vigencia.
- **La precondición de nota de crédito cambia de sujeto**: mira el asiento **vigente** de la factura
  referenciada, no "el asiento". Una factura cuyo asiento se anuló y se rehízo vuelve a admitir
  notas de crédito, que es el comportamiento correcto. Esto se cruza con R2-C10.
- **Reabrir y anular dejan de ser intercambiables** y hay que decir cuándo se usa cada uno:
  `reabrir` corrige un asiento *dentro* del periodo, conservando su número; `anular` lo saca del
  libro y obliga a uno nuevo. La regla de tope de reapertura de la ronda 1 (§C1/C2) se aplica solo
  al primero.
- El correlativo **no es reutilizable**: un asiento anulado conserva su número y ese número no
  vuelve a emitirse. Es un hueco en la secuencia de importes, no en la de números, que es
  exactamente lo que un libro debe mostrar.
- `AuditoriaCorreccion` ya cubre anulaciones; la mención a *"reactivaciones"* del TDD se retira.

### Hallazgos afectados

R2-C4 cerrado, y R2-C10 parcialmente encaminado. Toca ADR 0008 (retirar el endpoint), ADR 0006
(cardinalidad, índice de vigencia y criterio reabrir/anular), `TECH-DESIGN.md` (modelo y
`AuditoriaCorreccion`) y `REGLAS.md` §5 y §9.

---

## R2-C5 — Clave de sincronización externa · CERRADO sin consulta

Único hallazgo de la ronda con una sola respuesta defendible. Se decide directamente.

### El problema, verificado

El TDD promete: *"Drive **busca antes de crear** y Sheets hace **upsert por clave**: repetir un
evento no duplica carpeta ni fila."* Ni el TDD ni ningún ADR dicen **cuál es esa clave**. La única
clave de negocio natural es `(RUC, tipo, número)` — y el diseño contiene un flujo cuyo propósito
explícito es cambiarla, más el evento `FACTURA_CORREGIDA` para propagar el cambio.

Consecuencia: corregir el número de una factura ya sincronizada hace que el *upsert* no encuentre la
fila anterior e **inserte una nueva**. La vieja permanece. Looker Studio cuenta el gasto dos veces,
de forma permanente y silenciosa. Corregir el proveedor `P00000` cambia el RUC, con el mismo efecto.

### La decisión

La clave de idempotencia externa es **`FacturaId`**, el identificador subrogado, inmutable por
construcción. La identidad fiscal viaja en el *payload* como atributo, **nunca como clave**.

| Destino | Clave | Dónde vive |
|---|---|---|
| Google Sheets | `FacturaId` | Columna propia de la hoja, junto a `_Secuencia` (R2-C3) |
| Google Drive | `FacturaId` | `appProperties` de la carpeta, no su nombre |

El nombre de la carpeta de Drive **puede** incluir el número de comprobante, porque lo leen personas.
Lo que no puede es ser el criterio de búsqueda: "buscar antes de crear" consulta por `appProperties`,
que el usuario no ve y ninguna corrección modifica.

### Por qué

Es la regla general: **una clave de sincronización no puede ser un dato que el usuario corrige**. El
diseño ya tiene un identificador que cumple, y ya lo usa internamente.

### Consecuencias

- `FACTURA_CORREGIDA` puede cambiar el número, el RUC o el proveedor sin duplicar nada. El *upsert*
  encuentra la fila, la reescribe entera y actualiza la secuencia.
- La carpeta de Drive **no se renombra** al corregir el número, salvo que se decida hacerlo
  explícitamente. Si se renombra, sigue siendo la misma carpeta: la clave no cambió.
- Sheets gana dos columnas técnicas: `FacturaId` y `_Secuencia`.

### Hallazgos afectados

R2-C5 cerrado. Toca `TECH-DESIGN.md` (Flujo 5) y ADR 0004 (*payload* de los eventos).

---

## R2-C6 — El PRD contradice al diseño por escrito · CERRADO

### El problema, verificado

El PRD sigue siendo el documento contractual y hoy contradice al TDD en cinco puntos sustantivos.
Dos de ellos —el mapeo por producto y los tres reintentos— tienen ADR propio con contexto,
alternativas y costos. Los otros tres no: el cambio de tipo de cambio, que es **el de mayor impacto
económico de los cinco**, vive en una línea con un paréntesis dentro de ADR 0006.

"Confirmado" en el PRD significa que alguien lo decidió. Revertir una decisión confirmada sin dejar
rastro hace imposible, en la revisión del contador que `REGLAS.md` §12 deja pendiente, distinguir un
cambio deliberado de un error de transcripción.

### La decisión

**ADR 0018 nuevo + sección de reversiones al final del PRD.** El PRD no se reescribe: se le añade el
registro de lo que cambió y por qué.

`adrs/0018-tipo-de-cambio-aplicable.md` recoge las tres decisiones cambiarias que hoy no tienen ADR,
porque son la misma familia:

1. **Tipo de cambio venta**, con el fundamento normativo: una compra genera un **pasivo** en moneda
   extranjera, y los pasivos se convierten al tipo de cambio venta.
2. **Sin tipo de cambio no se abre la factura para edición** (`409`), en lugar del `0.00` con
   observación que pedía el PRD. Un asiento con TC `0.00` es basura contable; el caso real —que la
   SBS no publique— se cierra con la carga manual de `REGLAS.md` §6.
3. La **fecha** cuyo tipo de cambio se aplica: la de emisión del comprobante, y su congelamiento al
   confirmar.

El bloqueo de `P00000` **no entra en 0018**: pertenece a ADR 0006, donde ya está como invariante
global 4. Lo que falta ahí es declarar explícitamente que revierte al PRD, no la decisión.

Y en `PRD.md`, una sección nueva:

| # | El PRD decía | Vigente | Respaldo |
|---|---|---|---|
| 1 | Tipo de cambio **compra** de la fecha de emisión | Tipo de cambio **venta** | ADR 0018 |
| 2 | Sin TC del día se registra **0.00** con observación | La factura no se abre para edición; `409` | ADR 0018 |
| 3 | Asiento con `P00000`, corregido después | `409` al validar | ADR 0006 |
| 4 | Detalle mapeando cada producto del catálogo | El **motivo** determina la cuenta | ADR 0011 |
| 5 | Reintento **3 veces** para todo fallo | Tres clases de error | ADR 0010 |

### Por qué

Un PRD reescrito queda más limpio de leer y pierde exactamente lo que el hallazgo pide conservar: el
rastro de qué se decidió primero, qué cambió y con qué fundamento. La tabla de reversiones es el
artefacto que un contador o un auditor puede leer en un minuto.

### Consecuencias

- El PRD deja de contradecir al diseño **sin perder su historia**. Las cinco filas son el índice de
  las decisiones que hay que revalidar si cambia la premisa.
- Las reglas 1 y 2 de la tabla quedan además en la lista de `REGLAS.md` §12 —pendiente de
  ratificación formal por un contador—, que es donde ya estaban.
- Si el criterio correcto resultara ser el del PRD, la corrección **no es un ajuste de código**: es
  reprocesar todo asiento en moneda extranjera ya confirmado. Queda escrito en 0018 como su
  consecuencia principal.

### Hallazgos afectados

R2-C6 cerrado. Crea `adrs/0018-tipo-de-cambio-aplicable.md`, toca `PRD.md` (sección nueva) y ADR
0006 (declarar la reversión de `P00000`).

---

## R2-C7 — Respaldo sobre una base compartida · CERRADO con verificación pendiente

### El problema, verificado

ADR 0003 establece que las tablas maestras las mantiene **el sistema contable de la compañía**, en
esta misma base. ADR 0014 decide, sobre esa misma base, `FULL BACKUP` diario y `LOG BACKUP` cada 15
minutos con RPO de 15 minutos, y fue escrito como si la base fuera exclusiva de este proyecto.

Tres problemas que no considera:

1. **La cadena de log no se comparte.** Si el sistema contable ya toma sus propios `LOG BACKUP` a
   otro destino, las dos cadenas se intercalan y **ninguna restaura por sí sola**.
2. **El modelo de recuperación no es decisión de este proyecto.** `LOG BACKUP` exige modelo `FULL`.
3. **La restauración no es local.** No se puede restaurar "las tablas de este proyecto": un
   *point-in-time restore* **revierte también la contabilidad de la compañía**.

### La decisión

**La base es compartida.** Confirmado por el responsable, y coherente con la restricción vigente del
proyecto: todo se graba en la base asignada.

En consecuencia, **ADR 0014 se reescribe en términos de "qué añadimos al respaldo que ya existe"**,
no de "qué respaldo montamos". Su postura pasa a ser:

- **Este proyecto no define la política de respaldo de la instancia.** No fija el modelo de
  recuperación, no crea una cadena de `LOG BACKUP` propia y no cambia la frecuencia del `FULL`. Esas
  decisiones pertenecen a quien administra la instancia.
- **El RPO de 15 minutos deja de ser una decisión y pasa a ser un requisito** que se le traslada al
  administrador. Si la política vigente no lo alcanza, es una restricción del proyecto que hay que
  declarar, no un respaldo que este proyecto monte por su cuenta.
- **El volumen compartido de documentos sí es responsabilidad propia**, y ahí se conserva íntegro el
  orden de la ronda 1: **primero el volumen, después la base**. Una referencia colgante en la base a
  un archivo que no se copió es el modo de fallo que ese orden evita.
- **La restauración se declara como procedimiento de la compañía, no del proyecto.** ADR 0014 debe
  decir con todas sus letras que restaurar esta base a un punto en el tiempo **revierte también el
  sistema contable**, y que por tanto no existe una recuperación unilateral de este proyecto.

### Verificación pendiente, con tres preguntas concretas

Estas tres no se pueden decidir desde el diseño y bloquean la redacción final de ADR 0014:

| # | Pregunta a quien administra la instancia |
|---|---|
| 1 | ¿En qué **modelo de recuperación** está la base: `SIMPLE` o `FULL`? |
| 2 | ¿Existe ya una **cadena de `LOG BACKUP`**, a qué destino y con qué frecuencia? |
| 3 | ¿Cuál es el **RPO efectivo** hoy, y alcanza los 15 minutos que este proyecto necesita? |

Mientras no se respondan, ADR 0014 queda **condicionado** y así debe leerse. Es preferible a un plan
escrito con seguridad sobre una instancia que no controlamos.

### Consecuencias

- El respaldo deja de ser una fortaleza declarada del diseño y pasa a ser una **dependencia
  externa**. La frase de ADR 0014 —*"un respaldo que nunca se restauró no es un respaldo, es una
  suposición"*— sigue siendo cierta y ahora apunta a la prueba de restauración de la compañía.
- La prueba de restauración periódica que ADR 0014 exigía **no la puede ejecutar este proyecto** en
  un entorno productivo compartido. Se traslada a entorno de prueba, con copia restaurada aparte.
- Se cruza con R2-A12: si la base es compartida, el derecho a ejecutar DDL sobre ella y los permisos
  por usuario dejan de ser un detalle y pasan a ser la decisión que sostiene a ADR 0003 y ADR 0016.

### Hallazgos afectados

R2-C7 cerrado con verificación pendiente. Reescribe ADR 0014 y condiciona ADR 0016.

---

## R2-C8 — Estrategia de verificación · CERRADO

### El problema, verificado

El TDD dedica cien líneas a criterios de aceptación por flujo, y no hay **ni una sola decisión**
sobre cómo se verifican. ADR 0006 dice que las invariantes del bloque principal *"son tres caminos
que probar, no uno"*, y ahí se detiene. El TDD lista como riesgo abierto que *"convendrían pruebas
de contrato sobre esas tablas"* — un deseo, no una decisión.

Es desproporcionado con el resto: un sistema cuyo núcleo es un puñado de invariantes aritméticas
sobre dinero —con conversión de moneda, redondeo, percepción, notas de crédito parciales y
contabilidad por destino generada automáticamente— no tiene decidido cómo se comprueba que suma
bien. Y el mejor insumo de pruebas del proyecto ya está escrito y sin usar: `REGLAS.md` §10 trae
**cinco ejemplos numéricos completos y cuadrados**, y §7 define **siete invariantes comprobables**.

### La decisión

**ADR 0019 — Estrategia de verificación**, con tres niveles.

#### 1 · Núcleo contable, sin infraestructura

La generación del asiento y la evaluación de las invariantes viven en un **núcleo sin dependencias
de base de datos, HTTP ni reloj**. Recibe la factura, el plan de cuentas aplicable y el tipo de
cambio como datos de entrada, y devuelve el asiento o el rechazo.

Casos de referencia, ya escritos:

| Origen | Qué cubre |
|---|---|
| `REGLAS.md` §10, 5 ejemplos | Gravada con destino · boleta con IGV al costo · dólares con redondeo derivado · con percepción · nota de crédito |
| `REGLAS.md` §7, 7 invariantes | Cada una en sus dos caminos: acepta y rechaza |
| `REGLAS.md` §8, 8 reglas de rechazo | Una prueba por regla, con su salida esperada |

Faltan por escribir dos casos que la ronda 2 hizo aparecer: **nota de crédito sobre boleta** (R2-A13)
y **nota de crédito en moneda extranjera** (R2-C9).

#### 2 · Contrato de frontera, contra el esquema versionado

Pruebas sobre las **cinco tablas de frontera** —`OutboxEvent`, `CommandQueue`, `InboxEvent`,
`Procesamiento` y los datos extraídos— que se ejecutan contra el esquema aplicado por la herramienta
de ADR 0016, desde **ambos lados**: .NET escribe y Python lee, y a la inversa.

Es la única mitigación declarada del riesgo de divergencia de tipos que ADR 0002 registra y ADR 0016
dice cubrir. Sin esta prueba, esa mitigación es una afirmación.

#### 3 · Un extremo a extremo, sobre datos fijos

**Uno solo**, no una suite: correo con adjuntos → ingesta → procesamiento → promoción → validación →
evento en el outbox. Con un juego de correos de referencia fijo y comprobantes conocidos.

No verifica reglas contables —de eso se encarga el nivel 1— sino que **las piezas están conectadas**:
que el `InboxEvent` se consume, que la promoción crea la factura, que la validación emite el evento.
Es el único nivel que detecta un cableado roto.

### Por qué

Los tres niveles responden a tres riesgos distintos y ninguno cubre al otro. El nivel 1 protege el
dinero, el 2 protege la frontera entre dos runtimes que comparten esquema, el 3 protege el
cableado. Y el nivel 1 es el más barato de los tres: los casos ya están escritos y cuadrados.

### Consecuencias

- **La lógica contable no puede vivir en el controlador ni en el repositorio.** El nivel 1 obliga a
  una separación que hoy el diseño no exige explícitamente. Es una restricción de arquitectura, y
  ADR 0019 la impone.
- El nivel 3 exige entorno con base de datos y volumen. Al ser **uno solo**, el costo de
  mantenimiento se acota; convertirlo en una suite es lo que hay que evitar.
- El plan de cuentas real —1650 filas, 907 hojas de 6 dígitos— entra como **dato fijo de prueba**,
  no como consulta a la base. Un cambio en el catálogo externo no puede romper la suite.
- Los cinco ejemplos de `REGLAS.md` §10 pasan a ser **normativos**: si el código y el ejemplo
  discrepan, se corrige uno de los dos deliberadamente, nunca en silencio.

### Hallazgos afectados

R2-C8 cerrado. Crea `adrs/0019-estrategia-de-verificacion.md` y cierra el riesgo abierto del TDD
sobre pruebas de contrato.

---

## R2-C9 — Tipo de cambio de la nota de crédito · CERRADO

### El problema, verificado

`REGLAS.md` §6 fija **una sola** regla de conversión: se ancla `totalPEN` e `igvPEN` con el tipo de
cambio venta **de la fecha de emisión**, y se deriva la base. §5 define la nota de crédito como
**espejo** de la factura que modifica. Y ningún documento dice con qué tipo de cambio se convierte
la nota.

Aplicada la única regla escrita, la nota usa el TC de **su propia** fecha, que casi nunca es la de la
factura. Consecuencia aritmética: **una nota de crédito que anula el 100% de una factura en dólares
no deja el pasivo en cero**. Deja

```
residuo = totalOrig × (TCventa_NC − TCventa_factura)
```

repartido entre `421212`/`431212`, la cuenta de cargo heredada y —al invertirse el bloque destino—
entre `ctarefleja` y `ctapuente`. Con tres milésimas de movimiento sobre USD 10.000 son **S/ 30
colgados en una cuenta por pagar, por proveedor, para siempre**.

Ninguno de los tres mecanismos existentes lo atrapa: el cuadre se cumple porque el asiento de la
nota cuadra consigo mismo —el descuadre es **entre dos asientos** y no hay invariante que mire ese
par—; y `REGLAS.md` §1 deja la diferencia de cambio fuera de alcance porque *"nace al pagar o al
cierre"*, cuando este residuo **lo genera este sistema**, dentro del libro de compras, sin que
intervenga ningún pago.

### La decisión

**La nota de crédito hereda el tipo de cambio congelado de la factura referenciada.** No usa el de su
propia fecha de emisión.

```
Nota de crédito 07 sobre la factura F:

    TC aplicado = F.TipoCambio        (el congelado al confirmar F)
    NO el TC venta de la fecha de emisión de la NC
```

Una nota del 100% deja `421212` / `431212` en **0.00 exacto**. El residuo cambiario desaparece por
construcción, no por tolerancia.

### Por qué

Es lo coherente con llamarla **espejo**. La nota ya hereda el motivo y la cuenta de cargo por
decisión de §5: heredar también el tipo de cambio es la misma regla aplicada al tercer atributo, no
una excepción.

Y mantiene la diferencia de cambio fuera de alcance, que es lo que `REGLAS.md` §1 declara. La
alternativa —TC de la fecha propia— no es indefendible, pero arrastra consigo la línea de ajuste por
diferencia de cambio y su cuenta, es decir, reabre un alcance que el proyecto cerró
deliberadamente.

### Consecuencias

- `TipoCambio` de la nota de crédito **no se calcula**: se copia de la factura referenciada al
  crearse el borrador. La tabla de tipos de cambio no se consulta para el tipo `07`.
- La regla de rechazo por falta de tipo de cambio (`409`) **no aplica al tipo `07`**: la nota hereda
  uno que ya existe. Una nota emitida un día sin publicación de la SBS se registra sin problema.
- Se rompe la simetría con la declaración tributaria si la SUNAT exige el TC de la fecha propia.
  **Esto entra en `REGLAS.md` §12** como quinto punto pendiente de ratificación formal por un
  contador, junto a los cuatro que ya lista. Es una decisión de diseño tomada con fundamento, no una
  confirmación normativa.
- Aparece un caso de prueba nuevo para ADR 0019: **nota de crédito del 100% en moneda extranjera con
  TC distinto**, cuyo resultado esperado es saldo cero exacto.

### Hallazgos afectados

R2-C9 cerrado, y R2-C10 simplificado: con el mismo TC en ambos documentos, el tope acumulado da el
mismo resultado en soles o en moneda original. Toca `REGLAS.md` §5, §6, §7 y §12, y ADR 0006.

---

## R2-C10 — El tope de notas de crédito filtra por el estado equivocado · CERRADO sin consulta

Queda determinado por R2-C4 y R2-C9. Se decide directamente.

### El problema, verificado

`REGLAS.md` §7 lo enuncia como norma: *"La suma de las notas de crédito **vigentes** sobre una
factura no puede exceder su monto total... Una nota anulada **libera** su importe."* La consulta
escrita para evaluarla filtra por `Factura.Estado = 'VALIDADA'`.

Pero **la anulación se aplica al asiento, no a la factura** — lo dice este mismo documento en §C5 de
la ronda 1, al sacar `FACTURA_ANULADA` del catálogo de eventos. Una nota cuyo asiento se anuló
conserva `Estado = 'VALIDADA'` y **sigue sumando**. La capacidad nunca se libera. Implementada
literalmente, la regla rechaza notas de crédito legítimas con `409` sin que nadie entienda por qué.

Es la única consulta escrita de la única invariante que depende del estado de otras filas, y está
mal.

### La decisión

La consulta se une a `AsientoContable` y mira el **asiento vigente**, en el sentido que R2-C4 acaba
de definir:

```sql
-- Rechaza con 409 si el acumulado supera el total de la factura original
SELECT COALESCE(SUM(f.MontoTotal), 0)
  FROM Factura f
  JOIN AsientoContable a
    ON a.FacturaId = f.FacturaId
   AND a.Estado   <> 'ANULADO'          -- el asiento vigente
 WHERE f.FacturaReferenciaId = @facturaOriginalId
   AND f.Estado              = 'VALIDADA'
   AND f.FacturaId          <> @notaActual;
```

Se conserva `Factura.Estado` con sus tres valores. **No se le añade un estado que refleje la
anulación de su asiento**: eso contradiría §C5 de la ronda 1 y duplicaría una verdad que ya vive en
un solo sitio.

### La moneda del tope deja de importar

La ronda 1 dejó declarado como supuesto que el tope compara **monto total** y no base imponible, y la
revisión señaló que la moneda del campo cambiaba el resultado. **Con R2-C9 el problema desaparece**:
factura y notas comparten el tipo de cambio congelado, de modo que comparar en soles o en moneda
original da exactamente la misma proporción. El supuesto sobre *total frente a base* sigue vigente y
sigue pendiente de confirmación contable.

### Consecuencias

- La invariante *"una nota anulada libera su importe"* pasa a ser cierta, y lo es **por el mismo
  mecanismo** que R2-C4 introdujo. Las dos decisiones se sostienen mutuamente.
- Una nota de crédito cuyo asiento se anuló y se rehízo vuelve a contar, porque su asiento nuevo es
  vigente. Correcto: el documento fiscal sigue existiendo.
- La comprobación va **dentro de la transacción** que confirma la nota, igual que se decidió en la
  ronda 1.
- Caso de prueba para ADR 0019: anular el asiento de una nota parcial debe **liberar** capacidad y
  permitir una nota nueva que antes se rechazaba.

### Hallazgos afectados

R2-C10 cerrado. Toca `REGLAS.md` §7 y la consulta de §A4 de la ronda 1.

---

## R2-C11 — La factura mixta no es detectable · CERRADO

### El problema, verificado

`REGLAS.md` §8 lista ocho reglas de rechazo. Siete son comprobables con los datos que el sistema
tiene. La octava —*"factura con líneas gravadas y no gravadas mezcladas → fuera de alcance"*— no lo
es: `FacturaDetalle` se eliminó (ADR 0011) y `Afectacion` es **un único campo de cabecera** con tres
valores. Una factura mixta no tiene representación posible: el extractor elegirá uno de los tres, el
comprobante parecerá homogéneo y **pasará las ocho reglas**.

El modo de fallo es silencioso y va en la peor dirección: una mixta registrada como `GRAVADA` **toma
crédito fiscal sobre la porción que no lo genera**. Y `Afectacion` es justamente el campo que
gobierna si el IGV se desagrega o se incorpora al costo: el campo que no puede representar el caso es
el que decide la estructura del asiento.

### La decisión

**No se revive `FacturaDetalle`** — matar esas tablas sigue siendo la mejor decisión de ADR 0011. La
detección es mucho más barata.

El extractor calcula un indicador de tres estados que viaja en los datos extraídos:

```
AfectacionMixta  BIT NULL

    true   → el XML declara más de un código de afectación
             → rechazo al validar (409), fuera de alcance
    false  → el XML declara uno solo: afectación verificada
    NULL   → no hay XML: afectación NO verificada
```

El `NULL` **no bloquea**. Enciende el mismo tipo de indicador que ya usan el proveedor genérico, el
posible duplicado, los campos no extraídos y la fecha en domingo: *"afectación no verificada"*. El
asistente la confirma explícitamente antes de validar.

### Por qué

El XML UBL trae las líneas con su código de afectación, así que la comprobación es un recorrido del
documento que Python ya está parseando. Cuesta un campo.

Y el `NULL` visible es lo que evita que la regla mienta sobre su propia cobertura. Sobre un PDF
escaneado la mezcla **no es detectable de forma fiable por ningún medio**, y dejar pasar ese caso en
silencio —la opción de solo-XML— pone el modo de fallo exactamente donde el OCR ya es menos fiable.
Convertirlo en una confirmación explícita del asistente traslada la decisión a quien sí puede mirar
el documento.

### Consecuencias

- `Afectacion` sigue siendo **un campo de cabecera con tres valores**. El modelo no cambia; lo que
  cambia es que ahora hay una forma de saber si ese valor es de fiar.
- La regla 8 de `REGLAS.md` §8 se reescribe con su **cobertura declarada**: automática para los
  comprobantes con XML, por confirmación del asistente para los que solo traen PDF.
- El indicador de afectación no verificada entra en la lista de indicadores de `Factura` del TDD y en
  el chip derivado de la bandeja.
- La confirmación explícita **se registra**: es una afirmación del asistente sobre un documento
  fiscal, y `AuditoriaCorreccion` es donde vive ese rastro.
- Caso de prueba para ADR 0019: XML con dos códigos de afectación → `409`; sin XML → validación
  bloqueada hasta la confirmación.

### Hallazgos afectados

R2-C11 cerrado. Toca `REGLAS.md` §8, `TECH-DESIGN.md` (indicadores de `Factura`), ADR 0017 (el
extractor calcula el indicador) y ADR 0011 (declarar que no reabre el detalle).

---

## R2-A1 — Generación del correlativo y cambio de periodo · CERRADO

### El problema, verificado

ADR 0006 justifica asignar el correlativo **al confirmar** con un argumento correcto —*"si se
reservara antes, cada factura abandonada quemaría un número"*— y **no dice con qué se genera**. Con
`SEQUENCE` o `IDENTITY` una transacción revertida quema el número igual, y las invariantes de
confirmación se evalúan **dentro** de esa transacción: la vía de reversión tardía existe.

Segundo hueco: el periodo sale de `FechaContable`, que es editable y sobrevive a un `reabrir`.

### La decisión, primera mitad — tabla contador

El correlativo se genera con una **tabla contador**, con reinicio mensual controlado en la propia
tabla por año y mes, actualizada **dentro de la misma transacción** que confirma:

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

Si la transacción revierte, **el contador revierte con ella**. Es el único mecanismo que cumple la
promesa; `SEQUENCE` no lo hace por diseño, porque está pensada precisamente para no bloquear.

El reinicio es **por fila**: cada `(año, mes, origen)` arranca en cero al insertarse. No hay proceso
de cierre mensual que reinicie nada, ni tarea programada que pueda no ejecutarse.

**Costo aceptado:** serializa las confirmaciones del mismo periodo y origen. Con un solo usuario no
cuesta nada, y es el precio inherente de un correlativo sin huecos.

### La decisión, segunda mitad — cambio de periodo

Si un asiento reabierto cambia su `FechaContable` a otro mes, **devuelve su número y toma uno nuevo
de la serie del mes destino**.

### Consecuencia que hay que declarar

**El mes de origen queda con un hueco.** Es exactamente lo que ADR 0006 prometió no tener, así que la
promesa deja de ser absoluta y pasa a ser condicionada:

> El correlativo no tiene huecos por **facturas abandonadas ni por validaciones fallidas** —que era
> el riesgo que motivó asignarlo al confirmar—. Sí puede tenerlos por **traslado deliberado de un
> asiento a otro periodo**, que es un acto explícito del asistente, queda registrado en
> `AuditoriaCorreccion` con su motivo, y por tanto **es justificable en una revisión**.

Esa es la diferencia que sostiene la decisión: un hueco por accidente no se puede explicar; uno por
traslado deliberado sí, y tiene su rastro.

El número devuelto **no se reutiliza**. Reasignarlo a otro asiento sería peor que el hueco: dos
documentos distintos habrían llevado el mismo número en momentos distintos.

### Consecuencias

- `AuditoriaCorreccion` gana un caso explícito: **traslado de periodo**, con el número anterior, el
  nuevo y el motivo. Sin ese rastro el hueco es injustificable.
- Se cruza con el tope de reapertura de la ronda 1 (§C1/C2): trasladar a un periodo **cerrado** debe
  rechazarse, igual que reabrir en él.
- Caso de prueba para ADR 0019: validación que falla dentro de la transacción **no debe consumir**
  número; traslado de periodo **debe** dejar hueco en origen y número nuevo en destino.

### Hallazgos afectados

R2-A1 cerrado. Toca ADR 0006 (mecanismo, promesa condicionada y traslado de periodo) y
`TECH-DESIGN.md` (tabla `CorrelativoAsiento`).

---

## R2-A2 — Nota de crédito: reparto parcial y referencia externa · CERRADO

### Primera mitad — el reparto de una nota parcial

**El problema.** ADR 0006 establece que la nota *"hereda motivo y cuenta de la factura referenciada;
el asistente no elige"*, y ADR 0011 permite **dividir el cargo entre varias cuentas** del motivo.
Cuando la factura repartió en N cuentas y la nota es **parcial** —un descuento, una devolución de
parte de la mercadería—, "hereda la cuenta" no designa nada: no dice cuál de las N ni en qué
proporción, y el asistente tiene prohibido decidirlo.

**La decisión.** La nota reparte su base entre **las mismas N cuentas, en la misma proporción** que
la factura. El céntimo residual lo absorbe la cuenta de mayor importe.

```
Factura F, base 1000.00        Nota parcial del 40%, base 400.00
   631101   700.00  (70%)         631101   280.00  (70%)
   656101   300.00  (30%)         656101   120.00  (30%)
```

**Por qué.** Es el espejo literal: revierte cada cuenta en la medida en que se cargó. Es
determinista, no exige ninguna decisión del asistente y no contradice la regla de §5 —la sigue al
pie, extendiéndola de una cuenta a N.

**Costo declarado.** Si la devolución corresponde a **una** línea concreta —lo habitual cuando se
devuelve mercadería—, el reparto proporcional no representa el hecho económico exacto. Se acepta: el
asiento cuadra, el saldo por cuenta es correcto en el agregado, y la alternativa exige levantar la
prohibición de elegir y añadir una pantalla.

La regla de absorción del céntimo es la misma que ya se decidió en la ronda 1 (§A5) para la factura,
aplicada aquí. La suma de las líneas iguala `basePEN`, que es el valor derivado.

### Segunda mitad — notas contra facturas anteriores al sistema

**El problema.** `FacturaReferenciaId` es obligatorio para el tipo `07` y validar se rechaza con
`409` si la factura referenciada no existe. En el arranque, y durante meses, llegarán notas de
crédito contra facturas emitidas **antes de que el sistema existiera**. Hoy la única acción
disponible es descartarlas: **perder un documento fiscal real**.

**La decisión.** Se admite la **nota de crédito con referencia externa**:

```
Factura tipo 07
    FacturaReferenciaId   NULL
    EsReferenciaExterna   BIT
    RefExternaSerie       'F001'
    RefExternaNumero      '00123'
    RefExternaFecha       2026-05-14
```

Los tres campos de referencia salen del XML, que los trae siempre. Y como no hay factura de quien
heredar:

- **El asistente elige motivo y cuenta**, igual que en una factura normal. La prohibición de §5 se
  aplica cuando hay de quién heredar; aquí no lo hay.
- **No entra en el tope acumulado.** No existe la factura contra la que topar. El tope de §7 se
  evalúa solo sobre las notas con `FacturaReferenciaId` no nulo.
- **El tipo de cambio es el de su propia fecha de emisión.** R2-C9 hace que la nota herede el TC
  congelado de su factura; sin factura, se aplica la regla general de §6.

**Por qué.** Es el único camino que no pierde un documento fiscal ni exige una carga inicial de
facturas anteriores —que además rozaría la restricción vigente de que no hay migración—. Y el caso
no es solo de arranque: una factura registrada por otra vía puede recibir su nota aquí en cualquier
momento.

**Costo declarado.** La nota con referencia externa **no tiene control de tope**: nada impide
registrar notas por encima del total de una factura que el sistema no conoce. Es un límite
intrínseco, no un descuido, y hay que escribirlo. El indicador `EsReferenciaExterna` lo hace visible
en la bandeja para que esas notas se puedan revisar aparte.

### Consecuencias

- `FacturaReferenciaId` deja de ser **obligatorio** para el tipo `07`: lo obligatorio pasa a ser
  *"referencia interna o referencia externa, exactamente una de las dos"*.
- El indicador `EsReferenciaExterna` entra en la lista de indicadores de `Factura` y en el chip
  derivado de la bandeja.
- La precondición de nota de crédito de la ronda 1 (§A4) se reformula: las cuatro comprobaciones se
  aplican **solo a las notas con referencia interna**.
- Casos de prueba para ADR 0019: nota parcial sobre factura con reparto en N cuentas —incluido el
  céntimo residual— y nota con referencia externa, que debe validarse sin tope y con motivo elegido.

### Hallazgos afectados

R2-A2 cerrado. Toca `REGLAS.md` §5, §7 y §8, ADR 0006, ADR 0011 y `TECH-DESIGN.md` (modelo de
`Factura` e indicadores).

---

## R2-A3 / R2-A4 — Estado de las integraciones y vigilancia del worker · CERRADO

### El problema, verificado

**A3.** `DESIGN_BRIEF.md` especifica *"estado de conexión con Gmail, Drive y Google Sheets (conectado
/ con error)"* y `DESIGN.md` define la píldora como parte del vocabulario de estados. ADR 0008 expone
`POST /api/integraciones/{nombre}/sincronizar` y `POST /api/integraciones/google/reconectar`, pero
**ningún `GET`**. No hay tabla, endpoint ni campo que sostenga ese indicador: es el único elemento
del diseño de interfaz sin cobertura en el modelo de datos.

**A4.** El TDD acepta explícitamente como riesgo que el worker no tenga vigilancia: *"si el worker se
detiene o se cuelga, nadie avisa, porque el mecanismo de notificación vive dentro de él. Una bandeja
sin facturas nuevas es indistinguible de un día sin facturas."* Esa aceptación se tomó **antes** de
que ADR 0015 decidiera desplegar un agregador de logs *"con búsqueda, retención configurable y
alertas por patrón"*. **El precio de la mitigación cambió**, y la aceptación merece revisarse contra
el precio nuevo, no contra el viejo.

### La decisión

Una tabla de estado por integración, un `GET` que la expone, y una alerta por **ausencia** de latido.

```sql
CREATE TABLE EstadoIntegracion (
    Nombre         VARCHAR(20) NOT NULL PRIMARY KEY,  -- GMAIL|DRIVE|SHEETS|SBS|WORKER
    UltimoIntento  DATETIME2   NULL,
    UltimoExito    DATETIME2   NULL,
    UltimoError    NVARCHAR(500) NULL,
    FallosSeguidos INT         NOT NULL DEFAULT 0
);
```

```
GET /api/integraciones/estado
    → alimenta la píldora "Conectado / Con error" de la pantalla de Configuración
```

La píldora **se deriva**, no se almacena: `Con error` si `FallosSeguidos > 0` o si `UltimoExito` es
más viejo que el intervalo esperado de esa integración. Es el mismo criterio que el TDD ya aplica al
chip de la bandeja.

`WORKER` es una fila más, y su latido es lo que cierra A4: una alerta por patrón en el agregador de
ADR 0015 sobre *"`UltimoExito` de `WORKER` con más de 30 minutos"* avisa de que el worker se detuvo.
**La alerta no vive dentro del worker**, que era exactamente por qué el riesgo se había aceptado.

### Por qué

La aceptación de A4 fue legítima con la información de entonces: montar vigilancia costaba desplegar
algo. Hoy ese algo **ya está en el plan de despliegue**, con `CorrelationId` propagado a los tres
artefactos, y la alerta es una regla de patrón sobre datos que ya se escriben. El costo bajó de
"componente nuevo" a "una fila y una consulta".

Dos criterios de éxito del PRD dependen hoy de que alguien esté mirando: visibilidad en 15 minutos y
entrega de notificaciones ≥99%. Ninguno de los dos se sostiene sin esto.

### Consecuencias

- **`EstadoIntegracion` es tabla de publicación**, en el sentido de ADR 0003: la escriben varios
  orígenes —el worker de Python para `GMAIL`, `SBS` y `WORKER`; el consumidor del outbox para
  `DRIVE` y `SHEETS`— y el discriminador es `Nombre`. Hay que declararla en la partición o rompe la
  invariante.
- Se escribe **fuera** de la transacción de negocio. Es telemetría: que su escritura falle no puede
  tumbar una validación.
- El intervalo esperado por integración es **configuración**, no constante. Se decide junto con las
  frecuencias de sondeo, que ADR 0005 ya declara que hay que fijar conjuntamente.
- El riesgo del TDD *"nadie avisa si el worker se detiene"* se **retira de la lista de riesgos
  aceptados** y pasa a mitigado.
- La alerta por ausencia depende de que el agregador esté vivo. Es un riesgo residual y hay que
  nombrarlo: nada vigila al vigilante.

### Hallazgos afectados

R2-A3 y R2-A4 cerrados. Toca ADR 0008 (`GET` nuevo), ADR 0003 (clase de la tabla), ADR 0015 (regla
de alerta), `TECH-DESIGN.md` (modelo y lista de riesgos) y `DESIGN_BRIEF.md` (el indicador ya tiene
respaldo).

---

## R2-A5 — Adjuntos posteriores a la validación · CERRADO

### El problema, verificado

ADR 0013 introduce `AdjuntoManual` justificándolo con el caso borde del PRD —el correo *"que llega
sin OC o sin medios probatorios"*— y con el criterio de éxito del **100% de facturas validadas
archivadas con sus medios probatorios**. El empaquetado usa *"la lista completa de rutas"* que viaja
en el *payload* de `FACTURA_VALIDADA`, **congelada al validar**.

Pero ADR 0008 expone `POST /api/facturas/{id}/adjuntos` y `DELETE .../{adjuntoId}` **sin
restringirlos al borrador**, y ADR 0004 no tiene ningún evento de adjunto. Consecuencia: el medio
probatorio que llega tarde —**el escenario más probable, porque es justo el que motivó la
funcionalidad**— se sube al sistema y no se archiva nunca. Un adjunto eliminado después de validar
tampoco desaparece de Drive. El criterio del 100% se incumple en silencio.

### La decisión

**Quinto evento en el catálogo: `DOCUMENTACION_ACTUALIZADA`.**

Añadir y eliminar adjuntos siguen permitidos después de validar, y **cada cambio emite el evento**,
que vuelve a sincronizar la carpeta de Drive: añade lo nuevo y retira lo eliminado.

```
POST   /api/facturas/{id}/adjuntos            → permitido tras validar
DELETE /api/facturas/{id}/adjuntos/{adjuntoId} → permitido tras validar
                                     ambos → DOCUMENTACION_ACTUALIZADA
```

### Por qué

Las tres decisiones de esta ronda encajan aquí sin trabajo extra:

- **R2-C5** da la clave: la carpeta se encuentra por `FacturaId` en `appProperties`, que ninguna
  corrección modifica. El reempaquetado no crea una segunda carpeta.
- **R2-C3** da la guarda: el evento lleva su `Secuencia` y no se pisa con los demás de la factura.
- El *payload* autosuficiente que R2-C3 ya exige significa que el evento lleva **la lista completa**
  de rutas vigentes, no un delta. Reempaquetar es idempotente por construcción.

Cerrar los adjuntos al validar era más simple, pero deja sin salida el caso real: la guía de remisión
que llega dos días después obligaría a reabrir el asiento, es decir, a convertir *"subí una foto"* en
una **reapertura contable** con su motivo y su rastro. Es desproporcionado.

### Consecuencias

- El catálogo de eventos pasa de cuatro a cinco: `FACTURA_VALIDADA`, `FACTURA_CORREGIDA`,
  `ASIENTO_CORREGIDO`, `ASIENTO_ANULADO` y `DOCUMENTACION_ACTUALIZADA`.
- **La lista de rutas deja de ser un dato congelado de `FACTURA_VALIDADA`** y pasa a ser el estado
  vigente de los adjuntos en el momento de emitir cada evento. Es el cambio conceptual real de esta
  decisión.
- El evento **no toca Sheets**. Los adjuntos no son un dato del dashboard: solo se sincroniza Drive.
  La `Secuencia` es por agregado, así que un evento que no aplica a Sheets no debe avanzar la
  secuencia de esa hoja — se marca aplicado sin escribirla.
- **Eliminar un adjunto borra un archivo de Drive.** Es la única operación destructiva del flujo de
  publicación y merece quedar registrada en `AuditoriaCorreccion`, no solo en el log.
- Caso de prueba para ADR 0019: adjuntar después de validar debe dejar el archivo en Drive; eliminar
  después de validar debe quitarlo.

### Hallazgos afectados

R2-A5 cerrado. Toca ADR 0004 (catálogo), ADR 0013 (el empaquetado deja de ser único), ADR 0008
(declarar que los endpoints emiten evento) y `TECH-DESIGN.md` (Flujo 5).

---

## R2-A6 / R2-A14 — Qué se congela al confirmar · CERRADO

### El problema, verificado

ADR 0006 congela los **importes** al confirmar, con el argumento correcto de que un asiento
confirmado es *"un hecho, no una vista"*: **no referencias vivas a la factura**. Lo que no congela es
todo lo demás, y todo lo demás se resuelve contra catálogos **externos** sobre los que esta
aplicación solo tiene `SELECT`.

**A6 — las descripciones.** ADR 0003 ya declara el riesgo heredado: *"si el sistema contable elimina
o renumera una cuenta que un asiento ya usó, esta aplicación no puede impedirlo. El asiento conserva
el código; la descripción deja de resolver."* Lo nombra y lo deja abierto. Un libro de compras
impreso dos años después muestra códigos sin glosa — sobre datos que, por ADR 0014, ya no tienen
copia en ningún otro sistema.

**A14 — el mapeo de destino, que es más grave.** El bloque destino se deriva de `ctarefleja` y
`ctapuente`, **columnas del catálogo externo** `CuentaContable`. Dos huecos:

1. Si `ctarefleja` cambia entre la confirmación de la factura y la de su nota de crédito, **el
   espejo revierte contra una cuenta de destino distinta de la que cargó**. Las dos quedan con saldo
   y nada lo señala, porque cada asiento cuadra por separado.
2. Si una cuenta deja de declarar `ctarefleja`, un asiento reabierto y reconfirmado **pierde su
   bloque destino** sin que ninguna invariante lo note: la de §7 se enuncia *"para cada línea
   principal cuya cuenta declare `ctarefleja`"*, y si ya no lo declara, la comprobación se satisface
   vacía.

A6 afecta a lo que se muestra; A14, a lo que se contabiliza.

### La decisión

Se congela **todo lo que viene de fuera**, junto a los importes que ya se congelaban.

```
AsientoContableDetalle
    CuentaCodigo          '631101'                    (ya existía)
    CuentaDescripcion     'Transporte de carga'        NUEVO
    CtaReflejaCodigo      '791101'                     NUEVO
    CtaPuenteCodigo       '941101'                     NUEVO

AsientoContable
    MotivoDescripcion     'Flete de mercadería'        NUEVO
```

La nota de crédito **hereda las cuentas de destino congeladas** de la factura, igual que hereda el
motivo, la cuenta de cargo (R2-A2) y el tipo de cambio (R2-C9). No las vuelve a resolver.

### Por qué

Es el mismo argumento de ADR 0006 aplicado hasta el final. Congelar los importes y dejar vivo el
mapeo que los distribuye es una media medida: el asiento sigue dependiendo de un catálogo que otro
sistema puede cambiar mañana.

Con esto el asiento pasa a ser el **documento autocontenido** que ADR 0006 quiso conseguir. Se puede
imprimir, exportar y auditar sin consultar nada externo.

### Consecuencias

- **La invariante 2 de A14 se puede reformular.** Deja de depender del catálogo vivo: *"para cada
  línea principal con `CtaReflejaCodigo` no nulo"* se evalúa sobre datos propios del asiento, y un
  cambio externo ya no la satisface vacía.
- La resolución contra el catálogo externo ocurre **una sola vez**, al confirmar. Mientras el asiento
  está en `BORRADOR` sigue resolviéndose en vivo, que es lo correcto: ahí todavía se está decidiendo.
- **Cuatro columnas nuevas** de texto. El costo real es de esquema, no de rendimiento.
- Reabrir y reconfirmar **vuelve a congelar** con los valores del momento de la reconfirmación. Si el
  catálogo cambió, el asiento reconfirmado refleja el catálogo nuevo — y su nota de crédito heredará
  eso. Correcto: la reconfirmación es una decisión deliberada.
- Se cierra sin más trabajo el riesgo heredado que ADR 0003 dejaba abierto.
- Caso de prueba para ADR 0019: cambiar `ctarefleja` en el catálogo entre confirmar la factura y
  confirmar su nota debe producir un espejo que revierte contra **la misma** cuenta.

### Hallazgos afectados

R2-A6 y R2-A14 cerrados, y el riesgo abierto de ADR 0003 con ellos. Toca ADR 0006 (qué se congela),
ADR 0003 (riesgo cerrado), `REGLAS.md` §5 y §7, y `TECH-DESIGN.md` (modelo de asiento y detalle).

---

## R2-A7 — Concurrencia en los `PATCH` · CERRADO sin consulta

**El problema.** ADR 0008 define `PATCH /api/facturas/{id}` y `PATCH /api/asientos/{id}` sin `ETag`
ni `If-Match`, y el modelo no tiene `rowversion`. Con un solo usuario suena innecesario, pero **el
usuario no es el único escritor**: el servicio alojado de ADR 0005 promueve y escribe `Factura`, la
bandera de duplicado se recalcula al guardar y al validar, y `reabrir` / `anular` tocan el mismo
agregado. Dos pestañas abiertas bastan para perder una corrección sin que nada lo advierta — y toda
corrección perdida es además una fila que `AuditoriaCorreccion` registrará como válida.

**La decisión.** `rowversion` en `Factura` y en `AsientoContable`, expuesto como `ETag` y exigido con
`If-Match` en los dos `PATCH`. Discrepancia → `412 Precondition Failed`.

```sql
ALTER TABLE Factura         ADD Version ROWVERSION;
ALTER TABLE AsientoContable ADD Version ROWVERSION;
```

**Por qué.** Es una columna y una cabecera. El costo de añadirlo ahora es despreciable; el de
añadirlo después obliga a tocar todos los `PATCH`, su cliente y sus pruebas. Y el argumento de "un
solo usuario" es falso desde ADR 0005: hay un escritor de fondo.

**Consecuencias.** El `412` entra en la tabla de respuestas de ADR 0008, junto al `409` y al `422`.
Angular debe distinguirlo: `409` significa "tu dato viola una regla", `412` significa "alguien más lo
cambió, recarga". Son mensajes distintos para el asistente.

---

## R2-A9 — La métrica de precisión reporta de más · CERRADO sin consulta

**El problema.** Dos sesgos, ninguno declarado en ADR 0017:

1. **Mezcla dos poblaciones con riesgo opuesto.** Con XML como fuente prioritaria, los campos de un
   comprobante electrónico son exactos por construcción: esa población puntúa ≈100%. El riesgo real
   —el PDF escaneado sin XML— queda diluido. Si el 80% de las facturas trae XML, la métrica global
   supera el 90% **aunque el OCR acierte poco más de la mitad**.
2. **Solo cuenta los errores que el asistente notó.** La medición compara los datos extraídos contra
   la factura ya corregida; un campo mal extraído y no advertido cuenta como acierto. El sesgo apunta
   siempre hacia arriba.

**La decisión.** La métrica se reporta **partida por fuente**: precisión con XML y precisión sin XML,
por separado y siempre. El agregado puede mostrarse, pero nunca solo.

El criterio de éxito del ≥90% se aplica a la partición **sin XML**, que es la única donde la cifra
dice algo accionable. Con XML, el objetivo no es 90%: es 100%, y menos de eso es un defecto de
parseo, no de extracción.

**Sobre el segundo sesgo.** No se corrige con una métrica: se corrige con la prueba previa sobre
facturas reales que ADR 0017 ya reclama. Se deja **declarado en el propio ADR** que la cifra es una
cota superior, no una medición.

**Consecuencias.** ADR 0017 gana la partición y la declaración del sesgo. `DatosExtraidos` debe
registrar **de qué fuente salió cada campo** —XML o PDF— para poder partirla; hoy se sabe del
documento, no del campo.

---

## R2-A11 — `DIFERIBLE` no notifica nada · CERRADO sin consulta

**El problema.** El PRD dice: *"agotados los reintentos, la notificación se envía en un máximo de 5
minutos"*. Para un error `DIFERIBLE` los reintentos **no se agotan**: se reprograman a la apertura de
la ventana de cuota, que en una cuota diaria es al día siguiente. **El estado que más tiempo mantiene
el sistema degradado es el único que no dispara ninguna notificación.**

Combinado con el riesgo del worker, el resultado es el mismo modo de fallo: nada llega, nada avisa.

**La decisión.** Se cambia el disparador: se notifica **al entrar** en `DIFERIBLE`, no al agotar. Una
sola vez por incidencia, no en cada reintento.

**Por qué.** El criterio del PRD está redactado para un error que se reintenta y falla. `DIFERIBLE`
no es eso: es un error que **se sabe** que no se va a resolver pronto. Esperar a que se agote algo
que no se agota es no notificar nunca.

**Consecuencias.** ADR 0010 gana la regla de notificación por clase: `TRANSITORIO` notifica al
agotar, `PERMANENTE` notifica de inmediato, `DIFERIBLE` notifica al entrar, `OBSOLETO` —la clase que
introduce R2-C3— **no notifica nunca**, porque no es un error.

---

## R2-A13 — Nota de crédito sobre boleta · CERRADO sin consulta

**El problema.** `REGLAS.md` §5 define **un solo** asiento de nota de crédito, siempre con tres
líneas y `401111` al Haber, y §7 lo convierte en invariante. Pero una boleta `03` —y una factura
`EXONERADA` o `INAFECTA`— se registró con **dos** líneas y el IGV incorporado al costo, por decisión
del propio §5. Su nota de crédito tiene que ser el espejo de **eso**. Ni §5 ni §7 lo contemplan, y §8
no prohíbe una nota sobre una boleta.

Quien implemente §7 literalmente hará una de dos cosas, ambas malas: **rechazar una nota de crédito
legítima**, o **generar una línea de IGV que revierte un crédito fiscal que nunca se tomó**.

**La decisión.** El criterio ya estaba tomado —*el espejo hereda la estructura del documento que
rectifica*—; solo faltaba escribirlo. §5 gana un segundo bloque y §7 una fila más:

| Documento rectificado | Estructura de la nota |
|---|---|
| Factura `01` `GRAVADA` | Tres líneas. Cargos al Haber = base · `401111` al Haber = IGV · proveedor al Debe = total |
| Boleta `03`, o factura `EXONERADA` / `INAFECTA` | **Dos líneas.** Cargos al Haber = **total** · proveedor al Debe = total. **Sin `401111`** |

Y el bloque destino se invierte en los dos casos, con las cuentas congeladas que introduce R2-A14.

**Consecuencias.** La invariante de §7 para el tipo `07` deja de ser una y pasa a ser dos, elegidas
por la afectación del documento rectificado — que ahora viaja congelada en el asiento de la factura,
no se vuelve a resolver. Caso de prueba nuevo para ADR 0019.

---

## R2-A8 — Límite de intentos y recuperación de contraseña · CERRADO

**El problema.** ADR 0007 decide bien lo que decide —sesión de servidor, `HttpOnly`, `SameSite=Lax`,
prefijo `__Host-`, Argon2id con sal, mensajes que no revelan si el usuario existe— y es honesto sobre
su límite. Faltan dos piezas del mismo tamaño: **sin límite de intentos** —ADR 0012 menciona los
límites de tasa del proxy como ventaja, pero ningún ADR decide configurarlos, y un formulario de
login sin freno es exactamente donde la elección de Argon2id deja de importar— y **sin camino de
recuperación**: un usuario, ningún rol de administrador, ninguna pantalla. Una contraseña olvidada se
resolvería con un `UPDATE` a mano sobre la base de la contabilidad de la compañía.

**La decisión.**

*Bloqueo temporal, en la aplicación:*

```sql
ALTER TABLE Usuario
    ADD IntentosFallidos INT       NOT NULL DEFAULT 0,
        BloqueadoHasta   DATETIME2 NULL;
```

Cinco fallos consecutivos: bloqueo de 15 minutos, creciente en bloqueos sucesivos. Un inicio de
sesión correcto pone el contador a cero. El mensaje de bloqueo **no revela** si el usuario existe,
igual que el de credenciales.

*Restablecimiento, como procedimiento operativo escrito:* lo ejecuta el administrador de la
instancia, mediante un **comando de la propia aplicación** —que aplica la misma derivación con sal—,
nunca con un `UPDATE` a mano.

**Por qué.** El límite va en la aplicación y no en el proxy por dos razones: no depende de la
topología —funciona igual en desarrollo, donde no hay proxy— y **el proxy no distingue usuarios**,
solo direcciones. Un bloqueo por usuario es el control correcto para un sistema de un usuario.

Y una pantalla de restablecimiento exige un segundo canal —correo, teléfono— que el proyecto no
tiene. Un procedimiento escrito es honesto sobre lo que realmente hay.

**Consecuencias.** ADR 0007 gana las dos secciones. El procedimiento de restablecimiento entra en la
documentación de operación, junto al de respaldo. El comando de la aplicación hay que construirlo: es
la única funcionalidad de administración del sistema.

---

## R2-A10 — Carga inicial de `SugerenciaCuenta` · CERRADO

**El problema.** ADR 0011 reconoce el costo: *"la sugerencia no generaliza a proveedores nuevos"*. En
el arranque **todas** las facturas caen al tercer escalón —"la primera candidata del motivo"—, que
para un motivo con 34 candidatas es prácticamente arbitrario. El criterio de menos de 5 minutos por
factura está en su peor momento **justo cuando se forma la confianza del usuario**. Y el insumo para
evitarlo ya existe: los asientos históricos de la compañía viven en la misma base.

**La decisión.** Un proceso de arranque, ejecutado una vez al desplegar, cuenta `(proveedor, cuenta)`
sobre los asientos históricos y siembra `SugerenciaCuenta`:

```sql
INSERT INTO fact.SugerenciaCuenta (ProveedorId, CuentaCodigo, Veces)
SELECT d.ProveedorId, d.CuentaCodigo, COUNT(*)
  FROM <asientos históricos del sistema contable> d
 WHERE d.Fecha >= @desde
 GROUP BY d.ProveedorId, d.CuentaCodigo;
```

**Esto no es migración de datos.** Es un `SELECT` sobre el histórico y un `INSERT` en una tabla
propia: no mueve, no transforma y no toca nada del sistema contable. La restricción vigente del
proyecto se respeta.

**Por qué.** No cambia el mecanismo ni pierde explicabilidad: el fundamento que se muestra al usuario
sigue siendo un número —*"esta cuenta se usó 47 veces con este proveedor"*—, solo que ahora ese
número existe desde la primera factura.

**Consecuencias.**

- La ventana `@desde` es una decisión: demasiado histórico arrastra un plan de cuentas anterior;
  demasiado poco no siembra nada. Doce meses es el punto de partida razonable, y hay que **verificar
  que el plan de cuentas no cambió** en ese periodo.
- Las cuentas históricas que **ya no existen** en el plan actual deben excluirse en la siembra, o la
  cascada sugerirá una cuenta imposible.
- El proceso es **idempotente y repetible**: ejecutarlo dos veces no debe duplicar contadores.
- La calidad de la siembra hereda la calidad del histórico. Si el histórico tiene criterios
  inconsistentes, la sugerencia los reproduce. Es aceptable: el asistente corrige y el contador
  vuelve a aprender.

---

## R2-A12 — Permisos de base de datos y derechos DDL · CERRADO

**El problema.** ADR 0003 apoya su propuesta de valor en que la partición es *"implementable en el
motor"* y que las cuatro clases *"permiten refuerzo real con permisos por usuario de base de datos"*.
ADR 0016 versiona el esquema con una herramienta que aplica DDL en el despliegue. **Ninguno de los
dos decide nada**: no hay usuarios, ni matriz de permisos, ni los permisos entran en el SQL
versionado. La propiedad más fuerte de ADR 0003 —*"nadie escribe una tabla externa"*— queda sostenida
por convención, que es exactamente aquello sobre lo que ADR 0003 dice mejorar.

Y con la base compartida (R2-C7), la pregunta previa es real: **¿este proyecto tiene derecho a
ejecutar DDL ahí?**

**La decisión.** Esquema propio, dos usuarios, y los `GRANT` en el SQL versionado.

```sql
CREATE SCHEMA fact;   -- todos los objetos de este proyecto
```

| Usuario | Escribe y lee | Solo lee | Sin acceso |
|---|---|---|---|
| `usr_api` (.NET) | `fact.Factura`, `fact.AsientoContable`, `fact.AsientoContableDetalle`, `fact.OutboxEvent`, `fact.InboxEvent`, `fact.CommandQueue`, satélites, `fact.CorrelativoAsiento` | `dbo.Proveedor`, `dbo.CuentaContable`, `dbo.Motivo`, `dbo.Origen` | `fact.Procesamiento` |
| `usr_worker` (Python) | `fact.Procesamiento`, `fact.InboxEvent`, `fact.CommandQueue` | `dbo.Proveedor`, `dbo.CuentaContable`, `fact.OutboxEvent` | `fact.Factura`, `fact.AsientoContable*` |

Los `GRANT` viajan en el SQL versionado como cualquier otro cambio de esquema: se revisan, se aplican
en orden y se reproducen en cada entorno. La matriz **es** la partición de ADR 0003 expresada en el
motor.

**Por qué.** El esquema propio hace evidente qué objeto es de quién dentro de una base compartida, y
aísla el DDL: la herramienta de ADR 0016 opera sobre `fact` y nunca sobre `dbo`. Eso convierte la
pregunta del derecho a DDL en una mucho más fácil de conceder.

**Consecuencias.**

- **`usr_api` no tiene acceso a `fact.Procesamiento`.** La invariante que R2-C2 dejó cerrada por
  diseño queda además **impuesta por el motor**: aunque alguien escriba ese `SELECT`, falla.
- Las tablas externas se referencian con su esquema real (`dbo`), lo que las hace visualmente
  distintas en cada consulta. La clase "externa" deja de ser una nota del ADR y se lee en el código.
- **Cuarta premisa a verificar** con quien administra la instancia, junto a las tres de R2-C7: ¿se
  puede crear un esquema y ejecutar DDL sobre él? Si el proveedor del sistema contable condiciona el
  soporte por objetos de terceros, la alternativa escrita es que el DDL lo ejecute el administrador
  tras revisar el SQL, sin cambiar nada más de esta decisión.
- El despliegue necesita **credenciales separadas** por componente, que ADR 0015 ya cubre con el
  gestor de secretos.

---

## R2-A15 — La premisa del alta inmediata de proveedores · CERRADO

**El problema.** El bloqueo de `P00000` está decidido y bien razonado: `421211` es una cuenta por pagar
**por proveedor**, y un saldo acumulado contra "Varios" no se puede conciliar ni pagar porque no se
sabe a quién se le debe. Eso no se discute.

Lo que sobrevive es un **supuesto no verificado**. ADR 0003 descarta replicar los datos maestros con
este argumento: *"el asistente registra el proveedor en el otro sistema y vuelve **de inmediato** a
seleccionarlo"*. Ningún documento del proyecto establece que el asistente contable **tenga permiso de
alta** en el sistema contable de la compañía, ni que ese alta sea inmediato. Si la hace otra persona o
pasa por una aprobación, la factura queda en `PENDIENTE_VALIDACION` por tiempo indefinido, sin ningún
criterio de aceptación que cubra esa espera — y la premisa que sostiene el descarte de la replicación
se cae.

**La decisión.** El bloqueo se mantiene. La premisa se **escribe como tal** y se le da plan de
contingencia.

*Premisas externas a verificar*, que ahora son cinco y viven juntas:

| # | Premisa | Dónde se apoya |
|---|---|---|
| 1 | Modelo de recuperación de la base | ADR 0014 |
| 2 | Cadena de `LOG BACKUP` existente y su destino | ADR 0014 |
| 3 | RPO efectivo de la instancia | ADR 0014 |
| 4 | Derecho a crear esquema y ejecutar DDL | ADR 0016, ADR 0003 |
| 5 | **El asistente da de alta proveedores, y el alta es inmediata** | ADR 0003, ADR 0006 |

*Si la premisa 5 resulta falsa*, el diseño no cambia de criterio pero sí gana dos piezas:

- Un indicador **"esperando alta de proveedor"** en la factura, distinto del genérico de proveedor
  `P00000`, para que esas facturas se puedan ver y contar aparte.
- Un **criterio de aceptación para la espera** —cuánto tiempo es tolerable— que hoy no existe en
  ninguna parte, y sin el cual nadie sabe si el sistema está funcionando mal o la compañía está
  tardando.

**Por qué.** Es la diferencia entre un supuesto y un supuesto declarado. El primero se descubre el día
que falla; el segundo tiene una pregunta escrita y una respuesta preparada.

**Consecuencias.** ADR 0003 y ADR 0006 declaran la premisa en vez de darla por buena. Las cinco
premisas se agrupan en un solo sitio del TDD, porque las cuatro primeras y esta última son la misma
clase de riesgo: **supuestos sobre lo que hay fuera del proyecto**.

---

## R2-S1 a R2-S4 · CERRADAS sin consulta

Las cuatro sugerencias son correcciones de precisión, no decisiones. Se resuelven directamente.

### S1 — Let's Encrypt no puede emitir para `facturas.empresa.local`

ADR 0012 deja pendiente el origen del certificado y enumera tres opciones: *"autoridad interna,
Let's Encrypt o comprado"*. Con el host que el propio ADR fija, **solo la primera es viable**: Let's
Encrypt exige un dominio público validable, y `.local` está reservado para mDNS por RFC 6762, de modo
que ni Let's Encrypt ni una autoridad comercial pueden emitir para él.

La lista se corrige. La elección real son dos opciones, no tres:

| Opción | Qué implica |
|---|---|
| **Autoridad interna** | Certificado propio para `facturas.empresa.local`. Exige distribuir la raíz a los equipos que accedan. Es lo coherente con el host actual. |
| **Dominio público con DNS interno** | Cambiar el host a un subdominio real de la compañía, resuelto internamente. Habilita Let's Encrypt o un certificado comprado, sin distribuir nada. |

Se corrige la lista para que la decisión pendiente no arranque desde una opción falsa. La elección
entre las dos sigue abierta y depende de si la compañía tiene un dominio público disponible.

### S2 — La alternativa PostgreSQL de ADR 0002 dejó de ser una opción real

ADR 0002 descarta PostgreSQL *"por decisión de plataforma de la organización"*. Después, ADR 0003
revisión 3 descubre que los datos maestros los mantiene el sistema contable **en esta misma base**,
que es SQL Server — y R2-C7 lo confirma como base compartida.

Desde ese momento PostgreSQL no era una alternativa descartada por preferencia sino una
**imposibilidad**: el diseño lee las tablas maestras con `SELECT` directo, sin réplica ni copia.

Se añade una nota en la sección de alternativas de ADR 0002 dejando constancia del cambio de premisa.
No cambia la decisión; cambia su fundamento, y eso importa si alguien la revisa en dos años.

### S3 — La consulta de Gmail que evita el reproceso

ADR 0017 decide que el worker aplica una etiqueta propia al correo ya ingestado y que **nunca borra**
—decisión correcta y bien argumentada—. Lo que no dice es cómo se acota la consulta de sondeo. Si es
solo por la etiqueta de origen, cada ciclo relee todo el histórico y la idempotencia recae
íntegramente en el hash del contenido: funciona, pero crece sin límite.

La consulta efectiva queda escrita:

```
label:<etiqueta-origen>  -label:<etiqueta-procesado>  after:<fecha-inicio-configurada>
```

Los tres términos son **configurables**, coherentes con la decisión de ADR 0017 de no fijar nada en
el código. `after:` acota el arranque y evita que la primera ejecución arrastre años de correo.

Es donde se concilia la frecuencia de sondeo con la cuota de la API de Gmail que el TDD deja como
riesgo abierto: la consulta acotada es lo que hace que ese riesgo sea manejable.

### S4 — "La primera candidata del motivo" no tiene un orden definido

La cascada de `REGLAS.md` §3 termina en *"si tampoco hay historial, la primera candidata del
motivo"*. Las candidatas se obtienen resolviendo prefijos contra las 907 hojas de 6 dígitos, y el
propio §3 muestra que el motivo 70 tiene **34** y el motivo 6 tiene **20**. "La primera" sin
`ORDER BY` es la que devuelva el motor, y eso cambia con un índice nuevo o un plan distinto.

Se fija: **`ORDER BY CuentaCodigo`**, ascendente.

No es grave —la sugerencia nunca decide sola— pero produce un comportamiento que parece un error
cuando la misma pantalla propone cuentas distintas en dos días. Con R2-A10 este escalón además se
alcanza mucho menos, porque la siembra desde el histórico resuelve la mayoría de los casos antes.

### Residuo menor — los bloques de respuesta vacíos

`PREGUNTAS-CONTABLES.md` tiene los diecisiete bloques `**Respuesta:**` vacíos. Las respuestas viven en
este documento y en `REGLAS.md`, pero quien abra el cuestionario primero concluirá que nada se
respondió.

Se añade un enlace en la cabecera de `PREGUNTAS-CONTABLES.md` que apunte a dónde están las
respuestas. Una línea.

---

## Estado de los hallazgos de la ronda 2

### Críticos

| # | Hallazgo | Estado | Decisión |
|---|---|---|---|
| C1 | Índice único contra flujo de duplicados | **Cerrado** | Índice de detección; unicidad al validar (`409`) |
| C2 | Idempotencia en tabla privada de Python | **Cerrado** | `UQ_Factura_Procesamiento` + marca de consumo en `InboxEvent` |
| C3 | Orden por agregado contra `DIFERIBLE` | **Cerrado** | `Secuencia` por agregado + guarda de obsolescencia; clase `OBSOLETO` |
| C4 | `reactivar` sin transición ni evento | **Cerrado** | `ANULADO` terminal; asiento vigente por factura; endpoint retirado |
| C5 | Clave de sincronización mutable | **Cerrado** | `FacturaId`; Drive por `appProperties` |
| C6 | El PRD contradice al diseño | **Cerrado** | ADR 0018 + tabla de reversiones en el PRD |
| C7 | Respaldo sobre base compartida | **Cerrado con verificación** | ADR 0014 reescrito; tres premisas a verificar |
| C8 | Sin estrategia de pruebas | **Cerrado** | ADR 0019: núcleo puro + contrato + un extremo a extremo |
| C9 | TC de la nota de crédito | **Cerrado** | Hereda el TC congelado de la factura |
| C10 | El tope filtra por el estado equivocado | **Cerrado** | `JOIN` al asiento vigente |
| C11 | La factura mixta no es detectable | **Cerrado** | `AfectacionMixta` desde XML; `NULL` enciende indicador |

### Advertencias

| # | Hallazgo | Estado | Decisión |
|---|---|---|---|
| A1 | Correlativo sin mecanismo y cambio de periodo | **Cerrado** | Tabla contador con reinicio mensual; traslado deja hueco justificable |
| A2 | Nota de crédito: reparto parcial y arranque | **Cerrado** | Reparto proporcional; nota con referencia externa |
| A3 | Estado de conexión sin respaldo en el modelo | **Cerrado** | Tabla `EstadoIntegracion` + `GET` |
| A4 | Worker sin vigilancia | **Cerrado** | Alerta por ausencia de latido en el agregador de ADR 0015 |
| A5 | Adjuntos posteriores a la validación | **Cerrado** | Evento `DOCUMENTACION_ACTUALIZADA` |
| A6 | Descripciones sin congelar | **Cerrado** | Se congelan descripciones de cuenta y motivo |
| A7 | `PATCH` sin control de concurrencia | **Cerrado** | `rowversion` + `If-Match`; `412` |
| A8 | Sin límite de intentos ni recuperación | **Cerrado** | Bloqueo temporal en la app + procedimiento operativo |
| A9 | Métrica de precisión sesgada | **Cerrado** | Se reporta partida por fuente; el ≥90% aplica sin XML |
| A10 | `SugerenciaCuenta` arranca vacía | **Cerrado** | Siembra desde el histórico de la compañía |
| A11 | `DIFERIBLE` no notifica | **Cerrado** | Se notifica al entrar, no al agotar |
| A12 | Permisos y derechos DDL sin decidir | **Cerrado con verificación** | Esquema `fact` + dos usuarios; `GRANT` versionados |
| A13 | Nota de crédito sobre boleta | **Cerrado** | Dos líneas, sin `401111` |
| A14 | `ctarefleja` sin congelar | **Cerrado** | Se congelan `CtaReflejaCodigo` y `CtaPuenteCodigo` |
| A15 | Premisa del alta inmediata de proveedores | **Cerrado** | Premisa declarada + plan si resulta falsa |

### Sugerencias

| # | Hallazgo | Estado |
|---|---|---|
| S1 | Let's Encrypt y `.local` | **Cerrada.** Lista corregida a dos opciones reales |
| S2 | PostgreSQL dejó de ser alternativa | **Cerrada.** Nota en ADR 0002 |
| S3 | Consulta de Gmail sin especificar | **Cerrada.** Consulta escrita y configurable |
| S4 | "La primera candidata" sin orden | **Cerrada.** `ORDER BY CuentaCodigo` |

---

## Lo que esta ronda añade al proyecto

**Dos ADRs nuevos:**

| ADR | Título | Origen |
|---|---|---|
| 0018 | Tipo de cambio aplicable a la conversión | R2-C6 |
| 0019 | Estrategia de verificación | R2-C8 |

**Cinco tablas o columnas nuevas de esquema:**

| Objeto | Origen |
|---|---|
| `Factura.ProcesamientoId` + índice único | R2-C2 |
| `OutboxEvent.Secuencia` | R2-C3 |
| `CorrelativoAsiento` | R2-A1 |
| `EstadoIntegracion` | R2-A3 / R2-A4 |
| `Factura.Version`, `AsientoContable.Version` (`rowversion`) | R2-A7 |

Más: los campos de referencia externa de la nota de crédito (R2-A2), el indicador
`AfectacionMixta` (R2-C11), las cuatro columnas congeladas del asiento (R2-A6 / R2-A14), el índice
`UQ_Asiento_Vigente` (R2-C4) y las dos columnas de bloqueo de `Usuario` (R2-A8).

**Un cambio de cardinalidad:** `AsientoContable` deja de ser 1:1 con `Factura` y pasa a 1:N con un
único vigente (R2-C4). Es el cambio de modelo más profundo de la ronda.

**Un evento nuevo:** `DOCUMENTACION_ACTUALIZADA` (R2-A5). El catálogo pasa de cuatro a cinco.

**Una clase terminal nueva:** `OBSOLETO` (R2-C3). Las clases de error pasan de tres a cuatro.

**Cinco premisas externas declaradas** (R2-C7, R2-A12, R2-A15), agrupadas en un solo sitio del TDD
porque son la misma clase de riesgo.

---

## Decisiones que se reforzaron entre sí

Tres cruces que el informe no anticipaba y que aparecieron al resolver los hallazgos en orden:

1. **R2-C4 resolvió la mitad de R2-C10.** El concepto de *asiento vigente*, introducido para que
   anular no dejara la factura sin salida, es exactamente lo que la consulta del tope necesitaba para
   distinguir una nota anulada de una viva.
2. **R2-C9 disolvió la ambigüedad de moneda de R2-C10.** Con factura y nota compartiendo el tipo de
   cambio congelado, comparar el tope en soles o en moneda original da el mismo resultado. La
   pregunta dejó de existir en vez de responderse.
3. **R2-C3 y R2-C5 habilitaron R2-A5 sin trabajo extra.** El evento de documentación necesita
   encontrar la carpeta correcta —que da la clave inmutable— y no pisarse con los demás eventos de la
   factura —que da la guarda de obsolescencia—. Ambas piezas ya estaban puestas.

Y una tensión que quedó declarada en vez de resuelta: **R2-A1** obliga a retirar la promesa absoluta
de "correlativo sin huecos" de ADR 0006. Sigue sin huecos por accidente; puede tenerlos por traslado
deliberado de periodo, que es un acto explícito y con rastro. La promesa se vuelve condicionada, y
eso hay que escribirlo en el ADR.

---

## Lo que sigue pendiente después de esta ronda

| # | Qué falta | Bloquea |
|---|---|---|
| 1 | Las cinco premisas externas (R2-C7, R2-A12, R2-A15) | La redacción final de ADR 0014 y ADR 0016 |
| 2 | `REGLAS.md` §12 — ratificación formal de un contador | Ahora son **seis** puntos: los cuatro originales, más el TC de la nota de crédito (R2-C9) y la estructura de la nota sobre boleta (R2-A13) |
| 3 | Propagación de estas 30 decisiones a TDD, ADRs, `REGLAS.md` y `PRD.md` | La coherencia del corpus |

---

## Precisiones acordadas antes de la propagación

Cuatro puntos que quedaron abiertos al cerrar los hallazgos y se resolvieron antes de escribir.

### Marca de consumo de `InboxEvent` (pendiente de R2-C2)

R2-C2 dejó dicho que el índice único cubre *"no crear dos facturas"* pero no *"no reprocesar un
evento que decidí no promover"*. La marca registra **qué se decidió**, no solo que se consumió:

```sql
ALTER TABLE InboxEvent
    ADD EstadoConsumo  VARCHAR(12) NOT NULL DEFAULT 'PENDIENTE',  -- PENDIENTE|PROMOVIDO|DESCARTADO
        ConsumidoEn    DATETIME2     NULL,
        FacturaId      BIGINT        NULL,   -- si PROMOVIDO
        MotivoDescarte NVARCHAR(200) NULL;   -- si DESCARTADO
```

**Por qué el estado y no una marca de tiempo.** Permite responder *"cuántos documentos procesados no
llegaron a factura, y por qué"*, que hoy no se puede contestar desde ninguna tabla y es exactamente
la métrica que le interesa al operador. ADR 0005 ya contempla la decisión de no promover; hasta
ahora no la persistía en ninguna parte.

`InboxEvent` es tabla de contrato, coescribible por diseño: la escritura de .NET no rompe la
partición.

### Corrección a la matriz de permisos de R2-A12

La matriz daba a `usr_worker` **solo lectura** sobre `OutboxEvent`. Es incorrecto:
`TECH-DESIGN.md` Flujo 5 establece que **Python consume el outbox** y ejecuta Drive, Sheets, Telegram
y correo, manteniendo *"estado propio por integración"*. El consumidor tiene que actualizar ese
estado.

`usr_worker` necesita `SELECT` **y** `UPDATE` sobre `OutboxEvent`. No necesita `INSERT`: los eventos
los escribe .NET en la transacción del hecho de negocio, y esa asimetría —insertar de un lado,
actualizar del otro— es justamente lo que la matriz debe expresar y una convención no puede.

### `AfectacionMixta` es un indicador de `Factura`, no solo de la extracción (pendiente de R2-C11)

R2-C11 lo situó en los datos extraídos, que son **privados de Python**. Pero quien tiene que
rechazar la validación es .NET. El indicador viaja en el *payload* del `InboxEvent` y se persiste
como **indicador propio de `Factura`**, junto al proveedor genérico, el posible duplicado, los campos
no extraídos y la fecha en domingo. Es el mismo patrón que ya usan los otros cuatro.

### Hallazgo nuevo · R2-N1 — La métrica de precisión no la puede calcular nadie

**No está en el informe de la revisión.** Apareció al construir la matriz de permisos.

ADR 0017 establece que la métrica se obtiene *"comparando `DatosExtraidos` —inmutable— contra la
factura ya corregida"*. Pero `DatosExtraidos` es **privada de Python** (ADR 0003) y `Factura` es
**dominio de .NET**: ningún componente puede leer las dos. Mientras la partición era convención, el
problema era invisible; con la matriz de permisos de R2-A12 **falla de verdad**, con un error del
motor.

Es la misma familia que R2-C2: una capacidad que el diseño da por hecha y que la partición prohíbe.

**La decisión.** La evidencia de extracción viaja al dominio de .NET y se persiste al promover:

```
FacturaExtraccion            -- dominio de .NET
    FacturaId
    CampoNombre              'numero' | 'ruc' | 'total' | 'igv' | 'fechaEmision' | ...
    ValorExtraido
    Fuente                   'XML' | 'PDF'
```

El dato ya viaja: el `InboxEvent` lleva el resultado de la extracción, porque sin él .NET no podría
promover. Lo único nuevo es **persistirlo** en lugar de descartarlo tras crear la factura.

`DatosExtraidos` sigue siendo privada de Python como registro de trabajo del procesamiento. La copia
que importa para la métrica vive junto a la factura que originó.

**Cierra R2-A9 de paso.** La columna `Fuente` por campo es exactamente lo que A9 pedía para partir la
métrica entre XML y PDF, y por campo es más preciso que por documento: una factura con XML puede
tener un campo que el XML no traía.

**Consecuencias.**

- La métrica se calcula **enteramente del lado de .NET**, con una consulta sobre dos tablas propias.
  No hay canal nuevo ni dependencia de la cola.
- `FacturaExtraccion` es **inmutable**, igual que `DatosExtraidos`. La promesa del TDD —*"no se
  modifica en ningún momento posterior"*— se traslada a ella.
- Es una tabla por factura y por campo. Con 10-50 facturas diarias y una docena de campos, el
  volumen es despreciable.
- ADR 0017 debe corregir de dónde sale la métrica: hoy nombra una tabla que quien la calcula no
  puede leer.

---

## Cierre de la ronda 2 — Premisas externas y ratificación contable

Los dos frentes que quedaron abiertos al terminar la propagación se resolvieron con el responsable
del proyecto. **La ronda 2 queda cerrada por completo.**

### Las cinco premisas externas

| # | Premisa | Resolución |
|---|---|---|
| 1 | Modelo de recuperación de la base | **Condición de producción** |
| 2 | Cadena de `LOG BACKUP` existente | **Condición de producción** |
| 3 | RPO efectivo de la instancia | **Condición de producción** |
| 4 | Derecho a crear esquema y ejecutar DDL | **Confirmado** |
| 5 | Alta de proveedor inmediata por el asistente | **Confirmado** |

**Premisas 4 y 5 — confirmadas.** La base está asignada al proyecto, de modo que puede crear el
esquema `fact` y aplicar DDL sin intermediarios. Y el asistente contable sí tiene permiso de alta de
proveedores en el sistema contable, con efecto inmediato: sale, lo registra, vuelve y lo encuentra.

La premisa 5 era la que más sostenía en pie. De ella dependían **tres decisiones distintas**: el
descarte de replicar los datos maestros (ADR 0003), el bloqueo de `P00000` al validar (ADR 0006) y la
reversión 3 del PRD. Las tres quedan firmes.

**Premisas 1 a 3 — no aplican al entorno actual.** El proyecto es una demostración académica sin
contabilidad real que perder. Pasan de bloqueantes a **condiciones de puesta en producción**: ADR
0014 se reescribe declarando que el plan de respaldo se diseña y no se ejecuta, y que las tres
preguntas hay que responderlas antes de registrar la primera factura real.

Se conservan escritas, y no se borran, por una razón concreta: **para que la ausencia de respaldo no
se arrastre por inercia**. Un plan que no se ejecuta y nadie recuerda por qué es indistinguible de un
plan olvidado.

### La ratificación contable de `REGLAS.md` §12

**Se asume el riesgo.** No habrá contador que revise las seis reglas del núcleo.

§12 pasa de ser una lista de pendientes a una **advertencia explícita**: las seis son decisiones de
diseño con fundamento, internamente coherentes y razonadas, pero **no son criterios normativos
verificados**. El sistema construido sobre ellas cuadra y es explicable; lo que no está comprobado es
que coincida con la norma tributaria peruana ni con el criterio del contador de la compañía.

La advertencia dice además lo que hace falta que diga: **este sistema no debe operar con contabilidad
real sin esa revisión**. No porque las reglas estén probablemente mal, sino porque **el costo de
equivocarse no es simétrico**. Los puntos 1 y 5 —tipo de cambio venta y herencia del TC en la nota de
crédito— afectan a *todo* asiento en moneda extranjera ya confirmado: corregirlos después es
reprocesar el libro, no cambiar una línea de código.

Y se precisa la salida del punto 5, que antes quedaba a medias: si el criterio correcto fuera el tipo
de cambio propio, **hay que aceptar las dos cosas a la vez** — el residuo deja de ser un defecto y
pasa a ser diferencia de cambio legítima, y el sistema necesita la línea de ajuste y su cuenta. No se
puede adoptar el tipo de cambio propio y seguir declarando la diferencia de cambio fuera de alcance.

### Estado final del corpus

| Documento | Estado |
|---|---|
| `TECH-DESIGN.md` | v4 |
| `REGLAS.md` | v2 |
| `PRD.md` | Con tabla de reversiones |
| `adrs/` | 19 ADRs, **ninguno condicionado** |
| `DESIGN.md`, `DESIGN_BRIEF.md` | Alineados con el modelo |
| `DECISIONES-REVISION.md` | Rondas 1 y 2 registradas |

**No queda ningún hallazgo abierto de ninguna de las dos revisiones adversariales.**

Lo que sí queda, y no es un hallazgo sino una decisión consciente, son **dos condiciones de puesta en
producción**:

1. Responder las tres preguntas de respaldo de ADR 0014.
2. Someter las seis reglas de `REGLAS.md` §12 a revisión contable formal.

Ninguna de las dos bloquea construir el sistema. Las dos bloquean **operarlo con datos reales**, y
esa distinción está escrita en los documentos donde corresponde para que no se pierda.
