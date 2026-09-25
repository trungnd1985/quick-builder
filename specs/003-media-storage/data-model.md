# Data Model: Media Library and Storage Abstraction

**Feature**: `003-media-storage` | **Date**: 2026-09-25

This feature adds one database schema, `media`, with one table plus the module's outbox table, and one set of stored
objects in the configured storage provider. It changes no existing schema.

---

## 1. Schema `media`

Owned by the Media module, with its own `MediaDbContext` and its own migration history
table `media.__EFMigrationsHistory` (001 FR-022). Column names are snake_case through the
shared naming convention.

### Table `media_files`

One row per stored media file. The row exists exactly while the file is in the library.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `uuid` | no | Primary key. A random v4 value from `Guid.NewGuid()`, which uses the OS CSPRNG. It is also the public identifier (research M-11), so it must stay hard to guess. Time-ordered v7 ids are deliberately not used. |
| `site_id` | `uuid` | no | The owning site. It refers to `sites.sites.id` but has **no foreign key**, because no reference may cross module schemas (Principle I). |
| `file_name` | `varchar(255)` | no | The original name, cleaned for display (M-17). Never used in keys or paths. |
| `content_type` | `varchar(64)` | no | One of the six allowed types, detected from content (M-07). |
| `extension` | `varchar(8)` | no | Lower-case and without the dot. It is the extension of `file_name`, validated against `content_type` (M-08), and it appears in the public address. |
| `size_bytes` | `bigint` | no | Exact stored length. Check: `size_bytes > 0`. |
| `uploaded_at_utc` | `timestamptz` | no | When the row was committed, which is also when the file became public. |
| `uploaded_by` | `uuid` | no | The uploading user's id. Names are resolved through `IUserDirectory` (002). Accounts are never deleted, so the id always resolves. No foreign key, because the account lives in another module. |
| `storage_key` | `varchar(200)` | no | `{site_id:N}/media/{id:N}` (M-03). Stored rather than derived, so a future key layout change needs no data migration and the orphan sweep compares keys directly. |

**Constraints and indexes**

| Name | Definition | Serves |
|---|---|---|
| `pk_media_files` | `PRIMARY KEY (id)` | Public serving and preview lookups (M-14) |
| `ux_media_files_storage_key` | `UNIQUE (storage_key)` | Orphan sweep batch lookups (M-15); one row per stored object |
| `ix_media_files_site_listing` | `(site_id, uploaded_at_utc DESC, id DESC)` | Keyset listing and per-site count (M-16, SC-009a) |
| `ck_media_files_size_positive` | `CHECK (size_bytes > 0)` | FR-008: empty files are never recorded |
| `ck_media_files_extension` | `CHECK (extension ~ '^[a-z0-9]{1,8}$')` | The public path segment is always safe |

**Site ownership**: the entity implements `ISiteOwned`. `MediaDbContext` declares its
query filter as `SiteId == CurrentSiteId`, reading the ambient site through a context
property (001's `SiteQueryFilter` guidance). The consequences:
- On the public surface, a lookup by id can only ever return the resolved site's file
  (FR-014).
- On the admin surface there is no ambient site, so every admin read calls
  `AcrossAllSites("<reason>")` and then filters by the `{siteId}` from the route,
  exactly as `Sites` does.
- The orphan sweep reads across sites with its own written reason.
- `SiteFilterCoverageTests` picks up the new entity automatically.

**Why there is no status column**: a row means "complete, public and in the library".
- An upload in progress has no row yet: its bytes are written first (M-04, M-11).
- A deleted file has no row any more (M-13).
- Deletion is permanent, and files are public from the start (spec clarifications).

So none of the usual states (pending, private, deleted) exists.

### Lifecycle

```text
         bytes written atomically          row committed                 row deleted
(none) ─────────────────────────▶ orphan ─────────────────▶ in library ───────────────▶ orphan ──▶ (none)
                                    │     (upload handler)   public, listed  (delete)    bytes purged after
                                    │                                                     commit, or by sweep
                                    └── insert failed or crashed: swept after the grace period (FR-012)
```

"Orphan" means bytes in storage with no row. It is never visible: nothing serves or lists
bytes without a row.

## 2. Stored objects

| Aspect | Rule |
|---|---|
| Key | `{siteId:N}/media/{fileId:N}`. Lower-case hex and fixed segments only, under the grammar in [contracts/storage-provider.md](./contracts/storage-provider.md). |
| Content | Exactly the uploaded bytes, never modified (FR-005). |
| Local layout | `{Storage:Local:Path}/{siteId:N}/media/{fileId:N}`, mode `0640`, no extension. Incomplete writes live only under `{Storage:Local:Path}/{siteId:N}/.incoming/`, namespaced by site like the files (M-04). |
| Backup unit | A site is the prefix `{siteId:N}/`. The installation is the whole storage location plus the database (FR-029). |

## 3. Types in shared projects

### `QuickBuilder.Shared.Infrastructure/Storage/`

| Type | Kind | Purpose |
|---|---|---|
| `IFileStorage` | interface | The abstraction: write, open for read, check existence, delete, list (M-02) |
| `StorageKey` | readonly record struct | A validated key. Construction throws on grammar violations (M-03). |
| `StoredFile` | sealed record | The result of `OpenReadAsync`: a seekable `Stream`, `Length` and `LastModifiedUtc`. Disposable. |
| `StoredFileInfo` | sealed record | One `ListAsync` entry: `Key`, `Length`, `LastModifiedUtc`. |
| `StorageKeyExistsException` | exception | Thrown by `WriteAsync` for an existing key (create-only). |
| `IFileStorageHealthCheck` | interface | The readiness check each provider supplies, separate from `IFileStorage` (M-22). |
| `StorageOptions` | options | `Storage:Provider` and `Storage:Local:Path`. |
| `LocalFileStorage` | internal sealed class | The local file-system provider. |
| `LocalStorageOptionsValidator` | internal | Startup validation (M-05). |
| `LocalIncomingCleanupService` | internal hosted service | Removes stale `{siteId}/.incoming/*.part` files, one site directory at a time (M-04). |

The kernel gains nothing. The abstraction is infrastructure, not a domain concept.

## 4. Media module types

### Contracts (`QuickBuilder.Media.Contracts`)

| Type | Purpose |
|---|---|
| `MediaFileDto` | The public shape of a library entry. Fields are listed in [contracts/media-api.yaml](./contracts/media-api.yaml). |

No read interface for other modules is added yet. Pages will add one when it needs to
resolve media references (Principle XI).

### Implementation (`QuickBuilder.Media`)

| Area | Types |
|---|---|
| Domain | `MediaFile` (entity, `ISiteOwned`), `MediaFileType` (the six allowed types with their signatures, canonical MIME types and extensions), `MediaFileName` (the cleaning rules in M-17) |
| Infrastructure | `MediaDbContext` and `Migrations/`, `MediaStorageKeys`, `FileTypeDetector`, `MediaResponseHeaders` (M-11, M-12), `MediaOrphanSweepService` (M-15), `UploadSizeLimitAttribute` (M-09), `MediaErrorResults` |
| Root | `MediaModule` (`AddMediaModule()`), `MediaCapabilities`, `MediaOptions` |
| Features | `UploadFile/`, `ListFiles/`, `GetFileContent/` (preview), `DeleteFile/`, `ServePublicFile/` |

### Validation rules, by source requirement

| Rule | Requirement | Where |
|---|---|---|
| Type is detected from the leading bytes and must be in `Media:AllowedTypes` | FR-006, FR-007 | `FileTypeDetector`, upload handler |
| Extension and declared type agree with the detected type | FR-006 | `MediaFileType.Accepts(extension, declaredType)` |
| 0 < size ≤ `Media:MaxFileSizeBytes`, enforced while reading | FR-008 | `UploadSizeLimitAttribute` plus a counting guard |
| File name is present after cleaning, and ≤ 255 characters | spec edge cases | `MediaFileName.Clean` |
| Row is inserted only after `WriteAsync` returns | FR-011 | Upload handler |
| Deleting someone else's file requires `media.files.delete` | FR-025, clarification Q4 | Delete handler |

### Capabilities

These are registered in `AddMediaModule()`. Their names and role mapping are in research
M-18.

## 5. Configuration

`MediaOptions` (section `Media`) and `StorageOptions` (section `Storage`). Keys, defaults
and validation are in [research.md → Configuration keys introduced](./research.md#configuration-keys-introduced).

## 6. Migration

One migration, `InitialMedia`, creates schema `media`, table `media_files`, the module's outbox table `media.outbox_messages` (001 FR-015; every module context carries it) and the
constraints and indexes above. It is additive and forward-only, and it is safe to re-run
through EF's history table. It touches no other schema. `UpgradeFrom002Tests` restores a
002 snapshot, applies it, and asserts:
- every site, account and grant is unchanged;
- every site's library is empty.
