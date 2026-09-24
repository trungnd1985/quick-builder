using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// User Story 4: people see and act only on the sites they are permitted on (spec FR-020 to
/// FR-023, SC-003).
/// </summary>
/// <remarks>
/// A site the user may not see must look exactly like a site that does not exist — the
/// same bytes apart from <c>traceId</c> — or its existence leaks. A site they may see but
/// not change answers 403 honestly.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SiteVisibilityTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private Guid _siteA;
    private Guid _siteB;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("visibility").ConfigureAwait(false));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
        _siteA = await UsersApi.CreateSiteAsync(_admin, "site-a.test").ConfigureAwait(false);
        _siteB = await UsersApi.CreateSiteAsync(_admin, "site-b.test").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US4 scenarios 1 and 4: the site list is exactly what each user may see.</summary>
    [Fact]
    public async Task Each_user_lists_exactly_the_sites_they_hold_a_role_on()
    {
        var editorOfA = await ClientWithAsync((_siteA, SystemRole.Editor)).ConfigureAwait(true);
        var mixed = await ClientWithAsync((_siteA, SystemRole.Designer), (_siteB, SystemRole.Editor)).ConfigureAwait(true);
        var nobody = await ClientWithAsync().ConfigureAwait(true);

        Assert.Equal(new[] { _siteA, _siteB }.Order(), await SiteIdsAsync(_admin).ConfigureAwait(true));
        Assert.Equal(new[] { _siteA }, await SiteIdsAsync(editorOfA).ConfigureAwait(true));
        Assert.Equal(new[] { _siteA, _siteB }.Order(), await SiteIdsAsync(mixed).ConfigureAwait(true));
        Assert.Empty(await SiteIdsAsync(nobody).ConfigureAwait(true));
    }

    /// <summary>
    /// US4 scenario 2, SC-003: an unpermitted site answers exactly as a missing one does.
    /// </summary>
    [Fact]
    public async Task A_site_you_cannot_see_is_indistinguishable_from_one_that_does_not_exist()
    {
        var editorOfA = await ClientWithAsync((_siteA, SystemRole.Editor)).ConfigureAwait(true);

        var hidden = await editorOfA.GetAsync(new Uri($"/api/v1/sites/{_siteB}", UriKind.Relative)).ConfigureAwait(true);
        var missing = await _admin.GetAsync(new Uri($"/api/v1/sites/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true);
        var missingForEditor = await editorOfA.GetAsync(new Uri($"/api/v1/sites/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(missing.Content.Headers.ContentType?.MediaType, hidden.Content.Headers.ContentType?.MediaType);

        var hiddenBody = await SignInHelpers.BodyWithoutTraceIdAsync(hidden).ConfigureAwait(true);
        Assert.Equal(await SignInHelpers.BodyWithoutTraceIdAsync(missing).ConfigureAwait(true), hiddenBody);
        Assert.Equal(await SignInHelpers.BodyWithoutTraceIdAsync(missingForEditor).ConfigureAwait(true), hiddenBody);

        // A write naming the hidden site gets the same answer, not a 403 that would confirm it.
        var write = await AddHostNameAsync(editorOfA, _siteB, "sneak.site-b.test").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal(hiddenBody, await SignInHelpers.BodyWithoutTraceIdAsync(write).ConfigureAwait(true));
    }

    /// <summary>US4 scenario 3, FR-023: a visible site, a forbidden action — an honest 403.</summary>
    [Fact]
    public async Task A_visible_site_refuses_what_the_role_does_not_allow()
    {
        foreach (var role in new[] { SystemRole.Editor, SystemRole.Designer })
        {
            var client = await ClientWithAsync((_siteA, role)).ConfigureAwait(true);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri($"/api/v1/sites/{_siteA}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

            var write = await AddHostNameAsync(client, _siteA, $"{role}.site-a.test".ToLowerInvariant()).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
            Assert.Equal("identity.forbidden", await UsersApi.ErrorCodeAsync(write).ConfigureAwait(true));

            var create = await client.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Mine", hostNames = new[] { $"{role}-own.test".ToLowerInvariant() } }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }
    }

    /// <summary>US4 scenario 5: each site is judged by the role held on that site.</summary>
    [Fact]
    public async Task Different_roles_on_different_sites_are_judged_separately()
    {
        var client = await ClientWithAsync((_siteA, SystemRole.Designer), (_siteB, SystemRole.Editor)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri($"/api/v1/sites/{_siteA}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri($"/api/v1/sites/{_siteB}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddHostNameAsync(client, _siteB, "editor.site-b.test").ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US4 scenario 4: administrators act on every site without a grant.</summary>
    [Fact]
    public async Task Administrators_act_on_every_site()
    {
        Assert.Equal(HttpStatusCode.Created, (await AddHostNameAsync(_admin, _siteB, "admin.site-b.test").ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US4 scenario 6: a new site is reachable only by administrators until someone grants a role.</summary>
    [Fact]
    public async Task A_new_site_is_visible_only_to_administrators()
    {
        var editorOfA = await ClientWithAsync((_siteA, SystemRole.Editor)).ConfigureAwait(true);
        var siteC = await UsersApi.CreateSiteAsync(_admin, "site-c.test").ConfigureAwait(true);

        Assert.DoesNotContain(siteC, await SiteIdsAsync(editorOfA).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.NotFound, (await editorOfA.GetAsync(new Uri($"/api/v1/sites/{siteC}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Contains(siteC, await SiteIdsAsync(_admin).ConfigureAwait(true));
    }

    private async Task<HttpClient> ClientWithAsync(params (Guid SiteId, SystemRole Role)[] roles)
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, siteRoles: roles).ConfigureAwait(false);
        return await IdentitySeed.CreateSignedInClientAsync(_app, user.Id).ConfigureAwait(false);
    }

    private static async Task<List<Guid>> SiteIdsAsync(HttpClient client)
    {
        var sites = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(false);
        return [.. sites.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Order()];
    }

    private static Task<HttpResponseMessage> AddHostNameAsync(HttpClient client, Guid siteId, string hostName) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/sites/{siteId}/host-names", UriKind.Relative), new { hostName });
}
