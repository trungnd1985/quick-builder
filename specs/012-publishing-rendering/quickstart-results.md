# Quickstart results: Publishing and rendering (#12)

## Public texts awaiting native review

The not-found page's three strings (`notFound.title`, `notFound.message`, `notFound.home`)
exist for all 40 supported languages in
`src/Modules/Rendering/QuickBuilder.Rendering/Resources/public-texts/` (FR-030, research
PR-13). English is the source. The other 39 were drafted without a native speaker. Each one
needs a native review before it is called final:

`ar`, `bg`, `cs`, `da`, `de`, `el`, `es`, `et`, `fa`, `fi`, `fr`, `he`, `hi`, `hr`, `hu`,
`id`, `it`, `ja`, `ko`, `lt`, `lv`, `ms`, `nb`, `nl`, `pl`, `pt`, `pt-BR`, `ro`, `ru`, `sk`,
`sl`, `sr`, `sv`, `th`, `tr`, `uk`, `vi`, `zh-Hans`, `zh-Hant`.

A reviewer changes only the three values, keeps the keys, and runs
`PublicTextCatalogueTests`.

## Automated verification (T107), 2026-09-30

| Check | Result |
|---|---|
| `dotnet format --verify-no-changes` | clean (four files had their usings reordered first) |
| `dotnet build` (Release) | 0 warnings, 0 errors |
| Unit tests | 820 passed |
| Architecture tests | 27 passed |
| Integration tests | 575 passed, 3 skipped (the skips predate 012) |
| Admin: `npm run lint`, `type-check`, `test`, `build` | clean; 423 tests passed |
| Line coverage, merged | 94.52% |
| `QuickBuilder.Rendering` | 87.15% (gate 80%) |
| `QuickBuilder.Pages` | 97.25% |
| `QuickBuilder.Editor` | 95.22% |
| `ReferenceDeploymentSmokeTests` on the built image | passed, including 9e: a component and a page published and viewed from the site's host name, the stylesheet served, and the page gone (`404` HTML) once unpublished |
| `RenderPerformanceTests` on `docker-compose.perf.yml` (2 CPUs, 4 GB) | 200-block page, 42,120 bytes: miss p50 12.0 ms, **p95 16.1 ms** (budget 200 ms); hit p50 4.1 ms, **p95 5.8 ms** (budget 20 ms); 0 bytes of `<script>` |
| Component publish with 2,000 dependent pages (`CacheInvalidationTests`) | under 500 ms |

## The manual quickstart run (T106): still to do

Nobody has run quickstart §1–§11 by hand in a browser yet. What the automated suites already
cover of each section:

| Section | Automated evidence |
|---|---|
| §1 Publish and view | `PublishApiTests`, `PublicRenderingTests`, smoke step 9e |
| §2 Components and design | `ComponentPublishApiTests`, `DesignPublishApiTests`, `DesignPublish.spec.ts` |
| §3 History and rollback | `RollbackApiTests`, `PublishLogApiTests`, `HistoryView.spec.ts`, `PublishLogView.spec.ts` |
| §4 Unpublish and delete | `UnpublishApiTests`, `addressWarning.spec.ts` |
| §5 Schedules | `ScheduleApiTests`, `ScheduledPublishExactlyOnceTests`, `ScheduleDialog.spec.ts` |
| §6 Two instances | `RenderingAcrossInstancesTests` (two hosts, one database) |
| §7 Not-found page | `NotFoundParityTests`, `PublicTextCatalogueTests` |
| §8 Performance | `RenderPerformanceTests` (above) |
| §9 Keyboard and translation | axe and keyboard checks in the admin specs; the catalogue test |
| §10 Upgrade | `UpgradeFrom011Tests` |
| §11 Smoke test | `ReferenceDeploymentSmokeTests` (above) |

Still to record by hand:
- the SC-001 time, measured by a person;
- the browser versions;
- §6 with two instances of the reference compose setup;
- a look at the published pages, the history and the log screens in a real browser.

## Performance budgets in CI (decided on PR #31, 2026-09-30)

The first CI runs of the smoke job put every performance test on a shared GitHub runner.
The reference journey and 012's render budgets passed. Three budgets from earlier features
were missed on the runner, though they pass on the reference setup:

| Budget (feature) | Target | GitHub runner |
|---|---|---|
| Sign-in p95 under a burst (002) | 1,000 ms | 2,346 ms and 2,863 ms |
| Media first byte p95 (003) | 100 ms | 503 ms (passed on the other run) |
| Site resolution one at a time (009) | +5 ms | +6.9 ms (local: +4.5 ms) |

Nothing in 012 touches sign-in or uploads; the runner is slower and noisier than the
reference environment the budgets are set for. So CI now has two jobs:
- **`smoke`** (required): the reference journey. A failure fails the build.
- **`performance`** (report only): every performance test. The numbers go to the run
  summary, and a missed budget is a warning, not a failure.

The budgets themselves are asserted on the reference environment in the quickstart run.
This refines 012 analysis I1, which had a missed budget failing the build.
