# ADR 0013: Almacenamiento y entrega de documentos

## Estado

Aceptado. Revisión 3. Corrige la "vista unificada de documentos" de la revisión 2: **no** es una
lectura cruzada de `fact.DocumentoRecibido` desde .NET. Es una **proyección propiedad de .NET**,
`fact.DocumentoFactura` (esquema 016), poblada de forma asíncrona desde el *payload* del
`InboxEvent` al promover (BACKLOG #12, hallazgo de bloqueo de `design.md`).

Revisión 2. Los adjuntos siguen abiertos después de validar y su cambio emite
`DOCUMENTACION_ACTUALIZADA`, para que el medio probatorio que llega tarde llegue a Drive (revisión
adversarial v2, A5).

Decisión nueva en la revisión 1: el diseño anterior incluía el campo "ubicación del archivo
almacenado" sin decir dónde, y no tenía ningún endpoint para servir documentos.

## Contexto

`DESIGN_BRIEF.md` define la pantalla de detalle y validación como *"la pantalla central del producto
— aquí se pasa la mayor parte del tiempo"*, y su razón de ser es el patrón documento más
formulario: *"imagen/PDF de la factura escaneada a la izquierda, formulario de datos extraídos a la
derecha, para poder verificar visualmente cada campo contra el original"*. **Sin el documento a la
vista, la pantalla no sirve para lo único que existe para hacer.**

El prototipo no lo implementa: es un marcador de posición con proporción de hoja, fondo rayado y el
texto "PDF de la factura".

Faltaba decidir una cadena completa, no un endpoint: quién guarda los bytes, quién los sirve, cómo
se renderizan, cómo se autoriza la petición y de dónde sale el archivo **antes** de validar —porque
Drive solo recibe los archivos **al** validar, de modo que el visor no puede apoyarse en Drive—.

Se suma que la interfaz permite **adjuntar y eliminar archivos a mano**, un camino que el diseño
anterior no contemplaba y que contradice la partición de propiedad de datos.

## Decisión

### Disco compartido

Python escribe los archivos descargados en un volumen; la base guarda la ruta relativa a una **raíz
configurable** entregada a ambos runtimes (ADR 0012). .NET lee ese volumen y sirve los bytes.

Drive **no es el almacén de trabajo**: es el archivo de destino. El visor se sirve siempre desde el
volumen.

### Dos tablas, según el origen

| Tabla | Propietario | Contenido |
|---|---|---|
| `DocumentoRecibido` | Python | Adjuntos descargados de Gmail |
| `AdjuntoManual` | .NET | Archivos subidos por el asistente desde la SPA |

La partición de ADR 0003 queda **impecable por tabla**: ningún componente escribe donde no le
corresponde, y el refuerzo con permisos por usuario de base de datos sigue siendo aplicable.

.NET expone una **vista unificada de documentos de la factura**. Angular nunca combina fuentes.

### Revisión 3 · La vista unificada es una proyección .NET, no una lectura cruzada

La revisión 2 daba por hecho que ".NET lee ese volumen y sirve los bytes" bastaba para justificar
que la vista unificada leyera `DocumentoRecibido` directamente para el origen ingesta. Es una
lectura de intención — dice qué runtime sirve los bytes del archivo, no de qué tabla lee los
metadatos del documento — y en BACKLOG #12 (`design.md`, hallazgo de bloqueo) resultó ser **una
violación estructural**, no solo un vacío de redacción.

**El motivo — DENY explícito, simétrico con ADR 0003.** `008_usuarios_y_permisos.sql` ejecuta
`DENY SELECT ... ON fact.DocumentoRecibido TO fact_api`. ADR 0003 §Privadas clasifica
`DocumentoRecibido` entre las tablas **privadas de Python** ("un solo componente escribe **y**
lee") y su invariante 3 lo dice sin ambigüedad: *"Ningún componente sondea la tabla privada del
otro"*, con la consecuencia ya escrita en ese mismo ADR — *"`usr_api` no puede leer
`fact.Procesamiento`. Aunque alguien escriba ese `SELECT`, falla."* — aplicada aquí, por simetría,
a `DocumentoRecibido`. La propuesta original de BACKLOG #12 y las dos specs derivadas
(`documentos-lista-unificada-api`, `documento-contenido-api`) asumían un `SELECT` de solo lectura
sobre `DocumentoRecibido` desde .NET. Eso es estructuralmente imposible — el motor lo rechaza — y
normativamente prohibido por ADR 0003, no una omisión de permisos que baste con corregir con un
`GRANT`.

**Qué cambia.** La resolución replica el mecanismo que este mismo ADR ya eligió para la dirección
opuesta (Python no puede leer `AdjuntoManual`, así que las rutas viajan en el *payload* del
evento): los metadatos del documento (`nombreArchivo`, `mimeType`, `rutaRelativa`, `tamanoBytes`)
viajan en el *payload* del `InboxEvent` y se persisten del lado de .NET **al promover**, en una
tabla nueva y propia de .NET, `fact.DocumentoFactura` (esquema 016, `GRANT` para `fact_api`, `DENY`
para `fact_worker` — mismo patrón de refuerzo que el resto de tablas privadas de .NET). La vista
unificada de documentos de la factura combina `fact.DocumentoFactura` (proyección, origen ingesta)
con `AdjuntoManual` (origen manual) — **ambas tablas propiedad de .NET** — y nunca lee
`DocumentoRecibido`, ni para listar ni para servir bytes (`GET /api/documentos/{id}/contenido`
resuelve `RutaRelativa` desde `fact.DocumentoFactura`).

Alternativas descartadas en el momento de decidir esto (`design.md` Decisión D1): `GRANT SELECT`
sobre `DocumentoRecibido` para `fact_api` — desmontaría la garantía más fuerte de ADR 0003; una
vista SQL con *ownership chaining* — evade el `DENY` de forma encubierta, peor que el `GRANT`
directo; servir la pantalla solo con `AdjuntoManual` — la pantalla pierde su razón de ser (el caso
más común es el documento **ingerido**, no el subido a mano).

**Costo nuevo, ya asumido en el esquema de rollout de BACKLOG #12.** La proyección es asíncrona:
un documento recién ingerido puede estar temporalmente ausente de la vista unificada hasta que su
promoción termine — no es un error, es consistencia eventual esperada. Los documentos ingeridos
**antes** del esquema 016 no tienen fila de proyección y no pueden reconstruirse retroactivamente
(reconstruirlos exigiría leer `DocumentoRecibido`, la misma lectura prohibida); la vista se degrada
a solo `AdjuntoManual` para esas facturas, sin tratarse como error.

### Entrega y renderizado

```http
GET /api/documentos/{id}/contenido
→ 200, Content-Type según el MIME real del archivo
```

```html
<iframe src="/api/documentos/12/contenido"></iframe>
```

**Visor nativo del navegador.** Maneja PDF de varias páginas, y un adjunto JPG o PNG servido con su
MIME correcto se renderiza igual sin código adicional. Al ser mismo origen, la cookie de sesión
viaja (ADR 0007, ADR 0012).

### Borrado con rastro

Eliminar un adjunto manual es **borrado lógico con auditoría**: `EliminadoEn`, `EliminadoPor` y
`MotivoEliminacion`. `AuditoriaCorreccion` registra campos modificados, no archivos eliminados, y
sin esto quedaba un hueco de trazabilidad justo sobre el **respaldo documental de un asiento
contable**.

### Empaquetado hacia Drive

El worker empaqueta a Drive, y **no puede leer `AdjuntoManual`** (ADR 0003). El *payload* del evento
incluye la **lista completa de rutas de ambos orígenes**, resuelta por .NET al emitirlo. Python
empaqueta desde el *payload* y no consulta ninguna de las dos tablas.

Es la razón concreta por la que ADR 0004 eligió eventos con *payload* explícito y no un evento
genérico de reconciliación.

### Los adjuntos siguen abiertos después de validar

Añadir o eliminar un adjunto **después** de validar emite `DOCUMENTACION_ACTUALIZADA` (ADR 0004),
que vuelve a sincronizar la carpeta de Drive: añade lo nuevo y retira lo eliminado.

> **Por qué.** La versión anterior empaquetaba con la lista congelada en `FACTURA_VALIDADA`, y ADR
> 0008 no restringía los endpoints de adjuntos al borrador. El resultado era que el medio probatorio
> que llega tarde —**el escenario más probable, porque es justo el que motivó `AdjuntoManual`**— se
> subía al sistema y no se archivaba nunca; y un adjunto eliminado después de validar tampoco
> desaparecía de Drive. El criterio de éxito del 100% de facturas archivadas con sus medios
> probatorios se incumplía en silencio.
>
> Cerrar los adjuntos al validar era más simple, pero deja sin salida el caso real: la guía de
> remisión que llega dos días después obligaría a reabrir el asiento, convirtiendo *"subí una foto"*
> en una **reapertura contable** con su motivo y su rastro. Es desproporcionado.

Tres piezas ya decididas hacen que esto funcione sin trabajo extra: la carpeta se encuentra por
`FacturaId` en `appProperties` —ninguna corrección la mueve—, el evento lleva su secuencia y no se
pisa con los demás de la factura, y el *payload* lleva la lista **completa**, no un delta, de modo
que reempaquetar es idempotente.

**La lista de rutas deja de ser un dato congelado de `FACTURA_VALIDADA`** y pasa a ser el estado
vigente de los adjuntos en el momento de emitir cada evento. Es el cambio conceptual real.

## Alternativas consideradas

- **Bytes en la base de datos.** Elimina el volumen compartido, unifica el respaldo en una sola
  copia y suprime el acuerdo de rutas entre dos runtimes. Se descartó para mantener la base pequeña
  y manejable, aceptando a cambio la coordinación de dos almacenes.
- **Almacén de objetos S3-compatible con URL firmada.** Escala sin tocar la base ni la API. Se
  descartó por desproporcionado para 10-50 facturas diarias, y porque la URL firmada esquiva la
  cookie de sesión y abre una superficie de autorización paralela.
- **Una sola tabla de documentos con columna de origen.** Es el patrón adoptado para `TipoCambio`.
  Se descartó aquí porque un adjunto de Gmail y un archivo subido tienen metadatos de procedencia
  distintos, y porque mantener la tabla privada de Python permite el refuerzo por permisos.
- **No permitir adjuntos manuales.** Cero endpoints, cero decisiones sobre tamaño y tipos. Se
  descartó porque el PRD declara como caso borde el correo *"que llega sin OC o sin medios
  probatorios"*, y el criterio de éxito exige que el 100% de las validadas terminen en Drive con la
  factura **y los medios probatorios**. Sin carga manual, ese criterio dependería de que el
  proveedor reenvíe.

## Consecuencias

- La pantalla central del producto puede cumplir su función.
- El caso borde del PRD tiene una respuesta dentro del sistema.
- La base de datos se mantiene pequeña.
- **(Revisión 3) La vista unificada gana una tercera tabla propia de .NET**, `fact.DocumentoFactura`
  (esquema 016), además de `AdjuntoManual`. El costo de "dos tablas con formas distintas" ya listado
  más abajo pasa a ser, en la práctica, dos tablas .NET-owned con la misma forma (`DocumentoFactura`
  espeja las columnas de `AdjuntoManual`) más un tercer nombre (`DocumentoRecibido`) que nunca se lee
  desde .NET.
- **(Revisión 3) La proyección es asíncrona y puede quedar temporalmente desactualizada** respecto a
  lo que Python ya ingirió: un documento recién llegado puede tardar hasta que su `InboxEvent` se
  promueva en aparecer en la vista unificada. No es un error — es la misma consistencia eventual que
  ya rige `OutboxEvent`/`InboxEvent` (ADR 0003, ADR 0004) — pero es un costo nuevo que la revisión 2
  no declaraba porque asumía una lectura en vivo.
- **(Revisión 3) Los documentos ingeridos antes del esquema 016 no tienen proyección** y no pueden
  reconstruirse retroactivamente (reconstruirlos exigiría la misma lectura de `DocumentoRecibido`
  que esta revisión prohíbe). Para esas facturas la vista unificada se degrada a solo
  `AdjuntoManual`; se acepta porque la ingesta en producción no había empezado.
- **Costo:** el volumen compartido es obligatorio y restringe la topología (ADR 0012).
- **Costo:** **dos respaldos que coordinar**, base y volumen, en el orden que fija ADR 0014. Si se
  desincronizan, hay asientos cuyo documento ya no existe.
- **Costo:** riesgo de huérfanos —fila sin archivo o archivo sin fila—, que exige una verificación
  periódica de integridad.
- **Costo:** el visor, el contador de medios probatorios y el empaquetado operan sobre dos tablas
  con formas distintas, para algo que el usuario ve como una sola lista.
- **Costo:** eliminar un adjunto **borra un archivo de Drive**. Es la única operación destructiva del
  flujo de publicación, y por eso queda registrada en `AuditoriaCorreccion`, no solo en el log.
- **Pendiente:** tipos permitidos y tamaño máximo de los adjuntos manuales, que se configuran desde
  la pantalla de Configuración.
