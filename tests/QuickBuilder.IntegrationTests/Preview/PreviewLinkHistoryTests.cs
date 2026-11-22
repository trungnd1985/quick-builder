using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// Creating and revoking a link is recorded in the page's or item's history, with who and when,
/// never the link or its label (022 FR-016, research PV-16).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkHistoryTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Label = "Secret client name";

    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-history").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Delivers every preview link event in the Editor outbox, twice, as a redelivery would.</summary>
    private async Task DeliverTwiceAsync()
    {
        foreach (var payload in await PreviewHistory.EditorOutboxAsync(_world.App, PreviewLinkCreated.EventType).ConfigureAwait(false))
        {
            var created = JsonSerializer.Deserialize<PreviewLinkCreated>(payload)!;
            await _world.App.DeliverAsync(created).ConfigureAwait(false);
            await _world.App.DeliverAsync(created).ConfigureAwait(false);
        }

        foreach (var payload in await PreviewHistory.EditorOutboxAsync(_world.App, PreviewLinkRevoked.EventType).ConfigureAwait(false))
        {
            var revoked = JsonSerializer.Deserialize<PreviewLinkRevoked>(payload)!;
            await _world.App.DeliverAsync(revoked).ConfigureAwait(false);
            await _world.App.DeliverAsync(revoked).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task A_pages_history_records_a_link_created_and_revoked_once_without_its_secret_or_label()
    {
        var page = await _world.PageAsync("pricing").ConfigureAwait(true);
        var path = PagesApi.PagePath(_world.Site, page);
        await PublishingApi.SaveAsync(_world.Admin, path, PublishingApi.HeadingDocument("Prices")).ConfigureAwait(true);
        var expires = DateTimeOffset.UtcNow.AddDays(3);
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, expires, label: Label).ConfigureAwait(true);
        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, link.Id).ConfigureAwait(true);

        await DeliverTwiceAsync().ConfigureAwait(true);

        var history = await (await PublishingApi.HistoryAsync(_world.Admin, path).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true);
        using var body = JsonDocument.Parse(history);
        var entries = Entries(body.RootElement).ToList();
        var created = Assert.Single(entries, e => e.GetProperty("action").GetString() == "preview_link_created");
        Assert.Single(entries, e => e.GetProperty("action").GetString() == "preview_link_revoked");
        Assert.Equal(expires.ToUnixTimeSeconds(), created.GetProperty("expiresAtUtc").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.NotEqual(JsonValueKind.Null, created.GetProperty("actor").ValueKind);
        Assert.DoesNotContain(Label, history, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Url[^43..], history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_items_history_records_a_link_created_and_revoked_once()
    {
        var blog = await _world.PageAsync("blog").ConfigureAwait(true);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(true);
        var post = ItemsApi.Id(await _world.Items.PostAsync("hello", new JsonObject { ["f1"] = "Hello" }).ConfigureAwait(true));
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "item", post, DateTimeOffset.UtcNow.AddDays(1), label: Label).ConfigureAwait(true);
        await PreviewLinksApi.RevokeAsync(_world.Admin, _world.Site, link.Id).ConfigureAwait(true);

        await DeliverTwiceAsync().ConfigureAwait(true);

        var history = await (await ItemsApi.HistoryAsync(_world.Admin, _world.Site, _world.Posts, post).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Equal(1, Count(history, "previewLinkCreated"));
        Assert.Equal(1, Count(history, "previewLinkRevoked"));
        Assert.DoesNotContain(Label, history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Links_that_simply_expire_add_nothing()
    {
        var page = await _world.PageAsync("quiet").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument("Quiet")).ConfigureAwait(true);
        await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, DateTimeOffset.UtcNow.AddHours(1)).ConfigureAwait(true);

        Assert.Empty(await PreviewHistory.EditorOutboxAsync(_world.App, PreviewLinkRevoked.EventType).ConfigureAwait(true));
    }

    private static IEnumerable<JsonElement> Entries(JsonElement history) =>
        history.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Array)
            .SelectMany(p => p.Value.EnumerateArray())
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("action", out _));

    private static int Count(string text, string value) =>
        (text.Length - text.Replace($"\"{value}\"", string.Empty, StringComparison.Ordinal).Length) / (value.Length + 2);
}
