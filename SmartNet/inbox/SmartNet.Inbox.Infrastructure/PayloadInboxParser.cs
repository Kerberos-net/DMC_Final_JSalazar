using System.Globalization;
using System.Text.Json;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// Deserializes <c>fact.InboxEvent.Payload</c> (design.md Interfaces/Contracts JSON example,
/// `payload_inbox.py`'s exact output shape) into <see cref="EventoInbox"/>. JSON parsing lives
/// only here, never in <c>SmartNet.Inbox.Core</c> (design D9) -- Core sees only the already-built
/// record. <c>comprobante: null</c> is the <c>Estado='ERROR'</c> case (#6 never writes
/// <c>DatosExtraidos</c> for a failed document).
/// </summary>
public static class PayloadInboxParser
{
    public static EventoInbox Parse(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        var documento = root.GetProperty("documento");
        var comprobante = ParseComprobante(root.GetProperty("comprobante"));
        var evidencia = root.GetProperty("evidencia").EnumerateArray()
            .Select(e => new EvidenciaCampo(
                e.GetProperty("campo").GetString()!,
                e.GetProperty("valor").GetString()!,
                e.GetProperty("fuente").GetString()!))
            .ToList();

        return new EventoInbox(
            Version: root.GetProperty("version").GetInt32(),
            EstadoProcesamiento: root.GetProperty("estadoProcesamiento").GetString()!,
            DocumentoRecibidoId: documento.GetProperty("documentoRecibidoId").GetInt64(),
            TipoDocumento: documento.GetProperty("tipoDocumento").GetString()!,
            DocumentoAsociadoId: GetNullableInt64(documento, "documentoAsociadoId"),
            NombreArchivo: documento.GetProperty("nombreArchivo").GetString()!,
            MimeType: documento.GetProperty("mimeType").GetString()!,
            RutaRelativa: documento.GetProperty("rutaRelativa").GetString()!,
            TamanoBytes: documento.GetProperty("tamanoBytes").GetInt64(),
            Comprobante: comprobante,
            Evidencia: evidencia,
            AfectacionMixta: GetNullableBool(root, "afectacionMixta"),
            CamposNoExtraidos: GetStringList(root, "camposNoExtraidos"),
            AdvertenciasAsociacion: GetStringList(root, "advertenciasAsociacion"));
    }

    private static ComprobanteExtraido? ParseComprobante(JsonElement comprobanteElement)
    {
        if (comprobanteElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new ComprobanteExtraido(
            TipoComprobante: GetNullableString(comprobanteElement, "tipoComprobante"),
            Numero: GetNullableString(comprobanteElement, "numero"),
            RucProveedor: GetNullableString(comprobanteElement, "rucProveedor"),
            NombreProveedor: GetNullableString(comprobanteElement, "nombreProveedor"),
            Monto: GetNullableDecimal(comprobanteElement, "monto"),
            Moneda: GetNullableString(comprobanteElement, "moneda"),
            FechaEmision: GetNullableDate(comprobanteElement, "fechaEmision"));
    }

    private static IReadOnlyList<string> GetStringList(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).EnumerateArray().Select(e => e.GetString()!).ToList();

    private static string? GetNullableString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static long? GetNullableInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt64()
            : null;

    private static bool? GetNullableBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetBoolean()
            : null;

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // payload_inbox.py serializes `monto` as a string (`str(Decimal)`), never a JSON number, so
        // no precision is lost crossing the Python/.NET boundary (ADR 0019 level 2 contract).
        return decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    }

    private static DateOnly? GetNullableDate(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
}
