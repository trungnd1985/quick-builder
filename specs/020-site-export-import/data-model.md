# Data Model: Site Export and Import

**Feature**: 020 · **Spec**: [spec.md](./spec.md) · **Research**: [research.md](./research.md)

New tables: `sites.transfer_jobs` and `identity.historical_authors`. No other table changes.
Parts read and write the existing tables of their modules (research ST-03).

## 1. `sites.transfer_jobs`

One export, upload or import. This is installation data: an import as a new site has no site
until it commits. The entity is marked
`[InstallationScoped("Transfers are Administrator-only installation operations; a new-site import has no site until it commits (020 ST-15).")]`,
and every read filters by `site_id` explicitly where a site is given.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `kind` | text | `export`, `import` |
| `trigger` | text | `admin`, `cli`, `pre_restore` |
| `mode` | text, null | import only: `new_site`, `merge`, `restore` |
| `start_fresh` | bool | `new_site` only (FR-021a) |
| `conflict_policy` | text, null | `merge` only: `replace`, `skip`, `rename` |
| `site_id` | uuid, null | export: the source. Merge and restore: the target. New site: set at commit |
| `new_site_name` | text, null | `new_site`: the name the Administrator gives |
| `state` | text | §1.1 |
| `step` | text, null | `reading`, `media`, `design`, `cms`, `pages`, `sites`, `committing`, `pre_restore` |
| `percent` | smallint | 0–100 |
| `progress_at_utc` | timestamptz | last progress write |
| `archive_key` | text, null | storage key of the archive (export result or upload) |
| `archive_bytes` | bigint, null | |
| `archive_expires_at_utc` | timestamptz, null | export +7 days, upload +24 hours (ST-16) |
| `manifest` | jsonb, null | import: the archive's manifest after the integrity check |
| `check_result` | jsonb, null | §3 |
| `check_fingerprint` | text, null | hash of the conflicts and removals confirmed (ST-06) |
| `summary` | jsonb, null | export: counts and problems (FR-009) |
| `report` | jsonb, null | import: §4 |
| `failure_code` | text, null | §5 |
| `pre_restore_job_id` | uuid, null | restore: the export made first (FR-027d) |
| `cancel_requested` | bool | |
| `lease_owner` | text, null | instance id |
| `lease_until_utc` | timestamptz, null | |
| `started_by` | uuid, null | null for `cli` |
| `created_at_utc`, `confirmed_at_utc`, `finished_at_utc` | timestamptz | |
| `xmin` | xid | concurrency token |

Indexes:
- `ux_transfer_jobs_active_site`: unique `(site_id)` where `site_id is not null`, `trigger <> 'pre_restore'`, and either `kind = 'export'` with `state in ('queued','running')` or `kind = 'import'` with `state in ('confirmed','running')`.
  This enforces one active transfer per site (FR-010).
  - A check (`checking`, `awaiting_confirmation`) holds no slot, so an unconfirmed check never
    blocks exports. Confirm takes the slot and fails with `transfer.busy` if another transfer
    holds it.
  - The pre-restore export runs while its restore holds the site's slot. It is the only job
    allowed to (research ST-13).
- `ix_transfer_jobs_claim`: `(state, created_at_utc)` where `state in ('queued','confirmed')`.
- `ix_transfer_jobs_expiry`: `(archive_expires_at_utc)` where `archive_key is not null`.

### 1.1 States

```text
export:  queued ─▶ running ─▶ succeeded ─▶ expired      (archive deleted after 7 days or on delete)
                      └──▶ failed | cancelled

import:  uploading ─▶ uploaded ─▶ queued ─▶ checking ─▶ awaiting_confirmation ─▶ confirmed ─▶ running ─▶ succeeded
                                  │                 │                                 └──▶ failed | cancelled
                                  └▶ refused        └▶ expired (24 h) | cancelled
```

- `refused`: the check failed (FR-012 to FR-014). The report lists every reason and nothing
  was changed.
- `confirmed`: restore runs its pre-restore export (`step = pre_restore`) before `running`.
  If that export fails, the restore ends `failed` with `pre_restore_export_failed`.
- A lease that runs out ends a `checking` or `running` job as `failed` with
  `interrupted` (ST-15).
- Rows are deleted 90 days after `finished_at_utc`.

## 2. `identity.historical_authors`

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK; the author's id as it appears in archived rows after mapping |
| `display_name` | text | 1–200 characters, from `authors.jsonl` |
| `imported_at_utc` | timestamptz | |
| `source_user_id` | uuid | the id in the archive |
| `source_installation` | text, null | unused today; reserved for de-duplication across imports |

Rules:
- One row per `source_user_id` that is not a user of this installation. A second import that
  names the same source id reuses the row (`ux_historical_authors_source`).
- No sign-in, email, role or session refers to these rows, and the Users list does not show
  them.
- `IUserDirectory.FindDisplayNamesAsync` falls back to this table and returns
  `isHistorical: true`.

## 3. Check result (`check_result`)

```text
CheckResult
  archive: { format, release, createdAtUtc, source: { siteId, siteName, sameSiteAsTarget } }
  versions: [{ contract, archiveVersion, currentVersion, action: current|upgrade }]
  counts:   { pages, components, collections, items, menus, redirects, mediaFiles, mediaBytes, folders }
  problems: [{ severity: error|warning, code, part, subject: { kind, archiveId, label }, detail }]
  hostNamesLeftOut: [hostName]                         new_site
  conflicts: [{ kind, archiveId, label, targetId, matchedBy: identity|address|name|slug|source,
                clash?: { targetId, label } }]         merge (FR-022, FR-022a)
  schemaChanges: [{ collectionId, field, change, valuesConverted, valuesLost }]   merge replace, restore
  removals:  [{ kind, targetId, label }]               restore (FR-027b)
  returns:   [{ kind, id, label }]                     restore: in the archive, deleted from the site
  changes:   [{ kind, id, label }]                     restore: content differs
  pastSchedules: [{ kind, archiveId, label, atUtc, action }]                       FR-029
  allowedModes: [new_site, merge, restore]             restore only if source site exists
```

- An `error` refuses the archive. A `warning` (missing or refused media file, a host name
  left out, a past schedule) needs confirmation.
- Labels are titles, names and addresses shown to the Administrator. They live only in the
  job row, never in logs or events (Principle IX).
- Lists are capped at 1,000 entries each, with a total count. The admin pages through them.

## 4. Import report (`report`)

```text
Report
  mode, startFresh, policy, siteId, siteUrl
  imported:   { pages, components, collections, items, menus, redirects, mediaFiles, folders }
  resolved:   [{ kind, archiveId, targetId, outcome: replaced|skipped|renamed|reused, label, newLabel? }]
  leftOut:    [{ kind, archiveId, label, reason }]    host names, refused files
  broken:     [{ kind, ownerId, ownerLabel, missingKind, missingId }]
  droppedSchedules: [{ kind, id, label, atUtc }]
  removed:    { counts per kind }                    restore
  preRestoreJobId                                    restore
  historicalAuthors: count
```

## 5. Failure and refusal codes

| Code | Where | Meaning |
|---|---|---|
| `transfer.busy` | start | the site already has an active transfer (FR-010) |
| `transfer.too_large` | upload | above `SiteTransfer:MaxUploadSizeBytes` (413) |
| `archive.unreadable` | check | not a ZIP, or truncated |
| `archive.integrity` | check | an entry is missing, extra, or its length or hash differs |
| `archive.unsafe_path` | check | an entry name outside the allowed shapes |
| `transfer.format_too_new`, `transfer.release_too_new`, `transfer.release_unsupported` | check | ST-21 |
| `transfer.document_too_new`, `transfer.document_invalid` | check | per entry |
| `transfer.media_refused`, `transfer.media_missing` | check | warnings (FR-015) |
| `transfer.restore_not_allowed` | confirm | the archive's source is not the target (FR-027a) |
| `transfer.confirmation_mismatch` | confirm | the typed name differs (FR-027b) |
| `transfer.target_changed` | apply | conflicts or removals differ from the confirmed check (ST-06) |
| `transfer.check_expired` | confirm | more than 24 hours after the check |
| `site.import_in_progress` | every content write | 423 while an import applies (ST-07) |
| `interrupted`, `pre_restore_export_failed`, `storage_failed`, `unexpected` | run | `failure_code` |

## 6. The id map (in memory, per job)

```text
IdMap
  Resolve(kind, archiveId) → targetId | null
  Outcome(kind, archiveId) → new | matched | replaced | skipped | renamed | reused
```

It is built during check (ST-08), rebuilt inside the apply transaction, and compared by
fingerprint. It is never stored in full. A 10,000-item site holds about 12,000 entries
(about 1 MB).

## 7. Validation rules carried from the spec

- Restore requires `manifest.source.siteId == target.id` and a typed name equal to the site's
  current name, compared ordinally after trimming (FR-027a, FR-027b).
- `start_fresh` requires `mode = new_site`, and `conflict_policy` requires `mode = merge`.
- New site:
  - `new_site_name` follows 009's name rules;
  - host names already used are left out (FR-020);
  - a source that was disabled is created disabled.
- Merge and restore never change host names or the enabled state (FR-027, FR-027c).
- A past-due schedule is dropped. A future schedule is kept, with its author mapped (FR-029).
