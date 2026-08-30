# ADR 0021: Generación de archivos Excel en la API

## Estado

Aceptado. Revisión 1. Ratificado por el dueño del proyecto el 2026-08-30, junto con el cierre del
ítem #22 (BACKLOG), que es el primero en pedir que la API entregue un archivo y no un JSON. ADR 0002 fijó el stack por componente y ADR 0008 fijó la forma de los contratos;
ninguno de los dos dice qué ocurre cuando el diseño de interfaz pide un `.xlsx` real, que es un
formato que .NET no sabe escribir de fábrica. Este ADR no reabre esas decisiones: añade la primera
dependencia de generación de archivos del backend y, sobre todo, **acota dónde puede vivir**.

## Contexto

El *canvas* de diseño (`handoff/Gestor de Facturas.dc.html`, líneas 1041 y 1091) coloca un botón
`Exportar a Excel` en las pantallas de `Proveedores` y de `Plan contable`, y el dueño extendió el
alcance a las tres pantallas de catálogo del ítem #22. La decisión del dueño fue explícita: un `.xlsx`
real generado por el servidor, no un CSV renombrado ni una exportación armada en el navegador.

De ahí salen cuatro huecos que no se pueden resolver en silencio dentro de un módulo:

1. **.NET no tiene escritor de OOXML en la biblioteca base.** Un `.xlsx` es un paquete ZIP con varias
   partes XML relacionadas (`workbook.xml`, `sheet1.xml`, la tabla de cadenas compartidas, las
   relaciones). Escribirlo a mano sin biblioteca es posible y es exactamente el tipo de código que
   falla de forma silenciosa: Excel se niega a abrir el archivo y no dice por qué. Hace falta una
   dependencia nueva.

2. **Sería la primera dependencia de este tipo en el backend.** El proyecto ha sido deliberadamente
   delgado en dependencias: ADO directo en vez de un ORM (ADR 0016 versiona SQL plano, sin
   migraciones de EF Core ni Alembic), sin librería de estado en la SPA (ADR 0009), sin librería de
   iconos —los glifos de la barra lateral son `div` y pseudo-elementos—. `SmartNet.Api.csproj` no
   tiene hoy **ninguna** `PackageReference`: solo referencias a proyectos. Añadir la primera no es un
   detalle de implementación.

3. **ADR 0019 mantiene el núcleo contable libre de infraestructura, y una dependencia mal colocada lo
   rompe por transitividad.** Si el paquete entra por `SmartNet.Api.csproj` o por un
   `*.Infrastructure` que un `*.Core` referencie, la frontera deja de ser una promesa verificable.
   `PurityScanTests` vigila el núcleo, pero vigila código, no el grafo de paquetes.

4. **Un archivo descargable es una superficie nueva.** El sistema hasta ahora solo devolvía JSON (y
   el contenido de documentos ya recibidos, ADR 0013). Entregar un archivo generado introduce dos
   preguntas que no existían: qué extensión se emite, y de dónde sale el nombre que viaja en
   `Content-Disposition`.

## Decisión

### 1 · La biblioteca es `DocumentFormat.OpenXml` 3.x, y es la única

Licencia MIT, mantenida por Microsoft, **un solo paquete** sin grafo transitivo de terceros. Es el
SDK de OOXML de primera parte: la misma pieza sobre la que se apoyan las alternativas envolventes.

La versión se fija de forma exacta en el `.csproj` del proyecto que la usa, siguiendo la convención
vigente —no existe `Directory.Packages.props` ni `Directory.Build.props` en el repositorio, y cada
proyecto declara su pin (por ejemplo `Microsoft.Data.SqlClient` 7.0.2 en
`SmartNet.Catalogos.Infrastructure`).

**Es la única dependencia de generación de archivos que el backend adquiere.** Un formato futuro
—PDF, CSV— se resuelve dentro del proyecto de la decisión 2, o abre su propio ADR; no se añade una
segunda biblioteca en paralelo.

### 2 · Vive en un proyecto propio, `SmartNet.Exportacion.Infrastructure`, y ningún `*.Core` la ve

```
SmartNet/SmartNetApi/exportacion/SmartNet.Exportacion.Infrastructure/   ← la PackageReference vive aquí
SmartNet/SmartNetApi/exportacion/SmartNet.Exportacion.Infrastructure.Tests/
```

`SmartNet.Api` la alcanza por `ProjectReference`. El proyecto expone una sola pieza —un escritor que
recibe un `Stream`, una secuencia de filas y una descripción de columnas— y no conoce ninguna regla
contable: exportar es una **proyección de solo lectura de un resultado de consulta que ya existía**,
no una decisión de negocio. Por eso no hay `SmartNet.Exportacion.Core` y no hay puerto nuevo en
ningún núcleo.

La frontera se prueba, no se comenta: una prueba estructural afirma que ningún proyecto `*.Core`
referencia el paquete, ni directa ni transitivamente —el mismo mecanismo con el que la solución ya
impide que la API alcance `SmartNet.Db.Runner`—.

Se coloca en un proyecto nuevo y no en `SmartNet.Catalogos.Infrastructure` porque ese proyecto son
adaptadores SQL sobre `dbo.*` (ADR 0003) y porque la exportación de tipo de cambio, que pertenece a
otro módulo, tendría que referenciarlo para nada más que escribir un XML.

### 3 · El archivo se arma en memoria, porque un `.xlsx` no se puede transmitir en flujo

Conviene decirlo sin adornos, porque el alcance del ítem lo pedía "en streaming" y **no se puede**: un
`.xlsx` es un paquete ZIP, y crear un paquete exige un `Stream` con capacidad de posicionamiento
(*seek*). El cuerpo de una respuesta HTTP no la tiene. El archivo se escribe entero en un
`MemoryStream` y después se entrega.

Lo que sí se acota es el pico. Se escribe con el escritor secuencial de la biblioteca —fila a fila,
sin construir el árbol completo del libro en memoria—, de modo que el peor caso del sistema
(`dbo.Proveedor`, ~6.600 filas y tres columnas cortas) queda en un **pico de ~5–10 MB** en vuelo para
un archivo de ~200 KB. El plan contable y el histórico de tipo de cambio (acotado a 366 días por dos
orígenes, ≤732 filas) quedan muy por debajo.

Consecuencia operativa de armar en memoria: **toda la validación de parámetros ocurre antes del
primer byte**, porque el código de estado ya no se puede cambiar una vez empezada la respuesta. A
cambio, la respuesta lleva un `Content-Length` real, que una respuesta troceada no tendría.

El límite queda declarado: este diseño es válido para catálogos de este tamaño. Si `dbo.Proveedor`
superara el orden de 100.000 filas, la exportación deja de ser una petición síncrona y pasa a ser un
trabajo en segundo plano —y eso es otro ADR, no un ajuste dentro de este—.

### 4 · Se emite `.xlsx`, nunca `.xlsm`, y el nombre del archivo no lo escribe el usuario

Dos reglas, ambas verificables con una prueba:

- **La extensión es `.xlsx`**, el formato sin macros, con el tipo de contenido
  `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` y `Content-Disposition:
  attachment`. `.xlsm` —el formato que admite macros— no se emite en ninguna ruta. Los bytes los
  genera el servidor a partir de filas de la base de datos; nunca se devuelve contenido subido por
  nadie.
- **Ningún dato de entrada del usuario llega a `Content-Disposition`.** El nombre se compone de una
  constante por catálogo más la fecha del servidor (`proveedores-2026-08-30.xlsx`). Los parámetros de
  la consulta (`q`, `orden`) filtran y ordenan filas, y ahí termina su alcance. Dejar que `q` entre
  en la cabecera sería una vía de inyección de cabeceras y de recorrido de rutas por un valor que
  viaja en la URL. La prueba correspondiente pide una exportación con un `q` hostil y afirma que la
  cabecera devuelta es la forma constante.

La fecha del nombre sale del `TimeProvider` ya registrado en el contenedor. El *endpoint* puede leer
el reloj —es el anfitrión HTTP—; el núcleo no (ADR 0019).

## Alternativas consideradas

- **`ClosedXML`.** Es la opción que el planteamiento inicial nombraba, tiene licencia MIT y una API
  mucho más agradable. Se descarta por dos motivos que se refuerzan: envuelve **este mismo** SDK y le
  suma `SixLabors.Fonts`, `ExcelNumberFormat` y `XLParser`, de modo que la primera dependencia del
  anfitrión se convierte en cuatro o más; y construye el libro completo en memoria antes de
  serializarlo, con un pico mayor que el escritor secuencial. Lo que se compra con eso son unas
  ochenta líneas menos de código propio, en un proyecto que ya eligió ADO sobre un ORM y glifos CSS
  a mano sobre una librería de iconos. **Es el punto más discutible de este ADR**: si en algún
  momento se necesitan hojas con formato, fórmulas o estilos condicionales, escribirlo con el SDK
  crudo deja de ser razonable y esta decisión debe revisarse.
- **`EPPlus` versión 5 o posterior.** Es la biblioteca más conocida del ecosistema. **Se descarta por
  licencia, no por técnica**: desde la versión 5 se distribuye bajo Polyform Noncommercial, que exige
  licencia de pago para uso comercial. Esto es el libro de compras de una empresa; el uso es
  comercial por definición. No es una alternativa que se pueda tomar "por ahora".
- **`MiniExcel`.** Apache-2.0, muy pequeña y con escritura en flujo. Se descarta porque no ofrece
  ninguna ventaja sobre el SDK de primera parte —el pico de memoria ya queda acotado por la decisión
  3— y sí tiene una base de mantenimiento notablemente menor.
- **Escribir el OOXML a mano, sin dependencia.** Mantendría el backend en cero paquetes. Se descarta
  porque el modo de fallo es el peor posible: un paquete mal formado produce un archivo que Excel se
  niega a abrir sin explicar la causa, y la prueba que lo detecta necesitaría de todas formas un
  lector de OOXML.
- **Devolver CSV en vez de `.xlsx`.** Cero dependencias y una implementación trivial. Se descarta
  porque el dueño pidió un `.xlsx` real, y porque un CSV con códigos de cuenta y RUC se abre en Excel
  con las columnas convertidas a número y los ceros a la izquierda perdidos —que es justamente el
  dato que un catálogo contable no puede perder—.
- **Generar el archivo en la SPA.** Evitaría por completo la dependencia del backend. Se descarta
  porque solo puede exportar lo que el navegador ya tiene, y la decisión del dueño es exportar el
  **conjunto filtrado completo**, no la página visible: en `Proveedores` eso significaría descargar
  las ~6.600 filas al cliente para volver a escribirlas. Además trasladaría la dependencia al
  paquete de la SPA, donde pesa en el presupuesto de bundle.
- **Poner la `PackageReference` directamente en `SmartNet.Api.csproj`.** Es más barato: ni proyecto
  nuevo ni entradas en la solución. Se descarta porque el código de armado del libro acabaría en el
  archivo del *endpoint*, y ADR 0019 exige del anfitrión precisamente lo contrario —enlazar, validar
  y delegar—; y porque deja la frontera del paquete sin un lugar donde probarla.

## Consecuencias

- El backend adquiere su primera dependencia de generación de archivos y la solución un proyecto más.
  Es un costo real y declarado; a cambio, la frontera queda en un solo punto del grafo de proyectos.
- Ningún `*.Core` puede ver el paquete, y eso deja de depender de la disciplina del siguiente
  colaborador: hay una prueba estructural que falla si alguien lo referencia.
- **Costo:** escribir con el SDK crudo es más verboso que con una envoltura. Se acepta a cambio del
  grafo de dependencias mínimo, y se acota escribiendo el escritor una sola vez para las tres
  exportaciones.
- **Costo:** la exportación consume memoria proporcional al catálogo. Queda acotada por el tamaño
  actual de los datos, no por el diseño; el punto de ruptura (~100.000 filas) está declarado y su
  salida es un trabajo en segundo plano con su propio ADR.
- Las rutas de exportación repiten la consulta de su listado sin paginar. En `Proveedores` eso es el
  **mismo** barrido con orden que una página ya paga —no uno adicional—, porque `dbo.Proveedor` no
  admite índice (ADR 0003); el plan contable ya se sirve sin paginar y el tipo de cambio es una
  búsqueda por clave primaria acotada a 366 días.
- La exportación honra el filtro **y** el orden visibles, para que el archivo no llegue ordenado de
  una forma que el usuario no pidió. Consecuencia menor: el predicado de filtro del plan contable
  queda expresado dos veces —en la SPA, para el filtrado inmediato, y en SQL, para la exportación—.
  Se acepta porque es una regla de una línea y se afirma en ambos lados.
- Las tres rutas exigen sesión y devuelven 401 sin ella. La descarga la hace la SPA con su cliente
  HTTP y no abriendo una pestaña: una pestaña con 401 muestra una página en blanco y esquiva la
  redirección por sesión vencida.
- **Costo:** el proyecto de pruebas de la API pasa a referenciar el mismo paquete, solo para volver a
  abrir los bytes devueltos y afirmar que son un libro válido. Es una dependencia de prueba, no de
  producción, pero existe.
- Este ADR no toca el esquema, no concede permisos y no escribe en ninguna tabla. La exportación es
  `SELECT` sobre lo que `usr_api` ya puede leer (ADR 0003, ADR 0016).

## Relacionado

- ADR 0002 — el stack por componente al que este ADR añade su primera dependencia de archivos.
- ADR 0003 — la partición de propiedad de datos: la exportación es `SELECT` sobre catálogos externos,
  sin escritura ni índice nuevo sobre `dbo.*`.
- ADR 0008 — los contratos de API; las tres rutas de exportación son subrecursos explícitos y no un
  *endpoint* genérico parametrizado, que ese ADR ya descartó por dejar el contrato no inspeccionable.
- ADR 0009 — los *signals* sin librería de estado, el mismo criterio de delgadez que sostiene la
  elección de biblioteca.
- ADR 0012 — el mismo origen para SPA y API, que es lo que permite a la SPA leer
  `Content-Disposition`.
- ADR 0013 — el único precedente de entrega binaria del sistema (contenido de documentos ya
  recibidos), frente al que este ADR introduce el primer archivo **generado**.
- ADR 0016 — el esquema versionado que este ADR **no** modifica.
- ADR 0019 — el núcleo contable sin infraestructura, y los niveles de verificación que la prueba
  estructural de la decisión 2 hace cumplir.
- `BACKLOG.md` ítem #22 (consultas de catálogos), que motiva este ADR.
