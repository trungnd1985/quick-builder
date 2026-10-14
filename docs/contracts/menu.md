# Contract: Menu Document, Version 1

**Audience**: anyone who reads or writes menus: the admin's menu editor and Navigation block
preview, the Pages validator, publishing, and the renderer.

**Introduced by**: [specs/024-navigation](../../specs/024-navigation/) (research NV-03, NV-04,
NV-20).

A menu document is the draft, or one published version, of one menu: an ordered tree of
entries. Its shape is [`menu/v1.schema.json`](menu/v1.schema.json). Both validators embed that
file and run the golden examples in [`menu/examples/`](menu/examples/) (§7), so the server and
the admin agree on what a valid menu is (constitution Principle VI). Changing anything here
needs this document, the schema file and the examples in the same pull request.

Pages show a menu through the `navigation` node of the page schema
([page-schema.md](page-schema.md) §2), which names the menu by id.

---

## 1. What a menu document is

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
| `entries` | Array of entries: the top level. May be empty. A new menu starts with `[]`. |

A document is stored as `jsonb`, which keeps its value but not its key order or whitespace.
"Read back unchanged" means **equal as a JSON value**.

## 2. Entry

| Property | Rule | Default |
|---|---|---|
| `id` | `^[A-Za-z0-9_-]{1,64}$`, unique in the document. Stable across saves. | required |
| `label` | String, at most 100 characters, no control characters. An absent or blank label on a `page` or `item` entry uses the page's live title or the item's published name. | absent |
| `target` | One of §3. | required |
| `newTab` | Boolean. Only on `page`, `item` and `url` targets (rule 7). | `false` |
| `hidden` | Boolean. A hidden entry and all its children are not shown on the site. It is the inverse of the switch the admin labels **Shown**. | `false` |
| `children` | Array of entries. Always present. | required |

No other properties are allowed.

## 3. Targets

| `type` | Properties | Notes |
|---|---|---|
| `page` | `pageId`\* (UUID), `anchor` | A page of the menu's site. It may later name a page that no longer exists. A template page (016) is never live as a target. |
| `item` | `collectionId`\* (UUID), `itemId`\* (UUID) | An item of that collection, in the menu's site. It may later name an item that no longer exists. |
| `url` | `href`\* | As a page-schema link (page-schema.md §2a): schemes `https`, `http`, `mailto` and `tel`; never protocol-relative; at most 2,048 characters. |
| `anchor` | `anchor`\* | A name on the current page. |
| `none` | — | A label that groups its children. |

**Anchor names** match `^[A-Za-z][A-Za-z0-9_-]{0,99}$`, without the `#`. They are never checked
against a page. The admin removes a leading `#` the member types before validating.

## 4. Rules beyond the schema

Validation stops at the first failure and reports a `rule` and an RFC 6901 `pointer` into the
submitted document. The checks run in this order, so a given document always fails the same
way:

0. The document is at most **262,144 bytes** of UTF-8 JSON (`document_too_large`, pointer
   empty).
1. Well-formed JSON (`malformed_json`), without an object holding the same property twice
   (`duplicate_property`, at the second one), with a supported `schemaVersion`
   (`unsupported_schema_version`, at `/schemaVersion`; a missing or non-integer version is
   left to the schema).
2. The schema. A failure inside `entries` is `entry_invalid`, at the failing value; a failed
   `oneOf` of a target is reported at the target's `/type`. Any other failure is
   `schema_violation`.
3. Entry ids are unique (`duplicate_entry_id`, at the later entry's `/id`, in document order).
4. At most **300** entries in all (`too_many_entries`, at the 301st entry in document order).
5. At most **3** levels; top-level entries are level 1 (`too_deep`, at the first entry at
   level 4).
6. `url`, `anchor` and `none` entries have a label that is not blank after trimming
   (`label_required`, at the entry).
7. `newTab` is not `true` on `anchor` and `none` entries (`new_tab_not_allowed`, at the
   entry's `/newTab`).
8. **On the server only, last**: every `page` and `item` target that the menu's previous draft
   did not already hold names a page of the menu's site, or an item of the named collection in
   the menu's site (`unknown_target`, at the entry's `/target`). Targets kept from the
   previous draft are not checked again, so a page deleted later never makes the menu
   unsaveable. An unknown id and another site's id are the same failure.

"Document order" is depth first: an entry, then its children, then its next sibling.

## 5. Limits

| Limit | Value |
|---|---|
| Document size | 262,144 bytes |
| Entries per menu | 300 |
| Levels | 3 |
| Entry id length | 64 |
| Label length | 100 |
| Anchor length | 100 |
| Address length | 2,048 |

Like the page schema's, these limits are part of the contract, not operator settings.

## 6. Versioning

As [page-schema.md](page-schema.md) §6:

- **Additive changes stay in version 1**: a new target type, or a new optional property.
- **Breaking changes need a new version**: `vN.schema.json` beside the older file, this
  document with a "Changes from vN-1" section, a forward migration of `pages.menu_drafts` and of
  `pages.published_versions` where `subject_kind = 'menu'`, validators accepting `N`, and a
  renderer that renders every version still stored.
- The renderer lists the menu versions it renders, and `MenuRendererCoverageTests` fails the
  build when `menu/` holds a `vN.schema.json` it does not list.

## 7. Examples (golden files)

Every validator of this schema runs the same files:

- `menu/examples/valid/*.json` must pass.
- `menu/examples/invalid/<rule>--<case>.json` must fail with the rule its name begins with, at
  the pointer given in `menu/examples/invalid/expected.json`.

Rule 8 needs a site and the previous draft, so it is tested by the server's integration tests
rather than by examples. Documents over the size or entry limit are generated by the tests
rather than stored.

| File | Expected |
|---|---|
| `valid/empty.json` | passes: no entries |
| `valid/every-target.json` | passes: one entry of each target type, with and without labels |
| `valid/three-levels.json` | passes: entries at levels 1, 2 and 3 |
| `valid/anchors.json` | passes: an anchor alone, and a page with an anchor |
| `valid/hidden-and-new-tab.json` | passes: `hidden` entries with children; `newTab` on page, item and url |
| `invalid/malformed_json--truncated.json` | `malformed_json` |
| `invalid/duplicate_property--label.json` | `duplicate_property` at the second `label` |
| `invalid/unsupported_schema_version--version-2.json` | `unsupported_schema_version` at `/schemaVersion` |
| `invalid/schema_violation--missing-entries.json` | `schema_violation` at the root |
| `invalid/entry_invalid--javascript-href.json` | at `…/target/href` |
| `invalid/entry_invalid--protocol-relative-href.json` | at `…/target/href` |
| `invalid/entry_invalid--anchor-digit.json` | at `…/target/anchor` |
| `invalid/entry_invalid--anchor-hash.json` | at `…/target/anchor` |
| `invalid/entry_invalid--label-control-char.json` | at `…/label` |
| `invalid/entry_invalid--label-101.json` | at `…/label` |
| `invalid/entry_invalid--unknown-target-type.json` | at `…/target/type` |
| `invalid/entry_invalid--extra-property.json` | at the extra property |
| `invalid/entry_invalid--missing-children.json` | at the entry |
| `invalid/duplicate_entry_id--twice.json` | at the second entry's `/id` |
| `invalid/too_deep--fourth-level.json` | at the level-4 entry |
| `invalid/label_required--url.json` | at the entry |
| `invalid/label_required--anchor-blank.json` | at the entry |
| `invalid/label_required--none.json` | at the entry |
| `invalid/new_tab_not_allowed--none.json` | at `…/newTab` |
