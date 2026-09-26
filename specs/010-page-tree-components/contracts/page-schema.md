# Contract: Page Schema, Version 1

**Feature**: `010-page-tree-components` | **Audience**: the Pages module now, the editor
(#11) and the renderer (#12) next.

This is the planning copy of `docs/contracts/page-schema.md`. The implementation copies
it there, with the schema below saved as `docs/contracts/page-schema/v1.schema.json` and
the examples under `docs/contracts/page-schema/examples/`. From then on the copy in
`docs/contracts/` is authoritative, and `PageSchemaContractTests` keeps it in step with
the schema file (research P-09). Changing either needs the contract document, the schema
file and, for a new version, the forward migration in the same PR (constitution
Development Workflow).

---

## 1. What a document is

A **content document** is the editable content of one page or one shared component. It is
stored as a draft in this release, and #12 adds the published copy. Every document is a
JSON object:

```json
{
  "schemaVersion": 1,
  "root": {
    "id": "root",
    "type": "root",
    "props": {},
    "children": [
      { "id": "hdr", "type": "component-ref", "props": { "componentId": "0192a3b4-…" }, "children": [] }
    ]
  }
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | Integer. The version this document was written in. This release supports `1` only. |
| `root` | A node of type `root`. |

Every **node** has exactly four properties:

| Property | Rule |
|---|---|
| `id` | String matching `^[A-Za-z0-9_-]{1,64}$`. **Unique within the document.** Stable across saves, so the editor can keep selection and #12 can track changes. |
| `type` | A node type from §2. |
| `props` | An object matching that type's properties, with no properties beyond those listed. |
| `children` | An array of nodes, always present. Empty for types that take no children. |

A new page or component starts with `root` and an empty `children` array.

## 2. Node types

| Type | Props | Children | Allowed in |
|---|---|---|---|
| `root` | none (`{}`) | any node except `root` | page and component documents, only as the envelope's `root` |
| `component-ref` | `componentId`: UUID of a shared component of the **same site** | none | page documents only |

In version 1, therefore, a component document can only be an empty `root`. That's
expected: components get content once #11 adds block types. This feature establishes
their identity, storage, versioning and usage tracking.

Content block types (text, image, section and so on) arrive with the editor (#11). Each
one is added to this table, and to the schema's `$defs` and `oneOf`, without touching the
others (Principle VI).

## 3. Rules beyond the schema

The schema file can't express these, so the validator checks them in code and they are
equally binding:

1. Node `id`s are unique within the document (`duplicate_node_id`).
2. A document holds at most **5,000** nodes (`too_many_nodes`).
3. Nesting depth is at most **32**, counting `root` as 1 (`too_deep`).
4. The document is at most **1 MiB** of UTF-8 JSON (`pages.document_too_large`).
5. No object has two properties with the same name (`duplicate_property`).
6. Every `componentId` names a component of the document's own site
   (`unknown_component`). An unknown id and another site's id are the same failure.
7. A component document contains no `component-ref` (`component_ref_not_allowed`), so
   components never nest.
8. **No raw HTML, anywhere.** No property may carry markup. Rich text, when a block type
   needs it, is structured JSON, and the renderer produces HTML from an allow-list
   (Principle IX). A block type whose props include an HTML string is refused in review.

Validation stops at the first failure and reports `rule` plus a JSON `pointer` into the
submitted document. The checks run in this order, so a given document always fails with
the same rule:
1. size (rule 4);
2. well-formed JSON without duplicate properties (rule 5);
3. `schemaVersion` supported;
4. a structural walk: depth, node count and unique ids (rules 1–3);
5. the schema;
6. the component rules (rules 6–7).

Depth and count come before the schema on purpose. They bound the work the schema check
does, and they give an oversized document its own reason rather than a generic
`schema_violation`.

## 4. Limits

| Limit | Value |
|---|---|
| Document size | 1 MiB (1,048,576 bytes) |
| Nodes per document | 5,000 |
| Node depth | 32 |
| Node id length | 64 |
| Slug length | 100 |
| Address length | 1,024 |
| Page nesting | 10 levels |
| Pages per site | 2,000 |

The limits are part of the contract, not operator settings: a document valid on one
installation is valid on every installation.

## 5. Addresses and reserved segments

- A slug matches `^[a-z0-9]+(-[a-z0-9]+)*$`.
- A page's address is its parent's address, then `/`, then its slug. The home page is
  `/` and contributes nothing to its children's addresses.
- Addresses are unique per site. They never include the host name or base path;
  absolute addresses come from the site's base URL (009).

**Reserved first segments**: no address may begin with one of these, because the
platform answers there before any page could.

| Segment | Owner |
|---|---|
| `api` | Administrative API |
| `admin` | Admin shell |
| `health` | Health probes |

Platform prefixes that the slug grammar can't produce, such as `/_media`, need no entry.
`ReservedAddressCoverageTests` fails the build if the platform gains a prefix that is
neither listed here nor impossible to produce.

## 6. Versioning

- **Additive changes stay in version 1**: a new node type, or a new optional prop on an
  existing type. Documents that were valid stay valid, and the renderer must render every
  type in the current schema.
- **Breaking changes need a new version**: removing or renaming a type or prop, making a
  prop required, or narrowing a value. A breaking change needs all of these in one PR:
  1. `vN.schema.json` beside the older files;
  2. this document updated with a "Changes from vN-1" section;
  3. a forward migration that rewrites every stored document still at `N-1`;
  4. the validator accepting `N`;
  5. the renderer able to render every version still present in the database.
- `schemaVersion` in the document and the `schema_version` column always agree.

## 7. The schema (version 1)

`docs/contracts/page-schema/v1.schema.json`. It uses only the keyword subset the Pages
validator interprets (research P-09), which `PageSchemaSubsetTests` enforces.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://quickbuilder.dev/contracts/page-schema/v1.schema.json",
  "title": "QuickBuilder content document, version 1",
  "type": "object",
  "required": ["schemaVersion", "root"],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": { "const": 1 },
    "root": { "$ref": "#/$defs/root" }
  },
  "$defs": {
    "nodeId": { "type": "string", "pattern": "^[A-Za-z0-9_-]{1,64}$" },
    "root": {
      "type": "object",
      "required": ["id", "type", "props", "children"],
      "additionalProperties": false,
      "properties": {
        "id": { "$ref": "#/$defs/nodeId" },
        "type": { "const": "root" },
        "props": { "type": "object", "additionalProperties": false, "properties": {} },
        "children": { "type": "array", "items": { "$ref": "#/$defs/childNode" } }
      }
    },
    "childNode": {
      "oneOf": [
        { "$ref": "#/$defs/component-ref" }
      ]
    },
    "component-ref": {
      "type": "object",
      "required": ["id", "type", "props", "children"],
      "additionalProperties": false,
      "properties": {
        "id": { "$ref": "#/$defs/nodeId" },
        "type": { "const": "component-ref" },
        "props": {
          "type": "object",
          "required": ["componentId"],
          "additionalProperties": false,
          "properties": { "componentId": { "type": "string", "format": "uuid" } }
        },
        "children": { "type": "array", "maxItems": 0 }
      }
    }
  }
}
```

## 8. Examples (golden files)

`docs/contracts/page-schema/examples/valid/*.json` must pass.
`…/invalid/<rule>--<case>.json` must fail with the `rule` in its name. At least:

| File | Expected |
|---|---|
| `valid/empty.json` | passes |
| `valid/header-and-footer.json` | passes (page document with two `component-ref`s) |
| `invalid/schema_violation--missing-schema-version.json` | `schema_violation` at `` (root) |
| `invalid/unsupported--version-2.json` | `pages.unsupported_schema_version` |
| `invalid/unknown_type--paragraph.json` | `unknown_type` at `/root/children/0/type` |
| `invalid/props_invalid--component-id-not-uuid.json` | `props_invalid` at `/root/children/0/props/componentId` |
| `invalid/props_invalid--extra-prop.json` | `props_invalid` |
| `invalid/children_not_allowed--ref-with-child.json` | `children_not_allowed` |
| `invalid/root_misplaced--nested-root.json` | `root_misplaced` |
| `invalid/duplicate_node_id--twice.json` | `duplicate_node_id` at the second occurrence |
| `invalid/duplicate_property--props.json` | `duplicate_property` |
| `invalid/too_deep--33.json` | `too_deep` (generated by the test, not stored) |
| `invalid/too_many_nodes--5001.json` | `too_many_nodes` (generated by the test, not stored) |

The editor (#11) will run the same files against its own validator, which is what "both
sides validate against the same definition" means in practice.
