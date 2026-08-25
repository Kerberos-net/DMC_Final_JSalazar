namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 2 — espejo de <c>fact.AdjuntoManual</c> (design.md File Changes no describe un puerto de
/// almacenamiento de bytes: la subida/archivado a Drive es de otro ítem, ADR 0013). Este record
/// transporta METADATOS ya resueltos (nombre, ruta relativa, mime, tamaño) — el llamador (capa Api)
/// es responsable de dejar el archivo físico en <see cref="RutaRelativa"/> antes de llamar a
/// <see cref="ServicioDeFacturas.RegistrarAdjuntoAsync"/>; Core nunca toca el sistema de archivos.
/// </summary>
public sealed record AdjuntoManual(
    long AdjuntoManualId,
    long FacturaId,
    string NombreArchivo,
    string RutaRelativa,
    string MimeType,
    long TamanoBytes,
    long SubidoPorUsuarioId,
    DateTimeOffset SubidoEn,
    DateTimeOffset? EliminadoEn);
