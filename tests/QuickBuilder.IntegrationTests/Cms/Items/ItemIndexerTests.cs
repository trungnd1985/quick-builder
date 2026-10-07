using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Everything derived from an item follows its values (015 research IT-11).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemIndexerTests(DatabaseFixture database)
{
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Photo = Guid.NewGuid();
    private static readonly Guid Banner = Guid.NewGuid();

    [Fact]
    public async Task Names_search_text_attention_and_rows_are_derived_and_refreshing_twice_changes_nothing()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-indexer");
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "cover", "image"),
                CmsApi.Field("~3", "summary", "text", required: true, settings: new { multiline = true, maxLength = 5000 })),
        };
        var collection = await scenario.CollectionAsync("posts", schema);
        var collectionId = collection.GetProperty("id").GetGuid();
        var related = CmsApi.Schema(collection);
        CmsApi.Fields(related).Add(CmsApi.Field("~4", "related", "multiReference", settings: new { target = collectionId }));
        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, collectionId, CmsApi.SaveBody(collection, related));
        Assert.Equal(4, CmsApi.Fields(CmsApi.Schema(saved)).Count);

        var (target, item) = await scenario.WithDatabaseAsync(async cms =>
        {
            var now = DateTimeOffset.UtcNow;
            var target = Item.New(scenario.SiteA, collectionId, "target", Values(("f1", "Target"), ("f3", "x")), Ann, now);
            var item = Item.New(scenario.SiteA, collectionId, "hello-world", Values(("f1", "Héllo Wörld"), ("f2", Media(Photo))), Ann, now);
            cms.Items.AddRange(target, item);
            await cms.SaveChangesAsync();

            var published = Values(("f1", "Héllo Wörld"), ("f2", Media(Banner)), ("f3", "y"), ("f4", new JsonArray(target.Id.ToString(), Guid.NewGuid().ToString())));
            cms.ItemVersions.Add(item.Publish(published, "old-slug", ItemVersionKinds.Publish, null, null, Ann, now));
            item.SaveDraft("hello-world", Values(("f1", "Héllo Wörld"), ("f2", Media(Photo)), ("f4", new JsonArray(target.Id.ToString(), item.Id.ToString()))), Ann, now);
            await cms.SaveChangesAsync();
            return (target, item);
        });

        var first = await RefreshAndReadAsync(scenario, collectionId, item.Id);
        var second = await RefreshAndReadAsync(scenario, collectionId, item.Id);

        Assert.Equal(first, second);
        Assert.Equal("Héllo Wörld", first.Name);
        Assert.Equal("hello world hello-world", first.SearchText);
        Assert.Equal(ItemStatuses.Changed, first.Status);
        Assert.Equal(ItemIndexer.CurrentVersion, first.IndexVersion);

        // The draft is missing the required summary and refers to itself: both reported, and the
        // media files do not exist in this test, so they are missing too.
        Assert.Contains("missing_required", first.Attention, StringComparison.Ordinal);
        Assert.Contains("reference_self", first.Attention, StringComparison.Ordinal);
        Assert.Contains("media_missing", first.Attention, StringComparison.Ordinal);

        // Both slugs are held; the reference to the target is in both copies, the reference to
        // a missing item and to itself are never stored.
        Assert.Equal(["hello-world", "old-slug"], first.Slugs);
        Assert.Equal([(target.Id, "f4", true, true, false)], first.References);
        Assert.Equal(
            [(Banner, false, true, false), (Photo, true, false, false)],
            first.Media.OrderBy(m => m.MediaId == Photo).ToList());
    }

    private static async Task<Snapshot> RefreshAndReadAsync(CmsScenario scenario, Guid collectionId, Guid itemId)
    {
        await using var scope = scenario.App.Services.CreateAsyncScope();
        var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<ItemIndexer>();
        var schema = CollectionSchemaDocument.Read(await cms.ForSite<Collection>(scenario.SiteA).Where(c => c.Id == collectionId).Select(c => c.Schema).SingleAsync());

        await using (var transaction = await cms.Database.BeginTransactionAsync())
        {
            var item = await cms.ForSite<Item>(scenario.SiteA).SingleAsync(i => i.Id == itemId);
            await indexer.RefreshAsync(item, schema, versionsChanged: true, CancellationToken.None);
            await cms.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        cms.ChangeTracker.Clear();
        var stored = await cms.ForSite<Item>(scenario.SiteA).AsNoTracking().SingleAsync(i => i.Id == itemId);
        return new Snapshot(
            stored.Name,
            stored.SearchText,
            stored.Status,
            stored.IndexVersion,
            stored.Attention?.RootElement.GetRawText() ?? string.Empty,
            await cms.ForSite<ItemSlug>(scenario.SiteA).Where(s => s.ItemId == itemId).OrderBy(s => s.Slug).Select(s => s.Slug).ToListAsync(),
            (await cms.ForSite<ItemReference>(scenario.SiteA).Where(r => r.SourceItemId == itemId).ToListAsync())
                .Select(r => (r.TargetItemId, r.FieldId, r.InDraft, r.InPublished, r.InScheduled)).ToList(),
            (await cms.ForSite<ItemMediaUse>(scenario.SiteA).Where(m => m.ItemId == itemId).ToListAsync())
                .Select(m => (m.MediaId, m.InDraft, m.InLive, m.InHistory)).ToList());
    }

    private static JsonObject Media(Guid id) => new() { ["mediaId"] = id.ToString() };

    private static ItemValues Values(params (string Id, JsonNode Value)[] values)
    {
        var map = new JsonObject();
        foreach (var (id, value) in values)
        {
            map[id] = value.DeepClone();
        }

        return ItemValues.Read(JsonDocument.Parse(new JsonObject { ["schemaVersion"] = 1, ["values"] = map }.ToJsonString()));
    }

    private sealed record Snapshot(
        string? Name,
        string SearchText,
        string Status,
        int IndexVersion,
        string Attention,
        List<string> Slugs,
        List<(Guid TargetItemId, string FieldId, bool InDraft, bool InPublished, bool InScheduled)> References,
        List<(Guid MediaId, bool InDraft, bool InLive, bool InHistory)> Media)
    {
        public bool Equals(Snapshot? other) =>
            other is not null
            && (Name, SearchText, Status, IndexVersion, Attention) == (other.Name, other.SearchText, other.Status, other.IndexVersion, other.Attention)
            && Slugs.SequenceEqual(other.Slugs)
            && References.SequenceEqual(other.References)
            && Media.OrderBy(m => m.MediaId).SequenceEqual(other.Media.OrderBy(m => m.MediaId));

        public override int GetHashCode() => HashCode.Combine(Name, SearchText, Status);
    }
}
