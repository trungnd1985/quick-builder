using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Shared.Kernel.Events;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// The form registry follows publishing (018 research FS-05; data-model §1, <c>forms</c> and
/// <c>form_placements</c>): what is live, where, and the last published settings of every form.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormRegistryTests(DatabaseFixture database)
{
    [Fact]
    public async Task Publishing_a_page_and_a_component_records_their_forms_and_where_they_are_live()
    {
        using var world = await CreateAsync(database, "forms-registry").ConfigureAwait(true);
        var footer = await world.ComponentAsync("Footer").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);

        await world.PublishComponentAsync(footer, Document(Form("footer-form", "Question"))).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Page([footer], Form("contact-form", "Contact", ""","retentionDays":90"""), Form("call-back", "Call back"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        Assert.Equal(
            [("call-back", "Call back", null), ("contact-form", "Contact", 90), ("footer-form", "Question", null)],
            await world.FormsAsync().ConfigureAwait(true));
        Assert.Equal(
            [("page", contact, "call-back"), ("page", contact, "contact-form"), ("component", footer, "footer-form")],
            await world.PlacementsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_form_left_out_of_a_republish_loses_its_placement_but_keeps_its_last_settings()
    {
        using var world = await CreateAsync(database, "forms-registry-removed").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);

        await world.PublishPageAsync(contact, Document(Form("a", "First", ""","retentionDays":30"""), Form("b", "Second"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("b", "Second, renamed"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        Assert.Equal([("a", "First", 30), ("b", "Second, renamed", null)], await world.FormsAsync().ConfigureAwait(true));
        Assert.Equal([("page", contact, "b")], await world.PlacementsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Rollback_restores_the_rolled_back_versions_forms_and_unpublish_removes_them()
    {
        using var world = await CreateAsync(database, "forms-registry-rollback").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var path = PagesApi.PagePath(world.Site, contact);

        await world.PublishPageAsync(contact, Document(Form("a", "Version one", ""","retentionDays":365"""))).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("b", "Version two"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        Assert.Equal([("page", contact, "b")], await world.PlacementsAsync().ConfigureAwait(true));

        await PagesApi.OkAsync(await PublishingApi.RollbackAsync(world.Admin, path, 1).ConfigureAwait(true)).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        Assert.Equal([("page", contact, "a")], await world.PlacementsAsync().ConfigureAwait(true));
        Assert.Contains(("a", "Version one", 365), await world.FormsAsync().ConfigureAwait(true));

        var unpublished = await PublishingApi.UnpublishAsync(world.Admin, path).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        Assert.Empty(await world.PlacementsAsync().ConfigureAwait(true));
        Assert.Equal(2, (await world.FormsAsync().ConfigureAwait(true)).Count);
    }

    [Fact]
    public async Task The_same_event_handled_twice_writes_the_same_rows()
    {
        using var world = await CreateAsync(database, "forms-registry-twice").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("a"))).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        // A redelivery, as at-least-once delivery may make (001 FR-016).
        var published = new PagePublished(Guid.NewGuid(), DateTimeOffset.UtcNow, world.Site, contact, 1, "publish", "/contact", null);
        await using (var scope = world.App.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<PagePublished>>();
            await handler.HandleAsync(published, CancellationToken.None).ConfigureAwait(true);
            await handler.HandleAsync(published, CancellationToken.None).ConfigureAwait(true);
        }

        Assert.Equal([("a", "Contact", null)], await world.FormsAsync().ConfigureAwait(true));
        Assert.Equal([("page", contact, "a")], await world.PlacementsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Another_sites_publishes_never_touch_this_sites_rows()
    {
        using var world = await CreateAsync(database, "forms-registry-sites").ConfigureAwait(true);
        var contactA = await world.PageAsync("contact").ConfigureAwait(true);
        var contactB = await world.PageAsync("contact", world.SiteB).ConfigureAwait(true);

        await world.PublishPageAsync(contactA, Document(Form("shared-id", "Site A form"))).ConfigureAwait(true);
        await world.PublishPageAsync(contactB, Document(Form("shared-id", "Site B form")), world.SiteB).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.SiteB, contactB)).ConfigureAwait(true)).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        Assert.Equal([("shared-id", "Site A form", null)], await world.FormsAsync().ConfigureAwait(true));
        Assert.Equal([("page", contactA, "shared-id")], await world.PlacementsAsync().ConfigureAwait(true));
        Assert.Equal([("shared-id", "Site B form", null)], await world.FormsAsync(world.SiteB).ConfigureAwait(true));
        Assert.Empty(await world.PlacementsAsync(world.SiteB).ConfigureAwait(true));
    }
}
