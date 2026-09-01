# Security Pass — Gestor de Facturas de Compra (SmartNet)

Fecha: 2026-08-30
Alcance revisado:

- **Producto / requisitos** (`PRD.md`, criterios de aceptación de `TECH-DESIGN.md`) — revisado.
- **Arquitectura / ADRs** (0001–0019, foco en 0003 partición de datos, 0007 identidad, 0012
  despliegue/TLS, 0013 documentos, 0015 secretos) — revisado.
- **Código API .NET** (`SmartNet/SmartNetApi/`): 10 grupos de endpoints, `Program.cs`, núcleo y
  adaptadores de `auth`, adaptadores SQL de `facturacion`/`catalogos`/`inbox`/`tipos-de-cambio`,
  `admin` CLI, esquema SQL `002`/`008`/`011`, `ExportadorXlsx` — revisado.
- **Código worker Python** (`SmartNet/SmartNetWorker/src/smartnet_worker/`): clientes de integración
  (Gmail, Telegram, SMTP, SBS), parser XML/UBL, OCR/PDF, acceso a base, consumidores de
  CommandQueue/Outbox — revisado.
- **SPA Angular** (`SmartNet/SmartNetWeb/src/`): interceptores HTTP, guard de auth, login, visor de
  documentos, uso de `innerHTML`/`bypassSecurityTrust*`, almacenamiento en navegador — revisado.
- **CI / configuración** (`.github/workflows/ci.yml`, `.gitignore`, manifiestos de dependencias) —
  revisado.
- **No revisado por falta de material**: no existe configuración del proxy inverso ni del
  certificado TLS en el repositorio (ADR 0012 lo deja explícitamente sin decidir); no hay pipeline
  de despliegue. Los adaptadores Drive/Sheets del worker **no existen todavía** (ítems #15/#16 sin
  implementar) — quedan para un pase posterior. `SmartNet.Db.Runner/Program.cs` (aplicación DbUp) y
  varios repos de catálogo se revisaron por `grep` de SQL dinámico, no línea por línea.

---

## Resumen ejecutivo

El sistema está construido con una disciplina de seguridad **notablemente alta** para su tamaño:
toda consulta SQL de los tres runtimes está parametrizada (incluidas las listas `IN (...)`
dinámicas), la partición de datos se refuerza con `GRANT`/`DENY` por login de base y no solo por
convención, la sesión es server-side con revocación real, el hashing es Argon2id con comparación de
tiempo constante, el parser de XML entrante está explícitamente blindado contra XXE, el alcance de
Gmail es mínimo (`gmail.modify`, nunca borrado), la contención de path traversal en el visor es
correcta, y no hay ningún secreto real comiteado en un repositorio que es público.

Los riesgos que sí aparecen son de segunda línea, no fallas de diseño:

1. **Fuga de credencial** (MEDIO): el token del bot de Telegram puede terminar escrito en claro en
   `fact.EstadoIntegracion.UltimoError` a través del mensaje de una excepción de `requests`, y de
   ahí lo expone `GET /api/integraciones/estado` a la SPA autenticada.
2. **Llaves de Data Protection sin cifrar en disco** (MEDIO): sin `ProtectKeysWithDpapi()`, quien
   pueda leer el directorio del key ring puede forjar cookies de sesión.
3. **Falta de límites de recursos** (MEDIO) sobre adjuntos de correo controlados por el atacante en
   el worker: sin tope de tamaño, sin límite de geometría de PDF, sin timeout por documento — un
   correo hostil puede detener la ingesta, y el documento venenoso se reintenta indefinidamente.
4. **Defensa en profundidad del visor de documentos** (MEDIO): el `<iframe>` no lleva `sandbox`, no
   hay CSP, y el registro de adjuntos manuales confía en `RutaRelativa`/`MimeType`/`TamanoBytes` del
   cliente sin validarlos.
5. Un **usuario desactivado (`Activo = 0`) sigue autenticándose** (MEDIO, latente): el campo se lee
   pero nunca se consulta en el login.

El resto son endurecimientos (cabeceras de seguridad, rate limiting, lockfile del worker, patrones
de `.gitignore`, permisos de CI). Ninguno es explotable de forma remota sin autenticación.

---

## Fortalezas de seguridad

Controles que ya existen y **no deben tocarse** al remediar:

- **SQL 100% parametrizado** en los tres runtimes. Las interpolaciones `$"""..."""` de
  `SqlBandejaRepository`/`SqlProveedorRepository` solo inyectan constantes de compilación
  (`ASC`/`DESC`, nombres de columna mapeados por `switch`); las listas `IN (...)` de Python solo
  interpolan marcadores `?`. Metacaracteres de `LIKE` escapados. **No se encontró superficie de
  inyección SQL.**
- **Partición de datos reforzada por el motor** (ADR 0003): `usr_api` tiene `DENY SELECT` explícito
  sobre las tablas de ingesta de Python y viceversa; `dbo.*` es `SELECT` a nivel de objeto. Un bug
  de código no puede cruzar el límite.
- **Autenticación**: Argon2id (`FixedTimeEquals`), token de sesión de 256 bits de CSPRNG, hash
  SHA-256 en base con collation `BIN2`, sesión server-side con chequeo de revocación en cada
  request, logout y `restablecer-clave` invalidan del lado del servidor, sin fijación de sesión
  (token nuevo en cada `SignInAsync`). Cookie `__Host-session` con `HttpOnly` + `Secure` +
  `SameSite=Lax`. `PhcCodec.Parse` es total (nunca lanza). Lockout creciente 15/30/60/120 min con
  dos contadores, correcto contra ADR 0007 Rev 4. Paridad de respuesta y hash señuelo contra
  enumeración de usuarios.
- **Parser XML/UBL del worker blindado**: `etree.XMLParser(resolve_entities=False, no_network=True,
  load_dtd=False, huge_tree=False)` cierra billion-laughs, XXE, SSRF-vía-DTD y expansión de nodos.
  XPath siempre literal — sin inyección. Sin XSLT.
- **Gmail**: alcance `gmail.modify`, solo `addLabelIds`. No hay `.trash()`/`.delete()`/`.batchDelete()`
  en todo el paquete. Tokens OAuth solo en memoria, nunca en disco.
- **Sin `subprocess`/`shell=True`/`pickle`/`yaml.load`/`eval`/`verify=False`** en el worker.
  `tesseract` recibe un `PIL.Image`, nunca el nombre de archivo del atacante.
- **Sanitización de nombre de archivo** con re-chequeo de contención (`is_relative_to`) al escribir
  adjuntos.
- **Contención de path traversal** en `GET /api/documentos/{id}/contenido`: `ResolverRutaSegura`
  neutraliza `../` y rutas absolutas; el MIME servido se degrada a `application/octet-stream` fuera
  de una allow-list de 3 tipos, con `X-Content-Type-Options: nosniff` y `Content-Disposition: inline`.
- **`ExportadorXlsx`** escribe toda celda como `CellValues.InlineString` — un `=` inicial en el
  valor es texto literal, no fórmula. **No hay inyección de fórmulas** en los cuatro endpoints de
  exportación, pese a que los valores incluyen nombres de proveedor de `dbo.*`.
- **Sin CORS** (guardado por `NoCorsTests`), sin `appsettings.json` comiteado (config solo por
  variables de entorno, con nombres de variable deliberadamente distintos por principal), key ring
  persistido fuera del proceso.
- **CI**: la contraseña `sa` hardcodeada es la de un contenedor efímero destruido con el job — no
  protege nada y su evaluación como "no secreto" es correcta. La SPA comitea `package-lock.json` y
  usa `npm ci`; los `.csproj` fijan versión exacta.
- **SPA**: cero usos de `innerHTML`/`[innerHTML]`/`bypassSecurityTrustHtml`/`eval`/`Function`. El
  único `bypassSecurityTrustResourceUrl` está justificado (URL construida por el propio componente).
  Ningún token en `localStorage`/`sessionStorage` (solo preferencias de UI validadas contra
  allow-list). Errores del servidor renderizados por interpolación con auto-escape.

---

## Findings

### CRITICAL

Ninguno.

### HIGH

Ninguno. (El revisor de la SPA propuso HIGH para el visor de documentos; tras correlacionar con las
mitigaciones del lado servidor —allow-list de MIME + `nosniff` + degradación a
`application/octet-stream`— la explotabilidad real baja a MEDIO. Ver **SP-04**.)

### MEDIUM

---

**SP-01 — El token del bot de Telegram se filtra a `fact.EstadoIntegracion.UltimoError` y se expone por la API**

- **Severity:** MEDIUM
- **Confidence:** HIGH
- **Category:** Exposición de secretos / dato sensible en almacén de datos, cruce de frontera de confianza
- **Affected artifact:** `SmartNetWorker` — `notificaciones.py`, consumido por `cli_outbox.py`; sink en `estado_integracion.py`; expuesto por `SmartNetApi` `GET /api/integraciones/estado`
- **Location:**
  - `src/smartnet_worker/notificaciones.py:44` — `url = f"https://api.telegram.org/bot{self._bot_token}/sendMessage"`
  - `src/smartnet_worker/notificaciones.py:50` — `respuesta.raise_for_status()`
  - `src/smartnet_worker/notificaciones.py:99` — `registrar_fallo(cursor, canal.nombre, instante, str(error))`
  - `src/smartnet_worker/estado_integracion.py:45-48` — `_UPDATE_FALLO` → columna `UltimoError`
- **Description:** El token va en la URL de la petición. Ante cualquier error de Telegram (token
  rotado/revocado, `chat not found`, 429, 5xx transitorio, timeout de red) `requests` lanza una
  excepción cuyo mensaje incluye la URL completa —y por tanto el token—. `notificar` captura la
  excepción y escribe `str(error)` (truncado a 2000 caracteres, de sobra para el token de ~46) en
  `fact.EstadoIntegracion.UltimoError` para `Nombre='TELEGRAM'`.
- **Evidence:**
  ```python
  def enviar(self, mensaje: str) -> None:
      url = f"https://api.telegram.org/bot{self._bot_token}/sendMessage"
      respuesta = requests.post(url, json={...}, timeout=config.HTTP_TIMEOUT_SECONDS)
      respuesta.raise_for_status()
  # ...
  except Exception as error:  # noqa: BLE001
      registrar_fallo(cursor, canal.nombre, instante, str(error))
  ```
- **Attack scenario:** No lo dispara directamente el atacante del correo, pero los errores de
  Telegram son frecuentes (rotación de token, bot revocado, rate-limit). En cuanto ocurre uno, el
  token queda en claro en una tabla que `usr_api` puede leer y que `GET /api/integraciones/estado`
  entrega a la SPA autenticada, además de posibles logs de stdout capturados por cron/Task Scheduler.
- **Potential impact:** Compromiso total del bot de Telegram (enviar/leer mensajes al canal de
  alertas, phishing de operadores). Credencial en claro en reposo en una base compartida con el
  sistema contable de la compañía, fuera de su frontera de confianza prevista (secreto del worker
  ahora visible para la capa API/SPA).
- **Existing mitigation:** Truncado a 2000 caracteres — no ayuda, el token está al principio de la
  URL.
- **Recommended remediation:** Envolver `TelegramCanal.enviar` para que capture
  `requests.RequestException` y re-lance un mensaje sin token (p. ej. `f"{type(error).__name__}:
  {getattr(error.response, 'status_code', 'sin respuesta')}"`); o pasar todo string de error de
  canal que se vaya a persistir por un `replace(self._bot_token, "***")`.
- **Suggested verification:** Test unitario: `TelegramCanal` con `requests.post` stub devolviendo
  401; afirmar que el string entregado a `registrar_fallo` no contiene el token. Test de barrido
  que verifique que ningún `str(error)` de cliente HTTP se persiste sin filtrar.
- **Required change type:** CODE FIX

---

**SP-02 — El key ring de Data Protection se guarda sin cifrar; la protección depende de ACLs de sistema no especificadas**

- **Severity:** MEDIUM
- **Confidence:** MEDIUM
- **Category:** Gestión de claves criptográficas / secretos en reposo
- **Affected artifact:** `SmartNetApi` — configuración de Data Protection
- **Location:** `api/SmartNet.Api/Program.cs:151-157`; `api/SmartNet.Api/ApiKeyRingOptions.cs:6-9`
- **Description:** `AddDataProtection()` se configura con `FileSystemXmlRepository` en
  `SMARTNET_API_KEYRING_PATH` pero **sin** `ProtectKeysWithDpapi()` ni
  `ProtectKeysWithCertificate()`. Las claves maestras AES quedan como XML en claro. Esas claves
  desenvuelven la cookie `__Host-session`, cuyo payload es el token de sesión crudo. Tampoco hay
  `SetApplicationName()`.
- **Evidence:**
  ```csharp
  builder.Services.AddDataProtection();
  builder.Services.AddOptions<KeyManagementOptions>()
      .Configure<IConfiguration, ILoggerFactory>((options, configuration, loggerFactory) =>
      {
          var keyRingPath = ApiKeyRingOptions.Resolve(configuration);
          options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keyRingPath), loggerFactory);
      });   // sin ProtectKeysWith* en ninguna parte
  ```
  `ApiKeyRingOptions.cs` solo *recomienda* `C:\ProgramData\SmartNet\dataprotection-keys` en un
  comentario; la app no exige nada sobre los permisos del directorio.
- **Attack scenario:** Cualquier proceso/usuario que pueda leer el directorio del key ring (job de
  respaldo, recurso compartido mal configurado, otra cuenta de servicio en el mismo host, un
  path traversal en una app co-alojada) obtiene las claves maestras y puede forjar o descifrar
  cookies de sesión offline y luego reproducir un `__Host-session` válido contra la API.
- **Potential impact:** Forja de sesión / bypass de autenticación sin tocar la base. En un host
  Windows compartido es un paso realista de movimiento lateral.
- **Existing mitigation:** Las claves viven fuera del checkout de git (por convención); despliegue
  de instancia única; TLS en el proxy.
- **Recommended remediation:** Añadir `.ProtectKeysWithDpapi()` (objetivo Windows de instancia
  única, coherente con ADR 0012) o un certificado; llamar `.SetApplicationName("SmartNet.Api")`.
  Documentar en el ADR de despliegue el ACL requerido del directorio (cuenta de servicio de
  Kestrel: lectura/escritura; Administradores: total; resto: nada) e, idealmente, fallar el
  arranque si el directorio es legible por todos.
- **Suggested verification:** Inspeccionar un `key-*.xml` generado — el `<value>` debe estar
  envuelto en `<encryptedSecret>`/`EncryptedXml`, no base64 en claro. Extender `KeyRingPersistenceTests`.
- **Required change type:** CODE FIX + DESIGN / ADR CHANGE

---

**SP-03 — Un usuario desactivado (`fact.Usuario.Activo = 0`) sigue autenticándose; las sesiones no se revalidan contra el registro del usuario**

- **Severity:** MEDIUM
- **Confidence:** HIGH
- **Category:** Autenticación / control de seguridad muerto
- **Affected artifact:** `SmartNetApi` — secuencia de login y almacén de tickets de sesión
- **Location:** `api/SmartNet.Api/SesionEndpoints.cs:30-83`; `auth/SmartNet.Auth.Core/AccessPolicy.cs:12-15`; `auth/SmartNet.Auth.Infrastructure/SqlSesionTicketStore.cs` (`RetrieveAsync`)
- **Description:** `SqlUsuarioRepository.FindByNameAsync` lee `Activo` y lo materializa en
  `UsuarioCredentialState.Activo`, pero nada en el camino de login lo consulta. `AccessPolicy.Evaluate`
  solo mira `BloqueadoHasta`. Una fila con `Activo = 0` inicia sesión con normalidad, y
  `RetrieveAsync` tampoco re-verifica el usuario en peticiones posteriores. Relacionado: `RenewAsync`
  amplía `ExpiraEn` en cada request sin tope sobre `CreadaEn`, así que una sesión usada al menos cada
  8 h vive indefinidamente (ver también SP-12).
- **Evidence:**
  ```csharp
  public static AccessDecision Evaluate(UsuarioCredentialState estado, DateTimeOffset ahora) =>
      estado.BloqueadoHasta is { } bloqueadoHasta && bloqueadoHasta > ahora
          ? AccessDecision.Locked : AccessDecision.Allowed;   // Activo nunca se consulta
  ```
  `db/runner/.../PermissionMatrixTests.cs:194` incluso ejercita `UPDATE fact.Usuario SET Activo = 0`.
- **Attack scenario:** Un operador "desactiva" la cuenta de un usuario comprometido o que se fue
  poniendo `Activo = 0` (la lectura natural de la columna, y la única palanca disponible: no hay
  verbo admin para borrar un usuario). La cuenta sigue funcionando — los logins tienen éxito y las
  sesiones vivas siguen válidas.
- **Potential impact:** Una credencial que se cree revocada sigue dando acceso completo al libro de
  compras. Baja probabilidad hoy (sistema de un usuario; `smartnet-admin` no tiene verbo
  `usuario desactivar`), pero es un bypass de autenticación latente en cuanto se agregue multiusuario
  o un camino de desactivación.
- **Existing mitigation:** Ninguna en código. `Activo` es `DEFAULT 1` y nada escribe 0 en producción.
- **Recommended remediation:** En `AccessPolicy.Evaluate` (o en `PostSesionAsync` justo tras el
  chequeo de `null`) tratar `!estado.Activo` como credenciales inválidas, devolviendo el 401
  idéntico. Que `SqlSesionRepository.FindActiveAsync` haga `JOIN` a `fact.Usuario` y exija
  `Activo = 1`, de modo que desactivar también mate las sesiones vivas.
- **Suggested verification:** Test xUnit: usuario con `Activo = 0` y contraseña correcta →
  `POST /api/sesion` devuelve 401 `credenciales-invalidas`; caso `Activo:false` en `AccessPolicyEvaluateTests`.
- **Required change type:** CODE FIX (+ nota de SPEC: "desactivado" es un estado de autenticación)

---

**SP-04 — Defensa en profundidad del visor de documentos: `<iframe>` sin `sandbox`, sin CSP, y registro de adjuntos manuales sin validar**

- **Severity:** MEDIUM
- **Confidence:** MEDIUM
- **Category:** Aislamiento de mismo origen / manejo de contenido almacenado / validación de entrada
- **Affected artifact:** `SmartNetWeb` visor de documentos + `SmartNetApi` `POST /api/facturas/{id}/adjuntos`
- **Location:**
  - `SmartNet/SmartNetWeb/src/app/detalle/ui/visor-documento/visor-documento.html:15` — `<iframe [src]="url" ...>` sin `sandbox`
  - `SmartNet/SmartNetApi/api/SmartNet.Api/FacturaEndpoints.cs:152-163` + `RegistrarAdjuntoRequest` (`:211`)
  - `SmartNet/SmartNetApi/api/SmartNet.Api/DocumentoContenido.cs:33-48` (`ResolverRutaSegura` — no resuelve symlinks/junctions)
- **Description:** Dos huecos de segunda línea que se combinan. (1) El `<iframe>` que renderiza
  `GET /api/documentos/{id}/contenido` —contenido originado en un adjunto de Gmail, es decir dato
  influido por el atacante— no lleva `sandbox`, `referrerpolicy` ni `csp`, y no hay CSP a nivel de
  documento ni de shell. (2) `POST /api/facturas/{id}/adjuntos` toma `NombreArchivo`, `RutaRelativa`,
  `MimeType` y `TamanoBytes` directamente del cuerpo JSON y los persiste **sin validación**: no
  comprueba que el archivo exista, ni allow-list de MIME al registrar, ni tope de tamaño, ni que
  `RutaRelativa` esté confinada a un subdirectorio por factura. La API nunca recibe los bytes —
  guarda un puntero al volumen compartido.
- **Evidence:**
  ```csharp
  var adjunto = new AdjuntoManual(
      AdjuntoManualId: 0, FacturaId: id, NombreArchivo: cuerpo.NombreArchivo,
      RutaRelativa: cuerpo.RutaRelativa, MimeType: cuerpo.MimeType, TamanoBytes: cuerpo.TamanoBytes, ...);
  ```
  ```html
  <iframe [src]="url" title="Documento de la factura" class="visor-documento"></iframe>
  ```
- **Attack scenario:** Un usuario autenticado registra un adjunto cuya `RutaRelativa` apunta a
  cualquier archivo bajo `SMARTNET_API_STORAGE_ROOT` (documento de otra factura, archivo escrito por
  el worker) y lo lee vía `GET /api/documentos/manual-{id}/contenido`. El traversal fuera de la raíz
  está bloqueado; dentro del volumen no. La ejecución de script en el origen de la SPA está
  **mitigada** por la allow-list de MIME (`text/html`/`image/svg+xml` degradan a
  `application/octet-stream`) + `nosniff`, pero el `sandbox` ausente es la única barrera que queda si
  esa mitigación se debilita, y una junction creada dentro del volumen (por el worker o acceso al
  filesystem) escaparía la raíz porque `GetFullPath` no canonicaliza reparse points.
- **Potential impact:** Divulgación de documentos entre facturas; confusión de MIME; lectura de
  archivo arbitrario si se combina con una junction. Acotado por el contexto de un solo usuario.
- **Existing mitigation:** `ResolverRutaSegura` (contención de raíz), `MimeAllowList` + `nosniff` +
  `Content-Disposition: inline` al servir, sistema de un usuario.
- **Recommended remediation:**
  - SPA: añadir `sandbox=""` al `<iframe>` (PDF/JPG/PNG siguen renderizando sin `allow-scripts`;
    nunca combinar `allow-scripts` con `allow-same-origin`).
  - API: validar `MimeType` contra la misma allow-list al registrar (rechazar, no guardar en
    silencio), exigir un `TamanoBytes` máximo y que el archivo exista con esa longitud, y forzar que
    `RutaRelativa` empiece por `manual/{facturaId}/`. Si los adjuntos manuales deben ser subidas
    reales, añadir un endpoint multipart que escriba los bytes en el servidor y derive la ruta.
  - Tras resolver la ruta, verificar el objetivo final con `File.ResolveLinkTarget(path, true)` y
    re-comprobar que queda bajo la raíz.
  - Decisión de arquitectura: servir `/api/documentos/*/contenido` desde un origen separado
    (sandbox domain) — ADR 0013 hoy ratifica el mismo origen.
- **Suggested verification:** Registrar un adjunto con `MimeType: "text/html"`,
  `RutaRelativa: "ingesta/../otro/secret.pdf"` y `TamanoBytes` desmesurado → rechazo. Guardar un
  HTML y un SVG-con-script como documento y confirmar que el iframe ni ejecuta script ni puede
  llamar `/api/*`. Aserción Vitest de que el iframe lleva `sandbox`.
- **Required change type:** CODE FIX + DESIGN / ADR CHANGE

---

**SP-05 — Sin cabeceras de seguridad HTTP ni Content-Security-Policy (clickjacking sobre operaciones que cambian estado; radio de impacto de XSS sin contención)**

- **Severity:** MEDIUM
- **Confidence:** HIGH
- **Category:** Endurecimiento faltante
- **Affected artifact:** `SmartNetApi` `Program.cs` (pipeline), `SmartNetWeb` `src/index.html`, proxy inverso (sin decidir)
- **Location:** `api/SmartNet.Api/Program.cs` (pipeline sin middleware de cabeceras); `SmartNet/SmartNetWeb/src/index.html:1-13` (sin `<meta http-equiv="Content-Security-Policy">`)
- **Description:** Ni la API ni la SPA emiten `X-Frame-Options` / `Content-Security-Policy` /
  `Strict-Transport-Security`. No hay configuración de proxy comiteada que pudiera añadirlas. La app
  autenticada completa puede ser embebida en un `<iframe>` por un sitio externo; no hay
  `frame-ancestors 'self'`. La salida de Angular no usa scripts inline, así que un `script-src 'self'`
  estricto es factible.
- **Evidence:** El pipeline de `Program.cs` es `UseAuthentication(); UseAuthorization(); Map...;
  Run();` — sin middleware de cabeceras. `index.html` solo tiene `charset`, `title`, `base href`,
  `viewport`, favicon.
- **Attack scenario:** Clickjacking / UI-redress contra el operador único: un atacante embebe la SPA
  y superpone señuelos para forzar clics en controles que cambian estado (`validar`, `descartar`,
  `anular`). `SameSite=Lax` no bloquea XHR de mismo origen dentro de un frame una vez que el
  operador tiene sesión viva. Además, cualquier inyección futura en el origen de la SPA tiene
  `connect-src` sin restringir.
- **Potential impact:** Ejecución de acciones por ingeniería social dirigida; amplificación de
  cualquier XSS a exfiltración total. Bajo por ser usuario interno único, pero real.
- **Existing mitigation:** `nosniff` en el contenido de documentos; `SameSite=Lax`; red interna;
  auto-escape de Angular.
- **Recommended remediation:** Middleware de cabeceras en la API (`X-Frame-Options: DENY` o CSP
  `frame-ancestors 'none'`, `Strict-Transport-Security` si el proxy no lo añade). Definir la CSP de
  la SPA como requisito de despliegue en el ADR pendiente del proxy inverso:
  `default-src 'self'; script-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self';
  connect-src 'self'`. Para el endpoint de documentos, `Content-Security-Policy: sandbox; default-src 'none'`
  sobre los bytes servidos.
- **Suggested verification:** `curl -I` sobre la raíz desplegada y sobre `/api/documentos/{id}/contenido`
  afirmando las cabeceras; fixture de configuración del proxy una vez elegido.
- **Required change type:** CODE FIX (cabeceras de la API) + DESIGN / ADR CHANGE (CSP en el ADR de proxy/TLS)

---

**SP-06 — El worker no impone límites de recursos sobre adjuntos controlados por el atacante, y no hay timeout por documento**

- **Severity:** MEDIUM
- **Confidence:** HIGH (tamaño) / MEDIUM (geometría de PDF)
- **Category:** Agotamiento de recursos / DoS
- **Affected artifact:** `SmartNetWorker` — `cli_gmail.py`, `gmail_client.py`, `almacenamiento.py`, `pdf_lectura.py`, `cli_procesamiento.py`
- **Location:**
  - `src/smartnet_worker/cli_gmail.py:163-168` — descarga + hash + escritura sin tope de tamaño
  - `src/smartnet_worker/gmail.py:139` — `tamano_bytes` se captura, nunca se verifica
  - `src/smartnet_worker/pdf_lectura.py:151-161` — `_rasterizar_pagina`: `bitmap = pagina.render(scale=escala)` sin límite de píxeles por página
  - `src/smartnet_worker/cli_procesamiento.py:161-192` — `_procesar_documento` sin timeout de reloj
- **Description:** La candidatura de adjuntos es solo por extensión. No hay tope sobre
  `AdjuntoGmail.tamano_bytes` ni sobre la longitud decodificada; el adjunto entero se carga en
  memoria, se hashea (SHA-256) y se escribe al volumen compartido. `_MAXIMO_PAGINAS_OCR = 5` limita
  el *número* de páginas pero no su *tamaño*: un PDF con un MediaBox enorme rasterizado a 300 DPI
  pide a pdfium un bitmap de miles de millones de píxeles. No hay `SIGALRM`/timeout por documento, y
  la clasificación de "excepción no reconocida" es `TRANSITORIO`, así que un OOM/timeout se reintenta
  cada ciclo — el documento venenoso re-cuelga para siempre (`NumeroIntento < 3`, backoff transcurrido).
- **Evidence:**
  ```python
  for adjunto in candidatos:
      datos = cliente.obtener_adjunto(mensaje_id, adjunto.attachment_id)
      hash_hex = calcular_hash(datos)
      escribir(raiz_almacenamiento, ruta, datos)
  ```
  ```python
  def _rasterizar_pagina(documento, indice):
      escala = config.OCR_DPI / 72
      bitmap = documento[indice].render(scale=escala)
      imagen_pil = bitmap.to_pil()
  ```
- **Attack scenario:** Cualquiera que consiga meter un correo en la etiqueta monitoreada (modelo de
  amenaza declarado) envía mensajes con varios PDF/XML grandes, o un PDF con MediaBox desmesurado o
  estructura que dispare un bucle de parseo. El worker sufre OOM o se cuelga; el volumen compartido
  —del que también depende la API para servir descargas— se llena.
- **Potential impact:** Detención de la ingesta; OOM repetido; agotamiento de disco con impacto en la
  API. Dos criterios de éxito del PRD dependen de que la ingesta siga viva (visibilidad en 15 min,
  entrega de notificaciones ≥99%).
- **Existing mitigation:** Aislamiento transaccional por mensaje; `_MAXIMO_PAGINAS_OCR`;
  `MAX_IMAGE_PIXELS` por defecto de Pillow (~178 MP, parcial y `TRANSITORIO` así que reintenta);
  `huge_tree=False` acota el árbol XML pero no el `read_bytes` previo.
- **Recommended remediation:** `config.MAX_ADJUNTO_BYTES` — omitir (registrando el motivo) todo
  adjunto cuyo `tamano_bytes` lo exceda **antes** de `obtener_adjunto`, y `assert len(datos) <=
  limite` tras decodificar. Antes de `pagina.render`, leer `pagina.get_size()` y omitir el OCR
  cuando `ancho*alto*escala²` exceda un presupuesto de píxeles configurado (tratarlo como "sin
  pareja", igual que el caso sobre `_MAXIMO_PAGINAS_OCR`). Guardia de reloj por documento en
  `_procesar_documento` clasificada `PERMANENTE` para que un documento venenoso no se reintente
  indefinidamente. Tope de bytes previo a `etree.fromstring` (`cli_procesamiento.py:164`).
- **Suggested verification:** Test con cliente Gmail falso devolviendo un `size` desmesurado →
  ninguna llamada a `obtener_adjunto`/`escribir`. Test estilo `pytest -m ocr` con un PDF de una
  página y MediaBox enorme → texto vacío, sin asignación masiva. Test de que un `LectorPdf` falso que
  se cuelga queda acotado.
- **Required change type:** CODE FIX (+ pequeño SPEC CHANGE que fije los límites; nota en Decisión 3/7 de design.md)

---

**SP-07 — Dependencias del worker Python con rangos abiertos, sin lockfile comiteado, sin fijación de hash**

- **Severity:** MEDIUM
- **Confidence:** HIGH
- **Category:** Cadena de suministro / reproducibilidad de build
- **Affected artifact:** `SmartNetWorker`
- **Location:** `SmartNet/SmartNetWorker/pyproject.toml:6-24`; `.github/workflows/ci.yml` (`pip install -e .[dev]` en dos jobs). No hay `uv.lock`/`requirements*.txt`/`poetry.lock` en el repo (solo existe `SmartNetWeb/package-lock.json`).
- **Description:** Todas las dependencias de runtime usan cotas inferiores abiertas (`requests>=2.32`,
  `lxml>=5.3`, `google-api-python-client>=2.140`, `pypdf>=5.1`, `pytesseract>=0.3.13`, `Pillow>=11`,
  …). CI y cualquier despliegue resuelven "la última que cumpla" sin lockfile ni `--require-hashes`.
  Una versión comprometida de cualquiera de estas o de una transitiva se instala en silencio. `lxml`
  y `Pillow` en particular son fuentes recurrentes de CVE y procesan documentos suministrados por el
  atacante.
- **Evidence:** Lista de dependencias de `pyproject.toml` con `>=` en todas; job
  `pruebas-de-worker-python` → `run: pip install -e .[dev]`.
- **Attack scenario:** Se publica una versión maliciosa de una dependencia transitiva; la siguiente
  ejecución de CI o instalación en producción sobre el worker —que tiene el refresh token de Gmail,
  credenciales SMTP, token de Telegram y un login de base— la ejecuta.
- **Potential impact:** Robo de credenciales del host del worker; parseo de documentos corrompido.
- **Existing mitigation:** El lado .NET fija versión exacta en cada `.csproj`; la SPA comitea
  `package-lock.json` y usa `npm ci`. Solo el worker carece de esto.
- **Recommended remediation:** Adoptar un lockfile (`uv lock` / `pip-compile --generate-hashes`),
  comitearlo, y que CI/despliegue instalen desde él con `--require-hashes`. Añadir cotas superiores
  o Dependabot/renovate para `pip`.
- **Suggested verification:** Checkout limpio + instalación desde el lock produce las mismas versiones
  dos veces; CI falla ante mismatch de hash.
- **Required change type:** PROCESS / HARNESS CHANGE (+ pequeña edición de CI)

---

**SP-08 — `.gitignore` sin patrones de archivos de secretos (repositorio público)**

- **Severity:** MEDIUM
- **Confidence:** MEDIUM
- **Category:** Prevención de exposición de secretos
- **Affected artifact:** `.gitignore` de la raíz
- **Location:** `D:\Proyectos\Claude\Clases\Proyecto\DMC_Final_JSalazar\.gitignore` (archivo completo)
- **Description:** El repositorio es público y, según ADR 0015, el sistema maneja "secretos de peso"
  (refresh tokens de Gmail/Drive/Sheets, token del bot de Telegram, credenciales SMTP, cadena de
  conexión a SQL Server). `.gitignore` cubre cachés de herramientas y salida de build pero no `.env`,
  `*.pfx`, `*.pem`, `*.key`, `*.p12`, `credentials*.json`, `token*.json`, `client_secret*.json`,
  `appsettings.*.local.json`, `secrets.json`. Un desarrollador con el hábito de "ponlo en un .env
  local" no tiene nada que impida el commit.
- **Evidence:** Contenido de `.gitignore`: `.claude/`, `.agents/`, `.atl/`, `~$*.xlsx`, `bin/`,
  `obj/`, `*.user`, `.vs/`, `.venv/`, `__pycache__/`, `*.egg-info/`, `.pytest_cache/`, `*.pyc`,
  `.codegraph/`, `.engram/`. Sin patrones de credenciales.
- **Attack scenario:** Un `git add .` accidental de un `.env` local o un `credentials.json` exportado
  de Google → exposición permanente en el historial público de tokens vivos.
- **Potential impact:** Compromiso del buzón de Gmail, Drive, hoja de Looker, canal de Telegram y el
  SQL Server compartido.
- **Existing mitigation:** El código lee secretos solo de variables de entorno; no hay librería
  dotenv en uso. Ningún secreto real está comiteado hoy.
- **Recommended remediation:** Añadir los patrones de credenciales/keystores a `.gitignore`; añadir
  un hook `gitleaks` de pre-commit y un job de escaneo de secretos en CI (el job rápido
  `verificaciones-estaticas` puede alojarlo).
- **Suggested verification:** `git check-ignore -v .env test.pfx credentials.json` devuelve coincidencias.
- **Required change type:** PROCESS / HARNESS CHANGE

---

### LOW

---

**SP-09 — Sin rate limiting en la API (spraying de login, griefing de lockout, DoS de endpoints de exportación)**

- **Severity:** LOW · **Confidence:** HIGH · **Category:** Disponibilidad / resistencia a fuerza bruta
- **Affected artifact:** `api/SmartNet.Api/Program.cs` (ausencia de `AddRateLimiter`/`UseRateLimiter`)
- **Location:** `api/SmartNet.Api/Program.cs` (ausencia)
- **Description:** No hay limitación de tasa. `POST /api/sesion` solo está protegido por lockout
  por cuenta (no por IP ni conexión). Endpoints autenticados incluyen trabajo no acotado:
  `GET /api/catalogos/plan-contable` (toda `dbo.CuentaContable`), `.../exportacion` (construye el
  `.xlsx` completo en memoria sobre ~6600 filas, con dos copias completas: `MemoryStream` +
  `ToArray()`), `GET /api/bandeja` (batch multi-sentencia con `COUNT(*) OVER()` + subconsultas
  correlacionadas).
- **Attack scenario:** (a) Spraying distribuido de usuario/contraseña; el lockout se puede
  weaponizar para negar servicio a la cuenta legítima. (b) Un cliente autenticado (o una sesión
  robada) itera los endpoints de exportación para fijar CPU/memoria.
- **Potential impact:** DoS de un Kestrel de instancia única; griefing de login. Acotado por red
  interna y auth en todo salvo login.
- **Existing mitigation:** Lockout por cuenta; auth en todos los endpoints salvo login; el proxy
  inverso *podría* limitar, pero no está documentado.
- **Recommended remediation:** Rate limiting de ASP.NET Core — ventana fija/deslizante estricta en
  `POST /api/sesion` por IP remota, y un limitador de concurrencia en las rutas `*/exportacion`. O
  documentar que el proxy inverso es la capa obligatoria de rate limiting (ADR).
- **Suggested verification:** Test de integración martillando `POST /api/sesion` desde un cliente →
  429 tras N intentos, independiente del usuario.
- **Required change type:** CODE FIX o DESIGN / ADR CHANGE

---

**SP-10 — Canales laterales de tiempo en el login (la ruta bloqueada omite Argon2id; la contraseña incorrecta hace una escritura extra en base)**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Divulgación de información (enumeración de usuarios)
- **Affected artifact:** `POST /api/sesion`
- **Location:** `api/SmartNet.Api/SesionEndpoints.cs:41-64`
- **Description:** El cuerpo de respuesta es idéntico en todos los fallos (bien) y un hash señuelo
  iguala el costo de usuario desconocido vs. contraseña incorrecta (bien). Quedan dos canales: (1)
  una cuenta existente bloqueada retorna **sin ninguna llamada a Argon2id** (~15–19 ms más rápido),
  revelando que el usuario existe y está bloqueado; (2) la contraseña incorrecta sobre un usuario
  existente hace un `UPDATE fact.Usuario` (`ApplyFailure` → `SaveCredentialStateAsync`) que la ruta
  de usuario desconocido no hace.
- **Attack scenario:** Un atacante mide distribuciones de latencia sobre usuarios candidatos para
  saber cuáles existen y cuáles están bloqueados, afinando una campaña dirigida.
- **Potential impact:** Bajo en este sistema — red interna, efectivamente una cuenta. Debilita la
  defensa de enumeración en la que el diseño invirtió explícitamente.
- **Existing mitigation:** Cuerpos idénticos; hash señuelo para usuario desconocido; lockout por cuenta.
- **Recommended remediation:** Ejecutar el `hasher.Verify` señuelo también en la ruta bloqueada
  (descartando el resultado). Considerar que la escritura de estado de fallo sea fire-and-forget o
  emitir un round-trip dummy equivalente en la ruta de usuario desconocido. O aceptar el riesgo
  explícitamente dado el contexto de un solo usuario.
- **Suggested verification:** Test de bandas de tiempo (ya es el estilo de `NonexistentUsernameTests`)
  afirmando conteos iguales de llamadas al hasher en las rutas desconocido/incorrecta/bloqueada.
- **Required change type:** CODE FIX o ACCEPT RISK (documentar en ADR 0007)

---

**SP-11 — La autorización es por endpoint, sin `FallbackPolicy`: un endpoint futuro sin `.RequireAuthorization()` queda público en silencio**

- **Severity:** LOW · **Confidence:** HIGH · **Category:** Defensa en profundidad / valor por defecto seguro
- **Affected artifact:** `api/SmartNet.Api/Program.cs:144`
- **Location:** `Program.cs:144` (`AddAuthorization()` sin `FallbackPolicy`) y cada `MapXxxEndpoints`
- **Description:** `AddAuthorization()` se llama sin `FallbackPolicy`. Hoy todos los endpoints
  encadenan `.RequireAuthorization()` y solo `POST /api/sesion` es anónimo a propósito (verificado
  en los 10 archivos). Pero la postura segura por defecto (denegar salvo anotación) no está
  configurada.
- **Attack scenario:** Un desarrollador añade una ruta nueva (endpoint de debug/reporte) y olvida
  `.RequireAuthorization()`; se despliega como endpoint no autenticado sobre datos `fact.*` sin un
  test que lo detecte.
- **Potential impact:** Exposición latente de datos sin autenticación.
- **Existing mitigation:** Cobertura del 100% de la anotación hoy; guardas estructurales existen
  para otras invariantes (`NoCorsTests`).
- **Recommended remediation:** `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`
  y marcar `POST /api/sesion` explícitamente `.AllowAnonymous()`. Test estructural que enumere los
  endpoints mapeados y afirme que cada uno es autenticado o explícitamente anónimo.
- **Suggested verification:** Test de metadatos de endpoint en `SmartNet.Api.Tests`.
- **Required change type:** CODE FIX (+ PROCESS / HARNESS: test estructural)

---

**SP-12 — Las sesiones no tienen tope de vida absoluto**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Gestión de sesión
- **Affected artifact:** `auth/SmartNet.Auth.Infrastructure/SqlSesionTicketStore.cs:48-52`; `Program.cs:127-128`
- **Location:** `SqlSesionTicketStore.RenewAsync`; `SqlSesionRepository.RenewAsync`
- **Description:** `RenewAsync` amplía `fact.Sesion.ExpiraEn` en cada request activa sin techo sobre
  `CreadaEn`. Una sesión usada al menos cada 8 h vive indefinidamente. `RetrieveAsync` re-lee la fila
  (bien para revocación) pero nunca verifica que `fact.Usuario` siga existiendo / esté `Activo`
  (SP-03).
- **Attack scenario:** Un token capturado una vez (log de debug, máquina compartida, SP-02) sigue
  válido para siempre mientras se ejercite, y no puede envejecer salvo por un purgado admin de todas
  las filas viejas.
- **Potential impact:** Validez prolongada de un token robado. Bajo dado `HttpOnly`/`Secure`/`__Host-`
  y usuario único.
- **Existing mitigation:** Almacén server-side con chequeo de revocación por request; atributos de
  cookie; timeout de inactividad de 8 h; verbo `sesion purgar`; el reset de contraseña revoca todo.
- **Recommended remediation:** Limitar la renovación a una edad absoluta (`CreadaEn + N días`) —
  rechazar en `SqlSesionRepository.FindActiveAsync`/`RenewAsync`. `JOIN` a `fact.Usuario` en
  `FindActiveAsync` exigiendo fila existente y activa.
- **Suggested verification:** Test de que una sesión más vieja que el tope devuelve 401 aun con
  actividad reciente.
- **Required change type:** CODE FIX (+ pequeña nota de SPEC sobre vida absoluta)

---

**SP-13 — Sin manejador global de excepciones ni ProblemDetails; la exposición de detalle depende de `ASPNETCORE_ENVIRONMENT`, y una cookie malformada produce 500 en vez de 401**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Manejo de errores / divulgación de información
- **Affected artifact:** `api/SmartNet.Api/Program.cs`; `auth/SmartNet.Auth.Infrastructure/CsprngSessionTokenFactory.cs:32-37`
- **Location:** `Program.cs` (sin `UseExceptionHandler`/`AddProblemDetails`); `CsprngSessionTokenFactory.Base64UrlDecode`
- **Description:** No hay `app.UseExceptionHandler(...)` ni `AddProblemDetails()`. Una excepción no
  manejada cae al comportamiento por defecto del framework: 500 desnudo en Producción, pero **página
  de excepción de desarrollador con stack trace y texto SQL si `ASPNETCORE_ENVIRONMENT=Development`**.
  Como la config es solo por variables de entorno sin `appsettings.json`, un operador que deje esa
  variable puesta despliega errores verbosos. Aparte, `Base64UrlDecode` puede lanzar `FormatException`
  con un token de longitud ≡ 1 (mod 4); `RetrieveAsync`/`RemoveAsync` no lo capturan, así que un
  `__Host-session` basura produce 500 en vez de un 401 limpio.
- **Attack scenario:** Disparar cualquier excepción del servidor (cookie malformada, hipo de base) y
  leer stack traces / detalles de esquema cuando el entorno está mal configurado; usar el 500 de
  cookie malformada como oráculo de liveness/versión.
- **Potential impact:** Fuga de detalle interno; menor.
- **Existing mitigation:** La ausencia de `appsettings.json` reduce la probabilidad de un
  `Development` comiteado; la cookie normalmente va envuelta por DP.
- **Recommended remediation:** `app.UseExceptionHandler()` + `AddProblemDetails()` devolviendo un
  500 `application/problem+json` opaco. Envolver `Base64UrlDecode` para que un token indecodificable
  mapee a "sin sesión" (→ 401). Opcional: fallar el arranque si `IHostEnvironment.IsDevelopment()`
  en el artefacto desplegado.
- **Suggested verification:** Test posteando una cookie `__Host-session` basura → 401 sin stack
  trace; forzar una excepción de endpoint → 500 genérico.
- **Required change type:** CODE FIX

---

**SP-14 — `POST /api/sesion` sin protección anti-forgery (login CSRF, residual)**

- **Severity:** LOW · **Confidence:** LOW · **Category:** CSRF
- **Affected artifact:** `api/SmartNet.Api/SesionEndpoints.cs:22`
- **Location:** `SesionEndpoints.MapSesionEndpoints`
- **Description:** No hay token antiforgery (coherente con el diseño sin CORS). Los endpoints de
  negocio que cambian estado están adecuadamente protegidos (POST/PATCH/DELETE con cuerpo JSON;
  `SameSite=Lax` no adjunta la cookie a un `POST` cross-site). El caso residual es **login CSRF**: un
  `POST` de formulario cross-site de nivel superior a `/api/sesion` puede hacer que el navegador de
  la víctima *acepte* un `Set-Cookie` de una cuenta del atacante.
- **Attack scenario:** El atacante auto-envía un formulario a `https://smartnet.local/api/sesion` con
  sus propias credenciales; la víctima queda logueada en la cuenta del atacante e introduce datos de
  factura / documentos allí, que el atacante luego lee.
- **Potential impact:** Bajo — requiere que el hostname interno sea alcanzable desde el contexto del
  navegador de la víctima y un motivo plausible; operador único. El binder JSON de Minimal API exige
  `application/json`, que un formulario cross-site no puede fijar sin preflight CORS — esto atenúa
  sustancialmente el ataque.
- **Existing mitigation:** Cookie `__Host-`, `SameSite=Lax`, SPA de mismo origen, red interna, binder
  solo-JSON.
- **Recommended remediation:** Dado el binder solo-JSON + `__Host-` + Lax, es probablemente
  aceptable; documentarlo. Si se endurece: cookie de doble-envío / chequeo `X-Requested-With` que la
  SPA envía en el login.
- **Suggested verification:** Confirmar que `POST /api/sesion` con `Content-Type: application/x-www-form-urlencoded`
  devuelve 415/400, no 204.
- **Required change type:** ACCEPT RISK (documentar) o CODE FIX

---

**SP-15 — Texto de documento controlado por el atacante se persiste verbatim en columnas de error**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Manejo de salida / contenido almacenado que cruza frontera de confianza
- **Affected artifact:** `SmartNetWorker` — `ubl.py`, `pdf_lectura.py`, `cli_procesamiento.py`, escritores de `ProcesamientoError`/`ProcesamientoIntentos`/`EstadoIntegracion`
- **Location:** `src/smartnet_worker/ubl.py:114-123,165,171`; `src/smartnet_worker/pdf_lectura.py:100,137,147`; `src/smartnet_worker/cli_procesamiento.py:211`
- **Description:** Los mensajes de `UblInvalidoError`/`PdfIlegibleError`/`XMLSyntaxError` incrustan
  fragmentos del XML/PDF del atacante y del nombre de archivo. Esos strings van a
  `fact.ProcesamientoError.Mensaje`, `fact.ProcesamientoIntentos.Mensaje` y
  `fact.EstadoIntegracion.UltimoError`, que la API lee y la SPA renderiza.
- **Attack scenario:** El atacante crea un XML cuyo texto de `cbc:ID` es `<img src=x onerror=...>` o
  un payload de fórmula; falla la validación de identidad, el valor crudo se almacena y luego se
  renderiza. El impacto depende del escape del consumidor.
- **Potential impact:** XSS almacenado / inyección de fórmula en la UI del operador **si** un
  consumidor no escapa. **Mitigado hoy en la SPA** (renderiza por interpolación con auto-escape;
  verificado) y en `ExportadorXlsx` (celdas `InlineString`, `=` literal). Riesgo residual: cualquier
  consumidor futuro de esas columnas, y logs.
- **Existing mitigation:** Sanitización de nombre de archivo (`[A-Za-z0-9._-]`), truncado a 2000
  caracteres, `repr()` en algunos valores; auto-escape de Angular; celdas `InlineString` en el xlsx.
- **Recommended remediation:** Mantener los mensajes de error persistidos estructurales (tipo de
  error + código + identificadores seguros), no ecos crudos del contenido del documento; o
  documentar explícitamente en la SPEC que todo consumidor de `Mensaje`/`UltimoError` debe
  escapar HTML/CSV.
- **Suggested verification:** Test de que los mensajes de error de `ubl.parsear` no contienen texto
  de elemento crudo más allá de un token acotado y sanitizado.
- **Required change type:** SPEC CHANGE (definir qué puede persistirse)

---

**SP-16 — `gmail_message_id` usado sin sanitizar como segmento de ruta de filesystem**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Path traversal (hueco de defensa en profundidad)
- **Affected artifact:** `SmartNetWorker` — `gmail.ruta_relativa`, `almacenamiento.escribir`
- **Location:** `src/smartnet_worker/gmail.py:202-213`
- **Description:** Todos los demás componentes de la ruta de almacenamiento se sanitizan o son fijos;
  `m.gmail_message_id` (de `mensaje.get("id")` en el JSON de la API de Gmail) se inserta verbatim
  como nombre de directorio. Los IDs de Gmail no son controlables por el atacante en la práctica,
  pero el código trata la respuesta de la API como confiable para construir rutas.
- **Attack scenario:** Requiere una respuesta de la API de Gmail comprometida/falsificada (MITM pese
  a TLS, o un falso malicioso en un despliegue mal configurado) con un `id` que contenga `../`. El
  chequeo `is_relative_to(raiz_resuelta)` de `escribir` lo contiene hoy — nada se escribe — pero esa
  guarda es lo único entre el valor crudo y la resolución de `Path`.
- **Potential impact:** Ninguno bajo la contención actual; sería path traversal si `escribir` se
  debilitara.
- **Existing mitigation:** Chequeo de contención `resolve` + `is_relative_to` en `almacenamiento.escribir`
  (design.md Decisión 5, "defensa en profundidad").
- **Recommended remediation:** Pasar `gmail_message_id` por la misma sanitización
  (`_CARACTER_NO_PERMITIDO_RE`) o afirmar que cumple `^[A-Za-z0-9_-]+$` en `parsear_mensaje` o
  `ruta_relativa`.
- **Suggested verification:** Test de `ruta_relativa` con un id `../../etc` → el segmento se
  sanitiza (no solo que `escribir` lo rechace después).
- **Required change type:** CODE FIX

---

**SP-17 — El fetch a la SBS sigue redirecciones; el parser recibe lo que venga, sin tope de tamaño ni chequeo de content-type**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** SSRF / confianza en la respuesta
- **Affected artifact:** `SmartNetWorker` — `cli_tipo_cambio.py`, `sbs.py`
- **Location:** `src/smartnet_worker/cli_tipo_cambio.py:33`
- **Description:** La URL es una constante HTTPS fija (bien — no influida por el atacante, verificación
  TLS por defecto). Pero `requests` sigue redirecciones por defecto y no hay `allow_redirects=False`,
  ni chequeo de `Content-Type`, ni tope de tamaño antes de `parse_tipo_cambio(respuesta.text)`.
- **Attack scenario:** Si `sbs.gob.pe` es comprometido o hay hijack de DNS/BGP, puede redirigir el
  worker a un host arbitrario y/o devolver un cuerpo HTML de cientos de MB que BeautifulSoup parsea
  en memoria. TLS aún protege contra MITM pasivo. Impacto limitado: fallo de parseo → error
  registrado; un número inválido lo atraparían las reglas de dominio de la API y el parseo `Decimal`.
- **Potential impact:** Agotamiento de memoria ante una respuesta hostil/desmesurada; ingesta de un
  tipo de cambio falso (mitigado aguas abajo por la validación de la API y la búsqueda exacta de la
  fila `Dólar de N.A.`).
- **Existing mitigation:** URL fija, verificación TLS por defecto, timeout de 10 s, parseo estructural
  estricto, validación `Decimal(str())`, proceso de una sola ejecución.
- **Recommended remediation:** `allow_redirects=False` (o fijar el host permitido de la URL final),
  verificar `resp.headers["content-type"]` empieza por `text/html`, y acotar el tamaño del cuerpo
  antes de parsear.
- **Suggested verification:** Test alimentando una respuesta de redirección y un cuerpo desmesurado a
  un `requests` falso; afirmar rechazo antes de `parse_tipo_cambio`.
- **Required change type:** CODE FIX

---

**SP-18 — Recursión no acotada sobre partes MIME en `parsear_mensaje`**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** DoS (por mensaje)
- **Affected artifact:** `SmartNetWorker` — `gmail.py` (`_recorrer_adjuntos`)
- **Location:** `src/smartnet_worker/gmail.py:128-144`
- **Description:** Una estructura `multipart/*` profundamente anidada (controlada por el atacante)
  lleva la recursión de Python a `RecursionError`.
- **Attack scenario:** Un correo con ~1000+ niveles anidados. `parsear_mensaje` lanza `RecursionError`
  (no `ParseoGmailError`); `_procesar_mensaje` lo captura por mensaje, cuenta un fallo, la ejecución
  sale con 1. Sin caída global, sin corrupción. Gmail limita el anidamiento, así que es en gran
  medida teórico.
- **Potential impact:** Un mensaje falla por correo diseñado; ruido menor en los conteos de fallo.
- **Existing mitigation:** Aislamiento de excepción por mensaje en `cli_gmail._procesar_mensaje`.
- **Recommended remediation:** Pasar un contador de profundidad a `_recorrer_adjuntos`, lanzar
  `ParseoGmailError` pasado un límite sano (p. ej. 20).
- **Suggested verification:** Test con un árbol sintético de 100 de profundidad → esperar
  `ParseoGmailError`, no `RecursionError`.
- **Required change type:** CODE FIX

---

**SP-19 — El `returnUrl` del login no se valida como ruta local**

- **Severity:** LOW · **Confidence:** LOW · **Category:** Open redirect
- **Affected artifact:** `SmartNetWeb` — login
- **Location:** `SmartNet/SmartNetWeb/src/app/login/feature/login-page/login-page.ts:31-37,44`; `src/app/shared/auth.guard.ts:17`
- **Description:** `returnUrl` se lee verbatim del query string y se pasa a
  `this.router.navigate([this.returnUrl ?? '/bandeja'])` sin comprobar que es una ruta relativa de
  mismo origen (sin `//` inicial, sin esquema, sin `..`).
- **Attack scenario:** Enlace de phishing `…/login?returnUrl=https://evil.example/…`; si un refactor
  futuro cambia `navigate([...])` por `navigateByUrl(returnUrl)` o el valor llega a
  `window.location`, se vuelve un redirect funcional para cosechar credenciales re-introducidas.
- **Potential impact:** Phishing de credenciales (latente hoy).
- **Existing mitigation:** `router.navigate` con un array de un elemento se resuelve como ruta
  interna; las rutas sin coincidencia no navegan fuera del sitio.
- **Recommended remediation:** Validar antes de usar: aceptar solo valores que casen `^/(?!/)` y
  rechazar todo lo que contenga `://`; si no, `/bandeja`. Test unitario.
- **Required change type:** CODE FIX

---

**SP-20 — Endurecimiento de CI: sin bloque `permissions`, actions en tags mutables, caché compartida entre `pull_request` y `main`, `curl | sudo tee` al repo de Microsoft**

- **Severity:** LOW · **Confidence:** HIGH · **Category:** Cadena de suministro / mínimo privilegio en CI
- **Affected artifact:** `.github/workflows/ci.yml`
- **Location:** `ci.yml` — nivel superior (sin `permissions:`), todos los `uses: actions/*@v4|v5`, `on: pull_request:` sin filtro, job `pruebas-de-worker-python` step "Instalar sqlcmd"
- **Description:** Cuatro huecos de higiene: (1) sin `permissions:` el `GITHUB_TOKEN` toma el scope
  por defecto del repo (posible `contents: write` en `push` a `main`) — el workflow solo compila y
  testea. (2) Las actions se referencian por tag móvil (`@v4`/`@v5`), no por SHA. (3)
  `actions/setup-node` con `cache: 'npm'` puede permitir que un PR de fork que toque
  `package-lock.json` pueble una entrada de caché que un run posterior de la base restaure. (4) El
  job del worker añade la clave y el repo APT de Microsoft con `curl -fsSL … | sudo tee` y luego
  instala — dependencia dura de la integridad del CDN de Microsoft en tiempo de ejecución.
- **Attack scenario:** Una action o dependencia comprometida en un run de `main` usa el token ambiente
  para empujar commits/tags; un PR de fork envenena una caché de dependencias para un run confiable
  posterior.
- **Potential impact:** Manipulación del repo / integridad de release (acotado: los runs de
  `pull_request` de fork ya reciben token de solo lectura); dependencias manipuladas en un run
  posterior.
- **Existing mitigation:** GitHub da a los `pull_request` de fork token de solo lectura; `npm ci`
  verifica contra el lockfile; `pull_request` (no `pull_request_target`), así que no se filtran
  secretos a forks.
- **Recommended remediation:** Añadir `permissions: contents: read` a nivel de workflow; fijar
  actions a SHA completo con comentario de versión (Dependabot `github-actions`); condicionar el
  guardado de caché a `github.event_name == 'push'` y añadir `paths:`; vendorizar la clave
  `microsoft.asc` en el repo y fijar `mssql-tools18` a versión exacta, o hacer el chequeo de
  huérfanos vía `docker exec … sqlcmd` sin instalar las herramientas MS.
- **Suggested verification:** Inspeccionar el bloque "GITHUB_TOKEN Permissions" de un run → solo
  `contents: read`. Confirmar que las entradas de caché solo se escriben en runs de `main`.
- **Required change type:** PROCESS / HARNESS CHANGE

---

**SP-21 — El prototipo de diseño (`handoff/`) se publica en el repositorio público**

- **Severity:** LOW · **Confidence:** MEDIUM · **Category:** Superficie de ataque / exposición de información
- **Affected artifact:** `handoff/Gestor de Facturas.dc.html`, `handoff/support.js`, `handoff/DESIGN_BRIEF.md`
- **Location:** raíz del repo, `handoff/` (los cuatro archivos trackeados)
- **Description:** Un prototipo React autocontenido con un motor de plantillas `{{expr}}` propio
  (`support.js`: `template: dc.innerHTML`, `new DOMParser().parseFromString`, binding de handlers
  `onClick="{{...}}"`). Incluye campos de contraseña y un input "Token del bot" de Telegram. Los
  valores son placeholders vacíos (`tokenBot:''`, `chatId:''`) — **no hay secreto**. El riesgo es (a)
  documenta públicamente el mapa completo de pantallas / nombres de campo / lista de integraciones, y
  (b) si alguien despliega `handoff/` tal cual, su interpolación de expresiones en cliente sobre un
  blob `data-props` es un sink de inyección. No es parte del build de Angular (`angular.json` solo
  incluye `public/`).
- **Existing mitigation:** No está en el build; valores vacíos.
- **Recommended remediation:** Mover los artefactos de diseño fuera del repo publicado (o a una
  ubicación ignorada); si se conservan, añadir una nota de nivel superior de que `handoff/` es una
  maqueta no desplegable.
- **Required change type:** PROCESS / HARNESS CHANGE

---

**SP-22 — Sin escaneo de vulnerabilidades de dependencias en CI (.NET / npm / pip / actions)**

- **Severity:** LOW · **Confidence:** LOW · **Category:** Riesgo de dependencias
- **Affected artifact:** `.github/workflows/ci.yml`; manifiestos en todo el repo
- **Location:** `ci.yml` (ausencia de un step de auditoría)
- **Description:** Buena práctica observada: los `.csproj` fijan versión exacta; la SPA comitea el
  lockfile. Pero no hay `dotnet list package --vulnerable`, `npm audit`, ni auditoría de `pip` /
  `github-actions` en CI, así que un CVE nuevo en una dependencia fijada no se detecta.
  `Microsoft.Data.SqlClient` 7.0.2, `DocumentFormat.OpenXml` 3.3.0, etc. — este pase no puede
  confirmarlos contra un feed de avisos vivo.
- **Recommended remediation:** Añadir `dotnet list package --vulnerable --include-transitive` y
  `npm audit --production` como step no bloqueante en `verificaciones-estaticas`; habilitar
  Dependabot para `nuget` + `npm` + `pip` + `github-actions`.
- **Required change type:** PROCESS / HARNESS CHANGE

---

### INFO

---

**SP-23 — Parámetros de Argon2id en el piso recomendado, no por encima**

- **Severity:** INFO · **Confidence:** HIGH
- **Location:** `auth/SmartNet.Auth.Infrastructure/Argon2idPasswordHasher.cs:16-20` — `m = 19456 KiB (19 MiB)`, `t = 2`, `p = 1`, sal de 16 bytes, salida de 32.
- **Description:** Coinciden exactamente con el *mínimo* de OWASP. `FixedTimeEquals` para comparar
  (correcto), tamaños de sal/salida adecuados, el hash señuelo usa parámetros idénticos (correcto).
  No hay rehash-on-login (`Verify` solo devuelve Correct/Incorrect/Unreadable), así que un futuro
  aumento de parámetros no actualizará los hashes existentes hasta el reset de contraseña. Si el host
  tiene margen, subir `m` a 46–64 MiB añade colchón.
- **Required change type:** ACCEPT RISK / decisión de producto opcional

---

**SP-24 — La contraseña `sa` hardcodeada en CI está correctamente evaluada como no-secreto**

- **Severity:** INFO · **Confidence:** HIGH
- **Location:** `.github/workflows/ci.yml` — `MSSQL_SA_PASSWORD: 'CiSoloEfimero_2026!'` (dos bloques de servicio), health-cmd, `SMARTNET_TEST_MASTER_CONNECTION`, `SMARTNET_WORKER_TEST_LOGIN_PASSWORD`
- **Description:** La justificación es válida: es el `sa` de un contenedor de servicio que solo existe
  para el job, alcanzable solo en la red del job (`localhost:1433`), aloja solo bases
  `fact_test_<guid>` desechables, y se destruye con el job. Comitearla (vs. `secrets.*`) es
  preferible aquí: mantiene la credencial desechable visiblemente inerte. El barrido de secretos en
  todo el repo (`password`, `secret`, `client_secret`, `BEGIN … PRIVATE KEY`, `AKIA`, …) **no
  encontró ninguna credencial real**. No hay `.pfx`/`.pem`/`.key`/`client_secret*.json`/`.env`
  trackeado.
- **Required change type:** ACCEPT RISK (ninguna acción; opcional: comentario que referencie ADR 0015)

---

**SP-25 — El parser XML/UBL del worker está correctamente blindado (verificado)**

- **Severity:** INFO · **Confidence:** HIGH
- **Location:** `src/smartnet_worker/ubl.py:79-85` (`_PARSER`), `:163` (`etree.fromstring(datos, parser=_PARSER)`)
- **Description:** `resolve_entities=False` (billion laughs / blowup cuadrático bloqueados),
  `no_network=True` + `load_dtd=False` (DTD externa / entidad de parámetro externa / SSRF-vía-entidad
  bloqueados), `huge_tree=False` (tope de nodos/profundidad). XPath siempre literal con namespace map
  fijo — sin inyección de XPath. Sin XSLT. Endurecimiento opcional: tope de bytes previo a
  `fromstring` (liga con SP-06) y rechazo explícito de cualquier `<!DOCTYPE`.
- **Required change type:** ACCEPT RISK (opcional CODE FIX menor)

---

**SP-26 — Confirmaciones positivas adicionales (sin acción)**

- `RECONECTAR_GOOGLE` / `CommandQueue`: `EncolarAsync` pasa payload `"{}"` hardcodeado y `Tipo`
  whitelisted; `{nombre}` mapeado por allow-list (`gmail`/`sbs`), resto 404. Sin inyección de payload.
- El worker nunca `eval`/`pickle`/`json.loads`-ejecuta payloads de cola. `RECONECTAR_GOOGLE` solo
  hace `UPDATE … SET FallosSeguidos = 0` (idempotente); `REPROCESAR_DOCUMENTO` solo pone una fila a
  `PENDIENTE` por id parametrizado. Ambas tablas de cola son de escritura restringida a `usr_api`.
- Inyección de cabeceras SMTP: `Subject` constante; `From`/`To` de config; cuerpo por
  `EmailMessage.set_content` (codificación segura). El texto influido por el atacante solo llega al
  cuerpo.
- TLS: sin `verify=False`, sin `ssl._create_unverified_context`, sin endpoints `http://`. SMTP usa
  `starttls()`.
- API: sin `BinaryFormatter`, sin JSON polimórfico; `PayloadInboxParser` usa `System.Text.Json` sobre
  contenido de base confiable. PATCH bindea records dedicados campo a campo — sin mass assignment.
  `*Options.Resolve` lanza con el *nombre* de la variable, nunca el valor.
- `smartnet-admin`: `ConsolePasswordPrompt` con `Console.ReadKey(intercept: true)` (sin eco); no
  existe flag de contraseña en argv (guardado por `AdminArgumentsTests`); `restablecer-clave` revoca
  todas las sesiones.
- Matriz de permisos de base (`008` + `011`): `fact_api` sin acceso a tablas de ingesta (`DENY`
  cruzado), `DELETE` solo sobre `fact.Sesion`, `dbo.*` solo `SELECT` a nivel de objeto. Coherente con
  ADR 0003.

---

## Prioridad

Orden recomendado de atención (no idéntico a la severidad individual — hay dependencias):

1. **SP-01** (token de Telegram) — fuga de credencial activa que se dispara con errores rutinarios de
   Telegram. Corrección pequeña y contenida. Primero.
2. **SP-03** (usuario desactivado autentica) + **SP-12** (sin tope de sesión) — misma zona de código
   (`AccessPolicy` / `FindActiveAsync`), se arreglan juntos. Cierra un bypass de autenticación
   latente antes de cualquier cambio a multiusuario.
3. **SP-02** (key ring sin cifrar) — `ProtectKeysWithDpapi()` + `SetApplicationName()` es una
   línea; el ACL del directorio necesita la decisión de despliegue (ver Gobernanza).
4. **SP-06** (límites de recursos del worker) — protege la ingesta, de la que dependen dos criterios
   de éxito del PRD. Necesita fijar los valores de límite (SPEC).
5. **SP-04** (defensa del visor) — `sandbox=""` en el iframe y validación de `RegistrarAdjuntoRequest`
   son CODE FIX inmediatos; el origen separado para documentos es decisión de arquitectura.
6. **SP-05** (cabeceras / CSP) — el middleware de cabeceras de la API es CODE FIX; la CSP completa
   entra en el ADR de proxy pendiente.
7. **SP-07** (lockfile del worker) + **SP-08** (`.gitignore`) + **SP-20** / **SP-22** — endurecimiento
   de cadena de suministro y CI; agrupables en un solo cambio de proceso.
8. **SP-11**, **SP-13**, **SP-09**, **SP-10** — endurecimiento de la API; `FallbackPolicy` y el
   manejador de excepciones son baratos y de alto valor defensivo.
9. **SP-15..SP-19**, **SP-21**, **SP-14**, **SP-23** — correcciones puntuales y de documentación,
   sin urgencia.

---

## Gobernanza / Decisión requerida

Los siguientes findings **no pueden resolverse sin una decisión humana** de producto o arquitectura:

- **SP-02 — DESIGN / ADR CHANGE.** El cifrado del key ring en reposo y, sobre todo, el ACL exacto del
  directorio `SMARTNET_API_KEYRING_PATH` y quién lo aplica son una decisión de despliegue. Debe
  entrar en el ADR 0012 (topología de despliegue) junto con el origen del certificado TLS que ya
  está pendiente allí.
- **SP-04 — DESIGN / ADR CHANGE.** ADR 0013 ratifica que el visor de documentos se sirve desde el
  **mismo origen** que la SPA. Servirlo desde un origen separado (sandbox domain) elimina de raíz la
  clase de riesgo, pero contradice una decisión aceptada y tiene costo (segundo host, segunda ruta
  de proxy). La alternativa —quedarse en mismo origen con `sandbox` + allow-list de MIME reforzada—
  es CODE FIX pero deja el riesgo residual. Elegir entre ambas es del dueño de la arquitectura.
- **SP-05 — DESIGN / ADR CHANGE.** La Content-Security-Policy y las cabeceras `Strict-Transport-Security`
  dependen de dónde se terminan (API vs. proxy inverso). El ADR del proxy inverso/TLS está sin
  decidir (ADR 0012); la CSP debe plegarse en esa misma decisión.
- **SP-06 — SPEC CHANGE (parcial).** Los valores concretos de `MAX_ADJUNTO_BYTES`, el presupuesto de
  píxeles por página y el timeout por documento son decisiones de producto (¿cuál es la factura más
  grande legítima?). El código para *aplicarlos* es CODE FIX; los *números* necesitan quien conozca
  el negocio.
- **SP-09 — DESIGN / ADR CHANGE (si se delega al proxy).** Rate limiting en la aplicación vs. en el
  proxy inverso. Si se decide que el proxy es la capa responsable, hay que escribirlo (como ya se
  hizo con "mismo origen" y "TLS").
- **SP-10 — ACCEPT RISK (posible).** Los canales laterales de tiempo del login: dado el contexto de
  un solo usuario en red interna, aceptar el riesgo es defendible. Requiere una decisión explícita
  documentada en ADR 0007, no dejarlo implícito.
- **SP-14 — ACCEPT RISK (probable).** Login CSRF residual: el binder solo-JSON lo atenúa fuertemente.
  Aceptar el riesgo es razonable, pero debe quedar escrito.
- **SP-15 — SPEC CHANGE.** Definir qué se permite persistir en las columnas de error del worker
  (¿ecos crudos de contenido, o solo tipos/códigos estructurales?) es una decisión de contrato entre
  el worker y sus consumidores.
- **SP-21 — PROCESS / HARNESS CHANGE.** Decidir si los artefactos de diseño (`handoff/`) deben vivir
  en el repositorio público es una decisión del equipo.

El resto de los findings (SP-01, SP-03, SP-07, SP-08, SP-11, SP-12, SP-13, SP-16, SP-17, SP-18,
SP-19, SP-20, SP-22) son **CODE FIX**, **TEST FIX** o **PROCESS / HARNESS CHANGE** y no requieren una
decisión de producto o arquitectura.
