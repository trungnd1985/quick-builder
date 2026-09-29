using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 User Story 2: an administrator manages a site's host names (FR-007 to FR-012,
/// SC-004).
/// </summary>
/// <remarks>
/// Also the dedicated test for the cross-site reads in <c>AddHostName</c> and
/// <c>RemoveHostName</c>: the admin surface has no resolved site, so loading the named
/// site must bypass the filter.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class HostNameManagementTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("host-names").ConfigureAwait(false),
            settings: new Dictionary<string, string> { ["Hosting:AdminHostNames:0"] = "admin.test" });
        // With admin host names configured, the administrative surface answers only there.
        _admin = await _app.CreateAdminClientAsync("admin.test").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_internationalised_name_is_stored_in_ascii_and_shown_readably_and_resolves()
    {
        var site = await CreateAsync("Books", "books.test").ConfigureAwait(true);

        var response = await SitesApi.AddHostNameAsync(_admin, site, "Bücher.test.").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = await SitesApi.BodyAsync(response).ConfigureAwait(true);
        Assert.Equal("xn--bcher-kva.test", added.GetProperty("hostName").GetString());
        Assert.Equal("bücher.test", added.GetProperty("displayName").GetString());
        Assert.False(added.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(site, added.GetProperty("siteId").GetGuid());

        Assert.Equal(site, await PublicSite.ResolvedSiteAsync(await SitesApi.PublicGetAsync(_app, "xn--bcher-kva.test").ConfigureAwait(true)).ConfigureAwait(true));
    }

    /// <summary>
    /// US2 scenario 2, FR-008, SC-004: every spelling of a held name is refused as taken,
    /// and the refusal never says who holds it.
    /// </summary>
    [Fact]
    public async Task Every_equivalent_spelling_of_a_held_name_is_taken_and_the_holder_is_never_named()
    {
        var holder = await CreateAsync("Holder Site", "held.test", "bücher-held.test").ConfigureAwait(true);
        var other = await CreateAsync("Other", "other.test").ConfigureAwait(true);

        Assert.True(HostNames.TryNormalise("bücher-held.test", out var asciiForm));
        var spellings = new[]
        {
            "held.test", "HELD.TEST", "Held.Test", "held.test.", "  held.test  ",
            "bücher-held.test", "BÜCHER-HELD.test", asciiForm, asciiForm.ToUpperInvariant(),
        };

        foreach (var spelling in spellings)
        {
            var response = await SitesApi.AddHostNameAsync(_admin, other, spelling).ConfigureAwait(true);
            var body = await SitesApi.RawAsync(response).ConfigureAwait(true);

            Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"'{spelling}' was not refused: {(int)response.StatusCode}");
            Assert.Contains("sites.host_name_taken", body, StringComparison.Ordinal);
            Assert.DoesNotContain(holder.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Holder Site", body, StringComparison.Ordinal);
        }

        // The same site holding it already is also a conflict.
        var again = await SitesApi.AddHostNameAsync(_admin, holder, "HELD.test").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_name_held_by_a_disabled_site_or_configured_for_administration_is_taken()
    {
        var dormant = await CreateAsync("Dormant", "dormant.test").ConfigureAwait(true);
        await DisableDirectlyAsync(dormant).ConfigureAwait(true);
        var other = await CreateAsync("Taker", "taker.test").ConfigureAwait(true);

        var disabledHolder = await SitesApi.AddHostNameAsync(_admin, other, "dormant.test").ConfigureAwait(true);
        var adminHost = await SitesApi.AddHostNameAsync(_admin, other, "ADMIN.test").ConfigureAwait(true);
        var adminHostOnCreate = await SitesApi.CreateAsync(_admin, "Sneaky", ["admin.test"]).ConfigureAwait(true);

        Assert.Equal("sites.host_name_taken", await SitesApi.ErrorCodeAsync(disabledHolder).ConfigureAwait(true));
        Assert.Equal("sites.host_name_taken", await SitesApi.ErrorCodeAsync(adminHost).ConfigureAwait(true));
        Assert.Equal("sites.host_name_taken", await SitesApi.ErrorCodeAsync(adminHostOnCreate).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("example.test:8080")]
    [InlineData("https://example.test")]
    [InlineData("example.test/path")]
    [InlineData("*.example.test")]
    [InlineData("exa mple.test")]
    public async Task Structural_forms_are_refused_as_invalid(string hostName)
    {
        var site = await CreateAsync($"Invalid {Guid.NewGuid():N}", $"{Guid.NewGuid():N}.test").ConfigureAwait(true);

        var response = await SitesApi.AddHostNameAsync(_admin, site, hostName).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await SitesApi.BodyAsync(response).ConfigureAwait(true)).GetProperty("errors").TryGetProperty("HostName", out _));
    }

    /// <summary>US2 scenarios 3 and 6: a removed name stops resolving and is free again.</summary>
    [Fact]
    public async Task A_removed_name_stops_resolving_at_the_next_request_and_can_be_reused()
    {
        var first = await CreateAsync("First", "first.test", "staging.test").ConfigureAwait(true);
        var second = await CreateAsync("Second", "second.test").ConfigureAwait(true);

        // Warm: the name resolves to the first site now.
        Assert.Equal(first, await ResolvedSiteAsync("staging.test").ConfigureAwait(true));

        var removed = await SitesApi.RemoveHostNameAsync(_admin, first, "STAGING.test.").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SitesApi.PublicGetAsync(_app, "staging.test").ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await SitesApi.AddHostNameAsync(_admin, second, "staging.test").ConfigureAwait(true)).StatusCode);
        Assert.Equal(second, await ResolvedSiteAsync("staging.test").ConfigureAwait(true));
    }

    /// <summary>US2 scenario 4, FR-011.</summary>
    [Fact]
    public async Task The_primary_and_the_only_host_name_cannot_be_removed_and_unknown_names_are_not_found()
    {
        var two = await CreateAsync("Two", "two.test", "www.two.test").ConfigureAwait(true);
        var one = await CreateAsync("One", "one.test").ConfigureAwait(true);

        var primary = await SitesApi.RemoveHostNameAsync(_admin, two, "two.test").ConfigureAwait(true);
        var only = await SitesApi.RemoveHostNameAsync(_admin, one, "one.test").ConfigureAwait(true);
        var unknown = await SitesApi.RemoveHostNameAsync(_admin, two, "nowhere.test").ConfigureAwait(true);
        var othersName = await SitesApi.RemoveHostNameAsync(_admin, two, "one.test").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, primary.StatusCode);
        Assert.Equal("sites.primary_host_name_removal", await SitesApi.ErrorCodeAsync(primary).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.Conflict, only.StatusCode);
        Assert.Equal("sites.last_host_name", await SitesApi.ErrorCodeAsync(only).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("sites.host_name_not_found", await SitesApi.ErrorCodeAsync(unknown).ConfigureAwait(true));
        Assert.Equal("sites.host_name_not_found", await SitesApi.ErrorCodeAsync(othersName).ConfigureAwait(true));

        // Nothing was removed anywhere.
        Assert.Equal(2, (await SitesApi.GetOkAsync(_admin, two).ConfigureAwait(true)).GetProperty("hostNames").GetArrayLength());
        Assert.Equal(one, await ResolvedSiteAsync("one.test").ConfigureAwait(true));
    }

    /// <summary>Edge case: two administrators adding one name to two sites at once.</summary>
    [Fact]
    public async Task Concurrent_adds_of_one_name_to_two_sites_let_exactly_one_win()
    {
        for (var round = 0; round < 10; round++)
        {
            var a = await CreateAsync($"A{round}", $"a{round}.race.test").ConfigureAwait(true);
            var b = await CreateAsync($"B{round}", $"b{round}.race.test").ConfigureAwait(true);
            var contested = $"contested{round}.race.test";

            var responses = await Task.WhenAll(
                SitesApi.AddHostNameAsync(_admin, a, contested),
                SitesApi.AddHostNameAsync(_admin, b, contested)).ConfigureAwait(true);

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        }
    }

    /// <summary>A site that serves its own id at <c>/</c>, so resolution shows which site answered.</summary>
    private async Task<Guid> CreateAsync(string name, params string[] hostNames)
    {
        var site = (await SitesApi.CreateOkAsync(_admin, name, hostNames).ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublicSite.MarkAsync(_admin, site).ConfigureAwait(false);
        return site;
    }

    private async Task<Guid> ResolvedSiteAsync(string hostName)
    {
        var response = await SitesApi.PublicGetAsync(_app, hostName).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await PublicSite.ResolvedSiteAsync(response).ConfigureAwait(false))!.Value;
    }

    /// <summary>
    /// Disables a site underneath the API, so this story's tests do not depend on the
    /// disable endpoint (User Story 4).
    /// </summary>
    private async Task DisableDirectlyAsync(Guid siteId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var site = await sites.Sites
            .AcrossAllSites("Test setup: disabling a named site directly.")
            .FirstAsync(s => s.Id == siteId)
            .ConfigureAwait(false);
        site.Disable(TimeProvider.System);
        await sites.SaveChangesAsync().ConfigureAwait(false);
        Assert.Equal(SiteStatus.Disabled, site.Status);
    }
}
