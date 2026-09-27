# Quickstart Validation Results

**Feature**: `010-page-tree-components` | **Run**: 2026-09-27 | **Task**: T097

Each section of [quickstart.md](./quickstart.md) was checked through the automated tests it
names. §7 was also checked against the reference deployment (`docker compose`, with the
image built from this branch) by the release smoke test and the page tree performance
test.

**Environment**: Windows 11, Docker Desktop 28.5.1 (WSL 2), .NET 10.0.401, Node 24.19. This
is a developer workstation. Only the performance test ran under the reference limits
(2 CPUs, 4 GB, via `docker-compose.perf.yml`).

## Results

| § | Scenario | Criteria | Result |
|---|---|---|---|
| 1 | Build a structure | SC-001, US1 | ✅ `PageTreeApiTests` (12): a nine-page, three-level tree listed depth-first with computed addresses, depths and child counts; placement last, first and after an anchor; the same slug under two parents; duplicate, reserved (`api`, `admin`, `health`) and home-child clashes refused with nothing saved; invalid title and slug reported together. `PageTreeCreateTests` (unit) cover the same rules, plus the 10-level and 2,000-page limits. `slugify.spec.ts`: `Dịch vụ & Giá` → `dich-vu-gia`. ⚠️ The 5-minute manual timing was **not taken**. |
| 2 | Rename, reorder and move | SC-002, SC-004, US2 | ✅ `PageStructureApiTests` (7): a reslug recomputes four addresses in one event; a title change keeps the address; subtree move; reorder; refused changes leave the tree byte-identical; stale versions and departed anchors are out of date; make-home swaps both subtrees. `PageTreeConcurrencyTests` (5): 10 parallel creates of one address give one page; crossed moves (5 rounds) never make a cycle; a reslug raced against a create leaves one page at the address; renaming one page is never refused because another was reordered. `PageTreeInvariantTests`: 200 seeded sequences of 50 random operations keep every invariant. |
| 3 | Drafts and the page schema contract | SC-003, SC-007, US3 | ✅ `PageDocumentValidatorTests` and `DraftApiTests`: both valid golden examples round-trip equal as JSON; each of the 11 invalid examples is refused with its rule and pointer, and the draft is kept; stale version gives 409; a body over the cap and a document of 1 MiB + 1 byte give 413; an unknown component gives 400; a save writes no event and changes no public response. `PageSchemaContractTests` (7) keep the document's node types, limits, reserved segments and examples in step with the schema and code, and prove the embedded schema is the file in `docs/contracts/`. `PageSchemaSubsetTests` and `SchemaSubsetValidatorTests` pin the keyword subset. |
| 4 | Shared components | US4 | ✅ `ComponentApiTests` (6): unique names ignoring case, per site; usage follows drafts and blocks deletion with `usageCount`; another site's component looks exactly like an unknown one; renames keep references; component drafts may not contain components. `ComponentUsageRaceTests`: 50 rounds of delete raced against a referencing save — never both succeed, never a dangling usage. |
| 5 | Delete and roles | US5, Q2, Q3 | ✅ `DeletePageTests` (4): leaf deleted with its draft and usages and its address reusable; parent refused with `childCount`; home page refused while others exist. `PagesAuthorizationTests`: every endpoint for Administrator, Designer and Editor (Editor: views and page drafts only, 403 elsewhere), and 404 `sites.not_found` on all 17 endpoints for a user with no role on the site. |
| 6 | Isolation | SC-006 | ✅ `SiteIsolationSuite` passes for Pages: 13 record-addressed endpoints sent the other site's ids, with valid bodies, answer 404, and the listings leak nothing. `IsolationCoverageTests` passes. `PagesSiteScopingTests`: `ForSite` returns only the named site's rows for all six entities, and it is the module's only cross-site read. |
| 7 | Upgrade, smoke and performance | FR-035, FR-036, SC-005, SC-008 | ✅ `UpgradeFrom009Tests`: a 009 snapshot upgrades, sites, grants and media still work, and pages can be created. The smoke test on the built image creates a page and a child, saves a draft and reads it back. `PageTreePerformanceTests` on the reference limits, with 2,000 pages: **list p95 14 ms; move a 201-page subtree p95 72 ms; rename p95 56 ms** (budget 2 s each). `pageTreeView.spec.ts` renders 2,000 rows. Admin screen tests (35 new) include axe baselines and keyboard-only move, rename and delete. ⚠️ The in-browser 2 s timing of the Pages screen and a keyboard walkthrough by hand were **not done**. |

**All 7 sections pass on their automated checks.** The full suite passed: 621 unit,
24 architecture, 393 integration (3 Unix-only cases skipped on Windows; they run in CI),
92 admin, and smoke plus page tree performance on the built image. Merged line coverage is
93.0%, and `QuickBuilder.Pages` is at 95.9% (gate 80%).

## Not done by hand

These need a person with a browser. They are the only open part of T097:

- **SC-001**: time a designer building a 10-page, three-level structure (target: under
  5 minutes).
- **SC-005, on screen**: time the Pages screen showing 2,000 pages in a browser (target:
  under 2 s). The API p95 is 14 ms, and jsdom renders the 2,000 rows, so this is expected
  to pass comfortably.
- **SC-008**: a keyboard-only walkthrough of both screens, by hand.
