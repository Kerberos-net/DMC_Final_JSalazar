# ADR 0005: Frontera de promoción de documento procesado a factura de negocio

## Estado

Aceptado. Reemplaza la versión previa (`adrs - v1/0005`), en la que .NET sondeaba `Procesamiento`,
una tabla privada de Python.

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

Al terminar el procesamiento, Python escribe un mensaje en `InboxEvent`:

```
PROCESAMIENTO_COMPLETADO
PROCESAMIENTO_FALLIDO
```

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

`Procesamiento` lleva el indicador de si ya originó una factura. Reejecutar la promoción sobre el
mismo procesamiento **no crea una segunda factura**. El respaldo real es el índice único de
identidad del comprobante `(RUC, tipo, número)` definido en el modelo de datos del TECH-DESIGN, que
convierte la unicidad en una invariante del motor y no en una bandera calculada.

### Indicadores al promover

La factura se crea con los indicadores que la interfaz debe resaltar: proveedor genérico asignado,
posible duplicado, campos no extraídos y fecha en domingo. Son **campos propios**, no estados: el
chip de la bandeja se deriva de ellos.

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

- `Procesamiento` vuelve a ser privada de Python. Las tres direcciones de comunicación son ahora
  contratos nombrados y simétricos (ADR 0003, ADR 0004).
- .NET conserva el control de sus invariantes: puede rechazar una promoción, aplicar reglas propias
  y decidir no crear nada.
- Los falsos positivos y los fallos de extracción tienen un destino explícito que no contamina la
  tabla de negocio.
- **Costo:** la API aloja un servicio de fondo. Es legítimo y no contradice a ADR 0004, cuyo rechazo
  de una alternativa se apoya en que no participa de la transacción del hecho de negocio, no en el
  hecho de usar un servicio alojado.
- **Costo:** dos bucles de sondeo encadenados —Gmail y el inbox— consumen el mismo presupuesto de 15
  minutos de visibilidad. Sus frecuencias deben fijarse conjuntamente.
