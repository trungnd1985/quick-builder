using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>Shared steps for tests that go through the real setup endpoints.</summary>
internal static class SetupHelpers
{
    public const string AdminEmail = "owner@example.test";

    /// <summary>
    /// The current setup code. Asking for the status first makes the application create the
    /// installation row, as the admin shell's first request would; the code is then read
    /// where the operator would find it printed.
    /// </summary>
    public static async Task<string> GetSetupCodeAsync(WebApplicationFactory<Program> app)
    {
        var client = IdentitySeed.CreateAnonymousClient(app);
        (await client.GetAsync(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(false))
            .EnsureSuccessStatusCode();

        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var installation = await database.Installation.AsNoTracking().SingleAsync().ConfigureAwait(false);

        return installation.SetupCode
            ?? throw new InvalidOperationException("The installation is already claimed; there is no setup code.");
    }

    public static Task<HttpResponseMessage> PostSetupAsync(
        HttpClient client,
        string? setupCode,
        string email = AdminEmail,
        string password = IdentitySeed.Password,
        string displayName = "Site Owner") =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/identity/setup", UriKind.Relative),
            new { setupCode, displayName, email, password });

    /// <summary>An anonymous client that appears to come from <paramref name="remoteAddress"/>.</summary>
    public static HttpClient ClientFrom(WebApplicationFactory<Program> app, string remoteAddress, bool https = true)
    {
        var client = https
            ? IdentitySeed.CreateAnonymousClient(app)
            : app.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                HandleCookies = false,
                AllowAutoRedirect = false,
            });

        if (!https)
        {
            client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        }

        client.DefaultRequestHeaders.Add(IdentitySeed.RemoteAddressHeader, remoteAddress);
        return client;
    }

    /// <summary>The value of one cookie a response set, or null.</summary>
    public static string? CookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var cookie = cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));
        return cookie?[(name.Length + 1)..].Split(';')[0];
    }

    public static string? SetCookieHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    public static async Task<int> CountUsersAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync().ConfigureAwait(false);
    }

    public static async Task<int> CountEventsAsync(WebApplicationFactory<Program> app, string eventType)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents.CountAsync(e => e.EventType == eventType).ConfigureAwait(false);
    }
}
