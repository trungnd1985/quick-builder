# Contract: Structured Data Types, Version 1

**Audience**: the editor's structured data control, the Pages validator and the renderer.
**Decisions**: 017 research SE-18, SE-19.

## 1. Where the mapping lives

A template page's `root.props.seo.structuredData`:

```json
{ "type": "Article",
  "bind": { "headline": { "value": { "field": "f1" } },
            "image": { "value": { "field": "f4" } },
            "authorName": { "value": { "field": "f1", "via": "f9" } } } }
```

- `type` is a name from §3.
- `bind` keys are that type's properties. Each value is a 016 `binding` with scope `"page"` and
  `empty: "fallback"` (fallback: the default in §3, else the property is left out).
- An unknown property key, or a binding of a type the property does not accept, is
  **broken**: the editor marks it, and the renderer leaves the property out. It is never
  refused (binding-expression.md §4).
- On a page that is not a template, the whole mapping is broken (page-schema rule 23).

## 2. Output

- One node in the page's `@graph` ([public-seo.md](./public-seo.md) §4).
- `url` is the item page's canonical address.
- Text is plain text, with rich text converted (research SE-04).
- Dates are ISO 8601 UTC. Images are absolute addresses of the original or the largest
  variant up to 1200 px.
- Empty properties are left out.
- If a **required** property is empty, the node is left out entirely.

## 3. Types

`types.json` is the single source. Both sides load it, and `StructuredDataTypesContractTests`
and `structuredDataTypes.spec.ts` compare it with this table.

### `Article`

| Property | Accepts | Default | Written as |
|---|---|---|---|
| `headline` (required) | text, option, number, date, `slug`, text template | the item's name | `headline` |
| `description` | text, rich text, text template | — | `description` |
| `image` | image | — | `image` |
| `authorName` | text, reference (its name) | — | `author: { "@type": "Person", "name" }` |
| `datePublished` | date, `firstPublishedAt` | `firstPublishedAt` | `datePublished` |
| `dateModified` | date, `lastPublishedAt` | `lastPublishedAt` | `dateModified` |

### `Product`

| Property | Accepts | Default | Written as |
|---|---|---|---|
| `name` (required) | as `headline` | the item's name | `name` |
| `description` | text, rich text, text template | — | `description` |
| `image` | image | — | `image` |
| `brand` | text, option, reference | — | `brand: { "@type": "Brand", "name" }` |
| `sku` | text | — | `sku` |
| `price` | number | — | `offers.price`; without it there is no `offers` |
| `priceCurrency` | text, option | — | `offers.priceCurrency`, only if `^[A-Z]{3}$`, else no `offers` |
| `availability` | boolean | — | `offers.availability`: `https://schema.org/InStock` / `OutOfStock` |

### `Event`

| Property | Accepts | Default | Written as |
|---|---|---|---|
| `name` (required) | as `headline` | the item's name | `name` |
| `description` | text, rich text, text template | — | `description` |
| `image` | image | — | `image` |
| `startDate` (required) | date | — | `startDate` |
| `endDate` | date | — | `endDate` |
| `locationName` | text, reference | — | `location: { "@type": "Place", "name" }` |
| `locationAddress` | text | — | `location.address` (text); a `Place` is written when either is set |

## 4. Block-derived nodes (not mappable)

| Block | Node |
|---|---|
| `embed` showing a video | `VideoObject`: `name` (embed `title`, else page title), `embedUrl`, `thumbnailUrl` (YouTube only) |
| `collection-list` with a live template | `ItemList`: `itemListElement` of `ListItem` with `position` and `url`, in shown order |

Page-level nodes (`BreadcrumbList`, `WebSite`, `Organization`) are in public-seo.md §4.

## 5. Versioning

- **Adding a type**, or an optional property of an existing type, is additive. It needs this
  document, `types.json` and golden examples in one PR. Stored mappings and their output do
  not change (FR-035a).
- **Removing or renaming** a type or property, or changing what it accepts, is breaking. It
  needs a version 2 of this contract and a migration of stored mappings
  (constitution Principle VI).

## 6. Golden examples

Under `structured-data/examples/`:

- `map/*.json`: a collection schema, a template mapping and, for each property, whether its
  binding is broken and why. The editor's control and the renderer both run them, so they mark
  the same bindings.
- `render/*.json`: a mapping, a published item with what it refers to, and the expected node,
  or `null` when it is left out. The renderer runs them; the editor shows no output.

The accept names used in `types.json` and the examples are listed in its `accepts` object:
`text`, `richText`, `option`, `number`, `date`, `boolean`, `image`, `reference`, `slug`,
`firstPublishedAt`, `lastPublishedAt` and `template` (a text template). In the tables of §3,
"rich text" is `richText`, "text template" is `template`, and "as `headline`" accepts
exactly what `headline` accepts.
