# Contract draft: Menu Document, Version 1

Draft of `docs/contracts/menu.md`, with `docs/contracts/menu/v1.schema.json` and
`docs/contracts/menu/examples/{valid,invalid}/`. Decisions: research NV-03, NV-04, NV-20.
Shared by the admin (menu editor, Navigation block preview) and the server (draft save,
publish, renderer). Both validators embed the same schema file (constitution Principle VI).

## 1. What a menu document is

The draft or a published version of one menu: an ordered tree of entries.

```json
{
  "schemaVersion": 1,
  "entries": [
    { "id": "home", "target": { "type": "page", "pageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" }, "children": [] },
    {
      "id": "services",
      "target": { "type": "page", "pageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10" },
      "children": [
        {
          "id": "hosting",
          "target": { "type": "page", "pageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e11" },
          "children": [
            { "id": "status", "label": "Status", "target": { "type": "url", "href": "https://status.example" }, "newTab": true, "children": [] }
          ]
        }
      ]
    },
    { "id": "latest", "label": "Latest post", "target": { "type": "item", "collectionId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20", "itemId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e21" }, "children": [] },
    { "id": "contact", "target": { "type": "page", "pageId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e12", "anchor": "form" }, "children": [] },
    { "id": "company", "label": "Company", "target": { "type": "none" }, "children": [] }
  ]
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | Integer. This release supports `1` only. Equals the stored `schema_version`. |
| `entries` | Array of entries, the top level. May be empty. |

## 2. Entry

| Property | Rule | Default |
|---|---|---|
| `id` | `^[A-Za-z0-9_-]{1,64}$`, unique in the document. Stable across saves. | required |
| `label` | String, at most 100 characters, no control characters. Trimmed by the editor; an empty or absent label uses the target's title (page) or name (item). | absent |
| `target` | One of §3. | required |
| `newTab` | Boolean. Only on `page`, `item` and `url` targets. | `false` |
| `hidden` | Boolean. A hidden entry and all its children are not shown on the site. It is the inverse of the spec's "shown" (FR-006); the admin labels the switch **Shown**. | `false` |
| `children` | Array of entries. Always present. | required |

No other properties are allowed.

## 3. Targets

| `type` | Properties | Notes |
|---|---|---|
| `page` | `pageId`\* (UUID), `anchor` | A page of the menu's site. It may later name a page that no longer exists. |
| `item` | `collectionId`\* (UUID), `itemId`\* (UUID) | An item of that collection, in the menu's site. It may later name an item that no longer exists. |
| `url` | `href`\* | Same as a page-schema link: schemes `https`, `http`, `mailto`, `tel`; no protocol-relative address (page-schema §2a). |
| `anchor` | `anchor`\* | A name on the current page. |
| `none` | — | A label that groups its children. |

**Anchor names**: `^[A-Za-z][A-Za-z0-9_-]{0,99}$`, without the `#`. Not checked against any
page (FR-007a).

## 4. Rules beyond the schema

Checked in this order. Each failure reports a `rule` and an RFC 6901 `pointer`.

0. The document is at most **256 KiB** of UTF-8 JSON (`document_too_large`).
1. Well-formed JSON (`malformed_json`) without duplicate properties (`duplicate_property`);
   `schemaVersion` supported (`unsupported_schema_version`).
2. The schema (`schema_violation`, or `entry_invalid` under an entry, at the failing value).
3. Entry ids are unique (`duplicate_entry_id`, at the second entry).
4. At most **300** entries in all (`too_many_entries`, at the 301st entry).
5. At most **3** levels; top-level entries are level 1 (`too_deep`, at the first entry at
   level 4).
6. `url`, `anchor` and `none` entries have a non-blank `label` (`label_required`, at the
   entry).
7. `newTab` is not `true` on `anchor` and `none` entries (`new_tab_not_allowed`, at
   `…/newTab`).
8. **Server only, last**: every `page` and `item` target that the menu's previous draft did not
   already hold names a page of the menu's site, or an item of the named collection in the
   menu's site (`unknown_target`, at `…/target`). Targets kept from the previous draft are not
   checked again, so a page deleted later never makes the menu unsaveable.

## 5. Limits

| Limit | Value |
|---|---|
| Document size | 262,144 bytes |
| Entries per menu | 300 |
| Levels | 3 |
| Entry id length | 64 |
| Label length | 100 |
| Anchor length | 100 |

## 6. Versioning

As page-schema §6: additive changes (a new target type, a new optional property) stay in
version 1; breaking changes need `v2.schema.json`, a forward migration of `menu_drafts` and
`published_versions` where `subject_kind = 'menu'`, validators accepting both, and a renderer
that renders every version still stored. `MenuRendererCoverageTests` fails the build when
`menu/` holds a version the renderer does not list.

## 7. Golden examples

`valid/`: empty menu; every target type; three levels; labels absent and present; anchors with
and without a page; `hidden`; `newTab` on each linking type.
`invalid/`: one per rule and per schema failure (fourth level, duplicate id, 301 entries,
missing label on `url`/`anchor`/`none`, `newTab` on `none`, `javascript:` href,
protocol-relative href, anchor starting with a digit or `#`, control character in a label,
unknown target type, extra property). Server-only rule 8 has its own integration test.
