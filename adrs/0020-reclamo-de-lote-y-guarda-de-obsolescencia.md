# ADR 0020: Reclamo de lote y guarda de obsolescencia en el consumidor de outbox

## Estado

Propuesto. Revisión 2. Nace de la implementación del ítem #14 (BACKLOG), que es el primer consumidor
real de `fact.OutboxEvent`. ADR 0004 decidió **que** existe una guarda de obsolescencia y ADR 0002
declaró `READPAST` como su única excepción de portabilidad; ninguno de los dos dice **cómo** se
reclama una fila, **quién** materializa la fila hija por integración, ni **cómo** se impide que
`OBSOLETO` se confunda con un error. Este ADR no reabre esas decisiones: las hace ejecutables.

La revisión 2 añade la **decisión 5** y el hueco 5 del contexto: al diseñar el ítem #14 se descubrió
que el predicado que decide si un evento se emite (`Factura.Estado == 'VALIDADA'`) nunca es cierto en
producción. Es el mismo tipo de hueco que los otros cuatro —una promesa de ADR 0004 sin dato debajo—
y por eso vive aquí y no en un ADR aparte; si los ítems #11 o #13 acaban necesitando citar la
transición de estado por sí sola, esa decisión se promueve a ADR propio.

## Contexto

Al construir el consumidor aparecen cinco huecos que el esquema del ítem #1 y los ADR vigentes
dejan abiertos, y que no se pueden resolver en silencio dentro de un módulo:

1. **`fact.OutboxEventIntegracion` nunca se llena.** `SqlUnidadDeTrabajo.EmitirOutboxAsync` inserta
   la fila padre y nada más. `008_usuarios_y_permisos.sql` concede a `fact_worker` únicamente
   `SELECT, UPDATE` sobre esa tabla: el consumidor **no puede** crear sus propias filas hijas. Sin
   filas hijas no hay estado por integración y, por lo tanto, no hay progreso contra el cual comparar
   una secuencia. La guarda de ADR 0004 no tiene de dónde leer.

2. **`CK_OutboxEventIntegracion_Estado` no tiene un estado "en proceso".** Sus cuatro valores son
   `PENDIENTE`, `COMPLETADO`, `OBSOLETO` y `ERROR` —a diferencia de `fact.CommandQueue`, que sí tiene
   `EN_PROCESO`—. Un reclamo no puede anunciarse cambiando de estado, y ADR 0016 hace de cualquier
   columna nueva un cambio de esquema versionado que el ítem #14 declaró fuera de alcance.

3. **`READPAST` salta filas bloqueadas, no filas reclamadas.** Si el reclamo sostiene el bloqueo
   durante todo el ciclo, la transacción de SQL Server queda abierta mientras se llama a Google Drive
   o a Sheets (ítems #15 y #16). Es exactamente el acoplamiento entre lo transaccional y lo remoto
   que ADR 0004 se propuso eliminar.

4. **`OBSOLETO` y `ERROR` viven en la misma columna.** ADR 0010 clasificará los fallos del handler en
   `TRANSITORIO`/`DIFERIBLE`/`PERMANENTE` (ítem #17). Si el descarte por obsolescencia viaja por el
   mismo camino de código que un fallo, basta un `except` compartido para que un evento superado
   empiece a contar como incidencia, a reintentarse y a notificar. ADR 0004 es explícito en lo
   contrario: es un estado terminal **sin error y sin notificación**.

5. **Nada en producción escribe nunca `fact.Factura.Estado = 'VALIDADA'`.** `ValidarInternoAsync`
   confirma el asiento (`GuardarAsientoAsync`) y ahí termina; los únicos `UPDATE fact.Factura` del
   sistema son `GuardarFacturaAsync` (dirigido por PATCH) y `ConfirmarAfectacionAsync` (solo
   `AfectacionMixta`), y `005_negocio.sql` no tiene ningún trigger. La columna existe, tiene su
   `CK_Factura_Estado`, y su valor `VALIDADA` es inalcanzable. Consecuencia directa para este ADR:
   las tres guardas `Estado == VALIDADA` de `ServicioDeFacturas` —y con ellas la emisión de
   `DOCUMENTACION_ACTUALIZADA` y la nueva de `FACTURA_CORREGIDA`— son **código muerto**. Las pruebas
   unitarias pasan únicamente porque fijan el estado a mano. Un consumidor construido sobre un
   predicado que nunca es cierto no es un consumidor: es una tubería vacía con pruebas verdes.

## Decisión

### 1 · La fila hija la escribe `fact_api`, y solo para los destinos a los que el evento aplica

`EmitirOutboxAsync` inserta, **en la misma transacción** que el evento, una fila de
`fact.OutboxEventIntegracion` por cada integración a la que ese `Tipo` aplica. El mapa
`Tipo → Integracion` es el de la tabla de ADR 0004 (`DOCUMENTACION_ACTUALIZADA` solo sincroniza
Drive) y vive en **Infrastructure**, nunca en el núcleo contable: el dominio emite un hecho, no
decide destinos (ADR 0019).

Esto además resuelve por construcción el último costo declarado de ADR 0004 —*"un evento que no
aplica a un destino se marca aplicado sin avanzar la secuencia de ese destino"*—: si no aplica, no
hay fila; si no hay fila, nunca entra en el conjunto de progreso. No hace falta una marca de
"aplicado que no avanza", que es la parte que el esquema no tiene dónde guardar.

### 2 · El reclamo es un **arrendamiento** sobre `ProximoIntentoEn`, no un bloqueo sostenido

El ciclo son tres pasos, con dos transacciones cortas y ninguna llamada remota dentro de ellas:

```
tx corta:  UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK)
           SET ProximoIntentoEn = ahora + arrendamiento
           WHERE Estado = 'PENDIENTE'
             AND Integracion IN (destinos registrados)
             AND (ProximoIntentoEn IS NULL OR ProximoIntentoEn <= ahora)
   ---     despacho al handler (fuera de toda transacción)
tx corta:  UPDATE ... SET Estado = 'COMPLETADO' | 'OBSOLETO' | 'ERROR'
```

El arrendamiento dura **5 minutos** (decidido por el dueño del proyecto). Está acotado por los dos
lados: tiene que superar con holgura la cadencia de 1 minuto para que una corrida lenta no sea
reclamada otra vez por el tic siguiente, y tiene que quedar muy por debajo del presupuesto de
visibilidad de 15 minutos de ADR 0005 para que un proceso que muere a mitad del despacho libere la
fila dentro de ese presupuesto (5 min de arrendamiento + 1 min hasta el siguiente tic ≈ 6 min en el
peor caso). Entre esos dos límites el número es una apuesta sobre la latencia del handler del ítem
#15; vive como una única constante nombrada en el módulo puro `reclamo.py`, no incrustada en el SQL,
para que revisarla sea cambiar un valor y no releer una consulta.

`ProximoIntentoEn` ya significa *"no elegible antes de T"*. Un arrendamiento es esa misma frase; la
espera por reintento de ADR 0010 también. Reusar la columna no la sobrecarga con dos sentidos: le da
uno solo. Si el proceso muere entre el paso 1 y el 3, el arrendamiento vence y la fila vuelve a ser
elegible sola, sin barrido ni proceso de rescate.

`Intentos` **no** se toca al reclamar. Contar intentos es política de reintentos y pertenece al ítem
#17.

### 3 · La guarda es una función pura que devuelve un veredicto, y nunca lanza

```
progreso(FacturaId, Integracion) = MAX(Secuencia) de las filas COMPLETADO de ese par
obsoleto  ⟺  progreso existe  ∧  Secuencia ≤ progreso
```

Se evalúa **antes** de cualquier despacho. `Obsoleto` marca la fila y termina: no se llama al
handler, no se incrementa `Intentos`, no se escribe `UltimoError`, no se notifica.

La separación es **por tipo, no por convención**: la clasificación del ítem #17 opera sobre
**excepciones lanzadas por el handler**, y la guarda no lanza ninguna. Los dos caminos no comparten
un `except` que un refactor pudiera unificar por descuido.

### 4 · `READPAST` vive en un único módulo, detrás de un `Protocol`

`outbox_repo.py` es el único archivo del proyecto que contiene la palabra `READPAST`. El despachador
depende de `ReclamoDeLote` (un `typing.Protocol` en `reclamo.py`, sin `pyodbc`). Se usa un `Protocol`
estructural y no una clase base abstracta ni un nombre con prefijo `I`: Python no tiene esa
convención y el resto del worker ya se escribe con módulos planos y *dataclasses* congeladas.

### 5 · La factura alcanza `VALIDADA` en la misma transacción que confirma su asiento, por un miembro de puerto de transición única

Se arregla la causa, no el síntoma: `ValidarInternoAsync` escribe `fact.Factura.Estado = 'VALIDADA'`
dentro de su propia transacción, en vez de reescribir el predicado del outbox para que apunte a algo
que hoy ya sea cierto (por ejemplo "el asiento tiene `NumeroAsiento`"). Rodear un estado de dominio
equivocado deja el libro de compras diciendo que ninguna factura fue validada nunca; eso no es un
detalle de mensajería.

El puerto gana **un** miembro, y su forma es la decisión:

```csharp
Task<TransicionEstadoFactura> MarcarFacturaValidadaAsync(long facturaId, CancellationToken ct);
public enum TransicionEstadoFactura { Aplicada, YaValidada, NoTransicionable }
```

```sql
UPDATE fact.Factura SET Estado = 'VALIDADA'
WHERE FacturaId = @id AND Estado = 'PENDIENTE_VALIDACION';   -- 'VALIDADA' es literal, no parámetro
```

No recibe versión ni estado destino. **Sí es un segundo camino de escritura hacia `fact.Factura`, y
sí es más débil en una dimensión: no hay CAS contra `Version`.** Conviene decirlo sin adornos, porque
era el riesgo declarado. Lo que lo hace aceptable no es que sea pequeño, sino que cambia un
compare-and-swap por otro: el predicado de versión se sustituye por un **predicado de estado**, así
que dos validaciones concurrentes siguen sin poder aplicar las dos —la perdedora lee `@@ROWCOUNT = 0`—
y lo único que se pierde es la concurrencia optimista *del cliente*, que en esta ruta nunca existió
para la factura: el `If-Match` de `POST /validar` es el ETag del **asiento**. Además el miembro solo
corre después de que `GuardarAsientoAsync` devolvió `Aplicado`, es decir después de que el CAS del
asiento ya ganó: la escritura de la factura es la *consecuencia* de una transición protegida, no una
mutación independiente. Y su radio de acción está cerrado por construcción —una columna, un valor
destino literal, un único estado de origen legal—: no puede resucitar una factura `DESCARTADA`, no
puede volver a `PENDIENTE_VALIDACION`, y ningún llamador futuro puede reutilizarlo para otra
transición.

El orden dentro de la transacción es normativo, no incidental: `GuardarAsientoAsync` →
`MarcarFacturaValidadaAsync` → construcción del payload → `EmitirOutboxAsync` → `CommitAsync`. El
payload se arma releyendo por el puerto (la transacción ve sus propias escrituras); emitir antes de
la transición produciría un evento `FACTURA_VALIDADA` que se contradice a sí mismo, con
`"estado": "PENDIENTE_VALIDACION"` dentro.

## Alternativas consideradas

- **Que el consumidor cree sus propias filas hijas al reclamar.** Es lo natural si se piensa el
  estado por integración como asunto del consumidor. Se descarta porque contradice la partición de
  ADR 0003 tal como está concedida: `fact_worker` no tiene `INSERT` sobre esa tabla, y ampliarlo
  significaría que el consumidor decide a qué destinos aplica un evento **después** de que el hecho
  ocurrió, con el productor sin manera de saberlo.
- **Añadir un estado `EN_PROCESO` a `CK_OutboxEventIntegracion_Estado`.** Es la solución más legible
  y la que usa `CommandQueue`. Se descarta en este ítem por ser un cambio de esquema (ADR 0016) que
  el alcance excluyó, y porque el arrendamiento sobre `ProximoIntentoEn` cubre el caso sin introducir
  un quinto estado que también habría que vencer y limpiar. **Es el punto más discutible de este
  ADR**: si el ítem #17 acaba necesitando distinguir "reclamado" de "esperando reintento", esta
  decisión debe revisarse.
- **Sostener la transacción del reclamo durante el despacho.** Elimina el arrendamiento entero: el
  bloqueo *es* el reclamo, y `READPAST` hace el resto. Se descarta porque mantiene una transacción de
  SQL Server abierta durante una llamada a la API de Google, cuya latencia y cuota no controlamos
  (ADR 0010 contempla horas de espera por cuota agotada).
- **Modelar la obsolescencia como una excepción (`EventoObsoleto`) capturada por el despachador.** Es
  cómodo: un solo punto de salida. Se descarta por el punto 3 — pone el descarte terminal en el mismo
  canal que los fallos reales, que es precisamente lo que ADR 0004 prohíbe, y lo hace de una forma
  que ninguna prueba de tipos detecta.
- **Una columna `SecuenciaAplicada` por fila de integración.** Haría el progreso una lectura directa
  en vez de un `MAX` con `JOIN`. Se descarta por ser un cambio de esquema, y porque la decisión 1
  vuelve el `MAX` correcto sin ella.
- **Cambiar el predicado de emisión de "factura VALIDADA" a "el asiento tiene `NumeroAsiento`"**
  (hueco 5). Es la alternativa barata y honesta en apariencia: usa un dato que sí es cierto hoy, y
  coincide con el razonamiento de la propia decisión D1 del ítem #14. Se descarta porque deja
  `fact.Factura.Estado` permanentemente equivocado y traslada el problema a quien lea la bandeja, no
  a quien lea el outbox. La corrección contable manda sobre la comodidad del mensajero.
- **Reutilizar `GuardarFacturaAsync` releyendo la `Version` dentro de la transacción.** Evita el
  miembro nuevo. Se descarta por dos motivos: el CAS sería tautológico —se compara contra una versión
  leída microsegundos antes bajo READ COMMITTED—, de modo que un PATCH concurrente entre el `SELECT`
  y el `UPDATE` devolvería **412 a una petición de `validar` cuyo `If-Match` era del asiento**, que
  es mentirle al cliente; y ese `UPDATE` reescribe las ocho columnas editables desde nuestro
  snapshot, convirtiendo una transición de estado en una ventana de *lost update* de fila completa.
- **Que `SqlUnidadDeTrabajo.GuardarAsientoAsync` escriba el estado de la factura cuando el asiento
  queda `CONFIRMADO`.** Sin miembro nuevo y imposible de usar mal. Se descarta porque "confirmar un
  asiento valida su factura" es una **regla contable**, y ADR 0019 mantiene las reglas contables
  fuera de la infraestructura: `FakeUnidadDeTrabajo` tendría que reimplementarla para que las pruebas
  de Core la vieran, que es la definición de una regla viviendo en la capa equivocada. Es la imagen
  especular de la decisión 1 de este mismo ADR: los destinos **no** son una regla de dominio y por
  eso su mapa sí vive en Infrastructure.
- **Un miembro genérico `TransicionarEstadoFacturaAsync(id, esperado, nuevo)`.** Se descarta porque
  ese sí sería el segundo camino de escritura más débil que se quería evitar: sabe expresar todas las
  transiciones, incluidas las que ninguna regla permite.

## Consecuencias

- La guarda de obsolescencia de ADR 0004 pasa de ser una promesa escrita a un camino de código
  ejecutable, con un dato del cual leer.
- El productor .NET adquiere conocimiento del mapa `Tipo → Integracion`. Es un acoplamiento real:
  cuando el ítem #15 o el #16 cambien a qué destinos aplica un evento, hay que tocar Infrastructure
  en .NET, no solo el handler en Python. Se acepta porque ese mapa ya es una decisión de ADR 0004, no
  un detalle de implementación del handler.
- **Costo:** un evento emitido antes de este cambio no tiene fila hija y nunca será reclamado. Es
  aceptable únicamente porque ningún destino ha consumido nunca el outbox; deja de serlo en cuanto
  #15 esté en producción.
- El consumidor del ítem #14 es **inerte**: con el registro de destinos vacío no reclama nada y las
  filas se acumulan `PENDIENTE`, listas para #15/#16. Es deliberado —marcar `COMPLETADO` eventos que
  Drive nunca recibió los perdería en silencio—, pero significa que el ítem #14 no se puede verificar
  observando efectos: se verifica con un destino falso en pruebas.
- `ProximoIntentoEn` queda con un único significado compartido por el arrendamiento y por el
  reintento del ítem #17. Ese ítem debe respetarlo: escribir ahí una espera de reintento es también
  soltar el arrendamiento.
- El progreso de la decisión 3 es un `MAX(Secuencia)` sobre las filas `COMPLETADO` de un par
  `(FacturaId, Integracion)`. Eso presupone **un evento por hecho**: si una misma transacción
  emitiera dos veces el mismo `Tipo` para la misma factura, la decisión 1 crearía dos juegos de filas
  hijas y el destino aplicaría el hecho dos veces antes de que la guarda pudiera descartar nada. Por
  eso `EmitirOutboxAsync` lleva un conjunto de `(Tipo, FacturaId)` por transacción y **falla** ante
  una repetición en vez de insertar la segunda fila (design.md D8): la unicidad del hecho es una
  precondición de esta guarda, no una cortesía del productor.
- **Costo:** `READPAST` solo se puede probar de verdad con dos conexiones concurrentes contra una
  instancia real de SQL Server. Esa prueba es de nivel 2 (ADR 0019) y se omite —nunca se da por
  pasada— donde no hay base disponible.
- La decisión 5 cierra un hueco del ítem #11 desde el #14. El outbox deja de depender de un predicado
  falso, pero el precio es que **tres** guardas dormidas despiertan a la vez, no dos: además de
  `DOCUMENTACION_ACTUALIZADA` y `FACTURA_CORREGIDA`, se activa la de `DescartarAsync`. A partir de
  este cambio, descartar una factura validada devuelve 409 en vez de aplicar. Es exactamente lo que
  ADR 0008 dice desde siempre, pero es un cambio visible para el usuario en un endpoint existente y
  pertenece a la nota de entrega del ítem, no al descubrimiento de quien lo estrene.
- **Costo:** la transición es solo hacia adelante y no se rellenan datos históricos. Una factura
  validada antes de este cambio conserva `PENDIENTE_VALIDACION` para siempre: nunca emitirá
  `DOCUMENTACION_ACTUALIZADA` ni `FACTURA_CORREGIDA`, y seguirá siendo descartable. Es inocuo para el
  outbox —ningún destino consumió nunca nada— pero deja la bandeja comportándose distinto para filas
  viejas y nuevas de forma indefinida.
- La escritura toca `fact.Factura.Version` (es un `rowversion`), así que un PATCH en vuelo que
  sostenga el ETag anterior a la validación recibirá 412 después de validar. Es correcto —la factura
  cambió de verdad— pero es un 412 nuevo que la SPA no veía antes.
- **Decidido por el dueño:** `validar` sobre una factura `DESCARTADA` (hoy alcanzable vía
  `abrir → descartar → validar`, y hoy produce en silencio un asiento `CONFIRMADO` colgando de una
  factura descartada) **rechaza con 409 y revierte** la confirmación del asiento —`NoTransicionable`
  como estado terminal, sin escribir `Estado = VALIDADA` ni emitir `FACTURA_VALIDADA`—, consistente
  con la regla de ADR 0008 de que una factura `VALIDADA` (y, por extensión, una descartada que nunca
  llegó a estarlo por esta vía) no admite esa transición en silencio. Es un cambio de comportamiento
  visible en un endpoint existente y va en la nota de entrega del ítem.
- Queda fuera del alcance del #14, por decisión explícita, si `ReabrirAsync`/`AnularAsync` deben
  devolver la factura a `PENDIENTE_VALIDACION` —ninguno de los dos toca hoy esa columna, de modo que
  tras anular el asiento la factura sigue diciendo `VALIDADA`—. La decisión D1 del ítem #14 *depende*
  de esa asimetría para su ruta de reconfirmación, así que el ítem #14 la entrega tal cual; revertir
  el estado sería otro ítem, con su propia auditoría y sus propias consecuencias de outbox.

## Relacionado

- ADR 0002 — `READPAST` como única dependencia declarada de motor.
- ADR 0005 — el presupuesto de visibilidad de 15 minutos que acota el arrendamiento de 5.
- ADR 0006 — el ciclo de vida de la factura y del asiento sobre el que opera la decisión 5.
- ADR 0008 — la tabla de conflictos 409, incluida la regla "una factura VALIDADA no puede
  descartarse" que la decisión 5 vuelve alcanzable.
- ADR 0003 — la partición de datos que concede `INSERT` a `fact_api` y `UPDATE` a `fact_worker`.
- ADR 0004 — el catálogo de eventos, la guarda de obsolescencia y el estado por integración.
- ADR 0010 — la política de reintentos que el ítem #17 implementará sobre estas mismas columnas.
- ADR 0016 — el esquema versionado que este ADR decide **no** modificar.
- ADR 0019 — los niveles de verificación; el núcleo contable no toca infraestructura.
- `BACKLOG.md` ítems #11 (cuya transición de estado faltante cierra la decisión 5), #14, #15, #16
  y #17.
