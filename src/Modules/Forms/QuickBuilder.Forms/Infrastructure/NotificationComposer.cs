using System.Globalization;
using System.Text;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Shared.Infrastructure.Email;
using QuickBuilder.Shared.Kernel.Routing;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Where the admin is reached from outside, for the link in a notification (018 research FS-19).
/// Built from <c>Hosting:PublicUrl</c>, never from a request (002 FR-011b). An empty public
/// URL means emails carry no link.
/// </summary>
internal sealed record AdminLinks(string PublicUrl)
{
    public bool CanLink => !string.IsNullOrWhiteSpace(PublicUrl);

    /// <summary>The submission's page in the admin, or null when there is no public URL.</summary>
    public string? Submission(Guid siteId, Guid submissionId) =>
        CanLink ? $"{PublicUrl.TrimEnd('/')}{AdminRoutes.Root}/sites/{siteId}/submissions/{submissionId}" : null;
}

/// <summary>
/// Builds the notification for a submission (018 FR-031 to FR-033; research FS-19): plain text,
/// the fixed words in the site's language, the values only when the form says so, and a reply
/// address only from a valid email value.
/// </summary>
/// <remarks>
/// Nothing a visitor typed reaches a header except the reply address, which must pass the email
/// rule; the subject comes from the form's settings and loses its line breaks in the sender.
/// </remarks>
internal sealed class NotificationComposer(IFormNotificationTexts catalogue, AdminLinks links)
{
    private const string Indent = "    ";

    public EmailMessage Compose(Submission submission, FormRecord form, string siteName, string? language)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(form);

        var texts = catalogue.For(language);
        var values = SubmissionReads.Values(submission);
        var subject = OneLine(string.IsNullOrWhiteSpace(form.Subject) ? Fill(texts.Subject, form.Name, siteName) : form.Subject);

        var body = new StringBuilder();
        body.Append(Fill(texts.Intro, form.Name, siteName)).Append('\n').Append('\n');
        body.Append(texts.Page).Append(": ").Append(submission.PagePath).Append('\n');
        body.Append(texts.Received).Append(": ")
            .Append(submission.ReceivedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))
            .Append(" (").Append(submission.ReceivedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(')')
            .Append('\n').Append('\n');

        if (form.IncludeValues)
        {
            foreach (var value in values.Where(v => v.Values.Count > 0))
            {
                AppendValue(body, value);
            }
        }
        else
        {
            body.Append(texts.ValuesOff).Append('\n');
        }

        body.Append('\n');
        if (links.Submission(submission.SiteId, submission.Id) is { } link)
        {
            body.Append(texts.Open).Append('\n').Append(link).Append('\n');
        }
        else
        {
            body.Append(texts.OpenWithoutLink).Append('\n');
        }

        body.Append('\n').Append("-- ").Append('\n').Append(Fill(texts.Footer, form.Name, siteName)).Append('\n');

        return new EmailMessage([.. form.Recipients], subject, body.ToString(), ReplyTo(form, values));
    }

    /// <summary>The reply field's value, when values are included and it is a valid email address (FR-033).</summary>
    private static string? ReplyTo(FormRecord form, IReadOnlyList<StoredValue> values)
    {
        if (!form.IncludeValues || form.ReplyFieldId is null)
        {
            return null;
        }

        var reply = values.FirstOrDefault(v => v.FieldId == form.ReplyFieldId)?.Values is [var first, ..] ? first : null;
        return reply is not null && FormValueRules.IsEmail(reply) ? reply : null;
    }

    // "Label: value" on one line; a value of several lines goes under its label, indented, so
    // a line of the value can never pass for another label.
    private static void AppendValue(StringBuilder body, StoredValue value)
    {
        var text = string.Join(", ", value.Values);
        var lines = text.Split('\n');
        if (lines.Length == 1)
        {
            body.Append(value.Label).Append(": ").Append(text).Append('\n');
            return;
        }

        body.Append(value.Label).Append(':').Append('\n');
        foreach (var line in lines)
        {
            body.Append(Indent).Append(line).Append('\n');
        }
    }

    // A header holds one line; the sender strips line breaks again, so neither alone is trusted.
    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Fill(string template, string form, string site) =>
        template.Replace("{form}", form, StringComparison.Ordinal).Replace("{site}", site, StringComparison.Ordinal);
}
