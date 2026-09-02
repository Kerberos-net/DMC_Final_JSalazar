namespace SmartNet.Inbox.Core;

/// <summary>
/// BACKLOG #24 (design C2) — puerto que la tubería de promoción usa para sembrar el asiento
/// BORRADOR de una factura recién promovida, SIN que <c>SmartNet.Inbox.Infrastructure</c> tome una
/// dependencia sobre el módulo de facturación (sólo referencia <c>SmartNet.Inbox.Core</c>). El
/// adaptador vive en <c>SmartNet.Api</c> y delega en <c>ServicioDeFacturas.AbrirAsync</c>.
///
/// La implementación NUNCA lanza (design C3, owner decision): una factura en moneda extranjera sin
/// tipo de cambio vigente — o una factura que ya no existe — se deja sin asiento y la promoción
/// igual tiene éxito; el botón "generar asiento" de la SPA es el reintento. Una excepción abortaría
/// el <c>foreach</c> de <c>ProcesarPendientesAsync</c> y dejaría varados los eventos pendientes
/// restantes.
/// </summary>
public interface ISembradorDeAsiento
{
    Task SembrarAsync(long facturaId, CancellationToken ct);
}
