# ADR 0005: Frontera de promoción de documento procesado a factura de negocio

## Estado

Aceptado. Revisión 3. Mueve el indicador de idempotencia al lado de .NET —donde vive la decisión de
promover— y persiste el resultado del consumo en `InboxEvent`, cerrando una contradicción que este
ADR tenía consigo mismo (revisión adversarial v2, C2).

La revisión 2 reemplazó la versión previa (`adrs - v1/0005`), en la que .NET sondeaba
`Procesamiento`, una tabla privada de Python.

## Contexto

Python procesa un documento y obtiene tipo de comprobante, número, RUC, proveedor, monto, moneda y
fecha de emisión. Ese resultado debe convertirse en una `Factura` del dominio, que es propiedad
exclusiva de .NET (ADR 0003).

La versión anterior resolvía esto haciendo que .NET consultara `Procesamiento` buscando filas
completadas y no promovidas. Eso convertía una tabla privada de Python en la cola de trabajo de
.NET: un contrato de facto que ADR 0003 no reconocía y que rompía al primer cambio de esquema del
lado del worker.

Hay además un requisito que la promoción automática ignoraba: el PRD declara como caso borde el
**falso positivo de detección** —un correo que no corresponde a una factura de compra real—. Si toda
extracción se promueve automáticamente, todo PDF procesado se convierte en una factura que
permanece en la bandeja indefinidamente.

## Decisión

### Python notifica un hecho; .NET decide

Al terminar el procesamiento, Python escribe un mensaje en `InboxEvent`. `Tipo` es siempre el
literal único `PROCESAMIENTO_FINALIZADO` de `CK_InboxEvent_Tipo`; el resultado —éxito o fallo— se
lee de `Procesamiento.Estado` (`COMPLETADO`/`ERROR`), nunca de un segundo literal de `Tipo`.
**Corregido en el ítem #7**: la versión anterior de este ADR declaraba dos literales
(`PROCESAMIENTO_COMPLETADO`/`PROCESAMIENTO_FALLIDO`); el esquema construido usa uno solo.

Un servicio alojado en la API consume ese inbox y, **dentro de una transacción propia**, decide si
corresponde promover el resultado a una `Factura`.

Dos reglas delimitan la frontera:

1. **Python no accede a las tablas de dominio de .NET.**
2. **Python no solicita operaciones de dominio.** No pide "crear factura": informa que el
   procesamiento terminó. **La decisión de promover es de .NET, y puede ser no promover.**

### Qué se promueve y qué no

| Resultado del procesamiento | Efecto |
|---|---|
| Datos suficientes para representar una factura | Se crea `Factura` en `PENDIENTE_VALIDACION` |
| Adjunto corrupto, XML inválido, OCR fallido, PDF no asociado o ambiguo, tipo de comprobante no válido | **No se crea factura.** El error vive en las tablas de ingesta y aparece en la bandeja como incidencia (ADR 0010) |

Una `Factura` **solo se crea cuando el procesamiento documental produjo datos suficientes**. El
estado `ERROR` de `Factura` desaparece: un documento que falló antes de la promoción no es una
factura y no se finge que lo sea.

### Idempotencia

La factura guarda **de qué procesamiento nació**, y un índice único impide que nazca dos veces:

```sql
ALTER TABLE Factura
    ADD ProcesamientoId BIGINT NULL;

CREATE UNIQUE INDEX UQ_Factura_Procesamiento
    ON Factura (ProcesamientoId)
    WHERE ProcesamientoId IS NOT NULL;
```

Reejecutar la promoción produce una violación de índice, que el servicio de fondo captura y trata
como **no-op idempotente**: el `InboxEvent` se marca consumido y no se crea una segunda factura.

El indicador vive **del lado de .NET**, que es quien decide promover. La versión anterior lo situaba
en `Procesamiento` —tabla privada de Python— y se contradecía consigo misma: obligaba a .NET a
escribir en ella para luego celebrar, tres párrafos más abajo, que `Procesamiento` *"vuelve a ser
privada de Python"*. Con la matriz de permisos de ADR 0003 esa escritura ya no es solo incoherente:
**falla en el motor**.

Y es una **invariante del motor, no una bandera calculada**, que era el argumento correcto de la
versión anterior aplicado al sitio correcto. Ese argumento se apoyaba antes en el índice único de
identidad del comprobante; ese índice dejó de ser único —la unicidad se comprueba ahora al validar,
para que el flujo de resolución de duplicados sea alcanzable (TECH-DESIGN, Flujo 3)—, de modo que
sin `UQ_Factura_Procesamiento` la promoción no tendría ninguna garantía de motor.

`ProcesamientoId` es además el eslabón de trazabilidad que faltaba: desde una factura se llega a los
adjuntos que la originaron sin pasar por el correo.

### No reprocesar lo que se decidió no promover

El índice cubre *"no crear dos facturas"*. No cubre *"no volver a intentarlo"* cuando .NET decide
**no promover**, que es un resultado que este ADR contempla en su tabla y que hasta ahora no se
persistía en ninguna parte: el evento se reconsumiría en cada ciclo para siempre.

La marca vive en `InboxEvent`, que es **tabla de contrato** y por tanto coescribible por diseño
(ADR 0004): `EstadoConsumo` con `PROMOVIDO` o `DESCARTADO`, la factura creada o el motivo del
descarte.

### Indicadores al promover

La factura se crea con los indicadores que la interfaz debe resaltar: proveedor genérico asignado,
posible duplicado, campos no extraídos, fecha en domingo, **afectación no verificada** y
**referencia externa**. Son **campos propios**, no estados: el chip de la bandeja se deriva de ellos.

Los dos últimos son de la revisión v2:

- **Afectación no verificada** se enciende cuando el comprobante llega **solo en PDF** y por tanto no
  se pudo comprobar si mezcla líneas gravadas y no gravadas. Sobre un PDF escaneado esa mezcla no es
  detectable de forma fiable por ningún medio, así que la comprobación se traslada al asistente, que
  sí puede mirar el documento (ADR 0017).
- **Referencia externa** marca una nota de crédito contra una factura anterior al sistema, que no
  tiene `FacturaReferenciaId` y por tanto no entra en el tope acumulado (ADR 0006).

**Corregido en el ítem #7:** al promover, `SmartNet.Inbox.Core` calcula **5** de estos seis
indicadores. `EsReferenciaExterna` queda con su valor DDL por defecto (`0`), sin calcularse: notas de
crédito es el ítem #10 del backlog, y `DatosExtraidos` no tiene columnas de referencia de las que
derivarlo en este ítem. Calcularlo sin esa fuente sería inventar el dato.

### La evidencia de extracción se persiste al promover

Al crear la factura se persiste `FacturaExtraccion`: qué leyó la extracción en cada campo y de qué
fuente —XML o PDF—. El dato ya viaja en el *payload*, porque sin él no se podría promover; lo nuevo
es guardarlo en vez de descartarlo.

Existe porque la métrica de precisión compara esa evidencia contra la factura corregida, y
`DatosExtraidos` es privada de Python: sin esta tabla, ningún componente puede leer los dos lados de
la comparación (ADR 0017).

## Alternativas consideradas

- **Mantener el sondeo directo de `Procesamiento`.** Cero tablas nuevas, ya estaba diseñado. Se
  descartó porque es el contrato de facto que ADR 0003 prohíbe, y porque un cambio en el esquema de
  Python rompería a .NET sin que ningún contrato lo advirtiera.
- **Python llama a un endpoint de .NET para promover.** Inmediato, sin sondeo ni latencia acumulada.
  Se descartó por dos razones: reintroduce un contrato de red que ADR 0001 elimina, y si la API está
  caída Python debe reintentar y guardar estado igualmente, con lo que reaparece la cola con más
  piezas. Además convertiría a Python en solicitante de operaciones de dominio.
- **Promoción bajo demanda al consultar la bandeja.** Elimina un bucle. Se descartó porque hace que
  una consulta de lectura escriba en el dominio, y porque el criterio de visibilidad en 15 minutos
  pasaría a depender de que alguien esté mirando la pantalla.

## Consecuencias

- `Procesamiento` es **estrictamente privada de Python**, sin excepciones. La revisión 2 lo afirmaba
  y su propia sección de idempotencia lo desmentía; la revisión 3 lo hace cierto, y la matriz de
  permisos de ADR 0003 lo impone: `usr_api` no tiene ningún acceso a esa tabla. Las tres direcciones
  de comunicación son contratos nombrados y simétricos (ADR 0003, ADR 0004).
- **Se puede responder cuántos documentos procesados no llegaron a factura y por qué.** Antes esa
  decisión se tomaba y no se registraba en ninguna parte.
- `ProcesamientoId` da trazabilidad de vuelta: desde una factura se llega a los adjuntos que la
  originaron.
- .NET conserva el control de sus invariantes: puede rechazar una promoción, aplicar reglas propias
  y decidir no crear nada.
- Los falsos positivos y los fallos de extracción tienen un destino explícito que no contamina la
  tabla de negocio.
- **Costo:** la API aloja un servicio de fondo. Es legítimo y no contradice a ADR 0004, cuyo rechazo
  de una alternativa se apoya en que no participa de la transacción del hecho de negocio, no en el
  hecho de usar un servicio alojado.
- **Costo:** dos bucles de sondeo encadenados —Gmail y el inbox— consumen el mismo presupuesto de 15
  minutos de visibilidad. Sus frecuencias deben fijarse conjuntamente.
