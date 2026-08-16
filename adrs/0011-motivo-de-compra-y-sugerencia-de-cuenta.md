# ADR 0011: El motivo de compra como origen de las líneas del asiento

## Estado

Aceptado. Revisión 3. Añade la carga inicial de `SugerenciaCuenta` desde el histórico de la compañía
y fija el orden del tercer escalón de la cascada (revisión adversarial v2: A10, S4).

La revisión 2 se hizo tras recibir los datos maestros reales: 1650 cuentas, 90 motivos y 13 orígenes
de libro.

> **La eliminación de `FacturaDetalle` y `Producto` sigue en pie.** La revisión v2 señaló que el
> sistema debe rechazar la factura con líneas gravadas y no gravadas mezcladas y no tenía con qué
> detectarla. **No es un argumento para revivir esas tablas**: la detección se resuelve con un
> indicador calculado en la extracción a partir del XML (ADR 0017), que cuesta un campo.

## Contexto

El diseño original sostenía que el detalle del asiento se generaba *"mapeando cada producto de
`FacturaDetalle` a su cuenta contable"*. **Nada alimentaba esa tabla**: ni el PRD, ni
`DatosExtraidos`, ni el prototipo contemplaban líneas de producto. `Producto` y el mapeo
producto→cuenta eran estructura muerta.

El flujo real de la compañía es otro: **al registrar la factura, el asistente selecciona el motivo
de la compra**, y ese motivo determina la cuenta de cargo.

## Decisión

### El motivo determina la cuenta de cargo

Se eliminan del modelo `FacturaDetalle`, `Producto` y el mapeo producto→cuenta.

1. Al abrir la factura, el asistente selecciona un **motivo**. Es **obligatorio**: sin motivo no hay
   cuenta de cargo y la validación se rechaza.
2. El motivo declara **uno o varios prefijos de cuenta**, separados por coma.
3. El sistema **sugiere** la candidata más probable. El asistente confirma o cambia por otra del
   mismo motivo.
4. El asistente puede **dividir el cargo** entre varias cuentas del motivo.

### Los prefijos, no las cuentas

`MotivoCuenta` **no almacena cuentas: almacena prefijos**, de longitud variable. Las candidatas se
resuelven contra las **907 hojas de 6 dígitos** del plan.

```
Motivo 22  Fletes traslado mercadería  →  631111   →   1 candidata
Motivo  6  Transferencia entre Bancos  →  104      →  20 candidatas
Motivo 70  Préstamos a terceros        →  16       →  34 candidatas
Motivo  8  Tributos por pagar          →  4011,4017,4018,403,417,167101,1674  →  22
```

**Solo las cuentas de 6 dígitos son imputables.** Los niveles 2 a 5 son jerarquía. Los 90 motivos
entregados resuelven correctamente: ningún prefijo queda sin cuentas.

`MotivoCuenta` no es una tabla externa aparte: es la **interpretación** que esta aplicación hace del
campo de prefijos del catálogo de motivos (ADR 0003).

### Alcance por origen de libro

Cada motivo declara su **origen** en `MotivoAtributo`. La pantalla de validación **solo ofrece los
motivos de origen `02` COMPRAS**, no los 90.

De ahí se deriva que **el origen del asiento siempre sea `02`**: no es un campo que alguien elija,
sino una consecuencia del motivo seleccionado.

### Bajas

`MotivoAtributo.Activo` retira un motivo del selector **sin borrarlo**: los asientos históricos lo
referencian y esa referencia debe seguir resolviendo.

### La sugerencia es frecuencia histórica, no un modelo

```sql
CREATE TABLE SugerenciaCuenta (
    ProveedorCodigo CHAR(6)   NOT NULL,   -- 'P00000' — el catálogo externo se identifica por código
    MotivoId      INT         NOT NULL,
    CuentaCodigo  VARCHAR(10) NOT NULL,
    Veces         INT         NOT NULL DEFAULT 0,
    UltimoUso     DATETIME2   NOT NULL,
    PRIMARY KEY (ProveedorCodigo, MotivoId, CuentaCodigo)
);
```

> **Corrección (ciclo `esquema-y-permisos`).** La revisión 3 de este ADR escribía
> `ProveedorId BIGINT` y `MotivoId BIGINT`, dando por hecho que los catálogos externos usan
> identificadores subrogados. **No es así.** `dbo.Proveedor` se identifica por un **código de cinco
> caracteres** —`P00000` es literalmente la clave, no una etiqueta—, `dbo.Motivo` por un **entero**,
> `dbo.CuentaContable` por el **código de cuenta como texto de longitud variable** y `dbo.Origen`
> por un **código de dos caracteres**.
>
> El caso de `CuentaCodigo` no es cosmético: tiene que ser `VARCHAR` y no `CHAR`, porque los motivos
> guardan **prefijos de 2 a 6 dígitos** y un tipo de longitud fija los rellenaría con espacios,
> rompiendo la resolución por `LIKE prefijo + '%'` que sostiene toda la cascada de sugerencia.

`Veces` se incrementa **al confirmar el asiento**, no al sugerir.

**Cascada:** cuenta más usada para `(proveedor, motivo)` → más usada para el `motivo` → primera
candidata del motivo **ordenada por `CuentaCodigo`**. El mismo mecanismo, keyed solo por proveedor,
sugiere el **motivo**.

El `ORDER BY` del tercer escalón no es un detalle: sin él, "la primera" es la que devuelva el motor,
y eso cambia con un índice nuevo o un plan de ejecución distinto. La misma pantalla propondría
cuentas distintas en dos días, lo que parece un error aunque no lo sea.

### Carga inicial desde el histórico de la compañía

`SugerenciaCuenta` **no arranca vacía**. Un proceso ejecutado una vez al desplegar cuenta
`(proveedor, cuenta)` sobre los asientos históricos que el sistema contable mantiene en esta misma
base (ADR 0003) y siembra la tabla:

```sql
INSERT INTO SugerenciaCuenta (ProveedorCodigo, CuentaCodigo, Veces)
SELECT d.ProveedorCodigo, d.CuentaCodigo, COUNT(*)
  FROM <asientos históricos del sistema contable> d
 WHERE d.Fecha >= @desde
 GROUP BY d.ProveedorCodigo, d.CuentaCodigo;
```

**No es migración de datos.** Es un `SELECT` sobre el histórico y un `INSERT` en una tabla propia: no
mueve nada, no transforma nada y no toca el sistema contable.

Sin la siembra, en el arranque **todas** las facturas caen al tercer escalón, que para un motivo con
34 candidatas es prácticamente arbitrario — y el criterio de menos de 5 minutos por factura estaría
en su peor momento justo cuando se forma la confianza del usuario en el sistema.

No cambia el mecanismo ni pierde explicabilidad: el fundamento que se muestra sigue siendo un número,
solo que ahora existe desde la primera factura.

Tres condiciones del proceso:

- La ventana `@desde` es una decisión. Doce meses es el punto de partida razonable, y hay que
  **verificar que el plan de cuentas no cambió** en ese periodo.
- Las cuentas históricas que **ya no existen** en el plan actual se excluyen, o la cascada sugeriría
  una cuenta imposible.
- El proceso es **idempotente**: ejecutarlo dos veces no duplica contadores.

### La sugerencia nunca decide sola

El asistente siempre confirma o corrige. La interfaz muestra el fundamento —*"usado 14 de 15 veces
con este proveedor"*— para que la decisión sea informada y no un autocompletado ciego.

## Alternativas consideradas

- **El XML UBL como fuente de líneas.** Trae las líneas exactas del comprobante. Se descartó porque
  el asiento no se estructura por producto sino por motivo contable: una descripción libre del
  proveedor seguiría necesitando que alguien decida la cuenta. El XML se usa como fuente prioritaria
  de cabecera (ADR 0017).
- **Captura manual de líneas contra un catálogo de productos.** Se descartó: contradice el criterio
  de menos de 5 minutos por factura y exigiría mantener un catálogo que la compañía no lleva.
- **Generación fija de tres líneas desde el total.** Es lo que hace el prototipo. Se descartó porque
  usa una única cuenta de compras para todo, y el plan personalizado distingue la naturaleza del
  gasto en 907 cuentas imputables.
- **Sugerencia mediante IA sobre la descripción del comprobante.** Generalizaría a proveedores
  nuevos. Se descartó porque dejaría de ser explicable ante un contador, añadiría costo y latencia
  por factura, y sacaría datos del comprobante fuera de la organización.
- **Almacenar cuentas en vez de prefijos.** Sería más simple de consultar. Se descartó porque el
  dato maestro real usa prefijos, y expandirlos al cargar congelaría una foto: una cuenta nueva bajo
  `104` no aparecería hasta reprocesar el catálogo.

## Consecuencias

- El asiento es implementable. El insumo existe y lo aporta el asistente en un clic.
- El sistema mejora con el uso sin dejar de ser determinista: la sugerencia se explica con un
  número, y se puede auditar y reproducir.
- Una cuenta nueva bajo un prefijo ya declarado **aparece sola**, sin tocar nada.
- **Costo:** la sugerencia **no generaliza a proveedores nuevos**. La primera factura de un proveedor
  cae al segundo o tercer nivel de la cascada. Es el precio de que sea explicable. La siembra desde
  el histórico lo mitiga para los proveedores que la compañía ya conoce, que son la mayoría; no para
  los realmente nuevos.
- **Costo de la siembra:** hereda la calidad del histórico. Si el histórico tiene criterios
  inconsistentes, la sugerencia los reproduce. Es aceptable —el asistente corrige y el contador
  vuelve a aprender— pero conviene saberlo antes de culpar al mecanismo.
- **Costo:** un motivo mal elegido produce un asiento **correcto en forma y equivocado en fondo**.
  El control de cuadre no lo detecta, porque cuadra igual. Solo lo detecta la revisión humana.
- **Costo:** un prefijo corto ofrece muchas candidatas —hasta 34— y la sugerencia por frecuencia es
  lo único que evita que el asistente elija a ciegas la primera vez.
- **Corrección al prototipo:** sus cuentas son de 5 dígitos (`60111`, `40111`, `42011`). El plan real
  es de **6 dígitos**. Las del prototipo son ilustrativas.
