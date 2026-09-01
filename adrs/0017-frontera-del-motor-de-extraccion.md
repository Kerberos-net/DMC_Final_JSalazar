# ADR 0017: Frontera del motor de extracción y candidatura de correos

## Estado

Aceptado. Revisión 3. Añade la segunda pasada de asociación por containment del nombre de archivo
contra la clave autoritativa del XML y la re-emisión de `InboxEvent` restringida al lado PDF para
asociaciones tardías (BACKLOG `asociacion-pdf-clave-desde-xml`).

Revisión 2. Añade la detección de la factura mixta desde el XML, parte la métrica de
precisión por fuente y la traslada a una tabla que quien la calcula sí puede leer, y escribe la
consulta de sondeo de Gmail (revisión adversarial v2: C11, A9, S3, y el hallazgo N1 detectado al
construir la matriz de permisos).

Decisión nueva en la revisión 1: el diseño anterior declaraba la precisión del OCR como *"el mayor
riesgo técnico del proyecto"* y no tenía ni una ADR al respecto.

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
puede usarse como respaldo, siempre que la coincidencia sea **inequívoca**. Dos formas, ambas
verificadas y ninguna inferida:

1. **Clave propia desde el nombre**, cuando el nombre respeta la convención SUNAT completa y
   produce los cuatro componentes; se asocia entonces por la regla exacta del punto 3.
2. **Containment contra la clave autoritativa del XML** (revisión 3): cuando el PDF no produce
   clave propia, un XML huérfano con clave completa puede reclamarlo si su **RUC de emisor, serie
   y número** normalizados aparecen los tres como **tokens delimitados y distintos** del nombre de
   archivo del PDF. El tipo de comprobante **no** se exige del nombre: es el componente que los
   emisores mutilan. La comparación va del XML **hacia** el nombre —se verifica una clave que ya
   existe, no se adivina una—, y la autoridad sigue siendo el XML.

La exclusividad es **1:1 bilateral sobre todo el conjunto sin pareja**: si más de un XML califica
para un PDF, o más de un PDF para un XML, **ninguno** se asocia. Un huérfano antiguo no
relacionado puede así suprimir una asociación válida; se acepta el costo, porque el modo de fallo
es "queda sin asociar", nunca "asociado al comprobante equivocado".

**Evidencia insuficiente, explícitamente:** el asunto, el remitente, la fecha del correo y la
posición del archivo entre los adjuntos **no establecen asociación en ningún caso**.

**Sin coincidencia inequívoca:** el PDF permanece **sin asociar** y se registra un evento para
revisión. **Nunca se asigna a un comprobante por proximidad o descarte.**

### Escritura en Gmail

El worker aplica una **etiqueta propia** al correo ya ingestado. **Nunca borra.** Alcance
`gmail.modify` (ADR 0015). Es reversible, no pisa el estado leído/no leído del usuario, y el correo
original sobrevive como evidencia de última instancia.

La consulta de sondeo se acota con los tres términos, todos **configurables**:

```
label:<etiqueta-origen>  -label:<etiqueta-procesado>  after:<fecha-inicio-configurada>
```

Sin el segundo término, cada ciclo relee todo el histórico de la etiqueta y la idempotencia recae
íntegramente en el hash del contenido: funciona, pero crece sin límite. Sin el tercero, la primera
ejecución arrastra años de correo.

Es donde se concilia la frecuencia de sondeo con la cuota de la API de Gmail: la consulta acotada es
lo que hace manejable ese riesgo.

### Detección de la factura mixta

El extractor calcula, recorriendo las líneas del XML UBL, un indicador de tres estados que viaja en
el *payload* hacia .NET y se persiste como indicador de `Factura`:

```
AfectacionMixta

    true   → el XML declara más de un código de afectación
             → rechazo al validar (409): fuera de alcance
    false  → el XML declara uno solo: afectación verificada
    NULL   → no hay XML: afectación NO verificada
```

`REGLAS.md` §8 manda rechazar la factura con líneas gravadas y no gravadas mezcladas, y el sistema no
tenía **con qué detectarla**: `FacturaDetalle` se eliminó (ADR 0011) y `Afectacion` es un único campo
de cabecera con tres valores. Una factura mixta no tiene representación posible: el extractor elegiría
uno de los tres, el comprobante parecería homogéneo y **pasaría las ocho reglas**. El modo de fallo va
en la peor dirección: una mixta registrada como `GRAVADA` **toma crédito fiscal sobre la porción que
no lo genera**.

El `NULL` **no bloquea**: enciende el indicador *"afectación no verificada"* y el asistente la
confirma antes de validar. Sobre un PDF escaneado la mezcla no es detectable de forma fiable por
ningún medio, y dejar pasar ese caso en silencio pondría el modo de fallo justo donde el OCR ya es
menos fiable. La confirmación es una afirmación del asistente sobre un documento fiscal, y queda en
`AuditoriaCorreccion`.

**La cobertura de la regla queda declarada:** automática para los comprobantes con XML, por
confirmación del asistente para los que solo traen PDF.

### Frontera de abstracción y evaluación

El motor de extracción se consume tras una **interfaz propia** en el worker, con una implementación
sustituible. La decisión sobre **si los documentos salen de la organización** hacia un servicio de
terceros es una decisión de negocio que debe tomarse explícitamente antes de elegir implementación,
no como consecuencia de ella.

### La métrica de precisión

Se obtiene comparando la evidencia de extracción contra la factura ya corregida, y **se reporta
siempre partida por fuente**:

| Población | Objetivo |
|---|---|
| Campos extraídos del **XML** | **100%.** Menos de eso es un defecto de parseo, no de extracción |
| Campos extraídos del **PDF** | **≥90%**, el criterio de éxito del PRD |

El agregado puede mostrarse, pero **nunca solo**. Con XML como fuente prioritaria, los campos de un
comprobante electrónico son exactos por construcción y esa población puntúa ≈100%: el riesgo real
—el PDF escaneado sin XML— quedaría diluido en el promedio. Si el 80% de las facturas trae XML, la
métrica global supera el 90% **aunque el OCR acierte poco más de la mitad**.

**Dónde vive la evidencia.** En `FacturaExtraccion`, tabla de dominio de .NET, con el valor extraído
y la **fuente por campo**:

```
FacturaExtraccion
    FacturaId
    CampoNombre       'numero' | 'ruc' | 'total' | 'igv' | 'fechaEmision' | ...
    ValorExtraido
    Fuente            'XML' | 'PDF'
```

No puede vivir en `DatosExtraidos`. Esa tabla es **privada de Python** (ADR 0003) y `Factura` es
dominio de .NET: ningún componente puede leer los dos lados de la comparación, y con la matriz de
permisos eso deja de ser una incoherencia y **falla en el motor**. El dato ya viaja en el *payload*
del `InboxEvent`, porque sin él no se podría promover; lo único nuevo es persistirlo (ADR 0005).

La fuente **por campo** y no por documento es más precisa: una factura con XML puede tener un campo
que el XML no traía.

`DatosExtraidos` sigue siendo privada de Python, inmutable, como registro de trabajo del
procesamiento. `FacturaExtraccion` es igualmente inmutable.

> **Segundo sesgo, declarado y no corregido.** La métrica solo cuenta los errores que el asistente
> **notó**: un campo mal extraído y no advertido cuenta como acierto, y el sesgo apunta siempre hacia
> arriba. No se corrige con una métrica mejor, sino con la prueba previa sobre facturas reales. **La
> cifra es una cota superior, no una medición**, y así debe leerse.

**Esa comparación mide, no sustituye una prueba previa con facturas reales.**

## Alternativas consideradas

- **Filtros por asunto, remitente y palabras clave**, como dibujaba el prototipo. Se descartaron:
  añaden lógica de detección que hay que mantener y probar, duplican lo que los filtros nativos de
  Gmail ya hacen mejor, y un falso positivo se corrige más rápido reetiquetando en Gmail.
- **Asociar PDF y XML por convención de nombre SUNAT únicamente.** Determinista y trivial. Se
  descartó porque un proveedor que renombre el archivo rompe la asociación y deja el PDF huérfano,
  perdiendo la evidencia visual que alimenta el visor. *(Sigue descartada como mecanismo **único**;
  la revisión 3 la admite solo como verificación contra una clave XML ya existente.)*
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
- **Revisión 3:** una **segunda pasada acotada** corre sobre el residuo de la pasada exacta —un
  XML huérfano con clave completa puede reclamar un PDF sin clave por containment del nombre de
  archivo, con exclusividad 1:1 bilateral y el XML como única autoridad— y una **re-emisión de
  `InboxEvent` restringida al lado PDF** reporta a .NET las asociaciones que se completan después
  de que ya se emitieron todos los eventos del `Procesamiento` (el lado XML nunca se re-emite).
- **Costo:** los PDF sin asociar necesitan una superficie de revisión en el panel de errores y una
  acción manual de resolución.
- **Riesgo abierto:** no se ha seleccionado ni evaluado el motor de extracción. Debería validarse
  con facturas reales antes de comprometer el resto del desarrollo.
