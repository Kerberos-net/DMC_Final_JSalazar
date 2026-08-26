using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.24/4.25 -- ADR 0012's same-origin requirement: no CORS middleware registered, no
/// Access-Control-* response headers ever emitted (design.md Decision 6, and confirmed by
/// omission in Program.cs -- there is deliberately no app.UseCors(...) call anywhere).
/// </summary>
public class NoCorsTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task CrossOriginPreflightRequest_ReceivesNoAccessControlHeaders()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/sesion");
        request.Headers.Add("Origin", "https://un-origen-distinto.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Headers"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task OrdinaryCrossOriginRequest_ReceivesNoAccessControlAllowOriginHeader()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sesion");
        request.Headers.Add("Origin", "https://un-origen-distinto.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
