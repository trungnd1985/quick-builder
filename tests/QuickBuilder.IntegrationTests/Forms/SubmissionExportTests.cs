using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// Exporting submissions (018 FR-029, FR-030, SC-007; research FS-16): the filtered inbox as
/// CSV, columns by label across forms, values made safe, and the export recorded without the
/// search words.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SubmissionExportTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_export_is_the_filtered_inbox_with_a_column_per_label_and_is_recorded()
    {
        using var world = await CreateAsync(database, "subs-export").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        var message = """{"id":"m","type":"form","props":{"name":"Message form","fields":[{"id":"e00000000001","type":"email","label":"Email"},{"id":"a00000000002","type":"textarea","label":"Message"},{"id":"a00000000003","type":"checkboxes","label":"Topics","options":[{"value":"x","label":"X"},{"value":"y","label":"Y"}]}]},"children":[]}""";
        await world.PublishPageAsync(contact, Document(Form("contact", "Contact"), message)).ConfigureAwait(true);
        await world.DeliverEventsAsync().ConfigureAwait(true);

        await world.SubmitAsync("contact", contact, "/contact", [new("f.e00000000001", "anna@example.com"), new("f.f00000000001", "=HYPERLINK(\"http://evil\")")], json: true).ConfigureAwait(true);
        await world.SubmitAsync("m", contact, "/contact",
            [new("f.e00000000001", "bao@example.com"), new("f.a00000000002", "Xin chào,\n\"thế giới\" 😀"), new("f.a00000000003", "y"), new("f.a00000000003", "x")], json: true).ConfigureAwait(true);
        await world.WithFormsAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO forms.submissions (id, site_id, form_id, form_name, page_id, page_path, received_at_utc, values, search_text, fingerprint, is_read, is_spam, notification_state) " +
            "SELECT gen_random_uuid(), site_id, form_id, form_name, page_id, page_path, received_at_utc, values, search_text, fingerprint, false, true, 'none' FROM forms.submissions WHERE form_id = 'contact'")).ConfigureAwait(true);

        var response = await world.Admin.GetAsync(new Uri($"/api/v1/forms/sites/{world.Site}/submissions/export?q=example", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.StartsWith("attachment; filename=\"submissions-cms-a-test-", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");

        // Newest first; labels in order of first appearance; Email shared by both forms; spam left out.
        Assert.Equal("Received (UTC),Form,Page,Email,Message,Topics,Name", lines[0]);
        Assert.EndsWith(",Message form,/contact,bao@example.com,\"Xin chào,\n\"\"thế giới\"\" 😀\",x; y,", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",Contact,/contact,anna@example.com,,,\"'=HYPERLINK(\"\"http://evil\"\")\"", lines[2], StringComparison.Ordinal);
        Assert.Equal([""], lines[3..]);

        var entry = await world.WithFormsAsync(db => db.ForSite<QuickBuilder.Forms.Domain.SubmissionLogEntry>(world.Site).AsNoTracking().SingleAsync(e => e.Action == "export")).ConfigureAwait(true);
        Assert.Equal(2, entry.Count);
        using var filters = System.Text.Json.JsonDocument.Parse(entry.Filters!);
        Assert.True(filters.RootElement.GetProperty("searchUsed").GetBoolean());
        Assert.DoesNotContain("example", entry.Filters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filters_narrow_the_export_and_a_bad_filter_is_refused_before_any_byte()
    {
        using var world = await CreateAsync(database, "subs-export-filter").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(Form("one", "One"), Form("two", "Two"))).ConfigureAwait(true);
        await world.SubmitAsync("one", contact, "/contact", [new("f.e00000000001", "a@b.co")], json: true).ConfigureAwait(true);
        await world.SubmitAsync("two", contact, "/contact", [new("f.e00000000001", "c@d.co")], json: true).ConfigureAwait(true);

        var only = await world.Admin.GetStringAsync(new Uri($"/api/v1/forms/sites/{world.Site}/submissions/export?formId=two", UriKind.Relative)).ConfigureAwait(true);
        var refused = await world.Admin.GetAsync(new Uri($"/api/v1/forms/sites/{world.Site}/submissions/export?read=maybe", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(3, only.Split("\r\n").Length);
        Assert.Contains("c@d.co", only, StringComparison.Ordinal);
        Assert.DoesNotContain("a@b.co", only, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
    }
}
