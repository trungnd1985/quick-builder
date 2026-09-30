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
