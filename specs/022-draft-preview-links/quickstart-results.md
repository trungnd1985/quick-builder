# Quickstart results: Draft preview links (022)

**Status**: automated part done 2026-11-21. Checks by hand (§2 timing, §8) and the performance
run (§7) are still to do, so task T088 stays open.

This records which parts of [quickstart.md](./quickstart.md) the automated suites prove, and
what a person still has to check.

## Automated evidence

| Quickstart section | Proven by | Result |
|---|---|---|
| §1 Member preview matches the published page (SC-001) | `ITESTS/Preview/MemberPreviewTests.cs`, `PreviewParityTests.cs` (golden comparison, page and item page) | pass |
| §2 Share a link (SC-002 behaviour, SC-005) | `PreviewLinkTests.cs`, `PreviewLinkTamperingTests.cs` (1,000+ variants, all byte-identical), `PreviewLinkAddressTests.cs`, `PreviewLinkThrottleTests.cs` | pass |
| §3 Search engines, caches, forms (SC-004) | `PreviewResponseRulesTests.cs`, `FormSubmitIsolationTests.cs`, `PreviewSurfaceTests.cs` (logs), `ITESTS/Seo/RobotsTests.cs`, `ATESTS/PreviewBoundaryTests.cs` | pass |
| §4 Revoke and who has access (SC-006) | `PreviewLinkRevocationTests.cs` (incl. second instance and creator access), `PreviewLinkHistoryTests.cs`, `PreviewLinkSweepTests.cs`, `PreviewLinksIsolationProbe` | pass |
| §5 Past versions (SC-007) | `VersionPreviewTests.cs` | pass |
| §6 Edge cases | `PreviewEdgeCaseTests.cs`, `PreviewLinkTests.cs` (trash, invalid draft, limit, emergency switch), `ITESTS/Transfer/PreviewLinksImportTests.cs`, `PreviewUpgradeTests.cs` | pass |
| §8 Accessibility, automated part | axe in `ADMINTESTS/features/preview/SharePreviewDialog.spec.ts`, `PreviewLinksView.spec.ts` | pass |

## Full runs (2026-11-21)

| Suite | Result |
|---|---|
| Unit | 2,651 passed |
| Architecture | 66 passed |
| Integration | 1,578 passed, 5 skipped, 1 failed: `VariantProductionExactlyOnceTests` (Media, two-instance timing), which passes when run alone (2/2) and is not touched by this feature |
| Admin (vitest), lint, type-check | 1,501 passed; clean |
| `dotnet format --verify-no-changes` | clean |

**Coverage (Principle X, T086)**: `PreviewLinkResolver` 100%, `PreviewMediaSet` 100%,
`PreviewComposition` 100% (unit), `LinkPreviewController` 94%, `PreviewLinkFailures` 94%,
`PreviewItemReader` 92%, `PreviewResponder` 91%, `CreatePreviewLinkHandler` 89%,
`MemberPreviewController` 89%, `PreviewContentSource` 85%, `PreviewContentReader` 84%,
`PageComposer` 81% (with the rendering suites).

## Still to do by hand

- **§7 Performance (SC-003)**: `tests/QuickBuilder.SmokeTests/PreviewBudgetTests.cs` is written
  but did not run on this machine: the smoke harness's `docker compose build` stops at a Docker
  Bake privilege prompt (`--allow=fs.read=Dockerfile`), and its named-image mode pulls from a
  registry. Run it where the other smoke performance tests run.
- **§2 timing (SC-002)**: time going from the editor to a copied link.
- **§8**: keyboard-only and screen-reader passes over Share preview, the site's Preview links
  view and the bar's "Hide bar"; and the three preview widths with the bar hidden against the
  published page (FR-021).
