namespace SmartNet.Inbox.Core;

/// <summary>
/// Outcome of <see cref="IPromocionRepository.PromoverAsync"/>. <see cref="YaExistia"/> is
/// <c>true</c> only on the idempotent-catch path (design D2 — INSERT, catch SQL 2601/2627 from
/// <c>UQ_Factura_Procesamiento</c>, then resolve the existing <c>FacturaId</c>); it never means a
/// second <c>Factura</c> row was created.
/// </summary>
public sealed record ResultadoPromocion(long FacturaId, bool YaExistia);

/// <summary>Outcome of <see cref="IPromocionRepository.ResolverProveedorAsync"/> — SELECT-only fact
/// the caller needs BEFORE invoking <c>CalculoDeIndicadores.Calcular</c>/<c>ConstruccionDeFactura.Construir</c>
/// (design.md Interfaces/Contracts: "proveedorResuelto ... resolved in Infrastructure and passed in
/// as a fact"). <see cref="Codigo"/> is the DDL default <c>P00000</c> when no match exists.</summary>
public sealed record ProveedorResuelto(bool Existe, string Codigo);

/// <summary>
/// Port over the write side of promotion (design.md data flow: one <c>SqlTransaction</c> per
/// event). Implementation (<c>SqlPromocionRepository</c>, <c>usr_api</c> login) lives in
/// <c>SmartNet.Inbox.Infrastructure</c> (Phase 3).
/// </summary>
public interface IPromocionRepository
{
    /// <summary>
    /// INSERTs <c>Factura</c> (<c>PENDIENTE_VALIDACION</c>) + <c>FacturaExtraccion</c> rows, INSERTs
    /// the projected <c>fact.DocumentoFactura</c> row (BACKLOG #12, design D1, schema 016), and
    /// updates the source <c>InboxEvent</c> to <c>PROMOVIDO</c> — all in one transaction. On a
    /// duplicate <c>ProcesamientoId</c>, resolves the existing <c>FacturaId</c> without inserting a
    /// second <c>Factura</c> row (design D2, spec.md "Idempotent promotion"); on a duplicate
    /// <c>DocumentoRecibidoId</c> (a re-processed event for the same document), skips the
    /// <c>DocumentoFactura</c> INSERT the same way (<c>UQ_DocumentoFactura_DocumentoRecibidoId</c>).
    /// </summary>
    Task<ResultadoPromocion> PromoverAsync(
        long inboxEventId, long procesamientoId, FacturaPromovida factura, DocumentoPromovido documento,
        CancellationToken ct);

    /// <summary>Updates the source <c>InboxEvent</c> to <c>DESCARTADO</c> — creates zero <c>Factura</c> rows.</summary>
    Task DescartarAsync(long inboxEventId, string motivoDescarte, CancellationToken ct);

    /// <summary>Read-only fact: resolves <c>dbo.Proveedor</c> by RUC (the one ADR 0003 "clase
    /// externa" this project is granted SELECT on) — not a write, an orchestration-time input.</summary>
    Task<ProveedorResuelto> ResolverProveedorAsync(string? rucProveedor, CancellationToken ct);

    /// <summary>Read-only fact: whether a non-discarded <c>fact.Factura</c> already shares this
    /// identity (RUC + tipo de comprobante + número) via <c>IX_Factura_Identidad</c>.</summary>
    Task<bool> ExisteIdentidadPreviaAsync(
        string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct);

    /// <summary>
    /// design.md Decision 2 -- resolves the associated PDF's paired partner via query A
    /// (<c>fact.DocumentoFactura</c> JOIN <c>fact.Factura</c>, <c>Estado &lt;&gt; 'DESCARTADA'</c>),
    /// falling back to query B (<c>fact.InboxEvent.Payload</c> JSON) only when A is empty. Both
    /// queries run inside <c>usr_api</c>'s grants -- never <c>fact.Procesamiento</c> or
    /// <c>fact.DocumentoRecibido</c> (ADR 0003).
    /// </summary>
    Task<ResolucionPar> ResolverParAsync(long documentoAsociadoId, CancellationToken ct);

    /// <summary>
    /// design.md Decision 4 -- projects <paramref name="documento"/> onto the already-promoted
    /// <paramref name="facturaId"/> and marks <paramref name="inboxEventId"/> <c>PROMOVIDO</c>, in
    /// one transaction. Reuses the same private INSERT + idempotency catch as <c>PromoverAsync</c>;
    /// never calls <c>InsertarFacturaAsync</c>/<c>InsertarExtraccionesAsync</c> -- no second
    /// <c>fact.Factura</c> row is ever created on this path.
    /// </summary>
    Task FusionarDocumentoAsync(long inboxEventId, long facturaId, DocumentoPromovido documento, CancellationToken ct);
}
