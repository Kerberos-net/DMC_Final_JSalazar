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

Este documento **reporta**; no modifica `TECH-DESIGN.md` ni ningún ADR. Qué se corrige y cómo es
decisión del autor del diseño.

## Resumen ejecutivo

El diseño es serio. Las ADRs tienen alternativas genuinas y costos declarados con honestidad, algo
poco frecuente. Pero contiene **siete huecos que impiden implementarlo tal como está escrito**, y
varios solo aparecen al contrastar el TDD contra el prototipo de pantallas.

Los tres que bloquean antes que ningún otro son: de dónde salen las líneas de la factura (C1), cómo
se resuelve un duplicado del lado del servidor (C2), y cuál es el ciclo de vida real del asiento
—borrador, confirmación, edición de cabecera— (C3). Los tres tocan el mismo entregable central y
ninguno se resuelve implementando: se resuelven decidiendo.

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
| S1 | Sugerencia | `Factura.estado` mezcla dos ejes ortogonales | TDD · Flujo 7 |
| S2 | Sugerencia | Falta unicidad en el motor sobre (RUC, tipo, número) | Modelo de datos |
| S3 | Sugerencia | "Superación de cuota" mal clasificada como transitorio | ADR 0010 |
| S4 | Sugerencia | Falta ADR de migraciones del esquema compartido | Área de decisión ausente |

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
