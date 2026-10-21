using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.UnitTests.Rendering.Forms;

/// <summary>
/// The Form block's HTML (018 FR-009, FR-011, FR-012, SC-008; public-forms.md §2.1). The
/// whole-page golden files of the form examples run in <see cref="RendererGoldenTests"/> and,
/// against the canvas, in <c>CanvasMarkupParity.spec.ts</c>; these pin what each field type,
/// setting and state decides.
/// </summary>
public sealed class FormGoldenTests
{
    private static readonly Guid Contact = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");
    private static readonly Guid Privacy = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e41");

    private static string Render(string fields, string props = "", string language = "en", string basePath = "")
    {
        var harness = new RenderHarness { CurrentPageId = Contact, Path = "/contact", Language = language, BasePath = basePath };
        harness.Links[Privacy] = new PageLinkTarget("/privacy", IsLive: true, LiveTitle: "Privacy policy");
        var form = $$"""{ "id": "contact-form", "type": "form", "props": { "name": "Contact", "fields": [ {{fields}} ]{{props}} }, "children": [] }""";
        var html = harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ {{form}} ] }]"""));
        return html[html.IndexOf("<div class=\"qb-form\"", StringComparison.Ordinal)..(html.LastIndexOf("</form></div>", StringComparison.Ordinal) + 13)];
    }

    [Fact]
    public void A_form_posts_to_its_own_address_and_says_which_page_sent_it()
    {
        var html = Render("""{ "id": "a00000000001", "type": "email", "label": "Email" }""", basePath: "/cms");

        Assert.StartsWith("<div class=\"qb-form\" id=\"qb-p.contact-form\">", html, StringComparison.Ordinal);
        Assert.Contains("<form class=\"qb-form__form\" method=\"post\" action=\"/cms/_forms/contact-form\" data-qb-form=\"contact-form\"", html, StringComparison.Ordinal);
        Assert.Contains($"<input type=\"hidden\" name=\"_page\" value=\"{Contact:D}\">", html, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"_path\" value=\"/contact\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_success_panel_is_the_fragment_a_submission_without_scripts_lands_on()
    {
        var html = Render("""{ "id": "a00000000001", "type": "email", "label": "Email" }""", """, "successMessage": "Thanks, we will reply." """);

        Assert.Contains("<div class=\"qb-form__sent\" id=\"qb-sent-contact-form\" role=\"status\" tabindex=\"-1\"><p>Thanks, we will reply.</p></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_like_fields_have_a_tied_label_help_and_the_browsers_own_constraints()
    {
        var html = Render("""
            { "id": "a00000000001", "type": "text", "label": "Name", "required": true, "placeholder": "Jane", "minLength": 2, "maxLength": 80, "help": "As on your passport." },
            { "id": "a00000000002", "type": "email", "label": "Email" },
            { "id": "a00000000003", "type": "phone", "label": "Phone" },
            { "id": "a00000000004", "type": "number", "label": "Guests", "min": 1, "max": 12, "wholeNumber": true },
            { "id": "a00000000005", "type": "date", "label": "Day", "earliest": "2026-01-01", "latest": "2026-12-31" },
            { "id": "a00000000006", "type": "textarea", "label": "Message", "message": "Tell us more." }
            """);

        Assert.Contains(
            "<div class=\"qb-form__field\"><label class=\"qb-form__label\" for=\"qb-p.contact-form-f-a00000000001\">Name<span class=\"qb-form__req\"> (required)</span></label>"
            + "<p class=\"qb-form__help\" id=\"qb-p.contact-form-h-a00000000001\">As on your passport.</p>"
            + "<input class=\"qb-form__input\" id=\"qb-p.contact-form-f-a00000000001\" name=\"f.a00000000001\" type=\"text\" required=\"required\" minlength=\"2\" maxlength=\"80\" placeholder=\"Jane\" aria-describedby=\"qb-p.contact-form-h-a00000000001\"></div>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("type=\"email\" autocomplete=\"email\" maxlength=\"254\">", html, StringComparison.Ordinal);
        Assert.Contains("type=\"tel\" autocomplete=\"tel\" maxlength=\"500\">", html, StringComparison.Ordinal);
        Assert.Contains("type=\"number\" min=\"1\" max=\"12\" step=\"1\">", html, StringComparison.Ordinal);
        Assert.Contains("type=\"date\" min=\"2026-01-01\" max=\"2026-12-31\">", html, StringComparison.Ordinal);
        Assert.Contains("<textarea class=\"qb-form__input\" id=\"qb-p.contact-form-f-a00000000006\" name=\"f.a00000000006\" rows=\"5\" maxlength=\"10000\" data-msg=\"Tell us more.\"></textarea>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Choices_are_a_select_or_a_fieldset_with_a_legend()
    {
        var html = Render("""
            { "id": "b00000000001", "type": "select", "label": "Topic", "options": [{ "value": "sales", "label": "Sales" }, { "value": "support", "label": "Support" }] },
            { "id": "b00000000002", "type": "radio", "label": "Contact by", "required": true, "options": [{ "value": "email", "label": "Email" }] },
            { "id": "b00000000003", "type": "checkboxes", "label": "Interests", "options": [{ "value": "web", "label": "Web" }, { "value": "seo", "label": "Search" }] }
            """);

        Assert.Contains("<select class=\"qb-form__input\" id=\"qb-p.contact-form-f-b00000000001\" name=\"f.b00000000001\"><option value=\"\"></option><option value=\"sales\">Sales</option><option value=\"support\">Support</option></select>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<fieldset class=\"qb-form__field qb-form__group\"><legend class=\"qb-form__label\">Contact by<span class=\"qb-form__req\"> (required)</span></legend>"
            + "<div class=\"qb-form__choice\"><input id=\"qb-p.contact-form-f-b00000000002-0\" type=\"radio\" name=\"f.b00000000002\" value=\"email\" required=\"required\"><label for=\"qb-p.contact-form-f-b00000000002-0\">Email</label></div></fieldset>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<input id=\"qb-p.contact-form-f-b00000000003-1\" type=\"checkbox\" name=\"f.b00000000003\" value=\"seo\"><label for=\"qb-p.contact-form-f-b00000000003-1\">Search</label>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_consent_is_required_by_default_and_links_to_its_live_page_by_title()
    {
        var html = Render($$"""
            { "id": "c00000000001", "type": "consent", "label": "Consent", "statement": "I agree.", "linkPageId": "{{Privacy}}" },
            { "id": "c00000000002", "type": "checkbox", "label": "Newsletter" },
            { "id": "c00000000003", "type": "consent", "label": "Terms", "statement": "I accept.", "linkPageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e99" }
            """);

        Assert.Contains(
            "<div class=\"qb-form__field qb-form__check\"><input id=\"qb-p.contact-form-f-c00000000001\" type=\"checkbox\" name=\"f.c00000000001\" value=\"yes\" required=\"required\">"
            + "<label class=\"qb-form__label\" for=\"qb-p.contact-form-f-c00000000001\">I agree.<span class=\"qb-form__req\"> (required)</span></label>"
            + "<p class=\"qb-form__help\"><a class=\"qb-form__link\" href=\"/privacy\">Privacy policy</a></p></div>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<label class=\"qb-form__label\" for=\"qb-p.contact-form-f-c00000000002\">Newsletter</label>", html, StringComparison.Ordinal);

        // A link to a page that is not live is left out, like every link of the site (012 FR-026).
        Assert.Contains("I accept.<span class=\"qb-form__req\"> (required)</span></label></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_and_messages_follow_the_sites_language()
    {
        var html = Render("""{ "id": "a00000000001", "type": "email", "label": "Email", "required": true }""", language: "vi");

        Assert.Contains("<p>Cảm ơn bạn. Tin nhắn của bạn đã được gửi.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<button class=\"qb-form__submit\" type=\"submit\">Gửi</button>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"qb-form__req\"> (bắt buộc)</span>", html, StringComparison.Ordinal);
        Assert.Contains("data-msg-required=\"{label} là bắt buộc.\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_label_message_and_option_is_encoded_for_its_context()
    {
        var html = Render(
            """
            { "id": "a00000000001", "type": "select", "label": "<script>alert(1)</script>", "help": "a & b", "options": [{ "value": "\"><img src=x>", "label": "<b>bold</b>" }] }
            """,
            """, "submitLabel": "Send <now>", "successMessage": "Thanks & \"bye\"" """);

        Assert.Contains(">&lt;script&gt;alert(1)&lt;/script&gt;</label>", html, StringComparison.Ordinal);
        Assert.Contains(">a &amp; b</p>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"&quot;&gt;&lt;img src=x&gt;\">&lt;b&gt;bold&lt;/b&gt;</option>", html, StringComparison.Ordinal);
        Assert.Contains(">Send &lt;now&gt;</button>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Thanks &amp; &quot;bye&quot;</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_in_a_component_gets_ids_of_the_components_scope()
    {
        var footer = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20");
        var harness = new RenderHarness { CurrentPageId = Contact, Path = "/contact" };
        harness.Components[footer] = new LiveDocument(1, RenderHarness.Document("""
            [{ "id": "fs", "type": "section", "props": {}, "children": [
              { "id": "footer-form", "type": "form", "props": { "name": "F", "fields": [ { "id": "a00000000001", "type": "email", "label": "Email" } ] }, "children": [] } ] }]
            """));

        var html = harness.Page(RenderHarness.Document($$"""
            [{ "id": "ref", "type": "component-ref", "props": { "componentId": "{{footer}}" }, "children": [] },
             { "id": "s", "type": "section", "props": {}, "children": [
               { "id": "page-form", "type": "form", "props": { "name": "P", "fields": [ { "id": "a00000000001", "type": "email", "label": "Email" } ] }, "children": [] } ] }]
            """));

        Assert.Contains("id=\"qb-p.page-form-f-a00000000001\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"qb-c\\.[^\"]*footer-form-f-a00000000001\"", html);
    }
}
