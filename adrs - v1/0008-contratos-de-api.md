# ADR 0008: Contratos de comunicación entre componentes

## Estado

Aceptado

## Contexto

La ADR 0001 define tres componentes. Sus contratos de comunicación son de dos naturalezas
distintas:

- **API de negocio ↔ worker Python**: ya resuelto por las ADR 0003 y 0004. La comunicación es
  exclusivamente a través de la base de datos compartida, con propiedad de tablas separada y una
  tabla de salida transaccional. No existe ningún contrato de red entre ambos.
- **SPA Angular ↔ API de negocio**: pendiente de definir. Que el transporte sea HTTP con JSON no
  está en discusión; lo que debe decidirse es la forma del contrato.

El dominio no se reduce a operaciones CRUD. Contiene operaciones de negocio con precondiciones e
invariantes propias:

- **Validar una factura**, que en una sola transacción cambia el estado de la factura, genera el
  asiento contable con sus líneas y escribe el evento en `IntegrationOutbox`.
- **Anular** y **reactivar** un asiento contable, sin alterar el estado de validación de la factura.
- **Corregir la cuenta contable de una línea** del detalle, dejando registro de auditoría.

## Decisión

**Contrato SPA ↔ API de negocio:** HTTP/JSON con estilo **REST para consultas y edición de borrador,
más endpoints de comando explícitos para las operaciones de negocio**.

- Consultas y edición previa a la validación siguen el estilo de recursos: `GET /facturas`,
  `GET /facturas/{id}`, `PATCH /facturas/{id}` para guardar avance sin validar.
- Cada operación de negocio expone su propio endpoint, con la intención explícita en la ruta:
  `POST /facturas/{id}/validar`, `POST /asientos/{id}/anular`, `POST /asientos/{id}/reactivar`,
  `POST /asientos/{id}/lineas/{numero}/cuenta`.
- Cada endpoint de comando valida sus propias precondiciones y devuelve un error de dominio
  específico cuando no se cumplen (por ejemplo, intentar validar una factura marcada como duplicada
  sin resolver).

La API de negocio es la **propietaria del contrato**: la SPA se adapta a él y no existe ningún otro
consumidor.

**Contrato API de negocio ↔ worker Python:** no hay contrato de red. La comunicación se realiza a
través de las tablas de integración y de `IntegrationOutbox`, cuyo esquema constituye el contrato
(ADR 0003 y ADR 0004).

## Alternativas consideradas

- **REST puro orientado a recursos**, expresando cada operación como una actualización del recurso
  (`PUT /facturas/{id}` con el nuevo estado) y dejando que el backend infiera la operación
  comparando el estado anterior con el nuevo. Era más uniforme y evitaba multiplicar endpoints. Se
  descartó porque disfraza de actualización de campo la operación más importante del sistema: la
  validación no modifica un atributo, sino que ejecuta una transacción que abarca factura, asiento y
  outbox. Con ese estilo, el servidor debe adivinar la intención del cliente y las invariantes se
  defienden a posteriori en lugar de estar expresadas en el contrato.
- **GraphQL** — Habría permitido a la SPA pedir exactamente los campos de cada pantalla con un solo
  endpoint. Se descartó por desproporcionado: hay un único cliente con pantallas conocidas de
  antemano, de modo que la flexibilidad de consulta no aporta valor, y las operaciones
  transaccionales del dominio resultan menos naturales de modelar como mutaciones.
- **Comunicación por HTTP entre la API de negocio y el worker Python** — Habría dado
  retroalimentación inmediata y un contrato explícito y versionable entre ambos. Se descartó en la
  ADR 0004 porque acoplaría la validación contable a la disponibilidad de servicios externos.

## Consecuencias

- La intención de cada operación queda explícita en el contrato, de modo que la API documenta por sí
  misma qué puede hacer el sistema en lugar de esconderlo tras actualizaciones genéricas.
- Cada comando concentra la validación de sus precondiciones en un punto único, lo que hace directa
  la aplicación de las reglas del PRD (bloquear la validación de un duplicado, impedir confirmar un
  asiento con líneas sin cuenta asignada).
- La auditoría registra qué operación se ejecutó, no solo qué campo cambió, que es exactamente lo
  que el PRD pide poder rastrear.
- La escritura en `IntegrationOutbox` ocurre en un lugar bien delimitado del código —el comando de
  validación— en lugar de depender de detectar un cambio de estado.
- **Costo:** el contrato deja de ser uniforme; hay más endpoints que recursos y su descubrimiento
  depende de la documentación, no de una convención mecánica.
- **Costo:** al no existir contrato de red entre .NET y Python, el acoplamiento se traslada al
  esquema de base de datos, que no puede versionarse con las herramientas habituales de una API y
  exige coordinar despliegues de ambos componentes ante cualquier cambio en la frontera.
- **Costo:** los tipos del contrato se definen dos veces, en C# y en TypeScript, y una divergencia
  entre ambos solo se manifiesta en tiempo de ejecución salvo que se genere el cliente a partir de
  la especificación de la API.
