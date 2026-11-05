# Contract: Archive Parts and Module Surface

**Feature**: 020 · **Research**: ST-01, ST-03 … ST-12, ST-14

## 1. Interfaces (Sites.Contracts, `Transfer/`)

```text
interface ISiteArchivePart
  string Name { get; }                       // "sites", "media", "design", "cms", "pages"
  int Order { get; }                         // 10, 20, 30, 40, 50
  int Version { get; }                       // part format version, 1
  IReadOnlyDictionary<string,int> Contracts { get; }

  Task ExportAsync(PartExportContext context, CancellationToken ct)
  Task CheckAsync(PartCheckContext context, CancellationToken ct)
  Task ApplyAsync(PartApplyContext context, CancellationToken ct)

PartExportContext
  Guid SiteId
  ISiteTransferSession Session               // join with Session.Enlist(dbContext)
  IArchiveWriter Archive                     // OpenEntry(name) → Stream, WriteLinesAsync<T>(name, rows)
  ITransferProgress Progress
  void Problem(string code, string kind, Guid id)
  void Count(string kind, long n)

PartCheckContext
  ArchiveManifest Manifest
  IArchiveReader Archive                     // ReadLinesAsync<T>(name), OpenEntry(name) (hash-verified)
  ImportMode Mode; Guid? TargetSiteId; bool SameSiteAsTarget
  ISiteTransferSession Session               // read-only snapshot of the target
  IIdMapBuilder Ids                          // Declare(kind, archiveId), Match(kind, archiveId, targetId, matchedBy)
  ICheckReport Report                        // Error/Warning, Conflict, Removal, Return, Change, SchemaChange, PastSchedule

PartApplyContext
  same as check, plus:
  IIdMap Ids                                 // complete, read-only
  ConflictPolicy? Policy; bool StartFresh
  Guid TargetSiteId                          // new site's id is known before parts apply
  IAuthorMap Authors                         // archive user id → id on this installation
  IImportReport Report                       // Resolved, LeftOut, Broken, DroppedSchedule, Removed
  ITransferCancellation Cancellation         // ThrowIfCancelled() between batches

interface ISiteTransferSession              // implemented in Shared.Infrastructure Data/ (Principle I, 2.6.0)
  void Enlist(DbContext context)             // SetDbConnection + UseTransaction
  bool IsReadOnly { get; }

```

As built, there is no write-gate interface for parts or features to call: the gate is
`SiteWriteGateInterceptor` in Shared.Infrastructure `Data/`, a SaveChanges interceptor on
the Sites, Media, Editor, Cms and Pages contexts (research ST-07).

Rules:
- A part reads and writes only its own module's tables, through its own `DbContext`
  enlisted in the session.
- It calls other modules only through their existing Contracts interfaces (validation of
  references uses the id map, not other modules' tables).
- `CheckAsync` must not write. `ApplyAsync` must not call `SaveChanges` outside the session,
  and must write its outbox events through its usual outbox helper on the enlisted context.
- `ApplyAsync` declares no new id. Every id comes from `IIdMap`, which the orchestrator
  completes from every part's `CheckAsync` (`Declare` gives an unmatched entry a new id, or
  its original id for restore).
- Parts handle `ImportMode.NewSite`, `Merge` and `Restore`, and `StartFresh` (ST-11, ST-12).

## 2. Edges and events

- **No new module edge.** Pages, Cms, Media and Editor already reference Sites.Contracts.
  Identity already consumes Sites events (`RecordSiteSecurityEvents` handles `SiteDisabled`
  and others), and its handler gains the four transfer events.
- **New Identity.Contracts surface**: `IHistoricalAuthors.EnsureAsync(IReadOnlyList<(Guid sourceId, string displayName)>, ISiteTransferSession) → IReadOnlyDictionary<Guid,Guid>`.
  `IUserDirectory` display-name lookups gain `IsHistorical`.
- **New Sites.Contracts events**: `SiteExportFinished`, `SiteImportFinished`,
  `SiteArchiveDownloaded`, `SiteArchiveDeleted` (ST-19). Consumers: Identity (security
  events). `SiteCreated` is unchanged and is raised for a new site.
- **Existing events raised by apply**, so that the usual consumers rebuild their state after
  the commit:
  - Pages `PageCreated`, `ComponentCreated`, `PagePublished`, `PageUnpublished`,
    `ComponentPublished` and `ComponentUnpublished`: cache, sitemap and the form registry.
    Menus have no integration events (024 deferred them); Pages rebuilds `menu_targets` and
    `menu_usages` itself, inside the part's apply;
  - Cms `CollectionCreated`, `CollectionChanged`, `ItemCreated` and `ItemPublished`: list
    refresh and media usage;
  - Editor `DesignPublished`;
  - Media `FileChanged`: variants and metadata backfill;
  - `PageDeleted`, `ComponentDeleted`, `CollectionDeleted`, `ItemDeleted` and `FileDeleted`
    for what a restore removes.

  Handlers are idempotent already (Principle I).

## 3. Entries per part (format 1)

**As built** (2026-11-02). The exact field names and their order are pinned by the part golden
tests (`UTESTS/<Module>/Transfer/*PartGoldenTests.cs`); the record types are in each part's
`*ArchivePart.cs`.

| Part | Entry | One line per | Fields |
|---|---|---|---|
| sites | `settings.jsonl` | the site (one line) | id, name, scheme, defaultLanguage, enabled, primaryHostName, createdAtUtc |
| | `host-names.jsonl` | host name | id, hostName, isPrimary |
| | `search.jsonl` | the site (one line) | titleSuffix, defaultImageId, organisationName, organisationLogoId, discouraged, robotsRules, redirectOtherHosts, updatedBy |
| media | `folders.jsonl` | folder | id, parentId, name, createdAtUtc, createdBy |
| | `files.jsonl` | file | id, folderId, fileName, contentType, extension, sizeBytes, sha256, altText, width, height, isAnimated, contentVersion, uploadedAtUtc, uploadedBy, replacedAtUtc, replacedBy, bytes (`included` or `missing`) |
| design | `design.jsonl` | the site (one line; absent when the design was never edited) | draftSchemaVersion, draft, draftUpdatedAtUtc, draftUpdatedBy, liveNumber, nextNumber, liveSinceUtc, liveBy |
| | `design-versions.jsonl` | retained version | number, schemaVersion, document, kind, restoredFromNumber, publishedBy, publishedAtUtc |
| cms | `collections.jsonl` | collection | id, name, slug, description, schemaVersion, schema, revision, schemaRevision, nextFieldNumber, nextOptionNumber, createdAtUtc, createdBy, updatedAtUtc, updatedBy |
| | `schema-changes.jsonl` | schema revision | collectionId, revision, changedAtUtc, changedBy, summary |
| | `items.jsonl` | item | id, collectionId, slug, name, status, draft, revision, draftRevision, draftChangedBy, draftChangedAtUtc, draftChangeKind, published, publishedSlug, publishedAtUtc, publishedBy, firstPublishedAtUtc, liveVersion, nextVersionNumber, copiedFromId, copiedFromSavedAtUtc, copiedBy, copiedAtUtc, createdAtUtc, createdBy, updatedAtUtc, updatedBy |
| | `item-versions.jsonl` | retained version | itemId, number, values, slug, kind, restoredFrom, publishedBy, publishedAtUtc |
| | `item-schedules.jsonl` | pending schedule | id, itemId, action, dueAtUtc, revision, capturedValues, capturedSlug, redirectOldAddress, redirectToPageId, createdBy, createdAtUtc |
| | `item-history.jsonl` | history entry | id, itemId, kind, versionNumber, scheduleAction, dueAtUtc, failureCode, atUtc, by |
| pages | `tree.jsonl` | page, parents first | id, parentId, position, title, slug, isHome, templateCollectionId, copiedFromId, copiedFromSavedAtUtc, copiedBy, copiedAtUtc, createdAtUtc, createdBy, updatedAtUtc, updatedBy |
| | `page-drafts.jsonl` | page | subjectId, schemaVersion, document, updatedAtUtc, updatedBy |
| | `components.jsonl` | component | id, name, copiedFromId, copiedFromSavedAtUtc, copiedBy, copiedAtUtc, createdAtUtc, createdBy, updatedAtUtc, updatedBy, draft (as a page draft) |
| | `menus.jsonl` | menu | id, name, createdAtUtc, createdBy, updatedAtUtc, updatedBy, draft (as a page draft); publications, versions and schedules as below (024 FR-036) |
| | `publications.jsonl` | page, component or menu with a publication | subjectKind, subjectId, liveNumber, nextNumber, liveSinceUtc, liveBy |
| | `versions.jsonl` | retained version of a page, component or menu | id, subjectKind, subjectId, number, schemaVersion, document, title, kind, restoredFromNumber, publishedBy, publishedAtUtc |
| | `schedules.jsonl` | pending schedule of a page, component or menu | id, subjectKind, subjectId, action, dueAtUtc, revision, capturedDocument, capturedSchemaVersion, capturedTitle, redirectToPageId, createdBy, createdAtUtc |
| | `redirects.jsonl` | redirect | id, matchKind, sourcePath, sourceQuery, statusCode, targetKind, targetPageId, targetCollectionId, targetItemId, targetPath, targetUrl, enabled, note, origin, createdAtUtc, createdBy, updatedAtUtc, updatedBy |

Not carried because they are this installation's state, not the site's content: a draft's or
schedule's captured row version (`xmin`), a publication's render generation, item search text and
index rows, media variants and their states.

Field names follow the owning module's response DTOs. Each part's tests pin its entries to
golden lines. Rows the module rebuilds (research ST-03) are never written.

## 4. Remapping (ST-09)

| Part | Rewrites |
|---|---|
| pages | page documents and versions: page, item, collection, component, menu and media ids, form `redirect.pageId` and `consent.linkPageId`, binding collection ids; tree `parentId`; templates; menu documents (`pageId`, `itemId`, `collectionId`); redirect targets; copy origins |
| cms | item values (reference, multi-reference, image, file, rich-text links and images); collection templates; copy origins |
| design | media references in tokens |
| media | `folderId`, `parentId` |
| every part | author ids, through `IAuthorMap` |

Golden files: `docs/contracts/<contract>/remap/<case>/{input.json,map.json,expected.json}`.
