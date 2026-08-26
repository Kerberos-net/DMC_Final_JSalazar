using Microsoft.AspNetCore.Http;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// design D2 -- codec HTTP compartido para el encabezado <c>If-Match</c> de todas las superficies
/// mutables de facturas/asientos: envuelve <see cref="TokenDeConcurrencia.TryDecodificar"/> (Core,
/// puro) y decide el 428 (obs #138, addendum a ADR 0008) que Core deliberadamente no conoce -- Core
/// solo sabe codificar/decodificar un token, nunca qué código HTTP corresponde a su ausencia.
/// </summary>
internal static class IfMatch
{
    /// <summary>Ausente, <c>*</c>, o no decodificable -&gt; <c>false</c> con <paramref name="error"/>
    /// listo para devolver (428). Presente y decodificable -&gt; <c>true</c> con
    /// <paramref name="version"/> el rowversion de 8 bytes a usar como CAS esperado.</summary>
    public static bool Requerido(HttpContext context, out byte[] version, out IResult? error)
    {
        var encabezado = context.Request.Headers.IfMatch.ToString();

        if (!TokenDeConcurrencia.TryDecodificar(encabezado, out var decodificado) || decodificado is null)
        {
            version = Array.Empty<byte>();
            error = ProblemasDeNegocio.PreconditionRequerida();
            return false;
        }

        version = decodificado;
        error = null;
        return true;
    }
}
