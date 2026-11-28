using System.Globalization;
using System.Text.Json;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.NotFound;
using QuickBuilder.Rendering.Rendering.Blocks;
using QuickBuilder.Shared.Kernel.Routing;

namespace QuickBuilder.Rendering.Rendering.Forms;

/// <summary>
/// The published markup of a Form block (018 FR-009, FR-011, FR-012; research FS-09 to FS-12;
/// public-forms.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// A standard HTML form that works without scripts: it posts to <c>/_forms/{formId}</c>, every
/// input has a visible label tied to it, help and error texts are tied with
/// <c>aria-describedby</c>, choices are grouped in a <c>fieldset</c>, and the browser's own
/// constraint attributes are written so it checks what it can before sending.
/// </para>
/// <para>
/// After a successful submission without scripts the visitor lands on
/// <c>#qb-sent-{formId}</c>, and the stylesheet shows the success panel and hides the form for
/// that target, so the page stays the same cached bytes. After a refused one, the page is
/// rendered again outside the cache with a <see cref="FormState"/>: the values come back, each
/// failing field says why, and a summary at the top, focused on load, links to them (FR-011).
/// The editor canvas draws the same elements (page-schema.md §9). Every text and value goes
/// through <see cref="HtmlWriter"/>.
/// </para>
/// </remarks>
internal static class FormMarkup
{
    /// <summary>The fragment a successful submission without scripts lands on.</summary>
    public static string SentId(string formId) => $"qb-sent-{formId}";

    public static void Write(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var inputs = context.Inputs;
        var texts = PublicTexts.For(inputs.Language).Forms;
        var formId = RenderContext.IdOf(node);
        var state = inputs.FormState is { } given && given.FormId == formId ? given : null;
        var view = new FormView($"qb-{scope}{formId}", texts, state);
        var fields = context.Prop(node, "fields") is { ValueKind: JsonValueKind.Array } list ? [.. list.EnumerateArray()] : Array.Empty<JsonElement>();
        var errorMessage = context.String(node, "errorMessage") is { Length: > 0 } error ? error : texts.Error;

        // 066: a border frames the whole form, its messages included.
        writer.Open("div").Attribute("class", "qb-form" + BorderStyle.Classes(node, context)).Attribute("id", view.IdBase);
        context.Style(writer, scope, node, BorderStyle.Declarations(node, context));
        writer.EndTag();

        writer.Open("div").Attribute("class", "qb-form__sent").Attribute("id", SentId(formId))
            .Attribute("role", "status").Attribute("tabindex", "-1").EndTag();
        writer.Open("p").EndTag().Text(context.String(node, "successMessage") is { Length: > 0 } success ? success : texts.Success).Close("p");
        writer.Close("div");

        writer.Open("form").Attribute("class", "qb-form__form").Attribute("method", "post");
        // 022 research PV-12: a preview's forms post to a page that stores and sends nothing.
        writer.SitePath("action", PageShell.TrimBase(inputs.BasePath) + (inputs.Preview is { } preview ? preview.Addresses.FormAction : PublicRoutes.Forms + "/" + formId));
        writer.Attribute("data-qb-form", formId)
            .Attribute("data-msg-error", errorMessage)
            .Attribute("data-msg-limited", texts.Limited)
            .Attribute("data-msg-summary", texts.Summary);
        foreach (var (code, template) in texts.Rules.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            writer.Attribute($"data-msg-{code.Replace('_', '-')}", template);
        }

        writer.EndTag();

        Banner(state, errorMessage, texts, writer);
        Summary(fields, view, writer);

        writer.Open("input").Attribute("type", "hidden").Attribute("name", "_page")
            .Attribute("value", inputs.CurrentPageId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty).EndTag();
        writer.Open("input").Attribute("type", "hidden").Attribute("name", "_path").Attribute("value", inputs.Path).EndTag();

        // FS-12: the trap field. Off-screen rather than display:none, which some bots skip;
        // hidden from assistive technology and the tab order, and labelled in case it shows.
        writer.Open("div").Attribute("class", "qb-form__hp").Attribute("aria-hidden", "true").EndTag();
        writer.Open("label").Attribute("for", view.IdBase + "-hp").EndTag().Text(texts.Trap).Close("label");
        writer.Open("input").Attribute("id", view.IdBase + "-hp").Attribute("name", "_hp").Attribute("type", "text")
            .Attribute("tabindex", "-1").Attribute("autocomplete", "off").EndTag();
        writer.Close("div");

        foreach (var field in fields)
        {
            Field(field, view, context, writer);
        }

        writer.Open("div").Attribute("class", "qb-form__actions").EndTag();
        writer.Open("button").Attribute("class", "qb-form__submit").Attribute("type", "submit").EndTag()
            .Text(context.String(node, "submitLabel") is { Length: > 0 } submit ? submit : texts.Submit)
            .Close("button");
        writer.Close("div");

        writer.Close("form");
        writer.Close("div");

        // 022 research PV-12: a preview never sends a form, so it carries no script and no
        // redirect origins; its policy lets forms post to the preview only.
        if (inputs.Preview is not null)
        {
            context.PreviewFormWritten = true;
            return;
        }

        // FS-11: browsers apply form-action to the redirect after a submission, so the page's
        // policy names the origin of an external redirect target.
        if (context.Prop(node, "redirect") is { ValueKind: JsonValueKind.Object } redirect
            && redirect.TryGetProperty("href", out var href)
            && Uri.TryCreate(href.GetString(), UriKind.Absolute, out var target)
            && target.Scheme is "https" or "http")
        {
            context.FormActionOrigins.Add(target.GetLeftPart(UriPartial.Authority));
        }

        // FS-10: one script for every form of the page, admitted by its hash.
        if (!context.FormScriptWritten)
        {
            writer.Markup(FormScript.Element);
            context.FormScriptWritten = true;
        }
    }

    /// <summary>The form's error or rate-limit message, focused on load (public-forms.md §2.4).</summary>
    private static void Banner(FormState? state, string errorMessage, FormTexts texts, HtmlWriter writer)
    {
        if (state is not { Banner: not FormBanner.None } banner)
        {
            return;
        }

        writer.Open("div").Attribute("class", "qb-form__banner").Attribute("role", "alert")
            .Attribute("tabindex", "-1").Attribute("autofocus", "autofocus").EndTag();
        writer.Open("p").EndTag().Text(banner.Banner == FormBanner.Limited ? texts.Limited : errorMessage).Close("p");
        writer.Close("div");
    }

    /// <summary>Every failing field, in form order, each linking to its input (FR-011).</summary>
    private static void Summary(IReadOnlyList<JsonElement> fields, FormView view, HtmlWriter writer)
    {
        var failing = fields.Where(f => view.ErrorOf(Text(f, "id")) is not null).ToList();
        if (failing.Count == 0)
        {
            return;
        }

        writer.Open("div").Attribute("class", "qb-form__summary").Attribute("role", "alert")
            .Attribute("tabindex", "-1").Attribute("autofocus", "autofocus").EndTag();
        writer.Open("h2").EndTag().Text(view.Texts.Summary).Close("h2");
        writer.Open("ul").EndTag();
        foreach (var field in failing)
        {
            var id = Text(field, "id");
            var target = Text(field, "type") is "radio" or "checkboxes" ? $"{view.InputId(id)}-0" : view.InputId(id);
            writer.Open("li").EndTag();
            writer.Open("a").Attribute("href", "#" + target).EndTag().Text(Message(field, view.ErrorOf(id)!, view.Texts)).Close("a");
            writer.Close("li");
        }

        writer.Close("ul");
        writer.Close("div");
    }

    private static void Field(JsonElement field, FormView view, RenderContext context, HtmlWriter writer)
    {
        var id = Text(field, "id");
        var type = Text(field, "type");
        var inputId = view.InputId(id);
        var helpId = $"{view.IdBase}-h-{id}";
        var errorId = $"{view.IdBase}-e-{id}";
        var help = Text(field, "help");
        var error = view.ErrorOf(id);
        var describedBy = string.Join(' ', new[] { help.Length > 0 ? helpId : null, error is null ? null : errorId }.OfType<string>());
        var describes = describedBy.Length > 0 ? describedBy : null;
        var required = field.TryGetProperty("required", out var r) ? r.ValueKind == JsonValueKind.True : type == "consent";
        var values = view.ValuesOf(id);

        switch (type)
        {
            case "radio" or "checkboxes":
                writer.Open("fieldset").Attribute("class", "qb-form__field qb-form__group").Attribute("aria-describedby", describes);
                FieldData(field, id, writer);
                if (type == "checkboxes")
                {
                    writer.Attribute("data-required", required ? "required" : null)
                        .Attribute("data-min", Number(field, "minSelected"))
                        .Attribute("data-max", Number(field, "maxSelected"));
                }

                writer.EndTag();
                writer.Open("legend").Attribute("class", "qb-form__label").EndTag();
                Label(field, required, view.Texts, writer);
                writer.Close("legend");
                Help(help, helpId, writer);
                Error(field, error, errorId, view.Texts, writer);
                var options = field.TryGetProperty("options", out var list) ? [.. list.EnumerateArray()] : Array.Empty<JsonElement>();
                for (var i = 0; i < options.Length; i++)
                {
                    var optionId = $"{inputId}-{i.ToString(CultureInfo.InvariantCulture)}";
                    var value = Text(options[i], "value");
                    writer.Open("div").Attribute("class", "qb-form__choice").EndTag();
                    writer.Open("input").Attribute("id", optionId)
                        .Attribute("type", type == "radio" ? "radio" : "checkbox")
                        .Attribute("name", $"f.{id}")
                        .Attribute("value", value);
                    if (type == "radio" && required)
                    {
                        writer.Attribute("required", "required");
                    }

                    if (values.Contains(value, StringComparer.Ordinal))
                    {
                        writer.Attribute("checked", "checked");
                    }

                    if (error is not null)
                    {
                        writer.Attribute("aria-invalid", "true");
                    }

                    writer.EndTag();
                    writer.Open("label").Attribute("for", optionId).EndTag().Text(Text(options[i], "label")).Close("label");
                    writer.Close("div");
                }

                writer.Close("fieldset");
                return;

            case "checkbox" or "consent":
                writer.Open("div").Attribute("class", "qb-form__field qb-form__check");
                FieldData(field, id, writer);
                writer.EndTag();
                writer.Open("input").Attribute("id", inputId).Attribute("type", "checkbox")
                    .Attribute("name", $"f.{id}").Attribute("value", "yes").Attribute("aria-describedby", describes);
                if (required)
                {
                    writer.Attribute("required", "required");
                }

                if (values.Count > 0)
                {
                    writer.Attribute("checked", "checked");
                }

                if (error is not null)
                {
                    writer.Attribute("aria-invalid", "true");
                }

                writer.EndTag();
                writer.Open("label").Attribute("class", "qb-form__label").Attribute("for", inputId).EndTag();
                if (type == "consent")
                {
                    writer.Text(Text(field, "statement"));
                    Required(required, view.Texts, writer);
                }
                else
                {
                    Label(field, required, view.Texts, writer);
                }

                writer.Close("label");
                Help(help, helpId, writer);
                Error(field, error, errorId, view.Texts, writer);
                ConsentLink(field, context, writer);
                writer.Close("div");
                return;

            default:
                writer.Open("div").Attribute("class", "qb-form__field");
                FieldData(field, id, writer);
                writer.EndTag();
                writer.Open("label").Attribute("class", "qb-form__label").Attribute("for", inputId).EndTag();
                Label(field, required, view.Texts, writer);
                writer.Close("label");
                Help(help, helpId, writer);
                Error(field, error, errorId, view.Texts, writer);
                Control(field, type, inputId, describes, required, error is not null, values, writer);
                writer.Close("div");
                return;
        }
    }

    /// <summary>The input of a single-value field, with the browser's own constraints.</summary>
    private static void Control(
        JsonElement field, string type, string inputId, string? describedBy, bool required, bool invalid, IReadOnlyList<string> values, HtmlWriter writer)
    {
        var name = $"f.{Text(field, "id")}";
        var value = values.Count > 0 ? values[0] : null;

        if (type == "textarea")
        {
            writer.Open("textarea").Attribute("class", "qb-form__input").Attribute("id", inputId).Attribute("name", name).Attribute("rows", "5");
            Constraints(field, type, required, describedBy, invalid, writer);
            writer.EndTag().Text(value).Close("textarea");
            return;
        }

        if (type == "select")
        {
            writer.Open("select").Attribute("class", "qb-form__input").Attribute("id", inputId).Attribute("name", name);
            Constraints(field, type, required, describedBy, invalid, writer);
            writer.EndTag();
            writer.Open("option").Attribute("value", string.Empty).EndTag().Close("option");
            if (field.TryGetProperty("options", out var options))
            {
                foreach (var option in options.EnumerateArray())
                {
                    var optionValue = Text(option, "value");
                    writer.Open("option").Attribute("value", optionValue);
                    if (string.Equals(optionValue, value, StringComparison.Ordinal))
                    {
                        writer.Attribute("selected", "selected");
                    }

                    writer.EndTag().Text(Text(option, "label")).Close("option");
                }
            }

            writer.Close("select");
            return;
        }

        writer.Open("input").Attribute("class", "qb-form__input").Attribute("id", inputId).Attribute("name", name)
            .Attribute("type", type switch
            {
                "email" => "email",
                "phone" => "tel",
                "number" => "number",
                "date" => "date",
                _ => "text",
            });
        if (type == "email")
        {
            writer.Attribute("autocomplete", "email");
        }
        else if (type == "phone")
        {
            writer.Attribute("autocomplete", "tel");
        }

        Constraints(field, type, required, describedBy, invalid, writer);
        writer.Attribute("value", value);
        writer.EndTag();
    }

    private static void Constraints(JsonElement field, string type, bool required, string? describedBy, bool invalid, HtmlWriter writer)
    {
        if (required)
        {
            writer.Attribute("required", "required");
        }

        switch (type)
        {
            case "text" or "textarea" or "email" or "phone":
                writer.Attribute("minlength", Number(field, "minLength"));
                writer.Attribute("maxlength", Number(field, "maxLength") ?? (type switch
                {
                    "textarea" => "10000",
                    "email" => "254",
                    _ => "500",
                }));
                break;
            case "number":
                writer.Attribute("min", Number(field, "min"))
                    .Attribute("max", Number(field, "max"))
                    .Attribute("step", field.TryGetProperty("wholeNumber", out var whole) && whole.ValueKind == JsonValueKind.True ? "1" : "any");
                break;
            case "date":
                writer.Attribute("min", field.TryGetProperty("earliest", out var earliest) ? earliest.GetString() : null)
                    .Attribute("max", field.TryGetProperty("latest", out var latest) ? latest.GetString() : null);
                break;
        }

        if (field.TryGetProperty("placeholder", out var placeholder) && placeholder.GetString() is { Length: > 0 } hint)
        {
            writer.Attribute("placeholder", hint);
        }

        writer.Attribute("aria-describedby", describedBy);
        if (invalid)
        {
            writer.Attribute("aria-invalid", "true");
        }

        if (field.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } own)
        {
            writer.Attribute("data-msg", own);
        }
    }

    /// <summary>What the form script reads of a field: its id and the label its messages name.</summary>
    private static void FieldData(JsonElement field, string id, HtmlWriter writer) =>
        writer.Attribute("data-field", id).Attribute("data-label", Text(field, "label"));

    private static void Label(JsonElement field, bool required, FormTexts texts, HtmlWriter writer)
    {
        writer.Text(Text(field, "label"));
        Required(required, texts, writer);
    }

    /// <summary>The visible marker, read as part of the label by assistive technology.</summary>
    private static void Required(bool required, FormTexts texts, HtmlWriter writer)
    {
        if (required)
        {
            writer.Open("span").Attribute("class", "qb-form__req").EndTag().Text(" " + texts.Required).Close("span");
        }
    }

    private static void Help(string help, string helpId, HtmlWriter writer)
    {
        if (help.Length > 0)
        {
            writer.Open("p").Attribute("class", "qb-form__help").Attribute("id", helpId).EndTag().Text(help).Close("p");
        }
    }

    private static void Error(JsonElement field, string? code, string errorId, FormTexts texts, HtmlWriter writer)
    {
        if (code is not null)
        {
            writer.Open("p").Attribute("class", "qb-form__error").Attribute("id", errorId).EndTag().Text(Message(field, code, texts)).Close("p");
        }
    }

    /// <summary>
    /// What a failing field says: its own message, or the code's text with the field's label and
    /// the bound the value broke (public-forms.md §4).
    /// </summary>
    public static string Message(JsonElement field, string code, FormTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (Text(field, "message") is { Length: > 0 } own)
        {
            return own;
        }

        var (min, max) = code switch
        {
            "too_short" or "too_long" => ("minLength", "maxLength"),
            "too_small" or "too_large" => ("min", "max"),
            "too_early" or "too_late" => ("earliest", "latest"),
            "too_few" or "too_many" => ("minSelected", "maxSelected"),
            _ => (string.Empty, string.Empty),
        };
        var maxValue = Bound(field, max) ?? (max == "maxLength" ? (Text(field, "type") switch
        {
            "textarea" => "10000",
            "email" => "254",
            _ => "500",
        }) : string.Empty);

        return texts.Template(code)
            .Replace("{label}", Text(field, "label"), StringComparison.Ordinal)
            .Replace("{min}", Bound(field, min) ?? string.Empty, StringComparison.Ordinal)
            .Replace("{max}", maxValue, StringComparison.Ordinal);
    }

    /// <summary>A consent's link to its page (a privacy policy), named by the page's live title; nothing when the page is not live.</summary>
    private static void ConsentLink(JsonElement field, RenderContext context, HtmlWriter writer)
    {
        if (!field.TryGetProperty("linkPageId", out var link)
            || !Guid.TryParse(link.GetString(), out var pageId)
            || !context.Inputs.LinkTargets.TryGetValue(pageId, out var target)
            || !target.IsLive)
        {
            return;
        }

        // 022 research PV-06: in a preview, the public address on the site's host name.
        if (PageLinks.To(context.Inputs, target.Path) is not { } pageLink)
        {
            return;
        }

        writer.Open("p").Attribute("class", "qb-form__help").EndTag();
        writer.Open("a").Attribute("class", "qb-form__link").Attribute("href", pageLink.Href).EndTag()
            .Text(target.LiveTitle is { Length: > 0 } title ? title : target.Path)
            .Close("a");
        writer.Close("p");
    }

    private static string? Bound(JsonElement field, string name) =>
        name.Length == 0 || !field.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : null;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static string? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    /// <summary>One form's ids, texts and, after a refusal, its state.</summary>
    private sealed record FormView(string IdBase, FormTexts Texts, FormState? State)
    {
        public string InputId(string fieldId) => $"{IdBase}-f-{fieldId}";

        public string? ErrorOf(string fieldId) =>
            State is { } state && state.Errors.TryGetValue(fieldId, out var code) ? code : null;

        public IReadOnlyList<string> ValuesOf(string fieldId) =>
            State is { } state && state.Values.TryGetValue(fieldId, out var values) ? values : [];
    }
}
