using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// BACKLOG #24 (design A1/A2, ADR 0019 nivel 1) — pure builder that turns a
/// <see cref="FacturaPersistida"/> plus the externally-resolved <see cref="HechosDeComposicion"/>
/// into the seed <see cref="AsientoContable"/> for <c>abrir</c> / promotion.
///
/// <see cref="Construir"/> is the field-by-field map onto <see cref="EntradaAsiento"/>;
/// <see cref="Sembrar"/> runs <see cref="ComposicionDeAsiento.Componer"/> (byte-for-byte unchanged)
/// and, when there is no suggested account, appends one placeholder PRINCIPAL cargo line with
/// <c>CuentaCodigo = null</c> so REGLAS.md §7 Global-2 ("ninguna línea sin cuenta") blocks
/// <c>validar</c> — ADR 0006's founding requirement, finally reachable. No sentinel account code.
///
/// NC composition (<c>Herencia</c>) and percepción are out of scope for #24: <c>Herencia = null</c>,
/// <c>PercepcionOrig = 0</c>.
/// </summary>
public static class SembradoDeAsiento
{
    public static EntradaAsiento Construir(FacturaPersistida factura, HechosDeComposicion hechos)
    {
        ArgumentNullException.ThrowIfNull(factura);
        ArgumentNullException.ThrowIfNull(hechos);

        var igvOrig = factura.IgvOrig ?? 0m;
        var baseOrig = factura.TotalOrig - igvOrig;
        var comprobante = CodigoComprobante.Convertir(factura.TipoComprobante);
        var afectacion = MapearAfectacion(factura.Afectacion);
        var importePEN = ImportePrincipal(comprobante, afectacion, baseOrig, igvOrig, hechos.TipoCambio);

        var cargos = hechos.CuentaSugerida is null
            ? Array.Empty<CargoSolicitado>()
            : new[] { new CargoSolicitado(hechos.CuentaSugerida, importePEN) };

        return new EntradaAsiento(
            ProveedorCodigo: factura.ProveedorCodigo,
            EsRelacionada: hechos.EsRelacionada,
            Moneda: factura.Moneda == "PEN" ? MonedaAsiento.Pen : MonedaAsiento.Usd,
            FechaContable: factura.FechaEmision,
            MotivoDescripcion: hechos.MotivoDescripcion,
            Comprobante: comprobante,
            Afectacion: afectacion,
            BaseOrig: baseOrig,
            IgvOrig: igvOrig,
            PercepcionOrig: 0m,
            TipoCambio: hechos.TipoCambio,
            Cargos: cargos,
            Herencia: null);
    }

    public static AsientoContable Sembrar(FacturaPersistida factura, HechosDeComposicion hechos)
    {
        var entrada = Construir(factura, hechos);
        var asiento = ComposicionDeAsiento.Componer(entrada);

        // BACKLOG #24 (Batch 7 guard) — a GRAVADA factura with IgvOrig = 0 drives Componer to emit
        // a 401111 line with Debe = 0, which the persisted-shape check CK_Linea_Tipo
        // (Tipo = 'D' AND Debe > 0) rejects → the seed INSERT throws → 500 at abrir/recomponer.
        // A zero-amount line carries no accounting content, so drop any Debe == 0 AND Haber == 0
        // line before the final AsientoContable is built. ComposicionDeAsiento.Componer stays
        // byte-for-byte unchanged; §7's PRINCIPAL/Global invariants still gate validar on whatever
        // remains (Option 3 — the seed is best-effort, §7 is the accounting gate).
        var lineas = asiento.Lineas
            .Where(l => l.Debe != 0m || l.Haber != 0m)
            .ToList();

        if (hechos.CuentaSugerida is null)
        {
            // design A2 — Componer ran with zero cargos; append the placeholder line so the seed
            // balances (Global-1) yet cannot confirm (Global-2 + PRINCIPAL).
            var importePEN = ImportePrincipal(
                entrada.Comprobante, entrada.Afectacion, entrada.BaseOrig, entrada.IgvOrig, entrada.TipoCambio);
            lineas.Add(new LineaAsiento(
                0, Bloque.Principal, TipoLinea.D, importePEN, 0m, null, null, null, null));
        }

        // Orden is presentation-only per ADR 0006 — renumber 1..n after the drop / placeholder append.
        var renumeradas = lineas
            .Select((linea, indice) => linea with { Orden = (short)(indice + 1) })
            .ToList();

        return asiento with { Lineas = renumeradas };
    }

    // Same pure function BACKLOG #19 / D4 uses, so the seed's default cargo and the scalar
    // projection agree by construction for a freshly opened factura (gravada → BasePEN;
    // boleta / EXONERADA / INAFECTA → NetoPEN, per REGLAS.md §5).
    private static decimal ImportePrincipal(
        TipoComprobante comprobante, Afectacion afectacion, decimal baseOrig, decimal igvOrig,
        TipoCambioCongelado? tipoCambio) =>
        ProyeccionDeImportes.Derivar(comprobante, afectacion, baseOrig, igvOrig, tipoCambio?.Venta ?? 1m).BasePEN;

    // design A1 — same criterio as ServicioDeFacturas.MapearAfectacion / SqlUnidadDeTrabajo
    // .MapearAfectacion: an absent or unknown value composes as GRAVADA (salvo prueba en contrario).
    private static Afectacion MapearAfectacion(string? codigo) => codigo switch
    {
        "EXONERADA" => Afectacion.Exonerada,
        "INAFECTA" => Afectacion.Inafecta,
        _ => Afectacion.Gravada,
    };
}
