# Exploración — BACKLOG #9 Sugerencia de cuenta

## Estado actual

El ítem #8 (núcleo-contable) está cerrado — la única dependencia declarada de #9 está
satisfecha. El ítem #3 (catálogos-y-satélites) ya entregó los insumos que #9 necesita, pero
excluyó deliberadamente el ranking:

- Tabla `fact.SugerenciaCuenta` existe (`SmartNet/db/schema/004_satelites_datos_maestros.sql`):
  PK `(ProveedorCodigo, Motivo, CuentaCodigo)`, columnas `Veces`, `UltimoUso`.
- `SmartNet.Catalogos.Core.ISugerenciaCuentaRepository`
  (`SmartNet/catalogos/SmartNet.Catalogos.Core/ISugerenciaCuentaRepository.cs`) expone
  `ListarPorProveedorYMotivoAsync`, `ListarPorMotivoAsync`, `ListarPorProveedorAsync`,
  `RegistrarUsoAsync(...)` — solo almacenamiento, estructuralmente blindado contra ranking
  (`NoRankingStructuralTests.cs`). `RegistrarUsoAsync` recibe `instante` como parámetro (no
  `SYSUTCDATETIME()`) a propósito, para que #9 sea determinístico de testear.
- `ResolverCandidatas` (función pura en `SmartNet.Catalogos.Core`) resuelve los prefijos de un
  motivo a cuentas hoja de 6 dígitos. La Decisión 2 del `design.md` del ítem #3 establece
  explícitamente que #3 entrega la función, **#9 entrega el sitio de invocación** — #9 debe
  filtrar los códigos de `SugerenciaCuenta` almacenados contra la salida en vivo de
  `ResolverCandidatas` (el invariante real es "es candidata de este motivo", no "existe en
  `dbo.CuentaContable`").
- No existe ningún proyecto `SmartNet.SugerenciaCuenta.*` — #9 es terreno nuevo para su propio
  módulo.

## Reglas normativas

REGLAS.md §3 (líneas 115–128) y ADR `adrs\0011-motivo-de-compra-y-sugerencia-de-cuenta.md`
(autoritativo, revisión 3, resuelve los hallazgos adversariales A10/S4):

**Cascada de sugerencia**: (1) cuenta más usada para `(ProveedorCodigo, Motivo)`, (2) si no hay
histórico, la más usada para el `Motivo` a nivel global, (3) si sigue sin haber, la primera
candidata del motivo **ordenada por `CuentaCodigo`** — ADR 0011 es explícito en que ese
`ORDER BY` es estructural, no incidental. `Veces` solo se incrementa al confirmar el asiento
(trabajo del ítem #11), nunca al momento de sugerir. El mismo mecanismo, indexado solo por
proveedor, también sugiere el **motivo**. La sugerencia nunca decide sola — la UI (ítem #12)
debe mostrar el texto de justificación, por lo que #9 debe exponerlo como dato.

**Siembra histórica**: la sección "Carga inicial desde el histórico" de ADR 0011 exige una
siembra única e idempotente de `SugerenciaCuenta` a partir de "los asientos históricos que el
sistema contable mantiene en esta misma base" (ADR 0003). BACKLOG.md ubica esta siembra dentro
del alcance declarado de #9; el `spec.md` de `esquema-y-permisos` (ítem #3) la difiere
explícitamente ("belongs to item #3... #9's job", ver `design.md` línea 107: "la siembra
histórica de #9 es N llamadas a `RegistrarUsoAsync`").

## Áreas afectadas

- `SmartNet/catalogos/SmartNet.Catalogos.Core/` y `.Infrastructure/` — insumos directos.
- `SmartNet/db/schema/008_usuarios_y_permisos.sql` — posible `GRANT SELECT` nuevo para la fuente
  de asientos históricos, lo que cruza de vuelta al territorio del ítem #1 (cerrado).
- REGLAS.md §3, `adrs\0011-motivo-de-compra-y-sugerencia-de-cuenta.md` — únicas fuentes
  normativas.

## Preguntas abiertas (NO resueltas — para sdd-propose/sdd-design)

1. **La tabla/vista de asientos históricos nunca se nombra** en ningún documento (REGLAS.md, ADR
   0011, ADR 0003) — solo dice "vive en la misma base". Se necesita que el dueño del proyecto
   identifique el objeto `dbo.*` real antes de poder diseñar.
2. **El SQL de ejemplo de la siembra en ADR 0011 agrupa solo por `(ProveedorCodigo,
   CuentaCodigo)`**, omitiendo `Motivo` — pero `fact.SugerenciaCuenta.Motivo` es `NOT NULL` y
   parte de la PK, y el sistema contable externo casi seguro no tiene concepto de "Motivo" (es
   invención propia de este proyecto). Cómo la siembra deriva/asigna `Motivo` por línea histórica
   queda sin resolver — es un hueco de diseño real, no solo un detalle faltante.
3. **Expansión de permisos**: la siembra necesita `SELECT` sobre una 6ª tabla `dbo.*` además de
   las 5 ya otorgadas y cerradas por `esquema-y-permisos/spec.md` ("the only objects listed are
   exactly these 5"). No está claro si #9 puede agregar este grant por su cuenta o necesita una
   decisión formal que reabra el ítem #1.
4. **Desempate dentro de los pasos 1–2 de la cascada** no está especificado (solo el paso 3 tiene
   `ORDER BY CuentaCodigo`). Se necesita una regla explícita (p. ej. `UltimoUso DESC, CuentaCodigo
   ASC`) para determinismo de punta a punta (espíritu de ADR 0019).
5. **Ubicación estructural**: ¿proyecto core nuevo o extender `SmartNet.Catalogos.Core`?, y cuánto
   de la cascada puede ser función pura/testeable (dado un conjunto de filas pre-obtenidas) versus
   orquestación que toca infraestructura — sin decidir.
6. **Mecanismo de siembra idempotente** (delete-reinsert vs. upsert-with-max vs. guard de una sola
   ejecución) y si corre como SQL versionado (ADR 0016, restringido al esquema `fact`) o como
   comando administrativo .NET (patrón usado para la creación del primer usuario del ítem #2) —
   sin decidir.

## Recomendación

Dividir #9 en dos work units: **WU1** = cascada de ranking + filtrado de candidatas + sugerencia
de motivo (totalmente especificado, sin bloqueos, listo para `sdd-propose`) y **WU2** = siembra
histórica (bloqueado en las preguntas abiertas 1–3, necesita input del dueño del proyecto
primero).

---

**Status**: partial
**Next recommended**: sdd-propose (recomendando dividir en WU1 ranking-cascade / WU2
historical-seed, según las Preguntas abiertas de arriba)
**Risks**:
- La fuente de datos de la siembra histórica no está nombrada en ningún documento del proyecto —
  bloqueo real para el diseño de WU2.
- El SQL de siembra omite `Motivo` pese a ser columna requerida de la PK — hueco de mapeo sin
  resolver.
- La siembra puede requerir reabrir el cambio de esquema/permisos cerrado del ítem #1 para
  agregar un 6º grant sobre tabla externa.
- Sin regla de desempate para los pasos 1–2 de la cascada hay riesgo de sugerencias no
  determinísticas (viola el espíritu de determinismo del proyecto, ADR 0019).
