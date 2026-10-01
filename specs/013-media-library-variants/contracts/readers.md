# Contract: Cross-Module Readers, Events and Capabilities

**Feature**: 013 | **Research**: MV-01, MV-05, MV-06, MV-15, MV-17, MV-24

What modules promise each other in this feature. Everything here is in a `*.Contracts`
project or in a shared contract document. Nothing crosses a module boundary any other way
(Principle I).

---

## 1. `Media.Contracts`

### 1.1 `IPublicMediaReader` (changed, additive)

The signature is unchanged (012 readers.md §1.3). `PublicMediaFile` gains three members:

```csharp
public sealed record PublicMediaFile(
    string PublicPath,                              // original of the current content version (MV-08)
    string ContentType,
    int? Width,                                     // displayed width; null until recorded or for PDFs
    int? Height,
    IReadOnlyList<PublicMediaVariant> Variants,     // ascending by width; empty when not eligible (MV-11)
    bool OfferOriginal = false);                    // srcset may add the original: only when it is no
                                                    // wider than the largest configured width (added
                                                    // during implementation; the renderer cannot see
                                                    // the configuration)

public sealed record PublicMediaVariant(int Width, string PublicPath);
```

- **Paths**: relative to the base path, as before. The caller prefixes the base path.
- **Variants**: exactly the configured widths smaller than `Width`, and only for eligible
  files (data-model §3.1). Each address is valid for the file's current content version.
- **Performance**: still one query on the read-only connection (012 PR-06). The variant list
  is computed from configuration, with no storage calls on the render path.
- **Consumer**: Rendering's `ImageRenderer` (public-media.md §2).

### 1.2 `Media.FileChanged` (new event)

```csharp
public sealed record FileChanged(
    Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid FileId,
    string Reason) : IIntegrationEvent          // "replaced" | "dimensions" | "variants"
{
    public static string EventType => "Media.FileChanged";
}
```

- **Published** in the Media outbox, in the transaction that changed the row (MV-17).
- **Carries** ids and a reason only.
- **Subscriber**: Pages' `RefreshPagesShowingMediaHandler`. It bumps the render generation of
  live pages that show the file, directly or through a component. It is idempotent per
  event id, the same as for `Media.FileDeleted`.
- **Naming**: `Media.FileChanged` is a published name. Renaming it is breaking. A new reason
  value is additive, and subscribers must treat unknown reasons like `replaced`.

---

## 2. `Pages.Contracts`

### 2.1 `IMediaUsageReader` (new)

```csharp
public interface IMediaUsageReader
{
    /// Counts per file. Files with no usage are present with zeros.
    Task<IReadOnlyDictionary<Guid, MediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);

    /// Every place that uses the file, pages first, then components, each by title.
    Task<IReadOnlyList<MediaUsagePlace>> ListAsync(
        Guid siteId, Guid mediaId, CancellationToken cancellationToken = default);
}

public sealed record MediaUsageSummary(
    int Places, int DraftPlaces, int LivePlaces, int HistoryOnlyPlaces)
{
    public bool InLiveUse => LivePlaces > 0;
    public bool InUse => Places > 0;
}

public sealed record MediaUsagePlace(
    string Kind,                        // "page" | "component"
    Guid Id,
    string Title,
    bool InDraft,
    bool Live,
    bool InHistory,
    int? LivePagesThroughComponent);    // for live components only
```

- **Semantics**: as in data-model §2.3. `HistoryOnlyPlaces` counts places whose only use is
  in non-live versions.
- **Consistency**: it reads committed data maintained in the same transaction as draft
  saves and publishes (MV-05), so it is never stale.
- **Implementation**: Pages, over `PagesDbContext` with no tracking, filtering explicitly by
  `siteId` (admin reads name the site in the route). `SummariseAsync` is one grouped query
  for up to 100 ids. `ListAsync` is at most three queries.
- **Callers**: Media's listing, get-file, usage, delete and replace handlers. Media never
  caches the result.
- **Titles**: page titles and component names. Media returns them to the admin shell only
  when the caller holds `PageContentCapabilities.View` (§2.2).

### 2.2 Capability names moved into the contract

```csharp
public static class PageContentCapabilities
{
    public const string View = "pages.content.view";   // unchanged name; previously internal to Pages
}
```

- `PagesCapabilities.ContentView` now refers to this constant.
- `PublishingCapabilities.Manage` (`pages.publishing.manage`, 012) is used by Media for
  live-use delete and replace (MV-06).
- Neither name changes, so neither change breaks anything.

---

## 3. Shared infrastructure

- **`IFileStorage`** and **`IFileStorageHealthCheck`**: unchanged (MV-01). The providers
  are in storage-providers.md.
- **`StorageOptions.SupportedProviders`**: now `["Local", "S3", "AzureBlob"]`.

---

## 4. Contract documents updated in the same PR

| Document | Change |
|---|---|
| `specs/001-foundation-skeleton/contracts/integration-events.md` | + `Media.FileChanged` (publisher, payload, subscribers, idempotency) |
| `specs/002-identity-setup/contracts/authorization.md` §2 | + `media.storage.check` (installation, Administrator only) |
| `docs/contracts/page-schema.md` §9 | Image rendering: `srcset`, `sizes`, `width`, `height` (public-media.md §2). The schema file is unchanged. |
| `specs/003-media-storage/contracts/storage-provider.md` | Pointer to this feature's storage-providers.md, for the two new providers and the key layout |

---

## 5. Module references added

| From | To | Why |
|---|---|---|
| `QuickBuilder.Media` | `QuickBuilder.Pages.Contracts` | `IMediaUsageReader`, `PublishingCapabilities`, `PageContentCapabilities` |

- There is no cycle: `Pages.Contracts` references nothing in Media. `QuickBuilder.Pages`
  already references `Media.Contracts` (012).
- The architecture tests continue to allow only Contracts references.
