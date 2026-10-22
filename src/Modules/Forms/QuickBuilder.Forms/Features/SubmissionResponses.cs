using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;

namespace QuickBuilder.Forms.Features;

/// <summary>A form of the registry (forms-api.yaml, <c>FormSummary</c>).</summary>
public sealed record FormSummaryResponse(
    string FormId,
    string Name,
    bool Published,
    int? RetentionDays,
    IReadOnlyList<FormFieldResponse> Fields,
    DateTimeOffset LastPublishedAt);

public sealed record FormFieldResponse(string Id, string Label, string Type);

public sealed record FormListResponse(IReadOnlyList<FormSummaryResponse> Items);

/// <summary>One row of the submission list (forms-api.yaml, <c>SubmissionSummary</c>).</summary>
public sealed record SubmissionSummaryResponse(
    Guid Id,
    string FormId,
    string FormName,
    bool FormPublished,
    string PagePath,
    DateTimeOffset ReceivedAt,
    bool Read,
    string Notification,
    string Preview);

public sealed record SubmissionPermissionsResponse(bool Delete);

public sealed record SubmissionListResponse(
    IReadOnlyList<SubmissionSummaryResponse> Items,
    string? Next,
    SubmissionPermissionsResponse Permissions);

/// <summary>One submission with every value (forms-api.yaml, <c>Submission</c>).</summary>
public sealed record SubmissionResponse(
    Guid Id,
    string FormId,
    string FormName,
    bool FormPublished,
    string PagePath,
    DateTimeOffset ReceivedAt,
    bool Read,
    string Notification,
    string Preview,
    IReadOnlyList<SubmissionValueResponse> Values,
    bool Spam,
    int? RetentionDays,
    string? NotificationError);

/// <summary>A value: one string, or a list for a checkbox group.</summary>
public sealed record SubmissionValueResponse(string FieldId, string Label, string Type, object Value);

public sealed record CountResponse(int Count);

public sealed record DeletedResponse(int Deleted);

internal static class SubmissionResponses
{
    public static SubmissionSummaryResponse Summary(Submission submission, IReadOnlyDictionary<string, (FormRecord Form, bool Published)> forms) =>
        new(
            submission.Id,
            submission.FormId,
            forms.TryGetValue(submission.FormId, out var form) ? form.Form.Name : submission.FormName,
            forms.TryGetValue(submission.FormId, out var known) && known.Published,
            submission.PagePath,
            submission.ReceivedAtUtc,
            submission.IsRead,
            submission.NotificationState,
            SubmissionReads.Preview(SubmissionReads.Values(submission)));

    public static SubmissionResponse Detail(
        Submission submission, IReadOnlyDictionary<string, (FormRecord Form, bool Published)> forms, string? notificationError)
    {
        var summary = Summary(submission, forms);
        return new SubmissionResponse(
            summary.Id,
            summary.FormId,
            summary.FormName,
            summary.FormPublished,
            summary.PagePath,
            summary.ReceivedAt,
            summary.Read,
            summary.Notification,
            summary.Preview,
            [.. SubmissionReads.Values(submission).Select(v => new SubmissionValueResponse(
                v.FieldId, v.Label, v.Type, v.Type == FieldTypes.Checkboxes ? v.Values : (object)(v.Values.Count > 0 ? v.Values[0] : string.Empty)))],
            submission.IsSpam,
            forms.TryGetValue(submission.FormId, out var form) ? form.Form.RetentionDays : null,
            notificationError);
    }
}
