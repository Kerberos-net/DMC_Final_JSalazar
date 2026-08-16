# ADR 0004: Contratos de mensajería entre .NET y Python

## Estado

Aceptado. Reemplaza la versión previa (`adrs - v1/0004`), que tenía un solo evento, sobreafirmaba la
idempotencia del outbox y rechazaba una alternativa por la razón equivocada.

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

Cada evento **declara en su *payload* qué información debe actualizarse**. No existe un evento
genérico de reconciliación de estado. El *payload* de `FACTURA_VALIDADA` incluye la lista completa
de rutas de documentos, de ambos orígenes, para que Python no lea tablas de .NET (ADR 0003).

### `CommandQueue` — órdenes de ejecución

Representa **lo que hay que hacer**. Originado por el usuario o por un proceso interno.

`REPROCESAR_DOCUMENTO` · `SINCRONIZAR_GMAIL` · `SINCRONIZAR_SBS`

Angular invoca la API; .NET **valida la solicitud** y registra el comando con referencia, *payload*,
estado, intentos y `CorrelationId`. Python consume, ejecuta y actualiza el estado. Los resultados
relevantes **pueden generar eventos en `OutboxEvent`**.

### `InboxEvent` — hechos de procesamiento

Dirección inversa. Python informa que un procesamiento terminó; .NET decide qué hacer (ADR 0005).

### Garantías comunes

- **Entrega al menos una vez.** El consumidor implementa idempotencia. Siempre.
- **Orden por agregado.** Los eventos de una misma factura se procesan **serializados y en orden de
  creación**; los de facturas distintas pueden procesarse en paralelo. Sin esto, `ASIENTO_ANULADO` y
  `ASIENTO_CORREGIDO` aplicados fuera de orden dejan la fila de Sheets con el dato equivocado de
  forma permanente.
- **Estado independiente por integración.** Si Drive se completó y Sheets falló, el reintento
  ejecuta únicamente Sheets.

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
  trabajo que sí hay que hacer.
- **Costo:** la serialización por factura reduce el paralelismo del consumidor. A 10-50 facturas
  diarias es irrelevante.
