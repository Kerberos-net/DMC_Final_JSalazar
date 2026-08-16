# ADR 0011: El motivo de compra como origen de las líneas del asiento

## Estado

Aceptado. Revisión 2, tras recibir los datos maestros reales: 1650 cuentas, 90 motivos y 13
orígenes de libro.

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
    ProveedorId   BIGINT      NOT NULL,
    MotivoId      BIGINT      NOT NULL,
    CuentaCodigo  VARCHAR(10) NOT NULL,
    Veces         INT         NOT NULL DEFAULT 0,
    UltimoUso     DATETIME2   NOT NULL,
    PRIMARY KEY (ProveedorId, MotivoId, CuentaCodigo)
);
```

`Veces` se incrementa **al confirmar el asiento**, no al sugerir.

**Cascada:** cuenta más usada para `(proveedor, motivo)` → más usada para el `motivo` → primera
candidata del motivo. El mismo mecanismo, keyed solo por proveedor, sugiere el **motivo**.

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
- **Costo:** la sugerencia **no generaliza a proveedores nuevos**. La primera factura de un
  proveedor cae siempre al segundo o tercer nivel de la cascada. Es el precio de que sea explicable.
- **Costo:** un motivo mal elegido produce un asiento **correcto en forma y equivocado en fondo**.
  El control de cuadre no lo detecta, porque cuadra igual. Solo lo detecta la revisión humana.
- **Costo:** un prefijo corto ofrece muchas candidatas —hasta 34— y la sugerencia por frecuencia es
  lo único que evita que el asistente elija a ciegas la primera vez.
- **Corrección al prototipo:** sus cuentas son de 5 dígitos (`60111`, `40111`, `42011`). El plan real
  es de **6 dígitos**. Las del prototipo son ilustrativas.
