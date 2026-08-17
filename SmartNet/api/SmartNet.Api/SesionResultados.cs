using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SmartNet.Api;

/// <summary>
/// The generic <c>401 application/problem+json</c> response every login failure returns —
/// unknown user, wrong password, locked account (design.md Decision 6: "Every login failure ...
/// returns the identical 401 problem document"; task 4.26). A single static factory with fixed
/// field values, no timestamp/trace-id extension, guarantees byte-for-byte identical bodies
/// across all three call sites — the property this design decision depends on.
/// </summary>
internal static class SesionResultados
{
    private const string TipoProblema = "https://smartnet.local/problemas/credenciales-invalidas";
    private const string Titulo = "Credenciales inválidas";
    private const string Detalle = "El nombre de usuario o la contraseña no son válidos.";

    public static JsonHttpResult<ProblemaCredenciales> CredencialesInvalidas() =>
        TypedResults.Json(
            new ProblemaCredenciales(TipoProblema, Titulo, StatusCodes.Status401Unauthorized, Detalle),
            statusCode: StatusCodes.Status401Unauthorized,
            contentType: "application/problem+json");
}

/// <summary>
/// RFC 7807 shape, deliberately minimal — no <c>instance</c>, no extension members — so
/// serialization is fully deterministic for the byte-identical-body requirement (task 4.26).
/// </summary>
internal sealed record ProblemaCredenciales(string Type, string Title, int Status, string Detail);
