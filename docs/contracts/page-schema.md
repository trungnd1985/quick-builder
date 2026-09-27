# Contract: Page Schema, Version 1

**Audience**: anyone who reads or writes page and component content: the Pages module
today, the visual editor (#11) and the renderer (#12) next.

**Introduced by**: [specs/010-page-tree-components](../../specs/010-page-tree-components/)
(research P-09 and P-10).

This document and [`page-schema/v1.schema.json`](page-schema/v1.schema.json) together are
the contract. The schema file is the single definition of a valid document: the product
embeds this exact file and validates against it, and every other validator (the editor's)
must use it too. `PageSchemaContractTests` fails the build when this document and the
schema disagree. Changing either needs this document, the schema file and, for a new
version, the forward migration of stored documents, all in the same pull request
(constitution, Development Workflow).

---

## 1. What a document is

A **content document** is the editable content of one page or one shared component. In
this release only drafts exist; publishing (#12) adds the published copy. Every document is
a JSON object:

```json
{
  "schemaVersion": 1,
  "root": {
    "id": "root",
    "type": "root",
    "props": {},
    "children": [
      { "id": "header", "type": "component-ref", "props": { "componentId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" }, "children": [] }
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
| `id` | String matching `^[A-Za-z0-9_-]{1,64}$`. **Unique within the document.** Stable across saves, so the editor can keep selection and the renderer can track changes. |
| `type` | A node type from §2. |
| `props` | An object matching that type's properties, with no properties beyond those listed. |
| `children` | An array of nodes, always present. Empty for types that take no children. |

A new page or component starts with `root` and an empty `children` array.

A document is stored as `jsonb`, which keeps its value but not its key order or
whitespace. "Read back unchanged" means **equal as a JSON value**.

## 2. Node types

| Type | Props | Children | Allowed in |
|---|---|---|---|
| `root` | none (`{}`) | any node except `root` | page and component documents, only as the envelope's `root` |
| `component-ref` | `componentId`: UUID of a shared component of the **same site** | none | page documents only |

In version 1 a component document can therefore only be an empty `root`. Components get
content once the editor adds block types.

Content block types (text, image, section and so on) arrive with the editor (#11). Each
one is added to this table, and to the schema's `$defs` and `childNode.oneOf`, without
touching the others (constitution Principle VI).

## 3. Rules beyond the schema

The schema file can't express these, so validators check them in code. They are equally
binding.

1. Node `id`s are unique within the document (`duplicate_node_id`).
2. A document holds at most **5,000** nodes, the root included (`too_many_nodes`).
3. Nesting depth is at most **32**, counting `root` as 1 (`too_deep`).
4. The document is at most **1 MiB** of UTF-8 JSON (`pages.document_too_large`).
5. No object has two properties with the same name (`duplicate_property`). Parsers
   disagree about which one wins, so neither is allowed.
6. Every `componentId` names a component of the document's own site (`unknown_component`).
   An unknown id and another site's id are the same failure.
7. A component document contains no `component-ref` (`component_ref_not_allowed`), so
   components never nest.
8. **No raw HTML, anywhere.** No property may carry markup. Rich text, when a block type
   needs it, is structured JSON, and the renderer produces HTML from an allow-list
   (Principle IX). A block type whose props include an HTML string is refused in review.

Validation stops at the first failure and reports a `rule` and a JSON `pointer` (RFC 6901)
into the submitted document. The checks run in this order, so a given document always
fails the same way:

1. size (rule 4);
2. well-formed JSON (`malformed_json`) without duplicate properties (rule 5);
3. `schemaVersion` supported (`unsupported_schema_version`). A missing or non-integer
   version is left to the schema;
4. a structural walk: depth, node count and unique ids (rules 1–3);
5. the schema: `schema_violation`, or more specifically `unknown_type`, `root_misplaced`,
   `props_invalid` or `children_not_allowed`;
6. the component rules (rules 6–7).

Depth and count come before the schema on purpose. They bound the work the schema check
does, and they give an oversized document its own reason rather than a generic one.

## 4. Limits

| Limit | Value |
|---|---|
| Document size | 1,048,576 bytes |
| Nodes per document | 5,000 |
| Node depth | 32 |
| Node id length | 64 |
| Slug length | 100 |
| Address length | 1,024 |
| Page nesting | 10 |
| Pages per site | 2,000 |

The limits are part of the contract, not operator settings: a document valid on one
installation is valid on every installation, which site export and import (#20) rely on.

## 5. Addresses and reserved segments

- A slug matches `^[a-z0-9]+(-[a-z0-9]+)*$`.
- A page's address is its parent's address, then `/`, then its slug. The home page is `/`
  and contributes nothing to its children's addresses; it keeps its slug, unused, so it has
  an address again if another page becomes the home page.
- Addresses are unique per site. They never include the host name or base path; absolute
  addresses come from the site's base URL (009).

**Reserved first segments**: no address may begin with one of these, because the platform
answers there before any page could.

| Segment | Owner |
|---|---|
| `api` | Administrative API |
| `admin` | Admin shell |
| `health` | Health probes |

Platform prefixes the slug grammar can't produce, such as `/_media`, need no entry.
`ReservedAddressCoverageTests` fails the build if the platform gains a prefix that is
neither listed here nor impossible to produce.

## 6. Versioning

- **Additive changes stay in version 1**: a new node type, or a new optional prop on an
  existing type. Documents that were valid stay valid, and the renderer must render every
  type in the current schema.
- **Breaking changes need a new version**: removing or renaming a type or prop, making a
  prop required, or narrowing a value. A breaking change needs all of these in one PR:
  1. `vN.schema.json` beside the older files;
  2. this document updated, with a "Changes from vN-1" section;
  3. a forward migration that rewrites every stored document still at `N-1`;
  4. validators accepting `N`;
  5. the renderer able to render every version still present in the database.
- `schemaVersion` in the document and the `schema_version` column always agree.

## 7. The schema file

[`page-schema/v1.schema.json`](page-schema/v1.schema.json), JSON Schema 2020-12. It uses
only this subset of the vocabulary, which the Pages validator interprets and
`PageSchemaSubsetTests` enforces:

- **Validation**: `type`, `properties`, `required`, `additionalProperties`, `const`, `enum`,
  `pattern`, `minLength`, `maxLength`, `minimum`, `maximum`, `items`, `maxItems`,
  `format: uuid`.
- **Composition**: `$ref` to `#/$defs/…`, and `oneOf` whose branches are told apart by a
  `type` property's `const`.
- **Annotations**: `$schema`, `$id`, `$defs`, `title`, `description`.

A block type that needs more than this is a planning decision for its feature. The
recorded fallback is a complete JSON Schema implementation (010 research P-09).

## 8. Examples (golden files)

Every validator of this schema runs the same files:

- `page-schema/examples/valid/*.json` must pass.
- `page-schema/examples/invalid/<rule>--<case>.json` must fail with the rule its name
  begins with, at the pointer given in `page-schema/examples/invalid/expected.json`.

| File | Expected |
|---|---|
| `valid/empty.json` | passes |
| `valid/header-and-footer.json` | passes (a page document with two `component-ref`s) |
| `invalid/malformed_json--truncated.json` | `malformed_json` |
| `invalid/schema_violation--missing-schema-version.json` | `schema_violation` at the root |
| `invalid/schema_violation--bad-node-id.json` | `schema_violation` at `/root/id` |
| `invalid/unsupported_schema_version--version-2.json` | `unsupported_schema_version` |
| `invalid/unknown_type--paragraph.json` | `unknown_type` at `/root/children/0/type` |
| `invalid/props_invalid--component-id-not-uuid.json` | `props_invalid` at `…/props/componentId` |
| `invalid/props_invalid--extra-prop.json` | `props_invalid` at `…/props/label` |
| `invalid/children_not_allowed--ref-with-child.json` | `children_not_allowed` |
| `invalid/root_misplaced--nested-root.json` | `root_misplaced` |
| `invalid/duplicate_node_id--twice.json` | `duplicate_node_id` at the second occurrence |
| `invalid/duplicate_property--props.json` | `duplicate_property` |

Documents too deep or with too many nodes are generated by the tests rather than stored.
