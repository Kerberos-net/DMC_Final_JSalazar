# ADR 0009: Manejo de estado en el frontend Angular

## Estado

Aceptado

## Contexto

La ADR 0001 define la interfaz como una SPA independiente y la ADR 0002 fija Angular como su
tecnología. El conjunto de pantallas derivado del PRD y del prototipo de `handoff/` comprende
bandeja principal con filtros y contadores, detalle y validación de factura, registro de compra con
el detalle del asiento, catálogos de proveedores y plan contable en solo lectura, panel de errores y
configuración.

El estado realmente compartido entre vistas es limitado: el usuario en sesión y algunos filtros. La
mayor parte de lo que muestran las pantallas son datos que residen en la base de datos y que se
consultan a la API de negocio.

Existe además una particularidad: la bandeja de facturas cambia por causas ajenas a la interacción
del usuario, porque el worker Python ingesta correos y promueve nuevas facturas de forma continua.
El PRD fija como criterio de éxito que una factura recibida sea visible en el software en un máximo
de 15 minutos.

## Decisión

Se adopta **Angular Signals con `HttpClient` y servicios por dominio**, sin librería global de
gestión de estado.

- El **backend es la fuente de verdad** de las entidades de negocio. El frontend no mantiene una
  réplica autorizada del dominio.
- Los signals administran **estado de presentación**: filtros, usuario en sesión, resultados de
  consultas y estado de carga y error de cada operación.
- No se introduce inicialmente NgRx ni ninguna otra librería global de estado, dado que el estado
  compartido entre vistas es limitado y la mayoría de los datos provienen del servidor.
- Para los datos susceptibles de cambiar por procesos externos —en particular la bandeja de facturas
  alimentada por el worker Python— se implementan mecanismos de *refetch* y sondeo según la
  necesidad de cada vista. Queda contemplada la incorporación posterior de SignalR para
  actualización en tiempo real, sin que sea un requisito inicial.
- Si la aplicación desarrolla necesidades significativas de caché, invalidación y sincronización de
  estado de servidor, se evaluará entonces una solución específica para ese propósito, sin
  convertirla en un requisito inicial de la arquitectura.

## Alternativas consideradas

- **NgRx (store, actions, effects, selectors)** — Aporta una estructura impuesta y uniforme, gran
  trazabilidad de las transiciones de estado y herramientas de depuración maduras, lo que resulta
  valioso en aplicaciones grandes con varios desarrolladores. Se descartó por desproporcionado a
  este tamaño: exigiría una acción, un reducer, un efecto y un selector por operación sobre datos
  que apenas se comparten entre vistas, añadiendo ceremonia sin resolver ningún problema real de un
  proyecto de un solo desarrollador y un solo usuario.
- **Signals junto con una capa de caché de estado de servidor** (patrón de consulta con caché,
  invalidación y *refetch* automático) — Atacaba directamente el problema del refresco de la
  bandeja y evitaría implementar el sondeo a mano en cada vista. Se descartó como requisito inicial
  porque implica una dependencia adicional o construir esa capa desde cero antes de saber si el
  patrón de uso la justifica; queda explícitamente abierta para evaluarse más adelante.

## Consecuencias

- La arquitectura del frontend queda proporcionada al problema: no se construye un aparato de
  gestión de estado para administrar, sobre todo, copias de estado que vive en el servidor.
- No se añaden dependencias externas al frontend, y el enfoque coincide con la dirección que Angular
  moderno favorece, lo que reduce la fricción de mantenimiento.
- La separación es explícita: el estado de presentación vive en signals y el estado de dominio vive
  en el backend, lo que evita la ambigüedad sobre quién manda cuando ambos difieren.
- La incorporación posterior de NgRx, de una capa de caché o de SignalR sigue siendo posible de
  forma incremental, sin rehacer lo construido.
- **Costo:** la consistencia entre servicios de dominio depende de la disciplina del desarrollador,
  no de una estructura impuesta por la librería; sin una convención escrita, cada servicio puede
  acabar resolviendo lo mismo de forma distinta.
- **Costo:** el refresco de datos alterados por procesos externos debe implementarse manualmente por
  vista, y el sondeo introduce peticiones periódicas que consumen recursos aunque no haya cambios.
- **Costo:** al no existir una caché compartida, distintas vistas pueden mostrar simultáneamente
  versiones ligeramente distintas del mismo dato hasta que cada una refresque.
- **Costo:** sin un registro centralizado de transiciones de estado, depurar un comportamiento
  inesperado de la interfaz exige rastrear el servicio implicado en lugar de revisar un historial
  único de acciones.
