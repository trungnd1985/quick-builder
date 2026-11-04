# Site Archive, format 1

**Since**: 020 · **Definition**: [site-archive/manifest.v1.schema.json](site-archive/manifest.v1.schema.json)
· **Examples**: [site-archive/examples/](site-archive/examples/) and the golden archive
[tests/fixtures/archives/0.2.qbsite](../../tests/fixtures/archives/0.2.qbsite)

A site archive is one file holding everything of one site that an export carries: settings,
pages, shared components, menus, redirects, design tokens, collections, items and media, with
their drafts, published versions and pending schedules
([specs/020-site-export-import](../../specs/020-site-export-import/spec.md)). It is a
versioned contract (Principle VI): a release reads every format it lists in
`ArchiveFormat.SupportedFormats`, and `ManifestSchemaTests` checks that what
`ZipArchiveWriter` writes matches the schema.

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
  line, `\n`-separated, and may end with a final `\n`. An entry with no rows is empty.
- `manifest.json` is written last, because it lists every other entry with its hash.

## 2. `manifest.json`

```json
{
  "format": 1,
  "release": "0.2.0",
  "createdAtUtc": "2026-11-02T16:15:38Z",
  "source": { "siteId": "01a19d0b-71d6-7743-aee1-cf7a6fc49d30", "siteName": "Site A", "enabled": true },
  "parts": [
    { "name": "sites",  "version": 1, "contracts": {} },
    { "name": "media",  "version": 1, "contracts": {} },
    { "name": "design", "version": 1, "contracts": { "design-tokens": 1 } },
    { "name": "cms",    "version": 1, "contracts": { "collection-schema": 1 } },
    { "name": "pages",  "version": 1, "contracts": { "page-schema": 1, "binding-expression": 1, "menu": 1 } }
  ],
  "counts": { "page": 4, "component": 1, "collection": 2, "item": 2, "menu": 1, "redirect": 1,
              "media_file": 3, "media_bytes": 261, "versions": 4, "schedules": 1 },
  "problems": [ { "code": "transfer.media_missing", "kind": "media_file", "id": "1cbdfc7f-be54-4af0-8cc0-ad201aef6e2b" } ],
  "entries": [
    { "name": "parts/sites/settings.jsonl", "length": 198, "sha256": "5c90…9038" },
    { "name": "media/12169d6694ef4c66acf9dfecc6ed9a5b", "length": 70, "sha256": "4977…1581" },
    { "name": "authors.jsonl", "length": 81, "sha256": "971d…612f" }
  ]
}
```

- `contracts` gives the **highest** schema version that any document of the part uses. Each
  document still carries its own `schemaVersion`.
- `counts` holds rows per kind, named as the parts name their kinds, plus `media_bytes`.
- `entries` lists every entry except `manifest.json` itself. Import verifies the list
  against the ZIP's central directory, then the length and SHA-256 of each entry as it reads
  it. Any difference refuses the archive with `archive.integrity`.
- `problems` repeats the export summary's problems, for example missing media bytes. They are
  shown again at check.
- The manifest holds no member names, emails or content apart from `source.siteName`.

## 3. `authors.jsonl`

```json
{"id":"01a19d0b-70ab-71e3-adfa-fbdd45ba17de","displayName":"Test Administrator"}
```

Every user id that appears in any part, with the display name only. On import, a user of
this installation with that id is kept; any other becomes a historical author by name.

## 4. Parts

Each part's entries are defined by the module that owns it, in the order the parts are
written and applied: `sites`, `media`, `design`, `cms`, `pages`
([archive-parts.md](../../specs/020-site-export-import/contracts/archive-parts.md) §3). Every
row carries its original `id`. Documents are embedded as JSON values in the format of their
content contract (page schema, collection schema, menu, design tokens), with their own
`schemaVersion`, exactly as they are stored.

Not carried: members, sessions and grants; form submissions and their logs; editing
sessions; usage and dependency rows, which are rebuilt from the documents; and change logs,
which record what happened on the installation that wrote them.

## 5. Media

`media/<fileId>` holds the original bytes of the file's current content version. Variants
are not carried. A file listed in `parts/media/files.jsonl` with `"bytes": "missing"` has no
media entry.

## 6. Versions and compatibility

| Archive | Result |
|---|---|
| `format` ≤ highest supported | read |
| `format` > highest supported | `transfer.format_too_new` |
| `release` major > current major | `transfer.release_too_new` |
| `release` older than `ArchiveFormat.IsSupportedRelease` accepts | `transfer.release_unsupported` |
| a document's `schemaVersion` > current | `transfer.document_too_new` |
| a document's `schemaVersion` < current | upgraded for validation, stored as exported |

A breaking change to this format raises `format`, and the release that does it keeps a
reader for the previous format and adds a golden archive of it under `tests/fixtures/archives/`.

## 7. Examples

[site-archive/examples/minimal/](site-archive/examples/minimal/) is an archive's entries
written out as files: its `manifest.json` lists the other files with their lengths and
hashes, and zipping the directory gives an archive whose manifest verifies. It holds the
`sites` part only, so it shows the container and manifest, not an importable site; the
golden archive above is a whole one.
