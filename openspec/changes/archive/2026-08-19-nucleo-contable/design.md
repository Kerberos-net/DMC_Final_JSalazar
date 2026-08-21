# Design: Núcleo contable (BACKLOG #8)

## Technical Approach

Un solo proyecto nuevo `SmartNet/contable/SmartNet.Contable.Core` (+ `.Tests`), replicando el
patrón `catalogos`/`tipos-de-cambio`: `net10.0`, **cero `PackageReference`**, `PurityScanTests`
copiado. **No hay `SmartNet.Contable.Infrastructure`** — sin DB, sin HTTP, sin reloj (ADR 0019).

El motor son **dos funciones puras**, alineadas con el split de ADR 0006:

| Fase | Función | Qué hace |
|---|---|---|
| BORRADOR | `ComposicionDeAsiento.Componer(EntradaAsiento) → AsientoContable` | Resuelve PRINCIPAL (§5, 4 casos) + DESTINO (§5) + conversión §6. **Total**: nunca lanza ni rechaza; un borrador puede quedar incompleto o descuadrado, que es exactamente lo que ADR 0006 permite. |
| CONFIRMADO | `InvariantesDeConfirmacion.Evaluar(AsientoContable, DateOnly fechaCorteContable) → ResultadoConfirmacion` | Puerta de §7 sobre el asiento **ya congelado**. Devuelve **todas** las invariantes incumplidas, no la primera. |

`Evaluar` no recibe catálogo: `AsientoContable` es autocontenido (ADR 0006 "qué se congela"), de
modo que la invariante de DESTINO se evalúa contra el dato congelado en la línea y no contra el
catálogo vivo — que es literalmente lo que exige §7.

## Architecture Decisions

### Decisión 1 — DTO de entrada propio, que **reusa** `CuentaContable` y envuelve el TC

| Opción | Tradeoff | Decisión |
|---|---|---|
| Duplicar `CuentaContable` dentro de Contable.Core | Desacopla, pero bifurca el contrato del plan de cuentas: `CtaReflejaCodigo`/`CtaPuenteCodigo` quedarían definidos en dos sitios | Rechazada |
| Recibir `ResultadoTipoCambio` | El motor tendría que decidir qué hacer con `SinTipoCambio` — eso es rechazo §8, terreno de #11 | Rechazada |
| `EntradaAsiento` propio + `ProjectReference` a `SmartNet.Catalogos.Core` + `SmartNet.TiposCambio.Core` | Dos Core puros referenciando otro Core puro: la purity scan sigue verde | **Elegida** |

`EntradaAsiento` no es una porción de `fact.Factura`: es lo que §5/§6 necesitan, ya resuelto por
#3/#4. #8 **no** re-resuelve prefijos (`ResolucionDePrefijos`) ni re-elige SBS/MANUAL
(`SeleccionDeTipoCambio`); los consume compuestos.

El TC entra como `TipoCambioCongelado`, un envoltorio de una sola línea sobre `decimal Venta` con
dos constructores nombrados: `DeTipoCambio(TipoCambio)` (lee `.Venta`, nunca `.Compra` — ADR 0018
pt. 1 imposible de equivocar en el call site) y `Heredado(decimal)` (NC con referencia interna,
§6). Un `decimal` desnudo dejaba pasar `Compra` sin que nada lo notara.

### Decisión 2 — la NC recibe la herencia **pre-aplanada**, con adaptador puro incluido

`Componer` acepta `EntradaAsiento.Herencia: HerenciaNotaCredito?`, un record con exactamente los
cuatro atributos que §5 enumera (afectación congelada, TC congelado, cuentas de cargo congeladas
con sus importes, cuentas de destino congeladas). **No** recibe el `AsientoContable` de la factura.

Rechazado pasar el asiento completo: arrastra campos de ciclo de vida (`Estado`, `NumeroAsiento`,
`Version`) que son de #11 y que #8 no debe poder leer; y #8, sin repositorio, no puede obtenerlo.

El punto de enganche para #10 es explícito y vive aquí: `HerenciaNotaCredito.DesdeAsiento(
AsientoContable factura)`, adaptador puro que hace el aplanado una sola vez. #10 no lo reimplementa;
#10 aporta **de dónde sale** ese asiento (referencia interna/externa, tope acumulado, reparto).

### Decisión 3 — rechazo como jerarquía cerrada, nunca excepción

`ResultadoConfirmacion` abstracto con `private protected` ctor y dos casos anidados sellados:
`Confirmable(AsientoContable)` e `InvariantesIncumplidas(IReadOnlyList<InvarianteIncumplida>)` —
copia exacta de la forma de `ResultadoTipoCambio` (item #4, Decisión 2). Rechazadas: excepciones
(un incumplimiento de §7 es un resultado esperado del dominio, no un fallo), y `Result<T,E>` de
NuGet (rompe la purity scan). Las excepciones quedan solo para errores de programación
(`ArgumentNullException`), nunca para resultados contables.

`InvarianteIncumplida` lleva un `enum InvarianteContable` (un valor por invariante de §7) y los
importes en conflicto. **No lleva código HTTP**: traducir a `409`/`412`/`422` es de #11.

### Decisión 4 — límite §7/§8: es evaluable-desde-un-solo-asiento o no lo es

| Regla | Dueño | Por qué |
|---|---|---|
| §7 globales 1–5, PRINCIPAL (4 filas), DESTINO | **#8** | Evaluables sobre un `AsientoContable` + `fechaCorteContable` (parámetro, jamás `DateTime.Today`) |
| §7 tope acumulado de NC | #10 | Exige `SELECT` sobre otras facturas y su asiento vigente. #8 **no** deja placeholder |
| §8 duplicado, domingo, XML mixto, precondiciones de NC | #11 | Catálogo operativo de rechazo, no invariante contable |

La precondición vieja de NC ("factura original validada") **no aparece en #8 en ninguna forma**,
ni como `enum` ni como comentario: fue relajada por el dueño (2026-08-19) y codificarla la
contradiría en silencio. Ver `Backlog de mejoras.md` §1.

### Decisión 5 — el reparto son **importes absolutos en PEN**, que §7 verifica

`EntradaAsiento.Cargos: IReadOnlyList<CargoSolicitado>`, con `CargoSolicitado(CuentaContable
Cuenta, decimal ImportePEN)`. Lista de uno = caso normal; lista de N = "División del cargo" (§5).

Rechazadas las **proporciones** (`decimal Proporcion` sumando 1): harían que "los cargos igualan la
base" se cumpliera por construcción y **borrarían una invariante que §7 manda comprobar**. Con
importes, §7 hace trabajo real. Derivar el reparto proporcional de una NC parcial y aplicar la
regla del céntimo residual (§5) es de #10; #8 espeja los N importes que recibe.

## Data Flow

    #3 ResolucionDePrefijos ──┐
    #4 SeleccionDeTipoCambio ─┼─▶ EntradaAsiento ──▶ Componer (§5+§6) ──▶ AsientoContable
    #10 HerenciaNotaCredito ──┘                                              │ (congelado)
                                                                             ▼
                        ResultadoConfirmacion { Confirmable │ InvariantesIncumplidas }
                                    └──▶ #11 persiste fact.AsientoContable(+Detalle) / responde

Dentro de `Componer`: cuenta de proveedor por (Moneda × EsRelacionada) §4 → PRINCIPAL según
(TipoComprobante × Afectación) §5 → totalPEN/igvPEN anclados y basePEN **derivado** §6 → DESTINO
por cada cargo con `CtaReflejaCodigo` no nulo.

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/contable/SmartNet.Contable.Core/SmartNet.Contable.Core.csproj` | Create | `net10.0`, cero `PackageReference`, `ProjectReference` a Catalogos.Core y TiposCambio.Core |
| `.../AsientoContable.cs`, `LineaAsiento.cs` | Create | Records congelados, espejo de `fact.AsientoContable(+Detalle)` sin campos de ciclo de vida |
| `.../EntradaAsiento.cs`, `CargoSolicitado.cs`, `TipoCambioCongelado.cs`, `HerenciaNotaCredito.cs` | Create | Contrato de entrada (Decisiones 1, 2, 5) |
| `.../ComposicionDeAsiento.cs`, `ConversionDeMoneda.cs`, `CuentaDeProveedor.cs` | Create | §5, §6, §4 |
| `.../InvariantesDeConfirmacion.cs`, `ResultadoConfirmacion.cs`, `InvarianteContable.cs` | Create | §7 (Decisiones 3, 4) |
| `SmartNet/contable/SmartNet.Contable.Core.Tests/` | Create | `PurityScanTests` copiado + goldens §10 + §7 en ambos caminos |
| `SmartNet/SmartNet.sln` | Modify | Carpeta `contable` + 2 proyectos |
| `.github/workflows/ci.yml` | Modify | `Contable.Core.Tests` en `verificaciones-estaticas` (no necesita contenedor: es puro) |

## Interfaces / Contracts

```csharp
public enum Bloque { Principal, Destino }
public enum TipoLinea { D, H }
public enum Afectacion { Gravada, Exonerada, Inafecta }
public enum TipoComprobante { Factura, Boleta, NotaCredito }   // 01 / 03 / 07

public sealed record LineaAsiento(
    short Orden, Bloque Bloque, TipoLinea Tipo, decimal Debe, decimal Haber,
    string? CuentaCodigo, string? CuentaDescripcion,
    string? CtaReflejaCodigo, string? CtaPuenteCodigo)
{
    public bool SinCuenta => CuentaCodigo is null;      // §7 invariante 2
}

public sealed record AsientoContable(
    string ProveedorCodigo, DateOnly FechaContable, string? MotivoDescripcion,
    decimal? TipoCambioVenta, decimal BasePEN, decimal IgvPEN, decimal NetoPEN,
    Afectacion AfectacionCongelada, TipoComprobante Comprobante,
    IReadOnlyList<LineaAsiento> Lineas);

public sealed record TipoCambioCongelado
{
    public decimal Venta { get; }
    public static TipoCambioCongelado DeTipoCambio(TipoCambio tc);   // ADR 0018 pt. 1
    public static TipoCambioCongelado Heredado(decimal ventaCongelada);   // §6, NC interna
}

public abstract record ResultadoConfirmacion
{
    private protected ResultadoConfirmacion() { }
    public sealed record Confirmable(AsientoContable Asiento) : ResultadoConfirmacion;
    public sealed record InvariantesIncumplidas(
        IReadOnlyList<InvarianteIncumplida> Fallos) : ResultadoConfirmacion;
}
```

`Moneda` viaja como `enum MonedaAsiento { Pen, Usd }` — §4 cruza moneda con `EsRelacionada` para
cuatro cuentas fijas; un `string` admitiría un quinto valor que ninguna fila cubre.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit (golden §10) | Los 7 ejemplos numéricos de `REGLAS.md` §10, línea a línea (cuenta, tipo, importe, bloque, orden) | Fixtures en memoria; el `expected` se transcribe del documento, no del código |
| Unit (§7) | Cada invariante de §7 dentro del alcance de #8 (Decisión 4), en **ambos** caminos (acepta / rechaza), y el caso multi-fallo (`InvariantesIncumplidas` con 2+) | `Evaluar` sobre asientos construidos a mano, sin pasar por `Componer` |
| Unit (§6) | `basePEN` derivado, no calculado: caso 10.3 y el céntimo que absorbe la cuenta de cargo; NC 100% USD (10.7) deja el pasivo en cero exacto con TC heredado | `TipoCambioCongelado.Heredado` |
| Unit (§5 estructura) | Los 4 casos de PRINCIPAL, incluida la fila v2 (NC sobre boleta: 2 líneas, **sin** `401111`); DESTINO ausente para cuentas clase 1/4 sin `ctarefleja` | Tabla de casos |
| Unit (purity) | `PurityScanTests` sobre `SmartNet.Contable.Core.dll` | Copia literal de `SmartNet.TiposCambio.Core.Tests` (NetArchTest + escaneo IL de `DateTime.Now/UtcNow`) |
| Integration / E2E | **Ninguno** | #8 no toca DB ni HTTP; el E2E único de ADR 0019 se arma en #11 |

TDD estricto (`config.yaml strict_tdd: true`): los goldens de §10 se escriben en RED antes de
`Componer`.

## Threat Matrix

| Boundary | Applicability | Reason |
|---|---|---|
| Documentation-like paths / executable classification | N/A | El proyecto no clasifica ni ejecuta archivos |
| Shell / subprocess / routing | N/A | Biblioteca pura sin proceso externo, red ni entrada de usuario en runtime |
| Git / commit / push / PR automation | N/A | Sin automatización de VCS |

**ADR 0003 (partición de datos): no aplica y se documenta la exclusión.** #8 no abre conexión, no
nombra tabla alguna y no cruza la frontera .NET/Python; la purity scan lo hace estructural.
**ADR 0016 (schema en SQL versionado): no aplica** — #8 no añade ni modifica DDL; `005_negocio.sql`
ya existe y #8 solo moldea su salida para él.

## Migration / Rollout

No migration required. Aditivo puro: dos proyectos nuevos y dos ediciones de wiring
(`SmartNet.sln`, `ci.yml`). Revertir el commit los elimina; nada existente los referencia hasta
que #9/#10/#11 lo hagan.

## Open Questions — resueltas

Las cinco preguntas de `Backlog de mejoras.md` §3 quedan cerradas por las Decisiones 1–5, en ese
orden. No queda ninguna abierta.

**Advertencia de alcance que sobrevive al archivado** (`Backlog de mejoras.md` §5, `REGLAS.md`
§12): las seis reglas contables que este motor implementa **no están ratificadas por un contador**.
Completar #8 significa que el motor hace lo que `REGLAS.md` dice, no que el sistema esté listo para
contabilidad real.
