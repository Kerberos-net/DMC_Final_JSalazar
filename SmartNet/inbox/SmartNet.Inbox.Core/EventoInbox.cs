namespace SmartNet.Inbox.Core;

/// <summary>
/// One field's evidence inside <c>InboxEvent.Payload.evidencia[]</c> (design.md
/// Interfaces/Contracts). Design D4 (confirmed): <see cref="Fuente"/> is the ONLY evidence per
/// field — the document's <c>TipoDocumento</c>, uniform per event — never a confidence value
/// (#6/ADR 0017 compute none; emitting one would fabricate data).
/// </summary>
public sealed record EvidenciaCampo(string Campo, string Valor, string Fuente);

/// <summary>
/// <c>InboxEvent.Payload.comprobante</c> (design.md Interfaces/Contracts). Every field is
/// nullable: the payload always carries the key, but a failed/partial extraction may leave any of
/// them empty — sufficiency is <see cref="PoliticaDePromocion"/>'s job, not this record's.
/// </summary>
public sealed record ComprobanteExtraido(
    string? TipoComprobante,
    string? Numero,
    string? RucProveedor,
    string? NombreProveedor,
    decimal? Monto,
    string? Moneda,
    DateOnly? FechaEmision);

/// <summary>
/// Parsed <c>fact.InboxEvent.Payload</c> (design.md Interfaces/Contracts JSON example). Parsing
/// the raw JSON happens only in <c>SmartNet.Inbox.Infrastructure</c> (design D9) — Core sees this
/// record already built. <see cref="Comprobante"/> is <c>null</c> when
/// <see cref="EstadoProcesamiento"/> is <c>ERROR</c> (#6 never writes <c>DatosExtraidos</c> for a
/// failed document).
///
/// BACKLOG #12 (design D1): <see cref="NombreArchivo"/>/<see cref="MimeType"/>/
/// <see cref="RutaRelativa"/>/<see cref="TamanoBytes"/> travel here because .NET has no SELECT
/// grant on <c>fact.DocumentoRecibido</c> (ADR 0003 DENY, 008) — this is the only symmetric path
/// to project the document's metadata into the .NET-owned <c>fact.DocumentoFactura</c> at
/// promoción (schema 016).
/// </summary>
public sealed record EventoInbox(
    int Version,
    string EstadoProcesamiento,
    long DocumentoRecibidoId,
    string TipoDocumento,
    long? DocumentoAsociadoId,
    string NombreArchivo,
    string MimeType,
    string RutaRelativa,
    long TamanoBytes,
    ComprobanteExtraido? Comprobante,
    IReadOnlyList<EvidenciaCampo> Evidencia,
    bool? AfectacionMixta,
    IReadOnlyList<string> CamposNoExtraidos,
    IReadOnlyList<string> AdvertenciasAsociacion);
