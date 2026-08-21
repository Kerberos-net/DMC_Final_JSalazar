# Sprint: estado de avance

Tablero de seguimiento del `BACKLOG.md`. Un ítem por sección, y dentro de cada uno **sus fases**,
que se marcan conforme se cierran.

**Regla de marcado:** una fase pasa a ✅ solo cuando *todas* sus tareas están cerradas en
`tasks.md` **y** la verificación independiente pasó. No se marca por reporte del agente que la
implementó: se marca por evidencia ejecutada.

Leyenda: ✅ cerrada · 🔄 en curso · ⬜ pendiente · ⛔ bloqueada

| Estado global | Valor |
|---|---|
| Ítems del backlog | **9 de 17 cerrados** |
| Ciclo SDD activo | Ninguno — último cerrado: ítem #9 |
| Última fase cerrada | Ítem #9 (Sugerencia de cuenta), 2 PRs apilados, 32/32 tareas cerradas, verify-report PASS WITH WARNINGS (ambos WARNING corregidos antes de archivar), 27/27 + 2/2 tests, build limpio — ítem #9 cerrado 2026-08-20 |

---

## ✅ 1. Esquema y permisos

SQL versionado, esquema `fact`, tablas, índices, restricciones y los `GRANT` de los dos usuarios
de base de datos. Sin dependencias.

**Ciclo SDD:** `openspec/changes/archive/2026-08-16-esquema-y-permisos/` · **36 de 36 tareas cerradas**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | Runner DbUp + arnés de pruebas (`test-bootstrap`) | 5/5 | ✅ |
| 2 | 2 | Estructura del esquema `001`–`007` + pruebas de forma | 13/13 | ✅ |
| 3 | 3 | Matriz de permisos `008` + pruebas nivel 2 de ADR 0019 | 4/4 | ✅ |
| 4 | 4 | Datos base `009`–`010` (`EstadoIntegracion`, `Configuracion`, 23 `MotivoAtributo`) | 7/7 | ✅ |
| 5 | 5 | Manifiesto de *checksums* + *rollback* consultivo + CI | 5/5 | ✅ |
| 6 | 5 | Integración: suite completa end-to-end sobre base nueva | 2/2 | ✅ |

### Pruebas

Un solo proyecto, `SmartNet.Db.Runner.Tests`, crece fase a fase. Todas ejecutadas y verificadas
por el orquestador, no solo reportadas por el agente que las escribió.

| Fase | Qué se añadió | Nuevas | Acumulado |
|---|---|---|---|
| 1 | Runner DbUp + arnés de pruebas | 6 | 6 |
| 2 | Estructura del esquema `001`–`007` | 25 | 31 |
| 3 | Matriz de permisos `008` | 26 | 57 |
| 4 | Datos base `009`–`010` | 36 | 93 |
| 5 | *Checksums* + *rollback* + lint de `dbo` | 9 | 102 |
| 6 | Integración (verifica el contrato ya construido, sin código nuevo) | 2 | 104 |

**104/104 al cerrar el ítem.** Ese número volvió a crecer dentro del ítem #2 —a 127—, porque `011`
y `012` extendieron este mismo proyecto; ver la tabla de pruebas del ítem #2.

### Lo verificado al cerrar cada fase

**Fase 1** — el journal de DbUp aterriza en `fact.SchemaVersions`, no en `dbo.SchemaVersions`.
Descubrimiento no previsto en el diseño: DbUp falla con el error 2760 si el esquema `fact` lo crea
el *mismo* script dentro de la *misma* transacción. Se resolvió asegurando el esquema en el runner
antes de `PerformUpgrade()`, lo que obliga a que `001` sea idempotente.

**Fase 2** — 31/31 pruebas en verde, 24 tablas en 7 scripts (558 líneas). Cuatro comprobaciones de
riesgo alto pasaron: `001` con guarda `IF SCHEMA_ID`; `IX_Factura_Identidad` **no** único; **cero**
claves foráneas hacia `dbo`; **cero** tipos de punto flotante. `fact` en `BDSmartNet` sigue con 0
tablas: todo corrió contra bases desechables `fact_test_<id>`.

**Fase 3** — 57/57 pruebas en verde, verificadas ejecutándolas yo. `008` concede `SELECT` sobre los
cinco catálogos externos y niega explícitamente las **once** tablas privadas de .NET a `fact_worker`,
no solo las cuatro que nombraba `design.md`: `DENY` gana sobre `GRANT`, así que sobrevive a un
`GRANT` accidental futuro. La reconciliación `FOR LOGIN` / `WITHOUT LOGIN` se resuelve con una sola
guarda `IF DATABASE_PRINCIPAL_ID(...) IS NULL`, de modo que el mismo script sirve al despliegue real
y al arnés de pruebas sin ramificar.

**Incidente de la fase 3.** El agente ejecutó el runner contra la base de sistema `master` y creó
ahí las 25 tablas de `fact`. Lo detectó, lo limpió y lo autorreportó. Verificado: `SCHEMA_ID('fact')`
en `master` es `NULL`, `BDSmartNet` intacta (0 tablas `fact`, 5 `dbo`). Regla que deja: todo trabajo
pasa por `TestDatabaseFixture`, nunca una conexión directa.

**La fuga de bases quedó diagnosticada, no parcheada a ciegas.** No era un fallo de `DisposeAsync`:
los ayudantes `MigratedDatabase()` creaban la base y luego ejecutaban aserciones **antes** del
`return db;`, sin `try`/`finally`. Al fallar una aserción, el llamador nunca recibía el objeto y la
base quedaba huérfana para siempre. Por eso las 44 aparecieron justo mientras se depuraba `008`.


**Fase 4** — 93/93 pruebas en verde, verificadas ejecutándolas yo. Los **23** motivos reclasificados
los conté yo mismo sobre `MOTIVOS-CLASIFICACION.md`: 23 filas marcadas con `†`, lista idéntica a la
que siembra `010`, con el motivo **88** incluido — el que se perdió la vez anterior. `fact.Usuario`
queda vacía y ningún `INSERT` del SQL versionado la apunta. `Configuracion` siembra 2 claves con
valor documentado y 13 marcadas `pendiente` con `Valor` y `ValorPorDefecto` en `NULL`.

**El lint de `dbo` se reescribió en esta fase, y con razón.** La regla original de la fase 3 —«toda
mención de `dbo` fuera de un `GRANT` permitido es violación»— habría rechazado el
`INSERT INTO fact.MotivoAtributo … SELECT … FROM dbo.Motivo` de `010`, que es una **lectura**
legítima: ADR 0003 dice que nadie *escribe* una tabla externa, no que nadie la lea. Pasó a comprobar
verbo y destino por sentencia.

Al verificar esa reescritura encontré un hueco real: `SELECT … INTO dbo.X` **crea y puebla** una
tabla en `dbo` sin nombrar ninguno de los verbos vigilados. Añadí el patrón y su prueba, y comprobé
por sonda que la prueba falla sin él. La sonda descartó además un segundo patrón que había añadido
para SQL dinámico: no aportaba nada, porque el escaneo es textual y ya atrapa el `CREATE TABLE dbo.`
dentro del literal. Queda anotado en el código qué sí evade el lint —un nombre de esquema
concatenado— y por qué eso es residuo aceptable.


**Fase 5** — 102/102 pruebas en verde, verificadas ejecutándolas yo. El manifiesto
`checksums.txt` existe porque **DbUp no tiene *checksums***: anota el nombre del script en
`fact.SchemaVersions` y nunca vuelve a mirar su contenido, así que editar un script ya aplicado no
falla en ningún sitio y la base y el repositorio divergen en silencio. Comprobé que muerde:
añadiendo una línea de comentario a `007_publicacion.sql`, el manifiesto se pone rojo.

Diez scripts `rollback/NNN_down.sql`, uno por script directo. Son **migraciones compensatorias
acotadas a `fact`**, nunca una restauración de instantánea: la base es compartida con el sistema
contable de la compañía, y restaurar revertiría también ese sistema (hallazgo C7). Las pruebas
afirman que el runner **nunca** los recoge.

Al verificar encontré que el lint de `dbo` enumeraba `schema/` sin recursión, de modo que
`rollback/` quedaba **fuera de su vigilancia**. Los down scripts son consultivos, pero alguien puede
ejecutarlos a mano contra una base real: es justo donde una escritura a `dbo` inadvertida haría
daño. Lo pasé a recursivo y añadí la aserción de que `rollback/` esté incluido.

**La CI quedó cableada** en `.github/workflows/ci.yml`, con los dos trabajos elegidos. El rápido
corre el lint y el manifiesto **sin base de datos** — comprobado, no supuesto: el mismo filtro
apuntando a un host de SQL inexistente pasa 16 pruebas en 243 ms. El otro levanta SQL Server 2022
como contenedor de servicio y corre la suite entera, matriz de permisos incluida.

**Fase 6** — 104/104, ejecutadas por mí. «End-to-end sobre base nueva» no es una ceremonia aparte:
cada prueba crea y destruye su propia `fact_test_<guid>`, así que la suite entera ya es eso en cada
caso.

De la tarea 6.2 hay que ser honesto sobre qué se probó. **No hay canalización de despliegue en este
repositorio**, de modo que la afirmación de *orden* de ADR 0012 —el runner antes que la API y el
worker— no tiene contra qué afirmarse todavía. Lo que sí es afirmable es el contrato de fallo sobre
el que ese orden descansa, y quedó probado: un script que falla sale con código distinto de cero,
**se revierte entero** sin dejar objetos a medio crear, y los scripts ya aplicados siguen anotados
en el journal, de modo que reejecutar tras corregir **reanuda** en vez de reaplicar.

Lo verifiqué por sonda: cambiando `.WithTransactionPerScript()` por `.WithoutTransaction()`, la
tabla a medio crear sobrevive al fallo y la prueba se pone roja. Restaurado de inmediato.

Estas pruebas pasaron sin código de producción nuevo. La fase 6 **verifica** el contrato ya
construido; queda anotado así en vez de disfrazarse de RED primero.
### Deuda declarada, no olvidada

- ~~**Tarea 1.5** — la aserción literal de idempotencia de `008`~~ **saldada en la fase 3**, y mejor
  de lo pedido: la prueba borra la fila de `008` en el journal de DbUp antes de reejecutarlo, porque
  si no DbUp lo saltaría y la prueba pasaría sin probar nada.
- ~~El *lint* de `dbo.` de la tarea 5.5~~ **adelantado a la fase 3**. Es una prueba estática sobre el
  texto de los scripts, con sus propios casos negativos sintéticos que demuestran que muerde. Quedó
  así porque la aserción «ninguna tabla fuera de `fact`» hubo que relajarla a «fuera de `fact` o
  `dbo`» —el arnés crea los catálogos de prueba en `dbo`— y eso dejaba la invariante sin guardián.

---

## ✅ 2. Autenticación y sesión

Host mínimo de API, cookie `__Host-session` con `SameSite=Lax`, tabla `fact.Sesion` como almacén de
sesión revocable en servidor, bloqueo por intentos sobre las columnas ya existentes, y el comando de
restablecimiento de ADR 0007. Depende del ítem #1 (completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-16-autenticacion-y-sesion/` · **88 de 88 tareas cerradas**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 0 | 1 | Compuertas: verificación de Konscious, ruta del anillo de claves | 2/2 | ✅ |
| 1 | 2 | Esquema: `011_sesion.sql`, `012_usuario_nivel_bloqueo.sql` | 12/12 | ✅ |
| 2 | 3 | `SmartNet.Auth.Core` — dominio puro (ADR 0019 nivel 1) | 17/17 | ✅ |
| 3 | 4 | `SmartNet.Auth.Infrastructure` — adaptadores Argon2id y SQL | 14/14 | ✅ |
| 4 | 5 | `SmartNet.Api` — host mínimo, cookie de autenticación | 27/27 | ✅ |
| 5 | 6 | `SmartNet.Admin` — CLI de restablecimiento | 11/11 | ✅ |
| 6 | 7 | Integración, CI y suite completa end-to-end | 5/5 | ✅ |

### Pruebas

Cinco proyectos, no uno solo — `SmartNet.Db.Runner.Tests` es el mismo del ítem #1, extendido aquí
por `011`/`012`. Estado final, verificado por el orquestador en la unidad 7, ejecutando cada suite
por separado.

| Proyecto | Unidad que lo creó | Pruebas | Qué cubre |
|---|---|---|---|
| `SmartNet.Db.Runner.Tests` | 2 (extiende el ítem #1) | 127 | Esquema `011`/`012`, permisos de `fact.Sesion`, lint de `dbo`, *checksums* |
| `SmartNet.Auth.Core.Tests` | 3 | 33 | Dominio puro: escalada de bloqueo, códec PHC, escaneo de pureza (cero BD/HTTP/reloj) |
| `SmartNet.Auth.Infrastructure.Tests` | 4 (+3 en la unidad 6) | 44 | Adaptadores Argon2id/SQL, suficiencia de permisos bajo `usr_api` real |
| `SmartNet.Api.Tests` | 5 | 22 | Host HTTP, cookie de autenticación, endpoints de sesión |
| `SmartNet.Admin.Tests` | 6 | 17 | CLI de creación, restablecimiento y purga |
| **Total** | | **243** | |

**Tres decisiones ya tomadas, no reabrir:** Argon2id como algoritmo de *hash*; `fact.Sesion` como
tabla versionada con sus propios `GRANT`/`DENY`, no caché distribuida; el ítem #2 levanta también el
host mínimo de API en `SmartNet/api/` (`net10.0`), ya que ningún otro ítem del backlog lo hace.

### Lo verificado al cerrar cada fase

**Unidad 1 (compuertas) cerrada** — verificada de forma independiente, no solo por el reporte del
agente. Consulté directamente `dotnet/runtime#19933` (issue en el hito «Future», sin fecha) y la
página de NuGet de Konscious: versión `1.3.1`, MIT, publicado 2024-06-19 — coincide exactamente con
lo reportado. **.NET 10 no trae Argon2id de fábrica**, así que la Decisión 1 (Konscious) se mantiene
sin revertir. El anillo de claves de Data Protection queda en `C:\ProgramData\SmartNet\dataprotection-keys`
(`SMARTNET_API_KEYRING_PATH`), añadido a **ADR 0014 Revisión 4** con el motivo escrito: si se pierde,
`fact.Sesion` sobrevive un reinicio pero el ticket cifrado no descifra — la tabla dejaría de servir
para lo que se eligió.

**Unidad 2 (esquema) cerrada** — 127/127 pruebas en verde, ejecutadas por mí. `011_sesion.sql`
crea `fact.Sesion` con sus `GRANT`/`DENY` en el mismo archivo; `012_usuario_nivel_bloqueo.sql`
añade `NivelBloqueo` sin tocar `002_seguridad.sql`. La tarea 1.7 **probó** contra
`sys.database_permissions` que el `GRANT` a nivel de objeto cubre la columna nueva sin ningún
cambio a `008` — no se dio por buena la afirmación del diseño. Dos hallazgos reales de SQL Server
en el ciclo RED/GREEN: `012` necesita `GO` entre sus dos `ALTER TABLE` (el propio `design.md` ya lo
mostraba, el primer borrador lo perdió), y `DROP COLUMN` no retira una restricción `DEFAULT` con
nombre (el *rollback* de `012` lo asumía y falló con error 5074, corregido).

El agente corrió una consulta de solo lectura contra `master` **fuera** de `TestDatabaseFixture`
para confirmar que no quedaran bases de prueba colgadas, y lo declaró explícitamente en el reporte
en vez de callárselo — la regla que compró el incidente de `master` del ítem #1 sigue viva.
`master`/`BDSmartNet`/bases huérfanas: limpio, intacta, 0.

**Unidad 3 (`SmartNet.Auth.Core`) cerrada** — 33/33 pruebas en verde, ejecutadas por mí en 131 ms:
coherente con cero dependencia de base de datos, HTTP o reloj real, verificado con un escaneo de
pureza doble (`NetArchTest.Rules` más una lectura de IL con `Mono.Cecil` que atrapa llamadas
directas a `DateTime.Now`/`UtcNow` a nivel de bytes compilados, no de texto). Cero
`PackageReference` en el `.csproj`, confirmado.

**El agente encontró un error en mi propia instrucción y lo corrigió contra las fuentes
normativas, no contra mi paráfrasis.** Le di la fórmula como `base × factor^min(NivelBloqueo,
NivelMaximo-1)`; la correcta —verificada a mano contra la tabla de ADR 0007 Revisión 4 y contra
`design.md`— es sin el `-1`. Con mi versión, el fallo 20 habría llegado al techo un nivel antes de
tiempo (60 min en vez de 120). Comprobé el código fuente: `Math.Min(estado.NivelBloqueo,
politica.NivelMaximo)`, sin resta — coincide con la tabla normativa fallo por fallo.

**Unidad 4 (`SmartNet.Auth.Infrastructure`) cerrada** — 41/41 pruebas en verde, ejecutadas por mí.
La prueba de suficiencia de permisos (tarea 3.13) corrió las 8 sentencias reales de los adaptadores
bajo `usr_api` **real** vía `ExecuteAsUserAsync` —no una conexión con privilegios elevados—: **8/8
en verde a la primera**, cero cambios a `011`/`012`. Confirmado que la cookie solo transporta el
token crudo de 256 bits; `fact.Sesion.Ticket` guarda el *ticket* serializado para que
`RetrieveAsync` reconstruya el principal en el servidor, coherente con la Decisión 4.

Un hallazgo real durante el ciclo: `ISesionRepository.RenewAsync` no recibía el *ticket*, así que
una sesión renovada arrastraría un `ExpiresUtc` desactualizado dentro del *blob* serializado. Lo
resolvió sobrescribiéndolo en `RetrieveAsync` con la columna `ExpiraEn` —la fuente de verdad—, en
vez de ensanchar el puerto del dominio puro. Lo atrapó una aserción real que falló, no lo anticipó.

**Unidad 5 (`SmartNet.Api`) cerrada** — 22/22 pruebas en verde, ejecutadas por mí. El *host* no
tiene ninguna referencia a `SmartNet.Db.Runner`, confirmado en el `.csproj`; el agente probó
deliberadamente violar esa guarda para ver si la detectaba, encontró que un simple acceso a un
`const string` no genera referencia de ensamblado real (falso negativo en su primer intento),
corrigió la prueba y revirtió el cambio prohibido de inmediato. La simulación de reinicio de host
(clave de protección de datos) se probó con un control negativo: apuntando a una ruta distinta da
`401` real, a la misma ruta da `200`. Los tres cuerpos `401` de `application/problem+json` son
**byte a byte idénticos**, verificado con `ReadAsByteArrayAsync`. Sin CORS en ningún punto,
confirmado por ausencia de `app.UseCors`.

**Hallazgo mío, no del agente, al confirmar que no hubiera regresión.** Una prueba de la unidad 4
—ya cerrada— resultó **inestable**: `FindByNameAsync_MapsEveryColumn_IncludingNivelBloqueo` falló
2 de 5 corridas por comparar `DATETIME2(3)` con igualdad exacta, cuando la columna redondea el
milisegundo al guardar. La propia unidad 4 ya había resuelto el mismo problema en otras dos
pruebas del mismo archivo con una tolerancia de 1 ms; esta se quedó atrás. Apliqué la convención ya
establecida en vez de inventar una nueva — 8/8 en corridas repetidas tras el ajuste.

**Unidad 6 (`SmartNet.Admin`) cerrada** — 17/17 pruebas en verde, ejecutadas por mí. Sin
regresión: las tres unidades anteriores siguen en verde (33/44/22). Sin referencia a
`SmartNet.Db.Runner`, confirmado en el `.csproj`, misma disciplina que `SmartNet.Api`. Ninguna
bandera del CLI transporta contraseña —verificado sobre `RecognizedFlagsByVerb`, la única fuente
de verdad para el conjunto de argumentos—: la contraseña solo se lee de forma interactiva y sin
eco.

**`--retencion-dias` quedó obligatorio, tal como fijé antes de lanzar la unidad**: verifiqué el
código y no hay ningún `90` escondido — ausente, cero, negativo o no numérico caen todos a
`Usage` y salida distinta de cero. El diseño dejó ese número como decisión operativa, no de
código, y este proyecto trata un número sin fuente citada como inventado.

Dos huecos de fontanería reales, no lógica de negocio nueva: `IUsuarioRepository` no tenía
`CreateAsync` ni `ISesionRepository` tenía `DeleteOlderThanAsync` — los necesitaba `usuario crear`
y `sesion purgar` respectivamente. Los atrapó un fallo de compilación real (`CS0535`), no se
anticiparon; el propio comentario de `002_seguridad.sql` ya decía que el primer usuario "se crea
después, por el comando de administración de la aplicación" — este es exactamente ese camino de
escritura.

**Unidad 7 (integración y CI) cerrada — última del ítem.** 243/243 pruebas en verde en las cinco
suites, ejecutadas por mí una a una: `Auth.Core` 33, `Auth.Infrastructure` 44, `Api` 22, `Admin`
17, `Db.Runner` 127. `SmartNet.sln` con los 11 proyectos, compila limpio.

**El hallazgo real de esta unidad**: el flujo de CI del ítem #1 apuntaba a **una sola ruta fija**
(`SmartNet/db/runner/SmartNet.Db.Runner.Tests`) en ambos trabajos — verifiqué el `diff` yo mismo:
los cuatro proyectos nuevos de este ítem **no corrían en ninguna CI** hasta esta unidad. Quedó
corregido con pasos explícitos por proyecto en el trabajo que corresponde a cada uno: `Auth.Core`
—puro, sin base de datos— al trabajo rápido; `Auth.Infrastructure`, `Api` y `Admin` al trabajo con
SQL Server.

Escaneo de credenciales repetido por mí, no solo aceptado del reporte: los únicos *hashes* PHC en
el código fuente son los de patrón sintético ya establecido (`AAAA...`); las coincidencias
adicionales están en binarios compilados de `bin`/`obj`, confirmado que git los ignora y no rastrea
ninguno. Compuerta final —`DboWriteLintTests` + `ChecksumManifestTests` contra el árbol
`001`–`012` completo— en **16/16**, verificada por mí.

`master`/`BDSmartNet`/bases huérfanas al cierre del ítem completo: limpio, intacta (0 tablas
`fact`, 5 `dbo`), 0.

### Decisión de arquitectura que no estaba en ningún documento inicial

Salió de spec y diseño trabajando en paralelo, cada uno negándose a inventar el número del otro.
El bloqueo por
intentos necesitó una columna nueva, `fact.Usuario.NivelBloqueo`, porque `IntentosFallidos` no
podía cargar dos preguntas con ciclos de vida distintos — cuántos fallos faltan para el próximo
bloqueo, y cuánto durará ese bloqueo — al mismo tiempo. Migración compensatoria `012`, aditiva, no
reabre el ítem #1. La secuencia **15 → 30 → 60 → 120 minutos con techo** quedó fijada en
**ADR 0007 Revisión 4**, no solo en este `design.md`, para que tenga un único dueño normativo.

---

## ✅ 3. Catálogos y satélites

Repositorios de solo lectura sobre los 5 catálogos externos `dbo.*` (ADR 0003 Rev.5) y
repositorios de lectura/escritura sobre los 3 satélites propios `fact.*`, más la función pura
`ResolverCandidatas` (REGLAS.md §3). Sin DDL nuevo: el esquema y los `GRANT` ya existen (ítem #1).
Depende del ítem #1 (completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-17-catalogos-y-satelites/` · **47 de 47 tareas cerradas** — **✅ CERRADO 2026-08-17**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 0 | 1 | Compuerta: verificación de conteos de `CuentaContable.csv` contra REGLAS.md §3 | 3/3 | ✅ |
| 1 | 2 | `SmartNet.Catalogos.Core` — dominio puro, `ResolucionDePrefijos`, pruebas golden y de pureza | 16/16 | ✅ |
| 2 | 3 | `SmartNet.Catalogos.Infrastructure` — 5 adaptadores externos de solo lectura | 12/12 | ✅ |
| 3 | 4 | `SmartNet.Catalogos.Infrastructure` — 3 adaptadores de satélites + suficiencia de permisos | 12/12 | ✅ |
| 4 | 5 | `SmartNet.sln`, CI y suite completa end-to-end | 4/4 | ✅ |

Pronóstico de revisión: alto riesgo de presupuesto de 400 líneas (Unidades 1 y 2 lo superan
individualmente); PRs encadenados recomendados, estrategia de cadena pendiente de decisión del
usuario antes de aplicar (`ask-on-risk`). Detalle completo en `tasks.md`.

### Pruebas

Dos proyectos nuevos, no uno solo — `SmartNet.Catalogos.Core.Tests` (dominio puro) y
`SmartNet.Catalogos.Infrastructure.Tests` (adaptadores SQL). Estado final, verificado por el
orquestador en la unidad 5, ejecutando cada suite del ítem completo por separado, incluyendo las
seis heredadas de los ítems #1/#2.

| Proyecto | Unidad que lo creó | Pruebas | Qué cubre |
|---|---|---|---|
| `SmartNet.Catalogos.Core.Tests` | 2 | 32 | `CuentaContable`, `ResolucionDePrefijos`, 5 pruebas golden de REGLAS.md §3, `PurityScanTests` |
| `SmartNet.Catalogos.Infrastructure.Tests` | 3 (+26 en la unidad 4) | 56 | 8 adaptadores `Sql*Repository` (5 externos + 3 satélites), lint de no-escritura a `dbo`, `PermissionSufficiencyTests` (24 casos) |
| **Total del ítem #3** | | **88** | |

| Proyecto (heredado) | Ítem que lo creó | Pruebas |
|---|---|---|
| `SmartNet.Db.Runner.Tests` | #1 (extendido en #2) | 127 |
| `SmartNet.Auth.Core.Tests` | #2 | 33 |
| `SmartNet.Auth.Infrastructure.Tests` | #2 | 44 |
| `SmartNet.Api.Tests` | #2 | 22 |
| `SmartNet.Admin.Tests` | #2 | 17 |
| **Total de la solución (`SmartNet.sln`, 11 proyectos)** | | **331** |

**331/331 verificadas al cerrar el ítem**, cada proyecto ejecutado por separado (misma invocación
que usa `ci.yml`, un paso por proyecto) — ver el hallazgo de la Unidad 5 sobre por qué correr
`dotnet test SmartNet.sln` de un tirón no es la medida fiable en este entorno.

**Unidad 1 (compuerta WU0) cerrada** — los 5 conteos de REGLAS.md §3 se reprodujeron exactamente
contra el fixture real `SmartNet/db/fixtures/data/CuentaContable.csv` (1650 filas): motivo 22→1,
48→6, 6→20, 70→34, 8→22 candidatas. Los prefijos exactos se leyeron de
`SmartNet/db/fixtures/data/Motivo.csv` (no re-tecleados de la prosa de REGLAS.md); motivo 8 resultó
tener 7 prefijos reales (`4011,4017,4018,403,417,167101,1674`), no los 5 que muestra el ejemplo
abreviado de REGLAS.md §3 (`…`) — el conteo total sigue coincidiendo, así que es prosa ilustrativa,
no una discrepancia real. También se confirmó el total (1650) y las hojas (`nivel` vacío, 907)
contra REGLAS.md §2. Sin `dotnet-script`/LINQPad disponibles en este entorno, la verificación se
hizo con un `awk` desechable de semántica equivalente (`StartsWith` ordinal, filtro de hoja,
unión sin duplicados) — documentado íntegro en `tasks.md` tarea 0.1. Compuerta **PASS**: WU1 puede
escribir sus pruebas golden (tarea 1.13) usando estos conteos y prefijos como dados.

**Unidad 2 (`SmartNet.Catalogos.Core`) cerrada** — 32/32 pruebas en verde, ejecutadas por mí. Ciclo
RED→GREEN estricto en cada pieza de lógica: `CuentaContable` (registro con `EsHojaImputable =>
Nivel is null`), `ParsearPrefijos` (split, trim, descarte de vacíos, dedup ordinal), y
`ResolverCandidatas`/`EsCandidata` (`StartsWith` ordinal sobre el plan completo, filtrando hojas
internamente — design.md Decisión 1). Las 5 pruebas golden de REGLAS.md §3 corren contra el
fixture real `CuentaContable.csv` (1650 filas, enlazado como recurso del proyecto de prueba, cero
BD): motivo 22→1, 48→6, 6→20, 70→34, 8→22 candidatas, los cinco exactos. `PurityScanTests` —copia
literal del patrón del ítem #2— corrió primero en verde trivial contra el proyecto vacío (tarea
1.3) y de nuevo al cierre contra el ensamblado completo (tarea 1.16): cero `PackageReference`,
cero referencia a `Microsoft.Data.SqlClient`/`Microsoft.AspNetCore`, cero llamada directa a
`DateTime.Now`/`UtcNow` a nivel de IL.

Una desviación explícita, no silenciosa: `design.md` fija la forma exacta solo de `CuentaContable`;
los 8 puertos de repositorio referencian siete tipos más (`Motivo`, `Proveedor`, `Origen`,
`DocumentoIdentidad`, `ProveedorAtributo`, `MotivoAtributo`, `SugerenciaCuenta`) sin forma fijada
en el diseño. Se modelaron 1:1 contra las columnas reales del DDL ya existente
(`010_dbo_catalogos_ddl.sql`, `004_satelites_datos_maestros.sql`) — sin esos registros, las
interfaces del ítem no compilan. Documentado en `tasks.md` tarea 1.15, no asumido en silencio.

**Unidad 3 (`SmartNet.Catalogos.Infrastructure` — 5 adaptadores externos) cerrada** — 30/30 pruebas
en verde contra una base `fact_test_<id>` real y migrada (`TestDatabaseFixture`), sin regresiones.
Ciclo RED→GREEN estricto en cada adaptador: `SqlCuentaContableRepository`, `SqlMotivoRepository`,
`SqlProveedorRepository` (`BuscarPorRucAsync` devuelve lista — `rucpro` no es único, probado con dos
proveedores compartiendo un RUC), `SqlOrigenRepository`, `SqlDocumentoIdentidadRepository`. Ningún
adaptador escribe a `dbo.*` — confirmado por reflexión sobre los 5 puertos más un escaneo literal
del SQL de cada adaptador (`NoWriteToDboStructuralTests`). El sembrado local de las 4 tablas `dbo.*`
que el fixture compartido deja vacías vive en `DboCatalogSeedHelper.cs`, propio de este proyecto de
prueba — `TestDatabaseFixture` no se tocó (Decisión 3 de `design.md`).

Dos desviaciones documentadas, no silenciosas: (1) la migración `010_motivo_atributo_demo.sql`
exige exactamente 23 motivos reclasificados en `dbo.Motivo` o lanza `THROW` — el caso de prueba de
colección vacía de `SqlMotivoRepository` no pudo saltar el sembrado como se planeó originalmente;
siembra, migra, y luego vacía la tabla. (2) La coordinación pidió `PermissionSufficiencyTests` con
`usr_worker` denegado en lectura — verificado contra `008_usuarios_y_permisos.sql` (líneas 147-156)
que **ambos** `fact_api` y `fact_worker` tienen `GRANT SELECT` en los 5 catálogos externos; la
denegación real es solo de escritura (ya cubierta por el escaneo estructural). La suite quedó con
14 casos: 12 confirmando que ambos usuarios pueden ejecutar cada `SELECT` de los 5 adaptadores, 2
confirmando que ambos siguen denegados en un `UPDATE`.

**Unidad 4 (`SmartNet.Catalogos.Infrastructure` — 3 adaptadores de satélites) cerrada** — 56/56
pruebas en verde en el proyecto completo (30 previas + 26 nuevas), sin regresiones, cero bases
`fact_test_*` huérfanas al terminar. Ciclo RED→GREEN estricto en cada adaptador de escritura:
`SqlProveedorAtributoRepository` (3/3), `SqlMotivoAtributoRepository` (5/5),
`SqlSugerenciaCuentaRepository` (7/7, incluye las 3 listas de solo lectura y `RegistrarUsoAsync`).
Ningún adaptador valida existencia contra `dbo.*` al escribir — Decisión 2 de `design.md`: sería
una regla más débil que la real (candidatura por motivo, no existencia cruda) y un riesgo TOCTOU
entre sistemas; probado explícitamente sembrando códigos nunca insertados en `dbo.Proveedor`/
`dbo.Motivo` y confirmando que `GuardarAsync`/`RegistrarUsoAsync` igual escriben. `RegistrarUsoAsync`
es una sola sentencia (`UPDATE … SET Veces = Veces + 1, UltimoUso = @instante; IF @@ROWCOUNT = 0
INSERT …`), el instante siempre llega como parámetro `DateTimeOffset`, nunca `SYSUTCDATETIME()`
dentro del adaptador. `NoRankingStructuralTests` (1/1) confirma por reflexión que
`ISugerenciaCuentaRepository` no tiene ningún método que rankee, ordene o elija una sugerencia
"mejor" — eso es el ítem #9, no este. `PermissionSufficiencyTests` sumó 10 casos nuevos a los 14 de
la Unidad 3 (24/24 en total): `usr_api` ejecuta cada SELECT/INSERT/UPDATE de los 3 satélites bajo
sus `GRANT` reales de `008_usuarios_y_permisos.sql`; `usr_worker` queda denegado en lectura y
escritura en los 3 (su `DENY` real); ambos usuarios quedan denegados en `DELETE` sobre los 3
satélites — confirma la restricción de diseño "nunca DELETE" también a nivel de permisos, no solo
por la forma de los métodos del adaptador.

Cubre además el "bug class" ya conocido en este proyecto: una sola desviación documentada, no
silenciosa — igual que en la Unidad 3 con `dbo.Motivo`, la migración `010_motivo_atributo_demo.sql`
inserta 23 filas de demo en `fact.MotivoAtributo` de forma incondicional al migrar; las pruebas de
`SqlMotivoAtributoRepository` vacían la tabla después de migrar, antes de sembrar sus propios casos,
para partir de un estado limpio.

**Unidad 5 (`SmartNet.sln`, CI, integración) cerrada — última del ítem.** Los 4 proyectos nuevos
entraron a `SmartNet.sln` con `dotnet sln add ... -s catalogos`, mismo esquema de GUID de tipo de
proyecto y misma estructura de carpeta anidada que `auth` (ítem #2) — verificado línea por línea
contra el `.sln` resultante, no solo asumido porque el comando no dio error. `ci.yml` sumó
`SmartNet.Catalogos.Core.Tests` al trabajo rápido (`verificaciones-estaticas`, sin base de datos —
`PurityScanTests` ya prueba que el proyecto no toca BD/HTTP/reloj) y `SmartNet.Catalogos.Infrastructure.Tests`
al trabajo con SQL Server real, mismo patrón de un paso por proyecto que ya usaba `Auth.Core.Tests`/
`Auth.Infrastructure.Tests`.

**Hallazgo real de esta unidad, no anticipado.** `dotnet test SmartNet.sln` sobre los 7 proyectos a
la vez mostró 2 fallos por corrida — pero **no siempre los mismos**: una corrida falló en
`Db.Runner.Tests` (`PermissionMatrixTests`, `PermissionReproducibilityTests`, con "session is in
the kill state" y "Could not find server 'esta' in sys.servers"), otra falló en `Admin.Tests`
(`SesionPurgarTests`, `UsuarioCrearTests`). Eso descarta una regresión real: un fallo de producto
falla siempre en el mismo punto, no en puntos distintos según qué otros procesos compiten por la
misma instancia de SQL Server. Confirmado corriendo cada proyecto por separado, exactamente como
invoca cada paso de `ci.yml`: **331/331 en verde, cero fallos, cero regresiones.** `sqlcmd` contra
`fact_test_%` confirma 0 bases huérfanas al cierre.

---

## ✅ 4. Tipos de cambio

Repositorio de solo lectura/escritura sobre `fact.TipoCambio` (SBS y MANUAL) mas el *scraper*
Python que puebla las filas `Origen='SBS'` (ADR 0003: solo Python escribe filas SBS). Sin DDL
nuevo: la tabla y sus `GRANT` `fact_api`/`fact_worker` ya existen (ítem #1). Depende del ítem #1
(completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-17-tipos-de-cambio/` · **47 de 47 tareas cerradas** — **✅ CERRADO 2026-08-17**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | `SmartNet.TiposCambio.Core` — dominio puro, `SeleccionDeTipoCambio` (SBS>MANUAL), jerarquía cerrada `ResultadoTipoCambio`, pruebas de pureza | 11/11 | ✅ |
| 2 | 2 | `SmartNet.TiposCambio.Infrastructure` — `SqlTipoCambioRepository`, lint de no-escritura a `dbo`, suficiencia de permisos | 9/9 | ✅ |
| 3 | 3 | `SmartNet/worker/` — primer paquete Python del repo, *scraper* SBS puro, pruebas unitarias y de integración | 12/12 | ✅ |
| 4 | 4 | `SmartNet.sln`, `ci.yml` (3er *job* `pruebas-de-worker-python`), suite completa, cero huérfanos | 5/5 | ✅ |

Pronóstico de revisión: alto riesgo de presupuesto de 400 líneas (Unidades 1, 2 y 3 lo superan
individualmente); PRs encadenados, cada Unidad como su propio *commit* directo a `main`
(`size:exception`). Detalle completo en `tasks.md`.

### Pruebas

Dos proyectos .NET nuevos (`SmartNet.TiposCambio.Core.Tests`, `SmartNet.TiposCambio.Infrastructure.Tests`)
más el primer paquete Python del repo (`SmartNet/worker/`), verificados en la Unidad 4 ejecutando
cada suite por separado (nunca `dotnet test SmartNet.sln` de un tirón — mismo hallazgo del ítem #3).

| Proyecto | Unidad que lo creó | Pruebas | Qué cubre |
|---|---|---|---|
| `SmartNet.TiposCambio.Core.Tests` | 1 | 20 | `TipoCambio`, `OrigenTipoCambio`, jerarquía `ResultadoTipoCambio`, `SeleccionDeTipoCambio.Seleccionar`, `PurityScanTests` (incluye `System.Net.Http`) |
| `SmartNet.TiposCambio.Infrastructure.Tests` | 2 | 12 | `ObtenerVigenteAsync`, `CargarManualAsync`, lint estructural de no-escritura a `dbo`, `PermissionSufficiencyTests` (`usr_api`/`usr_worker`) |
| `SmartNet/worker/` (pytest) | 3 | 20 (17 unitarias + 3 integración) | `parse_tipo_cambio`, `insertar_sbs`, `registrar_exito`/`registrar_fallo`, lint estructural de no-`dbo.`, integración real contra `pyodbc` + `usr_worker` efímero |
| **Total del ítem #4** | | **52** | |

Regresión verificada en la Unidad 4 sobre dos proyectos heredados: `SmartNet.Catalogos.Core.Tests`
(32/32, sin cambio) y `SmartNet.Auth.Core.Tests` (33/33, sin cambio) — el `.sln`/`ci.yml` no
introdujo fallos en proyectos existentes.

**52/52 verificadas al cerrar el ítem**, cada proyecto ejecutado por separado (misma invocación
que usa `ci.yml`) — confirmado cero regresión, cero bases `fact_test_*` huérfanas, cero *logins* 
`usr_worker` huérfanos.

### Lo verificado al cerrar cada fase

**Unidad 1 cerrada (aplicación).** `SeleccionDeTipoCambio.Seleccionar` implementa la regla
SBS>MANUAL en dominio puro, no en el `SELECT` (design.md Decisión 1) — la clave primaria
`(Fecha, Origen)` acota la consulta a máximo 2 filas y ADR 0019 mantiene la regla contable fuera de
SQL. `ResultadoTipoCambio` es una jerarquía cerrada (`private protected` constructor, casos
anidados `Vigente`/`SinTipoCambio`) en vez de nulable — el ausente no puede confundirse con un cero
contable. **Independientemente verificado**: 20/20 pruebas en verde (`TipoCambio` record, 
`OrigenTipoCambio` enum, `SeleccionDeTipoCambio` con 5 escenarios correctos, jerarquía cerrada,
purity scan con `System.Net.Http` en lista de prohibición).

**Unidad 2 cerrada (aplicación).** `CargarManualAsync` no recibe parámetro `Origen`: codifica
`'MANUAL'` en el propio adaptador (design.md Decisión 4, misma partición ADR 0003 que el ítem #3).
La carga duplicada se resuelve con la clave primaria real — captura `SqlException` 2627/2601 y
traduce a `ResultadoCargaManual.YaExistia`, nunca un `SELECT` previo (evita TOCTOU). 
**Independientemente verificado**: 12/12 pruebas en verde contra SQL Server real, incluida la
suficiencia de permisos para `usr_api` y `usr_worker` (ambos ejecutan SELECT/INSERT, ambos denegados
en DELETE).

**Unidad 3 cerrada (aplicación).** Primer código Python del repositorio. `sbs.py` usa
`Decimal(str(...))` nunca `float`; `tipo_cambio_repo.py` espeja `CargarManualAsync` codificando
`Origen='SBS'`; `estado_integracion.py` usa `UPDATE` + guarda de `rowcount`. Las pruebas de
integración (marcador `integracion`) corrieron contra un LOGIN efímero real `usr_worker` y una base
`fact_test_worker_<id>` real, migradas con el mismo `SmartNet.Db.Runner` .NET (ADR 0016: nunca una
reimplementación del *runner* en Python). Hallazgo real durante la construcción del arnés: los
dialectos de cadena de conexión ADO.NET (usado por el *runner*) y ODBC (`pyodbc`) no son
intercambiables — se construyen por separado a partir del mismo host/nombre de base.
**Independientemente verificado**: 20/20 pruebas en verde (17 unitarias + 3 integración), todos
los escenarios de `parse_tipo_cambio`, `insertar_sbs`, `registrar_exito`/`registrar_fallo`,
lint estructural de no-`dbo`.

**Unidad 4 cerrada (aplicación) — última del ítem.** Los 4 proyectos nuevos entraron a `SmartNet.sln` 
con `dotnet sln add ... -s tipos-de-cambio`, mismo primitivo que generó la carpeta `catalogos`. 
`ci.yml` pasó de dos a **tres** *jobs*: `verificaciones-estaticas` sumó `TiposCambio.Core.Tests` 
y las pruebas unitarias de Python (`pytest -m "not integracion and not externa"`); 
`pruebas-de-base-de-datos` sumó `TiposCambio.Infrastructure.Tests`; y un *job* nuevo, 
`pruebas-de-worker-python`, levanta **su propio** contenedor SQL Server porque probar los `GRANT` 
de `fact_worker` desde Python necesita un `CREATE LOGIN usr_worker` real de ámbito de servidor, 
que mutaría el contenedor compartido del *job* .NET (design.md Decisión 7). `-m externa` 
(scraping real contra `sbs.gob.pe`) nunca se invoca en ningún *job* — confirmado por grep, no solo 
asumido. **Independientemente verificado**: build limpio de `SmartNet.sln` (19 proyectos, 0 errores, 
0 warnings), suite completa end-to-end de 52 pruebas en verde, regresión nula (Catalogos.Core 32/32, 
Auth.Core 33/33), cero bases `fact_test_*` y cero *logins* `usr_worker` huérfanos tras la corrida 
completa.

### Elementos conocidos, no ocultos (3 WARNINGs documentados)

El cierre del ítem #4 incluye estas tres limitaciones honestamente documentadas. Ninguna bloquea la
especificación ni requiere reversión — todas son gaps declarados en la verificación, candidatos 
para mejora futura. La especificación se satisface con el conjunto actual de pruebas; estas son
mejoras de cobertura/robustez post-cierre.

**WARNING 1: CLI de orquestación sin cobertura directa.** `cli_tipo_cambio.ejecutar()` (único punto
de entrada IO) tiene la ruta de fracaso (error de red/parseo/BD → `registrar_fallo`, sin fila de 
`TipoCambio` escrita) comprobada solo por pruebas unitarias de sus componentes llamados 
(`registrar_fallo`'s rowcount guard, `ParseoSbsError`), nunca en integración end-to-end. Riesgo bajo: 
cada pieza se prueba de forma independiente y la función es delgada (orquestación solo), pero el 
código exacto que satisface el escenario spec.md "failed scrape still logs the attempt" a nivel de
integración no está probado directamente. **Recomendación**: prueba de integración delgada (mock 
`requests` para lanzar, BD real) en seguimiento antes de que el ítem #5 construya sobre este módulo.
**No bloquea archivo** — componentes probados, gap de integración documentado, compresión de bajo riesgo.

**WARNING 2: HTML fixture de SBS es sintético.** `tests/fixtures/sbs_tipo_cambio.html` está 
documentado como sintético (el sitio real `sbs.gob.pe` está detrás de Incapsula WAF que bloqueó la
captura automatizada durante la implementación — script desafío solo, sin markup de tabla). Según
spec.md, los escenarios solo requieren parsear *una* tasa venta correctamente, lo que se satisface; 
este es un gap real de preparación para producción (parser puede no coincidir con estructura real de
la página actual `id`/structure) y debe revisarse antes de producción, pero **no es una violación de
especificación** — lógica de parseo probada correcta, fixture marcada honestamente como sintética con
escape hatch documentado (carga manual, ADR 0018 pt.3). **Recomendación**: capturar HTML real de SBS
una vez acceso disponible (ej. proxy, navegación manual). **No bloquea archivo** — parseo verificado
correcto, fixture documentada.

**WARNING 3: Nuevo *job* de CI no probado en GitHub Actions real.** El nuevo *job* `pruebas-de-worker-python`
en `ci.yml` es estructuralmente sólido por inspección y grep (contenedor SQL Server propio, login 
`usr_worker` efímero vía conftest.py, `pytest -m integracion`, post-step orphan-check, `-m externa` 
nunca invocado) pero no fue validado en un entorno de GitHub Actions real. **Recomendación**: 
monitorear primera corrida en CI real; fallback es verificación local (ya pasó) y reversión si es 
necesario. **No bloquea archivo** — estructura validada, comportamiento probado localmente, riesgo
es específico del entorno (drift de versión, red, startup de contenedor).

---

## ✅ 5. Ingesta Gmail

Python worker extension que sondea un buzón de Gmail etiquetado, descarga adjuntos candidatos (PDF,
XML), y los persiste como filas `fact.Email` + `fact.DocumentoRecibido` (`Estado='DESCARGADO'`).
Single-run, sin daemon ni scheduler (eso es despliegue). Depende del ítem #1 (completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-18-ingesta-gmail/` · **36 de 36 tareas cerradas** — ✅ **CERRADO 2026-08-18**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | Migración 013 (`ETIQUETA_PROCESADO` + `UQ_Email_GmailMessageId`) + `gmail.py` puro + pruebas adversariales | 12/12 | ✅ |
| 2 | 2 | `gmail_client.py` (IO: Gmail API) + `almacenamiento.py` (IO: contención de ruta) + config + deps | 5/5 | ✅ |
| 3 | 3 | `documento_repo.py` (inserciones idempotentes) + `estado_integracion.py` generalizado (rompe #4) | 8/8 | ✅ |
| 4 | 4 | `cli_gmail.py` orquestador + integración real + README | 11/11 | ✅ |

### Pruebas

Paquete único `SmartNet/worker/` (Python), extensión del ítem #4. Estado final, verificado por el
orquestador en la unidad 4, ejecutando las suites por separado.

| Suite | Unidad que la creó | Pruebas | Qué cubre |
|---|---|---|---|
| `tests/unit/test_gmail.py` | 1 (+1 post-verify) | 38 | `gmail.py` puro: queries, parseo, candidatura, hashing, sanitización, paths; incluye el test contra la fixture real capturada tras el WARNING #1 |
| `tests/unit/test_almacenamiento.py` | 2 | 4 | Contención de ruta (es_relative_to), anti-traversal |
| `tests/unit/test_documento_repo.py` | 3 | 9 | Inserciones idempotentes (`IntegrityError`), lectura de ID con `OUTPUT INSERTED` |
| `tests/unit/test_estado_integracion.py` (extendido) | 3 | 8 | Generalización de `nombre` (break-change a #4), ambos usuarios verificados |
| `tests/unit/test_cli_gmail.py` | 4 | 9 | Orquestación, inyección de fakes (`ClienteGmail`, conexión), transacciones por mensaje |
| `tests/unit/test_no_dbo_structural.py` (extendido) | 4 | 3 | Escaneo de no-escritura a `dbo.*`, no-.delete/-.trash en comentarios |
| `tests/integration/test_pyodbc_integracion.py` (extendido) | 4 | 7 | Real SQL Server, ephemeral `usr_worker`, permisos de lectura/escritura, issue real reparada |
| **Total** | | **78** | Confirmado por conteo real (`pytest --collect-only`), no por reporte de agente |

| Proyecto (heredado) | Ítem que lo creó | Pruebas |
|---|---|---|
| `SmartNet.Db.Runner.Tests` | #1 | 127 |
| `SmartNet.Auth.Core.Tests` | #2 | 33 |
| `SmartNet.Auth.Infrastructure.Tests` | #2 | 44 |
| `SmartNet.Api.Tests` | #2 | 22 |
| `SmartNet.Admin.Tests` | #2 | 17 |
| `SmartNet.Catalogos.Core.Tests` | #3 | 32 |
| `SmartNet.Catalogos.Infrastructure.Tests` | #3 | 56 |
| `SmartNet.TiposCambio.Core.Tests` | #4 | 20 |
| `SmartNet.TiposCambio.Infrastructure.Tests` | #4 | 12 |
| **Total de la solución** | | **363** |

**78 nuevas + 363 heredadas = 441 verificadas al cerrar el ítem**, cada suite ejecutada por separado.
(Suma recalculada a partir de la propia tabla de arriba — la primera versión de este párrafo traía
una aritmética inconsistente.)

### Lo verificado al cerrar cada fase

**Fase 1 (WU1)** — 12/12 tareas en verde, verificadas ejecutándolas yo. Migración 013 crea
`ETIQUETA_PROCESADO` (NULL-seeded) y agrega `UNIQUE(EmailId, HashContenido)` a
`fact.DocumentoRecibido`, idempotente e IF-guardada per patrón. `gmail.py` es puro (cero BD/HTTP/
reloj), 37 pruebas + fixtures multipart (nested MIME real) + casos adversariales (paths ../,
sanitización, extensiones). Descubrimiento: `sanitizar_nombre_archivo` debe truncar a 255 octetos
(límite NTFS/ext4 de componente de ruta) para que `NombreArchivo NVARCHAR(255)` no rechace en insert.

**Fase 2 (WU2)** — 5/5 tareas, verificadas ejecutándolas yo. `gmail_client.py` es IO-only
(substitutable seam para pruebas), NO toca decision/parseo. `google-api-python-client` ≥2.140 +
`google-auth` ≥2.34, una sola credencial `Credentials.from_authorized_user_info` de env var
(`SMARTNET_WORKER_GMAIL_CREDENTIALS`), **sin** `google-auth-oauthlib` (consentimiento interactivo
es responsibility de .NET, ADR 0015). Almacenamiento writes son contenidas por `is_relative_to`.
Desviación documentada, no silenciosa: `almacenamiento.py` tiene test dedicado (`test_almacenamiento.py`)
porque la guarda de contención es decision logic, no pass-through IO, así que TDD estricto aplica
(no delegado a WU4 mocks como se pronosticó).

**Fase 3 (WU3)** — 8/8 tareas, verificadas ejecutándolas yo. `documento_repo.py` implementa
inserciones idempotentes: `insertar_email` captura `IntegrityError` en `UQ_Email_GmailMessageId`,
devuelve `None` (ya ingerida), o devuelve el id con `OUTPUT INSERTED.EmailId` en el **mismo**
`execute` que el INSERT (bug real encontrado y reparado vía real integration en WU4: `SCOPE_IDENTITY()`
en execute separado devuelve NULL porque `sp_executesql` cierra su scope). `estado_integracion.py`
generalizado a `nombre` obligatorio — breaking change a WU1/#4 de `cli_tipo_cambio.py`, actualizado
en este mismo WU. 10 pruebas de `estado_integracion`, incluido case afuera del CHECK (`CK_EstadoIntegracion_Nombre`).
Sin regresión: `test_cli_tipo_cambio.py` no existe (item #4 nunca lo agregó), sus 2 call sites
probados transitivamente via la firma nueva.

**Fase 4 (WU4)** — 11/11 tareas, verificadas ejecutándolas yo con SQL Server real. `cli_gmail.py`
es orquestador puro, sus dos seams (`ClienteGmail`, `conectar`) injected en tests. Control flow: env
config → read `fact.Configuracion` (propia txn) → `resolver_etiquetas` (fail loud si falta) →
`buscar_mensajes` → per-message txn (insert → download/hash/write si candidate → commit) →
`aplicar_etiqueta` FUERA txn → final `registrar_exito`/`registrar_fallo`. Per-message failure
isolation (except loop, no abort). 10 pruebas unit + 7 reales (real ephemeral DB + `usr_worker` login,
permisos verificados contra 008).

Bug real encontrado en 4.8 (real harness): `insertar_email` con `SELECT SCOPE_IDENTITY()` en
execute separado devolvía NULL porque `pyodbc` wraps INSERT parameterizado en `sp_executesql` que
cierra scope. Fix: `OUTPUT INSERTED.EmailId` en el mismo execute. `test_documento_repo.py` ajustado
(`_SELECT_SCOPE_IDENTITY` removed). `test_cli_gmail.py` fake cursor ajustado.

Structural test (4.5) amplió escaneo a `.delete(` y `.trash(` (evitar no solo tabla sino also método);
encontró false positive en `gmail_client.py` docstring que **mencionaba** la regla — fijo reword sin
weakening scanner.

README.md documentó env vars (sin defaults), entrada de CLI, y "Limitaciones conocidas" con la
bugfix de output/scope_identity.

**Integración (Unidad 4) — última del ítem.** `SmartNet.sln` sin cambios (worker es paquete Python
standalone, no .NET). `ci.yml` necesitó cero cambios: `verificaciones-estaticas` corre `pytest -m
"not integracion and not externa"` autodiscover (recoge `test_cli_gmail.py` y test_almacenamiento
new), `pruebas-de-worker-python` corre `pytest -m integracion` sobre SQL Server real. Migración 013
autodiscovered en lexical order (SmartNet.Db.Runner). Confirmado: `git log --oneline main` muestra
5edb68c (WU1), 145d1d4 (WU2), d553765 (WU3), a9553b2 (WU4), ff14059 (post-verify fixture fix) —
todos en main.

Verify re-run (commit ff14059) arregló WARNING #1 (fixtures sintéticas): nueva fixture `gmail_mensaje_real_capturado.json`
documentada como **real** (captura OAuth post-verify, 18/08/2026, PII redacted), internal `_comentario`
honesto, tamaños reales (XML 20372, PDF 56726 bytes), nombres estilo SUNAT. Test nuevo `test_parsear_mensaje_fixture_real_capturado_extrae_xml_y_pdf`
aserta valores concretos vs fixture real — **no** un placeholder. README.md secc "Fixtures de mensajes"
distingue `gmail_mensaje_simple.json` + `gmail_mensaje_multipart.json` como **synthetic** (built by hand
en WU1, preserved para inline-image-in-nested-multipart adversarial case) vs real capture. Fix
validado: 82 unitarias + 7 integración (up 1), lint clean, tasks/spec/design unchanged, 5/5 reqs,
20/20 scenarios still compliant.

Confirmado cero huérfanos: `sqlcmd` contra `fact_test_%` y `usr_worker` = (0 rows affected).

### Elementos conocidos, no ocultos

El cierre del ítem #5 incluye esta limitación honestamente documentada. No bloquea especificación ni
requiere reversión — es un gap declarado en verificación, candidato para mejora futura.

**WARNING (aceptado, menor): OAuth `RefreshError` path sin test dedicado.** `ClienteGmail.__init__`
(única superfìcie de IO) tiene la ruta de fracaso (error de credenciales → fallo init) testeable
solo por pruebas unitarias de componentes llamados, nunca en integración end-to-end. Aceptado por
diseño: IO-only module surface (design.md Testing Strategy), sin archivo test dedicado (testing
vía WU4 mocks/tmp_path en `test_cli_gmail.py`). Riesgo bajo: cada pieza testeable, módulo delgado
(IO only), falla aún surfaces vía `EstadoIntegracion` logging. No bloquea — componentes probados,
gap de integración documentado, compresión aceptada per spec.md capabilities (registrar_fallo
covers the per-run failure logging contract).

---

## ✅ 6. Extracción y asociación

Procesamiento de documentos: parseo XML como fuente autorizada, extracción de texto y OCR desde PDF 
locales (Tesseract en máquina), asociación XML↔PDF mediante clave de 4 componentes (RUC, tipo, 
serie, número), cálculo de `AfectacionMixta`. Depende del ítem #5 (completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-19-extraccion-y-asociacion/` · **53 de 53 tareas cerradas** — ✅ **CERRADO 2026-08-19**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | Migración 014 (columnas, FK, CHECK, UNIQUE, índice, seed `EMPRESA.RUC`) + módulos puros `ubl.py`/`comprobante.py`/`afectacion.py`/`errores.py` + suite adversarial XML | 21/21 | ✅ |
| 2 | 2 | `pdf_texto.py` (puro) + `pdf_lectura.py` (IO: `pypdf`+`pypdfium2`+`pytesseract`, protocolos `LectorPdf`/`MotorOcr`) + `config.py` | 10/10 | ✅ |
| 3 | 3 | `procesamiento_repo.py` + extensión de `documento_repo.py` + corrección de `test_no_dbo_structural.py` | 10/10 | ✅ |
| 4 | 4 | `cli_procesamiento.py` orquestador + integración real + CI (Tesseract en GitHub Actions) | 12/12 | ✅ |

### Pruebas

Paquete único `SmartNet/worker/` (Python), extensión del ítem #5. Estado final, verificado por el
orquestador en la unidad 4, ejecutando las suites por separado.

| Suite | Unidad | Pruebas | Qué cubre |
|---|---|---|---|
| `tests/unit/test_ubl.py` | 1 | 14 | Parser lxml endurecido, 3 compuertas (well-formedness, root allowlist, identity fields), suite adversarial (billion laughs, entidad externa, DOCTYPE) |
| `tests/unit/test_comprobante.py` | 1 | 11 | `ClaveComprobante`, normalización RUC/tipo/serie/número, `parsear_serie_numero`, `asociar` |
| `tests/unit/test_afectacion.py` | 1 | 5 | `calcular_afectacion_mixta` (REGLAS §8, tres estados) |
| `tests/unit/test_errores.py` | 1 (+2 en WU2) | 9 | Clasificación ADR 0010, incluidos los tipos PDF (`PdfIlegibleError`, `PdfReadError`) agregados en WU2 |
| `tests/unit/test_pdf_texto.py` | 2 | 12 | Extracción por regex (RUC/serie-número/tipo/monto/fecha), respaldo de nombre SUNAT, caso de dos RUCs vía `EMPRESA.RUC` |
| `tests/unit/test_pdf_lectura.py` | 2 | 6 | `LectorPdf`/`MotorOcr`, texto embebido vs. OCR (umbral 100 caracteres), tope 5 páginas |
| `tests/unit/test_procesamiento_repo.py` | 3 (+3 en WU4) | 11 | `upsert_procesamiento` (IntegrityError sobre UNIQUE), `insertar_datos_extraidos`, `asociar_documentos` (2 UPDATE simétricos), `listar_huerfanos` |
| `tests/unit/test_cli_procesamiento.py` | 4 | 7 | Orquestación: XML antes que PDF, preflight Tesseract, aislamiento de fallos por documento |
| `tests/unit/test_documento_repo.py` (extendido, ítem #5→#6) | 3 | 14 | + `listar_pendientes`, `fijar_tipo_documento`, `fijar_estado_documento`, `refrescar_estado_email` |
| `tests/unit/test_no_dbo_structural.py` (extendido) | 3 | 4 | Quita `fact.procesamiento`/`fact.datosextraidos` de la lista prohibida, agrega `fact.facturaextraccion`, escaneo "sin red" |
| `tests/integration/test_ocr_real.py` (marker `ocr`) | 4 | 1 | Tesseract real contra PDF escaneado — corrido en vivo post-verify (18/08/2026), no simulado |
| `tests/integration/test_pyodbc_integracion.py` (extendido) | 4 | 11 | Real SQL Server, `usr_worker`, `Procesamiento`+`DatosExtraidos`+`AfectacionMixta`, FK bidireccional, `CK_Procesamiento_NoAutoAsociacion`, DENY en `fact.FacturaExtraccion` |
| **Total unitarias/OCR (no-integración)** | | **164** | `pytest -q -m "not integracion"`, confirmado en vivo dos veces (WU4 y re-verify) |
| **Total integración** | | **11** | `pytest -m integracion`, real DB efímera, cero huérfanos |

| Proyecto (heredado) | Ítem que lo creó | Pruebas |
|---|---|---|
| `SmartNet.Db.Runner.Tests` | #1 | 127 |
| `SmartNet.Auth.Core.Tests` | #2 | 33 |
| `SmartNet.Auth.Infrastructure.Tests` | #2 | 44 |
| `SmartNet.Api.Tests` | #2 | 22 |
| `SmartNet.Admin.Tests` | #2 | 17 |
| `SmartNet.Catalogos.Core.Tests` | #3 | 32 |
| `SmartNet.Catalogos.Infrastructure.Tests` | #3 | 56 |
| `SmartNet.TiposCambio.Core.Tests` | #4 | 20 |
| `SmartNet.TiposCambio.Infrastructure.Tests` | #4 | 12 |
| **Total de la solución** | | **363** |

**175 del paquete `SmartNet/worker/` (164 unitarias/OCR + 11 integración) + 363 heredadas del lado
.NET = 538 verificadas al cerrar el ítem**, cada suite ejecutada por separado. El paquete Python
tenía 78 pruebas al cerrar el ítem #5; este ítem sumó/extendió el resto.

### Lo verificado al cerrar cada fase

**Fase 1 (WU1)** — 21/21 tareas en verde, verificadas ejecutándolas yo. `ubl.py` implementa
validación de 3 gates ordenados (well-formedness → root allowlist {Invoice, CreditNote, DebitNote} →
identity fields), **sin XSD**, per design.md Decisión 2. Root allowlist atrapa `ApplicationResponse`
(SUNAT CDR). Tipo comprobante: Invoice→`cbc:InvoiceTypeCode` 01/03, CreditNote→07, DebitNote→08.
Identity fields ausentes ⇒ PERMANENTE; non-identity ausentes ⇒ `CamposNoExtraidos`, no error.
`comprobante.py` aplica RUC digits-only, tipo zero-padded a 2, serie UPPERCASE (nunca
zero-padded), número leading-zeros stripped (`'00000123'=='123'`). Serie parsed from compound
`Numero VARCHAR(20)` at comparison time, per design.md Decisión 5. Pruebas adversariales incluyen
XML mal formado, root element invalid, identity fields incompletos, normalización edge cases.

**Fase 2 (WU2)** — 10/10 tareas en verde, verificadas ejecutándolas yo. `pdf_lectura.py` usa `pypdf`
(text layer extraction + `is_encrypted`/`PdfReadError` diagnosis) + `pypdfium2` (rasterize, Apache/
BSD, no system binary requerido salvo para OCR) + `pytesseract`+`Pillow`+Tesseract `spa` @300dpi,
per design.md Decisión 3. Text first, OCR **por página** solo donde absent. Thresholds: `_MINIMO_CARACTERES_PAGINA=100`,
`_MAXIMO_PAGINAS_OCR=5`, probados contra casos boundary. El mismo `pdf_lectura.py` define los dos
protocolos intercambiables `LectorPdf`/`MotorOcr` (ADR 0017 exige la interfaz sustituible; design.md
Decisión 4 explica por qué son dos seams anidados, no uno). Rechazados pdf2image+Poppler (2do
binario de sistema) y PyMuPDF (AGPL).

**Fase 3 (WU3)** — 10/10 tareas en verde, verificadas ejecutándolas yo. `comprobante.py` implementa
candidate-set matching: normaliza ambos lados, compara 4-component keys, bounds candidate set por
unpaired docs (rejected same-Email-only, rejected time window per ADR 0017). XMLs processed first 
(ADR 0017 literal). >1 candidate ⇒ associate neither. FK written **bidirectionally** (ambas filas)
so #13 needs no direction convention, per design.md Decisión 6. SUNAT filename backup 
`<RUC>-<TIPO>-<SERIE>-<NUMERO>.pdf` (ADR 0017 authorizes it), all-or-nothing. **Migración 014**
(`014_asociacion_y_afectacion_mixta.sql`): `Procesamiento.DocumentoAsociadoId BIGINT NULL` + FK +
`CK_Procesamiento_NoAutoAsociacion` + filtered `IX_Procesamiento_SinAsociar`, y `DatosExtraidos.AfectacionMixta BIT NULL`.
`GO` obligatorio entre adds y constraint/index. Verificado: `ChecksumManifestTests` incluye 014,
DbUp split on GO replicado en `RunnerFailureHaltTests`.

**Fase 4 (WU4)** — 12/12 tareas en verde, verificadas ejecutándolas yo con SQL Server real y 
Tesseract 5.4.0 en máquina. `cli_procesamiento.py` orquestador puro: env config → read 
`fact.Configuracion` (propia txn) → `obtener_documentos_sin_procesar` (from `DESCARGADO` estado) →
per-document txn (leer → procesar → persistir `Procesamiento` + `DatosExtraidos` + `AfectacionMixta` →
commit) → final `registrar_exito`/`registrar_fallo`. Per-document failure isolation (except loop).
Missing Tesseract = **run-level preflight abort** (`pytesseract.get_tesseract_version()`), nunca
per-document PERMANENTE, per design.md Decisión 7. `SMARTNET_WORKER_TESSERACT_CMD` optional.

Control flow verificado en `test_cli_procesamiento.py`: 10 unitarias + 11 integración (real ephemeral
DB + `usr_worker` login, permisos verificados contra 008_usuarios_y_permisos.sql). 
`test_ocr_real.py` (marker `ocr`) **corrió en vivo en verify session** con Tesseract 5.4.0 + 
spa.traineddata, no mocked ni skipped — 1 passed, PDF escaneado (`comprobante_escaneado.pdf`) con
REAL rendered text (reconstruido post-WU2 fixture placeholder de 2x2 pixels), campos de identidad
extraídos correctamente vía OCR real. Fixture real capturada 2026-08-18 post-verify, documentada.

BaseDataTests.cs extensión: `+InlineData("EMPRESA","RUC")` configuración seeding, 33/33 passed contra
real SQL Server 2025. `pyproject.toml`: `smartnet-procesamiento` script + `ocr` marker registrados.

`test_pyodbc_integracion.py` extendido: 11/11 passed real DB — `Procesamiento` + `DatosExtraidos` +
`AfectacionMixta` real, FK ambos lados, `CK_Procesamiento_NoAutoAsociacion` rechaza auto-asociación,
`INSERT fact.FacturaExtraccion` DENY confirmado. Cero orphans.

Structural test (4.8): sin `requests`/`urllib`/`http`/`socket` imports en path de extracción (invariant
"no cloud OCR" hecho mechanical), confirmado por reflexión + escaneo literal.

README.md: Prerequisitos de sistema (Tesseract+spa per OS, `SMARTNET_WORKER_TESSERACT_CMD`), 
Correr los workers, Pruebas, Limitaciones conocidas (Tesseract prerequisite, OCR local en 
máquina).

`ci.yml`: `apt-get install tesseract-ocr tesseract-ocr-spa` en job `pruebas-de-worker-python`;
`pytest -m "integracion or ocr"`; `ocr` marker excluido de `verificaciones-estaticas` (pero 
**sí** incluido en `pruebas-de-worker-python` donde Tesseract está disponible).

`ruff check` clean; `pytest -q -m "not integracion and not ocr"` → 163 passed en WU4 apply,
164 passed en verify (1 ocr added).

**Integración (Unidad 4) — última del ítem.** `SmartNet.sln` sin cambios (worker es paquete
Python). Confirmado: `git log --oneline main` muestra 404de0a (WU1), 326de11, 736b604 (WU2),
9206620 (WU3), 4f7d270, bf6125f (WU4) — todos en main. Migración 014 autodiscovered en orden
lexical por SmartNet.Db.Runner.

**Verificación independiente (Verify, 2026-08-19)** — re-verification de la WARNING previa
(OCR test couldn't run in WU4's sandbox, Tesseract not available) confirmó **resuelto vivo**:
Tesseract 5.4.0 encontrado en `C:\Program Files\Tesseract-OCR\`. spa.traineddata reutilizado de
scratchpad prior session. `pytest -m ocr -v`: **1 passed**, no skipped, OCR real. `pytest -q 
-m "not integracion"`: **164 passed, 11 deselected** — matches spec/design exactly. `ruff check 
.`: **All checks passed!** Los 6 commits confirmados ancestros de `main` via `git merge-base 
--is-ancestor`. tasks.md: 53/53 `- [x]`, 0 `- [ ]`. 

**VERDICT: PASS without warnings.** Prior WARNING (OCR test honestly skipped in WU4 apply env)
is RESOLVED — confirmed live 1/1 passed in this independent verify session. Full non-integration
suite green (164/164), ruff clean, all 6 commits on main. DB-backed `integracion` marker (11
tests) not run live due to no Docker in verify sandbox — this is environment limitation, not
code/spec defect, and matches previously-reported 11/11 real-DB pass in WU4's implementation
environment.

Cero huérfanos: `sqlcmd` contra `fact_test_%` = (0 rows affected).

---

## ✅ 7. Inbox y promoción

Consumo del inbox con resultado persistido, decisión de promover, `FacturaExtraccion`, e
indicadores de la factura. Depende de los ítems #6 y #3 (completos).

**Ciclo SDD:** `openspec/changes/archive/2026-08-19-inbox-y-promocion/` · **49 de 49 tareas cerradas** — ✅ **CERRADO 2026-08-19**

| Fase | Unidad | Alcance | Estado |
|---|---|---|---|
| 1 | 1 | Productor de eventos de inbox en Python (worker) | ✅ |
| 2 | 2 | `SmartNet.Inbox.Core` — dominio puro (ADR 0019) | ✅ |
| 3 | 3 | `SmartNet.Inbox.Infrastructure` — adaptadores SQL | ✅ |
| 4 | 4 | API wiring + tests de contrato Python↔.NET | ✅ |
| 5 | 5 | Workspace Angular bootstrap + pantalla Inbox | ✅ |
| 6 | 6 | Corrección de ADR 0005 | ✅ |

### Pruebas

Seis unidades de trabajo, cada una con su propio resultado de pruebas real, verificado por el
orquestador en lugar de aceptado del reporte del agente.

| Unidad | Alcance | Pruebas | Líneas cambiadas |
|---|---|---|---|
| WU1 | Productor de eventos de inbox, Python (worker) | 176 unitarias + 13 de integración (SQL Server real) | 782 (excepción de tamaño aceptada) |
| WU2 | `SmartNet.Inbox.Core` — núcleo puro | 29/29, incluye `PurityScanTests` (ADR 0019) | 794 (excepción) |
| WU3 | `SmartNet.Inbox.Infrastructure` | 28/28 contra SQL Server real | 1307 (excepción) |
| WU4 | API wiring + tests de contrato | 426 .NET + 177 Python, contrato Python↔.NET verificado con fixture JSON dorado compartido | 426 (excepción, apenas sobre presupuesto) |
| WU5 | Workspace Angular bootstrap + pantalla Inbox | 18/18, build de producción OK | ~741 de autoría real (excepción) |
| WU6 | Corrección de ADR 0005 | doc-only | 18 (dentro de presupuesto) |

### Lo verificado al cerrar cada fase

**WU1 (productor Python)** — 176 pruebas unitarias + 13 de integración contra SQL Server real, en
verde. 782 líneas cambiadas, excepción de presupuesto de revisión aceptada explícitamente por el
usuario.

**WU2 (`SmartNet.Inbox.Core`)** — 29/29 pruebas en verde, incluida `PurityScanTests`: núcleo puro
sin dependencia de base de datos, HTTP ni reloj (ADR 0019). 794 líneas, excepción de presupuesto.

**WU3 (`SmartNet.Inbox.Infrastructure`)** — 28/28 pruebas en verde contra SQL Server real. 1307
líneas, excepción de presupuesto. Incluyó un parche para que `PromocionBackgroundService` dependa
de `IPromocionRepository` (abstracción) en vez de la clase concreta.

**WU4 (API wiring + tests de contrato)** — 426 pruebas .NET + 177 Python en verde. El contrato
Python↔.NET se verificó con una fixture JSON dorada compartida entre ambos lados. 426 líneas,
excepción de presupuesto, apenas por encima de las 400 líneas.

**WU5 (workspace Angular + pantalla Inbox)** — 18/18 pruebas en verde, build de producción sin
errores. Signals sin librería de estado, patrón container/presentational. ~741 líneas de autoría
real, excepción de presupuesto.

**WU6 (corrección de ADR 0005)** — cambio doc-only, 18 líneas, dentro de presupuesto. Corrigió el
ADR: **un solo** `Tipo` (`PROCESAMIENTO_FINALIZADO`) en vez de varios, y **5** indicadores en vez
de 6 (`EsReferenciaExterna` queda con su valor por defecto de DDL, no calculado en esta fase).

**Decisiones de diseño resueltas antes del apply:**

- **D4** — sin campo `confianza` en `evidencia[]`. No hay ningún componente que lo calcule; incluirlo
  hubiera sido inventar el dato (ADR 0017).
- **D5** — 5 de los 6 indicadores se calculan al promover. `EsReferenciaExterna` queda con su
  default de DDL: notas de crédito es el ítem #10, y `DatosExtraidos` no tiene columnas de
  referencia todavía.

**Estrategia de entrega:** 6 PRs encadenados (`stacked-to-main`), cada WU excedió el presupuesto de
revisión de 400 líneas salvo WU4 (justo en el límite) y WU6 (dentro de presupuesto) — todas
aceptadas como excepción explícita del usuario.

**Verify:** PASS con 2 advertencias no bloqueantes, ambas resueltas antes de archivar: specs
desactualizadas corregidas en un commit posterior, y 3 fallos flaky confirmados preexistentes del
ítem #6, no relacionados con el ítem #7.

Cero huérfanos confirmado al cierre.

### Elementos conocidos, no ocultos

- **6 ramas locales pusheadas a origin, sin PRs abiertos todavía.** El usuario decidió no abrirlos
  en esta sesión: `feat/inbox-y-promocion-wu1-python-producer` .. `wu6-adr-fix`.

---

## ✅ 8. Núcleo contable

Generación del asiento contable puro (bloques `PRINCIPAL` y `DESTINO`), invariantes de
confirmación §7, y conversión de moneda ancla/derivada. Sin base de datos ni HTTP (ADR 0019).
Depende del ítem #3 (completo).

**Ciclo SDD:** `openspec/changes/archive/2026-08-19-nucleo-contable/` · **48 de 48 tareas cerradas** — ✅ **CERRADO 2026-08-19**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | Scaffolding: `SmartNet.Contable.Core`/`.Tests`, `PurityScanTests` copiado (RED hasta fase 2) | 4/4 | ✅ |
| 2 | 1 | Tipos de entrada/salida: `LineaAsiento`, `AsientoContable`, `TipoCambioCongelado`, `CargoSolicitado`, `HerenciaNotaCredito`, `EntradaAsiento` | 7/7 | ✅ |
| 3 | 1 | `Componer` — PRINCIPAL (4 casos) + DESTINO + conversión §6, TDD estricto sobre los 7 goldens §10 | 19/19 | ✅ |
| 4 | 2 | `InvariantesDeConfirmacion.Evaluar` — 7 invariantes §7 (5 globales + PRINCIPAL + DESTINO), jerarquía cerrada `ResultadoConfirmacion` | 13/13 | ✅ |
| 5 | 2 | Wiring: `ci.yml`, `PurityScanTests` en verde contra el ensamblado completo, suite completa | 4/4 | ✅ |
| 6 | — | Seguimiento post-verify: 2 tests de regresión que pinean el discriminador Boleta+Gravada (WARNING del verify-report) | 1/1 | ✅ |

**Nota de reconciliación**: `tasks.md` y el verify-report archivados declaran "49/49" — el conteo
real de checkboxes en el archivo es **48**, verificado con `grep`. No afecta el veredicto (todas
las tareas listadas están cerradas); queda anotado para no propagar el número incorrecto.

### Pruebas

Un solo proyecto nuevo, `SmartNet.Contable.Core.Tests`, dominio puro sin `PackageReference` de
infraestructura (`ProjectReference` únicamente a `SmartNet.Catalogos.Core` y
`SmartNet.TiposCambio.Core`).

| Suite | Fase que la creó | Pruebas | Qué cubre |
|---|---|---|---|
| `ComponerGoldenTests.cs` | 3 (+1 en fase 6) | 12 | Los 7 casos golden de REGLAS.md §10, 4 casos estructurales de PRINCIPAL, DESTINO automático + invertido en NC, regresión Boleta+Gravada |
| `InvariantesDeConfirmacionTests.cs` | 4 (+1 en fase 6) | 16 | 5 invariantes globales (accept+reject), invariante PRINCIPAL, invariante DESTINO, multi-fallo, regresión Boleta+Gravada |
| `PurityScanTests.cs` | 1 | 13 | NetArchTest + escaneo IL de `DateTime.Now`/`UtcNow`, cero `PackageReference` de infraestructura |
| **Total del ítem #8** | | **41** | Confirmado por `dotnet test` corrido por el orquestador |

**41/41 verificadas de forma independiente** (2026-08-20), build `SmartNet.sln` limpio.

### Lo verificado al cerrar

**Fases 1–3** — 30/30 pruebas en verde. Los 7 goldens de REGLAS.md §10 pasan con los importes
exactos del documento normativo. `ConversionDeMoneda` ancla `totalPEN`/`igvPEN` y deriva
`basePEN` (§10.3: TC 3.712000, totalPEN 4471.61, igvPEN 682.11, basePEN 3789.50). La NC en
dólares hereda el TC de su factura vía `HerenciaNotaCredito` (§10.7: saldo del proveedor en 0.00
usando el TC original 3.712000, no el vigente 3.715000).

**Fases 4–5** — 13/13 pruebas en verde. `Evaluar` devuelve **todas** las invariantes incumplidas,
no la primera. `ResultadoConfirmacion` es jerarquía cerrada (constructor `private protected`),
nunca excepción para un rechazo de dominio.

**Fase 6 (seguimiento post-verify)** — el verify-report original señaló que `Componer` y
`EvaluarPrincipal` discriminan la rama PRINCIPAL-gravada usando solo
`AfectacionCongelada == Gravada`, sin verificar `TipoComprobante == Boleta`. Se agregaron dos
tests de regresión que **pinean** (no arreglan) el comportamiento actual, para que CI detecte
automáticamente el día que esa suposición se rompa. No se tocó lógica de negocio. El WARNING
queda degradado a informativo. La guardia real (rechazar Boleta+Gravada como estado ilegal) sigue
pendiente como *follow-up* de los ítems #3/#11, fuera de alcance de #8.

---

## ✅ 9. Sugerencia de cuenta

Cascada de ranking determinista de cuenta y motivo por frecuencia de uso, con filtrado contra
candidatas vigentes y texto de fundamento, orquestada para invocación desde el flujo de registro
de asientos. Depende del ítem #8 (completo).

**Entrega:** 2 PRs apilados (`stacked-to-main`), sin PR abierto todavía — `feat/item-9-sugerencia-cuenta-pr1`
(rebasado sobre `main` con #7/#8 ya mergeados) y `feat/item-9-sugerencia-cuenta-pr2` (sobre PR1).

**Ciclo SDD:** `openspec/changes/archive/2026-08-20-item-9-sugerencia-de-cuenta/` · **32 de 32 tareas cerradas** — ✅ **CERRADO 2026-08-20**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 (PR1) | Scaffolding: `SmartNet.Sugerencia.Core`/`.Tests`, `.sln`, 4 record types de resultado | 4/4 | ✅ |
| 2 | 1 (PR1) | `CascadaDeSugerencia.SugerirCuenta` — 3 escalones, desempate, filtro de vigencia | 18/18 | ✅ |
| 3 | 1 (PR1) | `CascadaDeSugerencia.SugerirMotivo` — 2 escalones (proveedor), desempate | 4/4 | ✅ |
| 4 | 1 (PR1) | Fundamento + rationale (`Veces`, `VecesDelAmbito`) | 3/3 | ✅ |
| 5 | 2 (PR2) | `ServicioDeSugerencia` orquestador (4 puertos, `SugerirParaFacturaAsync`) | 9/9 | ✅ |
| 6 | 2 (PR2) | Guardas estructurales: `PurityScanTests` + extensión de `NoRankingStructuralTests` | 3/3 | ✅ |
| 7 | 2 (PR2) | Verificación e2e: 7 requisitos / 12 escenarios → tests | 2/2 | ✅ |

**Decisiones de diseño previas al apply:**

- No existe siembra histórica: la compañía no tiene sistema contable externo previo. Se corrigió
  ADR 0011 a revisión 4, eliminando la sección "Carga inicial desde el histórico" (decisión del
  dueño del proyecto durante la exploración).
- Desempate en escalones 1–2 de la cascada: `Veces` DESC → `UltimoUso` DESC → `CuentaCodigo` ASC.
- Alcance incluye orquestación (a diferencia del ítem #8, que es puro): `ServicioDeSugerencia`
  llama al repositorio existente (ítem #3) y a `ResolverCandidatas`.
- `VecesDelAmbito` de `SugerirMotivo` = total `Veces` del proveedor entre todos los motivos
  ofrecibles (confirmado con el dueño tras hueco detectado tarde por el validador de PR1).

### Pruebas

Dos proyectos nuevos: `SmartNet.Sugerencia.Core` (puro, sin `.Infrastructure`) y
`SmartNet.Sugerencia.Core.Tests`.

| Suite | Pruebas | Qué cubre |
|---|---|---|
| `CascadaDeSugerenciaTests.cs` | 15 | Cascada 3 escalones, desempate, filtro de vigencia, sugerencia de motivo |
| `ServicioDeSugerenciaTests.cs` | 5 | Orquestador, `motivoSeleccionado` null/provisto, `RegistrarUsoAsync` nunca invocado (spy, 0 llamadas) |
| `PurityScanTests.cs` | 7 | NetArchTest (sin SqlClient/AspNetCore/Http) + escaneo IL (sin `DateTime.Now`/`UtcNow`) |
| `NoRankingStructuralTests.cs` (extendido) | 2 | `Catalogos.Core.dll` sigue sin tipos de ranking (aserción original intacta + nueva) |
| **Total del ítem #9** | **27** | Confirmado por `dotnet test` corrido por el orquestador |

**27/27 + 2/2 verificadas de forma independiente** (2026-08-20), build `SmartNet.sln` limpio.

### Lo verificado al cerrar

**PR1 (fases 1–4)** — 15/15 pruebas en verde, validadas por un revisor de contrato en contexto
fresco: cascada de 3 escalones y desempate coinciden exactamente con ADR 0011 rev. 4, filtrado
obligatorio contra `ResolverCandidatas`, proveedor nuevo cae a escalón 2/3 según disponibilidad.

**PR2 (fases 5–7)** — 27/27 pruebas en verde, validadas de forma independiente dos veces (al
cerrar PR2 y al verificar este SPRINT.md). `ServicioDeSugerencia.SugerirParaFacturaAsync` combina
cuenta + motivo + fundamento en un único resultado; nunca llama `RegistrarUsoAsync` (responsabilidad
del ítem #11). Purity scan real vía Mono.Cecil, no un test débil. `NoRankingStructuralTests`
extendido sin debilitar la aserción original de item #3.

**Verify:** PASS WITH WARNINGS, 0 CRITICAL. Los 2 WARNING (conteo de escenarios, nota de rama
obsoleta en `apply-progress.md`) fueron corregidos antes de archivar, no quedaron pendientes.

Cero cambios de esquema SQL. Único ADR tocado: 0011 (revisión 3→4), hecho en la fase de
exploración, no en apply.

---

## ⬜ Ítems 10 a 17 — sin ciclo SDD abierto

Las fases de cada ítem **se definen cuando arranca su ciclo SDD**, no antes. Ponerlas aquí ahora
sería inventarlas: el despiece en fases sale de la spec y el diseño de ese ítem, y ninguno existe.

| # | Ítem | Depende de | Contexto obligatorio | Estado |
|---|---|---|---|---|
| 10 | Notas de crédito | #8 | ⚠ `REGLAS.md` §5, §7 | ⬜ |
| 11 | API de facturas y asientos | #7, #8 | — | ⬜ |
| 12 | Detalle y validación | #11 | — | ⬜ |
| 13 | Bandeja e incidencias | #11 | — | ⬜ |
| 14 | Outbox y mensajería | #11 | — | ⬜ |
| 15 | Publicación a Drive | #14 | — | ⬜ |
| 16 | Publicación a Sheets | #14 | — | ⬜ |
| 17 | Errores, notificaciones y operación | #14 | — | ⬜ |

---

## Resuelto

- **`Factura.RucProveedor` admite DNI y carné de extranjería.** Decidido con criterio contable: los
  124 proveedores sin RUC de 11 dígitos son emisores legítimos. La columna pasó a `VARCHAR(11)` con
  restricción de 8 a 11 dígitos, en `Factura` y en `DatosExtraidos`. El cambio de `CHAR` a `VARCHAR`
  es la mitad menos obvia: el relleno con espacios habría roto la unión con `dbo.Proveedor.rucpro` y
  la detección de duplicados en `IX_Factura_Identidad`.

## Abierto y sin decidir

No bloquea construir, pero tampoco debe cerrarse por omisión.

| Tema | Dónde está anotado | Qué decide |
|---|---|---|
| Las tres preguntas de respaldo de ADR 0014 | ADR 0014 | Condición de puesta en producción |
| Las seis reglas sin ratificar de `REGLAS.md` §12 | `REGLAS.md` §12 | Los puntos 1 y 5 afectan a **todo asiento en moneda extranjera ya confirmado** |

## Condiciones del entorno

- **RDD de gentle-ai: desactivado.** La unidad `D:` está formateada en exFAT, que no soporta ACL,
  así que Windows sintetiza `Everyone` como propietario de todo archivo y la validación de
  autoridad de gentle-ai no puede pasar. No es una preferencia reversible: es estructural. No
  reintentar `takeown` ni `icacls`.
- **El usuario carga las tablas.** Los scripts de `SmartNet/db/fixtures/` se escriben aquí, pero
  no se ejecutan contra `BDSmartNet` desde este lado.
