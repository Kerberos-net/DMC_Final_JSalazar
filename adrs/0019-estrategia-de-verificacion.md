# ADR 0019: Estrategia de verificación

## Estado

Aceptado. Revisión 1. Cubre el hueco estructural que la revisión adversarial v2 señaló como el mayor
del diseño (C8).

## Contexto

El TECH-DESIGN dedica cien líneas a criterios de aceptación por flujo, y son concretos y
verificables. **No hay ninguna decisión sobre cómo se verifican.**

El diseño lo reclama sin recogerlo:

- ADR 0006 dice que las invariantes del bloque principal *"son tres caminos que probar, no uno"*, y
  ahí se detiene.
- El TECH-DESIGN lista como riesgo abierto que *"convendrían pruebas de contrato sobre esas tablas"*
  — un deseo, no una decisión.
- ADR 0002 registra el riesgo de divergencia de tipos entre C# y Python; ADR 0016 dice mitigarlo con
  el SQL versionado; nada verifica esa mitigación.

Es desproporcionado con el resto del diseño. Un sistema cuyo núcleo es un puñado de invariantes
aritméticas sobre dinero —con conversión de moneda, redondeo, percepción, notas de crédito parciales
y contabilidad por destino generada automáticamente— no tiene decidido cómo se comprueba que suma
bien, mientras todo lo demás está decidido con detalle.

Y el mejor insumo de pruebas del proyecto **ya está escrito y sin usar**: `REGLAS.md` §10 trae cinco
ejemplos numéricos completos y cuadrados, y §7 define siete invariantes comprobables.

## Decisión

Tres niveles, que responden a tres riesgos distintos. Ninguno cubre al otro.

### 1 · Núcleo contable, sin infraestructura

La generación del asiento y la evaluación de las invariantes viven en un **núcleo sin dependencias de
base de datos, HTTP ni reloj**. Recibe como datos de entrada la factura, el plan de cuentas aplicable
y el tipo de cambio, y devuelve el asiento o el rechazo con su motivo.

Casos de referencia, en su mayoría ya escritos:

| Origen | Qué cubre |
|---|---|
| `REGLAS.md` §10 — cinco ejemplos | Gravada con destino · boleta con IGV al costo · dólares con redondeo derivado · con percepción · nota de crédito |
| `REGLAS.md` §7 — siete invariantes | Cada una en sus dos caminos: acepta y rechaza |
| `REGLAS.md` §8 — ocho reglas de rechazo | Una prueba por regla, con su salida esperada |

Casos que la revisión v2 hizo aparecer y hay que escribir:

- Nota de crédito **sobre boleta**: dos líneas, sin `401111` (A13).
- Nota de crédito **del 100% en moneda extranjera** con tipo de cambio distinto: saldo cero exacto
  (C9).
- Nota de crédito **parcial sobre factura con reparto en N cuentas**, incluido el céntimo residual
  (A2).
- Nota de crédito **con referencia externa**: se valida sin tope y con motivo elegido (A2).
- Factura con **`ctarefleja` cambiado** entre confirmar la factura y confirmar su nota: el espejo
  debe revertir contra la misma cuenta (A14).
- **Validación que falla dentro de la transacción**: no debe consumir correlativo (A1).
- **Traslado de periodo**: hueco en origen, número nuevo en destino (A1).
- **XML con dos códigos de afectación** → rechazo; **sin XML** → validación bloqueada hasta la
  confirmación del asistente (C11).
- **Anular el asiento de una nota parcial** debe liberar capacidad y permitir una nota que antes se
  rechazaba (C10).

El plan de cuentas real —1650 filas, 907 hojas de seis dígitos— entra como **dato fijo de prueba**,
no como consulta. Un cambio en el catálogo externo no puede romper la suite.

### 2 · Contrato de frontera, contra el esquema versionado

Pruebas sobre las **tablas de frontera** —`OutboxEvent`, `CommandQueue`, `InboxEvent`,
`Procesamiento` y la evidencia de extracción— ejecutadas contra el esquema que aplica la herramienta
de ADR 0016, y desde **ambos lados**: .NET escribe y Python lee, y a la inversa.

Es la única mitigación declarada del riesgo de divergencia de tipos. Sin esta prueba, esa mitigación
es una afirmación.

La prueba comprueba además la **matriz de permisos** de ADR 0003: que `usr_api` no puede leer
`Procesamiento` y que `usr_worker` no puede escribir `Factura` son invariantes verificables, y son la
propiedad más fuerte que ese ADR reivindica.

### 3 · Un extremo a extremo, sobre datos fijos

**Uno solo, no una suite.** Correo con adjuntos → ingesta → procesamiento → promoción → validación →
evento en el outbox, con un juego de correos de referencia fijo y comprobantes conocidos.

No verifica reglas contables —de eso se encarga el nivel 1— sino que **las piezas están conectadas**:
que el `InboxEvent` se consume, que la promoción crea la factura, que la validación emite el evento.
Es el único nivel que detecta un cableado roto.

## Alternativas consideradas

- **Solo el núcleo contable.** Es el nivel más barato y el que más riesgo cubre por unidad de
  esfuerzo. Se descarta como estrategia completa porque deja sin verificar la frontera entre dos
  runtimes que comparten esquema, que es la decisión estructural del proyecto.
- **Una suite de extremo a extremo por flujo.** Cubriría los cinco flujos del TECH-DESIGN con sus
  criterios de aceptación literales. Se descarta por costo de mantenimiento: es la parte de la
  pirámide que más cuesta sostener, y con un solo caso ya se detecta el cableado roto.
- **Probar la lógica contable a través de la API.** Cero refactorización previa. Se descarta porque
  haría que cada caso contable necesitara base de datos, sesión y transacción, y porque los cinco
  ejemplos de `REGLAS.md` son aritmética pura: probarlos a través de HTTP no añade información.

## Consecuencias

- **La lógica contable no puede vivir en el controlador ni en el repositorio.** El nivel 1 obliga a
  una separación que hoy el diseño no exige explícitamente. Es una restricción de arquitectura, y
  este ADR la impone.
- Los cinco ejemplos de `REGLAS.md` §10 pasan a ser **normativos**: si el código y el ejemplo
  discrepan, se corrige uno de los dos deliberadamente, nunca en silencio.
- El nivel 3 exige entorno con base de datos y volumen compartido. Al ser **uno solo**, el costo se
  acota; convertirlo en una suite es lo que hay que evitar, y merece quedar escrito.
- El nivel 2 solo tiene sentido si el esquema versionado se aplica igual en el entorno de prueba que
  en el de despliegue. Es una dependencia real de ADR 0016.
- **Costo:** la suite contable no verifica que las reglas sean las correctas, solo que están
  implementadas como se escribieron. La ratificación del contador que `REGLAS.md` §12 deja pendiente
  no la sustituye ninguna prueba.

## Relacionado

- `REGLAS.md` §7, §8, §10 y §12 — el insumo de casos.
- ADR 0003 — la matriz de permisos que el nivel 2 verifica.
- ADR 0016 — el esquema versionado contra el que corre el nivel 2.
