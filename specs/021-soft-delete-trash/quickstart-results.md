# Quickstart results: Soft delete, trash and restore (021)

**Status**: automated part and API walk-through done 2026-11-15 (task T098). The admin-screen
pass, the keyboard-only pass of §8 and the screen-reader check are still to do by hand.

This records which parts of [quickstart.md](./quickstart.md) the automated suites prove, what
an API walk-through against a real deployment showed, and what a person still has to check.

## Automated evidence

| Quickstart section | Proven by | Result |
|---|---|---|
| §1 Restore each kind (SC-001) | `ITESTS/Trash/PagesTrashRoundTripTests.cs`, `ItemAndFileTrashRoundTripTests.cs` | pass |
| §2 Gone everywhere (SC-004, SC-005) | `TrashVisibilityTests`, `TrashedIdProbeTests`, `ITESTS/Rendering/MediaChangeRefreshTests.cs` (two instances) | pass |
| §3 Locks | `TrashLockTests` | pass |
| §4 Trash view | `TrashRestoreRulesTests`, `TrashBulkEndpointTests`, `ADMINTESTS/features/trash/*.spec.ts` | pass |
| §4 Performance (SC-003) | `TrashListPerformanceTests` with `QB_TRASH_PERF=1`, 10,000 entries | pass |
| §5 Place gone (SC-008) | `TrashRestoreRulesTests`, `UTESTS/Pages/PageTreeTrashTests.cs` | pass |
| §6 Permanent delete and purge (SC-006, SC-007) | `TrashPurgeTests`, `TrashBulkEndpointTests`, `TrashAuthorizationTests`, `TrashIsolationProbe`, `StorageStartupValidationTests` | pass |
| §7 Export, import, upgrade | `TransferTrashTests`, `UpgradeFrom020Tests` | pass |
| §8 Accessibility, automated part | axe in `TrashView.spec.ts`, `RestoreDialog.spec.ts`, `EmptyTrashDialog.spec.ts` | pass |

## API walk-through against a deployment

This checkout was built into its own image and run with `docker-compose.yml` on a separate
port, with its own database and media volumes, as a freshly claimed installation. One
Administrator (A), one Designer (D) and one Editor (E) were created through the API. Each step
of §1, §2, §4, §5 and §6 was driven through the admin API, and the site was visited through its
host name. The stack was removed afterwards.

| Section | Checks | Result |
|---|---|---|
| §1 | D deletes a page, a component, an item and a file; each answer carries the restore-until date; each tab lists the entry with D and the time; each restores with its plan; Team is back under About at `/about/team` with its draft; P2's reference to P1 works; the file's bytes are served | 17 pass |
| §2 | Team, live, shows the image; deleting the file asks for confirmation (`media.in_use`); after confirming, its `/_media/…` address answers 404 and Team renders without it; after restoring, both are back. A published item is deleted, unpublished by the delete, not found, missing from the count, counted as trashed by its collection, and its plan offers restore-and-publish | 10 pass (see note 1) |
| §4 | Search "team" finds 10 of 20; filters by who deleted, by E with the Posts collection, and by Media folder; Media shows the total size; 3 restored at once, each with its outcome; E sees no Restore button, and the API refuses E's restore with 403 | 8 pass |
| §5 | Parent in the trash: 422 `trash.placement_required` until a choice is made; "with parents" plans `/about/team`, "top" plans `/team`; parent purged: picker only; address taken since: `/team-2`; a stale plan: 409 `trash.plan_changed` | 6 pass |
| §6 | D and E cannot delete permanently (403); A can, and the entry cannot be restored; a purged file's bytes are gone; Empty with a stale count is refused (409), D cannot empty (403), and A's Empty spares the page deleted after the list was shown; the site read gives the retention (30 days) | 8 pass |
| §6.5 | An instance started with `Trash__RetentionDays=0` stops at startup with "Trash:RetentionDays must be between 1 and 3650 days." | pass |

**Notes**

1. As first written, §2 asked to delete P1 after §1 made P2 refer to it. That delete is refused
   with 409 `cms.item_referenced`, as 015 FR-008 requires, so the walk-through used an item
   nothing refers to. Quickstart §2 now says to remove P2's reference first.
2. Not driven through the API: §3 (locks need two live editing sessions; `TrashLockTests`
   covers it), the §5 rows on folders, required fields and deleting the collection
   (`TrashRestoreRulesTests`, `TrashBulkEndpointTests`), the retention job's timing in §6.4
   (`TrashPurgeTests`), and §7 (`TransferTrashTests`, `UpgradeFrom020Tests`).

## Still to do by hand

1. **The admin screens** for §1, §2, §4 and §6.2 in a browser: the "can be restored until"
   notice after each delete, the Trash tabs, preview, restore dialog, and Empty trash.
2. **§2.4**: E's open item switches to "Moved to the trash by D", read-only.
3. **§8 keyboard only**: §1, §4 and §6.2 without a mouse.
4. **§8 screen readers** (exception E-5).

Record each result here with the date and the browser.
