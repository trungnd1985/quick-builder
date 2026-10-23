# Contract: Snapshots, Expectations and the Upgrade Gate

**Feature**: 019 · **Requirements**: FR-008, FR-011 – FR-014, FR-016, FR-019, FR-036,
SC-001, SC-002, SC-006, SC-007 · **Research**: RL-09, RL-14, RL-15, RL-17, RL-18, RL-20

## 1. Bundle

Three assets on the GitHub release of version `V` (data-model §3):

- `quickbuilder-V-snapshot.dump` — `pg_dump --format=custom --no-owner --no-acl`, taken from
  the `db` service of the gate's compose project with the app **stopped** (consistent dump).
- `quickbuilder-V-media.tar.gz` — `tar -czf` of the media volume root, ownership numeric.
- `quickbuilder-V-expectations.json` — §2.

Restoring (the gate and the rollback guide use the same steps): stop the stack; remove both
volumes; start `db` alone; `pg_restore --no-owner --role=quickbuilder -d quickbuilder` ; untar
into the media volume through a throwaway container running as the image's `app` user; start
the app with the chosen image.

## 2. Expectations file

```json
{
  "format": 1,
  "createdBy": "0.2.0",
  "createdFrom": "0.1.0",
  "pages": [
    { "host": "release-0-1-0.example.test", "path": "/", "status": 200,
      "fingerprint": "sha256:…", "text": "first 400 characters for diffs" }
  ],
  "records": { "pages.pages": 41, "cms.items": 120, "forms.submissions": 9 },
  "members": [
    { "email": "admin@release-0-1-0.example.test", "password": "<generated>", "role": "Administrator" }
  ],
  "drafts": [
    { "kind": "page", "site": "<id>", "id": "<id>", "endpoint": "/api/v1/sites/{site}/pages/{id}/draft" }
  ]
}
```

- **pages**: every published page and item page of every site in the database, on the site's
  first enabled host name, plus one path per site that must answer 404 and one redirect source
  (status 301/308 with its `Location`). Disabled sites: their host must still answer as a
  disabled site does.
- **fingerprint**: SHA-256 of the *visible form* (§3).
- **records**: `SELECT count(*)` for every table in every module schema except the volatile
  list (§3).
- **members**: every member the journeys created. Passwords are generated per journey run
  and exist only in this file (FR-013).
- Files from earlier releases are merged: snapshot(N)'s file holds every page and member of
  snapshot(N-1) plus N's new site.

## 3. Comparison rules

**Visible form of a page**: parse the HTML; take `<title>`, then `<body>` text content with
runs of whitespace collapsed to one space; then, in document order, every `href`, `src`,
`srcset`, `action` and `alt` attribute value, with:
- the content hash removed from Rendering's asset file names (`blocks.<hash>.css` →
  `blocks.css`, likewise for scripts);
- media variant query strings kept (they are content addresses).

**Time-dependent content**: the content journey schedules publishes and unpublishes for
`2099-01-01` or later, and creates no content whose visible form depends on the current date.
At capture, a page whose visible form contains the current date (ISO or the site's display
format) fails with "time-dependent page". This keeps a snapshot restored months later
identical to when it was taken.

**Allowed visible changes** (FR-012a): files `docs/releases/unreleased/<issue>-visible-changes.json`,
reviewed in the pull request that makes the change:

```json
{ "issue": 42, "reason": "Buttons now read 'Send' instead of 'Submit'.",
  "pages": [ { "host": "*", "path": "/contact" }, { "host": "release-0-1-0.example.test", "path": "/news/*" } ] }
```

`host` is a host name or `*`; `path` is exact or ends in `/*`. At step 3 and step 6, a page
whose fingerprint differs passes only if an entry matches it; the diff is still printed. An
entry that matches no differing page fails the gate ("unused allow-list entry"), so stale
entries cannot hide later regressions. Status codes, records and sign-in are never
allow-listed. `qb-release notes` lists every entry under "Visible changes", and `archive`
moves the files with the fragments.

**Volatile tables** (not counted): every `__ef_migrations_history`, `outbox_messages`,
`processed_events`, Identity `sessions`, `sign_in_throttles`, `known_devices` and `set_password_links`, Forms `rate_windows` and
`spam_counters`, Pages render-cache bookkeeping, `platform.database_version`. The list lives in
`ReleaseExpectations.VolatileTables` with a reason per entry; a table not on the list and not
counted fails the capture ("unknown table").

## 4. The upgrade gate (one matrix leg)

| # | Step | Passes when |
|---|---|---|
| 1 | Restore the source bundle (§1) | `pg_restore` exits 0 |
| 2 | Start the candidate; measure up → ready | ready within 5 minutes (SC-001) |
| 3 | Compare pages | every page: same status; same fingerprint, unless allowed (§3) (SC-002) |
| 4 | Compare records | every counted table: count ≥ before; no table missing (SC-002) |
| 5 | Sign in every member; open every draft | 200 each (US1 scenario 3) |
| 6 | Wait until background rewrites are done (no `cms.items` below the current index version, polled in SQL, ≤ 5 min), compare pages again | same as 3 (FR-019) |
| 7 | Restart the app; compare `pg_stat_user_tables` write counters before/after | zero inserts/updates/deletes outside volatile tables (SC-007, FR-016) |
| 8 | *Previous-release leg only*: run the content journey of `V`, capture expectations, dump → new bundle artifact | journey succeeds |
| 9 | *Previous-release leg only*: start the **previous** image on the upgraded database | readiness `Incompatible` within 30 s; Critical log line names both versions; write counters unchanged (SC-006) |
| 10 | *Previous-release leg only*: rollback drill — restore the source bundle, start the previous image | ready; pages and records as in the source expectations (FR-036, SC-010) |

Step 9 is skipped when the previous release has the same major.minor as `V` (a patch: the
older patch is *allowed* to run, clarification Q1); instead the leg asserts it becomes ready.
For 0.1.0 (baseline source), steps 9 and 10 use the baseline image; step 9 is replaced by
"starts and is ready" because the baseline has no check (it predates this feature).

Each failure prints the step, the page or table, and for pages a unified diff of the first
400 characters of visible text.

## 5. Test code

- `tests/QuickBuilder.SmokeTests/Release/`: `ContentJourney.cs` (data-model §4),
  `ReleaseExpectations.cs` (capture, merge, compare), `SnapshotBundle.cs` (dump, restore),
  `ReleaseUpgradeTests.cs` (`[Trait("Category","ReleaseUpgrade")]`, steps of §4, driven by
  `QB_UPGRADE_FROM`, `QB_SMOKE_IMAGE`, `QB_PREVIOUS_IMAGE`, `QB_SNAPSHOT_DIR`,
  `QB_SNAPSHOT_OUT`).
- Excluded from the normal CI run like the other smoke tests.
- `VisibleFormTests` (unit, `QuickBuilder.UnitTests/Release/`) pin the visible-form rules on
  fixed HTML.
