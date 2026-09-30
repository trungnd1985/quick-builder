# Data Model: Media Library, Image Variants and Remote Storage Providers

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

All tables are PostgreSQL, created through EF Core migrations in the owning module's schema,
with snake_case columns. Every site-owned table carries `site_id` and a global query filter
(Principle V). "New" and "changed" are relative to 012.

---

## 1. Media schema (`media`)

### 1.1 `media_files` (changed)

Existing columns (003) are unchanged: `id`, `site_id`, `file_name`, `content_type`,
`extension`, `size_bytes`, `uploaded_at_utc`, `uploaded_by` and `storage_key`.
`content_type`, `extension`, `size_bytes` and `storage_key` now change on replace.

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `folder_id` | uuid | yes | null | FK → `folders(site_id, id)`, `ON DELETE RESTRICT`. Null is the top level (MV-02). |
| `alt_text` | varchar(300) | yes | null | Images only (check constraint). Trimmed, control characters removed (MV-04). |
| `width` | int | yes | null | Displayed width after EXIF orientation (MV-10). Images only. |
| `height` | int | yes | null | Displayed height. Null together with `width`. |
| `is_animated` | bool | no | false | More than one frame or page. |
| `metadata_state` | smallint | no | see MV-26 | 0 `pending`, 1 `recorded`, 2 `not_applicable` (PDF), 3 `over_limit`, 4 `unreadable` |
| `content_version` | int | no | 1 | ≥ 1. Incremented by replace (MV-07). |
| `replaced_at_utc` | timestamptz | yes | null | Time of the most recent replacement. |
| `replaced_by` | uuid | yes | null | User who made it. `uploaded_by` never changes (clarification). |
| `variants_state` | smallint | no | 0 | 0 `available`, 1 `unavailable` (MV-12). Reset to 0 on replace. |
| `search_text` | text | no | from `file_name` | Normalised `file_name + ' ' + alt_text` (MV-03). |
| `sort_name` | varchar(255) | no | from `file_name` | Normalised `file_name`. |
| `search_text_state` | smallint | no | 1 | 0 `provisional` (the migration's `lower(file_name)` for existing rows), 1 `normalised` (`SearchText.For`). The start-up backfill moves 0 to 1, one site at a time (MV-26). |
| `xmin` | xid | — | — | Row version: concurrency token for replace, move, alternative text and delete. |

**Check constraints (new)**:
- `ck_media_files_alt_text_images`: `alt_text IS NULL OR content_type LIKE 'image/%'`.
- `ck_media_files_dimensions`: `(width IS NULL) = (height IS NULL)` and both are `> 0` when
  set.
- `ck_media_files_content_version`: `content_version >= 1`.

**Indexes**:
- Existing: `ux_media_files_storage_key` and `ix_media_files_site_listing`
  `(site_id, uploaded_at_utc DESC, id DESC)`.
- New:
  - `ix_media_files_folder_uploaded` `(site_id, folder_id, uploaded_at_utc DESC, id DESC)`
  - `ix_media_files_folder_name` `(site_id, folder_id, sort_name, id)`
  - `ix_media_files_folder_size` `(site_id, folder_id, size_bytes, id)`
  - `ix_media_files_metadata_pending` `(metadata_state)` where `metadata_state = 0`: for
    the backfill.
- Whole-library sorts by name and size use `(site_id, sort_name, id)` and
  `(site_id, size_bytes, id)`, which are also new.

### 1.2 `folders` (new)

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | v7. PK. |
| `site_id` | uuid | no | Alternate key `(site_id, id)`, target of the FKs. |
| `parent_id` | uuid | yes | FK → `folders(site_id, id)`, `ON DELETE RESTRICT`. Null is the top level. |
| `name` | varchar(100) | no | Cleaned as in 003 M-17. 1–100 characters. |
| `name_key` | varchar(100) | no | Normalised name: lower case, accents removed. |
| `created_at_utc` | timestamptz | no | |
| `created_by` | uuid | no | Display only. Grants no permission (clarification). |

- **Unique**: `ux_folders_sibling_name` `(site_id, parent_id, name_key)` `NULLS NOT DISTINCT`.
- **Index**: `ix_folders_site` `(site_id)`: the whole tree is loaded per site (MV-02).

### 1.3 `folder_trees` (new)

| Column | Type | Notes |
|---|---|---|
| `site_id` | uuid | PK. |
| `revision` | bigint | Incremented on every structural folder change. |
| `xmin` | xid | Concurrency token. A conflicting change gets `media.folder_conflict`. |

The row is created lazily on a site's first folder change.

### 1.4 `variant_claims` (new)

| Column | Type | Notes |
|---|---|---|
| `file_id` | uuid | PK part. |
| `content_version` | int | PK part. |
| `width` | int | PK part. |
| `site_id` | uuid | Site filter. |
| `claimed_at_utc` | timestamptz | The lease is 2 × `Media:Variants:Timeout` (MV-12). |
| `instance_id` | uuid | Random per process. Diagnostic only. |

Rows exist only while a variant is being produced, and are deleted when it finishes.

### 1.5 `storage_checks` (new, installation-level)

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | v7. PK. |
| `trigger` | varchar(16) | `startup`, `admin` or `cli`. |
| `provider` | varchar(16) | `Local`, `S3` or `AzureBlob` at the time of the run. |
| `status` | varchar(16) | `running`, `completed` or `failed`. |
| `started_at_utc` | timestamptz | |
| `finished_at_utc` | timestamptz null | |
| `lease_until_utc` | timestamptz | Renewed every 30 s while running. |
| `started_by` | uuid null | Set for `admin` runs. |
| `files_checked` | int | |
| `missing_count` | int | Exact, even beyond the stored list. |

- **Unique partial index** `ux_storage_checks_running` on `(status)` where
  `status = 'running'`: at most one run at a time (MV-21).
- It has no site, by design: it describes the installation's storage. Only
  `media.storage.check` holders can read it.

### 1.6 `storage_check_missing` (new)

| Column | Type | Notes |
|---|---|---|
| `check_id` | uuid | FK → `storage_checks`, cascade. PK part. |
| `site_id` | uuid | PK part. Site-filtered. The report reads with `AcrossAllSites` (MV-21). |
| `file_id` | uuid | PK part. |

At most 1,000 rows per check.

---

## 2. Pages schema (`pages`)

### 2.1 `draft_media_references` (new)

| Column | Type | Notes |
|---|---|---|
| `site_id` | uuid | |
| `subject_kind` | varchar(16) | `page` or `component` (check constraint). |
| `subject_id` | uuid | |
| `media_id` | uuid | Not a foreign key: Media's table is another module's. |

- **PK**: `(subject_kind, subject_id, media_id)`.
- **Index**: `ix_draft_media_references_media` `(site_id, media_id)`.
- **Written** by `DraftWrites` on every page and component draft save: the set of
  `DependencyExtractor` media ids is replaced. **Removed** by the page and component
  deletes, in their transaction.

### 2.2 `version_media_references` (new)

| Column | Type | Notes |
|---|---|---|
| `site_id` | uuid | |
| `version_id` | uuid | FK → `published_versions(id)`, `ON DELETE CASCADE`. |
| `media_id` | uuid | |

- **PK**: `(version_id, media_id)`.
- **Index**: `ix_version_media_references_media` `(site_id, media_id)`.
- **Written** in the publish transaction (publish, scheduled publish, rollback) for the new
  version. Retention and deletes remove them by cascade.

### 2.3 Deriving usage (MV-05)

For a file `m` of site `s`, a **place** is a distinct `(subject_kind, subject_id)`:

| Flag | True when |
|---|---|
| `inDraft` | a `draft_media_references` row `(s, kind, id, m)` exists |
| `live` | a `version_media_references` row for `m` has `version_id = publications.live_version_id` of `(kind, id)` |
| `inHistory` | a `version_media_references` row for `m` exists on a version of `(kind, id)` that is not live |
| `livePagesThroughComponent` | for a live component place: the number of pages with a `live_dependencies` edge page → that component |

- `inLiveUse(m)` = any place is `live`.
- `inUse(m)` = any place exists.

---

## 3. Domain rules and state changes

### 3.1 `MediaFile` (Media, now mutable)

| Operation | Allowed when | Effect | Event |
|---|---|---|---|
| `MoveTo(folder)` | folder is the same site's, or null | `folder_id` | none |
| `SetAltText(text)` | the file is an image; length ≤ 300 after cleaning | `alt_text`, `search_text` | none |
| `RecordImageMetadata(w, h, animated)` | `metadata_state = pending`, or during an upload or replace | dimensions, `is_animated`, `metadata_state = recorded` | `FileChanged(dimensions)` when it was `pending` and the file is not new |
| `MarkMetadata(over_limit or unreadable)` | backfill only | `metadata_state` | none (nothing changes for pages) |
| `Replace(type, ext, size, key, w, h, animated, by, at)` | same kind; row version matches | increments `content_version`; updates `storage_key`, type, extension, size, dimensions, `replaced_*` and `search_text`; resets `variants_state` | `FileChanged(replaced)` |
| `MarkVariantsUnavailable(version)` | `version = content_version` | `variants_state = unavailable` | `FileChanged(variants)` |
| delete (003) | MV-06 rules | row removed | `FileDeleted` (012) |

- **Invariants**: `id`, `site_id`, `uploaded_by`, `uploaded_at_utc` and `file_name` never
  change (the display name stays across a replacement, FR-017).
- **Variant eligibility**:
  `content_type ∈ {jpeg, png, webp, avif}` ∧ `¬is_animated` ∧
  `metadata_state = recorded` ∧ `variants_state = available` ∧ the AVIF codec is present
  (for AVIF).

### 3.2 `Folder` and `FolderTree` (Media)

| Operation | Rules | Errors |
|---|---|---|
| Create | parent exists in the site; depth ≤ 8; site total ≤ 1,000; sibling name free | `media.folder_not_found`, `media.folder_too_deep`, `media.folder_limit`, `media.folder_name_taken`, `media.invalid_folder_name` |
| Rename | sibling name free | `media.folder_name_taken`, `media.invalid_folder_name` |
| Move | new parent is not the folder itself or a descendant; the resulting depth of its whole subtree ≤ 8 | `media.folder_cycle`, `media.folder_too_deep` |
| Delete | no child folders and no files (the database enforces it) | `media.folder_not_empty` |

- Every operation increments `folder_trees.revision` in the same transaction. A concurrent
  change gives `media.folder_conflict`.

### 3.3 Variant production state (per file, version and width)

```text
absent ──request──▶ claimed ──produced──▶ stored ──(replace, delete or width removed)──▶ swept (≥ 1 h)
   ▲                   │
   │                   └──failure or timeout──▶ file.variants_state = unavailable
   └──claim lease expired (crash)── taken over by the next request
```

### 3.4 Storage check

```text
(none) ──start──▶ running ──done──▶ completed
                     │
                     ├──error──▶ failed
                     └──lease expired──▶ failed (marked by the next claimer)
```

---

## 4. Integration events

| Event | Published by | Payload | Subscribers |
|---|---|---|---|
| `Media.FileDeleted` (012, unchanged) | Media | id, time, site, file, actor | Pages: refresh live pages |
| **`Media.FileChanged`** (new) | Media | id, time, site, file, reason (`replaced`, `dimensions`, `variants`) | Pages: refresh live pages (same handler) |

Ids only. No names, alternative text or content (FR-041).

---

## 5. Error codes (new; 003's codes are unchanged)

| Code | HTTP | When |
|---|---|---|
| `media.dimensions_too_large` | 422 | Upload or replace above `Media:MaxImagePixels` |
| `media.kind_mismatch` | 422 | Replacement of another kind (image ↔ document) |
| `media.in_use` | 409 | Delete without `confirmInUse` of a file with any place. The body carries the usage summary. |
| `media.in_live_use` | 403 | Delete or replace of a live-used file without `pages.publishing.manage` |
| `media.conflict` | 409 | The row changed under a replace, move, alternative-text edit or delete |
| `media.alt_text_not_allowed` | 422 | Alternative text on a document |
| `media.invalid_alt_text` | 422 | Longer than 300 characters after cleaning |
| `media.folder_not_found` | 404 | A folder id that is not the site's |
| `media.folder_name_taken` | 409 | |
| `media.invalid_folder_name` | 422 | Empty or longer than 100 characters after cleaning |
| `media.folder_too_deep` | 422 | |
| `media.folder_cycle` | 422 | |
| `media.folder_limit` | 422 | 1,000 folders per site |
| `media.folder_not_empty` | 409 | |
| `media.folder_conflict` | 409 | A concurrent structural change |
| `media.too_many_files` | 422 | A bulk move of more than 100 files |
| `media.invalid_query` | 400 | An unknown `sort`, `kind` or `scope`, `scope=subtree` without `folderId`, or `q` longer than 100 characters |
| `media.storage_check_running` | 409 | A check is already running |
