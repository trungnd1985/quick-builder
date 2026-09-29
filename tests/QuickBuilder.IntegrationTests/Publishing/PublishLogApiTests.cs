using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Events;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 3: the site's publish log, for Administrators, keeps entries for pages and
/// components that no longer exist (012 FR-017, clarification Q2, research PR-16).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublishLogApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Dictionary<string, Guid> _pages = [];

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "publish-log").ConfigureAwait(false);
        _pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about\npricing").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Only_administrators_read_the_log()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.LogAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true)).StatusCode);
        await PagesApi.RefusedAsync(await PublishingApi.LogAsync(designer, _scenario.SiteA).ConfigureAwait(true), HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);
    }

    [Fact]
    public async Task The_log_filters_by_subject_and_user_and_pages_without_gaps()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);
        var about = Path("/about");
        var pricing = Path("/pricing");
        for (var i = 0; i < 3; i++)
        {
            await PublishingApi.PublishCurrentAsync(_scenario.Admin, about).ConfigureAwait(true);
        }

        await PublishingApi.PublishCurrentAsync(designer, pricing).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(designer, pricing).ConfigureAwait(true);

        var everything = await AllAsync(string.Empty, limit: 2).ConfigureAwait(true);
        Assert.Equal(5, everything.Count);
        Assert.Equal(5, everything.Select(e => e.GetProperty("id").GetGuid()).Distinct().Count());
        var times = everything.Select(e => e.GetProperty("occurredAtUtc").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        var aboutOnly = await AllAsync($"subjectId={_pages["/about"]}", limit: 50).ConfigureAwait(true);
        Assert.Equal(3, aboutOnly.Count);
        Assert.All(aboutOnly, e => Assert.Equal("/about", e.GetProperty("subjectPath").GetString()));

        var byDesigner = (await AllAsync($"subjectKind=page", limit: 50).ConfigureAwait(true))
            .Where(e => e.GetProperty("subjectId").GetGuid() == _pages["/pricing"])
            .Select(e => e.GetProperty("actor").GetProperty("id").GetGuid())
            .Distinct()
            .Single();
        Assert.Equal(2, (await AllAsync($"userId={byDesigner}", limit: 50).ConfigureAwait(true)).Count);

        await PagesApi.RefusedAsync(await PublishingApi.LogAsync(_scenario.Admin, _scenario.SiteA, "?cursor=nonsense").ConfigureAwait(true), HttpStatusCode.BadRequest, "pages.invalid_cursor").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PublishingApi.LogAsync(_scenario.Admin, _scenario.SiteA, "?limit=101").ConfigureAwait(true), HttpStatusCode.BadRequest, "pages.invalid_limit").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_deleted_page_keeps_its_entries_with_its_last_label_and_address()
    {
        var pricing = Path("/pricing");
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, pricing).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, pricing).ConfigureAwait(true)).ConfigureAwait(true);

        var page = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/pricing").ConfigureAwait(true);
        var version = page.GetProperty("version").GetInt32();
        var deleted = await _scenario.Admin.DeleteAsync(new Uri($"{pricing}?version={version}", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var entries = await AllAsync($"subjectId={_pages["/pricing"]}", limit: 50).ConfigureAwait(true);

        Assert.Equal(["deleted", "unpublish", "publish"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.All(entries, e =>
        {
            Assert.False(e.GetProperty("subjectExists").GetBoolean());
            Assert.Equal("/pricing", e.GetProperty("subjectPath").GetString());
            Assert.False(string.IsNullOrEmpty(e.GetProperty("subjectLabel").GetString()));
        });
    }

    [Fact]
    public async Task A_page_that_was_never_published_leaves_no_entry_when_deleted()
    {
        var about = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        var deleted = await _scenario.Admin.DeleteAsync(
            new Uri($"{Path("/about")}?version={about.GetProperty("version").GetInt32()}", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty(await AllAsync($"subjectId={_pages["/about"]}", limit: 50).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_design_publish_is_recorded_once_however_often_it_is_delivered()
    {
        var tokens = await PagesApi.OkAsync(await _scenario.Admin.GetAsync(
            new Uri($"/api/v1/editor/sites/{_scenario.SiteA}/design-tokens", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await _scenario.Admin.PostAsJsonAsync(
            new Uri($"/api/v1/editor/sites/{_scenario.SiteA}/design-tokens/publish", UriKind.Relative),
            new { version = tokens.GetProperty("version").GetUInt32() }).ConfigureAwait(true)).ConfigureAwait(true);

        await DispatchAsync().ConfigureAwait(true);

        var design = Assert.Single(await AllAsync("subjectKind=design", limit: 50).ConfigureAwait(true));
        Assert.Equal("publish", design.GetProperty("action").GetString());
        Assert.Equal(1, design.GetProperty("versionNumber").GetInt32());
        Assert.Equal(_scenario.SiteA, design.GetProperty("subjectId").GetGuid());
        Assert.True(design.GetProperty("subjectExists").GetBoolean());
        Assert.Equal(JsonValueKind.Object, design.GetProperty("actor").ValueKind);

        // The same event again, as a redelivery would bring it.
        await using (var scope = _scenario.App.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<DesignPublished>>();
            await handler.HandleAsync(
                    new DesignPublished(design.GetProperty("id").GetGuid(), DateTimeOffset.UtcNow, _scenario.SiteA, 1, "publish", null, null),
                    CancellationToken.None)
                .ConfigureAwait(true);
        }

        Assert.Single(await AllAsync("subjectKind=design", limit: 50).ConfigureAwait(true));
    }

    private string Path(string address) => PagesApi.PagePath(_scenario.SiteA, _pages[address]);

    /// <summary>Every entry the filter selects, following the cursor page by page.</summary>
    private async Task<List<JsonElement>> AllAsync(string filter, int limit)
    {
        var all = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var query = $"?limit={limit}" + (filter.Length > 0 ? $"&{filter}" : string.Empty) + (cursor is null ? string.Empty : $"&cursor={cursor}");
            var page = await PagesApi.OkAsync(await PublishingApi.LogAsync(_scenario.Admin, _scenario.SiteA, query).ConfigureAwait(false)).ConfigureAwait(false);
            all.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.String ? page.GetProperty("nextCursor").GetString() : null;
        }
        while (cursor is not null);

        return all;
    }

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }
}
