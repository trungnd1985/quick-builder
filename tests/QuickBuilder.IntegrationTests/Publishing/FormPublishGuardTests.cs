using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Forms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// A form reaches visitors complete and pointing only at live pages (page-schema.md §3 rule 28;
/// 018 FR-008, FR-039; research FS-04): by hand, by rollback and by schedule, for pages and
/// components alike. A draft may be incomplete; only publishing refuses it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormPublishGuardTests(DatabaseFixture database)
{
    private const string NoFields = """{"id":"empty-form","type":"form","props":{"name":"Empty","fields":[]},"children":[]}""";

    [Fact]
    public async Task An_incomplete_form_saves_but_does_not_publish_and_the_refusal_names_it()
    {
        using var world = await CreateAsync(database, "form-guard-page").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var path = PagesApi.PagePath(world.Site, contact);
        await PublishingApi.SaveAsync(world.Admin, path, Document(Form("ok-form"), NoFields)).ConfigureAwait(true);

        var refused = await PagesApi.RefusedAsync(
                await PublishingApi.PublishAsync(world.Admin, path, await PublishingApi.DraftVersionAsync(world.Admin, path).ConfigureAwait(true)).ConfigureAwait(true),
                HttpStatusCode.Conflict,
                "pages.form_invalid")
            .ConfigureAwait(true);

        Assert.Equal("empty-form", refused.Body.GetProperty("formId").GetString());
        Assert.Equal("no_fields", refused.Body.GetProperty("rule").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, refused.Body.GetProperty("fieldId").ValueKind);
    }

    [Fact]
    public async Task A_field_problem_names_its_field_and_the_same_holds_for_components()
    {
        using var world = await CreateAsync(database, "form-guard-component").ConfigureAwait(true);
        var footer = await world.ComponentAsync("Footer").ConfigureAwait(true);
        var path = PagesApi.ComponentPath(world.Site, footer);
        var duplicateOption = """{"id":"pick","type":"form","props":{"name":"Pick","fields":[{"id":"a00000000001","type":"select","label":"Topic","options":[{"value":"x","label":"X"},{"value":"x","label":"Y"}]}]},"children":[]}""";
        await PublishingApi.SaveAsync(world.Admin, path, Document(duplicateOption)).ConfigureAwait(true);

        var refused = await PagesApi.RefusedAsync(
                await PublishingApi.PublishAsync(world.Admin, path, await PublishingApi.DraftVersionAsync(world.Admin, path).ConfigureAwait(true)).ConfigureAwait(true),
                HttpStatusCode.Conflict,
                "pages.form_invalid")
            .ConfigureAwait(true);

        Assert.Equal(("pick", "a00000000001", "duplicate_option"), (
            refused.Body.GetProperty("formId").GetString(), refused.Body.GetProperty("fieldId").GetString(), refused.Body.GetProperty("rule").GetString()));
    }

    [Fact]
    public async Task A_redirect_or_a_consent_link_to_a_page_that_is_not_live_is_refused()
    {
        using var world = await CreateAsync(database, "form-guard-redirect").ConfigureAwait(true);
        var thanks = await world.PageAsync("thanks").ConfigureAwait(true);
        var privacy = await world.PageAsync("privacy").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var path = PagesApi.PagePath(world.Site, contact);

        await PublishingApi.SaveAsync(world.Admin, path, Document(Form("redirecting", extraProps: $$""","redirect":{"type":"page","pageId":"{{thanks}}"}"""))).ConfigureAwait(true);
        var redirect = await PagesApi.RefusedAsync(
                await PublishingApi.PublishAsync(world.Admin, path, await PublishingApi.DraftVersionAsync(world.Admin, path).ConfigureAwait(true)).ConfigureAwait(true),
                HttpStatusCode.Conflict,
                "pages.form_redirect_not_live")
            .ConfigureAwait(true);
        Assert.Equal(("redirecting", thanks), (redirect.Body.GetProperty("formId").GetString(), redirect.Body.GetProperty("pageId").GetGuid()));

        var consent = $$"""{"id":"consenting","type":"form","props":{"name":"C","fields":[{"id":"a00000000001","type":"consent","label":"Consent","statement":"I agree.","linkPageId":"{{privacy}}"}]},"children":[]}""";
        await PublishingApi.SaveAsync(world.Admin, path, Document(consent)).ConfigureAwait(true);
        var link = await PagesApi.RefusedAsync(
                await PublishingApi.PublishAsync(world.Admin, path, await PublishingApi.DraftVersionAsync(world.Admin, path).ConfigureAwait(true)).ConfigureAwait(true),
                HttpStatusCode.Conflict,
                "pages.form_redirect_not_live")
            .ConfigureAwait(true);
        Assert.Equal(privacy, link.Body.GetProperty("pageId").GetGuid());

        // Once the pages are live, both publish.
        await world.PublishPageAsync(thanks, Document()).ConfigureAwait(true);
        await world.PublishPageAsync(privacy, Document()).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(consent, Form("redirecting", extraProps: $$""","redirect":{"type":"page","pageId":"{{thanks}}"}"""))).ConfigureAwait(true);
    }

    [Fact]
    public async Task Rolling_back_to_a_version_whose_redirect_page_went_away_is_refused()
    {
        using var world = await CreateAsync(database, "form-guard-rollback").ConfigureAwait(true);
        var thanks = await world.PageAsync("thanks").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(thanks, Document()).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("f", extraProps: $$""","redirect":{"type":"page","pageId":"{{thanks}}"}"""))).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("f"))).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, thanks)).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
                await PublishingApi.RollbackAsync(world.Admin, PagesApi.PagePath(world.Site, contact), 1).ConfigureAwait(true),
                HttpStatusCode.Conflict,
                "pages.form_redirect_not_live")
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task A_scheduled_publish_of_an_incomplete_form_fails_with_the_reason()
    {
        using var world = await CreateAsync(database, "form-guard-schedule").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var path = PagesApi.PagePath(world.Site, contact);
        await PublishingApi.SaveAsync(world.Admin, path, Document(NoFields)).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(world.Admin, path).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(world.Admin, path, "publish", DateTimeOffset.UtcNow.AddHours(1), draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        await using (var scope = world.App.Services.CreateAsyncScope())
        {
            var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
            await pages.Database.ExecuteSqlRawAsync(
                    "UPDATE pages.outbox_messages SET available_at_utc = now() - interval '1 second' WHERE event_type = 'Pages.PublishScheduleDue' AND completed_at_utc IS NULL")
                .ConfigureAwait(true);
        }

        await world.DeliverEventsAsync().ConfigureAwait(true);

        await using (var scope = world.App.Services.CreateAsyncScope())
        {
            var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
            var schedule = await PagesScenario.Everything<PublishSchedule>(pages).SingleAsync(s => s.SubjectId == contact).ConfigureAwait(true);
            Assert.Equal((ScheduleStates.Failed, "pages.form_invalid"), (schedule.State, schedule.FailureCode));
        }
    }
}
