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

    public static ResultadoComando? Validar(CorreccionFactura cambios)
    {
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
