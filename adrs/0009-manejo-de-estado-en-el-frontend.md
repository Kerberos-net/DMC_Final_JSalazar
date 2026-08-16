# ADR 0009: Manejo de estado en el frontend Angular

## Estado

Aceptado. Cambios menores respecto de la versión previa (`adrs - v1/0009`): se añade la
consecuencia de que la bandeja llega ya combinada desde el servidor.

## Contexto

La SPA tiene un usuario, siete pantallas y un estado modesto: la bandeja con sus filtros, la
factura en edición junto a su asiento en borrador, los catálogos en solo lectura, el panel de
errores y la configuración.

El estado compartido real es pequeño. Lo que sí es exigente es la **pantalla de detalle**, donde
factura y asiento se editan a la vez y el cuadre debe recalcularse en cada cambio de importe o de
línea.

## Decisión

**Signals de Angular**, sin librería de estado externa.

- El estado del servidor vive en servicios `providedIn: 'root'`, con signals privados escribibles y
  su exposición pública mediante `asReadonly()`.
- Los derivados —cuadre del asiento, sumas de debe y haber, contadores de la bandeja, validez del
  formulario— se calculan con `computed()`, nunca se almacenan.
- Los filtros de la bandeja son signals; la consulta se deriva de ellos.
- `effect()` se reserva para efectos colaterales reales, nunca para calcular estado derivado.

El componente de detalle es el único con estado local de peso, y se sincroniza con el servidor
mediante "Guardar avance" (ADR 0006).

## Alternativas consideradas

- **NgRx.** Es el estándar de facto en Angular para estado complejo, con *devtools*, trazabilidad de
  acciones y un patrón conocido. Se descartó por desproporcionado: para un desarrollador y un
  usuario, la ceremonia de acciones, reducers y efectos por cada operación cuesta más de lo que
  aporta, y el estado compartido que justificaría ese andamiaje no existe aquí.
- **Servicios con `BehaviorSubject` y RxJS.** Es el patrón previo a signals y funciona bien. Se
  descartó porque signals expresa mejor el estado derivado —el cuadre del asiento es exactamente un
  `computed`— y porque evita las suscripciones manuales y su gestión de baja.

## Consecuencias

- El cuadre del asiento y los contadores de la bandeja se recalculan solos al cambiar una línea, sin
  código de sincronización.
- Menos superficie que aprender y mantener para un desarrollador solo.
- La aplicación es compatible con detección de cambios sin zonas.
- **Costo:** sin *devtools* de estado, depurar un estado inconsistente es leer código en vez de
  inspeccionar una traza de acciones.
- **Costo:** la disciplina de no exponer signals escribibles desde los servicios es una convención;
  nada la fuerza salvo la revisión.
- **Consecuencia de ADR 0003:** la bandeja llega **ya combinada** desde el servidor. Angular no une
  facturas con incidencias de procesamiento ni documentos con adjuntos manuales: consume vistas
  resueltas. Eso mantiene el estado del frontend deliberadamente simple y es la razón por la que
  esta decisión se sostiene pese a que el modelo del servidor sí es complejo.
- **Riesgo abierto:** el criterio del PRD de que la bandeja refleje facturas nuevas sin recargar
  depende de un intervalo de sondeo del cliente que aún no se ha fijado.
