---

description: "Task list for 012-publishing-rendering"
---

# Tasks: Publishing and Rendering

**Input**: Design documents from `/specs/012-publishing-rendering/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden-file tests for rendering every block type;
  - at least 80% coverage on the publish and render paths.
- The spec measures behaviour through automated checks: SC-002, SC-003, SC-004, SC-005,
  SC-006, SC-007, SC-008 and SC-009.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US7 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US7)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `EDITOR` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `MEDIAC` = `src/Modules/Media/QuickBuilder.Media.Contracts`
- `REND` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `RENDC` = `src/Modules/Rendering/QuickBuilder.Rendering.Contracts`
- `SHARED` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `PUBUI` = `src/admin/src/features/publishing`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`
- `FIX` = `tests/fixtures/rendering`

**Conventions**:
- **Backend slices** follow 001's one-folder-per-slice rule. Every controller action:
  - declares exactly one `[RequireCapability(...)]` (research PR-18);
  - checks the site with `ISiteDirectory.FindByIdAsync` first, returning `sites.not_found`;
  - refuses through `PagesErrorResults.cs` or `EditorErrorResults.cs` with the codes in
    [data-model.md §6](./data-model.md).
- **Publishing writes** happen only through the `Publication` aggregate, in one
  `PagesDbContext` transaction, with the `xmin` retry of research PR-03.
- **Renderers** write only through `HtmlWriter`. No renderer builds HTML by string
  concatenation from content (PR-09).
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task that uses them. `npm run lint` enforces this.
- **Logs** carry ids and numbers only (FR-044).

**References**:
- research decisions: `PR-xx`;
- tables and rules: [data-model.md](./data-model.md);
- endpoints: [contracts/publishing-api.yaml](./contracts/publishing-api.yaml);
- public output: [contracts/public-rendering.md](./contracts/public-rendering.md);
- readers and events: [contracts/readers.md](./contracts/readers.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the new Rendering module's projects, and build and CI
wiring. Nothing here has behaviour.

- [X] T001 Commit `specs/012-publishing-rendering/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `012-publishing-rendering`, referencing #12 (plan.md "Development Workflow").
- [X] T002 Create the two Rendering projects and add both to `QuickBuilder.slnx`:
  - `RENDC/QuickBuilder.Rendering.Contracts.csproj`, referencing only `src/Shared/QuickBuilder.Shared.Kernel` and `src/Modules/Sites/QuickBuilder.Sites.Contracts` (for `SiteDto`);
  - `REND/QuickBuilder.Rendering.csproj`, referencing `RENDC`, `Shared.Kernel`, `Shared.Infrastructure`, and the Contracts projects of Sites, Identity, Pages, Editor and Media. Copy the property groups and `InternalsVisibleTo` entries from `EDITOR/QuickBuilder.Editor.csproj`. Add an `EmbeddedResource` for `../../../admin/src/editor/blocks/blocks.css` with `LogicalName` `QuickBuilder.Rendering.blocks.css` (PR-10).
- [X] T003 [P] Add `ProjectReference`s to both Rendering projects in `ITESTS/QuickBuilder.IntegrationTests.csproj`, `UTESTS/QuickBuilder.UnitTests.csproj` and `ATESTS/QuickBuilder.ArchitectureTests.csproj`. Add `REND` to `HOST/QuickBuilder.Host.csproj`.
- [X] T004 [P] Add the Rendering module and contracts assemblies to the lists in `ATESTS/ModuleBoundaryTests.cs`. Add two rules:
  - `QuickBuilder.Rendering` references no module implementation project and declares no `DbContext` subclass;
  - `QuickBuilder.Rendering.Contracts` references only `Shared.Kernel`, `Sites.Contracts` and the BCL.
- [X] T005 [P] Update `Dockerfile`:
  - in the restore layer, add `COPY` lines for `src/Modules/Rendering/QuickBuilder.Rendering.Contracts/*.csproj` and `src/Modules/Rendering/QuickBuilder.Rendering/*.csproj`;
  - in the build stage, before `dotnet publish`, add `COPY src/admin/src/editor/blocks/blocks.css src/admin/src/editor/blocks/blocks.css`, with a comment citing PR-10.
- [X] T006 [P] Register the module in `HOST/Program.cs`: `builder.Services.AddRenderingModule(builder.Configuration)` after `AddEditorModule`, and `.AddApplicationPart(typeof(RenderingModule).Assembly)`. Create `REND/RenderingModule.cs` with an empty `AddRenderingModule` so that it compiles.
- [X] T007 [P] Add `QuickBuilder.Rendering` to the per-package coverage loop in `.github/workflows/ci.yml`, with a comment "012: the renderer and the public page path are critical paths" (Principle X).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the reshaped cache, read-only connections and delayed outbox messages;
- all new tables and migrations;
- the capabilities and error codes;
- the read-only readers;
- the renderer's core (writer, defaults, token CSS, dispatch, page shell);
- the not-found hand-off;
- the admin publishing client.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Shared infrastructure (tests first)

- [X] T008 [P] Create `UTESTS/Caching/MemoryCacheProviderTests.cs`:
  - an entry larger than the remaining size limit evicts the least recently used entries;
  - ten concurrent `GetOrCreateAsync` calls for one missing key run the factory once (single-flight);
  - sliding expiry renews on read;
  - keys from `CacheKey.ForSite` of two sites never collide.
- [X] T009 Reshape `SHARED/Caching/ICacheProvider.cs` as contracts/readers.md §2 describes:
  - add `CacheEntrySettings(long Size, TimeSpan? Sliding, TimeSpan? Absolute)`;
  - the `MemoryCacheProvider` owns a `MemoryCache` with `SizeLimit` from `Cache:MemoryLimitMegabytes`;
  - add per-key single-flight through a `ConcurrentDictionary<string, Lazy<Task<object?>>>` that is removed on completion.

  Add `SHARED/Caching/CacheOptions.cs` (`[Range(16, 16384)]`, default 256), bind it with `ValidateOnStart` in `HOST/Program.cs`, and add `"Cache": { "MemoryLimitMegabytes": 256 }` to `HOST/appsettings.json` and the key to `docs/configuration.md`. Rewrite the remarks, which name E-4 as open, to cite PR-05 and PR-07. Make T008 pass.
- [X] T010 [P] Create `SHARED/Data/ReadOnlyConnection.cs` with `static string From(string connectionString)`. It uses `NpgsqlConnectionStringBuilder` to append `-c default_transaction_read_only=on` to `Options`, keeping any existing options (PR-06). Add `ITESTS/Infrastructure/ReadOnlyConnectionTests.cs`: an `INSERT` through a connection built from it fails with SQLSTATE `25006`, and a `SELECT` succeeds.
- [X] T011 [P] Let an outbox message be added with a future `AvailableAtUtc` (PR-14):
  - add an optional `availableAtUtc` parameter to the message factory in `SHARED/Outbox/OutboxMessage.cs`;
  - add `ITESTS/Outbox/DelayedMessageTests.cs`: a message available in the future is not claimed by `OutboxDispatcher.RunOnceAsync`, and it is claimed once its time has passed. Set `available_at_utc` directly in the test to move time.

### Tables, migrations, capabilities and errors

- [X] T012 Add the Pages publishing entities with their fields, no behaviour yet, following [data-model.md §1](./data-model.md):
  - `PAGES/Domain/Publication.cs`, `PublishedVersion.cs`, `PublishSchedule.cs`, `PublishLogEntry.cs` and `LiveDependency.cs`, all `ISiteOwned`.

  Configure them in `PAGES/Infrastructure/PagesDbContext.cs`:
  - composite keys, `xmin` on `publications`, cascades to `pages` and `components`;
  - `ux_published_versions_subject_number`, the partial unique `ux_publish_schedules_pending`, `ix_live_dependencies_target`, `ix_publish_log_site_time` and `ix_publish_log_subject`;
  - no foreign key from `publish_log`;
  - site query filters.

  Generate `PAGES/Infrastructure/Migrations/<ts>_AddPublishing.cs` with `dotnet ef migrations add AddPublishing`.
- [X] T013 [P] Add `EDITOR/Domain/DesignPublication.cs` and `EDITOR/Domain/DesignVersion.cs` ([data-model.md §2](./data-model.md)). Configure them in `EDITOR/Infrastructure/EditorDbContext.cs`, and generate `EDITOR/Infrastructure/Migrations/<ts>_AddDesignPublishing.cs`.
- [X] T014 [P] Add `PAGESC/Publishing/PublishingCapabilities.cs` with `Manage = "pages.publishing.manage"` and `LogView = "pages.publish_log.view"`. Register both in `PAGES/PagesCapabilities.cs`: `Manage` is granted to `[Designer]`, and `LogView` to `[]` (Administrator only) (PR-18). Update `specs/002-identity-setup/contracts/authorization.md` §2 with both.
- [X] T015 [P] Add the refusal codes of [data-model.md §6](./data-model.md) to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs`, including the `componentIds` and `pageCount` extension members, and `editor.out_of_date` and `editor.design_version_not_found` to `EDITOR/EditorErrors.cs` and `EDITOR/EditorErrorResults.cs`.

### Read-only readers (PR-06)

- [X] T016 [P] Add `PAGESC/Publishing/IPublishedContentReader.cs` and its records (contracts/readers.md §1.1).
  - Implement `PAGES/Infrastructure/PublishedReadDbContext.cs`: maps `pages`, `publications`, `published_versions` and `components` read-only; the connection comes from `ReadOnlyConnection.From`; `QueryTrackingBehavior.NoTracking`; `SaveChanges` and `SaveChangesAsync` throw.
  - Implement `PAGES/Infrastructure/PublishedContentReader.cs`, with `FindLiveByPathAsync` as an EF compiled query on `(site_id, path)`.
  - Register both in `PAGES/PagesModule.cs`.
- [X] T017 [P] Add `EDITORC/IPublishedDesignReader.cs` and `PublishedDesign` (contracts/readers.md §1.2). Implement `EDITOR/Infrastructure/PublishedDesignReadDbContext.cs` and `PublishedDesignReader.cs`, returning `DefaultDesignTokens` with number 0 when no `design_publications` row exists. Register them in `EDITOR/EditorModule.cs`.
- [X] T018 [P] Add `MEDIAC/IPublicMediaReader.cs` and `PublicMediaFile` (contracts/readers.md §1.3). Implement `MEDIA/Infrastructure/PublicMediaReader.cs` over a read-only context, building paths with `MediaFileMapper.PublicPath` and returning only available files. Register it in `MEDIA/MediaModule.cs`.
- [X] T019 Create `ITESTS/Rendering/PublicPathReadOnlyTests.cs` (Principle VII):
  - each of the three readers' contexts refuses a write in PostgreSQL (`25006`), and `SaveChanges` throws;
  - each reader returns nothing for another site's ids.

### Rendering core

- [X] T020 [P] Add `RENDC/IPublicNotFoundPage.cs`, `RENDC/PublicResponse.cs` and `RENDC/RenderingRoutes.cs` (`AssetsPrefix = "/_assets"`) (contracts/readers.md §1.4). In `HOST/Routing/RoutePrefixes.cs`, add `RenderingRoutes.AssetsPrefix` to `PublicReserved`. Extend `ITESTS/Pages/ReservedAddressCoverageTests.cs` so that `/_assets` is accepted as a prefix the slug grammar can't produce.
- [X] T021 Route the public not-found answer through `IPublicNotFoundPage` (PR-08):
  - `HOST/Middleware/PublicResponses.cs` gains `WritePublicNotFoundAsync(HttpContext, SiteDto)`, which writes the `PublicResponse`;
  - `HOST/Middleware/SiteResolutionMiddleware.cs` and `AdminHostConfinementMiddleware.cs` pass the resolved site;
  - `REND/Features/NotFound/NotFoundPage.cs` initially returns the existing JSON `public.not_found` bytes unchanged, and US7 replaces it.

  Existing 009 tests must stay green.
- [X] T022 [P] Create `REND/Rendering/HtmlWriter.cs` over `ArrayBufferWriter<byte>`:
  - `Raw(ReadOnlySpan<char>)` is internal and used for constant markup only;
  - `Text` uses `HtmlEncoder`, `Attribute` uses attribute encoding, and `UrlAttribute` validates the scheme before encoding.

  Add `UTESTS/Rendering/HtmlWriterTests.cs`: `<script>`, quotes, `&`, astral characters, and a refused `javascript:` URL.
- [X] T023 [P] Create `REND/Domain/BlockDefaults.cs`, which reads every `$defs` block's property `default` from the embedded `v1.schema.json` of `QuickBuilder.Pages.Contracts` once at startup (PR-09). Add `UTESTS/Rendering/BlockDefaultsTests.cs`: it asserts the Section `width` default `normal` and one token-ref default per block type.
- [X] T024 [P] Create `FIX/token-css/*.json` (the default set, a custom set, and a set without content widths), each with `{ tokens, expected }` where `expected` is the custom-property map `tokenCss.ts` produces today.
  - Port the logic to `REND/Domain/TokenCss.cs`.
  - Add `UTESTS/Rendering/TokenCssParityTests.cs` and `ADMINTESTS/editor/tokenCss.parity.spec.ts`, both running every fixture (PR-10).
- [X] T025 [P] Create the renderer core:
  - `REND/Rendering/Blocks/IBlockRenderer.cs`;
  - `REND/Domain/NodeContext.cs` (the node, resolved defaults, token lookup with fallback, and a per-node style collector keyed by node id);
  - `REND/Domain/RenderInputs.cs`;
  - `REND/Rendering/PageRenderer.cs`, which walks the document, dispatches by `type` through the registered renderers, skips unknown types, and dispatches on `schemaVersion` with `SupportedSchemaVersions = [1]`;
  - `REND/Rendering/PageShell.cs`, which writes public-rendering.md §3's shell: the `lang`, the title, the stylesheet link, one `<style>` holding the token map on `.qb-page` and the collected `[data-qb]` rules, and the `qb-canvas qb-page` wrapper. It returns the SHA-256 of the style block.
- [X] T026 [P] Create `UTESTS/Rendering/BlockRendererCoverageTests.cs`: every node type in `v1.schema.json` has exactly one registered `IBlockRenderer`, and no renderer names an unknown type. Create `UTESTS/Rendering/RendererSchemaVersionsTests.cs`: every `CONTRACTS/page-schema/v*.schema.json` is in `PageRenderer.SupportedSchemaVersions` (FR-029). Both fail until US1 and US2 add the renderers.
- [X] T027 [P] In `ADMIN/editor/blocks/blocks.css`, add `.qb-page { container-type: inline-size; container-name: page; }` inside the `qb-reset` layer, with a comment citing page-schema.md §9 (PR-10).

### Admin foundations

- [X] T028 [P] Create `ADMIN/api/publishing.ts`: typed functions and types for every operation in contracts/publishing-api.yaml (`Publication`, `Schedule`, `SubjectHistory`, `VersionContent`, `LogEntry`, `DesignPublication`). Add the `publication` field to the page tree and component types in `ADMIN/api/pages.ts`, and to the design token type in `ADMIN/api/editor.ts`.
- [X] T029 [P] Create `PUBUI/errorMessages.ts` (every `pages.*` and `editor.*` code of data-model §6 → an i18n key, with `componentIds` and `pageCount` interpolated) and `PUBUI/PublicationBadge.vue` (Draft only, Published, Published — changes, Scheduled, Schedule failed), with keys in `ADMIN/i18n/en.json`.

**Checkpoint**: migrations apply, readers work and are read-only, and the renderer core compiles. The coverage tests of T026 are expected to fail until the renderers exist.

---

## Phase 3: User Story 1 - Publish a page and visitors see it at its address (Priority: P1) 🎯 MVP

**Goal**: a site member publishes a page, and its address serves complete server-rendered
HTML from the live version, with every block type. Draft changes stay private until the
next publish.

**Independent Test**: quickstart §1. Build a page with all ten block types, publish it, and
`GET` its address: complete HTML that matches the golden output and works with scripts off.
Change the draft: the public page is unchanged. Publish again: the change appears.

### Tests for User Story 1 ⚠️

- [X] T030 [P] [US1] Create `UTESTS/Pages/Publishing/PublicationTests.cs` for the aggregate's publish:
  - the first publish creates number 1, the next number 2;
  - the live pointer moves;
  - `published_from_draft_version` is set;
  - `HasUnpublishedChanges` is false after publish and true after a draft or title change;
  - a stale draft version is refused (`pages.out_of_date`).
- [X] T031 [P] [US1] Create `ITESTS/Publishing/PublishApiTests.cs`:
  - publish returns `Publication`;
  - an invalid draft gives `400 pages.invalid_document`, and nothing changes;
  - a stale `draftVersion` gives `409 pages.out_of_date`;
  - two concurrent publishes both succeed with consecutive numbers (PR-03);
  - the page tree shows `publication.state = published` and `hasUnpublishedChanges`;
  - a `publish` log entry and a `Pages.PagePublished` outbox row are written in the same transaction.
- [X] T032 [P] [US1] Create `ITESTS/Rendering/PublicRenderingTests.cs` (public-rendering.md §1–§3):
  - a published page on its host gives `200` `text/html` with every header in §3;
  - `/About/` and `/about?x=1` serve `/about`;
  - `HEAD` returns the headers with no body;
  - `POST` gives `405` with `Allow`;
  - a draft-only page and an unknown path give the not-found response;
  - a page of site A is never served on site B's host;
  - a disabled site gives 009's JSON;
  - `..` and `%2e%2e` segments give not found;
  - no `<script>` and no `style=` appear in the body.

  Also create `ITESTS/Rendering/PublicRouteBoundaryTests.cs` (PR-08), so that the catch-all page route never takes over the administrative surface:
  - an admin deep link such as `/admin/sites/{id}/pages` still returns the admin shell (`MapFallbackToFile`);
  - `GET /api/v1/unknown` keeps the 404 it gives today;
  - `/health/live` still answers;
  - on a site host, `/about` reaches the page endpoint.
- [X] T033 [P] [US1] Create per-block renderer tests in `UTESTS/Rendering/Blocks/`:
  - `SectionRendererTests.cs`, `ContainerRendererTests.cs`, `SpacerRendererTests.cs` and `DividerRendererTests.cs`: classes, `data-qb` and collected custom properties per width, alignment and column pattern, and the defaults applied;
  - `HeadingRendererTests.cs` and `TextRendererTests.cs`: the level, encoding and line breaks;
  - `RichTextRendererTests.cs`: every allowed node and mark, nested lists, a page link to a live page, a page link to an unpublished page (text only), and an unknown node skipped.
- [X] T034 [P] [US1] Create `UTESTS/Rendering/Blocks/ImageRendererTests.cs`, `ButtonRendererTests.cs` and `EmbedRendererTests.cs`:
  - an available image, a decorative image (`alt=""`) and a missing image (left out);
  - a button with an external link, a new tab (`rel="noopener noreferrer"`), and a page link to a non-live page (a `span` with the same classes);
  - YouTube and Vimeo iframes with `loading="lazy"` and the privacy domains (PR-11).
- [X] T035 [P] [US1] Create `FIX/context.json`, a fixed design (the defaults), link targets and media for the golden run. Create `UTESTS/Rendering/RendererGoldenTests.cs`: for every file in `CONTRACTS/page-schema/examples/valid/`, render with `context.json` and compare byte for byte with `FIX/golden/<name>.html`. Set an environment variable `QB_UPDATE_GOLDEN=1` to rewrite the files; review every golden file by hand before committing.
- [X] T036 [P] [US1] Create `ADMINTESTS/editor/CanvasMarkupParity.spec.ts` (SC-008, PR-10): for each valid example document, mount the canvas with `@vue/test-utils` and parse `FIX/golden/<name>.html`. Normalise both: element names, `qb-*` classes, allow-listed attributes, and each node's custom properties (from `style` in the canvas, from the `[data-qb]` rules in the golden). Drop editor-only attributes. Assert equality, except for the documented differences in page-schema.md §9.
- [X] T037 [P] [US1] Create `UTESTS/Rendering/PathNormaliserTests.cs` (public-rendering.md §2) and `UTESTS/Rendering/PublishedPageJavaScriptBudgetTests.cs` (0 bytes of script in every golden page, failing above 100 KB; FR-028).
- [X] T038 [P] [US1] Create `ITESTS/Publishing/PublishingAuthorizationTests.cs` for page publish:
  - Designer gets `200`;
  - Editor gets `403 identity.forbidden`;
  - a member of another site gets `404 sites.not_found`;
  - Administrator gets `200`.

  Extend `ATESTS/AuthorizationDeclarationTests.cs` to cover the new controllers.
- [X] T039 [P] [US1] Create `ADMINTESTS/publishing/PublishMenu.spec.ts`: Publish is shown only with `pages.publishing.manage`, publishes with the current draft version, and shows the badge change and the **View live page** link. Axe passes, keyboard operation works, and all text resolves through i18n.

### Implementation for User Story 1

- [X] T040 [US1] Implement `Publication.Publish(...)` in `PAGES/Domain/Publication.cs`, following data-model §3.1 (the version insert, numbering, the live pointer, the log entry, a `PagePublishedDomainEvent`) and without dependencies or schedules, which US2 and US5 add. Make T030 pass.
- [X] T041 [US1] Add `PAGESC/Events/PagePublished.cs` (contracts/readers.md §3). Map the domain event in `PAGES/Infrastructure/PagesOutbox.cs`, and register the event type in `PAGES/PagesModule.cs`.
- [X] T042 [US1] Create the `PAGES/Features/PublishPage/` slice: `PublishPageController` (`POST …/pages/{pageId}/publish`, `[RequireCapability(PublishingCapabilities.Manage)]`), the request, and the handler. The handler:
  - loads the draft;
  - validates it with `IPageDocumentValidator`;
  - calls `Publication.Publish`;
  - saves with the `xmin` retry modelled on `PAGES/Infrastructure/TreeConcurrencyRetry.cs`;
  - returns `Publication`.

  Make T031 and T038 pass.
- [X] T043 [US1] Add the `publication` summary (data-model §1.6) to `PAGES/Features/GetPageTree/` and `PAGES/Features/GetPage/` through a projection in `PAGES/Infrastructure/PageReads.cs`, computing `hasUnpublishedChanges` as research PR-02 describes, and `liveUrl` from `SiteDto.BaseUrl`.
- [X] T044 [P] [US1] Create `REND/Rendering/Blocks/SectionRenderer.cs`, `ContainerRenderer.cs`, `SpacerRenderer.cs` and `DividerRenderer.cs`, matching the markup and classes of `ADMIN/editor/blocks/<type>/<Type>Block.vue`. Make their T033 tests pass.
- [X] T045 [P] [US1] Create `REND/Rendering/Blocks/HeadingRenderer.cs`, `TextRenderer.cs`, `RichTextBlockRenderer.cs` and `REND/Rendering/RichTextRenderer.cs` (the allow-list of PR-11). Make their T033 tests pass.
- [X] T046 [P] [US1] Create `REND/Rendering/LinkResolver.cs` (page targets from `RenderInputs`, `Hosting:BasePath` prefix, the plain-text fallback, and an external scheme re-check) and `REND/Rendering/Blocks/ImageRenderer.cs`, `ButtonRenderer.cs` and `EmbedRenderer.cs`. Make T034 pass.
- [X] T047 [US1] Create the `REND/Features/RenderPage/` slice:
  - `PathNormaliser.cs`;
  - `RenderPageController.cs`: `GET`/`HEAD {**path:publicPath}` with `Order = int.MaxValue`, and `405` for other methods. `publicPath` is a route constraint registered in `RenderingModule` that rejects paths under `/api`, `/admin` and `/health` (`RoutePrefixes.ReservedPrefixes`), so the admin SPA fallback and unknown API paths behave as before (PR-08). The action also requires `ISiteContext.HasSite`;
  - `RenderPageHandler.cs`: `FindLiveByPathAsync`, then build `RenderInputs` (the live page, the design from `IPublishedDesignReader.GetLiveAsync`, link targets and media collected in one pass over the document), then render with `PageRenderer` and `PageShell`, then write the headers of public-rendering.md §3 including the CSP with the style hash. Not found goes to `IPublicNotFoundPage`. No cache yet (US6).

  Register the renderers, the `publicPath` constraint and the services in `REND/RenderingModule.cs`. Make T032 (including `PublicRouteBoundaryTests`) and T037 pass.
- [X] T048 [P] [US1] Create the `REND/Features/ServeBlockStylesheet/` slice: load the embedded `blocks.css` once, compute the 12-character base64url SHA-256, and serve `GET`/`HEAD /_assets/blocks.{hash}.css` with the immutable caching headers. A wrong name gets `IPublicNotFoundPage` (public-rendering.md §5). Add a case to `PublicRenderingTests.cs`.
- [X] T049 [US1] Generate and review `FIX/golden/*.html` through T035's update mode. Make T035, T036 and T037 pass.
- [X] T050 [US1] Create `PUBUI/PublishMenu.vue`. Use it in `ADMIN/features/pages/PageTreeView.vue` with `PublicationBadge` per page. Mount `PublishMenu.vue` in `ADMIN/editor/EditorTopBar.vue` too, with the publication state and **Publish** as the primary button: it saves pending changes first, then publishes with the saved draft version, and refusals go through `errorMessages.ts`. Later stories add their actions (unpublish, schedule, history) to `PublishMenu.vue` once, so the page tree, the component list and the editor all offer them (FR-039). Add the i18n keys, and make T039 pass.

**Checkpoint**: quickstart §1 works, except component use (US2). This is the MVP: sites have visible pages.

---

## Phase 4: User Story 2 - Shared components and design are published once and appear on every page (Priority: P1)

**Goal**: components and the site's design are published on their own and appear on every
published page that uses them, with no republishing of pages.

**Independent Test**: quickstart §2, steps 1–3. Publish a header and three pages that use
it. Publish a header change: all three show it and no page was republished. Publish the
design: every page takes it.

### Tests for User Story 2 ⚠️

- [X] T051 [P] [US2] Create `UTESTS/Pages/Publishing/DependencyExtractorTests.cs`: a `component-ref`, a Button page link, rich-text page links in nested lists, Image media, duplicates collapsed, and external links ignored (data-model §1.3).
- [X] T052 [P] [US2] Create `ITESTS/Publishing/ComponentPublishApiTests.cs`:
  - publishing a component writes version 1;
  - publishing a page whose draft references unpublished components gives `409 pages.unpublished_components` with their ids;
  - unpublishing a component used by a live page gives `409 pages.component_in_use_by_published_pages` with `pageCount`;
  - deleting such a component is refused the same way;
  - after publishing a new component version, the page's public HTML shows it without the page being republished;
  - `live_dependencies` rows are replaced on each publish.
- [X] T053 [P] [US2] Create `UTESTS/Rendering/Blocks/ComponentRefRendererTests.cs`: the live component's blocks are rendered in place with no wrapper, and a component with no live version renders nothing.
- [X] T054 [P] [US2] Create `ITESTS/Publishing/DesignPublishApiTests.cs`:
  - before any publish, public pages use the default tokens and `GetLiveNumberAsync` returns 0;
  - publishing the design with a stale `version` gives `409 editor.out_of_date`;
  - after publishing, a public page's style block holds the new colour, without republishing the page;
  - saving draft tokens alone changes nothing public;
  - an `Editor.DesignPublished` outbox row is written;
  - Editor gets `403` and Designer `200`;
  - `POST …/design-tokens/rollback` publishes version N again as a new version with `kind = rollback`, leaves the draft token set unchanged, and gives `404 editor.design_version_not_found` for a version that isn't retained;
  - `GET …/design-tokens/versions` lists versions newest first with the live number, and `GET …/design-tokens/versions/{n}` returns the document;
  - 55 design publishes leave 50 versions, never removing the live one;
  - Editor can read the versions (`editor.design_tokens.view`) but gets `403` on rollback (constitution Principle X: every endpoint has integration tests).
- [X] T055 [P] [US2] Create `ADMINTESTS/publishing/DesignPublish.spec.ts` (**Publish design** in `DesignTokensView`, the unpublished-changes state, the version list, and rollback confirmation) and extend `PublishMenu.spec.ts` for the component list. Axe, keyboard operation and i18n are checked.

### Implementation for User Story 2

- [X] T056 [US2] Create `PAGES/Domain/DependencyExtractor.cs`. Extend `Publication.Publish` to replace the subject's `LiveDependency` rows, and `PublishPage` to refuse `pages.unpublished_components` (FR-003). Make T051 pass.
- [X] T057 [US2] Add `PAGESC/Events/ComponentPublished.cs` and `ComponentUnpublished.cs`, the outbox mapping, and the registration. Create the `PAGES/Features/PublishComponent/` and `PAGES/Features/UnpublishComponent/` slices. Unpublish checks for live dependents (`pages.component_in_use_by_published_pages`) and deletes the component's dependency rows.
- [X] T058 [US2] Extend `PAGES/Features/DeleteComponent/` to refuse a live component or one used by live pages (FR-004). Add the `publication` summary to `PAGES/Features/ListComponents/`.
- [X] T059 [US2] Create `REND/Rendering/Blocks/ComponentRefRenderer.cs`. Extend `RenderPageHandler` to load the live components with `GetLiveComponentsAsync`, and to collect their link targets and media into the same `RenderInputs`. Make T053 and T052's rendering case pass, and T026 pass completely.
- [X] T060 [US2] Add `EDITORC/Events/DesignPublished.cs` and `EDITOR/Domain/DesignPublication.Publish(...)`, with numbering, retention of 50 (PR-15) and the event. Create the `EDITOR/Features/PublishDesign/` slice (`POST …/design-tokens/publish`, `[RequireCapability(PublishingCapabilities.Manage)]`). Add the `publication` summary to `EDITOR/Features/GetDesignTokens/`.
- [X] T061 [US2] Create the `EDITOR/Features/RollbackDesign/`, `ListDesignVersions/` and `GetDesignVersion/` slices (publishing-api.yaml, Design publishing). Make T054 pass.
- [X] T062 [US2] Add the publishing menu and `PublicationBadge` to `ADMIN/features/pages/ComponentsView.vue`. Add **Publish design**, the design version list and **Roll back** with confirmation to `ADMIN/features/design/DesignTokensView.vue`. Add the i18n keys, and make T055 pass.

**Checkpoint**: quickstart §2, steps 1–3. All P1 publishing of pages, components and the design works.

---

## Phase 5: User Story 3 - View the publish history and roll back (Priority: P1)

**Goal**: every page and component has a history of versions and actions. A member rolls
back to a retained version, or copies it to the draft. Administrators see a site-wide
publish log that keeps entries for deleted pages.

**Independent Test**: quickstart §3 and §4, step 4. Publish three times, roll back to 1:
version 4 is live, the draft is unchanged, and the history shows all four. Delete an
unpublished page: its entries remain in the publish log.

### Tests for User Story 3 ⚠️

- [X] T063 [P] [US3] Extend `UTESTS/Pages/Publishing/PublicationTests.cs`:
  - a rollback creates a new version with `restored_from_number`, sets `published_from_draft_version` to null, and leaves the draft alone;
  - rolling back to a pruned number is refused;
  - retention keeps the 50 highest numbers plus the live version.
- [X] T064 [P] [US3] Create `ITESTS/Publishing/RollbackApiTests.cs`:
  - rollback for a page and for a component;
  - `pages.version_not_found`;
  - `pages.version_missing_components`, and `pages.unpublished_components` for a component that exists but is not live;
  - the public page shows the rolled-back content;
  - `GET …/history` returns the entries newest first with `retainedVersions`;
  - `GET …/versions/{n}` returns the document;
  - `POST …/copy-to-draft` replaces the draft through 010's rules, gives `409 pages.out_of_date` with a stale version, and changes nothing public;
  - 55 publishes leave 50 versions;
  - the history, version and copy-to-draft cases are repeated for components through `GetComponentHistory`, `GetComponentVersion` and `CopyComponentVersionToDraft`. The copy requires `pages.components.manage`, so Editor gets `403` (constitution Principle X).
- [X] T065 [P] [US3] Create `ITESTS/Publishing/PublishLogApiTests.cs`:
  - the log needs `pages.publish_log.view` (Administrator `200`, Designer `403`);
  - the filters by subject and user work, and the cursor paginates without gaps;
  - deleting an unpublished page keeps its entries with the last label and path, adds `deleted`, and sets `subjectExists = false`;
  - an `Editor.DesignPublished` event delivered twice creates one `design` entry.
- [X] T066 [P] [US3] Create `ADMINTESTS/publishing/HistoryView.spec.ts` (the list, **View** opening the read-only editor route, **Roll back** and **Copy to draft** with confirmations, and the error messages) and `ADMINTESTS/publishing/PublishLogView.spec.ts` (filters, paging, deleted subjects marked, shown only to Administrators). Axe, keyboard operation and i18n are checked.

### Implementation for User Story 3

- [X] T067 [US3] Implement `Publication.Rollback(...)` and version retention in `PAGES/Domain/Publication.cs` and `PAGES/Infrastructure/` (prune with `ExecuteDeleteAsync` in the same transaction; PR-15). Retention also applies to publish. Make T063 pass.
- [X] T068 [US3] Create the `PAGES/Features/RollbackPage/` and `RollbackComponent/` slices (`kind = rollback` on `PagePublished` and `ComponentPublished`), with the missing-component and unpublished-component checks.
- [X] T069 [P] [US3] Create the `PAGES/Features/GetPageHistory/`, `GetComponentHistory/`, `GetPageVersion/` and `GetComponentVersion/` slices, using `[RequireCapability(PagesCapabilities.ContentView)]`.
- [X] T070 [P] [US3] Create the `PAGES/Features/CopyPageVersionToDraft/` (`pages.page_drafts.edit`) and `CopyComponentVersionToDraft/` (`pages.components.manage`) slices, reusing `PAGES/Infrastructure/DraftWrites.cs` so that validation, `component_usages` and the out-of-date check stay 010's. Write the `copied_to_draft` log entry. Make T064 pass.
- [X] T071 [US3] Extend `PAGES/Features/DeletePage/` and `DeleteComponent/` to append a `deleted` log entry when the subject has history. Create the `PAGES/Features/GetPublishLog/` slice (`PublishingCapabilities.LogView`, a keyset cursor on `(occurred_at_utc, id)`, `subjectExists` through a left join).
- [X] T072 [US3] Create `PAGES/Features/RecordDesignPublish/RecordDesignPublishHandler.cs`, handling `Editor.DesignPublished` by inserting a `design` log entry whose id is the event id, and ignoring a duplicate key (PR-16). Register it in `PAGES/PagesModule.cs`. Make T065 pass.
- [X] T073 [US3] Create `PUBUI/HistoryView.vue` with routes `/sites/:siteId/pages/:pageId/history` and `/sites/:siteId/components/:componentId/history`. Add routes `/sites/:siteId/pages/:pageId/versions/:number` and `/sites/:siteId/components/:componentId/versions/:number`, which open `ADMIN/editor/EditorView.vue` in its existing read-only mode (011 ED-15) with the version document. Register everything in `ADMIN/app/router.ts`, and link **History** from `PublishMenu.vue`.
- [X] T074 [US3] Create `PUBUI/PublishLogView.vue` at `/sites/:siteId/publish-log`, linked from the site detail view for Administrators only. Add the i18n keys, and make T066 pass.

**Checkpoint**: quickstart §3. All P1 stories are complete: a site can be published, maintained and restored.

---

## Phase 6: User Story 4 - Unpublish a page (Priority: P2)

**Goal**: a published page can be taken down, and a live page cannot be deleted by
accident. Structure changes to live pages warn first and move the page to its new address.

**Independent Test**: quickstart §4. Unpublish a page: the next request gets the not-found
answer, and the draft and history remain. Publish again at its current address. Deleting
a live page is refused.

### Tests for User Story 4 ⚠️

- [X] T075 [P] [US4] Create `ITESTS/Publishing/UnpublishApiTests.cs`:
  - unpublish, then the next `GET` gets the not-found answer;
  - `409 pages.not_published` when it is not live;
  - the history records `unpublish`;
  - the child pages of an unpublished page are still served;
  - deleting a live page gives `409 pages.page_published`;
  - republishing serves the page at its current address;
  - after a slug change of a live page, the new address serves it and the old one gets not found (FR-011);
  - `Pages.PageUnpublished` is written.
- [X] T076 [P] [US4] Create `ADMINTESTS/publishing/addressWarning.spec.ts`: the count of live pages in a moved or renamed subtree comes from tree data, the warning appears only when the count is above zero, and **Delete** is disabled with a reason for live pages in `PageTreeView`.

### Implementation for User Story 4

- [X] T077 [US4] Implement `Publication.Unpublish()` (the live pointer cleared, dependencies deleted, the log entry, the domain event). Add `PAGESC/Events/PageUnpublished.cs` with its mapping and registration, and create the `PAGES/Features/UnpublishPage/` slice.
- [X] T078 [US4] Extend `PAGES/Features/DeletePage/` to refuse a live page (`pages.page_published`, FR-006) and to cascade versions, the publication and schedules otherwise. Make T075 pass.
- [X] T079 [US4] Add **Unpublish**, with confirmation, to `PUBUI/PublishMenu.vue`. Disable **Delete** for live pages in `ADMIN/features/pages/PageTreeView.vue` with a reason. Create `PUBUI/addressWarning.ts` and use it in `ADMIN/features/pages/MoveDialog.vue` and `PageFormDialog.vue` before a slug change or move (FR-011). Add the i18n keys, and make T076 pass.

**Checkpoint**: quickstart §4.

---

## Phase 7: User Story 5 - Schedule a publish or unpublish (Priority: P2)

**Goal**: members schedule publishes of pages and components, and unpublishes of pages. The
schedule runs exactly once at its time, with the content reviewed when it was set.

**Independent Test**: quickstart §5. Schedule two minutes ahead, then change the draft. At
the time, the reviewed content goes live once. Across a restart and with two dispatchers,
each schedule still runs once.

### Tests for User Story 5 ⚠️

- [X] T080 [P] [US5] Create `UTESTS/Pages/Publishing/PublishScheduleTests.cs`: the states and transitions of data-model §4.2, `revision` incremented by a time change, run skipped when not pending or on a revision mismatch, and past times refused.
- [X] T081 [P] [US5] Create `ITESTS/Publishing/ScheduleApiTests.cs`:
  - `PUT …/schedules/publish` captures the draft and validates it (`400 pages.invalid_document`) and refuses past times (`400 pages.schedule_in_past`);
  - a second `PUT` replaces the time and increments the revision;
  - `DELETE` cancels, and gives `404 pages.schedule_not_found` when there is none;
  - an unpublish schedule on a draft-only page gives `409 pages.not_published`;
  - a manual publish, unpublish or rollback cancels the pending publish schedule with `superseded_by_manual_action`, and a manual unpublish also cancels a pending unpublish;
  - the tree shows `pendingSchedules` and `failedSchedule`;
  - the same for components.
- [X] T082 [P] [US5] Create `ITESTS/Publishing/ScheduledPublishExactlyOnceTests.cs` (SC-007), driving `OutboxDispatcher.RunOnceAsync` and setting `available_at_utc` directly:
  - a due publish runs once with the captured content, not the later draft, and is logged `scheduled_publish` with the schedule owner;
  - two dispatchers running concurrently produce one version;
  - redelivering after `done` is a no-op;
  - a stale revision message is a no-op;
  - a due publish whose component was unpublished becomes `failed` with `pages.unpublished_components`, and the page is unchanged;
  - a message due while no dispatcher ran is handled on the next pass;
  - a scheduled unpublish runs and logs `scheduled_unpublish`.
- [X] T083 [P] [US5] Create `ADMINTESTS/publishing/ScheduleDialog.spec.ts`:
  - the local date and time with the time-zone name is converted to UTC;
  - the past is refused in the dialog;
  - an existing schedule is shown with Change and Cancel;
  - a manual publish warns about cancelling a pending schedule (FR-019).

  Axe, keyboard operation and i18n are checked.

### Implementation for User Story 5

- [X] T084 [US5] Implement the `PublishSchedule` behaviour in `PAGES/Domain/PublishSchedule.cs` (set, change, cancel, run, fail), and schedule cancellation inside `Publication.Publish`, `Unpublish` and `Rollback`, with log entries. Make T080 pass.
- [X] T085 [US5] Add the module-internal `PAGES/Features/RunDueSchedule/PublishScheduleDue.cs` (`EventType` `Pages.PublishScheduleDue`), registered in `PAGES/PagesModule.cs`. Create the `PAGES/Features/SetPageSchedule/`, `CancelPageSchedule/`, `SetComponentSchedule/` and `CancelComponentSchedule/` slices. Setting writes the schedule and the due message with `AvailableAtUtc = due` in one transaction (T011). Make T081 pass.
- [X] T086 [US5] Create `PAGES/Features/RunDueSchedule/RunDueScheduleHandler.cs`:
  - it loads the schedule and returns if it is not pending or the revision differs;
  - otherwise it runs publish with the captured content and `kind = scheduled`, skipping the draft-version check, or it runs unpublish;
  - on a domain refusal it marks the schedule `failed` with the code, logs `schedule_failed`, and returns normally (PR-14).

  Make T082 pass.
- [X] T087 [US5] Create `PUBUI/ScheduleDialog.vue`. Add **Schedule**, and **Change** and **Cancel** schedule, to `PublishMenu.vue` for pages and components. Show pending, overdue and failed states in `PublicationBadge.vue`. Add the i18n keys, and make T083 pass.

**Checkpoint**: quickstart §5.

---

## Phase 8: User Story 6 - Visitors get fast pages that are always up to date (Priority: P2)

**Goal**: rendered pages are cached and refreshed for exactly the pages a change affects,
correctly on every instance, within the performance budgets.

**Independent Test**: quickstart §6. With 50 cached pages, a component publish re-renders
exactly its 10 users. Two instances both serve new content on the next request.

### Tests for User Story 6 ⚠️

- [X] T088 [P] [US6] Create `ITESTS/Rendering/CacheInvalidationTests.cs` (SC-006, FR-032), counting renders through the internal `RenderMetrics` counter. Warm 50 pages, 10 of which use component C. Then:
  - publish C: exactly 10 re-render;
  - publish one page: exactly it and the pages linking to it re-render;
  - rollback and unpublish: likewise;
  - an address change of page X: the pages linking to X re-render;
  - a design publish: all pages re-render;
  - a change in `SiteDto.Version`: all pages re-render;
  - a component publish on a 2,000-page site where every page uses it completes in under 500 ms (plan Risks).
- [X] T089 [P] [US6] Create `ITESTS/Rendering/RenderingAcrossInstancesTests.cs` (SC-002, PR-05): two `WebApplicationFactory<Program>` hosts share one PostgreSQL database, each with its own in-memory cache. Warm on both, publish through host 1, and host 2's next response has the new content.
- [X] T090 [P] [US6] Extend `ITESTS/Rendering/PublicRenderingTests.cs`:
  - a `GET` with the returned `ETag` in `If-None-Match` gives `304` with no body;
  - after a publish, the old ETag gives `200`;
  - with a failing `ICacheProvider` test double, pages are still served correctly (FR-034).
- [X] T091 [P] [US6] Create `ITESTS/Publishing/GenerationBumpSiteFilterTests.cs` (Principle V): a bump caused on site A never changes a generation on site B, even when B has pages that depend on ids equal in value. Create `ITESTS/Rendering/MediaDeletionRefreshTests.cs`: deleting a media file shown by a live page and by a component used by another page bumps both. After the outbox pass, their HTML omits the image, and a duplicate `Media.FileDeleted` bumps once.

### Implementation for User Story 6

- [X] T092 [US6] Create `PAGES/Infrastructure/GenerationBump.cs`: `BumpForTargetsAsync(siteId, targets)` expands two levels through `ix_live_dependencies_target` (data-model §3.2) and runs one `ExecuteUpdateAsync` with an explicit site filter. Call it:
  - from `Publication` publish, unpublish and rollback of pages (the page itself and its linkers) and of components (their users);
  - from `PAGES/Infrastructure/PageTreeWrites.cs` for pages whose address changed, in the tree operation's transaction.
- [X] T093 [US6] Add `MEDIAC/Events/FileDeleted.cs`. Write it in `MEDIA/Features/DeleteFile/` in the delete's transaction, and register it. Create `PAGES/Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs`, with its own processed-event row in the Pages schema, like `RecordSiteSecurityEventsHandler` in Identity. Make T091 pass.
- [X] T094 [US6] Create `REND/Features/RenderPage/RenderKey.cs` (outside `Domain`, which the architecture tests reserve for site-owned entities once a type has a `SiteId`; research PR-05: generation, design number, `SiteDto.Version`, and a renderer stamp from the stylesheet hash and assembly version, built with `CacheKey.ForSite`) and `REND/Domain/RenderedPage.cs`. In `RenderPageHandler`:
  - read `FindLiveByPathAsync` and `GetLiveNumberAsync` first;
  - answer `304` when `If-None-Match` equals the key's ETag;
  - otherwise `GetOrCreateAsync` the page with `CacheEntrySettings(size: byte length, sliding: 1 h)`, falling back to a direct render if the cache throws;
  - set `ETag` and `Cache-Control: public, no-cache`.

  Add an internal `REND/Domain/RenderMetrics.cs` counter. Make T088, T089 and T090 pass.

**Checkpoint**: quickstart §6. The cache is on, and correct across instances.

---

## Phase 9: User Story 7 - A visitor who follows a broken link gets a helpful not-found page (Priority: P3)

**Goal**: missing addresses on an enabled site get an HTML not-found page in the site's
design and language, identical to the answer for admin paths on client domains.

**Independent Test**: quickstart §7. An unknown path gives `404` HTML with a link home when
the home page is live. `/admin` on a client domain gives the same bytes. An unknown host
still gets 009's JSON.

### Tests for User Story 7 ⚠️

- [X] T095 [P] [US7] Create `ITESTS/Rendering/NotFoundParityTests.cs` (PR-08, 009 S-13):
  - with `Hosting:AdminHostNames` set, `/admin` and `/api/x` on a site host give bytes identical to `/no-such-page`;
  - an unpublished page's address, a draft-only page's address and a never-used address give identical bytes;
  - the body has no path, query or host from the request;
  - `404`, `Cache-Control: no-store`, the §3 security headers, and no `ETag`;
  - the home link is present only while the home page is live;
  - an unknown host and a disabled site keep 009's JSON byte for byte.
- [X] T096 [P] [US7] Create `UTESTS/Rendering/PublicTextCatalogueTests.cs`: every tag in the Sites module's `SupportedLanguages` has a file `REND/Resources/public-texts/{tag}.json`, and every file has exactly the keys of `en.json`, each non-empty (FR-030, SC-009). Also test that a tag without a file (a language added to Sites later) falls back to English rather than failing. Read the tag list through a small `Sites.Contracts` constant or by reflection in the test; never hard-code it.

### Implementation for User Story 7

- [X] T097 [US7] Create `REND/Resources/public-texts/en.json` (`notFound.title`, `notFound.message`, `notFound.home`) and one file per remaining supported language (`ar` … `zh-Hant`, 40 files in all), embedded as resources, with a loader that falls back to `en.json` for a tag without a file. Name any translation that still needs a native speaker's review in `specs/012-publishing-rendering/quickstart-results.md`. Replace T021's placeholder in `REND/Features/NotFound/NotFoundPage.cs` with the HTML page of public-rendering.md §4: `PageShell` with the live design, no components, the texts, and the home link when `IsHomePageLiveAsync`. Cache the bytes under the `notfound` key of PR-13. Make T095 and T096 pass, and keep T032's not-found cases green.

**Checkpoint**: quickstart §7.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: isolation, logs, upgrade, smoke and performance, docs, and the manual
validation run.

- [X] T098 [P] Create `ITESTS/Isolation/Probes/PublishingIsolationProbe.cs` and register it in `ITESTS/Isolation/IsolationProbes.cs`. It covers:
  - publications, versions, dependencies, schedules, log entries and design versions through every new admin endpoint with another site's ids (answered as not found);
  - the public probe: site A's published page and its stylesheet-linked page are never served on site B's host (FR-037, SC-005).
- [X] T099 [P] Extend `ITESTS/Observability/LoggingTests.cs`: publish, rollback, schedule, run a due schedule and render a page with distinctive title and text. The logs contain ids and numbers only: no titles, paths or content (FR-044).
- [X] T100 [P] Create `ITESTS/Publishing/UpgradeFrom011Tests.cs`, modelled on `ITESTS/Editor/UpgradeFrom010Tests.cs`:
  - restore a 011 snapshot and start the host;
  - both migrations apply, and drafts and token sets are byte-identical;
  - no publication exists;
  - a public request gets the not-found page (FR-042).
- [X] T101 [P] Create `FIX/page-200-blocks.json`, a valid 200-block page using every block type plus a header and footer `component-ref`, checked by the golden runner. Create `STESTS/RenderPerformanceTests.cs`, following `STESTS/PageTreePerformanceTests.cs` (SC-003, SC-004, FR-041):
  - publish the header, footer and page;
  - 200 miss renders, each after a republish bumps the generation, at p95 under 200 ms;
  - 1,000 hits at p95 under 20 ms;
  - 0 bytes of `<script>` in the body.

  Record the numbers in the test output.
- [X] T102 [P] Extend `STESTS/ReferenceDeploymentSmokeTests.cs` (FR-043, E-3 closed):
  - create a Header component, save a draft with a Section and Heading, and publish it;
  - create a page referencing it and publish it;
  - `GET` the page from the site's host name and assert its heading and the header text;
  - fetch the stylesheet link and expect `200`;
  - unpublish, and assert `404` HTML.

  Update the class remarks: E-3 closed, E-5 unchanged.

  Then add a `smoke` job to `.github/workflows/ci.yml` (FR-041, analysis I1). It needs `image`, runs on `ubuntu-latest`, sets up .NET, and runs `dotnet test tests/QuickBuilder.SmokeTests`. That covers the reference journey and every performance test, including `RenderPerformanceTests` on `docker-compose.perf.yml`, so a broken budget fails the build. The job sets `QB_SMOKE_REQUIRE_DOCKER=1`; teach `STESTS/ComposeDeployment.cs` to fail, instead of skip, when that variable is set and `docker info` fails, so CI never passes by skipping. Upload `docker compose logs` as an artifact when the job fails.
- [X] T103 [P] Update `CONTRACTS/page-schema.md`, following contracts/page-schema-rendering.md: the §5 note, the §6 addition, the §8 addition, and §9 rewritten as normative "Rendering". Add the six events and the module-internal message to `specs/001-foundation-skeleton/contracts/integration-events.md` (the contract `docs/contributing.md` links to).
- [X] T104 [P] Update the documentation:
  - `docs/operations.md`: publishing and schedules, `Cache:MemoryLimitMegabytes` and memory sizing, multi-instance (E-4 closed; media still needs shared storage), the HTML not-found page, `/_assets/`, the read-only connection pools per module;
  - `docs/contributing.md` "Adding a block type": the renderer class, golden files and parity;
  - the plan's upgrade notes in the release notes draft;
  - `docs/backlog.md`: the deferred items of plan.md.
- [X] T105 [P] Record in `specs/001-foundation-skeleton/plan.md` Complexity Tracking, as a dated note, that E-3 and E-4 are closed by 012, citing plan.md.
- [ ] T106 Run [quickstart.md](./quickstart.md) §1–§11 by hand on the reference compose setup, including §6 with two instances. Record the results, the SC-001 timings, the SC-003 numbers and the browser versions in `specs/012-publishing-rendering/quickstart-results.md`.
- [ ] T107 Run `dotnet format --verify-no-changes`, `dotnet test`, and `npm run lint && npm run type-check && npm test && npm run build` in `src/admin`. Fix any failures, and confirm the per-package coverage gate passes for `QuickBuilder.Rendering`, `QuickBuilder.Pages` and `QuickBuilder.Editor`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 comes first, then T003–T007 in parallel.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
  - The shared infrastructure (T008–T011), the tables (T012–T015), the readers
    (T016–T019), the renderer core (T020–T027) and the admin foundations (T028–T029) can
    proceed in parallel.
  - T019 needs T016–T018. T021 needs T020.
- **US1 (Phase 3)**: after Foundational. **The MVP.**
- **US2 (Phase 4)**: after US1. It extends `Publication.Publish`, `RenderPageHandler` and
  `PublishMenu`.
- **US3 (Phase 5)**: after US1. The design history entry (T072) needs US2's event (T060).
- **US4 (Phase 6)**: after US1, independent of US2 and US3.
- **US5 (Phase 7)**: after US1. The cancellation on rollback (T084) needs US3's rollback
  (T067) when both are built; otherwise that part is added with US3.
- **US6 (Phase 8)**: after US2, because it needs the dependency rows (T056) to know what to
  bump. It is independent of US3, US4 and US5; the bumps for rollback and unpublish attach
  wherever those exist.
- **US7 (Phase 9)**: after Foundational (T021). It is independent of the other stories, but
  its parity test uses US1's page endpoint for the "missing page" case.
- **Polish (Phase 10)**: after the stories it covers. T106 is last but one, and T107 is
  last.

### Within Each User Story

- Tests are written first and fail.
- The domain comes before slices, slices before rendering integration, and the backend
  before the admin UI.
- The i18n keys and the green-test task close each story.

### Parallel Opportunities

- **Phase 1**: T003–T007.
- **Phase 2**: T008, T010, T011, T013–T018, and T022–T029, all together.
- **US1**: tests T030–T039 together; renderers T044, T045 and T046 together; T048 beside
  T047.
- **US2**: tests T051–T055 together; the backend (T056–T061) beside the admin (T062) once
  the endpoints exist.
- **US3**: tests T063–T066; slices T069 and T070.
- **Across stories**, after US1: US3, US4, US5 and US7 can go to different developers;
  US6 after US2.

---

## Parallel Example: User Story 1

```bash
# Tests together, before implementation:
Task: "T030 PublicationTests"
Task: "T031 PublishApiTests"
Task: "T032 PublicRenderingTests"
Task: "T033 Section/Container/Spacer/Divider/Heading/Text/RichText renderer tests"
Task: "T034 Image/Button/Embed renderer tests"
Task: "T035 RendererGoldenTests + context.json"
Task: "T036 CanvasMarkupParity.spec.ts"

# Renderers side by side once T025 exists:
Task: "T044 Section, Container, Spacer, Divider renderers"
Task: "T045 Heading, Text, Rich Text renderers"
Task: "T046 LinkResolver + Image, Button, Embed renderers"
```

## Parallel Example: User Story 6

```bash
Task: "T088 CacheInvalidationTests"
Task: "T089 RenderingAcrossInstancesTests"
Task: "T090 ETag/304 and failing-cache cases"
Task: "T091 GenerationBumpSiteFilterTests + MediaDeletionRefreshTests"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational. Readers are proven read-only, and token CSS matches the editor.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. A page is published and served as HTML that
   matches the canvas.

### Incremental Delivery

1. Setup and Foundational.
2. US1: publish and view. **Demo.**
3. US2 (components and design) and US3 (history and rollback). All P1 stories are done;
   sites are publishable and recoverable.
4. US6 (cache and invalidation), then US4 (unpublish) and US5 (schedules), in any order.
5. US7 (not-found page).
6. Polish, the manual quickstart run, and the PR closing #12.

The feature ships in one PR, as with 010 and 011, so there is no feature flag (plan,
Principle XI).

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US7.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #12.
- **Contract changes** (T103, and authorization.md in T014) land in the same PR as their
  tests (constitution, Development Workflow).
- **Avoid**:
  - writing through a reader context;
  - building HTML from content outside `HtmlWriter`;
  - deleting cache entries to invalidate (bump generations instead, PR-05);
  - putting titles, paths or content in logs or events.
