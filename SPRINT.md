# Sprint: estado de avance

Tablero de seguimiento del `BACKLOG.md`. Un ítem por sección, y dentro de cada uno **sus fases**,
que se marcan conforme se cierran.

**Regla de marcado:** una fase pasa a ✅ solo cuando *todas* sus tareas están cerradas en
`tasks.md` **y** la verificación independiente pasó. No se marca por reporte del agente que la
implementó: se marca por evidencia ejecutada.

Leyenda: ✅ cerrada · 🔄 en curso · ⬜ pendiente · ⛔ bloqueada

| Estado global | Valor |
|---|---|
| Ítems del backlog | 1 de 17 en curso, 0 cerrados |
| Ciclo SDD activo | `openspec/changes/esquema-y-permisos/` |
| Última fase cerrada | Ítem #1, fase 4 — datos base (la 5 va 4/5: falta cablear CI) |

---

## 🔄 1. Esquema y permisos

SQL versionado, esquema `fact`, tablas, índices, restricciones y los `GRANT` de los dos usuarios
de base de datos. Sin dependencias.

**Ciclo SDD:** `openspec/changes/esquema-y-permisos/` · **33 de 36 tareas cerradas**

| Fase | Unidad | Alcance | Tareas | Estado |
|---|---|---|---|---|
| 1 | 1 | Runner DbUp + arnés de pruebas (`test-bootstrap`) | 5/5 | ✅ |
| 2 | 2 | Estructura del esquema `001`–`007` + pruebas de forma | 13/13 | ✅ |
| 3 | 3 | Matriz de permisos `008` + pruebas nivel 2 de ADR 0019 | 4/4 | ✅ |
| 4 | 4 | Datos base `009`–`010` (`EstadoIntegracion`, `Configuracion`, 23 `MotivoAtributo`) | 7/7 | ✅ |
| 5 | 5 | Manifiesto de *checksums* + scripts de *rollback* consultivos | 4/5 | 🔄 |
| 6 | 5 | Integración: suite completa end-to-end sobre base nueva | 0/2 | ⬜ |

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


**Fase 5 — 4 de 5.** 102/102 pruebas en verde, verificadas ejecutándolas yo. El manifiesto
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

Queda **la tarea 5.3**: cablear la canalización de CI de dos trabajos que el usuario eligió. Las
comprobaciones ya son un comando cada una; falta solo el envoltorio.
### Deuda declarada, no olvidada

- ~~**Tarea 1.5** — la aserción literal de idempotencia de `008`~~ **saldada en la fase 3**, y mejor
  de lo pedido: la prueba borra la fila de `008` en el journal de DbUp antes de reejecutarlo, porque
  si no DbUp lo saltaría y la prueba pasaría sin probar nada.
- ~~El *lint* de `dbo.` de la tarea 5.5~~ **adelantado a la fase 3**. Es una prueba estática sobre el
  texto de los scripts, con sus propios casos negativos sintéticos que demuestran que muerde. Quedó
  así porque la aserción «ninguna tabla fuera de `fact`» hubo que relajarla a «fuera de `fact` o
  `dbo`» —el arnés crea los catálogos de prueba en `dbo`— y eso dejaba la invariante sin guardián.

---

## ⬜ Ítems 2 a 17 — sin ciclo SDD abierto

Las fases de cada ítem **se definen cuando arranca su ciclo SDD**, no antes. Ponerlas aquí ahora
sería inventarlas: el despiece en fases sale de la spec y el diseño de ese ítem, y ninguno existe.

| # | Ítem | Depende de | Contexto obligatorio | Estado |
|---|---|---|---|---|
| 2 | Autenticación y sesión | #1 | — | ⬜ |
| 3 | Catálogos y satélites | #1 | ⚠ `Cuentas.xlsx` | ⬜ |
| 4 | Tipos de cambio | #1 | — | ⬜ |
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

## Abierto y sin decidir

No bloquea construir, pero tampoco debe cerrarse por omisión.

| Tema | Dónde está anotado | Qué decide |
|---|---|---|
| `Factura.RucProveedor` admite 11 dígitos, pero 124 proveedores tienen DNI o carné | `SmartNet/db/fixtures/README.md` | Criterio contable. Afecta al ítem #3 y al #7 |
| Las tres preguntas de respaldo de ADR 0014 | ADR 0014 | Condición de puesta en producción |
| Las seis reglas sin ratificar de `REGLAS.md` §12 | `REGLAS.md` §12 | Los puntos 1 y 5 afectan a **todo asiento en moneda extranjera ya confirmado** |

## Condiciones del entorno

- **RDD de gentle-ai: desactivado.** La unidad `D:` está formateada en exFAT, que no soporta ACL,
  así que Windows sintetiza `Everyone` como propietario de todo archivo y la validación de
  autoridad de gentle-ai no puede pasar. No es una preferencia reversible: es estructural. No
  reintentar `takeown` ni `icacls`.
- **El usuario carga las tablas.** Los scripts de `SmartNet/db/fixtures/` se escriben aquí, pero
  no se ejecutan contra `BDSmartNet` desde este lado.
