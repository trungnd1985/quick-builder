---

description: "Task list for 013-media-library-variants"
---

# Tasks: Media Library, Image Variants and Remote Storage Providers

**Input**: Design documents from `/specs/013-media-library-variants/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - a separate integration suite for each optional infrastructure provider;
  - golden files for rendering;
  - 80 % coverage on critical paths.
- The spec measures behaviour through automated checks: SC-001, SC-003 … SC-012, SC-014
  and SC-015.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US6 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US6)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `MEDIAC` = `src/Modules/Media/QuickBuilder.Media.Contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `REND` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `SHARED` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `MEDIAUI` = `src/admin/src/features/media`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`
- `FIX` = `tests/fixtures/media`

**Conventions**:
- **Backend slices** follow the one-folder-per-slice rule. Every controller action:
  - declares exactly one `[RequireCapability(...)]` (research MV-24);
  - checks the site with `ISiteDirectory.FindByIdAsync` first, returning `sites.not_found`;
  - refuses through `MEDIA/Infrastructure/MediaErrorResults.cs` with the codes in
    [data-model.md §5](./data-model.md).
- **Admin reads** of a site named in the route use
  `.AcrossAllSites("<reason> (001 FR-011b)")` plus an explicit `SiteId ==` filter, exactly
  as `ListFilesHandler` does. Public reads go through the site-filtered context.
- **NetVips** is referenced only from `MEDIA/Imaging/` (MV-09). Nothing else touches the
  native library.
- **Storage** is reached only through `IFileStorage`. No handler knows which provider is
  configured.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task that uses them. `npm run lint` enforces this.
- **Logs** carry ids and counts only. They never contain file names, folder names,
  alternative text or secrets (FR-041).

**References**:
- research decisions: `MV-xx`;
- tables and rules: [data-model.md](./data-model.md);
- endpoints: [contracts/media-api.yaml](./contracts/media-api.yaml);
- addresses and markup: [contracts/public-media.md](./contracts/public-media.md);
- providers: [contracts/storage-providers.md](./contracts/storage-providers.md);
- readers and events: [contracts/readers.md](./contracts/readers.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, packages, the image-library spike, and build and
test wiring. Nothing here has behaviour.

- [X] T001 Commit `specs/013-media-library-variants/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `013-media-library-variants`, referencing #13 (plan.md "Development Workflow").
- [X] T002 Add package versions to `Directory.Packages.props` (research MV-22):
  - `NetVips` 3.x;
  - `NetVips.Native.linux-musl-x64`, `NetVips.Native.linux-musl-arm64`, `NetVips.Native.linux-x64` and `NetVips.Native.win-x64`, all at the same libvips version;
  - `AWSSDK.S3` 4.x, `Azure.Storage.Blobs` 12.x, `Azure.Identity` 1.x;
  - `Microsoft.Extensions.Configuration.Binder` 10.0.x;
  - `Testcontainers.Minio` and `Testcontainers.Azurite` at the `Testcontainers.PostgreSql` version.

  Add a comment block per group: why it exists, its licence, and the alternative rejected.
- [X] T003 Wire the packages:
  - **`MEDIA/QuickBuilder.Media.csproj`**: `NetVips`, the four `NetVips.Native.*` packages, and a `ProjectReference` to `PAGESC/QuickBuilder.Pages.Contracts.csproj` (readers.md §5). Update the csproj comment.
  - **`SHARED/QuickBuilder.Shared.Infrastructure.csproj`**: `AWSSDK.S3`, `Azure.Storage.Blobs`, `Azure.Identity`, `Microsoft.Extensions.Configuration.Binder`.
  - **`ITESTS/QuickBuilder.IntegrationTests.csproj`**: `Testcontainers.Minio` and `Testcontainers.Azurite`.
- [X] T004 **Spike** (plan Risks, MV-09); do this before any US2 work:
  1. Build the image with `docker build` for `linux/amd64` and `linux/arm64` (`docker buildx`).
  2. In each, call a throwaway `HOST` start-up check that loads NetVips, prints `NetVips.Version`, and reports whether `heifload` and `heifsave` with AV1 are available (`NetVips.TypeFind("VipsForeignLoad", "heifload")`, `Operation` lookup).
  3. On the reference compose setup (2 vCPU, 4 GB), time a 20 MiB AVIF, a 20 MiB JPEG and a 6,000-pixel PNG resized to 1,920 px with MV-11's settings.
  4. Adjust `Dockerfile` if the natives are not copied by `dotnet publish` (they live under `runtimes/<rid>/native`).
  5. Record the results under a new "Spike result" heading at the end of research.md MV-09, and remove the throwaway check.
- [X] T005 [P] Create `THIRD-PARTY-NOTICES.md` at the repository root, listing libvips (LGPL-3.0-or-later, with its source location), every library bundled in `NetVips.Native` (from its package notice file), AWSSDK.S3 (Apache-2.0), and Azure.Storage.Blobs and Azure.Identity (MIT). In `Dockerfile`'s runtime stage, copy the file to `/app/THIRD-PARTY-NOTICES.md`.
- [X] T006 [P] Create `docker-compose.storage.yml`, a development and test overlay with:
  - `minio` (with a one-shot `mc mb` job creating bucket `qb-media`);
  - `azurite` (with a one-shot `az storage container create` job creating container `qb-media`);
  - `app2`: a second instance of `app` sharing the database, on port 8081.

  Add a header comment that it is never for production (quickstart §6).
- [X] T007 [P] Extend `ATESTS/ModuleBoundaryTests.cs`:
  - `QuickBuilder.Media` may reference `QuickBuilder.Pages.Contracts`;
  - only `QuickBuilder.Media` references the `NetVips` assembly, and only types in namespace `QuickBuilder.Media.Imaging` use it;
  - only `QuickBuilder.Shared.Infrastructure` references `AWSSDK.S3`, `Azure.Storage.Blobs` and `Azure.Identity`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the address grammar, search normalisation and the extended `MediaFile`;
- all new tables and both migrations (including the usage backfill);
- options, capabilities and error codes;
- the change event and its Pages handler;
- usage references and `IMediaUsageReader`;
- versioned serving headers;
- the admin API client.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Domain primitives (tests first)

- [X] T008 [P] Create `UTESTS/Media/MediaAddressTests.cs` from contracts/public-media.md §1:
  - it formats the three canonical forms;
  - it parses each one back;
  - it refuses non-canonical forms: `-v1` on an original, a leading zero in a version, upper case, an extra segment, `..`, an empty extension, and a width not in the configured list;
  - version bounds are 1 … 99,999.
- [X] T009 Create `MEDIA/Domain/MediaAddress.cs` (MV-08):
  - `readonly record struct MediaAddress(Guid FileId, int Version, int? Width, string Extension)`;
  - `static MediaAddress? TryParse(string file, IReadOnlySet<int> widths)`, with a `GeneratedRegex` and the canonical-form checks;
  - `string ToPath()`, which returns `/_media/...` without the base path;
  - factories `Original(MediaFile)` and `Variant(MediaFile, int width)`.

  Make T008 pass.
- [X] T010 [P] Create `UTESTS/Media/SearchTextTests.cs` and `MEDIA/Domain/SearchText.cs` (MV-03):
  - `Normalise(string)`: `ToLowerInvariant`, NFD, drop non-spacing marks, collapse whitespace;
  - `For(fileName, altText)`;
  - `LikePattern(term)`: escapes `%`, `_` and `\`, and wraps in `%…%`.

  Cases: `Été` → `ete`; `BROCHURE-été.pdf` contains `ete`; `%`, `_` and `\` are literal; a null alternative text; a 100-character term.
- [X] T011 Extend `MEDIA/Domain/MediaFile.cs` with the columns and operations of data-model §1.1 and §3.1. Add:
  - the properties `FolderId`, `AltText`, `Width`, `Height`, `IsAnimated`, `MetadataState` (an enum), `ContentVersion`, `ReplacedAtUtc`, `ReplacedBy`, `VariantsState` (an enum), `SearchText`, `SortName`, `SearchTextState` (an enum: `provisional`, `normalised`);
  - the methods `MoveTo`, `SetAltText`, `RecordImageMetadata`, `MarkMetadata`, `Replace` and `MarkVariantsUnavailable`, each enforcing its rule and returning a `Result` or throwing for programmer errors. `Create` sets `SearchText` and `SortName` and takes optional metadata.

  Update the class remarks: the file is no longer immutable, and the invariants from data-model §3.1 remain. Add `UTESTS/Media/MediaFileTests.cs` covering every rule in data-model §3.1, including the kind check on `Replace`, the reset of `VariantsState`, and `UploadedBy` never changing.
- [X] T012 [P] Create the new Media entities, with fields and constructors only (behaviour comes in US1, US2 and US3):
  - `MEDIA/Domain/MediaFolder.cs` (data-model §1.2);
  - `MEDIA/Domain/FolderTree.cs` (§1.3, with `Revision`);
  - `MEDIA/Domain/VariantClaim.cs` (§1.4);
  - `MEDIA/Domain/StorageCheck.cs` and `StorageCheckMissing.cs` (§1.5, §1.6).

  All site-owned ones implement `ISiteOwned`.

### Media tables, migration, options, capabilities, errors

- [X] T013 Configure the new columns and tables in `MEDIA/Infrastructure/MediaDbContext.cs` exactly as data-model §1 describes:
  - `xmin` row versions on `media_files` and `folder_trees`;
  - check constraints;
  - the alternate key `(site_id, id)` on `folders`, with `RESTRICT` foreign keys from `media_files.folder_id` and `folders.parent_id`;
  - the unique sibling index with `NULLS NOT DISTINCT` (`.AreNullsDistinct(false)`);
  - the new listing indexes;
  - the partial unique index on `storage_checks`;
  - query filters on the site-owned tables (not `storage_checks`).

  Generate the migration `MEDIA/Infrastructure/Migrations/<ts>_AddLibraryAndVariants.cs` with `dotnet ef migrations add AddLibraryAndVariants`. Then edit `Up` to:
  - set `metadata_state` = 0 for `image/%` rows and 2 for PDFs;
  - fill `search_text` and `sort_name` for existing rows with `lower(file_name)`, and set `search_text_state` = 0 (`provisional`) on those rows. New rows get state 1 (`normalised`) from `MediaFile.Create`.

  A migration cannot read rows into C#, so the accent folding happens after start-up (MV-26). Create `MEDIA/Infrastructure/SearchTextBackfillService.cs`, a `BackgroundService` running after `ApplicationStarted`:
  - list the distinct sites with provisional rows, then work **one site at a time** (Principle V);
  - in batches of 500, recompute `search_text` and `sort_name` with `SearchText.For` and set the state to `normalised`;
  - the work is idempotent and safe on several instances, because it recomputes the same value;
  - log counts per site only.

  Register it in `MEDIA/MediaModule.cs`. Create `ITESTS/Media/SearchTextBackfillTests.cs`: rows inserted as provisional (`Été.pdf`) are found by `ete` after one pass, and a second concurrent pass changes nothing.
- [X] T014 [P] Extend `MEDIA/Infrastructure/MediaStorageKeys.cs` (MV-14):
  - `For(siteId, fileId, version)`, which returns the version-1 key unchanged;
  - `Variant(siteId, fileId, version, width)` under `media-variants`;
  - `SiteOf` accepts the `-v{n}` suffix;
  - a new `VariantKeys.TryParse(StorageKey)` returns `(siteId, fileId, version, width)`.

  Extend `UTESTS/Storage/StorageKeyTests.cs` or add `UTESTS/Media/MediaStorageKeysTests.cs`: every key satisfies the `StorageKey` grammar at the maximum version and width, and round-trips.
- [X] T015 [P] Extend `MEDIA/MediaOptions.cs` with `MaxImagePixels` (default 100,000,000; range 1,000,000 – 1,000,000,000) and a nested `Variants` with:
  - `Widths` (default `[320, 640, 960, 1280, 1920, 2560]`; 1–10 ascending values, 64–4,096; empty means the default, filled by `MediaOptionsDefaults` like `AllowedTypes`);
  - `MaxConcurrent` (default `Math.Max(1, Environment.ProcessorCount / 2)`, 1–64);
  - `Timeout` (default 10 s, 1–60 s).

  Validate them in `MediaOptionsValidator`, naming the key. Extend `UTESTS/Media/MediaOptionsValidationTests.cs`. Add the keys with their defaults to `HOST/appsettings.json` and `docs/configuration.md`.
- [X] T016 [P] Add `StorageCheck = "media.storage.check"` (installation scope, granted to no site role) to `MEDIA/MediaCapabilities.cs`. Add it to `specs/002-identity-setup/contracts/authorization.md` §2.
- [X] T017 [P] Add every error code in data-model §5 to `MEDIA/Infrastructure/MediaErrors` (wherever `MediaErrors` is declared today) and map each to its HTTP status in `MEDIA/Infrastructure/MediaErrorResults.cs`. `media.in_use` carries an extension member `usage` (a `UsageSummary`), written as a problem-details extension.

### Change event and usage (Pages)

- [X] T018 [P] Create `MEDIAC/Events/FileChanged.cs` (readers.md §1.2) and register it in `MEDIA/MediaModule.cs` with `EventTypeRegistration<FileChanged>`. Add `Media.FileChanged` to `specs/001-foundation-skeleton/contracts/integration-events.md`.
- [X] T019 [P] Create `PAGESC/PageContentCapabilities.cs` with `View = "pages.content.view"`, and change `PAGES/PagesCapabilities.cs` so that `ContentView` refers to it (readers.md §2.2; the name is unchanged).
- [X] T020 Create `PAGES/Domain/DraftMediaReference.cs` and `VersionMediaReference.cs` (data-model §2.1, §2.2), both `ISiteOwned`, and configure them in `PAGES/Infrastructure/PagesDbContext.cs`:
  - keys, indexes and the subject-kind check;
  - the cascade foreign key from `version_media_references` to `published_versions`;
  - query filters.

  Generate `PAGES/Infrastructure/Migrations/<ts>_AddMediaReferences.cs`, and append to `Up` the backfill SQL of MV-05:
  - `INSERT … SELECT DISTINCT` from `page_drafts` and `component_drafts` into `draft_media_references`, and from `published_versions` into `version_media_references`;
  - each using `jsonb_path_query(document, 'lax $.**.media.mediaId')`, keeping only values matching the UUID pattern;
  - with `ON CONFLICT DO NOTHING`.

  Comment it with MV-05.
- [X] T021 [P] Create `ITESTS/Pages/MediaReferenceBackfillTests.cs`:
  - seed drafts and published versions from the rendering golden fixtures (`tests/fixtures/rendering/golden`), each with image nodes, rich text and component references;
  - roll the Pages migration back to the one before `AddMediaReferences` and forward again;
  - the reference rows equal what `DependencyExtractor.Extract(...)` returns for the media kind, for every document.
- [X] T022 Maintain references on writes (MV-05):
  - **Drafts**: in `PAGES/Infrastructure/DraftWrites.cs`, replace the draft's `draft_media_references` set with the media ids from `DependencyExtractor.Extract(document)`, beside the existing `component_usages` upkeep, for page and component drafts.
  - **Versions**: in `PAGES/Features/Publish/Publish.cs`, `PAGES/Features/Rollback/` and `PAGES/Features/RunDueSchedule/`, add `version_media_references` for each new `PublishedVersion`, from the dependencies already extracted there.
  - **Deletes**: in `PAGES/Features/DeletePage/DeletePage.cs` and `PAGES/Features/DeleteComponent/DeleteComponent.cs`, remove the subject's draft references in the same transaction.
- [X] T023 Create `PAGESC/Media/IMediaUsageReader.cs` and `PAGESC/Media/MediaUsage.cs` (readers.md §2.1).
  - Implement `PAGES/Infrastructure/MediaUsageReader.cs` with no tracking and an explicit site filter:
    - `SummariseAsync`: one grouped query over both reference tables, joined to `publications` for liveness;
    - `ListAsync`: page titles and component names, plus `livePagesThroughComponent` from `live_dependencies`.
  - Register it as scoped in `PAGES/PagesModule.cs`.
  - Create `ITESTS/Pages/MediaUsageReaderTests.cs`: draft-only, live, history-only, live through a component on 3 pages, a page deleted, a draft changed (the counts update in the same request, SC-008), and another site's references are never counted.
- [X] T024 Handle `FileChanged` in `PAGES/Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs`:
  - implement `IIntegrationEventHandler<FileChanged>` in the same class, sharing the bump and deduplication code with `FileDeleted`, with separate `HandlerName` values per event type;
  - register it in `PAGES/PagesModule.cs`.

  Rename `ITESTS/Rendering/MediaDeletionRefreshTests.cs` to `MediaChangeRefreshTests.cs`, and add: a `FileChanged` for each reason bumps exactly the live pages showing the file directly or through a component, once per event id.

### Serving, mapping and client foundations

- [X] T025 Switch serving and addresses to `MediaAddress` (MV-08). In each case variants stay `not_found` until US2, and versions ≥ 2 only occur after US6.
  - **Serving**: `MEDIA/Features/ServePublicFile/ServePublicFile.cs` parses with `MediaAddress.TryParse` (widths from `IOptions<MediaOptions>`), matches the id, version and extension against the row, and answers `not_found` for any variant for now.
  - **Headers**: in `MEDIA/Infrastructure/MediaResponseHeaders.cs`, the `ETag` is `"{id}"`, `"{id}-v{n}"` or `"{id}-v{n}-w{w}"`, and `Last-Modified` is `ReplacedAtUtc ?? UploadedAtUtc`. Add `MediaResponseKind.VariantFallback` (the same headers with `Cache-Control: no-store`).
  - **Addresses**: `MEDIA/Infrastructure/MediaFileMapper.cs` `PublicPath` and `MEDIA/Infrastructure/PublicMediaReader.cs` build paths with `MediaAddress`. `PublicMediaReader` selects `content_version`.
  - **Contract**: `MEDIAC/IPublicMediaReader.cs` gains `Width`, `Height` and `Variants` (empty for now) and `PublicMediaVariant` (readers.md §1.1). Fix the compile in `REND/Rendering/Blocks/MediaRenderers.cs`, with no behaviour change yet.

  Extend `ITESTS/Media/PublicServingTests.cs` and `MediaServingHeadersTests.cs`: 003 addresses still serve byte-identical content; non-canonical forms are `not_found`; the versioned `ETag`.
- [X] T026 Extend `MEDIAC/MediaFileDto.cs` with the fields of contracts/media-api.yaml `MediaFile`:
  - `FolderId`, `AltText`, `Width`, `Height`, `ContentVersion`, `MetadataState`, `ReplacedAtUtc`, `ReplacedBy`, `Variants`, `ThumbnailPath`, `Usage` (a `MediaUsageSummaryDto`) and `CanReplace`;
  - `CanDelete` gains the live-use rule.

  `MEDIA/Infrastructure/MediaFileMapper.cs` fills them:
  - one `IMediaUsageReader.SummariseAsync` call per page of files;
  - `PublishingCapabilities.Manage` evaluated once per request;
  - `CanDelete` and `CanReplace` as in MV-24 (another person's file needs `media.files.delete`; live use needs publishing);
  - `Variants` and `ThumbnailPath` from admin addresses: until US2, `ThumbnailPath` is 003's preview address and `Variants` is empty.
- [X] T027 [P] Extend `ADMIN/api/media.ts` with the types and functions for every operation in contracts/media-api.yaml:
  - `list(siteId, query)`, `get`, `updateAltText`, `move`, `replace(siteId, fileId, file, expectedContentVersion, onProgress)`, `usage`, `remove(siteId, fileId, { confirmInUse })`;
  - `folders.list/create/update/remove`;
  - `storageCheck.get/start`.

  Keep `publicUrl` and add `variantUrl(file, width)` for admin addresses. Map the new problem codes to typed errors in the existing `ApiError`.

**Checkpoint**: both migrations apply. The references and backfill are proven. `FileChanged` refreshes pages. Every 003 address still serves. `dotnet test` is green.

---

## Phase 3: User Story 1 - Editors organise and find media in folders and by search (Priority: P1) 🎯 MVP

**Goal**: folders (create, rename, move, delete when empty), moving files, search across
the library or a subtree, filters and sorts, and alternative text. The library screen is
rebuilt on `MediaBrowser`.

**Independent Test**: quickstart §1. Nested folders, moves that keep addresses, name
clashes refused, search by name and by alternative text (ignoring case and accents), and
pages within 1 s at 10,000 files (SC-001).

### Tests for User Story 1 ⚠️

- [X] T028 [P] [US1] Create `UTESTS/Media/MediaFolderTreeTests.cs` for `FolderTree`: create at depth 8 succeeds and depth 9 fails; moving into self or a descendant fails; a move making the deepest descendant exceed 8 fails; sibling names clash ignoring case and accents; 1,000 folders is the limit; `Descendants(folderId)` includes the folder itself.
- [X] T029 [P] [US1] Create `UTESTS/Media/ListingCursorTests.cs`: cursors for each sort round-trip; a cursor from another sort or query is refused; malformed input gives null.
- [X] T030 [P] [US1] Create `ITESTS/Media/MediaFolderApiTests.cs` covering every folder operation in contracts/media-api.yaml:
  - create nested folders, rename, move, and delete an empty folder;
  - `media.folder_not_empty` when a file or child exists, including a file moved in concurrently (a `RESTRICT` violation mapped);
  - `media.folder_conflict` for two concurrent moves that would form a cycle: exactly one succeeds;
  - an Editor can rename a folder a Designer created (clarification);
  - a user with no role gets not-found, and a viewer-only role gets forbidden.
- [X] T031 [P] [US1] Create `ITESTS/Media/MediaListingApiTests.cs`:
  - folder browsing and the top level;
  - `q` across the whole library, and `scope=subtree`;
  - `kind`; each `sort` and `direction`;
  - every file appears exactly once while others are uploaded during paging;
  - `totalCount` equals the query's matches;
  - `media.invalid_query` cases;
  - a search matches alternative text;
  - `%` is literal;
  - the `usage` summary is present;
  - moving files keeps `publicPath` identical (FR-003).
- [X] T032 [P] [US1] Create `ITESTS/Media/AltTextApiTests.cs`: set, clear and change; 300 characters is accepted and 301 refused; control characters are stripped; a PDF gives `media.alt_text_not_allowed`; the text appears in search; `media.conflict` on a stale row version; the text is never in logs (assert via the test log sink).

  Also create `ITESTS/Media/GetFileApiTests.cs` for `GET …/files/{fileId}` (Principle X: every endpoint has integration tests):
  - it returns every field of contracts/media-api.yaml `MediaFile`, including `usage`;
  - `canDelete` and `canReplace` for the uploader, another Editor and a Designer, with and without live use;
  - another site's file id gives `media.not_found`;
  - a user with no role gets not-found, and a role without `media.files.view` gets forbidden.
- [X] T033 [P] [US1] Create `ITESTS/Media/MoveFilesTests.cs`: moving 100 files succeeds and 101 gives `media.too_many_files`; an unknown folder gives `media.folder_not_found`; another site's file ids are ignored; `null` moves to the top level.
- [X] T034 [P] [US1] Create `ADMINTESTS/media/FolderTree.spec.ts` and `ADMINTESTS/media/MediaBrowser.spec.ts` against a fake API:
  - keyboard navigation of the tree (the `aria` treeview pattern: arrows, Home/End, Enter);
  - create, rename and delete folders;
  - the search box debounced at 250 ms;
  - kind, sort and direction;
  - paging with "Load more";
  - the empty-search state offering "search all folders";
  - axe clean.

### Implementation for User Story 1

- [X] T035 [US1] Implement `FolderTree` behaviour in `MEDIA/Domain/FolderTree.cs` and `MediaFolder.cs` (data-model §3.2): `Create`, `Rename`, `Move` and `Descendants`, over the site's loaded folders; name cleaning reuses `MediaFileName`'s cleaning rules, with the 100-character limit. Make T028 pass.
- [X] T036 [US1] Create the folder slices, each one file with its controller, command or query, and handler. Each structural change loads the site's `FolderTree` row (created if missing), applies the rule, bumps `Revision`, and saves. A `DbUpdateConcurrencyException` maps to `media.folder_conflict`, and a Postgres `23503` on delete maps to `media.folder_not_empty`.
  - `MEDIA/Features/Folders/ListFolders/ListFolders.cs`: the tree with direct file counts via one grouped query, and `revision`;
  - `MEDIA/Features/Folders/CreateFolder/CreateFolder.cs`;
  - `MEDIA/Features/Folders/UpdateFolder/UpdateFolder.cs` (rename and/or move);
  - `MEDIA/Features/Folders/DeleteFolder/DeleteFolder.cs`.

  Register the handlers in `MEDIA/MediaModule.cs`. Make T030 pass.
- [X] T037 [US1] Create `MEDIA/Features/MoveFiles/MoveFiles.cs`: `POST …/files/move`; validate the folder; one `ExecuteUpdate` of `folder_id` for ids of the route's site (explicit site filter). Make T033 pass.
- [X] T038 [US1] Rework `MEDIA/Features/ListFiles/ListFiles.cs` (MV-03):
  - parse and validate `folderId`, `scope`, `q`, `kind`, `sort`, `direction` and `limit`;
  - resolve subtree ids from the loaded `FolderTree`;
  - apply `SearchText.LikePattern` with `EF.Functions.Like(f.SearchText, pattern, "\\")`;
  - keyset per sort through a new `ListingCursor` (sort id, key and id, base64url) replacing the nested `Cursor`;
  - count the query's matches;
  - map through `MediaFileMapper`.

  Make T029 and T031 pass.
- [X] T039 [P] [US1] Create `MEDIA/Features/GetFile/GetFile.cs` (`GET …/files/{fileId}`, `media.files.view`) and `MEDIA/Features/UpdateFile/UpdateFile.cs` (`PATCH`, `media.files.upload`: `SetAltText` with the row version, re-deriving `SearchText`). Make T032 pass.
- [X] T040 [US1] Extend `MEDIA/Features/UploadFile/UploadFile.cs` with an optional `folderId` query parameter (uploads are a raw body named by `X-QuickBuilder-File-Name`, 003 M-10): validate it belongs to the site, or give `media.folder_not_found` before any bytes are stored, and pass it to `MediaFile.Create`. Extend `ITESTS/Media/UploadFileTests.cs` for it.
- [X] T041 [P] [US1] Create `MEDIAUI/FolderTree.vue`: an accessible tree with inline create, rename, move (a "Move to…" dialog listing the tree) and delete with confirmation. It shows `media.folder_not_empty` and `folder_conflict` messages; on conflict it reloads the tree. Add the i18n keys.
- [X] T042 [US1] Create `MEDIAUI/MediaBrowser.vue` with props `siteId`, `mode: 'library' | 'picker'` and `initialFolderId`, and emits `choose(file)` and `folderChange(id)`:
  - search, the kind filter, sort and direction;
  - the grid of files, using 003's preview until US2 (thumbnails come from `thumbnailPath`);
  - "Load more" paging;
  - multi-select with "Move to…" (library mode);
  - `UploadArea` into the current folder;
  - the empty, loading and error states.
  - In picker mode, only images are shown and a click chooses.

  Add the i18n keys. Make T034 pass.
- [X] T043 [US1] Create `MEDIAUI/MediaDetailsPanel.vue` and `MEDIAUI/AltTextEditor.vue`:
  - the panel shows the preview, details (type, size, dimensions, uploaded by and at, folder), copy address and the alternative-text editor (images only, with a 300-character counter; saving shows `media.conflict` as a reload prompt);
  - it has slots for usage (US5) and replace (US6).

  Rebuild `MEDIAUI/MediaLibraryView.vue` as `FolderTree` + `MediaBrowser` + `MediaDetailsPanel`, keeping 003's upload notice and the no-host-name explanation. Retire the parts of `MediaList.vue` that `MediaBrowser` replaces. Update `ADMINTESTS/mediaLibraryView.spec.ts`.
- [X] T044 [US1] Extend `STESTS/MediaPerformanceTests.cs` (SC-001): with 10,000 seeded rows (with names and alternative text), the last page of the top level sorted by name, a whole-library search and a subtree search each load in ≤ 1 s at p95 over 20 runs. Record the numbers in the output.

**Checkpoint**: US1 is demoable. Quickstart §1 passes.

---

## Phase 4: User Story 2 - Published pages deliver correctly sized images (Priority: P1)

**Goal**: dimensions and the pixel limit at upload; variants produced on demand exactly
once; eager thumbnails; the backfill of existing images; variant clean-up; responsive
image markup on published pages and the canvas.

**Independent Test**: quickstart §2. A large photo on a published page gets `srcset`,
`sizes`, `width` and `height`; a mobile visitor downloads under 20 % of the original's
bytes; variants are upright and carry no metadata; unconfigured widths are not found;
production happens exactly once across two instances (SC-003 … SC-006).

### Tests for User Story 2 ⚠️

- [X] T045 [P] [US2] Add the image fixtures to `FIX/`: `photo-6000.jpg` (6,000 × 4,000, EXIF orientation 6, GPS and camera tags), `logo.png` (alpha), `logo-v2.png`, `anim.gif`, `anim.webp`, `huge-pixels.png` (12,000 × 12,000, highly compressible), `photo.avif`, `photo-20mib.jpg` and `brochure-été.pdf`, with `FIX/README.md` stating the origin and licence of each (generated by the project, CC0). Copy the relevant ones into the `ITESTS/QuickBuilder.IntegrationTests.csproj` output.
- [X] T046 [P] [US2] Create `UTESTS/Media/ImageMetadataTests.cs` for `ImageMetadataReader`:
  - displayed dimensions swap for orientations 5–8;
  - animated GIF and WebP are detected;
  - `huge-pixels.png` is over the limit and is read from the header only (it completes quickly with no pixel decode, asserted with a small memory budget);
  - a truncated JPEG is unreadable;
  - a PDF is not applicable.
- [X] T047 [P] [US2] Create `UTESTS/Media/VariantPlanTests.cs` for `VariantPlan`:
  - eligibility per data-model §3.1, including AVIF with the codec absent;
  - widths strictly below the displayed width;
  - the `srcset` rule for including the original (public-media.md §2);
  - `src` selection;
  - `sizes` for `full` and `natural` with section widths 720, 1,200 and 1,440.
- [X] T048 [P] [US2] Create `UTESTS/Media/VariantEncodingGoldenTests.cs`: for each eligible fixture and width, the output is upright (dimensions match orientation), has no EXIF, XMP or ICC data (read back with NetVips `GetFields()`), keeps alpha for `logo.png`, has width exactly equal to the requested width, and stays below a recorded byte ceiling per fixture and width (catching encoder-setting drift).
- [X] T049 [P] [US2] Create `ITESTS/Media/VariantServingTests.cs`:
  - a public variant is served on the owning site's host name with 003's headers and the variant `ETag`, and is not found on another site's host;
  - unconfigured widths, widths ≥ the file's width, a stale version, GIF, animated, PDF and variants-unavailable are all `not_found`;
  - the second request is served from storage (no production, asserted by the claims-table and production counters);
  - the admin variant endpoint works for a site with no host name and requires `media.files.view`;
  - a forced encoder failure serves the original with `Cache-Control: no-store`, marks `variants_state`, emits `FileChanged(variants)`, and makes later requests `not_found`;
  - bounded production (FR-029), with `Media:Variants:MaxConcurrent = 1`, `Timeout = 2 s` and a slow encoder decorator:
    - a second production for another file waits for the first;
    - a production that exceeds the timeout, including time spent waiting, falls back to the original;
    - meanwhile `GET …/files` for the site completes within 1 s.
- [X] T050 [P] [US2] Create `ITESTS/Media/VariantProductionExactlyOnceTests.cs` (SC-005): two `WebApplicationFactory` hosts on one database and one storage directory; 100 concurrent first requests for one variant, split across both, produce it exactly once (a counting `IVariantEncoder` decorator) and all get identical bytes; a stale claim (its `claimed_at_utc` moved into the past) is taken over.
- [X] T051 [P] [US2] Create `ITESTS/Media/ThumbnailLatencyTests.cs`: after uploading `photo-20mib.jpg`, the thumbnail key exists in storage within 5 s (FR-028), without delaying the upload response (it returns before the thumbnail exists, asserted with a slow encoder decorator). Create `ITESTS/Media/ImageUploadTests.cs` (revised from extending `UploadValidationTests.cs`, whose single table-driven test predates images with dimensions): `huge-pixels.png` gives `media.dimensions_too_large` and leaves no stored bytes; accepted images return `width`, `height` and `metadataState: recorded`.
- [X] T052 [P] [US2] Create `ITESTS/Media/ImageMetadataBackfillTests.cs`: rows inserted as they were before this feature (`metadata_state = pending`) get dimensions, their thumbnails get queued, `FileChanged(dimensions)` is emitted for each, over-limit and unreadable rows are marked without events, and two concurrent runs give the same final state. With pending rows on two sites, every batch the service processes belongs to a single site (asserted through a recording decorator), and log lines are per site (Principle V). (Revised during implementation: the batch query names the site in its filter, so the test asserts the per-site log lines rather than adding a recording decorator.)
- [X] T053 [P] [US2] Extend `ITESTS/Media/MediaOrphanSweepTests.cs` with the variant pass (MV-14): variant keys older than 1 hour are deleted when their file is deleted, their version is stale, their width is unconfigured, or the file's variants are unavailable; younger keys and current variants are kept; original keys under `media/` are never touched by this pass.
- [X] T054 [P] [US2] Extend `UTESTS/Rendering/Blocks/MediaRendererTests.cs` (SC-003, SC-006):
  - `width` and `height` are always present when known;
  - `srcset` lists variants and never offers a 6,000-pixel original;
  - `sizes` follows the Section's content width;
  - `src` is the largest variant ≤ 1,280;
  - there is no `srcset` for GIFs or files without variants;
  - the attribute order matches public-media.md §2.

  Regenerate the golden files under `tests/fixtures/rendering/golden/` whose documents contain images, and update `tests/fixtures/rendering/context.json` with `PublicMediaFile` dimensions and variants.
- [X] T055 [P] [US2] Keep `ADMINTESTS/editor/CanvasMarkupParity.spec.ts` passing with the new image markup: it already compares structure and classes and ignores attributes. (Revised during implementation: `width`, `height` and `srcset` come from the media API on the canvas, which the parity test stubs, so they cannot be compared there; the renderer tests and golden files pin them on the published side, and page-schema.md §9 lists the address and `sizes` difference.)

### Implementation for User Story 2

- [X] T056 [US2] Create `MEDIA/Imaging/NetVipsSetup.cs`, called once from `MediaModule`'s registration through a hosted start-up step (MV-09):
  - `NetVips.BlockUntrusted = true`;
  - block every `VipsForeignLoad` except the JPEG, PNG, GIF, WebP and HEIF loaders;
  - `Cache.Max = 0`, `Cache.MaxMem = 0`, `Cache.MaxFiles = 0`;
  - `NetVips.Concurrency = 1`;
  - detect AVIF support (`heifload` and `heifsave` with AV1), exposing it as `ImagingCapabilities.AvifVariants`, and log one warning when it is absent.
- [X] T057 [US2] Create `MEDIA/Imaging/ImageMetadataReader.cs`: `Task<ImageMetadata> ReadAsync(Stream seekable, long maxPixels, CancellationToken)`, using `Image.NewFromStream(..., access: Enum.Access.Sequential)` to read the header only. It returns the displayed width and height (the orientation field from `GetTypeOf("orientation")`), `IsAnimated` (`n-pages` > 1) and a state (`recorded`, `over_limit`, `unreadable`). Make T046 pass.
- [X] T058 [US2] Create `MEDIA/Imaging/VariantEncoder.cs` behind `internal interface IVariantEncoder`: `Task<byte[]> EncodeAsync(Stream original, MediaFileType type, int width, CancellationToken)`, using `Image.ThumbnailStream` with auto-rotate, sRGB import and export, then the per-format save of MV-11 (JPEG Q80 progressive and optimised coding; PNG compression 6; WebP Q80 effort 4; AVIF Q55 effort 2) and `keep: none`. Make T048 pass.
- [X] T059 [US2] Record metadata at upload (MV-10). Refactor `MEDIA/Features/UploadFile/UploadFile.cs` so that, after the bytes are stored and before the row is committed, images are opened through `IFileStorage.OpenReadAsync` and read with `ImageMetadataReader`:
  - `over_limit` deletes the stored key and refuses with `media.dimensions_too_large`;
  - `unreadable` keeps the file with `metadata_state = unreadable` (no variants; research MV-10, revised);
  - otherwise it passes the metadata to `MediaFile.Create`.

  Extract the validate-store-read-metadata sequence into `MEDIA/Features/UploadFile/UploadPipeline.cs`, so that US6 reuses it. Make T051's validation part pass.
- [X] T060 [US2] Create `MEDIA/Domain/VariantPlan.cs`: eligibility, widths for a file, `srcset` entries, and the `src` choice. It is pure, used by the reader, the renderer inputs and the admin mapper. Make T047 pass.
- [X] T061 [US2] Create `MEDIA/Variants/VariantClaims.cs` (MV-12):
  - `TryClaimAsync`: insert a `VariantClaim`; on a unique violation, take over with `ExecuteUpdate` where `claimed_at_utc < now − 2 × Timeout`;
  - `ReleaseAsync`: `ExecuteDelete`.

  Create `MEDIA/Variants/VariantService.cs`:
  - `GetOrProduceAsync(MediaFile file, int width, CancellationToken)`: storage hit first, then an in-process single-flight (`ConcurrentDictionary<(Guid, int, int), Lazy<Task<VariantResult>>>`), then the claim;
  - the winner produces under a `SemaphoreSlim(MaxConcurrent)` and a `CancellationTokenSource(Timeout)` (waiting counts), writes with `WriteAsync` (treating `StorageKeyExistsException` as success) and releases;
  - losers poll `ExistsAsync` every 200 ms until the lease;
  - on failure or timeout it calls `MarkVariantsUnavailable` with `FileChanged(variants)` in one transaction and returns `VariantResult.Fallback`.

  Register both in `MEDIA/MediaModule.cs` with a random per-process `instance_id`. Make T050 pass.
- [X] T062 [US2] Serve variants:
  - in `MEDIA/Features/ServePublicFile/ServePublicFile.cs`, route variant addresses through `VariantService`: stored bytes are served with `MediaResponseKind.Public`; `Fallback` sends the original's stream with `MediaResponseKind.VariantFallback`;
  - create `MEDIA/Features/GetFileVariant/GetFileVariant.cs` (`GET …/files/{fileId}/variants/{width}?v=`, `media.files.view`), with `Cache-Control: private, max-age=31536000, immutable`, falling back like the public route.

  Make T049 pass.
- [X] T063 [US2] Create `MEDIA/Variants/ThumbnailQueue.cs` (a bounded `Channel<(Guid Site, Guid File, int Version)>`, capacity 1,000, `DropWrite`) and `MEDIA/Variants/ThumbnailWorker.cs` (a `BackgroundService` calling `VariantService.GetOrProduceAsync` at the smallest configured width, loading the row across sites with a reason string). Enqueue from `UploadPipeline` after commit. Make T051 pass.
- [X] T064 [US2] Create `MEDIA/Infrastructure/ImageMetadataBackfillService.cs` (MV-10): after `ApplicationStarted`, list the distinct sites with `pending` rows (the only cross-site read: `AcrossAllSites` with a reason, covered by T103), then work **one site at a time** (Principle V), in batches of 50 `pending` rows filtered explicitly to that site. Log counts per site only. For each row, read the metadata, apply `RecordImageMetadata` or `MarkMetadata`, add `FileChanged(dimensions)` for recorded ones in the same save, and enqueue the thumbnails. Stop when none remain; check again hourly. Make T052 pass.
- [X] T065 [US2] Add the variant pass to `MEDIA/Infrastructure/MediaOrphanSweepService.cs` (MV-14): list `{site}/media-variants/` per site found under the root, parse with `VariantKeys.TryParse`, batch-load the rows, and delete stale keys older than 1 hour. Log counts only. Make T053 pass.
- [X] T066 [US2] Fill the variants into the public reader and the admin mapper:
  - `MEDIA/Infrastructure/PublicMediaReader.cs` selects `width`, `height`, `is_animated`, `metadata_state` and `variants_state`, and fills `PublicMediaFile.Width`, `Height` and `Variants` from `VariantPlan`, with no storage calls;
  - `MEDIA/Infrastructure/MediaFileMapper.cs` fills `Variants` and `ThumbnailPath` with admin variant addresses (`?v={version}`) for eligible files.
- [X] T067 [US2] Responsive markup (MV-15):
  - in `REND/Domain/RenderContext.cs`, track the current Section's content width, set by the Section renderer in `REND/Rendering/Blocks/LayoutRenderers.cs` from the token width choice, with 1,200 as the default outside Sections;
  - in `REND/Rendering/Blocks/MediaRenderers.cs`, `ImageRenderer` writes `src`, `srcset`, `sizes`, `width` and `height` in the order and by the rules of public-media.md §2, through `HtmlWriter.SitePath` and `Attribute`.

  Make T054 pass.
- [X] T068 [US2] Update `CONTRACTS/page-schema.md` §9 with the image markup of public-media.md §2 (the schema file and `schemaVersion` are unchanged, plan Principle VI).
- [X] T069 [US2] Canvas and library thumbnails (MV-16):
  - in `ADMIN/editor/blocks/image/ImageBlock.vue`, write the same `srcset`, `sizes`, `width` and `height` with admin variant addresses from the file's `variants`, taking the Section width from the canvas context;
  - `MEDIAUI/MediaBrowser.vue` and `MediaDetailsPanel.vue` use `thumbnailPath` with a `2x` `srcset` of the next width, and the 960 variant in the panel.

  Make T055 pass, and keep `ADMINTESTS/editor/EditorView.spec.ts` green.
- [X] T070 [US2] Create `STESTS/MediaPerformanceTests.Variants.cs`, a part of the existing media performance test so it shares that deployment (revised during implementation from a separate `VariantPerformanceTests.cs`; Performance category, `docker-compose.perf.yml`, SC-004):
  - with 50 concurrent requests for a stored variant, time to first byte is under 100 ms at p95 over 1,000 requests;
  - the first request for each width of `photo-20mib.jpg` and a 20 MiB AVIF completes within 3 s.

  Record the numbers in the output.

**Checkpoint**: US2 is demoable. Quickstart §2 passes. Published pages ship `srcset`.

---

## Phase 5: User Story 3 - Operators run several instances on shared S3-compatible or Azure Blob storage (Priority: P1)

**Goal**: the S3-compatible and Azure Blob providers behind `IFileStorage`, selected by
configuration, validated at start-up without leaking secrets, reported by readiness, and
proven across two instances; plus the storage consistency check (admin, CLI and start-up)
with its admin screen.

**Independent Test**: quickstart §6. The conformance suite passes unchanged on MinIO and
Azurite; two instances share media; each misconfiguration stops start-up with the key
named and no secret; the check finds exactly the files left out of a copy (SC-010,
SC-011, SC-014).

### Tests for User Story 3 ⚠️

- [X] T071 [P] [US3] Create `ITESTS/Storage/MinioFixture.cs` and `AzuriteFixture.cs`: xUnit collection fixtures starting one container each through Testcontainers, creating a fresh bucket or container per test class, and exposing ready-made `StorageOptions`. Skip them with `Skip.IfNot` (Xunit.SkippableFact) when Docker is unavailable locally, and fail when `QB_REQUIRE_DOCKER=1` (set in CI). (Revised during implementation: each container is started once per run and shared, with a fresh bucket or container per test class through `IClassFixture`; like the PostgreSQL container they need Docker and fail without it, rather than skip. MinIO is pinned to `minio/minio:latest` because MinIO no longer publishes images; see the US3 checkpoint note.)
- [X] T072 [P] [US3] Create `ITESTS/Storage/S3FileStorageConformanceTests.cs` and `ITESTS/Storage/AzureBlobFileStorageConformanceTests.cs`, both deriving from the existing abstract `FileStorageConformanceTests` with no overrides of test cases, each with a unique `KeyPrefix` per test (FR-030, SC-010).
- [X] T073 [P] [US3] Create `ITESTS/Storage/S3FileStorageTests.cs` (contracts/storage-providers.md §7):
  - a 70 MiB write goes multipart and reads back identically;
  - cancelling mid-multipart leaves no object and no pending upload;
  - `KeyPrefix` is applied and stripped;
  - `ForcePathStyle`;
  - `S3AbandonedUploadCleanupService` aborts a pending upload older than 1 hour;
  - range reads through `OpenReadAsync` then `Seek`.

  (Revised during implementation: MinIO lists pending multipart uploads only by exact object name, so those tests name the object, and the clean-up takes the listing prefix as a parameter.)

  Create `ITESTS/Storage/AzureBlobFileStorageTests.cs`: `KeyPrefix`; a missing container on read is `null`, not an exception; a large block-staged upload.
- [X] T074 [P] [US3] Create `UTESTS/Storage/S3RangeReadStreamTests.cs` against a fake ranged source: sequential reads cross buffer boundaries; seeking backwards and forwards issues exactly the needed ranges; `Length`; reading past the end returns 0; disposal.
- [X] T075 [P] [US3] Create `ITESTS/Storage/S3StartupValidationTests.cs` and `AzureStartupValidationTests.cs`: each misconfiguration in storage-providers.md §5 stops the host before it listens, with a message naming the key:
  - a missing `Bucket` or `Region`, or one key of the pair;
  - a wrong secret, a missing bucket or container, a bad URL;
  - a service without conditional writes (an in-process `HttpMessageHandler` proxy in front of MinIO that strips `If-None-Match`);
  - both or neither of `ConnectionString` and `AccountUrl`.

  Extend `ITESTS/Startup/StorageStartupValidationTests.cs`: an unknown provider lists `Local, S3, AzureBlob`.

  Create `UTESTS/Storage/CredentialSelectionTests.cs` (FR-032). This needs no cloud: it inspects the built clients through an internal factory seam.
  - With no S3 keys, `S3FileStorage` builds its client without explicit credentials (the SDK's default chain). With both keys, it uses `BasicAWSCredentials`.
  - With only `AccountUrl`, `AzureBlobFileStorage` uses `DefaultAzureCredential` with the interactive-browser, Visual Studio, Visual Studio Code and Azure Developer CLI sources excluded. With `ConnectionString`, it uses no token credential.
- [X] T076 [P] [US3] Create `ITESTS/Storage/SecretRedactionTests.cs` (SC-011): for every failure in T075 and a readiness failure, neither the exception message nor any captured log event contains the configured `SecretAccessKey`, the connection string, or the `AccountKey` inside it.
- [X] T077 [P] [US3] Create `ITESTS/Media/MediaAcrossInstancesTests.cs` (US3 scenario 3, FR-036): two hosts on one database and one MinIO bucket; upload through A, then list, preview, serve publicly, produce and serve a variant, replace (once US6 exists; mark with `Skip` until then), and delete through B, each visible or refused on the next request. Readiness is unhealthy when MinIO is paused (`docker pause`), and uploads then give `media.storage_unavailable`; both recover on unpause (US3 scenario 5). (Revised during implementation: on a container of its own, since pausing the shared one stalls other classes; a paused service makes requests hang until the SDK timeout, so `media.storage_unavailable` is asserted after stopping the container, and readiness across pause and unpause.)
- [X] T078 [P] [US3] Create `ITESTS/Media/StorageCheckTests.cs` (SC-014):
  - copy a Local library to a remote provider leaving out one site's keys, and switch to it (revised during implementation: Azurite, the check being provider-agnostic);
  - the start-up run (triggered after `ApplicationStarted`, and not delaying readiness) reports exactly those files with an exact count;
  - `POST` while running gives `409 media.storage_check_running`;
  - a crashed run (a lease in the past) is marked failed and a new run can start;
  - two hosts starting together produce one run;
  - the CLI `media check-storage` exits `3` with ids printed, and `0` after the full copy;
  - only `media.storage.check` holders can read or start it;
  - the missing list is capped at 1,000 while the count stays exact.
- [X] T079 [P] [US3] Create `ADMINTESTS/settings/StorageSettingsView.spec.ts`: shows the provider and the latest run, the missing list with library links, the Run check button disabled while running with polling every 5 s, the empty state; axe clean.

### Implementation for User Story 3

- [X] T080 [US3] Extend `SHARED/Storage/StorageOptions.cs`:
  - `SupportedProviders = ["Local", "S3", "AzureBlob"]`;
  - `S3StorageSettings` and `AzureBlobStorageSettings` classes with the keys of contracts/storage-providers.md §3.1 and §4.1;
  - `IsS3` and `IsAzureBlob`.

  In `SHARED/Storage/StorageRegistration.cs`, bind `Storage:S3` and `Storage:AzureBlob` with the configuration binder, and replace the unconditional Local registration with a switch on the provider. Register exactly one provider's `IFileStorage`, `IFileStorageHealthCheck`, validator and clean-up service (MV-20). Update the remarks. Extend `UTESTS/Storage/StorageOptionsValidationTests.cs` for the settings-level rules.
- [X] T081 [US3] Create `SHARED/Storage/RemoteProbe.cs`: `Task<ProbeResult> RunAsync(IRemoteProbeTarget target, TimeSpan budget, bool checkConditionalWrite, CancellationToken)`. It creates, re-creates and expects an exists refusal, reads and compares, then deletes. It returns a failure kind (credentials, container missing, access denied, unreachable, no conditional writes, mismatch) with no secret text. It is shared by validation (30 s, with the conditional check) and health (2 s, without).
- [X] T082 [US3] Create `SHARED/Storage/S3/S3UploadSpool.cs` (a temporary file under `Path.GetTempPath()/quickbuilder-s3/`, opened with `FileOptions.DeleteOnClose`, bounded by the bytes read) and `SHARED/Storage/S3/S3RangeReadStream.cs` (seekable, 1 MiB read-ahead, ranged `GetObject` per miss). Make T074 pass.
- [X] T083 [US3] Create `SHARED/Storage/S3/S3FileStorage.cs` implementing `IFileStorage` and `IFileStorageHealthCheck` per contracts/storage-providers.md §3.3:
  - an `AmazonS3Client` built from the settings (`ServiceURL`, `ForcePathStyle`, `RequestChecksumCalculation` and `ResponseChecksumValidation = WHEN_REQUIRED`, standard retries with 3 attempts; explicit `BasicAWSCredentials` or the default chain);
  - `PutObject` or multipart with `IfNoneMatch = "*"`, with `412` mapped to `StorageKeyExistsException` and an abort on failure;
  - the `HEAD` + range stream for reads;
  - streamed `ListObjectsV2`;
  - `KeyPrefix` handling;
  - health through `RemoteProbe`.

  Leave `AWSConfigs.LoggingConfig` off. Make T072's S3 half and T073's S3 part pass.
- [X] T084 [US3] Create `SHARED/Storage/S3/S3StorageOptionsValidator.cs` (the settings rules, then `RemoteProbe` with a 30 s budget; failure kinds mapped to the messages in storage-providers.md §5 naming `Storage:S3:*` keys) and `SHARED/Storage/S3/S3AbandonedUploadCleanupService.cs` (hourly; `ListMultipartUploads` under the prefix; abort those older than 1 hour; log counts). Make the S3 parts of T075 and T076 pass.
- [X] T085 [US3] Create `SHARED/Storage/AzureBlob/AzureBlobFileStorage.cs` per contracts/storage-providers.md §4.2:
  - a `BlobContainerClient` from `ConnectionString`, or from `AccountUrl` with `DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true, ExcludeVisualStudioCredential = true, ExcludeVisualStudioCodeCredential = true, ExcludeAzureDeveloperCliCredential = true })`;
  - `UploadAsync` with `IfNoneMatch = ETag.All`, mapping `409` and `412` to `StorageKeyExistsException`;
  - seekable `OpenReadAsync` with a 1 MiB buffer;
  - `DeleteIfExistsAsync`;
  - streamed `GetBlobsAsync`;
  - `KeyPrefix`;
  - health through `RemoteProbe`.

  Create `SHARED/Storage/AzureBlob/AzureBlobStorageOptionsValidator.cs`. Make the Azure parts of T072, T073, T075 and T076 pass.
- [X] T086 [US3] Create `MEDIA/Infrastructure/StorageCheckRunner.cs` (MV-21):
  - claim a run: mark expired `running` rows `failed`, then insert, treating a unique violation as "already running";
  - renew the lease every 30 s;
  - per site (distinct `site_id` from `media_files` across sites, with a reason): stream `ListAsync("{site:N}/media/")` into a `HashSet<string>`, stream the rows' `storage_key`, record the missing ones (the first 1,000 as rows) and exact counts;
  - finish `completed` or `failed`;
  - log counts only.

  Create `MEDIA/Infrastructure/StorageConsistencyCheckService.cs`: after `ApplicationStarted`, start a `startup` run unless one started after this process's start time is running or finished.
- [X] T087 [US3] Create the storage-check endpoints and the CLI command:
  - `MEDIA/Features/StorageCheck/GetStorageCheck/GetStorageCheck.cs` (`GET /api/v1/media/storage-check`: provider and latest run with the missing list);
  - `MEDIA/Features/StorageCheck/StartStorageCheck/StartStorageCheck.cs` (`POST`, 202; the run continues in a background task owned by a singleton `StorageCheckRunner`; `409` when running).

  Both declare `media.storage.check`. Create `MEDIA/Infrastructure/MediaCommands.cs`, an `IHostCommand` named `media` with the sub-command `check-storage`: it runs synchronously with trigger `cli`, prints counts and up to 100 `site/file` ids, and exits `0`, `3` or `1`. Register it and the service in `MEDIA/MediaModule.cs`. Make T078 pass.
- [X] T088 [US3] Create `ADMIN/features/settings/StorageSettingsView.vue`, add the Administrator-only route `/admin/settings/storage` in `ADMIN/app/router.ts` and a navigation entry beside the existing settings items, and add the i18n keys. Make T079 pass.
- [X] T089 [US3] Make T077 pass. Then update the documentation:
  - `docs/configuration.md`: every `Storage:S3:*` and `Storage:AzureBlob:*` key, secrets through environment variables only, and ambient identity;
  - `docs/operations.md`: choosing a provider, the minimum permissions (storage-providers.md §3.4, §4.3), the S3 lifecycle rule, multi-instance now supported with S3 or Azure (no shared cache needed, 012 E-4), backing up bucket or container together with the database, "Moving media between providers" (MV-21 commands and exclusions), the consistency check and CLI exit codes, and the outbound traffic of each provider (MV-20).

**Checkpoint**: US3 is demoable. Quickstart §6 passes. Multi-instance is documented.

**Open at the US3 checkpoint (2026-10-01)**: MinIO no longer publishes images to Docker Hub or quay.io. The S3 tests and `docker-compose.storage.yml` use `minio/minio:latest`, which works only where it is already cached; the S3 test service for CI is to be decided.

---

## Phase 6: User Story 4 - Choosing an image in the editor from the full library (Priority: P2)

**Goal**: the Image block's picker becomes `MediaBrowser` in a dialog, opens in the last
used folder, uploads into the current folder, and copies library alternative text into
blocks without it.

**Independent Test**: quickstart §3.

**Depends on**: US1 (`MediaBrowser`). US2 is not required; thumbnails fall back to
previews.

### Tests for User Story 4 ⚠️

- [X] T090 [P] [US4] Create `ADMINTESTS/editor/MediaControl.spec.ts`:
  - the dialog opens `MediaBrowser` in picker mode, in the last folder from `lastFolder.ts`, and falls back to the top level when that folder no longer exists;
  - choosing an image with alternative text fills an empty block `alt`, and does not overwrite a non-empty one;
  - choosing one without alternative text keeps 011's prompt;
  - closing without choosing changes nothing;
  - an upload in the dialog lands in the current folder and is chosen;
  - keyboard-only operation; axe clean.

### Implementation for User Story 4

- [X] T091 [P] [US4] Create `MEDIAUI/lastFolder.ts`: `get(userId, siteId)` and `set(userId, siteId, folderId | null)` over `localStorage` with key `qb.media.lastFolder.{userId}.{siteId}`, every access wrapped in `try/catch`, returning null on failure (MV-23).
- [X] T092 [US4] Rework `ADMIN/editor/panel/controls/MediaControl.vue`: replace the inline list-and-upload with a modal dialog (focus trap, `Escape` closes, focus returns to the trigger) containing `MediaBrowser` (`mode="picker"`, `initialFolderId` from `lastFolder.get`). On `folderChange`, call `lastFolder.set`. On `choose`, emit `{ mediaId }`, and when the block's `alt` is empty and not decorative, also emit the alternative-text change through the property panel's existing multi-prop change path (FR-009). Keep the missing-image marking. Add the i18n keys. Make T090 pass, and keep `ADMINTESTS/editor/PropertyPanel.spec.ts` green. (Revised during implementation: there was no multi-prop path, so the panel chains the file and the alternative text as two property changes applied once, one undo step; `MediaBrowser` falls back to the top level itself when a folder is gone, which the library screen benefits from too.)

**Checkpoint**: US4 is demoable. Quickstart §3 passes.

---

## Phase 7: User Story 5 - Seeing where a file is used before deleting it (Priority: P2)

**Goal**: the usage list in the library, the server-enforced in-use warning on delete, and
live-use deletion limited to members who may publish.

**Independent Test**: quickstart §4 (SC-007, SC-008, SC-015).

**Depends on**: Foundational (the references and reader) and US1 (the details panel).

### Tests for User Story 5 ⚠️

- [X] T093 [P] [US5] Create `ITESTS/Media/FileUsageApiTests.cs`:
  - `GET …/usage` lists draft, live, history and component places with correct flags and `livePagesThroughComponent`;
  - the counts match `IMediaUsageReader` after draft edits, publish, rollback and page deletion (SC-008);
  - `places` is empty with counts only for a caller without `pages.content.view` (simulated with a test capability set);
  - another site's file is not found.
- [X] T094 [P] [US5] Create `ITESTS/Media/DeleteInUseTests.cs` (SC-007, SC-015):
  - a used file without `confirmInUse` gives `409 media.in_use` with `usage`;
  - with it, the file is deleted;
  - an unused file is deleted without the flag;
  - an Editor deleting their own live-used file gets `403 media.in_live_use`, even with the flag;
  - an Editor may delete their own file used only in drafts or history (with the flag);
  - a Designer may delete a live-used file, and the live page renders without the image at the next request;
  - `canDelete` in listings reflects the live rule.

  Extend `ITESTS/Media/DeleteFileTests.cs` so that 003's cases still pass.
- [X] T095 [P] [US5] Create `ADMINTESTS/media/UsageList.spec.ts` and extend `ADMINTESTS/media/DeleteDialog.spec.ts`:
  - the list groups places with draft, live and history badges and links;
  - the delete dialog with usage names the places and needs a second confirmation;
  - the live-use message is shown to Editors before they act, and the Delete action is disabled when `canDelete` is false;
  - the plain confirmation when unused;
  - axe clean.

### Implementation for User Story 5

- [X] T096 [US5] Create `MEDIA/Features/GetFileUsage/GetFileUsage.cs` (`GET …/files/{fileId}/usage`, `media.files.view`): it calls `IMediaUsageReader.ListAsync` and `SummariseAsync`, and names places only when `access.HasCapabilityAsync(PageContentCapabilities.View, siteId)` holds. Make T093 pass.
- [X] T097 [US5] Extend `MEDIA/Features/DeleteFile/DeleteFile.cs` (MV-06). After the ownership check:
  - call `SummariseAsync`;
  - when `InLiveUse` and the caller lacks `PublishingCapabilities.Manage`, refuse with `media.in_live_use`;
  - when `InUse` and `!command.ConfirmInUse`, refuse with `media.in_use` carrying the summary.

  Add `[FromQuery] bool confirmInUse` to the controller and use the row version on delete (`media.conflict`). Update the class remarks with the accepted race (MV-06). Make T094 pass.
- [X] T098 [US5] Create `MEDIAUI/UsageList.vue` (places with badges and router links to the page editor or component editor, "on N live pages" for components) and place it in `MediaDetailsPanel.vue`'s usage slot. Rework `MEDIAUI/DeleteDialog.vue`:
  - show the usage summary and named places;
  - require a second explicit confirmation when in use, then call `remove(..., { confirmInUse: true })`;
  - explain the Designer or Administrator rule when `canDelete` is false because of live use;
  - handle a `409 media.in_use` race by reloading the usage.

  Add the i18n keys. Make T095 pass.

**Checkpoint**: US5 is demoable. Quickstart §4 passes.

---

## Phase 8: User Story 6 - Replacing a file's contents while keeping everything that points to it (Priority: P2)

**Goal**: replace with a new content version, a new address, and old addresses not found
at once. Live pages show the new contents at their next request. Live-used files may be
replaced only by members who may publish. The uploader is kept, and the replacement is
recorded separately.

**Independent Test**: quickstart §5 (SC-009, SC-015).

**Depends on**: US5 (the live-use check and usage summary in the dialog). US2 (variants)
only for the variant parts of the tests: skip those assertions when US2 is absent.

### Tests for User Story 6 ⚠️

- [ ] T099 [P] [US6] Create `ITESTS/Media/ReplaceFileApiTests.cs`:
  - a replace keeps the id, name, folder, alternative text, uploader, upload time and usage; increments `contentVersion`; sets `replacedBy` and `replacedAtUtc`; and returns the new `publicPath` (`…-v2.png`);
  - the old original and variant addresses are `not_found` on the next request (SC-009), and zero requests to the new address ever return old bytes;
  - the old key is deleted, or swept when the delete fails (with `FaultyStorage`);
  - a live page shows the new image at its next request through `FileChanged(replaced)`;
  - `media.kind_mismatch`, `media.dimensions_too_large` and 003's validation codes;
  - a stale `expectedContentVersion` gives `media.conflict`;
  - an interrupted upload leaves the file and address unchanged (FR-020);
  - an Editor replacing their own live-used file gets `media.in_live_use`, and their own unused file succeeds (SC-015);
  - another person's file needs `media.files.delete`;
  - the new thumbnail is queued.
- [ ] T100 [P] [US6] Create `ADMINTESTS/media/ReplaceDialog.spec.ts`: shows the usage summary and that live pages change immediately; the Editor live-use message before upload; progress; success refreshes the panel with the new version; error messages per code; axe clean.

### Implementation for User Story 6

- [ ] T101 [US6] Create `MEDIA/Features/ReplaceFile/ReplaceFile.cs` (`PUT …/files/{fileId}/content`, declaring `media.own_files.delete`; MV-06, MV-07; a raw body named by `X-QuickBuilder-File-Name`, like upload, with `expectedContentVersion` as a query parameter):
  1. Load the row, check its version against `expectedContentVersion`, and apply the ownership, live-use (`SummariseAsync` + `PublishingCapabilities.Manage`) and kind rules.
  2. Run `UploadPipeline` to store under `MediaStorageKeys.For(site, id, version + 1)` and read the metadata.
  3. Call `MediaFile.Replace` and add `FileChanged(replaced)` in one save with the row version, mapping a concurrency failure to `media.conflict` and deleting the new key on any failure before commit.
  4. Delete the old key best effort, and enqueue the thumbnail.

  Reuse `UploadSizeLimitAttribute` and `UploadGate` like the upload controller. Register it in `MEDIA/MediaModule.cs`. Make T099 pass. If US3 is complete, un-skip the replace step in `ITESTS/Media/MediaAcrossInstancesTests.cs`.
- [ ] T102 [US6] Create `MEDIAUI/ReplaceDialog.vue` (a single file input with the accepted extensions for the file's kind, the usage summary, "live pages will show the new file straight away", the Editor live-use explanation when `canReplace` is false, and progress) and place a **Replace** action in `MediaDetailsPanel.vue`'s replace slot, showing *Replaced by … at …* when set. Add the i18n keys. Make T100 pass.

**Checkpoint**: US6 is demoable. Quickstart §5 passes.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: isolation, logs, authorization declarations, upgrade, smoke, contracts and
docs, and the manual validation run.

- [ ] T103 [P] Extend `ITESTS/Isolation/Probes/MediaIsolationProbe.cs`: every new admin endpoint (folders, move, get, patch, usage, replace, admin variant) called with another site's ids is not found; public variant addresses of site A are not found on site B's host; `storage_check_missing` rows of site A never appear in a site-filtered read. The `AcrossAllSites` reads in `StorageCheckRunner`, `ImageMetadataBackfillService`, `SearchTextBackfillService` and `ThumbnailWorker` each have a test asserting they filter by the site they claim (Principle V).
- [ ] T104 [P] Extend `ITESTS/Observability/LoggingTests.cs`: upload, create a folder, set alternative text, replace, produce a variant, fail a variant, run the storage check and fail S3 validation, all with distinctive names, folder names, alternative text and secrets. The logs contain ids and counts only (FR-041).

  Extend `ITESTS/Observability/NoEgressTests.cs` (FR-042): with the default Local provider, the following make zero outbound connections:
  - uploading an image;
  - serving an original and a variant;
  - the thumbnail worker;
  - the metadata and search-text backfills;
  - a storage check.
- [ ] T105 [P] Extend `ATESTS/AuthorizationDeclarationTests.cs` so that every new action is covered, and the public variant route is in the explicit anonymous list.
- [ ] T106 [P] Create `ITESTS/Media/UpgradeFrom012Tests.cs`, modelled on `ITESTS/Publishing/UpgradeFrom011Tests.cs` (SC-012, FR-045):
  - restore a 012 snapshot with files, drafts and published pages using images;
  - both migrations apply;
  - every file is at the top level with no alternative text, and serves byte-identical content at its 003 address;
  - `search_text` is filled (provisional), and after `SearchTextBackfillService` runs, an accent-folded search (`ete` for `Été.pdf`) finds the file;
  - after the metadata backfill, dimensions and thumbnails exist and live pages render `width`, `height` and `srcset`;
  - usage matches the documents;
  - folder, alternative text and content version are present for every file (FR-044: enough for a future media manifest).
- [ ] T107 [P] Extend `STESTS/ReferenceDeploymentSmokeTests.cs` (FR-043): upload a JPEG fixture, place it in the published page's draft through the API, publish, read the `srcset` from the site's host name, and fetch one variant, expecting `200 image/jpeg`. Add `VariantPerformanceTests` to the performance job's filter in `.github/workflows/ci.yml` if the job lists classes. Set `QB_REQUIRE_DOCKER=1` in the `test` job so the MinIO and Azurite suites cannot pass by skipping.
- [ ] T108 [P] Update `specs/003-media-storage/contracts/storage-provider.md` with a short "Providers added by 013" note pointing to `specs/013-media-library-variants/contracts/storage-providers.md` and the key layout (readers.md §4). Record in `specs/003-media-storage/spec.md` Out of Scope, as a dated note, that #13 now covers folders, search, alternative text, usage, replace, variants and both providers.
- [ ] T109 [P] Update `docs/operations.md` with the variant storage growth and the metadata backfill after upgrade, update `docs/contributing.md` (the NetVips.Native security-update practice, plan Risks), and add the plan's Deferred items to `docs/backlog.md`. Put the plan's upgrade notes in the release notes draft.
- [ ] T110 Run [quickstart.md](./quickstart.md) §1–§9 by hand on the reference compose setup, with `docker-compose.storage.yml` for §6. Record the results, the SC-001, SC-002, SC-003 and SC-004 numbers, the Lighthouse CLS figure (SC-006) and the browser versions in `specs/013-media-library-variants/quickstart-results.md`.
- [ ] T111 Run `dotnet format --verify-no-changes`, `dotnet test` (with Docker, `QB_REQUIRE_DOCKER=1`), and `npm run lint && npm run type-check && npm test && npm run build` in `src/admin`. Fix any failures, and confirm the per-package coverage gate passes for `QuickBuilder.Media`, `QuickBuilder.Pages` and `QuickBuilder.Rendering`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**:
  - T002 → T003 → T004 (the spike) in order;
  - T005, T006 and T007 can run in parallel after T003.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
  - T008–T012 (domain) come first;
  - T013 needs T010–T012; T014–T019 can run in parallel;
  - T020 → T021 and T022 → T023 → T024 (Pages);
  - T025 needs T009, T014 and T015 (the configured widths); T026 needs T023; T027 can run in parallel with everything.
- **US1 (Phase 3)**: after Foundational. **The MVP.**
- **US2 (Phase 4)**: after Foundational and the T004 spike result. It is independent of
  US1, except that T069 touches `MediaBrowser.vue` (T042) and `MediaDetailsPanel.vue`
  (T043); if US1 is not done, T069's library part moves to US1.
- **US3 (Phase 5)**: after Foundational. It is independent of US1 and US2. T077's variant
  step needs US2 and its replace step needs US6; both are skipped until those exist.
- **US4 (Phase 6)**: after US1.
- **US5 (Phase 7)**: after Foundational and US1 (the details panel slot).
- **US6 (Phase 8)**: after US5 (the live-use check and the usage summary UI) and after
  US2's `UploadPipeline` (T059). If US2 is not done, T059's extraction moves to US6.
- **Polish (Phase 9)**: after the stories it covers. T110 is last but one, and T111 is
  last.

### Within Each User Story

- Tests are written first and fail.
- The domain comes before slices, slices before the reader or renderer integration, and the
  backend before the admin UI.
- The i18n keys land with the component that uses them.

### Parallel Opportunities

- **Phase 1**: T005, T006, T007.
- **Phase 2**: T008, T010, T012, T014–T019 and T027 together; then T021 beside T022.
- **US1**: tests T028–T034 together; T039 beside T037; T041 beside T038.
- **US2**: tests T045–T055 together; T057 and T058 together after T056; T060 beside T061.
- **US3**: tests T071–T079 together; the S3 (T082–T084) and Azure (T085) tracks in
  parallel; the check (T086–T088) in parallel with both.
- **Across stories**, after Foundational: US1, US2 and US3 can go to three developers.
  US4 and US5 follow US1, and US6 follows US5.

---

## Parallel Example: User Story 2

```bash
# Tests together, before implementation:
Task: "T046 ImageMetadataTests"
Task: "T047 VariantPlanTests"
Task: "T048 VariantEncodingGoldenTests"
Task: "T049 VariantServingTests"
Task: "T050 VariantProductionExactlyOnceTests"
Task: "T054 MediaRendererTests + golden files"

# Imaging side by side once NetVipsSetup (T056) exists:
Task: "T057 ImageMetadataReader"
Task: "T058 VariantEncoder"
```

## Parallel Example: User Story 3

```bash
# Provider tracks in parallel after T080–T081:
Task: "T082–T084 S3 spool, range stream, provider, validator, cleanup"
Task: "T085 Azure provider and validator"
Task: "T086–T088 storage check runner, endpoints, CLI, admin screen"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup, including the spike (T004).
2. Phase 2: Foundational. Every 003 address still serves, and the usage backfill is proven.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Folders, search and alternative text work at
   10,000 files.

### Incremental Delivery

1. Setup and Foundational.
2. US1: library organisation and search. **Demo.**
3. US2 (variants) and US3 (remote providers), in either order or in parallel. All P1
   stories are done: images are sized, and multi-instance is supported.
4. US4 (the picker), then US5 (usage and delete), then US6 (replace).
5. Polish, the manual quickstart run, and the PR closing #13.

The feature ships in one PR, as with 010–012, so there is no feature flag (plan, Principle
XI). The remote providers are inert until selected.

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US6.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #13.
- **Contract changes** (T016, T018, T068 and T108) land in the same PR as their tests
  (constitution, Development Workflow).
- **Avoid**:
  - calling NetVips outside `MEDIA/Imaging/`;
  - decoding pixels before the content type and pixel limit are checked;
  - serving the original with long caching at a variant address;
  - naming files, folders or secrets in logs or events;
  - making Media read Pages' tables (use `IMediaUsageReader`).
