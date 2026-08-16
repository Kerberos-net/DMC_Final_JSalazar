# ADR 0005: Frontera de promoción de documento procesado a factura de negocio

## Estado

Aceptado

## Contexto

El PRD establece que, tras la extracción automática por OCR/IA, "la factura queda en estado
**Pendiente de validación**", y que el asistente contable puede confirmar o corregir esos datos
antes de marcarla como "Validada". Exige además que toda corrección manual quede trazable: quién
corrigió, cuándo, y el valor original frente al valor corregido.

Esto colisiona con la partición de propiedad definida en la ADR 0003: quien ingesta el correo y
ejecuta la extracción es Python, propietario del contexto de integración, mientras que `Factura` es
una tabla del contexto de negocio, propiedad exclusiva de .NET. Si la entidad de negocio no existe
hasta el momento de validar, las correcciones previas a la validación tendrían que escribirse sobre
tablas propiedad de Python, lo que rompería la frontera o exigiría una tabla de borrador que sería
`Factura` con otro nombre.

Se necesita por tanto definir con precisión en qué instante un documento procesado se convierte en
una entidad de negocio, y quién ejecuta esa conversión.

## Decisión

La frontera de promoción es el momento en que **Python completa exitosamente el procesamiento y la
extracción del documento**. En ese punto, .NET ejecuta la operación `CrearFacturaDesdeProcesamiento`,
que crea una entidad `Factura` en estado `PENDIENTE_VALIDACION` a partir de los datos extraídos.

Reglas que rigen esta frontera:

- `Procesamiento` y `DatosExtraidos` constituyen **evidencia inmutable** de lo que fue recibido y
  extraído por Python. No se editan ni se corrigen.
- `Factura` es la **entidad de negocio**, propiedad exclusiva de .NET.
- Los datos extraídos se copian deliberadamente a `Factura`. Esta duplicación es un *snapshot* de
  negocio y no debe tratarse como una segunda fuente editable.
- Las correcciones de los usuarios ocurren **exclusivamente sobre `Factura`** y se registran en una
  estructura de auditoría que conserva campo modificado, valor original, valor nuevo, usuario y
  fecha.
- La bandeja de validación y todo el flujo posterior trabajan sobre `Factura`, nunca directamente
  sobre `Procesamiento`.
- Un `Procesamiento` completado solo puede originar **una** `Factura`, mediante una operación
  **idempotente**: reejecutar la promoción sobre el mismo procesamiento no crea un duplicado.
- Cuando la `Factura` pasa a estado `VALIDADA`, .NET genera el evento correspondiente en
  `IntegrationOutbox` (ADR 0004), que Python consume para ejecutar Google Drive, Google Sheets,
  Telegram y correo.

El reparto resultante es explícito: Python es propietario del procesamiento documental y de las
integraciones externas; .NET es propietario de la entidad `Factura`, de sus correcciones, de su
validación y de su ciclo de vida contable.

## Alternativas consideradas

- **Crear la `Factura` solo en el momento de validar** — Mientras la factura estuviera pendiente, la
  bandeja principal y la pantalla de validación leerían directamente de `DocumentoRecibido` y
  `Procesamiento`, y `Factura` contendría únicamente hechos contables confirmados, sin duplicación
  alguna. Se descartó porque el PRD exige guardar avance sin validar y dejar trazable cada
  corrección previa: esas ediciones tendrían que escribirse sobre tablas propiedad de Python, lo que
  violaría la frontera de la ADR 0003, u obligaría a introducir una tabla de borrador funcionalmente
  equivalente a `Factura`.
- **Que Python cree directamente la `Factura` en estado pendiente** — Habría evitado tanto la
  duplicación como el paso de promoción, ya que el componente que extrae los datos los escribiría en
  su destino final. Se descartó por contradecir frontalmente la ADR 0003: convertiría a Python en
  escritor de una tabla de negocio y devolvería al sistema el problema de dos runtimes escribiendo
  el mismo contexto contable.

## Consecuencias

- El ciclo de vida completo de la factura (pendiente de validación → validada → asiento generado)
  vive bajo un único propietario, por lo que las invariantes contables se aplican en un solo lugar.
- La trazabilidad exigida por el PRD es directa: como las correcciones ocurren sobre una entidad de
  negocio propiedad de .NET, la auditoría de campo, valor anterior, valor nuevo, usuario y fecha se
  implementa sin cruzar la frontera de propiedad.
- La evidencia inmutable del OCR queda preservada por separado, lo que permite comparar lo que la
  máquina leyó contra lo que el contador afirmó y medir de forma objetiva el criterio de precisión
  de ≥90% de campos correctos que fija el PRD.
- La idempotencia de la promoción protege contra duplicados ante reinicios del proceso o reejecución
  del sondeo.
- **Costo:** el dato extraído existe deliberadamente en dos lugares (`DatosExtraidos` y `Factura`).
  Es una duplicación intencional con semántica distinta, pero exige disciplina para que nadie la
  interprete como dos fuentes de verdad editables.
- **Costo:** aparece un mecanismo de promoción en .NET que debe detectar procesamientos completados
  y ejecutarse de forma fiable e idempotente, con su propio estado y su propia observabilidad.
- **Costo:** existe una latencia inevitable entre que Python completa la extracción y que la factura
  aparece en la bandeja del usuario, latencia que consume parte del presupuesto de 15 minutos que el
  PRD fija como criterio de éxito.
- **Costo:** una corrección posterior en el motor de extracción no se refleja retroactivamente en
  facturas ya promovidas, porque el *snapshot* de negocio no se recalcula.
