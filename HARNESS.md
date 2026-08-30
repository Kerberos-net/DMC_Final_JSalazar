# HARNESS.md

Harnesses activos en este repo. Un harness es un flujo chico de un solo proposito,
armado con la combinacion de skill / regla / hook que la tarea necesita.

---

## lecciones-aprendidas — captura de aprendizajes al cerrar un ciclo SDD

**Proposito (uno solo):** cada vez que termina el flujo SDD (despues de
`sdd-archive`), listar los conceptos nuevos que se aplicaron en ese cambio y,
segun lo que decida el usuario, escribir una nota en el vault de Obsidian con la
etiqueta `lecciones-aprendidas`.

**Bundle:** `harnesses/lecciones-aprendidas/`

### Que hace, paso a paso

1. **Disparo automatico (hook `Stop`).** `harnesses/lecciones-aprendidas/detect-archive.js`
   corre al final de cada turno. Compara las carpetas de
   `openspec/changes/archive/` contra el marcador `.claude/.lecciones-aprendidas-seen`.
   - Primera corrida: siembra el marcador con todo el historial y no dispara.
   - Carpeta nueva detectada: actualiza el marcador y devuelve
     `{"decision":"block"}` con instrucciones para que Claude ejecute la skill
     antes de cerrar el turno. Guardar el marcador antes de bloquear evita el
     bucle en el `Stop` siguiente.
2. **Revision (skill `lecciones-aprendidas`).** Lee la carpeta archivada del
   cambio + `REGLAS.md` + `CONVENTIONS.md` y arma una **lista numerada** de
   conceptos nuevos aplicados, en dos categorias:
   - Patrones tecnicos / convenciones.
   - Reglas contables de `REGLAS.md`.
   (ADRs y decisiones de `design.md` quedan fuera salvo pedido explicito.)
3. **Seleccion del usuario.** Claude presenta la lista y **espera** que el usuario
   elija cuales documentar (todos, algunos por numero, o ninguno).
4. **Borrador y aprobacion.** Claude redacta la nota y la muestra completa. No
   escribe nada hasta que el usuario apruebe el contenido.
5. **Escritura.** Genera `D:\Notas\Kerberos\Lecciones aprendidas\<Nombre Descriptivo>.md`
   con:
   - nombre de archivo descriptivo del tema (ej. `Contabilidad por destino.md`),
   - etiqueta en **frontmatter** (`tags: [lecciones-aprendidas]`) **y** en el
     cuerpo (`#lecciones-aprendidas`),
   - secciones: Contexto / Patrones tecnicos / Reglas contables / Para recordar.

### Guardrails (duros, en la skill)

- Nunca sobrescribe una nota existente con ese nombre sin preguntar.
- Nunca escribe en el vault (`D:\Notas\Kerberos`) hasta que el usuario apruebe el
  contenido final.
- Nunca modifica archivos del repo (codigo, specs, openspec, ADRs). El flujo es de
  solo lectura sobre el proyecto; solo escribe en el vault.

### Piezas

| Pieza | Archivo | Rol |
|---|---|---|
| Hook `Stop` | `harnesses/lecciones-aprendidas/detect-archive.js` | Detecta el archive nuevo y pide correr la skill. |
| Skill | `harnesses/lecciones-aprendidas/SKILL.md` (instalada en `.claude/skills/lecciones-aprendidas/`) | Revision, seleccion y escritura de la nota. |
| Estado | `.claude/.lecciones-aprendidas-seen` (git-ignored) | Marcador de carpetas de archive ya vistas. |
| Registro hook | `.claude/settings.json` | Registra el hook `Stop`. |

### Estado de activacion en esta maquina

- Skill instalada en `.claude/skills/lecciones-aprendidas/SKILL.md` — invocable ya.
- Hook registrado en `.claude/settings.json` — activo (aplica en la proxima sesion
  de Claude Code; reinicia la sesion para cargarlo).
- Marcador sembrado con los 17 archives existentes — no disparara con historial.
- `/lecciones-aprendidas` disponible para correr a mano cuando quieras.

### Uso manual

`/lecciones-aprendidas` en cualquier momento: toma la carpeta archivada mas
reciente, la confirma, y sigue desde el paso 2.

### Setup en otra maquina

Ver `harnesses/lecciones-aprendidas/README.md`.

---

## integration-spa-api — la SPA y la API funcionan juntas sobre HTTP

**Proposito (uno solo):** comprobar que `SmartNetWeb` y `SmartNetApi` integran
correctamente sobre el contrato `/api/*`, con componentes reales siempre que sea
razonable y sin mocks que terminen probando solo mocks. No cubre el worker, el
volumen de adjuntos ni el navegador real.

**Bundle:** `SmartNet/harnesses/integration-spa-api/` (cross-repo: vive en la
carpeta padre `SmartNet/`, no dentro de un repo hijo).

### Que hace

Es **solo una skill** — doctrina + checklist, sin hook ni regla. Bajo pedido
(`/integration-spa-api`):

1. Lee el contexto: `SmartNet/CLAUDE.md`, `SmartNet/SmartNetApi/CLAUDE.md`, los
   tests HTTP existentes (`SmartNetApiFactory`, `SesionEndpointsTestBase`,
   `Bandeja/Factura/Asiento/ConcurrenciaDosClientes...Tests`) y el cliente HTTP
   real de la SPA para cada flujo.
2. Verifica los flujos en alcance ejercitando la **API real**
   (`WebApplicationFactory<Program>`) sobre HTTP, con cookie de sesion real y base
   `fact_test_<guid>` desechable con el esquema versionado real:
   - **Sesion / login / 401** — cookie `__Host-session`, invalidacion server-side,
     401 con status plano.
   - **Bandeja + detalle de factura** — `GET /api/bandeja` con filtros,
     `/api/factura/{id}`, `/api/asiento/{id}`, ETag en header, `If-Match`
     desactualizado -> 412, id inexistente -> 404.
   - **Consultas de catalogos (BACKLOG #22)** — `GET /api/catalogos/plan-contable`,
     `GET /api/catalogos/proveedores?modo=catalogo` (envelope `PaginaBandeja`,
     regresion picker `{resultados,hayMas}` byte-frozen #18),
     `GET /api/tipos-cambio?desde=&hasta=` + las 3 rutas `/exportacion` (`.xlsx`,
     `attachment`, nombre constante). Solo lectura, camelCase, 401 sin cookie.
3. Chequea que lo que la SPA **espera** (ruta, headers, forma de payload) coincide
   con lo que la API responde.
4. Marca brechas de sobre-mockeo (flujo cubierto solo con backend stub, o sin HTTP
   real).

### Doctrina real vs doble

- **Siempre real:** host de la API (`WebApplicationFactory<Program>`), base
  `fact_test_<guid>` con esquema del runner, login y cookie de sesion, Argon2id,
  Data Protection key ring, logica de dominio.
- **Dobles permitidos (solo borde externo de la API):** `FakeTimeProvider`,
  decorador contador de `IPasswordHasher`, dir temporal de storage.
- **Prohibido:** mockear el backend HTTP del lado SPA, repos `fact.*` en memoria,
  saltear el login, correr contra `BDSmartNet`, servicios externos reales
  (Gmail / SBS / SMTP / Telegram).

### Salida

Reporte pass/fail por flujo en el chat: resumen, flujos con evidencia
(status/headers/estado de DB), chequeo de contrato SPA<->API, brechas, y seccion
"dobles usados vs componentes reales". Sin SQL Server local -> `BLOCKED`, nunca un
PASS inventado.

### Guardrails (duros, en la skill)

- No introducir dependencias nuevas (Playwright, Testcontainers, WireMock, ...).
- No modificar tests ni codigo existente sin aprobacion — reporta y propone.

### Piezas

| Pieza | Archivo | Rol |
|---|---|---|
| Skill | `SmartNet/harnesses/integration-spa-api/SKILL.md` (instalada en `.claude/skills/integration-spa-api/`) | Doctrina, flujos, procedimiento, reporte. |

### Estado de activacion en esta maquina

- Skill instalada en `.claude/skills/integration-spa-api/SKILL.md` — invocable ya
  con `/integration-spa-api`.
- Sin hook ni regla que registrar.
- Prerrequisito de corrida: SQL Server local + `dotnet` en el PATH.

### Setup en otra maquina

Ver `SmartNet/harnesses/integration-spa-api/README.md`.
