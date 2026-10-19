using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Rendering;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// <c>GET …/menus/references</c> (024 US4, FR-026; tasks T059): which menus point at a page or
/// an item, in their draft, their live version, or both, for the warnings before that page or
/// item is deleted or unpublished.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MenuReferencesTests(DatabaseFixture database)
{
    [Fact]
    public async Task Drafts_and_live_versions_are_told_apart_for_pages_and_items()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-refs").ConfigureAwait(true);
        var services = await world.PageAsync("services").ConfigureAwait(true);
        await world.PublishDocumentAsync(services, PublishingApi.HeadingDocument("Services")).ConfigureAwait(true);
        var post = ItemsApi.Id(await world.PublishedPostAsync("hello").ConfigureAwait(true));

        // Main: services live and in the draft. Footer: services only in the draft, the post live only.
        var main = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, main, Document(Entry("s", PageTarget(services)))).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, main).ConfigureAwait(true);

        var footer = await CreateIdAsync(world.Admin, world.Site, "Footer").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, footer, Document(Entry("p", ItemTarget(world.Posts, post), label: "Hello"))).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, footer).ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, footer, Document(Entry("s", PageTarget(services)))).ConfigureAwait(true);

        // A menu of another site that names the same page id is never listed.
        await CreateIdAsync(world.Admin, world.Cms.SiteB, "Elsewhere").ConfigureAwait(true);

        var page = await ReferencesOkAsync(world, $"pageId={services}").ConfigureAwait(true);
        Assert.Equal(["Footer", "Main"], page.Select(r => r.Name));
        Assert.Equal((true, false), Flags(page, footer));
        Assert.Equal((true, true), Flags(page, main));

        var item = await ReferencesOkAsync(world, $"itemId={post}").ConfigureAwait(true);
        Assert.Equal((false, true), Flags(Assert.Single(item), footer));

        Assert.Empty(await ReferencesOkAsync(world, $"pageId={Guid.NewGuid()}").ConfigureAwait(true));
    }

    [Fact]
    public async Task Copying_a_version_into_the_draft_updates_the_draft_references()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-refs-copy").ConfigureAwait(true);
        var first = await world.PageAsync("first").ConfigureAwait(true);
        var second = await world.PageAsync("second").ConfigureAwait(true);

        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", PageTarget(first), label: "First"))).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, menu).ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("b", PageTarget(second), label: "Second"))).ConfigureAwait(true);
        Assert.Equal((false, true), Flags(Assert.Single(await ReferencesOkAsync(world, $"pageId={first}").ConfigureAwait(true)), menu));

        var path = MenuPath(world.Site, menu);
        var draftVersion = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32();
        await PagesApi.OkAsync(await PublishingApi.CopyToDraftAsync(world.Admin, path, 1, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal((true, true), Flags(Assert.Single(await ReferencesOkAsync(world, $"pageId={first}").ConfigureAwait(true)), menu));
        Assert.Empty(await ReferencesOkAsync(world, $"pageId={second}").ConfigureAwait(true));
    }

    [Fact]
    public async Task Exactly_one_of_page_and_item_is_asked_about()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-refs-query").ConfigureAwait(true);

        foreach (var query in new[] { string.Empty, $"pageId={Guid.NewGuid()}&itemId={Guid.NewGuid()}" })
        {
            await PagesApi.RefusedAsync(await ReferencesAsync(world.Admin, world.Site, query).ConfigureAwait(true), HttpStatusCode.BadRequest, "pages.invalid_reference_query")
                .ConfigureAwait(true);
        }
    }

    private sealed record Reference(Guid MenuId, string Name, bool InDraft, bool InLive);

    private static (bool InDraft, bool InLive) Flags(IEnumerable<Reference> references, Guid menu) =>
        references.Where(r => r.MenuId == menu).Select(r => (r.InDraft, r.InLive)).Single();

    private static (bool InDraft, bool InLive) Flags(Reference reference, Guid menu)
    {
        Assert.Equal(menu, reference.MenuId);
        return (reference.InDraft, reference.InLive);
    }

    private static async Task<List<Reference>> ReferencesOkAsync(TemplatesWorld world, string query)
    {
        var body = await PagesApi.OkAsync(await ReferencesAsync(world.Admin, world.Site, query).ConfigureAwait(false)).ConfigureAwait(false);
        return
        [
            .. body.GetProperty("items").EnumerateArray().Select(r => new Reference(
                r.GetProperty("menuId").GetGuid(),
                r.GetProperty("name").GetString()!,
                r.GetProperty("inDraft").GetBoolean(),
                r.GetProperty("inLive").GetBoolean())),
        ];
    }
}
