namespace SmartNet.Catalogos.Core;

/// <summary>
/// BACKLOG #22 PR5 — design D7. The pure whitelist the proveedores catalogo-mode endpoint validates
/// the <c>orden</c> query parameter against, same shape as <c>EstadoDerivadoBandeja</c> in the inbox
/// core. A valid key is mapped by the SQL adapter to a COMPILE-TIME CONSTANT column
/// (<c>ruc → rucpro</c>, <c>codigo → codpro</c>, <c>proveedor → proveedor</c>); the user text is
/// never concatenated into the query as an identifier, so there is no injection surface.
/// Pure (ADR 0019 level 1): no DB, no HTTP, no clock.
/// </summary>
public static class OrdenProveedor
{
    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "proveedor", "ruc", "codigo",
        };

    public static bool EsValido(string? valor) => valor is not null && Valores.Contains(valor);
}
