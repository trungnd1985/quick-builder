using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 User Story 3: public requests reach the enabled site holding the host name the
/// visitor used, in any spelling, and every module sees that site (FR-013, FR-015,
/// SC-002). Also the dedicated test for the resolution query's cross-site read.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteResolutionTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private Guid _siteA;
    private Guid _siteB;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("resolution").ConfigureAwait(false));
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
        _siteA = (await SitesApi.CreateOkAsync(admin, "Site A", ["a.test", "bücher.test"]).ConfigureAwait(false)).GetProperty("id").GetGuid();
        _siteB = (await SitesApi.CreateOkAsync(admin, "Site B", ["b.test"]).ConfigureAwait(false)).GetProperty("id").GetGuid();
        await SitesApi.ServeOnEveryHostNameAsync(admin, _siteA).ConfigureAwait(false);

        // Each site serves its own id at /, so a response shows which site it came from.
        await PublicSite.MarkAsync(admin, _siteA).ConfigureAwait(false);
        await PublicSite.MarkAsync(admin, _siteB).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US3 scenario 1: any spelling, any port, reaches the same site.</summary>
    [Theory]
    [InlineData("a.test", "A")]
    [InlineData("A.TEST", "A")]
    [InlineData("a.test.", "A")]
    [InlineData("a.test:8443", "A")]
    [InlineData("xn--bcher-kva.test", "A")]
    [InlineData("XN--BCHER-KVA.TEST", "A")]
    [InlineData("b.test", "B")]
    [InlineData("B.test:80", "B")]
    public async Task Every_spelling_of_a_host_name_resolves_to_its_site(string host, string expected)
    {
        var response = await SitesApi.PublicGetAsync(_app, host).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected == "A" ? _siteA : _siteB, await PublicSite.ResolvedSiteAsync(response).ConfigureAwait(true));
    }

    /// <summary>US3 scenario 5: never a fallback, never a hint.</summary>
    [Theory]
    [InlineData("nobody.test")]
    [InlineData("a.test.evil")]
    [InlineData("localhost")]
    public async Task An_unknown_host_gets_the_fixed_not_resolved_body(string host)
    {
        var response = await SitesApi.PublicGetAsync(_app, host).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            """{"type":"https://quickbuilder.dev/errors/site-not-resolved","title":"Not found","status":404,"errorCode":"sites.not_resolved"}""",
            await SitesApi.RawAsync(response).ConfigureAwait(true));
    }

    /// <summary>
    /// FR-015, research S-02: the resolved site's details are one read away for any module,
    /// and reading them costs no second query.
    /// </summary>
    [Fact]
    public async Task The_current_site_carries_its_details_and_costs_no_second_query()
    {
        var counter = new QueryCounter();
        await using var scope = _app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var directory = services.GetRequiredService<SiteDirectory>();

        // What the middleware does: resolve, then set the ambient site.
        var resolved = await directory.FindByHostNameAsync("A.test.").ConfigureAwait(true);
        Assert.NotNull(resolved);
        services.GetRequiredService<ISiteContextSetter>().SetSite(resolved.Id);

        using (counter.Attach(services.GetRequiredService<SitesDbContext>()))
        {
            var current = services.GetRequiredService<ICurrentSite>().Site;
            var byId = await directory.FindByIdAsync(_siteA).ConfigureAwait(true);

            Assert.Same(resolved, current);
            Assert.Same(resolved, byId);
            Assert.Equal("a.test", current!.PrimaryHostName);
            Assert.Equal("https://a.test/", current.BaseUrl);
            Assert.Equal("en", current.DefaultLanguage);
        }

        Assert.Equal(0, counter.Commands);
    }

    [Fact]
    public async Task No_current_site_on_the_administrative_surface()
    {
        await using var scope = _app.Services.CreateAsyncScope();

        Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentSite>().Site);
    }

    /// <summary>
    /// Counts the queries a context runs while attached, by counting connection opens: EF
    /// opens the connection for each query it runs outside a transaction.
    /// </summary>
    private sealed class QueryCounter
    {
        private bool _attached;

        public int Commands { get; private set; }

        public IDisposable Attach(DbContext context)
        {
            _attached = true;
            var connection = context.Database.GetDbConnection();
            connection.StateChange += OnStateChange;
            return new Detach(() =>
            {
                _attached = false;
                connection.StateChange -= OnStateChange;
            });
        }

        private void OnStateChange(object sender, System.Data.StateChangeEventArgs e)
        {
            if (_attached && e.CurrentState == System.Data.ConnectionState.Open)
            {
                Commands++;
            }
        }

        private sealed class Detach(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
