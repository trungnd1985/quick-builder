using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// Proves site data stays inside its site (FR-013, FR-013a, SC-011).
/// </summary>
/// <remarks>
/// The failure this guards against is the quietest one in the codebase. An entity that
/// loses its query filter does not throw, log, or fail a build — it returns another site's
/// rows, and the software looks like it is working right up until a customer sees data
/// that is not theirs.
/// <para>
/// Two sites are configured for every test here, because a scoping bug is invisible with
/// one: every query returns the only site's data and passes.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "SiteScoping")]
public sealed class SiteScopingTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private Guid _alphaId;
    private Guid _betaId;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("scoping").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;

        _alphaId = await CreateSiteAsync("Alpha", "alpha.test").ConfigureAwait(true);
        _betaId = await CreateSiteAsync("Beta", "beta.test").ConfigureAwait(true);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// FR-013: with a site resolved, a site-owned query sees only that site's rows.
    /// </summary>
    /// <remarks>
    /// Queries the host names table directly rather than through HTTP, because the filter
    /// is what is under test — an endpoint that happens not to expose another site's data
    /// would pass while the filter itself was broken.
    /// </remarks>
    [Fact]
    public async Task Site_owned_reads_see_only_the_current_site()
    {
        foreach (var (siteId, expected) in new[] { (_alphaId, "alpha.test"), (_betaId, "beta.test") })
        {
            using var scope = _app.Services.CreateScope();

            // Resolve the site exactly as the public-surface middleware would.
            scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(siteId);

            var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var visible = await context.HostNames
                .AsNoTracking()
                .Select(h => h.HostName)
                .ToListAsync()
                .ConfigureAwait(true);

            Assert.Equal([expected], visible);
        }
    }

    /// <summary>
    /// The filter fails closed when no site is resolved.
    /// </summary>
    /// <remarks>
    /// This is the direction that matters. If an unresolved site matched everything rather
    /// than nothing, every administrative request — which deliberately has no site —
    /// would quietly see every site's data, and the scoping would be worse than useless.
    /// </remarks>
    [Fact]
    public async Task Site_owned_reads_return_nothing_when_no_site_is_resolved()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        var visible = await context.HostNames.AsNoTracking().CountAsync().ConfigureAwait(true);

        Assert.Equal(0, visible);
    }

    /// <summary>
    /// FR-013a: the admin list crosses sites, and does so through the annotated helper.
    /// </summary>
    /// <remarks>
    /// The dedicated test FR-013a asks for. Listing every site is a legitimate bypass —
    /// it is the whole point of the screen — and this asserts the bypass works while the
    /// previous test asserts nothing else gets it by accident.
    /// </remarks>
    [Fact]
    public async Task Admin_site_list_deliberately_crosses_sites()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var response = await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var sites = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        var names = sites.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alpha", names);
        Assert.Contains("Beta", names);
    }

    /// <summary>
    /// 002 FR-022: the same deliberate bypass, narrowed to the caller — a user with a role
    /// on Alpha only gets Alpha, even though the query itself crosses sites.
    /// </summary>
    [Fact]
    public async Task Site_list_bypass_is_narrowed_to_the_callers_sites()
    {
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);
        var designer = await IdentitySeed.SeedUserAsync(
            _app.Services,
            siteRoles: [(_alphaId, QuickBuilder.Shared.Kernel.Authorization.SystemRole.Designer)]).ConfigureAwait(true);
        var client = await IdentitySeed.CreateSignedInClientAsync(_app, designer.Id).ConfigureAwait(true);

        var sites = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal([_alphaId], sites.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// A public request reaches only the site its host name names.
    /// </summary>
    /// <remarks>
    /// The end-to-end version of the first test: resolution and filtering together, over
    /// HTTP, as a visitor would experience them.
    /// </remarks>
    [Fact]
    public async Task Public_requests_resolve_to_their_own_site_only()
    {
        var alpha = await ResolvePublicSiteAsync("alpha.test").ConfigureAwait(true);
        var beta = await ResolvePublicSiteAsync("beta.test").ConfigureAwait(true);

        Assert.Equal(_alphaId, alpha);
        Assert.Equal(_betaId, beta);
        Assert.NotEqual(alpha, beta);
    }

    /// <summary>
    /// Every cross-site bypass in production code carries a written reason (FR-013a).
    /// </summary>
    /// <remarks>
    /// The compiler already guarantees the bypass can only happen through
    /// <c>AcrossAllSites</c> — <c>IgnoreQueryFilters</c> is banned everywhere else and the
    /// build fails on it. What the compiler cannot check is whether the reason a caller
    /// supplies is meaningful, so this asserts every call site passes a non-trivial string
    /// rather than an empty one to satisfy the signature.
    /// </remarks>
    [Fact]
    public async Task Every_cross_site_bypass_states_a_reason()
    {
        var moduleRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Modules", "Sites", "QuickBuilder.Sites");

        var callSites = new List<(string File, string Line)>();

        foreach (var file in Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lines = await File.ReadAllLinesAsync(file).ConfigureAwait(true);

            foreach (var line in lines.Where(l => l.Contains("AcrossAllSites(", StringComparison.Ordinal)))
            {
                callSites.Add((Path.GetFileName(file), line.Trim()));
            }
        }

        Assert.NotEmpty(callSites);

        foreach (var (file, line) in callSites)
        {
            // A reason must be a real sentence, not "" or "x" passed to get past the
            // argument check.
            var reason = line[(line.IndexOf("AcrossAllSites(", StringComparison.Ordinal) + 15)..];
            var quoted = reason.IndexOf('"', StringComparison.Ordinal);

            Assert.True(quoted >= 0, $"{file}: cross-site read has no literal reason — {line}");

            var closing = reason.IndexOf('"', quoted + 1);
            var text = closing > quoted ? reason[(quoted + 1)..closing] : string.Empty;

            Assert.True(
                text.Length >= 20 && text.Contains(' ', StringComparison.Ordinal),
                $"{file}: cross-site reason is not a meaningful explanation — \"{text}\"");
        }
    }

    private async Task<Guid> CreateSiteAsync(string name, string hostName)
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var response = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name, hostNames = new[] { hostName } })
            .ConfigureAwait(true);

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var siteId = created.GetProperty("id").GetGuid();

        // The site serves its own id at /, so a public response shows which site answered.
        await PublicSite.MarkAsync(client, siteId).ConfigureAwait(true);
        return siteId;
    }

    private async Task<Guid> ResolvePublicSiteAsync(string hostName)
    {
        var client = _app.CreateClientForHost(hostName);
        var response = await client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);

        response.EnsureSuccessStatusCode();

        return (await PublicSite.ResolvedSiteAsync(response).ConfigureAwait(true))!.Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
