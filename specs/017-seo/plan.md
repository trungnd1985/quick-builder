# Implementation Plan: SEO, Sitemap and Redirects

**Branch**: `017-seo` | **Date**: 2026-10-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-seo/spec.md`

**Issue**: #17.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.

## Summary

Published pages become indexable and links survive moves. Members set search and sharing
metadata per page, and bind it to item fields on collection templates. Every page names a
canonical address. Each site serves a sitemap and robots.txt that follow every publish.
Designers manage 301 and 302 redirects (exact or prefix, with query-string sources), import
and export them, and are offered redirects whenever a live address changes. Pages and item
pages describe themselves with structured data.

The approach, detailed in [research.md](./research.md):
- **No new module** (SE-01):
  - **Pages**: metadata goes in the page document; redirects, redirect usage and a per-site
    sitemap generation go in tables;
  - **Sites**: site-wide search settings are columns on `sites.sites`;
  - **Rendering**: writes the head, the JSON-LD, the sitemap and robots.txt, and answers
    redirects.
- **Metadata** (SE-02 to SE-05):
  - **Where**: an optional `root.props.seo` (page schema v1, additive, rules 22–25), so
    drafts, publish, rollback and schedules need no new code.
  - **Templates**: they bind metadata with the 016 binding format, and "Hide from search
    engines" can be bound to a switch field (clarification 4).
  - **Restricted fields**: Editors cannot change the canonical address or hiding
    (`pages.seo_restricted`).
- **Site settings** (SE-07, SE-08):
  - **Columns**: title suffix, default sharing image, organisation, "Discourage search
    engines", custom robots rules, and "Redirect other host names to the primary one" (on
    for new sites, off for upgraded ones).
  - **Refresh**: the row's `xmin` is already `SiteVersion` in every page key, so a change
    refreshes every page with no new invalidation path.
- **Redirects** (SE-09 to SE-13):
  - **Storage**: `pages.redirects`, with normalised sources and canonical queries, and typed
    targets (`page`, `item`, `path`, `url`) so page and item targets follow moves.
  - **Matching order**: query redirects, then live content, then exact, then the longest
    prefix, then not found. This costs one indexed query, paid only on misses (and on query
    requests).
  - **Offers**:
    - **Pages**: a `redirectOldAddresses` flag on tree changes, applied in the same
      transaction (prefix rules for template moves, clarification 1);
    - **Items**: a flag on item publish, schedule and rollback, delivered as
      `Cms.ItemRedirectRequested`.
  - **Usage**: counted in memory and flushed every 60 s (clarification 3).
  - **Import and export**: an in-house CSV parser.
- **Sitemap and robots** (SE-14 to SE-17):
  - **Sitemap**: `sitemap.xml`, plus an index of numbered files beyond 50,000 addresses.
    Built from read-only Pages and Cms readers and cached under a per-site sitemap
    generation, bumped in Pages' transactions and in its existing Cms event handlers.
  - **robots.txt**: generated, then custom rules.
- **Structured data** (SE-18, SE-19):
  - **Output**: one JSON-LD graph per page, built while rendering. It holds breadcrumbs, the
    website and organisation, video embeds, and collection lists.
  - **Item pages**: Article, Product and Event, from a new `structured-data.md` contract
    that can grow (clarification 3).
  - **Freshness**: breadcrumbs add an ancestor-title dependency.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql, the in-house outbox and dispatcher,
  `System.Xml.XmlWriter`, `System.Text.Json`.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n.
- **Nothing new** (SE-26).

**Storage**: PostgreSQL 16+.
- **Pages** migration `AddSeo`: **+** `redirects`, **+** `redirect_usage`, **+**
  `redirect_changes` (audit, SE-13a), **+** `sitemap_state` (one row filled per site with
  pages), **~** `publish_schedules` + `redirect_to_page_id`.
- **Sites** migration `AddSearchSettings`: **~** `sites` + 8 columns (with
  `search_updated_by`).
- **Cms** migration `AddScheduleRedirect`: **~** `item_schedules` + `redirect_old_address`,
  `redirect_to_page_id`.
- **No document or data rewrite** (SE-23).

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are in research SE-24. The main ones:
- `SeoSchemaGoldenTests` with `seoSchema.spec.ts`;
- `HeadMetadataGoldenTests` and `StructuredDataGoldenTests` (SC-007);
- `RedirectMatchingTests`, `RedirectOfferTests` (SC-003) and `RedirectImportTests`;
- `RedirectUsageTests`;
- `SitemapTests` and `SitemapFreshnessTests` (SC-005);
- `HostRedirectTests`;
- `BreadcrumbInvalidationTests`;
- `UpgradeFrom016Tests`;
- `SitemapPerformanceTests` and `RedirectPerformanceTests` (SC-004, SC-006);
- the admin specs with axe (SC-008).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA, and the
server-rendered public site.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Redirects** (SC-004): with 10,000 redirects, p95 redirect response under 20 ms.
- **Pages**: 012's budgets unchanged (one more read on a cache miss: ancestors).
- **Sitemap** (SC-006): 100,000 item pages, each file under 2 s on a miss.
- **Freshness** (SC-005): sitemap changes visible within 5 s p95 on every instance.

**Constraints**:
- **Published only**: the public path reads published data through read-only connections,
  and never writes (usage counts are in memory) (Principle VII).
- **No script**: JSON-LD is a data block, and the CSP is unchanged.
- **Logs**: no slugs, sources, targets, titles or metadata in logs or events, except
  `oldSlug` in `Cms.ItemRedirectRequested`, which is never logged.
- **Offline**: zero outbound requests (YouTube thumbnails are addresses only).

**Scale/Scope**:
- **Code**: 0 new projects.
- **Migrations**: 3. Tables: 4 new, 3 changed.
- **Document**: 1 new prop, 4 new rules.
- **Contracts**: 1 new document (`structured-data.md`); `page-schema.md` and
  `binding-expression.md` are extended.
- **Endpoints**: 9 new admin operations and 12 changed. Public routes: 3 new (`robots.txt`,
  `sitemap.xml`, `sitemap-<n>.xml`).
- **Contracts readers**: 3 new interfaces and 3 extended.
- **Events**: 1 new, and 4 changed handlers.
- **Capabilities**: 3 new. **Configuration keys**: 0.
- **Admin**: about 12 components and 3 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module** (SE-01). The constitution's module list is unchanged. <br>• **Slices**: Pages `Features/Redirects/` (list, get, create, update, delete, import, export), `Features/CreateItemRedirect/` (event handler); Sites `Features/UpdateSearchSettings/`, `UpdateSearchVisibility/`; Rendering `Features/Sitemap/`, `Features/Robots/`. Controllers are thin, with in-house handlers. <br>• **Rich domain model**: a `Redirect` entity enforces source, target, chain and loop rules (pure `RedirectRules`, unit-tested). `PageTree` produces offered redirects with the change. <br>• **Cross-module**: no new reference edge (module-surface.md §1). Item offers cross Cms → Pages through the outbox. No transaction spans modules. <br>• **Enforcement**: `ModuleBoundaryTests` unchanged and passing; `AuthorizationDeclarationTests` cover the new endpoints. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. Three forward-only, automatic migrations. Health checks are unchanged. `docs/operations.md` covers robots.txt under a base path and the host name redirect. |
| III | Upgrades Never Lose Data | **Complies.** <br>• `UpgradeFrom016Tests` prove identical bodies, unchanged titles, and unchanged host behaviour (the setting is off for existing sites). <br>• No stored data is rewritten. <br>• **Site export (#20)**: obligations recorded (module-surface §7). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. <br>• **Cache**: sitemap and robots entries go through `ICacheProvider`. <br>• **Multi-instance**: redirects are read from the database per request, and usage counts add up across instances, so no cache messages are needed. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: `redirects`, `redirect_usage` and `sitemap_state` carry `site_id` with the global filter. <br>• **Cache keys**: sitemap and robots keys use `CacheKey.ForSite`. <br>• **Cross-site ids**: `page` and `item` targets are checked against the site. The public media reader ignores other sites' image ids. <br>• **Isolation suite**: + `SeoIsolationProbe`. <br>• **Raw SQL**: none (the prefix lookup and the usage upsert are LINQ and `ExecuteUpdate`). |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **Page schema**: `seo` is an additive prop, the schema stays v1 (page-schema.md §6), and both validators embed the same file. <br>• **Bindings**: metadata bindings are rows in binding-expression.md §4. <br>• **New contract**: `structured-data.md` v1 with `types.json` and golden examples, used by both sides; adding a type is additive (FR-035a). <br>• **Block types**: block-derived nodes are emitted by each block's own renderer. |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• **Metadata**: in the document, so it reaches visitors only through publish and follows rollback and schedules. <br>• **Read-only public path**: the sitemap, head and structured data read published data only, through read-only readers. Redirect hits are counted in memory and written by a background service, not the request path. <br>• **Not content**: redirects and site settings are site configuration, applied when saved, as host names are. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Server-side**: head and JSON-LD are written on the server, with no JS. <br>• **Dependency graph**: extended with page → ancestors (breadcrumbs), page → canonical target, page → sharing image, and site → every page (via `SiteVersion`). The sitemap gets its own generation. <br>• **Exact refresh**: proven by `BreadcrumbInvalidationTests` and `SitemapFreshnessTests`, on two instances too. <br>• **Budgets**: measured by the performance suites. |
| IX | Secure by Default | **Complies.** <br>• **Encoding**: every metadata value is attribute-encoded. JSON-LD escapes `<`, `>`, `&`, `'`, `+`, U+2028 and U+2029. Sitemap XML goes through `XmlWriter`. <br>• **Schemes**: redirect and canonical targets are `http`/`https` only, with no user info. Protocol-relative targets are refused. <br>• **Spreadsheets**: export guards against formula injection. <br>• **Authorization**: server-side policies (`pages.redirects.manage`, `sites.search.manage`, `sites.search_visibility.manage`, `pages.seo_restricted`). <br>• **Input bounds**: import is limited by size and rows. <br>• **Privacy**: no visitor data in usage counts, and nothing about sources or targets in logs. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new and changed endpoint. <br>• **Contracts**: shared golden tests for metadata rules, bindings and structured data types. <br>• **Rendering**: golden HTML for the head and JSON-LD. <br>• **Coverage**: 80 % line coverage on the render, publish and redirect-matching paths. <br>• **Smoke test**: the release journey gains a redirect and a sitemap check. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies**: the CSV parser is about 80 lines. <br>• **Not built**: no new module, no redirect cache, no regex redirects, no not-found log, no `twitter:*` tags. <br>• **Reuse**: `BindingControl`, the binding resolver, the media and page pickers. <br>• **Delivery**: one PR. If it must split: (1) metadata, canonical, sitemap and robots; (2) redirects and offers; (3) structured data. Each part is usable alone, and there is no feature flag mechanism yet. |

**Technology Constraints**:
- MVC controllers only; EF Core only, with no raw SQL.
- No Node process at run time.
- No new admin libraries.

**Development Workflow**:
- Issue #17 → `specs/017-seo` → branch `017-seo` → one PR that closes #17.
- Clarify has run: 3 questions at specify and 3 in clarify, all answered. Analyze follows
  tasks.
- **Contract and registry changes in the same PR**:
  - `docs/contracts/page-schema.md` (§2, §3 rules 22–25, §6) and
    `page-schema/v1.schema.json`, with golden examples;
  - `docs/contracts/binding-expression.md` §4 (metadata rows) and `resolve/` examples;
  - `docs/contracts/structured-data.md` (+), with `structured-data/types.json` and
    examples;
  - `docs/contracts/README.md` (lists the new contract);
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (017 section, `oldSlug`
    payload note);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (3 capabilities);
  - `specs/012-publishing-rendering/contracts/public-rendering.md` and `readers.md`;
  - `docs/backlog.md` (deferred items; #20 obligations).

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 3, additive and automatic: Pages `AddSeo`, Sites `AddSearchSettings`,
  Cms `AddScheduleRedirect`.
- **New dependencies**: none.
- **Upgrade notes**:
  1. Pages now carry a canonical address and Open Graph tags. Every cached page renders once
     after the upgrade.
  2. Each site serves `/sitemap.xml` and `/robots.txt`. Under a base path, expose
     `robots.txt` at the host root through the proxy (`docs/operations.md`).
  3. Addresses in other letter cases or with a trailing slash now redirect (301) to the
     page's address.
  4. "Redirect other host names to the primary one" is off for existing sites and on for
     new ones. Administrators can turn it on in the site's search settings.
  5. Designers can manage redirects. Moves and slug changes offer redirects, ticked by
     default in the admin.

**Post-Phase-1 re-check**:
- `seo-api.yaml` declares a capability on every operation;
- `module-surface.md` adds no reference edge;
- the new tables have `site_id` and the filter;
- the public readers are read-only, and `IRedirectHits` does no I/O;
- the event carries ids and the one documented slug;
- every schema change is additive under page-schema.md §6;
- the golden examples cover every rule, binding row and structured data type;
- no raw SQL.

**Gate: PASS** (no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/017-seo/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 6 questions)
├── research.md                # Phase 0: decisions SE-01 … SE-26 (with SE-13a)
├── data-model.md              # Phase 1: tables, document additions, rules, states, errors, events, keys
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── seo-metadata.md        # Draft of page-schema and binding-expression additions
│   ├── structured-data.md     # Draft of docs/contracts/structured-data.md
│   ├── seo-api.yaml           # HTTP: redirects, site search settings, changed operations
│   ├── public-seo.md          # Answer order, redirects, head, JSON-LD, sitemap, robots
│   └── module-surface.md      # Readers, events, obligations
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
docs/
~ contracts/page-schema.md  contracts/page-schema/v1.schema.json  contracts/page-schema/examples/
~ contracts/binding-expression.md  contracts/binding-expression/examples/resolve/
+ contracts/structured-data.md  contracts/structured-data/types.json  contracts/structured-data/examples/
~ contracts/README.md  backlog.md  operations.md

src/QuickBuilder.Host/
~ Middleware/SiteResolutionMiddleware.cs        # other host names → primary (SE-07)

src/Modules/
├── Pages/
│   ├── QuickBuilder.Pages.Contracts/
│   │   + Redirects/IRedirectReader.cs  RedirectMatch.cs  IRedirectHits.cs
│   │   + Publishing/ISitemapReader.cs
│   │   ~ Publishing/IPublishedContentReader.cs     # GetLiveAncestorsAsync
│   │   ~ PageSchema.cs                             # embeds the updated v1 schema
│   │   ~ PageAddresses.cs                          # platform paths documented (robots, sitemaps)
│   └── QuickBuilder.Pages/
│       + Domain/Redirects/Redirect.cs  RedirectRules.cs  RedirectSource.cs  CanonicalQuery.cs
│       + Domain/Redirects/OfferedRedirects.cs      # from a tree change (SE-11)
│       ~ Domain/PageTree.cs  DependencyExtractor.cs  PublishingTerms.cs
│       + Infrastructure/RedirectReader.cs  RedirectWrites.cs  RedirectHitCounter.cs  RedirectUsageFlusher.cs
│       + Infrastructure/SitemapReader.cs  SitemapGeneration.cs  RedirectCsv.cs
│       ~ Infrastructure/PagesDbContext.cs  PublishedReadDbContext.cs  PublishedContentReader.cs
│       ~ Infrastructure/PublishingWrites.cs  PageTreeWrites.cs  GenerationBump.cs  ScheduleWrites.cs
│       ~ Infrastructure/Schema/PageDocumentValidator.cs   # rules 22–25, restricted fields
│       + Infrastructure/Migrations/<ts>_AddSeo.cs
│       + Features/Redirects/ListRedirects.cs  GetRedirect.cs  CreateRedirect.cs  UpdateRedirect.cs
│       +                   DeleteRedirect.cs  ImportRedirects.cs  ExportRedirects.cs  RedirectResponses.cs
│       + Features/CreateItemRedirect/CreateItemRedirectHandler.cs
│       ~ Features/UpdatePage/  MovePage/  MakeHomePage/  SetPageTemplate/  Unpublish/  DeletePage/   # offers
│       ~ Features/SavePageDraft/                   # pages.seo_restricted
│       ~ Features/RefreshPagesListingItems/  RefreshPagesUsingCollection/  RefreshPagesShowingMedia/   # sitemap, site images
│       ~ PagesCapabilities.cs  PagesErrors.cs  PagesErrorResults.cs  PagesModule.cs
├── Sites/
│   ├── QuickBuilder.Sites.Contracts/
│   │   ~ SiteDto.cs                                # Search, RedirectOtherHostNames
│   └── QuickBuilder.Sites/
│       ~ Domain/Site.cs                            # search settings, visibility
│       ~ Infrastructure/SitesDbContext.cs  SiteDtoMapper.cs  SiteResponse.cs
│       + Infrastructure/Migrations/<ts>_AddSearchSettings.cs
│       + Features/UpdateSearchSettings/  UpdateSearchVisibility/
│       ~ Features/CreateSite/                      # redirect_other_hosts = true
│       ~ SitesCapabilities.cs
├── Cms/
│   ├── QuickBuilder.Cms.Contracts/
│   │   ~ Publishing/IPublishedItemReader.cs        # ListAddressesAsync
│   │   + Events/ItemRedirectRequested.cs
│   └── QuickBuilder.Cms/
│       ~ Infrastructure/PublishedItemReader.cs  ItemPublishing.cs  ItemWrites.cs  CmsDbContext.cs
│       + Infrastructure/Migrations/<ts>_AddScheduleRedirect.cs
│       ~ Features/Items/PublishItem/  RollbackItem/  ScheduleItem/  RunItemSchedule/  UnpublishItem/  DeleteItem/
├── Media/
│   ├── QuickBuilder.Media.Contracts/  ~ IPublicMediaReader.cs   # AltText
│   └── QuickBuilder.Media/            ~ Infrastructure/PublicMediaReader.cs
└── Rendering/QuickBuilder.Rendering/
    + Seo/HeadBuilder.cs  HeadMetadata.cs  SeoDocument.cs  CanonicalAddress.cs  PlainText.cs  OgLocale.cs
    + Seo/StructuredData/JsonLdWriter.cs  StructuredDataGraph.cs  StructuredDataTypes.cs  Breadcrumbs.cs
    ~ Rendering/PageShell.cs                        # head metadata, JSON-LD block
    ~ Rendering/Blocks/MediaRenderers.cs  ListRenderers.cs   # VideoObject, ItemList
    ~ Domain/RenderInputs.cs  RenderContext.cs
    ~ Features/RenderPage/PageResponder.cs  RenderPageQuery.cs  RenderPageController.cs   # order, redirects, normalisation
    + Features/RenderPage/RedirectResponder.cs
    + Features/Sitemap/SitemapController.cs  SitemapBuilder.cs  SitemapKey.cs
    + Features/Robots/RobotsController.cs  RobotsText.cs
    ~ RenderingModule.cs

src/admin/src/
+ editor/seo/SeoPanel.vue  SearchPreview.vue  SharingPreview.vue  StructuredDataControl.vue  seoFields.ts
+ editor/seo/structuredDataTypes.ts          # loads docs/contracts/structured-data/types.json
~ editor/EditorTopBar.vue  editor/bindings/bindingRules.ts  schema/schema.ts  schema/documentValidator.ts
+ features/seo/RedirectsView.vue  RedirectDialog.vue  RedirectImportDialog.vue  redirectErrors.ts
+ features/sites/SearchSettingsPanel.vue  SearchVisibilityPanel.vue
+ components/SearchHiddenBanner.vue  RedirectOfferCheckbox.vue  RedirectToPagePicker.vue
~ features/sites/SiteDetailView.vue
~ features/pages/MoveDialog.vue  PageFormDialog.vue  TemplateDialog.vue  PageTreeView.vue
~ features/publishing/ (unpublish confirmation)
~ features/cms/items/ItemFormView.vue          # slug-change offer, unpublish/delete target
~ app/router.ts  app/navigation  api/pages.ts  api/sites.ts  api/items.ts  i18n/en.json
src/admin/tests/
+ editor/seo/*.spec.ts  seoSchema.spec.ts (golden)  structuredDataTypes.spec.ts
+ features/seo/*.spec.ts  features/sites/Search*.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/            # AuthorizationDeclarationTests, IRedirectHits has no I/O
+ QuickBuilder.UnitTests/Pages/Redirects/    # RedirectRulesTests, CanonicalQueryTests, RedirectCsvTests, OfferedRedirectsTests
+ QuickBuilder.UnitTests/Rendering/Seo/      # HeadMetadataGoldenTests, JsonLdWriterTests, StructuredDataGoldenTests, PlainTextTests
~ QuickBuilder.UnitTests/Pages/              # SeoSchemaGoldenTests
~ QuickBuilder.IntegrationTests/
│   + Pages/RedirectEndpointsTests.cs  RedirectImportTests.cs  RedirectOfferTests.cs  RedirectUsageTests.cs  SeoDraftPermissionTests.cs
│   + Rendering/RedirectMatchingTests.cs  SitemapTests.cs  SitemapFreshnessTests.cs  RobotsTests.cs
│   +           SeoTemplateRenderTests.cs  BreadcrumbInvalidationTests.cs  UpgradeFrom016Tests.cs
│   + Sites/SearchSettingsTests.cs  HostRedirectTests.cs
│   + Cms/ItemRedirectOfferTests.cs
│   ~ Isolation/Probes/ (+ SeoIsolationProbe)  Infrastructure/EndpointCatalog.cs
~ QuickBuilder.SmokeTests/                   # + SitemapPerformanceTests, RedirectPerformanceTests; journey: redirect, sitemap
+ fixtures/redirects-300.csv
specs/001-foundation-skeleton/contracts/integration-events.md   # ~
specs/002-identity-setup/contracts/authorization.md             # ~
specs/012-publishing-rendering/contracts/public-rendering.md  readers.md   # ~
```

**Structure decision**:
- **Pages**: redirects and the sitemap generation, under `Domain/Redirects/` and
  `Features/Redirects/`, beside the page tree whose changes they follow.
- **Sites**: search settings, as two slices by capability.
- **Rendering**: head and structured data in a new `Seo/` folder beside `Binding/`. Sitemap
  and robots are their own features.
- **Admin**: the editor panel in `editor/seo/`, redirects in `features/seo/`.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Screen-reader checks of the new panels and dialogs are manual in quickstart §9.2, until #25. |

**New exceptions**: none.

## Risks

- **Item redirects arrive through the outbox** (SE-11): for a few seconds after an item's
  slug changes, its old address gives the not-found page. Page redirects have no such window.
  - *Mitigation*: FR-029 allows up to 5 s for items (amended 2026-10-13 after analysis).
    `ItemRedirectOfferTests` measures it, and the item form's confirmation states it.
  - *Fallback* (recorded, not built): Cms keeps the previous published slug, and Rendering
    falls back to it until the redirect exists.
- **Sitemap rebuilds on busy sites** (SE-25): every item publish moves the generation.
  - *Mitigation*: builds happen only on request, and crawlers request rarely.
  - *Fallback*: per-file generations by template.
- **Breadcrumb refresh breadth**: a home page title change refreshes every live page.
  - *Mitigation*: only when the live title or live state changes, and re-renders are lazy.
- **Normalisation redirects change long-standing behaviour**: `/Blog/` used to serve the page
  directly.
  - *Mitigation*: the upgrade notes, and `RedirectMatchingTests` covering the home page and
    the base path root.
- **Query redirects before content** can hide a list page (`?page=2`).
  - *Mitigation*: the admin warns (FR-017a), and the redirect list shows the source with its
    query.
- **robots.txt under a base path** is invisible to crawlers.
  - *Mitigation*: documented proxy rule in `docs/operations.md`.

## Deferred (recorded, not built)

- Wildcard or regular-expression redirects; a not-found log; redirect hit history over time.
- Image, news and video sitemaps; per-locale sitemaps and `hreflang`; pinging search
  engines.
- Structured data types beyond Article, Product and Event (additive, FR-035a). `uploadDate`
  and thumbnails for non-YouTube video.
- Per-item metadata fields; `twitter:*` tags.
- A previous-slug fallback for the item redirect window.
