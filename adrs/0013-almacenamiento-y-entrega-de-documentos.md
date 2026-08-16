# ADR 0013: Almacenamiento y entrega de documentos

## Estado

Aceptado. Revisión 2. Los adjuntos siguen abiertos después de validar y su cambio emite
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
