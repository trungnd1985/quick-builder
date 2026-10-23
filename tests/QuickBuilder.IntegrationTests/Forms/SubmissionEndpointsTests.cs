using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// Submissions in the admin (018 US3; FR-025 to FR-028, FR-030): list, filters, search, read
/// state, deletion by Designers and Administrators only, the forms list and the retention impact.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SubmissionEndpointsTests(DatabaseFixture database)
{
    private const string Email = "e00000000001";
    private const string Name = "f00000000001";

    private static string Submissions(Guid siteId) => $"/api/v1/forms/sites/{siteId}/submissions";

    private static async Task<(FormsWorld World, Guid Contact)> SeededAsync(DatabaseFixture database, string label)
    {
        var world = await CreateAsync(database, label).ConfigureAwait(false);
        var contact = await world.PageAsync("contact").ConfigureAwait(false);
        await world.PublishPageAsync(contact, Document(Form("contact", "Contact"), Form("callback", "Call back"))).ConfigureAwait(false);
        await world.DeliverEventsAsync().ConfigureAwait(false);

        foreach (var (form, email, name) in new[]
        {
            ("contact", "anna@example.com", "Anna, about Hosting plans"),
            ("contact", "bao@example.com", "Bảo Nguyễn wants 100% uptime_now"),
            ("callback", "chris@example.com", "Chris"),
        })
        {
            var response = await world.SubmitAsync(form, contact, "/contact", [new($"f.{Email}", email), new($"f.{Name}", name)], json: true).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        return (world, contact);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path) =>
        await PagesApi.OkAsync(await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);

    private static string[] Names(JsonElement list) =>
        [.. list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("preview").GetString()!)];

    [Fact]
    public async Task The_list_is_newest_first_with_form_page_state_and_a_preview()
    {
        var (world, _) = await SeededAsync(database, "subs-list").ConfigureAwait(true);
        using var _w = world;

        var list = await GetAsync(world.Admin, Submissions(world.Site)).ConfigureAwait(true);

        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["callback", "contact", "contact"], items.Select(i => i.GetProperty("formId").GetString()));
        var first = items[0];
        Assert.Equal(("Call back", true, "/contact", false, "not_needed"), (
            first.GetProperty("formName").GetString(), first.GetProperty("formPublished").GetBoolean(), first.GetProperty("pagePath").GetString(),
            first.GetProperty("read").GetBoolean(), first.GetProperty("notification").GetString()));
        Assert.Equal("chris@example.com · Chris", first.GetProperty("preview").GetString());
        Assert.True(list.GetProperty("permissions").GetProperty("delete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task Filters_and_search_combine_and_search_is_literal_ignoring_case_and_accents()
    {
        var (world, _) = await SeededAsync(database, "subs-filter").ConfigureAwait(true);
        using var _w = world;
        var site = Submissions(world.Site);

        Assert.Equal(2, Names(await GetAsync(world.Admin, $"{site}?formId=contact").ConfigureAwait(true)).Length);
        Assert.Single(Names(await GetAsync(world.Admin, $"{site}?q=HOSTING").ConfigureAwait(true)));
        Assert.Single(Names(await GetAsync(world.Admin, $"{site}?q=nguyen%20bao").ConfigureAwait(true)));
        Assert.Single(Names(await GetAsync(world.Admin, $"{site}?q=100%25").ConfigureAwait(true)));
        Assert.Single(Names(await GetAsync(world.Admin, $"{site}?q=uptime_now").ConfigureAwait(true)));
        Assert.Empty(Names(await GetAsync(world.Admin, $"{site}?q=uptime_x").ConfigureAwait(true)));
        Assert.Empty(Names(await GetAsync(world.Admin, $"{site}?formId=callback&q=anna").ConfigureAwait(true)));
        Assert.Empty(Names(await GetAsync(world.Admin, $"{site}?from={DateTimeOffset.UtcNow.AddDays(1):O}".Replace("+", "%2B", StringComparison.Ordinal)).ConfigureAwait(true)));
        Assert.Equal(3, Names(await GetAsync(world.Admin, $"{site}?read=unread").ConfigureAwait(true)).Length);

        var tooLong = await world.Admin.GetAsync(new Uri($"{site}?q={new string('x', 201)}", UriKind.Relative)).ConfigureAwait(true);
        await PagesApi.RefusedAsync(tooLong, HttpStatusCode.BadRequest, "forms.invalid_filter").ConfigureAwait(true);
    }

    [Fact]
    public async Task Pages_follow_the_cursor_without_gaps_or_repeats()
    {
        var (world, contact) = await SeededAsync(database, "subs-paging").ConfigureAwait(true);
        using var _w = world;
        for (var i = 0; i < 52; i++)
        {
            (await world.SubmitAsync("contact", contact, "/contact", [new($"f.{Email}", $"p{i}@example.com")], json: true).ConfigureAwait(true)).EnsureSuccessStatusCode();
        }

        var first = await GetAsync(world.Admin, Submissions(world.Site)).ConfigureAwait(true);
        var next = first.GetProperty("next").GetString();
        var second = await GetAsync(world.Admin, $"{Submissions(world.Site)}?after={next}").ConfigureAwait(true);

        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        Assert.Equal(55, ids.Distinct().Count());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task Opening_marks_read_and_shows_every_value_and_read_can_be_undone()
    {
        var (world, _) = await SeededAsync(database, "subs-open").ConfigureAwait(true);
        using var _w = world;
        var id = (await world.SubmissionsAsync().ConfigureAwait(true))[0].Id;
        var one = $"{Submissions(world.Site)}/{id}";

        var detail = await GetAsync(world.Admin, one).ConfigureAwait(true);
        Assert.Equal(
            [(Email, "Email", "anna@example.com"), (Name, "Name", "Anna, about Hosting plans")],
            detail.GetProperty("values").EnumerateArray().Select(v => (v.GetProperty("fieldId").GetString(), v.GetProperty("label").GetString(), v.GetProperty("value").GetString())));
        Assert.True(detail.GetProperty("read").GetBoolean());
        Assert.Equal(2, (await GetAsync(world.Admin, $"{Submissions(world.Site)}/unread-count").ConfigureAwait(true)).GetProperty("count").GetInt32());

        var unread = await world.Admin.PutAsJsonAsync(new Uri($"{one}/read", UriKind.Relative), new { read = false }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, unread.StatusCode);
        Assert.Equal(3, (await GetAsync(world.Admin, $"{Submissions(world.Site)}/unread-count").ConfigureAwait(true)).GetProperty("count").GetInt32());

        var missing = await world.Admin.GetAsync(new Uri($"{Submissions(world.Site)}/{Guid.NewGuid()}", UriKind.Relative)).ConfigureAwait(true);
        await PagesApi.RefusedAsync(missing, HttpStatusCode.NotFound, "forms.submission_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Designers_delete_and_it_is_recorded_while_editors_read_and_export_but_never_delete()
    {
        var (world, _) = await SeededAsync(database, "subs-delete").ConfigureAwait(true);
        using var _w = world;
        var designer = await world.UserAsync((world.Site, SystemRole.Designer)).ConfigureAwait(true);
        var editor = await world.UserAsync((world.Site, SystemRole.Editor)).ConfigureAwait(true);
        var stored = await world.SubmissionsAsync().ConfigureAwait(true);

        Assert.False((await GetAsync(editor, Submissions(world.Site)).ConfigureAwait(true)).GetProperty("permissions").GetProperty("delete").GetBoolean());
        var refused = await editor.SendAsync(Delete(Submissions(world.Site), [stored[0].Id])).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync(new Uri($"{Submissions(world.Site)}/export", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var deleted = await PagesApi.OkAsync(await designer.SendAsync(Delete(Submissions(world.Site), [stored[0].Id, stored[1].Id, Guid.NewGuid()])).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(2, deleted.GetProperty("deleted").GetInt32());
        Assert.Single(await world.SubmissionsAsync().ConfigureAwait(true));

        var log = await world.WithFormsAsync(db => db.ForSite<QuickBuilder.Forms.Domain.SubmissionLogEntry>(world.Site).AsNoTracking().Where(e => e.Action == "delete").ToListAsync()).ConfigureAwait(true);
        var entry = Assert.Single(log);
        Assert.Equal(2, entry.Count);
        Assert.NotNull(entry.ActorUserId);
    }

    [Fact]
    public async Task Another_sites_member_gets_nothing_and_another_sites_ids_are_not_deleted()
    {
        var (world, _) = await SeededAsync(database, "subs-sites").ConfigureAwait(true);
        using var _w = world;
        var other = await world.UserAsync((world.SiteB, SystemRole.Designer)).ConfigureAwait(true);
        var id = (await world.SubmissionsAsync().ConfigureAwait(true))[0].Id;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri(Submissions(world.Site), UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"{Submissions(world.Site)}/export", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"{Submissions(world.Site)}/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var deleted = await PagesApi.OkAsync(await world.Admin.SendAsync(Delete(Submissions(world.SiteB), [id])).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, deleted.GetProperty("deleted").GetInt32());
        Assert.Equal(3, (await world.SubmissionsAsync().ConfigureAwait(true)).Count);
    }

    [Fact]
    public async Task The_forms_list_and_the_retention_impact_come_from_the_registry_and_the_dates()
    {
        var (world, _) = await SeededAsync(database, "subs-forms").ConfigureAwait(true);
        using var _w = world;
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE forms.submissions SET received_at_utc = now() - interval '40 days' WHERE form_id = 'contact'")).ConfigureAwait(true);

        var forms = await GetAsync(world.Admin, $"/api/v1/forms/sites/{world.Site}/forms").ConfigureAwait(true);
        Assert.Equal(["Call back", "Contact"], forms.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(2, forms.GetProperty("items")[0].GetProperty("fields").GetArrayLength());

        var impact = $"/api/v1/forms/sites/{world.Site}/forms/contact/retention-impact";
        Assert.Equal(2, (await GetAsync(world.Admin, $"{impact}?days=30").ConfigureAwait(true)).GetProperty("count").GetInt32());
        Assert.Equal(0, (await GetAsync(world.Admin, $"{impact}?days=90").ConfigureAwait(true)).GetProperty("count").GetInt32());
        await PagesApi.RefusedAsync(await world.Admin.GetAsync(new Uri($"{impact}?days=60", UriKind.Relative)).ConfigureAwait(true), HttpStatusCode.BadRequest, "forms.invalid_filter").ConfigureAwait(true);
    }

    private static HttpRequestMessage Delete(string path, Guid[] ids) =>
        new(HttpMethod.Delete, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { ids }) };
}
