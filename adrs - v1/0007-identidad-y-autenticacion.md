# ADR 0007: Modelo de identidad y mecanismo de autenticación

## Estado

Aceptado

## Contexto

El PRD exige que el sistema requiera inicio de sesión, con credenciales gestionadas en la base de
datos del software. Al mismo tiempo, declara explícitamente en **No alcance** que no incluye
separación de roles ni permisos entre usuarios, y en Supuestos que "un mismo usuario revisa y valida
cada factura; no hay separación de roles/permisos en esta versión".

Durante la definición de la partición de propiedad de datos (ADR 0003) se enumeraron inicialmente
las tablas `Usuario`, `Rol` y `UsuarioRol`. Esa enumeración contradecía el alcance declarado en el
PRD, por lo que la contradicción se planteó explícitamente antes de decidir.

Por otro lado, la ADR 0001 establece que el frontend es una SPA en Angular desplegada como artefacto
independiente de la API en ASP.NET Core, lo que obliga a definir cómo se sostiene la sesión entre
dos artefactos separados.

## Decisión

**Modelo de identidad.** El esquema incluye únicamente la tabla `Usuario`. Se descartan `Rol` y
`UsuarioRol`: el sistema implementa **autenticación pero no autorización basada en roles**. Todo
usuario autenticado tiene acceso a la totalidad de las funciones, tal como declara el PRD.

**Mecanismo de autenticación.** La sesión se sostiene mediante una **cookie de sesión `HttpOnly` con
atributo `SameSite`**, emitida por ASP.NET Core tras validar las credenciales contra la tabla
`Usuario`. Las contraseñas se almacenan con una función de derivación de clave con sal, nunca en
claro ni con un hash simple. La API se configura con CORS que admite credenciales para el origen de
la SPA, y con protección CSRF para las operaciones que modifican estado.

## Alternativas consideradas

**Sobre el modelo de identidad:**

- **Crear `Rol` y `UsuarioRol` sin evaluar permisos** — Las tablas existirían con un rol único
  sembrado, dejando el esquema preparado para una versión futura con roles diferenciados y evitando
  una migración posterior. Se descartó porque introduce estructuras que ningún código consulta:
  cuando los roles sean realmente necesarios, el diseño se rehará de todos modos en función de los
  permisos concretos que entonces se conozcan, y mientras tanto el modelo contradice el alcance
  declarado en el PRD.
- **Ampliar el PRD para incluir roles y permisos en esta versión** — Habría alineado documento y
  sistema implementando autorización real. Se descartó porque amplía deliberadamente el alcance de
  un proyecto cuyo PRD ya fue cerrado con un único usuario que revisa y valida.

**Sobre el mecanismo de autenticación:**

- **JWT con esquema `Bearer` almacenado en el cliente** — Ofrecía autenticación sin estado en el
  servidor e independencia del dominio, útil si en el futuro hubiera clientes distintos de un
  navegador. Se descartó porque el token queda accesible al JavaScript de la propia página, de modo
  que una vulnerabilidad XSS permitiría robarlo, y porque revocar una sesión antes de su expiración
  exigiría mantener una lista de revocación, reintroduciendo el estado que el JWT pretendía evitar.
  El único cliente previsto es un navegador, por lo que ninguna de sus ventajas aplica.
- **Inicio de sesión federado con Google (OAuth)** — Aprovecharía la cuenta de Google Workspace que
  el PRD da por disponible y eliminaría por completo la gestión de contraseñas propias, que es la
  parte más delicada de la autenticación. Se descartó porque contradice el requisito explícito del
  PRD de gestionar las credenciales en la base de datos del software; adoptarlo exigiría modificar
  el PRD primero.

## Consecuencias

- El modelo de datos refleja exactamente el alcance declarado en el PRD, sin tablas ni código que
  aparenten una capacidad que el sistema no tiene.
- La credencial de sesión es inaccesible desde JavaScript, lo que elimina el robo de sesión por XSS
  como vector, y el cierre de sesión tiene efecto inmediato del lado del servidor.
- El navegador gestiona por sí mismo el envío y la expiración de la cookie, por lo que el código
  Angular no manipula credenciales.
- **Costo:** cuando el sistema necesite roles, hará falta una migración de esquema y la
  introducción de una capa de autorización que hoy no existe.
- **Costo:** la SPA y la API quedan atadas a una configuración de despliegue con dominios
  compatibles con la cookie, y obligan a configurar CORS con credenciales y protección CSRF
  correctamente; un error en esa configuración se manifiesta como fallos de sesión difíciles de
  diagnosticar.
- **Costo:** el proyecto asume la responsabilidad de almacenar y verificar contraseñas de forma
  segura, incluida su rotación y su política de complejidad, en lugar de delegarla en un proveedor
  de identidad.
