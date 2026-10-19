using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Rendering;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// Menu names, labels, anchors and addresses never reach the logs or the menu audit, whatever
/// 024 does with them: writes and refusals, publishing and its guards, previews and references,
/// the item refresh and rendering (024 tasks T077; constitution Principle IX).
/// </summary>
/// <remarks>
/// Every name, label, anchor and address written here carries the canary marker, so one scan of
/// every captured log event, and of every audit row, finds any that leaked.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MenuContentNotLoggedTests(DatabaseFixture database)
{
    [Fact]
    public async Task No_menu_name_label_anchor_or_address_appears_in_logs_or_the_audit()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-not-logged").ConfigureAwait(true);
        var marker = PagesApi.CanaryMarker;
        var services = await world.PageAsync("services").ConfigureAwait(true);
        await world.PublishDocumentAsync(services, PublishingApi.HeadingDocument("Services")).ConfigureAwait(true);
        var post = ItemsApi.Id(await world.PublishedPostAsync("hello").ConfigureAwait(true));

        // Writes, and refusals that echo nothing back into the logs.
        var menu = await CreateIdAsync(world.Admin, world.Site, $"{marker}Main").ConfigureAwait(true);
        await CreateAsync(world.Admin, world.Site, $"{marker}Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(
            Entry("page", PageTarget(services, anchor: $"{marker}team"), label: $"{marker}Team"),
            Entry("item", ItemTarget(world.Posts, post), label: $"{marker}Post"),
            Entry("url", UrlTarget($"https://{marker}docs.example/"), label: $"{marker}Docs"),
            Entry("anchor", AnchorTarget($"{marker}contact"), label: $"{marker}Contact"))).ConfigureAwait(true);
        await SaveCurrentDraftAsync(world.Admin, world.Site, menu, Document(
            Entry("bad", UrlTarget($"javascript:{marker}alert(1)"), label: $"{marker}Bad"))).ConfigureAwait(true);
        var version = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await RenameAsync(world.Admin, world.Site, menu, $"{marker}Primary", version).ConfigureAwait(true);
        var copy = (await CreateOkAsync(world.Admin, world.Site, $"{marker}Copy", duplicateOf: menu).ConfigureAwait(true)).GetProperty("id").GetGuid();

        // Publishing, its guards, and what the editor reads.
        var page = await world.PageAsync("home").ConfigureAwait(true);
        var pagePath = PagesApi.PagePath(world.Site, page);
        await PublishingApi.SaveAsync(world.Admin, pagePath, PageShowing(menu)).ConfigureAwait(true);
        await PublishingApi.PublishAsync(world.Admin, pagePath, await PublishingApi.DraftVersionAsync(world.Admin, pagePath).ConfigureAwait(true)).ConfigureAwait(true);
        await NavigationRenderTests.PublishMenuAsync(world, menu).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(world.Admin, pagePath).ConfigureAwait(true);
        await PublishingApi.UnpublishAsync(world.Admin, MenuPath(world.Site, menu)).ConfigureAwait(true);
        await PreviewAsync(world.Admin, world.Site, menu).ConfigureAwait(true);
        await UsagesAsync(world.Admin, world.Site, menu).ConfigureAwait(true);
        await ReferencesAsync(world.Admin, world.Site, $"pageId={services}").ConfigureAwait(true);
        await PublishingApi.HistoryAsync(world.Admin, MenuPath(world.Site, menu)).ConfigureAwait(true);

        // The item refresh and rendering.
        await world.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, world.Site, world.Posts, post, 2, "manual", false, null)).ConfigureAwait(true);
        await world.VisitAsync("/home").ConfigureAwait(true);

        // Deleting: one refused while live content shows it, one that goes.
        var copyVersion = (await GetOkAsync(world.Admin, world.Site, copy).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await DeleteAsync(world.Admin, world.Site, copy, copyVersion).ConfigureAwait(true);
        version = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await DeleteAsync(world.Admin, world.Site, menu, version).ConfigureAwait(true);

        var events = world.App.Logs.Events.Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);
        var leaked = events.FirstOrDefault(e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        Assert.True(leaked is null, $"A menu name, label, anchor or address reached the logs: {leaked}");

        // The audit holds ids, actions, actors and times only: its one text column is the action.
        await using var scope = world.App.Services.CreateAsyncScope();
        var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        var audit = await pages.ForSite<MenuChange>(world.Site).Select(c => c.Action).ToListAsync().ConfigureAwait(true);
        Assert.Contains(MenuChangeActions.Deleted, audit);
        Assert.All(audit, action => Assert.Contains(action, MenuChangeActions.All));
        var textColumns = await pages.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'pages' AND table_name = 'menu_changes' AND data_type IN ('text', 'character varying', 'citext', 'jsonb', 'USER-DEFINED')")
            .ToListAsync().ConfigureAwait(true);
        Assert.Equal(["action"], textColumns);
    }
}
