namespace SmartNet.Api;

/// <summary>
/// design D2 / Threat Matrix — la parte de <c>GET /api/documentos/{id}/contenido</c> que decide
/// "¿es seguro servir esto?" sin tocar el disco: contención de ruta (path traversal) y allow-list de
/// MIME (confusión de MIME / XSS almacenado contra la cookie de sesión de un <c>&lt;iframe&gt;</c>
/// mismo-origen). Deliberadamente sin I/O -- design.md Testing Strategy la clasifica "Unit ... no
/// I/O", así que <see cref="DocumentoEndpoints"/> es el único lugar que abre un archivo.
/// </summary>
internal static class DocumentoContenido
{
    /// <summary>design D2 -- MIME almacenado que SÍ se sirve con su Content-Type real. Cualquier otro
    /// valor (incluido <c>text/html</c>/<c>image/svg+xml</c>, ambos ejecutables en un
    /// <c>&lt;iframe&gt;</c> mismo-origen) degrada a <c>application/octet-stream</c> -- nunca se echa
    /// el MIME almacenado verbatim.</summary>
    internal static readonly IReadOnlySet<string> MimeAllowList = new HashSet<string>(StringComparer.Ordinal)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
    };

    internal static string ContentTypeFor(string mimeAlmacenado) =>
        MimeAllowList.Contains(mimeAlmacenado) ? mimeAlmacenado : "application/octet-stream";

    /// <summary>
    /// Resuelve <paramref name="rutaRelativa"/> (un valor de columna SQL, nunca confiable por sí
    /// mismo -- design D2) contra <paramref name="raiz"/> y canonicaliza el resultado. Devuelve
    /// <c>null</c> si la ruta canonicalizada queda FUERA de la raíz (traversal, ej. <c>../</c> o una
    /// ruta absoluta que reemplaza la raíz) -- el llamador traduce eso a 404, nunca a 400 (design D2:
    /// ni confirma ni niega la existencia del recurso más allá de "no servible").
    /// </summary>
    internal static string? ResolverRutaSegura(string raiz, string rutaRelativa)
    {
        var raizCompleta = Path.GetFullPath(raiz);
        var raizConSeparador = raizCompleta.EndsWith(Path.DirectorySeparatorChar)
            ? raizCompleta
            : raizCompleta + Path.DirectorySeparatorChar;

        // TrimStart evita que una rutaRelativa que empieza con '/' o '\' sea tratada por
        // Path.Combine como una ruta absoluta que DESCARTA la raíz por completo (Path.Combine's
        // propio comportamiento documentado) -- sin este trim, "/etc/passwd" pasaría el chequeo de
        // StartsWith de abajo trivialmente mal, porque GetFullPath ya la habría resuelto fuera de
        // la raíz ANTES de llegar aquí. Con el trim, se combina siempre como relativa.
        var combinada = Path.GetFullPath(Path.Combine(raizCompleta, rutaRelativa.TrimStart('/', '\\')));

        return combinada.StartsWith(raizConSeparador, StringComparison.Ordinal) ? combinada : null;
    }
}
