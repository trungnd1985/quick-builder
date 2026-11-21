using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// User Story 4: members revoke links and see who has access (022 FR-009, FR-014, FR-015,
/// FR-017, SC-006, clarification 2).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkRevocationTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Host = CmsScenario.HostA;

    private TemplatesWorld _world = null!;
    private Guid _page;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "preview-revoke").ConfigureAwait(false);
        _page = await _world.PageAsync("pricing").ConfigureAwait(false);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, _page), PublishingApi.HeadingDocument("Prices")).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<CreatedLink> LinkAsync(HttpClient? client = null, Guid? page = null, Guid? site = null) =>
        PreviewLinksApi.CreateOkAsync(client ?? _world.Admin, site ?? _world.Site, "page", page ?? _page, DateTimeOffset.UtcNow.AddDays(1));

    private async Task<HttpStatusCode> OpenAsync(CreatedLink link, QuickBuilderApp? app = null) =>
        (await PreviewLinksApi.OpenAsync(app ?? _world.App, link.Path, Host).ConfigureAwait(false)).Status;

    [Fact]
    public async Task Revoking_ends_only_that_link_and_revoking_again_changes_nothing()
    {
        var first = await LinkAsync().ConfigureAwait(true);
        var second = await LinkAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, first.Id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, first.Id).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(first).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(second).ConfigureAwait(true));

        var revocations = await PreviewHistory.EditorOutboxAsync(_world.App, "Editor.PreviewLinkRevoked").ConfigureAwait(true);
        Assert.Single(revocations, payload => payload.Contains(first.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revoke_all_ends_a_targets_links_and_an_administrator_ends_the_whole_sites()
    {
        var other = await _world.PageAsync("other").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, other), PublishingApi.HeadingDocument("Other")).ConfigureAwait(true);
        var siteBPage = (await PagesApi.CreateOkAsync(_world.Admin, _world.Cms.SiteB, "b").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Cms.SiteB, siteBPage), PublishingApi.HeadingDocument("B")).ConfigureAwait(true);

        var a1 = await LinkAsync().ConfigureAwait(true);
        var a2 = await LinkAsync().ConfigureAwait(true);
        var o1 = await LinkAsync(page: other).ConfigureAwait(true);
        var b1 = await LinkAsync(page: siteBPage, site: _world.Cms.SiteB).ConfigureAwait(true);

        var target = await PreviewLinksApi.RevokeAllAsync(_world.Admin, _world.Site, "page", _page).ConfigureAwait(true);
        Assert.Contains("\"revoked\":2", await target.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(a1).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(a2).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(o1).ConfigureAwait(true));

        var site = await PreviewLinksApi.RevokeAllAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        Assert.Contains("\"revoked\":1", await site.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(o1).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(b1).ConfigureAwait(true));
    }

    [Fact]
    public async Task The_site_list_names_every_active_link_and_its_target()
    {
        var link = await LinkAsync().ConfigureAwait(true);
        var revoked = await LinkAsync().ConfigureAwait(true);
        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, revoked.Id).ConfigureAwait(true);

        var body = await (await PreviewLinksApi.SiteListAsync(_world.Admin, _world.Site).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true);
        using var list = JsonDocument.Parse(body);
        var items = list.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(link.Id, items[0].GetProperty("link").GetProperty("id").GetGuid());
        Assert.EndsWith("pricing", items[0].GetProperty("targetLabel").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revoked_link_is_gone_on_another_instance_at_its_next_request()
    {
        using var second = new QuickBuilderApp(_world.App.ConnectionString);
        var link = await LinkAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(link, second).ConfigureAwait(true));

        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, link.Id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(link, second).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_link_ends_when_its_creator_loses_access_and_works_again_when_it_is_given_back()
    {
        var editor = await IdentitySeed.SeedUserAsync(_world.App.Services, siteRoles: [(_world.Site, SystemRole.Editor)], displayName: "Lan").ConfigureAwait(true);
        using var client = await IdentitySeed.CreateSignedInClientAsync(_world.App, editor.Id).ConfigureAwait(true);
        var theirs = await LinkAsync(client).ConfigureAwait(true);
        var mine = await LinkAsync().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(theirs).ConfigureAwait(true));

        await PagesApi.OkAsync(await Identity.UsersApi.ReplaceGrantsAsync(_world.Admin, editor.Id, Identity.UsersApi.Grants()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(theirs).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(mine).ConfigureAwait(true));

        await PagesApi.OkAsync(await Identity.UsersApi.ReplaceGrantsAsync(_world.Admin, editor.Id, Identity.UsersApi.Grants(false, (_world.Site, "Editor"))).ConfigureAwait(true))
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(theirs).ConfigureAwait(true));

        await PagesApi.OkAsync(await Identity.UsersApi.DeactivateAsync(_world.Admin, editor.Id).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, await OpenAsync(theirs).ConfigureAwait(true));
    }

    [Fact]
    public async Task Editors_and_designers_manage_a_targets_links_but_only_administrators_the_sites()
    {
        using var editor = await _world.Cms.UserAsync((_world.Site, SystemRole.Editor)).ConfigureAwait(true);
        using var designer = await _world.Cms.UserAsync((_world.Site, SystemRole.Designer)).ConfigureAwait(true);
        using var outsider = await _world.Cms.UserAsync((_world.Cms.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        foreach (var member in new[] { editor, designer })
        {
            var link = await LinkAsync(member).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, (await PreviewLinksApi.ListAsync(member, _world.Site, "page", _page).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PreviewLinksApi.RevokeAsync(member, _world.Site, link.Id).ConfigureAwait(true)).StatusCode);
            Assert.False((await PreviewLinksApi.SiteListAsync(member, _world.Site).ConfigureAwait(true)).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PreviewLinksApi.RevokeAllAsync(member, _world.Site).ConfigureAwait(true)).StatusCode);
        }

        var adminLink = await LinkAsync().ConfigureAwait(true);
        Assert.False((await PreviewLinksApi.CreateAsync(outsider, _world.Site, "page", _page, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(true)).IsSuccessStatusCode);
        Assert.False((await PreviewLinksApi.RevokeAsync(outsider, _world.Site, adminLink.Id).ConfigureAwait(true)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, await OpenAsync(adminLink).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, (await PreviewLinksApi.SiteListAsync(_world.Admin, _world.Site).ConfigureAwait(true)).StatusCode);
    }
}
