using System.Globalization;
using System.Text.Json;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.NotFound;

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
/// that target, so the page stays the same cached bytes. The editor canvas draws the same
/// elements (page-schema.md §9). Every text and value goes through <see cref="HtmlWriter"/>.
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
        var idBase = $"qb-{scope}{formId}";
        var fields = context.Prop(node, "fields") is { ValueKind: JsonValueKind.Array } list ? [.. list.EnumerateArray()] : Array.Empty<JsonElement>();

        writer.Open("div").Attribute("class", "qb-form").Attribute("id", idBase).EndTag();

        writer.Open("div").Attribute("class", "qb-form__sent").Attribute("id", SentId(formId))
            .Attribute("role", "status").Attribute("tabindex", "-1").EndTag();
        writer.Open("p").EndTag().Text(context.String(node, "successMessage") is { Length: > 0 } success ? success : texts.Success).Close("p");
        writer.Close("div");

        writer.Open("form").Attribute("class", "qb-form__form").Attribute("method", "post");
        writer.SitePath("action", PageShell.TrimBase(inputs.BasePath) + "/_forms/" + formId);
        writer.Attribute("data-qb-form", formId)
            .Attribute("data-msg-error", context.String(node, "errorMessage") is { Length: > 0 } error ? error : texts.Error)
            .Attribute("data-msg-limited", texts.Limited)
            .Attribute("data-msg-summary", texts.Summary);
        foreach (var (code, template) in texts.Rules.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            writer.Attribute($"data-msg-{code.Replace('_', '-')}", template);
        }

        writer.EndTag();

        writer.Open("input").Attribute("type", "hidden").Attribute("name", "_page")
            .Attribute("value", inputs.CurrentPageId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty).EndTag();
        writer.Open("input").Attribute("type", "hidden").Attribute("name", "_path").Attribute("value", inputs.Path).EndTag();

        foreach (var field in fields)
        {
            Field(field, idBase, context, texts, writer);
        }

        writer.Open("div").Attribute("class", "qb-form__actions").EndTag();
        writer.Open("button").Attribute("class", "qb-form__submit").Attribute("type", "submit").EndTag()
            .Text(context.String(node, "submitLabel") is { Length: > 0 } submit ? submit : texts.Submit)
            .Close("button");
        writer.Close("div");

        writer.Close("form");
        writer.Close("div");
    }

    private static void Field(JsonElement field, string idBase, RenderContext context, FormTexts texts, HtmlWriter writer)
    {
        var id = Text(field, "id");
        var type = Text(field, "type");
        var inputId = $"{idBase}-f-{id}";
        var helpId = $"{idBase}-h-{id}";
        var help = Text(field, "help");
        var describedBy = help.Length > 0 ? helpId : null;
        var required = field.TryGetProperty("required", out var r) ? r.ValueKind == JsonValueKind.True : type == "consent";

        switch (type)
        {
            case "radio" or "checkboxes":
                writer.Open("fieldset").Attribute("class", "qb-form__field qb-form__group").Attribute("aria-describedby", describedBy).EndTag();
                writer.Open("legend").Attribute("class", "qb-form__label").EndTag();
                Label(field, required, texts, writer);
                writer.Close("legend");
                Help(help, helpId, writer);
                var options = field.TryGetProperty("options", out var list) ? [.. list.EnumerateArray()] : Array.Empty<JsonElement>();
                for (var i = 0; i < options.Length; i++)
                {
                    var optionId = $"{inputId}-{i.ToString(CultureInfo.InvariantCulture)}";
                    writer.Open("div").Attribute("class", "qb-form__choice").EndTag();
                    writer.Open("input").Attribute("id", optionId)
                        .Attribute("type", type == "radio" ? "radio" : "checkbox")
                        .Attribute("name", $"f.{id}")
                        .Attribute("value", Text(options[i], "value"));
                    if (type == "radio" && required)
                    {
                        writer.Attribute("required", "required");
                    }

                    writer.EndTag();
                    writer.Open("label").Attribute("for", optionId).EndTag().Text(Text(options[i], "label")).Close("label");
                    writer.Close("div");
                }

                writer.Close("fieldset");
                return;

            case "checkbox" or "consent":
                writer.Open("div").Attribute("class", "qb-form__field qb-form__check").EndTag();
                writer.Open("input").Attribute("id", inputId).Attribute("type", "checkbox")
                    .Attribute("name", $"f.{id}").Attribute("value", "yes").Attribute("aria-describedby", describedBy);
                if (required)
                {
                    writer.Attribute("required", "required");
                }

                writer.EndTag();
                writer.Open("label").Attribute("class", "qb-form__label").Attribute("for", inputId).EndTag();
                if (type == "consent")
                {
                    writer.Text(Text(field, "statement"));
                    Required(required, texts, writer);
                }
                else
                {
                    Label(field, required, texts, writer);
                }

                writer.Close("label");
                Help(help, helpId, writer);
                ConsentLink(field, context, writer);
                writer.Close("div");
                return;

            default:
                writer.Open("div").Attribute("class", "qb-form__field").EndTag();
                writer.Open("label").Attribute("class", "qb-form__label").Attribute("for", inputId).EndTag();
                Label(field, required, texts, writer);
                writer.Close("label");
                Help(help, helpId, writer);
                Control(field, type, inputId, describedBy, required, writer);
                writer.Close("div");
                return;
        }
    }

    /// <summary>The input of a single-value field, with the browser's own constraints.</summary>
    private static void Control(JsonElement field, string type, string inputId, string? describedBy, bool required, HtmlWriter writer)
    {
        var name = $"f.{Text(field, "id")}";

        if (type == "textarea")
        {
            writer.Open("textarea").Attribute("class", "qb-form__input").Attribute("id", inputId).Attribute("name", name).Attribute("rows", "5");
            Constraints(field, type, required, describedBy, writer);
            writer.EndTag().Close("textarea");
            return;
        }

        if (type == "select")
        {
            writer.Open("select").Attribute("class", "qb-form__input").Attribute("id", inputId).Attribute("name", name);
            Constraints(field, type, required, describedBy, writer);
            writer.EndTag();
            writer.Open("option").Attribute("value", string.Empty).EndTag().Close("option");
            if (field.TryGetProperty("options", out var options))
            {
                foreach (var option in options.EnumerateArray())
                {
                    writer.Open("option").Attribute("value", Text(option, "value")).EndTag().Text(Text(option, "label")).Close("option");
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

        Constraints(field, type, required, describedBy, writer);
        writer.EndTag();
    }

    private static void Constraints(JsonElement field, string type, bool required, string? describedBy, HtmlWriter writer)
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
        if (field.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } own)
        {
            writer.Attribute("data-msg", own);
        }
    }

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

        var href = PageShell.TrimBase(context.Inputs.BasePath) + target.Path;
        if (!HtmlWriter.IsSitePath(href))
        {
            return;
        }

        writer.Open("p").Attribute("class", "qb-form__help").EndTag();
        writer.Open("a").Attribute("class", "qb-form__link").Attribute("href", href).EndTag()
            .Text(target.LiveTitle is { Length: > 0 } title ? title : target.Path)
            .Close("a");
        writer.Close("p");
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static string? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
}
