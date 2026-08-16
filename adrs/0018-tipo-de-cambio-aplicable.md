# ADR 0018: Tipo de cambio aplicable a la conversión

## Estado

Aceptado. Revisión 1. Recoge cuatro decisiones cambiarias que hasta ahora vivían dispersas —tres de
ellas revierten al PRD sin ADR propio, y una nace de la revisión adversarial v2 (C6, C9).

## Contexto

El sistema registra comprobantes en moneda extranjera y genera su asiento en soles. Toda la
aritmética del asiento depende de una cifra: el tipo de cambio aplicado. Es la decisión de mayor
impacto económico del proyecto y hasta ahora no tenía ADR.

Lo que había, disperso:

- El **tipo de cambio venta** vivía en una línea con un paréntesis de nueve palabras dentro de ADR
  0006, sin alternativa considerada ni consecuencia declarada. El `PRD.md` dice **compra** cuatro
  veces, dos de ellas bajo "Confirmado".
- La **negativa a registrar con 0.00** vivía en `DECISIONES-REVISION.md`. El PRD pide lo contrario.
- El **tipo de cambio de la nota de crédito** no estaba en ninguna parte, lo que producía un residuo
  cambiario permanente en la cuenta por pagar.

"Confirmado" en el PRD significa que alguien lo decidió. Revertir una decisión confirmada sin dejar
rastro hace imposible, en la revisión formal de un contador, distinguir un cambio deliberado de un
error de transcripción.

## Decisión

### 1 · Tipo de cambio **venta**, no compra

Una compra genera un **pasivo** en moneda extranjera —una cuenta por pagar al proveedor—, y los
pasivos se convierten al tipo de cambio **venta**: es el precio al que la empresa tendría que
comprar la divisa para cancelar la deuda.

El tipo de cambio compra corresponde a los activos en moneda extranjera. Aplicarlo a una cuenta por
pagar subvalúa sistemáticamente el pasivo.

### 2 · Fecha de emisión del comprobante, congelado al confirmar

El tipo de cambio aplicable es el publicado para la **fecha de emisión** del comprobante, no la de
recepción ni la de registro. Al confirmar el asiento, la cifra se **congela** en él: deja de ser una
referencia viva a la tabla de tipos de cambio.

Es coherente con el principio de ADR 0006: un asiento confirmado es un hecho, no una vista.

### 3 · Sin tipo de cambio publicado, la factura no se abre para edición

Si no existe fila de tipo de cambio para la fecha de emisión, la factura en moneda extranjera **no se
abre para edición** y la API responde `409`.

El PRD pedía registrar `0.00` con una observación. Un asiento con tipo de cambio `0.00` es basura
contable: cuadra —todo es cero— y no representa nada. Y no hace falta, porque el caso real está
resuelto por otra vía: cuando la SBS no publica, se **carga manualmente** la fila con
`Origen = "MANUAL"` y recién entonces la factura se abre.

La SBS publica por las noches, y lo publicado el viernes cubre sábado, domingo y lunes. El caso de
"fin de semana sin tipo de cambio" no existe; el de "la SBS no publicó" sí, y tiene salida.

### 4 · La nota de crédito hereda el tipo de cambio de la factura que rectifica

Una nota de crédito de tipo `07` con referencia interna **no calcula** su tipo de cambio: copia el
congelado de la factura referenciada.

```
Nota de crédito sobre la factura F:

    TC aplicado = F.TipoCambio        (el congelado al confirmar F)
    NO el TC venta de la fecha de emisión de la NC
```

Sin esta regla, aplicando la del punto 2, una nota que anula el **100%** de una factura en dólares
**no deja el pasivo en cero**. Deja

```
residuo = totalOrig × (TCventa_NC − TCventa_factura)
```

colgado entre la cuenta por pagar, la cuenta de cargo heredada y las cuentas de destino. Con tres
milésimas de movimiento sobre USD 10 000 son S/ 30 por proveedor, para siempre. Y ningún control lo
atrapa: el asiento de la nota cuadra perfectamente consigo mismo, porque **el descuadre es entre dos
asientos** y ninguna invariante mira ese par.

La regla es además coherente con lo que la nota ya hace: hereda el motivo, la cuenta de cargo y —tras
la revisión v2— las cuentas de destino congeladas. El tipo de cambio es el cuarto atributo heredado,
no una excepción.

**La nota con referencia externa** —contra una factura anterior al sistema— no tiene de quién
heredar, y aplica la regla general del punto 2.

## Alternativas consideradas

- **Tipo de cambio compra, como pide el PRD.** Se descarta por el fundamento del punto 1: es el
  criterio para activos, no para pasivos. Si un contador determinara lo contrario, la corrección no
  es un ajuste de código: es **reprocesar todo asiento en moneda extranjera ya confirmado**.
- **Registrar con `0.00` y corregir después, como pide el PRD.** Se descarta porque produce asientos
  que cuadran y no significan nada, y porque el caso que motivaba la regla ya tiene salida real —la
  carga manual—. Además obligaría a un flujo de corrección masiva el día que la SBS publica tarde.
- **La nota de crédito con el tipo de cambio de su propia fecha.** Es la lectura literal de la regla
  general y probablemente lo que exige la norma si la nota se declara como comprobante propio. Se
  descarta porque arrastra consigo la línea de ajuste por diferencia de cambio y su cuenta, es decir,
  reabre un alcance que `REGLAS.md` §1 cerró deliberadamente. **Es el punto más discutible de este
  ADR** y está en la lista de ratificación pendiente.
- **Tipo de cambio de la fecha de recepción del correo.** Se descarta: no tiene ningún fundamento
  contable y hace que el importe del asiento dependa de cuándo el proveedor envió el correo.

## Consecuencias

- Las cuatro reglas cambiarias viven en **un solo documento**, que es la lectura completa para el
  contador que tiene que ratificarlas. `REGLAS.md` §6 las referencia en vez de duplicarlas.
- **Dos de las cuatro están pendientes de ratificación formal** y así consta en `REGLAS.md` §12: el
  tipo de cambio venta (punto 1) y la herencia en la nota de crédito (punto 4).
- El punto 3 hace que **una factura en moneda extranjera pueda quedar bloqueada** por una causa
  externa al sistema. Es deliberado: es preferible a un asiento falso. La salida —carga manual— debe
  estar operativamente disponible, no solo escrita.
- El punto 4 hace que la regla de rechazo por falta de tipo de cambio **no aplique al tipo `07`** con
  referencia interna: la nota hereda uno que ya existe.
- Los puntos 1, 2 y 3 **revierten al PRD**, que queda actualizado con la tabla de reversiones.

## Relacionado

- ADR 0006 — el asiento como entidad propia; congelamiento al confirmar.
- `REGLAS.md` §5, §6, §7 y §12.
- `PRD.md`, sección de reversiones.
