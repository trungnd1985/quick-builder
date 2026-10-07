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

## Hand-run on the running app (`./qb.ps1`), 2026-10-07

Driven through the admin API as an Administrator, with item counts checked directly in
PostgreSQL. Data-changing steps ran on test collections (`qa-014` with 300 items from
`./qb.ps1 seed-items`, `qa-big` with 10,000), never on the user's `blog-posts`.

| § | Step | Result |
|---|---|---|
| §2 | On `blog-posts` (300 seeded items): rename and rekey a field, move a field, rename the collection, then restore | Preview: every change `free`, no confirmation. Revision 2 → 3; field ids unchanged; fingerprint of all 300 items (draft and published) identical. A re-added old key got the new id `f7` and no values. Names restored at revision 5. |
| §3.1 | Add an optional field | `free`; no item changed. |
| §3.2 | Add a required option with a fill-in | `filled: 300`; written to 300 drafts and 300 published copies. |
| §3.3 | Make a field required, no fill-in | `stillMissing: 92`, equal to the 92 items without it; nothing changed. |
| §3.4 | Remove a field with values | Without a digest: 409 `cms.confirmation_required`, `cleared: 200` (SQL: 200), 10 examples. With it: saved; no item holds the field. |
| §3.5 | Remove the title field | 400 `invalid_title_field`. |
| §4.1 | Text to whole number on "5", "12.5", "about 7", "" | converted 75, partly lost 75, cleared 150; stored 75 × 5, 75 × 13, 150 empty, draft and published. |
| §4.3 | Date to yes/no with values | 400 `cms.type_change_not_allowed`. |
| §4.4 | Lower `maxLength` to 10 | `reported`, `nowInvalid: 137` (SQL: 137 longer); values unchanged. |
| §4.6 | Stale preview | One item moved off the option after the preview: 409 `cms.preview_outdated`, fresh preview 66 instead of 67; saved with the fresh digest. |
| §5 | Delete a referenced collection | 409 `cms.collection_referenced` naming "Author in QA 014"; after removing the field, 204 and the address name is free again. A self-reference did not block anything. |
| §6.1 | History | 11 entries, newest first; no item value anywhere in it. |
| §6.2 | Save from a stale revision | 409 `cms.revision_conflict`, `currentRevision` 11. |
| §6.3 | Save while a change rewrites 10,000 items | 409 `cms.collection_busy`; reads still 200; the change completed. |
| §6.4 | `docker compose restart app` mid-change | Graceful stop: the in-flight change finished (200) before the app stopped. |
| §6.4 | `docker compose kill app` mid-change | Connection lost; after restart, revision and schema unchanged and all 10,000 items byte-identical; the same save then succeeded, so no lock was left behind. |

## Done by hand in a browser, 2026-10-07

Run by the maintainer, and reported as done:

- **SC-001**: a Designer created an eight-field collection (quickstart §1 step 1), timed.
- **SC-007 (manual part)**: the keyboard-only walk through §1 step 1 (create, add fields,
  edit options, reorder, preview, confirm, delete) with a screen reader.

The time and any notes were not passed on; add them here if they should be kept.

## Findings during implementation

- `IPublicMediaReader` finds nothing on the admin surface: it resolves its site from the
  host name. Media gained `IMediaContentTypes` (research CS-12, corrected).
- Rich text paragraphs allow at most 1,000 inline nodes, so text past 500 lines now shares
  paragraphs evenly rather than filling the last one (contract §7).
- Two settings changes that alter no value (`multiline` on, `decimals` raised) are `free`, and
  adding a required field is `reported` (contract §7).
