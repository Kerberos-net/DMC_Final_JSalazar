# ADR 0006: El asiento contable como entidad propia con relación 1:1 a la factura

## Estado

Aceptado

## Contexto

El PRD define que la cabecera del asiento contable contiene número de comprobante, origen del libro
(por defecto `02 Compras`), proveedor, glosa, fecha contable, tipo de cambio, base imponible, IGV y
neto, y que el detalle está formado por las líneas contables en débito/crédito. Casi todos los
campos de la cabecera existen ya en la factura o derivan de ella, lo que plantea si el asiento
justifica ser una entidad independiente o si basta con tratarlo como una proyección de la factura
más sus líneas.

Dos requisitos del PRD inclinan la decisión:

- El asiento generado **puede editarse o anularse después de creado**, y el prototipo de pantallas
  incluye acciones explícitas de "Anular asiento" y "Reactivar asiento" que no revierten la
  validación de la factura. Existe por tanto un estado legítimo en el que la factura está validada y
  su asiento anulado.
- La fecha contable es **editable e independiente de la fecha de emisión de la factura**, y el PRD
  exige señalar inconsistencias entre cabecera y detalle (por ejemplo, base imponible más IGV que no
  cuadra con el neto), lo que supone poder validar el cuadre sobre el conjunto del asiento.

## Decisión

El asiento contable se modela como una **entidad propia con relación 1:1 con la factura**:

- `AsientoContable` — cabecera del asiento, con sus propios atributos: número de comprobante, origen
  del libro, proveedor, **glosa**, **fecha contable**, tipo de cambio, base imponible, IGV, neto y
  estado del asiento (`GENERADO` / `ANULADO`).
- `AsientoContableDetalle` — líneas del asiento, cada una con su cuenta contable y su importe en
  débito o crédito.

Los importes y el tipo de cambio se registran en la cabecera del asiento como valores **congelados
en el momento de la contabilización**, no como referencias vivas a la factura. La glosa y la fecha
contable son atributos del asiento, no de la factura. Ambas entidades pertenecen al contexto de
negocio, propiedad exclusiva de .NET (ADR 0003).

## Alternativas consideradas

- **Usar la propia `Factura` como cabecera del asiento** — `AsientoContableDetalle` referenciaría
  directamente a `Factura`, y `glosa`, `fechaContable` y el estado del asiento se añadirían como
  columnas de la factura. Evitaba toda duplicación de importes y reducía el listado "Registro de
  compra" a una consulta directa sobre una sola tabla. Se descartó porque fusiona dos ciclos de vida
  que el PRD trata como distintos: anular el asiento pasaría a ser un campo dentro de la entidad
  factura, y la factura acumularía atributos que conceptualmente pertenecen al asiento.
- **Permitir varios asientos por factura, con asientos de reversión** — En lugar de anular, se
  emitiría un asiento inverso, que es la práctica de la contabilidad formal y preserva un rastro
  contable estricto. Se descartó porque ni el PRD ni el prototipo lo piden: ambos describen anular y
  reactivar el mismo asiento, de modo que adoptar reversión resolvería un problema que este proyecto
  no tiene y complicaría la consulta del asiento vigente.

## Consecuencias

- Factura y asiento pueden evolucionar por separado: es representable el estado "factura validada
  con asiento anulado" sin forzar semánticas artificiales sobre el estado de la factura.
- Los importes con los que se contabilizó quedan congelados en el asiento y sobreviven a
  correcciones posteriores de la factura, lo que preserva la fidelidad del registro contable.
- La validación de cuadre entre débitos y créditos, y la detección de inconsistencias entre cabecera
  y detalle que exige el PRD, se aplican sobre un agregado bien delimitado.
- La fecha contable como atributo propio permite contabilizar en un periodo distinto al de emisión
  de la factura, que es el comportamiento que el PRD describe.
- **Costo:** base imponible, IGV, neto y tipo de cambio existen en `Factura` y en `AsientoContable`.
  Es duplicación deliberada con semántica distinta —dato del documento frente a dato
  contabilizado— pero exige explicar por qué ambos pueden diferir legítimamente y evitar que se
  intenten "sincronizar".
- **Costo:** el listado "Registro de compra" requiere unir dos entidades en lugar de consultar una
  sola tabla.
- **Costo:** la creación del asiento añade un paso transaccional adicional tras la validación de la
  factura, y hay que definir qué ocurre si ese paso falla dejando una factura validada sin asiento.
