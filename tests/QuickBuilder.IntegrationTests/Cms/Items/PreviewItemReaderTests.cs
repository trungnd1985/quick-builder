using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Item drafts and retained versions for previews (022 research PV-02, module-surface.md §2.2),
/// through Cms' own read-only preview context.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewItemReaderTests(DatabaseFixture database) : IAsyncLifetime
{
    private ItemsScenario _items = null!;

    public async Task InitializeAsync() =>
        _items = await ItemsScenario.CreateAsync(database, "preview-items").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _items.Dispose();
        return Task.CompletedTask;
    }

    private async Task<T> WithReaderAsync<T>(Func<IPreviewItemReader, Task<T>> work, Guid? site = null)
    {
        await using var scope = _items.Cms.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(site ?? _items.Site);
        return await work(scope.ServiceProvider.GetRequiredService<IPreviewItemReader>()).ConfigureAwait(false);
    }

    [Fact]
    public async Task A_draft_is_read_as_an_item_with_its_draft_values_and_slug()
    {
        var created = await _items.PostAsync("hello", new JsonObject { ["f1"] = "Draft title" }).ConfigureAwait(true);
        var id = ItemsApi.Id(created);

        var draft = await WithReaderAsync(r => r.GetDraftAsync(_items.Site, id)).ConfigureAwait(true);

        Assert.NotNull(draft);
        Assert.Equal("hello", draft.Item.Slug);
        Assert.Equal("Draft title", draft.Item.Name);
        Assert.Equal(_items.Posts, draft.Item.CollectionId);
        Assert.False(draft.IsPublished);
        Assert.Null(draft.PendingScheduleAtUtc);
        Assert.Contains("Draft title", draft.Item.Values.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_missing_a_required_value_needs_attention()
    {
        var notes = await _items.Cms.CollectionAsync("notes", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", required: true, settings: new { multiline = false, maxLength = 50 })),
        }).ConfigureAwait(true);
        var created = await ItemsApi.CreateOkAsync(
                _items.Admin, _items.Site, notes.GetProperty("id").GetGuid(),
                ItemsApi.Draft("untitled", new JsonObject(), notes.GetProperty("schemaRevision").GetInt32()))
            .ConfigureAwait(true);

        var draft = await WithReaderAsync(r => r.GetDraftAsync(_items.Site, ItemsApi.Id(created))).ConfigureAwait(true);

        Assert.True(draft!.NeedsAttention);
    }

    [Fact]
    public async Task A_version_is_read_with_its_own_values_and_the_draft_does_not_change_it()
    {
        var created = await _items.PostAsync("versioned", new JsonObject { ["f1"] = "Published title" }).ConfigureAwait(true);
        var id = ItemsApi.Id(created);
        var published = await ItemsApi.PublishOkAsync(_items.Admin, _items.Site, _items.Posts, id, ItemsApi.Revision(created)).ConfigureAwait(true);
        await ItemsApi.SaveOkAsync(
                _items.Admin, _items.Site, _items.Posts, id,
                ItemsApi.Draft("versioned", new JsonObject { ["f1"] = "Changed title" }, _items.PostsSchemaRevision, ItemsApi.Revision(published)))
            .ConfigureAwait(true);

        var version = await WithReaderAsync(r => r.GetVersionAsync(_items.Site, id, 1)).ConfigureAwait(true);
        var draft = await WithReaderAsync(r => r.GetDraftAsync(_items.Site, id)).ConfigureAwait(true);

        Assert.Equal("Published title", version!.Item.Name);
        Assert.Equal(1, version.Number);
        Assert.Equal("Changed title", draft!.Item.Name);
        Assert.True(draft.IsPublished);
        Assert.Null(await WithReaderAsync(r => r.GetVersionAsync(_items.Site, id, 2)).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_trashed_item_and_another_sites_item_are_absent()
    {
        var created = await _items.PostAsync("to-trash").ConfigureAwait(true);
        var id = ItemsApi.Id(created);
        var deleted = await ItemsApi.DeleteAsync(_items.Admin, _items.Site, _items.Posts, id, ItemsApi.Revision(created)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var kept = ItemsApi.Id(await _items.PostAsync("kept").ConfigureAwait(true));

        Assert.Null(await WithReaderAsync(r => r.GetDraftAsync(_items.Site, id)).ConfigureAwait(true));
        Assert.Null(await WithReaderAsync(r => r.GetDraftAsync(_items.Site, kept), _items.Cms.SiteB).ConfigureAwait(true));
        Assert.Empty(await WithReaderAsync(r => r.GetNamesAsync(_items.Site, [kept]), _items.Cms.SiteB).ConfigureAwait(true));
    }

    [Fact]
    public async Task Names_are_read_from_the_draft_with_their_collection()
    {
        var person = ItemsApi.Id(await _items.PersonAsync("Ada Lovelace").ConfigureAwait(true));

        var names = await WithReaderAsync(r => r.GetNamesAsync(_items.Site, [person, Guid.NewGuid()])).ConfigureAwait(true);

        Assert.Equal([person], names.Keys);
        Assert.Equal(_items.People, names[person].CollectionId);
        Assert.EndsWith("Ada Lovelace", names[person].Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_preview_context_cannot_save()
    {
        await using var scope = _items.Cms.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(_items.Site);
        var context = scope.ServiceProvider.GetRequiredService<PreviewReadDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync()).ConfigureAwait(true);
    }
}
