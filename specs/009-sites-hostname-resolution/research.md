# Research: Sites and Host Name Resolution

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-26

Phase 0 decisions for issue #9. Each one is numbered `S-nn` so that the plan, the tasks
and the code comments can cite it. The spec had no open `NEEDS CLARIFICATION` items after
`/speckit-clarify`. This document settles the technical questions the spec left to
planning, chiefly how changes reach every instance (FR-012).

Starting point, from features 001 to 003 (see the plan's summary):
- `Site` and `SiteHostName` entities.
- Create, list and get sites, and add a host name.
- `SiteResolutionMiddleware`, which runs after forwarded headers and the base path.
- `ISiteContext`, the per-entity query filters and `CrossSiteQuery.AcrossAllSites`.
- `SiteFilterCoverageTests`, which covers the Sites model only.
- An in-memory host-name cache with a 5-minute expiry (001 D-09, exception E-4).

---

## S-01: Resolution reads the database on every public request; the host-name cache is removed

**Decision**: `SiteDirectory` stops caching host-name lookups across requests. Every public
request resolves its site with one indexed query, and the result is memoised for the rest
of that request only (S-02). Removing a host name, or disabling or enabling a site, is
committed to the database before the operation returns. So the very next request on
*any* instance sees the change, which satisfies FR-012 and SC-005 without the fallback
bound.

**Rationale**:
- FR-012 requires changes to reach every instance before the operation reports success.
  The in-memory cache can't do that. A removed host name would keep resolving on other
  instances for up to 5 minutes, and a disabled site would stay public. That is exactly
  the E-4 defect, and this feature would make it user-visible, because removal and
  disabling are new.
- The lookup is a single probe on the existing unique `citext` index
  (`ux_site_host_names_host_name`), joined to `sites` by primary key. On the reference
  environment this costs well under a millisecond. SC-007 allows 5 ms at p95, measured
  by S-19's performance test.
- Media reached the same conclusion for file metadata (003 M-14). There, correctness
  across instances was worth more than a cache hit on an indexed read.

**Alternatives considered**:
- *Keep the cache and shorten its expiry to 5 s*: this meets only the spec's fallback
  bound, and it still serves a disabled site for up to 5 s on other instances. Cache
  eviction would also still depend on which instance handled the write.
- *PostgreSQL `LISTEN/NOTIFY` to evict on every instance*: correct, but it needs a
  dedicated long-lived connection, reconnect handling and a test harness for lost
  notifications. That machinery belongs in the shared-cache spec that E-4 already points
  to, not here.
- *A "sites version" row checked on every request*: that is still one query per request,
  so it saves nothing over the direct lookup and adds a second source of truth.

**Consequence for E-4**: the host-name lookup was E-4's only concrete consumer. After this
feature, resolution is correct with several instances. E-4 narrows to "the cache
abstraction has no cross-instance invalidation, for its future consumers", which means the
render cache. `ICacheProvider` stays, because the constitution names it and Rendering will
use it, but it has no consumer until then. See the plan's Complexity Tracking.

---

## S-02: Site context carries the site's details, through a Sites contract

**Decision**:
- `ISiteContext` (in `Shared.Infrastructure`) keeps carrying only the `SiteId`, which is
  what query filters need.
- A new `ICurrentSite` in `QuickBuilder.Sites.Contracts` gives any module the resolved
  site's details (FR-015) as a `SiteDto`: identifier, name, host names, primary host
  name, scheme, base URL, default language, status and version.
- `SiteDirectory` is scoped. When resolution finds the site, the `SiteDto` it loaded is
  memoised inside that request's `SiteDirectory`. A module that reads `ICurrentSite` or
  calls `FindByIdAsync` for the same site in the same request therefore makes no second
  query.
- On the administrative surface, where the site is named in the route, modules use
  `ISiteDirectory.FindByIdAsync(siteId)` as they do today.

**Rationale**: Principle I puts "site context" in `Shared.Infrastructure`, and requires
that project to stay free of module-specific concepts. A site's name, language and base
URL are Sites concepts, so they belong in the Sites contract. Filters and other modules
keep depending only on the id.

**Alternatives considered**:
- *Put the details on `ISiteContext`*: that would move Sites' model into
  `Shared.Infrastructure`, so every new site setting would edit shared code.
- *Stash the DTO in `HttpContext.Items`*: untyped, and invisible to handlers, which don't
  see `HttpContext`.

---

## S-03: A disabled site answers exactly like an unknown host name

**Decision**:
- The resolution query filters on `status = 'enabled'` inside the same statement that
  looks up the host name. For a disabled site it returns no row, just as for an unknown
  host.
- The middleware therefore takes one code path, and writes the existing `sites.not_resolved`
  response body byte for byte.
- `ISiteDirectory.FindByHostNameAsync` keeps its name and signature. Its documented
  meaning narrows to "the enabled site holding this host name". The host middleware is
  its only caller in the codebase, and an architecture test pins that.

**Rationale**:
- The clarification made the two cases identical (Q1: A).
- Filtering in SQL rather than in C# means the two cases don't differ even in timing. The
  database does the same index probe and returns zero rows either way.
- Keeping the name avoids a breaking change to a module contract (Principle III). The
  narrowed meaning is what "resolve" always meant for the public surface. The
  architecture test stops a second caller from appearing without review.

**Alternatives considered**:
- *Load the site, then check `IsEnabled` in the middleware*: the response would be
  identical, but it adds a branch whose timing differs from the unknown case, and it would
  leave a contract method that hands disabled sites to public-surface code.
- *Rename the method to `ResolvePublicSiteAsync`*: clearer, but it is a breaking contract
  change, which Principle III allows only in a major version.

---

## S-04: One host-name normaliser, in `Shared.Kernel/MultiSite`

**Decision**: `HostNames.TryNormalise(string input, out string normalised)` becomes the
single definition of a host name, in `QuickBuilder.Shared.Kernel/MultiSite/HostNames.cs`.
It is used by the Sites domain, by request resolution, and by validation of admin host
names at startup (S-13).

Steps, in order:
1. Trim whitespace. An input with interior whitespace is refused.
2. Refuse anything containing `://`, `/`, `?`, `#`, `@` or `*`: a scheme, path, query,
   user info or wildcard (FR-010).
3. **IP literals**: an IPv4 address, or an IPv6 address with or without brackets, is
   accepted and stored in the canonical form `IPAddress.ToString()` produces (IPv6
   without brackets). Any other use of `:` is a port, and is refused.
4. **Everything else is a DNS name**. Remove one trailing dot, then convert to ASCII with
   `System.Globalization.IdnMapping.GetAscii` (UTS #46, which also lowercases). The
   result must match the existing label grammar: letters, digits and hyphens, labels of
   1–63 characters, 253 characters in total.

For display, `HostNames.ToDisplay(ascii)` returns `IdnMapping.GetUnicode(ascii)`. It falls
back to the ASCII form if decoding fails.

Resolution passes the request's `Host.Host` through the same function. A host that fails
normalisation resolves to nothing, and gets the unknown-site response.

**Rationale**:
- FR-009 requires the same rules for storage, comparison and resolution. Today they're
  spread across `SiteHostName.Normalise`, `IsValid` and the middleware.
- The kernel already holds `MultiSite/ISiteOwned`. A pure string function with no
  dependencies fits there, and both Host and Sites can reference it.
- `IdnMapping` is in the BCL, so no package is needed. It is locale-independent, and it
  rejects invalid Unicode.
- Storing ASCII keeps the unique `citext` index meaningful. `bücher.example` and
  `xn--bcher-kva.example` become the same row, which SC-004 tests.

**Alternatives considered**:
- *Store the Unicode form*: comparisons would then depend on Unicode normalisation.
  Browsers send the ASCII form in `Host` anyway.
- *Refuse IPv6*: the spec's edge case accepts "IP addresses" without qualification, and
  supporting both families costs one `IPAddress.TryParse` branch.

---

## S-05: Primary host name, and the aggregate's invariants under concurrency

**Decision**:
- **Primary flag**: `site_host_names` gains `is_primary boolean not null`. A partial unique
  index `ux_site_host_names_primary ON (site_id) WHERE is_primary` guarantees at most one
  primary per site in the database.
- **Aggregate invariants**: "exactly one primary" and "at least one host name" are enforced
  by the `Site` aggregate.
- **Concurrency token**: the `sites` row carries PostgreSQL's `xmin` system column, mapped
  as a `uint Version` with `IsRowVersion()` (Npgsql's supported mapping). Every change to
  the aggregate updates the `sites` row, at least `updated_at_utc`, so two concurrent
  changes to one site can't both commit. The second fails with
  `DbUpdateConcurrencyException`.
- **Operations with no client version**: adding a host name, removing one, and
  disable/enable carry no version from the client. On a concurrency failure they reload
  the site and retry, up to 3 attempts, then answer `409 sites.out_of_date`. The rules are
  re-checked on each retry, so a race can't remove the primary or the last host name.
- **Settings edits** carry the client's version (S-06).

**Rationale**: consider two administrators, one making `x.example` primary while the other
removes it. Without a lock on the aggregate, both would commit and leave a site with no
primary. `xmin` costs no column and no migration, and EF Core and Npgsql support it
directly. Retrying for host-name operations keeps them simple for callers: they're
idempotent-looking, single-purpose actions, and a lost race is rare.

**Alternatives considered**:
- *`sites.primary_host_name_id`, a foreign key to `site_host_names` with
  `ON DELETE RESTRICT`*: this is a circular foreign key. EF Core has to insert the site,
  insert its host names, then update the site, so the column must be nullable, and that
  is the state we are trying to forbid.
- *A dedicated `version integer` column*: it duplicates what `xmin` already provides.
- *`SERIALIZABLE` transactions*: heavier, and they fail in more places than the one
  invariant we care about.

---

## S-06: Settings are replaced as a whole, against a version

**Decision**:
- `PUT /api/v1/sites/{siteId}/settings` takes the complete editable set: `name`,
  `scheme`, `defaultLanguage`, `primaryHostName` and `version`.
- If `version` no longer matches, the answer is `409` with `errorCode: sites.out_of_date`,
  and nothing changes (US1 scenario 5, FR-005).
- `version` is the `xmin` value from S-05, exposed as an opaque integer on every site
  response.

**Rationale**: replacing the whole set with PUT avoids PATCH's "absent versus null"
ambiguity, and the settings form always sends every field. Returning the version in the
body rather than in `ETag`/`If-Match` matches how the admin client already handles errors
(`errorCode` in problem details), and adds no header plumbing.

**Alternatives considered**:
- *`PATCH` with optional fields*: more states to validate and to test.
- *`If-Match` with `412`*: standard, but it would be the only conditional request in the
  API. The body version is equally safe.

---

## S-07: The base URL is derived, never stored

**Decision**: `baseUrl = {scheme}://{primaryHostName}{Hosting:BasePath}`. It uses the ASCII
host and no port, and has no trailing slash, except that it is `/` when the base path is
empty. It is computed in the Sites module every time a `SiteDto` is built. The module
reads `Hosting:BasePath` from configuration, as Identity already does for its links. It
is never persisted.

**Rationale**:
- FR-002: a derived value can't disagree with the host names or the base path.
- Changing the base path in configuration immediately changes every site's base URL, with
  nothing to migrate.

**Port**: host names never carry a port (001 D-12), so the base URL assumes the scheme's
standard port. A deployment reached on a non-standard public port, which is typical only
of local testing, gets a base URL without that port. Nothing uses the base URL to serve
requests in this feature. It is documented in `docs/operations.md`, and listed under
Risks.

---

## S-08: Scheme

**Decision**:
- `scheme` is a text column, constrained to `'https'` or `'http'`, with default
  `'https'`.
- The API refuses any other value with `sites.invalid_scheme`.
- The admin shell shows a warning badge on an HTTP site, in the site list and on its
  settings page (clarification Q3: A).
- The server does nothing else differently for an HTTP site. The scheme only shapes the
  base URL. The visitor's actual scheme is still decided by the proxy and forwarded
  headers.

---

## S-09: The supported language list

**Decision**: the list is a fixed, ordered set of 40 BCP 47 tags, held in
`QuickBuilder.Sites/Domain/SupportedLanguages.cs`:

`ar, bg, cs, da, de, el, en, es, et, fa, fi, fr, he, hi, hr, hu, id, it, ja, ko, lt, lv,
ms, nb, nl, pl, pt, pt-BR, ro, ru, sk, sl, sr, sv, th, tr, uk, vi, zh-Hans, zh-Hant`

- Tags are compared case-insensitively and stored in the canonical case shown.
- `GET /api/v1/sites/languages` returns the tags, requiring only `RequireSignedIn`.
- The admin shell displays each tag's name in the shell's own language with the browser's
  `Intl.DisplayNames`, so no name tables are shipped.
- The default is `en` (clarification Q2: A).

**Rationale**:
- A single server-side list is the one the API validates against.
- `Intl.DisplayNames` is available in every browser the admin shell supports, and it
  keeps the 40 names out of `en.json` and out of future translations.
- The list covers the scripts and regions most commonly requested for web content. Adding
  a tag later is a code change with no data migration.

**Alternatives considered**:
- *Accept any well-formed tag* (clarification option C): rejected in clarification.
- *Ship display names from the server*: this duplicates CLDR data the browser already
  has.

---

## S-10: Disable and enable

**Decision**:
- **Storage**: `sites.status` is a text column, constrained to `'enabled'` or
  `'disabled'`, with default `'enabled'`.
- **Endpoints**: `POST /api/v1/sites/{siteId}/disable` and `/enable`. Each is idempotent:
  disabling a disabled site answers `200` with the unchanged site and publishes no event.
- **Unchanged by disabling**: host names, grants and data. Every administrative operation
  keeps working on a disabled site (FR-019).
- **Public surface**: resolution stops returning the site (S-03).

**Rationale**: a status column rather than `is_enabled` leaves room for the "deleted" or
"in trash" states that #21 will add, without renaming anything. Separate actions rather
than a settings field keep the audit event, the capability and the confirmation step
distinct from routine edits.

---

## S-11: Lifecycle and audit events flow through the outbox; Identity records them

**Decision**: Sites publishes four new integration events, each in the same transaction
as its change (001 FR-015):
- `Sites.HostNameAdded`
- `Sites.HostNameRemoved`
- `Sites.SiteDisabled`
- `Sites.SiteEnabled`

Each carries the site id, the host name where relevant, the acting user's id (from
`IAccessEvaluator.UserId`) and the time.

Identity subscribes to all four and appends security events of four new types:
`site_host_name_added`, `site_host_name_removed`, `site_disabled` and `site_enabled`.
Each records the actor, the site and, where relevant, the host name. Idempotency uses each
module's existing `processed_events` table.

`Sites.SiteDisabled` and `Sites.SiteEnabled` are also the lifecycle notices FR-021 asks
for. No other module needs to react in this feature, because the middleware already
stops public traffic.

**Rationale**:
- The security event record belongs to Identity, and Principle I forbids Sites from
  writing Identity's tables, or from spanning both in one transaction. The outbox is the
  constitution's mechanism for exactly this.
- The record appears a few seconds after the change, within the dispatcher's poll
  interval. That is acceptable for an audit trail, and it never loses an entry: an entry
  is only ever late.
- Security events already store structured details, and the host name is data an auditor
  needs. It goes in the security event, never in a log line (FR-032).

**Alternatives considered**:
- *A synchronous `ISecurityEventRecorder` contract exposed by Identity*: that is a
  cross-module write inside Sites' transaction, which is forbidden.
- *Record renames and language changes too*: they don't change what the public can
  reach, which is the spec's criterion in FR-028.

---

## S-12: Capabilities

**Decision**: two new site-scoped capabilities, which no role holds other than
Administrator (who holds everything):

| Capability | Covers |
|---|---|
| `sites.settings.manage` | `PUT …/settings` |
| `sites.availability.manage` | `POST …/disable`, `POST …/enable` |

Adding and removing host names stay under the existing `sites.host_names.manage`. Viewing,
including the new fields, stays under `sites.view`, which Designers and Editors hold on
their sites (FR-023).

**Rationale**: capability names are permanent (002 `authorization.md` §2). A future
per-site administrator role may well edit settings without being allowed to take a site
offline. Keeping them apart now costs one declaration.

---

## S-13: Admin host names

**Decision**:
- **Setting**: a new key, `Hosting:AdminHostNames:<n>`, empty by default. Each entry is
  normalised with S-04 at startup, and an invalid entry fails startup naming the key.
- **Routing**: in `SiteResolutionMiddleware`, when the list is non-empty and the request
  targets `/api` or `/admin` on a host that isn't in the list, the request is treated as
  public:
  - If the host resolves to no enabled site, it gets the unknown-site response.
  - If it resolves to a site, it gets that site's **public not-found** response. This is
    the same body that any unmatched public path on that site returns. A new shared
    `PublicNotFound` writer serves both cases, and the public surface's unmatched-path
    fallback uses it too, so they can't drift apart.
  - The endpoint that routing had matched never runs.
- **`/health` is exempt**: it answers on every host. Container orchestrators and the
  compose health check call it on the container's own address, and it reveals nothing
  about the admin surface.
- **Collisions with site host names**:
  - Adding a site host name that equals an admin host name is refused as
    `sites.host_name_taken`. The Sites module binds the same key, as Identity already
    reads `Hosting:BasePath`.
  - After migrations, startup checks whether any configured admin host name is held by a
    site. If one is, startup fails with a message naming `Hosting:AdminHostNames` and the
    host name. The host name is operator configuration, not visitor input, so naming it
    here is permitted.

**Rationale**:
- Clarification Q1: B.
- Doing this in the existing middleware keeps one place that decides the surface of a
  request (001 FR-011).
- A matched endpoint that is short-circuited before authorization and execution can't be
  reached, and the check sits before `UseAuthentication`, so no cookie is even read.

**Alternatives considered**:
- *Kestrel host filtering or `RequireHost` on controllers*: that would scatter the rule
  across every controller, and it responds with a different 404 than the public one,
  which would reveal the admin surface.
- *Restrict `/health` too*: it would break the reference compose health check and every
  orchestrator probe that uses the container IP.

---

## S-14: Forwarded headers and base path: kept, with multi-hop fixed and everything tested

**Decision**: D-12's configuration stays: forwarded host, scheme and client address,
trusted only from configured proxies or networks, and the base path applied with
`UsePathBase`. Two changes:
- **`ForwardLimit = null`**: when the operator trusts more than one hop (for example a CDN
  in front of a load balancer), every trusted hop is unwound. The middleware still stops
  at the first address that isn't trusted, so an untrusted hop can never supply the host.
  The default limit of 1 silently ignored the outer trusted hop.
- **An end-to-end proxy test matrix** (S-19), which didn't exist, covers:
  - trusted and untrusted sources;
  - one and two hops;
  - `X-Forwarded-Host` with a port and with mixed case;
  - the base path on the public and admin surfaces;
  - the base path included in the base URL and in media addresses.

**Rationale**: FR-016 and FR-017 were delivered by 001 but only partly tested (only
`RequestGuardTests` and `PublicServingTests` touch them). Site resolution is a critical
path under Principle X, so its proxy behaviour needs its own tests.

**Rejected**: honouring `X-Forwarded-Prefix` as a per-request base path. The base path
stays configuration, because a header that changes generated links is one more thing a
proxy can get wrong or an attacker can spoof.

---

## S-15: The shared isolation suite

**Decision**: a new folder, `tests/QuickBuilder.IntegrationTests/Isolation/`, holds:

- **The registration interface.** `ISiteIsolationProbe` is what a module implements once,
  in the test project, to register with the suite. It has:
  - `Module`, the module's name as `IModuleDatabaseProvider` describes it;
  - `SeedAsync(client, siteId)`, which creates at least one of each site-owned record type
    through the module's own API, and returns their ids and public paths;
  - `AdminRequests(siteId, recordIds)`, every administrative request that takes a record
    id: read, list, change, delete;
  - `PublicRequests(recordIds)`, every public request that takes a record id.
- **The suite.** `SiteIsolationSuite` is one test class, parameterised over every probe.
  - It creates sites A and B, and seeds both.
  - It sends every admin request for site A carrying B's record ids, and asserts that the
    response is `404` and that B's records are unchanged afterwards (FR-025, US5
    scenario 2).
  - It sends every public request for B's records on A's host, and asserts the same `404`
    as for an id that was never issued (US5 scenario 1).
  - It asserts that every list on A contains none of B's ids, and that no count or total
    on A includes them.
  - It disables B, and asserts that every public request for B's records on B's host
    answers like an unknown host (FR-026).
- **Coverage guards**, which make a forgotten registration fail the build (FR-025, US5
  scenarios 3 and 4):
  - `SiteFilterCoverageTests` is generalised. It enumerates every `IModuleDatabaseProvider`
    and asserts a query filter on every `ISiteOwned` entity in every module's model, not
    only the Sites model.
  - `IsolationCoverageTests`, a new test, fails when a module has an `ISiteOwned` entity
    but no probe.
  - It also fails when an endpoint in `EndpointCatalog` has both a `{siteId}` route value
    and a record-id route value (any value ending in `Id` other than `siteId`, or a named key
    such as `hostName`), but no probe's `AdminRequests` exercises it.
- **Probes shipped in this feature**: `SitesIsolationProbe` (host names) and
  `MediaIsolationProbe` (media files). `MediaSiteIsolationTests` stays as Media's detailed
  test. The probe reuses its helpers.
- **Documentation**: `docs/contributing.md` gains "Adding a module with site-owned data",
  which covers implementing a probe.

**Rationale**:
- The build-level guards turn "remember to test isolation" into "the build tells you". A
  module can't ship site-owned data without registering.
- Probes use the module's HTTP API, not its `DbContext`, so the suite tests what an
  attacker can reach, including the not-found rules.
- `EndpointCatalog` already enumerates every endpoint for 002's authorization coverage
  test, so the endpoint guard reuses it.

**Alternatives considered**:
- *A reflection-driven suite that calls every handler with forged ids*: it can't produce
  valid requests for arbitrary endpoints, and it would test handlers rather than the
  HTTP surface.
- *An abstract base class that each module's test inherits*: nothing would then fail
  when a module forgets to inherit it.

---

## S-16: Migration and upgrade

**Decision**: one migration in the `sites` schema, `SiteSettingsAndLifecycle`:
- **New `sites` columns**:
  - `scheme text not null default 'https'`, with a check constraint;
  - `default_language text not null default 'en'`;
  - `status text not null default 'enabled'`, with a check constraint.
- **New `site_host_names` column**: `is_primary boolean not null default false`.
- **Data step**: for each site, set `is_primary = true` on its earliest host name, ordered
  by `created_at_utc` then `id`. This runs as a single `UPDATE … FROM (SELECT DISTINCT ON
  (site_id) …)`, written in the migration with `migrationBuilder.Sql`. That is permitted,
  because schema changes and their data steps live in EF migrations.
- **Index**: create `ux_site_host_names_primary` after the data step.

The migration is additive, re-runnable through EF's history, and forward-only.

`UpgradeFrom003Tests` builds a 003 snapshot by applying the Sites, Identity and Media
migrations as shipped in 003. It seeds two sites with several host names, grants and
media, then starts the current application. It asserts that:
- every site is enabled, uses `https` and `en`, and has its earliest host name as primary;
- every host name still resolves;
- every media address still serves;
- every grant still applies (SC-008, FR-029).

---

## S-17: The HTTP contract changes additively

**Decision**: `/api/v1/sites` responses keep every field they had. `hostNames` stays an
array of ASCII strings. New fields are added:
- `primaryHostName`
- `hostNameDetails` (array of `{ hostName, displayName, isPrimary }`)
- `scheme`
- `baseUrl`
- `defaultLanguage`
- `status`
- `version`
- `updatedAtUtc`

`POST /api/v1/sites` accepts three new optional fields: `primaryHostName`, `scheme` and
`defaultLanguage`. New endpoints are listed in
[contracts/sites-api.yaml](./contracts/sites-api.yaml).

**Rationale**: 001 FR-010a reserves breaking changes for a new version segment. Every change
here is additive, so `v1` stays. The C# `SiteDto` contract gains properties, and its only
constructor call sites are inside Sites.

---

## S-18: Admin shell

**Decision**:
- **`SitesView`**:
  - Each row shows its status and an HTTP warning badge where relevant, and links to a new
    `SiteDetailView` at `/admin/sites/:siteId`.
  - The create dialog gains primary host name, scheme and language fields.
- **`SiteDetailView`** has three sections:
  1. **Settings**: a form with name, scheme (with the HTTP warning), language and primary
     host name, and version handling. On `sites.out_of_date` it offers to reload.
  2. **Host names**: add, remove and "make primary", which saves the settings with the new
     primary. Remove is disabled with an explanation for the primary or only host name.
     The server enforces this either way.
  3. **Availability**: disable or enable, with a confirmation dialog stating the effect on
     visitors.
- Designers and Editors see the same page read-only. Controls are hidden or disabled from the
  response's `permissions` object, following Media's `canDelete` pattern, and the server enforces every action.
- The Media link on a disabled site stays usable.
- Every string goes through the translation lookup (`en.json`). Vitest and axe cover
  keyboard operation and accessibility (SC-009).
- A new `api/sites.ts` holds the typed client functions.

---

## S-19: Tests that prove the success criteria

| SC / FR | Test (new unless noted) |
|---|---|
| SC-001 | Quickstart §1 (manual, timed), plus smoke test create → resolve |
| SC-002, FR-013/014 | `SiteResolutionTests`: A/B/unknown/disabled × case, trailing dot, port, IDN; disabled response byte-identical to unknown |
| SC-003, FR-024–026 | `SiteIsolationSuite` + `IsolationCoverageTests` + generalised `SiteFilterCoverageTests` (S-15) |
| SC-004, FR-008–011 | `HostNamesTests` (unit, ≥40 inputs: every equivalent spelling, every invalid form) + `HostNameManagementTests` (integration: add/remove/primary/last, concurrent adds of one name to two sites) |
| SC-005, FR-012 | `ResolutionAcrossInstancesTests`: two `QuickBuilderApp` instances over one database; remove, disable and enable on one instance; the very next request on the other reflects it |
| SC-006, FR-016 | `ProxyResolutionTests`: the S-14 matrix |
| FR-017 | `BasePathTests`: public, admin and media under `/cms`; `baseUrl` includes it |
| FR-018a | `AdminHostNameTests`: admin paths on site hosts give the site's public not-found (byte-identical to an unmatched page), health is exempt, add is refused, startup conflict fails |
| FR-005, US1 s5 | `SiteSettingsTests`: edit, validation reasons, stale version → `409 sites.out_of_date`; concurrent primary change against remove leaves exactly one primary |
| FR-019–021 | `SiteAvailabilityTests`: disable/enable idempotent, media on a disabled site answers not found publicly but works in the admin surface, host name stays taken, events published |
| FR-022/023 | Extended `SiteVisibilityTests` + existing `EndpointAuthorizationCoverageTests` (every new endpoint declares a capability) |
| FR-028 | `SiteSecurityEventTests`: each of the four events is recorded once, even when redelivered |
| FR-029, SC-008 | `UpgradeFrom003Tests` (S-16) |
| FR-031 | `ReferenceDeploymentSmokeTests` (extended) |
| FR-032 | `LoggingTests` (extended): no host name in any log line across the suite |
| SC-007 | `SiteResolutionPerformanceTests` in `QuickBuilder.SmokeTests` (S-20) |
| SC-009 | `siteDetailView.spec.ts`, `sitesView.spec.ts` (extended) |

Site resolution is already on the coverage gate's critical-path list (001). The new
`HostNames` normaliser and the `Site` aggregate join it.

---

## S-20: Performance

**Decision**:
- The resolution query is an EF Core compiled query (`EF.CompileAsyncQuery`), because it
  runs on every public request, which is the case the constitution names for compiled
  queries.
- The `SiteDto` projection loads host names in the same round trip.
- `SiteResolutionPerformanceTests` seeds 100 sites with 500 host names on the reference
  compose setup. It then measures p95 for `GET /` on a site host (which resolves) and for
  `GET /health/live` (which doesn't), 2,000 requests each at concurrency 20. It asserts
  that the difference is ≤ 5 ms (SC-007).

---

## New dependencies

None. `IdnMapping`, `IPAddress`, `Intl.DisplayNames` and Npgsql's `xmin` mapping are
already available.

## Configuration keys introduced

| Key | Default | Meaning |
|---|---|---|
| `Hosting:AdminHostNames:<n>` | *(empty)* | Host names on which the administrative surface (`/api`, `/admin`) answers. When empty, it answers on every host name. When set, other host names serve only the public surface. `/health` answers everywhere. An entry held by a site stops startup. |

No key is removed or renamed.
