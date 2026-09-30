# Implementation Plan: Publishing and Rendering

**Branch**: `012-publishing-rendering` | **Date**: 2026-09-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-publishing-rendering/spec.md`

**Issue**: #12. This feature closes exceptions **E-3** (the smoke test now runs create
page → publish → view) and **E-4** (rendering's cache is correct across instances by
construction). It adds no new exception.

## Summary

Everything a site holds is draft (010, 011); visitors get a not-found answer for every page.
This feature adds:
- **Publishing** in the Pages module for pages and components:
  - atomic, recorded publish and unpublish;
  - immutable numbered versions and rollback;
  - copying a version to the draft;
  - schedules;
  - a site publish log that outlives deleted pages.
- **Design publishing** in the Editor module: a separate, recorded, rollback-able action
  (clarification Q1).
- **A new Rendering module**: server-side HTML from published documents, one renderer per
  block type, an output cache, a not-found page and the block stylesheet endpoint.

The approach, detailed in [research.md](./research.md):
- **Ownership** (PR-01): Pages and Editor own published data, because each publish is one
  transaction in the module that owns the content. Rendering has no tables and reads
  through read-only contracts (PR-06). Principle VII is enforced by the database:
  `default_transaction_read_only`.
- **Versions** (PR-02, PR-03): a `publications` live pointer, immutable
  `published_versions`, and an append-only `publish_log`.
  - A publish carries the draft version the member saw.
  - Concurrent publishes serialise through an `xmin` retry.
  - 50 versions are retained per page or component (PR-15).
- **Dependencies and invalidation** (PR-04, PR-05): Pages records what each live version
  depends on (components, linked pages, media) at publish time. Every change bumps the
  **render generation** of exactly the affected pages, in the same transaction.
  - Rendering's cache key contains that generation, the live design number, the site
    version and a renderer stamp.
  - So a changed page simply gets a new key: fresh on every instance at the next request,
    with no cross-instance eviction and no shared cache required. This closes E-4.
- **Cache** (PR-07): the existing `ICacheProvider`, reshaped:
  - entries are sized, with the new key `Cache:MemoryLimitMegabytes`;
  - misses for one key are single-flight;
  - browsers revalidate with an `ETag` and `no-cache`, and get `304`.
- **Renderer** (PR-09 … PR-12): one C# `IBlockRenderer` per block type, writing
  HTML-encoded UTF-8.
  - **Defaults** come from the schema.
  - **Styles**: per-node values go in one hashed `<style>` block, so the CSP is strict.
    Zero JavaScript. Embeds use `loading="lazy"` iframes.
  - **Stylesheet**: the editor's `blocks.css` is embedded and served at
    `/_assets/blocks.{hash}.css`.
  - **Parity**: golden HTML files, and a vitest parity check against the canvas
    components, keep the canvas and the published page in step (SC-008).
- **Schedules** (PR-14): delayed messages in the Pages outbox, the "outbox worker" the
  issue names. They run exactly once through `SKIP LOCKED` and a guard on the schedule's
  state and revision.
- **Not found** (PR-08, PR-13): one HTML not-found page per site, in its design and
  language. The host writes the same bytes for missing pages and for admin paths on client
  domains, so 009's parity holds.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**:
- **Backend**: ASP.NET Core MVC, EF Core 10 + Npgsql (`jsonb`, `xmin`), `System.Text.Json`,
  and `System.Text.Encodings.Web` (`HtmlEncoder`, `UrlEncoder`, part of the shared
  framework). **Nothing new.**
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n and `@vue/test-utils`, as before. **Nothing
  new.**

**Storage**: PostgreSQL 16+.
- **`pages` schema**: five new tables: `publications`, `published_versions`,
  `live_dependencies`, `publish_schedules` and `publish_log`.
- **`editor` schema**: two new tables: `design_publications` and `design_versions`.
- **Rendering**: no tables. Rendered pages are held in the in-memory cache, behind
  `ICacheProvider` ([data-model.md](./data-model.md)).

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe, as before. New suites are listed in research PR-21. The main ones:
- **Renderer**: `RendererGoldenTests`, `CanvasMarkupParity.spec.ts`, `TokenCssParityTests`
  and its vitest twin, and `BlockRendererCoverageTests`.
- **Invalidation**: `CacheInvalidationTests` (SC-006) and `RenderingAcrossInstancesTests`
  (SC-002).
- **Schedules**: `ScheduledPublishExactlyOnceTests` (SC-007).
- **Safety**: `PublicPathReadOnlyTests` (Principle VII) and `NotFoundParityTests` (009
  S-13).
- **Performance**: `RenderPerformanceTests` in the smoke project, on the reference compose
  (SC-003, SC-004).

**Target Platform**: Linux containers (amd64 in CI), unchanged. Published pages work in any
browser: they use no script. Container queries need a current evergreen browser, as in 011.

**Project Type**: Web application: a modular-monolith backend, the Vue admin SPA, and now
the public site pages the backend renders.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Budgets** (SC-003): a 200-block page with a header and footer renders in under 200 ms
  at p95 on a miss, and is served in under 20 ms on a hit.
- **Freshness** (SC-002): the next request after a change serves it, on every instance.
- **Schedules** (SC-007): run within 1 minute of their time; the outbox polls every 5 s.

**Constraints**:
- The public path is read-only (Principle VII) and ships 0 bytes of JavaScript (FR-028).
- Output is strict-CSP HTML with no inline script, style attributes or third-party
  resources, except the two allow-listed video players, which are lazy.
- No content, titles or paths in logs (FR-044).
- Publish is one transaction (FR-001).
- `/api/v1/pages/...` gains 17 operations and `/api/v1/editor/...` gains 4, all additive.
  Tree and component list responses gain a `publication` field.

**Scale/Scope**:
- **Code**: 1 new module (2 projects).
- **Data**: 7 tables across 2 migrations.
- **Endpoints**: 21 admin endpoints and 2 public ones (the page and the stylesheet).
- **Capabilities**: 2 new.
- **Events**: 6 new published events and 1 module-internal message.
- **Renderers**: 11 block renderers (10 blocks and the component reference).
- **Admin shell**: 5 new or changed screens (page tree, component list, editor top bar,
  history, design screen) and 1 new screen (publish log).
- **Stated limits**: 2,000 pages per site (010) and 50 retained versions per subject.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **New module**: `Rendering` is one of the constitution's named modules, with `QuickBuilder.Rendering` and `QuickBuilder.Rendering.Contracts`, `internal` by default, and `AddRenderingModule()`. <br>• **Slices**: in Pages, `PublishPage`, `UnpublishPage`, `RollbackPage`, `GetPageHistory`, `GetPageVersion`, `CopyPageVersionToDraft`, `SetPageSchedule` and `CancelPageSchedule`, the component twins, `GetPublishLog`, and the handlers. In Editor, `PublishDesign`, `RollbackDesign`, `ListDesignVersions` and `GetDesignVersion`. In Rendering, `RenderPage` and `ServeBlockStylesheet`. Controllers are thin, with in-house handlers. <br>• **Domain model**: Pages gains a `Publication` aggregate and a `PublishSchedule` entity (data-model §3). Rendering's model is the renderer: block renderers, `RenderKey` and `TokenCss`; it needs no persistence. <br>• **Cross-module**: reads go through Contracts readers only (readers.md). Side effects go through events: the design log entry and media deletion. No transaction spans modules. <br>• **Editor → Pages.Contracts**: Editor references Pages.Contracts for the capability name, as it already does for the subset validator (011). |
| II | Easy to Install, Operate and Upgrade | **Complies.** One configuration key with a safe default, validated at startup (`Cache:MemoryLimitMegabytes`). Two additive, automatic migrations. No new service. Offline: published pages load nothing from third parties except a video player a site author embedded, and only when it is scrolled into view. |
| III | Upgrades Never Lose Data | **Complies.** `UpgradeFrom011Tests`: drafts and token sets are unchanged and nothing is published. Published versions carry `schema_version`. Export (#20) can read versions, since they are `jsonb` rows like drafts. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies. E-4 is closed.** <br>• **Cache**: reached only through `ICacheProvider` (reshaped, PR-07). <br>• **Background jobs**: schedules use the built-in outbox (PR-14). No external dependency. <br>• **Multi-instance correctness** doesn't depend on a shared cache: keys carry database-held generations (PR-05). `RenderingAcrossInstancesTests` proves it. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Filtering**: every new table is `ISiteOwned` and filtered. `PublishingIsolationProbe` joins the suite, including a public probe that no site's page is served on another's host. <br>• **Keys and jobs**: cache keys begin with `site:{siteId}` (`CacheKey.ForSite`). Schedule messages carry the site. <br>• **Raw SQL**: the generation bump is parameterised through EF with an explicit site filter, and `GenerationBumpSiteFilterTests` covers it. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **Versions**: the renderer dispatches on `schemaVersion` and must list every version in `page-schema/` (`RendererSchemaVersionsTests`). <br>• **One definition per block type**: one renderer per type, defaults read from the schema, and `BlockRendererCoverageTests`. <br>• **Contract change**: `page-schema.md` §9 becomes normative (contracts/page-schema-rendering.md), with golden rendered files. The schema file is unchanged. |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• **Separate state**: drafts and published versions are separate rows; editing never touches `publications`. <br>• **Read-only path**: the public path reads through read-only connections enforced by PostgreSQL (PR-06, `PublicPathReadOnlyTests`). <br>• **Publish**: atomic, logged and rollback-able. <br>• **Schedules**: idempotent through state and revision (PR-14). |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Server-side**: complete HTML with no script. <br>• **Cache and graph**: the output cache is keyed by generations driven by a tracked page → component, page → page (links) and page → media graph. Page → collection edges come with #16. <br>• **Invalidation**: exactly the affected entries, and correct across instances with or without a shared cache. <br>• **Budgets**: tested (`RenderPerformanceTests`) and JavaScript is 0 bytes. |
| IX | Secure by Default | **Complies.** <br>• **Encoding**: every content value is encoded for its context by `HtmlWriter`. Rich text goes through an allow-list. <br>• **Values**: link schemes are re-checked, and embeds are limited to the two providers. <br>• **Headers**: a strict CSP with a style hash, `nosniff`, a referrer policy and a permissions policy. <br>• **Authorization**: the new capabilities are declared on every action and checked server-side. <br>• **Logs**: no content, titles or paths. <br>• **Not-found parity**: no admin-surface leak on client domains. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: every new one has integration tests on PostgreSQL. <br>• **Renderers**: golden files for every block type and valid example. <br>• **Unit tests**: domain rules and the dependency and generation logic. <br>• **Smoke test**: now the constitution's full sequence (E-3 closed). <br>• **Coverage**: `QuickBuilder.Rendering` joins the per-package 80% gate; Pages and Editor are already in it. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies**: no scheduler library, no templating engine, no Redis. <br>• **Kept simple**: Rendering has no tables. <br>• **Deliberately not built**: cache warming, compression and design scheduling (PR-23). <br>• **Delivery**: ships whole in one PR, so no feature flag. |

**Technology Constraints**:
- MVC controllers only; the public endpoints are controller actions too.
- EF Core only. The generation bump and the retention prune are EF bulk operations
  (`ExecuteUpdate`, `ExecuteDelete`) with explicit site filters. There is no hand-written SQL
  beyond the outbox's existing claim.
- `jsonb` stores versions and design versions.
- No Node process: rendering is C#, and the editor's CSS file is embedded.

**Development Workflow**:
- Issue #12 → `specs/012-publishing-rendering` → branch `012-publishing-rendering` → one PR
  that closes #12.
- Clarify has run: 3 questions in specify and 4 in clarify, all answered. Analyze follows
  tasks.
- **Contract changes in the same PR**: `docs/contracts/page-schema.md` (§5, §6, §8, §9),
  `specs/001-foundation-skeleton/contracts/integration-events.md` (new events), and
  `specs/002-identity-setup/contracts/authorization.md` §2 (two capabilities).

**Operator impact**:
- **New configuration keys**: `Cache:MemoryLimitMegabytes` (default `256`, range 16–16,384).
- **Migrations**: 2, both additive and automatic: `AddPublishing` (Pages) and
  `AddDesignPublishing` (Editor).
- **New dependencies**: none.
- **Upgrade notes**:
  1. Sites now show published pages to visitors. Nothing is published after the upgrade,
     so every site keeps answering "not found" until a Designer or Administrator publishes.
  2. Publish shared components (header, footer) before the pages that use them, and
     **Publish design** to take the site's design tokens live. Until then, pages use the
     default design.
  3. Editors prepare drafts; only Designers and Administrators publish.
  4. Missing pages on a site's host names now show an HTML not-found page in the site's
     design, instead of a JSON body. Unknown host names still get the JSON answer.
  5. `/_assets/` is a new platform path on every site.
  6. Rendered pages use up to `Cache:MemoryLimitMegabytes` of memory per instance.
  7. Several instances behind a load balancer now serve up-to-date pages without a shared
     cache (E-4 closed). Media still needs a shared storage provider (003).

**Post-Phase-1 re-check**: the data model and contracts add no violation:
- publishing-api.yaml is all admin surface, with a capability on every operation;
- public-rendering.md reads only through read-only readers;
- every new table is site-owned;
- no module reads another's tables;
- events carry ids only.

E-3 and E-4 are closed, and no new exception is needed. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/012-publishing-rendering/
├── plan.md                      # This file
├── spec.md                      # Feature specification (clarified: 3 + 4 questions)
├── research.md                  # Phase 0: decisions PR-01 … PR-23
├── data-model.md                # Phase 1: tables, domain rules, state changes, error codes
├── quickstart.md                # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── publishing-api.yaml      # HTTP: publish, unpublish, rollback, history, versions, schedules, log, design
│   ├── public-rendering.md      # What visitors get: pages, 404, stylesheet, headers, freshness
│   ├── readers.md               # Cross-module readers, shared infrastructure changes, events
│   └── page-schema-rendering.md # Changes to docs/contracts/page-schema.md
├── checklists/
│   └── requirements.md
└── tasks.md                     # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
Dockerfile                                # ~ restore layer: + Rendering csprojs; build stage copies
                                          #   src/admin/src/editor/blocks/blocks.css (embedded resource)
.github/workflows/ci.yml                  # ~ QuickBuilder.Rendering joins the coverage gate; + `smoke` job
                                          #   running the smoke and performance tests (FR-041)
QuickBuilder.slnx                         # ~ + the two Rendering projects
src/QuickBuilder.Host/appsettings.json    # ~ + Cache:MemoryLimitMegabytes

docs/
~ contracts/page-schema.md                # §5 note, §6, §8, §9 normative (contracts/page-schema-rendering.md)
~ operations.md                           # publishing, cache memory, multi-instance, the not-found page
~ contributing.md                         # "Adding a block type" includes its renderer and golden files

src/
├── QuickBuilder.Host/
│   ~ Program.cs                          # + AddRenderingModule, + application part
│   ~ Routing/RoutePrefixes.cs            # + /_assets public reserved prefix
│   ~ Middleware/PublicResponses.cs       # public not-found → IPublicNotFoundPage (PR-08)
│   ~ Middleware/SiteResolutionMiddleware.cs  AdminHostConfinementMiddleware.cs   # use it
├── Shared/QuickBuilder.Shared.Infrastructure/
│   ~ Caching/ICacheProvider.cs           # CacheEntrySettings, size limit, single-flight (PR-07)
│   + Caching/CacheOptions.cs             # Cache:MemoryLimitMegabytes + validator
│   + Data/ReadOnlyConnection.cs          # default_transaction_read_only (PR-06)
│   ~ Outbox/OutboxMessage.cs             # settable AvailableAtUtc on add (PR-14)
└── Modules/
    ├── Pages/
    │   ├── QuickBuilder.Pages.Contracts/
    │   │   + Publishing/IPublishedContentReader.cs  PublishingCapabilities.cs
    │   │   + Events/PagePublished.cs  PageUnpublished.cs  ComponentPublished.cs  ComponentUnpublished.cs
    │   └── QuickBuilder.Pages/
    │       + Domain/Publication.cs  PublishedVersion.cs  PublishSchedule.cs  PublishLogEntry.cs
    │       + Domain/DependencyExtractor.cs
    │       + Infrastructure/PublishedReadDbContext.cs  PublishedContentReader.cs  GenerationBump.cs
    │       + Infrastructure/Migrations/<ts>_AddPublishing.cs
    │       ~ Infrastructure/PagesDbContext.cs  PagesOutbox.cs  PageTreeWrites.cs (bump on address change)
    │       ~ Features/DeletePage/  DeleteComponent/  GetPageTree/  ListComponents/  (publication checks and fields)
    │       + Features/PublishPage/  UnpublishPage/  RollbackPage/  GetPageHistory/  GetPageVersion/
    │       │          CopyPageVersionToDraft/  SetPageSchedule/  CancelPageSchedule/
    │       + Features/PublishComponent/  UnpublishComponent/  RollbackComponent/  GetComponentHistory/
    │       │          GetComponentVersion/  CopyComponentVersionToDraft/  SetComponentSchedule/  CancelComponentSchedule/
    │       + Features/GetPublishLog/
    │       + Features/RunDueSchedule/  RecordDesignPublish/  RefreshPagesShowingMedia/   # event handlers
    ├── Editor/
    │   ├── QuickBuilder.Editor.Contracts/
    │   │   + IPublishedDesignReader.cs  Events/DesignPublished.cs
    │   └── QuickBuilder.Editor/
    │       + Domain/DesignPublication.cs
    │       + Infrastructure/PublishedDesignReadDbContext.cs  PublishedDesignReader.cs
    │       + Infrastructure/Migrations/<ts>_AddDesignPublishing.cs
    │       + Features/PublishDesign/  RollbackDesign/  ListDesignVersions/  GetDesignVersion/
    │       ~ Features/GetDesignTokens/   # + publication summary
    ├── Media/
    │   ├── QuickBuilder.Media.Contracts/
    │   │   + IPublicMediaReader.cs  Events/FileDeleted.cs
    │   └── QuickBuilder.Media/
    │       + Infrastructure/PublicMediaReader.cs
    │       ~ Features/DeleteFile/        # + Media.FileDeleted in the outbox
    └── Rendering/
        + QuickBuilder.Rendering.Contracts/
        │   + IPublicNotFoundPage.cs  PublicResponse.cs  RenderingRoutes.cs
        + QuickBuilder.Rendering/
            + RenderingModule.cs
            + QuickBuilder.Rendering.csproj           # EmbeddedResource: admin blocks.css; Pages.Contracts schema
            + Domain/RenderKey.cs  RenderedPage.cs  BlockDefaults.cs  TokenCss.cs  NodeContext.cs
            + Rendering/HtmlWriter.cs  PageRenderer.cs  PageShell.cs  RichTextRenderer.cs  LinkResolver.cs
            + Rendering/Blocks/IBlockRenderer.cs  SectionRenderer.cs  ContainerRenderer.cs  HeadingRenderer.cs
            │          TextRenderer.cs  RichTextBlockRenderer.cs  ImageRenderer.cs  ButtonRenderer.cs
            │          SpacerRenderer.cs  DividerRenderer.cs  EmbedRenderer.cs  ComponentRefRenderer.cs
            + Features/RenderPage/RenderPageController.cs  RenderPageHandler.cs  PathNormaliser.cs
            + Features/ServeBlockStylesheet/
            + Features/NotFound/NotFoundPage.cs   # IPublicNotFoundPage
            + Resources/public-texts/{lang}.json  # one per supported site language (40), en.json the fallback

src/admin/
├── src/
│   ~ editor/blocks/blocks.css            # + .qb-page container rule
│   ~ editor/EditorTopBar.vue             # + Publish, publication state
│   + api/publishing.ts                   # client for contracts/publishing-api.yaml
│   ~ api/pages.ts  api/editor.ts         # + publication fields
│   ~ features/pages/PageTreeView.vue  ComponentsView.vue   # badges, Publishing menu, address warning, delete guard
│   + features/publishing/PublishMenu.vue  ScheduleDialog.vue  HistoryView.vue  PublishLogView.vue
│   │                     PublicationBadge.vue  addressWarning.ts  errorMessages.ts
│   ~ features/design/DesignTokensView.vue  # + Publish design, history, rollback
│   ~ app/router.ts                       # + history, version (read-only editor) and publish-log routes
│   ~ i18n/en.json
└── tests/
    + publishing/PublishMenu.spec.ts  ScheduleDialog.spec.ts  HistoryView.spec.ts  PublishLogView.spec.ts
    │            DesignPublish.spec.ts  addressWarning.spec.ts
    + editor/CanvasMarkupParity.spec.ts  tokenCss.parity.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Rendering: no DbContext, Contracts-only references
+ QuickBuilder.UnitTests/Pages/Publishing/   # PublicationTests, PublishScheduleTests, DependencyExtractorTests, GenerationBumpTests
+ QuickBuilder.UnitTests/Rendering/       # RendererGoldenTests, <Block>RendererTests, RichTextRendererTests, TokenCssParityTests,
│                                         #   BlockRendererCoverageTests, RendererSchemaVersionsTests, PathNormaliserTests,
│                                         #   PublishedPageJavaScriptBudgetTests, PublicTextCatalogueTests
~ QuickBuilder.IntegrationTests/
│   + Publishing/PublishApiTests.cs  ComponentPublishApiTests.cs  RollbackApiTests.cs  ScheduleApiTests.cs
│   │            PublishLogApiTests.cs  DesignPublishApiTests.cs  PublishingAuthorizationTests.cs
│   │            ScheduledPublishExactlyOnceTests.cs  GenerationBumpSiteFilterTests.cs  UpgradeFrom011Tests.cs
│   + Rendering/PublicRenderingTests.cs  CacheInvalidationTests.cs  RenderingAcrossInstancesTests.cs
│   │           PublicPathReadOnlyTests.cs  NotFoundParityTests.cs
│   + Isolation/Probes/PublishingIsolationProbe.cs
│   ~ Isolation/IsolationProbes.cs  Observability/LoggingTests.cs  Authorization/AuthorizationDeclarationTests.cs
+ tests/fixtures/rendering/golden/*.html  context.json  token-css/*.json  page-200-blocks.json
~ QuickBuilder.SmokeTests/                # ReferenceDeploymentSmokeTests: + publish, view, unpublish;
                                          # + RenderPerformanceTests
specs/002-identity-setup/contracts/authorization.md   # ~ §2 gains the two capabilities
specs/001-foundation-skeleton/contracts/integration-events.md   # ~ + 6 events, 1 module-internal message
```

**Structure decision**:
- **Rendering** is laid out like the other modules: `Domain/` holds the value types and
  token CSS, `Rendering/` the writer and block renderers, and `Features/` the two public
  endpoints and the not-found page. It has no `Infrastructure/` folder, because it has no
  database.
- **Pages and Editor** gain slices beside their existing ones.
- **Admin**: the publishing UI lives in `features/publishing/` and is used from the page
  tree, the component list, the editor top bar and the design screen.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as the Governance section requires.

| Exception | Status after this feature |
|---|---|
| **E-1**, **E-2** | Closed earlier. |
| **E-3** (the smoke test isn't the constitution's full sequence) | **Closed.** The journey now runs setup → create page → edit draft → publish → view from the site's host name → unpublish (FR-043). A new `smoke` CI job runs it on every build, together with every performance test (FR-041). Until now the smoke project was excluded from CI. Issue #4 can be closed for this part. |
| **E-4** (the cache can't invalidate across instances) | **Closed.** Rendering, the cache's only consumer, never relies on eviction: keys include database-held generations read on every request (PR-05), so every instance serves the new content at its next request whatever the provider. `RenderingAcrossInstancesTests` proves it. A shared cache provider remains a possible optimisation (sharing rendered bytes), not a correctness requirement. Multi-instance still needs a shared media storage provider, which 003 documents. |
| **E-5** (no browser in the release smoke test) | Unchanged. Published pages need no browser to check: the smoke test reads HTML. The editor's publish button is covered by vitest and quickstart §1, until #25. |

**New exceptions**: none.

## Risks

- **Markup drift between the canvas and the renderer** (two implementations of each
  block's markup: Vue and C#).
  - *Mitigation*: `CanvasMarkupParity.spec.ts` compares normalised trees for every golden
    document, and `RendererGoldenTests` pins the C# output. Changing either side without
    the other fails CI.
- **Hit-path latency**: site resolution (009 measured 1.4–1.9 ms), plus the live lookup and
  the design number, before any cache read.
  - *Mitigation*: compiled queries on unique or primary-key indexes, and
    `RenderPerformanceTests` asserts the 20 ms p95. If it is ever exceeded, the fallback is
    a short-lived per-instance cache of `(site, path) → generation`, invalidated by the same
    generation check. It is recorded, not built.
- **Generation bumps on large sites**: a component used by all 2,000 pages bumps 2,000 rows
  in the publish transaction.
  - *Mitigation*: one set-based `UPDATE` on the primary key. It is measured in
    `CacheInvalidationTests` at 2,000 pages, with a target under 500 ms.
- **Memory** from caching many pages, including stale generations until they age out.
  - *Mitigation*: sized entries under `Cache:MemoryLimitMegabytes`, a 1-hour sliding
    expiry, and eviction by least recent use.
- **A schedule's outbox message failing permanently** after the outbox's retries (only on a
  persistent infrastructure fault).
  - *Mitigation*: the schedule stays `pending` with a due time in the past. The admin shell
    shows it as overdue, a member can set it again, and the outbox's failed-message log
    line names it.
- **Read-only connections** add a second connection pool per reader module (Pages, Editor,
  Media).
  - *Mitigation*: pools are small and share the operator's server limits.
    `docs/operations.md` states the connection count.
- **Embedded provider players** contact YouTube or Vimeo when scrolled into view.
  - *Mitigation*: privacy-enhanced domains (`youtube-nocookie.com`, `dnt=1`), lazy loading,
    and a CSP that allows exactly those two frame origins. A click-to-load option is
    deferred.

## Deferred (recorded, not built)

- **Collection-driven pages and page → collection dependencies**: #16.
- **SEO metadata, sitemaps, canonical addresses and redirects**: #17. It will subscribe to
  the page events added here.
- **CDN integrations and external cache purge.**
- **Draft previews through the renderer**: #22 (clarification Q3 of clarify).
- **A custom not-found page and other error pages.**
- **Bulk publish, approval workflows, side-by-side version comparison, and scheduling the
  design.**
- **Cache warming, pre-compressed responses, a shared cache provider, and click-to-load
  embeds** (PR-23).
