# Quickstart Validation Results

**Feature**: `009-sites-hostname-resolution` | **Run**: 2026-09-26 | **Task**: T077

Each section of [quickstart.md](./quickstart.md) was checked through the automated tests
it names, and §1 and §4 also against the reference deployment (`docker compose`, image
built from this branch) by the release smoke test.

**Environment**: Windows 11, Docker Desktop 28.5.1 (WSL 2), .NET 10.0.401, Node 24.19. This
is a developer workstation. Only §10's performance part ran under the reference limits
(2 CPUs, 4 GB, via `docker-compose.perf.yml`).

## Results

| § | Scenario | Criteria | Result |
|---|---|---|---|
| 1 | Create a site and reach it | SC-001, US1, US3 | ✅ `SiteSettingsTests` (defaults, every setting in any spelling); `SiteResolutionTests` (case, trailing dot, port, IDN; fixed unknown-host body); smoke test on the built image: two sites, each host reaching its own site. The 2-minute manual timing was not taken. |
| 2 | Edit settings, including a stale edit | US1 | ✅ `SiteSettingsTests`: every setting changes, base URL follows primary and scheme, stale version gives `409 sites.out_of_date` with nothing changed, 10 rounds of a primary change raced against removing that name always leave a valid primary; admin screen tests cover the reload prompt and the HTTP warning |
| 3 | Manage host names | SC-004, US2 | ✅ `HostNamesTests` (52 cases, over the 40 required); `HostNameManagementTests`: every equivalent spelling refused as taken without naming the holder, disabled holders and admin host names count as taken, primary and last name protected, 10 rounds of concurrent adds of one name to two sites give exactly one winner |
| 4 | Disable and re-enable | SC-002, US4 | ✅ `SiteAvailabilityTests`: pages and media of a disabled site byte-identical to an unknown host, admin surface still works, name stays taken, idempotent with one event each, re-enabling restores everything; `SiteSecurityEventTests`: each change recorded once, redelivery adds nothing; smoke test disables and re-enables on the real image |
| 5 | Changes reach every instance at once | SC-005 | ✅ `ResolutionAcrossInstancesTests`: two instances over one database; add, remove, new primary, disable and enable are all seen by the other instance at its very next request. Not repeated with two containers by hand. |
| 6 | Behind a reverse proxy, with a base path | SC-006, US3 | ✅ `ProxyResolutionTests`: trusted host, scheme and address used; untrusted ignored; two trusted hops use the outer host; an untrusted outer hop cannot choose the site. `BasePathTests`: public, admin and media under `/cms`, base URL includes it. Not repeated with a real proxy by hand. |
| 7 | Keep the admin area off client domains | FR-018a | ✅ `AdminHostNameTests`: on a client domain, `/admin`, its assets and `/api` answer byte-identically to a missing page with no admin headers, even for a signed-in administrator; state-changing calls never run; `/health` answers everywhere; invalid or conflicting settings stop startup naming the key |
| 8 | Isolation holds for every module | SC-003, US5 | ✅ `SiteIsolationSuite` for Sites and Media, including the disabled-site step; `IsolationCoverageTests` passes and both guards are proven to fire |
| 9 | Upgrade from 003 | SC-008, FR-029 | ✅ `UpgradeFrom003Tests`: every site enabled, HTTPS, English, earliest host name primary; every host name and media address still works; grants still scope |
| 10 | Performance and accessibility | SC-007, SC-009 | ⚠️ **SC-007 not met under load — see below.** SC-009 ✅: admin screen tests 57/57, including axe baselines and keyboard checks for the list and detail screens; admin lint and type-check clean. The keyboard-only walkthrough by hand was not done. |

**9 of 10 fully pass on their automated checks; §10 is open.** Full suite: 310 unit,
23 architecture, 321 integration (3 Unix-only cases skipped on Windows; they run in CI),
57 admin; smoke test passes on the built image. Merged line coverage 92.1%;
`QuickBuilder.Sites` 95.0%, `QuickBuilder.Shared.Kernel` 84.8%, `QuickBuilder.Host` 90.4%;
`HostNames`, `SiteResolutionMiddleware` and `AdminHostConfinementMiddleware` 100%.

## SC-007: resolution under load

`SiteResolutionPerformanceTests`, 100 sites and 500 host names, reference limits:

| Measurement | p95 |
|---|---|
| `GET /` on a site host, one request at a time | 2.3 ms |
| `/health/live`, one request at a time | 0.9 ms |
| **Added by resolution, one at a time** | **1.4 ms** (within the 5 ms budget) |
| `GET /` on a site host, 20 at once | 15.4–16.5 ms |
| `/health/live`, 20 at once | 6.0–6.2 ms |
| An unknown host (resolution, no controller), 20 at once | 11.3–12.8 ms |
| **Added by resolution, 20 at once** | **9.3–10.5 ms** (over the 5 ms budget) |

The query is not the cost: turning it around to start from the unique host-name index
changed nothing, and a miss costs as much as a hit. The cost is the per-request database
context and round trip, which queueing on two CPUs amplifies. Meeting the budget under this
load needs a resolution cache whose invalidation reaches every instance — the mechanism
research S-01 deferred to the shared-cache work (exception E-4). The trade-off is recorded
for a decision rather than settled here.

## Deviations found by this run

- **The primary host name is a column on the site, not a flag on each host name.** A
  partial unique index is checked row by row, so moving the flag between two rows could
  fail depending on update order (research S-05, revised).
- **Invariant globalization has no Unicode normalisation.** The normaliser lowercases
  before converting to ASCII; the admin shell normalises typed host names to NFC (S-04).
- **Resolution is a plain LINQ query, not a compiled one**, because the cross-site helper
  cannot appear inside a compiled query (S-20).
- **Unmatched public paths are answered by the resolution middleware**, not a fallback
  endpoint, which would have been a minimal API.
