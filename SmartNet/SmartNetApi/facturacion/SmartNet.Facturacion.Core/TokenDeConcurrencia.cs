namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md D2 — codec compartido para el ETag de concurrencia optimista de todas las superficies
/// mutables de #11/#12/#13. NO hay interfaz <c>IConcurrencyToken</c>: un valor no tiene polimorfismo
/// que ocultar (D2, resuelto). ETag = comillas + Base64(rowversion de 8 bytes).
/// </summary>
public static class TokenDeConcurrencia
{
    /// <summary>Codifica un <c>ROWVERSION</c> de SQL Server (8 bytes) como ETag HTTP.</summary>
    public static string Codificar(byte[] version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return $"\"{Convert.ToBase64String(version)}\"";
    }

    /// <summary>
    /// Intenta decodificar un ETag HTTP de vuelta al <c>ROWVERSION</c> original. Falla (devuelve
    /// <c>false</c>) para cualquier valor que no sea exactamente comillas envolviendo Base64 válido
    /// — un If-Match ausente, <c>*</c>, o malformado se traduce a 428 en <c>SmartNet.Api</c>
    /// (design D2), nunca aquí.
    /// </summary>
    public static bool TryDecodificar(string? etag, out byte[]? version)
    {
        version = null;

        if (string.IsNullOrEmpty(etag) || etag.Length < 2 || etag[0] != '"' || etag[^1] != '"')
        {
            return false;
        }

        var base64 = etag[1..^1];
        if (base64.Length == 0)
        {
            return false;
        }

        try
        {
            version = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
