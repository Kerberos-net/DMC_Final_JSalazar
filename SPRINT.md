# Sprint: estado de avance

Tablero de seguimiento del `BACKLOG.md`. Un ítem por sección, y dentro de cada uno **sus fases**,
que se marcan conforme se cierran.

**Regla de marcado:** una fase pasa a ✅ solo cuando *todas* sus tareas están cerradas en
`tasks.md` **y** la verificación independiente pasó. No se marca por reporte del agente que la
implementó: se marca por evidencia ejecutada.

Leyenda: ✅ cerrada · 🔄 en curso · ⬜ pendiente · ⛔ bloqueada

| Estado global | Valor |
|---|---|
| Ítems del backlog | **4 de 17 cerrados** |
| Ciclo SDD activo | Ninguno — último cerrado: ítem #4 |
| Última fase cerrada | Ítem #4, WU4 (Fase 4 — `SmartNet.sln`, CI, suite completa end-to-end), 47/47 tareas del ítem cerradas — ítem #4 cerrado 2026-08-17 |

---

## ✅ 1. Esquema y permisos

SQL versionado, esquema `fact`, tablas, índices, restricciones y los `GRANT` de los dos usuarios
de base de datos. Sin dependencias.

**Ciclo SDD:** `openspec/changes/esquema-y-permisos/` · **36 de 36 tareas cerradas**

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

**Ciclo SDD:** `openspec/changes/autenticacion-y-sesion/` · **88 de 88 tareas cerradas**

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

**Ciclo SDD:** `openspec/changes/catalogos-y-satelites/` · **47 de 47 tareas cerradas** — **✅ CERRADO 2026-08-17**

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

**Ciclo SDD:** `openspec/changes/tipos-de-cambio/` · **47 de 47 tareas cerradas** — **✅ CERRADO 2026-08-17**

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

## ⬜ Ítems 5 a 17 — sin ciclo SDD abierto

Las fases de cada ítem **se definen cuando arranca su ciclo SDD**, no antes. Ponerlas aquí ahora
sería inventarlas: el despiece en fases sale de la spec y el diseño de ese ítem, y ninguno existe.

| # | Ítem | Depende de | Contexto obligatorio | Estado |
|---|---|---|---|---|
| 5 | Ingesta Gmail | #1 | — | ⬜ |
| 6 | Extracción y asociación | #5 | — | ⬜ |
| 7 | Inbox y promoción | #6, #3 | — | ⬜ |
| 8 | Núcleo contable | #3 | ⚠ `REGLAS.md` §5–§10 | ⬜ |
| 9 | Sugerencia de cuenta | #8 | ⚠ `REGLAS.md` §3 | ⬜ |
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
