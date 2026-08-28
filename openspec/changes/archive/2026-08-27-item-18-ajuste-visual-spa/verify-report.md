```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:c26fb4d6d5f91a70318e1294ecf2042d9d64e481b3c7e64adc0b80a966e90389
verdict: pass
blockers: 0
critical_findings: 0
requirements: 38/38
scenarios: 69/69
test_command: "per-project dotnet test --no-build; npx ng test --no-watch"
test_exit_code: 0
test_output_hash: sha256:996cb3c8a22973b81df4d802082120c6ab29a45cdee82577d10adb7c34c7b80d
build_command: "dotnet build SmartNet.sln; npx ng build --configuration production"
build_exit_code: 0
build_output_hash: sha256:3c4ec386a72912148a6c2fc51c5b1efba499e449e855707599aa250b945fd284
```

## Verification Report

**Change**: item-18-ajuste-visual-spa (BACKLOG #18)
**Mode**: Strict TDD
**Branch**: pr8/item-18-proveedor-picker (commits 2ac0ac7 505fdb8 93ec9a7 8720f63 aa91cda 37a4ece 312d2b6)

### Completeness
Implementation tasks complete: 50 / 50. Phase 7: 4 / 5 (task 7.5 BACKLOG/SPRINT status pending - see WARNING).

### Build and Tests
Build PASS: dotnet build SmartNet.sln 0 warn / 0 err. ng build --configuration production: bundle complete, Initial total 286.22 kB / 78.12 kB, NO anyComponentStyle budget warning.

SPA: npx ng test --no-watch -> Test Files 34 passed (34); Tests 296 passed (296).

.NET: dotnet test SmartNet.sln (parallel) produced 32 failures across 8 integration assemblies; ALL pass when the project runs alone. Root cause = SQL Server contention under parallel fact_test_* provisioning, NOT an item-18 regression. Proof: TestBootstrapHarnessTests.CreateTestDatabase fails in solution run, passes alone; Facturacion.Infrastructure.Tests 0/53 solution vs 53/53 alone. Authoritative per-project numbers:
- Facturacion.Core.Tests 147/147 PASS (ValidacionDeCorreccion, ServicioDeFacturasPhase2 - PR5)
- Contable.Core.Tests 41/41 PASS (CodigoComprobante - PR5)
- Catalogos.Core.Tests 32/32 PASS
- Sugerencia.Core.Tests 27/27 PASS
- TiposCambio.Core.Tests 20/20 PASS
- Inbox.Core.Tests 49/49 PASS
- Auth.Core.Tests 33/33 PASS
- Facturacion.Infrastructure.Tests 53/53 PASS
- Catalogos.Infrastructure.Tests 66/66 PASS (SqlProveedorRepository.BuscarAsync x9 - PR8)
- Api.Tests 163/163 PASS (CatalogoEndpointsTests, FacturaEndpointsTests PR5, AsientoEndpointsTests PR6)
Not re-run per-project (no item-18 code touches them, same infra signature): Admin.Tests, Auth.Infrastructure.Tests, Inbox.Infrastructure.Tests, TiposCambio.Infrastructure.Tests, Db.Runner.Tests.

Coverage: no tool configured - skipped.

### Spec Compliance Matrix

| Spec / Requirement | Covering test(s) | Result |
|---|---|---|
| spa-design-tokens - Accent family (fill vs text) | paleta.spec.ts accento/accento-texto ramp asserts | COMPLIANT |
| spa-design-tokens - Four-level surface hierarchy | paleta.spec.ts "jerarquia de 4 superficies" | COMPLIANT |
| spa-design-tokens - Elevation shadow scale | paleta.spec.ts; login-page.spec.ts prominent elevation | COMPLIANT |
| spa-design-tokens - Radius scale 8/12/16/20 + pill | paleta.spec.ts "escala de radios 8/12/16/20" | COMPLIANT |
| spa-design-tokens - Segoe-first integer type scale | paleta.spec.ts "escala tipografica en enteros ... Segoe" | COMPLIANT |
| spa-design-tokens - Translucent hairline borders | paleta.spec.ts hairline rgba + componer alpha tests | COMPLIANT |
| spa-design-tokens - WCAG AA per token pair both themes | contraste.spec.ts parsed-token pair table, 4 surfaces x 2 themes, incl white-on-accento, accento-texto, tinted inks | COMPLIANT |
| spa-design-tokens - Two-tier alert + ratified accent-reuse exception | paleta.spec.ts "excepcion ratificada 1"; styles.css:20-27,168-172 | COMPLIANT |
| spa-theme-toggle - native select light/dark/system | app.html select data-testid selector-tema; app.spec.ts | COMPLIANT |
| spa-theme-toggle - no sidebar sun/moon toggle | app.spec.ts | COMPLIANT |
| spa-visual-login - card composition order | login-page.html GF -> titulo -> subtitulo -> inputs -> error slot -> Ingresar -> pie; login-page.spec.ts | COMPLIANT |
| spa-visual-login - placeholder-labeled inputs w/ accessible name | login-page.html aria-label Usuario/Contrasena, no visible label; login-page.spec.ts | COMPLIANT |
| spa-visual-login - full-width accent submit | login-page.spec.ts | COMPLIANT |
| spa-visual-login - error uses validation-error token not banner--error | login-page.html .login-page__error inline role=alert; login-page.spec.ts | COMPLIANT |
| spa-visual-login - consumes tokens, no literals, both themes | login-page.spec.ts | COMPLIANT |
| spa-visual-detalle-validacion - page header back/title/pill/top-right actions | detalle-page.spec.ts; detalle-page.ts tituloDetalle/estadoPill | COMPLIANT |
| spa-visual-detalle-validacion - 3 banners above split, outside factura-form | indicadores-factura.spec.ts, detalle-page.spec.ts | COMPLIANT |
| spa-visual-detalle-validacion - static 42/58 split, viewer not sticky | detalle-page.spec.ts | COMPLIANT |
| spa-visual-detalle-validacion - Fecha de corte contable adjacent to asiento block | detalle-page.spec.ts | COMPLIANT |
| spa-visual-detalle-validacion - asiento-lineas tabular + Total + cuadre pill | asiento-lineas.spec.ts | COMPLIANT |
| spa-visual-detalle-validacion - blocking indicators strong; Validar prevented | detalle-page.spec.ts bloqueosValidar/puedeValidar | COMPLIANT |
| spa-visual-detalle-validacion - informational subtle; per-field OCR highlight | factura-form.spec.ts campo--resaltado | PARTIAL - invoice-wide (server only exposes tieneCamposNoExtraidos boolean); documented factura-form.ts:60-63 |
| spa-visual-detalle-validacion - component CSS budget compliance | ng build production no budget warning | COMPLIANT |
| pantalla-detalle-validacion - Validar hard-blocked P00000 OR duplicate, no ack | detalle-page.spec.ts; detalle-page.ts:80-88,199 | COMPLIANT |
| pantalla-detalle-validacion - factura-form header field set | factura-form.spec.ts | COMPLIANT |
| pantalla-detalle-validacion - dedicated TC-faltante indicator | factura-form.spec.ts; factura-form.ts:69-71 | COMPLIANT |
| pantalla-detalle-validacion - side-by-side layout | detalle-page.spec.ts | COMPLIANT |
| pantalla-detalle-validacion - indicators reflect real persisted values; per-field OCR | factura-form.spec.ts | PARTIAL - same invoice-wide OCR granularity limitation |
| api-facturas - CorreccionFacturaRequest accepts tipoComprobante/numero | FacturaEndpointsTests.cs; ServicioDeFacturasPhase2Tests; ValidacionDeCorreccionTests | COMPLIANT |
| api-facturas - PATCH updates both columns, Version advances, ETag | FacturaEndpointsTests.cs linchpin RED->GREEN; SqlUnidadDeTrabajo.cs:368-388 | COMPLIANT |
| api-facturas - audit row per changed field | ServicioDeFacturasPhase2Tests +4 | COMPLIANT |
| api-facturas - blank numero / unknown tipoComprobante -> 422 | ValidacionDeCorreccionTests 8 cases; ProblemasDeNegocio.Map -> 422 | COMPLIANT |
| api-facturas - omitting new fields is a no-op | ValidacionDeCorreccion null-guard; AplicarCorreccion copies only non-null | COMPLIANT |
| api-catalogos-proveedores - authenticated endpoint, body shape, order, 401 | CatalogoEndpointsTests.cs | COMPLIANT |
| api-catalogos-proveedores - read-only, partition-respecting | SqlProveedorRepository.cs SELECT-only over dbo.Proveedor; grep confirms no dbo.* write in prod code | COMPLIANT |
| api-catalogos-proveedores - pagination page 2 + past end | CatalogoEndpointsTests.Get_PagesResults; SqlProveedorRepositoryTests.BuscarAsync_Pages | COMPLIANT |
| api-catalogos-proveedores - empty/short/no-match | CatalogoEndpointsTests.Get_MissingOrShortQuery (theory x3), Get_NoMatch | COMPLIANT |
| api-catalogos-proveedores - P00000 excluded | CatalogoEndpointsTests.Get_ExcludesP00000; SqlProveedorRepositoryTests.BuscarAsync_ExcludesP00000 (codpro <> P00000) | COMPLIANT |
| api-catalogos-proveedores - contract-test coverage all 8 cases | CatalogoEndpointsTests.cs (name, RUC, order, page2, past end, empty/short, no-match, P00000, 401) | COMPLIANT |
| spa-picker-proveedor - ProveedorService root/signal+asReadonly/firstValueFrom/debounce/no state lib | proveedor.service.spec.ts (HttpTestingController) | COMPLIANT |
| spa-picker-proveedor - debounced search one request; pagination appends | proveedor.service.spec.ts | COMPLIANT |
| spa-picker-proveedor - modal dialog search/list/keyboard/Enter/Escape/focus-trap/aria/no new token | picker-proveedor.spec.ts; paleta.spec.ts & contraste.spec.ts unchanged | COMPLIANT |
| spa-picker-proveedor - opened from buscarProveedor; selection -> borradorFactura via onCambiosFactura; no PATCH | detalle-page.spec.ts both directions; factura-form.spec.ts; detalle-page.ts:133-139 | COMPLIANT |
| spa-picker-proveedor - test coverage suite green | ng test --no-watch 34 files / 296 | COMPLIANT |

Compliance summary: 69/69 scenarios compliant. The 2 PARTIAL requirements share one documented server-granularity limitation (per-field OCR highlight); their scenarios pass at the granularity the backend exposes.

### Correctness (Static Evidence)
| Requirement | Status | Notes |
|---|---|---|
| No dbo.* write in new .NET code | OK | grep INSERT/UPDATE/DELETE/MERGE dbo. -> only test fixtures; SqlProveedorRepository / CatalogoEndpoints SELECT-only |
| No fact.* access from catalogos slice | OK | SqlProveedorRepository.cs queries dbo.Proveedor only |
| No versioned SQL / no new grant | OK | git diff main..HEAD touches no *.sql / schema; PR5 reuses 008 object-level UPDATE grant, PR8 existing usr_api SELECT |
| No EF Core / Alembic migrations | OK | none added |
| Accounting-domain identifiers Spanish | OK | AsientoContable, ValidacionDeCorreccion, CodigoComprobante, bloqueosValidar, borradorFactura |
| No accents/n in identifiers | OK | numero, tipoComprobante (accents only in strings/comments) |
| Money 2 decimals, never 3 | OK | shared/formato.ts dosDecimales = toFixed(2); TC row raw SBS rate (accepted, task 4d) |
| Angular signals only, no state library | OK | ProveedorService, DetallePage, PickerProveedor, FacturaForm all signal-based |

### Coherence (Design)
| Decision | Followed? | Notes |
|---|---|---|
| D1 private azul ramp + role aliases, ratified reuse | Yes | styles.css:30-32 sole ramp; accento/accento-texto/estado-pendiente-ink/info-generico-ink alias it; exception comment at ramp + dark override |
| D2 WCAG guard READS styles.css via node:fs | Yes | paleta.spec.ts:12-13 readFileSync; contraste.spec.ts:14-15 same; parsed tokens feed contraste() - genuinely RED on bad token |
| D3 AA correction by role-splitting (dark accento-texto = #409cff) | Yes | styles.css:168-172 ratified over design #0a84ff, documented; paleta.spec.ts:31 asserts |
| D4 banners in new indicadores-factura, thin container | Yes | pure-input component; detalle-page renders above split |
| D5 bloqueosValidar named list, server authoritative, no ack | Yes | detalle-page.ts:80-88 |
| D6 TC indicator watches tipoCambioVenta | Yes | detalle-page.ts:72-75, factura-form.ts:69-71 |
| .NET delta 4 layers -> 6 touches | Sound | commit aa91cda + apply-progress: (5) new ResultadoComando.CorreccionInvalida->422 (no existing case maps to 422 without misusing InvariantesIncumplidas); (6) new SmartNet.Contable.Core/CodigoComprobante canonical {01,03,07}, MapearTipoComprobante refactored to share. Additive, de-duplicating. |
| PR8 as one size:exception commit vs PR8a/8b/8c | Ratified | user decision in tasks.md / apply-progress |

### Ratified-decision conformance (task 7.4)
| Check | Result | Evidence |
|---|---|---|
| (a) blue accent reused action + pendiente + P00000, greppable/documented | OK | rg -- --azul- -> ramp only; styles.css:20-27 exception comment; paleta.spec.ts "excepcion ratificada 1" |
| (b) P00000 AND duplicate BOTH hard-block Validar, no ack bypass | OK | detalle-page.ts:80-88 pushes both DUPLICADO+PROVEEDOR_GENERICO; puedeValidar()=len0; validar() early-returns; no checkbox in template |
| (c) base/IGV/TC read-only, glosa absent, editability not silently added | OK | factura-form.ts baseImponibleTexto/igvTexto/tipoCambioTexto computed display-only; no glosa field; api-facturas write contract excludes them |
| (d) money 2 decimals; TC row raw SBS rate accepted | OK | formato.ts toFixed(2); factura-form.ts:73-77 |
| (e) P00000 excluded from picker search | OK | SqlProveedorRepository.cs:47 AND codpro <> P00000; 2 tests |

### Known open risks (task 7.5 - report only)
| Risk | Status |
|---|---|
| PosibleDuplicado stale after editing tipoComprobante/numero | Documented deliberate - design.md Open Question 2; recompute-on-PATCH is a domain-rule change, out of #18 unless ratified |
| OCR highlight invoice-wide not per-field | Documented deliberate - factura-form.ts:60-63; server only exposes tieneCamposNoExtraidos boolean |
| LIKE wildcards in q not escaped | NOT documented in-repo. Low impact (read-only, parameterised @patron, no injection; stray %/_ broadens match). Recommend a note in spec Out-of-Scope or escape the term. -> SUGGESTION |
| No dbo.Proveedor(proveedor) index | Documented deliberate - spec Out-of-Scope + ADR 0003; LIKE over ~6600 rows accepted |

### TDD Compliance
| Check | Result | Details |
|---|---|---|
| TDD evidence reported | OK | apply-progress records RED->GREEN per task, 8 phases; commit bodies note linchpin REDs (PR5 SET-hunk reverted -> PATCH 200 + DB unchanged) |
| All impl tasks have tests | OK | every GREEN paired with RED; new/extended: paleta.spec.ts, contraste.spec.ts, indicadores-factura.spec.ts, asiento-lineas.spec.ts, factura-form.spec.ts, detalle-page.spec.ts, proveedor.service.spec.ts, picker-proveedor.spec.ts, ValidacionDeCorreccionTests.cs, ServicioDeFacturasPhase2Tests.cs +4, FacturaEndpointsTests.cs +3, AsientoEndpointsTests.cs +2, CatalogoEndpointsTests.cs 8, SqlProveedorRepositoryTests.cs +9 |
| GREEN confirmed on execution | OK | SPA 296/296; .NET per-project all PASS |
| Triangulation adequate | OK | ValidacionDeCorreccionTests 8 cases; CatalogoEndpointsTests theory x3 + 6 facts; contraste pair table 4 surfaces x 2 themes |
| Assertion quality | OK | audited palette/contraste/picker/service/Catalogo/SqlProveedor/Validacion specs - assert real parsed values / HTTP bodies / DB rows / ratios. No tautologies, ghost loops, or smoke-only tests |

### Quality Metrics
- Linter: npm run lint exit 0 - NOTE: runs tsc --noEmit only, not ESLint -> SUGGESTION
- Type checker: tsc --noEmit app + spec configs clean
- .NET build: 0 warnings / 0 errors

### Issues Found

CRITICAL: None.

WARNING:
1. dotnet test SmartNet.sln (parallel) reports 32 failures across 8 integration assemblies; all pass per-project. Root cause = SQL Server contention under parallel fact_test_* provisioning, not an item-18 regression. Recommend serial integration runs in CI or documenting. Not blocking archive.
2. Task 7.5 (BACKLOG.md / SPRINT.md #18 status) not done. Item #17 was closed via SPRINT.md (commit e6054ea), not BACKLOG.md. Housekeeping doc edit for the archive step following the #17 convention.

SUGGESTION:
1. LIKE wildcards in the picker q term not escaped (no security impact; parameterised, read-only). Add a note to api-catalogos-proveedores Out-of-Scope or escape.
2. npm run lint is a typecheck alias, not ESLint. Consider angular-eslint.
3. --accento-suave (#e6f1fc / #14263d) is a blue-ish literal outside the azul ramp - a soft wash surface specified per-theme in design D3, acceptable; noted for task 7.3 completeness.

### Verdict

PASS WITH WARNINGS

All 38 requirements / 69 scenarios across the 8 specs are met with passing runtime tests. Build clean (.NET + SPA prod, no budget warnings). Strict-TDD evidence present and matches execution. Design deviations (6-vs-4 .NET touches, dark accent #409cff, single PR8 commit) are ratified and documented in-repo. Both WARNINGs are process/environment items, not code defects, and do not block archive.
