using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// Thin adapter of ASP.NET Core's <see cref="ITicketStore"/> over <see cref="ISesionRepository"/>
/// (design.md Decision 4/5). The "key" this store hands back to the cookie middleware -- which is
/// what actually gets Data-Protection-wrapped into the <c>__Host-session</c> cookie -- IS the raw
/// 256-bit <see cref="ISessionTokenFactory"/> token, never a reference to the deserialized claims
/// principal. Only <see cref="TokenHashOf"/> ever touches the database as the lookup key
/// (<c>UQ_Sesion_TokenHash</c>); the serialized ticket itself lives in <c>fact.Sesion.Ticket</c>
/// purely so <see cref="RetrieveAsync"/> can reconstruct the principal server-side.
/// </summary>
public sealed class SqlSesionTicketStore : ITicketStore
{
    private readonly ISesionRepository _sesiones;
    private readonly ISessionTokenFactory _tokens;
    private readonly TimeProvider _timeProvider;

    public SqlSesionTicketStore(string connectionString)
        : this(new SqlSesionRepository(connectionString), new CsprngSessionTokenFactory(), TimeProvider.System)
    {
    }

    public SqlSesionTicketStore(ISesionRepository sesiones, ISessionTokenFactory tokens, TimeProvider timeProvider)
    {
        _sesiones = sesiones;
        _tokens = tokens;
        _timeProvider = timeProvider;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var (token, tokenHash) = _tokens.Create();
        var usuarioId = ExtractUsuarioId(ticket);
        var expiraEn = ticket.Properties.ExpiresUtc ?? _timeProvider.GetUtcNow().AddHours(8);
        var serialized = SerializeTicket(ticket);

        await _sesiones.CreateAsync(usuarioId, tokenHash, expiraEn, serialized, CancellationToken.None);

        // The key returned here is exactly the raw token -- it is what the cookie middleware
        // Data-Protection-wraps into __Host-session. No claims payload ever crosses that boundary.
        return token;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var expiraEn = ticket.Properties.ExpiresUtc ?? _timeProvider.GetUtcNow().AddHours(8);
        await _sesiones.RenewAsync(TokenHashOf(key), expiraEn, _timeProvider.GetUtcNow(), CancellationToken.None);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var activa = await _sesiones.FindActiveAsync(TokenHashOf(key), _timeProvider.GetUtcNow(), CancellationToken.None);
        if (activa is null)
        {
            return null;
        }

        var ticket = DeserializeTicket(activa.Ticket);
        // fact.Sesion.ExpiraEn is the authoritative freshness value (design.md Decision 4's
        // read query, "WHERE ... ExpiraEn > @ahora"): RenewAsync widens the COLUMN, not the
        // serialized blob (ISesionRepository.RenewAsync has no ticket parameter, by design.md
        // Decision 5's port shape). Override the deserialized ticket's ExpiresUtc with the
        // column's current value so a renewed session is never rejected downstream for carrying
        // a stale embedded expiry.
        ticket.Properties.ExpiresUtc = activa.ExpiraEn;
        return ticket;
    }

    public async Task RemoveAsync(string key) =>
        await _sesiones.RevokeAsync(TokenHashOf(key), MotivoRevocacion.CierreSesion, _timeProvider.GetUtcNow(), CancellationToken.None);

    // Exposed as instance methods (not static) so tests/callers never bypass the injected
    // ISessionTokenFactory's HashOf -- one seam, one implementation of "how a token becomes a
    // lookup hash", matching CsprngSessionTokenFactory's own contract.
    private string TokenHashOf(string token) => _tokens.HashOf(token);

    private static long ExtractUsuarioId(AuthenticationTicket ticket)
    {
        var claim = ticket.Principal.FindFirst("usuarioId")
            ?? throw new InvalidOperationException("AuthenticationTicket is missing the 'usuarioId' claim.");
        return long.Parse(claim.Value);
    }

    private static string SerializeTicket(AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        return Convert.ToBase64String(bytes);
    }

    private static AuthenticationTicket DeserializeTicket(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return TicketSerializer.Default.Deserialize(bytes)
            ?? throw new InvalidOperationException("Stored ticket payload could not be deserialized.");
    }
}
