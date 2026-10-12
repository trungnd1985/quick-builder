using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// Constitution Principle V: a change on one site never moves a generation on another (012
/// data-model §3.2). The bump runs on the admin surface, where no site is resolved, so its
/// explicit site predicate is all that stands between the two.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GenerationBumpSiteFilterTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "bump-site-filter").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_component_publish_on_site_a_leaves_site_b_alone_even_with_the_same_target_id()
    {
        var component = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Shared name").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var componentPath = PagesApi.ComponentPath(_scenario.SiteA, component);
        await PublishingApi.SaveAsync(_scenario.Admin, componentPath, PublishingApi.ComponentDocument("A")).ConfigureAwait(true);

        // Site B: a live page whose dependency row names the very same id. It cannot arise
        // through the API — ids are unique — which is exactly why only the site predicate
        // keeps them apart.
        var foreignPage = Guid.NewGuid();
        await _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO pages.publications (subject_kind, subject_id, site_id, next_number, generation) VALUES ('page', {0}, {1}, 1, 7);
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id) VALUES ({1}, 'page', {0}, 'component', {2});
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id) VALUES ({1}, 'page', {0}, 'page', {3});
            """,
            foreignPage,
            _scenario.SiteB,
            component,
            Guid.Empty)).ConfigureAwait(true);

        await PublishingApi.PublishCurrentAsync(_scenario.Admin, componentPath).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, componentPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, componentPath).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(7, await GenerationAsync(foreignPage).ConfigureAwait(true));
    }

    [Fact]
    public async Task An_address_change_on_site_a_leaves_site_b_alone()
    {
        var page = (await PagesApi.CreateOkAsync(_scenario.Admin, _scenario.SiteA, "moving").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var foreignPage = Guid.NewGuid();
        await _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO pages.publications (subject_kind, subject_id, site_id, next_number, generation) VALUES ('page', {0}, {1}, 1, 7);
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id) VALUES ({1}, 'page', {0}, 'page', {2});
            """,
            foreignPage,
            _scenario.SiteB,
            page)).ConfigureAwait(true);

        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/moving").ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, page, node.GetProperty("title").GetString()!, "moved", node.GetProperty("version").GetInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(7, await GenerationAsync(foreignPage).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_cms_event_on_site_a_leaves_site_b_alone_even_with_the_same_collection_id()
    {
        // 016 TB-10: the collection id of a site A event, on a live site B page's rows.
        var collection = Guid.NewGuid();
        var foreignPage = Guid.NewGuid();
        await _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO pages.publications (subject_kind, subject_id, site_id, next_number, generation) VALUES ('page', {0}, {1}, 1, 7);
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id) VALUES ({1}, 'page', {0}, 'collection', {2});
            INSERT INTO pages.live_dependencies (site_id, subject_kind, subject_id, target_kind, target_id) VALUES ({1}, 'page', {0}, 'template', {2});
            """,
            foreignPage,
            _scenario.SiteB,
            collection)).ConfigureAwait(true);

        await _scenario.App.DeliverAsync(new ItemPublished(
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, _scenario.SiteA, collection, Guid.NewGuid(), 1, "manual", false, null)).ConfigureAwait(true);
        await _scenario.App.DeliverAsync(new CollectionChanged(
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, _scenario.SiteA, collection, 2, true, true, null)).ConfigureAwait(true);
        await _scenario.App.DeliverAsync(new CollectionDeleted(
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, _scenario.SiteA, collection, null)).ConfigureAwait(true);

        Assert.Equal(7, await GenerationAsync(foreignPage).ConfigureAwait(true));
    }

    private Task<long> GenerationAsync(Guid pageId) =>
        _scenario.ReadAsync(db => db.Database
            .SqlQueryRaw<long>("SELECT generation AS \"Value\" FROM pages.publications WHERE subject_kind = 'page' AND subject_id = {0}", pageId)
            .SingleAsync());
}
