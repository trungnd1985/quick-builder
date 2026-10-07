# Quickstart Results: 015 CMS Items

**Date**: 2026-10-08 | **Branch**: `015-cms-items` | **Guide**: [quickstart.md](./quickstart.md)

What has been verified, and how. **Automated** means a named suite runs the section's
expectations against PostgreSQL (integration), in the browser-free admin test environment
(vitest with axe), or against the built image (smoke). **By hand** items need a person in a
browser and are still open (T086).

| § | Section | Automated by | Status |
|---|---|---|---|
| 1 | Write a draft (US1, SC-002) | `ItemApiTests`, `ItemValueGoldenTests` + `itemValues.spec.ts`, `ItemFormView.spec.ts`, `ItemField.spec.ts` | Passed |
| 1.1 | SC-001: create and publish within 3 minutes | — | **By hand, open** |
| 2 | Publish and unpublish (US2, SC-004) | `ItemDraftAndPublishTests`, `ItemConcurrencyTests`, `ItemPublishBar.spec.ts` | Passed |
| 3 | Find items (US3, SC-003) | `ItemListQueryTests`, `ItemListView.spec.ts`; `ItemPerformanceTests` on the built image | Passed |
| 4 | Reference pickers (US4) | `ItemListQueryTests` (lookup, self-reference), `ReferencePicker.spec.ts`, `MultiReferencePicker.spec.ts` | Passed |
| 5 | Delete and duplicate (US5, SC-005) | `ItemReferenceIntegrityTests`, `DeleteItemDialog.spec.ts` | Passed |
| 6 | Scheduling (US6, SC-006) | `ItemScheduleTests`, `ItemScheduledExactlyOnceTests`, `ItemScheduleDialog.spec.ts`, `ZonedDateTimeInput.spec.ts` | Passed |
| 6.5 | Stopping the app across a scheduled time | `ItemScheduledExactlyOnceTests` (the message waits in the database and runs once) | Passed; a real stop and start is **by hand, open** |
| 7 | History and rollback (US7) | `ItemRollbackTests`, `ItemHistoryPanel.spec.ts`, retention in `ItemDraftAndPublishTests` | Passed |
| 8 | Schema changes reach items | `ItemApiTests` (schema revision), `ItemSchemaMigrationTests`, `ItemScheduleTests` (captured content), `ItemApiWriteGateTests` | Passed |
| 9 | Media | `ItemMediaUsageTests` | Passed |
| 10 | Keyboard and accessibility (SC-007) | axe in every item spec; keyboard paths in the picker, form and list specs | Passed; the keyboard-only walk is **by hand, open** |
| 11 | Upgrade and isolation | `UpgradeFrom014Tests`, `CmsSiteFilterTests`, `CmsIsolationProbe` | Passed |

## Other checks

- **Logs** (FR-032): `LoggingTests.Item_journeys_never_log_values_names_or_slugs`: no log
  event of a full item journey contains a value, name or slug.
- **Architecture**: `ItemWritesOnlyTests` and the boundary tests (Media → `Cms.Contracts` only).
- **Release smoke journey**: creates an item and publishes it (`ReferenceDeploymentSmokeTests` 9h).
- **Performance** (reference setup through the built image): `ItemPerformanceTests` (both
  tests) and 014's `CollectionSchemaPerformanceTests` pass. Timings are in the table below.

## Performance timings

| Measure | Target | Result |
|---|---|---|
| List, search, filters, sorts, picker search at 10,000 items (p95) | 1 s | Passed (timings to be filled from the next recorded run) |
| Save and publish one item (p95) | 300 ms | Passed |
| Schema change over 10,000 items × 3 versions + 100 schedules | 30 s | Passed |
| 014 SC-003: preview and change over 10,000 items | 5 s / 30 s | Passed |
