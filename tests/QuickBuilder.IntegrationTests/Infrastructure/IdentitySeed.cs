using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// Puts accounts, grants and sessions straight into the database (002 research R-18).
/// </summary>
/// <remarks>
/// Tests about sites, the outbox or logging need a signed-in caller, not a trip through the
/// setup wizard. Seeding directly keeps each user story's tests independent: US3 and US4
/// tests do not depend on US1's wizard being implemented or correct. Tests <em>about</em>
/// setup and sign-in use the real endpoints instead.
/// </remarks>
internal static class IdentitySeed
{
    /// <summary>Long enough, and absent from the common-password list.</summary>
    public const string Password = "integration-test passphrase 7c1e";

    /// <summary>The header the test pipeline reads to pretend to be another client.</summary>
    public const string RemoteAddressHeader = "X-Test-Remote-Address";

    private static int _counter;

    /// <summary>Marks the installation claimed, as completing setup would.</summary>
    public static async Task ClaimInstallationAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var installation = await database.Installation.FirstOrDefaultAsync().ConfigureAwait(false);
        if (installation is null)
        {
            installation = Installation.CreateUnclaimed(Tokens.NewSetupCode(), now);
            database.Installation.Add(installation);
        }

        if (!installation.IsClaimed)
        {
            installation.Claim(now);
            await database.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Adds an active user with the given grants and, by default, a password.</summary>
    public static async Task<User> SeedUserAsync(
        IServiceProvider services,
        string? email = null,
        bool administrator = false,
        IEnumerable<(Guid SiteId, SystemRole Role)>? siteRoles = null,
        string? password = Password,
        string displayName = "Test User")
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Pbkdf2PasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        email ??= $"user{Interlocked.Increment(ref _counter)}-{Guid.NewGuid():N}@example.test";
        var user = User.Create(displayName, email, now).Value;
        if (password is not null)
        {
            user.SetPasswordHash(hasher.Hash(password), now);
        }

        database.Users.Add(user);

        if (administrator)
        {
            database.RoleGrants.Add(RoleGrant.Administrator(user.Id, grantedByUserId: null, now));
        }

        foreach (var (siteId, role) in siteRoles ?? [])
        {
            database.RoleGrants.Add(RoleGrant.ForSite(user.Id, siteId, role, grantedByUserId: null, now));
        }

        await database.SaveChangesAsync().ConfigureAwait(false);
        return user;
    }

    /// <summary>
    /// A client carrying a fresh session for <paramref name="userId"/> and the header every
    /// state-changing admin request needs (research R-11).
    /// </summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(
        WebApplicationFactory<Program> app,
        Guid userId,
        string? hostName = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var token = Tokens.NewSessionToken();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            database.Sessions.Add(Session.Start(userId, Tokens.Hash(token), DateTimeOffset.UtcNow, TimeSpan.FromHours(12), null));
            await database.SaveChangesAsync().ConfigureAwait(false);
        }

        var client = CreateAnonymousClient(app, hostName);
        client.DefaultRequestHeaders.Add("Cookie", $"{IdentityCookies.Session}={token}");
        return client;
    }

    /// <summary>Claims the installation, seeds an administrator, and signs them in.</summary>
    public static async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> app, string? hostName = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        await ClaimInstallationAsync(app.Services).ConfigureAwait(false);
        var admin = await SeedUserAsync(app.Services, administrator: true, displayName: "Test Administrator").ConfigureAwait(false);
        return await CreateSignedInClientAsync(app, admin.Id, hostName).ConfigureAwait(false);
    }

    /// <summary>
    /// A client with no session, over HTTPS (research R-18), sending the request header
    /// state-changing admin calls need.
    /// </summary>
    public static HttpClient CreateAnonymousClient(WebApplicationFactory<Program> app, string? hostName = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        if (hostName is not null)
        {
            client.DefaultRequestHeaders.Host = hostName;
        }

        return client;
    }

    /// <summary>
    /// Lets a test choose the client address a request appears to come from, by sending
    /// <see cref="RemoteAddressHeader"/>. Registered only in test hosts.
    /// </summary>
    public static void UseTestRemoteAddress(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestRemoteAddressStartupFilter>());
    }

    private sealed class TestRemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteAddressHeader, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });

            next(app);
        };
    }
}
