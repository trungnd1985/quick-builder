# Contract: S3-Compatible and Azure Blob Storage Providers

**Feature**: 013 | **Research**: MV-14, MV-18, MV-19, MV-20, MV-21

This extends `specs/003-media-storage/contracts/storage-provider.md`. The interface (§1),
the key grammar (§2), the provider duties (§3) and the conformance suite (§5) are
**unchanged**. This document says how the two new providers meet them, what operators
configure, and the key layout that makes moving between providers a plain copy (FR-037).

---

## 1. Selection

```text
Storage:Provider = Local (default) | S3 | AzureBlob      (case-insensitive)
```

- Exactly one provider is registered.
- An unknown name fails start-up:
  `Storage:Provider 'X' is not supported. Supported providers: Local, S3, AzureBlob.`
- No provider other than the selected one makes network calls (003 §3.4).

---

## 2. Key layout, identical on every provider

Object name = `{KeyPrefix}{StorageKey}`, where `KeyPrefix` is empty for Local.

| Keys | Owner | Meaning |
|---|---|---|
| `{site}/media/{id}` | Media | Original, content version 1 (003) |
| `{site}/media/{id}-v{n}` | Media | Original, content version n ≥ 2 (MV-07) |
| `{site}/media-variants/{id}-v{n}-w{width}` | Media | Variant (MV-14) |

- `{site}` and `{id}` are 32 lower-case hex digits.
- On Local, the object name is the path under `Storage:Local:Path`. Local's own
  `.incoming/` directories and `.probe-*` files are not objects.
- On S3 and Azure, `{KeyPrefix}.quickbuilder-probe/…` objects are health probes and not
  objects.
- **Moving between providers** = copying every object name unchanged, then switching
  `Storage:Provider`. Leave out the provider-private names above. Then read the consistency
  check (§6).

---

## 3. S3-compatible provider (`Storage:Provider = S3`)

### 3.1 Settings

| Key | Required | Default | Notes |
|---|---|---|---|
| `Storage:S3:Bucket` | yes | — | Must exist. It is never created. |
| `Storage:S3:Region` | yes | — | Any value for services that ignore regions |
| `Storage:S3:ServiceUrl` | no | AWS | Absolute `http(s)` URL for MinIO, R2, Ceph and others |
| `Storage:S3:ForcePathStyle` | no | `false` | `true` for most self-hosted services |
| `Storage:S3:KeyPrefix` | no | empty | e.g. `quickbuilder/`. Key-prefix grammar, ending in `/`. |
| `Storage:S3:AccessKeyId` | no | — | With `SecretAccessKey`, or neither |
| `Storage:S3:SecretAccessKey` | no | — | Secret: never logged or echoed |

With no keys, the SDK's ambient chain is used: environment, web identity or instance
profile.

### 3.2 Required service capabilities

The service must support:
- `PutObject`, `GetObject` with `Range`, `HeadObject`, `DeleteObject` and `ListObjectsV2`;
- multipart upload;
- **conditional writes with `If-None-Match: *`** on `PutObject` and
  `CompleteMultipartUpload`.

Start-up verifies the last one (§5).

### 3.3 Behaviour against 003 §1

| Operation | Implementation |
|---|---|
| `WriteAsync` | Spool to a temporary file (at most the upload limit), then `PutObject` (≤ 64 MiB) or multipart with 16 MiB parts, with `If-None-Match: *`. `412` → `StorageKeyExistsException`. Failure or cancellation aborts the multipart upload, so the key does not exist afterwards. |
| `OpenReadAsync` | `HeadObject`, then a seekable ranged-GET stream with a 1 MiB read-ahead. `404` → `null`. |
| `ExistsAsync` | `HeadObject` |
| `DeleteAsync` | `DeleteObject` (idempotent) |
| `ListAsync` | `ListObjectsV2` with the prefix, streamed page by page, `KeyPrefix` stripped, and names outside the key grammar skipped |

- **Checksums**: request and response checksums are `WHEN_REQUIRED`, for compatibility
  with non-AWS services.
- **Retries**: the SDK's standard mode, 3 attempts.
- **Incomplete writes** (003 §3.3): an hourly service aborts multipart uploads older than
  1 hour under `KeyPrefix`. The operations guide also recommends an
  abort-incomplete-multipart lifecycle rule.

### 3.4 Minimum permissions

- `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` and `s3:AbortMultipartUpload` on
  `arn:…:{bucket}/{KeyPrefix}*`;
- `s3:ListBucket` and `s3:ListBucketMultipartUploads` on the bucket, with the prefix
  condition.

---

## 4. Azure Blob provider (`Storage:Provider = AzureBlob`)

### 4.1 Settings

| Key | Required | Notes |
|---|---|---|
| `Storage:AzureBlob:Container` | yes | Must exist. It is never created. |
| `Storage:AzureBlob:ConnectionString` | one of these two | Account key, SAS or Azurite. Secret. |
| `Storage:AzureBlob:AccountUrl` | one of these two | `https://{account}.blob.core.windows.net`. Authenticates with the environment's managed or workload identity. |
| `Storage:AzureBlob:KeyPrefix` | no | As for S3 |

### 4.2 Behaviour against 003 §1

| Operation | Implementation |
|---|---|
| `WriteAsync` | `UploadAsync` with `IfNoneMatch = *` (staged blocks, one commit). `409` or `412` → `StorageKeyExistsException`. Uncommitted blocks are never visible and expire on their own. |
| `OpenReadAsync` | `GetPropertiesAsync` plus a seekable `OpenReadAsync` (1 MiB buffer). `404` → `null`. |
| `ExistsAsync` | `ExistsAsync` |
| `DeleteAsync` | `DeleteIfExistsAsync` |
| `ListAsync` | `GetBlobsAsync(prefix)`, streamed, `KeyPrefix` stripped |

### 4.3 Minimum permissions

The **Storage Blob Data Contributor** role on the container, or a SAS with read, add,
create, write, delete and list permissions.

---

## 5. Start-up validation (both remote providers)

Validation runs before the host listens (`ValidateOnStart`), and each failure names the
key. Messages never contain secret values (FR-033):

1. **Settings**:
   - required keys are present;
   - pairs are complete (S3 keys; Azure `ConnectionString` xor `AccountUrl`);
   - URLs are absolute;
   - `KeyPrefix` follows the grammar.
2. **Reachability and behaviour** (30 s budget), using a probe object:
   1. create it with the create-only condition;
   2. create it again, which **must** be refused as existing. Otherwise:
      `Storage:S3:ServiceUrl: the service does not support conditional writes (If-None-Match), which QuickBuilder requires.`
   3. read it back and compare;
   4. delete it.
   - Errors are mapped to the key at fault:
     - credentials rejected → `…AccessKeyId` or `…ConnectionString`;
     - bucket or container not found → `…Bucket` or `…Container`;
     - access denied → the permission list in §3.4 or §4.3;
     - DNS, TLS or connection failure → `…ServiceUrl` or `…AccountUrl`.

**Readiness** (`/health/ready`): the same probe with a 2 s budget (003 §3.1). It reports
only healthy or unhealthy.

---

## 6. Consistency check (MV-21)

- **What it does**: for each site, it lists `{site}/media/` and reports every library file
  whose current original key is absent. Variants are never reported.
- **When it runs**:
  - automatically in the background after start-up, at most one run at a time across
    instances;
  - through `POST /api/v1/media/storage-check`;
  - with the CLI command `media check-storage`.
- **CLI exit codes**: `0` for none missing, `3` for some missing, `1` for an error.
- **Output**: counts are logged. Ids are listed in the admin screen, the API and the CLI.
  File names never appear.

---

## 7. Conformance

- 003's conformance suite runs **unchanged** against each provider (FR-030, SC-010):
  - Local: a temporary directory;
  - S3: a MinIO container;
  - Azure: an Azurite container.
- Provider-specific suites:
  - S3: spooling and multipart above 64 MiB; aborting on cancellation; `ForcePathStyle`;
    `KeyPrefix` stripping; rejection of a service without conditional writes (an in-process
    proxy in front of MinIO that drops the header); the abandoned-upload clean-up.
  - Azure: `ConnectionString` and `AccountUrl` validation paths; `KeyPrefix`; container
    missing.
  - Both: `SecretRedactionTests`, and `MediaAcrossInstancesTests` (two app instances, one
    bucket).
