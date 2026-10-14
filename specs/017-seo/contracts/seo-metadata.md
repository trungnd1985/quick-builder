# Page Schema and Binding Additions: Search Metadata

**Draft of** the changes to `docs/contracts/page-schema.md` (§2, §3, §6),
`page-schema/v1.schema.json` and `docs/contracts/binding-expression.md` (§3, §4). Everything
is additive, so the schema stays at version 1 (page-schema.md §6). **Decisions**: research
SE-02 to SE-05.

## 1. `root` gains an optional `seo` prop (page-schema §2)

| Node type | Props |
|---|---|
| `root` | `seo` (optional; page documents only, rule 22) |

```json
{ "schemaVersion": 1, "bindingVersion": 1,
  "root": { "id": "root", "type": "root",
    "props": { "seo": {
      "title": "Hello",
      "description": "…",
      "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" },
      "canonical": { "type": "page", "pageId": "0192f5a0-0000-7000-8000-000000000001" },
      "noindex": false, "noSitemap": false,
      "bind": { "title": { "value": { "parts": [ { "field": "f1" }, " | Acme blog" ] } },
                "description": { "value": { "field": "f2" } },
                "noindex": { "value": { "field": "f7" } } },
      "fallback": { "description": "Stories from Acme." },
      "structuredData": { "type": "Article", "bind": { "image": { "value": { "field": "f4" } } } }
    } },
    "children": [] } }
```

The `$defs` added to the schema file are `seo`, `seoBind`, `seoFallback` and
`structuredDataMapping`. Their shapes and limits are in data-model.md §2.

## 2. Rules (page-schema §3)

| # | Code | Rule |
|---|---|---|
| 22 | `seo_in_component` | A component document's `root.props` has no `seo`. |
| 23 | — | On a page that is not a template, `seo.bind` and `seo.structuredData` are broken bindings, shown as empty. They are kept and not refused. |
| 24 | (schema) | A `url` canonical is absolute `http` or `https`, with no user info: the `seoCanonical` pattern, so a failure is `props_invalid` at `…/canonical/href`. |
| 25 | (schema) | `title`, `description`, `ogTitle`, `ogDescription` and `fallback` texts contain no control characters: a schema pattern, so a failure is `props_invalid` at the text. |

Rule 13 applies: a document with any `seo.bind` or `seo.structuredData.bind` carries
`bindingVersion: 1`.

**Server-only rule** (not part of the schema, SE-03): changes to `seo.canonical`,
`seo.noindex` and `seo.bind.noindex` by a member without `pages.structure.manage` are
refused with `pages.seo_restricted`.

## 3. Binding additions (binding-expression §4)

| Field · prop | Accepts |
|---|---|
| `seo.title`, `seo.description`, `seo.ogTitle`, `seo.ogDescription` | everything `heading.text` accepts, rich text (as plain text), and text templates of these |
| `seo.ogImage` | image |
| `seo.noindex` | boolean (true hides the item page) |
| `seo.structuredData.bind.*` | per property, [structured-data.md](./structured-data.md) §3 |

- **`empty`** is always `"fallback"`: the field's `seo.fallback` text, then its default
  (research SE-05).
- **Scope**: only `"page"` exists for metadata. List scopes are not reachable from `root`.
- **Values** (research SE-04):
  - whitespace is collapsed;
  - rich text becomes plain text;
  - a value over the field's maximum is cut at a word boundary, with `…` added.

## 4. Changes from earlier in version 1

**Additions in 017 (still version 1)**:
- the optional `seo` prop on `root`, and rules 22–25;
- the binding rows above;
- the structured data mapping, with its own contract, structured-data.md.

Every document valid before stays valid and renders the same body (017 `UpgradeFrom016Tests`).

## 5. Golden examples

Listed in `docs/contracts/page-schema.md` §8 (`valid/seo-*.json`, `invalid/*--seo-*.json`). The
component rule (22) and the shapes no file can show run in `SeoSchemaGoldenTests` and
`seoSchema.spec.ts`. Resolved metadata values are in `binding-expression/examples/resolve/seo-*.json`
(US4).
