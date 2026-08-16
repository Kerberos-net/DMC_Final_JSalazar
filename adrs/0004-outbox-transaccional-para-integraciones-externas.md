# ADR 0004: Contratos de mensajería entre .NET y Python

## Estado

Aceptado. Revisión 3. Sustituye la garantía de orden por agregado —incompatible con los reintentos
diferidos de ADR 0010— por una **guarda de obsolescencia** en el destino, añade el evento
`DOCUMENTACION_ACTUALIZADA` y da a `InboxEvent` una marca de consumo con resultado (revisión
adversarial v2, C3, A5, C2).

La revisión 2 reemplazó la versión previa (`adrs - v1/0004`), que tenía un solo evento,
sobreafirmaba la idempotencia del outbox y rechazaba una alternativa por la razón equivocada.

## Contexto

Al validar una factura ocurren cinco cosas: cambia el estado de la factura, se confirma el asiento,
se incrementa el contador de sugerencia, se archiva la documentación en Drive y se sincroniza la
fila hacia Google Sheets. Las tres primeras son transaccionales; las dos últimas son llamadas a
terceros que pueden fallar o tardar.

Escribir la base y llamar a Drive dentro de la misma operación es un *dual write*: si Drive responde
y la transacción luego falla, hay carpeta sin factura validada; si la transacción confirma y Drive
falla, hay factura validada sin carpeta.

Además el sistema necesita dos canales más que la versión anterior no contemplaba:

- **Órdenes del usuario que ejecuta Python.** `REPROCESAR` una incidencia y "Sincronizar ahora" no
  son hechos ocurridos: son peticiones. No tienen transacción de negocio a la que subirse.
- **Notificaciones de Python hacia .NET.** Cuando termina un procesamiento, .NET debe enterarse sin
  sondear una tabla privada del worker.

## Decisión

Tres tablas de contrato, con **semánticas separadas y no intercambiables**.

### `OutboxEvent` — hechos de negocio

Representa **lo que ya ocurrió**. La modificación del dominio y la creación del evento se escriben
**en la misma transacción de SQL Server**. Los eventos son **inmutables**: cada operación genera un
registro nuevo, ninguno se edita.

| Evento | Se emite al |
|---|---|
| `FACTURA_VALIDADA` | Validar la factura y confirmar su asiento |
| `FACTURA_CORREGIDA` | Corregir datos de la factura |
| `ASIENTO_CORREGIDO` | Reconfirmar un asiento tras reapertura (ADR 0006) |
| `ASIENTO_ANULADO` | Anular el asiento |
| `DOCUMENTACION_ACTUALIZADA` | Añadir o eliminar un adjunto **después** de validar (ADR 0013) |

Cada evento **declara en su *payload* qué información debe actualizarse**. No existe un evento
genérico de reconciliación de estado.

El *payload* debe ser **autosuficiente**: lleva el estado completo de lo que el destino tiene que
reflejar, nunca un delta. Es lo que permite que un evento obsoleto se pueda **descartar entero** sin
dejar el destino a medias. En concreto, la lista de rutas de documentos viaja completa y de ambos
orígenes, para que Python no lea tablas de .NET (ADR 0003).

`DOCUMENTACION_ACTUALIZADA` existe porque el empaquetado hacia Drive usaba la lista congelada en
`FACTURA_VALIDADA`, y un medio probatorio que llega **después** de validar —el caso más probable, y
el que motivó `AdjuntoManual`— no se archivaba nunca. Solo sincroniza Drive: los adjuntos no son un
dato del dashboard.

### `CommandQueue` — órdenes de ejecución

Representa **lo que hay que hacer**. Originado por el usuario o por un proceso interno.

`REPROCESAR_DOCUMENTO` · `SINCRONIZAR_GMAIL` · `SINCRONIZAR_SBS`

Angular invoca la API; .NET **valida la solicitud** y registra el comando con referencia, *payload*,
estado, intentos y `CorrelationId`. Python consume, ejecuta y actualiza el estado. Los resultados
relevantes **pueden generar eventos en `OutboxEvent`**.

### `InboxEvent` — hechos de procesamiento

Dirección inversa. Python informa que un procesamiento terminó; .NET decide qué hacer (ADR 0005).

`InboxEvent` lleva el **resultado del consumo**, no solo la marca de que ocurrió:

```
EstadoConsumo    PENDIENTE | PROMOVIDO | DESCARTADO
ConsumidoEn      cuándo
FacturaId        si PROMOVIDO
MotivoDescarte   si DESCARTADO
```

Sin esto, un documento que .NET decide **no promover** —adjunto corrupto, XML inválido, PDF
ambiguo— se reconsume en cada ciclo para siempre: el índice único que garantiza la idempotencia de la
promoción (ADR 0005) cubre *"no crear dos facturas"*, no *"no volver a intentarlo"*. Y registrar el
resultado, no solo el consumo, permite responder **cuántos documentos procesados no llegaron a
factura y por qué**, que es la pregunta que le interesa al operador y que ninguna tabla contestaba.

### Garantías comunes

- **Entrega al menos una vez.** El consumidor implementa idempotencia. Siempre.
- **El efecto final corresponde al evento más reciente.** Cada evento lleva una `Secuencia` monótona
  por agregado, y cada fila del destino registra la del último evento aplicado. Al aplicar un evento
  cuya secuencia no supera la registrada, se **descarta como `OBSOLETO`** —estado terminal, sin error
  y sin notificación— y el destino no se toca.
- **Estado independiente por integración.** Si Drive se completó y Sheets falló, el reintento
  ejecuta únicamente Sheets.
- **Clave de sincronización inmutable.** El destino se identifica por `FacturaId`, nunca por la
  identidad fiscal `(RUC, tipo, número)`. Ver más abajo.

> **Por qué la garantía de orden se sustituyó.** La revisión 1 prometía que *"los eventos de una
> misma factura se procesan serializados y en orden de creación"*. Esa promesa es **incompatible con
> ADR 0010**: un evento clasificado `DIFERIBLE` por cuota de Google no termina durante horas, y
> serializar por agregado significaría congelar todos los eventos de esa factura hasta el día
> siguiente. Sin serializar, el daño era real: un `ASIENTO_CORREGIDO` diferido reintentado mañana
> **resucita el importe** sobre un asiento anulado esta tarde.
>
> Se suma que el mecanismo de reclamo tampoco la sostenía: `READPAST` —la única dependencia de motor
> que declara ADR 0002— **salta** las filas bloqueadas, de modo que reclamar por fila nunca produjo
> orden por agregado.
>
> La guarda de obsolescencia convierte el orden de aplicación de **precondición** en **resultado**.
> El evento diferido llega tarde, comprueba que ya se aplicó uno posterior y no hace nada. Es una
> garantía más débil y más honesta: es la que el sistema puede sostener teniendo reintentos
> diferidos.

### Clave de sincronización con los destinos externos

| Destino | Clave | Dónde vive |
|---|---|---|
| Google Sheets | `FacturaId` | Columna propia de la hoja, junto a la de secuencia |
| Google Drive | `FacturaId` | `appProperties` de la carpeta, **no su nombre** |

La identidad fiscal `(RUC, tipo, número)` **no puede ser clave de nada**: el diseño contiene un flujo
cuyo propósito explícito es cambiarla —corregir el número de un duplicado— y el evento
`FACTURA_CORREGIDA` existe para propagar ese cambio. Con la identidad como clave, corregir el número
de una factura ya sincronizada produce un *upsert* que no encuentra la fila anterior e **inserta una
nueva**: Looker Studio contaría el gasto dos veces, de forma permanente y silenciosa. Corregir el
proveedor `P0000` cambia el RUC, con el mismo efecto.

El nombre de la carpeta de Drive **puede** incluir el número de comprobante, porque lo leen personas.
Lo que no puede es ser el criterio de búsqueda.

## Alternativas consideradas

- **Una sola tabla con un discriminador de tipo.** Un bucle de sondeo, una purga, un ADR. Se
  descartó porque los eventos tienen una invariante que los comandos no pueden tener: el evento se
  escribe **siempre** dentro de la transacción del hecho que lo origina. Mezclarlos en una tabla
  hace que esa invariante deje de ser verificable por construcción.
- **Un evento genérico de reconciliación de estado.** El consumidor leería el estado vigente y haría
  *upsert*, lo que lo vuelve inmune al orden de llegada. Se descartó porque un evento debe decir qué
  ocurrió: con reconciliación genérica, Telegram no puede redactar la notificación sin volver a
  consultar, y el registro de eventos deja de ser legible como historia del sistema.
- **Cola de mensajería externa (RabbitMQ, Azure Service Bus).** Es la herramienta natural. Se
  descartó porque una cola externa **no participa de la transacción de SQL Server**: publicar en ella
  al validar reintroduce exactamente el *dual write* que se quiere evitar. Además añade un
  componente que desplegar, respaldar y vigilar para un sistema de un usuario.
- **Servicio alojado en .NET ejecutando las integraciones directamente.** Se descartó **no** por usar
  un servicio alojado —.NET tiene uno legítimamente para consumir su inbox (ADR 0005)— sino porque
  situaría todo el trabajo asíncrono y su política de reintentos en el componente transaccional,
  contradiciendo el eje de separación de ADR 0002.

## Consecuencias

- La validación es atómica: o se persisten factura, asiento, contador de sugerencia y evento, o no
  se persiste ninguno. Una caída de Drive, Sheets, Telegram o SMTP **no impide validar**.
- Anular o corregir un asiento **sí llega al dashboard**. En el diseño anterior un asiento anulado
  seguía contando como gasto en Looker Studio de forma permanente.
- Comandos y eventos se razonan por separado, con reglas distintas y sin confusión posible.
- **Costo:** tres tablas, tres estados, tres políticas de purga. Python mantiene dos bucles de
  consumo y .NET uno.
- **Costo, y corrección explícita a la versión anterior:** el estado independiente por integración
  **no regala idempotencia**. Entre que la API externa responde con éxito y que se persiste el
  estado hay una ventana: un reinicio ahí duplica la carpeta o la fila. La idempotencia **se
  construye en cada integración** —buscar antes de crear en Drive, *upsert* por clave en Sheets—, y
  afirmar lo contrario era el error más peligroso del documento anterior, porque desactivaba el
  trabajo que sí hay que hacer. La clave de ese *upsert* está ahora definida: `FacturaId`.
- Los eventos de una misma factura **pueden aplicarse en paralelo** sin romper nada. `READPAST` deja
  de ser un problema, y una cuota agotada ya no detiene los demás eventos de esa factura.
- **Costo:** el *payload* de cada evento debe ser autosuficiente para reconstruir la fila entera. Un
  evento que solo trajera un delta no se puede saltar. Los cinco del catálogo lo cumplen, y **es una
  restricción que hay que respetar al añadir el sexto**.
- **Costo:** la hoja de cálculo gana dos columnas técnicas —`FacturaId` y la secuencia— visibles para
  quien la abra. Looker Studio no las usa.
- **Costo:** un evento que no aplica a un destino —`DOCUMENTACION_ACTUALIZADA` no toca Sheets— se
  marca aplicado **sin avanzar la secuencia de ese destino**, o dejaría obsoletos a los siguientes
  que sí le aplican.
