namespace SmartNet.Contable.Core;

/// <summary>
/// Fase BORRADOR del pipeline (design.md, ADR 0006 split BORRADOR/CONFIRMADO): resuelve PRINCIPAL
/// (REGLAS.md §5, 4 casos) + DESTINO (§5) + conversión (§6). <b>Total</b>: nunca lanza ni rechaza
/// por motivos contables — un borrador puede quedar incompleto o descuadrado, que es exactamente
/// lo que ADR 0006 permite. Solo lanza <see cref="ArgumentNullException"/> por errores de
/// programación (entrada nula), nunca por un resultado contable.
/// </summary>
public static class ComposicionDeAsiento
{
    private const string CuentaIgv = "401111";
    private const string DescripcionIgv = "IGV - CUENTA PROPIA";
    private const string CuentaPercepcion = "401131";
    private const string DescripcionPercepcion = "IGV - REGIMEN DE PERCEPCIONES";
    private const string DescripcionCargasImputables = "CARGAS IMPUTABLES A CTA DE COSTOS";

    public static AsientoContable Componer(EntradaAsiento entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var esNotaCredito = entrada.Comprobante == TipoComprobante.NotaCredito;
        var herencia = entrada.Herencia;
        var heredaDeFactura = esNotaCredito && herencia is not null;

        var afectacionEfectiva = heredaDeFactura ? herencia!.AfectacionCongelada : entrada.Afectacion;
        var cargos = heredaDeFactura ? herencia!.CargosCongelados : entrada.Cargos;
        var motivoDescripcion = heredaDeFactura ? herencia!.MotivoDescripcion : entrada.MotivoDescripcion;

        // REGLAS.md §6 "La nota de crédito hereda el tipo de cambio de su factura": el TC
        // heredado (si existe) gana sobre el propio de la entrada, incluida una NC en soles
        // (herencia null => sin conversión, igual que la entrada).
        var tipoCambio = heredaDeFactura ? herencia!.TipoCambioCongelado : entrada.TipoCambio;

        var aplicaConversion = tipoCambio is not null;
        decimal totalPEN, igvPEN, basePEN;
        if (aplicaConversion)
        {
            (totalPEN, igvPEN, basePEN) = ConversionDeMoneda.Convertir(
                entrada.BaseOrig, entrada.IgvOrig, tipoCambio!.Venta);
        }
        else
        {
            igvPEN = entrada.IgvOrig;
            basePEN = entrada.BaseOrig;
            totalPEN = entrada.BaseOrig + entrada.IgvOrig;
        }

        var percepcionPEN = aplicaConversion
            ? Math.Round(entrada.PercepcionOrig * tipoCambio!.Venta, 2, MidpointRounding.AwayFromZero)
            : entrada.PercepcionOrig;

        var cuentaProveedor = CuentaDeProveedor.Codigo(entrada.Moneda, entrada.EsRelacionada);
        var descripcionProveedor = CuentaDeProveedor.Descripcion(entrada.Moneda, entrada.EsRelacionada);

        // REGLAS.md §5: el 401111 aparece únicamente cuando la afectación congelada es GRAVADA.
        // Una boleta no otorga crédito fiscal por construcción — su Afectacion nunca se marca
        // Gravada aguas arriba (#3/#11); el catálogo de motivos no ofrece esa combinación.
        var esGravada = afectacionEfectiva == Afectacion.Gravada;

        var lineas = new List<LineaAsiento>();
        short orden = 1;

        if (!esNotaCredito)
        {
            foreach (var cargo in cargos)
            {
                lineas.Add(LineaCargo(ref orden, cargo, TipoLinea.D));
            }

            if (esGravada)
            {
                lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.D, igvPEN, 0m,
                    CuentaIgv, DescripcionIgv, null, null));

                if (entrada.PercepcionOrig != 0m)
                {
                    lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.D, percepcionPEN, 0m,
                        CuentaPercepcion, DescripcionPercepcion, null, null));
                }

                lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.H, 0m, totalPEN + percepcionPEN,
                    cuentaProveedor, descripcionProveedor, null, null));
            }
            else
            {
                lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.H, 0m, totalPEN,
                    cuentaProveedor, descripcionProveedor, null, null));
            }
        }
        else
        {
            lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.D, totalPEN, 0m,
                cuentaProveedor, descripcionProveedor, null, null));

            foreach (var cargo in cargos)
            {
                lineas.Add(LineaCargo(ref orden, cargo, TipoLinea.H));
            }

            if (esGravada)
            {
                lineas.Add(new LineaAsiento(orden++, Bloque.Principal, TipoLinea.H, 0m, igvPEN,
                    CuentaIgv, DescripcionIgv, null, null));
            }
        }

        AgregarBloqueDestino(lineas, ref orden);

        return new AsientoContable(
            entrada.ProveedorCodigo,
            entrada.FechaContable,
            motivoDescripcion,
            tipoCambio?.Venta,
            basePEN,
            igvPEN,
            totalPEN,
            afectacionEfectiva,
            esNotaCredito ? TipoComprobante.NotaCredito : entrada.Comprobante,
            lineas);
    }

    private static LineaAsiento LineaCargo(ref short orden, CargoSolicitado cargo, TipoLinea tipo)
    {
        var linea = tipo == TipoLinea.D
            ? new LineaAsiento(orden, Bloque.Principal, TipoLinea.D, cargo.ImportePEN, 0m,
                cargo.Cuenta.Cuenta, cargo.Cuenta.Descripcion, cargo.Cuenta.CtaReflejaCodigo, cargo.Cuenta.CtaPuenteCodigo)
            : new LineaAsiento(orden, Bloque.Principal, TipoLinea.H, 0m, cargo.ImportePEN,
                cargo.Cuenta.Cuenta, cargo.Cuenta.Descripcion, cargo.Cuenta.CtaReflejaCodigo, cargo.Cuenta.CtaPuenteCodigo);
        orden++;
        return linea;
    }

    /// <summary>
    /// REGLAS.md §5 "Bloque DESTINO": para cada línea de cargo con <c>CtaReflejaCodigo</c>
    /// congelado, genera el par reflejo/puente por el mismo importe. El reflejo copia la
    /// dirección (D/H) de la línea de cargo; el puente es la opuesta. En una nota de crédito el
    /// par ya sale invertido porque el cargo mismo va al Haber.
    /// </summary>
    private static void AgregarBloqueDestino(List<LineaAsiento> lineas, ref short orden)
    {
        var cargosConDestino = lineas
            .Where(l => l.Bloque == Bloque.Principal && l.CtaReflejaCodigo is not null)
            .ToList();

        foreach (var cargo in cargosConDestino)
        {
            var importe = cargo.Tipo == TipoLinea.D ? cargo.Debe : cargo.Haber;

            if (cargo.Tipo == TipoLinea.D)
            {
                lineas.Add(new LineaAsiento(orden++, Bloque.Destino, TipoLinea.D, importe, 0m,
                    cargo.CtaReflejaCodigo, null, null, null));
                lineas.Add(new LineaAsiento(orden++, Bloque.Destino, TipoLinea.H, 0m, importe,
                    cargo.CtaPuenteCodigo, DescripcionCargasImputables, null, null));
            }
            else
            {
                lineas.Add(new LineaAsiento(orden++, Bloque.Destino, TipoLinea.H, 0m, importe,
                    cargo.CtaReflejaCodigo, null, null, null));
                lineas.Add(new LineaAsiento(orden++, Bloque.Destino, TipoLinea.D, importe, 0m,
                    cargo.CtaPuenteCodigo, DescripcionCargasImputables, null, null));
            }
        }
    }
}
