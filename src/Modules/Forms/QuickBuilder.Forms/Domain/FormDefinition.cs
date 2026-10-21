using System.Globalization;
using System.Text.Json;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// A form as a published page or component defines it (018 research FS-03, FS-05;
/// contracts/form-block.md §2).
/// </summary>
/// <remarks>
/// Read from documents that passed the page schema, so shapes are trusted and only defaults are
/// filled in here. Texts that default to the site language (the submit label, the messages, the
/// subject) stay null when absent: Rendering and the notification composer pick the text.
/// </remarks>
internal sealed record FormDefinition(
    string Id,
    string Name,
    IReadOnlyList<FormField> Fields,
    string? SubmitLabel,
    string? SuccessMessage,
    string? ErrorMessage,
    FormRedirect? Redirect,
    FormNotify Notify,
    int? RetentionDays)
{
    public const string NodeType = "form";

    /// <summary>Every form of a content document, in document order.</summary>
    public static IReadOnlyList<FormDefinition> FindAll(JsonElement document)
    {
        var forms = new List<FormDefinition>();
        if (document.TryGetProperty("root", out var root))
        {
            Collect(root, forms);
        }

        return forms;
    }

    /// <summary>The form with <paramref name="formId"/> in <paramref name="document"/>, if any.</summary>
    public static FormDefinition? Find(JsonElement document, string formId) =>
        FindAll(document).FirstOrDefault(f => string.Equals(f.Id, formId, StringComparison.Ordinal));

    /// <summary>Reads one <c>form</c> node.</summary>
    public static FormDefinition Parse(JsonElement node)
    {
        var props = node.GetProperty("props");

        return new FormDefinition(
            node.GetProperty("id").GetString()!,
            props.GetProperty("name").GetString()!,
            [.. props.GetProperty("fields").EnumerateArray().Select(FormField.Parse)],
            Text(props, "submitLabel"),
            Text(props, "successMessage"),
            Text(props, "errorMessage"),
            props.TryGetProperty("redirect", out var redirect) ? FormRedirect.Parse(redirect) : null,
            props.TryGetProperty("notify", out var notify) ? FormNotify.Parse(notify) : FormNotify.None,
            props.TryGetProperty("retentionDays", out var retention) ? retention.GetInt32() : null);
    }

    /// <summary>What the registry keeps (research FS-05): settings and the field list, not the rules.</summary>
    public FormSettings ToSettings() =>
        new(
            Name,
            RetentionDays,
            Notify.IncludeValues,
            Notify.Recipients,
            Notify.Subject,
            Notify.ReplyFieldId,
            JsonSerializer.Serialize(Fields.Select(f => new { id = f.Id, label = f.Label, type = f.Type })));

    private static void Collect(JsonElement node, List<FormDefinition> forms)
    {
        if (node.TryGetProperty("type", out var type) && type.GetString() == NodeType)
        {
            forms.Add(Parse(node));
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, forms);
            }
        }
    }

    internal static string? Text(JsonElement props, string name) =>
        props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>One field of a form, with its type-specific rules (form-block.md §2).</summary>
internal sealed record FormField(
    string Id,
    string Type,
    string Label,
    string? Help,
    string? Placeholder,
    bool Required,
    string? Message,
    int? MinLength,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    DateOnly? Earliest,
    DateOnly? Latest,
    IReadOnlyList<FormOption> Options,
    int? MinSelected,
    int? MaxSelected,
    string? Statement,
    Guid? LinkPageId)
{
    public static FormField Parse(JsonElement field)
    {
        var type = field.GetProperty("type").GetString()!;

        return new FormField(
            field.GetProperty("id").GetString()!,
            type,
            field.GetProperty("label").GetString()!,
            FormDefinition.Text(field, "help"),
            FormDefinition.Text(field, "placeholder"),
            field.TryGetProperty("required", out var required) ? required.GetBoolean() : type == FieldTypes.Consent,
            FormDefinition.Text(field, "message"),
            Int(field, "minLength"),
            Int(field, "maxLength") ?? FieldTypes.DefaultMaxLength(type),
            field.TryGetProperty("min", out var min) ? min.GetDecimal() : null,
            field.TryGetProperty("max", out var max) ? max.GetDecimal() : null,
            field.TryGetProperty("wholeNumber", out var whole) && whole.GetBoolean(),
            Date(field, "earliest"),
            Date(field, "latest"),
            field.TryGetProperty("options", out var options)
                ? [.. options.EnumerateArray().Select(o => new FormOption(o.GetProperty("value").GetString()!, o.GetProperty("label").GetString()!))]
                : [],
            Int(field, "minSelected"),
            Int(field, "maxSelected"),
            FormDefinition.Text(field, "statement"),
            field.TryGetProperty("linkPageId", out var link) ? link.GetGuid() : null);
    }

    private static int? Int(JsonElement field, string name) =>
        field.TryGetProperty(name, out var value) ? value.GetInt32() : null;

    private static DateOnly? Date(JsonElement field, string name) =>
        FormDefinition.Text(field, name) is { } text
            ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
}

/// <summary>One choice of a <c>select</c>, <c>radio</c> or <c>checkboxes</c> field.</summary>
internal sealed record FormOption(string Value, string Label);

/// <summary>Where a successful submission goes: a page of the site, or an http(s) address.</summary>
internal sealed record FormRedirect(Guid? PageId, string? Href)
{
    public static FormRedirect Parse(JsonElement redirect) =>
        redirect.GetProperty("type").GetString() == "page"
            ? new FormRedirect(redirect.GetProperty("pageId").GetGuid(), null)
            : new FormRedirect(null, redirect.GetProperty("href").GetString());
}

/// <summary>Who is told about a submission, and how (form-block.md §2, <c>formNotify</c>).</summary>
internal sealed record FormNotify(IReadOnlyList<string> Recipients, string? Subject, bool IncludeValues, string? ReplyFieldId)
{
    /// <summary>A form without <c>notify</c>: nobody is told; values would be included.</summary>
    public static FormNotify None { get; } = new([], null, IncludeValues: true, ReplyFieldId: null);

    public static FormNotify Parse(JsonElement notify) =>
        new(
            notify.TryGetProperty("recipients", out var recipients) ? [.. recipients.EnumerateArray().Select(r => r.GetString()!)] : [],
            FormDefinition.Text(notify, "subject"),
            !notify.TryGetProperty("includeValues", out var include) || include.GetBoolean(),
            FormDefinition.Text(notify, "replyFieldId"));
}

/// <summary>The field types of form-block.md §2.</summary>
internal static class FieldTypes
{
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Number = "number";
    public const string Date = "date";
    public const string Select = "select";
    public const string Radio = "radio";
    public const string Checkboxes = "checkboxes";
    public const string Checkbox = "checkbox";
    public const string Consent = "consent";

    /// <summary>The value a ticked <c>checkbox</c> or <c>consent</c> submits.</summary>
    public const string Ticked = "yes";

    /// <summary>The schema's default <c>maxLength</c>, or null for types without one.</summary>
    public static int? DefaultMaxLength(string type) => type switch
    {
        Text or Phone => 500,
        Textarea => 10_000,
        Email => 254,
        _ => null,
    };

    /// <summary>Types whose value is one of the field's options.</summary>
    public static bool IsChoice(string type) => type is Select or Radio or Checkboxes;
}
