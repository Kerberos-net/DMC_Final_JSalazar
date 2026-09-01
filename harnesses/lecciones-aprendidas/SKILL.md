---
name: lecciones-aprendidas
description: >-
  Revisa un cambio SDD recien archivado, identifica los patrones tecnicos /
  convenciones y las reglas contables de REGLAS.md aplicadas por primera vez,
  escribe una nota de leccion aprendida en el vault de Obsidian y, como segundo
  paso independiente, actualiza la seccion del item en SPRINT.md con el formato
  de los items anteriores. Use when an SDD cycle was just archived (or the user
  runs /lecciones-aprendidas) and the new concepts applied in that change should
  be captured as an Obsidian note and reflected in SPRINT.md.
---

# Lecciones aprendidas (post sdd-archive)

## Proposito

Dos pasos, un solo foco — **dejar registrado lo que produjo un ciclo SDD apenas
se cierra**:

1. **Nota en Obsidian** con los conceptos nuevos aplicados.
2. **Actualizar la seccion del item en `SPRINT.md`** con el formato de los items
   anteriores.

No audita codigo, no reabre el cambio, no toca specs ni ADRs. Lee lo archivado y,
con tu decision en cada paso, escribe la nota y actualiza `SPRINT.md`. Los dos
pasos son **independientes**: si te salteas la nota, igual se te ofrece el paso de
`SPRINT.md`, y viceversa.

## Alcance de "concepto nuevo aplicado" (paso 1, la nota)

Solo dos categorias:

1. **Patrones tecnicos / convenciones** — patrones de codigo, convenciones de
   `CONVENTIONS.md`, tecnicas de implementacion usadas por primera vez o de forma
   notable en este cambio.
2. **Reglas contables de `REGLAS.md`** — reglas o ejemplos numericos de `REGLAS.md`
   que este ciclo implemento o afino.

ADRs y decisiones de `design.md` **no** entran salvo que el usuario lo pida en el
momento.

## Contexto a leer cada corrida

- La carpeta del cambio archivado: `openspec/changes/archive/<fecha>-<nombre>/`
  (`proposal.md`, `design.md`, `tasks.md`, `apply-progress.md`, `archive-report.md`,
  `verify-report.md`, `specs/`).
- `REGLAS.md` en la raiz del repo (normativo — sus siete ejemplos son casos de prueba).
- `CONVENTIONS.md` en la raiz del repo.
- `SPRINT.md` en la raiz del repo — **para copiar el formato de un item cerrado
  reciente** (usa el ultimo `## ✅ N.` como plantilla) y para ubicar donde va este
  item (seccion propia ya existente, o fila en la tabla "Ítems … sin ciclo SDD
  abierto" + su subseccion "#N — alcance recordado").
- El diff real del cambio si hace falta confirmar que un patron es nuevo, o los
  conteos de pruebas antes/despues (`git log`/`git diff` acotado a los archivos que
  menciona `apply-progress.md`).

## Flujo

1. **Identificar el cambio.** Si el hook te paso el nombre de carpeta, usa esa. Si
   te invocaron manualmente sin argumento, toma la carpeta mas reciente de
   `openspec/changes/archive/` y confirma con el usuario cual es.
2. **Leer el contexto** de la seccion anterior.

### Paso 1 — nota en Obsidian

3. **Extraer candidatos.** Arma una lista de los conceptos nuevos aplicados,
   clasificados en las dos categorias. Para cada uno: una linea de que es y donde
   se aplico (archivo/regla). Si no hay candidatos reales, dilo y pasa al Paso 2
   sin escribir nota.
4. **Presentar lista numerada** al usuario y **esperar su seleccion** (todos,
   algunos por numero, o ninguno). No sigas sin respuesta.
5. **Redactar la nota** con los conceptos elegidos (ver "Formato de la nota") y
   mostrar el borrador completo al usuario.
6. **Esperar aprobacion explicita** del contenido. Recien entonces escribir el
   archivo en `D:\Notas\Kerberos\Lecciones aprendidas\<Nombre Descriptivo>.md`.
7. Si el usuario dice "salto" / "despues" / elige "ninguno": **no escribas nota** y
   pasa al Paso 2 igual (los pasos son independientes).

### Paso 2 — actualizar SPRINT.md

8. **Redactar la actualizacion de `SPRINT.md`** (ver "Formato de la actualizacion
   de SPRINT.md"). Toma los numeros de `verify-report.md`, `tasks.md`,
   `apply-progress.md` y `archive-report.md` — nunca inventes conteos de pruebas
   ni de tareas; si un dato no esta, marcalo como pendiente en el borrador y
   preguntalo.
9. **Mostrar el borrador completo** de los bloques que vas a cambiar: (a) la
   seccion del item y (b) los tres renglones de la tabla "Estado global". Presenta
   el antes/despues de cada bloque.
10. **Esperar aprobacion explicita.** Recien entonces editar `SPRINT.md`.
11. Si el usuario dice "salto" / "despues": termina sin tocar `SPRINT.md`. El
    marcador del hook ya quedo actualizado, asi que hay que reinvocar
    `/lecciones-aprendidas` a mano para retomar.

## Formato de la nota

- **Nombre de archivo:** descriptivo del tema, no la fecha ni el id del item.
  Ej. `Contabilidad por destino.md`, `Concurrencia optimista con ETag.md`.
- **Etiqueta en ambos lugares:**
  - frontmatter: `tags: [lecciones-aprendidas]`
  - cuerpo: una linea con `#lecciones-aprendidas`
- Estructura:

```markdown
---
tags: [lecciones-aprendidas]
fecha: <YYYY-MM-DD>
cambio: <fecha>-<nombre-carpeta-archivada>
---

#lecciones-aprendidas

# <Nombre Descriptivo>

## Contexto
<1-2 frases: que ciclo SDD lo produjo y por que aparecio el concepto>

## Patrones tecnicos / convenciones
- **<nombre>** — <que es, donde se aplico, por que importa>

## Reglas contables (REGLAS.md)
- **<regla / ejemplo>** — <que se implemento o afino>

## Para recordar
<la leccion en si: que harias igual o distinto la proxima vez>
```

Omite una seccion de categoria si el usuario no eligio nada de ella.

## Formato de la actualizacion de SPRINT.md

**Plantilla:** el ultimo item cerrado del archivo (el `## ✅ N.` con numero mas
alto). Copia su estructura exacta, no una aproximada.

**a) Seccion del item.** Debe quedar con:

- Encabezado `## ✅ N. <titulo del item>` (estado `✅`).
- Parrafo de intro (1-2 frases: que hacia falta y que resolvio este ciclo) —
  destilado de `proposal.md`.
- Linea **`**Ciclo SDD:**`** con la ruta de la carpeta archivada y
  `**M de M tareas cerradas** — ✅ **CERRADO <YYYY-MM-DD>**`.
- Tabla de fases: `| Fase | Unidad | Alcance | Tareas | Estado |`, una fila por
  fase/unidad de `tasks.md`, cada una `X/X` y `✅`.
- `### Pruebas` — tabla con conteos antes/despues por suite y el total, tal como
  aparecen en `verify-report.md` / `apply-progress.md`. Una frase de que fue
  ejecutado por el orquestador y contra que (SQL Server local real, `npm run
  lint`/`build`, etc.).
- `### Decisiones de diseño` — bullets con las decisiones `Dn` de `design.md` que
  el usuario haya marcado como relevantes; si no marco ninguna, incluye solo las
  que `verify-report.md` o `archive-report.md` destacan.
- `### Elementos conocidos, no ocultos` — el conteo de `sdd-verify`
  (`N CRITICAL, N WARNING, N SUGGESTION`) y un bullet por cada WARNING/SUGGESTION
  con su estado (reconciliada / aceptada / atendida / arrastrada). Cierra con una
  linea `*Follow-ups:*` si `verify-report.md` lista alguno, y con la nota de
  entrega (`size:exception`, commits apilados, RDD desactivado) que llevan todos
  los items previos.
- Separador `---` al final.

**Ubicacion:** inserta la seccion en **orden numerico** entre los `## ✅`
existentes. Si el item estaba en la tabla "Ítems … sin ciclo SDD abierto":
elimina su fila de esa tabla **y** su subseccion "### #N — alcance recordado", y
ajusta el titulo de esa seccion agrupada si el item era el unico o cambia la lista.

**b) Tabla "Estado global"** (tope del archivo) — actualiza exactamente estos tres
renglones:

- **Ítems del backlog** — incrementa el conteo "X de Y cerrados" y mueve el item
  de la lista de abiertos a la de cerrados, con su fecha.
- **Ciclo SDD activo** — a `Ninguno — último cerrado: ítem #N (<titulo corto>), <fecha>`.
- **Última fase cerrada** — resumen de una frase: item, nº de fases, `T/T` tareas,
  resultado de `verify` (`PASS` / `PASS WITH WARNINGS` con el desglose), specs
  nuevas/delta, y los commits.

No toques ningun otro renglon ni ninguna otra seccion del archivo.

## Guardrails (duros)

- **Nunca sobrescribir** una nota de Obsidian existente con ese nombre sin
  preguntar. Si el archivo ya existe, mostrar el conflicto y ofrecer: renombrar,
  fusionar contenido, o cancelar.
- **Nunca escribir en el vault** (`D:\Notas\Kerberos`) hasta que el usuario apruebe
  el contenido final.
- **`SPRINT.md` es el UNICO archivo del repo que este flujo puede modificar.**
  Sigue prohibido tocar codigo, `openspec/` (specs, proposal, design, tasks),
  ADRs, `REGLAS.md`, `CONVENTIONS.md`, `BACKLOG.md` o cualquier otro archivo. El
  resto del proyecto es solo lectura.
- **Nunca editar `SPRINT.md` hasta que el usuario apruebe el borrador** del Paso 2
  (antes/despues de ambos bloques).
- **Dentro de `SPRINT.md`, solo la seccion de este item y los tres renglones de la
  tabla "Estado global".** No reformatees, reordenes ni "arregles de paso" ninguna
  otra seccion ni la de otro item.
- **Nunca inventar numeros.** Conteos de pruebas, de tareas y de hallazgos de
  `verify` salen de los artefactos archivados. Si falta un dato, se pregunta.
- Si el usuario dice "salto" / "despues" en un paso, ese paso no escribe nada. El
  marcador del hook ya quedo actualizado, asi que hay que reinvocar
  `/lecciones-aprendidas` a mano para retomar lo que falte.

## Herramientas

Para la nota: la skill `obsidian-cli` o `obsidian-markdown`, o `Write` directo si
la ruta del vault es accesible. La carpeta `Lecciones aprendidas/` ya existe.

Para `SPRINT.md`: `Read` la seccion del ultimo item cerrado como plantilla y
`Edit` con reemplazos acotados — nunca `Write` sobre el archivo completo (1800+
lineas; un `Write` arriesga perder el resto).
