---

description: "Task list for 017-seo"
---

# Tasks: SEO, Sitemap and Redirects

**Input**: Design documents from `/specs/017-seo/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for contracts and rendering;
  - 80 % coverage on critical paths (render, publish, redirect matching).
- The spec measures behaviour through automated checks: SC-003 … SC-008.
- Within each story, test tasks come first and must fail before implementation starts.

**Added after analysis (2026-10-13)**: T116 and T117 (US2) and T118 (US5) sit in their
phases out of number order, so that earlier IDs stay stable.

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
- `SITES` = `src/Modules/Sites/QuickBuilder.Sites`
- `SITESC` = `src/Modules/Sites/QuickBuilder.Sites.Contracts`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `MEDIAC` = `src/Modules/Media/QuickBuilder.Media.Contracts`
- `RENDER` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `EDITOR` = `src/admin/src/editor`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`
- `FIX` = `tests/fixtures`

**Conventions**:
- **Backend slices** live under `<MODULE>/Features/<Slice>/<Slice>.cs`. Every controller
  action:
  - is routed as in [contracts/seo-api.yaml](./contracts/seo-api.yaml);
  - declares exactly one `[RequireCapability(...)]` (research SE-03);
  - dispatches through `IDispatcher`;
  - refuses with the codes in [data-model.md §4](./data-model.md);
  - is registered in its module and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **The public path** reads only through Contracts readers on read-only connections, and
  never writes. Redirect usage goes through `IRedirectHits`, which is in memory only
  (SE-12). Block renderers stay synchronous and do no I/O. Data comes in through
  `RenderInputs`.
- **Page structure changes** (and the redirects they offer) go through `PageTree` and
  `PAGES/Infrastructure/PageTreeWrites.cs`, in one transaction with the page `version` check
  (010 FR-010).
- **Generation bumps** go through `PAGES/Infrastructure/GenerationBump.cs` with the explicit
  site predicate. Sitemap bumps go through `PAGES/Infrastructure/SitemapGeneration.cs`
  (SE-15).
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task.
- **Logs and events** carry ids, counts and codes only. They never contain slugs, redirect
  sources or targets, titles or metadata. The one exception is the `oldSlug` field of
  `Cms.ItemRedirectRequested`, which is never logged.

**References**:
- decisions: `SE-xx` in [research.md](./research.md);
- tables, document rules, states, errors, events and cache keys:
  [data-model.md](./data-model.md);
- metadata in documents: [contracts/seo-metadata.md](./contracts/seo-metadata.md);
- structured data types: [contracts/structured-data.md](./contracts/structured-data.md);
- endpoints: [contracts/seo-api.yaml](./contracts/seo-api.yaml);
- the public surface: [contracts/public-seo.md](./contracts/public-seo.md);
- readers and events: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents. No new project references are needed
(module-surface.md §1).

- [X] T001 Commit `specs/017-seo/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `017-seo`, referencing #17.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the `seo` prop in the page schema, with rules 22–25 on both sides;
- the three migrations;
- the Contracts interfaces and DTO additions;
- the capabilities and error codes;
- the `SeoDocument` parser used by Rendering.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Page schema (FR-001, FR-010, SE-02)

- [X] T002 Extend `CONTRACTS/page-schema/v1.schema.json`, staying at version 1:
  - an optional `seo` prop on `root` with `$defs` `seo`, `seoBind`, `seoFallback` and `structuredDataMapping`, with the keys and limits of data-model.md §2;
  - `seoBind` values reuse `$defs/binding`, with `empty` restricted to `"fallback"`;
  - `structuredDataMapping.type` is an enum of `Article`, `Product`, `Event`, and its `bind` is an object of `binding` values.

  Use only the vocabulary in page-schema.md §7. `PageSchemaSubsetTests` must pass.
- [X] T003 Update `CONTRACTS/page-schema.md` from `specs/017-seo/contracts/seo-metadata.md`: the §2 `root` row, rules 22–25 in §3 (after rule 21, with rule 23 as a broken-binding note, not a refusal), and an "Additions in 017 (still version 1)" paragraph in §6. Run `ITESTS/Pages/PageSchemaContractTests.cs`; it must pass.
- [X] T004 [P] Create golden examples in `CONTRACTS/page-schema/examples/`. Use the eight files in seo-metadata.md §5, each invalid one with `expected.json` listing `rule` and `pointer`.

### Validators, server and admin

- [X] T005 [P] Create `UTESTS/Pages/SeoSchemaGoldenTests.cs`. It runs T004's examples through `PAGES/Infrastructure/Schema/PageDocumentValidator.cs` and also checks:
  - rule 22 for component documents;
  - that `seo.bind` on an ordinary page is accepted (rule 23).

  It must fail first.
- [X] T006 [P] Create `ADMINTESTS/editor/seoSchema.spec.ts`, running the same examples through `EDITOR/schema/documentValidator.ts`. It must fail first.
- [X] T007 Implement rules 22, 24 and 25 in `PAGES/Infrastructure/Schema/PageDocumentValidator.cs`. Rule 22 applies only to component documents. Rule 13 (`bindingVersion`) must also count `seo.bind` and `seo.structuredData.bind`. Make T005 pass.
- [X] T008 Mirror rules 22, 24 and 25 and the rule 13 extension in `EDITOR/schema/documentValidator.ts` and `EDITOR/schema/schema.ts`. Make T006 pass.

### Database (SE-23)

- [X] T009 [P] Add `pages.redirects`, `pages.redirect_usage`, `pages.redirect_changes` and `pages.sitemap_state`, and `publish_schedules.redirect_to_page_id`, to `PAGES/Infrastructure/PagesDbContext.cs`:
  - entities in `PAGES/Domain/Redirects/Redirect.cs` (state only for now), `PAGES/Domain/Redirects/RedirectChange.cs` and `PAGES/Domain/SitemapState.cs`, and the new property on `PAGES/Domain/PublishSchedule.cs`;
  - columns, check constraints and indexes exactly as data-model.md §1.1–§1.3, §1.6 and §1.7;
  - the site filter on all four tables.

  Create migration `PAGES/Infrastructure/Migrations/<ts>_AddSeo.cs`. It inserts one `sitemap_state` row (generation 1) per site that has pages.
- [X] T010 [P] Add the eight columns of data-model.md §1.4 (including `search_updated_by`) to `SITES/Domain/Site.cs` and `SITES/Infrastructure/SitesDbContext.cs`, and create migration `SITES/Infrastructure/Migrations/<ts>_AddSearchSettings.cs`. Defaults are null or false, so `redirect_other_hosts` is false for existing rows. In `SITES/Features/CreateSite/CreateSite.cs`, set `redirect_other_hosts = true` for new sites (SE-07).
- [X] T011 [P] Add `redirect_old_address boolean not null default false` to `cms.item_schedules` in `CMS/Infrastructure/CmsDbContext.cs` and its entity, and create migration `CMS/Infrastructure/Migrations/<ts>_AddScheduleRedirect.cs`. It also adds a nullable `redirect_to_page_id uuid` for scheduled unpublish (data-model.md §1.5).

### Contracts (module-surface.md §2–§5)

- [X] T012 [P] Create `PAGESC/Redirects/IRedirectReader.cs` (with `RedirectMatch` and `RedirectTargetKind`), `PAGESC/Redirects/IRedirectHits.cs` and `PAGESC/Publishing/ISitemapReader.cs` (with `SitemapPages`, `SitemapPage` and `SitemapTemplate`), exactly as module-surface.md §2. Add `GetLiveAncestorsAsync` and `LiveAncestor` to `PAGESC/Publishing/IPublishedContentReader.cs`.
- [X] T013 [P] Add `SiteSearchSettings` and the `Search` and `RedirectOtherHostNames` init properties to `SITESC/SiteDto.cs`. Fill them in `SITES/Infrastructure/SiteDtoMapper.cs` (projection included).
- [X] T014 [P] Add `ListAddressesAsync` and `PublishedItemAddress` to `CMSC/Publishing/IPublishedItemReader.cs`, and create `CMSC/Events/ItemRedirectRequested.cs` (module-surface.md §3). Add stub implementations that throw `NotImplementedException` in `CMS/Infrastructure/PublishedItemReader.cs`, to be completed in US3.
- [X] T015 [P] Add `string? AltText = null` to `PublicMediaFile` in `MEDIAC/IPublicMediaReader.cs`, and fill it from the file's alternative text in `MEDIA/Infrastructure/PublicMediaReader.cs`. Extend the existing public media reader test to assert it.
  *Done as*: there is no direct public media reader test; `og:image:alt` in `PageMetadataRenderTests` (T021) asserts it through the published page.

### Capabilities and errors (SE-03, data-model §4)

- [X] T016 Add the capabilities:
  - `pages.redirects.manage` (Designer) in `PAGES/PagesCapabilities.cs`;
  - `sites.search.manage` (Designer) and `sites.search_visibility.manage` (no role, so Administrator only) in `SITES/SitesCapabilities.cs`.

  Record all three in `specs/002-identity-setup/contracts/authorization.md` §2.
- [X] T017 [P] Add every error code of data-model.md §4 to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs` (HTTP statuses as listed), and `sites.search_invalid` to the Sites errors (`SITES/ErrorResults.cs`). Add admin messages for each in `ADMIN/features/seo/redirectErrors.ts` (new), `ADMIN/features/pages/pageErrors.ts` and `ADMIN/features/sites/siteErrors.ts`, with `ADMIN/i18n/en.json`.

### Rendering core

- [X] T018 [P] Create `UTESTS/Rendering/Seo/SeoDocumentTests.cs`. It must fail first. It checks that `RENDER/Seo/SeoDocument.cs`:
  - parses `root.props.seo` from a live document into an immutable record;
  - tolerates an absent `seo`;
  - ignores unknown keys;
  - exposes `bind`, `fallback` and `structuredData` raw for the resolver.
- [X] T019 Implement `RENDER/Seo/SeoDocument.cs`. Add `Seo` (`SeoDocument?`) and `Site` (`SiteDto`) to `RENDER/Domain/RenderInputs.cs`, and pass them from `RENDER/Features/RenderPage/PageResponder.cs` without changing output yet. Make T018 pass. Existing golden HTML tests stay green.

**Checkpoint**: schema, migrations and contracts are in place. User stories can start.

---

## Phase 3: User Story 1 - Setting search and sharing metadata for a page (Priority: P1) 🎯 MVP

**Goal**: a published page carries its meta title, description, Open Graph fields,
canonical address and robots directive, with fallbacks, edited in the editor and in the
site's search settings.

**Independent Test**: quickstart §1. Set every field, leave each optional field empty in
turn, and publish. Draft changes are invisible until publish, and hostile values are encoded.

### Tests for User Story 1 ⚠️

- [X] T020 [P] [US1] Create `UTESTS/Rendering/Seo/HeadMetadataGoldenTests.cs` with golden files in `UTESTS/Rendering/Seo/golden/head/`. It covers:
  - every field and every fallback of SE-05 (title suffix, `og:title` → title, `og:description` → description, `og:image` → site default → none);
  - no empty tags;
  - `og:locale` for `en`, `pt-BR` and `vi`;
  - image variant choice (the largest ≤1200 px);
  - absolute addresses with a base path;
  - `og:image:alt`;
  - a title of `"><script>x</script>` encoded in every attribute.
- [X] T021 [P] [US1] Create `ITESTS/Rendering/PageMetadataRenderTests.cs`:
  - a published page shows its metadata;
  - a draft change does not show until publish;
  - rollback restores the previous metadata;
  - a site title suffix change shows on every page at the next request, with no republish (`SiteVersion`);
  - a deleted sharing image leaves the page working without `og:image`.
- [X] T022 [P] [US1] Create `ITESTS/Pages/SeoDraftPermissionTests.cs` (FR-002, SE-03):
  - an Editor saving a draft that changes `seo.canonical`, `seo.noindex` or `seo.bind.noindex` gets `403 pages.seo_restricted` naming the field;
  - an Editor saving a draft with those values unchanged succeeds;
  - a Designer can change them.
- [X] T023 [P] [US1] Create `ITESTS/Sites/SearchSettingsTests.cs` for `PUT /api/v1/sites/{id}/search-settings`:
  - suffix, image and organisation values are saved;
  - limits give `400 sites.search_invalid`;
  - a stale version gives `409 sites.out_of_date`;
  - Designers are allowed, Editors are refused, and a member of another site gets 404.
- [X] T024 [P] [US1] Create `ITESTS/Pages/SiteImageRefreshTests.cs` (SE-20): replacing or deleting the site's default sharing image bumps every live page of that site and no page of another site. Replacing a page's own `seo.ogImage` bumps that page through its media dependency.
- [X] T025 [P] [US1] Create `ADMINTESTS/editor/seo/SeoPanel.spec.ts`. It checks:
  - edits go through editor ops (undo and redo work);
  - the 60/160 character warnings appear;
  - values over the hard maximum are refused;
  - search and sharing previews update;
  - canonical and hide controls are disabled for a member without `pages.structure.manage`;
  - keyboard-only use, and axe.

### Implementation for User Story 1

- [X] T026 [US1] Create `RENDER/Seo/HeadMetadata.cs`, `RENDER/Seo/OgLocale.cs` and `RENDER/Seo/HeadBuilder.cs`. `HeadBuilder` builds `HeadMetadata` from `RenderInputs` (`Seo`, `Site`, title, media files) with the SE-05 fallbacks. For now the canonical address is the page's own address on `Site.BaseUrl`; US6 adds overrides and page parameters.
- [X] T027 [US1] Extend `RENDER/Rendering/PageShell.cs` to write the head tags in the order of public-seo.md §3 through `HtmlWriter.Attribute`, and call `HeadBuilder` from `RENDER/Features/RenderPage/PageResponder.cs`. Fetch `seo.ogImage`, the site's default image and the organisation logo with the page's other media in one `IPublicMediaReader` call. Make T020 and T021 pass.
- [X] T028 [US1] Add `seo.ogImage` media and `seo.canonical` page targets to `PAGES/Domain/DependencyExtractor.cs` (SE-20).
- [X] T029 [US1] In `PAGES/Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs`, read the site through `ISiteDirectory`. When the file is the site's `Search.DefaultImageId` or `Search.OrganisationLogoId`, bump every live page of the site through `GenerationBump`, staying idempotent through `processed_events`. Make T024 pass.
- [X] T030 [US1] Enforce restricted fields in `PAGES/Features/SavePageDraft/SavePageDraft.cs`. Compare `seo.canonical`, `seo.noindex` and `seo.bind.noindex` with the current draft, and refuse `pages.seo_restricted` when they changed and the member lacks `pages.structure.manage`. Make T022 pass.
- [X] T031 [US1] Create `SITES/Features/UpdateSearchSettings/UpdateSearchSettings.cs` (request, validator, handler, controller with `sites.search.manage`), modelled on `SITES/Features/UpdateSettings/UpdateSettings.cs`, with `Site.ChangeSearchSettings(...)` in `SITES/Domain/Site.cs`, which records `search_updated_by` (FR-021). Add `search` and `searchVisibility` to `SITES/Infrastructure/SiteResponse.cs`. Make T023 pass, and extend T023 to assert `search_updated_by`.
- [X] T032 [P] [US1] Create `EDITOR/seo/seoFields.ts` (limits, warning thresholds, fallback rules mirroring SE-05), `EDITOR/seo/SearchPreview.vue`, `EDITOR/seo/SharingPreview.vue` and `EDITOR/seo/SeoPanel.vue`. The panel has fields, counters, the media picker with a missing-file mark when the sharing image no longer exists (spec edge case), and "Hide from search engines" and "Leave out of sitemap" switches. Edits go to `root.props.seo` through the editor's ops in `EDITOR/state/editorStore.ts`.
- [X] T033 [US1] Add a "Search and sharing" button to `EDITOR/EditorTopBar.vue` that opens `SeoPanel`. Disable the restricted controls without `pages.structure.manage`, and show the reason. Add i18n keys. Make T025 pass.
- [X] T034 [P] [US1] Create `ADMIN/features/sites/SearchSettingsPanel.vue` (title suffix, default sharing image, organisation name and logo, with a missing-file mark) and add it to `ADMIN/features/sites/SiteDetailView.vue`. Add `updateSearchSettings` to `ADMIN/api/sites.ts`, and the spec `ADMINTESTS/features/sites/SearchSettingsPanel.spec.ts` with axe.

**Checkpoint**: quickstart §1 passes. Published pages are presentable in search results and
link previews.

---

## Phase 4: User Story 2 - Keeping old links working with redirects (Priority: P1)

**Goal**: Designers manage, import and export exact and prefix redirects, including
query-string sources. Visitors are redirected in the FR-016 order, and usage is counted
without public writes.

**Independent Test**: quickstart §2. Run CRUD, import and export. Then request each source
and confirm the status, target, query handling and the content-wins rule, on two instances.

### Tests for User Story 2 ⚠️

- [X] T035 [P] [US2] Create `UTESTS/Pages/Redirects/RedirectRulesTests.cs` and `UTESTS/Pages/Redirects/CanonicalQueryTests.cs` (SE-09). They cover:
  - source normalisation (case, trailing slash, dot segments refused);
  - refused reserved and platform sources and `/` without a query;
  - canonical queries (order, decoding and re-encoding, case of names kept, 512 limit);
  - query only on `exact`;
  - target validation (schemes, user info, protocol-relative, prefix with `url` or `item`);
  - chain shortening up to 10 steps;
  - loops, including a prefix matching its own target.
- [X] T036 [P] [US2] Create `UTESTS/Pages/Redirects/RedirectCsvTests.cs`. It covers:
  - RFC 4180 quoting, embedded commas, quotes and line breaks;
  - a BOM, CRLF and LF line ends;
  - header detection;
  - `kind` and `match` spellings;
  - malformed rows;
  - the export formula guard (`=`, `+`, `-`, `@`, tab, CR).
- [X] T037 [P] [US2] Create `ITESTS/Pages/RedirectEndpointsTests.cs` for every redirect operation in seo-api.yaml:
  - list search, state filter, sort, paging;
  - create, get, update (version conflict), delete;
  - turning off;
  - `duplicate` and `loop` naming the other redirect;
  - `shortened` set when a chain was shortened;
  - `limit_reached` at 10,000;
  - the `hidden` and `broken` states;
  - a path target equal to a page's address stored as `page`;
  - authorization (Designer allowed, Editor 403, other site 404).
- [X] T038 [P] [US2] Create `ITESTS/Pages/RedirectImportTests.cs`. It covers:
  - 297 valid rows + 2 duplicates + 1 loop → `added: 297` with three line reports;
  - duplicates within the file;
  - a Latin-1 file → `400 redirects.import_encoding`, with nothing added;
  - over 1 MiB or 5,000 rows → 413;
  - over the site limit → 409 for the whole file;
  - an export that re-imports as all duplicates;
  - a refused row's detail never echoes its content.

  Add the fixture `FIX/redirects-300.csv`.
- [X] T039 [P] [US2] Create `ITESTS/Rendering/RedirectMatchingTests.cs` for the order and responses of public-seo.md §1–§2:
  - query redirect before a live page (`/?p=12` with a live home page);
  - a live page winning over an exact redirect;
  - an item page winning;
  - exact before prefix, and the longest prefix;
  - the prefix remainder;
  - query carry rules (carried, not carried from a query source, not carried when the target has a query);
  - `?ref=mail&id=8` not matching `?id=8`;
  - 301 and 302 headers;
  - a broken page target → not-found bytes;
  - a disabled redirect ignored;
  - a change visible at the next request on a second instance.
- [X] T040 [P] [US2] Create `ITESTS/Pages/RedirectUsageTests.cs` (SE-12). It covers:
  - hits counted and flushed on a forced flush;
  - two instances adding up;
  - `last_used_at` keeping the latest time;
  - a redirect deleted before the flush is ignored;
  - the request path making no database write (assert with an EF interceptor counting commands on the writable context during redirect requests).
- [X] T041 [P] [US2] Create `ADMINTESTS/features/seo/RedirectsView.spec.ts`, `RedirectDialog.spec.ts` and `RedirectImportDialog.spec.ts`. They cover:
  - the list with states and usage sort;
  - the create and edit dialog with page, item, path and URL targets, match kind and query warnings (the source path holds live content, or the query uses a list's page parameter);
  - the import report per line;
  - export;
  - keyboard only, and axe.

### Implementation for User Story 2

- [X] T042 [US2] Implement `PAGES/Domain/Redirects/RedirectSource.cs`, `CanonicalQuery.cs` and `RedirectRules.cs` (pure: normalisation, validation, chain shortening, loop detection over a resolver delegate), and complete `PAGES/Domain/Redirects/Redirect.cs` with `Create`, `Change`, `TurnOn` and `TurnOff` enforcing the rules. Make T035 pass.
- [X] T043 [US2] Implement `PAGES/Infrastructure/RedirectWrites.cs`:
  - load the site's redirect sources for chain and loop checks;
  - resolve `page` and `item` targets (Pages tree; `IPublishedItemReader` for items);
  - store a path equal to a page's current address as `page`;
  - store a `url` on the site's primary host name and base path as `path` (then `page`), and keep a `url` on another host name of the site absolute (SE-09);
  - enforce the 10,000 limit;
  - save with the `xmin` concurrency check.
- [X] T044 [US2] Implement `PAGES/Infrastructure/RedirectCsv.cs` (an in-house RFC 4180 reader and writer, with UTF-8 validation and the formula guard). Make T036 pass.
- [X] T045 [US2] Create the slices in `PAGES/Features/Redirects/`:
  - `ListRedirects.cs` (search, state, sort and paging; `hidden` and `broken` computed for the page of results through `IPublishedContentReader` and the item reader; usage joined from `redirect_usage`);
  - `GetRedirect.cs`, `CreateRedirect.cs`, `UpdateRedirect.cs` and `DeleteRedirect.cs`;
  - `ImportRedirects.cs` (a `text/csv` body limited to 1 MiB, all valid rows in one transaction, line reports);
  - `ExportRedirects.cs`;
  - `RedirectResponses.cs`.

  Every action has `pages.redirects.manage`. Register them in `PAGES/PagesModule.cs` and `ITESTS/Infrastructure/EndpointCatalog.cs`. Make T037 and T038 pass.

  *Done as*: get, create, update and delete share `ManageRedirects.cs` (one handler class, one controller); `RedirectViews` in `RedirectResponses.cs` builds states, addresses and warnings; import and export are `RedirectFiles.cs`. The endpoint catalog finds endpoints by itself. The CSV reader and writer are pure, so they sit in `PAGES/Domain/Redirects/RedirectCsv.cs` beside the rules. `CanonicalQuery` moved to `PAGESC/Redirects/` because Rendering canonicalises request queries the same way. Cms gained `ICollectionSchemaReader.GetItemAddressPartsAsync` for item targets.
- [X] T046 [US2] Implement `PAGES/Infrastructure/RedirectReader.cs` (`IRedirectReader`) on `PublishedReadDbContext`:
  - `FindQueryAsync` is one indexed lookup;
  - `FindAsync` is one query: the exact match without a query, else `source_path = ANY(@ancestors)` with `match_kind = 'prefix'`, ordered by length descending, `LIMIT 1`.

  Enabled rows only. Register it as scoped.
- [X] T047 [US2] Implement `PAGES/Infrastructure/RedirectHitCounter.cs` (an `IRedirectHits` singleton over a `ConcurrentDictionary`, never throwing) and `PAGES/Infrastructure/RedirectUsageFlusher.cs`, a `BackgroundService` that:
  - swaps the counters every 60 s and on `StopAsync`;
  - upserts `hits = hits + n` and `last_used_at = greatest(...)` per site through `PagesDbContext`, using LINQ and `ExecuteUpdate` plus inserts, with no raw SQL;
  - skips deleted redirects.

  Register both in `PAGES/PagesModule.cs`, with a test hook to flush. Make T040 pass.
- [X] T048 [US2] Create `RENDER/Features/RenderPage/RedirectResponder.cs`. It builds the 301 or 302 `PublicResponse` (empty body, `Location` with base path, current target address, prefix remainder and query rules; `Cache-Control` per public-seo.md §2) and calls `IRedirectHits.Record`. Item targets resolve through `IPublishedContentReader.GetLiveTemplatePatternsAsync` and `IPublishedItemReader.GetAsync`. A target that is not live gives null, so the caller serves the not-found page.
- [X] T049 [US2] Change `RENDER/Features/RenderPage/PageResponder.cs` and `RenderPageQuery.cs` to match in the FR-016 order:
  - pass the full query to compute the canonical query;
  - try `FindQueryAsync` first when the query is non-empty;
  - after the page and item lookups miss, try `FindAsync`;
  - fall back to the not-found page.

  Make T039 pass. Existing `RouteMatchingTests` stay green.
- [X] T050 [P] [US2] Create `ADMIN/features/seo/RedirectsView.vue`, `RedirectDialog.vue` (target pickers reuse the page picker and 015's item picker) and `RedirectImportDialog.vue`, with `ADMIN/api/redirects.ts`. Add the route `/sites/:id/redirects` in `ADMIN/app/router.ts` and a navigation entry for members holding `pages.redirects.manage`. Add i18n keys. Make T041 pass.
- [X] T116 [US2] Add the redirect warnings of data-model.md §4 (added after analysis, C1, C2):
  - `CreateRedirect.cs` and `UpdateRedirect.cs` return `warnings`: `source_holds_content` when a query source's path is a live page or item page, and `source_uses_page_param` when a parameter is named like a paginated list's parameter there;
  - page create, slug change, move, home change, template removal and publish responses (`PAGES/Features/PageResponses.cs`, `PublishingResponses.cs`) carry `redirect_hidden`, naming the redirect, when the page's live address is the source of an enabled redirect without a query.

  Tests go in `ITESTS/Pages/RedirectWarningTests.cs`, written first. `ADMIN/features/seo/RedirectDialog.vue` and `ADMIN/features/pages/PageTreeView.vue` show the warnings, with i18n keys.
- [X] T117 [US2] Record every redirect action in `pages.redirect_changes` (research SE-13a, FR-021): create, change, turn on or off, delete and import in `RedirectWrites.cs`, and offers and retargeting in T079 and T082, in the same transaction, with ids, the action, the origin, the actor and the time only. `GetRedirect.cs` returns the last 20 as `history`. Tests go in `ITESTS/Pages/RedirectAuditTests.cs`, written first. They check every action, a delete leaving its rows, and no source, target or note stored.

**Checkpoint**: quickstart §2 passes. Old links work through managed and imported redirects.

---

## Phase 5: User Story 3 - A sitemap and robots.txt that keep themselves current (Priority: P1)

**Goal**: each site serves an exact, fresh `sitemap.xml` (an index when large) and
`robots.txt`. Administrators can discourage search engines and add robots rules.

**Independent Test**: quickstart §3. Check the sitemap content and freshness after every
kind of change, on two instances. Check the index split at 50,001 addresses, robots.txt, and
discouraging.

### Tests for User Story 3 ⚠️

- [X] T051 [P] [US3] Create `ITESTS/Rendering/SitemapTests.cs`. It covers:
  - published, draft-only, unpublished, hidden, `noSitemap` and canonical-override pages;
  - pages under an unpublished parent;
  - a live template with items in every state;
  - `lastmod` for pages and items (the later of item and template);
  - ordering;
  - XSD validation against `FIX/sitemap-0.9.xsd` (add it) and `siteindex.xsd`;
  - 50,001 addresses → an index with two files;
  - `sitemap-3.xml` → not found;
  - `sitemap-1.xml` on a single-file site → not found;
  - a base path;
  - ETag and `304`;
  - a disabled site → the site-not-resolved answer.
- [X] T052 [P] [US3] Create `ITESTS/Rendering/SitemapFreshnessTests.cs` (SE-15). Each of these bumps the generation and shows at the next request on a second instance:
  - page publish, unpublish, rollback and scheduled publish;
  - slug change, move and home change;
  - template set and remove;
  - page delete;
  - `Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted`, `CollectionChanged` (`itemsRewritten`) and `CollectionDeleted`.

  Redelivered events bump at most once. Measure the end-to-end delay of an item publish (SC-005: under 5 s).
- [X] T053 [P] [US3] Create `ITESTS/Rendering/RobotsTests.cs`. It covers:
  - the default text with an absolute `Sitemap:` line;
  - custom rules after the generated block;
  - discouraged → `Disallow: /` with the sitemap line kept;
  - a base path;
  - ETag;
  - a change visible at the next request.
- [X] T054 [P] [US3] Create `ITESTS/Sites/SearchVisibilityTests.cs` for `PUT /api/v1/sites/{id}/search-visibility`:
  - Administrator only (Designer 403);
  - robots rules limits (10 KB, 500-character lines, control characters) → `400 sites.search_invalid`;
  - a stale version → 409;
  - discouraging gives `noindex` meta and `X-Robots-Tag` on pages, item pages, sitemap and robots, and an empty `urlset`.
- [X] T055 [P] [US3] Create `ADMINTESTS/features/sites/SearchVisibilityPanel.spec.ts` and `ADMINTESTS/components/SearchHiddenBanner.spec.ts`. They cover:
  - the discourage switch and robots rules editor;
  - the banner shown on every site screen while discouraged, for every role;
  - axe.

### Implementation for User Story 3

- [X] T056 [US3] Create `PAGES/Infrastructure/SitemapGeneration.cs` (`BumpAsync(siteId)` in the caller's transaction, creating the row when missing). Call it from:
  - `PAGES/Infrastructure/PublishingWrites.cs` (publish, rollback, unpublish);
  - `ScheduleWrites.cs` (scheduled runs);
  - `PageTreeWrites.cs` (slug, move, home, template set or remove, delete, when live pages or templates are affected);
  - the existing handlers `RefreshPagesListingItemsHandler.cs` (`Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted` with `wasPublished`) and `RefreshPagesUsingCollectionHandler.cs` (`CollectionChanged` with `itemsRewritten`, `CollectionDeleted`).
- [X] T057 [US3] Implement `PAGES/Infrastructure/SitemapReader.cs` (`ISitemapReader`) on `PublishedReadDbContext`:
  - `GetGenerationAsync`;
  - `GetPagesAsync`, one query over live pages and templates. It projects path or pattern, `LiveSinceUtc`, collection, and the `seo.noindex`, `seo.noSitemap` and `seo.canonical` flags and the `seo.bind.noindex.value.field` id from the live document's `jsonb` through EF JSON translation.

  `Indexable` follows data-model §3.1, without the site flags. Register it.
- [X] T058 [US3] Implement `ListAddressesAsync` in `CMS/Infrastructure/PublishedItemReader.cs`: keyset by id, published items of the collection, slug, `LastPublishedAtUtc`, and `Hidden` = the published boolean value of `hideFieldId` is true (EF JSON translation, no raw SQL). Add `ITESTS/Cms/PublishedItemAddressesTests.cs` covering paging, the hide flag (true, false, missing, non-boolean) and site isolation.
- [X] T059 [US3] Create `RENDER/Features/Sitemap/SitemapKey.cs`, `SitemapBuilder.cs` and `SitemapController.cs`:
  - routes `GET`/`HEAD` `sitemap.xml` and `sitemap-{n:int}.xml`, anonymous, ordered before the page catch-all;
  - `SitemapBuilder` reads the generation (key first), then builds every entry: pages by path, then each indexable template's items by id, with absolute canonical addresses from `SiteDto.BaseUrl`;
  - the files are split at 50,000 and written with `XmlWriter`;
  - each file is cached under its key with size accounting;
  - a discouraged site gives an empty `urlset`.

  Make T051 and T052 pass.
- [X] T060 [US3] Create `RENDER/Features/Robots/RobotsText.cs` and `RobotsController.cs` (SE-16), cached under `robots:{siteVersion}:{base}:{stamp}`. Make T053 pass.
- [X] T061 [US3] Implement discouraging in Rendering:
  - `HeadBuilder` writes the robots meta when `Site.Search.Discouraged`;
  - `PageResponder`, `SitemapController` and `RobotsController` add `X-Robots-Tag: noindex, nofollow`;
  - the not-found page is unchanged.
- [X] T062 [US3] Create `SITES/Features/UpdateSearchVisibility/UpdateSearchVisibility.cs` (`sites.search_visibility.manage`; discourage, robots rules normalised to `\n`, and `redirectOtherHostNames` stored, enforced in US6), with `Site.ChangeSearchVisibility(...)`, which records `search_updated_by`. Make T054 pass.
- [X] T063 [P] [US3] Create `ADMIN/features/sites/SearchVisibilityPanel.vue` (discourage switch, robots rules textarea with limits; the host redirect switch arrives in T087) and `ADMIN/components/SearchHiddenBanner.vue`, shown by the site shell when `searchVisibility.discourageSearchEngines`. Add `updateSearchVisibility` to `ADMIN/api/sites.ts` and i18n keys. Make T055 pass.
- [X] T064 [US3] Add `robots.txt` and the sitemap paths to the platform paths documented in `PAGESC/PageAddresses.cs` remarks and `CONTRACTS/page-schema.md` §5 (impossible slugs, so not reserved). Extend `ITESTS/Rendering/PublicRouteBoundaryTests.cs` to assert they are answered before the catch-all.

**Checkpoint**: quickstart §3 passes. All three P1 stories are complete.

---

## Phase 6: User Story 4 - Metadata templates for collection items (Priority: P2)

**Goal**: on a template, metadata fields and "Hide from search engines" bind to item fields
with 016's bindings, so each item page has its own metadata, and items can be hidden one by
one.

**Independent Test**: quickstart §4. Bind every metadata field, then check items whose values
are present, empty or too long, broken bindings, and the hide switch in the head and the
sitemap.

### Tests for User Story 4 ⚠️

- [X] T065 [P] [US4] Add golden cases to `CONTRACTS/binding-expression/examples/resolve/` (`seo-*.json`). They cover:
  - a text template title;
  - a rich text description (plain text);
  - whitespace collapsing;
  - cutting at a word with `…` (and one over-long word);
  - an empty value → fallback text → default;
  - a broken binding;
  - a single-reference value;
  - the hide switch: true, false, empty, non-boolean.
- [X] T066 [P] [US4] Extend `UTESTS/Rendering/Binding/BindingRulesGoldenTests.cs` and `ADMINTESTS/editor/bindings/bindingRules.spec.ts` to run the `seo-*` cases and the new compatibility rows (seo-metadata.md §3) on both sides. Create `UTESTS/Rendering/Seo/PlainTextTests.cs` for the rich text conversion and cutting. Both must fail first.
- [X] T067 [P] [US4] Create `ITESTS/Rendering/SeoTemplateRenderTests.cs`. It covers:
  - item pages show bound metadata from published data;
  - the per-item key moves only for the republished item;
  - removing a bound field marks it broken in the editor response and uses the fallback on the site;
  - `seo.bind` on an ordinary page renders as unbound (rule 23);
  - the hide switch writes `noindex` for that item only and leaves the sitemap;
  - the other items stay.
- [X] T068 [P] [US4] Extend `ADMINTESTS/editor/seo/SeoPanel.spec.ts` for templates:
  - each metadata field offers `BindingControl` with compatible fields only;
  - a fallback text input;
  - the hide switch bindable to boolean fields;
  - the broken mark;
  - the canvas preview of the sample item's values.

### Implementation for User Story 4

- [X] T069 [US4] Add the metadata rows of seo-metadata.md §3 to `CONTRACTS/binding-expression.md` §4, and to the compatibility tables in `RENDER/Binding/BindingRules.cs` and `EDITOR/bindings/bindingRules.ts`.
- [X] T070 [US4] Create `RENDER/Seo/PlainText.cs` (rich text to plain text, whitespace collapsing, word-boundary cutting with `…`). Make `PlainTextTests` pass.
- [X] T071 [US4] Resolve metadata bindings in `RENDER/Seo/HeadBuilder.cs`:
  - use `BindingResolver` and `ValueFormatter` with the page scope item from `RenderInputs.Data`;
  - include `seo.bind` field ids in the `BindingFetcher` plan (`RENDER/Binding/BindingFetcher.cs`) so values are fetched before rendering;
  - apply the fallback chain (fallback text, then the SE-05 default);
  - read `seo.ogImage` media ids from bound image values;
  - treat `seo.bind.noindex` true as `noindex`.

  Record broken metadata bindings in `RenderContext.BrokenBindings` with node id `root`. Make T065 to T067 pass.
- [X] T072 [US4] Extend the sitemap: `SitemapBuilder` passes `SitemapTemplate.HideFieldId` to `ListAddressesAsync` and leaves out hidden items. Item publishes already bump the generation (T056). Extend `SitemapTests` with a hidden item.
- [X] T073 [US4] In `EDITOR/seo/SeoPanel.vue`, when the page is a template, put the existing `EDITOR/panel/controls/BindingControl.vue` (scope `page`) beside each metadata field and "Hide from search engines", with a fallback input and broken marks. Show resolved values for the sample item in the previews. Make T068 pass.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 - Being offered a redirect when an address changes (Priority: P2)

**Goal**: every change that moves or removes a live address offers redirects. Page changes
apply them in the same transaction. Item changes apply them through
`Cms.ItemRedirectRequested`.

**Independent Test**: quickstart §5. Accept and decline the offer for every FR-028 change.
Check prefix redirects for template moves, retargeting, loops turned off, and the Editor's
item offer.

### Tests for User Story 5 ⚠️

- [X] T074 [P] [US5] Create `UTESTS/Pages/Redirects/OfferedRedirectsTests.cs` for `PAGES/Domain/Redirects/OfferedRedirects.cs`. Given a tree before and after a change, plus the live states, it checks:
  - exact redirects for changed live pages;
  - a prefix redirect for each live template whose pattern changed;
  - a prefix to the parent when a template is removed;
  - nothing for a collection change;
  - old addresses now held by live content skipped;
  - `path` redirects retargeted;
  - loops turned off.
- [X] T075 [P] [US5] Create `ITESTS/Pages/RedirectOfferTests.cs` (SC-003). For `UpdatePage` (slug), `MovePage`, `MakeHomePage`, `SetPageTemplate` (set, remove) with the flag on and off, it checks:
  - the old addresses reach the new content in one hop from the next request (same transaction: no window);
  - the response's `redirects` outcome;
  - moving a template back turns the earlier prefix off and lists it in `turnedOff`;
  - `Unpublish` and `DeletePage` with `redirectToPageId`;
  - flag omitted = no redirect (API compatibility).
- [X] T076 [P] [US5] Create `ITESTS/Cms/ItemRedirectOfferTests.cs`. It checks:
  - an Editor publishing a slug change with `redirectOldAddress` gets an exact redirect old item address → item target in under 5 s (FR-029; measure and record it);
  - the same for rollback, a scheduled publish (the flag kept on the schedule) and unpublish or delete with `redirectToPageId`;
  - no event when the slug did not change or the flag is off;
  - no redirect when the collection has no live template;
  - a redelivered event creates one redirect;
  - the event payload has the documented fields only, and `oldSlug` never appears in logs.
- [X] T077 [P] [US5] Create `ADMINTESTS/components/RedirectOfferCheckbox.spec.ts` and extend `ADMINTESTS/features/pages/MoveDialog.spec.ts`, `PageFormDialog.spec.ts` and `TemplateDialog.spec.ts`, and the item form spec. They check:
  - the checkbox is shown and ticked only when the change moves live addresses;
  - "Redirect this address to…" in the unpublish and delete confirmations, unticked by default;
  - the outcome note after saving;
  - axe.

### Implementation for User Story 5

- [X] T078 [US5] Implement `PAGES/Domain/Redirects/OfferedRedirects.cs` (pure, data-model §3.4). Make T074 pass.
- [X] T079 [US5] Add `redirectOldAddresses` to the requests and commands of `PAGES/Features/UpdatePage/UpdatePage.cs`, `MovePage/MovePage.cs`, `MakeHomePage/MakeHomePage.cs` and `SetPageTemplate/SetPageTemplate.cs` (body for PUT; query for DELETE). In `PAGES/Infrastructure/PageTreeWrites.cs`:
  - compute the offered redirects from the tree change and live states;
  - insert, retarget and turn off through `RedirectWrites` in the same `SaveChanges` as the change;
  - return a `RedirectOutcome` added to `PAGES/Features/PageResponses.cs`.
- [X] T080 [US5] Add `redirectToPageId` to `PAGES/Features/Unpublish/Unpublish.cs` (body) and `DeletePage/DeletePage.cs` (query), created in the same transaction when the page was live and the target is a page of the site. Make T075 pass.
- [X] T081 [US5] In Cms:
  - add `redirectOldAddress` to `PublishItem`, `RollbackItem` and `ScheduleItem` (publish; stored in `redirect_old_address`), and `redirectToPageId` to `UnpublishItem`, `DeleteItem` and a scheduled unpublish (stored in `redirect_to_page_id`);
  - in `CMS/Infrastructure/ItemPublishing.cs` and `ItemWrites.cs`, when the published slug changes (or the item stops being live) and a redirect was asked for, write `Cms.ItemRedirectRequested` to the Cms outbox in the same transaction;
  - apply the stored choice in `CMS/Features/Items/RunItemSchedule/`;
  - return `redirectRequested` in the response.
- [X] T082 [US5] Create `PAGES/Features/CreateItemRedirect/CreateItemRedirectHandler.cs`, handling `Cms.ItemRedirectRequested`:
  - when the collection has a live template, build the old address from its pattern and `oldSlug`;
  - skip it when live content holds the address;
  - create an exact redirect (origin `offer`) to the item (`moved`) or to the page (`removed`);
  - retarget `path` redirects to the old address;
  - stay idempotent through `processed_events` and the unique index.

  Register the subscription. Make T076 pass.
- [X] T083 [US5] Record `Cms.ItemRedirectRequested` and the new subscriptions in a "017" section of `specs/001-foundation-skeleton/contracts/integration-events.md`, including the `oldSlug` payload note.
- [X] T084 [P] [US5] Create `ADMIN/components/RedirectOfferCheckbox.vue` and `ADMIN/components/RedirectToPagePicker.vue`, and use them in:
  - `ADMIN/features/pages/MoveDialog.vue`, `PageFormDialog.vue` and `TemplateDialog.vue`;
  - the home page action and the unpublish and delete confirmations in `ADMIN/features/pages/PageTreeView.vue` and `ADMIN/features/publishing/`;
  - `ADMIN/features/cms/items/ItemFormView.vue` (publish, schedule and rollback when the slug differs from the published one; unpublish and delete).

  Extend `ADMIN/api/pages.ts` and `ADMIN/api/items.ts`, and show the outcome note. Add i18n keys. Make T077 pass.
- [X] T118 [US5] Add a redirect choice to scheduled page unpublishes (added after analysis, C3):
  - `SetScheduleRequest` in `PAGES/Features/Schedules/Schedules.cs` gains optional `redirectToPageId`, stored in `publish_schedules.redirect_to_page_id`, unpublish only;
  - `PAGES/Features/RunDueSchedule/RunDueScheduleHandler.cs` creates the redirect in the unpublish's transaction when the page was live;
  - the page's schedule control offers `RedirectToPagePicker`;
  - `RedirectOfferTests` (T075) gets the scheduled case, written first.

**Checkpoint**: quickstart §5 passes.

---

## Phase 8: User Story 6 - Canonical addresses and one primary host (Priority: P2)

**Goal**: one indexed address per page. Canonical overrides and list page parameters are
honoured, other letter cases and trailing slashes redirect, and Administrators can redirect
other host names to the primary one.

**Independent Test**: quickstart §6. Request pages on every host name, in other cases, with
trailing slashes and with page parameters. Then set an override and toggle the host
redirect.

### Tests for User Story 6 ⚠️

- [X] T085 [P] [US6] Create `UTESTS/Rendering/Seo/CanonicalAddressTests.cs` for `RENDER/Seo/CanonicalAddress.cs`. It covers:
  - the home page `/`;
  - a base path;
  - an item address;
  - page parameters beyond the first page, in name order;
  - unknown parameters dropped;
  - a page override following the target's current address;
  - a target that is not live → self;
  - a `url` override as written.
- [X] T086 [P] [US6] Create `ITESTS/Rendering/AddressNormalisationTests.cs`. It covers:
  - `/Blog/` and `/blog/` → `301 /blog` with the query kept;
  - item pages likewise;
  - `/` and the base path root never redirected;
  - a miss in another case going on to redirects and then not found, with identical bytes.

  Add `ITESTS/Sites/HostRedirectTests.cs`:
  - setting off: other host names serve with a canonical on the primary;
  - setting on: `301` to the primary with path and query for pages, redirects, sitemap and robots;
  - `/_media` and `/_assets` not redirected;
  - a new site defaults on, and an upgraded site off;
  - only Administrators can change it.
- [X] T087 [P] [US6] Extend `ADMINTESTS/features/sites/SearchVisibilityPanel.spec.ts` (the host redirect switch with a confirmation dialog listing the primary and the other host names) and `ADMINTESTS/editor/seo/SeoPanel.spec.ts` (the canonical control: page picker or URL, and `http`/`https` validation).

### Implementation for User Story 6

- [X] T088 [US6] Implement `RENDER/Seo/CanonicalAddress.cs` and use it in `HeadBuilder` (`canonical` and `og:url`), in `SitemapBuilder`, and in `RedirectResponder` for absolute addresses. Page override targets come from `GetLinkTargetsAsync`, already collected through the link dependency (T028). Paginated list parameters come from `ListRequest.Params`. Make T085 pass.
- [X] T089 [US6] Add the normalisation redirect to `RENDER/Features/RenderPage/RenderPageQuery.cs` and `PageResponder.cs`. Keep the raw path. When the live page or item page matched the normalised path and the raw path differs (case or trailing slash, never for `/`), answer `301` with the query and `Cache-Control: public, max-age=300` before rendering.
- [X] T090 [US6] In `HOST/Middleware/SiteResolutionMiddleware.cs`, after resolving the site: when `site.RedirectOtherHostNames` is on, the normalised request host is not `site.PrimaryHostName`, and the path is not under `/_media` or `/_assets`, write a `301` to `site.BaseUrl + path + query` with `Cache-Control: public, max-age=300`. Make T086 pass. The existing site-resolution tests stay green.
- [X] T091 [P] [US6] Add the canonical control (page picker or URL) to `EDITOR/seo/SeoPanel.vue`, restricted as in T033. Add the host redirect switch and its confirmation dialog to `ADMIN/features/sites/SearchVisibilityPanel.vue`. Add i18n keys. Make T087 pass.

**Checkpoint**: quickstart §6 passes.

---

## Phase 9: User Story 7 - Structured data that describes the content (Priority: P3)

**Goal**: one JSON-LD graph per page, holding breadcrumbs, the website and organisation,
video embeds, collection lists, and Article, Product or Event for item pages, from a shared,
extensible type contract.

**Independent Test**: quickstart §7. Run the golden JSON-LD for each node type, the encoding
of hostile values, and the breadcrumb refresh after a parent's rename.

### Tests for User Story 7 ⚠️

- [X] T092 [P] [US7] Create `CONTRACTS/structured-data.md` from `specs/017-seo/contracts/structured-data.md` (drop the "Draft of" line), `CONTRACTS/structured-data/types.json` (types, properties, accepted field types, defaults, required flags), and golden examples in `CONTRACTS/structured-data/examples/{map,render}/`. Add the contract to `CONTRACTS/README.md`.
- [X] T093 [P] [US7] Create `UTESTS/Rendering/Seo/StructuredDataTypesContractTests.cs` and `ADMINTESTS/editor/seo/structuredDataTypes.spec.ts`, both comparing `types.json` with the tables of `CONTRACTS/structured-data.md` §3 and with their own loaders. They must fail first.
- [X] T094 [P] [US7] Create `UTESTS/Rendering/Seo/JsonLdWriterTests.cs`. Values containing `</script>`, `<!--`, `&`, `'`, `+`, U+2028 and U+2029 must never appear raw inside the element, and the output must parse back to the same values.
- [X] T095 [P] [US7] Create `UTESTS/Rendering/Seo/StructuredDataGoldenTests.cs` with golden graphs in `UTESTS/Rendering/Seo/golden/jsonld/`. It covers:
  - breadcrumbs (a nested page, an item page, an unpublished home page);
  - `WebSite` with and without `Organization`;
  - a YouTube and a Vimeo embed;
  - a collection list with and without a live template;
  - Article, Product (with and without price, bad currency) and Event (missing `startDate` → left out);
  - the `examples/render/` cases.
- [X] T096 [P] [US7] Create `ITESTS/Rendering/BreadcrumbInvalidationTests.cs`:
  - renaming and publishing a parent refreshes its live descendants and its template's item pages at the next request, and unrelated pages stay cached;
  - republishing a parent without a title change refreshes nothing extra;
  - a home page title change refreshes every live page;
  - this also holds on a second instance.
- [X] T097 [P] [US7] Create `ADMINTESTS/editor/seo/StructuredDataControl.spec.ts`:
  - type choice;
  - per-property `BindingControl` offering only accepted field types;
  - defaults shown;
  - required properties marked;
  - broken marks;
  - axe.

### Implementation for User Story 7

- [X] T098 [US7] Implement `RENDER/Seo/StructuredData/JsonLdWriter.cs` (System.Text.Json, default encoder plus explicit escaping of U+2028 and U+2029) and `StructuredDataGraph.cs` (an `@graph` collector added to `RENDER/Domain/RenderContext.cs`). Write the `<script type="application/ld+json">` element from `RENDER/Rendering/PageShell.cs` when the graph is non-empty. Make T094 pass.
- [X] T099 [US7] Implement `GetLiveAncestorsAsync` in `PAGES/Infrastructure/PublishedContentReader.cs` (one query over the address prefixes, live titles from live versions, root first). Add `RENDER/Seo/StructuredData/Breadcrumbs.cs`, plus the `WebSite` and `Organization` nodes for the home page, using `Site.Name`, `Site.BaseUrl` and `Site.Search`. Fetch the ancestors in `PageResponder` on a miss only.
- [X] T100 [US7] In `PAGES/Infrastructure/PublishingWrites.cs`, when a publish, rollback, scheduled publish or unpublish changes a page's live title or live state, bump its live descendants (every live page for the home page) through `GenerationBump` in the same transaction. Make T096 pass.
- [X] T101 [US7] Emit block nodes from their renderers:
  - `VideoObject` from the Embed renderer in `RENDER/Rendering/Blocks/MediaRenderers.cs`, with `thumbnailUrl` for YouTube ids only and no request made;
  - `ItemList` from `CollectionListRenderer` in `RENDER/Rendering/Blocks/ListRenderers.cs`, using the shown items' addresses when the collection has a live template.
- [X] T102 [US7] Implement `RENDER/Seo/StructuredData/StructuredDataTypes.cs`, which loads `types.json` embedded as a resource in `RENDER/QuickBuilder.Rendering.csproj`. Map `seo.structuredData` on item pages with `BindingResolver`, `PlainText`, defaults, required checks, the currency check and `offers` rules. Make T093 (server side) and T095 pass.
- [X] T103 [P] [US7] Create `EDITOR/seo/structuredDataTypes.ts`, which imports `docs/contracts/structured-data/types.json` through the Vite alias the binding examples use, and `EDITOR/seo/StructuredDataControl.vue`. Show the control in `SeoPanel.vue` on templates. Add i18n keys. Make T093 (admin side) and T097 pass.
- [X] T104 [US7] Add "Structured data" notes to `docs/contributing.md` "adding a block type": how a block contributes a node.

**Checkpoint**: quickstart §7 passes. Every story is complete.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [X] T105 [P] Create `ITESTS/Isolation/Probes/SeoIsolationProbe.cs`. Site B cannot list, get, change, delete, import into or export site A's redirects. Site A's redirects, sitemap entries and robots rules never answer on site B's host names. A redirect target naming another site's page or item is refused as unknown. Register it, and keep `ITESTS/Isolation/IsolationCoverageTests.cs` green (FR-038).
- [X] T106 [P] Run `ATESTS/AuthorizationDeclarationTests.cs` and confirm every new action declares exactly one capability. Add an architecture test in `ATESTS/` checking that `RedirectHitCounter` (the `IRedirectHits` implementation) depends on no `DbContext` or I/O type.
- [X] T107 [P] Create `STESTS/RedirectPerformanceTests.cs` and `STESTS/SitemapPerformanceTests.cs` (Performance category):
  - with 10,000 redirects, p95 under 20 ms for redirect responses, exact and prefix, and page renders still within 012's budgets (SC-004);
  - with 100,000 published items, each sitemap file under 2 s on a miss and under 50 ms on a hit (SC-006).

  Record the results here. If a budget fails, apply the fallback in plan Risks and record it.

  **Results (2026-10-14, reference compose setup, `SeoPerformanceTests`)**: with 10,000 redirects, exact redirect p95 7.9 ms, prefix redirect p95 7.3 ms, cached page p95 3.8 ms; with 100,000 item pages, the slowest sitemap file miss 517 ms and hit p95 30 ms. Every budget holds; no fallback was needed.
- [X] T108 [P] Create `ITESTS/Rendering/UpgradeFrom016Tests.cs`, following `UpgradeFrom035Tests.cs`. A 016 snapshot upgrades, and the three migrations apply and roll back. Then check that:
  - every existing page renders the same body;
  - titles are unchanged;
  - the head gains only the canonical address and the Open Graph tags;
  - existing sites keep `redirect_other_hosts = false`;
  - `robots.txt` and `sitemap.xml` answer.
- [X] T109 Add "create a redirect and follow it; fetch sitemap.xml and robots.txt" to the release smoke journey in `STESTS/ReferenceDeploymentSmokeTests.cs`.
- [X] T110 [P] Add `seed-redirects -Count <n>` to `qb.ps1`. It imports a generated CSV through the API. Update the help text.
- [X] T111 [P] Update the docs:
  - `docs/operations.md`:
    - robots.txt under a base path (the proxy rule);
    - the host name redirect;
    - redirects and their usage counts;
    - the sitemap and its rebuild note;
    - normalisation redirects;
  - `docs/backlog.md`: the deferred items in plan.md, and the #20 obligations (module-surface §7);
  - `specs/012-publishing-rendering/contracts/public-rendering.md` and `readers.md` (from `specs/017-seo/contracts/public-seo.md` and module-surface §2);
  - `docs/configuration.md`: no new keys (state it).
- [X] T112 [P] Run `dotnet test` with coverage. `PageResponder`, `RedirectResponder`, `RENDER/Seo/`, `SitemapBuilder`, `PAGES/Domain/Redirects/` and `PublishingWrites` keep at least 80 % line coverage. Run `npm run lint`, `npm run type-check` and `npm test`, and check that `dotnet format --verify-no-changes` and the analyzer gates are clean.
- [X] T113 Create `ITESTS/Observability/SeoContentNotLoggedTests.cs` over every 017 path that logs (redirect matching, offers, import, item redirect handler, sitemap, robots). Slugs, sources, targets, titles and metadata must never appear, including `oldSlug`.
- [ ] T114 Run the whole of `quickstart.md` by hand on the reference compose setup:
  - SC-001 and SC-002 with a stopwatch;
  - SC-008 keyboard-only with NVDA;
  - schema.org validation of §7.

  Record the results in `specs/017-seo/quickstart-results.md`.
- [ ] T115 Open the PR closing #17, with the plan's operator impact and upgrade notes in the description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on Foundational only. It can run in parallel with US1.
- **US3 (Phase 5)**: depends on Foundational. Its `noindex` and `noSitemap` flags are read
  from documents, so the metadata fields of US1 make it demonstrable, but its tests can
  publish documents directly. Its discouraged head tag uses US1's `HeadBuilder` (T026).
- **US4 (Phase 6)**: depends on US1 (`HeadBuilder`, `SeoPanel`). Its sitemap part (T072)
  depends on US3.
- **US5 (Phase 7)**: depends on US2 (redirect table, writes and matching).
- **US6 (Phase 8)**: depends on US1 (head). The host redirect part depends on US3's
  visibility endpoint (T062).
- **US7 (Phase 9)**: depends on US1 (head and `PageShell`). Item types use US4's binding
  resolution (T071).
- **Polish (Phase 10)**: after all stories.

### Within Each User Story

- Tests first, and they must fail.
- Then domain, infrastructure, slices, renderer and admin.
- A story is done when its quickstart section passes.

### Parallel Opportunities

- **Foundational**:
  - T004, T005 and T006 (examples, tests) are parallel;
  - T009, T010 and T011 (the three migrations) are parallel;
  - T012 to T015 (contracts) are parallel;
  - T017 and T018 are parallel.
- **Stories**:
  - US1, US2 and US3 run in parallel after Foundational, by different people;
  - US5 can start when US2's T042 to T046 land;
  - US6's host redirect (T090) is independent of the canonical work (T088, T089).
- **Within a story**: every test task marked [P] is parallel, and the admin tasks marked
  [P] are parallel with the server tasks.

---

## Parallel Example: Foundational

```text
Task: "T004 golden examples in CONTRACTS/page-schema/examples/"
Task: "T005 SeoSchemaGoldenTests in UTESTS/Pages/SeoSchemaGoldenTests.cs"
Task: "T009 AddSeo migration in PAGES/Infrastructure/Migrations/"
Task: "T010 AddSearchSettings migration in SITES/Infrastructure/Migrations/"
Task: "T012 Pages.Contracts readers in PAGESC/"
```

## Parallel Example: User Story 2

```text
Task: "T035 RedirectRulesTests in UTESTS/Pages/Redirects/"
Task: "T036 RedirectCsvTests in UTESTS/Pages/Redirects/"
Task: "T037 RedirectEndpointsTests in ITESTS/Pages/"
Task: "T039 RedirectMatchingTests in ITESTS/Rendering/"
Task: "T041 admin redirect specs in ADMINTESTS/features/seo/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Published pages carry metadata, sharing tags and a
   canonical address.

### Incremental Delivery

1. Setup and Foundational.
2. US1: page metadata. **Demo.**
3. US2 (redirects) and US3 (sitemap and robots). All P1 stories are done.
4. US4 (metadata templates), US5 (redirect offers), US6 (canonical and host).
5. US7 (structured data).
6. Polish, the manual quickstart run, and the PR closing #17.

One PR, as with 010–016. There is no feature flag mechanism. If the PR must split, follow
plan.md's Principle XI order: (1) US1, US3, US4, US6; (2) US2, US5; (3) US7.

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US7.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #17.
- **Contract changes** (T002–T004, T069, T092) land in the same PR as their tests
  (Development Workflow).
- **Avoid**:
  - database writes on the public path (usage goes through `IRedirectHits` only);
  - raw SQL;
  - caching redirect lookups;
  - slugs, sources, targets or metadata in logs;
  - refusing saves because of broken metadata bindings.
