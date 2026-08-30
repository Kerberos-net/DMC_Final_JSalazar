using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Catalogos.Core;
using SmartNet.Exportacion.Infrastructure;

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
        app.MapGet("/api/catalogos/plan-contable", (Delegate)ListarPlanContableAsync).RequireAuthorization();
        app.MapGet("/api/catalogos/plan-contable/exportacion", (Delegate)ExportarPlanContableAsync).RequireAuthorization();

        return app;
    }

    // BACKLOG #22 PR2 — api spec req 4: full plan in ONE response, no pagination. Filter and sort
    // are client-side (spec.md); this route always returns the whole plan ordered by cuenta.
    // EsHojaImputable is PROJECTED from the domain record (design D3), not recomputed here.
    private static async Task<IResult> ListarPlanContableAsync(
        ICuentaContableRepository repositorio, CancellationToken ct)
    {
        var plan = await repositorio.ListarPlanCompletoAsync(ct);

        var items = plan
            .OrderBy(c => c.Cuenta, StringComparer.Ordinal)
            .Select(c => new CuentaContableResultado(c.Cuenta, c.Descripcion, c.Nivel, c.EsHojaImputable))
            .ToArray();

        return Results.Ok(new PlanContableRespuesta(items));
    }

    // BACKLOG #22 PR2 — api spec req 6 / ADR 0021: real .xlsx of the FULL filtered set. The `q`
    // predicate mirrors the SPA's client-side "contains over cuenta|descripcion" (design D9, the
    // consequence that the predicate is expressed twice, asserted both sides). ADR 0021 decision 4:
    // no user input reaches Content-Disposition — the filename is a constant plus the server date
    // from the registered TimeProvider (the endpoint may read the clock; the core may not, ADR 0019).
    private static async Task<IResult> ExportarPlanContableAsync(
        string? q, ICuentaContableRepository repositorio, TimeProvider reloj, CancellationToken ct)
    {
        var plan = await repositorio.ListarPlanCompletoAsync(ct);
        var filtro = (q ?? string.Empty).Trim();

        var filas = plan
            .Where(c => filtro.Length == 0
                || c.Cuenta.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                || c.Descripcion.Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Cuenta, StringComparer.Ordinal)
            .Select(c => (IReadOnlyList<string>)new[]
            {
                c.Cuenta,
                c.Descripcion,
                c.Nivel?.ToString() ?? string.Empty,
                c.EsHojaImputable ? "Si" : "No",
            })
            .ToArray();

        using var buffer = new MemoryStream();
        ExportadorXlsx.Escribir(buffer, filas, new[] { "Cuenta", "Descripcion", "Nivel", "Es hoja imputable" });

        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        return Results.File(
            buffer.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: $"plan-contable-{hoy:yyyy-MM-dd}.xlsx");
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

/// <summary>Una fila del plan contable: <c>cuenta</c> = <c>cuenta</c>, <c>descripcion</c> =
/// <c>descripcion</c>, <c>nivel</c> = <c>nivel</c> (nullable), <c>esHojaImputable</c> proyectado
/// del dominio (<c>nivel IS NULL</c>), no recalculado en el endpoint (design D3).</summary>
internal sealed record CuentaContableResultado(string Cuenta, string Descripcion, byte? Nivel, bool EsHojaImputable);

/// <summary>Cuerpo de <c>GET /api/catalogos/plan-contable</c>: el plan completo, sin paginar
/// (api spec req 4). Filtro y orden son del lado del cliente.</summary>
internal sealed record PlanContableRespuesta(IReadOnlyList<CuentaContableResultado> Items);
