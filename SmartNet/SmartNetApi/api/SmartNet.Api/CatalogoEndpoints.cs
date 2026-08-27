using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Catalogos.Core;

namespace SmartNet.Api;

/// <summary>
/// BACKLOG #18 PR8 — <c>api-catalogos-proveedores</c> (spec): one thin, authenticated, read-only
/// route delegating to <see cref="IProveedorRepository.BuscarAsync"/>. No accounting rule and no
/// SQL live here (ADR 0019); the query is a <c>SELECT</c> over <c>dbo.Proveedor</c> under the
/// existing <c>usr_api</c> grant only — no <c>dbo.*</c> write, no <c>fact.*</c> access, no new
/// grant or versioned SQL (CLAUDE.md rules 2 and 3, ADR 0003).
/// </summary>
public static class CatalogoEndpoints
{
    public static IEndpointRouteBuilder MapCatalogoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalogos/proveedores", (Delegate)BuscarProveedoresAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> BuscarProveedoresAsync(
        string? q, int? pagina, IProveedorRepository repositorio, CancellationToken ct)
    {
        var consulta = (q ?? string.Empty).Trim();
        var pag = pagina is > 0 ? pagina.Value : 1;

        var busqueda = await repositorio.BuscarAsync(consulta, pag, ct);

        var resultados = busqueda.Resultados
            .Select(p => new ProveedorResultado(p.Codigo, p.Nombre, p.Ruc))
            .ToArray();

        return Results.Ok(new BusquedaProveedoresRespuesta(resultados, busqueda.HayMas));
    }
}

/// <summary>Una fila del picker: <c>codigo</c> = <c>codpro</c>, <c>nombre</c> = <c>proveedor</c>,
/// <c>ruc</c> = <c>rucpro</c> (nullable).</summary>
internal sealed record ProveedorResultado(string Codigo, string Nombre, string? Ruc);

/// <summary>Cuerpo de <c>GET /api/catalogos/proveedores</c>: la página de resultados más
/// <c>hayMas</c> (¿existen más páginas para el mismo <c>q</c>?).</summary>
internal sealed record BusquedaProveedoresRespuesta(IReadOnlyList<ProveedorResultado> Resultados, bool HayMas);
