# Proposal: Núcleo contable

BACKLOG.md ítem **#8**. Depende de: **#3** (catálogos y satélites, archivado). Contexto extra
requerido: ⚠ **`REGLAS.md` §5–§10** (normativo; los 7 ejemplos de §10 son casos de prueba
obligatorios). Ver también `openspec/changes/nucleo-contable/Backlog de mejoras.md` para los
pendientes, preguntas de diseño abiertas y advertencias que este documento cita sin duplicar.

## Intent

Los ítems #3 (catálogos) y #4 (tipos de cambio) ya existen y están archivados, pero nada convierte
una factura promovida y validada en un `AsientoContable` real. Los ítems #9 (sugerencia de cuenta),
#10 (notas de crédito) y #11 (API de facturas y asientos) están bloqueados por esta ausencia. #8
entrega ese motor: puro, sin base de datos, sin HTTP, sin reloj (ADR 0019), probado contra los 7
ejemplos numéricos de `REGLAS.md` §10 y las invariantes de §7.

## Scope

### In Scope
- `SmartNet/contable/SmartNet.Contable.Core` (+ `.Tests`): tipos de dominio (`AsientoContable`,
  `LineaAsiento`), generación de los bloques PRINCIPAL/DESTINO para los 4 casos de comprobante
  (§5), conversión de moneda anclada/derivada (§6, ADR 0018), y evaluación de las 7 invariantes de
  confirmación (§7).
- Consumo, sin modificar, de `SmartNet.Catalogos.Core` (plan de cuentas resuelto por prefijos,
  ítem #3) y `SmartNet.TiposCambio.Core` (TC ya seleccionado, ítem #4) como entradas ya resueltas.
- `PurityScanTests` copiado del patrón `catalogos`/`tipos-de-cambio` (zero PackageReference).
- Golden tests: los 7 ejemplos de `REGLAS.md` §10 y ambos caminos (aceptar/rechazar) de las 7
  invariantes de §7.
- Salida moldeada para minimizar traducción futura hacia `fact.AsientoContable` /
  `AsientoContableDetalle` (ítem #11), sin persistirla.

### Out of Scope
- Persistencia y HTTP del asiento — ítem #11.
- Sugerencia de cuenta por frecuencia — ítem #9.
- Notas de crédito completas (referencia interna/externa, herencia, reparto proporcional, tope
  acumulado) — ítem #10.
- **Precondición de NC (§8, §12 punto 4)**: el dueño del proyecto ratificó relajarla — se debe
  admitir NC que hoy `REGLAS.md` rechaza con `409` por "factura original sin validar". Esa
  implementación es de #10, no de #8. **#8 no debe codificar la precondición vieja en ningún
  lugar** (ni siquiera como placeholder de rechazo), para no contradecir en silencio esta decisión
  ya tomada. Ver `Backlog de mejoras.md` §1.
- Golden cases específicos de NC (100% USD, referencia externa, reparto proporcional) — quedan
  para la suite de #10, no son obligatorios en #8.
- Catálogo completo de rechazo §8 (`409`/`412`/`422`) — ítem #11, salvo lo que sea estrictamente
  invariante contable de §7.

## Capabilities

### New Capabilities
- `nucleo-contable`: generación pura del asiento contable (bloques PRINCIPAL/DESTINO, conversión
  de moneda, invariantes de confirmación §7) a partir de entradas ya resueltas por catálogos y
  tipos de cambio.

### Modified Capabilities
- None.

## Approach

Pipeline en dos etapas, alineado con el split BORRADOR/CONFIRMADO de ADR 0006: **Componer**
(resolución en vivo, PRINCIPAL + DESTINO) y **Validar/Confirmar** (evalúa las invariantes de §7
sobre datos ya congelados). Forma exacta del DTO de entrada, representación del rechazo y modelado
de reparto se resuelven en `sdd-design` (5 preguntas abiertas ya listadas en `Backlog de
mejoras.md` §3).

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/contable/SmartNet.Contable.Core` | New | Dominio puro: asiento, líneas, invariantes |
| `SmartNet/contable/SmartNet.Contable.Core.Tests` | New | `PurityScanTests` + goldens §10/§7 |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| El motor implementa 6 reglas contables de `REGLAS.md` §12 **no ratificadas por un contador**; completar #8 no implica que el sistema esté listo para contabilidad real | High (conocido) | Documentado aquí y en `Backlog de mejoras.md`; no operar con datos reales sin revisión contable formal |
| Confundir el límite §7 (invariantes, #8) con §8 (catálogo de rechazo, #11) | Med | Backlog de mejoras §3 pregunta 4, a resolver en `sdd-design` |
| Codificar por accidente la precondición de NC vieja como placeholder | Med | Explícito en Non-Goals; revisar en `sdd-spec`/`sdd-design` |

## Rollback Plan

Cambio aditivo puro: nuevo proyecto `SmartNet.Contable.Core` sin tocar código existente ni schema.
Revertir = eliminar la carpeta `SmartNet/contable/` y el proyecto de la solución; ningún otro ítem
depende de #8 en tiempo de compilación hasta que #9/#10/#11 lo consuman explícitamente.

## Dependencies

- #3 Catálogos y satélites (archivado) — plan de cuentas resuelto por prefijos.
- #4 Tipos de cambio (archivado) — `SeleccionDeTipoCambio` ya resuelto.

## Success Criteria

- [ ] Los 7 ejemplos numéricos de `REGLAS.md` §10 pasan como golden tests.
- [ ] Las 7 invariantes de §7 están probadas en ambos caminos (aceptar/rechazar).
- [ ] `PurityScanTests` confirma cero dependencias de infraestructura (ADR 0019).
- [ ] Ningún artefacto de #8 codifica la precondición de NC no ratificada (§8/§12 punto 4).
