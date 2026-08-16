# ADR 0010: Política de reintentos y clasificación de errores

## Estado

Aceptado. Revisión 3. Añade la clase terminal `OBSOLETO` y define **cuándo notifica cada clase**:
`DIFERIBLE` avisa al entrar, no al agotar, porque sus reintentos no se agotan (revisión adversarial
v2: C3, A11).

La revisión 2 reemplazó la versión previa (`adrs - v1/0010`), que agrupaba la superación de cuota con
los fallos de red y no definía camino de recuperación tras agotar los reintentos.

## Contexto

El PRD exige hasta 3 reintentos con espera creciente y notificación en un máximo de 5 minutos tras
agotarlos. Pero trata todos los fallos bajo el mismo mecanismo, y no todos se comportan igual:

- Un fallo de red se resuelve solo en segundos.
- Un adjunto corrupto **no se resuelve nunca**: reintentarlo tres veces solo retrasa el aviso.
- Una cuota de Gmail o Drive se restablece **por ventana** —por minuto o diaria—, de modo que tres
  intentos con espera corta se agotan sin llegar a la reapertura y producen un error falsamente
  terminal.

La versión anterior distinguía bien las dos primeras clases y fallaba en la tercera. Y en todos los
casos definía un estado terminal `ERROR` **sin ninguna acción de reproceso**: el criterio del PRD de
que el 100% de las validadas tengan carpeta en Drive era inalcanzable por construcción, y la única
salida era intervenir la base de datos a mano.

## Decisión

### Tres clases de error, y una clase terminal que no lo es

| Clase | Comportamiento | Ejemplos |
|---|---|---|
| `TRANSITORIO` | Hasta 3 intentos, espera creciente en segundos | Timeout de red, 5xx de Google, SQL Server momentáneamente inaccesible |
| `DIFERIBLE` | Reintento planificado **al abrirse la ventana de cuota**, no en segundos | Superación de cuota de Gmail, Drive o Sheets (429 con reintento diferido) |
| `PERMANENTE` | **Sin reintentos.** Notificación inmediata | Adjunto corrupto, protegido con contraseña o en formato no soportado; XML inválido; credenciales revocadas; tipo de comprobante no válido |
| `OBSOLETO` | **No es un error.** Estado terminal, sin reintento y sin notificación | El evento no es el más reciente de su agregado: ya se aplicó uno posterior (ADR 0004) |

Cada intento se registra con su número, resultado, momento y **próxima fecha de reintento**.

### Cuándo notifica cada clase

| Clase | Cuándo se notifica |
|---|---|
| `TRANSITORIO` | **Al agotar** los tres intentos, en un máximo de 5 minutos |
| `PERMANENTE` | **De inmediato** |
| `DIFERIBLE` | **Al entrar** en la clase. Una sola vez por incidencia, no en cada reintento |
| `OBSOLETO` | **Nunca** |

`DIFERIBLE` notifica al entrar y no al agotar porque **sus reintentos no se agotan**: se reprograman
a la apertura de la ventana de cuota, que en una cuota diaria es al día siguiente. Con el criterio
del PRD aplicado literalmente —*"agotados los reintentos, la notificación se envía en un máximo de 5
minutos"*—, **el estado que más tiempo mantiene el sistema degradado sería el único que no avisa
nunca**. La incidencia existiría en el panel y solo la vería quien fuera a buscarla.

### Camino de recuperación

Toda incidencia expone al menos: **tipo de error, mensaje, fecha, número de intentos y acciones
disponibles**.

```http
POST /api/incidencias/{id}/reprocesar
```

`REPROCESAR` viaja por `CommandQueue` (ADR 0004) y Python reejecuta la operación. **El reproceso no
crea duplicados**: la idempotencia se apoya en el `HashContenido` del adjunto y en el índice único
de identidad del comprobante.

### Notificación

Telegram como canal primario, correo electrónico como respaldo si Telegram falla. Ambos intentos se
registran. El mecanismo vive en el worker.

### Reautenticación

Las credenciales revocadas se clasifican como `PERMANENTE` y notifican de inmediato. La salida es
`POST /api/integraciones/google/reconectar`, que lanza el flujo OAuth (ADR 0015). Sin ese endpoint,
la clasificación era correcta pero no llevaba a ninguna parte.

## Alternativas consideradas

- **Reintentar todo por igual, con 3 intentos.** Es lo que dice el PRD literalmente. Se descartó
  porque retrasa la notificación de los fallos permanentes justo cuando el usuario podría actuar, y
  porque agota el presupuesto de intentos de los diferibles antes de que la cuota se reabra.
- **Reintento exponencial ilimitado con techo.** Elimina la clase permanente. Se descartó porque un
  PDF corrupto reintentado indefinidamente consume recursos sin ninguna posibilidad de éxito, y
  oculta el problema en vez de escalarlo.
- **Cola de mensajes muertos.** Es el patrón habitual. Se descartó como mecanismo separado porque la
  bandeja unificada de ADR 0003 ya cumple esa función y es visible para el usuario, en lugar de ser
  una tabla que solo el operador consulta.

## Consecuencias

- Un adjunto corrupto se notifica de inmediato, sin gastar tres intentos.
- Una cuota agotada deja de producir errores falsamente terminales.
- El criterio del PRD sobre carpetas en Drive es alcanzable: existe una acción de reproceso por
  incidencia, sin tocar la base de datos.
- **Costo:** clasificar mal un error es asimétrico. Marcar como permanente algo transitorio detiene
  el procesamiento y exige intervención; lo contrario solo gasta intentos. La clasificación debe
  errar hacia transitorio ante la duda.
- **Costo:** la clase `DIFERIBLE` obliga a interpretar las cabeceras de reintento de cada API de
  Google, que no son uniformes entre servicios.
- **Costo, ya mitigado:** el mecanismo de notificación vive dentro del worker, de modo que si el
  worker se detiene no hay quien avise, y el reinicio es manual (ADR 0001). La revisión v2 lo cierra
  desde fuera: el worker escribe un **latido** en `EstadoIntegracion` (ADR 0003) y el agregador de
  logs alerta por **ausencia** de ese latido (ADR 0015). La alerta no vive dentro del worker, que era
  exactamente por qué el riesgo se había aceptado.
- **Costo:** `OBSOLETO` no es un error y no debe contar como fallo en ninguna métrica ni disparar
  ninguna alerta. Un pico de eventos obsoletos es normal tras una racha de correcciones sobre la
  misma factura.
