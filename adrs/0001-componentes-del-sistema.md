# ADR 0001: Componentes del sistema

## Estado

Aceptado. Reemplaza la versión previa (`adrs - v1/0001`), que declaraba "autenticación por token"
en sus consecuencias, contradiciendo a ADR 0007.

## Contexto

El sistema reemplaza el registro manual de facturas de compra descrito en el PRD. Debe atender a un
único usuario —el asistente contable— con un volumen de 10 a 50 facturas diarias, y combinar dos
naturalezas de trabajo muy distintas:

- **Trabajo transaccional síncrono.** Validar una factura, generar y confirmar su asiento, aplicar
  las invariantes contables y registrar la auditoría. Todo ocurre dentro de una transacción, con el
  usuario esperando la respuesta.
- **Trabajo asíncrono frente a terceros.** Sondear Gmail, descargar adjuntos, extraer datos por
  OCR o XML, raspar el tipo de cambio de la web de la SBS, crear carpetas en Drive, escribir en
  Sheets y notificar por Telegram y correo. Latencia variable, fallos frecuentes y reintentos.

Mezclar ambas en un solo proceso hace que un reintento de Drive compita por recursos con la atención
de la interfaz, y que un despliegue del dominio interrumpa una ingesta en curso.

Además, la SPA debe mostrar el documento escaneado junto al formulario de datos —el patrón central
que define `DESIGN_BRIEF.md`—, lo que obliga a servir bytes de archivo bajo la sesión del usuario.

## Decisión

El sistema se compone de **tres artefactos desplegables** sobre una base de datos SQL Server
compartida, servidos tras un **proxy inverso** que unifica el origen.

**1. SPA — Angular**
Interfaz del asistente contable: bandeja, detalle y validación de factura con el asiento embebido,
registro de compra, catálogos en solo lectura, panel de errores y configuración. No accede a la base
de datos; consume exclusivamente la API de negocio.

**2. API de negocio — ASP.NET Core**
Propietaria del dominio contable y de la seguridad. Aplica las reglas de negocio, es el único
componente que escribe las tablas de negocio y de seguridad, y sirve los documentos al visor de la
SPA. Aloja un servicio de fondo que consume el inbox de integración (ADR 0005).

**3. Worker de procesamiento e integraciones — Python**
Proceso de fondo que ejecuta todo el trabajo asíncrono contra sistemas externos: Gmail, extracción
OCR/XML, SBS, Drive, Sheets, Telegram y correo, junto con la política de reintentos de todas ellas.

**4. Proxy inverso**
Sirve la SPA compilada en `/` y enruta `/api` hacia Kestrel. Termina TLS. Es lo que permite que
SPA y API compartan origen (ADR 0007, ADR 0012).

### Comunicación

- **SPA ↔ API:** HTTP/JSON, mismo origen. Autenticación por cookie de sesión `HttpOnly` (ADR 0007).
- **API ↔ Worker:** no existe contrato de red. La comunicación es exclusivamente a través de tablas
  de contrato en SQL Server, en tres direcciones y con semánticas separadas (ADR 0003, ADR 0004).
- **Worker ↔ servicios externos:** las APIs de Google, la web de la SBS, Telegram y SMTP.

## Alternativas consideradas

- **Un único artefacto .NET que lo haga todo, con servicios alojados.** Elimina la frontera, el
  despliegue coordinado y los tipos duplicados en dos lenguajes. Se descartó porque el ecosistema de
  extracción documental en Python es sustancialmente mejor, y la precisión del OCR es el mayor
  riesgo técnico del proyecto (ADR 0017 no lo resuelve; lo acota). Cambiar complejidad de
  infraestructura por riesgo en el núcleo del producto es un mal intercambio.
- **Reducir Python a OCR y SBS, con .NET dueño del resto de integraciones.** Los clientes de Gmail,
  Drive y Sheets en .NET son igual de maduros, y la frontera se encogería. Se descartó porque el eje
  de separación adoptado no es la madurez de las librerías sino el **modelo de ejecución**: todo el
  trabajo asíncrono y tolerante a fallo vive en un lado, todo el trabajo transaccional en el otro.
  Ese criterio es verificable ante una integración nueva; "cuál librería es mejor" no lo es.
- **SPA servida por la propia API, sin proxy inverso.** Menos piezas móviles. Se descartó porque
  ataría la renovación del certificado TLS al ciclo de vida de la aplicación y obligaría a desplegar
  el backend para publicar un cambio de frontend.

## Consecuencias

- El trabajo de fondo nunca compite con la atención de la interfaz, y cada artefacto se reinicia,
  detiene o despliega por separado.
- SPA y API comparten origen, de modo que `SameSite=Lax` funciona y el visor de documentos recibe la
  cookie de sesión dentro de un `<iframe>` (ADR 0007, ADR 0013).
- El certificado TLS se gestiona en un solo lugar.
- **Costo:** dos runtimes que instalar, versionar y desplegar de forma coordinada, en el orden que
  fija ADR 0016.
- **Costo:** los tipos de las tablas de frontera existen en C# y en Python. Una divergencia solo se
  manifiesta en tiempo de ejecución.
- **Costo:** el worker requiere acceso al mismo volumen de documentos que la API (ADR 0013), lo que
  restringe la topología de despliegue posible.
- **Costo asumido:** no existe vigilancia automática del worker. Si se detiene, nadie avisa, y el
  reinicio es manual. Es una decisión explícita del responsable del proyecto, registrada como riesgo
  abierto en el TECH-DESIGN.
