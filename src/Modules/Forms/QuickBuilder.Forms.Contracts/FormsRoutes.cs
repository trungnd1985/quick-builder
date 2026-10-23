namespace QuickBuilder.Forms.Contracts;

/// <summary>The public platform path forms post to (018 research FS-06; public-forms.md §1).</summary>
public static class FormsRoutes
{
    /// <summary>
    /// Reserved on every site's host names, ahead of the page route. The slug grammar cannot
    /// produce <c>_</c>, so no page address can ever collide with it (page-schema.md §5).
    /// </summary>
    public const string SubmitPrefix = "/_forms";
}
