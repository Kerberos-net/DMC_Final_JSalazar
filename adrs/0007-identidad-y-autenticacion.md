# ADR 0007: Modelo de identidad y mecanismo de autenticación

## Estado

Aceptado. Revisión 3. Añade límite de intentos y camino de recuperación de contraseña, dos piezas
que faltaban del mismo tamaño que las que sí decidía (revisión adversarial v2, A8).

La revisión 2 reemplazó la versión previa (`adrs - v1/0007`), que dejaba sin decidir el valor de
`SameSite` —exactamente donde estaba el problema—.

## Contexto

El PRD sitúa la autorización por roles en el "No alcance": hay un solo usuario, el asistente
contable, y todo usuario autenticado accede a la totalidad de las funciones.

Falta decidir dónde vive la credencial de la SPA y cómo se transporta. La decisión no es
"cookie o JWT" en abstracto: son dos preguntas distintas —dónde se guarda la credencial, y si el
servidor recuerda la sesión— y de ellas dependen la exposición a XSS y la capacidad de revocar.

Hay además una dependencia que la versión anterior no cerró: con SPA y API en orígenes distintos,
una cookie `SameSite=Lax` o `Strict` **no se envía**, y la sesión sencillamente no funciona. Solo
`SameSite=None; Secure` opera cross-site, y eso reabre la exposición a CSRF que la cookie pretendía
cerrar. El criterio de aceptación *"la cookie se emite con los atributos HttpOnly y SameSite"* no
era un criterio: era un marcador de posición.

## Decisión

### Sesión de servidor con cookie `HttpOnly`

```csharp
options.Cookie.HttpOnly     = true;
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
options.Cookie.SameSite     = SameSiteMode.Lax;
options.Cookie.Name         = "__Host-session";
options.ExpireTimeSpan      = TimeSpan.FromHours(8);
options.SlidingExpiration   = true;
```

`SameSite=Lax` **funciona porque SPA y API comparten origen tras el proxy inverso** (ADR 0001,
ADR 0012). No se necesita CORS, ni `AllowCredentials`, ni `withCredentials` en Angular.

El cierre de sesión **invalida la sesión del lado del servidor**, no solo borra la cookie.

### Modelo de identidad

Una sola tabla, `Usuario`. **No existen `Rol` ni `UsuarioRol`**: el sistema implementa autenticación
pero no autorización por roles, en coherencia con el "No alcance" del PRD.

La contraseña se almacena mediante **función de derivación de clave con sal** —Argon2id o PBKDF2—,
nunca en claro ni con un hash de propósito general. Las credenciales inválidas producen un mensaje
que **no revela si el usuario existe**.

### Efecto sobre el visor de documentos

```html
<iframe src="/api/documentos/12/contenido"></iframe>
```

Al ser mismo origen, la cookie viaja y el visor funciona. Era el punto exacto donde la indecisión
sobre `SameSite` se habría manifestado como un panel en blanco (ADR 0013).

## Alternativas consideradas

- **JWT en `localStorage`.** Es el patrón por defecto en tutoriales de SPA. Se descartó por dos
  razones independientes: `localStorage` es legible por cualquier script del origen, de modo que un
  XSS se lleva la credencial a la máquina del atacante; y un JWT autocontenido **no se puede
  revocar** sin construir la lista de revocación que reintroduce el estado de servidor que el JWT
  pretendía evitar. Para un sistema de un usuario, no hay ninguna ganancia que compense.
- **JWT en cookie `HttpOnly`.** Resuelve el almacenamiento pero no la revocación. Se descartó por lo
  mismo: se paga la complejidad del token firmado sin obtener a cambio nada que la sesión de
  servidor no dé ya.
- **Delegar la identidad en un proveedor externo (Google, Entra ID).** Elimina la gestión de
  contraseñas propias, que es responsabilidad real. Se descartó porque el PRD asigna explícitamente
  esa responsabilidad al proyecto y porque añade una dependencia de red para poder entrar al
  sistema.
- **Orígenes distintos con `SameSite=None` y CORS con credenciales.** Se descartó en ADR 0012 por la
  topología, y con ella desaparecen el token antiforgery obligatorio y los tres modos de fallo
  garantizados que arrastra esa configuración.

### Límite de intentos

```sql
ALTER TABLE Usuario
    ADD IntentosFallidos INT       NOT NULL DEFAULT 0,
        BloqueadoHasta   DATETIME2 NULL;
```

Cinco fallos consecutivos: bloqueo de 15 minutos, creciente en bloqueos sucesivos. Un inicio de
sesión correcto pone el contador a cero. El mensaje de bloqueo **no revela si el usuario existe**,
igual que el de credenciales.

El límite va **en la aplicación, no en el proxy inverso**, por dos razones: no depende de la
topología —funciona igual en desarrollo, donde no hay proxy— y **el proxy no distingue usuarios**,
solo direcciones. Un bloqueo por usuario es el control correcto para un sistema de un usuario.

Sin esto, la elección de una función de derivación con sal deja de importar: un formulario de login
sin freno es exactamente donde el costo de cómputo por intento no protege nada.

### Recuperación de contraseña

Hay **un usuario, ningún rol de administrador y ninguna pantalla de restablecimiento**. Una contraseña
olvidada se resuelve con un **procedimiento operativo escrito**, ejecutado por el administrador de la
instancia mediante un **comando de la propia aplicación** —que aplica la misma derivación con sal—,
nunca con un `UPDATE` a mano sobre la base.

Una pantalla de restablecimiento exigiría un segundo canal —correo, teléfono— que el proyecto no
tiene. El procedimiento escrito es honesto sobre lo que realmente hay, y evita que el camino de
recuperación sea escribir a mano en la base de la contabilidad de la compañía.

## Consecuencias

- La credencial **no es accesible desde JavaScript**. Un XSS no puede exfiltrarla.
- El formulario de inicio de sesión tiene freno, y el freno es por usuario.
- El restablecimiento de contraseña es un procedimiento con dueño, no una improvisación.
- La sesión se revoca de verdad, desde el servidor, sin listas de revocación.
- El criterio de aceptación del Flujo 6 tiene ahora un valor concreto y verificable.
- **Costo:** `HttpOnly` **no hace inmune a XSS**. Si se ejecuta código en el origen, ese código
  puede hacer peticiones autenticadas con la cookie adjunta —*session riding*—. Lo que se gana es
  contención: la credencial no sale de la máquina. Es una diferencia grande, pero no es inmunidad, y
  no debe documentarse como si lo fuera.
- **Costo:** la sesión de servidor obliga a mantener su almacén. Con un usuario es irrelevante.
- **Costo:** la topología de mismo origen es ahora un **requisito de seguridad**, no una preferencia
  de despliegue. Si alguien separa los orígenes en el futuro, la sesión deja de funcionar y la
  solución obvia —`SameSite=None`— reabre CSRF. Debe quedar escrito en ADR 0012.
- **Costo:** el comando de restablecimiento hay que construirlo. Es la **única funcionalidad de
  administración** del sistema, y entra en la documentación de operación junto al procedimiento de
  respaldo.
- **Costo:** el bloqueo temporal es un vector de denegación de servicio contra el único usuario:
  quien conozca su nombre puede mantenerlo bloqueado. Con un sistema en red interna y un solo usuario
  el riesgo es bajo, pero es real y se declara.
