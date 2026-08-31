using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// BACKLOG #18 PR5 (api-facturas delta) — guarda PURA (ADR 0019: sin DB, HTTP ni reloj) para los
/// dos campos que PR5 hace PATCH-editables. <c>null</c> en un campo significa "no se toca" y nunca
/// se rechaza; solo se valida lo que viene con valor. Devuelve
/// <see cref="ResultadoComando.CorreccionInvalida"/> (-&gt; 422) o <c>null</c> cuando no hay nada
/// que objetar. <see cref="ServicioDeFacturas.PatchAsync"/> la llama ANTES de escribir, así una
/// corrección inválida no toca ninguna fila.
/// </summary>
public static class ValidacionDeCorreccion
{
    private const int NumeroMaximo = 20;

    private const string CodigoBoleta = "03";
    private const string CodigoNotaCredito = "07";
    private const string AfectacionExonerada = "EXONERADA";
    private const string AfectacionInafecta = "INAFECTA";

    public static ResultadoComando? Validar(FacturaPersistida original, CorreccionFactura cambios)
    {
        // BACKLOG #19 (design D1) — base imponible + IGV son un par atómico; la base es derivada.
        var tocaBase = cambios.BaseImponible is not null;
        var tocaIgv = cambios.Igv is not null;
        var tocaGlosa = cambios.Glosa is not null;

        if (tocaBase != tocaIgv)
        {
            return new ResultadoComando.CorreccionInvalida(
                "La base imponible y el IGV se corrigen juntos: envie ambos o ninguno.");
        }

        if ((tocaBase || tocaIgv) && cambios.TotalOrig is not null)
        {
            return new ResultadoComando.CorreccionInvalida(
                "No se puede enviar el par base/IGV junto con totalOrig: el total se deriva de base + IGV.");
        }

        // design D2 — los campos contables (base/IGV/glosa) solo son editables mientras la factura
        // sigue PENDIENTE_VALIDACION. Numero y tipo de comprobante conservan su comportamiento
        // (corregibles y auditados incluso tras validar).
        if ((tocaBase || tocaIgv || tocaGlosa) && original.Estado != FacturaPersistida.PendienteValidacion)
        {
            return new ResultadoComando.CorreccionInvalida(
                "La base imponible, el IGV y la glosa solo pueden corregirse mientras la factura esta PENDIENTE_VALIDACION.");
        }

        if (cambios.BaseImponible is < 0m)
        {
            return new ResultadoComando.CorreccionInvalida("La base imponible no puede ser negativa.");
        }

        if (cambios.Igv is < 0m)
        {
            return new ResultadoComando.CorreccionInvalida("El IGV no puede ser negativo.");
        }

        // design D1 owner-decision (a)/(b), REGLAS.md §5 — una boleta 03 y una factura no gravada
        // (EXONERADA / INAFECTA) que NO sea nota de credito no pueden llevar IGV != 0: el IGV va al
        // costo. La NC 07 con referencia interna SI puede (hereda la estructura del §6).
        if (tocaIgv && cambios.Igv is not 0m)
        {
            var tipoEfectivo = cambios.TipoComprobante ?? original.TipoComprobante;
            var afectacionEfectiva = cambios.Afectacion ?? original.Afectacion;

            var esNotaCredito = tipoEfectivo == CodigoNotaCredito;
            var esBoleta = tipoEfectivo == CodigoBoleta;
            var esNoGravada = afectacionEfectiva is AfectacionExonerada or AfectacionInafecta;

            if (esBoleta || (!esNotaCredito && esNoGravada))
            {
                return new ResultadoComando.CorreccionInvalida(
                    "Este comprobante no otorga credito fiscal: el IGV debe ser 0 (se incorpora al costo).");
            }
        }

        if (cambios.Numero is not null)
        {
            if (string.IsNullOrWhiteSpace(cambios.Numero))
            {
                return new ResultadoComando.CorreccionInvalida(
                    "El numero del comprobante no puede quedar en blanco.");
            }

            if (cambios.Numero.Length > NumeroMaximo)
            {
                return new ResultadoComando.CorreccionInvalida(
                    $"El numero del comprobante no puede superar los {NumeroMaximo} caracteres.");
            }
        }

        if (cambios.TipoComprobante is not null && !CodigoComprobante.EsValido(cambios.TipoComprobante))
        {
            return new ResultadoComando.CorreccionInvalida(
                $"Tipo de comprobante no aceptado: '{cambios.TipoComprobante}'. Valores validos: "
                + string.Join(", ", CodigoComprobante.Aceptados) + ".");
        }

        return null;
    }
}
