# Resolve cases

Each `*.json` here is one case for every implementation of binding resolution
(binding-expression.md §8): the server's `BindingResolver` and the editor's `resolve.ts`.

```text
{
  "description": what the case shows,
  "language": the site language for dates and numbers,
  "basePath": the installation's base path,
  "schemas": { collectionId: collection schema document },
  "pageItem": the template page's item, or null outside a template,
  "items": [ items reachable through references ],
  "templates": { collectionId: live template pattern },
  "media": { mediaId: public path } — files that exist,
  "scopes": [ { "name": list id, "item": item id } ] — the cards open around the nodes, outermost first,
  "document": a page document,
  "expected": { "<nodeId>.<prop>": outcome }
}
```

An item is `{ id, collectionId, slug, name, firstPublishedAt, lastPublishedAt, values }`, where
`values` is a published item values document. An outcome is one of:

- `{ "text": "…" }`, `{ "richText": { … } }`, `{ "media": mediaId }` or `{ "link": href }`: the
  binding gives that value;
- `{ "fallback": true }`: nothing to show, so the block's own value is used;
- `{ "hide": true }`: nothing to show, and the block is left out;

either of the last two with `"broken": reason` when the binding is broken rather than empty.
