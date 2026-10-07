# Quickstart Results: CMS Collections and Schema Builder

**Feature**: `014-cms-collections-schema` | **Date**: 2026-10-07 | **Guide**: [quickstart.md](./quickstart.md)

Machine: Windows 11, Docker Desktop 28.5.1, .NET SDK 10.0.401, Node 24.19.0. The image was
built from this branch by the smoke project's compose deployment.

## Verified

| Quickstart § | Criterion | How | Result |
|---|---|---|---|
| §1 | SC-006, US1 | `CollectionSchemaGoldenTests` (server) and `collectionSchema.spec.ts` (admin) over all 32 golden files; `CollectionApiTests` | Both validators agree on every file. 14 API tests pass. |
| §2 | SC-002, US2 | `RenameReorderTests` (200 items, every type, draft and published) | No stored value or field id changes; no item row is written. |
| §3 | US3 | `SchemaMigrationTests`, `ConfirmationTests` | Fill-ins, required, removals and option removals store exactly what the preview counted. |
| §4 | SC-004, US4 | `FieldConversionTests` (every contract §7 row), `SchemaMigrationTests` (all 13 allowed type pairs, media kinds) | Stored outcomes match the preview in every case. |
| §5 | US5 | `CollectionReferenceTests` | Referenced delete refused; self-reference deletes; the delete/add-reference race has one winner. |
| §6 | SC-005, FR-037, US6 | `InterruptedSchemaChangeTests` (10,000 items), `ItemWriteGateTests`, `ConcurrencyTests`, `SchemaHistoryTests` | An interrupted change leaves everything unchanged; item writes refused during a change; history has no item values. |
| §7 | SC-003 | `CollectionSchemaPerformanceTests` on the reference compose setup | 10,000 items: **preview 309 ms** (target 5 s), **confirmed text-to-number change 2,246 ms** (target 30 s). Counts: 9,000 converted, 1,000 cleared. |
| §8 | SC-007 (automated part) | axe checks in every `tests/cms/*.spec.ts`; keyboard reorder test | No serious or critical issues; Alt+↑/↓ reorders and announces. |
| §9 | Upgrade, isolation | `UpgradeFrom013Tests`, `CmsIsolationProbe`, `CmsSiteFilterTests` | Empty `cms` schema added, nothing else changed; no cross-site read or write. |
| — | Release smoke | `ReferenceDeploymentSmokeTests` | Passes, including step 9g (collection created and read back from the image). |
| — | `./qb.ps1 seed-items` | Its SQL run against PostgreSQL 16 with a schema of every seedable type | Well-formed values; optional fields sometimes empty; `min` respected. |

## Not yet done by hand

These need a person at a browser and are still open:

- **SC-001**: a first-time Designer creates an eight-field collection in under 5 minutes
  (quickstart §1 step 1), timed.
- **SC-007 (manual part)**: the keyboard-only walk through §1 step 1 (create, add fields,
  edit options, reorder, preview, confirm, delete) with a screen reader.
- **§2–§6 by hand** on `./qb.ps1` with `./qb.ps1 seed-items`, including stopping the app
  container mid-change (§6 step 4). The automated equivalents above all pass.

## Findings during implementation

- `IPublicMediaReader` finds nothing on the admin surface: it resolves its site from the
  host name. Media gained `IMediaContentTypes` (research CS-12, corrected).
- Rich text paragraphs allow at most 1,000 inline nodes, so text past 500 lines now shares
  paragraphs evenly rather than filling the last one (contract §7).
- Two settings changes that alter no value (`multiline` on, `decimals` raised) are `free`, and
  adding a required field is `reported` (contract §7).
