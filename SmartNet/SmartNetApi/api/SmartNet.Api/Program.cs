using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using SmartNet.Api;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;
using SmartNet.Catalogos.Core;
using SmartNet.Catalogos.Infrastructure;
using SmartNet.Facturacion.Core;
using SmartNet.Facturacion.Infrastructure;
using SmartNet.Inbox.Core;
using SmartNet.Inbox.Infrastructure;
using SmartNet.TiposCambio.Core;
using SmartNet.TiposCambio.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// TimeProvider.System by default; SmartNet.Api.Tests substitutes a FakeTimeProvider via
// ConfigureTestServices to drive the lockout escalation sequence without real waiting.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<ISessionTokenFactory, CsprngSessionTokenFactory>();

// Resolved LAZILY from the DI-injected IConfiguration, not from `builder.Configuration` eagerly:
// WebApplicationFactory<Program> (SmartNet.Api.Tests) overrides configuration by adding an extra
// source during `builder.Build()`'s interception -- code that runs BEFORE `Build()` (as this line
// textually does) would observe the PRE-override configuration under the test host. Resolving
// inside the factory delegate defers evaluation to first use, after `Build()` has already merged
// the test's override in.
builder.Services.AddSingleton<IUsuarioRepository>(sp =>
    new SqlUsuarioRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<ISesionRepository>(sp =>
    new SqlSesionRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// BACKLOG #7 WU4 composition root: the three SmartNet.Inbox.Infrastructure (WU3) adapters behind
// SmartNet.Inbox.Core's (WU2) ports, resolved the same lazy way as the auth repos above --
// deferred to first use, after Build() has already merged any WebApplicationFactory override in
// (SmartNet.Api.Tests.BandejaEndpointsTests, task 4.7).
builder.Services.AddSingleton<IEventoInboxRepository>(sp =>
    new SqlEventoInboxRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IPromocionRepository>(sp =>
    new SqlPromocionRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IBandejaRepository>(sp =>
    new SqlBandejaRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// BACKLOG #11 Phase 2 composition root: SmartNet.Facturacion.Infrastructure (PR 1) behind
// SmartNet.Facturacion.Core's ports (design D8) -- same lazy-resolution pattern as every repo
// above. ServicioDeFacturas is AddScoped per design D8 (it holds no per-request state; Scoped
// simply matches the documented decision instead of Singleton like the plain repos).
//
// PR 5 (Phase 5, SinTipoCambio gap closure): resolves the SAME ITipoCambioRepository singleton
// registered below for TipoCambioEndpoints (sp.GetRequiredService works regardless of
// registration order -- DI factories run lazily on first resolution), instead of constructing a
// second SqlTipoCambioRepository instance over the same connection string.
builder.Services.AddSingleton<IFacturacionStore>(sp =>
    new SqlFacturacionStore(
        ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>()),
        sp.GetRequiredService<ITipoCambioRepository>()));
builder.Services.AddScoped<ServicioDeFacturas>();

// BACKLOG #11 Phase 3 (PR 3) composition root: ServicioDeAsientos over the same IFacturacionStore
// above -- AddScoped for the same reason as ServicioDeFacturas (design D8, no per-request state).
builder.Services.AddScoped<ServicioDeAsientos>();

// BACKLOG #11 Phase 4 (PR 4) composition root: POST /api/tipos-cambio is a thin HTTP wrapper over
// item #4's existing SqlTipoCambioRepository -- same lazy-resolution pattern as every repo above,
// registered directly (no IUnidadDeTrabajo involvement: fact.TipoCambio's own composite PK is the
// only concurrency guard this route needs, design TipoCambioEndpoints.cs comment).
builder.Services.AddSingleton<ITipoCambioRepository>(sp =>
    new SqlTipoCambioRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// BACKLOG #11 Phase 4 composition root: ServicioDeIntegraciones (Core, PR 1) over the
// ICommandQueueRepository/IEstadoIntegracionRepository adapters (Infrastructure, PR 1) -- design D7
// "sincronizar/reconectar/reprocesar enqueue only". ServicioDeIntegraciones is AddScoped for the
// same reason as ServicioDeFacturas/ServicioDeAsientos above (design D8, no per-request state).
builder.Services.AddSingleton<ICommandQueueRepository>(sp =>
    new SqlCommandQueueRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IEstadoIntegracionRepository>(sp =>
    new SqlEstadoIntegracionRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddScoped<ServicioDeIntegraciones>();

// diseno-visual-spa-item-12 (BACKLOG #12 reabierto) composition root: design D7 -- historial de
// corrección es un read-only dedicado, no un miembro de IUnidadDeTrabajo -- misma forma lazy que
// IEstadoIntegracionRepository arriba.
builder.Services.AddSingleton<IAuditoriaRepository>(sp =>
    new SqlAuditoriaRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// BACKLOG #17 Phase 5 composition root: configuracion-api-spa (design D6) -- same lazy-resolution
// pattern as every repo above. IConfiguracionRepository holds no per-request state (plain
// GET/PUT over fact.Configuracion), so it is a Singleton like the read-only/read-write repos
// above, not Scoped like the *Servicio* facades.
builder.Services.AddSingleton<IConfiguracionRepository>(sp =>
    new SqlConfiguracionRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// BACKLOG #18 PR8 composition root: api-catalogos-proveedores — read-only proveedor search over
// dbo.Proveedor (ADR 0003 external catalog). Same lazy-resolution pattern as every repo above; a
// plain Singleton like the other read-only catalog/config repos, not Scoped like the *Servicio*
// facades.
builder.Services.AddSingleton<IProveedorRepository>(sp =>
    new SqlProveedorRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));

// design D7: PeriodicTimer(1 min) with the DI-registered TimeProvider.System above -- so a test
// that substitutes a FakeTimeProvider via SmartNetApiFactory could drive it deterministically the
// same way SmartNet.Inbox.Infrastructure.Tests.PromocionBackgroundServiceTests already does for
// the standalone service.
builder.Services.AddHostedService<PromocionBackgroundService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

// ADR 0007 Revisión 4 / spec.md's cookie-attribute scenario, exactly (task 4.6/4.7).
builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ISesionRepository, ISessionTokenFactory, TimeProvider>(
        (options, sesiones, tokens, timeProvider) =>
        {
            options.Cookie.Name = "__Host-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.TimeProvider = timeProvider;

            // Session state is a real server-side store (design.md, top of file): ADR 0007's
            // "el cierre de sesión invalida la sesión del lado del servidor" becomes structural.
            options.SessionStore = new SqlSesionTicketStore(sesiones, tokens, timeProvider);

            // An API, not a browser page: never redirect to a login page on 401 -- return the
            // plain status code (spec.md, GET /api/sesion "200 { nombreUsuario } | 401").
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });

builder.Services.AddAuthorization();

// design.md Decision 4, gate task 0.2 (CLOSED): the key ring must be persisted outside the
// process, or a host restart invalidates every live session cookie -- silently defeating the
// reason fact.Sesion was chosen over an in-memory store (task 4.8's gate check, task 4.9's test).
// Same lazy-resolution reasoning as the repositories above: KeyManagementOptions is resolved by
// the Data Protection system on first use (after Build()), not eagerly here.
builder.Services.AddDataProtection();
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IConfiguration, ILoggerFactory>((options, configuration, loggerFactory) =>
    {
        var keyRingPath = ApiKeyRingOptions.Resolve(configuration);
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keyRingPath), loggerFactory);
    });

var app = builder.Build();

// Fail loudly at startup if required configuration is absent -- checked against `app.Configuration`
// (post-Build, reflecting any WebApplicationFactory override), never a silent default.
_ = ApiConnectionOptions.Resolve(app.Configuration);
_ = ApiKeyRingOptions.Resolve(app.Configuration);
_ = DocumentoStorageOptions.Resolve(app.Configuration);

// design.md Decision 6 / ADR 0012: same-origin behind the reverse proxy is a precondition, not
// an assumption -- there is deliberately no app.UseCors(...) call anywhere in this file
// (task 4.24/4.25).
app.UseAuthentication();
app.UseAuthorization();

app.MapSesionEndpoints();
app.MapBandejaEndpoints();
app.MapFacturaEndpoints();
app.MapAsientoEndpoints();
app.MapTipoCambioEndpoints();
app.MapIntegracionEndpoints();
app.MapDocumentoEndpoints();
app.MapAuditoriaEndpoints();
app.MapConfiguracionEndpoints();
app.MapCatalogoEndpoints();

app.Run();

// Exposes the top-level Program class to WebApplicationFactory<Program> in SmartNet.Api.Tests
// (standard ASP.NET Core testing pattern).
public partial class Program;
