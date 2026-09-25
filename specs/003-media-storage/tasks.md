---

description: "Task list for 003-media-storage"
---

# Tasks: Media Library and Storage Abstraction

**Input**: Design documents from `/specs/003-media-storage/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec measures behaviour through automated checks (SC-002 to SC-013). FR-024 requires a conformance suite, and constitution Principle X makes tests mandatory. Within each story, test tasks come first and must fail before implementation starts.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md) so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story the task belongs to (US1–US4)
- Every task names an exact file path

## Path Conventions

The layout is the modular monolith from [plan.md](./plan.md#project-structure):
- **Backend**: `src/QuickBuilder.Host/`, `src/Shared/`, and `src/Modules/Media/` (new)
  alongside `Sites` and `Identity`.
- **Frontend**: `src/admin/src/`.
- **Tests and docs**: `tests/` and `docs/`.

Shorthand used below:
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `STORAGE` = `src/Shared/QuickBuilder.Shared.Infrastructure/Storage`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`

Feature folders follow 001's one-folder-per-slice rule. Each `Features/<Name>/<Name>.cs`
holds the controller action, request/response, command or query, validator and handler.
Research decisions are cited as `M-xx`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the new module's projects, wiring and configuration sections. Nothing here has behaviour.

- [ ] T001 Commit `specs/003-media-storage/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `003-media-storage` at the repository root (plan.md "Development Workflow")
- [ ] T002 [P] Create `src/Modules/Media/QuickBuilder.Media.Contracts/QuickBuilder.Media.Contracts.csproj`: a class library with no EF Core, ASP.NET Core or Npgsql references, mirroring `src/Modules/Sites/QuickBuilder.Sites.Contracts/QuickBuilder.Sites.Contracts.csproj`
- [ ] T003 [P] Create `src/Modules/Media/QuickBuilder.Media/QuickBuilder.Media.csproj`, mirroring `src/Modules/Identity/QuickBuilder.Identity/QuickBuilder.Identity.csproj`:
  - `FrameworkReference` to `Microsoft.AspNetCore.App`;
  - project references to Shared.Kernel, Shared.Infrastructure, Media.Contracts, Sites.Contracts and Identity.Contracts;
  - the EF Core, Npgsql and EF Design packages;
  - `InternalsVisibleTo` for `QuickBuilder.UnitTests`, `QuickBuilder.IntegrationTests` and `QuickBuilder.ArchitectureTests`.
- [ ] T004 Wire the new projects in:
  - add both projects to `QuickBuilder.slnx`;
  - reference `QuickBuilder.Media` from `src/QuickBuilder.Host/QuickBuilder.Host.csproj`;
  - reference both projects from `tests/QuickBuilder.ArchitectureTests/QuickBuilder.ArchitectureTests.csproj`, `UTESTS/QuickBuilder.UnitTests.csproj` and `ITESTS/QuickBuilder.IntegrationTests.csproj`.
- [ ] T005 [P] Add `Storage` (`Provider: "Local"`, `Local: { Path: "" }`) and `Media` (`MaxFileSizeBytes: 20971520`, `AllowedTypes` with the six types, `OrphanGracePeriod: "1.00:00:00"`) sections to `src/QuickBuilder.Host/appsettings.json`. Values come from research.md "Configuration keys introduced". An empty `Path` means the per-user default (M-06).
- [ ] T006 [P] Add the two Media `.csproj` files to the restore `COPY` block in `Dockerfile`, next to the Identity lines, so the restore layer still sees every project

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the storage abstraction, the local provider and its conformance suite, the Media domain, schema and module registration, and the test harness. Every story depends on these.

**⚠️ CRITICAL**: No user-story work can start until this phase is complete.

### Storage abstraction (M-01 to M-05, contracts/storage-provider.md)

- [ ] T007 [P] Create `STORAGE/StorageKey.cs`: a `readonly record struct StorageKey` whose constructor enforces the grammar (1–8 segments of `[a-z0-9-]{1,64}` separated by `/`) and throws `ArgumentException` naming the rule broken. Add `StorageKey.Parse`, `ToString`, `FirstSegment` and `StartsWith(string prefix)`.
- [ ] T008 [P] Create `STORAGE/IFileStorage.cs` with:
  - the `IFileStorage` interface (`WriteAsync`, `OpenReadAsync`, `ExistsAsync`, `DeleteAsync`, `ListAsync`), with XML docs stating every guarantee in contracts/storage-provider.md §1;
  - `StoredFile` (a disposable `Content` stream, `Length`, `LastModifiedUtc`);
  - `StoredFileInfo` (`Key`, `Length`, `LastModifiedUtc`);
  - `StorageKeyExistsException`;
  - `IFileStorageHealthCheck` (`Task<bool> CheckAsync(CancellationToken)`), declared beside `IFileStorage` but separate from it (M-22).
- [ ] T009 [P] Create `STORAGE/StorageOptions.cs` (section `Storage`, with `Provider` defaulting to `Local` and a nested `Local.Path`) and `STORAGE/Local/LocalStorageOptionsValidator.cs`, an `IValidateOptions<StorageOptions>` that enforces M-05:
  - the provider is supported (compared case-insensitively), and the message lists the supported names;
  - the path is absolute; an empty path resolves to `{LocalApplicationData}/QuickBuilder/media`;
  - after `ResolveLinkTarget`, the path is not the same as, or under, any entry of `StorageOptions.ForbiddenRoots`. This is an internal list that the host fills in through `AddFileStorage` (T012, T013) and that is never bound from configuration, so `Shared.Infrastructure` takes no ASP.NET Core dependency (M-05);
  - the directory is created if missing, and a probe file `{root}/.probe-{Guid:N}` is written and deleted.

  Every failure message names `Storage:Provider` or `Storage:Local:Path`.
- [ ] T010 Create `STORAGE/Local/LocalFileStorage.cs`, an internal sealed class implementing `IFileStorage` per M-04:
  - maps a key to `{root}/{key}`, checks with `Path.GetFullPath` that the result lies under `root`, and throws otherwise;
  - `WriteAsync` streams to `{root}/{key.FirstSegment}/.incoming/{Guid:N}.part` (the directory is created if missing, so temporary files are namespaced by site, as Principle V requires) with `UnixCreateMode = 0640` on non-Windows, calls `Flush(true)`, then `File.Move(part, final, overwrite: false)` after creating the parent directory. An existing target maps to `StorageKeyExistsException`, and any failure or cancellation deletes the `.part` file.
  - `OpenReadAsync` returns a seekable `FileStream` (`FileShare.Read | FileShare.Delete`, async, 80 KiB buffer), or `null`;
  - `ListAsync` enumerates lazily under `{root}/{prefix}` and skips every `.incoming` directory, at any depth;
  - `DeleteAsync` ignores missing files;
  - it also implements `IFileStorageHealthCheck`: write `{root}/.probe-{Guid:N}`, read it back, delete it, and return `false` on any exception. The probe code is shared with `LocalStorageOptionsValidator` (T009).
- [ ] T011 [P] Create `STORAGE/Local/LocalIncomingCleanupService.cs`, a `BackgroundService` that walks `{root}/*/.incoming/`, one first-segment (site) directory at a time, and deletes `.part` files older than one hour, at startup and then hourly on a `PeriodicTimer(TimeProvider)`. It also removes `.probe-*` files at the root that are older than one hour, in case a probe's process died mid-way. It logs counts only (M-04).
- [ ] T012 Create `STORAGE/StorageRegistration.cs` with `AddFileStorage(this IServiceCollection, IConfiguration, IReadOnlyList<string> forbiddenRoots)`:
  - binds `StorageOptions` with `ValidateOnStart`, copies `forbiddenRoots` (ignoring null or empty entries) into `ForbiddenRoots`, and registers the validator;
  - registers `LocalFileStorage` as the singleton `IFileStorage` when the provider is `Local`, and the same instance as `IFileStorageHealthCheck`;
  - registers `LocalIncomingCleanupService`.
- [ ] T013 Call `builder.Services.AddFileStorage(builder.Configuration, [AppContext.BaseDirectory, builder.Environment.ContentRootPath, builder.Environment.WebRootPath])` in `src/QuickBuilder.Host/Program.cs`, after `AddSharedInfrastructure()` and before the modules
- [ ] T014 Add a storage check to `/health/ready` in `src/QuickBuilder.Host/Health/HealthController.cs` (M-22). It calls `IFileStorageHealthCheck.CheckAsync` under a 2-second timeout and reports `Storage: Healthy|Unhealthy`. An unhealthy check makes the instance not ready. Add cases to `ITESTS/Startup/StartupTests.cs`:
  - after startup, making the storage directory read-only, or unreadable, makes `/health/ready` report not ready, and restoring the permissions makes it ready again (both skipped on Windows);
  - a check that hangs past 2 seconds, substituted through `configureTestServices`, also reports not ready.

### Storage tests (M-19)

- [ ] T015 [P] Unit-test the key grammar in `UTESTS/Storage/StorageKeyTests.cs`:
  - valid keys, and each rejected form: `..`, a leading or trailing `/`, upper case, `%2e`, `\`, empty segments, a 65-character segment, 9 segments;
  - `StartsWith`.
- [ ] T016 [P] Write the abstract conformance suite in `ITESTS/Storage/FileStorageConformanceTests.cs`, with `protected abstract IFileStorage CreateStorage()`. Implement every case in contracts/storage-provider.md §5: round trip (0 B, 1 B, 1 MiB, 25 MiB), create-only, atomicity under a throwing stream and under cancellation, seekable partial reads, missing keys, idempotent delete, listing by prefix, isolation between site prefixes, grammar rejection, and concurrency (20 distinct keys; 20 writers on one key yielding exactly one success).
- [ ] T017 Create `ITESTS/Storage/LocalFileStorageConformanceTests.cs`, deriving from T016 with a fresh temporary directory per test and deleting it on dispose. Add `ITESTS/Storage/LocalFileStorageTests.cs` for provider-specific behaviour:
  - the resolved-path containment guard;
  - files have no execute bits (mode `0640`, skipped on Windows);
  - `.part` files never appear in `ListAsync`;
  - a write to a key under site A creates its `.part` file under `{root}/{siteA}/.incoming/`, never under site B's directory or the root (Principle V);
  - `LocalIncomingCleanupService` removes a `.part` file older than one hour and keeps a newer one, in each of two site directories, using `ManualClock`.

### Media domain, schema and module

- [ ] T018 [P] Create `MEDIA/MediaOptions.cs` (section `Media`, with `MaxFileSizeBytes`, `AllowedTypes` and `OrphanGracePeriod`) plus an `IValidateOptions` that enforces the ranges in research.md "Configuration keys introduced". Every entry of `AllowedTypes` must be one of the six built-in types, and the list must not be empty (FR-007).
- [ ] T019 [P] Create `MEDIA/Domain/MediaFileType.cs`: the six types as static instances, each with its canonical MIME type, its extensions (M-08 table), `FromMime` and `Accepts(string extension, string? declaredType)`. `Accepts` treats a null or empty type and `application/octet-stream` as undeclared (M-08).
- [ ] T020 [P] Create `MEDIA/Domain/MediaFileName.cs` with `Clean(string raw) → string?`, which follows M-17:
  1. percent-decode;
  2. keep only the part after the last `/` or `\`;
  3. normalise to NFC;
  4. strip Unicode categories Cc and Cf;
  5. trim whitespace and dots;
  6. shorten the stem to at most 255 characters while keeping the extension;
  7. return `null` if nothing is left.

  Add `Extension(string cleaned)`, which returns the lower-case extension without the dot.
- [ ] T021 [P] Create `MEDIA/Infrastructure/FileTypeDetector.cs` with `Detect(ReadOnlySpan<byte> head) → MediaFileType?`, using the signatures in M-07. For AVIF, parse the `ftyp` box's major brand and compatible brands within the box size, bounded by `head`.
- [ ] T022 [P] Create `MEDIA/Domain/MediaFile.cs`: an entity implementing `ISiteOwned` with the fields in data-model.md §1 and a static `Create(...)` that enforces `size > 0` and an extension matching `^[a-z0-9]{1,8}$`. Create `MEDIA/Infrastructure/MediaStorageKeys.cs` with `For(Guid siteId, Guid fileId) → StorageKey` (`{siteId:N}/media/{fileId:N}`) and `SitePrefix(Guid siteId)`.
- [ ] T023 Create `MEDIA/Infrastructure/MediaDbContext.cs`:
  - schema `media`, with `MediaFiles` mapped to `media_files` through the shared snake-case convention;
  - the filter `SiteId == CurrentSiteId`, via a context property (001 `SiteQueryFilter` guidance);
  - the constraints and indexes in data-model.md §1;
  - `modelBuilder.AddOutbox(Schema)`, unconditionally, as `SitesDbContext` and `IdentityDbContext` do. `OutboxRetentionService` reads `Set<OutboxMessage>()` from every module context and throws without it.

  Also create `MEDIA/Infrastructure/MediaDbContextFactory.cs` (design time), mirroring `SitesDbContextFactory`.
- [ ] T024 Generate the `InitialMedia` migration into `MEDIA/Infrastructure/Migrations/` (`dotnet ef migrations add InitialMedia --project src/Modules/Media/QuickBuilder.Media`). Check that it creates only schema `media`, table `media_files`, the outbox table `media.outbox_messages`, `pk_media_files`, `ux_media_files_storage_key`, `ix_media_files_site_listing` and both check constraints.
- [ ] T025 [P] Create `MEDIA/MediaCapabilities.cs` with `media.files.view`, `media.files.upload`, `media.files.delete` and `media.own_files.delete`, all `CapabilityScope.Site` and granted as in the M-18 table
- [ ] T026 [P] Create `MEDIA/Infrastructure/MediaErrorResults.cs`, mapping error codes to statuses in the problem shape of `src/Modules/Sites/QuickBuilder.Sites/ErrorResults.cs`:

  | Error codes | Status |
  |---|---|
  | `media.not_found` | 404 |
  | `media.empty`, `media.file_name_required`, `media.type_mismatch`, `media.invalid_cursor`, `media.invalid_limit` | 400 |
  | `media.too_large` | 413 |
  | `media.unsupported_type` | 415 |
  | `media.storage_unavailable` | 503 |
  | `media.storage_full` | 507 |
- [ ] T027 [P] Create `MEDIA/Infrastructure/MediaResponseHeaders.cs` with `Apply(HttpResponse, MediaFile, MediaResponseKind kind)`. `kind` is `Public` or `Preview`, and the headers are exactly those in M-11 and M-12:
  - `nosniff`;
  - `Content-Disposition: inline; filename*=UTF-8''…` built with `ContentDispositionHeaderValue.SetHttpFileName`;
  - the image-only sandbox CSP;
  - `Cache-Control`, which differs by kind.

  It returns the `EntityTagHeaderValue` (`"<id:N>"`) and the `Last-Modified` value for `File(...)`.
- [ ] T028 [P] Create `src/Modules/Media/QuickBuilder.Media.Contracts/MediaFileDto.cs` with the `MediaFile` fields from contracts/media-api.yaml (`id`, `siteId`, `fileName`, `contentType`, `sizeBytes`, `uploadedAtUtc`, `uploadedBy` as `UserSummaryDto`, `publicPath`, `available`, `canDelete`) and `MediaFilePageDto` (`items`, `totalCount`, `nextCursor`)
- [ ] T029 Create `MEDIA/MediaModule.cs` with `AddMediaModule(this IServiceCollection, IConfiguration)`, mirroring `SitesModule`:
  - registers `MediaDbContext` with the migration history in schema `media`;
  - registers `MediaDatabaseProvider : IModuleDatabaseProvider`;
  - binds `MediaOptions` with its validator and `ValidateOnStart`;
  - registers `FileTypeDetector` and every capability in `MediaCapabilities`.

  Slice registrations are added by each story.
- [ ] T030 Wire the module in `src/QuickBuilder.Host/Program.cs`: add `builder.Services.AddMediaModule(builder.Configuration);` and `.AddApplicationPart(typeof(MediaModule).Assembly)`

### Test harness

- [ ] T031 Extend `ITESTS/Infrastructure/QuickBuilderApp.cs` to give each app instance its own storage directory:
  - add an optional `string? storagePath` constructor parameter;
  - when it is null, create a unique temporary directory and pass it with `UseSetting("Storage:Local:Path", …)`;
  - delete it on dispose unless it was supplied;
  - expose it as `StoragePath`.

  Two instances in one test share a directory by passing the same path (for SC-007).
- [ ] T032 [P] Create `ITESTS/Media/MediaApi.cs` with helpers that go through the real HTTP pipeline:
  - `UploadAsync(client, siteId, bytes, fileName, contentType)`, which sets `X-QuickBuilder-Request: 1`, the percent-encoded `X-QuickBuilder-File-Name` and `Content-Type`;
  - `ListAsync(client, siteId, cursor, limit)`;
  - `DeleteAsync(client, siteId, fileId)`;
  - `PreviewAsync(client, siteId, fileId)`;
  - `PublicGetAsync(anonymousClient, host, fileId, ext)`.

  Every file name it generates contains a canary marker `zz-canary-<guid>` (M-20).
- [ ] T033 [P] Create `ITESTS/Media/SampleFiles.cs`:
  - minimal valid bytes for each of the six types: 1×1 JPEG, PNG, GIF, WebP and AVIF, and a one-page PDF;
  - `OfSize(type, bytes)`, which pads a valid header to an exact size;
  - `Throwing(afterBytes)`, a stream that throws partway through.

### Foundational tests

- [ ] T034 [P] Unit-test `FileTypeDetector` in `UTESTS/Media/FileTypeDetectorTests.cs`:
  - each signature;
  - truncated headers;
  - near misses: `GIF88a`, `RIFF….WAVE`, `ftyp` with `mif1` only, `%PDF` without the dash;
  - AVIF with `avif` only as a compatible brand;
  - an empty span.
- [ ] T035 [P] Unit-test `MediaFileType.Accepts` and `MediaFileName.Clean` in `UTESTS/Media/MediaFileTypeTests.cs` and `UTESTS/Media/MediaFileNameTests.cs`:
  - case-insensitive extensions;
  - an undeclared type, and `application/octet-stream`;
  - a contradicting declared type;
  - paths with `/` and `\`;
  - a right-to-left override (`U+202E`) stripped;
  - NFC normalisation;
  - a 300-character name shortened with its extension kept;
  - a name that is only dots or spaces returns null.
- [ ] T036 [P] Unit-test both options validators in `UTESTS/Storage/StorageOptionsValidationTests.cs` and `UTESTS/Media/MediaOptionsValidationTests.cs`:
  - an unknown provider lists `Local`;
  - a relative path;
  - a path under `AppContext.BaseDirectory`;
  - an unwritable path (skipped on Windows);
  - an SVG in `AllowedTypes`;
  - an empty `AllowedTypes`;
  - out-of-range size and grace period.
- [ ] T037 Extend `tests/QuickBuilder.ArchitectureTests/ModuleBoundaryTests.cs`:
  - Media references no implementation project of another module;
  - `QuickBuilder.Shared.Infrastructure` (including `Storage/`) references no module;
  - no type in `QuickBuilder.Media` uses `System.IO.File`, `FileInfo`, `FileStream`, `Directory` or `DirectoryInfo`, and within `QuickBuilder.Shared.Infrastructure` only the `Storage.Local` namespace may (FR-019: bytes are reached only through `IFileStorage`);
  - `MediaFile` implements `ISiteOwned`.

  Add a matching violating fixture in `tests/QuickBuilder.ArchitectureTests/Fixtures/ViolatingFixtures.cs`, and assert in `RulesFireWhenBrokenTests.cs` that the new rule fires.

**Checkpoint**: the solution builds, the conformance suite passes against `LocalFileStorage`, the `media` schema migrates, and `SiteFilterCoverageTests` passes with `MediaFile` included. User-story work can begin.

---

## Phase 3: User Story 1 - A site member uploads files to the site's media library (Priority: P1) 🎯 MVP

**Goal**: a signed-in site member uploads images and PDFs to one site's library. Invalid files are refused with a reason. Valid files appear in the library with their details and a preview.

**Independent Test**: as a user with a role on one site, upload one allowed file of each type and confirm that each appears in that site's library with correct details. Then upload an oversized file and a disguised file, confirm both are refused with distinct reasons, and confirm neither appears (quickstart §1–§2).

### Tests for User Story 1 ⚠️ write first, confirm they fail

- [ ] T038 [P] [US1] Write `ITESTS/Media/UploadFileTests.cs`:
  - each of the six sample types uploads with `201`, and the body matches `MediaFile` in contracts/media-api.yaml, including `uploadedBy.displayName` and a `publicPath` of `/_media/{id:N}.{ext}`;
  - a row and a stored object exist;
  - the stored bytes are identical to the upload;
  - an Editor, a Designer and an Administrator can each upload;
  - a user with no role on the site gets `404 sites.not_found`, and no row is created (US1 #7).
- [ ] T039 [P] [US1] Write `ITESTS/Media/UploadValidationTests.cs`, a `[Theory]` over a generated set of at least 50 crafted files (M-20):
  - scripts and HTML renamed to `.jpg`, `.png` and `.pdf`;
  - SVG, plain and with script;
  - `MZ`, ELF and Mach-O headers;
  - ZIP and DOCX;
  - polyglots that lead with HTML (refused);
  - GIF/JS and JPEG/HTML polyglots that lead with a valid signature (accepted);
  - every type with a wrong extension, with a wrong declared type, and with no extension;
  - `application/octet-stream` with the right extension (accepted);
  - an empty body;
  - a missing file-name header;
  - bodies at the limit, one byte over, and 5× over, both with `Content-Length` and chunked.

  Each case asserts the exact status and `errorCode`, and that neither a row nor a stored object was created (SC-002).
- [ ] T040 [P] [US1] Write `ITESTS/Media/UploadConsistencyTests.cs`:
  - storage throws mid-write through `SampleFiles.Throwing`: no row, no object, and `503 media.storage_unavailable`;
  - the client aborts mid-body: no row and no object;
  - the database insert fails after the write (an EF `SaveChangesInterceptor` that throws, registered through `configureTestServices`): no row, and the stored object is gone, removed by the handler's best-effort delete. The case where that delete also fails is covered by the sweep tests;
  - storage throws an `IOException` with a disk-full HResult: `507 media.storage_full`, with no row and no object (spec edge case "the storage location fills up");
  - the listing never shows a partial file (US1 #6, FR-011).
- [ ] T041 [P] [US1] Write `ITESTS/Media/ListFilesTests.cs`:
  - the newest file comes first;
  - `limit` defaults to 50, the maximum is 100, and 101 gives `400 media.invalid_limit`;
  - following `nextCursor` across three pages visits every file exactly once, including when files are inserted between pages (US3 #1 is also satisfied here);
  - `totalCount` is correct;
  - a tampered cursor gives `400 media.invalid_cursor`;
  - a site the caller cannot see gives `404 sites.not_found`;
  - an Administrator listing a random site id gets `404 sites.not_found`, byte-identical (apart from `traceId`) to 002's response for a missing site;
  - files from another site never appear.
- [ ] T042 [P] [US1] Write `ITESTS/Media/PreviewTests.cs`:
  - a preview of a site with no host name returns the bytes;
  - the headers match M-12 (`private, no-cache`, `nosniff`, a sandbox CSP for images, none for PDF);
  - `If-None-Match` gives `304`;
  - `Range` gives `206`;
  - another site's file id gives `404 media.not_found`;
  - an Administrator previewing under a random site id gets `404 sites.not_found`;
  - no session gives `401`.
- [ ] T043 [P] [US1] Write `src/admin/tests/mediaLibraryView.spec.ts` (vitest + axe), mirroring `sitesView.spec.ts`:
  - the privacy notice renders before any upload;
  - the choose-files button is keyboard-operable;
  - each per-file result lands in an `aria-live="polite"` list;
  - a refusal shows the translated reason for each `errorCode`;
  - an empty library shows the empty state;
  - axe finds no violations.

### Implementation for User Story 1

- [ ] T044 [US1] Create `MEDIA/Infrastructure/UploadSizeLimitAttribute.cs`, an action filter implementing M-09:
  - a `Content-Length` above `MediaOptions.MaxFileSizeBytes` returns `413 media.too_large` before the body is read;
  - otherwise it sets `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` to the limit when it is not read-only.

  The `413` detail states the limit.
- [ ] T045 [US1] Create `MEDIA/Infrastructure/MediaFileMapper.cs`. It maps rows to `MediaFileDto`:
  - resolves uploader names in one `IUserDirectory.FindManyAsync` call;
  - builds `publicPath` from `HttpRequest.PathBase` + `/_media/{id:N}.{ext}`;
  - takes `available` and `canDelete` as inputs, set to `true` and computed from the M-18 rule for now.

  US3 refines `available`.
- [ ] T046 [US1] Implement `MEDIA/Features/UploadFile/UploadFile.cs`:
  - **Request**: `UploadFileCommand(SiteId, UserId, RawFileName, DeclaredType, Stream Body)`.
  - **Handler steps**:
    1. Confirm the site exists through `ISiteDirectory`.
    2. `MediaFileName.Clean`, or fail with `media.file_name_required`.
    3. Read up to 4 KiB of the head; an empty body fails with `media.empty`.
    4. Detect the type with `FileTypeDetector`. Fail with `media.unsupported_type` if the type is not detected or is not in `AllowedTypes`.
    5. Check `MediaFileType.Accepts`, or fail with `media.type_mismatch`.
    6. Generate `Guid.NewGuid()`.
    7. `IFileStorage.WriteAsync(MediaStorageKeys.For(...))` with a stream that concatenates the head and the rest and counts bytes, failing with `media.too_large` once the count passes the limit.
    8. Insert the `MediaFile` row and commit.
    9. If the commit fails, call `DeleteAsync` best-effort and rethrow.
  - **Storage failures**: `IOException` with disk-full HResults maps to `media.storage_full`; any other storage exception maps to `media.storage_unavailable`.
  - **Controller**: `POST api/v1/media/sites/{siteId:guid}/files` with `[RequireCapability(MediaCapabilities.Upload)]` and `[UploadSizeLimit]`. It reads `X-QuickBuilder-File-Name` and `Content-Type` and passes `Request.Body`. It returns `201` with `Location` and a `MediaFileDto` mapped through the helper in T045.
  - **Logging**: file id and site id only (FR-030).
  - **Registration**: in `MEDIA/MediaModule.cs`.
- [ ] T047 [US1] Implement `MEDIA/Features/ListFiles/ListFiles.cs`:
  - **Request**: `ListFilesQuery(SiteId, Cursor?, Limit)`.
  - **Handler**:
    - first confirms the site exists through `ISiteDirectory.FindByIdAsync`, and otherwise returns `sites.not_found`. Administrators pass the visibility check for any id (002 authorization.md §3), so this check is what keeps a made-up site a `404`;
    - reads with `AcrossAllSites("Admin listing of a site named explicitly in the route (001 FR-011b).")`, then filters `SiteId == siteId`, using `AsNoTracking`;
    - pages by keyset on `(UploadedAtUtc DESC, Id DESC)` with a base64url cursor encoding both values, and fails with `media.invalid_cursor` on a malformed cursor;
    - counts with `totalCount`;
    - maps through T045.
  - **Controller**: `GET api/v1/media/sites/{siteId:guid}/files` with `[RequireCapability(MediaCapabilities.View)]`.
  - **Registration**: in `MEDIA/MediaModule.cs`.
- [ ] T048 [US1] Implement `MEDIA/Features/GetFileContent/GetFileContent.cs`:
  - **Handler**: confirms the site exists through `ISiteDirectory` (else `sites.not_found`, as in T047), reads the row across sites for the route's `siteId`, then calls `IFileStorage.OpenReadAsync`. It returns `media.not_found` if the row or the bytes are missing.
  - **Controller**: `GET api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/content` with `[RequireCapability(MediaCapabilities.View)]`. It applies `MediaResponseHeaders` with `Preview`, then returns `File(stream, contentType, lastModified, etag, enableRangeProcessing: true)`.
  - **Registration**: in `MEDIA/MediaModule.cs`.
- [ ] T049 [P] [US1] Create `src/admin/src/api/media.ts`:
  - typed `MediaFile` and `MediaFilePage`;
  - `listFiles(siteId, cursor?)`;
  - `previewUrl(siteId, fileId)`;
  - `uploadFile(siteId, file, onProgress)`, using `XMLHttpRequest` with `withCredentials`, `X-QuickBuilder-Request: 1`, `X-QuickBuilder-File-Name: encodeURIComponent(file.name)` and `Content-Type: file.type || 'application/octet-stream'`. It maps the response to the same `ApiError` as `src/admin/src/api/request.ts`, including the 401 and 428 handling.
- [ ] T050 [US1] Add the route `{ path: '/sites/:siteId/media', name: 'site-media', component: MediaLibraryView }` to `src/admin/src/app/router.ts`, and a "Media" link per site in `src/admin/src/features/sites/SitesView.vue`
- [ ] T051 [P] [US1] Create `src/admin/src/features/media/UploadArea.vue`:
  - a real `<button>` opening a multiple-file `<input type="file">`, plus a drop zone;
  - the privacy notice from FR-027 above both;
  - uploads the chosen files through `uploadFile`, at most three at a time;
  - shows a per-file progress bar and result in a `<ul aria-live="polite">`;
  - maps each `errorCode` to a translated message;
  - emits `uploaded(file)`.
- [ ] T052 [P] [US1] Create `src/admin/src/features/media/MediaList.vue`, a table showing:
  - a preview (an `<img>` from `previewUrl` for images, and a PDF icon otherwise);
  - the name as text;
  - the type, the size (formatted), the upload time and the uploader.

  It also shows an empty state.
- [ ] T053 [US1] Create `src/admin/src/features/media/MediaLibraryView.vue`: the site's name as a heading, `UploadArea`, and `MediaList` loading the first page through `listFiles`. Newly uploaded files are prepended without a reload.
- [ ] T054 [US1] Add every new US1 string to `src/admin/src/i18n/en.json` under `media.*`: headings, the privacy notice, buttons, empty state, column headers, and messages for `media.empty`, `media.file_name_required`, `media.type_mismatch`, `media.too_large` (with the limit), `media.unsupported_type`, `media.storage_unavailable` and `media.storage_full`

**Checkpoint**: US1 is independently demoable. Uploads are validated and stored, and they appear in the site's library with previews.

---

## Phase 4: User Story 2 - Visitors receive uploaded files safely from the site's own address (Priority: P1)

**Goal**: each file is served on its own site's host names at `/_media/{id}.{ext}`, byte-exact, with pinned types, and never served for another site.

**Independent Test**: fetch every uploaded type on site A's host and compare bytes and headers. Fetch the same addresses on site B's host and get `404`. Fetch crafted polyglots and confirm the safety headers (quickstart §3–§4).

### Tests for User Story 2 ⚠️ write first, confirm they fail

- [ ] T055 [P] [US2] Write `ITESTS/Media/PublicServingTests.cs`:
  - GET on the site's host returns the exact bytes, `Content-Length` and type;
  - HEAD returns the same headers with no body;
  - `Range: bytes=0-99` gives `206` with the right `Content-Range`, a range past the end gives `416`, and `If-None-Match` gives `304`;
  - with `Hosting:BasePath` set, the address is served under the base path and `publicPath` includes it;
  - no session is needed.
- [ ] T056 [P] [US2] Write `ITESTS/Media/MediaSiteIsolationTests.cs`. With two sites and files on each, every combination of host and file is tested:
  - the own site returns `200`;
  - the other site's host returns `404 media.not_found`.

  Those `404` bodies are compared byte-for-byte, ignoring `traceId`, with the responses for a never-issued id, a wrong extension and a deleted row. An unknown host returns `sites.not_resolved`. Traversal attempts (`..%2F`, `%5C`, `//etc/passwd`, overlong ids, upper-case hex) all return `404` without touching storage, checked with a counting `IFileStorage` decorator (SC-003, US2 #6).
- [ ] T057 [P] [US2] Write `ITESTS/Media/MediaServingHeadersTests.cs`, a `[Theory]` over every *accepted* case in T039's crafted-file set plus the six samples. On both the public address and the preview it asserts:
  - the exact `Content-Type`;
  - `X-Content-Type-Options: nosniff`;
  - `Content-Disposition: inline` with `filename*`;
  - for images, `Content-Security-Policy` exactly as in M-11; for PDF, none;
  - `Cache-Control` per kind, a strong `ETag`, `Last-Modified` and `Accept-Ranges: bytes` (SC-004).
- [ ] T058 [US2] Add `"ServePublicFileController.GetAsync"` to `AnonymousAllowList` in `tests/QuickBuilder.ArchitectureTests/AuthorizationDeclarationTests.cs`. Add an assertion to `ITESTS/Infrastructure/EndpointCatalog.cs` (or its test) that `/_media/{file}` is the only new anonymous endpoint.

### Implementation for User Story 2

- [ ] T059 [US2] Add `PublicMedia = "/_media"` to `src/QuickBuilder.Host/Routing/RoutePrefixes.cs` as a separate `PublicReservedPrefixes` list. It isn't part of `IsReserved`, so `SiteResolutionMiddleware` still resolves the site. The remarks must document that no page may claim it (FR-015, M-11). Add a routing test in `ITESTS/Startup/StartupTests.cs` asserting that `/_media/...` is site-resolved and handled by the media controller, not by the public catch-all.
- [ ] T060 [US2] Implement `MEDIA/Features/ServePublicFile/ServePublicFile.cs`:
  - **Request**: `ServePublicFileQuery(FileId, Extension)`.
  - **Handler**:
    - reads through the site-filtered `MediaDbContext` (no `AcrossAllSites`), so `ISiteContext` decides the result;
    - requires the stored extension to equal the requested one;
    - opens the stored file;
    - returns the same `media.not_found` for every miss, including missing bytes, and logs missing bytes at warning level with the file id and site id.
  - **Controller**: `[AllowAnonymous]` with the comment "Public surface: site visitors never authenticate (002 contracts/authorization.md §4)". The route is `[HttpGet("/_media/{file:regex(^[[0-9a-f]]{{32}}\\.[[a-z0-9]]{{1,8}}$)}")]` plus `[HttpHead]`, and `file` is parsed into id and extension. It applies `MediaResponseHeaders` with `Public` and returns `File(..., enableRangeProcessing: true)`.
  - **Registration**: in `MEDIA/MediaModule.cs`.
- [ ] T061 [US2] Update `specs/002-identity-setup/contracts/authorization.md` §4, adding the row `GET|HEAD /_media/{file}`: "Site visitors never authenticate; justified by 003 FR-026". Also add a §2 note pointing to the media capabilities in `specs/003-media-storage/research.md` M-18.

**Checkpoint**: US1 + US2 make the MVP. Files are uploaded, listed and safely served on their own site only.

---

## Phase 5: User Story 3 - Site members browse and delete media (Priority: P2)

**Goal**: page through the library, copy public addresses, see unavailable files, and permanently delete files. Editors can delete only their own uploads.

**Independent Test**: with more than one page of files, page through and check each appears once. Copy an address and open it. Delete a file and confirm it is gone from the list and its address is `404` at once (quickstart §5).

**Depends on**: US1 (library and list) and US2 (public addresses for copy and the `404` check).

### Tests for User Story 3 ⚠️ write first, confirm they fail

- [ ] T062 [P] [US3] Write `ITESTS/Media/DeleteFileTests.cs`, covering the capability matrix from M-18:
  - an Administrator or Designer deletes anyone's file: `204`;
  - an Editor deletes their own file: `204`;
  - an Editor deletes another user's file: `403 identity.forbidden`, and the file is unchanged (US3 #5);
  - a user with no role on the site gets `404 sites.not_found`;
  - an Administrator deleting under a random site id gets `404 sites.not_found`;
  - an unknown file id, or another site's file id, gets `404 media.not_found`;
  - after `204`, the row is gone, the stored object is gone, and the public address and preview both return `404`;
  - deleting twice gives `404` the second time;
  - a failing `DeleteAsync` (a decorator that throws) still returns `204`, and the row is gone.
- [ ] T063 [P] [US3] Write `ITESTS/Media/DeleteAcrossInstancesTests.cs`, using two `QuickBuilderApp` instances on one database and one `storagePath`, following `ITESTS/Identity/RevocationTests.cs`. Upload on A, fetch publicly on B (`200`), delete on A, and the very next public GET on B returns `404` (SC-007).
- [ ] T064 [P] [US3] Extend `ITESTS/Media/ListFilesTests.cs`:
  - an object removed from storage by hand makes that item `available: false` while the listing still succeeds;
  - an Editor's listing marks `canDelete` true only on their own uploads, and a Designer's marks it true on all.
- [ ] T065 [P] [US3] Extend `src/admin/tests/mediaLibraryView.spec.ts`:
  - "Load more" appends the next page and moves focus to the first new row;
  - the Delete button appears only where `canDelete` is true;
  - the confirmation dialog traps focus, and Escape cancels;
  - "Copy address" writes `location.protocol//{first host}{publicPath}` to the clipboard (mocked);
  - a site without host names shows the explanation (US3 #4) and no copy action;
  - an unavailable file shows its label;
  - axe finds no violations.

### Implementation for User Story 3

- [ ] T066 [US3] Implement `MEDIA/Features/DeleteFile/DeleteFile.cs`:
  - **Request**: `DeleteFileCommand(SiteId, FileId, UserId)`.
  - **Handler**:
    1. Confirm the site exists through `ISiteDirectory` (else `sites.not_found`, as in T047). Read the row across sites for `siteId`; if it is missing, return `media.not_found`.
    2. If `UploadedBy != UserId` and `!IAccessEvaluator.HasCapabilityAsync(MediaCapabilities.Delete, siteId)`, return a forbidden error mapped to `403 identity.forbidden` in the 002 problem shape.
    3. Remove the row and commit.
    4. Call `IFileStorage.DeleteAsync` in a try/catch that logs a warning with the file id and site id and doesn't rethrow (M-13).
  - **Controller**: `DELETE api/v1/media/sites/{siteId:guid}/files/{fileId:guid}` with `[RequireCapability(MediaCapabilities.DeleteOwn)]`. It returns `204`.
  - **Registration**: in `MEDIA/MediaModule.cs`.
- [ ] T067 [US3] Refine `MEDIA/Features/ListFiles/ListFiles.cs` and `MEDIA/Infrastructure/MediaFileMapper.cs`:
  - `available` comes from `IFileStorage.ExistsAsync` for the page's items, at most eight at a time. Log one warning per listing with the ids of missing files.
  - `canDelete` is `true` when the caller is the uploader and holds `media.own_files.delete`, or holds `media.files.delete`. Each capability is evaluated once per request through `IAccessEvaluator`.
- [ ] T068 [P] [US3] Create `src/admin/src/features/media/DeleteDialog.vue`: a modal confirmation naming the file (as text), stating that deletion is permanent, with focus trapped, Escape to cancel, and focus returned to the triggering row. It calls `deleteFile(siteId, fileId)`, which is added to `src/admin/src/api/media.ts`.
- [ ] T069 [US3] Extend `src/admin/src/features/media/MediaList.vue` and `src/admin/src/features/media/MediaLibraryView.vue`:
  - a "Load more" button using `nextCursor`, which focuses the first new row;
  - a Delete action only where `canDelete`, opening `DeleteDialog` and removing the row on success;
  - a "Copy address" action, shown when the site has host names, that uses `navigator.clipboard` and announces the result;
  - an "unavailable" label for `available: false`;
  - the no-host-name explanation, reading host names from `src/admin/src/app/sitesStore.ts`.
- [ ] T070 [US3] Add the US3 strings to `src/admin/src/i18n/en.json`: load more, delete, the confirmation title and body, the permanent-deletion warning, copy address, copied, the unavailable label, and the no-host-name explanation

**Checkpoint**: the library is fully manageable, and the deletion rules hold on the server.

---

## Phase 6: User Story 4 - Operator chooses and relies on where media is stored (Priority: P2)

**Goal**: media works with zero configuration on the reference deployment, survives image replacement, is cleaned of orphans automatically, and bad storage configuration stops startup with a clear message.

**Independent Test**: on the reference deployment, upload, replace the image and confirm every file survives. Set an unwritable path and an unknown provider, and confirm each stops startup with a message naming the key (quickstart §6–§7).

### Tests for User Story 4 ⚠️ write first, confirm they fail

- [ ] T071 [P] [US4] Write `ITESTS/Startup/StorageStartupValidationTests.cs`, starting `QuickBuilderApp` with each bad setting from quickstart §6: `Storage:Provider=S3`, a relative path, a path under `AppContext.BaseDirectory`, an unwritable path (skipped on Windows), and `Media:AllowedTypes:0=image/svg+xml`. Each must throw `OptionsValidationException` on start, with a message naming the key (SC-010).
- [ ] T072 [P] [US4] Write `ITESTS/Media/UpgradeFrom002Tests.cs`, modelled on `ITESTS/Identity/UpgradeFrom001Tests.cs`:
  1. Build a 002 snapshot by applying only the Sites and Identity migrations.
  2. Seed sites, users and grants.
  3. Start the app so that all migrations apply.
  4. Assert that the seeded data is unchanged, that schema `media` exists, that every site's library is empty, and that an upload then works (SC-005's database half).
- [ ] T073 [P] [US4] Write `ITESTS/Media/MediaOrphanSweepTests.cs`, using `ManualClock`:
  - an object with no row, older than the grace period, is deleted;
  - an object with no row that is younger is kept;
  - an object with a row is never touched;
  - objects under two sites are each handled, and one log entry per site carries the `SiteId`;
  - leftovers from a failed insert whose best-effort delete also fails (a decorator that makes `DeleteAsync` throw), and from T062's failed post-delete purge, are gone after one sweep past the grace period;
  - two sweeps running concurrently both succeed (SC-006, FR-012).

### Implementation for User Story 4

- [ ] T074 [US4] Implement `MEDIA/Infrastructure/MediaOrphanSweepService.cs`, a `BackgroundService` modelled on `IdentityRetentionService` (M-15):
  - runs at startup, then hourly on a `PeriodicTimer(TimeProvider)`;
  - exposes `SweepAsync()` for tests;
  - streams `ListAsync("")` once, keeping only keys shaped `{32 hex}/media/{32 hex}`. In batches of 1,000, it loads the batch's existing keys with `AcrossAllSites("Background sweep compares stored objects with every site's rows (M-15).")` and `.Where(f => keys.Contains(f.StorageKey))`;
  - deletes objects with no row whose `LastModifiedUtc` is older than `now - OrphanGracePeriod`;
  - totals deletions by the key's first segment and logs one entry per `SiteId`.

  Register it in `MEDIA/MediaModule.cs`.
- [ ] T075 [US4] Add a dedicated test for the sweep's `AcrossAllSites` call in `ITESTS/Media/MediaOrphanSweepTests.cs`, following the pattern of `ITESTS/Sites/SiteScopingTests.cs` for annotated bypasses (001 FR-013a). Then check that the architecture test for bypass reasons passes.
- [ ] T076 [US4] Update `Dockerfile`'s runtime stage, before `USER app`:
  - `RUN mkdir -p /var/lib/quickbuilder/media && chown app:app /var/lib/quickbuilder/media`;
  - `ENV Storage__Local__Path=/var/lib/quickbuilder/media`;
  - `VOLUME /var/lib/quickbuilder/media`, with a comment explaining why it must be persistent (M-06).
- [ ] T077 [US4] Update `docker-compose.yml`:
  - mount the named volume `media-data:/var/lib/quickbuilder/media` on `app`;
  - declare `media-data` under `volumes:`;
  - add a comment at the mount saying that media must be backed up with `db-data`, and that custom deployments must mount persistent storage at this path.
- [ ] T078 [P] [US4] Add **Storage** and **Media** sections to `docs/configuration.md` covering all five keys, their defaults, their validation and the failure message for each. Document `/_media` as a reserved path on every site host, and that `/health/ready` now includes storage (M-22).
- [ ] T079 [P] [US4] Add a **Media storage** section to `docs/operations.md`:
  - where files live (the volume path, and the per-user default outside containers);
  - backing up and restoring with the database, and what happens if the two are restored from different moments (unavailable entries; bytes swept after the grace period);
  - moving an installation;
  - that deleted files may persist in browser and proxy caches;
  - the multi-instance caveat (E-4; every instance would need the same directory);
  - the upgrade notes from plan.md "Operator impact".

**Checkpoint**: all four stories are complete and independently testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: the smoke test, performance, privacy, offline and documentation checks, which span every story.

- [ ] T080 Extend `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs` (FR-032, SC-001, SC-005):
  1. After signing in and creating the site, upload a PNG through the API.
  2. Fetch `/_media/{id}.png` with the site's `Host` header and compare the bytes.
  3. Rebuild the image and restart `app` with the volumes kept (`ComposeDeployment` helpers).
  4. Fetch again and compare.
- [ ] T081 [P] Add `tests/QuickBuilder.SmokeTests/MediaPerformanceTests.cs` (`[Trait("Category","Performance")]`, run under `docker-compose.perf.yml`), with three tests:
  - a 10 MB upload in ≤ 5 s, and with 10 × 20 MiB uploads in flight, the p95 of `GET /api/v1/sites` ≤ 2× its idle p95 (SC-008);
  - 50 concurrent GETs of a 1 MB image with a p95 time to first byte ≤ 100 ms (SC-009);
  - with 10,000 rows (and matching stored objects) seeded directly into the database and storage volume, the first, middle and last library pages each load through the API in ≤ 1 s. The API page time stands in for the screen's load time (SC-009a).
- [ ] T082 [P] Extend `ITESTS/Observability/LoggingTests.cs`: run the upload, list, preview, public-serve, delete and sweep journeys with canary file names (T032), and assert that no captured log event renders a canary marker (SC-012, FR-030)
- [ ] T083 [P] Extend `ITESTS/Observability/NoEgressTests.cs` to run the upload, public serve and sweep steps and assert zero outbound connections (FR-031)
- [ ] T084 [P] Add Media and `Shared.Infrastructure/Storage` to the critical paths in the coverage gate step of `.github/workflows/ci.yml` (Principle X, 80%). Check that the merged report includes the new assemblies.
- [ ] T085 [P] Add a "Storing files" section to `docs/contributing.md`, pointing to `specs/003-media-storage/contracts/storage-provider.md`: generated keys only, site-first prefixes, never touching the file system directly, and passing the conformance suite for a new provider
- [ ] T086 [P] Add the i18n coverage check and the keyboard-only journey for the media screen to `src/admin/tests/mediaLibraryView.spec.ts`: every visible string resolves through the lookup, and upload, load more, copy and delete work without a pointer (SC-013, FR-028)
- [ ] T087 Run quickstart §1–§10 end to end on the reference deployment, including the manual Chromium and Firefox polyglot and PDF check in §4. **That §4 step is a merge gate**: it is the only verification of SC-004 for PDF. Record the browser versions and the outcome, and don't open the PR if it fails (plan.md Risks). Record the results in `specs/003-media-storage/quickstart-results.md`, in the format of `specs/002-identity-setup/quickstart-results.md`, and file any failure as a bug with a failing test first (Principle X).
- [ ] T088 Run `dotnet format --verify-no-changes`, `dotnet build -warnaserror`, the full test suite, and `npm run lint && npm run test && npm run build` in `src/admin/`, and fix every finding before opening the PR that closes #3

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. It blocks every story.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational. Its tests upload through US1's endpoint (T046), so run it after T046.
- **US3 (Phase 5)**: depends on US1 (list and mapper) and US2 (public addresses).
- **US4 (Phase 6)**: depends on Foundational. T073's orphan-sweep cases reuse T040 and T062's failure scenarios, and the Docker and docs tasks (T076–T079) have no code dependency.
- **Polish (Phase 7)**: depends on every story it exercises.

### User Story Dependencies

```text
Setup ─▶ Foundational ─┬─▶ US1 ─┬─▶ US2 ─▶ US3 ─┐
                       │        └───────────────┤
                       └─▶ US4 (T076–T079 any time; T073–T075 after T040/T062) ─┴─▶ Polish
```

### Within Each User Story

- Tests are written first and must fail.
- Domain and infrastructure come before slices, slices before UI, and UI before strings.
- Every slice's DI registration is added to `MEDIA/MediaModule.cs` in the same task as the slice.

### Parallel Opportunities

- **Setup**: T002, T003, T005 and T006.
- **Foundational**:
  - T007–T009, T011, T018–T022 and T025–T028 are independent files.
  - T015, T016, T034, T035 and T036 are independent test files.
  - T032 and T033 are harness files.
- **US1**: tests T038–T043 in parallel; T049, T051 and T052 in parallel once T046 and T047 exist.
- **US2**: T055–T057 in parallel.
- **US3**: T062–T065 in parallel; T068 alongside T066.
- **US4**: T071–T073, T078 and T079 in parallel.
- **Polish**: T081–T086 in parallel.

---

## Parallel Example: User Story 1

```text
# Tests first, all at once:
T038 UploadFileTests.cs        T039 UploadValidationTests.cs   T040 UploadConsistencyTests.cs
T041 ListFilesTests.cs         T042 PreviewTests.cs            T043 mediaLibraryView.spec.ts

# Then the backend slices (T044 → T045 → T046 → T047, T048), then the UI together:
T049 api/media.ts              T051 UploadArea.vue             T052 MediaList.vue
```

## Parallel Example: User Story 4

```text
T071 StorageStartupValidationTests.cs   T072 UpgradeFrom002Tests.cs   T073 MediaOrphanSweepTests.cs
T078 docs/configuration.md              T079 docs/operations.md
```

---

## Implementation Strategy

### MVP first (US1 + US2)

1. Phase 1 and Phase 2. The conformance suite must be green before any slice exists.
2. US1: uploads are validated, stored and listed with previews.
3. US2: public serving with isolation and safety headers.
4. **Stop and validate** quickstart §1–§4. That is a demoable media library whose files
   are usable on the site.

US1 alone is demoable inside the admin shell, but it doesn't deliver the constitution's
"never executed or served from an executable path" end to end. Ship US1 and US2
together.

### Incremental delivery

1. Add US3 (browse, copy and delete), then validate quickstart §5.
2. Add US4 (operator persistence, sweep and docs), then validate quickstart §6–§7.
3. Do the Polish phase, then open the PR that closes #3.

### Parallel team strategy

After Foundational:
- Developer A: US1, then US3 (the same slices and screen).
- Developer B: US4's sweep, Docker and docs, then US2 once T046 lands.

---

## Notes

- `[P]` means different files with no dependency on an incomplete task.
- The feature ships whole in one PR, so no feature flag is needed (plan.md, Principle XI).
- Never log a file name. Tests use canary names so a slip fails the build (T082).
- Don't add a package. Research rejected every candidate. A new one needs a plan
  amendment with its licence (Principle XI).
- Commit after each task or logical group, and stop at any checkpoint to validate a story
  on its own.
