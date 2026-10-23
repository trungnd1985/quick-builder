using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// Which design version a site's visitors get (012 research PR-17, data-model §2.1).
/// </summary>
/// <remarks>
/// No row means the site has never published its design, and pages render with the built-in
/// defaults as design number 0 (FR-010). A publish copies the draft token set into a new
/// <see cref="DesignVersion"/>; a rollback copies an older version. There is no unpublish: a
/// site always has a design.
/// </remarks>
internal sealed class DesignPublication : ISiteOwned
{
    /// <summary>Versions kept per site, never counting out the live one (PR-15).</summary>
    public const int RetainedVersions = 50;

    private DesignPublication(Guid siteId)
    {
        SiteId = siteId;
        NextNumber = 1;
    }

    // EF Core materialisation.
    private DesignPublication()
    {
    }

    public Guid SiteId { get; private set; }

    public int LiveNumber { get; private set; }

    public int NextNumber { get; private set; }

    /// <summary>The draft token set version the live one was copied from; null after a rollback.</summary>
    public long? PublishedFromTokenVersion { get; private set; }

    public DateTimeOffset LiveSinceUtc { get; private set; }

    public Guid LiveBy { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>; serialises concurrent publishes.</summary>
    public uint Version { get; private set; }

    public static DesignPublication Start(Guid siteId) => new(siteId);

    /// <summary>Publishes a copy of the draft token set as the next version.</summary>
    public DesignVersion Publish(string document, int schemaVersion, long tokenSetVersion, Guid actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        var version = new DesignVersion(SiteId, NextNumber, schemaVersion, document, DesignVersionKinds.Publish, null, actor, now);
        MakeLive(version, tokenSetVersion, actor, now);
        return version;
    }

    /// <summary>
    /// Publishes a retained version again, as the next version. The draft is not touched. The
    /// document goes live at the current schema version (019 FR-022); the source keeps its own.
    /// </summary>
    public DesignVersion Rollback(DesignVersion source, string currentDocument, int currentSchemaVersion, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(currentDocument);

        var version = new DesignVersion(
            SiteId, NextNumber, currentSchemaVersion, currentDocument, DesignVersionKinds.Rollback, source.Number, actor, now);
        MakeLive(version, tokenSetVersion: null, actor, now);
        return version;
    }

    /// <summary>Whether the draft token set differs from what visitors get.</summary>
    public bool HasUnpublishedChanges(long draftTokenVersion) => PublishedFromTokenVersion != draftTokenVersion;

    private void MakeLive(DesignVersion version, long? tokenSetVersion, Guid actor, DateTimeOffset now)
    {
        LiveNumber = version.Number;
        NextNumber = version.Number + 1;
        PublishedFromTokenVersion = tokenSetVersion;
        LiveSinceUtc = now;
        LiveBy = actor;
    }
}

/// <summary>How a design version came about.</summary>
internal static class DesignVersionKinds
{
    public const string Publish = "publish";

    public const string Rollback = "rollback";
}

/// <summary>An immutable copy of a site's token set, as published (012 data-model §2.2).</summary>
internal sealed class DesignVersion : ISiteOwned
{
    internal DesignVersion(
        Guid siteId,
        int number,
        int schemaVersion,
        string document,
        string kind,
        int? restoredFromNumber,
        Guid publishedBy,
        DateTimeOffset publishedAtUtc)
    {
        SiteId = siteId;
        Number = number;
        SchemaVersion = schemaVersion;
        Document = document;
        Kind = kind;
        RestoredFromNumber = restoredFromNumber;
        PublishedBy = publishedBy;
        PublishedAtUtc = publishedAtUtc;
    }

    // EF Core materialisation.
    private DesignVersion()
    {
        Document = string.Empty;
        Kind = string.Empty;
    }

    public Guid SiteId { get; private set; }

    public int Number { get; private set; }

    public int SchemaVersion { get; private set; }

    /// <summary>The token set document (docs/contracts/design-tokens.md) as JSON text.</summary>
    public string Document { get; private set; }

    /// <summary>One of <see cref="DesignVersionKinds"/>.</summary>
    public string Kind { get; private set; }

    public int? RestoredFromNumber { get; private set; }

    public Guid PublishedBy { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }
}
