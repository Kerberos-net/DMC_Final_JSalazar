using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Inbox.Core;

namespace SmartNet.Api;

/// <summary>
/// <c>GET /api/bandeja</c> (design D6 -- reuses ADR 0008's contract, #7-shaped, widened by #13
/// later). Thin -- ADR 0019's demand of the API host: sequence only, every decision delegated to
/// <see cref="IBandejaRepository"/> (<c>SqlBandejaRepository</c>, WU3); never a second query
/// surface over the same data.
/// </summary>
public static class BandejaEndpoints
{
    public static IEndpointRouteBuilder MapBandejaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bandeja", (Delegate)GetBandejaAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetBandejaAsync(
        string? estado,
        string? orden,
        IBandejaRepository bandeja,
        CancellationToken ct)
    {
        var items = await bandeja.ListarAsync(estado, orden ?? "desc", ct);
        return Results.Ok(items);
    }
}
