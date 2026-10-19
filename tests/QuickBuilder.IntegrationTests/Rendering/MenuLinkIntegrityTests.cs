using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// No published page links a visitor to a target that is not live (024 SC-003, FR-022 to
/// FR-024; tasks T058), whatever state each target is in, and an unpublished parent whose
/// children are still shown is written as text with the title visitors last saw.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class MenuLinkIntegrityTests(DatabaseFixture database)
{
    [Fact]
    public async Task Only_live_targets_are_linked_and_an_unpublished_parent_stays_as_text()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "nav-integrity").ConfigureAwait(true);

        // Live, and live after a move.
        var live = await world.PageAsync("live").ConfigureAwait(true);
        await world.PublishDocumentAsync(live, PublishingApi.HeadingDocument("Live")).ConfigureAwait(true);
        var movedParent = await world.PageAsync("area").ConfigureAwait(true);
        await world.PublishDocumentAsync(movedParent, PublishingApi.HeadingDocument("Area")).ConfigureAwait(true);
        var moved = await world.PageAsync("moved").ConfigureAwait(true);
        await world.PublishDocumentAsync(moved, PublishingApi.HeadingDocument("Moved")).ConfigureAwait(true);

        // Never published, scheduled for later, deleted, and published then unpublished.
        var draftOnly = await world.PageAsync("draft-only").ConfigureAwait(true);
        var scheduled = await world.PageAsync("scheduled").ConfigureAwait(true);
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, scheduled), PublishingApi.HeadingDocument("Scheduled")).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(
            world.Admin, PagesApi.PagePath(world.Site, scheduled), "publish", DateTimeOffset.UtcNow.AddDays(2),
            await PublishingApi.DraftVersionAsync(world.Admin, PagesApi.PagePath(world.Site, scheduled)).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var deleted = await world.PageAsync("deleted").ConfigureAwait(true);
        var parent = await world.PageAsync("parent").ConfigureAwait(true);
        await world.PublishDocumentAsync(parent, PublishingApi.HeadingDocument("Parent")).ConfigureAwait(true);

        // Items: one with a live template, one of a collection without a template.
        var blog = await world.PageAsync("blog").ConfigureAwait(true);
        await world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await world.PageAsync("post", blog).ConfigureAwait(true);
        await world.TemplateOkAsync(template, world.Posts).ConfigureAwait(true);
        await world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post")).ConfigureAwait(true);
        var post = ItemsApi.Id(await world.PublishedPostAsync("hello").ConfigureAwait(true));
        var person = ItemsApi.Id(await world.PublishedPersonAsync("Ada").ConfigureAwait(true));

        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(
            Entry("live", PageTarget(live, anchor: "top")),
            Entry("moved", PageTarget(moved)),
            Entry("draft-only", PageTarget(draftOnly), label: "Draft only"),
            Entry("scheduled", PageTarget(scheduled), label: "Scheduled"),
            Entry("deleted", PageTarget(deleted), label: "Deleted"),
            Entry("parent", PageTarget(parent), children: [Entry("child", PageTarget(live), label: "Child of parent")]),
            Entry("post", ItemTarget(world.Posts, post), label: "Hello post"),
            Entry("person", ItemTarget(world.People, person), label: "Ada person"),
            Entry("external", UrlTarget("https://status.example/"), label: "Status"),
            Entry("anchor", AnchorTarget("contact"), label: "Contact"))).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, menu).ConfigureAwait(true);

        // Now the states change under the live menu.
        await PagesApi.OkAsync(await PagesApi.MoveAsync(world.Admin, world.Site, moved, movedParent, null, await VersionAsync(world, moved).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        using (var removed = await PagesApi.DeleteAsync(world.Admin, world.Site, deleted, await VersionAsync(world, deleted).ConfigureAwait(true)).ConfigureAwait(true))
        {
            Assert.True(removed.IsSuccessStatusCode, $"Delete gave {(int)removed.StatusCode}");
        }

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, parent)).ConfigureAwait(true)).ConfigureAwait(true);

        var shower = await world.PageAsync("home").ConfigureAwait(true);
        await world.PublishDocumentAsync(shower, PageShowing(menu)).ConfigureAwait(true);
        var html = (await NavigationRenderTests.GetAsync(world, "/home").ConfigureAwait(true)).Body;
        var nav = html[html.IndexOf("<nav class=\"qb-nav", StringComparison.Ordinal)..(html.IndexOf("</nav>", StringComparison.Ordinal) + 6)];

        // Every link goes somewhere live.
        var hrefs = Href().Matches(nav).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "/live#top", "/area/moved", "/blog/hello", "https://status.example/", "#contact", "/live" },
            hrefs);

        // Entries whose target is not live are not shown at all...
        Assert.DoesNotContain("Draft only", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduled", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Deleted", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Ada person", nav, StringComparison.Ordinal);

        // ...except a parent with shown children: its last title, as text, and its children linked.
        Assert.Contains(PagesApi.Title("parent"), nav, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/parent\"", nav, StringComparison.Ordinal);
        Assert.Contains(">Child of parent</a>", nav, StringComparison.Ordinal);
    }

    private static async Task<int> VersionAsync(TemplatesWorld world, Guid pageId) =>
        (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, pageId).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("version").GetInt32();

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex Href();
}
