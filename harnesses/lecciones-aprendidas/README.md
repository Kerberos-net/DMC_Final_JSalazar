# Harness: lecciones aprendidas post sdd-archive

Deja registrado lo que produjo un ciclo SDD apenas se cierra. Cuando `sdd-archive`
deja una carpeta nueva en `openspec/changes/archive/`, un hook te pide (al terminar
tu turno) correr la skill, que hace dos pasos independientes:

1. Con tu seleccion, escribe una nota en el vault de Obsidian con la etiqueta
   `lecciones-aprendidas`.
2. Con tu aprobacion, actualiza la seccion del item en `SPRINT.md` (seccion
   completa + tabla "Estado global") con el formato de los items anteriores.

Un solo proposito: el registro post-archive. No audita codigo, no reabre el
cambio, no toca specs ni ADRs.

## Piezas

| Pieza | Archivo | Para que |
|---|---|---|
| Hook `Stop` | `detect-archive.js` | Detecta una carpeta nueva bajo `openspec/changes/archive/` comparando contra el marcador `.claude/.lecciones-aprendidas-seen`. Si hay algo nuevo, devuelve `{"decision":"block","reason":...}` para que Claude corra la skill antes de cerrar el turno. Guarda el marcador **antes** de bloquear, asi el `Stop` siguiente no entra en bucle. En la primera corrida siembra el marcador con todo el historial (no dispara con archivos viejos). |
| Skill `lecciones-aprendidas` | `SKILL.md` | El trabajo, en dos pasos. **Paso 1:** lee la carpeta archivada + `REGLAS.md` + `CONVENTIONS.md`, arma una lista numerada de patrones tecnicos/convenciones y reglas contables aplicadas por primera vez, espera tu seleccion, redacta el borrador, espera tu aprobacion y escribe el `.md` en `D:\Notas\Kerberos\Lecciones aprendidas\`. **Paso 2 (independiente):** con `verify-report.md` / `tasks.md` / `apply-progress.md` redacta la seccion del item para `SPRINT.md` copiando el formato del ultimo item cerrado + los tres renglones de la tabla "Estado global", te muestra el antes/despues y, con tu aprobacion, edita `SPRINT.md`. |

Sin sub-agente: el contexto que necesita (una carpeta de cambio + dos archivos de
raiz) cabe en el hilo principal.

## Guardrails (en la skill)

- Nunca sobrescribe una nota de Obsidian existente sin preguntar.
- Nunca escribe en el vault hasta que apruebes el contenido final.
- `SPRINT.md` es el **unico** archivo del repo que el flujo puede modificar — y
  solo la seccion de este item + los tres renglones de "Estado global". Codigo,
  `openspec/`, ADRs, `REGLAS.md`, `CONVENTIONS.md`, `BACKLOG.md`: solo lectura.
- Nunca edita `SPRINT.md` hasta que apruebes el borrador (antes/despues).
- Nunca inventa conteos de pruebas / tareas / hallazgos de `verify`: salen de los
  artefactos archivados; si falta un dato, se pregunta.

## Activarlo en otra maquina

1. **Skill:** copia `SKILL.md` a `.claude/skills/lecciones-aprendidas/SKILL.md`
   del proyecto (o al directorio de skills que uses).
2. **Hook:** agrega esta entrada a `.claude/settings.json` (sin borrar hooks
   existentes):
   ```json
   {
     "hooks": {
       "Stop": [
         { "matcher": "", "hooks": [
           { "type": "command",
             "command": "node \"$CLAUDE_PROJECT_DIR/harnesses/lecciones-aprendidas/detect-archive.js\"" }
         ] }
       ]
     }
   }
   ```
   Si `$CLAUDE_PROJECT_DIR` no expande en tu shell, reemplazalo por la ruta
   absoluta al repo.
3. **Marcador:** en la primera corrida el hook crea
   `.claude/.lecciones-aprendidas-seen` solo. No lo commitees si no querés
   compartir el estado (agregalo a `.gitignore`).
4. **Vault:** ajusta la ruta destino en `SKILL.md` (`D:\Notas\Kerberos\...`) a tu
   propio vault.
5. Requiere `node` en el PATH (viene con Claude Code).

## Correrlo a mano

`/lecciones-aprendidas` en cualquier momento. Toma la carpeta archivada mas
reciente y te la confirma.
