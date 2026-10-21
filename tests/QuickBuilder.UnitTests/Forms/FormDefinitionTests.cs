using System.Text.Json;
using QuickBuilder.Forms.Domain;
using QuickBuilder.UnitTests.Pages;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>
/// Reading a published <c>form</c> node (018 research FS-03, FS-05; contracts/form-block.md §2),
/// from the shared golden examples.
/// </summary>
public sealed class FormDefinitionTests
{
    [Fact]
    public void Every_form_of_a_document_is_found_in_document_order_however_deep()
    {
        using var document = Example("form-every-field-type.json");
        using var component = Example("form-in-component.json");

        Assert.Equal(["contact-form", "newsletter"], FormDefinition.FindAll(document.RootElement).Select(f => f.Id));
        Assert.Equal(["footer-form"], FormDefinition.FindAll(component.RootElement).Select(f => f.Id));
    }

    [Fact]
    public void A_document_without_forms_has_none()
    {
        using var document = Example("navigation-minimal.json");

        Assert.Empty(FormDefinition.FindAll(document.RootElement));
    }

    [Fact]
    public void Fields_keep_their_order_type_and_rules()
    {
        var form = Contact();

        Assert.Equal("Contact enquiry", form.Name);
        Assert.Equal(
            ["text", "email", "phone", "textarea", "number", "date", "select", "radio", "checkboxes", "checkbox", "consent"],
            form.Fields.Select(f => f.Type));

        var name = form.Fields[0];
        Assert.Equal(("a1b2c3d4e5f6", "Name", true, "Jane Doe", 100), (name.Id, name.Label, name.Required, name.Placeholder, name.MaxLength));

        var email = form.Fields[1];
        Assert.Equal(("We reply to this address.", 254), (email.Help, email.MaxLength));

        Assert.Equal("Tell us how we can help.", form.Fields[3].Message);
        Assert.Equal((1m, 12m, true), (form.Fields[4].Min, form.Fields[4].Max, form.Fields[4].WholeNumber));
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)), (form.Fields[5].Earliest, form.Fields[5].Latest));
        Assert.Equal(["sales", "support", "other"], form.Fields[6].Options.Select(o => o.Value));
        Assert.Equal((1, 2), (form.Fields[8].MinSelected, form.Fields[8].MaxSelected));

        var consent = form.Fields[10];
        Assert.True(consent.Required);
        Assert.StartsWith("I agree", consent.Statement, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e41"), consent.LinkPageId);
    }

    [Fact]
    public void Defaults_fill_what_the_node_leaves_out()
    {
        var newsletter = FormDefinition.FindAll(Parse("form-every-field-type.json")).Single(f => f.Id == "newsletter");
        var field = newsletter.Fields.Single();

        Assert.Null(newsletter.SubmitLabel);
        Assert.Null(newsletter.SuccessMessage);
        Assert.Null(newsletter.RetentionDays);
        Assert.True(field.Required);
        Assert.Null(field.MinLength);
        Assert.Equal(254, field.MaxLength);
        Assert.Equal(new FormRedirect(null, "https://newsletter.example/thanks"), newsletter.Redirect);

        var notify = newsletter.Notify;
        Assert.Empty(notify.Recipients);
        Assert.False(notify.IncludeValues);
    }

    [Fact]
    public void A_form_without_notify_includes_values_and_has_no_recipients()
    {
        var form = FormDefinition.FindAll(Parse("form-in-component.json")).Single();

        Assert.Empty(form.Notify.Recipients);
        Assert.Equal((null, true, null), (form.Notify.Subject, form.Notify.IncludeValues, form.Notify.ReplyFieldId));
        Assert.Equal(10_000, form.Fields[1].MaxLength);
    }

    [Fact]
    public void The_redirect_notify_and_retention_are_read()
    {
        var form = Contact();

        Assert.Equal(new FormRedirect(Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e42"), null), form.Redirect);
        Assert.Equal(["sales@client.example", "office@client.example"], form.Notify.Recipients);
        Assert.Equal("New enquiry from the website", form.Notify.Subject);
        Assert.Equal("0a1b2c3d4e5f", form.Notify.ReplyFieldId);
        Assert.Equal(365, form.RetentionDays);
        Assert.Equal("Send", form.SubmitLabel);
    }

    [Fact]
    public void Settings_keep_what_the_registry_needs_and_no_value_rules()
    {
        var settings = Contact().ToSettings();

        Assert.Equal(("Contact enquiry", 365, true, "0a1b2c3d4e5f"), (settings.Name, settings.RetentionDays, settings.IncludeValues, settings.ReplyFieldId));
        using var fields = JsonDocument.Parse(settings.FieldsJson);
        Assert.Equal(11, fields.RootElement.GetArrayLength());
        Assert.Equal(
            ["id", "label", "type"],
            fields.RootElement[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Consent", fields.RootElement[10].GetProperty("label").GetString());
    }

    private static FormDefinition Contact() =>
        FormDefinition.FindAll(Parse("form-every-field-type.json")).Single(f => f.Id == "contact-form");

    private static JsonElement Parse(string file)
    {
        using var document = Example(file);
        return document.RootElement.Clone();
    }

    private static JsonDocument Example(string file) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractFiles.ExamplesDirectory, "valid", file)));
}
