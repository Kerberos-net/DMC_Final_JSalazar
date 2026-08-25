# ADR 0003: Partición de propiedad de datos

## Estado

Aceptado. Revisión 6. Reclasifica `fact.ProcesamientoError` de privada de Python (clase 1) a
**lectura asimétrica** (clase 3): Python sigue siendo el único que escribe, pero ambos runtimes
pueden leer. BACKLOG #13 (bandeja e incidencias) necesita que `.NET` lea el historial de errores de
procesamiento para el panel de errores de la bandeja — sin esta reclasificación, el `DENY SELECT`
que `008_usuarios_y_permisos.sql` le puso a `fact_api` (revisión 4, ampliación defensiva del bucket
"privadas de Python") bloquea una lectura que ningún escenario ratificado de
`openspec/specs/esquema-y-permisos/spec.md` exige, y que el precedente `fact.Configuracion` ya
demuestra que este ADR sabe modelar (un solo escritor, ambos lectores). `018_permiso_lectura_
procesamiento_error.sql` aplica el cambio: `REVOKE` el `DENY SELECT`, `GRANT SELECT` a `fact_api`,
y re-`DENY INSERT, UPDATE, DELETE` explícitamente — Python sigue siendo el único escritor, la
frontera de escritura queda igual de reforzada por el motor que antes.

Revisión 5. Añade `DocumentoIdentidad` como quinta tabla externa: el catálogo se
incorporó después de escribir este ADR y `Proveedor` tiene clave foránea hacia él, de modo que
omitirlo dejaba sin lectura al tipo de documento del proveedor.

La revisión 4 convierte la partición en **permisos reales del motor** —esquema propio y dos
usuarios de base de datos— que hasta ahora se reivindicaban sin decidirse, y declara como premisa
verificable el supuesto sobre el alta de proveedores (revisión adversarial v2, A12, A15).

La revisión 3 añadió la clase de **tabla externa** tras conocerse que los datos maestros los mantiene
el sistema contable de la compañía, no esta aplicación.

## Contexto

Dos runtimes escriben la misma base de datos, sin contrato de red entre ellos. Sin una partición
explícita, una regla de negocio aplicada en .NET se puede saltar desde Python sin que nada lo
advierta.

A eso se suma un tercer actor que el diseño no contemplaba: **el sistema contable de la compañía
mantiene los datos maestros** —proveedores, plan de cuentas, motivos y orígenes de libro— en la
misma base de datos asignada al proyecto. El alta de un proveedor ocurre allí, fuera del alcance de
este software.

## Decisión

Partición **por tabla, con orígenes de escritura declarados**. Cuatro clases.

### 1. Privadas

Un solo componente escribe **y** lee.

| Contexto | Propietario | Tablas |
|---|---|---|
| Ingesta y procesamiento | Python | `Email`, `DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoIntentos` |
| Negocio | .NET | `Factura`, `AsientoContable`, `AsientoContableDetalle`, `AdjuntoManual`, `AuditoriaCorreccion`, `FacturaExtraccion`, `CorrelativoAsiento` |
| Satélites de datos maestros | .NET | `ProveedorAtributo`, `MotivoAtributo`, `SugerenciaCuenta` |
| Seguridad | .NET | `Usuario` |

`FacturaExtraccion` guarda, del lado de .NET, la evidencia de qué leyó la extracción en cada campo y
de qué fuente —XML o PDF—. Existe porque la métrica de precisión compara esa evidencia contra la
factura corregida, y **`DatosExtraidos` es privada de Python**: sin esta tabla, ningún componente
puede leer los dos lados de la comparación (revisión v2, hallazgo N1). El dato ya viaja en el
*payload* del `InboxEvent`; lo único nuevo es persistirlo en vez de descartarlo.

`CorrelativoAsiento` es la tabla contador del número de asiento, con reinicio mensual por año, mes y
origen (ADR 0006).

### 2. De contrato

Coescritas **por diseño**. Su coescritura no es una excepción: es lo que son.

| Tabla | Produce | Consume | Semántica |
|---|---|---|---|
| `OutboxEvent` | .NET | Python | Hechos de negocio ocurridos |
| `CommandQueue` | .NET | Python | Órdenes de ejecución |
| `InboxEvent` | Python | .NET | Hechos de procesamiento ocurridos |

La coescritura de estas tablas es **asimétrica y hay que expresarla**: quien produce inserta, quien
consume actualiza el estado. Python inserta en `InboxEvent` y actualiza `OutboxEvent`; .NET hace lo
contrario. Ninguno de los dos inserta en la tabla del otro.

`InboxEvent` lleva además el resultado del consumo —`PROMOVIDO` o `DESCARTADO`, con la factura
creada o el motivo del descarte—, que es lo que impide reprocesar para siempre un documento que .NET
decidió no promover (ADR 0005).

### 3. De publicación con múltiples orígenes

Un solo tipo de fila, escrita por distintos componentes según su procedencia.

| Tabla | Origen `SBS` | Origen `MANUAL` | Lectura |
|---|---|---|---|
| `TipoCambio` | Python | .NET | Ambos |

| Tabla | Escribe | Lee |
|---|---|---|
| `Configuracion` | .NET | Ambos |
| `ProcesamientoError` | Python | Ambos (revisión 6, BACKLOG #13: panel de errores de la bandeja) |

| Tabla | Filas escritas por Python | Filas escritas por .NET | Discriminador |
|---|---|---|---|
| `EstadoIntegracion` | `GMAIL`, `SBS`, `WORKER`, `DRIVE`, `SHEETS` | `TELEGRAM`, `CORREO` si la API los ejecuta | `Nombre` |

`EstadoIntegracion` lleva la última ejecución, el último éxito, el último error y los fallos
seguidos de cada integración. Alimenta la píldora "Conectado / Con error" de la pantalla de
Configuración —que hasta ahora el diseño de interfaz pedía sin que nada la sostuviera— y su fila
`WORKER` es el latido que permite alertar cuando el worker se detiene (ADR 0015).

Se escribe **fuera de la transacción de negocio**. Es telemetría: que su escritura falle no puede
tumbar una validación.

### 4. Externas

Escritas por un **sistema ajeno al proyecto**, en la misma base. Esta aplicación tiene **`SELECT`
únicamente**: sin `INSERT`, `UPDATE` ni `DELETE`.

| Tabla | Contenido |
|---|---|
| `Proveedor` | Catálogo de proveedores, incluido el genérico `P00000 (Varios)` |
| `CuentaContable` | Plan contable de la compañía, con `ctarefleja` y `ctapuente` |
| `Motivo` | Motivos de compra con sus prefijos de cuenta |
| `Origen` | Orígenes de libro |
| `DocumentoIdentidad` | Tipos de documento de identidad de SUNAT; `Proveedor` tiene clave foránea hacia él |

### Tablas satélite

Lo que **este proyecto necesita y el sistema contable no aporta** vive aparte, unido por clave. El
catálogo externo **no se toca nunca**.

| Satélite | Aporta | Para |
|---|---|---|
| `ProveedorAtributo` | `EsRelacionada` | Elegir entre `4212` y `4312` (ADR 0006) |
| `MotivoAtributo` | `Activo`, `OrigenLibro` | Bajas y alcance por origen (ADR 0011) |
| `SugerenciaCuenta` | Frecuencia por proveedor y motivo | El aprendizaje (ADR 0011) |

### Reglas invariantes

1. **Python no escribe ni lee tablas de dominio de .NET.** Lo que necesite viaja en el *payload* de
   un evento, resuelto por .NET al emitirlo.
2. **Python no solicita operaciones de dominio.** Informa hechos; la decisión es de .NET (ADR 0005).
3. **Ningún componente sondea la tabla privada del otro.** Toda comunicación pasa por un contrato.
4. **Nadie escribe una tabla externa.** Ni .NET ni Python.

### Refuerzo en el motor: esquema propio y dos usuarios

Las cuatro reglas anteriores dejan de ser convención. La base es **compartida** con el sistema
contable de la compañía (ADR 0014), de modo que los objetos de este proyecto viven en un **esquema
propio**:

```sql
CREATE SCHEMA fact;   -- todos los objetos de este proyecto
```

Dos usuarios de base de datos, uno por runtime, con permiso explícito por tabla:

| | `usr_api` (.NET) | `usr_worker` (Python) |
|---|---|---|
| **Privadas propias** | `SELECT`/`INSERT`/`UPDATE` sobre las de negocio, satélites, seguridad y `fact.CorrelativoAsiento` | `SELECT`/`INSERT`/`UPDATE` sobre las de ingesta y procesamiento |
| **Privadas del otro** | **Ninguno** sobre `fact.Procesamiento`, `fact.DatosExtraidos` y demás tablas de Python (excepto `fact.ProcesamientoError`, ver fila "Publicación") | **Ninguno** sobre `fact.Factura`, `fact.AsientoContable*`, `fact.AdjuntoManual`, `fact.Usuario` |
| **`fact.OutboxEvent`** | `INSERT`, `SELECT` | `SELECT`, `UPDATE` — Python **consume** el outbox y mantiene el estado por integración |
| **`fact.InboxEvent`** | `SELECT`, `UPDATE` — marca el resultado del consumo | `INSERT`, `SELECT` |
| **`fact.CommandQueue`** | `INSERT`, `SELECT` | `SELECT`, `UPDATE` |
| **Publicación** | `INSERT`/`UPDATE`/`SELECT` según discriminador | `INSERT`/`UPDATE`/`SELECT` según discriminador |
| **Externas (`dbo`)** | `SELECT` | `SELECT` |

Los `GRANT` **viajan en el SQL versionado** de ADR 0016, como cualquier otro cambio de esquema: se
revisan, se aplican en orden y se reproducen idénticos en cada entorno. La matriz **es** esta
partición expresada en el motor, no un documento paralelo que se desincroniza.

Dos consecuencias que valen más que la tabla:

- **`usr_api` no puede leer `fact.Procesamiento`.** Aunque alguien escriba ese `SELECT`, falla. Es la
  invariante 3 impuesta, no confiada.
- **Las tablas externas se nombran con su esquema real (`dbo`)**, lo que las hace visualmente
  distintas en cada consulta. La clase "externa" deja de ser una nota de este ADR y se lee en el
  código.

> **Confirmado.** El proyecto puede crear el esquema y ejecutar DDL: la base está **asignada al
> proyecto**, aunque conviva con las tablas maestras del sistema contable. La matriz de permisos se
> aplica sin intermediarios (ADR 0016).

## Alternativas consideradas

- **Replicar los datos maestros en tablas propias, sincronizadas periódicamente.** Aislaría a esta
  aplicación de cambios en el sistema contable. Se descartó por el flujo real del asistente: registra
  el proveedor en el otro sistema y vuelve **de inmediato** a seleccionarlo. Cualquier sincronización
  introduce una ventana en la que el proveedor existe allí pero no aquí, y la factura queda bloqueada.

  > **Confirmado (revisión v2, A15).** El supuesto es cierto: **el asistente contable tiene permiso
  > de alta de proveedores en el sistema contable, y el alta es inmediata.** Sale, lo registra,
  > vuelve y lo encuentra. El descarte de la replicación se sostiene, y el bloqueo con `409` de ADR
  > 0006 no necesita ningún indicador de espera.
  >
  > Es la premisa que más sostenía en pie: de ella dependían el descarte de la replicación, el
  > bloqueo de `P00000` y la reversión 3 del PRD. Merecía verificarse en vez de darse por buena.
- **Añadir las columnas que faltan directamente a las tablas externas.** Evitaría los satélites. Se
  descartó porque modificar el esquema de un sistema ajeno lo acopla a este proyecto y rompe la
  regla de que nadie escribe una tabla externa.
- **Base de datos por componente, con replicación.** Partición física real. Se descartó por
  desproporcionada, y porque la transacción única que exige la validación —factura, asiento,
  contador y evento— dejaría de ser posible.
- **Convención sin refuerzo en el motor.** Era la situación hasta la revisión 3: la clasificación en
  cuatro clases *permitía* el refuerzo, pero nadie lo decidía. Se descarta en la revisión 4 porque la
  propiedad más fuerte de este ADR —*"nadie escribe una tabla externa"*— quedaba sostenida por
  disciplina, que es exactamente aquello sobre lo que este ADR dice mejorar.
- **Un solo usuario de base de datos con permisos amplios.** Más simple de desplegar y de configurar.
  Se descarta porque haría indistinguibles las cuatro clases en el motor: sin dos identidades no hay
  forma de que `usr_api` **no pueda** leer `Procesamiento`, y esa imposibilidad es el valor de toda
  la partición.

## Consecuencias

- La partición **está implementada en el motor**, no es implementable. La clase externa es donde más
  importa: protege datos de los que este sistema no es responsable.
- Las cuatro reglas invariantes son **verificables**: que `usr_api` no pueda leer `Procesamiento` y
  que `usr_worker` no pueda escribir `Factura` son pruebas de contrato, no afirmaciones (ADR 0019).
- **Esta aplicación no escribe ningún dato maestro.** Un proveedor, una cuenta o un motivo nuevos
  aparecen solos, sin replicar nada, y desaparece el riesgo de dos catálogos que divergen en
  silencio hasta que un asiento usa una cuenta que ya no existe.
- La reclasificación de motivos por origen vive en `MotivoAtributo`, de modo que ajustarla **no toca
  el plan contable de la compañía**.
- **Costo:** una unión más en toda consulta de datos maestros, para recuperar los atributos
  satélite.
- **Costo:** el empaquetado hacia Drive necesita rutas de `DocumentoRecibido` **y** de
  `AdjuntoManual`, y Python no puede leer la segunda. Se resuelve por el *payload* (ADR 0004).
- **Costo:** la bandeja combina facturas con incidencias de procesamiento. .NET expone una vista
  lógica ya resuelta; Angular nunca combina fuentes.
- **Costo:** el despliegue necesita credenciales separadas por componente, que ADR 0015 cubre con el
  gestor de secretos.
- **Riesgo heredado, ahora cerrado.** Si el sistema contable elimina o renumera una cuenta que un
  asiento ya usó, esta aplicación no puede impedirlo. La revisión v2 lo cierra por el mismo camino
  que ADR 0006 ya usaba con los importes: **el asiento congela al confirmar** la descripción de la
  cuenta, la del motivo y las cuentas de destino `ctarefleja` / `ctapuente`. Un cambio posterior en
  el catálogo externo ya no altera lo que el asiento dice ni contra qué cuenta revierte su nota de
  crédito (A6, A14).
- **Las dos premisas externas de este ADR quedaron verificadas** y ya no condicionan nada: el derecho
  a crear esquema y ejecutar DDL, y el alta inmediata de proveedores por parte del asistente. Se
  conservan escritas porque, si alguna cambiara, cambiaría el diseño — no son detalles de
  implementación.
