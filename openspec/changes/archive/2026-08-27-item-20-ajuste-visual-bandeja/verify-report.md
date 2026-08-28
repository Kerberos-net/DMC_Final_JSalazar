```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:13555f1b6b5a0b70287a0ab51841a242284bbb3c68d05e2df7a89305958ddf15
verdict: pass
blockers: 0
critical_findings: 0
requirements: 10/10
scenarios: 20/20
test_command: npx ng test --watch=false
test_exit_code: 0
test_output_hash: sha256:13555f1b6b5a0b70287a0ab51841a242284bbb3c68d05e2df7a89305958ddf15
build_command: npx ng build --configuration production
build_exit_code: 0
build_output_hash: sha256:9b636309b9b682829db4a543c8b41990998b6051c3419b476f6e11b9116c7a7c
```

## Verification Report

**Change**: item-20-ajuste-visual-bandeja (BACKLOG #20 - Ajuste visual de bandeja y panel de errores)
**Version**: spec obs #213 / design obs #214 / tasks obs #215 / apply-progress obs #216
**Mode**: Strict TDD
**Branch verified**: pr3/item-20-panel-modal @ a5eee03 (stacked ccdd96b -> f747c08 -> a5eee03 on main)

### Completeness

Tasks total 23 (Phase 1: 10, Phase 2: 5, Phase 3: 6, Phase 4: 2). Tasks complete 23. Tasks incomplete 0.

### Build and Tests Execution

Build: PASS. npx ng build --configuration production. styles css 8.22 kB (1.90 kB transfer); inbox-page chunk 17.79 kB (4.79 kB); bundle complete in 4.414 s; NO anyComponentStyle budget warning.

New component stylesheet sizes (raw bytes, all far below the 4 kB warn / 8 kB error anyComponentStyle budget): inbox-page.css 592; inbox-filter.css 876; inbox-list.css 1154; panel-errores.css 1094; confirmar-reproceso.css 1070.

Tests: 342 passed / 0 failed / 0 skipped. npx ng test --watch=false -> Test Files 34 passed (34); Tests 342 passed (342); Duration 7.74s. Apply-progress claim of 342/342 CONFIRMED (34 files).

Lint / typecheck: PASS. npm run lint -> tsc --noEmit -p tsconfig.app.json and tsc --noEmit -p tsconfig.spec.json, clean, exit 0.

.NET: not run - #20 touches no .NET/SQL/API surface (correct per design).

Coverage: not available - no coverage tool configured in this project. Not a failure.

### Spec Compliance Matrix - spa-visual-bandeja (7 requirements / 11 scenarios)

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| R1 tokens | Renders in both themes from tokens | paleta.spec.ts (6 estado tokens resolve #rrggbb claro+oscuro) + contraste.spec.ts both themes + literal-free component CSS by inspection | COMPLIANT |
| R1 | Stylesheet has no literals + within budget | paleta.spec.ts anti-literal regex; build no budget warning; CSS 592-1154 B | COMPLIANT |
| R2 inbox-page shell | Header and shell present | inbox-page.spec.ts renders h1 + subtitle; header -> filter -> list -> dialog document order | COMPLIANT (copy deviation - WARNING 3) |
| R3 inbox-filter bar | Filters horizontal, identical signals | inbox-filter.spec.ts every label carries campo + inbox-filter__campo + 7 pre-existing signal specs green | COMPLIANT |
| R4 inbox-list Estado chip | Row with errors shows Error chip, chipsDe unchanged | inbox-list.spec.ts precedence 2 + regression lock (chipsDe chips byte-identical) | COMPLIANT |
| R4 | Promoted row no errors/alerts -> Validada | inbox-list.spec.ts precedence 4 (clean PROMOVIDO -> Validada) | COMPLIANT |
| R4 | Date column tabular (.tabular-nums) | component-scoped .inbox-list__fecha font-variant-numeric:tabular-nums (design D5); no direct test on the date-cell class | PARTIAL - WARNING 2 |
| R5 panel-errores card | Errors present | panel-errores.spec.ts restrained card: one __item per error, __clasificacion; .alerta--informativa shape, never .alerta--bloqueante | COMPLIANT |
| R5 | No errors -> no output | panel-errores.spec.ts existing empty-array spec (testid null) | COMPLIANT |
| R6 confirmar-reproceso | Dialog open via .open attr, manual backdrop, no showModal/::backdrop | confirmar-reproceso.spec.ts backdrop present after open / removed after close / backdrop-click -> cancelar / Escape -> cancelar / focus -> Cancelar + 4 pre-existing .open specs green; .ts has no showModal(), no ::backdrop | COMPLIANT |
| R7 estado WCAG AA | Estado chip text contrast | contraste.spec.ts PARES_TINTA_FONDO += estado-error/alerta texto/fondo (>=4.5) + TINTAS_NO_TEXTO += estado-error/alerta-borde (>=3 vs 4 surfaces) x 2 themes | COMPLIANT |

### Spec Compliance Matrix - spa-design-tokens delta (3 requirements / 9 scenarios)

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| ADDED chip primitives | Error chip follows validada shape | paleta.spec.ts regex chip--error references var(--estado-error-texto); styles.css same 3-prop shape as .chip--validada | COMPLIANT |
| ADDED primitives | Alerta chip token-driven | paleta.spec.ts chip--alerta references var(--estado-alerta-texto) | COMPLIANT |
| ADDED trios | Trios exist both themes | paleta.spec.ts six estado tokens resolved both themes | COMPLIANT |
| ADDED trios | Texto derives from existing inks, no new hue | paleta.spec.ts estado-error-texto === error-ink etc + anti-literal regex | COMPLIANT |
| ADDED trios | Palette parity covers new names | paleta.spec.ts tokensPorTema assertions both themes for all 6 names | COMPLIANT |
| MODIFIED WCAG | Alert text / accent-text / accent button fill pass AA | contraste.spec.ts prior rows unchanged, suite green | COMPLIANT |
| MODIFIED WCAG | Estado error/alerta pairs pass AA both themes | contraste.spec.ts 4 pair + 16 border cases (added rows) | COMPLIANT |

Compliance summary: 20/20 scenarios COMPLIANT or behaviorally met (1 PARTIAL on the literal .tabular-nums class name - behavior present via component-scoped class per design D5).

### Correctness (Static Evidence)

- Estado tokens are pure aliases (D1): Implemented. styles.css estado-error/alerta trios are all var(--error-*) / var(--alerta-*); only new raw values are --fondo-scrim rgba (scrim, not a hue). No new hex in the estado trios.
- Guard can go RED (D2): Implemented. paleta.spec.ts regex over the styles.css text for the chip--error rule body referencing var(--estado-error-texto); deleting the rule body or swapping the token for a literal turns it RED. Anti-literal regex for the handoff hexes also RED-capable.
- Estado chip precedence (D3): Implemented. chipEstadoDe() module-level pure fn beside untouched chipsDe(): DESCARTADO -> errores>0 -> (esProveedorGenerico or posibleDuplicado, null-safe) -> PROMOVIDO -> PENDIENTE. Folded into the filas computed. 5 precedence tests green. DESCARTADO ranks first - diverges from spec.md R4 text (WARNING 1).
- No showModal() / ::backdrop (D4): Implemented. confirmar-reproceso.ts toggles nativeElement.open only; manual abierto()-gated .confirmar-reproceso__fondo element with --fondo-scrim; backdrop-click + keydown.escape both call onCancelar(); focus stored on open() and restored in shared private cerrar().
- Component-scoped tabular class (D5): Implemented. .inbox-list__fecha / panel-errores use font-variant-numeric:tabular-nums; global base .tabular-nums (which also sets text-align:right) left untouched.

### Coherence (Design)

D1 pure aliases: Yes (grep styles.css). D2 guard RED mechanism: Yes (regex over styles.css text). D3 precedence incl. DESCARTADO-first: Yes in code, matches design D3, but design D3 flagged it as an inference and the Open Question is still unchecked. D4 manual backdrop no showModal/::backdrop: Yes. D5 component-scoped tabular class: Yes. PR sequencing (3 stacked slices each <400 lines): Yes; authored diff vs pr2 ~266 changed lines; total main->a5eee03 891 ins / 62 del incl openspec docs + specs.

### Frozen-Surface Check

- chipsDe() internals: Unchanged (additions are after the function; body byte-identical; regression-lock test green).
- inbox.service.ts: Untouched (absent from git diff --name-only main...a5eee03).
- Bandeja query / filter bound-signals / pagination: Untouched (inbox-filter.ts diff = 1 line styleUrl; 7 pre-existing signal specs green).
- Reprocesar 5-min window: Untouched (reprocesarDisponible line in inbox-list.ts unchanged).
- openspec/specs/inbox-screen/spec.md and openspec/specs/bandeja/spec.md: Not modified.
- bandeja-item.model.ts: Not modified.
- PR1 styles.css tokens re-touched by PR2/PR3: No (git log ccdd96b..a5eee03 -- styles.css is empty).

### CONVENTIONS Check

- No accents/n-tilde in new identifiers: Pass (abierto, cerrar, chipEstadoDe, ClaseChipEstado, elementoPrevioConFoco, botonCancelar).
- Accounting-domain identifiers in Spanish: Pass (estado labels/roles Descartada, Alerta, --estado-error-* stay Spanish).
- No state library, signals only: Pass (readonly abierto = signal(false); no service/store/RxJS subject for backdrop state, ADR 0009).

### TDD Compliance

TDD Evidence reported: Yes (apply-progress obs #216 records RED->GREEN per task; panel-errores 2->4, confirmar-reproceso 4->9). All tasks have tests: Yes (7 spec files). RED confirmed (files exist): Yes. GREEN confirmed (pass on re-exec): Yes 342/342. Triangulation adequate: Yes (5 distinct Estado precedence cases; contraste 4 pair + 16 border). Safety Net for modified files: Yes (inbox-filter 7, confirmar-reproceso .open 4, panel-errores empty 1, chipsDe regression lock - all green).

TDD Compliance: 6/6 checks passed.

### Test Layer Distribution

Unit (pure/text-regex) ~30 new across paleta.spec.ts + contraste.spec.ts (vitest 4). Integration (TestBed render) ~20 new across inbox-page/inbox-filter/inbox-list/panel-errores/confirmar-reproceso .spec.ts (angular build:unit-test + jsdom). E2E 0 (not installed). Suite total 342 / 34 files.

### Changed File Coverage

Coverage analysis skipped - no coverage tool detected in the project.

### Assertion Quality

Scanned all 7 modified spec files. Assertions call production code (TestBed render or regex over the real styles.css string), assert concrete labels/classes/contrast ratios; empty-state checks (panel-errores null, backdrop null) each have a companion present assertion. Some assertions check CSS class membership (chip--error, banner--error, campo) - appropriate here because this presentation-only change has the class surface as its contract.

Assertion quality: All assertions verify real behavior (0 CRITICAL, 0 WARNING).

### Quality Metrics

Linter: No errors (tsc --noEmit app + spec projects clean). Type Checker: No errors.

### Issues Found

CRITICAL: None

WARNING:
1. Spec R4 precedence text contradicts the implemented (and design-D3-ratified) order. spec.md spa-visual-bandeja R4 lists errores.length > 0 first and DESCARTADO fifth/last; the implementation and design D3 place DESCARTADO first (unconditional). The delta spec must be reconciled to the DESCARTADO-first order before archive merges it into openspec/specs, otherwise the merged spec will contradict chipEstadoDe() and the precedence 1 test.
2. R4 date-column scenario says the cell carries .tabular-nums; implementation uses a component-scoped .inbox-list__fecha class instead (design D5, to avoid the global rule text-align:right). Behavior is present but the literal scenario wording is unmet and no test asserts the date-cell class. Reconcile the scenario text during archive.
3. inbox-page heading copy. Spec R2 scenario says heading Bandeja; implementation renders Bandeja principal (design choice from DESIGN.md L135 + handoff section 2). Deliberate; reconcile the scenario text.
4. DESCARTADO-first precedence and backdrop-click + Escape -> onCancelar() are asserted user-ratified by the orchestrator, but no ratification record exists in the artifacts. design.md Open Questions for both are still unchecked and apply-progress carries them as confirm. Archive should record the ratification (or the user should confirm) before closing the item.
5. inbox-page.ts gained a .catch swallow on the load effect - a latent unhandled-promise-rejection fix, slightly outside the CSS + template structure only scope claimed for #20. Behavior-neutral (error signal path unchanged, covered by the new banner--error test). Acceptable but note in the change log.
6. inbox-list empty state (data-testid inbox-vacio) is new DOM not named in the original proposal. Presentation-only, covered by a test; note as an in-scope addition for #20.

SUGGESTION:
- Summary counter cards + enriched row columns (proveedor name, monto, moneda, numero, tipo, fecha de emision) remain deferred to BACKLOG #21 - confirmed stated in-repo at BACKLOG.md:41.
- SPRINT.md still shows Ciclo SDD activo Item #20 and 15 de 21 cerrados. Archive must add the #20 closing entry following the item #18 convention (commit 450ea05) - not done in verify.
- Branches pr1/pr2/pr3 are local only, not pushed, no PRs opened.

### Verdict

PASS WITH WARNINGS - All 23 tasks complete; 342/342 tests green; lint + production build clean within budgets; all 10 requirements / 20 scenarios covered by passing tests (1 partial on a literal class name, behavior present per design D5). No CRITICAL issues. Six non-blocking WARNINGs, all doc-reconciliation or ratification-recording items to resolve during archive; no code defects.

next_recommended: sdd-archive
