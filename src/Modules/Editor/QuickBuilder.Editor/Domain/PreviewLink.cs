using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// A secret, time-limited link to one page or item of one site (022 data-model §1, research
/// PV-07, PV-08).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate keeps the rules a link is created and ended by: an expiry after now and at most
/// 30 days ahead (clarification 3), a short label, one revocation. The secret itself is handed to
/// the creator once and never stored; only its hash is.
/// </para>
/// <para>
/// Whether the creator still may edit the content, and whether the content can still be read,
/// are not stored states: they are checked on every request (PV-08), so a restore from the trash
/// or a role given back makes an unexpired link work again.
/// </para>
/// </remarks>
internal sealed class PreviewLink : ISiteOwned
{
    /// <summary>The longest a link may last (clarification 3).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    public const int MaxLabelLength = 80;

    /// <summary>The most active links one page or item may have (spec FR-018).</summary>
    public const int MaxActivePerDocument = 20;

    private readonly List<object> _events = [];

    private PreviewLink(
        Guid siteId, string kind, Guid documentId, int? versionNumber, Guid? sampleItemId, byte[] secretHash, string? label, Guid by, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        DocumentKind = kind;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        SampleItemId = sampleItemId;
        SecretHash = secretHash;
        Label = label;
        CreatedBy = by;
        CreatedAtUtc = now;
        ExpiresAtUtc = expiresAt;
    }

    // EF Core materialisation.
    private PreviewLink()
    {
        DocumentKind = string.Empty;
        SecretHash = [];
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary><c>page</c> or <c>item</c>.</summary>
    public string DocumentKind { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>The published version shown; null for the draft.</summary>
    public int? VersionNumber { get; private set; }

    /// <summary>For a template page, the item shown in it.</summary>
    public Guid? SampleItemId { get; private set; }

    /// <summary>SHA-256 of the secret (PV-07). Unique.</summary>
    public byte[] SecretHash { get; private set; }

    /// <summary>Content a member typed: never logged, never in history or events.</summary>
    public string? Label { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? LastOpenedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public uint Version { get; private set; }

    /// <summary>The integration events raised and not yet written to the outbox.</summary>
    public IReadOnlyList<object> Events => _events;

    /// <summary>A new link and its secret, which the caller hands to the creator once.</summary>
    public static Result<(PreviewLink Link, string Secret)> Create(
        Guid siteId,
        string kind,
        Guid documentId,
        int? versionNumber,
        Guid? sampleItemId,
        string? label,
        DateTimeOffset expiresAt,
        Guid by,
        DateTimeOffset now)
    {
        if (expiresAt <= now || expiresAt > now + MaxLifetime)
        {
            return Result.Failure<(PreviewLink, string)>(PreviewLinkErrors.InvalidExpiry);
        }

        var trimmed = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (trimmed is { Length: > MaxLabelLength } || (trimmed?.Any(char.IsControl) ?? false))
        {
            return Result.Failure<(PreviewLink, string)>(PreviewLinkErrors.InvalidLabel);
        }

        var secret = PreviewSecrets.New();
        var link = new PreviewLink(siteId, kind, documentId, versionNumber, sampleItemId, PreviewSecrets.Hash(secret), trimmed, by, now, expiresAt);
        link._events.Add(new PreviewLinkCreated(Guid.CreateVersion7(now), now, siteId, kind, documentId, link.Id, versionNumber, expiresAt, by));
        return Result.Success((link, secret));
    }

    /// <summary>Whether the link works at <paramref name="now"/>, given the operator's switch (PV-07).</summary>
    public bool IsActive(DateTimeOffset now, DateTimeOffset? validFrom) =>
        RevokedAtUtc is null && now < ExpiresAtUtc && (validFrom is not { } from || CreatedAtUtc >= from);

    /// <summary>Ends the link. Revoking it again changes nothing and raises nothing.</summary>
    public void Revoke(Guid by, DateTimeOffset now)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = now;
        RevokedBy = by;
        _events.Add(new PreviewLinkRevoked(Guid.CreateVersion7(now), now, SiteId, DocumentKind, DocumentId, Id, VersionNumber, by));
    }
}

/// <summary>The refusals a preview link's rules give (022 data-model §5).</summary>
internal static class PreviewLinkErrors
{
    public static Error InvalidExpiry { get; } = Error.Validation(
        "preview.invalid_expiry", "Choose an expiry after now and at most 30 days ahead.");

    public static Error InvalidLabel { get; } = Error.Validation(
        "preview.invalid_label", $"A label may be at most {PreviewLink.MaxLabelLength} characters, without control characters.");

    public static Error NotFound { get; } = Error.NotFound("preview.not_found", "No such page or item on this site.");

    public static Error VersionNotRetained { get; } = Error.NotFound("preview.version_not_retained", "That version is no longer kept.");

    public static Error NoTemplate { get; } = Error.Conflict(
        "preview.no_template", "This collection has no template page, so its items cannot be previewed.");

    public static Error LinkLimit { get; } = Error.Conflict(
        "preview.link_limit", $"This page or item already has {PreviewLink.MaxActivePerDocument} active preview links. Revoke one first.");

    public static Error LinkNotFound { get; } = Error.NotFound("preview.link_not_found", "No such preview link.");
}
