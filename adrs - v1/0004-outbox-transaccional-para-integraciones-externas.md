# ADR 0004: Outbox transaccional para las integraciones posteriores a la validación

## Estado

Aceptado

## Contexto

El PRD define tres acciones que se disparan cuando el asistente contable marca una factura como
"Validada":

- Creación automática de una carpeta en Google Drive con la factura y sus medios probatorios.
- Carga de la información hacia una hoja de cálculo de Google Sheets, fuente de datos del dashboard
  de Looker Studio, "justo después de que el asistente contable confirma el registro, no por un
  intervalo programado".
- Notificación de error por Telegram y/o correo cuando alguna de estas operaciones falla, tras
  agotar los 3 reintentos.

Esto crea una tensión: la validación es un **evento de negocio**, propiedad de .NET según la ADR
0003, mientras que Drive, Sheets, Telegram y correo son **integraciones externas**, propiedad de
Python. Además, todas ellas dependen de servicios de terceros que pueden estar caídos.

La consecuencia inaceptable sería que una caída de Google Drive impidiera validar una factura: la
validación es una decisión contable del usuario y no puede quedar condicionada a la disponibilidad
de un servicio externo. Igualmente inaceptable sería perder la solicitud de archivado si el proceso
se reinicia entre la validación y la ejecución de la integración.

## Decisión

Se adopta el patrón **outbox transaccional** para todas las integraciones externas disparadas por
eventos de negocio:

1. Cuando .NET valida una factura, **dentro de la misma transacción de base de datos** actualiza el
   estado de la factura y crea el registro correspondiente en la tabla de salida, en estado
   `PENDIENTE`. Ambas escrituras se confirman o se revierten juntas: no existe un estado en el que
   la factura quede validada sin su solicitud de integración encolada, ni al revés.
2. Python consume las tareas pendientes de esa tabla y ejecuta las integraciones externas —Google
   Drive, Google Sheets, Telegram y correo— incluyendo la política de reintentos definida en el PRD
   (hasta 3 intentos antes de marcar error y notificar).
3. **Cada integración mantiene su propio estado de forma independiente**, de modo que un reintento
   nunca reprocese una operación que ya se completó con éxito. Si la carpeta de Drive se creó
   correctamente pero falló la sincronización con Sheets, el reintento ejecuta únicamente la
   sincronización pendiente.

.NET nunca invoca a Google ni a Telegram; su responsabilidad termina al registrar la intención
dentro de la transacción de negocio. Python nunca decide cuándo debe archivarse una factura; su
responsabilidad empieza al leer una tarea ya comprometida.

## Alternativas consideradas

- **.NET ejecuta las integraciones directamente con un `BackgroundService` propio** — Mantenía el
  evento de negocio y su consecuencia en el mismo componente, y el estado de "archivado en Drive"
  como atributo natural de la factura. Se descartó porque repartiría la política de reintentos entre
  dos componentes (Gmail y OCR en Python; Drive, Sheets y notificaciones en .NET), duplicando el
  mecanismo de resiliencia, y porque incorporaría dependencias de Google y Telegram al componente
  que debe concentrarse en el dominio contable.
- **Llamada síncrona de .NET al worker Python durante la validación** — Habría dado
  retroalimentación inmediata al usuario sobre el resultado del archivado. Se descartó porque acopla
  la validación contable a la disponibilidad de servicios externos: una caída de Drive haría fallar
  la validación de la factura, que es precisamente el escenario que esta decisión busca evitar.
- **Publicación del evento en una cola de mensajería externa** (por ejemplo RabbitMQ o Azure Service
  Bus) — Es la solución canónica de desacoplamiento y aporta reintentos y colas de mensajes
  fallidos. Se descartó porque introduce una pieza de infraestructura adicional que operar y
  monitorear, sin ventaja real a un volumen de 10 a 50 facturas diarias con un solo usuario, y
  porque una cola externa no participa de la transacción de base de datos: seguiría requiriendo un
  outbox para garantizar la atomicidad entre la validación y el encolado.

## Consecuencias

- La validación de una factura nunca falla por indisponibilidad de Google Drive, Google Sheets,
  Telegram o el servidor de correo: el usuario completa su trabajo contable y la integración se
  resuelve después.
- No se pierden solicitudes de integración: al estar comprometidas en la misma transacción que la
  factura, sobreviven a reinicios del worker y de la API.
- El estado independiente por integración hace que los reintentos sean seguros de repetir, evitando
  carpetas duplicadas en Drive o filas repetidas en la hoja de cálculo.
- La tabla de salida es también la fuente natural del panel de errores del PRD: qué falló, cuándo,
  cuántos reintentos se hicieron y si la notificación de respaldo se envió.
- **Costo:** las integraciones son eventualmente consistentes. Entre la validación y la creación de
  la carpeta en Drive existe una ventana en la que la factura está validada pero aún no archivada, y
  la interfaz debe representar ese estado intermedio en lugar de asumir que validar equivale a
  archivar.
- **Costo:** la tabla de salida se convierte en una segunda frontera de contrato entre .NET y
  Python, con su propio formato de tarea, sus estados y su versionado, que hay que mantener
  disciplinadamente sincronizada entre los dos runtimes.
- **Costo:** requiere implementar mecánica de consumo robusta —toma de tareas, expiración de tareas
  colgadas y prevención de doble procesamiento— que una llamada directa no habría necesitado.
- **Costo:** el criterio de éxito del PRD sobre el dashboard ("desfase máximo de 24 horas") y la
  expectativa de sincronización inmediata dependen ahora de la frecuencia de sondeo del worker, que
  pasa a ser un parámetro operativo a definir y vigilar.
