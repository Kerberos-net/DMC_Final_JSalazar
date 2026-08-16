# ADR 0001: Componentes del sistema

## Estado

Aceptado

## Contexto

El PRD describe dos naturalezas de trabajo con perfiles de ejecución muy distintos:

- **Interactiva**: el asistente contable revisa la bandeja, valida facturas y ajusta las cuentas de
  las líneas del asiento. Son operaciones cortas que deben responder de inmediato.
- **Asíncrona, sin usuario presente**: detección de correos en Gmail (criterio de éxito: la factura
  debe estar visible en el software en ≤15 minutos desde la llegada del correo), descarga de
  adjuntos, extracción OCR/IA (segundos por documento), consulta del tipo de cambio en la web de la
  SBS, creación de carpetas en Google Drive, sincronización hacia Google Sheets y envío de
  notificaciones por Telegram/correo.

El PRD impone además un modelo de fallos explícito para el trabajo asíncrono: reintento automático
hasta 3 veces ante fallas de conexión, marcado del registro como "Error" cuando se agotan los
reintentos, y envío de la notificación en un máximo de 5 minutos con una tasa de entrega ≥99%. Eso
exige estado persistente y un ciclo de vida propio por cada trabajo pendiente.

Restricciones reales: equipo de una sola persona, un único usuario concurrente, volumen de 10 a 50
facturas diarias y sin fecha límite fija. La escala no es el factor determinante; el aislamiento del
modelo de fallos sí lo es.

## Decisión

El sistema se compone de **tres componentes desplegables sobre una base de datos compartida**:

1. **Aplicación web (SPA)** — interfaz de usuario del asistente contable: bandeja principal,
   validación de facturas, consulta y edición del asiento contable, catálogos en solo lectura,
   panel de errores y configuración. Consume la API del backend; no accede a la base de datos.
2. **API de negocio (backend)** — dueña del dominio contable y de la seguridad. Expone la API que
   consume la SPA, aplica las reglas de negocio (validación de factura, generación del asiento,
   trazabilidad de correcciones, detección de duplicados) y es el único componente que escribe las
   tablas de negocio y de seguridad.
3. **Worker de procesamiento e integraciones** — proceso de fondo que ejecuta todo el trabajo
   asíncrono contra sistemas externos: ingesta desde Gmail, descarga de adjuntos, extracción
   OCR/IA de PDF/XML, consulta del tipo de cambio en la SBS, creación de carpetas en Google Drive,
   sincronización hacia Google Sheets y envío de notificaciones por Telegram y correo, junto con la
   política de reintentos de todas ellas.

La coordinación entre la API de negocio y el worker se hace exclusivamente a través de la base de
datos compartida, con propiedad de tablas estrictamente separada (ver ADR 0003) y una tabla de
salida transaccional para las integraciones disparadas por eventos de negocio (ver ADR 0004). No
hay llamadas directas entre la API de negocio y el worker.

## Alternativas consideradas

- **Monolito único** — Un solo proceso desplegable que contuviera la interfaz, la API y el
  planificador de trabajos de fondo. Era viable en capacidad: 50 facturas diarias con un usuario no
  exigen separación por rendimiento, y reduce el despliegue y el monitoreo a una sola pieza, lo cual
  es atractivo para un desarrollador solo. Se descartó porque la extracción OCR/IA y los reintentos
  con espera compartirían proceso con la atención de la interfaz, y porque un fallo o reinicio del
  trabajo de fondo dejaría inaccesible también la aplicación web.
- **Servicios separados por integración** (uno para Gmail, otro para OCR, otro para Drive, etc.) —
  Ofrecía el aislamiento máximo por dependencia externa. Se descartó por desproporcionado: para un
  único usuario y 50 facturas diarias multiplica despliegues, latencia entre servicios y puntos de
  fallo sin resolver ningún problema que el esquema de tres componentes no resuelva ya.

## Consecuencias

- La extracción OCR/IA y los reintentos de red nunca degradan la respuesta de la interfaz: el
  usuario puede seguir validando facturas mientras el worker procesa la ingesta.
- El worker puede reiniciarse, detenerse o desplegarse de forma independiente sin interrumpir el
  acceso del usuario, y viceversa.
- El estado de cada trabajo (pendiente, en curso, reintento n, error) queda persistido, lo que hace
  directamente verificable el requisito de "3 reintentos y luego notificar" y permite alimentar el
  panel de errores del PRD.
- **Costo:** hay tres artefactos que construir, desplegar y monitorear en lugar de uno, lo que
  multiplica la superficie operativa para un equipo de una sola persona.
- **Costo:** la base de datos compartida es el punto de acoplamiento entre la API de negocio y el
  worker; un cambio de esquema en la frontera afecta a ambos y debe desplegarse de forma coordinada.
- **Costo:** al ser la SPA un artefacto separado, se necesita configuración explícita de CORS,
  autenticación por token y una estrategia de publicación del frontend que un servidor de plantillas
  no habría requerido.
