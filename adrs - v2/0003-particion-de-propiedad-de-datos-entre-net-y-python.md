# ADR 0003: Partición de propiedad de datos

## Estado

Aceptado. Revisión 3. Añade la clase de **tabla externa** tras conocerse que los datos maestros los
mantiene el sistema contable de la compañía, no esta aplicación.

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
| Ingesta y procesamiento | Python | `Email`, `DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`, `ProcesamientoIntentos` |
| Negocio | .NET | `Factura`, `AsientoContable`, `AsientoContableDetalle`, `AdjuntoManual`, `AuditoriaCorreccion` |
| Satélites de datos maestros | .NET | `ProveedorAtributo`, `MotivoAtributo`, `SugerenciaCuenta` |
| Seguridad | .NET | `Usuario` |

### 2. De contrato

Coescritas **por diseño**. Su coescritura no es una excepción: es lo que son.

| Tabla | Produce | Consume | Semántica |
|---|---|---|---|
| `OutboxEvent` | .NET | Python | Hechos de negocio ocurridos |
| `CommandQueue` | .NET | Python | Órdenes de ejecución |
| `InboxEvent` | Python | .NET | Hechos de procesamiento ocurridos |

### 3. De publicación con múltiples orígenes

Un solo tipo de fila, escrita por distintos componentes según su procedencia.

| Tabla | Origen `SBS` | Origen `MANUAL` | Lectura |
|---|---|---|---|
| `TipoCambio` | Python | .NET | Ambos |

| Tabla | Escribe | Lee |
|---|---|---|
| `Configuracion` | .NET | Ambos |

### 4. Externas

Escritas por un **sistema ajeno al proyecto**, en la misma base. Esta aplicación tiene **`SELECT`
únicamente**: sin `INSERT`, `UPDATE` ni `DELETE`.

| Tabla | Contenido |
|---|---|
| `Proveedor` | Catálogo de proveedores, incluido el genérico `P0000 (Varios)` |
| `CuentaContable` | Plan contable de la compañía, con `ctarefleja` y `ctapuente` |
| `Motivo` | Motivos de compra con sus prefijos de cuenta |
| `Origen` | Orígenes de libro |

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

## Alternativas consideradas

- **Replicar los datos maestros en tablas propias, sincronizadas periódicamente.** Aislaría a esta
  aplicación de cambios en el sistema contable. Se descartó por el flujo real del asistente: registra
  el proveedor en el otro sistema y vuelve **de inmediato** a seleccionarlo. Cualquier sincronización
  introduce una ventana en la que el proveedor existe allí pero no aquí, y la factura queda bloqueada.
- **Añadir las columnas que faltan directamente a las tablas externas.** Evitaría los satélites. Se
  descartó porque modificar el esquema de un sistema ajeno lo acopla a este proyecto y rompe la
  regla de que nadie escribe una tabla externa.
- **Base de datos por componente, con replicación.** Partición física real. Se descartó por
  desproporcionada, y porque la transacción única que exige la validación —factura, asiento,
  contador y evento— dejaría de ser posible.
- **Convención sin refuerzo en el motor.** Se mantiene como situación inicial, pero la clasificación
  en cuatro clases permite **refuerzo real** con permisos por usuario de base de datos.

## Consecuencias

- La partición es **implementable en el motor**. La clase externa es donde más importa: protege
  datos de los que este sistema no es responsable.
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
- **Riesgo heredado:** si el sistema contable elimina o renumera una cuenta que un asiento ya usó,
  esta aplicación no puede impedirlo. El asiento conserva el código; la descripción deja de
  resolver.
