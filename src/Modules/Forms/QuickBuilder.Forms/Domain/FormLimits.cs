using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// The bounds the page schema puts on forms (docs/contracts/page-schema/v1.schema.json,
/// <c>$defs/form</c>; 018 contracts/form-block.md §2), as the Forms tables store them.
/// </summary>
internal static class FormLimits
{
    /// <summary>A form's id is its node id.</summary>
    public const int MaxFormIdLength = PageSchema.MaxNodeIdLength;

    public const int MaxNameLength = 100;

    public const int MaxSubjectLength = 200;

    /// <summary>Field ids are 12 lowercase hexadecimal characters.</summary>
    public const int FieldIdLength = 12;

    public const int MaxRecipients = 10;
}
