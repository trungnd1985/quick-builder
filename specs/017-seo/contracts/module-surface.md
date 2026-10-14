# Module Surface: SEO, Sitemap and Redirects

What each module exposes to the others in this feature. Public interfaces live in the module's
`Contracts` project (Principle I). Decisions: [research.md](../research.md).

## 1. Reference edges

**No new edges.** Every use below runs over an existing implementation-to-Contracts reference:

| From (implementation) | To (Contracts) | New use |
|---|---|---|
| `QuickBuilder.Rendering` | `Pages.Contracts` | `IRedirectReader`, `IRedirectHits`, `ISitemapReader`, `GetLiveAncestorsAsync` |
| `QuickBuilder.Rendering` | `Cms.Contracts` | `ListAddressesAsync` |
| `QuickBuilder.Rendering` | `Sites.Contracts` | search settings on `SiteDto` |
| `QuickBuilder.Pages` | `Sites.Contracts` | the site's default image and logo, for media refresh (SE-20) |
| `QuickBuilder.Pages` | `Cms.Contracts` | `Cms.ItemRedirectRequested`; item checks for `item` targets |
| `QuickBuilder.Cms` | `Pages.Contracts` | existing `ITemplateReader`, to know whether an item has a live address |

Sites still references no other module (SE-08).

## 2. Pages.Contracts

```csharp
// Public path, read-only connection (SE-10). Enabled redirects only.
public interface IRedirectReader
{
    /// Step 3 of public-seo.md §1: an exact redirect with this canonical query; null when none.
    Task<RedirectMatch?> FindQueryAsync(Guid siteId, string path, string canonicalQuery, CancellationToken ct = default);

    /// Steps 6–7: the exact redirect without a query, else the longest prefix; null when none.
    Task<RedirectMatch?> FindAsync(Guid siteId, string path, CancellationToken ct = default);
}

public sealed record RedirectMatch(
    Guid RedirectId, int StatusCode, bool FromQuerySource,
    RedirectTargetKind TargetKind, Guid? PageId, Guid? CollectionId, Guid? ItemId,
    string? Path, string? Url,
    string Remainder);                  // "" for exact; the rest of the path after a prefix source

public enum RedirectTargetKind { Page, Item, Path, Url }

/// How queries compare (FR-017a): Pages stores sources this way, Rendering canonicalises the request.
public static class CanonicalQuery
{
    public static string FromRaw(string? rawQuery);
    public static string FromPairs(IEnumerable<KeyValuePair<string, string?>> pairs);
}

/// Counting without writing on the request path (SE-12). Singleton; never throws.
public interface IRedirectHits
{
    void Record(Guid siteId, Guid redirectId);
}

/// Sitemap inputs, read-only (SE-14, SE-15).
public interface ISitemapReader
{
    Task<long> GetGenerationAsync(Guid siteId, CancellationToken ct = default);
    Task<SitemapPages> GetPagesAsync(Guid siteId, CancellationToken ct = default);
}

public sealed record SitemapPages(IReadOnlyList<SitemapPage> Pages, IReadOnlyList<SitemapTemplate> Templates);
public sealed record SitemapPage(Guid PageId, string Path, DateTimeOffset LiveSinceUtc, bool Indexable);
public sealed record SitemapTemplate(Guid PageId, string Pattern, Guid CollectionId, DateTimeOffset LiveSinceUtc,
    bool Indexable, string? HideFieldId);

// IPublishedContentReader gains (SE-18):
Task<IReadOnlyList<LiveAncestor>> GetLiveAncestorsAsync(Guid siteId, Guid pageId, CancellationToken ct = default);
public sealed record LiveAncestor(Guid PageId, string Path, string Title, bool IsHome);   // root first
```

`LivePageVersion` is unchanged: `seo` travels in the document.

## 3. Cms.Contracts

```csharp
// IPublishedItemReader gains (SE-14): published addresses for the sitemap, keyset by id.
Task<IReadOnlyList<PublishedItemAddress>> ListAddressesAsync(
    Guid siteId, Guid collectionId, string? hideFieldId, Guid? afterId, int limit, CancellationToken ct = default);

public sealed record PublishedItemAddress(Guid ItemId, string Slug, DateTimeOffset LastPublishedAtUtc, bool Hidden);

// Event (SE-11)
public sealed record ItemRedirectRequested(
    Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid CollectionId, Guid ItemId,
    string OldSlug, string Reason /* moved | removed */, Guid? TargetPageId, int StatusCode, Guid? ActorUserId)
    : IIntegrationEvent
{
    public static string EventType => "Cms.ItemRedirectRequested";
}
```

```csharp
// ICollectionSchemaReader (admin-side) gains: the collection and published slug of items that
// redirects target, for checking item targets and showing their address (SE-09).
Task<IReadOnlyDictionary<Guid, ItemAddressParts>> GetItemAddressPartsAsync(
    Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

public sealed record ItemAddressParts(Guid CollectionId, string? PublishedSlug);
```

`OldSlug` is the one slug carried by an event: an address, sent only on request and never
logged (SE-11). This is an addition to 001 `integration-events.md`'s payload rule, next to
`path`.

## 4. Sites.Contracts

```csharp
// SiteDto gains (init properties, as in 009 S-17):
public SiteSearchSettings Search { get; init; } = SiteSearchSettings.None;
public bool RedirectOtherHostNames { get; init; }

public sealed record SiteSearchSettings(
    string? TitleSuffix, Guid? DefaultImageId, string? OrganisationName, Guid? OrganisationLogoId,
    bool Discouraged, string? RobotsRules)
{
    public static SiteSearchSettings None { get; } = new(null, null, null, null, false, null);
}
```

## 5. Media.Contracts

`PublicMediaFile` gains `string? AltText = null` (additive, SE-05). Media fills it from the
file's alternative text.

## 6. Events and subscriptions

| Event | Publisher | New subscriber or behaviour |
|---|---|---|
| **+** `Cms.ItemRedirectRequested` | Cms | Pages: create the redirect (idempotent) |
| `Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted`, `CollectionChanged`, `CollectionDeleted` | Cms | Pages: also bump `sitemap_state` |
| `Media.FileChanged`, `Media.FileDeleted` | Media | Pages: also bump every live page when the file is the site's default image or logo |

`specs/001-foundation-skeleton/contracts/integration-events.md` gains a "017" section with the
event and these subscriptions.

## 7. Obligations on later features

- **#20 site export and import**: carry `pages.redirects` (remapping `page` and `item` target
  ids) and the site search settings. Page metadata travels in page documents already (FR-040).
- **#22 draft previews**: preview responses must write `noindex` and never appear in the
  sitemap.
