namespace QuickBuilder.Rendering.Domain;

/// <summary>Who is looking at a preview (022 research PV-05).</summary>
internal enum PreviewMode
{
    /// <summary>A signed-in member of the site, from the admin.</summary>
    Member,

    /// <summary>Anyone holding a preview link.</summary>
    Link,
}

/// <summary>
/// What makes a render a preview (022 research PV-04, data-model §4.6): the bar it carries and
/// where its addresses point. Set on <see cref="RenderInputs.Preview"/> only for previews.
/// </summary>
internal sealed record PreviewContext(PreviewMode Mode, PreviewBarModel Bar, PreviewAddresses Addresses);

/// <summary>
/// Where a preview's addresses point (022 research PV-06), so the page works from
/// <c>/_preview</c> on any host name and never leads a viewer to another draft.
/// </summary>
/// <param name="PublicSiteUrl">
/// The site's base URL (scheme, primary host name, base path), which links to pages and items
/// of the site use; null when the site has no host name, and such links are then not written.
/// </param>
/// <param name="MediaPrefix">
/// The preview's media route, relative to the base path like public media addresses, e.g.
/// <c>/_preview/l/{secret}/media</c>; replaces <c>/_media</c> in every media address.
/// </param>
/// <param name="AssetsPrefix">The preview's stylesheet route, relative to the base path.</param>
/// <param name="PagerPath">The preview's own address, relative to the base path, which list pager links keep.</param>
/// <param name="FormAction">Where forms post in a preview, relative to the base path (PV-12).</param>
internal sealed record PreviewAddresses(
    string? PublicSiteUrl,
    string MediaPrefix,
    string AssetsPrefix,
    string PagerPath,
    string FormAction)
{
    /// <summary>The prefix media addresses carry on the public site (003 research M-11).</summary>
    public const string PublicMediaPrefix = "/_media";

    /// <summary>
    /// A public media address (<c>/_media/…</c>, without the base path) as the preview serves it;
    /// anything else is returned unchanged.
    /// </summary>
    public string Media(string publicPath)
    {
        ArgumentNullException.ThrowIfNull(publicPath);
        return publicPath.StartsWith(PublicMediaPrefix + "/", StringComparison.Ordinal)
            ? MediaPrefix + publicPath[PublicMediaPrefix.Length..]
            : publicPath;
    }

    /// <summary>
    /// A media file with its original's and every variant's address as the preview serves them,
    /// so every renderer that writes media (images, rich text, bindings) writes preview
    /// addresses without knowing it is in a preview.
    /// </summary>
    /// <remarks>
    /// Addresses stay relative to the base path, as public ones are; renderers prefix it. The
    /// <see cref="MediaPrefix"/> therefore carries no base path either.
    /// </remarks>
    public QuickBuilder.Media.Contracts.PublicMediaFile Media(QuickBuilder.Media.Contracts.PublicMediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file with
        {
            PublicPath = Media(file.PublicPath),
            Variants = [.. file.Variants.Select(v => v with { PublicPath = Media(v.PublicPath) })],
        };
    }
}

/// <summary>
/// The preview bar's content (022 research PV-13). Names and labels are content: the bar
/// encodes them.
/// </summary>
/// <param name="State">What is shown: the draft, or a published version.</param>
/// <param name="At">When the draft was saved, or the version published.</param>
/// <param name="By">Who saved or published it, by display name; null when unknown.</param>
/// <param name="VersionNumber">The version shown; null for the draft.</param>
/// <param name="SampleItemName">For a template page, the item shown in it.</param>
/// <param name="FromDrafts">Parts shown from their drafts because they were never published (PV-03).</param>
/// <param name="ScheduledAt">A pending scheduled publish of the page or item.</param>
/// <param name="NeedsAttention">An item with missing or invalid values (PV-15).</param>
/// <param name="ExpiresAt">For a link viewer, when the link stops working; null for members.</param>
/// <param name="EditAddress">For a member, the editor or item form; null for link viewers.</param>
/// <param name="ShareAddress">For a member, the same with the share dialog open (<c>?share=1</c>).</param>
internal sealed record PreviewBarModel(
    PreviewState State,
    DateTimeOffset At,
    string? By,
    int? VersionNumber = null,
    string? SampleItemName = null,
    IReadOnlyList<PreviewDraftPart>? FromDrafts = null,
    DateTimeOffset? ScheduledAt = null,
    bool NeedsAttention = false,
    DateTimeOffset? ExpiresAt = null,
    string? EditAddress = null,
    string? ShareAddress = null);

/// <summary>Whether a preview shows a draft or a published version.</summary>
internal enum PreviewState
{
    Draft,
    Version,
}

/// <summary>What kind of part a preview shows from its draft (PV-03).</summary>
internal enum PreviewPartKind
{
    Component,
    Menu,
    Design,
    Template,
    SampleItem,
}

/// <summary>A part shown from its draft: its kind and its name (content; encoded when written).</summary>
internal sealed record PreviewDraftPart(PreviewPartKind Kind, string Name);
