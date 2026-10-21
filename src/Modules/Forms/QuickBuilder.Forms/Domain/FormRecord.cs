using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// The last published settings of one form, kept by the registry (018 research FS-05;
/// data-model §1, <c>forms</c>).
/// </summary>
/// <remarks>
/// Written only by the registry handler from Pages' publishing events. It outlives the form's
/// placements, so retention keeps working after a form is unpublished (FR-023a), and the
/// admin can name forms whose blocks are gone.
/// </remarks>
internal sealed class FormRecord : ISiteOwned
{
    public FormRecord(Guid siteId, string formId)
    {
        SiteId = siteId;
        FormId = formId;
        Name = string.Empty;
        Recipients = [];
        Fields = "[]";
    }

    public Guid SiteId { get; private set; }

    /// <summary>The <c>form</c> node's id (research FS-03).</summary>
    public string FormId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Null when off; else 30, 90, 180 or 365.</summary>
    public int? RetentionDays { get; private set; }

    public bool IncludeValues { get; private set; }

    public string[] Recipients { get; private set; }

    /// <summary>Null for the default subject.</summary>
    public string? Subject { get; private set; }

    public string? ReplyFieldId { get; private set; }

    /// <summary>The published field list as JSON: <c>[{ "id", "label", "type" }]</c>, in order.</summary>
    public string Fields { get; private set; }

    public DateTimeOffset LastPublishedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Takes the settings of a newly live version of the form.</summary>
    public void Apply(FormSettings settings, DateTimeOffset publishedAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Name = settings.Name;
        RetentionDays = settings.RetentionDays;
        IncludeValues = settings.IncludeValues;
        Recipients = [.. settings.Recipients];
        Subject = settings.Subject;
        ReplyFieldId = settings.ReplyFieldId;
        Fields = settings.FieldsJson;
        LastPublishedAtUtc = publishedAtUtc;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>What the registry keeps of a published form.</summary>
internal sealed record FormSettings(
    string Name,
    int? RetentionDays,
    bool IncludeValues,
    IReadOnlyList<string> Recipients,
    string? Subject,
    string? ReplyFieldId,
    string FieldsJson);

/// <summary>
/// "This form is live in that page or component" (data-model §1, <c>form_placements</c>).
/// A form with no placement is no longer published.
/// </summary>
internal sealed class FormPlacement : ISiteOwned
{
    public FormPlacement(Guid siteId, string subjectKind, Guid subjectId, string formId)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        FormId = formId;
    }

    public Guid SiteId { get; private set; }

    /// <summary><see cref="FormSubjectKinds.Page"/> or <see cref="FormSubjectKinds.Component"/>.</summary>
    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    public string FormId { get; private set; }
}

/// <summary>The publishing subjects a form can be live in.</summary>
internal static class FormSubjectKinds
{
    public const string Page = "page";

    public const string Component = "component";
}
