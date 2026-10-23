using System.Net;
using System.Text;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using static QuickBuilder.IntegrationTests.Forms.FormsWorld;

namespace QuickBuilder.IntegrationTests.Forms;

/// <summary>
/// <c>POST /_forms/{formId}</c> (018 US2; FR-011 to FR-015, FR-020, FR-022; research FS-06 to
/// FS-09; public-forms.md §1 to §3): every value is checked against the form as it is live,
/// with and without scripts, and only valid submissions are stored.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SubmitEndpointTests(DatabaseFixture database)
{
    private const string Email = "e00000000001";
    private const string Name = "f00000000001";

    private static KeyValuePair<string, string> Value(string fieldId, string value) => new($"f.{fieldId}", value);

    private static async Task<(FormsWorld World, Guid Contact)> ContactAsync(DatabaseFixture database, string label, string extraProps = "")
    {
        var world = await CreateAsync(database, label).ConfigureAwait(false);
        var contact = await world.PageAsync("contact").ConfigureAwait(false);
        await world.PublishPageAsync(contact, Document(Form("contact-form", "Contact", extraProps))).ConfigureAwait(false);
        return (world, contact);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task A_valid_submission_is_stored_with_its_values_labels_and_source()
    {
        var (world, contact) = await ContactAsync(database, "submit-ok").ConfigureAwait(true);
        using var _ = world;

        var response = await world.SubmitAsync("contact-form", contact, "/contact",
            [Value(Email, " jane@example.com "), Value(Name, "Jane"), Value("zz0000000000", "never stored"), new("other", "ignored")], json: true).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await JsonOf(response).ConfigureAwait(true)).GetProperty("status").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var stored = Assert.Single(await world.SubmissionsAsync().ConfigureAwait(true));
        Assert.Equal(("contact-form", "Contact", contact, "/contact", false, false), (stored.FormId, stored.FormName, stored.PageId, stored.PagePath, stored.IsRead, stored.IsSpam));
        using var values = JsonDocument.Parse(stored.Values);
        Assert.Equal(
            [(Email, "Email", "email", "jane@example.com"), (Name, "Name", "text", "Jane")],
            values.RootElement.EnumerateArray().Select(v => (v.GetProperty("fieldId").GetString(), v.GetProperty("label").GetString(), v.GetProperty("type").GetString(), v.GetProperty("value").GetString())));
        Assert.Contains("jane@example.com", stored.SearchText, StringComparison.Ordinal);
        Assert.DoesNotContain("never stored", stored.Values, StringComparison.Ordinal);
        Assert.Equal(32, stored.Fingerprint.Length);
    }

    [Fact]
    public async Task Invalid_values_are_refused_field_by_field_and_nothing_is_stored()
    {
        var (world, contact) = await ContactAsync(database, "submit-invalid").ConfigureAwait(true);
        using var _ = world;

        var json = await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, "jane@"), Value(Name, new string('x', 501))], json: true).ConfigureAwait(true);
        var missing = await world.SubmitAsync("contact-form", contact, "/contact", [], json: true).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, json.StatusCode);
        var body = await JsonOf(json).ConfigureAwait(true);
        Assert.Equal("invalid", body.GetProperty("status").GetString());
        Assert.Equal("email", body.GetProperty("errors").GetProperty(Email).GetString());
        Assert.Equal("too_long", body.GetProperty("errors").GetProperty(Name).GetString());
        Assert.Equal("required", (await JsonOf(missing).ConfigureAwait(true)).GetProperty("errors").GetProperty(Email).GetString());
        Assert.Empty(await world.SubmissionsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Without_scripts_a_refusal_is_the_page_again_with_values_and_messages_never_cached()
    {
        var (world, contact) = await ContactAsync(database, "submit-html-invalid").ConfigureAwait(true);
        using var _ = world;

        var response = await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, "jane@"), Value(Name, "Jane <b>")]).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Contains("class=\"qb-form__summary\"", html, StringComparison.Ordinal);
        Assert.Contains(">Email must be an email address.</a>", html, StringComparison.Ordinal);
        Assert.Contains("value=\"jane@\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Jane &lt;b&gt;\"", html, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        // The cached page never shows anyone the refusal.
        var page = await world.Visitor().GetStringAsync(new Uri("/contact", UriKind.Relative)).ConfigureAwait(true);
        Assert.DoesNotContain("qb-form__summary", page.Replace(QuickBuilder.Rendering.Rendering.Forms.FormScript.Element, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_scripts_a_success_redirects_to_the_success_panel_so_reloading_sends_nothing()
    {
        var (world, contact) = await ContactAsync(database, "submit-html-ok").ConfigureAwait(true);
        using var _ = world;

        var response = await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, "jane@example.com")]).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/contact#qb-sent-contact-form", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_success_goes_to_the_redirect_page_or_address_or_to_the_message_when_the_page_went_away()
    {
        using var world = await CreateAsync(database, "submit-redirect").ConfigureAwait(true);
        var thanks = await world.PageAsync("thanks").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishPageAsync(thanks, Document()).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document(
            Form("to-page", "Page", $$""","redirect":{"type":"page","pageId":"{{thanks}}"}"""),
            Form("to-url", "Url", ""","redirect":{"type":"url","href":"https://example.com/done"}"""))).ConfigureAwait(true);

        var toPage = await world.SubmitAsync("to-page", contact, "/contact", [Value(Email, "a@b.co")]).ConfigureAwait(true);
        var toPageJson = await world.SubmitAsync("to-page", contact, "/contact", [Value(Email, "c@d.co")], json: true).ConfigureAwait(true);
        var toUrl = await world.SubmitAsync("to-url", contact, "/contact", [Value(Email, "e@f.co")]).ConfigureAwait(true);
        Assert.Equal("/thanks", toPage.Headers.Location?.OriginalString);
        Assert.Equal("/thanks", (await JsonOf(toPageJson).ConfigureAwait(true)).GetProperty("redirect").GetString());
        Assert.Equal("https://example.com/done", toUrl.Headers.Location?.OriginalString);

        // Analysis M3: the redirect page was unpublished after the form went live.
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, thanks)).ConfigureAwait(true)).ConfigureAwait(true);
        var fallback = await world.SubmitAsync("to-page", contact, "/contact", [Value(Email, "g@h.co")]).ConfigureAwait(true);
        var fallbackJson = await world.SubmitAsync("to-page", contact, "/contact", [Value(Email, "i@j.co")], json: true).ConfigureAwait(true);
        Assert.Equal("/contact#qb-sent-to-page", fallback.Headers.Location?.OriginalString);
        Assert.False((await JsonOf(fallbackJson).ConfigureAwait(true)).TryGetProperty("redirect", out _));
    }

    [Fact]
    public async Task A_form_that_is_not_live_on_that_page_of_that_site_is_gone()
    {
        var (world, contact) = await ContactAsync(database, "submit-gone").ConfigureAwait(true);
        using var _ = world;
        var draftOnly = await world.PageAsync("draft-only").ConfigureAwait(true);
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, draftOnly), Document(Form("draft-form"))).ConfigureAwait(true);
        var contactB = await world.PageAsync("contact", world.SiteB).ConfigureAwait(true);
        await world.PublishPageAsync(contactB, Document(Form("b-form")), world.SiteB).ConfigureAwait(true);

        HttpResponseMessage[] responses =
        [
            await world.SubmitAsync("no-such-form", contact, "/contact", [Value(Email, "a@b.co")], json: true).ConfigureAwait(true),
            await world.SubmitAsync("draft-form", draftOnly, "/draft-only", [Value(Email, "a@b.co")], json: true).ConfigureAwait(true),
            await world.SubmitAsync("b-form", contactB, "/contact", [Value(Email, "a@b.co")], json: true).ConfigureAwait(true),
            await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, "a@b.co")], json: true, host: "unknown.test").ConfigureAwait(true),
        ];

        foreach (var response in responses.Take(3))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("gone", (await JsonOf(response).ConfigureAwait(true)).GetProperty("status").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, responses[3].StatusCode);
        Assert.Empty(await world.SubmissionsAsync().ConfigureAwait(true));
        Assert.Empty(await world.SubmissionsAsync(world.SiteB).ConfigureAwait(true));

        // Without scripts: the page it was sent from, as it is now. A form that is gone is no longer
        // on it to carry a message; a form that is there shows its error message.
        var html = await world.SubmitAsync("no-such-form", contact, "/contact", [Value(Email, "a@b.co")]).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, html.StatusCode);
        Assert.Equal("no-store", html.Headers.CacheControl?.ToString());
        Assert.Contains("id=\"qb-p.contact-form\"", await html.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);

        // public-forms.md §2.4: an address that no longer answers gets the site's not-found page.
        var nowhere = await world.SubmitAsync("no-such-form", contact, "/nowhere", [Value(Email, "a@b.co")]).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [Fact]
    public async Task A_form_in_a_component_is_recorded_with_the_page_that_showed_it_and_never_another_page()
    {
        using var world = await CreateAsync(database, "submit-component").ConfigureAwait(true);
        var footer = await world.ComponentAsync("Footer").ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        var contact = await world.PageAsync("contact").ConfigureAwait(true);
        await world.PublishComponentAsync(footer, Document(Form("footer-form"))).ConfigureAwait(true);
        await world.PublishPageAsync(about, Page([footer])).ConfigureAwait(true);
        await world.PublishPageAsync(contact, Document()).ConfigureAwait(true);

        await world.SubmitAsync("footer-form", about, "/contact", [Value(Email, "a@b.co")], json: true).ConfigureAwait(true);

        var stored = Assert.Single(await world.SubmissionsAsync().ConfigureAwait(true));
        Assert.Equal((about, "/about"), (stored.PageId, stored.PagePath));
    }

    [Fact]
    public async Task Bodies_that_are_too_large_or_not_urlencoded_and_other_methods_are_refused()
    {
        var (world, contact) = await ContactAsync(database, "submit-refused-bodies").ConfigureAwait(true);
        using var _ = world;
        using var client = world.Visitor();

        var large = await world.SubmitAsync("contact-form", contact, "/contact", [Value(Name, new string('x', 300_000))], json: true).ConfigureAwait(true);
        using var jsonBody = new StringContent("{\"f.e00000000001\":\"a@b.co\"}", Encoding.UTF8, "application/json");
        var json = await client.PostAsync(new Uri("/_forms/contact-form", UriKind.Relative), jsonBody).ConfigureAwait(true);
        var get = await client.GetAsync(new Uri("/_forms/contact-form", UriKind.Relative)).ConfigureAwait(true);

        Assert.True(large.StatusCode == HttpStatusCode.RequestEntityTooLarge, await large.Content.ReadAsStringAsync().ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal("POST", string.Join(",", get.Content.Headers.Allow));
        Assert.Empty(await world.SubmissionsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task No_submitted_value_reaches_the_logs()
    {
        var (world, contact) = await ContactAsync(database, "submit-logs").ConfigureAwait(true);
        using var _ = world;
        const string Secret = "secret-visitor-value-4242";

        await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, "jane@example.com"), Value(Name, Secret)], json: true).ConfigureAwait(true);
        await world.SubmitAsync("contact-form", contact, "/contact", [Value(Email, Secret)]).ConfigureAwait(true);

        Assert.DoesNotContain(world.App.Logs.Events, e => LogCapture.Render(e).Contains(Secret, StringComparison.Ordinal));
    }
}
