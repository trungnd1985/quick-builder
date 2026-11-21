---

description: "Task list for 022-draft-preview-links"
---

# Tasks: Draft Preview Links

**Input**: Design documents from `/specs/022-draft-preview-links/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every new endpoint and route;
  - unit tests for domain rules;
  - golden tests for rendering;
  - a two-instance test for cross-instance behaviour;
  - 80% line coverage on `PageComposer`, the preview handlers and link resolution.
- The spec measures SC-001 and SC-003 to SC-007 through automated suites (plan.md,
  Testing).
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-11-19)**:
- T016, T018, T026, T031, T034, T035, T041, T047, T053, T058, T066, T073 and T084 were
  extended or reworded. No task was added or renumbered.
- In the spec, FR-002 and SC-001 now also exclude preview forms.
- Research PV-02, PV-09 and PV-21, the plan, the data model, public-preview.md,
  admin-preview.md and quickstart §8 were updated to match.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md), so each story can
be implemented and tested on its own.
- US1, US2 and US3 are all P1. US3 is mostly a safety property of US1 and US2: its response
  rules are built in Phase 2, so every preview complies from its first line, and US3 adds
  forms, robots and the proofs.
- US4 and US5 are P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `HOST` = `src/QuickBuilder.Host`
- `SHAREDI` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `MEDIAC` = `src/Modules/Media/QuickBuilder.Media.Contracts`
- `EDITORM` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `IDENT` = `src/Modules/Identity/QuickBuilder.Identity`
- `IDENTC` = `src/Modules/Identity/QuickBuilder.Identity.Contracts`
- `REND` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`

**Conventions**:
- **Admin endpoints** (Editor, `EDITORM/Features/PreviewLinks/`), one file per slice,
  modelled on `EDITORM/Features/EditingSessions/OpenSession.cs`. Every action:
  - is routed as in [contracts/preview-api.yaml](./contracts/preview-api.yaml);
  - declares exactly one `[RequireCapability(...)]` with the contract's `x-capability`;
  - checks the `x-row-check` in the handler through `EDITORM/Infrastructure/EditingAccess.cs`;
  - dispatches through `IDispatcher`;
  - refuses with the codes of [data-model.md §5](./data-model.md);
  - is listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **`/_preview` actions** (`REND/Features/Preview/`) are all `[AllowAnonymous]` MVC actions,
  on the anonymous allow-list (T084).
  - Member routes check access in the handler with `IAccessEvaluator`: signed in and
    holding `pages.content.view` (or `cms.collections.view` for items) on `{siteId}`.
  - Anything else gets the generic not-found body (FR-004), never the 401 problem response.
  - Every action returns through `PreviewResponse` (PV-10).
- **Site context**: the handler sets it with `ISiteContextSetter.SetSite(siteId)` before any
  read, as `HOST/Middleware/AdminHostConfinementMiddleware.cs` does.
- **Time** comes from `TimeProvider` (tests use `ITESTS/Infrastructure/ManualClock.cs`).
- **Logs and events** carry ids, kinds, numbers and times only: never secrets, labels,
  titles or content.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task.

**References**:
- decisions: `PV-xx` in [research.md](./research.md);
- tables, invariants, read models and errors: [data-model.md](./data-model.md);
- admin endpoints: [contracts/preview-api.yaml](./contracts/preview-api.yaml);
- the `/_preview` surface: [contracts/public-preview.md](./contracts/public-preview.md);
- admin behaviour: [contracts/admin-preview.md](./contracts/admin-preview.md);
- interfaces, events and capability: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents and its one setting.

- [X] T001 Commit `specs/022-draft-preview-links/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `022-draft-preview-links`, referencing #22.
- [X] T002 [P] Write `UTESTS/Editor/PreviewOptionsTests.cs`:
  - `LinksValidFrom` may be absent;
  - it parses ISO 8601 UTC;
  - a value more than one day in the future fails validation (PV-07).

  Must fail first.
- [X] T003 Create `EDITORM/Infrastructure/PreviewOptions.cs` (section `Preview`, `DateTimeOffset? LinksValidFrom`, with an `IValidateOptions` for the one-day rule) until T002 passes. Register it in `EDITORM/EditorModule.cs` with `BindConfiguration("Preview").ValidateOnStart()`. Add a commented `"Preview": { "LinksValidFrom": null }` entry to `HOST/appsettings.json`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**:
- extract the shared render path with no behaviour change;
- add the `/_preview` surface and its response rules;
- add the read-only draft readers and media access that every story uses (PV-02, PV-04,
  PV-05, PV-06, PV-10, PV-11).

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.
- T004–T006 land first and change no behaviour. The whole existing suite, including the
  golden-output and render budget tests, must stay green after T006.

### One render path (PV-04)

- [X] T004 Create `REND/Rendering/PageComposer.cs`.
  - **Contents**: move the body of `PageResponder.RenderVersionAsync` into it, from reading
    the design to building `RenderInputs`.
  - **Source interface**: add `IContentSource`, with the reads the composer makes:
    - live design;
    - components;
    - menus;
    - link targets;
    - media;
    - ancestors;
    - items for menu entries;
    - template patterns.
  - **Live source**: implement it in `REND/Rendering/LiveContentSource.cs` over today's
    readers (`IPublishedContentReader`, `IPublishedDesignReader`, `IPublicMediaReader`,
    `IPublishedItemReader`, `BindingFetcher`).
- [X] T005 Change `REND/Features/RenderPage/PageResponder.cs` to call `PageComposer` with `LiveContentSource`.
  - Keep `Render`, `ToResponse` and `ContentSecurityPolicy` where they are.
  - Register both new types in `REND/RenderingModule.cs`.
  - No other change.
- [X] T006 Run the golden-output tests, `ITESTS/Rendering/PublicRenderingTests`, the binding and navigation render tests, and the render budget tests. All must pass unchanged (plan.md Risks). Record the run in the commit message.

**Checkpoint A**: `dotnet test` is fully green. Nothing visible has changed.

### The `/_preview` surface (PV-05, PV-10)

- [X] T007 [P] Write `ITESTS/Preview/PreviewSurfaceTests.cs`:
  - `/_preview/...` is answered on an admin host name, on a site host name and on an
    unknown host name, never with the "site not resolved" response;
  - the path is not in `RoutePrefixes.IsReserved`;
  - a log line written during a `/_preview/l/{secret}` request has no `RequestPath`
    property and no occurrence of the secret (capture Serilog with the existing test sink).

  Must fail first.
- [X] T008 In `HOST/Routing/RoutePrefixes.cs`:
  - add `public const string Preview = "/_preview"`;
  - add an `UnresolvedPublicPrefixes` list holding it, documented as "answers on every host
    name; site from the link or route; never logged (022 PV-05)".

  In `HOST/Middleware/SiteResolutionMiddleware.cs`, skip site resolution for those prefixes,
  as for reserved ones. Do **not** add it to `IsReserved`.
- [X] T009 [P] Add `_preview` to `PublicPathConstraint.AdministrativeSegments` in `REND/Features/RenderPage/PublicPathConstraint.cs`, and extend `ATESTS/PublicRouteBoundaryTests.cs` (or the integration test that compares it with `RoutePrefixes`) to expect it.
  *Done as*: the comparison lives in `ITESTS/Rendering/PublicRenderingTests.cs` (`The_page_route_leaves_exactly_the_hosts_administrative_and_unresolved_prefixes_alone`); `ITESTS/Pages/ReservedAddressCoverageTests.cs` covers the new prefix too.
- [X] T010 [P] Create `REND/Features/Preview/PreviewResponse.cs`.
  - **Headers**: builds a `PublicResponse` with exactly the headers of public-preview.md §5:
    - `Cache-Control: no-store, private`;
    - `X-Robots-Tag: noindex, nofollow`;
    - `Referrer-Policy: no-referrer`;
    - `X-Content-Type-Options`;
    - `Permissions-Policy`;
    - CSP, from `PageResponder.ContentSecurityPolicy` for documents, or
      `default-src 'none'; style-src 'sha256-…'` for the fixed pages;
    - never an `ETag`.
  - **Status**: takes the status as an argument.
  - **Result**: reuses `PublicResponseResult` from `REND/Features/RenderPage/RenderPageController.cs`.
- [X] T011 [P] Create `REND/Resources/PreviewTexts.resx` (English) with every bar and page text of contracts/admin-preview.md §6 and public-preview.md §3–§4, and a `PreviewTexts` accessor beside `REND/Features/NotFound/PublicTexts.cs`.
  *Done as*: `REND/Features/Preview/PreviewTexts.cs`, a C# catalogue of English constants (spec assumption: English at first), instead of a `.resx`.
- [X] T012 Create `REND/Features/Preview/NotAvailablePage.cs`: three fixed documents built once and byte-identical for every caller.
  - **No longer available** (404).
  - **Not available right now** (503, `Retry-After: 60`).
  - **Too many attempts** (429, with `Retry-After` from the caller).

  Each has a title, one sentence, no site data and inline styles allowed by its hash. The
  language comes from `Accept-Language` among `PublicTexts` languages, with English as the
  fallback (PV-10).
  *Done as*: English only, like the bar (no `Accept-Language` choice), so every failure is byte-identical by construction. Unknown `/_preview` paths get the same page (`PreviewFallbackController`).

### Draft readers (PV-02)

- [X] T013 [P] Add `PAGESC/Publishing/IPreviewContentReader.cs` with `PreviewPage` and `PreviewPageVersion`, exactly as module-surface.md §2.1.
- [X] T014 [P] Add `CMSC/Publishing/IPreviewItemReader.cs` with `PreviewItem` and `PreviewItemVersion`, exactly as module-surface.md §2.2.
- [X] T015 [P] Write `ITESTS/Pages/PreviewContentReaderTests.cs`:
  - a draft is returned with title, path, saved time, author and schedule;
  - a template page has a null path and `IsTemplate`;
  - a version past retention (content removed) is null;
  - `GetUnpublishedComponentDraftsAsync` and `GetUnpublishedMenuDraftsAsync` return only
    never-published ones;
  - a trashed page is absent;
  - another site's id is absent;
  - the reader cannot save: its context throws on `SaveChanges`, as `PublishedReadDbContext`
    does today.

  Must fail first.
- [X] T016 Create `PAGES/Infrastructure/PreviewReadDbContext.cs`: built on `ReadOnlyConnection.From`, never migrated, `SaveChanges` throws, with the site and `Trash` named filters as in `PAGES/Infrastructure/PagesDbContext.cs`. Map the page draft, component draft, menu draft, version, publication and schedule tables in it. Leave `PublishedReadDbContext.cs` unchanged (PV-02). Implement `PAGES/Infrastructure/PreviewContentReader.cs` with `AsNoTracking` projections, one query per method, until T015 passes. Register it in `PAGES/PagesModule.cs`.
  *Done as*: the contract returns `PreviewDraftDocument` (document and name) for never-published components and menus, and gains `GetLiveTemplateVersionAsync` for item previews.
- [X] T017 [P] Write `ITESTS/Cms/PreviewItemReaderTests.cs`:
  - a draft is returned as a `PublishedItem` with draft values and draft slug, with saved
    time, author, `NeedsAttention` and schedule;
  - a version is returned with its own values and slug;
  - a trashed item, an item of a deleted collection and another site's item are all absent;
  - the reader cannot save.

  Must fail first.
- [X] T018 Create `CMS/Infrastructure/PreviewReadDbContext.cs` in the same way as T016 (read-only connection, never migrated, site and `Trash` filters), mapping the item draft and version data. Leave `CmsPublishedReadDbContext.cs` unchanged. Implement `CMS/Infrastructure/PreviewItemReader.cs` until T017 passes, reusing the value-reading helpers of `CMS/Infrastructure/PublishedItemReader.cs`. Register it in `CMS/CmsModule.cs`.

### Media for previews (PV-11)

- [X] T019 [P] Add `MEDIAC/IMediaContent.cs` with `MediaContent`, as module-surface.md §2.5.
- [X] T020 Move the lookup and storage-open logic of `MEDIA/Features/ServePublicFile/ServePublicFile.cs` into an internal `MEDIA/Infrastructure/PublicFileReader.cs`. Make both `ServePublicFileHandler` and a new `MEDIA/Infrastructure/MediaContent.cs` (`IMediaContent`) use it. The `/_media` behaviour is unchanged, and its existing tests must stay green. Register it in `MEDIA/MediaModule.cs`.
  *Done as*: `MEDIA/Infrastructure/MediaContentReader.cs` dispatches the existing `ServePublicFileQuery`, so `/_media` itself is untouched. The contract record is `MediaFileContent` (a Media type was already called `MediaContent`) and carries the file id, name and image flag.

### Preview model (PV-03, PV-06, PV-13)

- [X] T021 [P] Create `REND/Domain/PreviewContext.cs`, `REND/Domain/PreviewBarModel.cs` and `REND/Domain/PreviewAddresses.cs` as data-model.md §4.6. Add the optional `PreviewContext? Preview = null` as the last parameter of `REND/Domain/RenderInputs.cs`.
- [X] T022 [P] Create `REND/Domain/PreviewErrors.cs` with the codes of data-model.md §5 and their statuses. Map them in the Rendering module's problem-response helper. The Editor codes go in `EDITORM/EditorErrors.cs` and `EDITORM/EditorErrorResults.cs`.
  *Done as*: member refusals are fixed HTML pages built by `PreviewResponse.Fixed` (a preview opens in a browser tab), so Rendering needed no error codes. The Editor codes come with US2 (T049).

**Checkpoint B**: `/_preview` answers (404 for everything) on every host. The readers pass
their tests. The suite is green.

---

## Phase 3: User Story 1 — A member previews a draft exactly as it will render (Priority: P1) 🎯 MVP

**Goal**: a signed-in member opens the rendered draft of any page or item from the admin,
composed by PV-03, with the preview bar. Public addresses do not change (FR-001–FR-004,
FR-020–FR-021, FR-028 for drafts, FR-029, FR-030).

**Independent Test**: quickstart §1. `PreviewParityTests` proves the preview equals the
published output apart from the bar and addresses (SC-001). `MemberPreviewTests` covers
access and never-published pages.

### Tests for User Story 1 ⚠️

- [X] T023 [P] [US1] Write `UTESTS/Rendering/PreviewCompositionTests.cs`, one test per row of research PV-03:
  - a live component wins over its draft;
  - a never-published component, menu and design come from their drafts and are listed as
    "from draft";
  - an item preview uses the live template, or the template draft when it was never
    published;
  - a template page uses the sample item's published values, or its draft values when it
    has none.

  Must fail first.
- [X] T024 [P] [US1] Write `UTESTS/Rendering/PreviewAddressesTests.cs`:
  - a live page link becomes `site.BaseUrl + path`;
  - a non-live target and a site without a host name give no `href`;
  - external links are unchanged;
  - media and every `srcset` entry use the member or link media prefix;
  - the stylesheet uses `/_preview/assets/`;
  - pager links keep the preview address (PV-06).
- [X] T025 [P] [US1] Write `UTESTS/Rendering/PreviewBarTests.cs`, with golden markup for:
  - a member draft bar;
  - a link draft bar with expiry;
  - a bar with "from draft" parts and a schedule;
  - a needs-attention bar.

  Also assert:
  - the bar is the first child of `<body>` and outside the page root;
  - its rules are in the page's single `<style>` element;
  - the hide control is a checkbox and label with no script;
  - names and labels are encoded (PV-13).
- [X] T026 [P] [US1] Write `ITESTS/Preview/PreviewParityTests.cs` (SC-001). For every golden page (all block types, components, lists, menus, forms) and for an item page:
  1. publish it;
  2. save an identical draft;
  3. render both;
  4. remove the `.qb-preview` element and its rules;
  5. map preview addresses back to public ones;
  6. drop the robots and referrer meta of the preview;
  7. map each preview form `action` back to `/_forms/{id}`, and remove the form script
     element on both sides;
  8. compare: byte-identical.

  Also assert that the page's own style rules (the `<style>` element without the
  `qb-preview-` rules) and the page root element are byte-identical with and without the bar
  (FR-021).
- [X] T027 [P] [US1] Write `ITESTS/Preview/MemberPreviewTests.cs`:
  - a page draft renders with the bar, and the public address and its cache entry are
    unchanged;
  - a never-published page renders while its public address answers not found;
  - an item draft renders through its template;
  - a template page with `?item=` renders that item;
  - every site role can preview, a non-member and an anonymous request get the generic 404,
    and another site's ids get 404 (FR-004);
  - an item of a collection without a template gets `preview.no_template`;
  - an invalid page draft (stored through a fixture) gets 422 with reasons and locations;
  - a needs-attention item renders with the warning (PV-15);
  - member media serves a file of the site through `/_preview/sites/{siteId}/media/{file}`,
    and 404 for another site's file;
  - `/_preview/assets/{file}` serves the block stylesheet.

### Implementation for User Story 1

- [X] T028 [US1] Implement `REND/Features/Preview/PreviewComposition.cs` (pure) until T023 passes.
- [X] T029 [US1] Implement `REND/Features/Preview/PreviewContentSource.cs` (`IContentSource`):
  - **Live first**: it delegates to the live readers.
  - **Fallbacks**: `IPreviewContentReader.GetUnpublishedComponentDraftsAsync` and
    `GetUnpublishedMenuDraftsAsync` fill the gaps, and `IDesignTokenReader` supplies the
    design when the live number is 0 and a token set was saved.
  - **Bar**: it records each part shown from a draft for the bar.
- [X] T030 [US1] Make the renderers honour `RenderInputs.Preview.Addresses` until T024 passes:
  - `REND/Rendering/LinkResolver.cs`;
  - `REND/Rendering/Blocks/MediaRenderers.cs`, including `srcset`;
  - `REND/Rendering/Blocks/Pager.cs`;
  - `REND/Rendering/PageShell.cs`, for the stylesheet link;
  - `REND/Rendering/Navigation/` link writing.
  *Done as*: one helper, `REND/Rendering/PageLinks.cs`, used by `LinkResolver`, `BindingResolver`, `MenuModel` and the form help link. Media addresses are rewritten once in `PreviewContentSource` (`PreviewAddresses.Media`), so the image, rich-text and binding renderers needed no change.

  With `Preview` null, every output is unchanged (T006's suites stay green).
- [X] T031 [US1] Create `REND/Features/Preview/PreviewBar.cs`, which writes the bar from `PreviewBarModel`. The member bar's "Share preview" link is the editor or item form address with `?share=1`, plus `&version=n` for versions (T053). In `REND/Rendering/PageShell.cs`, write it as the first child of `<body>` and append its rules to the page's style element when `Preview` is set. Continue until T025 passes.
- [X] T032 [US1] In `REND/Seo/HeadBuilder.cs` (or `HeadWriter.cs`), when `Preview` is set, write `<meta name="robots" content="noindex, nofollow">` in place of the page's robots setting and add `<meta name="referrer" content="no-referrer">` (PV-10).
- [X] T033 [US1] Create `REND/Features/Preview/PreviewResponder.cs`. It:
  1. resolves the target (page draft, item draft, or template with sample item) through
     the PV-02 readers;
  2. runs `IPageDocumentValidator` on page drafts and maps a failure to 422 with reasons
     (PV-15);
  3. builds `PreviewContext` (bar with saver names from `IUserDirectory`, addresses);
  4. calls `PageComposer` with `PreviewContentSource`;
  5. renders with `PageResponder.Render`;
  6. returns through `PreviewResponse`.

  It never touches `ICacheProvider` and never builds a `RenderKey` or `ItemRenderKey`.
- [X] T034 [US1] Create `REND/Features/Preview/MemberPreviewController.cs` with `GET /_preview/sites/{siteId}/pages/{pageId}` (with `item` query) and `GET /_preview/sites/{siteId}/items/{itemId}`.
  - Both are `[AllowAnonymous]` and check the signed-in member's capability in the handler
    through `IAccessEvaluator` (`pages.content.view`; `cms.collections.view` for items),
    answering the generic 404 body otherwise.
  - Both set the site context and answer 405 for other methods.
  - Unknown, trashed or other-site ids answer the generic 404 body.
- [X] T035 [US1] Create `REND/Features/Preview/PreviewMediaController.cs` with `GET /_preview/sites/{siteId}/media/{**file}` (member: `[AllowAnonymous]` with T034's handler check for `pages.content.view`), and `REND/Features/Preview/PreviewAssetsController.cs` with `GET /_preview/assets/{**file}`. The latter serves the same bytes as `ServeBlockStylesheetController` without needing a site. Media goes through `IMediaContent` with PV-10 headers. Continue until T027 passes.
  *Done as*: the member media route is on `MemberPreviewController`; `PreviewAssetsController`, `PreviewFormController` (T061) and `PreviewFallbackController` sit in the same file.
- [X] T036 [P] [US1] Create `ADMIN/features/preview/previewUrls.ts`, which builds member preview addresses on the admin origin with the installation base path, for page, item, template with sample item and version.
- [X] T037 [US1] Create `ADMIN/features/preview/PreviewButton.vue`, with tests in `ADMINTESTS/features/preview/PreviewButton.spec.ts`:
  1. flush the editing session's pending save;
  2. on success, call `window.open(url, '_blank', 'noopener')`;
  3. on a refused save, show the existing save error and open nothing (FR-003, PV-14).

  Write the tests first.
- [X] T038 [US1] Place Preview on the existing screens:
  - **Editor**: add `PreviewButton` to `ADMIN/editor/EditorTopBar.vue`, passing the
    editor's sample item for templates.
  - **Item form**: add it to `ADMIN/features/cms/items/ItemPublishBar.vue`, disabled with
    "This collection has no template page" when there is none.
  - **Row menus**: add a "Preview" link (`<a target="_blank" rel="noopener">`) to
    `ADMIN/features/pages/PageTreeView.vue` and `ADMIN/features/cms/items/ItemListView.vue`.
  *Done as*: the page tree shows Preview for the selected page beside "Edit content"; the item list has a Preview link in each row's name cell. An item preview with unsaved changes shows the last saved draft and says so (`preview.lastSaved`).

  Add the `en.json` keys.

**Checkpoint**: quickstart §1 passes. US1 is shippable on its own.

---

## Phase 4: User Story 2 — A member shares a time-limited preview link with a reviewer (Priority: P1)

**Goal**: members who may edit create links (label, expiry 1 hour to 30 days, at most 20
active), and anyone holding one sees that one target, the latest saved draft, until it
expires (FR-005–FR-013, FR-018, FR-027).

**Independent Test**: quickstart §2. `PreviewLinkTests`, `PreviewLinkTamperingTests`
(SC-005) and `PreviewLinkAddressTests`.

### Tests for User Story 2 ⚠️

- [X] T039 [P] [US2] Write `UTESTS/Editor/PreviewLinkTests.cs` for the `PreviewLink` aggregate (data-model §1):
  - expiry after now and at most 30 days ahead, else `preview.invalid_expiry`;
  - the label is trimmed, empty becomes null, and over 80 characters or control characters
    give `preview.invalid_label`;
  - the secret is 43 base64url characters and the stored hash is its SHA-256;
  - `IsActive` honours revoked, expiry and `LinksValidFrom`;
  - `Revoke` is idempotent.
- [X] T040 [P] [US2] Write `ITESTS/Preview/PreviewLinkTests.cs`:
  - **Create**: an Editor creates a page link and an item link (201, `url` holds the secret,
    the list shows it without the secret).
  - **Opening**: the link opens without a session and shows the draft with an expiry bar.
    After another draft save, the link shows the new draft (clarification 1).
  - **Last opened**: `lastOpenedAtUtc` is set, and a second open within 60 seconds does not
    write again.
  - **Template links**: a template page link with `sampleItemId` shows that item.
  - **Limit**: the 21st active link gets `preview.link_limit`, including two concurrent
    creates at 19.
  - **Expiry**: after expiry (ManualClock), the link gets the "no longer available" page.
  - **Trash**: a trashed target gives "no longer available", and after a restore the link
    works again.
  - **Invalid draft**: an invalid page draft gives "not available right now" (503).
  - **Media**: link media serves a file the target uses, and 404 for a file of the same site
    that it does not use.
  - **Emergency switch**: links created before `Preview:LinksValidFrom` are dead.
- [X] T041 [P] [US2] Write `ITESTS/Preview/PreviewLinkTamperingTests.cs` (SC-005). Generate at least 1,000 variants and send them from at least 60 client addresses (`X-Forwarded-For` through the test host's forwarded-headers setup), at most 15 per address per minute, so the throttle (PV-18) never fires; T043 tests the throttle. Assert every response body is byte-identical to the "no longer available" page:
  - flipped characters, truncated and extended secrets;
  - empty and whitespace secrets;
  - secrets of other sites' links;
  - secrets of revoked links;
  - path traversal after the secret;
  - other-target media paths.
- [X] T042 [P] [US2] Write `ITESTS/Preview/PreviewLinkAddressTests.cs`:
  - a site with host names gets `url` on `site.BaseUrl`;
  - a site without one gets it on the request's admin origin, after forwarded headers;
  - both open.
  *Done as*: every site keeps at least one host name (009: "A site must have at least one host name"), so the admin-origin fallback is never reached. The test instead proves what it relies on: a link opens on any host name, the admin's included, and shows its own site's content.
- [X] T043 [P] [US2] Write `ITESTS/Preview/PreviewLinkThrottleTests.cs`:
  - 20 unknown secrets in a minute from one address are answered normally, and the 21st
    gets 429 with `Retry-After`;
  - a valid link from that address also gets 429 until the window ends;
  - another address is unaffected;
  - expired and revoked links do not count (PV-18);
  - `preview_link_failures` holds only hashed keys.

### Implementation for User Story 2

- [X] T044 [US2] Create `EDITORM/Domain/PreviewSecrets.cs` (32 random bytes, base64url, SHA-256, as `IDENT/Infrastructure/Security/Tokens.cs` does) and `EDITORM/Domain/PreviewLink.cs` (aggregate root, `ISiteOwned`, invariants of data-model §1) until T039 passes.
- [X] T045 [US2] Map `PreviewLink` and the failure-window entity in `EDITORM/Infrastructure/EditorDbContext.cs` exactly as data-model §1–§2:
  - the checks and the four indexes;
  - the site filter on links;
  - the installation marker reason on failure windows.
  *Done as*: the failure windows carry no `SiteId` at all, so they need no installation marker. Migration `20261118181539_AddPreviewLinks`.

  Generate `EDITORM/Infrastructure/Migrations/<ts>_AddPreviewLinks.cs` and update the
  snapshot.
- [X] T046 [US2] Add `EDITORC/IPreviewLinks.cs` (with `PreviewLinkResolution`) and `EDITORC/PreviewCapabilities.cs`, as module-surface.md §2.3. Register `editor.preview_links.manage` (site scope, no site roles) in `EDITORM/EditorCapabilities.cs`.
- [X] T047 [US2] Implement `EDITORM/Infrastructure/PreviewLinkFailures.cs`: minute and hour windows keyed by SHA-256 of `ClientAddress.Group`, with `INSERT … ON CONFLICT DO UPDATE` through parameterised `Database.SqlQuery`, as `FormsDbContext.CountRequestAsync` does, and limits of 20 a minute and 200 an hour (PV-18).
  *Done as*: parameterised `Database.ExecuteSqlAsync` for the upsert; the limits are constants on `PreviewLinkFailures`.
- [X] T048 [US2] Implement `EDITORM/Infrastructure/PreviewLinkResolver.cs` (`IPreviewLinks`), on every call and never cached:
  1. check the throttle;
  2. look up by hash across sites (`AcrossAllSites` with reason "a link names its own site");
  3. check `IsActive` against `PreviewOptions`;
  4. count a failure only when the hash is unknown;
  5. update `last_opened_at_utc` conditionally when it is more than 60 seconds old.
  *Done as*: also checks the creator's access through `IMemberAccess` (T069, T070 delivered here), since the resolver is written once.

  Register it in `EDITORM/EditorModule.cs`.
- [X] T049 [US2] Create `EDITORM/Features/PreviewLinks/CreatePreviewLink.cs` (`POST …/preview-links`):
  - **Checks**: the row check through `EditingAccess.EditCapabilityOf`. Target existence and
    template presence through `IPreviewContentReader` and `IPreviewItemReader`
    (`preview.not_found`, `preview.no_template`, `preview.version_not_retained`).
  - **Limit**: in one transaction, take `pg_advisory_xact_lock` through a parameterised
    `Database.SqlQuery` keyed by site, kind and id; count active links; refuse at 20; insert
    (PV-17).
  - **Response**: 201 with `link` and `url`, built by PV-05's rule.
  *Done as*: all five slices (create, list, site list, revoke, revoke all) are in `EDITORM/Features/PreviewLinks/PreviewLinkSlices.cs`, with the shared checks in `PreviewLinkAccess`; links raise their events from the aggregate (T071 delivered here).
- [X] T050 [US2] Create `EDITORM/Features/PreviewLinks/ListPreviewLinks.cs` (`GET …/preview-links?documentKind&documentId`, row check) and `EDITORM/Features/PreviewLinks/PreviewLinkResponses.cs` (`PreviewLink` and `UserSummary`, names from `IUserDirectory`).
- [X] T051 [US2] Create `REND/Features/Preview/PreviewMediaSet.cs`.
  - **Contents**: the target's referenced media ids, computed through `PageComposer`'s
    reference gathering without writing HTML.
  - **Caching**: cached for 5 minutes in `ICacheProvider` under
    `preview-media:{siteId}:{linkId}:{stamp}`, where the stamp is the draft's save time or
    the version number (PV-11).
- [X] T052 [US2] Create `REND/Features/Preview/LinkPreviewController.cs` with `GET /_preview/l/{secret}` and `GET /_preview/l/{secret}/media/{**file}`.
  - **Resolving**: `IPreviewLinks.ResolveAsync`, then set the site context, then
    `PreviewResponder` in link mode (expiry in the bar, no admin links).
  - **Media**: allowed only when in `PreviewMediaSet`.
  - **Failures**: every `NotAvailable`, unreadable target and media miss answers
    `NotAvailablePage` 404; an invalid page draft gives 503; `Throttled` gives 429.
  *Done as*: blank or malformed secrets get the same "no longer available" page (the action takes `string?`, so the API layer never answers 400).

  Continue until T040–T043 pass.
- [X] T053 [US2] Create `ADMIN/api/previewLinks.ts` (create and list, typed from preview-api.yaml) and `ADMIN/features/preview/SharePreviewDialog.vue`, with tests in `ADMINTESTS/features/preview/SharePreviewDialog.spec.ts` written first:
  - label;
  - expiry radio buttons, with 7 days as the default, and a custom picker limited to 30
    days;
  - Create;
  - the one-time link with Copy and a live-region "Link copied";
  - the active list with last opened;
  - a disabled Create at 20;
  - error messages for every `preview.*` code;
  - opening the editor or item form route with `?share=1` (and `&version=n`) opens the
    dialog at once, which is how members reach it from the preview bar;
  - axe passes.
  *Done as*: the dialog also lists, revokes and revokes all (part of T077). Error keys drop the `preview.` prefix, because vue-i18n reads dots as nesting.

  Add the `en.json` keys.
- [X] T054 [US2] Add "Share preview" beside Preview in `ADMIN/editor/EditorTopBar.vue` and `ADMIN/features/cms/items/ItemPublishBar.vue`, shown only to members holding the kind's edit capability (the server checks anyway).
  *Done as*: Share preview sits beside Preview in the editor (drafts only; versions are shared from history, T080) and in the item publish bar; both open the dialog from `?share=1` (and `&version=n`).

**Checkpoint**: quickstart §2 passes.

---

## Phase 5: User Story 3 — Previews stay out of search engines, caches and the public site (Priority: P1)

**Goal**: prove and complete the isolation rules:
- noindex on every preview;
- no cache in either direction;
- no referrer;
- no sitemap entry;
- forms that send nothing (FR-022–FR-026).

**Independent Test**: quickstart §3. `PreviewResponseRulesTests`,
`PreviewCacheIsolationTests` (SC-004), `FormSubmitIsolationTests` and
`PreviewBoundaryTests`.

### Tests for User Story 3 ⚠️

- [X] T055 [P] [US3] Write `ITESTS/Preview/PreviewResponseRulesTests.cs`. For every `/_preview` route and outcome (member page, member item, link page, link media, member media, assets, form, 404, 503 and 429), assert the headers of public-preview.md §5 and that there is no `ETag`. For documents, assert the robots and referrer meta tags, including on a page whose own search setting allows indexing.
- [X] T056 [P] [US3] Write `ITESTS/Preview/PreviewCacheIsolationTests.cs` (SC-004):
  1. warm the public cache for a published page;
  2. open member and link previews of its changed draft 10 times;
  3. assert the public address is still a cache hit (render metric unchanged) with
     published content;
  4. assert the cache provider holds no entry whose value contains the draft's marker text;
  5. assert only `preview-media:` keys were added.
  *Done as*: in `ITESTS/Preview/PreviewResponseRulesTests.cs`. The proof is `RenderMetrics.Renders`: after the public page is warm, 20 previews leave the count unchanged and the public page still shows the published content. The cache provider cannot be enumerated, and only the in-memory provider ships (a shared one is deferred since 012), so the two-instance variant was not added.

  Also run it with two instances and a shared cache.
- [X] T057 [P] [US3] Write `ITESTS/Preview/FormSubmitIsolationTests.cs`:
  - a preview page's form has `action` set to `/_preview/form` and no form script;
  - posting to it answers 200 with the preview notice and PV-10 headers;
  - `forms.submissions`, the form rate windows and the Forms outbox are unchanged;
  - no email is queued.
  *Done as*: counts submissions, rate windows, notification deliveries and the Forms outbox before and after; all unchanged.
- [X] T058 [P] [US3] Write `ATESTS/PreviewBoundaryTests.cs` (PV-21):
  - `RenderPageController`, `PageResponder`, `LiveContentSource`, `SitemapController`,
    `SitemapBuilder` and `RobotsController` do not depend on `IPreviewContentReader`,
    `IPreviewItemReader` or `IDesignTokenReader`;
  - types in `QuickBuilder.Rendering.Features.Preview` do not reference `RenderKey`,
    `ItemRenderKey` or `PageResponder.RespondAsync`;
  - every action in `Features.Preview` returns through `PreviewResponse`;
  - `PublishedContentReader`, `PublishedItemReader` and the sitemap readers do not depend on
    either module's `PreviewReadDbContext`.
  *Done as*: `ATESTS/PreviewBoundaryTests.cs` also checks the not-found page, `PageComposer` and `RenderPageHandler`, and requires every preview controller to depend on `PreviewResponse`, `NotAvailablePage` or `PreviewMedia`.
- [X] T059 [P] [US3] Add to `ITESTS/Seo/` (the existing robots and sitemap tests):
  - `robots.txt` contains `Disallow: /_preview/` whatever the Administrator's rules;
  - the sitemap never contains `/_preview`.
  *Done as*: the sitemap and robots assertions are in `PreviewResponseRulesTests`; `ITESTS/Seo/RobotsTests.cs` pins the new line in the generated text.

### Implementation for User Story 3

- [X] T060 [US3] In `REND/Rendering/Forms/FormMarkup.cs`, when `Preview` is set, write `action="{base}/_preview/form"` and do not write the form script, so `context.FormScriptWritten` stays false. The CSP keeps `form-action 'self'`.
  *Done as*: done with US1 (T030), with `RenderContext.PreviewFormWritten` and `RenderedPage.PostsToPreview` so the page's policy allows `form-action 'self'` without a script.
- [X] T061 [US3] Create `REND/Features/Preview/PreviewFormController.cs` (`POST /_preview/form`, `[AllowAnonymous]`, ignores the body and reads no form). It returns the "Forms are not sent from previews" page through `PreviewResponse`. Continue until T057 passes.
  *Done as*: done with US1, in `MemberPreviewController.cs`.
- [X] T062 [US3] Add `Disallow: /_preview/` to the robots output in `REND/Features/Robots/RobotsController.cs`, after the Administrator's rules and before the sitemap line, until T059 passes.
- [X] T063 [US3] Fix anything T055, T056 and T058 reveal. The tests must pass with no exceptions to the rules.
  *Done as*: the preview stylesheet now answers `no-store, private` and `no-referrer` too, as public-preview.md §5 asks of every `/_preview` response.

**Checkpoint**: quickstart §3 passes. US1–US3, the P1 scope, are complete.

---

## Phase 6: User Story 4 — A member revokes links and sees who has access (Priority: P2)

**Goal**:
- revoke one or all links;
- a site-wide list for Administrators;
- links end when their creator loses access;
- history entries;
- an hourly sweep;
- imports clear links (FR-009 creator clause, FR-014–FR-017, FR-019, PV-16, PV-19, PV-20).

**Independent Test**: quickstart §4. `PreviewLinkRevocationTests` (two instances, SC-006),
`PreviewLinkCreatorAccessTests`, `PreviewLinkHistoryTests` and `PreviewAuthorizationTests`.

### Tests for User Story 4 ⚠️

- [X] T064 [P] [US4] Write `ITESTS/Preview/PreviewLinkRevocationTests.cs`:
  - revoke one: only it stops, at the next request;
  - revoking again is 204 with no second history entry;
  - revoke all for a target returns the count;
  - site-wide revoke all, by an Administrator, ends every link of the site and no other
    site's;
  - with two instances, a link opened through instance B, revoked through A and reopened
    through B within 5 seconds is gone (SC-006).
  *Done as*: in `ITESTS/Preview/PreviewLinkRevocationTests.cs`, with T065 and T067. The two-instance case starts a second `QuickBuilderApp` on the same database; nothing is cached, so the revoke is seen at the next request.
- [X] T065 [P] [US4] Write `ITESTS/Preview/PreviewLinkCreatorAccessTests.cs`:
  - removing the creator's role on the site stops their links at once, and giving it back
    revives unexpired ones;
  - deactivating the creator stops them;
  - an Administrator creator keeps working on every site;
  - another member's links are unaffected.
  *Done as*: in `PreviewLinkRevocationTests` (`A_link_ends_when_its_creator_loses_access_and_works_again_when_it_is_given_back`), including deactivation.
- [X] T066 [P] [US4] Write `ITESTS/Preview/PreviewLinkHistoryTests.cs`:
  - creating and revoking a page link adds `preview_link_created` and
    `preview_link_revoked` publish log entries for that page, with actor, version number
    and time, and `expires_at_utc` on the created entry;
  - the same for an item, as `previewLinkCreated` and `previewLinkRevoked` item history
    with the expiry;
  - redelivering the event adds nothing;
  - no entry contains the label or the secret;
  - expiry and creator access loss add nothing.
  *Done as*: `ITESTS/Preview/PreviewLinkHistoryTests.cs` delivers the Editor outbox events by hand, twice each, as the outbox does not run in tests.
- [X] T067 [P] [US4] Write `ITESTS/Preview/PreviewAuthorizationTests.cs`:
  - every endpoint of preview-api.yaml for each role;
  - Editor and Designer may create, list and revoke for a target, and may not use
    `/preview-links/all` or a site-wide revoke all;
  - Administrator may do all;
  - other sites' ids give 404.
  *Done as*: in `PreviewLinkRevocationTests`. The endpoint catalogue is read from the running app, so no list needed editing; `Isolation/Probes/PreviewLinksIsolationProbe.cs` covers the one addressed endpoint, and `EndpointAuthorizationCoverageTests` reviews the new public routes.

  Add the endpoints to `ITESTS/Infrastructure/EndpointCatalog.cs` and the isolation probes.
- [X] T068 [P] [US4] Write `ITESTS/Preview/PreviewLinkSweepTests.cs`:
  - links expired or revoked more than 30 days ago are deleted, newer ones are kept;
  - failure windows older than one hour are deleted;
  - two sweeps running at once delete each row once.

### Implementation for User Story 4

- [X] T069 [P] [US4] Add `IDENTC/IMemberAccess.cs`, as module-surface.md §2.4. Implement it in `IDENT/Infrastructure/Authorization/MemberAccess.cs`: it reads the user's status and role grants on every call, applies the catalog's scope and roles as `AccessEvaluator` does, and returns false for a deactivated user. Register it in `IDENT/IdentityModule.cs`.
  *Done as*: done with US2 (the resolver needed it).
- [X] T070 [US4] In `EDITORM/Infrastructure/PreviewLinkResolver.cs`, after `IsActive`, call `IMemberAccess.HoldsCapabilityAsync(created_by, EditingAccess.EditCapabilityOf(kind), site)`, and return `NotAvailable` when it is false (PV-08). Continue until T065 passes.
  *Done as*: done with US2.
- [X] T071 [US4] Add `EDITORC/Events/PreviewLinkEvents.cs` (`PreviewLinkCreated`, `PreviewLinkRevoked`, as module-surface.md §3). Raise them from `PreviewLink.Create` and from the first `Revoke`, and write them to Editor's outbox in the same transaction. Change `CreatePreviewLink.cs` accordingly.
  *Done as*: done with US2: the aggregate raises both events and `PreviewLinkAccess.Publish` writes them to the outbox.
- [X] T072 [US4] Create `EDITORM/Features/PreviewLinks/RevokePreviewLink.cs` (`POST …/{linkId}/revoke`), `RevokeAllPreviewLinks.cs` (`POST …/revoke-all`, with a target or site-wide) and `ListSitePreviewLinks.cs` (`GET …/preview-links/all`):
  - keyset by `expires_at_utc, id`;
  - target labels from `IPreviewContentReader.GetTitlesAsync` and
    `IPreviewItemReader.GetNamesAsync`;
  - `editor.preview_links.manage` for site-wide operations.
  *Done as*: done with US2, in `PreviewLinkSlices.cs`.

  Continue until T064 and T067 pass.
- [X] T073 [P] [US4] Add `preview_link_created` and `preview_link_revoked` to the log actions in `PAGES/Domain/PublishLogEntry.cs` and to the check constraint in `PAGES/Infrastructure/PagesDbContext.cs`, and a nullable `ExpiresAtUtc` (`expires_at_utc`) set only on `preview_link_created` entries. Generate `PAGES/Infrastructure/Migrations/<ts>_AddPreviewLinkLogActions.cs`. Create `PAGES/Features/PublishingHistory/RecordPreviewLinkEvents.cs`, which handles both events for `page`, with the entry id as UUID v5 of the event id, and the label and path taken from the page now. Make the admin history response name the new actions and carry `ExpiresAtUtc`.
  *Done as*: the publish log has no check on `action`, so the migration (`AddPreviewLinkLogExpiry`) only adds `expires_at_utc`. As `RecordDesignPublishHandler` does, the entry id is the event id (not a UUID v5) for idempotency. The handler is `PAGES/Features/PublishingHistory/RecordPreviewLinkEvents.cs`.
- [X] T074 [P] [US4] Add `previewLinkCreated` and `previewLinkRevoked` to `CMS/Domain/ItemHistoryEntry.cs` (a factory that sets `DueAtUtc` to the link's expiry for created entries) and to the kind check in `CMS/Infrastructure/CmsDbContext.cs`. Generate `CMS/Infrastructure/Migrations/<ts>_AddPreviewLinkHistoryKinds.cs`. Create `CMS/Features/Items/History/RecordPreviewLinkEvents.cs`, idempotent in the same way. Continue until T066 passes.
  *Done as*: `item_history` has no kind check either, so Cms needed no migration. The handler is `CMS/Features/Items/GetItemHistory/RecordPreviewLinkEvents.cs`; it uses the event id and tolerates an item already deleted.
- [X] T075 [US4] Create `EDITORM/Infrastructure/PreviewLinkSweepService.cs`, an hourly hosted service with batched conditional deletes, as research PV-19 describes. Register it in `EDITORM/EditorModule.cs`. Continue until T068 passes.
- [X] T076 [US4] In `EDITORM/Features/Transfer/DesignArchivePart.Import.cs`, delete the target site's `preview_links` in the import transaction (PV-20). Add a case to the existing transfer integration tests in `ITESTS/Transfer/`.
  *Done as*: deletes the site's links on every import mode. The test is `ITESTS/Transfer/PreviewLinksImportTests.cs`, which also checks that the archive holds no link.
- [X] T077 [US4] Add the admin parts:
  - **Share dialog**: Revoke (with confirmation) and Revoke all (with confirmation stating
    the count) in `ADMIN/features/preview/SharePreviewDialog.vue`.
  - **Site-wide list**: create `ADMIN/features/preview/PreviewLinksView.vue` (site-wide
    list, paging, Revoke, Revoke all, empty state), with tests written first in
    `ADMINTESTS/features/preview/PreviewLinksView.spec.ts` (axe included).
  - **Navigation**: a route in `ADMIN/app/router.ts`, guarded by
    `editor.preview_links.manage`, and a "Preview links" entry under site settings in
    `ADMIN/features/settings/`.
  - **API**: extend `ADMIN/api/previewLinks.ts`.
  - **History texts**: the history entry texts in `ADMIN/features/publishing/HistoryView.vue`
    and `ADMIN/features/cms/items/ItemHistoryPanel.vue`.
  *Done as*: the site list is linked from the site page beside the publish log (Administrators only) rather than a settings tab, matching how other site-wide Administrator views are reached. Revoke and revoke all in the dialog came with T053.

  Add the `en.json` keys.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 — A member previews a specific past version (Priority: P2)

**Goal**: preview and share any retained published version of a page or item, rendered as a
rollback would publish it now (FR-028, US5 scenarios).

**Independent Test**: quickstart §5. `VersionPreviewTests` (SC-007).

### Tests for User Story 5 ⚠️

- [ ] T078 [P] [US5] Write `ITESTS/Preview/VersionPreviewTests.cs`:
  - for a page with three versions, `?version=1`, `2` and `3` show each version's content
    with today's live header and the bar "Version n, published … by …";
  - the same for an item;
  - a link to version 1 keeps showing version 1 after more draft saves and publishes;
  - after a rollback to version 1, the public page equals the version 1 preview, compared
    as in T026 (SC-007);
  - a version past the 50-version retention gives `preview.version_not_retained` for
    members and "no longer available" for its link;
  - a link created for version N cannot be used to reach the draft or version M.

### Implementation for User Story 5

- [ ] T079 [US5] Handle the `version` query and `PreviewLinkResolution.Found.VersionNumber`:
  - in `REND/Features/Preview/PreviewResponder.cs`, resolve the target through
    `IPreviewContentReader.GetPageVersionAsync` and `IPreviewItemReader.GetVersionAsync`;
  - fill `PreviewBarModel` with the version number, publish time and publisher;
  - use the version's stamp in `PreviewMediaSet`.

  Continue until T078 passes.
- [ ] T080 [US5] Add Preview (a link to `?version=n`) and Share (which opens `SharePreviewDialog` with `versionNumber`) to each version with retained content in `ADMIN/features/publishing/HistoryView.vue` and `ADMIN/features/cms/items/ItemHistoryPanel.vue`. Make `SharePreviewDialog.vue` say "this version N" in its notes. Add the `en.json` keys.

**Checkpoint**: quickstart §5 passes. All stories are complete.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: performance, upgrade proof, the remaining edge cases, registries, docs,
coverage and validation.

- [ ] T081 [P] Write `ITESTS/Performance/PreviewBudgetTests.cs` in the existing performance suite (`docker-compose.perf.yml`): p95 under 500 ms for a member preview and a link preview of a 50-block page with two lists and 30 images, including the image requests (PV-22, SC-003).
- [ ] T082 [P] Write `ITESTS/Preview/PreviewUpgradeTests.cs` from the previous release's snapshot. The three migrations apply, the preview tables are empty, existing history reads unchanged, and a link can be created and opened (Principle III).
- [ ] T083 [P] Write `ITESTS/Preview/PreviewEdgeCaseTests.cs` for what earlier tasks do not cover (quickstart §6):
  - a page using a never-published component on a fresh site shows it from its draft and
    names it in the bar;
  - a never-published design shows the saved tokens;
  - a page with a pending schedule names it in the bar;
  - a link of a deleted collection's item is dead;
  - a site without host names previews and shares.
- [ ] T084 [P] Update the registries named in plan.md "Contract and registry changes":
  - `specs/001-foundation-skeleton/contracts/integration-events.md`: 2 events;
  - `specs/002-identity-setup/contracts/authorization.md`: the capability and the endpoints,
    and every `/_preview` action on the anonymous allow-list (§4) with the reason "access by
    link secret or handler check (022 PV-05, PV-09)". Add the same entries wherever
    `ATESTS/AuthorizationDeclarationTests.cs` reads its allow-list, so T086 passes;
  - `specs/012-publishing-rendering/contracts/`: a pointer to `/_preview` and the
    `PageComposer` note;
  - `specs/017-seo/contracts/`: `Disallow: /_preview/`;
  - `specs/020-site-export-import/contracts/archive-parts.md`: links cleared on import.
- [ ] T085 [P] Write the docs:
  - document `Preview:LinksValidFrom` in `docs/configuration.md`;
  - add an "Invalidate every preview link" procedure and the proxy note to
    `docs/operations.md`;
  - add the three upgrade notes of plan.md to
    `docs/releases/unreleased/22-draft-preview-links.md`;
  - in `docs/backlog.md`, remove the #22 lines (012, 016, 017 and 018 notes) and record
    the plan's Deferred list.
- [ ] T086 Confirm that these pass:
  - `ATESTS/AuthorizationDeclarationTests.cs`;
  - `ATESTS/ModuleBoundaryTests.cs` (no new edges);
  - `PreviewBoundaryTests`;
  - the site-filter coverage test, with `preview_links` filtered and the failure windows
    marked.

  Then confirm line coverage is at least 80% on `PageComposer`, `PreviewResponder`,
  `PreviewLinkResolver`, `CreatePreviewLink` and `LinkPreviewController` (Principle X).
- [ ] T087 Run `dotnet format --verify-no-changes`, `dotnet test`, and `cd src/admin && npm run lint && npm run type-check && npm test`.
- [ ] T088 Walk quickstart.md §1–§8 against `./qb.ps1 dev`, including the keyboard-only and screen-reader pass of §8 (E-5, manual). Record the results in `specs/022-draft-preview-links/quickstart-results.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup.
  - T004–T006 come first (Checkpoint A, no behaviour change).
  - Then T007–T022.
  - It blocks every story.
- **US1 (Phase 3)**: after Foundational.
- **US2 (Phase 4)**: after US1. It reuses `PreviewResponder`, `PreviewContentSource` and the
  bar (T028–T033).
- **US3 (Phase 5)**: after US1. Its form and response tests need US2's link routes for the
  link cases, so finish US2 first or mark those cases pending.
- **US4 (Phase 6)**: after US2 (links must exist).
- **US5 (Phase 7)**: after US1. Its link part needs US2. It is independent of US3 and US4.
- **Polish (Phase 8)**: after the stories that are shipping.

### User Story Dependencies

```text
Setup → Foundational ── US1 ─┬─ US2 ─┬─ US3 (P1)
                             │       └─ US4
                             └─ US5 (link part after US2)
                                                   └── Polish
```

### Within Each User Story

- Tests first, failing.
- Domain and pure rules come before handlers, handlers before controllers, and the server
  before the admin.
- Editor (links), Rendering (rendering) and Pages and Cms (readers and history) tasks inside
  a story are largely independent.

### Parallel Opportunities

- **Phase 2**:
  - T007, T009, T010 and T011 together;
  - the reader tracks (T013 to T016 for Pages, T014 with T017 and T018 for Cms) in
    parallel;
  - T019 and T020 (Media) alongside;
  - T021 and T022.
- **US1**: tests T023–T027 together; then T030, T031 and T032 touch different files; the
  admin work (T036, T037) runs alongside the server work.
- **US2**: tests T039–T043 together; Editor (T044–T050) in parallel with Rendering (T051,
  T052) once T046 defines the contract.
- **US3**: all test tasks T055–T059 together.
- **US4**:
  - tests T064–T068 together;
  - T069 (Identity), T073 (Pages) and T074 (Cms) in parallel.
- **US5**: one test task, then the server and admin tasks in parallel.

---

## Parallel Example: User Story 2

```bash
# Tests first:
Task: "PreviewLink aggregate tests in tests/QuickBuilder.UnitTests/Editor/PreviewLinkTests.cs"
Task: "Link behaviour in tests/QuickBuilder.IntegrationTests/Preview/PreviewLinkTests.cs"
Task: "1,000 tampered links in tests/QuickBuilder.IntegrationTests/Preview/PreviewLinkTamperingTests.cs"
Task: "Throttle in tests/QuickBuilder.IntegrationTests/Preview/PreviewLinkThrottleTests.cs"

# Then two tracks:
Task: "Editor: PreviewLink, migration, resolver, create/list (T044–T050)"
Task: "Rendering: PreviewMediaSet, LinkPreviewController (T051–T052)"
```

---

## Implementation Strategy

### MVP First (US1)

1. Phase 1 and Phase 2. Stop at Checkpoint A and confirm that the `PageComposer`
   extraction left every published page byte-identical and within budget.
2. US1: members see rendered drafts.
3. **Stop and validate** quickstart §1. This is shippable on its own: members finally see
   drafts as visitors will, and nothing reaches anyone outside the site.

### Incremental Delivery

1. MVP (US1).
2. US2 (links for reviewers), then US3 (the isolation proofs and forms). Together these
   complete the P1 scope of the issue.
3. US4 (revocation, the site list, history and sweep), then US5 (version previews).
4. Polish, then `/speckit-analyze` (mandatory) before `/speckit-implement`.

Links without US4 can only expire, not be revoked. The feature is not complete for release
until US4 lands, and the plan delivers everything in one PR.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- Commit after each task or logical group, referencing #22.
- If a task's file path turns out different in the code, record it as `*Done as*:` under the
  task, as 021's tasks.md does.
