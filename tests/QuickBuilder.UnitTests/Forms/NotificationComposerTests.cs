using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Rendering.Features.NotFound;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>
/// The notification email (018 FR-031 to FR-033; research FS-19): subject, plain-text body with
/// or without values, the admin link or the sentence that replaces it, the reply address, and
/// the fixed words in the site's language.
/// </summary>
public sealed class NotificationComposerTests
{
    private static readonly Guid Site = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Received = new(2026, 10, 22, 9, 30, 15, TimeSpan.Zero);

    private const string Values = """
        [{"fieldId":"e1","label":"Email","type":"email","value":"anna@example.com"},
         {"fieldId":"m1","label":"Message","type":"textarea","value":"Hello\nSubject: injected"},
         {"fieldId":"t1","label":"Topics","type":"checkboxes","value":["Sales","Support"]},
         {"fieldId":"p1","label":"Phone","type":"tel","value":""}]
        """;

    private static readonly AdminLinks WithLink = new("https://cms.example.com/", "/admin");

    private static Submission Submission(string values = Values) =>
        QuickBuilder.Forms.Domain.Submission.Receive(Site, new SubmissionReceipt(
            "contact", "Contact", Guid.NewGuid(), "/contact", Received, values, "", [1], IsSpam: false, NotificationStates.Pending));

    private static FormRecord Form(bool includeValues = true, string? subject = null, string? replyFieldId = "e1")
    {
        var form = new FormRecord(Site, "contact");
        form.Apply(new FormSettings("Contact", null, includeValues, ["a@example.com", "b@example.com"], subject, replyFieldId, "[]"), Received, Received);
        return form;
    }

    private static NotificationComposer Composer(AdminLinks? links = null) => new(new FormNotificationTextCatalogue(), links ?? WithLink);

    [Fact]
    public void The_default_subject_names_the_form_and_every_recipient_is_addressed()
    {
        var message = Composer().Compose(Submission(), Form(), "Acme", "en");

        Assert.Equal("New submission: Contact", message.Subject);
        Assert.Equal(["a@example.com", "b@example.com"], message.To);
    }

    [Fact]
    public void A_custom_subject_is_kept_on_one_line()
    {
        var message = Composer().Compose(Submission(), Form(subject: "Enquiry\r\nBcc: x@y.z"), "Acme", "en");

        Assert.Equal("Enquiry Bcc: x@y.z", message.Subject);
    }

    [Fact]
    public void The_body_lists_values_by_label_with_lines_of_a_long_value_indented()
    {
        var submission = Submission();
        var body = Composer().Compose(submission, Form(), "Acme", "en").TextBody;

        Assert.Equal(
            "Someone sent the form \"Contact\" on Acme.\n\n"
            + "Page: /contact\n"
            + "Received: 2026-10-22 09:30 UTC (2026-10-22T09:30:15Z)\n\n"
            + "Email: anna@example.com\n"
            + "Message:\n    Hello\n    Subject: injected\n"
            + "Topics: Sales, Support\n\n"
            + "Open it in the admin:\n"
            + $"https://cms.example.com/admin/sites/{Site}/submissions/{submission.Id}\n\n"
            + "-- \nSent by Acme.\n",
            body);
    }

    [Fact]
    public void Without_values_the_body_says_so_and_there_is_no_reply_address()
    {
        var message = Composer().Compose(Submission(), Form(includeValues: false), "Acme", "en");

        Assert.DoesNotContain("anna@example.com", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("The values are not included in this email.", message.TextBody, StringComparison.Ordinal);
        Assert.Null(message.ReplyTo);
    }

    [Fact]
    public void Without_a_public_url_the_email_says_where_to_look_instead_of_linking()
    {
        var body = Composer(new AdminLinks("", "/admin")).Compose(Submission(), Form(), "Acme", "en").TextBody;

        Assert.Contains("Open Submissions in the admin to read it.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reply_address_is_the_reply_field_when_it_holds_an_email_address()
    {
        Assert.Equal("anna@example.com", Composer().Compose(Submission(), Form(), "Acme", "en").ReplyTo);

        var invalid = Values.Replace("anna@example.com", "not an address", StringComparison.Ordinal);
        Assert.Null(Composer().Compose(Submission(invalid), Form(), "Acme", "en").ReplyTo);
        Assert.Null(Composer().Compose(Submission(), Form(replyFieldId: null), "Acme", "en").ReplyTo);
    }

    [Fact]
    public void The_fixed_words_follow_the_site_language()
    {
        var message = Composer().Compose(Submission(), Form(), "Acme", "de");

        Assert.Equal("Neue Einsendung: Contact", message.Subject);
        Assert.StartsWith("Jemand hat das Formular „Contact“ auf Acme abgeschickt.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Seite: /contact", message.TextBody, StringComparison.Ordinal);
    }
}
