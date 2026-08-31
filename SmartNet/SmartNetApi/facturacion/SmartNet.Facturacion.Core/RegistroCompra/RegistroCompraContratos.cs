namespace SmartNet.Facturacion.Core.RegistroCompra;

/// <summary>
/// spec registro-compra-api req 1 — one row of the purchase register listing (the "cabecera" of an
/// <c>fact.AsientoContable</c> joined to its <c>fact.Factura</c> and, when present, its
/// <c>dbo.Proveedor</c> name).
///
/// design D4: money / rate / <c>NumeroAsiento</c> / <c>NumeroComprobante</c> / <c>Glosa</c> are all
/// nullable in <c>005_negocio.sql</c>. They are echoed VERBATIM from the columns — coercing a NULL
/// amount to 0 would manufacture a fake descuadre in the SPA inconsistency badge. <c>OrigenLibro</c>
/// is the real column value, never the <c>ServicioDeFacturas</c> "02" constant.
/// </summary>
public sealed record RegistroCompraCabecera(
    long AsientoContableId,
    string? NumeroComprobante,
    string? NumeroAsiento,
    string OrigenLibro,
    string ProveedorCodigo,
    string? ProveedorNombre,
    string? Glosa,
    DateOnly FechaContable,
    decimal? TipoCambioVenta,
    decimal? BasePEN,
    decimal? IgvPEN,
    decimal? NetoPEN);

/// <summary>
/// spec registro-compra-api req 2 — one line of an asiento's detail (<c>fact.AsientoContableDetalle</c>),
/// read-only. <c>Debe</c>/<c>Haber</c> are non-nullable in the schema. Ordered by <c>Orden</c>.
/// </summary>
public sealed record LineaRegistro(
    short Orden,
    string Bloque,
    string Tipo,
    decimal Debe,
    decimal Haber,
    string? CuentaCodigo,
    string? CuentaDescripcion);

/// <summary>
/// spec registro-compra-api req 2 — an asiento's cabecera plus its ordered lines. A qualifying
/// asiento with zero lines yields an empty <see cref="Lineas"/> list (the endpoint returns 200,
/// never 404, for that case).
/// </summary>
public sealed record RegistroCompraDetalle(
    RegistroCompraCabecera Cabecera,
    IReadOnlyList<LineaRegistro> Lineas);

/// <summary>
/// spec registro-compra-api req 1 / design D1 — one page of listing results plus pagination
/// metadata. Field names are byte-identical to the project's <c>PaginaBandeja&lt;T&gt;</c> wire
/// envelope <c>{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }</c> so the SPA can
/// reuse its paginador as-is. This is a LOCAL type: <c>PaginaBandeja&lt;T&gt;</c> (SmartNet.Inbox.Core)
/// drags a mandatory <c>ResumenBandeja</c> and would make facturacion depend on inbox. #22 hit the
/// same wall and answered with a local <c>PaginaProveedores</c>. <see cref="TotalRegistros"/> is the
/// full filtered count from <c>COUNT(*) OVER()</c>.
/// </summary>
public sealed record PaginaRegistroCompra<T>(
    IReadOnlyList<T> Items,
    int Pagina,
    int TamanioPagina,
    int TotalRegistros,
    int TotalPaginas);
