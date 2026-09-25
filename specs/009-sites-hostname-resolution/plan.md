# Implementation Plan: Sites and Host Name Resolution

**Branch**: `009-sites-hostname-resolution` | **Date**: 2026-09-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-sites-hostname-resolution/spec.md`

**Issue**: #9. This feature narrows exception **E-4** from the
[001 plan](../001-foundation-skeleton/plan.md#complexity-tracking).

## Summary

Features 001 to 003 left a site that can be created but not managed. This feature makes
the site a complete concept:
- administrators edit its name, scheme, default language and primary host name;
- they add and remove host names, with one normalisation rule for every spelling;
- they disable and re-enable a site.

Public requests resolve only to enabled sites. A disabled site is indistinguishable from
an unknown host. Every module can read the resolved site's details, including a derived
base URL. A shared isolation suite, with build-level guards, makes every module that
stores site-owned data prove it keeps sites apart. Operators can keep the admin surface
off client domains.

The approach, detailed in [research.md](./research.md):
- **Resolution**: no cross-request cache. One compiled, indexed query per public request
  returns only enabled sites, and the result is memoised for the request. Changes reach
  every instance at the next request, so FR-012 holds without a bound and E-4's concrete
  defect goes away (S-01, S-02, S-03, S-20).
- **Host names**: one normaliser in `Shared.Kernel` covers trimming, the trailing dot,
  IDNA to ASCII and IP literals. It is used for storage, comparison, resolution and admin
  host names (S-04).
- **Aggregate integrity**: a `primary_host_name` column on the site, plus optimistic
  concurrency on the `sites` row through `xmin`. Settings edits carry a
  version. Host-name and availability actions retry on conflict (S-05, S-06).
- **Settings**: the scheme defaults to HTTPS, with a warning shown for HTTP. The language
  comes from a fixed list of 40 tags, defaulting to `en`. The base URL is derived, never
  stored (S-07, S-08, S-09).
- **Availability**: a `status` column and idempotent disable and enable actions.
  Lifecycle events go through the outbox, and Identity records them as security events
  (S-10, S-11, S-12).
- **Admin host names**: the optional `Hosting:AdminHostNames` setting confines `/api` and
  `/admin` to the listed hosts. Elsewhere those paths answer with the site's public
  not-found response. `/health` is exempt (S-13).
- **Proxy**: D-12 is kept. `ForwardLimit` is lifted so that every trusted hop is honoured,
  and a proxy and base-path test matrix is added (S-14).
- **Isolation suite**: per-module probes, a parameterised suite, and coverage guards over
  models and endpoints (S-15).
- **Upgrade**: one additive migration, with its snapshot upgrade test (S-16).

There are no new NuGet or npm packages.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**: ASP.NET Core MVC and `ForwardedHeadersMiddleware`, EF Core 10 +
Npgsql (`xmin` row version, compiled queries), BCL `IdnMapping` and `IPAddress`. Vue 3,
Pinia, vue-router and vue-i18n, plus the browser's `Intl.DisplayNames`. Nothing new.

**Storage**: PostgreSQL 16+. The `sites` schema gains three columns on `sites`, one on
`sites` for the primary host name ([data-model.md](./data-model.md)).
Identity's security event catalogue gains four types, with no schema change.

**Testing**: xUnit, `WebApplicationFactory` and Testcontainers PostgreSQL, as before.
New:
- the isolation suite with its coverage guards;
- the proxy and base-path matrix;
- the two-instance resolution test;
- `UpgradeFrom003Tests`;
- the host-name normaliser unit set (at least 40 inputs);
- `SiteResolutionPerformanceTests` in the smoke tests;
- vitest + axe for the site screens.

**Target Platform**: Linux containers (amd64 in CI), unchanged.

**Project Type**: Web application: a modular-monolith backend plus the Vue admin SPA it
serves.

**Performance Goals**:
- Resolution adds ≤ 5 ms to p95 of public requests with 100 sites and 500 host names
  (SC-007).
- Admin site screens load within the shell's existing budget.

**Constraints**:
- A change is visible on every instance at the next request (FR-012), so there is no
  cross-request resolution cache.
- A disabled site's response is byte-identical to an unknown host's (S-03).
- No host names in logs for unresolved requests (FR-032).
- Only additive changes to `/api/v1/sites` (001 FR-010a) and to `Sites.Contracts`
  (Principle III).

**Scale/Scope**:
- **Target size**: up to 100 sites and 500 host names with a performance target. Larger
  installations work without one.
- **Code**: 0 new modules; 1 new kernel type (`HostNames`); 1 new contract (`ICurrentSite`);
  4 new integration events.
- **Endpoints**: 5 new admin endpoints (languages, settings, remove host name, disable,
  enable) and 4 extended ones (list, get, create, add host name).
- **Admin shell**: 1 new screen, 1 extended one.
- **Capabilities**: 2 new ones.
- **Tests**: 1 new test folder (`Isolation/`).

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **Slices**: all new behaviour lives in the Sites module as new slices under `Features/`: `UpdateSettings`, `RemoveHostName`, `ChangeAvailability` and `ListLanguages`, plus the extended `CreateSite`, `AddHostName` and `GetSite`. Controllers stay thin. <br>• **Contracts**: other modules see only `Sites.Contracts` (`SiteDto`, `ISiteDirectory`, `ICurrentSite`, events). <br>• **Audit**: Identity learns of host-name and availability changes only through outbox events, so there is no cross-module write or transaction (S-11). <br>• **Shared code**: `HostNames` goes to `Shared.Kernel/MultiSite`. It is a dependency-free string rule that Host and Sites both need, and it holds no Sites concept (S-04). Site details stay out of `Shared.Infrastructure`'s `ISiteContext` (S-02). <br>• **Host**: it only composes. Its middleware decides the surface and calls `ISiteDirectory`. |
| II | Easy to Install, Operate and Upgrade | **Complies.** <br>• **Configuration**: one new optional key, `Hosting:AdminHostNames`, empty by default, which keeps today's behaviour. It is validated at startup, naming the key. A conflict with a site host name stops startup with a clear message. <br>• **Migration**: additive and automatic. <br>• **Network**: nothing outbound. <br>• **Health**: `/health` stays reachable on every host, so compose and orchestrator probes keep working (S-13). |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Migration**: it only adds columns with defaults, plus a data step that picks each site's earliest host name as primary. Nothing is dropped. <br>• **Upgrade test**: `UpgradeFrom003Tests` upgrades a 003 snapshot and asserts that every site, host name, grant and media address still works (FR-029, SC-008). <br>• **Contracts**: the HTTP and C# contracts change only additively. `FindByHostNameAsync` keeps its signature and narrows to enabled sites (S-03, S-17). <br>• **Export**: nothing here affects export (#20). The new site fields are plain columns an exporter can read. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies, and E-4 is narrowed.** No dependency is added. Removing the host-name cache makes site resolution correct with several instances, with no shared cache required (S-01). `ICacheProvider` stays for its next consumer (Rendering), which inherits E-4's remaining obligation. See Complexity Tracking. |
| V | Multi-Site Within One Installation | **Complies. This feature is the principle's home.** <br>• **Filters**: `SiteId` filtering on every site-owned entity is now guarded for **every** module's model, not just Sites'. <br>• **Isolation suite**: added, with coverage guards over models and endpoints (S-15). <br>• **Resolution**: from `Host` against configured host names, behind trusted proxies only, with a configurable base path (S-14). <br>• **Permissions**: per site, with two new site-scoped capabilities (S-12). <br>• **Cache keys**: no new ones. The global `site-by-host` key is removed (S-01). <br>• **Bypasses**: new `AcrossAllSites` uses in the settings, remove and availability slices each carry a reason. `SiteScopingTests` asserts the reasons. |
| VI | Content Schemas Are Versioned Contracts | **N/A.** No page-tree, collection or binding schema is touched. |
| VII | Draft and Published Are Separate Worlds | **N/A.** No pages, components or items. Disabling a site isn't publishing: it closes the public surface without touching content. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **N/A for rendering.** For the budgets: resolution is part of every public request, so it is held to ≤ 5 ms at p95 (SC-007). That fits well inside the 20 ms cache-hit budget Rendering will inherit. |
| IX | Secure by Default | **Complies.** <br>• **Forwarded headers**: trusted only from configured proxies, never by default. `ForwardLimit` can't let an untrusted hop through (S-14). <br>• **Admin exposure**: operators can confine the admin surface to admin host names, and requests there on client domains are indistinguishable from missing pages (S-13). <br>• **Disabled sites**: they reveal nothing about their existence (S-03). <br>• **Duplicate host names**: refusals never name the holder (FR-008). <br>• **HTTP**: HTTPS is the default, and choosing HTTP shows a visible warning (S-08). <br>• **Authorization**: every new endpoint declares a capability, and the authorization coverage test enforces it. <br>• **Audit**: host-name and availability changes are recorded as security events (S-11). <br>• **Logs**: host names never appear for unresolved requests (FR-032). |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Integration tests**: every new endpoint has them against real PostgreSQL. <br>• **Unit tests**: the normaliser and the aggregate. <br>• **Proxy**: the proxy and base-path matrix. <br>• **Instances**: the two-instance test for FR-012. <br>• **Upgrade**: the upgrade snapshot test. <br>• **Smoke test**: gains the two-site and disable steps (FR-031). <br>• **Coverage gate**: site resolution is already a critical path at ≥ 80%, and the normaliser and aggregate join it (S-19). |
| XI | Simplicity and Incremental Delivery | **Complies.** No packages. No speculative work: no deletion (deferred to #21 by clarification), no redirect engine (#17), no per-path sites, no distributed cache. The isolation suite is the smallest mechanism that makes forgetting fail. The feature ships whole in one PR, so no feature flag is needed. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only. The migration's one data `UPDATE` goes through `migrationBuilder.Sql`
  inside the owning module. Schema changes happen only through the migration. The data
  step deliberately updates every site's host names. `UpgradeFrom003Tests` (T012) is its
  dedicated test.
- The resolution path uses a compiled query and an `AsNoTracking` projection.
- Admin assets are built into the image, as before.

**Development Workflow**:
- Issue #9 → `specs/009-sites-hostname-resolution` → branch
  `009-sites-hostname-resolution` → one PR that closes #9.
- The spec folder's prefix is the issue number, as the constitution requires, and 001's
  `spec-ids.yml` check resolves `009` to issue #9. The issue exists and is open (checked
  2026-09-26).
- Clarify has run. Analyze follows tasks.

**Operator impact** (required by the workflow section):
- **New configuration keys**: 1, `Hosting:AdminHostNames:<n>`, optional, empty by default.
  See [research.md](./research.md#configuration-keys-introduced). It is to be added to
  `docs/configuration.md`.
- **Migrations**: 1 (`SiteSettingsAndLifecycle`, in the `sites` schema). Additive and
  automatic.
- **New dependencies**: none.
- **Upgrade notes**:
  1. Every existing site becomes **enabled**, uses **HTTPS** and **English**, and takes its
     earliest host name as **primary**. Nothing that was reachable stops being reachable.
  2. Host-name matching now also accepts internationalised names and trailing dots. An
     internationalised name added from now on is stored in its ASCII (`xn--`) form.
  3. With several trusted proxies, the forwarded host from the outermost trusted hop is
     now honoured. Before, only the nearest hop was.
  4. **Recommended**: set `Hosting:AdminHostNames` so that client domains don't expose the
     admin sign-in page.
  5. Site changes now take effect on every instance at once. Multi-instance deployments
     are still not advertised, because media storage and the future render cache need
     shared providers (E-4 as narrowed, and 003).

**Post-Phase-1 re-check**: the data model, the two contracts and the quickstart introduce
no violation:
- `sites-api.yaml` is additive over v1.
- `site-context.md` keeps site details in `Sites.Contracts` and only the id in
  `Shared.Infrastructure`.
- The isolation obligations restate Principle V as build-level checks.

E-4 is narrowed, and no new exception is needed. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/009-sites-hostname-resolution/
├── plan.md              # This file
├── spec.md              # Feature specification (clarified)
├── research.md          # Phase 0: decisions S-01 … S-20
├── data-model.md        # Phase 1: sites schema changes, invariants, events, error codes
├── quickstart.md        # Phase 1: validation guide, one section per story and criterion
├── contracts/
│   ├── sites-api.yaml   # HTTP contract: /api/v1/sites/** (additive) and the public surface
│   └── site-context.md  # Surface decision, ICurrentSite, HostNames, events, isolation duties
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/
├── QuickBuilder.Host/
│   ~ Configuration/HostingOptions.cs     # + AdminHostNames (validated via HostNames)
│   ~ Middleware/SiteResolutionMiddleware.cs
│   │                                     # surface decision per contracts/site-context.md §1;
│   │                                     # admin-host confinement; health exemption
│   + Middleware/PublicNotFound.cs        # the one public not-found writer (S-13)
│   ~ Startup/StartupTasks.cs             # ProxySetup: ForwardLimit = null (S-14);
│   │                                     # + admin-host-name conflict check after migrations
│   ~ Program.cs                          # public unmatched-path fallback → PublicNotFound
│   ~ appsettings.json                    # + Hosting:AdminHostNames: []
├── Shared/
│   └── QuickBuilder.Shared.Kernel/
│       + MultiSite/HostNames.cs          # TryNormalise, ToDisplay (S-04)
└── Modules/
    ├── Sites/
    │   ├── QuickBuilder.Sites.Contracts/
    │   │   ~ SiteDto.cs                  # + init properties; ISiteDirectory doc narrowed
    │   │   + ICurrentSite.cs
    │   │   + Events/HostNameAdded.cs  HostNameRemoved.cs  SiteDisabled.cs  SiteEnabled.cs
    │   └── QuickBuilder.Sites/
    │       ~ SitesModule.cs              # new slices, events, ICurrentSite, options
    │       ~ SitesCapabilities.cs        # + sites.settings.manage, sites.availability.manage
    │       + SitesOptions.cs             # binds Hosting:BasePath, Hosting:AdminHostNames
    │       ~ Domain/Site.cs              # scheme, language, status, primary, invariants, Version
    │       ~ Domain/SiteHostName.cs      # normalisation delegated to HostNames
    │       + Domain/SupportedLanguages.cs
    │       + Domain/SiteStatus.cs  SiteScheme.cs
    │       ~ Infrastructure/SitesDbContext.cs    # columns, checks, xmin, partial index
    │       ~ Infrastructure/SiteDirectory.cs     # no cache; compiled resolution query; per-request memo
    │       + Infrastructure/CurrentSite.cs       # ICurrentSite implementation
    │       + Infrastructure/SiteDtoMapper.cs     # one projection, including baseUrl
    │       + Infrastructure/ConcurrencyRetry.cs  # up to 3 attempts (S-05)
    │       + Infrastructure/Migrations/<ts>_SiteSettingsAndLifecycle.cs
    │       ~ Features/CreateSite/  AddHostName/  GetSite/  ListSites/   # extended
    │       + Features/UpdateSettings/  RemoveHostName/  ChangeAvailability/  ListLanguages/
    └── Identity/
        └── QuickBuilder.Identity/
            ~ Domain/SecurityEvent.cs     # + 4 event types
            + Features/RecordSiteSecurityEvents/   # 4 idempotent integration event handlers
            ~ IdentityModule.cs           # registers the handlers and event types

src/admin/
├── src/
│   ~ api/sites.ts                        # new fields and endpoints
│   ~ app/router.ts                       # + /sites/:siteId
│   ~ features/sites/SitesView.vue        # status + HTTP badge, link to detail, richer create dialog
│   + features/sites/SiteDetailView.vue   # settings, host names, availability
│   + features/sites/HostNameList.vue  AvailabilityPanel.vue  LanguageSelect.vue
│   ~ i18n/en.json                        # all new strings
└── tests/
    ~ sitesView.spec.ts
    + siteDetailView.spec.ts              # axe + keyboard

tests/
~ QuickBuilder.ArchitectureTests/         # FindByHostNameAsync only called from Host middleware;
│                                         #   HostNames lives in Kernel with no dependencies
~ QuickBuilder.UnitTests/Sites/           # + HostNamesTests (≥40 inputs), SiteAggregateTests,
│                                         #   SupportedLanguagesTests
~ QuickBuilder.IntegrationTests/
│   + Isolation/                          # ISiteIsolationProbe, SiteIsolationSuite,
│   │                                     #   IsolationCoverageTests, Probes/{Sites,Media}IsolationProbe
│   ~ Sites/SiteFilterCoverageTests.cs    # every module's model
│   + Sites/SiteResolutionTests.cs  HostNameManagementTests.cs  SiteSettingsTests.cs
│   │       SiteAvailabilityTests.cs  ResolutionAcrossInstancesTests.cs
│   │       ProxyResolutionTests.cs  BasePathTests.cs  AdminHostNameTests.cs
│   │       UpgradeFrom003Tests.cs
│   + Identity/SiteSecurityEventTests.cs
│   ~ Identity/SiteVisibilityTests.cs     # new endpoints: view vs manage
│   ~ Observability/LoggingTests.cs       # no host names in logs
~ QuickBuilder.SmokeTests/                # + two sites, disable; SiteResolutionPerformanceTests

docs/
~ configuration.md                        # + Hosting:AdminHostNames
~ operations.md                           # + "Sites and host names": resolution, unknown/disabled,
│                                         #   admin host names, proxies and base path, TLS/DNS are
│                                         #   the operator's; upgrading from 003
~ contributing.md                         # + "Adding a module with site-owned data"
specs/002-identity-setup/contracts/authorization.md   # ~ §2 capability table gains the 2 new ones
```

**Structure decision**: all product behaviour stays in the existing Sites module's
layout. The only shared addition is `HostNames` in `Shared.Kernel/MultiSite`, beside
`ISiteOwned`, because Host and Sites must agree on one definition (S-04). The isolation
suite is test infrastructure, and lives in the integration test project, where
`EndpointCatalog` and `QuickBuilderApp` already are. Changing 002's `authorization.md`
follows that document's own rule that new capabilities are listed there.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as required by the Governance
section.

| Exception | Status after this feature |
|---|---|
| **E-1** (no authentication on the admin surface) | Closed by 002. |
| **E-2** (no media storage abstraction) | Closed by 003. |
| **E-3** (smoke test is not the constitution's full sequence) | Unchanged. The smoke test gains two sites and disable, but create page, publish and view still wait for Pages and Rendering. Issue #4. |
| **E-4** (in-memory cache can't invalidate across instances) | **Narrowed.** Its concrete defect, host-name lookups going stale on other instances, is removed: resolution no longer caches (S-01), and `ResolutionAcrossInstancesTests` proves it. What remains is that `ICacheProvider` has no cross-instance invalidation, and has no consumer until Rendering. The Rendering or shared-cache spec must solve that before multi-instance is advertised. The docs keep not advertising it. |

**New exceptions**: none.

## Risks

- **Resolution now costs a database round trip on every public request.**
  - *Mitigation*: it is a compiled query on a unique index, and SC-007's performance test
    gates it at ≤ 5 ms p95. If Rendering's budgets later need it, a cache keyed by host
    can come back, with invalidation that works across instances (E-4) as the
    precondition.
- **The base URL has no port** (S-07). Local setups on a non-standard public port get a
  base URL that won't open as-is.
  - *Mitigation*: nothing in this feature uses the base URL to serve requests, and
    `docs/operations.md` says so. A later feature that emits absolute links (sitemaps,
    #17) can add a port setting if needed.
- **`ForwardLimit = null` changes behaviour for operators who trusted several proxies.**
  - *Mitigation*: the outer trusted hop is the correct source, and the old behaviour was a
    latent bug. It is listed in upgrade note 3 and covered by the proxy matrix.
- **Admin host names can lock operators out** if they're misconfigured (for example a
  typo, so the admin surface answers nowhere).
  - *Mitigation*: the setting is empty by default. `/health` stays reachable. Operators
    can always remove the key and restart. The documentation gives the recovery step. The
    startup conflict check catches the most likely mistake: listing a site's host name.
- **Audit entries arrive after the action.** They appear within the outbox poll interval,
  5 s by default (S-11).
  - *Mitigation*: the delay is documented, and the outbox guarantees delivery. Tests use
    the dispatcher's drain helper.
- **The isolation guard relies on `EndpointCatalog` recognising record-id route values.**
  - *Mitigation*: the guard treats as a record key any route value ending in `Id`, other than
    `siteId`, plus an explicit list of named keys (`hostName` today). A new kind of key
    must be added to that list. A test proves
    the guard fires on a fixture endpoint.

## Deferred (recorded, not built)

- **Deleting sites**, and what happens to their content: #21, by clarification. `status`
  is text so that #21 can add states.
- **Redirecting non-primary host names to the primary**: #17.
- **A public port in the base URL**: with the first feature that emits absolute links.
- **A cross-instance cache for site resolution**: only if Rendering's budgets require it,
  and only with E-4 solved.
- **Site-level design tokens**: #10.
- **Per-site administrator role**: a future identity feature. The two new capabilities are
  already separate, so it can grant one without the other (S-12).
