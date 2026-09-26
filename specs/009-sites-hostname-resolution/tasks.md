---

description: "Task list for 009-sites-hostname-resolution"
---

# Tasks: Sites and Host Name Resolution

**Input**: Design documents from `/specs/009-sites-hostname-resolution/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec measures behaviour through automated checks (SC-002 to
SC-009). FR-025 requires a shared isolation suite, and constitution Principle X makes
tests mandatory, with site resolution as a critical path. Within each story, test tasks
come first and must fail before implementation starts.

**Organization**: Tasks are grouped by user story (US1–US5 from spec.md) so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

The layout is the modular monolith from [plan.md](./plan.md#project-structure). This
feature adds no module. Shorthand used below:

- `SITES` = `src/Modules/Sites/QuickBuilder.Sites`
- `SITESC` = `src/Modules/Sites/QuickBuilder.Sites.Contracts`
- `IDENT` = `src/Modules/Identity/QuickBuilder.Identity`
- `HOST` = `src/QuickBuilder.Host`
- `KERNEL` = `src/Shared/QuickBuilder.Shared.Kernel`
- `ADMIN` = `src/admin/src`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`

Feature folders follow 001's one-folder-per-slice rule. Each `Features/<Name>/<Name>.cs`
holds the controller action, request/response, command or query, validator and handler,
and each slice is registered in `SITES/SitesModule.cs` in the same task. Research
decisions are cited as `S-xx`. Error codes are those in
[data-model.md](./data-model.md#validation-and-error-codes).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents and its one configuration key. Nothing here has
behaviour.

- [X] T001 Commit `specs/009-sites-hostname-resolution/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `009-sites-hostname-resolution` at the repository root (plan.md "Development Workflow")
- [X] T002 [P] Add `"AdminHostNames": []` to the `Hosting` section of `HOST/appsettings.json`, with the default from research.md "Configuration keys introduced"

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the host-name normaliser, the schema, the richer site contract and the
uncached resolution path that every story builds on.

**⚠️ CRITICAL**: no user-story work can begin until this phase is complete.

### Host-name normaliser (S-04)

- [X] T003 [P] Write `UTESTS/Sites/HostNamesTests.cs`: a theory with at least 40 inputs (SC-004).
  - **Equivalent spellings** that normalise to one value: case, surrounding spaces, trailing dot, the Unicode and `xn--` forms of `bücher.example`, `127.0.0.1`, and `[::1]`, `::1` and `0:0:0:0:0:0:0:1`.
  - **Refusals**: scheme, port, path, query, `@`, `*`, interior space, empty label, leading or trailing hyphen, a 64-character label, 254 characters in total, invalid Unicode, and `a..b`.
  - **`ToDisplay`**: round-trips, and falls back to ASCII on undecodable input.
- [X] T004 Implement `KERNEL/MultiSite/HostNames.cs`: `public static bool TryNormalise(string? input, out string normalised)` and `public static string ToDisplay(string normalised)`, following the ordered steps in S-04. Use `IdnMapping` (UseStd3AsciiRules) and `IPAddress.TryParse` only, with no package references, so that T003 passes.
- [X] T005 Make `SITES/Domain/SiteHostName.cs` delegate `Normalise` and `IsValid` to `HostNames.TryNormalise`, and delete its own regex. Then confirm the existing `ITESTS/Sites/SiteLifecycleTests.cs` still pass.

### Schema and domain (S-05, S-08, S-09, S-10, data-model.md)

- [X] T006 [P] Create `SITES/Domain/SupportedLanguages.cs`: the 40 tags from S-09 in order, `Default = "en"`, and `TryCanonicalise(string?, out string)`, which is case-insensitive and returns the canonical case. Test it in `UTESTS/Sites/SupportedLanguagesTests.cs`.
- [X] T007 [P] Create `SITES/Domain/SiteStatus.cs` (`Enabled` = `"enabled"`, `Disabled` = `"disabled"`) and `SITES/Domain/SiteScheme.cs` (`Https`, `Http`, and `TryParse`, which is case-insensitive).
- [X] T008 Write `UTESTS/Sites/SiteAggregateTests.cs` for the invariants in data-model.md:
  - create with an optional primary (default: the first host name), scheme and language;
  - `ChangeSettings(name, scheme, language, primary)`, with each error code;
  - `AddHostName`, which never makes the new name primary;
  - `RemoveHostName`, refusing the primary (`sites.primary_host_name_removal`) and the only name (`sites.last_host_name`);
  - `Disable` and `Enable`, which return whether the status changed;
  - `UpdatedAtUtc` moves on every change.
- [X] T009 Extend `SITES/Domain/Site.cs` so that T008 passes:
  - add `Scheme`, `DefaultLanguage`, `Status` and `uint Version`;
  - add `PrimaryHostName`, a column on the site (revised from a per-host-name flag; see S-05);
  - extend `Create(name, hostNames, primaryHostName?, scheme?, language?, clock)`;
  - add `ChangeSettings`, `RemoveHostName`, `Disable` and `Enable`, each returning `Result` or `Result<bool>`.

- [X] T010 Update `SITES/Infrastructure/SitesDbContext.cs`:
  - columns `scheme`, `default_language` and `status`, with their defaults and the two check constraints;
  - `primary_host_name` (`citext`);
  - `Version` mapped to `xmin` with `IsRowVersion()`.
- [X] T011 Generate the migration `SITES/Infrastructure/Migrations/<timestamp>_SiteSettingsAndLifecycle.cs` with `dotnet ef migrations add SiteSettingsAndLifecycle` (project `SITES`, startup `HOST`). Right after `primary_host_name` is added, insert the S-16 data step with `migrationBuilder.Sql`, which sets each site's `primary_host_name` to its earliest host name (`DISTINCT ON (site_id) … ORDER BY site_id, created_at_utc, id`).
- [X] T012 [P] Write `ITESTS/Sites/UpgradeFrom003Tests.cs`, following `ITESTS/Media/UpgradeFrom002Tests.cs`:
  - **Snapshot**: apply the Sites migrations only up to `InitialSites`, plus the full Identity and Media migrations, then seed two sites (one with three host names created at different times) and a grant. After the upgrade, a media file is uploaded to an upgraded site and served from its address.
  - **Start**: start the current app.
  - **Assert**: every site is `enabled`, `https` and `en`; the earliest host name is primary; every host name resolves; the media address serves; the grant applies (SC-008, FR-029).

### Contracts and the directory (S-01, S-02, S-17, contracts/site-context.md)

- [X] T013 Extend `SITESC/SiteDto.cs`: keep the four positional parameters, and add `required init` properties `PrimaryHostName`, `Scheme`, `BaseUrl`, `DefaultLanguage`, `Status`, `Version` and `UpdatedAtUtc` (contracts/site-context.md §3). Narrow the XML doc of `ISiteDirectory.FindByHostNameAsync` to "the enabled site holding this host name, or null" (S-03).
- [X] T014 [P] Create `SITESC/ICurrentSite.cs` with `SiteDto? Site { get; }`, and document it as public-surface only (contracts/site-context.md §2).
- [X] T015 [P] Create the four integration events in `SITESC/Events/`:
  - `HostNameAdded.cs`
  - `HostNameRemoved.cs`
  - `SiteDisabled.cs`
  - `SiteEnabled.cs`

  Each is a record with `Id`, `OccurredAtUtc`, `SiteId`, `HostName` where applicable, `Guid? ActorUserId`, and a `static EventType` from data-model.md. Mirror `SITESC/Events/SiteCreated.cs`, including its "names are permanent" remark.
- [X] T016 [P] Create `SITES/SitesOptions.cs`, bound in `SitesModule`. It reads `Hosting:BasePath` and `Hosting:AdminHostNames` (normalised with `HostNames.TryNormalise`, and invalid entries ignored here because the host validates them).
- [X] T017 Create `SITES/Infrastructure/SiteDtoMapper.cs`: one `Expression<Func<Site, …>>` projection plus a finishing step. It computes `BaseUrl` (S-07: `{scheme}://{primary}{BasePath}`, or with a trailing `/` when the base path is empty), orders `HostNames` primary first, and builds `hostNameDetails` using `HostNames.ToDisplay`. Switch `ListSites`, `GetSite`, `CreateSite` and `SiteDirectory` to it.
- [X] T018 Rewrite `SITES/Infrastructure/SiteDirectory.cs` (S-01, S-02, S-20):
  - remove `ICacheProvider` and `InvalidateHostName`, and every caller of it in `CreateSite` and `AddHostName`;
  - `FindByHostNameAsync` normalises, then runs an `EF.CompileAsyncQuery` over `HostNames.AcrossAllSites(reason)` joined to `Sites`, filtered on `Status == enabled`, projected with the mapper;
  - memoise the resolved `SiteDto` by id for the scope, so that `FindByIdAsync` for the same id returns it without a query;
  - the resolution bypass is proven by T046.
- [X] T019 [P] Create `SITES/Infrastructure/CurrentSite.cs`, which implements `ICurrentSite` from `ISiteContext.SiteId` and `SiteDirectory`'s memo. Register it as scoped in `SITES/SitesModule.cs`.
- [X] T020 [P] Create `SITES/Infrastructure/ConcurrencyRetry.cs`: `ExecuteAsync(Func<CancellationToken, Task<Result<T>>>, int attempts = 3)`. It clears the change tracker and retries on `DbUpdateConcurrencyException`, then returns `Error.Conflict("sites.out_of_date", …)` (S-05).
- [X] T021 Add `SettingsManage = "sites.settings.manage"` and `AvailabilityManage = "sites.availability.manage"` to `SITES/SitesCapabilities.cs`, both `CapabilityScope.Site` with no roles besides Administrator (S-12). Add both to the capability table in `specs/002-identity-setup/contracts/authorization.md` §2.

### Test harness

- [X] T022 [P] Create `ITESTS/Sites/SitesApi.cs`, typed helpers over `/api/v1/sites` for every endpoint in contracts/sites-api.yaml:
  - create, get, list, languages;
  - `PutSettings`, `AddHostName`, `RemoveHostName`, `Disable`, `Enable`;
  - `PublicGetAsync(app, host, path)`.

  Reuse `MediaApi`'s request-header conventions.
- [X] T023 [P] Generalise `ITESTS/Sites/SiteFilterCoverageTests.cs`. For every registered `IModuleDatabaseProvider`, create its context and assert a query filter on every `ISiteOwned` entity, and that at least one module has one (S-15, FR-024).

**Checkpoint**: the solution builds; `HostNamesTests`, `SiteAggregateTests` and `UpgradeFrom003Tests` pass; existing Sites, Identity and Media tests pass with the uncached directory; the migration applies to an empty and to a 003 database. User-story work can begin.

---

## Phase 3: User Story 1 - An administrator creates and edits a site's settings (Priority: P1) 🎯 MVP

**Goal**: create a site with a primary host name, scheme and language, and edit its
name, scheme, language and primary. Stale edits are refused, and Designers and Editors
can only view.

**Independent Test**: quickstart §1–§2. Create a site with two host names and a language,
then rename it, change the language and switch the primary. Invalid values are refused
with reasons, and a stale version gets `409 sites.out_of_date`.

### Tests for User Story 1 ⚠️ write first, confirm they fail

- [X] T024 [P] [US1] Write `ITESTS/Sites/SiteSettingsTests.cs`:
  - create with defaults and with explicit primary, scheme and language;
  - each 400 code from data-model.md, returned together in `errors`;
  - `PUT …/settings` changes every field and returns a new `version`, and `baseUrl` follows the primary and scheme;
  - a stale `version` gives 409 `sites.out_of_date`, with nothing changed;
  - a concurrent `PUT` making host X primary, raced against `DELETE` of X, leaves exactly one primary (US1 scenario 5, S-05).
- [X] T025 [P] [US1] Extend `ITESTS/Identity/SiteVisibilityTests.cs`:
  - a Designer and an Editor get 200 on `GET /api/v1/sites/{id}` with every new field, and 403 on `PUT …/settings`;
  - a user without a grant gets 404;
  - an Administrator gets 200 (FR-022, FR-023);
  - `permissions` is all false for Designer and Editor, and all true for Administrator.
- [X] T026 [P] [US1] Write `ITESTS/Sites/LanguagesEndpointTests.cs`: `GET /api/v1/sites/languages` returns the 40 tags in order with `default: "en"` for any signed-in user, and 401 anonymously.
- [X] T027 [P] [US1] Write `src/admin/tests/siteDetailView.spec.ts`, settings section:
  - the form loads a site, saves, and on `sites.out_of_date` shows the reload prompt;
  - the HTTP scheme shows the warning;
  - the language select shows `Intl.DisplayNames` names;
  - read-only when the site response has `permissions.canManageSettings = false`;
  - axe is clean, and keyboard-only save works.

### Implementation for User Story 1

- [X] T028 [US1] Extend `SITES/Features/CreateSite/CreateSite.cs`:
  - `CreateSiteRequest` and the command gain optional `PrimaryHostName`, `Scheme` and `DefaultLanguage`;
  - the validator reports `sites.invalid_scheme`, `sites.unsupported_language` and `sites.primary_host_name_invalid`, alongside the existing codes;
  - refuse a host name that equals a configured admin host name with `sites.host_name_taken` (S-13);
  - return the mapped `SiteDto`.
- [X] T029 [US1] Create `SITES/Features/UpdateSettings/UpdateSettings.cs`: `PUT api/v1/sites/{siteId:guid}/settings` with `[RequireCapability(SitesCapabilities.SettingsManage)]` and the `SiteSettings` body (S-06).
  - Load the site with `AcrossAllSites(reason)` and set the original `Version` from the request.
  - Call `ChangeSettings` and save.
  - Map `DbUpdateConcurrencyException` to 409 `sites.out_of_date`.
  - Publish no event.
  - Register the handler and validator in `SITES/SitesModule.cs`.
  - The `AcrossAllSites` bypass is proven by T024.
- [X] T030 [P] [US1] Create `SITES/Features/ListLanguages/ListLanguages.cs`: `GET api/v1/sites/languages` with `[RequireSignedIn]`, returning `{ languages, default }`. Register it.
- [X] T031 [US1] Update `SITES/Features/GetSite/GetSite.cs` and `SITES/Features/ListSites/ListSites.cs` to return the mapper's output, including `hostNameDetails`, and confirm that the existing response fields are unchanged (S-17). Add the `permissions` object to every site response, computed with `IAccessEvaluator` per request. It is HTTP only; `SiteDto` doesn't change.
- [X] T032 [P] [US1] Extend `ADMIN/api/sites.ts`: types for `Site` (including `permissions`), `HostName` and `SiteSettings` per contracts/sites-api.yaml, plus `getSite`, `putSettings` and `listLanguages`, and the new `createSite` fields.
- [X] T033 [P] [US1] Create `ADMIN/features/sites/LanguageSelect.vue`: options from `listLanguages`, labels from `new Intl.DisplayNames([locale], { type: 'language' })`, falling back to the tag.
- [X] T034 [US1] Create `ADMIN/features/sites/SiteDetailView.vue` with the settings section:
  - a form with name, scheme (with an HTTP warning), language and primary;
  - version handling and the out-of-date prompt;
  - read-only when `permissions.canManageSettings` is false.

  Add the route `/sites/:siteId` in `ADMIN/app/router.ts`.
- [X] T035 [US1] Extend `ADMIN/features/sites/SitesView.vue`:
  - each row links to the detail page and shows a status badge and an HTTP warning badge;
  - the create dialog gains primary, scheme and `LanguageSelect`.

  Update `src/admin/tests/sitesView.spec.ts`.
- [X] T036 [US1] Add every new US1 string to `ADMIN/i18n/en.json`: settings labels, scheme warning, out-of-date prompt and error-code messages.

**Checkpoint**: US1 is demoable. Sites are created and edited with all settings, and the rules hold on the server.

---

## Phase 4: User Story 2 - An administrator manages a site's host names (Priority: P1)

**Goal**: add and remove host names with one normalisation rule, and installation-wide
uniqueness that never names the holder. The primary and last host names are protected.
Every change is audited.

**Independent Test**: quickstart §3. Add, resolve and remove a name, and see the next
public request fail. Every equivalent spelling of a taken name is refused as taken, and
every invalid form is refused as invalid.

### Tests for User Story 2 ⚠️ write first, confirm they fail

- [X] T037 [P] [US2] Write `ITESTS/Sites/HostNameManagementTests.cs`:
  - add an IDN name, and the response carries `hostName` `xn--…` and `displayName`;
  - every equivalent spelling from T003 of a name held by another site gives 409 `sites.host_name_taken`, and the body never contains the other site's id or name;
  - adding a name the same site holds gives 409;
  - adding a name held by a disabled site gives 409 (insert the disabled status directly through `SitesDbContext` until US4 lands);
  - an admin host name gives 409;
  - removing a non-primary name gives 204, and the next public request gets `sites.not_resolved`;
  - removing the primary gives `sites.primary_host_name_removal`, the last name `sites.last_host_name`, and an unknown name 404 `sites.host_name_not_found`;
  - a removed name can be added to another site;
  - two concurrent adds of one name to two sites: exactly one 201 and one 409.
- [X] T038 [P] [US2] Write `ITESTS/Identity/SiteSecurityEventTests.cs` (host-name half): after an add and a remove, and after draining the outbox, Identity holds exactly one `site_host_name_added` and one `site_host_name_removed`, with actor, `siteId` and `hostName`. Redelivering the same outbox message adds nothing (FR-028, S-11).
- [X] T039 [P] [US2] Extend `src/admin/tests/siteDetailView.spec.ts` with the host-name section:
  - add, remove and make primary;
  - read-only when `permissions.canManageHostNames` is false;
  - remove disabled, with an explanation, for the primary and last name;
  - Unicode display;
  - error-code messages;
  - axe and keyboard.

### Implementation for User Story 2

- [X] T040 [US2] Update `SITES/Features/AddHostName/AddHostName.cs`:
  - normalise with `HostNames`;
  - refuse admin host names;
  - wrap the work in `ConcurrencyRetry`;
  - write `Sites.HostNameAdded` (actor from `IAccessEvaluator.UserId`) to the outbox in the same `SaveChanges`;
  - return `displayName` and `isPrimary` along with the existing fields;
  - register the event type in `SITES/SitesModule.cs`.
- [X] T041 [US2] Create `SITES/Features/RemoveHostName/RemoveHostName.cs`: `DELETE api/v1/sites/{siteId:guid}/host-names/{hostName}` with `[RequireCapability(SitesCapabilities.ManageHostNames)]`.
  - Normalise the route value, then call `Site.RemoveHostName` under `ConcurrencyRetry`.
  - Write `Sites.HostNameRemoved` to the outbox in the same `SaveChanges`.
  - Return 204.
  - Register the handler and the event type.
  - The `AcrossAllSites` bypass is proven by T037.
- [X] T042 [US2] Add four security event types to `IDENT/Domain/SecurityEvent.cs` (`site_host_name_added`, `site_host_name_removed`, `site_disabled`, `site_enabled`), with their detail records in `IDENT/Domain/Details/SiteEventDetails.cs` (`{ siteId, hostName? }`).
- [X] T043 [US2] Create `IDENT/Features/RecordSiteSecurityEvents/RecordSiteSecurityEvents.cs`, with `IIntegrationEventHandler` implementations for `HostNameAdded` and `HostNameRemoved`.
  - Each inserts a `ProcessedEvent(eventId, handlerName)` into **Identity's own** `processed_events`, and appends the security event, in one `IdentityDbContext.SaveChanges`.
  - A unique-key violation means the event was already recorded, so return without error.
  - Why: the shared dispatcher's marker lives in the publisher's schema and a separate transaction.
  - Register the handlers in `IDENT/IdentityModule.cs`.
- [X] T044 [P] [US2] Create `ADMIN/features/sites/HostNameList.vue`:
  - list with display names and a primary badge;
  - an add field;
  - remove with confirmation;
  - "Make primary", which calls `putSettings` with the current version.

  Mount it in `SiteDetailView.vue`, and add `addHostName` and `removeHostName` to `ADMIN/api/sites.ts`.
- [X] T045 [US2] Add the US2 strings to `ADMIN/i18n/en.json`.

**Checkpoint**: US1 + US2: sites and host names are fully manageable, and every host-name change is audited.

---

## Phase 5: User Story 3 - Visitors reach the right site, directly or through a reverse proxy (Priority: P1)

**Goal**: public requests resolve to enabled sites only, from the normalised visitor host,
behind trusted proxies and under a base path. Changes are visible on every instance at
once. The admin surface can be confined to admin host names.

**Independent Test**: quickstart §1, §5, §6 and §7. Two sites resolve correctly, directly
and through a trusted proxy with `/cms`. Spoofed forwarded headers are ignored. A change
on one instance is seen at the next request on another. Admin paths on a client domain
look like missing pages.

### Tests for User Story 3 ⚠️ write first, confirm they fail

- [X] T046 [P] [US3] Write `ITESTS/Sites/SiteResolutionTests.cs`:
  - A and B resolve with case, trailing dot, port and IDN variants;
  - an unknown host gets the fixed `sites.not_resolved` body;
  - `ICurrentSite.Site` inside a request equals `GET /api/v1/sites/{id}` for that site, and the second read in the same request makes no query (count commands with an EF interceptor);
  - an admin request sees `ICurrentSite.Site == null` (FR-013, FR-015).
- [X] T047 [P] [US3] Write `ITESTS/Sites/ResolutionAcrossInstancesTests.cs`, modelled on `ITESTS/Media/DeleteAcrossInstancesTests.cs`. With two apps over one database, warm resolution on instance 2, add, remove and change the primary through instance 1, and assert that instance 2's very next request reflects each change (SC-005, FR-012).
- [X] T048 [P] [US3] Write `ITESTS/Sites/ProxyResolutionTests.cs`, the S-14 matrix. Configure `Hosting:KnownProxies`, and use `TestServer`'s remote-address feature to vary the connection address:
  - a trusted `X-Forwarded-Host` resolves;
  - an untrusted one is ignored;
  - two trusted hops use the outer one;
  - an untrusted outer hop is ignored;
  - a forwarded host with a port or mixed case resolves;
  - forwarded proto and for are honoured only when trusted (SC-006).
- [X] T049 [P] [US3] Write `ITESTS/Sites/BasePathTests.cs`: with `Hosting:BasePath=/cms`, the public `/cms/`, the admin `/cms/api/v1/sites` and a media address under `/cms/_media/…` all work, and `baseUrl` ends with `/cms` (FR-017).
- [X] T050 [P] [US3] Write `ITESTS/Sites/AdminHostNameTests.cs`, with `Hosting:AdminHostNames:0=admin.test`:
  - on `admin.test`, `/api` and `/admin` work;
  - on a site host, `/admin/`, `/admin/sites` and `/api/v1/sites` answer byte-identically to `/no-such-page` on that host, and no endpoint runs (assert no authentication cookie is read, through a log or counter hook);
  - on an unknown host, they get `sites.not_resolved`;
  - `/health/ready` answers on every host;
  - on `admin.test`, `GET /api/v1/no-such-endpoint` gives the same admin-surface 404 as before this feature, not `public.not_found`;
  - with an empty list, the old behaviour holds;
  - a configured admin host name held by a site fails startup with a message naming `Hosting:AdminHostNames`;
  - an invalid entry fails startup (FR-018a).
- [X] T051 [P] [US3] Add `tests/QuickBuilder.ArchitectureTests/SiteResolutionRuleTests.cs` (IL scan with Mono.Cecil): `FindByHostNameAsync` is called only by the host's resolution middlewares. Also assert that `KERNEL/MultiSite/HostNames` references nothing outside the BCL, and prove the rule fires in `RulesFireWhenBrokenTests.cs` with a fixture.
- [X] T052 [P] [US3] Extend `ITESTS/Observability/LoggingTests.cs`: across unknown-host, disabled-host, admin-host-confinement and proxy requests, no captured log line contains the requested host name (FR-032).

### Implementation for User Story 3

- [X] T053 [US3] Add `AdminHostNames` to `HOST/Configuration/HostingOptions.cs`, with an options validator that normalises each entry through `HostNames.TryNormalise`, fails startup naming `Hosting:AdminHostNames` for an invalid entry, and exposes the normalised set.
- [X] T054 [P] [US3] Create `HOST/Middleware/PublicResponses.cs` (both fixed public answers) and `HOST/Middleware/AdminHostConfinementMiddleware.cs`, which runs before the security headers and static files: one fixed `404` problem body for "no such public path on this site" (`errorCode: "public.not_found"`), written with no request data. Answer unmatched public paths from the resolution middleware when routing found no endpoint (a fallback endpoint would be a minimal API, which the constitution rules out), for paths outside `/api`, `/admin`, `/health` and `/_media`. Unknown paths under `/api` keep their current admin-surface 404, `/_media` misses keep Media's 404, and `/admin/*` keeps the SPA fallback. The response is `PublicNotFound` in contracts/sites-api.yaml.
- [X] T055 [US3] Rewrite the surface decision in `HOST/Middleware/SiteResolutionMiddleware.cs`, following contracts/site-context.md §1:
  - `/health` passes through untouched;
  - reserved admin prefixes on an allowed host pass through as the admin surface;
  - reserved admin prefixes on any other host (when the list is non-empty) resolve the site, then write `sites.not_resolved` or `PublicNotFound`, without calling `next`;
  - everything else resolves through `FindByHostNameAsync`, which now returns enabled sites only.

  Keep the existing no-host-in-logs rule.
- [X] T056 [US3] In `HOST/Startup/StartupTasks.cs`:
  - set `ForwardLimit = null` in `ProxySetup.BuildOptions` (S-14), with a comment on why untrusted hops still stop processing;
  - after migrations, fail startup when any normalised admin host name is held by a site, naming the key and the host name (S-13). Read the host names through `ISiteDirectory`, and add `Task<bool> IsHostNameTakenAsync(string)` to `SITESC/SiteDto.cs`'s `ISiteDirectory` for this. It is additive. Implement it in `SITES/Infrastructure/SiteDirectory.cs` as an `AnyAsync` over `HostNames.AcrossAllSites(reason)`. Run this task after T013, because both edit `SITESC/SiteDto.cs`.

**Checkpoint**: US1–US3 form the MVP. Sites are managed, and resolution is correct for every deployment shape, with changes visible everywhere at once.

---

## Phase 6: User Story 4 - An administrator disables and re-enables a site (Priority: P2)

**Goal**: take a site offline without losing anything. It answers like an unknown host,
keeps its host names, stays workable in the admin surface, is audited, and can be
re-enabled exactly as it was.

**Independent Test**: quickstart §4. Disable a site with media. Public requests, including
media, answer byte-identically to an unknown host. The admin shell still works on it, and
its host name stays taken. Re-enable it and everything is back.

### Tests for User Story 4 ⚠️ write first, confirm they fail

- [X] T057 [P] [US4] Write `ITESTS/Sites/SiteAvailabilityTests.cs`:
  - `POST …/disable` gives 200 and `status: disabled`;
  - `GET /` and the media address on its host are byte-identical to an unknown host's response;
  - the admin media list and upload still work for the site;
  - adding its host name elsewhere gives 409;
  - disabling twice gives 200, with one event in the outbox;
  - `POST …/enable` restores public access with the same data;
  - Designer and Editor get 403 and non-members 404 (FR-019–FR-022, SC-002);
  - with two instances, disable on one and the other's next request is not resolved (extend T047's fixture).
- [X] T058 [P] [US4] Extend `ITESTS/Identity/SiteSecurityEventTests.cs`: disable and enable each record exactly one `site_disabled` or `site_enabled`, idempotent under redelivery, and a no-op disable records nothing.
- [X] T059 [P] [US4] Extend `src/admin/tests/siteDetailView.spec.ts` and `sitesView.spec.ts`:
  - the availability panel with a confirmation stating the visitor effect;
  - the disabled badge in the list;
  - the Media link still usable;
  - the panel is hidden when `permissions.canManageAvailability` is false;
  - axe and keyboard.

### Implementation for User Story 4

- [X] T060 [US4] Create `SITES/Features/ChangeAvailability/ChangeAvailability.cs` with two actions:
  - `POST api/v1/sites/{siteId:guid}/disable`
  - `POST api/v1/sites/{siteId:guid}/enable`

  Both use `[RequireCapability(SitesCapabilities.AvailabilityManage)]`. Call `Site.Disable` or `Site.Enable` under `ConcurrencyRetry`, and write `Sites.SiteDisabled` or `Sites.SiteEnabled` to the outbox only when the status changed. Return the mapped site, and register the handler and the event types. The `AcrossAllSites` bypass is proven by T057.
- [X] T061 [US4] Add the `SiteDisabled` and `SiteEnabled` handlers to `IDENT/Features/RecordSiteSecurityEvents/RecordSiteSecurityEvents.cs`, with the same idempotency as T043, and register them in `IDENT/IdentityModule.cs`.
- [X] T062 [P] [US4] Create `ADMIN/features/sites/AvailabilityPanel.vue`: disable and enable, with a confirmation dialog explaining that visitors will see "not found" and nothing is deleted. Mount it in `SiteDetailView.vue`, and add `disableSite` and `enableSite` to `ADMIN/api/sites.ts`.
- [X] T063 [US4] Show the disabled state in `ADMIN/features/sites/SitesView.vue` and in the detail header. When the site shown is disabled, keep its screens open with the explanation (spec edge case). Add the US4 strings to `ADMIN/i18n/en.json`.

**Checkpoint**: US1–US4 are complete. The full site lifecycle is available, apart from deletion, which is deferred to #21.

---

## Phase 7: User Story 5 - Every module is proven to keep sites apart (Priority: P2)

**Goal**: a shared isolation suite that every module with site-owned data must pass, and
build-level guards that fail when a module or endpoint isn't covered.

**Independent Test**: quickstart §8. The suite passes for Sites and Media. Adding an
uncovered `ISiteOwned` entity to the test-only module makes the build fail and names it.

### Tests and harness for User Story 5

- [X] T064 [P] [US5] Create `ITESTS/Isolation/ISiteIsolationProbe.cs` with the members from contracts/site-context.md §6: `Module`, `SeedAsync(HttpClient admin, Guid siteId)` returning `SeededRecords(ids, publicPaths)`, `AdminRequests(Guid siteId, SeededRecords other)` and `PublicRequests(SeededRecords other)`. Also create `ITESTS/Isolation/IsolationProbes.cs`, which lists every probe.
- [X] T065 [US5] Create `ITESTS/Isolation/SiteIsolationSuite.cs`, a `[Theory]` over `IsolationProbes`:
  1. Create sites A and B and seed both.
  2. Every admin request on A with B's record ids gives 404, and a fresh read shows B's records unchanged.
  3. Every public request for B's records on A's host is byte-identical to a never-issued id.
  4. Every list or count on A contains none of B's ids.
  5. Disable B: every public request for B's records on B's host is byte-identical to an unknown host (FR-025, FR-026, SC-003).
- [X] T066 [P] [US5] Create `ITESTS/Isolation/Probes/SitesIsolationProbe.cs`. It seeds host names. Its admin requests are `DELETE …/{A}/host-names/{B's name}`, which must give 404 `sites.host_name_not_found` with B unchanged, and `GET /api/v1/sites/{A}`, which must not list B's names.
- [X] T067 [P] [US5] Create `ITESTS/Isolation/Probes/MediaIsolationProbe.cs`. It seeds one file of each type through `MediaApi`. Its admin requests are the content preview and delete on A for B's file ids, and the list on A. Its public requests are B's `publicPath` values.
- [X] T068 [US5] Create `ITESTS/Isolation/IsolationCoverageTests.cs` with two rules:
  - every `IModuleDatabaseProvider` whose model has an `ISiteOwned` entity has a probe with a matching `Module`;
  - every endpoint in `ITESTS/Infrastructure/EndpointCatalog.cs` with a `{siteId}` route value and a record key (a route value ending in `Id` other than `siteId`, or a named key in `RecordKeys = ["hostName"]`) is matched by at least one probe's `AdminRequests` pattern.

  Prove both rules fire: the module guard names `Widgets` when run against `TwoModuleApp` (the existing test-only module, whose `Widget` is site-owned and has no probe), and the endpoint guard flags a synthetic unexercised endpoint.

### Documentation for User Story 5

- [X] T069 [US5] Add "Adding a module with site-owned data" to `docs/contributing.md`: the four obligations from contracts/site-context.md §6, how to write a probe with `MediaIsolationProbe` as the example, and what each guard reports.

**Checkpoint**: all five stories are complete and independently testable.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T070 [P] Document `Hosting:AdminHostNames` in `docs/configuration.md` under "Hosting and reverse proxies". Cover the default, the effect on `/api` and `/admin`, the `/health` exemption, the conflict check, and recovery by removing the key.
- [X] T071 [P] Add "Sites and host names" to `docs/operations.md`:
  - how host names reach sites, including normalisation and IDN;
  - what visitors see for unknown and disabled sites;
  - admin host names, and why they're recommended;
  - trusted proxies, several hops and the base path;
  - that the base URL has no port (S-07);
  - that TLS certificates and DNS are the operator's responsibility (FR-030);
  - the audit delay (S-11);
  - "Upgrading from feature 003", with the plan's five upgrade notes.
- [X] T072 [P] Update the E-4 wording in `docs/operations.md` "More than one instance" to match the plan's Complexity Tracking: resolution is multi-instance-correct, and multi-instance still isn't advertised.
- [X] T073 Extend `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs` (FR-031): create a second site, check that each site's host name reaches its own site, disable one and check that it's no longer served, then re-enable it.
- [X] T074 [P] Create `tests/QuickBuilder.SmokeTests/SiteResolutionPerformanceTests.cs` under the existing `Performance` category and `docker-compose.perf.yml`. Seed 100 sites with 500 host names, then run 2,000 requests at concurrency 20 each to `GET /` on site hosts and to `/health/live`. Assert that the p95 difference is ≤ 5 ms one request at a time, and report the same comparison at concurrency 20 without asserting it (SC-007, S-20 as revised).
- [X] T075 [P] Add `KERNEL/MultiSite/HostNames.cs`, `SITES/Domain/Site.cs` and `HOST/Middleware/SiteResolutionMiddleware.cs` to the critical-path coverage list in the coverage gate configuration used by CI (see the 001 gate), and confirm they're at ≥ 80%.
- [X] T076 Run the full suite: `dotnet build` (warnings as errors), `dotnet test` for all four test projects, and `npm test` plus lint in `src/admin`. Fix any analyzer or formatting finding.
- [X] T077 Run [quickstart.md](./quickstart.md) §1–§10 against the reference compose deployment, and record the results in `specs/009-sites-hostname-resolution/quickstart-results.md`, as 003 did.
- [X] T078 Open the pull request from `009-sites-hostname-resolution` to `main`, titled "Sites and host name resolution (#9)". Its body says "Closes #9" and links to `specs/009-sites-hostname-resolution/spec.md` and `plan.md` (constitution Development Workflow).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup, and blocks every story. Within it, the order is T004 → T005; T009 → T010 → T011 → T012; and T013 → T017 → T018 → T019.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational. Its "make primary" UI (T044) uses US1's `putSettings` (T032) and `SiteDetailView` (T034).
- **US3 (Phase 5)**: depends on Foundational. T047's host-name steps use US2's endpoints (T040, T041). The rest is independent of US1 and US2.
- **US4 (Phase 6)**: depends on Foundational. T061 extends US2's handler file (T043), and T062 and T063 extend US1's views.
- **US5 (Phase 7)**: depends on US2 (T041, the remove endpoint the Sites probe exercises) and on Media, which already exists. It runs its disabled-site step against US4's endpoints (T060).
- **Polish (Phase 8)**: depends on every story it exercises.

### User Story Dependencies

```text
Setup ─▶ Foundational ─┬─▶ US1 ─┬─▶ US2 ─┬─▶ US4 ─┐
                       │        │        └─▶ US5 ◀─┤ (disable step)
                       └─▶ US3 ◀┘ (T047 uses US2)  │
                                                   └─▶ Polish
```

### Within Each User Story

- Tests are written first and must fail.
- The order is domain, then slice, then admin API client, then views, then strings.
- A slice's DI registration and event-type registration go in `SITES/SitesModule.cs` in the same task as the slice.
- Every new `AcrossAllSites` call carries a reason. `SiteScopingTests.Every_cross_site_bypass_states_a_reason` must keep passing.

### Parallel Opportunities

- **Setup**: T002.
- **Foundational**:
  - T003, T006 and T007 in parallel;
  - T012 once T011 exists;
  - T014, T015 and T016 in parallel;
  - T019 and T020 in parallel;
  - T022 and T023 in parallel.
- **US1**: tests T024–T027 in parallel; then T030, T032 and T033 alongside T028 and T029.
- **US2**: T037–T039 in parallel; then T044 alongside T040–T043.
- **US3**: T046–T052 in parallel; then T054 alongside T053.
- **US4**: T057–T059 in parallel; then T062 alongside T060 and T061.
- **US5**: T064, T066 and T067 in parallel, then T065 and T068.
- **Across stories**: once Foundational is done, US1 and US3 can proceed in parallel, because they touch different files (`Features/*` and `SiteDetailView` versus `HOST/*`).
- **Polish**: T070–T072, T074 and T075 in parallel.

---

## Parallel Example: User Story 3

```text
# Tests first, all at once:
T046 SiteResolutionTests.cs      T047 ResolutionAcrossInstancesTests.cs   T048 ProxyResolutionTests.cs
T049 BasePathTests.cs            T050 AdminHostNameTests.cs               T051 ModuleBoundaryTests.cs
T052 LoggingTests.cs

# Then: T053 HostingOptions.cs + T054 PublicNotFound.cs, then T055 middleware, then T056 startup.
```

## Parallel Example: User Story 5

```text
# Harness and probes together:
T064 ISiteIsolationProbe.cs      T066 SitesIsolationProbe.cs      T067 MediaIsolationProbe.cs
# Then the suite and the guards:
T065 SiteIsolationSuite.cs  →  T068 IsolationCoverageTests.cs  →  T069 docs/contributing.md
```

---

## Implementation Strategy

### MVP first (US1 + US2 + US3)

1. Phases 1–2. Stop at the Foundational checkpoint, with the upgrade test green.
2. US1 (settings), then US2 (host names). The site is now fully manageable.
3. US3 (resolution, proxy, admin host names). Every P1 story is done.
4. **Validate**: quickstart §1, §2, §3, §5, §6 and §7. This is a shippable increment, because the spec's P1 promises all hold.

### Incremental delivery

- US4 adds disabling. It's small once the MVP exists.
- US5 adds the isolation suite and guards. It must land before Pages (#10) starts storing site-owned data.
- Polish finishes the docs, smoke test, performance gate and quickstart record.

### Parallel team strategy

After Foundational: developer A takes US1 → US2 → US4 (the Sites slices and admin UI).
Developer B takes US3 (host middleware and proxy tests), then US5 (isolation harness).
They meet at Polish.

---

## Notes

- `[P]` means different files and no dependency on an incomplete task.
- **Confirm each story's tests fail before implementing it.** Commit after each task, or
  each logical group, with the issue reference (#9).
- **Don't reintroduce a cross-request host-name cache.** FR-012 and SC-005 depend on
  there being none (S-01).
- **Only add to `SiteDto`, `ISiteDirectory` and `/api/v1/sites`.** Never rename or
  remove anything (S-17, Principle III).
- **Recording security events**: Identity records site events through its own
  `processed_events`, in the same transaction as the security event (T043). Never rely on
  the publisher's marker for Identity's effect.
