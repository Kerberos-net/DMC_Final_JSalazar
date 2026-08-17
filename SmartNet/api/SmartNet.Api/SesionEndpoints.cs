using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

namespace SmartNet.Api;

/// <summary>
/// <c>POST</c>/<c>DELETE</c>/<c>GET /api/sesion</c> (design.md Decision 6, Data Flow). Thin —
/// ADR 0019's demand of the API host: sequence only, all decisions delegated to
/// <see cref="AccessPolicy"/> and the injected ports.
/// </summary>
public static class SesionEndpoints
{
    public static IEndpointRouteBuilder MapSesionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sesion", (Delegate)PostSesionAsync);
        app.MapDelete("/api/sesion", (Delegate)DeleteSesionAsync).RequireAuthorization();
        app.MapGet("/api/sesion", (Delegate)GetSesion).RequireAuthorization();

        return app;
    }

    // design.md Data Flow: Evaluate -> decoy-or-real Verify -> ApplyFailure/ApplySuccess -> repos.
    private static async Task<IResult> PostSesionAsync(
        LoginRequest request,
        IUsuarioRepository usuarios,
        IPasswordHasher hasher,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken ct)
    {
        var ahora = timeProvider.GetUtcNow();
        var estado = await usuarios.FindByNameAsync(request.NombreUsuario, ct);

        if (estado is null)
        {
            // Login sequence step 1: run ONE real decoy Argon2id verification, same parameters as
            // a real hash, so an unknown username costs the same wall-clock time as a wrong
            // password against a real account -- the username-enumeration timing defense
            // (task 4.14: mechanism, not a raw stopwatch comparison).
            hasher.Verify(request.Clave, Argon2idPasswordHasher.DecoyHash);
            return SesionResultados.CredencialesInvalidas();
        }

        if (AccessPolicy.Evaluate(estado, ahora) == AccessDecision.Locked)
        {
            // Login sequence step 2: rejected BEFORE any hash is computed. No IPasswordHasher
            // call of any kind happens on this path -- task 4.16's exact assertion.
            return SesionResultados.CredencialesInvalidas();
        }

        var verificacion = hasher.Verify(request.Clave, estado.ClaveHash);
        if (verificacion != PasswordVerification.Correct)
        {
            var actualizado = AccessPolicy.ApplyFailure(estado, LockoutPolicy.Adr0007, ahora);
            await usuarios.SaveCredentialStateAsync(actualizado, ct);
            return SesionResultados.CredencialesInvalidas();
        }

        var exitoso = AccessPolicy.ApplySuccess(estado);
        await usuarios.SaveCredentialStateAsync(exitoso, ct);

        var claims = new[]
        {
            new Claim("usuarioId", estado.UsuarioId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, estado.NombreUsuario),
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = ahora.AddHours(8) });

        return Results.NoContent();
    }

    // design.md Data Flow: DELETE /api/sesion -> ITicketStore.RemoveAsync -> UPDATE
    // RevocadaEn/MotivoRevocacion='CIERRE_SESION'. SignOutAsync drives the cookie handler's
    // configured SessionStore, so the revocation happens through the same seam every
    // authenticated request reads through -- no separate revoke code path to drift from it.
    private static async Task<IResult> DeleteSesionAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    // spec.md: 200 { nombreUsuario } when authenticated, 401 otherwise (RequireAuthorization
    // handles the 401 via the cookie handler's OnRedirectToLogin override, Program.cs).
    private static IResult GetSesion(HttpContext http) =>
        Results.Ok(new { nombreUsuario = http.User.Identity!.Name });
}
