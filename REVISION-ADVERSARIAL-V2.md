# Revisión adversarial — TECH-DESIGN v3, ADRs 0001-0017 y PRD

**Alcance revisado:** `TECH-DESIGN.md` (v3), los 17 ADRs de `adrs/`, `PRD.md`, `DESIGN.md`,
`handoff/DESIGN_BRIEF.md` y —incorporados en la segunda pasada— `REGLAS.md`,
`DECISIONES-REVISION.md`, `MOTIVOS-CLASIFICACION.md`, `PREGUNTAS-CONTABLES.md` y `adrs - v1/`.

**Estado del documento:** revisión ejecutada en dos pasadas. La primera se hizo sin el corpus
contable, que no estaba en el repositorio. Al aportarse, cada hallazgo se volvió a evaluar contra él.
Los cuatro que quedaron sin fundamento **se retiraron del cuerpo del informe** y se resumen en el
apéndice final; los hallazgos restantes se **renumeraron de corrido**, de modo que los
identificadores ya no coinciden con los de la primera pasada. Dos hallazgos bajaron de severidad y
lo declaran en su propio texto: **C6** —cuya acusación original era falsa— y **A15**, que se reportó
como crítico. Los hallazgos **C9–C11**, **A13–A14** y **S4** son nuevos y salen exclusivamente de
leer `REGLAS.md` y `DECISIONES-REVISION.md` contra el TDD y los ADRs.

**Condición de la revisión:** conversación limpia, sin el historial que produjo el diseño. El TDD
declara ser versión 3 ya endurecida contra un `REVISION-ADVERSARIAL.md` previo (excluido del alcance
por indicación del responsable), de modo que el listón aplicado fue el de un documento maduro.

**Recuento:** 11 críticos (C1–C11), 15 advertencias (A1–A15) y 4 sugerencias (S1–S4).

---

## Crítico

### C1 — `UQ_Factura_Identidad` rompe dos casos borde del PRD y deja inalcanzable el flujo de duplicados

> **Confirmado y reforzado por los documentos aportados.** La contradicción no está entre dos
> documentos: está **dentro de una sola sección**. `DECISIONES-REVISION.md` §C2 prescribe el índice
> único filtrado y, cuatro párrafos más abajo, dice que *"la bandera de duplicado se recalcula al
> guardar avance y al validar"* y que la salida es *"el asistente corrige el número"*. Ambas cosas
> exigen que la fila duplicada **exista** en `Factura`. El índice impide que llegue a existir: el
> filtro es `Estado <> 'DESCARTADA'` y la promoción inserta en `PENDIENTE_VALIDACION`, que está
> dentro del filtro. `REGLAS.md` §8 repite la misma salida imposible ("corregir el número, o
> descartar la factura") y la clasifica como rechazo `409` **al validar**, cuando el motor ya habrá
> rechazado el `INSERT` días antes, en el worker, sin interfaz donde mostrarlo.

**Objetivo:** `TECH-DESIGN.md`, modelo de `Factura`; Flujo 2 y Flujo 3.

```sql
CREATE UNIQUE INDEX UQ_Factura_Identidad
    ON Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';
```

**Problema A — campos no extraídos.** El PRD declara como caso borde normal que el OCR no logre
extraer uno o más campos clave, y que la factura quede en `PENDIENTE_VALIDACION` con esos campos
vacíos, resaltados para carga manual. El TDD lo conserva como indicador propio de `Factura`. Pero
en SQL Server un índice único trata los `NULL` como iguales: solo admite **una** fila con
`(NULL, '01', NULL)`. Con cadena vacía el resultado es idéntico. Consecuencia concreta: **la segunda
factura del día cuyo número no se pudo extraer es rechazada por el motor al promover**, y por
ADR 0005 se convierte en incidencia en vez de en factura editable. El caso borde que el PRD manda
soportar es exactamente el que el índice impide.

**Problema B — el flujo de duplicados no puede ocurrir.** El TDD sostiene dos mecanismos que se
excluyen:

- Flujo 2: *"Intentar insertar una factura con `(RUC, tipo, número)` ya existente y no descartada es
  rechazado por el motor."*
- Flujo 3: *"Validar con duplicado sin resolver es rechazado por la API con `409`. Las salidas son
  corregir el número o descartar la factura."* Más el indicador `posibleDuplicado`, más *"la bandera
  de duplicado se recalcula al guardar y al validar"*.

Si el índice rechaza el `INSERT`, el duplicado **nunca llega a existir como `Factura`**. No hay nada
que abrir, ni número que corregir, ni factura que descartar. Todo el flujo de resolución de
duplicados —incluido el caso `409` de ADR 0008, el indicador de ADR 0005 y el recálculo al guardar—
es código inalcanzable. Y en el camino en que sí se alcanza —el asistente edita el número hacia uno
que ya existe— el fallo no llega como `409` con salidas, sino como violación de índice en un
`UPDATE`.

El criterio del PRD es *"detecta y alerta antes de permitir un nuevo registro"*: alertar, no
rechazar en el motor.

**Dirección:** decidir cuál de los dos mecanismos manda. Si manda el flujo de resolución, el índice
debe ser de detección (no único) y la unicidad se aplica **al validar**, no al insertar; el `409` con
sus dos salidas pasa a ser el único punto de control. Si manda el motor, hay que borrar del TDD el
flujo de duplicados completo y explicar qué hace el asistente con la incidencia.

---

### C2 — El indicador de idempotencia de la promoción vive en una tabla privada de Python

> **Confirmado con línea y número.** ADR 0005 se contradice consigo mismo en el mismo documento:
> la línea 57 dice *"`Procesamiento` lleva el indicador de si ya originó una factura"* y la línea 83
> celebra que *"`Procesamiento` vuelve a ser privada de Python"*. Como la promoción la ejecuta .NET
> (línea 42: *"la decisión de promover es de .NET"*), .NET tiene que escribir ese indicador.
> `DECISIONES-REVISION.md` §A3 cierra el hallazgo afirmando que *"ningún componente sondea la tabla
> interna del otro"* — cierto para la lectura, falso para esta escritura.

**Objetivo:** ADR 0005, sección "Idempotencia", contra ADR 0003, reglas invariantes 1 y 3.

ADR 0005: *"`Procesamiento` lleva el indicador de si ya originó una factura."*

ADR 0003 clasifica `Procesamiento` como **privada de Python**, en la clase donde *"un solo componente
escribe **y** lee"*, y establece dos invariantes: *"Python no escribe ni lee tablas de dominio de
.NET"* y *"ningún componente sondea la tabla privada del otro"*.

La contradicción no tiene salida limpia:

- Si **.NET** escribe ese indicador, escribe en una tabla privada de Python y rompe la partición y su
  refuerzo por permisos, que es la propiedad que ADR 0003 declara como su mayor logro.
- Si **Python** lo escribe, Python necesita saber si .NET promovió, lo que exige un cuarto canal que
  no existe: ADR 0004 define tres tablas y ninguna va de .NET hacia Python informando el resultado de
  la promoción.

Es especialmente grave porque ADR 0005 **existe precisamente para corregir** una violación de esta
misma regla —el sondeo directo de `Procesamiento` desde .NET—, y la reintroduce por otra puerta en su
propia sección de idempotencia.

**Dirección:** el indicador pertenece al lado de .NET. O una columna de estado en `InboxEvent`, que
es tabla de contrato y por tanto coescribible por diseño, o un índice único sobre
`Factura.ProcesamientoId`, que además convierte la idempotencia en invariante del motor —el mismo
argumento que ADR 0005 usa para preferir el índice de identidad sobre una bandera calculada.

---

### C3 — El orden por agregado y la clase `DIFERIBLE` se contradicen

**Objetivo:** ADR 0004, "Garantías comunes", contra ADR 0010, clase `DIFERIBLE`.

ADR 0004 promete: *"Los eventos de una misma factura se procesan serializados y en orden de
creación"*, y nombra la consecuencia de no cumplirlo: *"sin esto, `ASIENTO_ANULADO` y
`ASIENTO_CORREGIDO` aplicados fuera de orden dejan la fila de Sheets con el dato equivocado **de
forma permanente**"*.

ADR 0010 introduce `DIFERIBLE`: superación de cuota de Google, *"reintento planificado al abrirse la
ventana de cuota, no en segundos"* — es decir, horas, y en una cuota diaria hasta el día siguiente.

Escenario que el diseño permite hoy:

1. 10:00 — se reconfirma un asiento. Se emite `ASIENTO_CORREGIDO`.
2. 10:00 — el envío a Sheets golpea la cuota diaria. Se clasifica `DIFERIBLE` y se reprograma para
   el día siguiente.
3. 11:00 — el asistente anula ese mismo asiento. Se emite `ASIENTO_ANULADO`.
4. 11:00 — la cuota ya se liberó o el evento toma otra ruta, y `ASIENTO_ANULADO` se aplica.
5. Día siguiente — `ASIENTO_CORREGIDO` se reintenta y **resucita el importe sobre el estado
   anulado**.

Sheets queda permanentemente mal, que es literalmente el daño que ADR 0004 dice haber cerrado.

La garantía de orden por agregado exige **bloqueo de cabecera de línea**: mientras un evento de la
factura X no termine, ninguno posterior de X avanza. `DIFERIBLE` es, por definición, un evento que no
termina durante horas. Las dos políticas se diseñaron por separado y son incompatibles.

Se suma que el mecanismo de reclamo tampoco sostiene la garantía: ADR 0002 nombra `READPAST` como la
única dependencia del motor, y `READPAST` **salta** las filas bloqueadas — con dos consumidores en
paralelo, saltar la fila 1 y tomar la 2 de la misma factura es exactamente el modo de fallo que la
garantía prohíbe. Reclamar por fila no produce orden por agregado; hay que reclamar por agregado.

**Dirección:** hacer explícito el mecanismo. Un `Secuencia` monótona por `FacturaId`, reclamo que
tome el evento pendiente **más antiguo por factura** y no avance esa factura mientras haya uno
anterior en cualquier estado no terminal —incluido `DIFERIBLE`—. Y aceptar la consecuencia: una cuota
agotada detiene los eventos de esa factura, no solo el que falló.

---

### C4 — `POST /asientos/{id}/reactivar` no tiene transición de estado ni evento

> **Confirmado, y peor de lo reportado.** `REGLAS.md` §9 y `DECISIONES-REVISION.md` §C3 dibujan el
> mismo ciclo de vida sin ninguna flecha que salga de `ANULADO`, y la palabra "reactivar" no aparece
> en ninguno de los dos documentos: el endpoint de ADR 0008 no tiene respaldo en ninguna decisión.
> Y hay un callejón sin salida que ningún documento nombra: anular el asiento de una factura la deja
> irrectificable para siempre. La precondición de nota de crédito (`DECISIONES` §A4, `REGLAS` §8)
> rechaza toda NC cuya factura referenciada tenga el asiento `ANULADO`; la factura sigue en estado
> `VALIDADA`, de modo que el índice único impide volver a registrarla; y no hay transición de vuelta.
> Un error de anulación no tiene deshacer.

**Objetivo:** ADR 0008 (endpoints), ADR 0006 (ciclo de vida), ADR 0004 (catálogo de eventos).

ADR 0008 expone `POST /api/asientos/{id}/reactivar`. El TDD lo respalda: `AuditoriaCorreccion`
*"cubre correcciones de factura y de asiento, reaperturas con su motivo, **anulaciones y
reactivaciones**"*. `DESIGN_BRIEF.md` lo pide: *"puede editarse o anularse (y reactivarse)"*.

Pero:

- **ADR 0006 no tiene esa transición.** Su diagrama es
  `BORRADOR → CONFIRMADO → ANULADO`, con `reabrir` de vuelta desde `CONFIRMADO`. De `ANULADO` no sale
  ninguna flecha. El ADR que es dueño del ciclo de vida no conoce la operación.
- **ADR 0004 no tiene el evento.** Su catálogo es `FACTURA_VALIDADA`, `FACTURA_CORREGIDA`,
  `ASIENTO_CORREGIDO`, `ASIENTO_ANULADO`. No hay `ASIENTO_REACTIVADO`.
- El TDD lo confirma por omisión: los criterios del Flujo 4 dicen *"Anular emite `ASIENTO_ANULADO`, y
  el asiento deja de contar en el dashboard"*, y no dicen nada de reactivar.

Consecuencia: reactivar un asiento anulado lo devuelve a `CONFIRMADO` en la base **y no llega nunca a
Sheets**. El importe queda descontado del dashboard de forma permanente. Es el mismo bug que ADR 0004
celebra haber corregido —*"en el diseño anterior un asiento anulado seguía contando como gasto en
Looker Studio de forma permanente"*— con el signo invertido, y sobrevivió a la corrección porque el
endpoint se añadió en ADR 0008 sin volver a ADR 0006 ni a ADR 0004.

**Dirección:** o se añade la transición en ADR 0006 y el evento en ADR 0004, o se retira el endpoint
y se documenta que un asiento anulado es terminal (lo que además es la postura contable más
defendible, y coincide con la alternativa que ADR 0006 ya evaluó).

---

### C5 — La clave de sincronización hacia Sheets y Drive es mutable por diseño

> **Confirmado y reforzado.** `DECISIONES-REVISION.md` §C5 dice que *"la idempotencia se construye
> en cada integración —buscar antes de crear en Drive, *upsert* por clave en Sheets—"* y **no define
> cuál es esa clave** en ninguna parte. El mismo catálogo de eventos incluye `FACTURA_CORREGIDA`, es
> decir, el diseño espera explícitamente que los datos del comprobante cambien después de publicados.

**Objetivo:** `TECH-DESIGN.md`, Flujo 5; ADR 0004.

El criterio dice: *"Drive **busca antes de crear** y Sheets hace **upsert por clave**: repetir un
evento no duplica carpeta ni fila."* Ni el TDD ni ningún ADR dicen **cuál es esa clave**.

La única clave de negocio natural es la identidad del comprobante `(RUC, tipo, número)` — la misma
del índice y la misma del criterio de duplicados del PRD. Y el diseño contiene un flujo cuyo
propósito explícito es **cambiarla**: *"las salidas son corregir el número o descartar la factura"*, y
el evento `FACTURA_CORREGIDA` existe para propagar correcciones.

Consecuencia: corregir el número de una factura ya sincronizada produce un `upsert` que **no
encuentra** la fila anterior y **inserta una nueva**. La fila vieja permanece. Looker Studio cuenta
el gasto dos veces, de forma permanente y silenciosa. En Drive, si el nombre de la carpeta incluye el
número —que es lo natural—, "buscar antes de crear" tampoco encuentra la carpeta previa y crea una
segunda, dejando los medios probatorios repartidos.

Además la corrección del proveedor `P0000` cambia el RUC, con el mismo efecto.

**Dirección:** la clave de idempotencia externa debe ser el identificador subrogado `FacturaId`,
inmutable por construcción, y debe estar escrita en el TDD. La identidad fiscal es un atributo que
viaja en el *payload*, nunca la clave.

---

### C6 — El núcleo contable está declarado pendiente de ratificación, y el PRD contradice al diseño por escrito

> **Origen.** Reportado en la primera pasada (C8) como "cinco requisitos del PRD revertidos sin
> registro". Esa acusación era falsa y se retira; lo que queda en su lugar es de otra naturaleza y
> más serio.
>
> **Estado tras los documentos aportados.** La acusación central —"sin registro"— **es falsa y se
> retira**. `DECISIONES-REVISION.md` §C6 documenta el cambio a tipo de cambio **venta** con el
> fundamento correcto (una compra genera un pasivo, y los pasivos se convierten a venta), y de paso
> corrige un error de la revisión previa sobre la publicación de la SBS: publica por las noches y lo
> del viernes cubre sábado, domingo y lunes. La eliminación del 0.00 y el bloqueo de `P0000` están
> igualmente razonados. Los tres cambios tienen constancia; lo que no tienen es ADR, y el TDD cita
> `DECISIONES-REVISION.md` sin que ningún ADR lo recoja.
>
> **Lo que queda es más serio de lo que decía el hallazgo original.** `REGLAS.md` §12 declara por su
> cuenta que cuatro reglas del núcleo contable —el tipo de cambio venta, la absorción del redondeo en
> la cuenta de cargo, el IGV de la boleta al costo y las precondiciones de la nota de crédito—
> **están pendientes de revisión formal por un contador**. `PREGUNTAS-CONTABLES.md` §D1 pide esa
> confirmación por escrito y el bloque de respuesta está vacío. El diseño es honesto al declararlo;
> el punto es que el documento que se llama normativo dice, en su última sección, que su capítulo más
> caro no está ratificado — y si el criterio correcto resultara ser otro, la corrección no es un
> ajuste de código, es reprocesar todo asiento en moneda extranjera ya confirmado.
>
> Sigue vigente sin matices que el **PRD contradice al diseño por escrito** en los cinco puntos de la
> tabla. Un `PRD.md` v2 continúa siendo necesario.


**Objetivo:** transversal. El PRD sigue siendo el documento contractual y hoy contradice al TDD por
escrito en cinco puntos sustantivos.

| # | El PRD dice | El TDD hace | ¿La decisión técnica es correcta? |
|---|---|---|---|
| 1 | Tipo de cambio **compra** de la fecha de emisión (repetido en alcance, en manejo de moneda y como supuesto "Confirmado") | Tipo de cambio **venta** | **Sí**, casi con seguridad: una compra genera un pasivo en moneda extranjera. Pero cambia el importe de **todo** asiento en moneda extranjera. |
| 2 | Sin TC del día se registra **0.00** con observación (alcance y caso borde) | La factura **no se abre para edición**; `409` | Sí, y sin contrapartida: un asiento con TC 0.00 es basura contable, y `REGLAS.md` §6 cierra el caso real —SBS sin publicar— con la carga manual inmediata. |
| 3 | Asiento con `P0000`, corregido después (caso borde) | `409` al validar | Contestable. Ver A15. |
| 4 | Detalle generado **mapeando cada producto del catálogo** a su cuenta; catálogo de productos como dato maestro | `FacturaDetalle`, `Producto` y el mapeo **eliminados**; el motivo determina la cuenta | Sí, y ADR 0011 lo argumenta muy bien: nada alimentaba esas tablas. |
| 5 | Reintento **3 veces** para todo fallo | Tres clases; `PERMANENTE` sin reintentos, `DIFERIBLE` por ventana | Sí, y ADR 0010 lo argumenta bien. |

Los puntos 4 y 5 tienen ADR propio, con contexto, alternativas y costos: son reversiones ejemplares.
Los puntos **1, 2 y 3 no lo tienen**. El cambio de tipo de cambio —el de mayor impacto económico de
los cinco— vive en **una línea con un paréntesis de nueve palabras** dentro de ADR 0006, sin
alternativa considerada, sin consecuencia declarada y sin constancia de que alguien con autoridad
contable lo aprobara. El PRD lo lista cuatro veces, dos de ellas bajo "Confirmado".

**Por qué importa más que la higiene documental:** "Confirmado" en el PRD significa que alguien lo
decidió. Revertir una decisión confirmada sin dejar rastro hace que, en la revisión formal del
contador que el propio TDD deja pendiente, nadie pueda distinguir un cambio deliberado de un error de
transcripción. Y si el criterio correcto resultara ser el del PRD, la corrección no es un ajuste de
código: es reprocesar todo asiento en moneda extranjera ya confirmado.

**Dirección:** un ADR corto —"Tipo de cambio aplicable a la conversión"— con el fundamento normativo,
y un `PRD.md` v2 que recoja las cinco reversiones. El PRD no puede quedar contradiciendo al diseño
por escrito.

---

### C7 — El plan de respaldo puede ser inejecutable sobre una base que no es de este proyecto

> **Confirmado, y el supuesto ahora es explícito en contra.** `DECISIONES-REVISION.md` §C3 declara
> que `Proveedor`, `CuentaContable`, `Motivo` y `Origen` los mantiene **el sistema contable de la
> compañía en esta misma base de datos**. Entonces el plan de §C9 —`FULL BACKUP` diario y
> `LOG BACKUP` cada 15 minutos, con RPO de 15 minutos— se está imponiendo sobre una base compartida
> con un sistema ajeno al proyecto. Si ese sistema tiene su propia cadena de respaldo, un `FULL`
> nuevo le reasienta la base diferencial y cambiar el modelo de recuperación le altera la semántica;
> si no la tiene, este proyecto pasa a ser de facto el responsable del respaldo de datos que no le
> pertenecen. Ninguna de las dos posibilidades está escrita.

**Objetivo:** ADR 0014, contra ADR 0003, clase de tablas externas.

ADR 0003 establece que las tablas maestras las mantiene **el sistema contable de la compañía**, *"en
esta misma base"*. ADR 0014 decide, sobre esa misma base: `FULL BACKUP` diario y `LOG BACKUP` cada 15
minutos, con `RPO = 15 minutos` y prueba de restauración periódica.

Tres problemas que ADR 0014 no considera, porque fue escrito como si la base fuera exclusiva de este
proyecto:

1. **La cadena de log no se comparte.** Si el sistema contable ya toma sus propios `LOG BACKUP` a otro
   destino, las dos cadenas se intercalan y **ninguna de las dos restaura por sí sola**: recuperar
   exige ambos conjuntos, completos y en orden. Es un modo de fallo clásico de SQL Server y solo se
   descubre el día de la restauración.
2. **El modelo de recuperación no es una decisión de este proyecto.** `LOG BACKUP` exige modelo
   `FULL`. Si la base está en `SIMPLE`, cambiarla altera el crecimiento del log del sistema contable
   sin su consentimiento; si ya está en `FULL`, el punto 1 es casi seguro.
3. **La restauración no es local.** No se puede restaurar "las tablas de este proyecto". Restaurar la
   base a un punto en el tiempo **revierte también la contabilidad de la compañía**. El plan de
   recuperación, tal como está escrito, tiene como efecto colateral tirar atrás el sistema contable
   entero.

ADR 0014 se abre diciendo que esta es *"la respuesta al mayor riesgo de negocio del proyecto"* y se
cierra con *"un respaldo que nunca se restauró no es un respaldo, es una suposición"*. La frase es
correcta y se aplica a sí misma: el procedimiento no se ha contrastado contra el hecho de que la base
tiene un segundo dueño.

**Dirección:** verificar quién administra esa instancia y qué respaldo tiene hoy. Lo más probable es
que la respuesta correcta sea **base de datos propia** para este proyecto, con las tablas maestras
leídas por vista, `synonym` o consulta entre bases — lo que además vuelve viables los permisos de
ADR 0003 y los derechos DDL de ADR 0016 (ver A12). Si la base debe ser compartida, ADR 0014 tiene que
reescribirse en términos de "qué añadimos al respaldo que ya existe", no de "qué respaldo montamos".

---

### C8 — No hay ninguna decisión sobre estrategia de pruebas

> **Confirmado, y ahora es más difícil de justificar.** El corpus aportado no menciona pruebas
> automatizadas en ninguna de sus 1802 líneas, y a la vez entrega el mejor insumo de pruebas de todo
> el proyecto: `REGLAS.md` §7 define siete invariantes de confirmación comprobables y §10 trae
> **cinco ejemplos numéricos completos y cuadrados** —factura gravada con destino, boleta con IGV al
> costo, factura en dólares con redondeo derivado, factura con percepción y nota de crédito—. Son
> casos de prueba ya escritos. No existe decisión sobre quién los ejecuta ni con qué.

**Objetivo:** el conjunto. Área de decisión ausente.

El TDD dedica cien líneas a criterios de aceptación por flujo, y son buenos: concretos, verificables
y sin ambigüedad. No hay **ni una sola decisión** sobre cómo se verifican.

Lo que falta, y que el propio diseño reclama sin recogerlo:

- ADR 0006 dice que las invariantes del bloque principal *"son tres caminos que probar, no uno"*, y
  ahí se detiene.
- El TDD lista como riesgo abierto que *"convendrían pruebas de contrato sobre esas tablas"* — un
  deseo, no una decisión.
- La sincronización de tipos entre C# y Python se declara riesgo en ADR 0002, se dice mitigada por el
  SQL versionado en ADR 0016, y nada verifica esa mitigación.
- No hay decisión sobre cómo se prueban las reglas contables con datos fijos: ni juego de facturas de
  referencia, ni asientos esperados, ni cómo se prueba un asiento contra el plan de cuentas real de
  1650 filas.

Es el hueco estructural más grande del diseño, y es desproporcionado con el resto: un sistema cuyo
núcleo es un puñado de invariantes aritméticas sobre dinero, con conversión de moneda, redondeo,
percepción, notas de crédito parciales y contabilidad por destino generada automáticamente, no tiene
decidido cómo se comprueba que suma bien. Todo lo demás del documento está decidido con detalle.

**Dirección:** un ADR de estrategia de verificación. Como mínimo: dónde vive la lógica contable para
poder probarla sin base de datos, el juego de casos de referencia por tipo de comprobante —gravada,
boleta, exonerada, con percepción, nota de crédito total y parcial—, y una prueba de contrato sobre
las cinco tablas de frontera que corra contra el esquema versionado, que es la única mitigación
declarada del riesgo de divergencia de tipos.

---

### C9 — La nota de crédito en moneda extranjera deja un residuo cambiario permanente, y ninguna regla dice qué tipo de cambio usa

**Objetivo:** `REGLAS.md` §5, §6 y §7; `DECISIONES-REVISION.md` §A4; ADR 0006.

`REGLAS.md` §6 fija una sola regla de conversión: se ancla `totalPEN` e `igvPEN` con el tipo de
cambio venta **de la fecha de emisión**, y se deriva la base. §5 define la nota de crédito como
**espejo** de la factura que modifica, que hereda de ella el motivo y la cuenta de cargo. Y nada, en
ninguno de los cinco documentos, dice **con qué tipo de cambio se convierte la nota de crédito**.

Aplicada la única regla escrita, la nota usa el TC de **su propia** fecha de emisión, que casi nunca
es la de la factura. Consecuencia aritmética directa: una nota de crédito que anula el **100%** de
una factura en dólares **no deja el pasivo en cero**. Deja

```
residuo = totalOrig × (TCventa_NC − TCventa_factura)
```

repartido entre `421212`/`431212`, la cuenta de cargo heredada y —al invertirse también el bloque
destino— entre `ctarefleja` y `ctapuente`. Con un movimiento de tres milésimas sobre una factura de
USD 10.000 son S/ 30 colgados en una cuenta por pagar, por proveedor, para siempre.

Tres mecanismos deberían atraparlo y ninguno lo hace:

1. **El cuadre.** `SUM(Debe) = SUM(Haber)` se cumple: el asiento de la nota cuadra perfectamente
   consigo mismo. El descuadre es **entre dos asientos**, y no hay invariante que mire ese par.
2. **El tope acumulado.** La consulta de `DECISIONES` §A4 suma `MontoTotal`. Si ese campo está en
   moneda original, el tope funciona y el residuo es invisible; si está en soles, el tope compara
   importes convertidos a tasas distintas y una nota del 100% puede rebasarlo o quedarse corta por
   razones puramente cambiarias. **Cualquiera de las dos lecturas rompe algo**, y ningún documento
   dice cuál es.
3. **El alcance.** `REGLAS.md` §1 declara la diferencia de cambio fuera de alcance porque *"nace al
   pagar o al cierre"*. Este residuo no nace al pagar ni al cierre: **lo genera este sistema**,
   dentro del libro de compras, sin que intervenga ningún pago.

**Dirección:** decidirlo explícitamente en `REGLAS.md` §6 y en ADR 0006. Lo coherente con llamar
"espejo" a la nota de crédito es que **herede el tipo de cambio congelado de la factura referenciada**
—igual que hereda el motivo y la cuenta—, no el de su propia fecha. Si el criterio contable fuera el
contrario, entonces la diferencia de cambio deja de estar fuera de alcance y hace falta la línea de
ajuste que hoy se declara inexistente.

---

### C10 — "Una nota anulada libera su importe" es una invariante que la consulta escrita no puede cumplir

**Objetivo:** `REGLAS.md` §7, última invariante; `DECISIONES-REVISION.md` §A4.

`REGLAS.md` §7 lo enuncia como norma: *"La suma de las notas de crédito **vigentes** sobre una
factura no puede exceder su monto total... Una nota anulada **libera** su importe."*

La consulta que `DECISIONES` §A4 entrega para evaluarla es:

```sql
SELECT SUM(f.MontoTotal)
  FROM Factura f
 WHERE f.FacturaReferenciaId = @facturaOriginalId
   AND f.Estado = 'VALIDADA';
```

Filtra por `Factura.Estado`. Pero **la anulación se aplica al asiento, no a la factura** — lo dice
`DECISIONES` §C5 de forma expresa, al sacar `FACTURA_ANULADA` del catálogo de eventos: *"`Factura`
tiene los estados `PENDIENTE_VALIDACION`, `VALIDADA` y `DESCARTADA`, y la anulación aplica al
asiento"*. Una nota de crédito cuyo asiento se anuló conserva `Estado = 'VALIDADA'` y **sigue
sumando**. La capacidad nunca se libera, y la regla que el propio documento llama invariante es
inalcanzable con el modelo de estados que ese mismo documento define.

No es un descuido de redacción: es la única consulta escrita de la única invariante que depende del
estado de otras filas, y está mal. Implementada literalmente, produce un sistema que rechaza notas
de crédito legítimas con `409` sin que nadie entienda por qué.

**Dirección:** unir con `AsientoContable` y excluir `Estado = 'ANULADO'`, o dar a `Factura` un estado
que refleje la anulación de su asiento. Lo primero es una línea; lo segundo contradice `DECISIONES`
§C5. Elegir, y escribirlo.

---

### C11 — El sistema debe rechazar la factura mixta y no tiene con qué detectarla

**Objetivo:** `REGLAS.md` §8, última fila; `TECH-DESIGN.md` modelo de `Factura`; ADR 0011.

`REGLAS.md` §8 lista ocho reglas de rechazo. Siete son comprobables con los datos que el sistema
tiene. La octava —*"Factura con líneas gravadas y no gravadas mezcladas → fuera de alcance:
registrar por otra vía"*— **no lo es**.

`FacturaDetalle` se eliminó del modelo (ADR 0011, `DECISIONES` §C1) y `Afectacion` es **un único
campo de cabecera** con tres valores posibles (`TECH-DESIGN.md`, modelo de `Factura`). Una factura
mixta no tiene representación posible: el extractor elegirá uno de los tres valores, el comprobante
parecerá homogéneo y **pasará las ocho reglas**.

El modo de fallo es silencioso y va en la peor dirección: una factura mixta registrada como
`GRAVADA` toma crédito fiscal sobre la porción que no lo genera. Y lo único que el sistema hace con
`Afectacion` es decidir si el IGV se desagrega o se incorpora al costo — es decir, el campo que no
puede representar el caso es exactamente el que gobierna la estructura del asiento.

Esto **no es un argumento para revivir `FacturaDetalle`**: matar esas tablas fue la mejor decisión de
ADR 0011 y sigue siéndolo. La detección es mucho más barata. El XML UBL trae las líneas con su
código de afectación, así que basta un booleano calculado en la extracción —*"el XML declara más de
un tipo de afectación"*— que viaje en `DatosExtraidos` y dispare el rechazo. Cuesta un campo.

Queda un límite que hay que declarar y hoy no está: sobre una factura **solo en PDF** la mezcla no es
detectable de forma fiable por ningún medio. Si la regla de rechazo va a existir, tiene que decir que
su cobertura es la de los comprobantes con XML.

---

## Advertencia

### A1 — `NumeroAsiento` promete "sin huecos" sin decir cómo, y no define qué pasa al cambiar de periodo

ADR 0006 justifica asignar el correlativo al confirmar con un argumento correcto: *"si se reservara
antes, cada factura abandonada quemaría un número y el libro quedaría con huecos que en una revisión
hay que justificar."* Pero no dice con qué se genera. Si es `SEQUENCE` o `IDENTITY`, **una transacción
revertida quema el número igual** — y el TDD tiene al menos una vía de reversión tardía: las
invariantes de confirmación se evalúan dentro de la transacción de validación. El único mecanismo que
cumple la promesa es una tabla contador actualizada dentro de la misma transacción, con bloqueo de
actualización. Es una decisión, y no está tomada.

Segundo hueco: el correlativo es *"por periodo y origen"*, y el periodo sale de `FechaContable`, que
es **editable** y sobrevive a un `reabrir`. Si el asistente reabre un asiento confirmado en agosto y
le cambia la fecha contable a septiembre, no está definido si conserva su número —que entonces
pertenece a la serie del mes equivocado— o toma uno nuevo, dejando en agosto el hueco que el ADR
prometió no tener. Ambas opciones rompen algo prometido.

### A2 — Nota de crédito: dos caminos sin comportamiento definido

> **Vigente y confirmado.** `REGLAS.md` §5 repite la herencia (*"hereda motivo y cuenta de cargo: el
> asistente no elige"*) sin resolver el reparto en N cuentas, y §7 exige que los cargos al Haber
> igualen la base sin decir cómo se distribuyen. Sobre el segundo camino, `DECISIONES` §A4 declara las
> cuatro precondiciones como supuesto propio y pide confirmación —*"si alguna debe permitirse, por
> ejemplo una nota de crédito que llega antes que su factura, hay que decirlo"*—, pero no contempla el
> caso de arranque: notas contra facturas anteriores a la existencia del sistema.

ADR 0006 establece que la nota de crédito *"hereda motivo y cuenta de la factura referenciada; el
asistente no elige motivo"*, y a la vez ADR 0011 permite **dividir el cargo entre varias cuentas** del
motivo. Cuando la factura original tiene el cargo repartido en N cuentas y la nota de crédito es
**parcial** —que es el caso habitual: un descuento, una devolución de parte de la mercadería—,
"hereda la cuenta" no designa nada: no dice cuál de las N, ni en qué proporción, y el asistente
tiene prohibido decidirlo. La invariante *"la suma de las notas vigentes no puede exceder el monto
total"* confirma que las notas parciales están contempladas. No hay regla.

Segundo camino: `FacturaReferenciaId` es **obligatorio** para el tipo `07`, y validar se rechaza con
`409` si la factura referenciada no existe. En el arranque, y durante meses después, llegarán notas
de crédito contra facturas emitidas **antes de que el sistema existiera**. Esas notas no tienen
salida: no se pueden validar y la única acción disponible es descartarlas, es decir, perder un
documento fiscal real. El diseño no tiene ningún camino de arranque para este caso.

### A3 — La pantalla de Configuración pide un estado de conexión que ninguna API expone

`DESIGN_BRIEF.md` especifica: *"Estado de conexión con Gmail, Drive y Google Sheets (conectado / con
error)"*, y `DESIGN.md` define la píldora "Conectado / Con error" como parte del vocabulario de
estados de la aplicación. ADR 0008 expone `POST /api/integraciones/{nombre}/sincronizar` y
`POST /api/integraciones/google/reconectar`, pero **ningún `GET`**. No hay tabla, endpoint ni campo
que sostenga ese indicador. Es el único elemento del diseño de interfaz sin cobertura en el modelo de
datos.

Importa más de lo que parece por A4.

### A4 — La falta de vigilancia del worker se aceptó antes de que ADR 0015 decidiera desplegar el agregador

El TDD registra como riesgo **aceptado explícitamente** que el worker no tenga vigilancia: *"si el
worker se detiene o se cuelga, nadie avisa, porque el mecanismo de notificación vive dentro de él.
Una bandeja sin facturas nuevas es indistinguible de un día sin facturas."*

La decisión es del responsable del proyecto y no se discute aquí. Lo que sí corresponde señalar es
que **el precio de la mitigación cambió**: ADR 0015 decide desplegar un agregador de logs *"con
búsqueda, retención configurable y **alertas por patrón**"*, y ya lo usa para el umbral de espacio en
disco. La vigilancia que se descartó por costosa hoy es una alerta por **ausencia** de latido sobre
un componente que ya está en el plan de despliegue, con `CorrelationId` ya propagado a los tres
artefactos. Junto con A3 —un `GET` de última ejecución exitosa por integración—, cubre el riesgo casi
sin trabajo nuevo.

Vale la pena reconsiderar la aceptación contra este costo, no contra el anterior. De los criterios de
éxito del PRD, dos dependen hoy de la atención del operador: visibilidad en 15 minutos y entrega de
notificaciones ≥99%.

### A5 — Los adjuntos manuales posteriores a la validación nunca llegan a Drive

ADR 0013 introduce `AdjuntoManual` justificándolo con el caso borde del PRD del correo *"que llega
sin OC o sin medios probatorios"* y con el criterio de éxito del 100% de facturas validadas
archivadas **con sus medios probatorios**. El empaquetado usa *"la lista completa de rutas"* que
viaja en el *payload* de `FACTURA_VALIDADA`, congelada en el momento de validar.

Pero ADR 0008 expone `POST /api/facturas/{id}/adjuntos` y `DELETE .../{adjuntoId}` **sin restringirlos
al estado borrador**, y ADR 0004 no tiene ningún evento de adjunto. Consecuencia: el medio probatorio
que llega tarde —el escenario más probable, porque es justo el que motivó la funcionalidad— se sube
al sistema y **no se archiva nunca**. Un adjunto eliminado después de validar tampoco desaparece de
Drive. El criterio del 100% se incumple en silencio.

Salida mínima: o los adjuntos se cierran al validar, o hay un evento `DOCUMENTACION_ACTUALIZADA` que
reempaqueta.

### A6 — Los importes se congelan al confirmar, pero las descripciones de cuenta y motivo no

ADR 0003 declara el riesgo heredado: *"si el sistema contable elimina o renumera una cuenta que un
asiento ya usó, esta aplicación no puede impedirlo. El asiento conserva el código; la descripción
deja de resolver."* Lo nombra y lo deja abierto.

El propio diseño ya tiene el patrón que lo resuelve y lo aplica en otro sitio: ADR 0006 congela los
importes al confirmar *"no referencias vivas a la factura"*, con el argumento correcto de que un
asiento confirmado es un hecho, no una vista. La descripción de la cuenta y la del motivo merecen el
mismo tratamiento y no lo reciben. Son dos columnas en `AsientoContableDetalle` y una en
`AsientoContable`. Sin ellas, un libro de compras impreso dos años después muestra códigos sin
glosa — sobre datos que, por decisión de ADR 0014, ya no tienen copia en ningún otro sistema.

### A7 — `PATCH` sin control de concurrencia, con un servicio de fondo escribiendo `Factura`

ADR 0008 define `PATCH /api/facturas/{id}` y `PATCH /api/asientos/{id}` sin `ETag` ni `If-Match`, y el
modelo no tiene `rowversion`. Con un solo usuario suena innecesario, pero el usuario no es el único
escritor: el servicio alojado de ADR 0005 promueve y escribe `Factura`, la bandera de duplicado *"se
recalcula al guardar y al validar"*, y los caminos `reabrir` / `anular` / `reactivar` tocan el mismo
agregado. Dos pestañas abiertas sobre la misma factura bastan para perder una corrección sin que nada
lo advierta — y toda corrección perdida es además una fila que `AuditoriaCorreccion` registrará como
válida.

Es una columna `rowversion` y una cabecera. El costo de añadirlo ahora es despreciable; el de
añadirlo después obliga a tocar todos los `PATCH`.

### A8 — Autenticación: sin límite de intentos y sin camino de recuperación de contraseña

ADR 0007 decide bien lo que decide: sesión de servidor, `HttpOnly`, `SameSite=Lax` justificado por la
topología, prefijo `__Host-`, derivación de clave con sal, mensajes que no revelan si el usuario
existe. Y es honesto sobre su límite (*"`HttpOnly` no hace inmune a XSS"*).

Faltan dos piezas del mismo tamaño que las que sí decidió:

- **Sin límite de intentos ni bloqueo temporal.** ADR 0012 menciona los límites de tasa como una
  ventaja del proxy inverso, pero ningún ADR decide configurarlos. Un formulario de login sin
  freno es exactamente donde la elección de Argon2id deja de importar.
- **Sin camino de recuperación.** Hay un usuario, ningún rol de administrador y ninguna pantalla de
  cambio o restablecimiento de contraseña. Una contraseña olvidada se resuelve con un `UPDATE` a
  mano sobre la base — que es la misma base de la contabilidad de la compañía (C7). Es un
  procedimiento de operación que hay que decidir, no un descuido menor.

### A9 — La métrica de precisión ≥90% está construida para reportar de más

Dos sesgos, ninguno declarado en ADR 0017:

1. **Mezcla dos poblaciones con riesgo opuesto.** Con XML como fuente prioritaria, los campos de un
   comprobante electrónico son exactos por construcción: esa población puntúa ≈100%. El riesgo real
   —el PDF escaneado sin XML— quedará diluido en el promedio. Si el 80% de las facturas trae XML,
   la métrica global supera el 90% aunque el OCR acierte poco más de la mitad.
2. **Solo cuenta los errores que el asistente notó.** La medición compara `DatosExtraidos` contra la
   factura ya corregida; un campo mal extraído y no advertido cuenta como acierto. El sesgo apunta
   siempre hacia arriba.

ADR 0017 dice, con razón, que esa comparación *"mide, no sustituye una prueba previa con facturas
reales"*. Lo que falta es que la métrica se reporte **partida por fuente** —XML frente a PDF—, que es
donde la cifra dice algo accionable.

### A10 — `SugerenciaCuenta` arranca vacía teniendo el histórico en la misma base

> **Vigente.** `REGLAS.md` §3 fija la cascada de sugerencia y `DECISIONES` §C1b el mecanismo, pero
> ninguno resuelve la carga inicial pese a que §C3 confirma que el histórico contable de la compañía
> vive en la misma base.

ADR 0011 reconoce el costo: *"la sugerencia no generaliza a proveedores nuevos. La primera factura de
un proveedor cae siempre al segundo o tercer nivel de la cascada."* En el arranque **todas** las
facturas caen al tercer nivel —"primera candidata del motivo"—, que para un motivo con 34 candidatas
es prácticamente arbitrario. El criterio de menos de 5 minutos por factura está en su peor momento
justo cuando se forma la confianza del usuario en el sistema.

El insumo para evitarlo ya existe y está al alcance: los asientos históricos de la compañía viven en
la misma base (ADR 0003). Una carga inicial que cuente `(proveedor, cuenta)` sobre el histórico
siembra `SugerenciaCuenta` con el conocimiento real de la compañía desde la primera factura, sin
cambiar el mecanismo ni perder explicabilidad — el fundamento que se muestra al usuario sigue siendo
un número.

Merece por lo menos quedar registrado como decisión considerada, no ausente.

### A11 — `DIFERIBLE` no notifica nada, y una cuota agotada por la mañana detiene el día en silencio

El criterio del PRD dice: *"agotados los reintentos, la notificación se envía en un máximo de 5
minutos"*. Para un error `DIFERIBLE`, los reintentos **no se agotan**: se reprograman a la apertura de
la ventana de cuota, que en una cuota diaria es al día siguiente. Es decir: el estado que más tiempo
mantiene el sistema degradado es el único que no dispara ninguna notificación.

Combinado con el riesgo aceptado del worker —*"una bandeja sin facturas nuevas es indistinguible de
un día sin facturas"*— el resultado es el mismo modo de fallo: nada llega, nada avisa. La incidencia
existe en el panel, pero solo la ve quien va a buscarla.

Cerrarlo es cambiar el disparador: notificar **al entrar** en `DIFERIBLE`, no al agotar.

### A12 — Los permisos de base de datos y los derechos DDL se asumen en dos ADRs y no se deciden en ninguno

ADR 0003 apoya su propuesta de valor en que la partición es *"implementable en el motor"* y que las
cuatro clases *"permiten refuerzo real con permisos por usuario de base de datos"*. ADR 0016 versiona
el esquema con una herramienta neutral que aplica DDL como paso previo al despliegue.

Ninguno de los dos decide nada al respecto: no hay usuarios de base de datos, ni matriz de permisos,
ni los permisos entran en el SQL versionado de ADR 0016. La propiedad más fuerte que reivindica
ADR 0003 —*"nadie escribe una tabla externa"*— queda sostenida por convención, que es exactamente
aquello sobre lo que ADR 0003 dice mejorar.

Y hay una pregunta previa que ningún ADR formula: **¿este proyecto tiene derecho a ejecutar DDL en
esa base?** Es la base del sistema contable de la compañía. Muchos proveedores de software contable
condicionan o retiran el soporte si aparecen objetos de terceros en su base. ADR 0016 da esos derechos
por descontados, igual que ADR 0014 da por descontado el control del respaldo (C7). Es la misma
suposición no verificada apareciendo en tres ADRs distintos.

---

### A13 — `REGLAS.md` no define el asiento de una nota de crédito sobre una boleta

**Objetivo:** `REGLAS.md` §5 y §7.

§5 define un solo asiento de nota de crédito, y siempre tiene tres líneas con `401111` al Haber. §7
lo convierte en invariante: para el tipo `07`, cargos al Haber = base, `401111` al Haber = IGV,
proveedor al Debe = total.

Pero una boleta `03` —y una factura `EXONERADA` o `INAFECTA`— se registró con **dos** líneas y el IGV
incorporado al costo, por decisión del propio §5. Su nota de crédito tiene que ser el espejo de
**eso**: dos líneas, sin `401111`. Ni §5 ni §7 contemplan el caso, y §8 no prohíbe una nota de
crédito sobre una boleta.

Quien implemente §7 literalmente hará una de dos cosas, ambas malas: rechazar una nota de crédito
legítima, o generar una línea de IGV que revierte un crédito fiscal que nunca se tomó.

**Dirección:** una fila más en la tabla de §7 y un bloque más en §5. El criterio ya está decidido —el
espejo hereda la estructura del documento que rectifica—; solo falta escribirlo.

---

### A14 — El bloque destino se genera contra un catálogo externo que puede cambiar entre la factura y su nota de crédito

**Objetivo:** `REGLAS.md` §5 bloque DESTINO; ADR 0006 (congelamiento); `DECISIONES-REVISION.md` §C3.

El bloque destino se deriva de `ctarefleja` y `ctapuente`, **columnas del catálogo externo**
`CuentaContable`, que mantiene el sistema contable de la compañía y sobre el que esta aplicación solo
tiene `SELECT` (`DECISIONES` §C3).

ADR 0006 congela los **importes** al confirmar. No congela el **mapeo**. De ahí salen dos huecos:

1. Si `ctarefleja` de la cuenta heredada cambia entre la confirmación de la factura y la de su nota
   de crédito, el espejo revierte contra una cuenta de destino distinta de la que cargó. Las dos
   quedan con saldo y nada lo señala, porque cada asiento cuadra por separado.
2. Si una cuenta que hoy declara `ctarefleja` deja de declararla, un asiento reabierto y reconfirmado
   pierde su bloque destino sin que ninguna invariante de §7 lo note: esa invariante se enuncia como
   *"para cada línea principal **cuya cuenta declare `ctarefleja`**"*, y si ya no lo declara, la
   comprobación se satisface vacía.

Es la misma familia que A6, pero con más consecuencia: A6 afecta a lo que se muestra, esto a lo que
se contabiliza.

**Dirección:** persistir `ctarefleja` y `ctapuente` en la línea del asiento al confirmar, junto a los
importes que ya se congelan. Es una columna, y convierte el asiento en el documento autocontenido que
ADR 0006 quiso conseguir congelando los importes.

---

### A15 — Bloquear la validación con `P0000` contradice al PRD, y se apoya en un supuesto no verificado

> **Origen.** Este hallazgo se reportó como crítico en la primera pasada (C7) y bajó a advertencia al
> leer el corpus contable.
>
> **Estado tras los documentos aportados.** `DECISIONES-REVISION.md` §"C3 — Asiento contra el
> proveedor genérico" decide el bloqueo de forma explícita y lo argumenta con la razón correcta y
> específica del dominio: `421211` es una cuenta por pagar **por proveedor**, y un saldo acumulado
> contra "Varios" no se puede conciliar ni pagar porque no se sabe a quién se le debe. `REGLAS.md`
> lo eleva a invariante global 4 y a regla de rechazo. La decisión está tomada, razonada y escrita:
> **no es un hueco de diseño.**
>
> Sobreviven dos cosas. La primera: `DECISIONES-REVISION.md` presenta como *"ventaja operativa"* que
> el asistente *"sale, lo registra, vuelve y lo encuentra"* — vuelve a afirmar la inmediatez del alta
> sin que ningún documento establezca que esa persona tenga el permiso en el sistema contable de la
> compañía. La segunda: el PRD sigue pidiendo por escrito lo contrario (ver C6). El texto original se
> conserva por esos dos puntos.


**Objetivo:** ADR 0006, invariante global 4; `TECH-DESIGN.md`, Flujo 3; ADR 0003, alternativa
descartada.

El TDD: *"Validar con proveedor `P0000 (Varios)` es rechazado con `409`. El asistente registra al
proveedor **en el sistema externo** y vuelve a seleccionarlo; **no existe alta desde esta
aplicación**."*

El PRD dice tres cosas incompatibles con eso:

1. Alcance: *"De no existir se elige el proveedor **P0000** (Varios). Se debe de mostrar un mensaje
   que falta registrar al proveedor."* Un mensaje, no un bloqueo.
2. Caso borde: *"El asiento contable **se genera** con el proveedor genérico **P0000 (Varios)** por
   no detección automática; **al corregir el proveedor después**, el asiento ya generado debe
   editarse y el cambio quedar trazable."* El PRD pide expresamente que el asiento exista con
   `P0000` y se corrija más tarde.
3. Criterio de éxito: *"El 100% de las facturas marcadas como Validadas generan un asiento
   contable."*

El TDD invierte el punto 2 por completo. Y con él pierde sentido buena parte del andamiaje que el
propio diseño construyó para ese caso: `reabrir` con motivo, `AuditoriaCorreccion` sobre el asiento y
el evento `ASIENTO_CORREGIDO` fueron justificados, entre otras cosas, por la corrección posterior del
proveedor.

Hay además un supuesto no verificado en el que se apoya toda la cadena. ADR 0003 descarta replicar
los datos maestros con este argumento: *"el asistente registra el proveedor en el otro sistema y
vuelve **de inmediato** a seleccionarlo"*. Ningún documento del proyecto —ni el PRD, ni el
`DESIGN_BRIEF`, ni ningún ADR— establece que el asistente contable tenga permiso de alta de
proveedores en el sistema contable de la compañía, ni que ese alta sea inmediato. Si el alta la hace
otra persona o pasa por una aprobación, la factura queda bloqueada en `PENDIENTE_VALIDACION` por
tiempo indefinido, sin ningún criterio de aceptación que cubra esa espera, y la premisa que sostiene
el descarte de la replicación se cae.

**Dirección:** dos decisiones separadas. Primero, verificar y **escribir** quién puede dar de alta un
proveedor en el sistema externo y en cuánto tiempo. Segundo, si la respuesta es "inmediato y lo hace
el propio asistente", entonces el bloqueo es defendible y hay que **actualizar el PRD**, porque hoy
dice lo contrario por escrito; si no lo es, el bloqueo debe sustituirse por lo que el PRD pide —
validar con `P0000`, marcado, y corregir después por el camino que el diseño ya tiene construido.

---

## Sugerencia

### S1 — Let's Encrypt no puede emitir para `facturas.empresa.local`

ADR 0012 deja pendiente el origen del certificado y enumera tres opciones: *"autoridad interna,
Let's Encrypt o comprado"*. Con el host que el propio ADR fija —`https://facturas.empresa.local`—
solo la primera es viable: Let's Encrypt exige un dominio público validable, y `.local` está
reservado para mDNS por RFC 6762, de modo que ni Let's Encrypt ni una autoridad comercial pueden
emitir para él. La elección real es entre autoridad interna con su raíz distribuida, o cambiar el
nombre a un dominio público con DNS interno. Vale la pena corregir la lista para que la decisión
pendiente no arranque desde una opción falsa.

### S2 — La alternativa PostgreSQL de ADR 0002 dejó de ser una opción real

ADR 0002 descarta PostgreSQL *"por decisión de plataforma de la organización"*. Después, ADR 0003
revisión 3 descubre que los datos maestros los mantiene el sistema contable **en esta misma base**,
que es SQL Server. Desde ese momento PostgreSQL no era una alternativa descartada por preferencia
sino una imposibilidad, y la sección de alternativas de ADR 0002 no se revisó tras el cambio de
premisa. Una nota en ADR 0002 la deja consistente.

### S3 — La consulta de Gmail que evita el reproceso no está especificada

ADR 0017 decide que el worker aplica una etiqueta propia al correo ya ingestado y que **nunca borra**
—decisión correcta y bien argumentada—. Lo que no dice es cómo se acota la consulta de sondeo. Si la
consulta es solo por la etiqueta de origen, cada ciclo relee todo el histórico de la etiqueta y la
idempotencia recae íntegramente en `HashContenido`, lo que funciona pero crece sin límite. La consulta
efectiva —etiqueta de origen, menos etiqueta de procesado, más la fecha de inicio configurada— merece
estar escrita, porque es donde se concilia la frecuencia de sondeo con la cuota de la API de Gmail
que el TDD deja como riesgo abierto.

---

### S4 — "La primera candidata del motivo" no tiene un orden definido

**Objetivo:** `REGLAS.md` §3, tercer escalón de la cascada de sugerencia.

La cascada termina en *"si tampoco hay historial, la primera candidata del motivo"*. Las candidatas
se obtienen resolviendo prefijos contra las 907 hojas de 6 dígitos, y el propio §3 muestra que el
motivo 70 tiene **34** y el motivo 6 tiene **20**. "La primera" sin `ORDER BY` es la que devuelva el
motor, y eso cambia con un índice nuevo o un plan distinto.

No es grave —la sugerencia nunca decide sola, y §3 lo dice— pero produce un comportamiento que parece
un error cuando la misma pantalla propone cuentas distintas en dos días. Una cláusula
`ORDER BY CuentaCodigo` lo cierra.

---

## Apéndice · Hallazgos retirados en la segunda pasada

Cuatro hallazgos de la primera pasada quedaron sin fundamento al leer el corpus contable. Se retiran
del cuerpo del informe y se dejan aquí en una línea cada uno, para que quede constancia de qué se
reportó y por qué dejó de sostenerse.

> Los identificadores de esta tabla son los de la **primera pasada**. Al retirarlos, el resto del
> informe se renumeró de corrido, de modo que `C1`, `A6`, `A7` y `S3` designan hoy otros hallazgos.

| # en la 1.ª pasada | Lo que afirmaba | Por qué se retira |
|---|---|---|
| **C1** *(1.ª pasada)* | Cinco documentos normativos citados por el TDD no existían en el repositorio | Estaban fuera del alcance entregado, no ausentes. `REGLAS.md` es normativo y suficiente; `MOTIVOS-CLASIFICACION.md` marca los 22 motivos con `†`; `DECISIONES-REVISION.md` añade que la reclasificación vive en la satélite `MotivoAtributo`, de modo que revertirla es actualizar una tabla y no editar el plan contable |
| **A6** *(1.ª pasada)* | Sin publicación de la SBS los fines de semana, una factura de sábado nunca tendría tipo de cambio | La premisa era falsa. La SBS publica **por las noches** y lo del viernes cubre sábado, domingo y lunes (`REGLAS.md` §6). Para el caso real —que no publique— la regla está escrita: primero se carga la fila con `Origen = "MANUAL"`, y recién entonces la factura en moneda extranjera se abre para edición |
| **A7** *(1.ª pasada)* | La regla del domingo no tiene ADR y sus salidas son falsear la fecha o perder el documento | `REGLAS.md` §8 recoge la regla, su alcance exacto (aplica a `01`, `03` y `07`; sábados sí, feriados no se controlan) y, bajo el título "Consecuencia conocida", justo el costo señalado: una nota de crédito emitida en domingo deja la rectificación bloqueada hasta que el proveedor reemita |
| **S3** *(1.ª pasada)* | Al dividir el cargo entre varias cuentas queda un céntimo residual sin dueño | `DECISIONES-REVISION.md` §A5 lo cierra: la suma de las líneas divididas iguala `basePEN`, que es el valor **derivado** y por tanto siempre cuadra. De paso elimina la tolerancia de un céntimo del prototipo |

Un residuo menor que sí sobrevive al retiro del primero, y que no llega a hallazgo: `PREGUNTAS-CONTABLES.md` tiene los
diecisiete bloques `**Respuesta:**` vacíos. Las respuestas viven en `DECISIONES-REVISION.md` y
`REGLAS.md` lo dice, pero quien abra el cuestionario primero concluirá que nada se respondió. Un
enlace en la cabecera lo cierra.

---

## Lo que aguantó el escrutinio

Estas decisiones se cuestionaron y no se encontró un problema real. Se dicen por su nombre, no por
cortesía:

- **ADR 0009 (signals sin librería de estado).** Proporcionado a la escala, con dos alternativas
  genuinamente distintas —NgRx y `BehaviorSubject`—, ambas descartadas por razones específicas y no
  por moda, y con costos reales declarados (sin *devtools*, la disciplina de no exponer signals
  escribibles es convención). El único hueco es el intervalo de sondeo del cliente, que el propio ADR
  registra como riesgo abierto. No hay nada que objetar.
- **ADR 0011, eliminación de `FacturaDetalle` y `Producto`.** Detectar que nada alimentaba esas
  tablas y matarlas es el hallazgo más valioso de todo el conjunto de ADRs. La decisión de almacenar
  **prefijos** en vez de cuentas expandidas, con el argumento de que expandir congelaría una foto, es
  correcta y no obvia.
- **ADR 0017, asociación PDF ↔ XML.** La regla —coincidencia exacta de los cuatro componentes
  normalizados, y ante la duda el PDF queda sin asociar— es la elección correcta, y la lista
  explícita de evidencia insuficiente (asunto, remitente, fecha, posición del adjunto) es exactamente
  la clase de precisión que evita que alguien "mejore" el sistema con una heurística de proximidad.
- **ADR 0010, desviación del "3 reintentos" del PRD.** Es una contradicción del PRD, pero está
  argumentada, con las tres clases justificadas por comportamiento observable y no por gusto, y con
  el costo asimétrico de clasificar mal declarado y resuelto ("errar hacia transitorio").
- **ADR 0016.** Sólido de principio a fin. El argumento de que el esquema **es** el contrato de
  integración y por eso no puede pertenecer al ORM de uno de los dos participantes es la mejor pieza
  de razonamiento del documento. Su único hueco es A12, que no es suyo sino compartido.
- **ADR 0004, rechazo de la cola externa.** El motivo —una cola externa no participa de la
  transacción de SQL Server y reintroduce el *dual write*— es técnicamente correcto y no es el
  argumento fácil ("es demasiado para nuestro tamaño"). Y la corrección explícita del error de la
  versión anterior sobre la idempotencia del outbox está bien hecha: nombra el error, explica por qué
  era el más peligroso y dice qué trabajo desactivaba.
- **ADR 0006, congelar importes y asignar el correlativo al confirmar.** Las dos decisiones son
  correctas y sus fundamentos son específicos del dominio, no genéricos. Los problemas señalados en
  A1 son de mecanismo, no de criterio.

Del corpus contable aportado en la segunda pasada, tres cosas más:

- **`REGLAS.md` §6, derivar la base y anclar el IGV.** El fundamento —el IGV sustenta el crédito
  fiscal y debe ser exacto respecto del comprobante, y el céntimo lo absorbe la cuenta de cargo,
  donde no tiene consecuencia tributaria— es específico del dominio, no una preferencia de
  redondeo. Y elimina de raíz la clase entera de controles con tolerancia.
- **`MOTIVOS-CLASIFICACION.md`, la advertencia sobre los tres motivos raros.** El documento detecta
  por su cuenta que los motivos 5, 53 y 88 apuntan a cuentas de efectivo y por cobrar, avisa de que
  *"generarán un asiento que cuadra pero no representa una compra"* y recomienda dejarlos en `07`.
  Eso es exactamente lo que esta revisión habría reportado. Ya está reportado.
- **`DECISIONES-REVISION.md`, la disciplina de declarar supuestos propios.** En al menos cuatro
  puntos —las precondiciones de la nota de crédito, el tope contra monto total y no contra base, el
  alcance del bloqueo por tipo de cambio— el documento marca explícitamente *"esto lo asumí yo, no lo
  decidiste"*. Un documento de decisiones que distingue lo decidido de lo supuesto es raro y vale
  más que su contenido.

---

## Cierre

Con el corpus completo a la vista, el juicio sobre el diseño **mejora**, no empeora. `REGLAS.md` es
un documento normativo de verdad —invariantes numeradas, reglas de rechazo con su salida, cinco
ejemplos cuadrados— y `DECISIONES-REVISION.md` es una pieza poco común: registra el porqué de cada
decisión, corrige errores de la revisión anterior en vez de acatarlos, y marca sus propios supuestos.
Cuatro hallazgos se retiran enteros —uno de ellos crítico— y otros dos bajan de severidad, porque el
corpus ya los respondía.

Lo que queda se agrupa en cuatro clases, y las cuatro son de la misma naturaleza — no hay una sola
decisión mal tomada en la lista:

1. **Decisiones correctas que se cruzaron y nadie volvió a mirar juntas.** C2 (ADR 0005 contra sí
   misma), C3 (ADR 0004 contra ADR 0010), C4 (ADR 0008 contra el ciclo de vida de `REGLAS.md` §9),
   C11 (la regla de rechazo de `REGLAS.md` §8 contra el modelo que ADR 0011 dejó). Cada pieza es
   defendible leída sola. El daño aparece en el borde entre dos.
2. **Invariantes afirmadas sin mecanismo, o con un mecanismo que las contradice.** C1 (el índice
   único contra el flujo de resolución de duplicados, dentro de la misma sección), C10 (la consulta
   del tope filtra por un estado que la anulación no toca), C5 (la clave de *upsert* nunca definida),
   A1 y A12. En tres de los cinco casos el único mecanismo escrito hace lo contrario de lo enunciado.
3. **El eje temporal del asiento, que nadie recorrió entero.** C9 (el tipo de cambio de la nota de
   crédito), A14 (`ctarefleja` sin congelar), A6 (descripciones sin congelar). El diseño pensó muy
   bien la factura y su asiento en un instante, y muy poco el par factura–nota separado por semanas
   sobre catálogos y cotizaciones que se mueven.
4. **Supuestos sobre lo que hay fuera del proyecto.** Que el alta de proveedor es inmediata y la hace
   el propio asistente (A15), que se puede respaldar y restaurar esa base (C7), que se puede ejecutar
   DDL en ella (A12). Los tres se apoyan en la misma premisa no verificada — y `DECISIONES` §C3 la
   agravó al confirmar que la base es **compartida** con el sistema contable de la compañía.

Y dos notas sobre el orden de trabajo, que sustituyen a la de la primera pasada:

- **C10 y C11 son baratos y bloquean cosas caras.** El primero es una cláusula `JOIN`; el segundo, un
  booleano calculado en la extracción. Ambos protegen crédito fiscal, que es donde el error tiene
  consecuencia tributaria y no solo contable.
- **C9 va antes de escribir la primera línea del generador de asientos.** No es un bug a corregir
  después: es una regla que falta y que determina qué número se escribe en cada nota de crédito en
  moneda extranjera. Corregirla más tarde significa reprocesar asientos ya confirmados, que es
  exactamente lo que `REGLAS.md` §9 diseñó el correlativo para hacer difícil.

Sobre `REGLAS.md` §12: el documento declara su propio núcleo pendiente de ratificación por un
contador. Esa revisión debería incluir C9 y A13, que son del mismo capítulo y hoy no están en la
lista de §12.
