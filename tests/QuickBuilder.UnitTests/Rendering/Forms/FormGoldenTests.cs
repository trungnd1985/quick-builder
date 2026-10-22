using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Contracts;

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

    private static string RenderWithState(string fields, FormState state, string props = "")
    {
        var harness = new RenderHarness { CurrentPageId = Contact, Path = "/contact", FormState = state };
        var form = $$"""{ "id": "contact-form", "type": "form", "props": { "name": "Contact", "fields": [ {{fields}} ]{{props}} }, "children": [] }""";
        var other = """{ "id": "other-form", "type": "form", "props": { "name": "Other", "fields": [ { "id": "a00000000001", "type": "text", "label": "Name" } ] }, "children": [] }""";
        return harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ {{form}}, {{other}} ] }]"""));
    }

    private static FormState State(
        Dictionary<string, IReadOnlyList<string>> values, Dictionary<string, string> errors, FormBanner banner = FormBanner.None) =>
        new("contact-form", values, errors, banner);

    [Fact]
    public void A_refused_submission_keeps_the_values_and_says_what_is_wrong_where()
    {
        var html = RenderWithState(
            """
            { "id": "a00000000001", "type": "text", "label": "Name", "required": true },
            { "id": "a00000000002", "type": "email", "label": "Email", "help": "We reply here." },
            { "id": "a00000000003", "type": "textarea", "label": "Message", "maxLength": 10, "message": "Keep it short." },
            { "id": "a00000000004", "type": "select", "label": "Topic", "options": [{ "value": "sales", "label": "Sales" }] },
            { "id": "a00000000005", "type": "checkboxes", "label": "Pick", "minSelected": 2, "options": [{ "value": "a", "label": "A" }, { "value": "b", "label": "B" }] }
            """,
            State(
                new()
                {
                    ["a00000000002"] = ["jane@"],
                    ["a00000000003"] = ["Far too long a message"],
                    ["a00000000004"] = ["sales"],
                    ["a00000000005"] = ["b"],
                },
                new() { ["a00000000001"] = "required", ["a00000000002"] = "email", ["a00000000003"] = "too_long", ["a00000000005"] = "too_few" }));

        // The summary: focused on load, every failing field in form order, each linking to its input.
        Assert.Contains(
            "<div class=\"qb-form__summary\" role=\"alert\" tabindex=\"-1\" autofocus=\"autofocus\"><h2>There is a problem</h2><ul>"
            + "<li><a href=\"#qb-p.contact-form-f-a00000000001\">Name is required.</a></li>"
            + "<li><a href=\"#qb-p.contact-form-f-a00000000002\">Email must be an email address.</a></li>"
            + "<li><a href=\"#qb-p.contact-form-f-a00000000003\">Keep it short.</a></li>"
            + "<li><a href=\"#qb-p.contact-form-f-a00000000005-0\">Choose at least 2 options for Pick.</a></li></ul></div>",
            html,
            StringComparison.Ordinal);

        // Each field: its message tied to it, aria-invalid, and the value as typed.
        Assert.Contains(
            "<p class=\"qb-form__help\" id=\"qb-p.contact-form-h-a00000000002\">We reply here.</p>"
            + "<p class=\"qb-form__error\" id=\"qb-p.contact-form-e-a00000000002\">Email must be an email address.</p>"
            + "<input class=\"qb-form__input\" id=\"qb-p.contact-form-f-a00000000002\" name=\"f.a00000000002\" type=\"email\" autocomplete=\"email\" maxlength=\"254\" aria-describedby=\"qb-p.contact-form-h-a00000000002 qb-p.contact-form-e-a00000000002\" aria-invalid=\"true\" value=\"jane@\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">Far too long a message</textarea>", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"sales\" selected=\"selected\">Sales</option>", html, StringComparison.Ordinal);
        Assert.Contains("name=\"f.a00000000005\" value=\"b\" checked=\"checked\" aria-invalid=\"true\">", html, StringComparison.Ordinal);
        Assert.Contains("<fieldset class=\"qb-form__field qb-form__group\" aria-describedby=\"qb-p.contact-form-e-a00000000005\">", html, StringComparison.Ordinal);

        // The state applies to its form only.
        var other = html[html.IndexOf("id=\"qb-p.other-form\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("qb-form__error", other, StringComparison.Ordinal);
        Assert.DoesNotContain("qb-form__summary", other, StringComparison.Ordinal);
    }

    [Fact]
    public void The_limited_and_error_banners_say_so_and_keep_the_values()
    {
        var limited = RenderWithState(
            """{ "id": "a00000000001", "type": "text", "label": "Name" }""",
            State(new() { ["a00000000001"] = ["Jane"] }, [], FormBanner.Limited));
        var error = RenderWithState(
            """{ "id": "a00000000001", "type": "text", "label": "Name" }""",
            State(new() { ["a00000000001"] = ["Jane"] }, [], FormBanner.Error),
            """, "errorMessage": "Call us instead." """);

        Assert.Contains("<div class=\"qb-form__banner\" role=\"alert\" tabindex=\"-1\" autofocus=\"autofocus\"><p>Too many messages have been sent from your connection. Please try again later.</p></div>", limited, StringComparison.Ordinal);
        Assert.Contains("<p>Call us instead.</p></div>", error, StringComparison.Ordinal);
        Assert.Contains("value=\"Jane\"", limited, StringComparison.Ordinal);
        Assert.DoesNotContain("qb-form__summary", limited, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_shown_again_are_encoded_like_everything_else()
    {
        var html = RenderWithState(
            """{ "id": "a00000000001", "type": "text", "label": "Name" }, { "id": "a00000000002", "type": "textarea", "label": "Message" }""",
            State(new() { ["a00000000001"] = ["\"><script>alert(1)</script>"], ["a00000000002"] = ["</textarea><script>x</script>"] }, new() { ["a00000000001"] = "too_long" }));

        Assert.Contains("value=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"", html, StringComparison.Ordinal);
        Assert.Contains(">&lt;/textarea&gt;&lt;script&gt;x&lt;/script&gt;</textarea>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
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
