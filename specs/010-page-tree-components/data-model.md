# Data Model: Pages, Page Tree and Shared Components

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Every table lives in the new `pages` schema, which the Pages module owns (001 FR-022).
Every entity is `ISiteOwned` and declares `HasQueryFilter(e => e.SiteId == CurrentSiteId)`.
Admin handlers read through the module's single `ForSite(siteId)` helper (P-15). Ids are
version-7 GUIDs assigned by the domain.

## Aggregates

| Aggregate | Root table | Also owns | Concurrency token |
|---|---|---|---|
| `PageTree` (one per site) | `page_trees` | `pages` (structural fields only) | `page_trees.xmin` for serialisation (P-05); `pages.version` for caller intent |
| `PageDraft` (one per page) | `page_drafts` | none | `page_drafts.xmin` |
| `Component` (many per site) | `components` | none | `components.xmin` |
| `ComponentDraft` (one per component) | `component_drafts` | none | `component_drafts.xmin` |

`component_usages` belongs to no aggregate. It's an index the page-draft save rewrites
in the same transaction (P-11).

---

## `pages.page_trees`: PageTree root

| Column | Type | Null | Notes |
|---|---|---|---|
| `site_id` | `uuid` | no | PK. One row per site that has ever had a page. Created with the site's first page, retried on a PK race (P-05). |
| `page_count` | `integer` | no | `CHECK (page_count BETWEEN 0 AND 2000)`. Kept by the aggregate. |
| `updated_at_utc` | `timestamptz` | no | Set on every structural change, and always marked modified so `xmin` moves (the same device as 009's `MarkAggregateChanged`). |
| `xmin` (system) | `xid` | | Mapped as `uint Version`, `IsRowVersion()`. Never sent to clients. |

## `pages.pages`: a page's structure

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `uuid` | no | PK. Never changes. |
| `site_id` | `uuid` | no | FK → `page_trees.site_id`. |
| `parent_id` | `uuid` | yes | FK → `pages.id` `ON DELETE RESTRICT` (a page with children can't be deleted: P-12). Null means top level. |
| `position` | `integer` | no | `CHECK (position >= 0)`. Dense from 0 within `(site_id, parent_id)` (P-06). |
| `title` | `varchar(200)` | no | Trimmed, 1–200 characters. |
| `slug` | `varchar(100)` | no | `CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')`. Kept while the page is the home page (P-07). |
| `path` | `varchar(1024)` | no | Computed by the aggregate (P-03). `/` for the home page. |
| `depth` | `smallint` | no | `CHECK (depth BETWEEN 1 AND 10)`. Top level is 1. |
| `is_home` | `boolean` | no | Default `false`. |
| `version` | `integer` | no | Starts at 1, and grows when this page's title, slug, parent, position or home flag changes (P-05). |
| `created_at_utc`, `updated_at_utc` | `timestamptz` | no | |
| `created_by`, `updated_by` | `uuid` | no | User ids. No FK: Identity's tables are another module's. |

**Indexes**

| Name | Definition | Purpose |
|---|---|---|
| `ux_pages_site_path` | `UNIQUE (site_id, path)` | FR-006 backstop (SC-002). Also rendering's lookup in #12. |
| `ux_pages_site_home` | `UNIQUE (site_id) WHERE is_home` | At most one home page (FR-003). |
| `ix_pages_site_parent_position` | `(site_id, parent_id, position)` | Tree load order. Not unique, because renumbering rewrites a group row by row (P-06). |

## `pages.page_drafts` and `pages.component_drafts` (EF TPC from abstract `Draft`)

| Column | Type | Null | Notes |
|---|---|---|---|
| `page_id` / `component_id` | `uuid` | no | PK and FK → owner, `ON DELETE CASCADE`. |
| `site_id` | `uuid` | no | |
| `schema_version` | `smallint` | no | `CHECK (schema_version = 1)` in this release. Widened by the migration that introduces v2. Always equal to the document's own `schemaVersion`. |
| `document` | `jsonb` | no | The validated envelope (P-10). At most 1 MiB, checked before storage. |
| `updated_at_utc` | `timestamptz` | no | |
| `updated_by` | `uuid` | no | |
| `xmin` (system) | `xid` | | `uint Version`, sent to clients as `version` (FR-021). |

A draft row is created with its owner, holding the empty document (FR-015).

## `pages.components`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `uuid` | no | PK. |
| `site_id` | `uuid` | no | Unique pair `(site_id, id)` for the composite FK below. |
| `name` | `citext` (≤ 100) | no | Trimmed, 1–100 characters. `UNIQUE (site_id, name)`, ignoring case. |
| `created_at_utc`, `updated_at_utc` | `timestamptz` | no | |
| `created_by`, `updated_by` | `uuid` | no | |
| `xmin` (system) | `xid` | | `uint Version`, sent as `version`. |

## `pages.component_usages`

| Column | Type | Null | Notes |
|---|---|---|---|
| `site_id` | `uuid` | no | |
| `page_id` | `uuid` | no | FK `(site_id, page_id)` → `pages (site_id, id)` `ON DELETE CASCADE`. |
| `component_id` | `uuid` | no | FK `(site_id, component_id)` → `components (site_id, id)` `ON DELETE RESTRICT` (P-11). |

PK `(page_id, component_id)`. Index `(site_id, component_id)` for usage lists and
counts. The FKs make references to other sites' components, and deletion of used
components, impossible at the database level.

## Outbox

`pages.outbox_messages` and `pages.processed_events`, through `modelBuilder.AddOutbox`.

---

## Invariants of `PageTree`

Checked in the aggregate. The ones marked ★ are also backed by the database.

1. ★ Every `path` is unique in the site.
2. ★ At most one page has `is_home`. It has no parent, and its `path` is `/`.
3. Every `path` equals the address rule applied to its ancestors' slugs (P-03).
4. No page is its own ancestor.
5. ★ `depth` ≤ 10, and ★ `page_count` ≤ 2,000.
6. `position`s under each parent are exactly `0..n-1`.
7. ★ The first segment of every path is not in `ReservedFirstSegments` (`api`, `admin`,
   `health`). This is checked in the domain. `ReservedAddressCoverageTests` is the guard
   that keeps the list complete.
8. ★ `length(path)` ≤ 1,024.
9. ★ A page with children isn't deleted (`ON DELETE RESTRICT` on `parent_id`), and nor is
   the home page while other pages exist.

## State: page operations

```text
                 create (structure.manage)
                        │
                        ▼
 ┌────────────────────────────────────────────────┐  make-home   ┌──────────────┐
 │ page (top level or child)                       │ ───────────▶ │ home page "/" │
 │ rename · reslug · move · reorder                │ ◀─────────── │ (top level)   │
 └────────────────────────────────────────────────┘  another page└──────────────┘
                        │                             made home
                        │ delete: refused while it has children,
                        ▼ refused for the home page unless it is the only page
                    (removed; draft and usages cascade)
```

Every arrow checks the caller's `version` and re-validates every invariant. Operations
that change addresses record one `PageAddressesChanged` for the whole operation.

## Draft documents (version 1)

```text
Envelope  { schemaVersion: 1, root: Node }
Node      { id: string, type: string, props: object, children: Node[] }
root           props {}                      children: any node except root
component-ref  props { componentId: uuid }   children: []   (page documents only)
```

Rules beyond the schema, which the validator checks in code:
- node ids are unique within the document;
- at most 5,000 nodes;
- depth at most 32 (root is depth 1);
- at most 1 MiB, measured on the request;
- no duplicate property names;
- every `componentId` names a component of this site.

The full contract is in [contracts/page-schema.md](./contracts/page-schema.md).

## Derived values (never stored)

| Value | Rule |
|---|---|
| `childCount` | The number of pages with this `parent_id`, computed in the listing projection |
| `usageCount` | `count(*)` of `component_usages` for the component |
| `permissions` | Per request, from `IAccessEvaluator.HasCapabilityAsync` for the four capabilities (P-13) |
| Absolute address | Site `BaseUrl` (009) + `path`. Not used in this feature, but documented for #12 |

## Contract types (`QuickBuilder.Pages.Contracts`)

- `PageAddresses`: `ReservedFirstSegments`, `MaxSlugLength = 100`, `MaxPathLength = 1024`,
  `MaxDepth = 10`, `MaxPagesPerSite = 2000`, and `IsValidSlug(string)`.
- `PageSchema`: `CurrentVersion = 1`, `SupportedVersions = {1}`, `MaxDocumentBytes = 1_048_576`,
  `MaxNodes = 5000`, `MaxNodeDepth = 32`, and `OpenSchema(int version)`, which returns the
  embedded `v1.schema.json` stream.
- `IPageDocumentValidator`: `Validate(ReadOnlySpan<byte> utf8, DocumentKind kind) →
  DocumentValidationResult` (success with the referenced component ids, or the failure
  `rule` and `pointer`). Component existence is checked by the caller against its own
  site. #12 uses the same interface.
- The integration events below.

## Integration events (published through the Pages outbox)

| Event type | Payload (besides `Id` and `OccurredAtUtc`) | Published when |
|---|---|---|
| `Pages.PageCreated` | `SiteId`, `PageId`, `ParentId`, `Path`, `ActorUserId` | A page is created |
| `Pages.PageDeleted` | `SiteId`, `PageId`, `Path`, `ActorUserId` | A page is deleted |
| `Pages.PageAddressesChanged` | `SiteId`, `Changes: [{ PageId, OldPath, NewPath }]`, `ActorUserId` | A reslug, move or make-home changes any address. Reorders within a parent change no address and publish nothing. |
| `Pages.HomePageChanged` | `SiteId`, `PageId`, `PreviousPageId?`, `ActorUserId` | A page becomes home: on creation as home, or by make-home |
| `Pages.ComponentCreated` | `SiteId`, `ComponentId`, `ActorUserId` | A component is created |
| `Pages.ComponentDeleted` | `SiteId`, `ComponentId`, `ActorUserId` | A component is deleted |

Payloads carry ids and paths only: no titles, names or content (Principle IX logging
rule, FR-037). The type names are permanent contract names.

## Validation and error codes

Validator failures on a request's own fields are reported together as
`400 request.invalid` with `errors` (001 FR-008). The `pages.*` codes are for rules
checked against stored state.

| Code | Status | When |
|---|---|---|
| `pages.invalid_title` | 400 | Empty or longer than 200 characters after trimming |
| `pages.invalid_slug` | 400 | Outside the grammar, or longer than 100 characters |
| `pages.duplicate_address` | 409 | Any resulting address is already used. `detail` names the first conflicting path. |
| `pages.reserved_address` | 409 | Any resulting address starts with a reserved segment |
| `pages.address_too_long` | 409 | Any resulting address exceeds 1,024 characters |
| `pages.too_deep` | 409 | Any page would sit deeper than level 10 |
| `pages.too_many_pages` | 409 | The site already has 2,000 pages |
| `pages.move_into_descendant` | 409 | The target parent is the page or one of its descendants |
| `pages.parent_not_found` | 404 | `parentId` or `afterPageId` isn't a page of this site |
| `pages.home_page_exists` | 409 | Creating a page as home when the site already has one |
| `pages.home_page_must_be_top_level` | 409 | Make-home on a child page, or moving the home page under a parent |
| `pages.has_children` | 409 | Delete while the page has children. Extension: `childCount`. |
| `pages.home_page` | 409 | Delete of the home page while other pages exist |
| `pages.out_of_date` | 409 | Stale page, draft or component `version`, an anchor that is no longer a sibling, or 3 lost races |
| `pages.not_found` | 404 | No such page on this site, including another site's page |
| `pages.invalid_document` | 400 | The document breaks the contract. Extensions: `rule` and `pointer`. |
| `pages.unsupported_schema_version` | 400 | `schemaVersion` isn't a supported version |
| `pages.document_too_large` | 413 | The body is over the limit (P-16) |
| `pages.unknown_component` | 400 | A `componentId` that isn't a component of this site |
| `pages.invalid_component_name` | 400 | Empty or longer than 100 characters |
| `pages.duplicate_component_name` | 409 | The name is already used on this site, ignoring case |
| `pages.component_in_use` | 409 | Delete while page drafts reference it. Extension: `usageCount`. |
| `pages.component_not_found` | 404 | No such component on this site |
| `sites.not_found` | 404 | An unknown site, or one the caller may not see (002) |
