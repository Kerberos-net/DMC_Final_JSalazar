# ADR 0003: Partición de propiedad de datos entre .NET y Python

## Estado

Aceptado

## Contexto

La ADR 0001 estableció que la API de negocio (ASP.NET Core) y el worker de procesamiento (Python) se
coordinan a través de una base de datos SQL Server compartida, sin llamadas directas entre ellos.
Una base de datos compartida entre dos runtimes es un riesgo conocido: si ambos escriben libremente
sobre las mismas entidades, se duplican las reglas de negocio en dos lenguajes, se pierde el punto
único donde se validan las invariantes contables y aparecen inconsistencias difíciles de rastrear.

El sistema, sin embargo, tiene dos responsabilidades claramente distinguibles:

- **Integración**: qué correos llegaron, qué archivos se descargaron, qué extrajo el OCR, cuántos
  intentos lleva una operación y qué error devolvió. Es información volátil, técnica y ligada a
  sistemas externos.
- **Negocio y seguridad**: qué facturas son válidas, qué proveedores existen, qué asientos contables
  se generaron y quién puede hacer qué. Es información contable, con invariantes que deben
  sostenerse en un único lugar.

Esa distinción permite compartir la base de datos física sin compartir la propiedad de los datos.

## Decisión

Cada componente es **propietario exclusivo de escritura** de un conjunto cerrado de tablas. Ningún
componente escribe sobre tablas de las que no es propietario; la lectura cruzada sí está permitida
en la dirección definida.

**Tablas propiedad de Python (contexto de integración):**

- `Email` — correos detectados en Gmail.
- `DocumentoRecibido` — adjuntos descargados (factura, orden de compra, medios probatorios).
- `Procesamiento` — estado del procesamiento de cada documento.
- `ProcesamientoError` — errores registrados durante el procesamiento.
- `ProcesamientoIntentos` — número y detalle de los reintentos de cada operación.
- `TipoCambio` — tipo de cambio consultado en la web de la SBS, con los campos `fecha`, `compra`,
  `venta` y `fechaConsulta`.

**Tablas propiedad de .NET (contexto de negocio y seguridad):**

- `Factura`, `FacturaDetalle` — facturas válidas y su detalle.
- `AsientoContable` (cabecera y detalle) — asientos generados.
- `Proveedor` — catálogo de proveedores.
- `Usuario`, `Rol`, `UsuarioRol` — seguridad y control de acceso.

**Direcciones de lectura permitidas:**

- .NET lee las tablas de integración de Python para promover un documento procesado a factura de
  negocio y para mostrar el estado de la ingesta y el panel de errores.
- .NET lee `TipoCambio` como tabla de publicación: Python la escribe a partir de la consulta a la
  SBS y .NET la consulta al calcular la conversión a soles del asiento contable.
- Python no lee ni escribe las tablas de negocio ni las de seguridad; recibe el trabajo que debe
  ejecutar a través de la tabla de salida transaccional definida en la ADR 0004.

## Alternativas consideradas

- **Escritura libre de ambos componentes sobre las mismas tablas de negocio** — Era la vía más
  directa: cada componente escribe donde le resulta cómodo, sin fronteras que definir ni mantener.
  Se descartó porque obligaría a duplicar las reglas contables en Python y en .NET y eliminaría el
  punto único de validación de invariantes; en un sistema contable ese es exactamente el origen de
  inconsistencias que después nadie puede explicar ni auditar.
- **Python como servicio HTTP sin estado, con .NET dueño de toda la persistencia** — Python expondría
  únicamente endpoints de extracción OCR y de consulta del tipo de cambio, y .NET orquestaría e
  insertaría todo el resultado. Habría dejado un único propietario de la base de datos, que es el
  modelo más simple de razonar. Se descartó porque trasladaría a .NET la orquestación de la ingesta,
  los reintentos y el estado del procesamiento —trabajo intrínsecamente ligado a las integraciones
  externas— rompiendo la asignación de responsabilidades por competencia del ecosistema establecida
  en la ADR 0002.
- **Dos bases de datos separadas, una por contexto** — Habría hecho la frontera físicamente
  imposible de violar. Se descartó por desproporcionado para el volumen del proyecto: obliga a
  sincronizar datos entre bases y a renunciar a consultas directas entre integración y negocio, a
  cambio de una garantía que la disciplina de propiedad de tablas ya ofrece a este tamaño.

## Consecuencias

- Existe un único propietario por entidad, por lo que las reglas contables viven solo en .NET y la
  lógica de integración y reintentos solo en Python; ninguna regla se implementa dos veces.
- La frontera es explícita y auditable: puede reforzarse a nivel de motor con esquemas separados y
  permisos por usuario de base de datos, de modo que una violación falle en el momento de escribir
  en lugar de corromper datos silenciosamente.
- El contexto de integración puede evolucionar (cambiar el motor OCR, añadir campos de diagnóstico,
  purgar histórico técnico) sin impacto alguno sobre el modelo contable.
- `TipoCambio` funciona como tabla de publicación con un solo escritor, lo que elimina condiciones
  de carrera en un dato del que depende directamente la conversión a soles del asiento.
- **Costo:** el esquema queda partido en dos contextos que deben mantenerse coordinados; un cambio
  en la frontera (por ejemplo, un campo nuevo que .NET necesita leer de `Procesamiento`) exige
  coordinar el despliegue de los dos componentes.
- **Costo:** la disciplina de propiedad no la garantiza el lenguaje, sino la convención y la
  configuración de permisos; sin ese refuerzo explícito en el motor, nada impide técnicamente que un
  componente escriba donde no debe.
- **Costo:** los datos de una misma factura viven repartidos entre dos contextos, por lo que
  reconstruir su historia completa (desde el correo hasta el asiento) requiere consultar tablas de
  ambos lados en lugar de una sola entidad.
