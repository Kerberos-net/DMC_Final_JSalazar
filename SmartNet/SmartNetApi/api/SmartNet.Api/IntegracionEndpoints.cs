using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 4 (PR 4) — <c>api-incidencias-integraciones</c> (spec.md, design D7): four thin
/// routes delegating to <see cref="ServicioDeIntegraciones"/>, all "enqueue-only" per ADR 0003/0004
/// (.NET NEVER calls Python directly) except the read-only estado projection. None of the three
/// enqueue commands is in the <c>Accion</c> enum (design D6/proposal ratified answer #2) — no
/// <c>AuditoriaCorreccion</c> row is ever written here.
/// </summary>
public static class IntegracionEndpoints
{
    public static IEndpointRouteBuilder MapIntegracionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/incidencias/{id:long}/reprocesar", (Delegate)ReprocesarAsync).RequireAuthorization();
        app.MapPost("/api/integraciones/google/reconectar", (Delegate)ReconectarGoogleAsync).RequireAuthorization();
        app.MapPost("/api/integraciones/{nombre}/sincronizar", (Delegate)SincronizarAsync).RequireAuthorization();
        app.MapGet("/api/integraciones/estado", (Delegate)ObtenerEstadoAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ReprocesarAsync(long id, ServicioDeIntegraciones servicio, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        await servicio.EncolarAsync("REPROCESAR_DOCUMENTO", id, "{}", correlationId, ct);
        return Results.Accepted(value: new EnqueuedRespuesta(correlationId));
    }

    /// <summary>design D7 -- <c>{nombre}</c> se traduce a un <c>Tipo</c> de la lista blanca
    /// (<c>SINCRONIZAR_GMAIL</c>/<c>SINCRONIZAR_SBS</c>); cualquier otro nombre es 404, nunca se
    /// encola nada.</summary>
    private static async Task<IResult> SincronizarAsync(
        string nombre, ServicioDeIntegraciones servicio, CancellationToken ct)
    {
        var tipo = nombre.ToLowerInvariant() switch
        {
            "gmail" => "SINCRONIZAR_GMAIL",
            "sbs" => "SINCRONIZAR_SBS",
            _ => null,
        };

        if (tipo is null)
        {
            return Results.NotFound();
        }

        var correlationId = Guid.NewGuid();
        await servicio.EncolarAsync(tipo, referencia: null, "{}", correlationId, ct);
        return Results.Accepted(value: new EnqueuedRespuesta(correlationId));
    }

    private static async Task<IResult> ReconectarGoogleAsync(ServicioDeIntegraciones servicio, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        await servicio.EncolarAsync("RECONECTAR_GOOGLE", referencia: null, "{}", correlationId, ct);
        return Results.Accepted(value: new EnqueuedRespuesta(correlationId));
    }

    /// <summary>spec.md "GET /api/integraciones/estado deriva el pill, nunca lo almacena": la
    /// derivación Conectado/Con error vive SOLO aquí (SmartNet.Api), nunca en
    /// <see cref="EstadoIntegracion"/> (ese tipo transporta solo los hechos crudos).</summary>
    private static async Task<IResult> ObtenerEstadoAsync(ServicioDeIntegraciones servicio, CancellationToken ct)
    {
        var estados = await servicio.ObtenerEstadoAsync(ct);
        return Results.Ok(estados.Select(IntegracionEstadoRespuesta.De).ToArray());
    }
}

/// <summary>Cuerpo de respuesta 202 de las tres rutas "enqueue-only" (design D7).</summary>
internal sealed record EnqueuedRespuesta(Guid CorrelationId);

/// <summary>Forma de respuesta de <c>GET /api/integraciones/estado</c> — la "pill" derivada
/// (<c>Estado</c>) va junto a los hechos crudos que la sustentan.</summary>
internal sealed record IntegracionEstadoRespuesta(
    string Nombre, string Estado, DateTimeOffset? UltimaEjecucion, DateTimeOffset? UltimoExito,
    string? UltimoError, int FallosConsecutivos)
{
    public static IntegracionEstadoRespuesta De(EstadoIntegracion estado) => new(
        estado.Nombre,
        estado.FallosConsecutivos > 0 ? "Con error" : "Conectado",
        estado.UltimaEjecucion, estado.UltimoExito, estado.UltimoError, estado.FallosConsecutivos);
}
