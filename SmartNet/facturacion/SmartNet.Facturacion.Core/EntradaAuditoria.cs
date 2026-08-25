namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D6 — una fila de <c>fact.AuditoriaCorreccion</c>. Solo siete valores de <see cref="Accion"/>
/// existen (<see cref="Acciones"/>); <c>abrir</c>/<c>sincronizar</c>/<c>reconectar</c>/<c>reprocesar</c>
/// nunca construyen esta clase — no están en el enum de la tabla (CK_AuditoriaCorreccion_Accion).
/// <see cref="OcurridoEn"/> se recibe como parámetro (ADR 0019: Core nunca llama DateTime.UtcNow).
/// </summary>
public sealed record EntradaAuditoria(
    string EntidadTipo,
    long EntidadId,
    string Accion,
    string? Campo,
    string? ValorOriginal,
    string? ValorNuevo,
    string? Motivo,
    long UsuarioId,
    DateTimeOffset OcurridoEn)
{
    public static class EntidadTipos
    {
        public const string Factura = "FACTURA";
        public const string Asiento = "ASIENTO";
        public const string Adjunto = "ADJUNTO";
    }

    /// <summary>design D6 — los siete valores exactos de <c>CK_AuditoriaCorreccion_Accion</c>.</summary>
    public static class Acciones
    {
        public const string Correccion = "CORRECCION";
        public const string Reapertura = "REAPERTURA";
        public const string Anulacion = "ANULACION";
        public const string TrasladoPeriodo = "TRASLADO_PERIODO";
        public const string ConfirmacionAfectacion = "CONFIRMACION_AFECTACION";
        public const string EliminacionAdjunto = "ELIMINACION_ADJUNTO";
        public const string RepartoManual = "REPARTO_MANUAL";
    }
}
