using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using SmartNet.Api;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

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

// design.md Decision 6 / ADR 0012: same-origin behind the reverse proxy is a precondition, not
// an assumption -- there is deliberately no app.UseCors(...) call anywhere in this file
// (task 4.24/4.25).
app.UseAuthentication();
app.UseAuthorization();

app.MapSesionEndpoints();

app.Run();

// Exposes the top-level Program class to WebApplicationFactory<Program> in SmartNet.Api.Tests
// (standard ASP.NET Core testing pattern).
public partial class Program;
