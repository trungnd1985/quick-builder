# Implementation Plan: Draft Preview Links

**Branch**: `022-draft-preview-links` | **Date**: 2026-11-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-draft-preview-links/spec.md`

**Issue**: #22.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.

## Summary

Members can open any draft page or collection item, or any retained past version, rendered
by the same renderer visitors get. They can also hand a reviewer without an account a
secret link to one page or item. The link lasts 7 days by default and 30 at most, and
members can revoke it. Every preview carries a bar saying what is shown. No preview is ever
indexed, stored in a cache, or served at a public address.

The approach, detailed in [research.md](./research.md):

- **Ownership** (PV-01): Editor stores links in `editor.preview_links` and serves the admin
  endpoints. Rendering renders previews. No new module.
- **Reading drafts** (PV-02): new read-only contracts, `IPreviewContentReader` (Pages) and
  `IPreviewItemReader` (Cms). The public readers and the public responder stay untouched,
  so the public path still cannot reach a draft.
- **Composition** (PV-03, clarification Q2):
  - the target is shown from its draft or chosen version;
  - components, menus and the design are shown live, or from their draft when never
    published, and the bar names them;
  - lists and references show published data only.
- **One render path** (PV-04): `PageResponder`'s gathering step becomes a `PageComposer`
  over a content source, either live or preview. `RenderInputs` gains an optional
  `PreviewContext`. A parity test proves the preview equals the published output apart from
  the bar and addresses.
- **Where previews are served** (PV-05, PV-06):
  - a new `/_preview` prefix that answers on every host name, with the site taken from the
    link or route, never from `Host`;
  - member previews use the admin session cookie;
  - link previews use `/_preview/l/{secret}`;
  - media and the stylesheet are served through preview routes;
  - page links point at the public site.
- **Links** (PV-07, PV-08, PV-17, PV-18, PV-19):
  - a 256-bit random secret, stored as SHA-256;
  - checked on every request, never cached: active, unexpired, created after
    `Preview:LinksValidFrom`, the creator still allowed to edit (new
    `Identity.Contracts.IMemberAccess`), and the target readable (the trash filter applies);
  - at most 20 active links per target, under an advisory lock;
  - failed lookups throttled per address group;
  - an hourly sweep.
- **Response rules** (PV-10, PV-11, PV-12):
  - `no-store`, `noindex, nofollow` and `no-referrer` on every response;
  - one identical "no longer available" page for every failure;
  - media limited to what the target uses;
  - forms post to a harmless preview endpoint.
- **Bar** (PV-13): server-rendered, outside the page root, script-free hide control, styles
  in the page's single hashed style element.
- **Admin** (PV-14): a Preview button (save first, then open), a Share preview dialog, and
  version previews in both histories. Administrators also get a site-wide "Preview links"
  view.
- **History** (PV-16): `Editor.PreviewLinkCreated` and `PreviewLinkRevoked` events through
  the outbox. Pages and Cms record them in the page's publish log and the item's history.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 and Npgsql, the in-house dispatcher and outbox,
  and hosted services.
- **Admin**: Vue 3, Pinia, vue-router and vue-i18n.
- **Nothing new**.

**Storage**: PostgreSQL 16+. Three migrations, all additive (data-model.md):

| Module | Migration | Change |
|---|---|---|
| Editor | `AddPreviewLinks` | **+** `preview_links` (4 indexes), **+** `preview_link_failures` |
| Pages | `AddPreviewLinkLogExpiry` | **+** nullable `publish_log.expires_at_utc` |
| Cms | none | `item_history` checks no kind, so the two new kinds need no migration |

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe.

- **Unit**:
  - `PreviewCompositionTests` (each row of PV-03);
  - `PreviewLinkTests` (expiry bounds, label rules, revoke idempotence, `IsActive`);
  - `PreviewBarTests` (golden markup for each bar variant);
  - `PreviewAddressesTests`.
- **Integration**:
  - `PreviewParityTests` (SC-001);
  - `MemberPreviewTests`;
  - `PreviewLinkTests`;
  - `PreviewLinkTamperingTests` (SC-005);
  - `PreviewLinkAddressTests`;
  - `PreviewResponseRulesTests`;
  - `PreviewCacheIsolationTests` (SC-004);
  - `FormSubmitIsolationTests`;
  - `PreviewLogRedactionTests`;
  - `PreviewLinkRevocationTests`, two instances (SC-006);
  - `PreviewLinkCreatorAccessTests`;
  - `PreviewLinkHistoryTests`;
  - `PreviewAuthorizationTests`;
  - `VersionPreviewTests` (SC-007);
  - `PreviewEdgeCaseTests` (trash, no template, invalid draft, needs attention, limit,
    throttle, emergency switch, import);
  - `PreviewUpgradeTests`, which upgrades the previous release's snapshot through the
    three migrations.
- **Performance**: `PreviewBudgetTests` (SC-003).
- **Architecture**:
  - `PreviewBoundaryTests` (PV-21);
  - `AuthorizationDeclarationTests` and the isolation probes, extended;
  - `PublicRouteBoundaryTests`, extended for `_preview`.
- **Admin**: `SharePreviewDialog.spec.ts`, `PreviewLinksView.spec.ts` and
  `PreviewButton.spec.ts`.

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend and the Vue admin SPA, plus
one new server-rendered surface, `/_preview`.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Preview render**: p95 under 500 ms (PV-22). That is 012's 200 ms cache-miss render plus
  at most four indexed reads. The spec goal is 1 s (SC-003).
- **Public pages**: unchanged. The `PageComposer` extraction must keep the 200 ms / 20 ms
  budgets, and the existing budget tests guard it.
- **Link create, list and revoke**: one transaction each, a few indexed rows.

**Constraints**:
- **The public path stays published-only**, enforced by the type graph (PV-21).
- **No preview output in the render cache**; only the `preview-media:` id sets.
- **Link secrets** never stored, logged or put in history (FR-007, FR-032).
- **No outbound calls**.

**Scale/Scope**:
- **Code**: 0 new projects and 0 new modules.
- **Migrations**: 2. Tables added: 2. Tables changed: 1 (one nullable column).
- **Endpoints**:
  - admin: 5 (create, list for a target, site list, revoke, revoke all);
  - `/_preview`: 7 routes (contracts/public-preview.md).
- **Contracts**: 5 new interfaces (`IPreviewContentReader`, `IPreviewItemReader`,
  `IPreviewLinks`, `IMemberAccess`, `IMediaContent`), 2 events, 1 capability.
- **Configuration keys**: 1.
- **Admin**: about 4 components and 1 API module, plus 5 changed views.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module and no new project references** (module-surface.md §1). <br>• **Slices**: Editor gains `Features/PreviewLinks/` (create, list, site list, revoke, revoke all, sweep) and a `PreviewLink` aggregate that keeps its invariants. Rendering gains `Features/Preview/` (member preview, link preview, preview media, preview assets, preview form) and a pure `PreviewComposition`. Pages and Cms each add a reader and an event handler. <br>• **Cross-module**: reads go through contracts only. History reaches Pages and Cms through outbox events. No transaction spans modules; the link check reads before rendering, as 023 does. <br>• **Domain model**: Rendering's rich-model requirement is met by `PreviewComposition` and the `PreviewContext` value objects. Cms and Pages history entries keep using their aggregates' factories. |
| II | Easy to Install, Operate and Upgrade | **Complies.** One optional key, `Preview:LinksValidFrom` (no default), validated by `ValidateOnStart` and documented in `docs/configuration.md`. Three automatic, forward-only, re-runnable migrations. Health checks unchanged. Works with zero host names (PV-05). |
| III | Upgrades Never Lose Data | **Complies.** Additive migrations only. `PreviewUpgradeTests` upgrades the previous release's snapshot. Export and import are unchanged in format; an import clears the site's links (PV-20). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. Revocation is exact across instances because nothing is cached (PV-08). The only cache use is the `preview-media:` id set through `ICacheProvider`, which works in memory or with Redis, and a miss only costs a recomputation. Media is read through `IMediaContent`, so every storage provider works. The sweep is safe on several instances. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Filters**: `preview_links` is site-owned with the site filter. `preview_link_failures` is installation data with the marker reason. <br>• **Site context**: the preview handler sets `ISiteContext` from the link or route before any read, so every reader is filtered. <br>• **Namespacing**: the media cache key carries `SiteId`. <br>• **Background work**: the sweep reads across sites through `AcrossAllSites` with a reason. <br>• **Raw SQL**: the advisory lock is parameterised `SqlQuery` and names the site (PV-17). <br>• **Isolation**: probes cover the new admin endpoints and the member routes. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** No schema changes. Previews render every stored schema version through the same upgrade-on-read path as publishing, and page drafts are validated with the shared validator (PV-15). |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• **Visitors**: public addresses still read only published data. Drafts are reachable only through contracts the public responder cannot hold (architecture test). <br>• **Read-only**: previews read through read-only contexts and change no content. Their only writes are Editor's link bookkeeping (last-opened, failure windows), which is not content. <br>• **Publishing**: nothing publishes from a preview. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** Previews are server-side HTML from the same renderer (PV-04). They bypass the render cache entirely, so the dependency graph and invalidation are untouched (FR-023). The public budgets are guarded by the existing tests after the composer extraction. The preview bar adds no script. |
| IX | Secure by Default | **Complies.** <br>• **Secrets**: unguessable link secrets, stored as hashes and shown once. <br>• **Checks**: every request is checked, including the creator's current access. <br>• **Abuse**: failure throttling. <br>• **Leaks**: `noindex`, `no-store` and `no-referrer`; one identical failure page; paths with secrets never logged. <br>• **Content**: encoding unchanged. Forms in previews post nowhere real, and the CSP is kept. <br>• **Authorization**: server-side checks on every endpoint; the new capability is Administrator-only. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new endpoint and route. <br>• **Domain**: unit tests for the composition, the link aggregate, addresses and bar golden markup. <br>• **Contracts**: a parity golden test for rendering. <br>• **Multi-instance**: a two-instance revocation test. <br>• **Upgrade**: an upgrade test. <br>• **Coverage**: 80% line coverage on `PageComposer`, the preview handlers and link resolution, which are critical render paths. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: no signed tokens or key management (PV-07), no event-driven revocation (PV-08), no translations of the bar texts yet, no comments or approval. <br>• **Reuse**: the renderer, `EditingAccess`, `EditingDocumentKinds`, the outbox, the Identity hash pattern, `ClientAddress` and Media's serve logic. <br>• **Delivery**: one PR. Every story is demoable; no feature flag is needed because nothing changes until a member presses Preview. |

**Technology Constraints**:
- MVC controllers only, including the `/_preview` controllers.
- EF Core only. Two raw SQL uses, both parameterised and inside Editor:
  - the advisory lock (PV-17);
  - the failure-window upsert (`INSERT … ON CONFLICT`, PV-18), as 018's `RateWindows` does.

  Neither reads site-owned content; the failure windows are installation data.
- No Node process at run time.
- No new admin libraries.

**Development Workflow**:
- Issue #22 → `specs/022-draft-preview-links` → branch `022-draft-preview-links` → one PR
  that closes #22.
- Clarify has run: 2 questions at specify and 3 in clarify, all answered. Analyze follows
  tasks.
- **Spec alignment**: recorded in spec.md in this change, to be confirmed in
  `/speckit-analyze`.
  1. **FR-030, items** (PV-15): an item that "needs attention" is still previewed, with a
     bar warning. Only page drafts that fail the page schema are refused. Malformed item
     values cannot be stored.
  2. **FR-002 and SC-001, addresses** (PV-06): "identical apart from the preview bar and
     preview-only response rules" includes the rewritten media, stylesheet and link
     addresses and the robots and referrer meta tags.
  3. **Link host** (PV-05): a link is built on the site's primary host name, or on the admin
     origin when the site has none. The spec is silent on this.
  4. **Bar texts and times** (PV-13): English, with times in UTC. Translations are
     deferred.
  5. **Template page links** carry a sample item (data-model `sample_item_id`), which
     extends FR-005 for template pages.
- **Contract and registry changes in the same PR**:
  - `specs/001-foundation-skeleton/contracts/integration-events.md`: 2 events;
  - `specs/002-identity-setup/contracts/authorization.md`: 1 capability and the new
    endpoints;
  - `specs/012-publishing-rendering/contracts/`: a pointer from public rendering to
    `/_preview` and the extraction of `PageComposer`;
  - `specs/017-seo/contracts/`: `robots.txt` gains `Disallow: /_preview/`;
  - `specs/020-site-export-import/contracts/archive-parts.md`: the design part clears
    preview links on import;
  - `docs/configuration.md`, `docs/operations.md` (emergency switch), `docs/backlog.md`
    (remove the #22 items, add the deferred list);
  - `docs/contracts/` is not touched: no schema changes.

**Operator impact**:
- **New configuration keys**: `Preview:LinksValidFrom` (env `Preview__LinksValidFrom`), an
  optional ISO 8601 UTC timestamp with no default. Links created before it stop working.
  A value more than a day in the future stops startup.
- **Migrations**: 2, additive and automatic: Editor `AddPreviewLinks` and Pages
  `AddPreviewLinkLogExpiry`. Cms needs none (its history checks no kind).
- **New dependencies**: none.
- **Reverse proxy**: `/_preview/` must be passed to the app like every other path. Proxies
  that only forward `/` on site hosts and `/admin` and `/api` on admin hosts must add it on
  the host where links should open.
- **Upgrade notes** (`docs/releases/unreleased/22-draft-preview-links.md`):
  1. Members can preview drafts and past versions, and share time-limited preview links
     with people who have no account. Links work on the site's host name, or on the admin
     host name for sites without one.
  2. If your proxy forwards only selected paths, forward `/_preview/` too.
  3. To make every existing preview link stop working at once, set
     `Preview__LinksValidFrom` to the current time and restart.

**Post-Phase-1 re-check**:
- `preview-api.yaml` declares a capability on every operation, and a row check where the
  kind decides.
- `module-surface.md` adds no edges.
- Preview links are site-owned with the site filter, and the failure windows carry the
  installation marker.
- Events carry ids, a number and a time only.
- Raw SQL only for the advisory lock and the failure-window upsert.
- The published read contexts are unchanged; drafts are read through new
  `PreviewReadDbContext`s.
- The public responder's dependencies are unchanged except for the composer extraction.
- The archive format is unchanged.

**Gate: PASS** (no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/022-draft-preview-links/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 2 + 3 questions)
├── research.md                # Phase 0: decisions PV-01 … PV-22
├── data-model.md              # Phase 1: tables, invariants, states, read models, errors
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── preview-api.yaml       # HTTP: create, list, site list, revoke, revoke all
│   ├── public-preview.md      # The /_preview surface: routes, checks, document, headers
│   ├── admin-preview.md       # Admin behaviour: Preview, Share preview, site list, bar
│   └── module-surface.md      # Readers, IPreviewLinks, IMemberAccess, IMediaContent, events, capability
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/QuickBuilder.Host/
    ~ Routing/RoutePrefixes.cs                             # Preview prefix, UnresolvedPublicPrefixes
    ~ Middleware/SiteResolutionMiddleware.cs               # skips /_preview
    ~ Program.cs  appsettings.json                         # PreviewOptions, ValidateOnStart
src/Modules/
├── Editor/QuickBuilder.Editor.Contracts/
│   + IPreviewLinks.cs  PreviewCapabilities.cs  Events/PreviewLinkEvents.cs
├── Editor/QuickBuilder.Editor/
│   + Domain/PreviewLink.cs  PreviewSecrets.cs
│   + Features/PreviewLinks/CreatePreviewLink.cs  ListPreviewLinks.cs  ListSitePreviewLinks.cs
│   +                       RevokePreviewLink.cs  RevokeAllPreviewLinks.cs  PreviewLinkResponses.cs
│   + Infrastructure/PreviewLinkResolver.cs (IPreviewLinks)  PreviewLinkFailures.cs  PreviewLinkSweepService.cs  PreviewOptions.cs
│   ~ Infrastructure/EditorDbContext.cs                    # two sets
│   + Infrastructure/Migrations/<ts>_AddPreviewLinks.cs
│   ~ Features/Transfer/DesignArchivePart.Import.cs        # clear the site's links (PV-20)
│   ~ EditorCapabilities.cs  EditorErrors.cs  EditorModule.cs
├── Pages/QuickBuilder.Pages.Contracts/Publishing/ + IPreviewContentReader.cs
├── Pages/QuickBuilder.Pages/
│   + Infrastructure/PreviewContentReader.cs
│   + Infrastructure/PreviewReadDbContext.cs               # drafts and versions, read-only, never migrated
│   + Features/PublishingHistory/RecordPreviewLinkEvents.cs
│   ~ Domain/PublishLogEntry.cs (LogActions)
│   + Infrastructure/Migrations/<ts>_AddPreviewLinkLogExpiry.cs
├── Cms/QuickBuilder.Cms.Contracts/Publishing/ + IPreviewItemReader.cs
├── Cms/QuickBuilder.Cms/
│   + Infrastructure/PreviewItemReader.cs
│   + Infrastructure/PreviewReadDbContext.cs               # item drafts and versions, read-only, never migrated
│   + Features/Items/History/RecordPreviewLinkEvents.cs
│   ~ Domain/ItemHistoryEntry.cs (kinds)
├── Identity/QuickBuilder.Identity.Contracts/ + IMemberAccess.cs
├── Identity/QuickBuilder.Identity/Infrastructure/Authorization/ + MemberAccess.cs
├── Media/QuickBuilder.Media.Contracts/ + IMediaContent.cs
├── Media/QuickBuilder.Media/
│   + Infrastructure/MediaContent.cs                       # shares ServePublicFile's logic
│   ~ Features/ServePublicFile/ServePublicFile.cs          # logic moved to a shared reader, behaviour unchanged
└── Rendering/QuickBuilder.Rendering/
    + Rendering/PageComposer.cs  LiveContentSource.cs       # extracted from PageResponder (PV-04)
    ~ Features/RenderPage/PageResponder.cs                 # uses PageComposer; no behaviour change
    ~ Domain/RenderInputs.cs                               # + Preview
    + Domain/PreviewContext.cs  PreviewBarModel.cs  PreviewAddresses.cs
    + Features/Preview/PreviewComposition.cs  PreviewContentSource.cs  PreviewResponder.cs
    +                  MemberPreviewController.cs  LinkPreviewController.cs  PreviewMediaController.cs
    +                  PreviewAssetsController.cs  PreviewFormController.cs  PreviewResponse.cs
    +                  PreviewMediaSet.cs  NotAvailablePage.cs  PreviewBar.cs
    ~ Rendering/PageShell.cs  LinkResolver.cs  Blocks/MediaRenderers.cs  Blocks/Pager.cs
    ~ Rendering/Forms/FormMarkup.cs                        # preview action, no script
    ~ Seo/HeadBuilder.cs                                   # robots and referrer meta in previews
    ~ Features/Robots/RobotsController.cs                  # Disallow: /_preview/
    ~ Features/RenderPage/PublicPathConstraint.cs          # _preview segment
    + Resources/PreviewTexts.resx
    ~ RenderingModule.cs

src/admin/src/
+ api/previewLinks.ts
+ features/preview/PreviewButton.vue  SharePreviewDialog.vue  PreviewLinksView.vue  previewUrls.ts
~ editor/EditorTopBar.vue  features/cms/items/ItemPublishBar.vue  features/cms/items/ItemHistoryPanel.vue
~ features/publishing/HistoryView.vue  features/pages/PageTreeView.vue  features/cms/items/ItemListView.vue
~ app/router.ts  features/settings/ (nav entry)  i18n/en.json
src/admin/tests/
+ features/preview/SharePreviewDialog.spec.ts  PreviewLinksView.spec.ts  PreviewButton.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/ (+ PreviewBoundaryTests; AuthorizationDeclarationTests; PublicRouteBoundaryTests)
+ QuickBuilder.UnitTests/Rendering/PreviewCompositionTests.cs  PreviewBarTests.cs  PreviewAddressesTests.cs
+ QuickBuilder.UnitTests/Editor/PreviewLinkTests.cs
~ QuickBuilder.IntegrationTests/
│   + Preview/ (the suites listed under Testing)
│   ~ Isolation/Probes/  Infrastructure/EndpointCatalog.cs
│   ~ Performance/ (+ PreviewBudgetTests)
specs/001-…/contracts/integration-events.md  specs/002-…/contracts/authorization.md       # ~
specs/012-…/contracts/  specs/017-…/contracts/  specs/020-…/contracts/archive-parts.md      # ~
docs/configuration.md  docs/operations.md  docs/backlog.md  docs/releases/unreleased/      # ~ / +
```

**Structure decision**:
- **Editor**: owns the link records and their admin slice, next to editing sessions, which
  span the same document kinds.
- **Rendering**: owns everything rendered, the bar included, as one `Features/Preview/`
  slice. Its only shared change is the `PageComposer` extraction, which keeps one render
  path.
- **Pages, Cms, Media and Identity**: each adds one read-only contract implementation. Pages
  and Cms also add one event handler each.
- **Admin**: one `features/preview/` folder, plus a Preview and a Share entry point on
  existing screens.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6**, **E-7** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The keyboard and screen-reader passes over Share preview, the site list and the bar's hide control are manual in quickstart §8, until #25. |

**New exceptions**: none.

## Risks

- **The `PageComposer` extraction touches the hot render path.** A subtle change would alter
  every published page or its budget.
  - *Mitigation*: it lands first as a pure refactor, before any preview code. All golden
    tests, `PublicRenderingTests` and the budget tests must pass unchanged.
    `PreviewParityTests` then holds both paths together.
- **A draft reaching a public address** through a mis-wired dependency.
  - *Mitigation*: `PreviewBoundaryTests` forbid the public types from depending on the
    preview readers. `PreviewCacheIsolationTests` request public addresses right after
    previews.
- **The link secret leaking** through logs, `Referer` or history.
  - *Mitigation*: the redactor test, `no-referrer` (header and meta), history payloads with
    ids only, and the secret shown once.
- **Proxies that forward only selected paths** make links 404 on some installations.
  - *Mitigation*: upgrade note 2 and `docs/operations.md`. The admin's Share dialog shows the
    full address, so an operator can test it at once.
- **Images in link previews** cost a referenced-set computation.
  - *Mitigation*: one computation per draft save per instance for 5 minutes (PV-11), and
    previews are low-traffic. `PreviewBudgetTests` include a page with 30 images.
- **An Administrator assumes revoking stops someone who already saved the page.**
  - *Mitigation*: the Share dialog says links are for viewing, and anything already opened
    may have been saved. Nothing technical can prevent that.

## Deferred (recorded, not built)

- Translations of the preview bar and the "no longer available" page beyond English (PV-13).
- Showing times in the viewer's time zone (would need a script).
- A per-site switch to turn link sharing off (clarification 1 chose all editors).
- Comments, approvals and sign-off on previews (spec Out of Scope).
- Previewing a component, menu or the design on its own, or a whole draft site.
- Password-protected or person-bound links.
