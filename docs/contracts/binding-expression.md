# Contract: Binding Expressions, Version 1

**Audience**: anyone who reads or writes bindings or collection lists in page and component
documents: the editor, the Pages validator and the renderer.

**Introduced by**: [specs/016-collection-templates-binding](../../specs/016-collection-templates-binding/)
(research TB-04 to TB-07).

This document defines what bindings mean. Their shapes are `$defs` of
[`page-schema/v1.schema.json`](page-schema/v1.schema.json) (`binding`, `bindingPart`,
`valueFormat`, `listSource`, `listFilter`, `listOperand`, `listSort`, `listPaging`, and the
node types `collection-list`, `list-card` and `list-empty`), so the one schema file both
validators embed stays the single definition of a valid document. The rules the schema
cannot state are rules 13–21 of [page-schema.md](page-schema.md) §3. Changing any of it needs
this document, the schema file and the golden examples (§8) in the same pull request
(constitution, Development Workflow).

---

## 1. Where bindings live

- A block binds a property through its optional `props.bind` object, keyed by the property
  name. Only the keys in §4 are allowed for each block type.
- A document that holds any `bind` or `collection-list` carries `"bindingVersion": 1` in its
  envelope, beside `schemaVersion` (rule 13).
- Collections, fields and options are named by their ids (collection-schema.md §2), never by
  key or display name.

```json
{
  "schemaVersion": 1,
  "bindingVersion": 1,
  "root": { "id": "root", "type": "root", "props": {}, "children": [
    { "id": "s1", "type": "section", "props": {}, "children": [
      { "id": "h1", "type": "heading",
        "props": { "text": "Untitled", "level": 1,
                   "bind": { "text": { "value": { "field": "f1" } } } },
        "children": [] },
      { "id": "by", "type": "text",
        "props": { "text": "",
                   "bind": { "text": { "value": { "parts": [
                     "By ", { "field": "f1", "via": "f5" }, ", ",
                     { "field": "f6", "format": { "date": "long" } } ] },
                     "empty": "hide" } } },
        "children": [] }
    ] }
  ] }
}
```

## 2. A binding

```text
binding      = { "value": bindingValue | textTemplate, "empty"?: "fallback" | "hide" }
bindingValue = { ("field": fieldId | "item": builtIn), "via"?: fieldId, "scope"?: scope, "format"?: valueFormat }
textTemplate = { "parts": [ (string | bindingValue), … ] }     1–20 parts, ≤10 bindingValues, strings ≤500 chars
builtIn      = "url" | "slug" | "firstPublishedAt" | "lastPublishedAt"
scope        = "page" | <id of an enclosing collection-list node>
valueFormat  = { "date": "short" | "medium" | "long" | "iso" }
             | { "number": { "decimals": 0–6 } }
             | { "yes": string ≤100, "no": string ≤100 }
```

- **`field` and `item`**: exactly one of them.
- **`via`**: the id of a single **reference** field of the scope's item. The value is then
  read from the referenced item. One step only; `via` cannot be combined with another `via`.
- **`scope`**: when it is omitted, the nearest scope is used. That is the innermost
  enclosing `list-card`, else `"page"`.
- **`empty`**: defaults to `"fallback"`, which shows the property's fixed value from `props`.
  `"hide"` leaves the block out.
- **`textTemplate`**: allowed only for text properties (§4).

## 3. Scopes

| Scope | Item | Exists |
|---|---|---|
| `"page"` | the item whose address was requested | on a template page; anywhere else the binding is broken. Refused in component documents (rule 16). |
| a list id | the item of the current card | inside that list's `list-card` |

## 4. Compatibility: what may fill which property

| Block · prop | Accepts |
|---|---|
| `heading.text`, `text.text`, `button.label`, `image.alt` | text (single or multi-line; line breaks become spaces in `heading`), number (with `number` format), date (with `date` format), option (its label), boolean (with yes and no texts), reference (the referenced item's name), `slug`, `firstPublishedAt`, `lastPublishedAt`; and text templates of these |
| `rich-text.content` | rich text (limited to the field's `formats` and the block's allow-list), multi-line text (each line a paragraph) |
| `image.media` | image |
| `button.link`, `image.link`, `heading.link` | `item: "url"` (this item's page), `item: "url"` with `via` (the referenced item's page), file (the file's address), single-line text holding an `https`, `http`, `mailto` or `tel` address |
| `seo.title`, `seo.description`, `seo.ogTitle`, `seo.ogDescription` (017, on a template's root) | everything `heading.text` accepts, and rich text read as plain text; and text templates of these. The value is one line: whitespace collapsed, and over the field's limit (200 for titles, 500 for descriptions) cut at the last word that fits with `…` added |
| `seo.ogImage` (017) | image |
| `seo.noindex` (017) | boolean: `true` hides the item page; false, empty or broken leaves it shown |

Anything else is **broken**: the editor marks it with a reason (`field_missing`,
`type_mismatch`, `scope_missing`, `via_not_reference`) and the renderer treats it as empty.
A broken binding never stops a document from being saved or published.

Metadata bindings (017 seo-metadata.md §3) live in `root.props.seo.bind`, read the page's item
only, and always fall back when empty: to the field's `seo.fallback` text, then to its default.
On a page that is not a template they are not read (page-schema rule 23).

**Empty values**:
- no value stored;
- an empty string after trimming;
- a rich text with no text and no images (035);
- a reference whose item has no published data or does not exist;
- a media file that does not exist;
- `item: "url"` when the collection has no live template. An item address hidden by a
  static page still links to that address, and the visitor gets the static page (route
  order, FR-005);
- a broken binding.

**Formats**:
- `date`: `short` (10/10/2026), `medium` (10 Oct 2026), `long` (10 October 2026) or `iso`
  (2026-10-10). The examples are for `en`; the output follows the site's default language.
  A date-and-time value also shows the time, in UTC (014's time zone rule), except for
  `iso`, which shows the date only.
- `number`: rounded to `decimals`, with the language's digit grouping.
- A value of another type with a `format` that does not fit it ignores the format.

## 5. The Collection list block

```text
collection-list.props = {
  "source": { "collectionId": uuid } | { "field": fieldId, "scope"?: scope },
  "filters"?: [ listFilter ] (0–10),
  "sort"?: [ { ("field": fieldId | "item": "slug" | "firstPublishedAt" | "lastPublishedAt"), "dir": "asc" | "desc" } ] (0–3),
  "limit"?: 1–100 (default 12; ignored when paging is set),
  "paging"?: { "pageSize": 1–100, "param"?: slug ≤32 (default "page"),
               "layout"?: "numbers" | "steps" (default "numbers") },
  "columns"?: 1–4 (default 3), "gap"?: spaceRef, "layerLabel"?: string
}
collection-list.children = [ list-card, list-empty ]
listFilter = { ("field": fieldId | "item": "slug" | "firstPublishedAt" | "lastPublishedAt"),
               "op": "eq" | "ne" | "contains" | "gt" | "lt" | "empty" | "notEmpty",
               "value"?: literal | { "field": fieldId, "scope"?: scope } | { "current": true } }
```

Operators by field type (anything else makes the filter broken, and a broken filter matches
nothing):

| Type | Operators | Literal |
|---|---|---|
| text | `eq`, `ne`, `contains` (ignoring case and accents), `empty`, `notEmpty` | string ≤500 |
| number | `eq`, `ne`, `gt`, `lt`, `empty`, `notEmpty` | number |
| date | `eq`, `gt`, `lt`, `empty`, `notEmpty` | `YYYY-MM-DD` |
| boolean | `eq` | `true` or `false` |
| option | `eq`, `ne`, `empty`, `notEmpty` | option id |
| reference | `eq`, `ne`, `empty`, `notEmpty` | item id, or a scope value |
| multi-reference | `contains`, `empty`, `notEmpty` | item id, or a scope value |
| rich text, image, file | `empty`, `notEmpty` | |
| `item: "slug"` | `eq`, `ne` | slug |
| `item: "firstPublishedAt"`, `item: "lastPublishedAt"` | `eq`, `gt`, `lt` | `YYYY-MM-DD` |

**Missing values.** A field with no value (absent, `null`, `""`, or `[]` for a
multi-reference):
- matches `empty`, and never `eq`, `contains`, `gt` or `lt`;
- matches `ne`: a post with no category is "not News";
- counts as `false` for a boolean (014 FR-010), so `eq false` matches it.

**Dates.** A `YYYY-MM-DD` literal is compared with the day of the value, read in UTC:
`eq` matches any time that day, `gt` matches from the next day, and `lt` matches before
that day. This holds for date fields with or without a time, and for the built-in
publication dates.

**Paging.** `layout` is how the pager below a paginated list looks (016 FR-027): `numbers`
shows Previous, page numbers and Next; `steps` shows Previous and Next only. Neither is
shown when every item fits on one page. `param` must be unique among the paginated lists of
a page and of the components it uses (rules 19 and 21, page-schema.md §3).

- `{ "current": true }` is allowed only with `ne`, in a list whose source collection is the
  collection of an enclosing scope's item. It means "not that item".
- The `field` source is a **multi-reference** field of the scope's item. It lists the
  referenced items that are published, in stored order, before filters, sort and limit.

## 6. Rules beyond the schema

Rules 13–21 of [page-schema.md](page-schema.md) §3. Two shapes the schema subset cannot say
are checked with them and reported as `props_invalid`: a binding value names exactly one of
`field`, `item` and `parts`; a text-template value part, a filter and a sort key name exactly
one of `field` and `item`; a list `source` names exactly one of `collectionId` and `field`; a
scope operand names exactly one of `field` and `current`.

## 7. Versioning

- This is version 1. Additive changes stay in version 1: a new built-in value, format,
  operator, bindable property or block.
- Removing or renaming any of them, or changing the meaning of `scope` or `empty`, needs
  `bindingVersion: 2` and an upgrade step from 1 with golden files under
  `binding-expression/upgrades/v1-to-v2/`, in one PR. Pages runs it on every stored page
  document when it is read, after the page schema's own chain; documents are stored at the
  new version on their next save or publish (019 contracts/schema-upgrades.md; Principle VI).

## 8. Golden examples

[`binding-expression/examples/`](binding-expression/examples/):
- `valid/*.json`: page documents that every validator accepts;
- `invalid/<rule>--<case>.json`: page documents that every validator refuses with the rule
  and pointer listed in `invalid/expected.json`;
- `resolve/*.json`: a collection schema, items and a document, with the value each bound
  property resolves to.

`PageSchemaBindingTests` and `BindingRulesGoldenTests` (server) and `documentValidator.spec.ts`
and `bindingRules.spec.ts` (admin) run them. Rule 14 needs a site, so it is tested against the
running product rather than here.
