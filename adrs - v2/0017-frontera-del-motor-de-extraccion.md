# ADR 0017: Frontera del motor de extracción y candidatura de correos

## Estado

Aceptado. Decisión nueva. El diseño anterior declaraba la precisión del OCR como *"el mayor riesgo
técnico del proyecto"* y no tenía ni una ADR al respecto.

## Contexto

El PRD fija una meta de ≥90% de campos correctamente extraídos. Es el mayor riesgo técnico
declarado, y aun así no se había decidido nada: ni la frontera de abstracción del motor, ni el
criterio de evaluación, ni —crítico en un sistema contable— **si los documentos salen de la
organización hacia un servicio de terceros**.

Además, el primer paso del pipeline era una caja negra: nada definía qué convierte a un correo en
candidato a factura, que es justo el paso del que depende cuántos falsos positivos se producen.

Y el diseño mencionaba "XML" de pasada sin decidir nada, cuando la factura electrónica peruana trae
número, RUC, montos y fechas **exactos**.

## Decisión

### Candidatura: dos condiciones, y solo esas

Un correo es candidato a procesamiento cuando:

1. Pertenece a una **etiqueta o carpeta de Gmail** configurada como origen de facturas.
2. Contiene **al menos un adjunto con extensión permitida** (PDF o XML).

El asunto y el remitente **no intervienen**. La etiqueta y las extensiones son **configurables**,
nunca fijas en el código del worker. Los correos que no cumplen la regla **no se envían al
procesamiento documental**: no consumen extracción ni generan filas de trabajo.

> Candidatura **no** equivale a ser una factura. Determinar el tipo de comprobante es
> responsabilidad de la etapa de procesamiento.

### Identidad del adjunto

De cada adjunto se almacenan `GmailMessageId`, nombre, extensión, MIME type y **`HashContenido`**.
El hash es lo que sostiene la idempotencia del reproceso (ADR 0010).

### El XML es la fuente estructurada prioritaria

| Adjuntos | Fuente de datos | Evidencia |
|---|---|---|
| XML + PDF | **XML** | PDF |
| Solo PDF | Extracción de texto y, si hace falta, OCR | PDF |
| Solo XML | XML | XML |

El XML desactiva buena parte del riesgo de precisión para los comprobantes electrónicos, que son la
mayoría.

### Asociación PDF ↔ XML

**Clave:** el identificador tributario compuesto y normalizado —RUC del emisor, tipo de comprobante,
serie y número—.

1. Se procesan **primero todos los XML**. El XML es la autoridad.
2. Se procesa **cada PDF** para obtener los mismos cuatro datos.
3. El PDF se asocia **únicamente si los cuatro componentes normalizados coinciden de forma exacta**.

**Recuperación:** si no es posible extraer los datos del contenido del PDF, el nombre del archivo
puede usarse como respaldo, siempre que la coincidencia sea **inequívoca**.

**Evidencia insuficiente, explícitamente:** el asunto, el remitente, la fecha del correo y la
posición del archivo entre los adjuntos **no establecen asociación en ningún caso**.

**Sin coincidencia inequívoca:** el PDF permanece **sin asociar** y se registra un evento para
revisión. **Nunca se asigna a un comprobante por proximidad o descarte.**

### Escritura en Gmail

El worker aplica una **etiqueta propia** al correo ya ingestado. **Nunca borra.** Alcance
`gmail.modify` (ADR 0015). Es reversible, no pisa el estado leído/no leído del usuario, y el correo
original sobrevive como evidencia de última instancia.

### Frontera de abstracción y evaluación

El motor de extracción se consume tras una **interfaz propia** en el worker, con una implementación
sustituible. La decisión sobre **si los documentos salen de la organización** hacia un servicio de
terceros es una decisión de negocio que debe tomarse explícitamente antes de elegir implementación,
no como consecuencia de ella.

La métrica de precisión se obtiene comparando `DatosExtraidos` —inmutable— contra la factura ya
corregida. **Esa comparación mide, no sustituye una prueba previa con facturas reales.**

## Alternativas consideradas

- **Filtros por asunto, remitente y palabras clave**, como dibujaba el prototipo. Se descartaron:
  añaden lógica de detección que hay que mantener y probar, duplican lo que los filtros nativos de
  Gmail ya hacen mejor, y un falso positivo se corrige más rápido reetiquetando en Gmail.
- **Asociar PDF y XML por convención de nombre SUNAT únicamente.** Determinista y trivial. Se
  descartó porque un proveedor que renombre el archivo rompe la asociación y deja el PDF huérfano,
  perdiendo la evidencia visual que alimenta el visor.
- **Bandeja de candidatos con confirmación humana antes de procesar.** Eliminaría los falsos
  positivos de raíz. Se descartó porque añade un paso manual diario y hace que el criterio de
  visibilidad en 15 minutos dependa de que el usuario esté mirando.
- **Etiquetar y eliminar los correos procesados**, como ofrecía el prototipo. El borrado se
  descartó: un fallo en la lógica de "procesado" enviaría a la papelera correos con facturas nunca
  ingestadas, y con el worker sin vigilancia (ADR 0001) nadie lo notaría a tiempo.

## Consecuencias

- El primer paso del pipeline deja de ser una caja negra y es configurable sin tocar código.
- El XML reduce el riesgo de precisión donde más importa, y el PDF conserva su papel de evidencia.
- Nunca se asocia un documento a un comprobante equivocado: ante la duda, queda sin asociar y con
  aviso.
- **Costo:** el PDF pasa por extracción **aunque el XML ya aportara el dato exacto**. Se gasta
  presupuesto de extracción a cambio de que la asociación sea verificada y no inferida. Es una
  decisión consciente.
- **Costo:** los PDF sin asociar necesitan una superficie de revisión en el panel de errores y una
  acción manual de resolución.
- **Riesgo abierto:** no se ha seleccionado ni evaluado el motor de extracción. Debería validarse
  con facturas reales antes de comprometer el resto del desarrollo.
