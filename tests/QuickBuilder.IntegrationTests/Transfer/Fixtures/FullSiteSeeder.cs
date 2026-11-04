using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Seo;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.IntegrationTests.Transfer.Fixtures;

/// <summary>
/// A site that uses every kind of content an archive carries (020 quickstart §0, T020), built
/// through the API on top of <see cref="TemplatesWorld"/>:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>people and posts collections (every field type), a published person and a published
/// post whose draft is ahead of its live values, referencing the person, an image and a PDF;</item>
/// <item>a shared component used by a page; pages <c>/about</c> (two published versions and a
/// newer draft) and <c>/about/team</c> (a pending publish schedule); a template page of posts under <c>/blog</c>;</item>
/// <item>a published menu pointing at <c>/about</c>, a redirect to it, and a published design;</item>
/// <item>an image whose stored bytes are gone, which the export reports as missing.</item>
/// </list>
/// </remarks>
internal sealed class FullSite : IDisposable
{
    private FullSite(TemplatesWorld world) => World = world;

    public TemplatesWorld World { get; }

    public QuickBuilderApp App => World.App;

    public HttpClient Admin => World.Admin;

    public Guid Site => World.Site;

    public Guid Person { get; private set; }

    public Guid Post { get; private set; }

    public Guid Header { get; private set; }

    public Guid About { get; private set; }

    public Guid Team { get; private set; }

    public Guid Blog { get; private set; }

    public Guid Template { get; private set; }

    public Guid Menu { get; private set; }

    public Guid Redirect { get; private set; }

    public Guid MissingImage { get; private set; }

    public static async Task<FullSite> CreateAsync(DatabaseFixture database, string label)
    {
        var site = new FullSite(await TemplatesWorld.CreateAsync(database, label).ConfigureAwait(false));
        await site.SeedAsync().ConfigureAwait(false);
        return site;
    }

    private async Task SeedAsync()
    {
        var items = World.Items;

        Person = ItemsApi.Id(await World.PublishedPersonAsync("Ann").ConfigureAwait(false));
        var post = await World.PublishedPostAsync("hello", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Hello",
            ["f6"] = new JsonObject { ["mediaId"] = items.Image.ToString() },
            ["f7"] = new JsonObject { ["mediaId"] = items.Pdf.ToString() },
            ["f9"] = Person.ToString(),
        }).ConfigureAwait(false);
        Post = ItemsApi.Id(post);
        await ItemsApi.SaveOkAsync(Admin, Site, World.Posts, Post, ItemsApi.DraftOf("hello", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Hello, again",
            ["f9"] = Person.ToString(),
        }, items.PostsSchemaRevision, ItemsApi.DraftRevision(post))).ConfigureAwait(false);

        Header = (await PagesApi.CreateComponentOkAsync(Admin, Site, "Header").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var headerPath = PublishingApi.Subject(Site, "component", Header);
        await PublishingApi.SaveAsync(Admin, headerPath, PublishingApi.ComponentDocument("Acme")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(Admin, headerPath).ConfigureAwait(false);

        About = await World.PageAsync("about").ConfigureAwait(false);
        Team = await World.PageAsync("team", About).ConfigureAwait(false);
        var aboutPath = PagesApi.PagePath(Site, About);
        await World.PublishDocumentAsync(About, PublishingApi.HeadingDocument("About us", Header)).ConfigureAwait(false);
        await World.PublishDocumentAsync(About, PublishingApi.HeadingDocument("About Acme", Header)).ConfigureAwait(false);
        await PublishingApi.SaveAsync(Admin, aboutPath, PublishingApi.HeadingDocument("About Acme (draft)", Header)).ConfigureAwait(false);

        var teamPath = PagesApi.PagePath(Site, Team);
        await PublishingApi.SaveAsync(Admin, teamPath, PublishingApi.HeadingDocument("Team")).ConfigureAwait(false);
        var teamDraft = await PublishingApi.DraftVersionAsync(Admin, teamPath).ConfigureAwait(false);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(Admin, teamPath, "publish", DateTimeOffset.UtcNow.AddDays(2), teamDraft).ConfigureAwait(false))
            .ConfigureAwait(false);

        Blog = await World.PageAsync("blog").ConfigureAwait(false);
        Template = await World.PageAsync("post", Blog).ConfigureAwait(false);
        await World.TemplateOkAsync(Template, World.Posts).ConfigureAwait(false);

        Menu = await MenusApi.CreateIdAsync(Admin, Site, "Main").ConfigureAwait(false);
        await MenusApi.SaveCurrentDraftOkAsync(Admin, Site, Menu, MenusApi.Document(MenusApi.Entry("about", MenusApi.PageTarget(About)))).ConfigureAwait(false);
        var menuDraft = (await MenusApi.GetOkAsync(Admin, Site, Menu).ConfigureAwait(false)).GetProperty("draftVersion").GetUInt32();
        await PagesApi.OkAsync(await PublishingApi.PublishAsync(Admin, MenusApi.MenuPath(Site, Menu), menuDraft).ConfigureAwait(false)).ConfigureAwait(false);

        Redirect = (await RedirectsApi.CreateOkAsync(Admin, Site, "/old-about", RedirectsApi.ToPage(About)).ConfigureAwait(false)).GetProperty("id").GetGuid();

        var tokens = $"/api/v1/editor/sites/{Site}/design-tokens";
        await EditorApi.SaveCurrentAsync(Admin, Site, EditorApi.DefaultWithColor("brand", "Brand")).ConfigureAwait(false);
        var design = await EditorApi.GetOkAsync(Admin, Site).ConfigureAwait(false);
        await PagesApi.OkAsync(await Admin.PostAsJsonAsync(new Uri($"{tokens}/publish", UriKind.Relative), new { version = design.GetProperty("version").GetUInt32() }).ConfigureAwait(false))
            .ConfigureAwait(false);

        MissingImage = (await MediaApi.UploadOkAsync(Admin, Site).ConfigureAwait(false)).Id;
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFileStorage>().DeleteAsync(MediaStorageKeys.For(Site, MissingImage)).ConfigureAwait(false);
    }

    /// <summary>The counts an export of this site must report (FR-009).</summary>
    public static IReadOnlyDictionary<string, long> ExpectedCounts { get; } = new Dictionary<string, long>
    {
        ["page"] = 4,
        ["component"] = 1,
        ["collection"] = 2,
        ["item"] = 2,
        ["menu"] = 1,
        ["redirect"] = 1,
        ["media_file"] = 3,
    };

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public void Dispose() => World.Dispose();
}
