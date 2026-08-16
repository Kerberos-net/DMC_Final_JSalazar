# Revisión adversarial — TECH-DESIGN.md y ADRs 0001-0010

**Proyecto:** Gestor de Facturas de Compra
**Fecha:** 2026-08-09
**Objeto de la revisión:** `TECH-DESIGN.md` y los diez ADRs de `adrs/`
**Insumos de contraste:** `PRD.md`, `DESIGN.md`, `DESIGN_BRIEF.md` y el prototipo navegable
`handoff/Gestor de Facturas.dc.html`

## Método y condiciones

Revisión ejecutada en una conversación limpia, sin historial de cómo se produjo el diseño, que es la
condición bajo la cual este tipo de revisión conserva valor: preguntarle a la misma conversación que
generó un documento si está segura tiende a producir una defensa, no una crítica.

Los diez ADRs fueron cuestionados uno por uno contra este listón: que el contexto justifique
realmente la decisión, que las alternativas sean opciones viables y no una falsa elección, que las
consecuencias incluyan al menos un costo real, y que la decisión sea proporcional a la escala
efectiva del proyecto. Después se cruzaron los documentos entre sí y contra el prototipo.

Un segundo pase cubrió **infraestructura y operación** como área de decisión propia (C8, C9 y
A14-A18), que el primer pase solo había rozado de forma tangencial.

Este documento **reporta**; no modifica `TECH-DESIGN.md` ni ningún ADR. Qué se corrige y cómo es
decisión del autor del diseño.

## Resumen ejecutivo

El diseño es serio. Las ADRs tienen alternativas genuinas y costos declarados con honestidad, algo
poco frecuente. Pero contiene **once huecos que impiden implementarlo o explotarlo tal como está
escrito**, y varios solo aparecen al contrastar el TDD contra el prototipo de pantallas.

Los tres que bloquean el desarrollo antes que ningún otro son: de dónde salen las líneas de la
factura (C1), cómo se resuelve un duplicado del lado del servidor (C2), y cuál es el ciclo de vida
real del asiento —borrador, confirmación, edición de cabecera— (C3). Los tres tocan el mismo
entregable central y ninguno se resuelve implementando: se resuelven decidiendo.

A ellos se suman otros dos del mismo peso, ambos en la pantalla de detalle y validación —la que el
propio brief de diseño llama "la pantalla central del producto"—: el visor de documentos no tiene
ruta de datos ni decisión de renderizado (C10), y la interfaz permite adjuntar y eliminar archivos a
mano por un camino que contradice la partición de propiedad de datos (C11).

Aparte, y por encima de todos ellos en consecuencia para el negocio: **el diseño convierte a una
instancia de SQL Server en el único libro de compras de la empresa y no dice una sola palabra sobre
respaldo (C9)**. Es un problema de operación, no de código, y por eso no bloquea el desarrollo — pero
es el que puede costar más caro.

Infraestructura es, en conjunto, la mayor zona ciega del documento: diez ADRs, tres artefactos
desplegables y cero decisiones sobre dónde y cómo se ejecutan.

## Índice de hallazgos

| # | Severidad | Hallazgo | Documento afectado |
|---|---|---|---|
| C1 | Crítico | `FacturaDetalle` no tiene quién la escriba | TDD · modelo de datos |
| C2 | Crítico | El duplicado bloquea la validación sin camino de resolución | TDD Flujo 3 · ADR 0008 |
| C3 | Crítico | Ciclo de vida del asiento subespecificado y autocontradictorio | ADR 0006 · ADR 0008 · TDD Flujos 3 y 4 |
| C4 | Crítico | Los falsos positivos no existen en el diseño | TDD · ADR 0005 |
| C5 | Crítico | El outbox tiene un solo evento | ADR 0004 · TDD Flujo 5 |
| C6 | Crítico | Facturas en dólares de fin de semana quedan contabilizadas en cero | TDD Flujos 2 y 4 |
| C7 | Crítico | El estado `ERROR` de `Factura` es inalcanzable | ADR 0003 · modelo de datos |
| C8 | Crítico | Si el worker muere, el sistema queda mudo | Infraestructura · ADR 0001 |
| C9 | Crítico | La base es el único libro contable y no hay política de respaldo | Infraestructura · ausente |
| C10 | Crítico | El visor de documentos no tiene ruta de datos ni decisión de render | TDD · ADR 0008 · DESIGN_BRIEF |
| C11 | Crítico | Adjuntar y eliminar archivos a mano contradice ADR 0003 | ADR 0003 · ADR 0008 |
| A1 | Advertencia | Sin camino de recuperación tras agotar reintentos | ADR 0010 · TDD Flujo 5 |
| A2 | Advertencia | El alcance de Python se justifica con "coherencia" | ADR 0002 · ADR 0003 |
| A3 | Advertencia | ADR 0005 reintroduce el `BackgroundService` que ADR 0004 descartó | ADR 0005 · ADR 0001 · ADR 0004 |
| A4 | Advertencia | `IntegrationOutbox` es coescrita, y su idempotencia está sobreafirmada | ADR 0004 · TDD propiedad de datos |
| A5 | Advertencia | Sin política de redondeo ni tipo decimal | TDD Flujo 4 |
| A6 | Advertencia | La pantalla de Configuración no tiene modelo de datos ni contrato | TDD · ADR 0008 |
| A7 | Advertencia | No hay ADR de gestión de secretos | Área de decisión ausente |
| A8 | Advertencia | Dónde viven los archivos no está decidido | TDD · ADR 0001 |
| A9 | Advertencia | ADR 0007 no decide el valor de `SameSite` | ADR 0007 · ADR 0001 |
| A10 | Advertencia | ADR 0003 quedó desactualizada y sin marcar | ADR 0003 · ADR 0007 |
| A11 | Advertencia | El mayor riesgo del proyecto no tiene ADR | Área de decisión ausente |
| A12 | Advertencia | Zona horaria sin decidir | Área de decisión ausente |
| A13 | Advertencia | El TDD cita mal el prototipo en dos justificaciones | TDD · ADR 0006 |
| A14 | Advertencia | Ninguna ADR decide dónde y cómo se ejecuta el sistema | Infraestructura · ADR 0001 · ADR 0002 |
| A15 | Advertencia | TLS no está decidido y ADR 0007 depende de él | Infraestructura · ADR 0007 |
| A16 | Advertencia | Credenciales de Google sin decidir, con caducidad concreta | Infraestructura · ADR 0010 |
| A17 | Advertencia | No hay observabilidad fuera de la propia base de datos | Infraestructura · ausente |
| A18 | Advertencia | Sin entornos ni procedimiento de despliegue coordinado | Infraestructura · ADR 0001 · ADR 0003 |
| S1 | Sugerencia | `Factura.estado` mezcla dos ejes ortogonales | TDD · Flujo 7 |
| S2 | Sugerencia | Falta unicidad en el motor sobre (RUC, tipo, número) | Modelo de datos |
| S3 | Sugerencia | "Superación de cuota" mal clasificada como transitorio | ADR 0010 |
| S4 | Sugerencia | Falta ADR de migraciones del esquema compartido | Área de decisión ausente |
| S5 | Sugerencia | Crecimiento del almacenamiento no dimensionado | Infraestructura · PRD |
| D1 | Dependencia | Reglas contables pendientes: bloquean C1 y C3 | `REGLAS.md` no elaborado |

---

# Crítico

Un hallazgo es crítico cuando el diseño va a fallar o contradice un requisito declarado si no se
atiende.

## C1 — `FacturaDetalle` no tiene quién la escriba. El asiento automático no es implementable.

Todo el detalle del asiento depende de "mapear cada producto de `FacturaDetalle` a su cuenta
contable". Pero nada alimenta esa tabla:

- El PRD (línea 23) enumera lo que extrae el OCR: tipo de comprobante, número, proveedor, monto,
  moneda y fecha de emisión. **Sin líneas.**
- El propio `DatosExtraidos` del TDD repite exactamente esa lista. **Sin líneas.**
- El prototipo tiene **cero ocurrencias** de la palabra "producto". No hay pantalla de captura de
  líneas de factura ni ítem de navegación "Productos".
- Y el prototipo **sí genera el asiento**, con un modelo completamente distinto. En
  `defaultLineas()` (línea 1666) produce tres líneas fijas derivadas del total:
  `60111 Compras - Bienes` al debe por `monto / 1.18`, `40111 IGV` al debe por la diferencia, y
  `42011 Facturas por pagar` al haber por el monto.

Hay dos modelos incompatibles para la misma funcionalidad y el TDD adoptó el del PRD sin notar que
su insumo no existe. `Producto`, el mapeo producto→cuenta y el indicador "línea sin cuenta asignada"
son estructura muerta mientras no se decida de dónde salen las líneas.

**Por qué importa:** es el entregable central del sistema. El criterio de éxito del PRD dice que el
100% de las facturas validadas generan un asiento contable con cabecera y detalle.

## C2 — El duplicado bloquea la validación y no existe forma de resolverlo.

El criterio del TDD es explícito y correcto: *"Intentar validar una factura con el indicador de
duplicado sin resolver es rechazado por la API con un error de dominio específico, no por una
validación solo del lado del cliente"*. El problema es que "resolver" no está definido en ninguna
parte:

- No hay campo en `Factura` que represente el duplicado revisado.
- No hay endpoint en ADR 0008 para resolverlo.
- El único mecanismo existente es el checkbox **"Revisé el duplicado"** del prototipo (línea 468):
  un `dupAck` de estado local que se reinicia a `false` cada vez que se abre la factura (línea 1453)
  y que **nunca se persiste**. Es exactamente la validación de cliente que el criterio prohíbe.

Resultado: una factura marcada como duplicada queda bloqueada de forma permanente.

Hay un segundo agujero en la misma función: **la bandera se calcula en la promoción y nunca se
reevalúa**. El número de comprobante lo puso el OCR, de modo que el escenario más probable es que el
usuario lo corrija durante la validación — creando un duplicado real que pasa sin bandera, o
arrastrando una bandera falsa que ya no aplica.

**Por qué importa:** el criterio de éxito del PRD exige detectar el duplicado "sin excepciones". Una
bandera calculada una sola vez sobre un dato que el usuario puede editar después no es una
invariante.

## C3 — El ciclo de vida del asiento está subespecificado y los criterios se contradicen entre sí.

El Flujo 3 establece que el asiento se crea **en la misma transacción** que la validación. El Flujo 4
establece que *"el sistema impide **confirmar el asiento** mientras exista alguna [línea sin cuenta
asignada]"*. No se puede impedir la confirmación de algo que ya se creó atómicamente.

Falta un estado de borrador: `AsientoContable` solo contempla `GENERADO` y `ANULADO`, y no existe
endpoint de confirmación. El PRD (línea 28) es literal: el ajuste manual de la cuenta ocurre **antes**
de confirmar el asiento. El prototipo hace justamente eso — compone las líneas en la pantalla de
detalle y las copia al asiento en el momento de validar (línea 1528).

En el mismo bloque faltan tres piezas del contrato:

- **No existe endpoint para editar la cabecera del asiento**, pese a que el PRD lo exige
  textualmente: "al corregir el proveedor después, el asiento ya generado debe editarse y el cambio
  quedar trazable".
- No hay forma de **agregar ni eliminar líneas**, cosa que el prototipo sí permite (`addLinea`).
- `POST /asientos/{id}/lineas/{numero}/cuenta` identifica la línea por su número; si se agregan o
  eliminan líneas, ese identificador se corre y el endpoint deja de ser estable.

## C4 — Los falsos positivos no existen en el diseño.

El PRD los declara como caso borde: *"Correo mal etiquetado, en spam, o que no corresponde a una
factura de compra real (falso positivo de detección)"*. El TDD no los menciona ni una vez.

La promoción es automática e idempotente, y los estados de `Factura` son `PENDIENTE_VALIDACION`,
`VALIDADA`, `ERROR` y `ALERTA`: **no hay descarte**. Todo PDF que el worker procese se convierte en
una factura que permanece en la bandeja indefinidamente, sin acción posible.

Adyacente y del mismo origen: tampoco hay criterio de detección definido — etiqueta de Gmail,
remitente permitido, palabras clave, clasificación por IA. El primer paso del pipeline es una caja
negra, justo el paso del que depende que existan o no falsos positivos.

## C5 — El outbox tiene un solo evento. Lo posterior a la validación nunca llega a Sheets ni a Drive.

ADR 0004 cubre "las integraciones **posteriores a la validación**", disparadas por el evento de
validación. Pero el PRD exige que el asiento pueda anularse y editarse después de creado — el caso
canónico es corregir P0000 por el proveedor real. Ninguna de esas operaciones genera un evento de
outbox.

Consecuencia concreta: **un asiento anulado sigue contando como gasto en el dashboard de Looker
Studio**, y la carpeta de Drive conserva el proveedor equivocado. El criterio "desfase máximo de 24
horas" se incumple de forma permanente — no por retraso, sino porque el dato nunca se corrige.

## C6 — Toda factura en dólares emitida en fin de semana o feriado queda contabilizada en cero.

La SBS publica tipo de cambio únicamente en días hábiles. La regla del diseño es "TC de la fecha de
emisión; si no hay fila, 0.00 con observación". Una factura emitida un sábado **nunca** tendrá TC
para esa fecha: no es una condición transitoria que se resuelva sola, es permanente.

Como todo el asiento se expresa en soles con ese tipo de cambio, base imponible, IGV y neto quedan en
0.00 — y el control de cuadre entre débitos y créditos **pasa perfectamente**, porque 0 = 0. El
asiento es aritméticamente consistente y contablemente basura.

Faltan dos decisiones: la política contable real (en Perú se usa el tipo de cambio publicado del
último día hábil) y la re-resolución posterior, porque hoy nada reintenta la obtención del TC cuando
la SBS publica.

## C7 — El estado `ERROR` de `Factura` es inalcanzable bajo ADR 0003.

Python no puede escribir tablas de negocio. Los fallos de integración viven en el outbox y en las
tablas del contexto de integración. En consecuencia **nadie transiciona una factura a `ERROR`**:
.NET no se entera del fallo y Python no tiene permitido escribirlo.

Peor aún: los fallos previos a la promoción —adjunto corrupto, motor OCR caído— nunca llegan a
producir una `Factura`, de modo que no pueden aparecer en la bandeja. Pero el prototipo sí las
muestra (las facturas 6 y 11 tienen `estado:'error'`) y el `DESIGN_BRIEF.md` las pide explícitamente
como uno de los cuatro chips de estado de la bandeja. Modelo de datos, ADR 0003 y prototipo afirman
tres cosas distintas.

## C8 — Si el worker muere, el sistema queda mudo: nadie vigila al vigilante.

Todo el mecanismo de notificación —Telegram con respaldo por correo— vive **dentro del worker
Python**. Si el worker se cae, se cuelga en una llamada sin tiempo de espera, o la máquina se
reinicia, el componente encargado de avisar de los fallos es precisamente el que ha fallado. No hay
heartbeat, ni watchdog externo, ni alerta por ausencia del tipo "hace seis horas que no se procesa un
correo".

Y el fallo es silencioso por diseño: **una bandeja sin facturas nuevas es indistinguible de un día
sin facturas**. El asistente contable no tiene forma de notar la diferencia hasta que un proveedor
reclame.

**Por qué importa:** contradice dos criterios de éxito del PRD a la vez. El de notificación con tasa
de entrega ≥99% supone un notificador vivo, y el de visibilidad en ≤15 minutos supone un worker
corriendo — dos supuestos que nada en el diseño garantiza ni verifica. ADR 0001 afirma que el worker
"puede reiniciarse, detenerse o desplegarse de forma independiente", pero nunca dice **quién lo
reinicia**.

## C9 — La base de datos es el único libro contable de la empresa y no hay política de respaldo.

El PRD elimina deliberadamente el sistema contable externo: *"el asiento generado se guarda
únicamente en la base de datos asignada al software; no hay integración ni migración hacia ningún
sistema de gestión contable externo"*, y añade conservación indefinida. Esa decisión convierte a una
instancia de SQL Server en **el libro de compras de la empresa, sin copia en ningún otro sistema**.

Ni el TDD ni ninguna de las diez ADRs mencionan respaldo, frecuencia de copia, RPO/RTO, prueba de
restauración, retención de copias ni ubicación fuera del mismo host. Tampoco aparece en la sección de
riesgos técnicos abiertos.

Drive no cubre este hueco: contiene los adjuntos de las facturas validadas, pero **no los asientos,
ni las correcciones, ni la auditoría, ni el estado de nada**. Perder esa base es perder la
contabilidad de compras, y el diseño acaba de retirar el sistema que antes la sostenía en paralelo.

**Por qué importa:** es el mayor riesgo de negocio del proyecto y el único que no se recupera con
tiempo de desarrollo. No bloquea escribir código —por eso figura después de C1-C3— pero debe estar
resuelto antes de que el sistema procese la primera factura real.

## C10 — El visor de documentos no tiene ruta de datos ni decisión de renderizado.

`DESIGN_BRIEF.md` define la pantalla de detalle y validación como *"la pantalla central del producto
— aquí se pasa la mayor parte del tiempo"*, y su razón de ser es el patrón documento + formulario:
*"imagen/PDF de la factura escaneada a la izquierda, formulario de datos extraídos a la derecha, para
poder verificar visualmente cada campo contra el original"*. Sin el documento a la vista, la pantalla
no sirve para lo único que existe para hacer.

El prototipo **no lo implementa**: es un marcador de posición con proporción de hoja
(`aspect-ratio: 0.75`), fondo rayado y el texto "PDF de la factura" (línea 489). El prototipo difiere
la decisión, y el TDD nunca la recoge.

Lo que falta decidir es una cadena completa, no un endpoint:

- **Quién sirve los bytes.** El TDD es tajante: la SPA "no accede a la base de datos; consume
  exclusivamente la API de negocio". Entonces .NET debe servir el archivo. Pero el archivo lo
  escribió Python (A8), así que el visor es donde esa dependencia de almacenamiento compartido deja
  de ser teórica.
- **Por dónde.** ADR 0008 enumera el contrato completo y **no incluye ningún endpoint de
  documentos**. No hay forma de pedir el contenido de un adjunto.
- **Cómo se renderiza.** Visor nativo del navegador (`<iframe>` / `<embed>`), PDF.js, o conversión a
  imagen en el servidor. No es intercambiable: el brief dice "imagen/PDF", y un adjunto escaneado
  puede llegar como JPG o PNG, que exige otra ruta. Tampoco se define el caso de varias páginas.
- **Cómo se autoriza.** Con cookie de sesión y orígenes distintos (A9), un `<iframe src>` cross-origin
  no envía la cookie; con `SameSite=None` sí, pero exige HTTPS (A15). El visor es exactamente el
  punto donde esas dos decisiones pendientes se manifiestan como un panel en blanco.
- **De dónde sale el archivo antes de validar.** El documento debe verse **para poder validar**, y
  Drive solo recibe los archivos **al validar**. Es decir, el visor no puede apoyarse en Drive:
  obliga a un almacén previo, servido por .NET. Confirma A8 y lo convierte en bloqueante.

**Por qué importa:** el criterio de éxito del PRD fija menos de 5 minutos de validación por factura,
y esa cifra la domina el tiempo de tener el documento y el formulario a la vista al mismo tiempo. Hoy
no hay ninguna decisión que la sostenga.

## C11 — La interfaz permite adjuntar y eliminar archivos a mano, y ese camino contradice ADR 0003.

En la misma pantalla, los contadores "Orden de compra (N)" y "Medios probatorios (N)" (líneas 483-484)
abren un modal que contiene **`+ Adjuntar archivo(s)`** con `<input type="file" multiple>` (línea 41)
y una acción `onRemove` por archivo (línea 1808). El usuario sube y borra adjuntos desde la
aplicación.

El diseño no contempla nada de eso, y no es un endpoint que falte: es una contradicción estructural.

- `DocumentoRecibido` es propiedad **exclusiva de Python** (ADR 0003) y nace de un adjunto descargado
  de Gmail. Un archivo que sube el usuario entra por la SPA y llega a .NET. Que .NET escriba
  `DocumentoRecibido` **viola la partición de propiedad de datos**; la alternativa es una segunda
  tabla para adjuntos manuales, que nadie decidió y que parte en dos el concepto de "documentos de
  la factura".
- ADR 0008 no tiene endpoint de carga ni de borrado, ni decisión sobre tipos permitidos, tamaño
  máximo o análisis de los archivos — pese a que el prototipo expone "Adjuntos permitidos" como
  ajuste de configuración (A6).
- **Borrar un adjunto altera la evidencia que se archiva en Drive**, y `AuditoriaCorreccion` registra
  campos modificados, no archivos eliminados. Queda un hueco de trazabilidad justo sobre el respaldo
  documental de un asiento contable.

**Por qué importa:** esto no es un adorno del prototipo, es la respuesta natural a un caso borde que
el PRD declara — *"correo de factura que llega sin OC o sin medios probatorios adjuntos"*. El TDD lo
resuelve solo de forma descriptiva ("la ausencia se representa por la inexistencia de filas de ese
tipo, sin bloquear la factura") y se detiene ahí. Pero el criterio de éxito exige que el 100% de las
validadas terminen en Drive **con la factura y los medios probatorios**: si faltan y no hay forma de
aportarlos, ese criterio no se cumple.

---

# Advertencia

Un hueco real o una justificación débil, que conviene resolver antes de implementar.

## A1 — Sin camino de recuperación tras agotar los reintentos.

El PRD exige que el 100% de las facturas validadas tengan su carpeta en Drive. El diseño define un
estado terminal `ERROR` sin ninguna acción de reproceso: no hay endpoint, ni botón por incidencia en
el panel de errores, ni reencolado. El criterio es inalcanzable por construcción y la única salida
sería intervenir la base de datos a mano.

## A2 — El alcance de Python se justifica con una sola palabra: "coherencia".

ADR 0002 fundamenta Python por OCR y scraping, y ese argumento es sólido. Luego lo extiende a Gmail,
Drive, Sheets, Telegram y correo "por coherencia con la propiedad del procesamiento y de los
reintentos". Esa palabra es la que termina pagándose: dos runtimes, base de datos como mecanismo de
comunicación entre procesos, outbox, mecanismo de promoción, tipos duplicados en C#, TypeScript y
Python, y despliegue coordinado.

Los clientes de Gmail, Drive y Sheets en .NET son igual de maduros que los de Python. La alternativa
intermedia — **Python solo para OCR y SBS, .NET dueño del resto de integraciones y de toda la
persistencia** — no se evaluó en ninguna ADR. ADR 0003 consideró "Python como servicio HTTP sin
estado" y la descartó por trasladar la orquestación a .NET, pero .NET ya orquesta la promoción
(ADR 0005) y ya es dueño del outbox, así que ese rechazo es más débil de lo que aparenta.

Para un desarrollador solo, con un usuario y sin fecha límite, esa es la conversación que falta.

## A3 — ADR 0005 reintroduce por la puerta de atrás lo que ADR 0001 y ADR 0004 usaron para descartar alternativas.

El mecanismo de promoción exige que .NET sondee las tablas de Python con un `BackgroundService`
propio; la propia ADR lo reconoce en sus consecuencias. ADR 0004 descartó una alternativa
precisamente por poner un `BackgroundService` en .NET, y ADR 0001 justificó la separación de
componentes para que el trabajo de fondo no compartiera proceso con la atención de la interfaz.

Además quedan dos bucles de sondeo encadenados consumiendo el mismo presupuesto de 15 minutos, y
.NET usando una tabla de Python como cola de trabajo: un contrato de facto que ADR 0003 no reconoce
como tal.

## A4 — `IntegrationOutbox` no es "escribe .NET, consume Python", y su idempotencia está sobreafirmada.

Consumir un outbox exige `UPDATE`: tomar la tarea, marcar el estado por integración, contar
reintentos, expirar tareas colgadas. Python escribe esa tabla. No está mal, pero rompe la mitigación
que el propio TDD propone en sus riesgos abiertos —esquemas separados y permisos por usuario de base
de datos— y contradice la tabla de propiedad tal como está redactada.

En la misma ADR hay una afirmación que no se sostiene: *"El estado independiente por integración hace
que los reintentos sean seguros de repetir, evitando carpetas duplicadas en Drive o filas repetidas
en la hoja de cálculo"*. **Es falso** para la ventana entre que la API externa responde con éxito y
que se persiste el estado. Un reinicio en ese punto duplica la carpeta o la fila. La idempotencia hay
que construirla en cada integración —buscar antes de crear en Drive, upsert por clave en Sheets—; el
outbox no la regala.

## A5 — No hay política de redondeo ni tipo decimal.

Conversión de moneda más IGV del 18% más redondeo por línea garantiza que el control "los débitos no
coinciden con los créditos" dispare sobre asientos legítimos. El prototipo ya usa una tolerancia de
0.01 (línea 1357) que el TDD no recoge.

Faltan tres decisiones: el orden de operaciones (convertir y luego prorratear, o prorratear y luego
convertir), los decimales de almacenamiento, y si se genera una línea de ajuste por diferencia de
redondeo.

## A6 — La pantalla de Configuración completa no tiene modelo de datos ni contrato.

El prototipo persiste: carpetas monitoreadas, filtros de correo (asunto, remitentes permitidos,
palabras clave en el cuerpo), frecuencia de sondeo, fecha de "procesar desde", marcar correos como
leídos, eliminar correos procesados, adjuntos permitidos, token y chat destino del bot de Telegram,
cuatro toggles de notificación, resumen diario por correo, perfil de usuario (nombre completo, correo
electrónico, cambio de contraseña), apariencia y filas por página por defecto. Cero tablas, cero
endpoints.

Dos consecuencias sustantivas más allá del almacenamiento:

- "Marcar correos como leídos" y "eliminar correos procesados" implican que el worker **escribe en
  Gmail**, algo que el TDD nunca menciona.
- **"Sincronizar ahora" no tiene camino arquitectónico**: la SPA solo habla con .NET, y el único
  canal .NET→Python es un outbox de eventos de negocio. Una orden manual de sincronización no encaja
  en ese contrato.

## A7 — No hay ADR de gestión de secretos.

Refresh token de Gmail, Drive y Sheets; token del bot de Telegram; credenciales SMTP; cadena de
conexión a la base de datos. El prototipo además captura el token de Telegram desde la interfaz, lo
que obliga a persistirlo. En un sistema contable interno esto es una decisión de arquitectura, no un
detalle de despliegue.

## A8 — Dónde viven los archivos no está decidido.

`DocumentoRecibido` incluye "ubicación del archivo almacenado" sin decir dónde. Python escribe los
PDFs y .NET los sirve al visor de la SPA, lo que obliga a un volumen compartido o un almacén de
objetos que ninguna ADR menciona y que erosiona la independencia de despliegue que reivindica
ADR 0001.

Se suma que el PRD exige conservación indefinida de facturas y medios probatorios: falta decidir si
Drive es el archivo de registro y qué ocurre con los documentos previos a la validación, que nunca
llegan a Drive.

## A9 — ADR 0007 no decide el valor de `SameSite`, que es exactamente donde está el problema.

Con CORS con credenciales —es decir, SPA y API en orígenes distintos— una cookie `SameSite=Lax` o
`Strict` no se envía: la sesión simplemente no funciona. Solo `SameSite=None; Secure` opera
cross-site, y eso reabre la exposición a CSRF que la decisión pretendía cerrar.

La decisión real es **mismo origen detrás de un proxy inverso frente a orígenes distintos**, y no
está tomada. El criterio de aceptación correspondiente ("la cookie se emite con los atributos
`HttpOnly` y `SameSite`") no es un criterio, es un marcador de posición.

Añadido: ADR 0001 declara en sus consecuencias "autenticación por token", lo que contradice a
ADR 0007 y nunca se corrigió.

## A10 — ADR 0003 quedó desactualizada y nadie la marcó.

Sigue listando `Rol` y `UsuarioRol`, que ADR 0007 eliminó explícitamente, y omite `DatosExtraidos`,
`Producto`, `CuentaContable`, `AuditoriaCorreccion` e `IntegrationOutbox`. Ambas ADRs figuran como
"Aceptado" y se contradicen. En MADR eso se resuelve marcando la anterior como enmendada o superada
por la posterior.

## A11 — El mayor riesgo del proyecto no tiene ADR.

El propio TDD escribe que la precisión del OCR/IA es "el mayor riesgo técnico del proyecto". No hay
ni una ADR que fije la frontera de abstracción del motor de extracción, el criterio de evaluación con
facturas reales, ni —crítico en un sistema contable— **si los documentos salen de la organización
hacia un servicio de terceros**.

El TDD menciona "XML" de pasada, sin decidir nada al respecto, cuando la factura electrónica peruana
(UBL/SUNAT) trae número, RUC, montos **y líneas** exactos. Es probablemente la respuesta a C1 y
desactiva buena parte de este riesgo para los comprobantes electrónicos.

## A12 — Zona horaria sin decidir.

Lima es UTC-5, Gmail entrega marcas de tiempo en UTC, la SBS publica por fecha local y la fecha
contable determina el periodo. Una factura emitida a las 23:30 se registra al día siguiente en UTC:
busca el tipo de cambio equivocado y, si es fin de mes, cae en el periodo contable equivocado.

## A13 — El TDD cita mal el prototipo en dos justificaciones que se apoyan en él.

- Afirma en sus riesgos abiertos que el prototipo "no contempla de forma explícita" el estado
  intermedio entre validar y archivar. Sí lo contempla: cada factura del prototipo tiene `migracion`
  y `sheetsSync`.
- ADR 0006 sostiene que la glosa y la fecha contable "se derivaron del prototipo de pantallas" como
  atributos del asiento. En el prototipo, `glosa`, `mesContable` y `diaContable` viven en la
  **factura**, y el detalle del asiento las lee de ahí.

Ninguna de las dos invalida la decisión —modelar el asiento como entidad propia sigue siendo
correcto—, pero quien implemente va a confiar en la lectura del TDD en lugar de releer el prototipo.

En esa misma revisión aparece un conflicto que sí hay que cerrar: el prototipo tiene `compro`
(`CP-000112`), un correlativo interno distinto del número fiscal (`F001-00234`), que el modelo de
datos omite y que el PRD excluye explícitamente ("el número de comprobante de la cabecera del asiento
es el mismo número extraído de la factura, no un correlativo propio del software").

## A14 — Ninguna ADR decide dónde y cómo se ejecuta el sistema.

Diez ADRs, tres artefactos desplegables y **cero decisiones sobre el entorno de ejecución**: máquina
física, máquina virtual o contenedor; sistema operativo; cómo se sirve la SPA compilada; si hay proxy
inverso; qué supervisa los procesos y los reinicia tras un fallo; qué arranca tras un reinicio del
host.

No es un detalle que se resuelva al final, porque tres hallazgos ya abiertos dependen de esta
decisión:

- **A9** — que la SPA y la API compartan origen o no es lo que determina el valor de `SameSite`, y
  eso depende de si hay proxy inverso.
- **A8** — que Python escriba los archivos y .NET los sirva obliga a un volumen compartido o a un
  almacén de objetos, lo que restringe la topología posible.
- **C8** — quién reinicia el worker es exactamente esta decisión.

ADR 0002 llega a nombrar la restricción sin resolverla: *"SQL Server impone una huella de
infraestructura mayor... y ata el proyecto a las opciones de despliegue compatibles con esa
licencia"*. Se declara el costo y nunca se decide qué se hace con él.

## A15 — TLS no está decidido, y ADR 0007 depende de esa decisión.

Tres cosas del diseño exigen HTTPS y ninguna ADR lo menciona:

- La cookie `Secure` que obliga `SameSite=None`, si el despliegue termina siendo cross-origin (A9).
- Las credenciales del usuario, que viajan en el cuerpo del POST de login. Sin TLS, la contraseña
  cruza la red interna en claro — y el PRD asignó a este proyecto la responsabilidad de gestionar
  contraseñas propias en lugar de delegarla (ADR 0007).
- Los *redirect URI* de OAuth de Google, que exigen HTTPS salvo en `localhost`.

Falta decidir si hay certificado, de dónde sale (autoridad interna, Let's Encrypt, certificado
comprado), quién lo renueva y si la terminación TLS ocurre en el proxy o en Kestrel.

## A16 — Credenciales de Google sin decidir, con un modo de fallo concreto y fechado.

No se decide entre cuenta de servicio y OAuth de usuario, y las dos opciones tienen implicaciones
distintas que nadie evaluó:

- Con **cuenta de servicio**, acceder al buzón de Gmail exige delegación a nivel de dominio en
  Google Workspace: una configuración administrativa que hay que solicitar, no algo que el
  desarrollador active por su cuenta.
- Con **OAuth de usuario**, el refresh token caduca o se revoca. Y hay un caso concreto y muy
  probable: una aplicación en modo *testing* en Google Cloud **caduca sus refresh tokens cada 7
  días**. El sistema funcionaría una semana y se detendría.

ADR 0010 clasifica "credenciales revocadas o inválidas" como error permanente que se notifica de
inmediato — clasificación correcta —, pero **no existe camino de reautenticación**. El
"Conectar / Reconectar" del prototipo no tiene backend ni endpoint (A6), así que la única salida es
intervenir a mano en el servidor.

Súmese la cuota de almacenamiento de Drive frente al requisito de conservación indefinida: nadie
verificó que el plan de Workspace la soporte.

## A17 — No hay observabilidad fuera de la propia base de datos.

El panel de errores lee tablas, de modo que **por construcción no puede mostrar los fallos que
impidieron escribir en la base de datos**: caída de SQL Server, worker que no arranca, excepción no
capturada antes del primer `INSERT`. Justo los fallos que más importan son invisibles en la única
herramienta de diagnóstico prevista.

No hay decisión sobre dónde van los logs de los tres artefactos, cuánto se retienen, ni cómo se
accede a ellos. Para un desarrollador solo que además es el operador, decidir esto al principio
cuesta muy poco; añadirlo cuando algo ya falla en producción cuesta mucho.

## A18 — Sin entornos ni procedimiento de despliegue coordinado.

ADR 0001, ADR 0003 y ADR 0008 repiten que un cambio en la frontera del esquema "debe desplegarse de
forma coordinada" entre .NET y Python. Es un requisito operativo declarado tres veces y nunca
resuelto: no se decide cómo se despliega (copia manual, servicio de Windows, contenedores,
integración continua), ni en qué orden, ni qué pasa si un artefacto queda desactualizado respecto del
otro.

Tampoco se decide si existe un entorno distinto de producción. Sin él, probar el flujo completo
significa apuntar a la cuenta de Gmail real, crear carpetas en el Drive real y escribir en el Sheets
que alimenta el dashboard. ADR 0002 ya reconoce que probar el flujo completo exige levantar tres
procesos más la base de datos; falta el otro lado del problema, que son las dependencias externas.

Este hallazgo y S4 (migraciones) son el mismo agujero visto desde dos ángulos.

---

# Sugerencia

Mejoras menores, no bloqueantes.

## S1 — `Factura.estado` mezcla dos ejes ortogonales.

Ciclo de vida (pendiente / validada) y salud (alerta / error) son dimensiones independientes, y los
indicadores del propio modelo —proveedor genérico, posible duplicado, campos no extraídos— ya
expresan la salud. Con un solo campo, los filtros y contadores del Flujo 7 quedan ambiguos: ¿una
factura validada que tuvo alerta cuenta en el filtro "Alerta"? Conviene separar estado de indicadores
y derivar el chip de la bandeja.

## S2 — Falta unicidad en el motor sobre (RUC, tipo de comprobante, número).

Un índice único sobre facturas no anuladas es el respaldo que convierte C2 en una invariante real en
lugar de una bandera calculada.

## S3 — "Superación de cuota" está mal clasificada como transitorio.

ADR 0010 la agrupa con los fallos de red, con 3 intentos y espera creciente. Las cuotas de Gmail y
Drive se restablecen por ventana —por minuto o diaria—, de modo que tres intentos con espera corta no
alcanzan y producirán errores falsamente terminales. Merece una tercera clase, diferible, con un
horizonte de reintento distinto.

## S4 — Falta ADR de migraciones del esquema compartido.

El TDD lo lista como riesgo abierto, pero con dos runtimes escribiendo la misma base, decidir quién
es dueño de las migraciones —una sola herramienta, no dos— es una decisión de arquitectura, no un
detalle de implementación.

## S5 — El crecimiento del almacenamiento no está dimensionado.

El PRD dimensiona el volumen diario (10 a 50 facturas) pero nadie tradujo esa cifra a capacidad. Con
factura, orden de compra y medios probatorios por operación, y conservación indefinida, el orden de
magnitud son decenas de gigabytes al año repartidos entre el disco donde el worker deja los archivos
(A8), la cuota de Drive (A16) y el crecimiento de la base de datos.

No hace falta un cálculo fino, sí un umbral de aviso: el modo de fallo real es un disco lleno que
detiene la ingesta en silencio, que es la misma familia de problema que C8.

---

# D1 — Dependencia externa: reglas contables pendientes

**Esto no es un hallazgo contra el diseño.** El TDD ya lo declara entre sus riesgos abiertos y lo
declara bien: *"Sin ellas, la generación del asiento no es implementable en su totalidad"*. Está
previsto recogerlas en un `REGLAS.md` aún no elaborado.

Se documenta aquí por dos razones. La primera, para dejar explícito que **bloquea C1 y C3**: no se
puede decidir de dónde salen las líneas de la factura ni cuál es el ciclo de vida del asiento sin
saber qué se debita y qué se acredita. La segunda, para que la conversación con quien tenga la
respuesta sea **una reunión y no cinco**.

Y una advertencia sobre el alcance de este documento: **estas preguntas no las resuelve una revisión
técnica**. Las responde un contador. Lo único que aporto es la lista ordenada.

## Preguntas abiertas que hay que cerrar

1. **Notas de crédito (07).** ¿El asiento invierte los signos respecto de una factura, o es un asiento
   propio? ¿Se vincula al comprobante que modifica? Hoy `AsientoContable` es 1:1 con `Factura` y no
   tiene referencia a una factura anterior, así que la respuesta puede cambiar el modelo de datos.
2. **Cuenta de compra según naturaleza.** El plan del prototipo ya mezcla `60111` mercaderías,
   `60911` servicios de terceros y `63991` gastos de servicios. ¿Qué determina cuál se usa? Y sobre
   todo: el OCR no extrae si lo comprado es bien o servicio, de modo que la respuesta condiciona
   directamente C1.
3. **Cuentas fijas.** Qué cuenta exacta de IGV, y qué cuenta de proveedor —¿`42011` terceros,
   `42012` relacionadas?—, con qué criterio se elige.
4. **IGV.** ¿Siempre 18%? Qué se hace con exonerados, inafectos y no gravados, y con una factura que
   mezcla líneas gravadas y no gravadas. El prototipo asume 18% duro sobre el total (A5, C1).
5. **Contabilidad por destino.** ¿Se llevan cuentas de la clase 9? Si la respuesta es sí, cada
   asiento se duplica y el modelo de `AsientoContableDetalle` cambia.
6. **Detracciones, retenciones y percepciones.** Modifican el asiento y el importe por pagar. Ninguna
   aparece en el PRD ni en el TDD.
7. **Boletas (03).** Tratamiento distinto de la factura respecto del crédito fiscal: ¿cambia el
   asiento o solo el reporte?
8. **Diferencia de cambio.** Además de convertir con el tipo de cambio compra, ¿se registra
   diferencia de cambio? ¿En qué momento y contra qué cuenta? (Se cruza con C6.)
9. **Redondeo.** Quién absorbe la diferencia de céntimos entre débitos y créditos, y en qué cuenta se
   registra la línea de ajuste, si es que existe. (Se cruza con A5.)
10. **Periodo cerrado.** La fecha contable es libre y editable. ¿Se puede contabilizar en un mes ya
    cerrado? Hoy nada lo impide y no existe concepto de cierre de periodo.
11. **Correlativo del asiento.** El PRD dice que el número de comprobante es el de la factura, no un
    correlativo propio; el prototipo muestra además `compro` (`CP-000112`). Un libro de compras suele
    necesitar correlativo propio. Hay que cerrar cuál de los dos manda. (Se cruza con A13.)
12. **Proveedor genérico.** ¿Es admisible emitir un asiento contra `P0000 (Varios)`, o el asiento debe
    quedar retenido hasta identificar al proveedor real? El PRD asume lo primero, pero es una decisión
    contable, no de producto.

---

# Lo que aguantó la revisión

No todo se cae, y estas conclusiones son tan parte del informe como los hallazgos:

- **ADR 0010** es la mejor del conjunto. Distingue dos clases de fallo que el PRD confunde bajo el
  mismo mecanismo, su alternativa considerada es real y está bien argumentada, y reconoce la
  asimetría de una mala clasificación — un costo que la mayoría de las ADRs omitiría. El único
  matiz es el de cuota (S3).
- **ADR 0009** es proporcionada y honesta. Rechazar NgRx para un desarrollador y un usuario es
  correcto, las dos alternativas evaluadas son viables, y los cuatro costos declarados son concretos
  y no decorativos. Su hueco —el intervalo de sondeo— ya está declarado como riesgo abierto.
- **ADR 0006** acierta en la decisión y por la razón correcta: factura y asiento son dos ciclos de
  vida distintos, y el PRD los trata como tales. Sus problemas son de completitud (estados, edición
  de cabecera), no de la elección.
- **ADR 0004** elige el patrón adecuado, y su rechazo de la cola de mensajería externa está bien
  fundamentado, incluido el argumento de que una cola no participa de la transacción. Los defectos
  están en el alcance de eventos (C5) y en la sobreafirmación de idempotencia (A4), no en el patrón.

---

# Cierre

Si hubiera que atender solo tres cosas antes de escribir la primera línea de código:

1. **C1** — de dónde salen las líneas de la factura.
2. **C2** — cómo se resuelve un duplicado del lado del servidor.
3. **C3** — cuál es el ciclo de vida real del asiento: borrador, confirmación, edición de cabecera.

Los tres tocan el mismo entregable central y ninguno se resuelve implementando.

Y dos más, del mismo peso, en la pantalla de detalle y validación:

4. **C10** — de dónde salen los bytes del documento y cómo se muestran junto al formulario.
5. **C11** — por dónde entra un adjunto que sube el usuario sin romper la partición de propiedad.

Antes de todos ellos, en realidad, está **D1**: las reglas contables. C1 y C3 no se pueden decidir sin
esa conversación, y no la resuelve nadie del lado técnico.

Y dos que no bloquean el código, pero sí el proyecto, y que deben estar resueltas antes de que el
sistema procese la primera factura real:

6. **C9** — cómo se respalda la base de datos que pasa a ser el único libro de compras de la empresa.
7. **C8** — quién avisa cuando el que avisa es el que se cayó.

Sobre el conjunto: el documento decide muy bien **qué se construye** y casi nada sobre **dónde vive
lo construido**. Las diez ADRs cubren componentes, stack, propiedad de datos, contratos, estado y
errores; ninguna cubre ejecución, respaldo, red, credenciales de plataforma ni observabilidad. Con
tres artefactos y dos runtimes, esa mitad del problema no es menor que la otra, y para un
desarrollador que también será el operador del sistema es la que más caro sale postergar.
