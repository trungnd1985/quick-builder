# Contract: Site Archive, format 1

**Feature**: 020 · **Requirements**: FR-001 … FR-005, FR-012, FR-013, FR-038 · **Research**:
ST-02, ST-03, ST-21

**Promoted** to [docs/contracts/site-archive.md](../../../docs/contracts/site-archive.md), which is now the
contract; this draft is kept for the record of the plan. It was to become `docs/contracts/site-archive.md`, with
`docs/contracts/site-archive/manifest.v1.schema.json` and golden archives under
`docs/contracts/site-archive/examples/`, in the implementing PR. It is a versioned contract
(Principle VI): a release reads every format version that it lists in
`SiteArchive.SupportedFormats`.

## 1. Container

- A ZIP file (Zip64 allowed), extension `.qbsite`, media type `application/zip`.
- Entry names are exactly one of these shapes. Anything else refuses the archive with
  `archive.unsafe_path`:

```text
manifest.json
authors.jsonl
parts/<part>/<entry>.jsonl            <part>, <entry>: [a-z0-9-]{1,64}
media/<fileId>                        <fileId>: 32 lower-case hex digits (a uuid "N")
```

- Directory entries, duplicate names, encrypted entries and entries with a compression
  method other than Stored or Deflate are refused.
- JSON entries are UTF-8 without a byte-order mark. A `.jsonl` file has one JSON object per
  line, `\n`-separated, and may end with a final `\n`.

## 2. `manifest.json`

```json
{
  "format": 1,
  "release": "1.4.0",
  "createdAtUtc": "2026-11-01T10:00:00Z",
  "source": {
    "siteId": "6f1c…",
    "siteName": "Acme",
    "enabled": true
  },
  "parts": [
    { "name": "sites",  "version": 1, "contracts": {} },
    { "name": "media",  "version": 1, "contracts": {} },
    { "name": "design", "version": 1, "contracts": { "design-tokens": 1 } },
    { "name": "cms",    "version": 1, "contracts": { "collection-schema": 1 } },
    { "name": "pages",  "version": 1, "contracts": { "page-schema": 1, "binding-expression": 1, "menu": 1 } }
  ],
  "counts": { "pages": 84, "components": 12, "collections": 3, "items": 1240, "menus": 2,
              "redirects": 214, "mediaFiles": 610, "mediaBytes": 1932735283 },
  "problems": [ { "code": "transfer.media_missing", "kind": "media_file", "id": "…" } ],
  "entries": [
    { "name": "authors.jsonl", "length": 1234, "sha256": "…" },
    { "name": "parts/pages/pages.jsonl", "length": 99812, "sha256": "…" },
    { "name": "media/0b9e…", "length": 51234, "sha256": "…" }
  ]
}
```

- `contracts` gives the **highest** schema version that any document of the part uses. Each
  document still carries its own `schemaVersion`.
- `entries` lists every entry except `manifest.json` itself. Import verifies the list
  against the ZIP's central directory, then the length and SHA-256 of each entry as it reads
  it. Any difference refuses the archive with `archive.integrity`.
- `problems` repeats the export summary's problems, for example missing media bytes. They are
  shown again at check.
- The manifest holds no member names, emails or content apart from `source.siteName`.

## 3. `authors.jsonl`

```json
{"id":"…","displayName":"Linh Pham"}
```

Every user id that appears in any part, with the display name only (research ST-14).

## 4. Parts

Each part's entries are defined by the module that owns it in
[archive-parts.md](./archive-parts.md) §3. Every row carries its original `id`. Documents
are embedded as JSON values in the format of their content contract (page schema, collection
schema, menu, design tokens), with their own `schemaVersion`, exactly as they are stored.

## 5. Media

`media/<fileId>` holds the original bytes of the file's current content version. Variants
are not carried. A file listed in `parts/media/files.jsonl` with `"bytes": "missing"` has no
media entry (FR-009).

## 6. Versions and compatibility

| Archive | This release | Result |
|---|---|---|
| `format` ≤ highest supported | | read |
| `format` > highest supported | | `transfer.format_too_new` |
| `release` major > current major | | `transfer.release_too_new` |
| `release` major < current major, not in `SupportedMajors` | | `transfer.release_unsupported` |
| a document's `schemaVersion` > current | | `transfer.document_too_new` |
| a document's `schemaVersion` < current | | upgraded for validation, stored as exported (ST-21) |

A breaking change to this format raises `format`, and the release that does it keeps a
reader for the previous format and adds a golden archive of it.
