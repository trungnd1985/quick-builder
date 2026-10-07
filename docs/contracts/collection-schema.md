# Contract: Collection Schema, Version 1

**Audience**: anyone who reads or writes collection schemas or item values: the Cms module
and the admin's schema builder in this feature, then the item editor (#15) and the renderer
and bindings (#16).

**Introduced by**: [specs/014-cms-collections-schema](../../specs/014-cms-collections-schema/)
(research CS-02, CS-03, CS-04).

This document and [`collection-schema/v1.schema.json`](collection-schema/v1.schema.json)
together are the contract. The schema file is the single definition of a valid schema
document and of every item value: the product embeds this exact file
(`QuickBuilder.Cms.Contracts`) and validates against it, and the admin's schema builder
imports the same file. `CollectionSchemaContractTests` fails the build when the embedded
copy and the file differ. Changing either needs this document, the schema file and, for a
new version, the forward migration of stored documents, all in the same pull request
(constitution, Development Workflow).

---

## 1. Two documents

| Document | Stored in | Root of the definition |
|---|---|---|
| **Schema document**: a collection's fields | `cms.collections.schema` | the schema file's root |
| **Item values document**: one item's draft or published values | `cms.items.draft`, `cms.items.published` | `$defs/itemValues` |

Both are JSON objects carrying `schemaVersion`. This release supports `1` only. Stored as
`jsonb`, so "read back unchanged" means equal as a JSON value.

## 2. The schema document

```json
{
  "schemaVersion": 1,
  "titleFieldId": "f1",
  "fields": [
    { "id": "f1", "key": "title", "name": "Title", "help": "", "required": true,
      "type": "text", "multiline": false, "maxLength": 256 },
    { "id": "f2", "key": "body", "name": "Body", "help": "", "required": false, "type": "richText" },
    { "id": "f3", "key": "reading-time", "name": "Reading time", "help": "Minutes", "required": false,
      "type": "number", "decimals": 0, "min": 0 },
    { "id": "f4", "key": "category", "name": "Category", "help": "", "required": false,
      "type": "option", "options": [ { "id": "o1", "label": "News" }, { "id": "o2", "label": "Guides" } ] },
    { "id": "f5", "key": "author", "name": "Author", "help": "", "required": false,
      "type": "reference", "target": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" }
  ]
}
```

| Property | Rule |
|---|---|
| `schemaVersion` | Integer, `1`. |
| `titleFieldId` | The id of a field of type `text` with `multiline: false` and `required: true`. Absent only when there is no such field. |
| `fields` | Array of 0–100 fields, in display order. |

**Common field properties** (every type):

| Property | Rule |
|---|---|
| `id` | Stored: `^f[1-9][0-9]{0,5}$`. Proposal only: may be temporary, `^~[1-9][0-9]{0,2}$` (§6). Opaque to clients. Never changes; never reused within the collection. |
| `key` | `^[a-z][a-z0-9-]{0,63}$`, unique in the collection, not reserved (§4). |
| `name` | 1–100 characters, no control characters. |
| `help` | 0–300 characters. |
| `required` | Boolean. |
| `type` | One of the types below. |

**Types and their settings** (no other properties are allowed):

| `type` | Settings | Defaults offered by the builder |
|---|---|---|
| `text` | `multiline`: bool; `maxLength`: 1–10,000 | single line, 256 (several lines: 5,000) |
| `richText` | none | |
| `number` | `decimals`: 0–6 (0 = whole number); `min`, `max`: optional numbers, `min` ≤ `max` | 0 |
| `boolean` | none | |
| `date` | `withTime`: bool | false |
| `image` | none | |
| `file` | `accept`: `any`, `documents` or `images` | `any` |
| `option` | `options`: 1–200 of `{ id, label }`. `id` is `^o[1-9][0-9]{0,6}$` (temporary `^~[1-9][0-9]{0,2}$`); `label` is 1–100 characters, unique in the field ignoring case. | |
| `reference` | `target`: uuid of a collection of the same site (may be this collection) | |
| `multiReference` | `target`: same as `reference` | |

## 3. The item values document

```json
{ "schemaVersion": 1, "values": { "f1": "Hello", "f3": 7, "f4": "o2",
  "f5": "0192f5a1-0000-7000-8000-000000000001" } }
```

- `values` maps field ids to values. **No key means no value**, and `null` is never valid.
  Keys that are not fields of the collection are invalid.
- Value formats (one `$defs` entry each):

| Field type | Value | `$defs` |
|---|---|---|
| `text` | string; no `\n` or `\r` unless `multiline` | `textValue` |
| `richText` | page schema `rtDoc` (page-schema.md §3, including rule 9: at most 20,000 characters) | checked against page-schema `$defs/rtDoc` |
| `number` | number; integer within ±9,007,199,254,740,991 when `decimals` is 0, otherwise at most `decimals` places | `numberValue` |
| `boolean` | `true` or `false`; no key counts as `false` | `booleanValue` |
| `date` | `"YYYY-MM-DD"`, or with `withTime` `"YYYY-MM-DDTHH:MM:SSZ"` (UTC) | `dateValue`, `dateTimeValue` |
| `image`, `file` | `{ "mediaId": uuid }`, a media file of the same site | `mediaValue` |
| `option` | an option id of the field | `optionValue` |
| `reference` | an item id (uuid) of the target collection | `referenceValue` |
| `multiReference` | 1–100 distinct item ids | `multiReferenceValue` |

- **Well-formed versus valid**: a value is well-formed when it matches its `$defs` entry. It
  is valid when it also meets its field's settings (`maxLength`, `min`, `max`, `accept`,
  required). Stored values are always well-formed. They may be invalid after a settings
  change (§7), and are then reported as needing attention, never rewritten.
- A media or reference value whose file or item no longer exists stays well-formed and is
  reported as missing.

## 4. Reserved keys

`id`, `slug`, `status`, `created-at`, `updated-at`, `published-at`, `collection`,
`site`, `schema-version`. They are kept for item properties that #15 and #16 expose next to
fields.

## 5. Validation order and rules

A schema document is checked in this order, stopping at the first failure. Every failure
has an error code, a `rule` and a JSON Pointer to where it is:

1. **Size**: at most 64 KiB (`cms.proposal_too_large`).
2. **Well-formed JSON** (`malformed_json`), with **no duplicate property names**
   (`duplicate_property`): a parser keeps the last of two equal names, so two readers could
   disagree about the document.
3. **A supported `schemaVersion`** (`unsupported_schema_version`, code
   `cms.unsupported_schema_version`). A missing or malformed one is left to the schema.
4. **The schema file** (`schema_violation`).
5. **The rules below**, by `CollectionSchemaRules` (server) and `collectionSchemaRules.ts`
   (admin), each rule over every field before the next rule.

Every failure but the version uses the code `cms.invalid_schema`.

| `rule` | Meaning |
|---|---|
| `duplicate_key` | Two fields share a key, ignoring case |
| `reserved_key` | A key from §4 |
| `duplicate_field_id` | Two fields share an id |
| `duplicate_option_label` | Two options of one field share a label, ignoring case |
| `duplicate_option_id` | Two options of one field share an id |
| `min_above_max` | `min` greater than `max` |
| `invalid_title_field` | `titleFieldId` names no field, or names one that is not a required single-line text field |
| `missing_title_field` | A stored document has a single-line text field but no `titleFieldId` |
| `temporary_id_in_stored` | A stored document holds a temporary field or option id |

**Proposals** (§6) are normalised before the rules run: when `titleFieldId` is absent and
there is a single-line text field, the first one becomes the title field; the title field is
made required. So `missing_title_field` and the "not required" case of
`invalid_title_field` can only fail for a stored document, and `temporary_id_in_stored`
never fails for a proposal. A proposal that names a missing or unsuitable title field still
fails `invalid_title_field`.

A proposal must also not use one temporary id twice, across its fields and options together
(`duplicate_temporary_id`), because the save returns one map from temporary to permanent ids.

Checks that need the database are made by the server only, after these: an id the
collection never issued (`cms.unknown_field_id`, `cms.unknown_option_id`) and a reference
target that is not a collection of the same site (`cms.unknown_target_collection`)
(research CS-04, CS-11).

## 5a. Examples (golden files)

`collection-schema/examples/valid/` holds documents every validator must accept.
`collection-schema/examples/invalid/` holds documents every validator must refuse, each
named after the rule it breaks; `invalid/expected.json` gives each file's `code`, `rule`
and `pointer`. Both the server (`CollectionSchemaGoldenTests`) and the admin
(`collectionSchema.spec.ts`) run every file and must give exactly those answers (SC-006).
The examples are stored documents.

`collection-schema/examples/items/` holds **item values** examples (added by
[015](../../specs/015-cms-items/), research IT-05): one `schema.json`, one values document per
file, and `expected.json` giving each file's report, the list of `{ fieldId, problem, limit }`
the server (`ItemValueGoldenTests`) and the admin (`itemValues.spec.ts`) must both produce
(015 FR-012). The validators that run them know no media file and no item, so a media or
reference value is always reported missing. Problem codes are those of 015 data-model §2;
`malformed`, `unknown_field`, `unknown_option`, `reference_missing`,
`reference_wrong_collection`, `reference_self`, `duplicate_reference` and
`too_many_references` mark a value that is never stored. These are examples, not a change to
the contract: the version stays 1.

## 6. Proposals

The admin sends a whole proposed schema (research CS-06):
- **New fields and options** carry temporary ids (`~1`, `~2`, …), unique within the
  proposal. `titleFieldId` and fill-in values may refer to them. The server assigns
  permanent ids from the collection's counters and returns `idMap: { "~1": "f9", … }`.
- **Existing fields and options** carry their permanent id. An id the collection has never
  issued, or one whose field has been removed, is refused.
- **Order** is the array order. Moving a field is reordering the array.

## 7. Changing a schema: the rules

Fields are matched **by id** between the stored and proposed schemas. Each change has a rule
(research CS-07):
- **Free**: no value changes and nothing to report, so no preview is shown (FR-030).
  Rename, change of key or help, reorder, option relabel or reorder, loosened settings, and
  an added field without a fill-in value.
- **Reported**: no value is lost, but the member sees a preview with counts before saving
  (FR-023, FR-027, FR-029); no confirmation is needed. Tightened settings (values kept,
  counted as needing attention), required turned on (counted as missing or filled), and an
  added field with a fill-in value (counted as filled).
- **Lossless**: every value is converted and kept.
- **Lossy**: some values are shortened, rounded or cleared. The member must confirm the
  preview.
- **Forbidden**: refused unless no item has a value in the field.

| From → To | Rule | Conversion |
|---|---|---|
| `text` → `richText` | lossless | each line becomes a paragraph; past 500 lines, consecutive lines share paragraphs evenly, separated by hard breaks |
| `richText` → `text` | lossy | block text joined by line breaks (several lines) or spaces (single line); marks, links and lists dropped; shortened to `maxLength` |
| `number` → `text` | lossless | invariant, shortest round-trip, `.` separator, no grouping |
| `text` → `number` | lossy | trimmed, `^-?(0|[1-9][0-9]*)(\.[0-9]+)?$`; extra decimals rounded half away from zero; outside `min`/`max` or the safe range: cleared |
| `date` → `text` | lossless | the stored form |
| `text` → `date` | lossy | `YYYY-MM-DD`; with `withTime`, ISO 8601 with `Z` or offset (to UTC), or a date as midnight UTC; otherwise cleared |
| `option` → `text` | lossless | the label |
| `text` → `option` | lossy | trimmed, case-insensitive label match; otherwise cleared |
| `boolean` → `text` | lossless | `"true"` or `"false"` |
| `image` → `file` | lossless | unchanged |
| `file` → `image` | lossy | non-images cleared; missing files kept |
| `reference` → `multiReference` | lossless | a list of one |
| `multiReference` → `reference` | lossy | the first item kept |
| `decimals` raised | free | unchanged |
| `decimals` lowered | lossy | rounded half away from zero |
| `withTime` false → true | lossless | midnight UTC |
| `withTime` true → false | lossy | the UTC calendar date |
| `multiline` false → true | free | unchanged |
| `multiline` true → false | lossless | lines joined with one space |
| reference `target` changed | forbidden | |
| any other type change | forbidden | |

- **Option removed**: lossy; values choosing it are cleared.
- **Field removed**: lossy when any item has a value; its values are deleted from draft and
  published values.
- **Tightened `maxLength`, `min`, `max`, `accept`**: reported. Values are kept, and those no
  longer valid are counted as needing attention.
- **A converted value that breaks the new settings** is kept and counted as needing
  attention. Only `richText` → `text` shortens.
- **Fill-in values**: when a field is added or turned required, a value may be given for
  `text`, `number`, `boolean`, `date` and `option`. It is written to every item's draft and
  published values that lack one, and must be valid for the field.
- **Draft and published** values are converted the same way, in the same transaction.

## 8. Limits

| Limit | Value |
|---|---|
| Collections per site | 100 |
| Fields per collection | 100 |
| Options per field | 200 |
| Items per `multiReference` value | 100 |
| Proposal size | 64 KiB |
| Name, option label | 100 characters |
| Key, address name | 64 characters |
| Help text | 300 characters |
| Description | 500 characters |

Constants in `QuickBuilder.Cms.Contracts.CollectionSchemaLimits`. Raising a limit is
additive.

## 9. Versioning

- **Additive** (stays version 1): a new field type, a new optional setting with a default
  that matches today's behaviour, a new optional property on a value object (for example
  alternative text on `mediaValue`), or a raised limit. Old documents stay valid, and every
  reader must accept every type in the current file.
- **Breaking** (needs version 2): anything that makes a stored version-1 document invalid or
  changes its meaning. It needs a `v2.schema.json` beside v1 and a forward migration of
  every stored schema and item values document, in the same pull request (constitution,
  Development Workflow). Readers must read every version still stored.
