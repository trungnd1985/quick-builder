using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Forms;

/// <summary>
/// What the Forms module lets people do, by name (018 research FS-20; 002
/// contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Every site role reads submissions, because Editors are the people who answer enquiries;
/// deleting them for good is kept to Designers (clarification 1). Registered from
/// <see cref="FormsModule.AddFormsModule"/>; Identity enforces them without knowing this module
/// exists. Administrator holds all of them everywhere and is never listed. Nothing outside the
/// module names them, so they stay internal, as Cms's do. The names never change once shipped.
/// </remarks>
internal static class FormsCapabilities
{
    /// <summary>
    /// List, open, mark, search, filter and export submissions, see Spam and restore from it,
    /// and see the counters and retention impact. Every site role.
    /// </summary>
    public const string SubmissionsView = "forms.submissions.view";

    /// <summary>Delete submissions and spam for good (FR-028). Designer.</summary>
    public const string SubmissionsDelete = "forms.submissions.delete";

    /// <summary>Send a test email to oneself (FR-037). Administrators only.</summary>
    public const string EmailTest = "forms.email.test";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(SubmissionsView, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(SubmissionsDelete, CapabilityScope.Site, [SystemRole.Designer]),
        // Installation-wide: only Administrators hold it.
        new(EmailTest, CapabilityScope.Installation, []),
    ];
}
