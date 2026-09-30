using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 User Story 4: an administrator disables and re-enables a site (FR-019 to FR-022,
/// SC-002; clarification Q1). Also the dedicated test for the cross-site read in
/// <c>ChangeAvailability</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SiteAvailabilityTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("availability").ConfigureAwait(false));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// US4 scenario 1, clarification Q1: a disabled site's pages and media answer exactly
    /// as a host name no site holds.
    /// </summary>
    [Fact]
    public async Task A_disabled_site_is_indistinguishable_from_an_unknown_host()
    {
        var site = await CreateAsync("Quiet", "quiet.test").ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(_admin, site).ConfigureAwait(true);

        var disabled = await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal("disabled", (await SitesApi.BodyAsync(disabled).ConfigureAwait(true)).GetProperty("status").GetString());

        var unknown = await SitesApi.PublicGetAsync(_app, "nobody.test").ConfigureAwait(true);
        foreach (var path in new[] { "/", file.PublicPath, "/no-such-page" })
        {
            var response = await SitesApi.PublicGetAsync(_app, "quiet.test", path).ConfigureAwait(true);

            Assert.Equal(unknown.StatusCode, response.StatusCode);
            Assert.Equal(unknown.Content.Headers.ContentType?.ToString(), response.Content.Headers.ContentType?.ToString());
            Assert.Equal(await SitesApi.RawAsync(unknown).ConfigureAwait(true), await SitesApi.RawAsync(response).ConfigureAwait(true));
        }
    }

    /// <summary>US4 scenarios 2 and 3: the site stays whole, workable and holding its names.</summary>
    [Fact]
    public async Task A_disabled_site_keeps_its_names_and_stays_workable_on_the_administrative_surface()
    {
        var site = await CreateAsync("Kept", "kept.test").ConfigureAwait(true);
        await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true);

        // Still listed and marked.
        var listed = (await SitesApi.BodyAsync(await SitesApi.ListAsync(_admin).ConfigureAwait(true)).ConfigureAwait(true))
            .EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == site);
        Assert.Equal("disabled", listed.GetProperty("status").GetString());

        // Site members keep working on it: media uploads and lists.
        var editor = await IdentitySeed.CreateSignedInClientAsync(
            _app,
            (await IdentitySeed.SeedUserAsync(_app.Services, siteRoles: [(site, SystemRole.Editor)]).ConfigureAwait(true)).Id).ConfigureAwait(true);
        await MediaApi.UploadOkAsync(editor, site).ConfigureAwait(true);
        Assert.Equal(1, (await MediaApi.ListOkAsync(editor, site).ConfigureAwait(true)).TotalCount);

        // Its host name is still taken.
        var other = await CreateAsync("Other", "other-kept.test").ConfigureAwait(true);
        var taken = await SitesApi.AddHostNameAsync(_admin, other, "kept.test").ConfigureAwait(true);
        Assert.Equal("sites.host_name_taken", await SitesApi.ErrorCodeAsync(taken).ConfigureAwait(true));
    }

    /// <summary>US4 scenario 4: re-enabling brings back everything exactly as it was.</summary>
    [Fact]
    public async Task Re_enabling_restores_the_site_unchanged()
    {
        var site = await CreateAsync("Back", "back.test", "www.back.test").ConfigureAwait(true);
        await PublicSite.MarkAsync(_admin, site).ConfigureAwait(true);
        var file = await MediaApi.UploadOkAsync(_admin, site).ConfigureAwait(true);
        var before = await SitesApi.GetOkAsync(_admin, site).ConfigureAwait(true);

        await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true);
        var enabled = await SitesApi.EnableAsync(_admin, site).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var after = await SitesApi.BodyAsync(enabled).ConfigureAwait(true);
        Assert.Equal("enabled", after.GetProperty("status").GetString());
        Assert.Equal(before.GetProperty("hostNames").ToString(), after.GetProperty("hostNames").ToString());
        Assert.Equal(before.GetProperty("primaryHostName").GetString(), after.GetProperty("primaryHostName").GetString());
        // 012 spec, Edge Cases: its published pages are served again, unchanged.
        Assert.Equal(site, await PublicSite.ResolvedSiteAsync(await SitesApi.PublicGetAsync(_app, "www.back.test").ConfigureAwait(true)).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_app, "back.test", file.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>Idempotent: a repeat asks for nothing new and publishes nothing new.</summary>
    [Fact]
    public async Task Repeating_disable_or_enable_changes_nothing_and_publishes_one_event_each()
    {
        var site = await CreateAsync("Twice", "twice.test").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SitesApi.EnableAsync(_admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SitesApi.EnableAsync(_admin, site).ConfigureAwait(true)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        // Payloads are jsonb, so they are matched in memory rather than with LIKE.
        var messages = await scope.ServiceProvider.GetRequiredService<SitesDbContext>()
            .OutboxMessages.AsNoTracking().ToListAsync().ConfigureAwait(true);
        var siteText = site.ToString();
        Assert.Single(messages, m => m.EventType == SiteDisabled.EventType && m.Payload.Contains(siteText, StringComparison.OrdinalIgnoreCase));
        Assert.Single(messages, m => m.EventType == SiteEnabled.EventType && m.Payload.Contains(siteText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unknown_site_is_not_found()
    {
        var response = await SitesApi.DisableAsync(_admin, Guid.NewGuid()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateAsync(string name, params string[] hostNames) =>
        (await SitesApi.CreateOkAsync(_admin, name, hostNames).ConfigureAwait(false)).GetProperty("id").GetGuid();
}
