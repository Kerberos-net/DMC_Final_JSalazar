#!/usr/bin/env node
/**
 * Stop hook — "lecciones aprendidas" harness.
 *
 * Detects when an SDD cycle was just closed (a new folder appeared under
 * openspec/changes/archive/) and asks Claude to run the `lecciones-aprendidas`
 * skill for that change before ending the turn.
 *
 * Contract:
 *  - stdin: JSON from Claude Code (uses `stop_hook_active`, `cwd`).
 *  - stdout: {"decision":"block","reason":"..."} to continue the turn with
 *    instructions, or nothing (exit 0) when there is nothing to do.
 *
 * State: .claude/.lecciones-aprendidas-seen  (JSON array of archived folder names
 * already accounted for). On first run it is seeded with every existing folder,
 * so historical archives never trigger.
 */

const fs = require("fs");
const path = require("path");

function readStdin() {
  try {
    return fs.readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

let input = {};
try {
  input = JSON.parse(readStdin() || "{}");
} catch {
  input = {};
}

const projectDir = input.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();
const archiveDir = path.join(projectDir, "openspec", "changes", "archive");
const markerPath = path.join(projectDir, ".claude", ".lecciones-aprendidas-seen");

function currentArchives() {
  try {
    return fs
      .readdirSync(archiveDir, { withFileTypes: true })
      .filter((d) => d.isDirectory())
      .map((d) => d.name)
      .sort();
  } catch {
    return null; // archive dir does not exist yet
  }
}

function readMarker() {
  try {
    const raw = fs.readFileSync(markerPath, "utf8");
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function writeMarker(list) {
  try {
    fs.mkdirSync(path.dirname(markerPath), { recursive: true });
    fs.writeFileSync(markerPath, JSON.stringify(list.sort(), null, 2) + "\n");
  } catch {
    /* best effort — never break the turn over marker I/O */
  }
}

const archives = currentArchives();
if (archives === null) {
  process.exit(0); // no SDD archive dir — nothing to watch
}

const seen = readMarker();

// First run: seed silently, do not trigger on history.
if (seen === null) {
  writeMarker(archives);
  process.exit(0);
}

const fresh = archives.filter((name) => !seen.includes(name));

// Nothing new, or we are already inside a block-triggered continuation:
// reconcile the marker and stay quiet.
if (fresh.length === 0 || input.stop_hook_active) {
  writeMarker(archives);
  process.exit(0);
}

// Mark as accounted for BEFORE blocking so the follow-up Stop does not loop.
writeMarker(archives);

const list = fresh.map((n) => `  - ${n}`).join("\n");
const reason =
  `Un ciclo SDD se acaba de archivar. Carpeta(s) nueva(s) bajo openspec/changes/archive/:\n` +
  `${list}\n\n` +
  `Antes de terminar, ejecuta la skill \`lecciones-aprendidas\` para ese cambio: ` +
  `revisa la carpeta archivada, identifica los patrones tecnicos / convenciones y las reglas ` +
  `contables de REGLAS.md aplicadas por primera vez, y presentame la lista numerada para que ` +
  `yo elija que se documenta. No escribas nada en el vault todavia.\n\n` +
  `Si ahora no es el momento, dime "salto lecciones aprendidas" y lo dejamos para despues ` +
  `(se puede correr /lecciones-aprendidas manualmente).`;

process.stdout.write(JSON.stringify({ decision: "block", reason }));
process.exit(0);
