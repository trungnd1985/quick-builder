using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 1: a site member publishes a page (012 FR-001, FR-007, FR-012, research PR-03).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublishApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private string _aboutPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "publish").ConfigureAwait(false);
        var ids = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout").ConfigureAwait(false);
        _about = ids["/about"];
        _aboutPath = PagesApi.PagePath(_scenario.SiteA, _about);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About us")).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publishing_makes_the_draft_live_as_version_one()
    {
        var publication = await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        Assert.Equal("published", publication.GetProperty("state").GetString());
        Assert.Equal(1, publication.GetProperty("liveNumber").GetInt32());
        Assert.False(publication.GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.EndsWith("/about", publication.GetProperty("liveUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, publication.GetProperty("liveBy").ValueKind);
    }

    [Fact]
    public async Task The_tree_shows_the_publication_and_draft_changes()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        Assert.False(await HasChangesAsync().ConfigureAwait(true));

        // A draft change is not what visitors get (FR-007).
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About us, again")).ConfigureAwait(true);
        Assert.True(await HasChangesAsync().ConfigureAwait(true));

        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        Assert.False(await HasChangesAsync().ConfigureAwait(true));

        // The title is part of the version too.
        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(
            _scenario.Admin, _scenario.SiteA, _about, "About the team", "about", node.GetProperty("version").GetInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(await HasChangesAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_stale_draft_version_is_refused_and_nothing_changes()
    {
        var version = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Changed meanwhile")).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(_scenario.Admin, _aboutPath, version).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.out_of_date").ConfigureAwait(true);

        Assert.Equal(0, await CountAsync<PublishedVersion>().ConfigureAwait(true));
    }

    [Fact]
    public async Task An_unknown_page_is_not_found()
    {
        await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(_scenario.Admin, PagesApi.PagePath(_scenario.SiteA, Guid.NewGuid()), 1).ConfigureAwait(true),
            HttpStatusCode.NotFound,
            "pages.not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Concurrent_publishes_both_succeed_with_consecutive_numbers()
    {
        var version = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            PublishingApi.PublishAsync(_scenario.Admin, _aboutPath, version))).ConfigureAwait(true);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var numbers = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishedVersion>(db)
            .Where(v => v.SubjectId == _about)
            .Select(v => v.Number)
            .OrderBy(n => n)
            .ToListAsync()).ConfigureAwait(true);

        Assert.Equal([1, 2, 3, 4], numbers);
    }

    [Fact]
    public async Task The_log_entry_and_the_event_are_written_with_the_publish()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        var entry = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishLogEntry>(db).SingleAsync()).ConfigureAwait(true);
        Assert.Equal(LogActions.Publish, entry.Action);
        Assert.Equal(1, entry.VersionNumber);
        Assert.Equal("/about", entry.SubjectPath);
        Assert.NotNull(entry.ActorUserId);

        var events = await _scenario.OutboxAsync().ConfigureAwait(true);
        var published = Assert.Single(events, e => e.Type == PagePublished.EventType);
        using var payload = JsonDocument.Parse(published.Payload);
        Assert.Equal(_about, payload.RootElement.GetProperty("PageId").GetGuid());
        Assert.Equal("publish", payload.RootElement.GetProperty("Kind").GetString());

        // FR-044: ids, numbers and paths only.
        Assert.DoesNotContain("About us", published.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_draft_is_refused_with_its_reason()
    {
        // Saved under an older contract, say: written straight into the table.
        await _scenario.ReadAsync(db => PagesScenario.Everything<PageDraft>(db)
            .Where(d => d.PageId == _about)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Document,
                """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"x","type":"no-such-block","props":{},"children":[]}]}}"""))).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(_scenario.Admin, _aboutPath,
                await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true),
            HttpStatusCode.BadRequest,
            "pages.invalid_document").ConfigureAwait(true);

        Assert.True(problem.Body.TryGetProperty("pointer", out _));
        Assert.Equal(0, await CountAsync<PublishedVersion>().ConfigureAwait(true));
    }

    private async Task<bool> HasChangesAsync() =>
        (await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(false))
        .GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean();

    private Task<int> CountAsync<T>()
        where T : class =>
        _scenario.ReadAsync(db => PagesScenario.Everything<T>(db).CountAsync());
}
