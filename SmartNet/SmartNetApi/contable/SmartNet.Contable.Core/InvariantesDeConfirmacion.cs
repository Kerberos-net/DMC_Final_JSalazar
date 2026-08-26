namespace SmartNet.Contable.Core;

/// <summary>
/// Fase CONFIRMADO del pipeline (design.md, ADR 0006): puerta de REGLAS.md §7 sobre el asiento ya
/// congelado. Devuelve TODAS las invariantes incumplidas, no la primera. No recibe catálogo:
/// <see cref="AsientoContable"/> es autocontenido, de modo que la invariante DESTINO se evalúa
/// contra el dato congelado en la línea, nunca contra el catálogo vivo.
/// </summary>
public static class InvariantesDeConfirmacion
{
    private const string CuentaIgv = "401111";
    private const string CuentaPercepcion = "401131";

    public static ResultadoConfirmacion Evaluar(AsientoContable asiento, DateOnly fechaCorteContable)
    {
        ArgumentNullException.ThrowIfNull(asiento);

        var fallos = new List<InvarianteIncumplida>();

        EvaluarGlobal1(asiento, fallos);
        EvaluarGlobal2(asiento, fallos);
        EvaluarGlobal3(asiento, fechaCorteContable, fallos);
        EvaluarGlobal4(asiento, fallos);
        EvaluarGlobal5(asiento, fallos);
        EvaluarPrincipal(asiento, fallos);
        EvaluarDestino(asiento, fallos);

        return fallos.Count == 0
            ? new ResultadoConfirmacion.Confirmable(asiento)
            : new ResultadoConfirmacion.InvariantesIncumplidas(fallos);
    }

    private static void EvaluarGlobal1(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        var sumaDebe = asiento.Lineas.Sum(l => l.Debe);
        var sumaHaber = asiento.Lineas.Sum(l => l.Haber);

        if (sumaDebe != sumaHaber)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.SumaDebeIgualHaber, sumaHaber, sumaDebe,
                $"SUM(Debe)={sumaDebe} != SUM(Haber)={sumaHaber}"));
        }
    }

    private static void EvaluarGlobal2(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        var sinCuenta = asiento.Lineas.Count(l => l.SinCuenta);
        if (sinCuenta > 0)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.LineaSinCuenta, null, null,
                $"{sinCuenta} línea(s) sin cuenta contable asignada."));
        }
    }

    private static void EvaluarGlobal3(AsientoContable asiento, DateOnly fechaCorteContable, List<InvarianteIncumplida> fallos)
    {
        if (asiento.FechaContable < fechaCorteContable)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.FechaAnteriorAlCorte, null, null,
                $"FechaContable={asiento.FechaContable} es anterior a FechaCorteContable={fechaCorteContable}."));
        }
    }

    private static void EvaluarGlobal4(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        if (asiento.ProveedorCodigo == "P00000")
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.ProveedorVarios, null, null,
                "El proveedor es P00000 (Varios), no permitido para confirmar."));
        }
    }

    private static void EvaluarGlobal5(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        var inconsistentes = asiento.Lineas.Count(l => l.Tipo switch
        {
            TipoLinea.D => !(l.Debe > 0m && l.Haber == 0m),
            TipoLinea.H => !(l.Haber > 0m && l.Debe == 0m),
            _ => true,
        });

        if (inconsistentes > 0)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.TipoLineaInconsistente, null, null,
                $"{inconsistentes} línea(s) con Tipo/Debe/Haber inconsistentes."));
        }
    }

    /// <summary>
    /// REGLAS.md §7 tabla del bloque PRINCIPAL. La dirección de la línea de proveedor (D para NC,
    /// H para factura/boleta) discrimina si el asiento es una nota de crédito, sin necesitar el
    /// comprobante original.
    /// </summary>
    private static void EvaluarPrincipal(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        var esGravada = asiento.AfectacionCongelada == Afectacion.Gravada;
        var esNotaCredito = asiento.Comprobante == TipoComprobante.NotaCredito;

        var principal = asiento.Lineas.Where(l => l.Bloque == Bloque.Principal).ToList();
        var tipoCargo = esNotaCredito ? TipoLinea.H : TipoLinea.D;

        var cargos = principal.Where(l =>
            l.Tipo == tipoCargo &&
            l.CuentaCodigo is not null &&
            l.CuentaCodigo != CuentaIgv &&
            l.CuentaCodigo != CuentaPercepcion &&
            (l.CuentaCodigo.StartsWith('6') || l.CuentaCodigo.StartsWith('1')));

        var sumaCargos = cargos.Sum(l => tipoCargo == TipoLinea.D ? l.Debe : l.Haber);
        var esperadoCargos = esGravada ? asiento.BasePEN : asiento.NetoPEN;

        if (sumaCargos != esperadoCargos)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.Principal, esperadoCargos, sumaCargos,
                $"Los cargos 6x/1x suman {sumaCargos}, se esperaba {esperadoCargos}."));
            return;
        }

        var lineaIgv = principal.FirstOrDefault(l => l.CuentaCodigo == CuentaIgv);

        if (esGravada)
        {
            var importeIgv = lineaIgv is null ? 0m : (tipoCargo == TipoLinea.D ? lineaIgv.Debe : lineaIgv.Haber);
            if (lineaIgv is null || importeIgv != asiento.IgvPEN)
            {
                fallos.Add(new InvarianteIncumplida(
                    InvarianteContable.Principal, asiento.IgvPEN, importeIgv,
                    $"El cargo a {CuentaIgv} es {importeIgv}, se esperaba el IGV {asiento.IgvPEN}."));
            }
        }
        else if (lineaIgv is not null)
        {
            // REGLAS.md §7 cuarta fila: "401111 no aplica" — la boleta/no-gravada nunca otorgó
            // crédito fiscal, revertirlo (o cargarlo) sería indebido.
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.Principal, 0m, lineaIgv.Debe + lineaIgv.Haber,
                $"{CuentaIgv} no aplica: la afectación congelada no es GRAVADA."));
        }
    }

    /// <summary>
    /// REGLAS.md §7 "Del bloque DESTINO": para cada línea PRINCIPAL con CtaReflejaCodigo
    /// congelado, existe su par reflejo/puente por el mismo importe. Evaluado contra el dato
    /// congelado en la línea, nunca contra el catálogo vivo — el asiento no recibe catálogo.
    /// </summary>
    private static void EvaluarDestino(AsientoContable asiento, List<InvarianteIncumplida> fallos)
    {
        var conReflejo = asiento.Lineas
            .Where(l => l.Bloque == Bloque.Principal && l.CtaReflejaCodigo is not null)
            .ToList();

        var faltantes = 0;

        foreach (var cargo in conReflejo)
        {
            var importe = cargo.Tipo == TipoLinea.D ? cargo.Debe : cargo.Haber;
            var direccionReflejo = cargo.Tipo; // el reflejo copia la dirección del cargo (ComposicionDeAsiento)

            var tienePar = asiento.Lineas.Any(l =>
                l.Bloque == Bloque.Destino &&
                l.CuentaCodigo == cargo.CtaReflejaCodigo &&
                l.Tipo == direccionReflejo &&
                (direccionReflejo == TipoLinea.D ? l.Debe : l.Haber) == importe);

            if (!tienePar)
            {
                faltantes++;
            }
        }

        if (faltantes > 0)
        {
            fallos.Add(new InvarianteIncumplida(
                InvarianteContable.Destino, null, null,
                $"{faltantes} línea(s) PRINCIPAL con CtaReflejaCodigo sin su par DESTINO."));
        }
    }
}
