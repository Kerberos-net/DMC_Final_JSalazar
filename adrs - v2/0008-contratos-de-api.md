# ADR 0008: Contratos de comunicación entre componentes

## Estado

Aceptado. Reemplaza la versión previa (`adrs - v1/0008`), que carecía de endpoints para resolver
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
POST /api/asientos/{id}/reactivar
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

### Errores — RFC 9457

`application/problem+json` en toda respuesta de error.

| Código | Uso |
|---|---|
| `409 Conflict` | Estado inválido para la operación |
| `422 Unprocessable Content` | El cuerpo es válido pero viola una invariante del asiento |
| `400 Bad Request` | El cuerpo está malformado |

**Casos de `409`** — el estado del sistema impide la operación:

| Caso | Salida para el usuario |
|---|---|
| Duplicado sin resolver | Corregir el número, o descartar la factura |
| Comprobante emitido en **domingo** (`01`, `03` y `07`) | Corregir la fecha, o descartar |
| Factura en moneda extranjera **sin tipo de cambio** | Cargarlo con `POST /api/tipos-cambio` |
| Proveedor `P0000 (Varios)` sin resolver | Registrarlo en el sistema externo y seleccionarlo |
| `FechaContable` anterior a la **fecha de corte** | Ajustar la fecha contable |
| Nota de crédito cuya factura referenciada no existe, no está validada, está descartada o tiene el asiento anulado | Resolver primero la factura original |
| Asiento ya confirmado | Reabrirlo con motivo |

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
