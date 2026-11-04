using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// A site's design tokens: its colours, typography and spacing (research ED-14, data-model §2).
/// </summary>
/// <remarks>
/// <para>
/// One per site, and only once the site saves its own: until then there is no row, and reads
/// return the contract's default set with version 0. So a new site, and every site that
/// existed before this feature, starts with the defaults without any data being written
/// (011 FR-032, FR-043).
/// </para>
/// <para>
/// A save replaces the whole document, and only when the caller saw the current version
/// (<c>xmin</c>). The document is stored as <c>jsonb</c>; this type trusts its input, which
/// the handler has already validated against the token contract.
/// </para>
/// <para>
/// These are draft settings: changing them changes nothing visitors see (FR-034). Publishing
/// (#12) adds the published copy beside this one.
/// </para>
/// </remarks>
internal sealed class DesignTokenSet : ISiteOwned
{
    private DesignTokenSet(Guid siteId, string document, int schemaVersion, Guid actor, DateTimeOffset now)
    {
        SiteId = siteId;
        Document = document;
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private DesignTokenSet()
    {
        Document = string.Empty;
    }

    public Guid SiteId { get; private set; }

    /// <summary>Always equal to the document's own <c>schemaVersion</c>.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The validated token set, as JSON text.</summary>
    public string Document { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>, sent to clients as <c>version</c>.</summary>
    public uint Version { get; private set; }

    /// <summary>A site's first saved token set. The document has already passed validation.</summary>
    public static DesignTokenSet Create(Guid siteId, string document, int schemaVersion, Guid actor, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ArgumentNullException.ThrowIfNull(clock);
        return new DesignTokenSet(siteId, document, schemaVersion, actor, clock.GetUtcNow());
    }

    /// <summary>
    /// A site's token draft as an imported archive holds it (020 FR-021): its document, already
    /// validated by the import's check, and who last changed it and when.
    /// </summary>
    public static DesignTokenSet Import(Guid siteId, string document, int schemaVersion, Guid updatedBy, DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        return new DesignTokenSet(siteId, document, schemaVersion, updatedBy, updatedAtUtc);
    }

    /// <summary>Replaces the document with one that has already passed validation.</summary>
    public void Replace(string document, int schemaVersion, Guid actor, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ArgumentNullException.ThrowIfNull(clock);

        Document = document;
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = clock.GetUtcNow();
        UpdatedBy = actor;
    }
}
