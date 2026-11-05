# Quickstart: Validating Site Export and Import

**Feature**: 020 · **Spec**: [spec.md](./spec.md) · **Contracts**: [contracts/](./contracts/)

## 0. Prerequisites

- The reference compose setup (2 vCPU, 4 GB) running this branch's image, setup completed,
  and one Administrator, one Designer and one Editor on site **Acme**.
- A `./backups:/backups` volume mounted into the `app` service.
- **Acme** filled by the seed script `tests/fixtures/transfer/seed-acme.http`:
  - every block type, a form with a redirect, Navigation blocks and two menus;
  - three collections with every field type and cross-references, and a collection template;
  - 214 redirects, search settings and design tokens;
  - media in folders;
  - a copy made by 023;
  - drafts ahead of live, older versions, and a future and a past-due schedule.
- A second, empty installation (`docker compose -p qb2 up`) for moves.

Automated suites (research ST-23):

```bash
dotnet test tests/QuickBuilder.UnitTests --filter "FullyQualifiedName~Transfer|FullyQualifiedName~Remap"
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~Transfer"
dotnet test tests/QuickBuilder.ArchitectureTests
npm --prefix src/admin run test -- transfer
```

## 1. Export (Story 1; FR-001 … FR-010)

1. As the Administrator: Acme → Settings → Export and import → **Export site**.
   - *Expect*: progress with steps and percent; it is still shown after navigating away and
     back.
2. While it runs, the Editor edits and publishes a page.
   - *Expect*: no refusal; the archive holds the page either before or after the edit
     (`ExportSnapshotTests`).
3. Delete one media file's bytes from storage before exporting again.
   - *Expect*: the export succeeds, and the summary lists that file as missing.
4. Download with `curl -r 0-1023` and then the rest.
   - *Expect*: `206`, then a complete file whose SHA-256 matches a full download.
5. As the Designer, call `startExport` and `downloadArchive`.
   - *Expect*: `403` for both.
6. Unzip the archive and inspect it.
   - *Expect*: entries exactly as in [site-archive.md](./contracts/site-archive.md) §1; no
     submissions, editing sessions or emails (`grep -r "@" parts/` finds only content).

## 2. Import as a new site (Story 2; FR-017 … FR-021a)

1. On `qb2`: Sites → **Import site**, upload the archive.
   - *Expect*: the check shows the counts and no errors.
2. Confirm with the name "Acme copy".
   - *Expect*: the report shows the counts and zero broken references beyond those present in
     Acme.
3. Compare: `RoundTripTests` asserts equality. By hand, open five pages, an item page and a
   menu at the new site's address.
   - *Expect*: they look as on Acme; links go to the new site; images load.
4. Import the same archive again on the original installation.
   - *Expect*: `www.acme.example` is listed as left out; there are two independent sites.
     Edit one and confirm the other is unchanged.
5. Import again with **Start fresh**.
   - *Expect*: nothing is live (the public pages 404); every page and item has one draft;
     there is no history and no schedules.

## 3. Refusals (Story 3; FR-011 … FR-016; SC-003)

Run `ArchiveRefusalTests`, then by hand:
- a truncated file (`head -c 1000000`) → `archive.unreadable`;
- one byte changed in `parts/pages/pages.jsonl` → `archive.integrity`;
- an entry `../evil` added with a fixed manifest → `archive.unsafe_path`;
- `format: 2` → `transfer.format_too_new`;
- an invalid page block → `transfer.document_invalid`, naming the page.

*Expect*: each one is refused at check, and no table row count changes.

## 4. Merge and restore (Story 4; FR-022 … FR-029; SC-005)

1. Import the "Starter" fixture archive into Acme with **rename**.
   - *Expect*: `/about-2`, "Header (imported)", imported pages use it; Acme's own pages are
     unchanged.
2. Repeat with **skip**, then with **replace**.
   - *Expect*: the outcomes in research ST-10. After replace, the old versions are visible in
     the page history.
3. Export Acme. Rename `/about` to `/company`. Merge the archive back with replace.
   - *Expect*: `/company` takes the archive's content and its address goes back to `/about`;
     no duplicate page appears (FR-022a).
4. Export Acme. Add a page, delete an item, change a collection's fields, upload an image.
   Then **Restore** and type the site name.
   - *Expect*: the preview lists exactly those four changes; afterwards Acme equals the
     archive; host names are unchanged; the report links the pre-restore export.
5. Restore the pre-restore export.
   - *Expect*: the state of step 4 before the restore is back.
6. During the restore's apply, the Editor tries to save.
   - *Expect*: the banner appears, and the save is refused with `423`. Visitors see the old
     site until the commit, then the restored one.
7. `ImportFailureTests`: faults after each part, cancel, and a killed connection.
   - *Expect*: Acme is unchanged every time (SC-004); orphaned bytes are gone after the
     grace period.

## 5. Command line (Story 5; FR-032 … FR-034; SC-006)

```bash
docker compose exec -T app dotnet QuickBuilder.Host.dll sites export --all --out /backups; echo $?
docker compose exec -T app dotnet QuickBuilder.Host.dll sites check /backups/<file>.qbsite --into acme.example; echo $?
docker compose exec -T app dotnet QuickBuilder.Host.dll sites import /backups/<file>.qbsite --as-new --name "Acme 2"; echo $?
docker compose exec -T app dotnet QuickBuilder.Host.dll sites import /backups/<file>.qbsite --restore acme.example; echo $?
```

*Expect*:
- exit codes `0`, `0`, `0` and `6` (missing `--confirm`);
- `SitesCommandTests` covers 1, 2, 4, 5 and 64;
- a nightly cron example from `docs/operations.md` runs unchanged.

## 6. Performance (SC-002)

`TransferPerformanceTests` on the reference setup, with 500 pages, 10,000 items and 2 GB of
media.

*Expect*:
- export ≤ 15 min and import as a new site ≤ 20 min;
- the public p95 render stays within the Principle VIII budgets during both;
- container memory stays below 1 GB.

## 7. Accessibility and audit (FR-031, FR-039)

- axe runs in the admin specs. Check by hand with a screen reader that progress and
  completion are announced, and that the restore dialog can be used by keyboard alone.
- `identity.security_events` holds `site_exported`, `site_archive_downloaded`,
  `site_imported` and `site_restored` with ids only, and `operator: true` for the command
  line.

Record the results in `quickstart-results.md`.
