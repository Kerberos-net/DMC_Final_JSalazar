# Technical Design Document: Gestor de Facturas de Compra

**Versión:** 4. Incorpora las decisiones de la segunda revisión adversarial
(`REVISION-ADVERSARIAL-V2.md`): 11 críticos, 15 advertencias, 4 sugerencias y un hallazgo detectado
al propagarlas. La versión 3 había cerrado los hallazgos de `REVISION-ADVERSARIAL.md` y las reglas
contables con los datos maestros reales de la compañía (1650 cuentas, 90 motivos, 13 orígenes de
libro).

| Documento | Contenido |
|---|---|
| **`REGLAS.md`** | **Reglas contables normativas para generar el asiento** |
| `DECISIONES-REVISION.md` | Registro de cada decisión, con sus alternativas y costos. La sección `R2-` corresponde a esta ronda |
| `MOTIVOS-CLASIFICACION.md` | Los 90 motivos con su origen de libro validado |
| `PREGUNTAS-CONTABLES.md` | Las diecisiete preguntas que dieron origen a las reglas |
| `adrs - v1/`, `adrs - v2/` | Versiones anteriores de los ADRs, conservadas |

### Lo que cambia en la versión 4

| Cambio | Origen |
|---|---|
| `AsientoContable` deja de ser 1:1 con `Factura`: pasa a **un asiento vigente** por factura, y `ANULADO` es terminal | C4 |
| El índice de identidad del comprobante deja de ser único; la unicidad se comprueba **al validar** | C1 |
| Los eventos llevan `Secuencia` por agregado y el destino descarta los obsoletos | C3 |
| La clave de sincronización externa es `FacturaId`, nunca la identidad fiscal | C5 |
| El asiento congela al confirmar **todo lo que viene de catálogos externos**, no solo los importes | A6, A14 |
| Dos ADRs nuevos: 0018 tipo de cambio, 0019 estrategia de verificación | C6, C8 |
| **Cinco premisas externas** declaradas, que este documento ya no da por buenas | C7, A12, A15 |

**Tipo de proyecto:** Greenfield. El repositorio contiene únicamente documentación (`PRD.md`,
`DESIGN.md`, `DESIGN_BRIEF.md`) y un prototipo de pantallas no ejecutable en `handoff/`. No existe
código de aplicación previo ni esquema de base de datos heredado.

**Design.md disponible:** Sí. `DESIGN.md` aporta el sistema de diseño y
`handoff/Gestor de Facturas.dc.html` es un prototipo navegable. Ambos se usaron como fuente, pero
**el prototipo no es autoritativo**: la sección "Correcciones al prototipo" enumera dónde se aparta
del diseño acordado.

## Resumen

Se construye un software web interno que reemplaza el registro manual de facturas de compra descrito
en el PRD, hoy repartido entre Gmail, impresión física, un sistema contable externo y Google Drive.

El sistema detecta en Gmail los correos etiquetados como origen de facturas, descarga sus adjuntos,
extrae los datos priorizando el XML de la factura electrónica sobre el OCR del PDF, y deja la
factura en estado pendiente de validación. El asistente contable abre la factura —lo que genera su
asiento en borrador—, selecciona el **motivo de compra** que determina la cuenta de cargo, corrige
lo que haga falta y valida. En ese momento, y en una única transacción, la factura queda validada,
el asiento confirmado con su correlativo, y la tarea de integración encolada. El worker archiva la
documentación en Drive, sincroniza la hoja de cálculo que alimenta Looker Studio y notifica los
fallos por Telegram con respaldo por correo.

El asiento se compone de dos bloques: el **principal** —cargo, IGV y proveedor, con la estructura
que corresponda al tipo de comprobante— y el de **destino**, que implementa la contabilidad
analítica y se genera solo a partir del propio plan de cuentas.

Los datos maestros —proveedores, plan contable, motivos y orígenes— **los mantiene el sistema
contable de la compañía**. Este software solo los lee.

El asiento se guarda **únicamente en la base de datos asignada al software**. No hay integración ni
migración de datos hacia ningún sistema de gestión contable externo.

El sistema está dimensionado para un único usuario y un volumen de 10 a 50 facturas diarias.

## Arquitectura de componentes

Tres artefactos desplegables sobre una base de datos SQL Server compartida, servidos tras un proxy
inverso que unifica el origen (ADR 0001, ADR 0002, ADR 0012).

**1. SPA — Angular.** Bandeja, detalle y validación de factura con el asiento embebido, registro de
compra, catálogos en solo lectura, panel de errores y configuración. No accede a la base de datos ni
combina fuentes: consume vistas ya resueltas por la API.

**2. API de negocio — ASP.NET Core.** Propietaria del dominio contable y de la seguridad. Aplica las
reglas de negocio, sirve los documentos al visor y aloja el servicio que consume el inbox de
integración.

**3. Worker de procesamiento e integraciones — Python.** Todo el trabajo asíncrono contra sistemas
externos: Gmail, extracción XML/OCR, SBS, Drive, Sheets, Telegram y correo, con su política de
reintentos.

**4. Proxy inverso.** Sirve la SPA en `/`, enruta `/api` a Kestrel y termina TLS.

### El eje de separación

> **Python es el worker de integración y procesamiento asíncrono del sistema.**
> **.NET es el owner del dominio y de la API transaccional.**

Ante una integración nueva la pregunta es si su trabajo es transaccional o asíncrono, no en qué
lenguaje "encaja mejor" (ADR 0002).

### Comunicación entre componentes

```
   Gmail ─┐
     SBS ─┤
   Drive ─┼──▶ [ Worker Python ] ──escribe──▶ Tablas privadas de ingesta
  Sheets ─┤        │        ▲                 (Email, DocumentoRecibido,
Telegram ─┘        │        │                  Procesamiento, DatosExtraidos,
                   │        │                  ProcesamientoError, …)
        escribe    │        │  consume
                   ▼        │
            InboxEvent   OutboxEvent
                   │      CommandQueue
                   │        ▲
                   ▼        │
              [ API .NET ]──┘
                   │
           escribe │ lee
                   ▼
         Tablas privadas de negocio y seguridad
         (Factura, AsientoContable, Motivo, …)
                   ▲
              HTTP/JSON  (mismo origen)
                   │
            [ Proxy inverso ]
                   │
             [ SPA Angular ]
```

- **SPA ↔ API:** HTTP/JSON, **mismo origen**. Cookie de sesión `HttpOnly` con `SameSite=Lax`
  (ADR 0007).
- **API ↔ Worker:** sin contrato de red. Tres tablas de contrato en SQL Server, en tres direcciones
  (ADR 0004).
- **Worker ↔ externos:** APIs de Google, web de la SBS, Telegram y SMTP.

### Propiedad de datos

La partición es **por tabla, con orígenes de escritura declarados** (ADR 0003). Cuatro clases:

**Privadas** — un solo componente escribe y lee.

| Contexto | Propietario | Tablas |
|---|---|---|
| Ingesta y procesamiento | Python | `Email`, `DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`, `ProcesamientoIntentos` |
| Negocio | .NET | `Factura`, `AsientoContable`, `AsientoContableDetalle`, `AdjuntoManual`, `AuditoriaCorreccion`, `FacturaExtraccion`, `CorrelativoAsiento` |
| Satélites de datos maestros | .NET | `ProveedorAtributo`, `MotivoAtributo`, `SugerenciaCuenta` |
| Seguridad | .NET | `Usuario` |

**De contrato** — coescritas por diseño.

| Tabla | Produce | Consume | Semántica |
|---|---|---|---|
| `OutboxEvent` | .NET | Python | Hechos de negocio |
| `CommandQueue` | .NET | Python | Órdenes de ejecución |
| `InboxEvent` | Python | .NET | Hechos de procesamiento |

**De publicación con múltiples orígenes.**

| Tabla | Escribe | Lee |
|---|---|---|
| `TipoCambio` | Python (`Origen='SBS'`) y .NET (`Origen='MANUAL'`) | Ambos |
| `Configuracion` | .NET | Ambos |
| `EstadoIntegracion` | Ambos, según `Nombre` | Ambos |

**Externas** — escritas por el **sistema contable de la compañía**, en esta misma base. Esta
aplicación tiene **`SELECT` únicamente**.

| Tabla | Contenido | Se identifica por |
|---|---|---|
| `Proveedor` | Catálogo de proveedores, incluido `P0000 (Varios)` | **Código de 5 caracteres** |
| `CuentaContable` | Plan contable: 1650 cuentas, 907 imputables de 6 dígitos, con `ctarefleja` y `ctapuente` | **Código de cuenta, texto de longitud variable** |
| `Motivo` | 90 motivos de compra con sus prefijos de cuenta | **Entero** |
| `Origen` | 13 orígenes de libro | **Código de 2 caracteres** |

Las cuatro claves son **códigos de negocio, no identificadores subrogados**. `P0000` es literalmente
la clave del proveedor genérico, no una etiqueta sobre un `BIGINT`. Toda tabla satélite y toda
referencia desde el dominio se une por esos códigos.

`CuentaContable` se identifica por texto de **longitud variable** y eso es funcional, no estético:
los motivos guardan **prefijos de 2 a 6 dígitos**, y un tipo de longitud fija los rellenaría con
espacios, rompiendo la resolución por `LIKE prefijo + '%'` que sostiene la cascada de sugerencia.

**Este software no escribe ningún dato maestro.** El alta de proveedores ocurre en otro sistema,
fuera del alcance del proyecto. Lo que este proyecto necesita y el sistema contable no aporta vive
en tablas **satélite**, unidas por clave, sin tocar nunca el catálogo externo.

Cuatro reglas invariantes: Python no toca tablas de dominio de .NET; Python no solicita operaciones
de dominio, solo informa hechos; ningún componente sondea la tabla privada del otro; **nadie escribe
una tabla externa**.

### El refuerzo en el motor

Las cuatro reglas **no son convención**. Los objetos de este proyecto viven en un esquema propio
—`fact`— dentro de la base compartida con el sistema contable, y hay **dos usuarios de base de
datos**, uno por runtime, con `GRANT` explícito por tabla que viaja en el SQL versionado (ADR 0003,
ADR 0016).

Lo que eso compra, en una línea: **`usr_api` no puede leer `fact.Procesamiento`**. Aunque alguien
escriba ese `SELECT`, falla.

### Premisas externas — resueltas

Cinco supuestos sobre lo que hay **fuera** del proyecto, que este documento no daba por buenos. Se
verificaron todos, y **ninguno bloquea ya**:

| # | Premisa | Estado |
|---|---|---|
| 1 | Modelo de recuperación de la base | **Condición de producción** |
| 2 | Cadena de `LOG BACKUP` existente y su destino | **Condición de producción** |
| 3 | RPO efectivo de la instancia | **Condición de producción** |
| 4 | Derecho a crear esquema y ejecutar DDL | **Confirmado.** La base está asignada al proyecto |
| 5 | El asistente da de alta proveedores, y es inmediato | **Confirmado.** Sale, lo registra, vuelve y lo encuentra |

Las premisas **4 y 5 quedan cerradas** y con ellas la partición de ADR 0003, su matriz de permisos,
el bloqueo de `P0000` y el descarte de replicar los datos maestros. La 5 era la que más sostenía: de
ella dependían tres decisiones distintas.

Las premisas **1 a 3 no aplican al entorno actual**, que es una demostración académica sin
contabilidad real que perder. Pasan de bloqueantes a **condiciones de puesta en producción**: el día
que el sistema registre facturas reales, hay que responderlas antes de arrancar. ADR 0014 las
conserva escritas por esa razón — para que la ausencia de respaldo no se arrastre por inercia.

## Decisiones de arquitectura

| # | Decisión | Estado |
|---|---|---|
| [ADR-0001](adrs/0001-componentes-del-sistema.md) | Componentes del sistema | Aceptado |
| [ADR-0002](adrs/0002-stack-tecnologico-por-componente.md) | Stack tecnológico por componente | Aceptado |
| [ADR-0003](adrs/0003-particion-de-propiedad-de-datos-entre-net-y-python.md) | Partición de propiedad de datos | Aceptado |
| [ADR-0004](adrs/0004-outbox-transaccional-para-integraciones-externas.md) | Contratos de mensajería entre .NET y Python | Aceptado |
| [ADR-0005](adrs/0005-frontera-de-promocion-de-documento-procesado-a-factura.md) | Frontera de promoción a factura | Aceptado |
| [ADR-0006](adrs/0006-asiento-contable-como-entidad-propia.md) | El asiento como entidad propia con borrador | Aceptado |
| [ADR-0007](adrs/0007-identidad-y-autenticacion.md) | Identidad y autenticación | Aceptado |
| [ADR-0008](adrs/0008-contratos-de-api.md) | Contratos de API | Aceptado |
| [ADR-0009](adrs/0009-manejo-de-estado-en-el-frontend.md) | Manejo de estado en el frontend | Aceptado |
| [ADR-0010](adrs/0010-politica-de-reintentos-y-clasificacion-de-errores.md) | Reintentos y clasificación de errores | Aceptado |
| [ADR-0011](adrs/0011-motivo-de-compra-y-sugerencia-de-cuenta.md) | El motivo de compra como origen de las líneas | Aceptado |
| [ADR-0012](adrs/0012-topologia-de-despliegue-y-tls.md) | Topología de despliegue, TLS y entornos | Aceptado |
| [ADR-0013](adrs/0013-almacenamiento-y-entrega-de-documentos.md) | Almacenamiento y entrega de documentos | Aceptado |
| [ADR-0014](adrs/0014-respaldo-y-continuidad.md) | Respaldo y continuidad | Aceptado |
| [ADR-0015](adrs/0015-secretos-credenciales-y-observabilidad.md) | Secretos, credenciales y observabilidad | Aceptado |
| [ADR-0016](adrs/0016-versionado-del-esquema-de-base-de-datos.md) | Versionado del esquema | Aceptado |
| [ADR-0017](adrs/0017-frontera-del-motor-de-extraccion.md) | Motor de extracción y candidatura de correos | Aceptado |
| [ADR-0018](adrs/0018-tipo-de-cambio-aplicable.md) | Tipo de cambio aplicable a la conversión | Aceptado |
| [ADR-0019](adrs/0019-estrategia-de-verificacion.md) | Estrategia de verificación | Aceptado |

## Modelo de datos

### Convenciones transversales

| Regla | Detalle |
|---|---|
| Importes | `DECIMAL(18,2)`. **Nunca** `float` ni `real`. |
| Tipo de cambio | `DECIMAL(12,6)` |
| Fechas de negocio | `DATE` sin hora. Un día calendario no tiene zona horaria. |
| Marcas técnicas | `DATETIME2` en **UTC**, mostradas en hora de Lima |

### Contexto de ingesta y procesamiento (Python)

**`Email`** — correo candidato. Identificador del mensaje en Gmail, remitente, asunto, fecha de
recepción y de detección, estado. Un correo puede contener varias facturas, de modo que la relación
con `DocumentoRecibido` es 1:N.

**`DocumentoRecibido`** — adjunto descargado. `GmailMessageId`, nombre, extensión, **MIME type**,
tamaño, **`HashContenido`**, tipo de documento identificado, **ruta relativa** en el volumen
compartido y estado. El hash sostiene la idempotencia del reproceso.

**`Procesamiento`** — ejecución sobre un `DocumentoRecibido`. Estado, marcas de inicio y fin, e
indicador de si ya originó una factura.

**`DatosExtraidos`** — evidencia **inmutable** de lo que la extracción leyó: tipo de comprobante,
número, RUC y nombre del proveedor, monto, moneda, fecha de emisión, y qué campos no pudo extraer.
No se edita nunca. Es la base para medir la precisión de ≥90% contra la factura ya corregida.

**`ProcesamientoError`** — integración afectada, mensaje, clasificación (`TRANSITORIO`, `DIFERIBLE`
o `PERMANENTE`) y momento.

**`ProcesamientoIntentos`** — un registro por intento: número, resultado, momento, detalle y
**próxima fecha de reintento**.

### Tablas de contrato

**`OutboxEvent`** — hechos de negocio, escritos en la misma transacción que los origina.
Inmutables. Tipo, referencia, *payload*, **`Secuencia`** monótona por agregado, estado global y
**estado independiente por integración** (Drive, Sheets, Telegram, correo).

Cinco tipos: `FACTURA_VALIDADA`, `FACTURA_CORREGIDA`, `ASIENTO_CORREGIDO`, `ASIENTO_ANULADO` y
`DOCUMENTACION_ACTUALIZADA`.

**No se consumen serializados.** La garantía es que **el efecto final corresponde al evento más
reciente**: el destino registra la secuencia del último aplicado y descarta como `OBSOLETO` —terminal,
sin error, sin notificación— cualquiera que no la supere. La serialización por factura era
incompatible con los reintentos diferidos de ADR 0010, que pueden tardar hasta el día siguiente.

El *payload* debe ser **autosuficiente**: lleva el estado completo, nunca un delta. Es lo que permite
descartar un evento entero sin dejar el destino a medias.

**`CommandQueue`** — órdenes de ejecución. Tipo, referencia, *payload*, estado, intentos, próxima
fecha de reintento y **`CorrelationId`**.

**`InboxEvent`** — hechos de procesamiento notificados por Python. Tipo, `ProcesamientoId`,
*payload*, y el **resultado del consumo**: `EstadoConsumo` (`PENDIENTE` / `PROMOVIDO` /
`DESCARTADO`), `ConsumidoEn`, `FacturaId` si se promovió y `MotivoDescarte` si no.

Registrar el resultado, y no solo que se consumió, es lo que impide reprocesar para siempre un
documento que .NET decidió **no promover**, y lo que permite responder cuántos documentos procesados
no llegaron a factura y por qué.

### Tablas de publicación

**`TipoCambio`** — `Fecha` (`DATE`), `Compra`, `Venta`, `FechaConsulta`, **`Origen`** (`SBS` o
`MANUAL`), `CargadoPor`, `CargadoEn`. Python escribe las filas de la SBS; .NET las cargadas a mano.
Si la SBS publica después para una fecha con fila `MANUAL`, **no la pisa en silencio**: registra la
discrepancia.

**`Configuracion`** — tipada por secciones. Carpeta o etiqueta monitoreada, extensiones permitidas,
frecuencia de sondeo, fecha de inicio, tipos y tamaño máximo de adjuntos, destino de Telegram,
preferencias de notificación y de presentación, e **intervalo esperado por integración**.

**`EstadoIntegracion`** — una fila por integración: `Nombre` (`GMAIL`, `DRIVE`, `SHEETS`, `SBS`,
`WORKER`), `UltimoIntento`, `UltimoExito`, `UltimoError`, `FallosSeguidos`.

Sostiene la píldora "Conectado / Con error" de la pantalla de Configuración, que el diseño de
interfaz pedía sin que nada la respaldara. La píldora **se deriva**, no se almacena: `Con error` si
hay fallos seguidos o si el último éxito supera el intervalo esperado de esa integración.

La fila `WORKER` es el **latido**, y la alerta por su ausencia es lo que cierra el riesgo de que el
worker se detenga sin que nadie avise.

Se escribe **fuera de la transacción de negocio**: es telemetría, y que su escritura falle no puede
tumbar una validación.

### Contexto de negocio (.NET)

**`Factura`** — creada por promoción desde un `InboxEvent` (ADR 0005). Tipo de comprobante, número,
proveedor, RUC, monto, moneda y fecha de emisión, tipo de cambio aplicado, **motivo**,
**`Afectacion`** (`GRAVADA` / `EXONERADA` / `INAFECTA`), **`Percepcion`**, **`ProcesamientoId`**,
**`Version`** (`rowversion`) y la referencia del comprobante rectificado, que puede ser **interna o
externa**:

| Campo | Cuándo |
|---|---|
| `FacturaReferenciaId` | La factura rectificada está en el sistema |
| `EsReferenciaExterna`, `RefExternaSerie`, `RefExternaNumero`, `RefExternaFecha` | Es anterior al sistema |

Para el tipo `07` es obligatoria **exactamente una de las dos**.

Indicadores propios: proveedor genérico, posible duplicado, campos no extraídos, fecha en domingo,
**afectación no verificada** y **referencia externa**.

```sql
Estado: PENDIENTE_VALIDACION | VALIDADA | DESCARTADA

-- Detección, NO bloqueo
CREATE INDEX IX_Factura_Identidad
    ON Factura (RucProveedor, TipoComprobante, Numero)
    WHERE Estado <> 'DESCARTADA';

-- Idempotencia de la promoción: invariante del motor
CREATE UNIQUE INDEX UQ_Factura_Procesamiento
    ON Factura (ProcesamientoId)
    WHERE ProcesamientoId IS NOT NULL;
```

> **El índice de identidad no es único, y es deliberado.** Siéndolo, la fila duplicada **nunca
> llegaba a existir**: el motor rechazaba el `INSERT` en el worker, días antes y sin interfaz donde
> mostrarlo, dejando inalcanzable todo el flujo de resolución —el indicador `posibleDuplicado`, el
> recálculo al guardar y el `409` con sus dos salidas—. Y como SQL Server trata los `NULL` como
> iguales en un índice único, la **segunda factura del día con el número no extraído** también se
> rechazaba: justo el caso borde que el PRD manda soportar.
>
> La unicidad se comprueba **al validar**, dentro de la transacción, contra las facturas ya
> `VALIDADA`. `PENDIENTE_VALIDACION` es el estado de trabajo y admite duplicados y números vacíos: la
> limpieza ocurre al validar. El criterio del PRD es *"detecta y alerta antes de permitir un nuevo
> registro"* — alertar, no rechazar en el motor.

El estado contiene **solo ciclo de vida**. `ERROR` no existe: los fallos previos a la promoción no
producen factura. `ALERTA` tampoco: el chip de la bandeja **se deriva** de los indicadores.

Una factura admite **varias notas de crédito**; la suma de las vigentes —con asiento no anulado— no
puede exceder su monto total.

**`FacturaExtraccion`** — evidencia **inmutable** de qué leyó la extracción en cada campo y de qué
fuente: `FacturaId`, `CampoNombre`, `ValorExtraido`, `Fuente` (`XML` / `PDF`).

Vive del lado de .NET porque la métrica de precisión la compara contra la factura corregida, y
`DatosExtraidos` es privada de Python: sin esta tabla, **ningún componente puede leer los dos lados
de la comparación**. La fuente por campo, y no por documento, es lo que permite partir la métrica —
una factura con XML puede tener un campo que el XML no traía.

**`AsientoContable`** — cabecera. Una factura puede tener **varios asientos** a lo largo del tiempo,
pero **a lo sumo uno no anulado**. Lleva **dos números**: `NumeroComprobante` (fiscal, `F001-00234`)
y `NumeroAsiento` (correlativo propio por periodo y origen, `02-2026-08-000123`, asignado **al
confirmar**). Más origen del libro (`02 Compras`), proveedor, **glosa**, **fecha contable**, tipo de
cambio **venta**, base imponible, IGV, neto, **`MotivoDescripcion`** y **`Version`** (`rowversion`).

Todo lo que viene de fuera se congela al confirmar, no solo los importes.

```sql
CREATE UNIQUE INDEX UQ_Asiento_Vigente
    ON AsientoContable (FacturaId)
    WHERE Estado <> 'ANULADO';
```

```
Estado: BORRADOR ──validar──▶ CONFIRMADO ──anular──▶ ANULADO  (terminal)
             ▲                     │
             └──────reabrir────────┘
```

**De `ANULADO` no sale ninguna flecha.** Anular **libera la factura**, que vuelve a admitir un asiento
en `BORRADOR` con su propio correlativo. Es la ortodoxia contable —un hecho no se deshace, se anula y
se emite otro— y es lo que evita que anular por error deje la factura irrectificable para siempre.

`reabrir` corrige dentro del periodo y conserva el número; `anular` saca del libro y obliga a uno
nuevo. El tope de reapertura por cierre de periodo se aplica **solo a `reabrir`**.

**`CorrelativoAsiento`** — `Anio`, `Mes`, `Origen`, `Ultimo`. Actualizada con `UPDLOCK` **dentro de
la transacción que confirma**, para que una transacción revertida devuelva el número. `SEQUENCE` e
`IDENTITY` no sirven: están diseñadas para no bloquear y por eso queman el número igual. El reinicio
mensual es por fila, sin proceso de cierre que pueda no ejecutarse.

**`AsientoContableDetalle`** — `LineaId` (identificador **estable**), `Orden` (solo presentación),
**`Bloque`** (`PRINCIPAL` o `DESTINO`), `Tipo` (`D` o `H`), `Debe`, `Haber`, `CuentaCodigo`,
**`CuentaDescripcion`**, **`CtaReflejaCodigo`**, **`CtaPuenteCodigo`** e indicador de línea sin
cuenta.

Las tres columnas nuevas se **congelan al confirmar**. `ctarefleja` y `ctapuente` son columnas de un
catálogo **externo**: si cambian entre la confirmación de una factura y la de su nota de crédito, el
espejo revertiría contra una cuenta distinta de la que cargó, y **nada lo señalaría**, porque cada
asiento cuadra por separado. Con esto el asiento es autocontenido: se imprime, se exporta y se audita
sin consultar nada externo.

```sql
CONSTRAINT CK_Linea_Tipo CHECK (
    (Tipo = 'D' AND Debe > 0 AND Haber = 0) OR
    (Tipo = 'H' AND Haber > 0 AND Debe = 0)
)
```

El bloque `DESTINO` implementa la **contabilidad por destino** y se genera automáticamente desde el
plan de cuentas: para cada cargo cuya cuenta declare `ctarefleja`, se emite el reflejo al Debe y el
puente al Haber (ADR 0006).

**`AdjuntoManual`** — archivos subidos desde la SPA. Ruta relativa, MIME, tamaño, `SubidoPor`,
`SubidoEn`, y `EliminadoEn` / `EliminadoPor` / `MotivoEliminacion` para el **borrado lógico
auditado**.

**`AuditoriaCorreccion`** — entidad afectada, campo, valor original, valor nuevo, usuario y fecha.
Cubre correcciones de factura y de asiento, reaperturas con su motivo y anulaciones. No cubre
reactivaciones, porque no existen.

Cuatro casos que la revisión v2 añade, y sin los cuales quedarían actos sin rastro:

| Caso | Por qué tiene que estar |
|---|---|
| **Traslado de periodo**, con número anterior, nuevo y motivo | Sin él, el hueco en la serie del mes de origen es injustificable |
| **Confirmación de afectación** sobre una factura sin XML | Es una afirmación del asistente sobre un documento fiscal |
| **Eliminación de un adjunto** tras validar | Borra un archivo de Drive: la única operación destructiva del flujo |
| **Reparto manual** de cuentas, si se corrige el propuesto | El asiento cuadra igual; lo que cambia es el destino del gasto |

### Satélites de datos maestros (propiedad de .NET)

Aportan lo que el sistema contable no tiene, sin tocar el catálogo externo.

| Tabla | Contenido |
|---|---|
| `ProveedorAtributo` | `EsRelacionada`, que elige entre `4212` y `4312` |
| `MotivoAtributo` | `Activo` y `OrigenLibro` |
| `SugerenciaCuenta` | `(ProveedorCodigo, MotivoId, CuentaCodigo)` con `Veces` y `UltimoUso` |

`MotivoCuenta` **no es una tabla**: es la interpretación que la aplicación hace del campo de
prefijos del catálogo externo, resolviéndolos contra las 907 hojas de 6 dígitos.

> **Eliminadas del modelo:** `FacturaDetalle`, `Producto` y el mapeo producto→cuenta. Eran
> estructura muerta: nada las alimentaba (ADR 0011). También `Rol` y `UsuarioRol` (ADR 0007).

### Contexto de seguridad (.NET)

**`Usuario`** — contraseña almacenada mediante función de derivación de clave con sal.

## Criterios de aceptación por flujo

### Flujo 1 — Ingesta y extracción

- [ ] Un correo en la etiqueta configurada, con al menos un adjunto PDF o XML, genera una fila en
      `Email` y una en `DocumentoRecibido` por cada adjunto.
- [ ] Un correo **sin** adjunto de extensión permitida no genera ninguna fila ni consume extracción.
- [ ] El asunto y el remitente **no** influyen en la candidatura.
- [ ] Un correo con varias facturas genera un `DocumentoRecibido` y un `Procesamiento` por cada una.
- [ ] Cuando existe XML, los datos se toman del XML y el PDF queda como evidencia.
- [ ] Un PDF se asocia a un XML **solo** si coinciden exactamente RUC, tipo, serie y número
      normalizados.
- [ ] Un PDF sin coincidencia inequívoca **queda sin asociar** y genera una incidencia de revisión.
      No se asigna por proximidad.
- [ ] `DatosExtraidos` no se modifica en ningún momento posterior.
- [ ] Un adjunto corrupto o protegido se marca como **permanente sin reintentos** y notifica de
      inmediato.
- [ ] El worker **etiqueta** el correo procesado y **nunca lo elimina**.
- [ ] El tiempo entre la llegada del correo y la aparición en bandeja es inferior a 15 minutos.

### Flujo 2 — Promoción a factura

- [ ] Python notifica el fin del procesamiento por `InboxEvent`; **.NET decide** si promueve.
- [ ] Un procesamiento con datos suficientes origina exactamente una `Factura` en
      `PENDIENTE_VALIDACION`.
- [ ] Un procesamiento fallido **no crea factura**: aparece en la bandeja como incidencia.
- [ ] Reejecutar la promoción sobre el mismo procesamiento no crea una segunda factura: lo impide
      `UQ_Factura_Procesamiento`, y la violación se trata como **no-op idempotente**.
- [ ] Un `InboxEvent` que .NET decide **no promover** queda `DESCARTADO` con su motivo, y **no se
      vuelve a consumir**.
- [ ] Insertar una factura con `(RUC, tipo, número)` ya existente **sí se permite**: queda en
      `PENDIENTE_VALIDACION` con el indicador de posible duplicado. El rechazo ocurre al validar.
- [ ] Una segunda factura con el **número no extraído** se promueve sin chocar con la primera.
- [ ] Si el proveedor no existe, la factura se crea con `P0000` y el indicador correspondiente.
- [ ] Al promover se persiste `FacturaExtraccion` con el valor y la **fuente de cada campo**.
- [ ] Python **nunca** escribe ni lee `Factura`. Con la matriz de permisos, no puede aunque lo
      intente.

### Flujo 3 — Trabajo, corrección y validación

- [ ] Abrir la factura genera su `AsientoContable` en `BORRADOR` con las tres líneas sugeridas, si
      no existía. La creación es una **acción explícita**, no un efecto lateral de un `GET`.
- [ ] Una factura en moneda extranjera **sin tipo de cambio para su fecha no se abre para edición**.
- [ ] Cargar el tipo de cambio a mano desbloquea el trabajo **al instante**, con `Origen='MANUAL'`.
- [ ] "Guardar avance" persiste factura **y** asiento con sus líneas; ambos siguen editables.
- [ ] El motivo es **obligatorio** para validar.
- [ ] La cuenta sugerida se muestra con su fundamento (*"usado 14 de 15 veces con este proveedor"*).
- [ ] Toda corrección genera una fila en `AuditoriaCorreccion`.
- [ ] El motivo se elige de una lista que contiene **solo motivos activos de origen `02`**, no los 90
      del catálogo.
- [ ] Validar con duplicado sin resolver es rechazado por la API con `409`. Las salidas son corregir
      el número o **descartar** la factura. **Es el único punto de control de unicidad.**
- [ ] La bandera de duplicado **se recalcula al guardar y al validar**, no solo en la promoción.
- [ ] Validar una factura cuyo XML declara **más de un tipo de afectación** es rechazado con `409`:
      la factura mixta está fuera de alcance.
- [ ] Una factura **sin XML** no se puede validar hasta que el asistente **confirme la afectación**.
      La confirmación queda en `AuditoriaCorreccion`.
- [ ] `PATCH` sin `If-Match`, o con un `ETag` obsoleto, es rechazado con **`412`**. La interfaz lo
      distingue del `409`: uno significa "viola una regla", el otro "alguien más lo cambió".
- [ ] Validar una factura emitida en **domingo** es rechazado con `409`, para los tipos `01`, `03`
      **y** `07`. Los sábados se permiten.
- [ ] Validar con proveedor `P0000 (Varios)` es rechazado con `409`. El asistente registra al
      proveedor **en el sistema externo** y vuelve a seleccionarlo; **no existe alta desde esta
      aplicación**.
- [ ] Validar con `FechaContable` anterior a `Configuracion.FechaCorteContable` es rechazado con
      `409`.
- [ ] La validación se ejecuta contra `POST /facturas/{id}/validar`, no como actualización genérica.
- [ ] Al validar, en **una única transacción**: factura a `VALIDADA`, asiento a `CONFIRMADO`,
      contador de sugerencia incrementado y evento insertado. Si algo falla, nada se persiste.
- [ ] Una caída de Drive, Sheets, Telegram o SMTP **no impide validar**.
- [ ] Un falso positivo de detección se puede **descartar** con motivo, y sale de la bandeja activa.

### Flujo 4 — Asiento contable

- [ ] Toda factura `VALIDADA` tiene exactamente un `AsientoContable` **vigente** en `CONFIRMADO`.
      Puede tener además asientos `ANULADO`, que permanecen en el libro con su número.
- [ ] El `NumeroAsiento` correlativo se asigna **al confirmar**, con la tabla contador actualizada
      dentro de la misma transacción, de modo que **una validación fallida no consume número**.
- [ ] El contador **se reinicia cada mes** por fila `(año, mes, origen)`, sin proceso de cierre.
- [ ] Trasladar un asiento a otro periodo le da un número nuevo del mes destino y **deja un hueco en
      el de origen**, registrado en `AuditoriaCorreccion` con su motivo. Es el único hueco posible, y
      es justificable porque tiene rastro.
- [ ] Trasladar a un periodo **cerrado** se rechaza, igual que reabrir en él.
- [ ] Los importes se expresan en soles con el tipo de cambio **venta** de la fecha de emisión.
- [ ] `totalPEN` e `igvPEN` se convierten y redondean; `basePEN` **se deriva** como su diferencia.
- [ ] `basePEN + igvPEN = totalPEN` **siempre**, sin tolerancia.
- [ ] La cuenta de cargo proviene del **motivo**, resolviendo sus prefijos contra las hojas de 6
      dígitos del plan.
- [ ] La cuenta de proveedor se resuelve por **moneda × `EsRelacionada`**: `421211`, `421212`,
      `431211` o `431212`.
- [ ] En una **boleta `03`** o una factura `EXONERADA` / `INAFECTA`, el cargo es por el **monto
      total** y **no hay línea de IGV**.
- [ ] Una factura con **percepción** genera su línea en `401131`, y el abono al proveedor iguala
      **total + percepción**.
- [ ] Una **nota de crédito `07`** invierte los signos y **hereda cuatro cosas** de la factura
      referenciada: motivo, cuenta o cuentas de cargo, cuentas de destino congeladas y **tipo de
      cambio**. El asistente no elige ninguna.
- [ ] Una nota que anula el **100%** de una factura en moneda extranjera deja el pasivo en **0.00
      exacto**, sin residuo cambiario. Lo garantiza heredar el tipo de cambio, no una tolerancia.
- [ ] Una nota **parcial** sobre una factura con reparto en N cuentas reparte en **la misma
      proporción**; el céntimo residual lo absorbe la cuenta de mayor importe.
- [ ] Una nota sobre **boleta `03`** o factura no gravada tiene **dos líneas y ninguna de IGV**: es
      el espejo del documento que rectifica, no de una factura gravada.
- [ ] Una nota con **referencia externa** —contra una factura anterior al sistema— se valida sin
      factura referenciada: el asistente elige motivo y cuenta, no entra en el tope acumulado y usa
      el tipo de cambio de su propia fecha.
- [ ] La suma de las notas de crédito **vigentes** sobre una factura no excede su monto total.
      Vigente significa **con asiento no anulado**: anular el asiento de una nota **libera** su
      importe y permite registrar otra que antes se rechazaba.
- [ ] Para cada cargo cuya cuenta declare `ctarefleja`, el bloque `DESTINO` contiene su par
      reflejo/puente por el mismo importe. Se genera **automáticamente**, y las cuentas quedan
      **congeladas en la línea** al confirmar.
- [ ] Cambiar `ctarefleja` en el catálogo externo entre confirmar una factura y confirmar su nota
      **no altera** contra qué cuenta revierte la nota.
- [ ] Las invariantes contra base, IGV y total se aplican **solo al bloque `PRINCIPAL`**.
- [ ] El asiento **cuadra globalmente**, sumando ambos bloques.
- [ ] El asistente puede dividir el cargo en varias cuentas del motivo; su suma debe igualar
      `basePEN`, o el total en los casos sin IGV.
- [ ] Una línea sin cuenta impide confirmar.
- [ ] `Tipo='D'` exige `Debe>0` y `Haber=0`; `Tipo='H'` lo contrario. Restricción **en el motor**.
- [ ] La línea se identifica por `LineaId`; agregar o eliminar líneas no desestabiliza el contrato.
- [ ] Un asiento cuya fecha contable sea anterior a la **fecha de corte no se puede reabrir**.
- [ ] Reabrir un asiento confirmado exige **motivo**, devuelve la factura a `PENDIENTE_VALIDACION` y
      queda auditado.
- [ ] Reconfirmar tras reabrir emite `ASIENTO_CORREGIDO`, que **corrige Drive y Sheets**.
- [ ] Anular emite `ASIENTO_ANULADO`, y el asiento **deja de contar** en el dashboard.
- [ ] **`ANULADO` es terminal:** no existe reactivar. Anular libera la factura para un asiento nuevo,
      con su propio correlativo.
- [ ] Al confirmar se congelan, además de los importes: descripción de la cuenta, descripción del
      motivo, `ctarefleja`, `ctapuente` y tipo de cambio. **El asiento se puede imprimir y auditar
      sin consultar ningún catálogo externo.**
- [ ] Los importes no se recalculan si la factura se corrige después de confirmado.

### Flujo 5 — Integraciones posteriores

- [ ] El worker consume `OutboxEvent` y ejecuta Drive, Sheets, Telegram y correo según el evento.
- [ ] **El efecto final corresponde siempre al evento más reciente.** Un evento cuya `Secuencia` no
      supera la registrada en el destino se descarta como `OBSOLETO`: terminal, sin error y sin
      notificación.
- [ ] Un `ASIENTO_CORREGIDO` que quedó `DIFERIBLE` por cuota y se reintenta al día siguiente **no
      resucita** el importe de un asiento anulado entretanto.
- [ ] Una cuota agotada **no detiene** los demás eventos de esa factura.
- [ ] Cada integración mantiene su estado propio: si Drive se completó y Sheets falló, el reintento
      ejecuta únicamente Sheets.
- [ ] El empaquetado hacia Drive usa la **lista de rutas del *payload***, de ambos orígenes. Python
      **no consulta** `DocumentoRecibido` ni `AdjuntoManual` para esto.
- [ ] **La clave de sincronización es `FacturaId`**, nunca `(RUC, tipo, número)`. Drive busca por
      `appProperties`, no por el nombre de la carpeta.
- [ ] Corregir el número o el RUC de una factura ya sincronizada **actualiza** su fila y su carpeta;
      no crea una segunda.
- [ ] Drive **busca antes de crear** y Sheets hace **upsert por clave**: repetir un evento no
      duplica carpeta ni fila.
- [ ] Añadir o eliminar un adjunto **después de validar** emite `DOCUMENTACION_ACTUALIZADA` y
      reempaqueta la carpeta de Drive. Un medio probatorio que llega tarde **sí se archiva**.
- [ ] Un fallo transitorio se reintenta hasta 3 veces con espera creciente.
- [ ] Una **superación de cuota** se clasifica como `DIFERIBLE` y se reintenta al abrirse la
      ventana, no en segundos.
- [ ] `DIFERIBLE` **notifica al entrar**, no al agotar: sus reintentos no se agotan, y sin esto el
      estado que más tiempo mantiene el sistema degradado sería el único que nunca avisa.
- [ ] Agotados los reintentos de un `TRANSITORIO`, la notificación se envía en un máximo de 5
      minutos. Un `PERMANENTE` notifica de inmediato. Un `OBSOLETO` no notifica nunca.
- [ ] Si Telegram falla, la alerta se envía por correo, y ambos intentos quedan registrados.
- [ ] Toda incidencia expone tipo, mensaje, fecha, intentos y **acciones disponibles**.
- [ ] `REPROCESAR` reejecuta la operación **sin crear duplicados**.

### Flujo 6 — Autenticación y acceso

- [ ] Cualquier endpoint sin sesión válida es rechazado.
- [ ] Las credenciales inválidas no revelan si el usuario existe.
- [ ] La contraseña se almacena con función de derivación de clave con sal.
- [ ] La cookie se emite con `HttpOnly`, `Secure`, **`SameSite=Lax`** y prefijo `__Host-`, y no es
      accesible desde JavaScript.
- [ ] El cierre de sesión invalida la sesión **del lado del servidor**.
- [ ] Cinco intentos fallidos consecutivos **bloquean el usuario 15 minutos**, de forma creciente. El
      mensaje de bloqueo no revela si el usuario existe.
- [ ] El restablecimiento de contraseña tiene un **procedimiento operativo escrito**, ejecutado con
      un comando de la aplicación. Nunca con un `UPDATE` a mano sobre la base.
- [ ] Todo usuario autenticado accede a la totalidad de las funciones.
- [ ] El visor de documentos, dentro de un `<iframe>` de mismo origen, **recibe la cookie** y
      muestra el PDF.

### Flujo 7 — Bandeja y visibilidad

- [ ] La bandeja es una **vista lógica de .NET** que combina facturas e incidencias. Angular no
      combina fuentes.
- [ ] Los estados se diferencian visualmente mediante los distintivos de `DESIGN.md`.
- [ ] El chip se **deriva** de los indicadores; `Factura.Estado` solo contiene ciclo de vida.
- [ ] Los filtros por estado, rango de fechas y proveedor son combinables, y los contadores reflejan
      el filtro aplicado.
- [ ] La bandeja refleja facturas nuevas sin recarga manual.
- [ ] El panel de errores distingue `TRANSITORIO`, `DIFERIBLE` y `PERMANENTE`, e indica si la
      notificación de respaldo se envió. `OBSOLETO` **no aparece**: no es un error.
- [ ] Los indicadores nuevos tienen su distintivo: afectación no verificada y referencia externa.

### Flujo 8 — Operación

- [ ] Los tres artefactos emiten logs estructurados al agregador, con `CorrelationId` transversal.
- [ ] Una búsqueda por `CorrelationId` reconstruye el recorrido completo de una factura.
- [ ] `GET /api/integraciones/estado` alimenta la píldora "Conectado / Con error" de Configuración.
- [ ] El agregador **alerta por ausencia de latido** del worker. Es la única forma de detectar un
      componente detenido: un componente detenido no emite nada.
- [ ] El respaldo copia **primero el volumen y después la base**. La política de respaldo de la base
      es **de la instancia compartida**, no de este proyecto.
- [ ] La prueba de restauración se ejecuta sobre una copia en **entorno de prueba**, nunca en
      producción: restaurar esta base revierte también la contabilidad de la compañía.
- [ ] Una credencial de Google revocada se detecta, se notifica y se reconecta desde la interfaz.
- [ ] El despliegue aplica el esquema **antes** de la API, y la API antes del worker.
- [ ] El DDL versionado opera **solo sobre el esquema `fact`**, nunca sobre `dbo`.
- [ ] Los `GRANT` de los dos usuarios de base de datos viajan en el SQL versionado.
- [ ] La carga inicial de `SugerenciaCuenta` desde el histórico se ejecuta una vez, es idempotente y
      excluye las cuentas que ya no existen en el plan actual.

### Flujo 9 — Verificación

- [ ] La lógica contable vive en un **núcleo sin base de datos, HTTP ni reloj**, probable con datos
      de entrada.
- [ ] Los cinco ejemplos de `REGLAS.md` §10 son **normativos** y están cubiertos por pruebas.
- [ ] Las siete invariantes de §7 se prueban en sus dos caminos: acepta y rechaza.
- [ ] Las pruebas de contrato sobre las tablas de frontera corren contra el **esquema versionado**,
      desde ambos lados, y verifican también la matriz de permisos.
- [ ] Existe **una** prueba de extremo a extremo sobre datos fijos: correo → ingesta → procesamiento
      → promoción → validación → evento. Una, no una suite.

## Correcciones al prototipo

El prototipo se aparta del diseño acordado en estos puntos, y **manda el diseño**:

| Punto | Corrección |
|---|---|
| `defaultLineas()` usa cuentas de 5 dígitos (`60111`, `40111`, `42011`) | El plan real es de **6 dígitos**. Las del prototipo son ilustrativas. |
| Checkbox "Revisé el duplicado" (`dupAck`) | **Se elimina.** Era estado local nunca persistido: la validación de cliente que el criterio prohíbe. |
| Factura 5 marcada `duplicado:true` con RUC distinto de la factura 4 | **No es un duplicado.** Cada proveedor tiene su propia numeración. El dato de ejemplo induce a error. |
| Tolerancia `Math.abs(sumDebe-sumHaber) < 0.01` | **Se elimina.** La igualdad es exacta por construcción. |
| `'Bandeja de entrada'` entre las carpetas monitoreadas | **Se quita.** Contradice la candidatura por etiqueta. |
| Filtros de asunto, remitentes y palabras clave | **Se eliminan.** La regla de candidatura no los usa. |
| "Eliminar correos procesados" | **Se elimina.** El worker etiqueta, nunca borra. |
| Visor como marcador de posición rayado | Se implementa con visor nativo sobre `GET /api/documentos/{id}/contenido`. |
| `compro` (`CP-000112`), correlativo interno | **Resuelto: conviven los dos números.** `NumeroComprobante` es el fiscal y `NumeroAsiento` el correlativo propio del libro, asignado al confirmar. |
| `defaultLineas()` genera solo tres líneas | **Incompleto.** El asiento real tiene bloque `PRINCIPAL` y bloque `DESTINO`, y varía según el tipo de comprobante. |
| Selector de cuenta contra el plan completo | **Se acota.** El selector ofrece solo las candidatas del motivo elegido, ordenadas por frecuencia histórica. |

## Riesgos técnicos abiertos

- **Reglas de mapeo contable — RESUELTAS.** Las diecisiete preguntas quedaron cerradas con los datos
  maestros reales de la compañía y están recogidas en **`REGLAS.md`**, que es el documento normativo
  para implementar el asiento. **El sistema ya es construible y operable.** Queda pendiente su
  revisión formal por un contador, con cuatro puntos señalados en el propio documento.
- **Clasificación de motivos alterada para la demostración.** Los 23 motivos que corresponden a
  `07 CAJA CHICA` fueron reclasificados a `02 COMPRAS` por necesidad de la demo. **Contablemente son
  de caja chica y debe revertirse antes de producción.** Están marcados con `†` en
  `MOTIVOS-CLASIFICACION.md`, y viven en la tabla satélite `MotivoAtributo`, de modo que revertirlos
  no toca el plan contable de la compañía.
- **Alcance contable declarado.** Detracción, retención y diferencia de cambio **quedan fuera**:
  nacen al pagar o al cierre, y este sistema registra comprobantes. Dos consecuencias que quien use
  la información debe conocer: el saldo de `421212` / `431212` **no está ajustado a la fecha de
  cierre**, y la detracción condiciona **cuándo** se puede tomar el crédito fiscal, de modo que este
  libro **no basta por sí solo** para determinarlo.
- **La cuenta de IGV tiene fecha de caducidad.** `401111` vale mientras la compañía tome el crédito
  fiscal íntegro. Si aparecen ventas no gravadas y hay que prorratear, la cuenta pasaría a depender
  del destino de la compra: `401161` y `401171` ya están en el plan esperando.
- **Dependencia de un catálogo que este sistema no controla — CERRADO.** Si el sistema contable
  elimina o renumera una cuenta que un asiento ya usó, esta aplicación no puede impedirlo, pero ya no
  le afecta: el asiento **congela al confirmar** la descripción de la cuenta, la del motivo y las
  cuentas de destino. Un cambio externo posterior no altera lo que el asiento dice ni contra qué
  cuenta revierte su nota de crédito.
- **Precisión real del motor de extracción.** No se ha seleccionado ni evaluado. El XML desactiva
  buena parte del riesgo para los comprobantes electrónicos, pero el PDF escaneado sigue expuesto.
  Debería validarse con facturas reales antes de comprometer el resto del desarrollo. Incluye
  decidir **si los documentos salen de la organización** hacia un servicio de terceros.
- **El worker no tiene vigilancia automática — MITIGADO.** El riesgo se había aceptado
  explícitamente porque el mecanismo de notificación vive dentro del propio worker, y una bandeja sin
  facturas nuevas es indistinguible de un día sin facturas. **La aceptación se revisó cuando cambió
  el precio de la mitigación**: el agregador de logs ya está en el plan de despliegue, así que el
  worker escribe un latido en `EstadoIntegracion` y el agregador alerta por su **ausencia**. El
  reinicio sigue siendo manual, pero ahora alguien se entera de que hace falta.
  **Riesgo residual:** nada vigila al vigilante. Si el agregador se detiene, la alerta tampoco llega.
- **Fragilidad del scraping de la SBS.** Un cambio de maquetación rompe la obtención del tipo de
  cambio. La carga manual (ADR 0006) es la salida, pero no hay detección automática de la rotura.
- **Frecuencia de sondeo sin fijar.** Dos bucles encadenados —Gmail y el inbox— consumen el mismo
  presupuesto de 15 minutos, y debe conciliarse con las cuotas de la API de Gmail.
- **Certificado TLS.** Falta decidir su origen y quién lo renueva. Las opciones reales son **dos**:
  autoridad interna con su raíz distribuida, o cambiar el host a un dominio público con DNS interno.
  Let's Encrypt **no puede emitir** para `facturas.empresa.local`: `.local` está reservado para mDNS
  por RFC 6762.
- **Coordinación de los dos almacenes.** Documentos en volumen y metadatos en base pueden
  desincronizarse. El orden de respaldo lo mitiga, pero falta una verificación periódica de
  integridad que detecte huérfanos.
- **Retención y crecimiento.** Conservación indefinida frente a capacidad real del volumen, de la
  base, de los logs y de la cuota de Drive. Falta fijar la retención y verificar la cuota contra el
  plan de Workspace contratado.
- **Sincronización de tipos en la frontera — CERRADO.** Los tipos de las tablas de contrato existen
  en C# y en Python, y el SQL versionado es la referencia autoritativa. ADR 0019 decide las **pruebas
  de contrato** que verifican esa mitigación desde ambos lados, en vez de dejarla afirmada.
- **El entorno actual no tiene respaldo, y arrancar con datos reales sin resolverlo es el mayor
  riesgo del proyecto.** Es una demostración académica, así que hoy no hay nada que perder. Pero la
  base es **compartida** con el sistema contable de la compañía, de modo que este proyecto no puede
  fijar por su cuenta el modelo de recuperación ni crear una cadena de `LOG BACKUP` propia: el RPO de
  15 minutos es un **requisito que se traslada** al administrador de la instancia. Las tres
  condiciones de ADR 0014 hay que responderlas **antes** de registrar la primera factura real.
  **No es un riesgo del diseño: es un riesgo de la decisión de arrancar.**
- **La métrica de precisión es una cota superior, no una medición.** Solo cuenta los errores que el
  asistente **notó**: un campo mal extraído y no advertido cuenta como acierto. Se reporta partida
  por fuente para que la cifra del PDF sea accionable, pero el sesgo solo se corrige con la prueba
  previa sobre facturas reales.
- **La factura mixta solo se detecta con XML.** Sobre un comprobante que llega únicamente en PDF, la
  mezcla de líneas gravadas y no gravadas **no es detectable de forma fiable por ningún medio**. El
  sistema lo declara con un indicador y traslada la comprobación al asistente. Es un límite de la
  regla, no un defecto de implementación.
