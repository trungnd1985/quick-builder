# Quickstart: Validating Soft Delete, Trash and Restore

How to prove feature 021 works, end to end. It points at the contracts and research rather
than repeating them. Automated suites are listed in [research.md](./research.md) TR-21.
Record results in `quickstart-results.md`, as earlier features do.

## 0. Prerequisites

- Docker and the .NET 10 SDK, and Node for the admin tests.
- Run everything:

  ```text
  dotnet test                                    # unit, architecture, integration (Docker)
  dotnet test --filter "FullyQualifiedName~Trash"
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

- Run the app for the manual checks: `./qb.ps1 start` (or `./qb.ps1 dev`), then sign in to
  `/admin`.
- Create a site, and on it three members: Administrator A, Designer D and Editor E.
- Optional short retention for §6: set `Trash__RetentionDays=1` in `qb.local.env`.

## 1. Restore each kind (Story 1, SC-001)

1. As D, create the page "About" > "Team", a component "Promo", a collection "Posts" with
   items P1 and P2 (P2 has a reference field pointing to P1), and upload an image used on
   "Team".
2. Unpublish nothing. Delete "Team", "Promo" (once no draft uses it), P2 and the image. Each
   confirmation says "can be restored until {date}" (FR-002).
3. Open **Trash**. All four entries are there, each on its own tab, with who and when.
4. Restore each one. Check:
   - same ids in the URLs;
   - drafts and version lists as before;
   - "Team" back under "About" at its address;
   - P2's reference to P1 works;
   - the image shows on "Team" again.

**Automated**: `PagesTrashRoundTripTests`, `ItemAndFileTrashRoundTripTests`.

## 2. Gone everywhere (Story 2, SC-004, SC-005)

1. Publish "Team" and P1. Bind P1 into a list on a published page, and add a menu entry to
   P1.
2. Remove P2's reference to P1 (a referenced item cannot be deleted, 015 FR-008), then
   delete P1 (published). It is unpublished as part of the delete. Then:
   - its page answers 404;
   - the list and the menu drop it within one request;
   - it is missing from the item picker and from the collection's item count.
3. Delete the image used by the live page "Team" (as A, confirm the in-use warning). Its
   `/_media/…` address answers 404 and the live page renders without it. Restore it: the
   image is back on the next request, on both instances when running
   `docker-compose.perf.yml` with Redis.
4. Open P1 in a second browser as E before step 2. E's form switches to "Moved to the trash
   by D at …", read-only (TR-16).

**Automated**: `TrashVisibilityTests`, `TrashedIdProbeTests`, `MediaChangeRefreshTests`.

## 3. Locks (clarification Q2, TR-09)

1. E opens P2 and types (E holds the lock). D tries to delete P2 and gets "E is editing this
   item since …"; nothing changes.
2. After E leaves, or after E's lock has been idle for 30 minutes (use the fake clock in
   `TrashLockTests`), D's delete succeeds.

## 4. Trash view (Story 3)

1. Delete 30 mixed entries as D and E.
2. Search "team", filter by E and by the "Posts" collection, and filter Media by folder.
   The lists narrow correctly, and the Media tab shows the total size (FR-012).
3. Select 3 entries and restore them; the result lists each outcome (FR-019).
4. As E, the Pages tab shows entries but no Restore button (E lacks
   `pages.structure.manage`). Calling the restore API directly as E answers 403.

**Automated**: `TrashListPerformanceTests` with 10,000 entries, under 1 s at p95 (SC-003).

## 5. Restoring when the place is gone (Story 4, SC-008)

| Situation | Expected |
|---|---|
| Delete "Team", then "About". Restore "Team". | Offers "Restore About as well" or "Choose another place" (top level preselected); nothing happens until a choice is made |
| Permanently delete "About" (as A), then restore "Team" | Picker only |
| Create a new page at Team's old address, then restore "Team" | Plan shows `/about/team` → `/about/team-2` |
| Delete a file's folder (it holds only trashed files), then restore the file | Goes to the top level, and the dialog says so |
| Add a required field to "Posts", then restore P2 | Restored and marked "needs attention" |
| Delete the "Posts" collection while P2 is in the trash | Confirmation counts the trashed item; P2 leaves the trash |
| Change the plan between viewing and confirming (another tab takes the address) | `trash.plan_changed` with the new plan |

**Automated**: `TrashRestoreRulesTests`, `PageTreeTrashTests`.

## 6. Permanent delete and purge (Stories 5 and 6, SC-006, SC-007)

1. As A, permanently delete one entry. It cannot be restored, and for media the bytes are
   gone from storage.
2. As A, Empty trash: type the count. Delete one more item in another tab before confirming;
   that item survives (`trashedUpTo`).
3. As D and E, there is no permanent delete or Empty, and the API answers 403.
4. With `Trash__RetentionDays=1`, move the clock (integration test) or wait. Entries older
   than one day are purged within the hour, and the publish log shows `purged` by "system".
5. Start with `Trash__RetentionDays=0`. Startup fails, naming `Trash:RetentionDays` and
   1–3650.

**Automated**: `TrashPurgeTests` (two instances, race with restore, emptying),
`TrashBulkEndpointTests`, `TrashAuthorizationTests`, `StorageStartupValidationTests`.

## 7. Export, import, upgrade

1. With entries in the trash, export the site. The archive contains none of them
   (`TransferTrashTests`).
2. Restore-import an archive into the site: the trash is empty afterwards. Merge-import an
   archive holding a document that is in the trash: the imported one is added beside it, and
   the trashed copy stays in the trash.
3. `UpgradeFrom020Tests`: a 020 database upgrades, nothing is in the trash, every address is
   unchanged, and trash and restore work on upgraded rows.

## 8. Accessibility (FR-031)

Complete §1, §4 and §6.2 with the keyboard only. axe reports no violations in
`TrashView.spec.ts`, `RestoreDialog.spec.ts` and `EmptyTrashDialog.spec.ts`. The
screen-reader pass is manual (exception E-5).
