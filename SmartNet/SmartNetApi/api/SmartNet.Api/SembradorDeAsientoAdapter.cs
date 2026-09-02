using SmartNet.Facturacion.Core;
using SmartNet.Inbox.Core;

namespace SmartNet.Api;

/// <summary>
/// BACKLOG #24 (design C2/C3) — adaptador de <see cref="ISembradorDeAsiento"/> (puerto en
/// <c>SmartNet.Inbox.Core</c>) sobre <see cref="ServicioDeFacturas.AbrirAsync"/>. Vive aquí, en el
/// host, para que <c>SmartNet.Inbox.Infrastructure</c> nunca referencie el módulo de facturación.
///
/// <see cref="ServicioDeFacturas"/> está registrado <c>AddScoped</c>; este adaptador lo consume un
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> singleton, así que abre su propio
/// scope por invocación.
///
/// Traga (design C3) <see cref="CasoConflicto.SinTipoCambio"/> y <see cref="ResultadoComando.NoEncontrado"/>:
/// la promoción ya está confirmada y una siembra fallida no debe abortar el ciclo ni revertirla.
/// El botón "generar asiento" de la SPA es el reintento.
/// </summary>
internal sealed class SembradorDeAsientoAdapter : ISembradorDeAsiento
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SembradorDeAsientoAdapter> _logger;

    public SembradorDeAsientoAdapter(IServiceScopeFactory scopeFactory, ILogger<SembradorDeAsientoAdapter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task SembrarAsync(long facturaId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ServicioDeFacturas>();

        var resultado = await servicio.AbrirAsync(facturaId, ct);

        switch (resultado)
        {
            case ResultadoComando.Aplicado:
                return;
            case ResultadoComando.Conflicto conflicto when conflicto.Caso == CasoConflicto.SinTipoCambio:
                _logger.LogInformation(
                    "Promoción de factura {FacturaId}: asiento no sembrado por falta de tipo de cambio vigente; " +
                    "la factura queda sin asiento hasta 'generar asiento'.",
                    facturaId);
                return;
            case ResultadoComando.NoEncontrado:
                _logger.LogWarning(
                    "Promoción de factura {FacturaId}: la factura no se encontró al sembrar el asiento.", facturaId);
                return;
            default:
                _logger.LogWarning(
                    "Promoción de factura {FacturaId}: resultado inesperado al sembrar el asiento ({Resultado}); " +
                    "la promoción no se revierte.",
                    facturaId, resultado.GetType().Name);
                return;
        }
    }
}
