# ADR 0008: Contratos de comunicación entre componentes

## Estado

Aceptado. Revisión 3. Retira `reactivar` —endpoint sin respaldo en ninguna decisión—, añade el `GET`
de estado de integraciones que el diseño de interfaz pedía sin cobertura, e introduce concurrencia
optimista con `If-Match` y `412` (revisión adversarial v2: C4, A3, A7).

La revisión 2 reemplazó la versión previa (`adrs - v1/0008`), que carecía de endpoints para resolver
duplicados, editar el asiento, servir documentos, subir adjuntos y reautenticar contra Google.

## Contexto

La SPA no accede a la base de datos: todo pasa por la API. El diseño anterior definía un contrato
REST con algunos endpoints de comando, pero dejaba fuera cinco capacidades que la interfaz sí
ejerce, y una de ellas —el visor de documentos— es la razón de ser de la pantalla central del
producto.

Hay además un problema de fondo que la versión anterior no enunciaba: **actualizar un recurso no es
lo mismo que ejecutar una operación de negocio**. `PUT /facturas/42` con `{"estado":"VALIDADA"}`
obliga al servidor a adivinar la intención y a decidir qué efectos colaterales disparar a partir de
un cambio de campo.

## Decisión

### Regla de corte

> **¿Cambiar este dato tiene consecuencias más allá de guardarlo?**
> **No** → recurso REST. **Sí** → endpoint de comando explícito.

### Consultas y edición de borrador — REST

```http
GET    /api/bandeja?estado=&desde=&hasta=&proveedor=&pagina=
GET    /api/facturas/{id}
PATCH  /api/facturas/{id}
GET    /api/facturas/{id}/documentos
GET    /api/documentos/{id}/contenido
GET    /api/asientos/{id}
PATCH  /api/asientos/{id}
GET    /api/motivos
GET    /api/motivos/{id}/cuentas
GET    /api/cuentas
GET    /api/proveedores?buscar=
GET    /api/tipos-cambio?fecha=
GET    /api/configuracion
PUT    /api/configuracion
```

`GET /api/bandeja` devuelve la **vista lógica unificada** de facturas e incidencias de
procesamiento, resuelta por .NET. Cada elemento declara su origen. **Angular nunca combina fuentes**
(ADR 0003).

### Los catálogos son de solo lectura

`/api/motivos`, `/api/cuentas` y `/api/proveedores` exponen **tablas externas** mantenidas por el
sistema contable de la compañía (ADR 0003). **No existe `POST /api/proveedores`, ni de motivos, ni
de cuentas.**

El alta de un proveedor ocurre en otro sistema, fuera del alcance de este proyecto. El flujo del
asistente es: buscar → si no está, salir a registrarlo allí → volver y seleccionarlo. La factura
espera en `PENDIENTE_VALIDACION` con su avance guardado.

`GET /api/motivos` devuelve **solo los motivos activos de origen `02` COMPRAS**, no el catálogo
completo (ADR 0011).

`GET /api/motivos/{id}/cuentas` resuelve los prefijos del motivo contra las hojas de 6 dígitos del
plan y devuelve las candidatas, ordenadas por la frecuencia histórica del par proveedor-motivo.

### Operaciones de negocio — comandos

```http
POST /api/facturas/{id}/abrir
POST /api/facturas/{id}/validar
POST /api/facturas/{id}/descartar
POST /api/asientos/{id}/lineas
POST /api/asientos/{id}/reabrir
POST /api/asientos/{id}/anular
POST /api/facturas/{id}/adjuntos
POST /api/tipos-cambio
POST /api/incidencias/{id}/reprocesar
POST /api/integraciones/{nombre}/sincronizar
POST /api/integraciones/google/reconectar

PATCH  /api/asientos/{id}/lineas/{lineaId}
DELETE /api/asientos/{id}/lineas/{lineaId}
DELETE /api/facturas/{id}/adjuntos/{adjuntoId}
```

**`POST /api/facturas/{id}/abrir`** es una acción explícita, no un efecto lateral del `GET`. Crea el
asiento en `BORRADOR` si no existe (ADR 0006). Un `GET` que inserta filas rompería la semántica de
HTTP: un *prefetch* del navegador generaría asientos.

**La línea se identifica por `LineaId`**, nunca por su posición. Agregar o eliminar líneas no
desestabiliza el contrato.

**`POST /api/asientos/{id}/reabrir`** exige `motivo` en el cuerpo.

**No existe `POST /api/asientos/{id}/reactivar`.** La versión anterior lo exponía sin que ninguna
decisión lo respaldara: no estaba en el ciclo de vida de ADR 0006 ni tenía evento en ADR 0004.
`ANULADO` es terminal; anular libera la factura para un asiento nuevo (ADR 0006).

**Los adjuntos siguen abiertos después de validar**, y por eso `POST /api/facturas/{id}/adjuntos` y
`DELETE /api/facturas/{id}/adjuntos/{adjuntoId}` **emiten `DOCUMENTACION_ACTUALIZADA`** cuando la
factura ya está validada. Sin ese evento, el medio probatorio que llega tarde —el caso que motivó la
funcionalidad— nunca llegaría a Drive, y el criterio del 100% de facturas archivadas con sus medios
probatorios se incumpliría en silencio (ADR 0013).

### Estado de las integraciones

```http
GET /api/integraciones/estado
```

Devuelve, por integración, la última ejecución, el último éxito, el último error y los fallos
seguidos. Alimenta la píldora "Conectado / Con error" de la pantalla de Configuración, que hasta
ahora el diseño de interfaz pedía sin que ningún endpoint la sostuviera. La píldora **se deriva** de
esos campos; no se almacena.

### Concurrencia optimista

`PATCH /api/facturas/{id}` y `PATCH /api/asientos/{id}` exigen `If-Match` con el `ETag` que devolvió
el `GET`. Discrepancia → **`412 Precondition Failed`**.

No es teórico aunque haya un solo usuario: el servicio de fondo de ADR 0005 promueve y escribe
`Factura`, la bandera de duplicado se recalcula al guardar y al validar, y `reabrir` / `anular` tocan
el mismo agregado. Dos pestañas abiertas bastan para perder una corrección sin que nada lo advierta
—y toda corrección perdida es además una fila que `AuditoriaCorreccion` registrará como válida.

### Errores — RFC 9457

`application/problem+json` en toda respuesta de error.

| Código | Uso |
|---|---|
| `409 Conflict` | Estado inválido para la operación |
| `412 Precondition Failed` | Alguien más modificó el recurso: el `If-Match` no coincide |
| `422 Unprocessable Content` | El cuerpo es válido pero viola una invariante del asiento |
| `400 Bad Request` | El cuerpo está malformado |

`409` y `412` son mensajes **distintos** para el asistente y la interfaz debe separarlos: el primero
significa *"tu dato viola una regla"*; el segundo, *"alguien más lo cambió, recarga"*.

**Casos de `409`** — el estado del sistema impide la operación:

| Caso | Salida para el usuario |
|---|---|
| Duplicado sin resolver | Corregir el número, o descartar la factura |
| Comprobante emitido en **domingo** (`01`, `03` y `07`) | Corregir la fecha, o descartar |
| Factura en moneda extranjera **sin tipo de cambio** | Cargarlo con `POST /api/tipos-cambio` |
| Proveedor `P00000 (Varios)` sin resolver | Registrarlo en el sistema externo y seleccionarlo |
| `FechaContable` anterior a la **fecha de corte** | Ajustar la fecha contable |
| Nota de crédito con referencia **interna** cuya factura no existe, no está validada, está descartada o tiene el asiento **vigente** anulado | Resolver primero la factura original |
| Asiento ya confirmado | Reabrirlo con motivo |
| **Factura mixta**: el XML declara más de un tipo de afectación | Fuera de alcance: registrar por otra vía |
| **Afectación no verificada** sin confirmar: el comprobante llegó solo en PDF | Confirmar la afectación antes de validar |

El duplicado es el único caso de `409` que **antes no era alcanzable**: el índice único de identidad
lo rechazaba en el `INSERT`, días antes y en el worker, sin interfaz donde mostrarlo. El índice pasó a
ser de detección y la unicidad se comprueba aquí, que es donde el asistente puede resolverla.

**Casos de `422`** — las invariantes de ADR 0006: asiento descuadrado, cargos que no igualan la base
imponible o el total según el tipo de comprobante, línea sin cuenta, bloque de destino incompleto, o
notas de crédito acumuladas que exceden el importe de la factura.

```json
{
  "type": "https://facturas.empresa.local/errors/asiento-descuadrado",
  "title": "El asiento no cuadra",
  "status": 422,
  "detail": "La suma del debe (1180.00) no coincide con la del haber (1000.00).",
  "sumaDebe": 1180.00,
  "sumaHaber": 1000.00
}
```

Toda validación de negocio se aplica **en el servidor**. La interfaz puede anticiparla, nunca
sustituirla.

### Contratos internos

.NET ↔ Python no tienen contrato de red. Se comunican por `OutboxEvent`, `CommandQueue` e
`InboxEvent` (ADR 0004).

## Alternativas consideradas

- **CRUD puro sobre los recursos.** Un contrato uniforme y predecible. Se descartó porque validar
  una factura desencadena cinco efectos en una transacción; expresarlo como un cambio de campo
  obliga al servidor a inferir la intención a partir del *diff*.
- **Un endpoint genérico `/api/facturas/{id}/accion` con el nombre en el cuerpo.** Menos rutas. Se
  descartó porque el contrato deja de ser inspeccionable: no hay forma de saber qué acciones existen
  sin leer el código, y el enrutamiento y la autorización pierden granularidad.
- **URLs firmadas para el contenido de los documentos.** Descargarían directo sin pasar por la API.
  Se descartó porque esquiva la cookie de sesión y abre una superficie de autorización paralela, con
  el visor funcionando por un mecanismo distinto al del resto de la aplicación.

## Consecuencias

- La intención de cada operación está en la ruta. El *log* del servidor se lee como la historia de
  lo que el usuario hizo.
- Las cinco capacidades que faltaban —resolver duplicados, editar y reabrir el asiento, ver el
  documento, adjuntar archivos y reconectar Google— tienen contrato.
- Los códigos 409 y 422 distinguen "no puedes hacer esto ahora" de "esto que mandas no cuadra", que
  son diagnósticos distintos para el usuario.
- **Costo:** más rutas que un CRUD, y cada operación de negocio nueva añade la suya.
- **Costo:** `POST /abrir` obliga a la SPA a una llamada explícita al entrar a la pantalla de
  detalle, en lugar de un único `GET`.
