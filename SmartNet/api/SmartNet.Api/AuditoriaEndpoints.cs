using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 1 (PR 1) — <c>auditoria-correccion-lectura-api</c> (spec.md), design D7: una
/// única ruta thin, delegando en <see cref="IAuditoriaRepository"/>. Deliberadamente devuelve
/// <c>200 []</c> para un <c>facturaId</c> desconocido en vez de <c>404</c> (design D7): un 404 real
/// exigiría una consulta de existencia extra que la SPA no necesita -- solo llama esta ruta para un
/// id cuyo <c>GET /api/facturas/{id}</c> ya tuvo éxito.
/// </summary>
public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/facturas/{id:long}/historial", (Delegate)GetHistorialAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetHistorialAsync(long id, IAuditoriaRepository auditoria, CancellationToken ct)
    {
        var entradas = await auditoria.ListarPorFacturaAsync(id, ct);
        return Results.Ok(entradas.Select(EntradaAuditoriaRespuesta.De));
    }
}

/// <summary>Forma de respuesta de <c>GET /api/facturas/{id}/historial</c> (design Interfaces/
/// Contracts) -- newest-first, ya garantizado por <see cref="IAuditoriaRepository"/>.</summary>
internal sealed record EntradaAuditoriaRespuesta(
    string EntidadTipo, long EntidadId, string Accion, string? Campo,
    string? ValorOriginal, string? ValorNuevo, string? Motivo, long UsuarioId, DateTimeOffset OcurridoEn)
{
    public static EntradaAuditoriaRespuesta De(EntradaAuditoria entrada) => new(
        entrada.EntidadTipo, entrada.EntidadId, entrada.Accion, entrada.Campo,
        entrada.ValorOriginal, entrada.ValorNuevo, entrada.Motivo, entrada.UsuarioId, entrada.OcurridoEn);
}
