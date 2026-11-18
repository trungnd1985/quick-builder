# Module Surface: Draft Preview Links

What crosses module boundaries or the platform layer in feature 022. Research: PV-01, PV-02,
PV-08, PV-11, PV-16.

## 1. Edges

**No new edges.**
- Rendering already references Pages, Cms, Editor, Media, Identity and Sites contracts.
- Editor already references Pages, Cms, Identity and Sites contracts.
- Pages and Cms already reference `Editor.Contracts` (023).

`ModuleBoundaryTests` is unchanged.

## 2. New contracts

### 2.1 Pages.Contracts.Publishing

```csharp
/// Drafts and retained versions of pages, components and menus, for previews only (022 PV-02).
/// Never used by the public responder (architecture test). Read-only connection; site and
/// trash filters apply, so a trashed page is absent.
public interface IPreviewContentReader
{
    Task<PreviewPage?> GetPageDraftAsync(Guid siteId, Guid pageId, CancellationToken ct = default);
    Task<PreviewPageVersion?> GetPageVersionAsync(Guid siteId, Guid pageId, int number, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, LiveDocument>> GetUnpublishedComponentDraftsAsync(Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, LiveMenu>> GetUnpublishedMenuDraftsAsync(Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken ct = default);
    Task<PreviewPage?> GetTemplateDraftAsync(Guid siteId, Guid collectionId, CancellationToken ct = default);
    /// Current titles of pages, for the site-wide link list (FR-015). Absent ids are not pages of the site.
    Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken ct = default);
}

public sealed record PreviewPage(
    Guid PageId, string Title, string? Path, bool IsTemplate, Guid? CollectionId,
    int SchemaVersion, JsonElement Document, DateTimeOffset SavedAtUtc, Guid SavedBy,
    DateTimeOffset? PendingScheduleAtUtc, bool IsLive);

public sealed record PreviewPageVersion(
    Guid PageId, int Number, string Title, string? Path, int SchemaVersion, JsonElement Document,
    DateTimeOffset PublishedAtUtc, Guid PublishedBy);
```

### 2.2 Cms.Contracts.Publishing

```csharp
/// Item drafts and retained versions, for previews only (022 PV-02).
public interface IPreviewItemReader
{
    Task<PreviewItem?> GetDraftAsync(Guid siteId, Guid itemId, CancellationToken ct = default);
    Task<PreviewItemVersion?> GetVersionAsync(Guid siteId, Guid itemId, int number, CancellationToken ct = default);
    /// Current names of items, for the site-wide link list (FR-015).
    Task<IReadOnlyDictionary<Guid, (Guid CollectionId, string? Name)>> GetNamesAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
}

/// Item is built from draft values and the draft slug, in the shape bindings read (016 TB-09).
public sealed record PreviewItem(
    PublishedItem Item, DateTimeOffset SavedAtUtc, Guid SavedBy, bool NeedsAttention,
    DateTimeOffset? PendingScheduleAtUtc, bool IsPublished);

public sealed record PreviewItemVersion(PublishedItem Item, int Number, DateTimeOffset PublishedAtUtc, Guid PublishedBy);
```

### 2.3 Editor.Contracts

```csharp
/// Resolves a preview link's secret on every request (022 PV-08, PV-18). Never cached.
public interface IPreviewLinks
{
    Task<PreviewLinkResolution> ResolveAsync(string secret, IPAddress? client, CancellationToken ct = default);
}

public abstract record PreviewLinkResolution
{
    public sealed record Found(
        Guid LinkId, Guid SiteId, string DocumentKind, Guid DocumentId, int? VersionNumber,
        Guid? SampleItemId, DateTimeOffset ExpiresAtUtc) : PreviewLinkResolution;
    public sealed record NotAvailable : PreviewLinkResolution;
    public sealed record Throttled(TimeSpan RetryAfter) : PreviewLinkResolution;
}

public static class PreviewCapabilities
{
    /// List and revoke every preview link of a site (FR-015). Granted to no site role: Administrator only.
    public const string Manage = "editor.preview_links.manage";
}
```

`ResolveAsync` checks the target kind's edit capability for the creator through `IMemberAccess`.
Rendering checks that the target is readable through §2.1 and §2.2, because Editor does not
read content.

### 2.4 Identity.Contracts

```csharp
/// Whether another user may act on a site now (022 PV-08). Reads grants and status on every
/// call; never cached. A deactivated user holds nothing; Administrator holds everything.
public interface IMemberAccess
{
    Task<bool> HoldsCapabilityAsync(Guid userId, string capability, Guid siteId, CancellationToken ct = default);
}
```

### 2.5 Media.Contracts

```csharp
/// A current media file of the site in ISiteContext, by its public address, with the same
/// parsing, version and trash rules as /_media (022 PV-11). Null when not found.
public interface IMediaContent
{
    Task<MediaContent?> OpenAsync(string address, CancellationToken ct = default);
}

public sealed record MediaContent(Stream Content, string ContentType, long Length, string ETag, DateTimeOffset LastModifiedUtc);
```

## 3. Integration events (Editor outbox; specs/001 contracts/integration-events.md)

| Event | Payload | Consumers |
|---|---|---|
| `Editor.PreviewLinkCreated` | `SiteId, DocumentKind, DocumentId, LinkId, VersionNumber?, ExpiresAtUtc, By` | Pages (page history), Cms (item history) |
| `Editor.PreviewLinkRevoked` | `SiteId, DocumentKind, DocumentId, LinkId, By` | Pages, Cms |

Ids, a number and a time only: no secret and no label. Handlers ignore events for other kinds
and are idempotent (PV-16).

## 4. Capabilities (specs/002 contracts/authorization.md)

| Capability | Scope | Site roles | Registered by |
|---|---|---|---|
| `editor.preview_links.manage` | Site | none (Administrator only) | Editor |

Everything else reuses existing capabilities (PV-09).

## 5. Platform (Host)

- `RoutePrefixes.Preview = "/_preview"`, in a new `UnresolvedPublicPrefixes` list:
  - `SiteResolutionMiddleware` skips it;
  - it is **not** in `IsReserved`, so the log redactor removes its paths.
- `PublicPathConstraint.AdministrativeSegments` gains `_preview`.
- `PreviewOptions` (`Preview:LinksValidFrom`) is bound and validated with `ValidateOnStart`.

## 6. Transfer (020)

`DesignArchivePart.Import` deletes the site's `editor.preview_links` rows in the import
transaction (PV-20). The archive format is unchanged.
