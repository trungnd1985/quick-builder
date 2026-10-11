using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// The world most template and binding tests start from (016 tasks T024): <see cref="ItemsScenario"/>'s
/// two sites and its <c>posts</c> and <c>people</c> collections, plus helpers to publish items,
/// make templates, publish documents with bindings, and visit the public site.
/// </summary>
/// <remarks>
/// Field ids after the first save are fixed by <see cref="CmsApi.EveryType"/>: posts have
/// <c>f1</c> title, <c>f2</c> body, <c>f3</c> reading time, <c>f4</c> featured, <c>f5</c>
/// published on, <c>f6</c> cover, <c>f7</c> brochure, <c>f8</c> category (options <c>o1</c>
/// News, <c>o2</c> Guides), <c>f9</c> author (→ people) and <c>f10</c> related (→ people);
/// people have <c>f1</c> name. The outbox does not run in tests, so events are delivered by
/// hand with <see cref="EventDelivery"/>.
/// </remarks>
internal sealed class TemplatesWorld : IDisposable
{
    private TemplatesWorld(ItemsScenario items) => Items = items;

    public ItemsScenario Items { get; }

    public CmsScenario Cms => Items.Cms;

    public QuickBuilderApp App => Cms.App;

    public HttpClient Admin => Cms.Admin;

    public Guid Site => Cms.SiteA;

    public Guid Posts => Items.Posts;

    public Guid People => Items.People;

    public static async Task<TemplatesWorld> CreateAsync(DatabaseFixture database, string label) =>
        new(await ItemsScenario.CreateAsync(database, label).ConfigureAwait(false));

    /// <summary>A client for the public site of site A, as a visitor.</summary>
    public HttpClient Visitor(string host = CmsScenario.HostA) => App.CreateClientForHost(host);

    /// <summary>The body of a public request, and its status.</summary>
    public async Task<(HttpStatusCode Status, string Body)> VisitAsync(string path, string host = CmsScenario.HostA)
    {
        using var visitor = Visitor(host);
        using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    /// <summary>A post with these values, created and published through the API; returns the published item.</summary>
    public async Task<JsonElement> PublishedPostAsync(string slug, JsonObject? values = null)
    {
        var created = await Items.PostAsync(slug, values).ConfigureAwait(false);
        return await ItemsApi.PublishOkAsync(Admin, Site, Posts, ItemsApi.Id(created), ItemsApi.Revision(created)).ConfigureAwait(false);
    }

    /// <summary>A person, created and published through the API; returns the published item.</summary>
    public async Task<JsonElement> PublishedPersonAsync(string name)
    {
        var created = await Items.PersonAsync(name).ConfigureAwait(false);
        return await ItemsApi.PublishOkAsync(Admin, Site, People, ItemsApi.Id(created), ItemsApi.Revision(created)).ConfigureAwait(false);
    }

    /// <summary>A page created at <paramref name="slug"/> under <paramref name="parentId"/>; returns its id.</summary>
    public async Task<Guid> PageAsync(string slug, Guid? parentId = null) =>
        (await PagesApi.CreateOkAsync(Admin, Site, slug, parentId).ConfigureAwait(false)).GetProperty("id").GetGuid();

    /// <summary>Saves <paramref name="document"/> as the page's draft and publishes it.</summary>
    public async Task PublishDocumentAsync(Guid pageId, string document)
    {
        var path = PagesApi.PagePath(Site, pageId);
        await PublishingApi.SaveAsync(Admin, path, document).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(Admin, path).ConfigureAwait(false);
    }

    /// <summary>The template endpoint of a page (contracts/templates-api.yaml).</summary>
    public string TemplatePath(Guid pageId) => $"{PagesApi.PagePath(Site, pageId)}/template";

    public async Task<HttpResponseMessage> SetTemplateAsync(Guid pageId, Guid collectionId, HttpClient? client = null)
    {
        var version = (await PagesApi.OkAsync(await PagesApi.GetAsync(Admin, Site, pageId).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("version").GetInt32();
        return await (client ?? Admin).PutAsJsonAsync(new Uri(TemplatePath(pageId), UriKind.Relative), new { collectionId, version }).ConfigureAwait(false);
    }

    /// <summary>Makes the page a template of <paramref name="collectionId"/>, expecting success.</summary>
    public async Task<JsonElement> TemplateOkAsync(Guid pageId, Guid collectionId) =>
        await PagesApi.OkAsync(await SetTemplateAsync(pageId, collectionId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// A page document: one Section holding <paramref name="blocks"/> (node JSON), with
    /// <c>bindingVersion</c> set.
    /// </summary>
    public static string Document(params string[] blocks) =>
        """{"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
        + string.Join(',', blocks)
        + "]}]}}";

    /// <summary>A Heading whose text is bound to <paramref name="value"/> (a binding value object).</summary>
    public static string BoundHeading(string id, string value, string fallback = "Fallback", string? empty = null) =>
        "{\"id\":\"" + id + "\",\"type\":\"heading\",\"props\":{\"text\":" + JsonSerializer.Serialize(fallback)
        + ",\"level\":1,\"bind\":{\"text\":{\"value\":" + value
        + (empty is null ? string.Empty : ",\"empty\":\"" + empty + "\"")
        + "}}},\"children\":[]}";

    /// <summary>The Pages outbox's event types, oldest first, with their payloads.</summary>
    public async Task<List<(string Type, string Payload)>> PagesOutboxAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>();
        return (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    database.Set<QuickBuilder.Shared.Infrastructure.Outbox.OutboxMessage>()
                        .OrderBy(m => m.OccurredAtUtc)
                        .ThenBy(m => m.Id)
                        .Select(m => new { m.EventType, m.Payload }))
                .ConfigureAwait(false))
            .Select(m => (m.EventType, m.Payload))
            .ToList();
    }

    /// <summary>
    /// A Collection list of <paramref name="source"/> (a list source object) with more
    /// <paramref name="props"/> (JSON members, without braces), its card holding
    /// <paramref name="card"/> (node JSON) and an empty state saying "Nothing yet".
    /// </summary>
    public static string List(string id, string source, string? props = null, params string[] card) =>
        "{\"id\":\"" + id + "\",\"type\":\"collection-list\",\"props\":{\"source\":" + source
        + (props is null ? string.Empty : "," + props)
        + "},\"children\":[{\"id\":\"" + id + "-card\",\"type\":\"list-card\",\"props\":{},\"children\":[" + string.Join(',', card) + "]},{\"id\":\"" + id
        + "-empty\",\"type\":\"list-empty\",\"props\":{},\"children\":[{\"id\":\"" + id + "-none\",\"type\":\"text\",\"props\":{\"text\":\"Nothing yet\"},\"children\":[]}]}]}";

    /// <summary>A Collection list of the collection <paramref name="collectionId"/>.</summary>
    public static string ListOf(string id, Guid collectionId, string? props = null, params string[] card) =>
        List(id, "{\"collectionId\":\"" + collectionId + "\"}", props, card);

    /// <summary>A card's title (a Heading bound to <c>f1</c>) and its "Read" button to the item page.</summary>
    public static string[] TitleAndLink(string prefix = "c") =>
    [
        BoundHeading(prefix + "-title", """{"field":"f1"}"""),
        "{\"id\":\"" + prefix + "-go\",\"type\":\"button\",\"props\":{\"label\":\"Read\",\"bind\":{\"link\":{\"value\":{\"item\":\"url\"}}}},\"children\":[]}",
    ];

    /// <summary>The page's render generation, or null when it was never published.</summary>
    public Task<long?> GenerationAsync(Guid pageId) =>
        ScalarAsync("SELECT generation AS \"Value\" FROM pages.publications WHERE subject_kind = 'page' AND subject_id = {0}", pageId);

    /// <summary>The item's render stamp.</summary>
    public Task<long?> RenderStampAsync(Guid itemId) =>
        ScalarAsync("SELECT render_stamp AS \"Value\" FROM cms.items WHERE id = {0}", itemId);

    /// <summary>The page's live dependency rows, as (kind, target).</summary>
    public async Task<List<(string Kind, Guid Target)>> LiveDependenciesAsync(Guid pageId)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQueryRaw<string>(
                    database.Database,
                    "SELECT target_kind || ':' || target_id AS \"Value\" FROM pages.live_dependencies WHERE subject_kind = 'page' AND subject_id = {0}",
                    pageId))
            .ConfigureAwait(false);
        return rows.Select(r => r.Split(':')).Select(r => (r[0], Guid.Parse(r[1]))).ToList();
    }

    private async Task<long?> ScalarAsync(string sql, Guid id)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQueryRaw<long?>(database.Database, sql, id))
            .ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> with the public path's item reader, as site A's visitors read it.</summary>
    public async Task<T> WithItemReaderAsync<T>(Func<IPublishedItemReader, Task<T>> work, Guid? site = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(site ?? Site);
        return await work(scope.ServiceProvider.GetRequiredService<IPublishedItemReader>()).ConfigureAwait(false);
    }

    public void Dispose() => Items.Dispose();
}
