# Proposal: Sugerencia de cuenta (BACKLOG #9)

## Intent

Al validar una factura, el asistente contable debe elegir un motivo de compra y una cuenta de
cargo (ADR 0011). Elegir a ciegas entre hasta 34 cuentas candidatas por motivo es lento y
propenso a error. El ítem #9 entrega el motor que sugiere la cuenta (y el motivo) más probable
por frecuencia histórica de uso, con fundamento auditable, para que el ítem #11 (registro de
asientos) tenga un método listo para invocar y el ítem #12 (UI) tenga qué mostrar como
justificación (p. ej. "usado 14 de 15 veces con este proveedor").

## Scope

### In Scope
- Función pura de cascada de ranking: `(proveedor, motivo)` → más usada por `motivo` global →
  primera candidata por `CuentaCodigo` ASC. Desempate en escalones 1–2: `Veces` DESC, luego
  `UltimoUso` DESC, luego `CuentaCodigo` ASC (ADR 0011 rev. 4).
- Filtrado de las filas de `SugerenciaCuenta` contra la salida en vivo de `ResolverCandidatas`
  (invariante: "es candidata vigente de este motivo", no "existe en `dbo.CuentaContable`").
- Sugerencia de motivo por el mismo mecanismo, indexado solo por proveedor.
- Texto de fundamento (justificación) como dato expuesto, no solo el código sugerido.
- Servicio de aplicación/orquestación que invoca `ISugerenciaCuentaRepository` (ítem #3, ya
  existe) y `ResolverCandidatas` (ítem #3, ya existe), exponiendo un método listo para que el
  ítem #11 lo llame.

### Out of Scope
- Siembra/carga inicial desde histórico externo — eliminada por decisión del dueño del proyecto
  (la compañía no tiene sistema contable previo aprovechable; ADR 0011 revisión 4). No hay WU2.
- Incremento de `Veces`/`UltimoUso` al confirmar el asiento (`RegistrarUsoAsync`) — ya existe,
  es responsabilidad del ítem #11, no de #9.
- UI de selección/confirmación de cuenta y motivo — ítem #12.
- Cualquier tabla `dbo.*` o permiso nuevo — no aplica sin siembra histórica.

## Capabilities

### New Capabilities
- `sugerencia-cuenta`: cascada de ranking determinista de cuenta y motivo por frecuencia de uso,
  con filtrado contra candidatas vigentes y texto de fundamento, orquestada para invocación
  desde el flujo de registro de asientos.

### Modified Capabilities
None.

## Approach

Separar función pura de orquestación (CLAUDE.md, ADR 0019, aunque el alcance de #9 sí incluye
la capa de aplicación):
- Núcleo puro (testeable sin BD): dado un conjunto de filas `SugerenciaCuenta` ya obtenidas y el
  conjunto de candidatas vigentes de `ResolverCandidatas`, aplica la cascada y el desempate y
  devuelve la cuenta/motivo sugerido + fundamento.
- Capa de orquestación: llama a `ISugerenciaCuentaRepository` (`ListarPorProveedorYMotivoAsync`,
  `ListarPorMotivoAsync`, `ListarPorProveedorAsync`) y a `ResolverCandidatas`, arma la entrada
  del núcleo puro y expone el resultado.
- Ubicación estructural (proyecto core nuevo vs. extender `SmartNet.Catalogos.Core`) se decide
  en `sdd-design` — es técnica, no de producto.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/catalogos/SmartNet.Catalogos.Core/` o proyecto nuevo | New | Función pura de cascada + tipos de resultado/fundamento |
| Capa de aplicación (a definir en diseño) | New | Servicio que orquesta repositorio + `ResolverCandidatas` |
| `adrs/0011-...md` | Reference only | Ya corregido a revisión 4 (no se toca en esta propuesta) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Cascada no determinista si el desempate se implementa distinto al ADR | Low | Casos de prueba directos desde ADR 0011 rev. 4 (`Veces`, `UltimoUso`, `CuentaCodigo`) |
| Filtrado contra candidatas vigentes se olvida y se sugiere una cuenta obsoleta/fuera de prefijo | Med | Diseño obliga a intersectar siempre contra `ResolverCandidatas` antes de rankear |
| Ubicación estructural indecisa retrasa el diseño | Low | Delegada explícitamente a `sdd-design` |

## Rollback Plan

Cambio aditivo: nuevo código de sugerencia sin tocar esquema ni tablas existentes. Revertir el
commit/PR del módulo elimina la capacidad sin efectos colaterales; `SugerenciaCuenta` sigue
siendo alimentada exclusivamente por `RegistrarUsoAsync` (ítem #11), no depende de #9.

## Dependencies

- Ítem #8 (núcleo-contable) — cerrado.
- Ítem #3 (catálogos-y-satélites) — cerrado, entrega `ISugerenciaCuentaRepository` y
  `ResolverCandidatas`.
- Consumido por ítem #11 (registro de asientos), que debe invocar el método expuesto.

## Success Criteria

- [ ] La cascada de 3 escalones y su desempate reproducen exactamente ADR 0011 revisión 4 en
      casos de prueba (incluyendo empates).
- [ ] Ninguna sugerencia devuelve una cuenta que no esté en la salida vigente de
      `ResolverCandidatas` para ese motivo.
- [ ] El núcleo de ranking se prueba sin BD/HTTP/reloj (ADR 0019).
- [ ] El ítem #11 puede invocar un único método de aplicación para obtener cuenta + motivo +
      fundamento sugeridos.

## Proposal question round

No se abre una ronda de preguntas de producto: el alcance quedó fijado por la decisión ya
tomada del dueño del proyecto (eliminación de la siembra histórica, regla de desempate). El
único punto pendiente (ubicación estructural del código) es técnico y se delega a `sdd-design`,
no a producto.
