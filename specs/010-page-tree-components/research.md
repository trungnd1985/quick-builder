# Research: Pages, Page Tree and Shared Components

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-27

Phase 0 decisions for issue #10. Each is numbered `P-nn` so that the plan, the tasks and
code comments can cite it. The spec had no open `NEEDS CLARIFICATION` items after
`/speckit-clarify` (slugs unique per parent, delete refused while children exist,
Designer builds and Editor writes). This document settles what the spec left to planning.

Starting point, from features 001 to 009:
- Modules Sites, Identity and Media. Each has a `DbContext` in its own schema, the outbox, and
  `IModuleDatabaseProvider`.
- `ISiteOwned`, per-entity query filters against `CurrentSiteId`, `AcrossAllSites(reason)`,
  and `SiteFilterCoverageTests` over every module's model.
- The capability model (002 `contracts/authorization.md`), with the forbidden versus
  not-found rule.
- The shared isolation suite with its coverage guards (009 S-15).
- `RoutePrefixes`: `/api`, `/admin` and `/health` are the administrative surface, and
  `/_media` is reserved on the public surface. Its remarks say the Pages feature must
  refuse slugs that would shadow them.
- An empty `docs/contracts/README.md` that names this directory as the home of the page
  schema.

---

## P-01: A new `Pages` module, with the rich domain model the constitution requires

**Decision**: add `QuickBuilder.Pages.Contracts` and `QuickBuilder.Pages` under
`src/Modules/Pages/`, with the `pages` schema, laid out like Media. Principle I requires
a rich domain model with domain events for Pages, so the rules live in three aggregates
(P-02, P-08, P-11), and handlers only load, call and save.

**Rationale**: the constitution already names `Pages` among the modules. Page structure,
drafts and components change together (a draft references components, a delete needs a
usage check), so they belong to one module and one schema.

**Alternatives considered**:
- *Shared components in the Cms module*: Cms doesn't exist yet. Checking usage would then
  need a cross-module call on every page draft save, and the delete refusal would have
  nothing transactional behind it.
- *Pages inside Sites*: Sites is the scope, not content. Principle I lists them
  separately.

---

## P-02: The page structure of a site is one aggregate, `PageTree`

**Decision**: every structural rule (address uniqueness, no cycles, nesting depth, the
page limit, one home page, sibling order) is enforced by a `PageTree` aggregate that holds
**every page's structural fields** for one site: id, parent, position, slug, title, home
flag, address and version. Drafts are **not** part of it (P-08). A `pages.page_trees`
row per site is the aggregate root. It carries the page count, and its `xmin` is the
aggregate's concurrency token.

**Rationale**:
- The rules are about the whole tree. A move is valid only if no address in the moved
  subtree collides with any other page, and only if the target isn't a descendant. Two
  moves that are each valid alone (A under B, B under A) together create a cycle. No
  index can refuse that, so structural changes to one site must be serialised. Checking
  against a single root row does exactly that, cheaply.
- The tree is small. At the 2,000-page limit, the structural fields are about 2,000 short
  rows (≈ 300 KB). One indexed query loads them well inside SC-004's 2 s, and SC-005's
  listing reads the same rows.
- Different sites never contend: each has its own root row.

**Alternatives considered**:
- *One aggregate per page, with rules checked by queries*: the cycle and collision
  checks become check-then-act across rows. That would need `SERIALIZABLE` transactions or
  explicit locks, meaning more moving parts and retries on unrelated pages.
- *PostgreSQL `ltree`, or a closure table*: these make subtree queries cheap, but they
  don't make concurrent moves safe, and at 2,000 pages loading the whole tree is already
  cheap. That's needless machinery (Principle XI).
- *Nested sets*: every insert rewrites half the tree. Rejected.

---

## P-03: Addresses are stored, computed only by the aggregate, and unique by index

**Decision**:
- Each page row stores its computed `path`. The aggregate recomputes it for every page
  whose address changes (slug change, move, home change) in the same save.
- `UNIQUE (site_id, path)` backs the in-domain check.
- Addresses are always lowercase, because slugs are. So "ignoring letter case" (FR-006)
  holds by construction, with no `citext` needed.

**Address rule** (FR-004, FR-003):
- The home page's address is `/`.
- A top-level page's address is `/slug`.
- A child of the home page is also `/slug`, because the home page contributes nothing.
- Any other page's address is `parent path + "/" + slug`.

**Uniqueness is on addresses, not slugs** (clarification Q1): siblings can't collide,
because they would share an address. A top-level page and a child of the home page can't
share a slug either, because both would be `/slug`. No separate sibling-slug index is
needed.

**Why no in-between collisions**: a single operation changes the addresses of one
subtree only. Every new address starts with the subtree's new prefix, and the domain has
already refused any clash with pages outside the subtree. So no intermediate row order in
the batch update can collide either, and the plain unique index is enough.

**Rationale**:
- Rendering (#12) will map a request path to a page with one indexed lookup. Computing
  addresses at read time would need a recursive query per request.
- The index is the final guard. If the domain check and the aggregate's version were ever
  both bypassed, the database still refuses a duplicate address (SC-002).

**Alternatives considered**:
- *Compute paths on read with a recursive CTE*: that costs a recursive query on every
  public request later. Uniqueness would also be enforceable only in code.

---

## P-04: Slugs, suggestions and reserved addresses

**Decision**:
- **Grammar**: `^[a-z0-9]+(-[a-z0-9]+)*$`, 1–100 characters (FR-005). Typed slugs outside
  the grammar are refused, never rewritten.
- **Suggestion** is client-side, in `src/admin/src/features/pages/slugify.ts`:
  1. Unicode NFD.
  2. Remove combining marks.
  3. Map the letters NFD can't decompose (`đ/Đ → d`, `ø → o`, `ß → ss`, `æ → ae`,
     `œ → oe`, `ł → l`).
  4. Lowercase.
  5. Turn runs of anything else into a single `-`, and trim hyphens.
  6. Cut to 100 characters at a hyphen boundary.
  An empty result gives no suggestion.
- **Reserved first segments**: `api`, `admin` and `health`. These are the administrative
  surface prefixes. With `Hosting:AdminHostNames` empty, which is the default, they answer
  on every site host, so a page there would be shadowed. `_media` can't be produced by
  the grammar (it has an underscore), so every `RoutePrefixes` public-reserved prefix
  that starts with `_` is safe by construction.
- The list is `PageAddresses.ReservedFirstSegments` in `Pages.Contracts` and is written in
  the page schema contract document.
- **Guard**: `ReservedAddressCoverageTests` is an integration test in the Host's test
  set. It asserts that every prefix in `RoutePrefixes.ReservedPrefixes` and
  `PublicReservedPrefixes` is either in the list or not producible by the slug grammar. A
  new platform prefix then fails the build until Pages reserves it.

**Rationale**: the suggestion is a typing convenience. The server's rule is the grammar,
so there's one rule to test, and the admin shell's suggestion can never produce an
address the server would store differently. The Pages module can't reference the Host
(Principle I), which is why the guard is a test rather than a shared constant.

**Alternatives considered**:
- *A server slug-suggestion endpoint*: that's a round trip per keystroke for a pure
  function.
- *Reserve every first segment starting with `_`*: redundant, since the grammar already
  excludes `_`.

---

## P-05: Concurrency: page versions for intent, the tree version for serialisation

**Decision**:
- Every page has an integer `version`. The aggregate increments it whenever that page's
  own fields change: title, slug, parent, position or home flag.
- Every structural request (rename or reslug, move, make home, delete) carries the
  `version` of the page the caller acted on. A mismatch gives `409 pages.out_of_date`
  (FR-010).
- **Moves name an anchor rather than an index**:
  `{ parentId, afterPageId }`, where `afterPageId: null` means first among the siblings.
  If the anchor is no longer a child of `parentId`, the move is refused as out of date.
  So the caller's intent ("after Services") survives concurrent changes to other
  siblings, and a stale view of siblings is refused only when it matters.
- The whole load–change–save runs under the `page_trees` row's `xmin`. If another
  structural change to the site commits first, the attempt is reloaded and retried, up
  to 3 times, with every rule re-checked. This mirrors 009 S-05 (`ConcurrencyRetry`).
  After three lost races, the caller gets `pages.out_of_date`.
- Creating a page carries no version. It is retried the same way.

**Rationale**: the spec asks for two things:
- a stale change must be refused (FR-010), which is what the caller's page version
  checks;
- concurrent changes must never corrupt the tree (SC-002, the edge case of two creates
  at one address), which is what serialising on the tree row ensures.
Keeping them separate avoids refusing a rename of *Contact* just because someone
reordered *Services*.

**Alternatives considered**:
- *One tree-wide version sent by the caller*: any change anywhere in the site would make
  every open tree screen stale. That's a false conflict for the common case of two
  people working on different sections.
- *Position indexes in move requests*: a concurrent insert before the target shifts every
  index, and the page silently lands in the wrong place.

---

## P-06: Sibling order is a dense integer, renumbered per affected group

**Decision**: `position` counts from 0 within a parent, without gaps. An insert, a move or
a delete renumbers only the sibling groups it touches: at most two per operation. There's
no unique index on `(parent, position)`, because renumbering a group row by row would
trip it. The tree row serialises writers (P-05), and `PageTreeInvariantTests` assert
contiguity after every operation.

**Rationale**: order is small data. There are at most a few hundred siblings, typically
under 20. Dense integers are trivial to read, test and render in navigation later.

**Alternatives considered**:
- *Fractional or lexicographic ranks*: they avoid renumbering but need rebalancing and are
  harder to test. That's machinery for a scale the limits rule out.

---

## P-07: The home page keeps a slug in reserve

**Decision**:
- Every page has a slug, including the home page. While a page is the home page, its
  address is `/` and its slug appears in no address (FR-003, as refined in the spec).
- **Make home** (`POST …/pages/{id}/make-home`) moves the flag from the current home page
  to a top-level page. It recomputes both subtrees: the former home's descendants gain
  its slug as a prefix, and the new home's descendants lose theirs. It is refused if any
  resulting address collides or is reserved.
- A page can be created as the home page only when the site has none.
- Deleting the home page is refused unless it is the only page (FR-013).
- `UNIQUE (site_id) WHERE is_home` backs "at most one".

**Rationale**: without a stored slug, demoting the home page would need a slug from
somewhere at that moment. That means an extra field in the make-home request, and an
extra failure mode.

**Spec note**: the original wording of FR-003 ("has no slug") was refined during planning
to "its slug does not appear in any address while it is the home page", so that the spec
and this decision agree. `/speckit-analyze` should see no conflict.

---

## P-08: Drafts are separate aggregates, one per page and one per component

**Decision**:
- `pages.page_drafts` and `pages.component_drafts` hold one row each per page and per
  component. Each row has `schema_version`, `document jsonb`, `updated_at_utc`,
  `updated_by` and `xmin` as its version.
- Both derive from one abstract `Draft` class that is not itself in the EF model, so each
  maps to its own table with its own key and foreign key. (Planned as EF TPC; TPC needs a
  key shared by both types, which two differently-owned tables do not have.)
- A save replaces the whole document (spec assumption) when the caller's `version`
  matches (FR-021).
- Draft saves don't touch the tree row, so writing content never contends with
  reorganising.

**Rationale**:
- A 2,000-page tree must load without 2,000 documents of up to 1 MB each.
- Saving content and changing structure are different people's work
  (clarification Q3), and they shouldn't serialise against each other.
- Principle VII wants draft and published kept apart. Separate draft tables leave room
  for #12 to add `published_*` tables next to them without touching these.

**jsonb and "exactly as saved"**: `jsonb` keeps the value, not the text. Key order and
insignificant whitespace aren't preserved, and duplicate keys aren't allowed, since the
validator refuses them before storage. The contract therefore defines "unchanged"
(FR-020, SC-003) as JSON value equality, and the tests compare parsed values. The
constitution requires `jsonb` for page trees.

**Alternatives considered**:
- *The document as a column on `pages`*: every tree load would drag the documents along,
  or need table splitting and careful projections everywhere.
- *`json` (text) to keep the bytes*: that goes against the technology constraint, and
  nothing downstream depends on key order.

---

## P-09: The page schema contract: one JSON Schema file, a restricted keyword subset, and an in-house validator

**Decision**:
- **The definition**: `docs/contracts/page-schema/v1.schema.json` (JSON Schema 2020-12) is
  the single definition of a valid version-1 document. The Pages module embeds this exact
  file as a resource through a linked `EmbeddedResource`, so the file and what the product
  validates against can't differ.
- **Readable companion**: `docs/contracts/page-schema.md` describes the envelope, the node
  types, the rules beyond the schema, the limits, the reserved segments and the versioning
  policy.
- **Validation order**, stopping at the first failure and reporting its location as a
  JSON Pointer:
  1. **Size** is checked on the raw request body: at most 1 MiB, with UTF-8 bytes.
  2. The document is parsed with `System.Text.Json`, refusing duplicate property names.
  3. **The envelope's `schemaVersion`** is checked, and anything but `1` is refused with
     `pages.unsupported_schema_version`.
  4. **A structural walk** checks depth (at most 32), node count (at most 5,000) and
     node-id uniqueness. It runs before the schema on purpose: it bounds the schema
     check's work on hostile input, and it gives oversized documents their own `rule`
     instead of a generic `schema_violation`.
  5. The document is checked against the schema by an **in-house validator** in
     `Pages/Infrastructure/Schema/`. It interprets only a documented subset of keywords:
     `type`, `properties`, `required`, `additionalProperties: false`, `const`, `enum`,
     `pattern`, `minLength`, `maxLength`, `minimum`, `maximum`, `items`, `maxItems`,
     `format: uuid`, `$ref` to `#/$defs/*`, and `oneOf` discriminated by a `type` const.
  6. The **component rules**, which JSON Schema can't express, are checked in code: no
     `component-ref` in component documents, and references resolving to components of
     the same site (P-11).
- **Guard**: `PageSchemaSubsetTests` fails if the schema file uses a keyword outside the
  subset. That keeps the validator honest as block types are added in #11.
- **Error shape**: `400 pages.invalid_document`, with `pointer` (for example
  `/root/children/3/props/componentId`) and `rule` (for example
  `unknown_type`, `duplicate_node_id`, `props_invalid`, `too_deep`, `too_many_nodes`,
  `unknown_component`) as extensions of the problem details.
- **Build check that doc and schema agree** (FR-022, SC-007): `PageSchemaContractTests`
  - reads `page-schema.md` and asserts that its node-type table lists exactly the node
    types in the schema's `$defs`;
  - asserts that its limits table equals the validator's constants and the schema's
    `maxLength`/`maxItems` values;
  - asserts that its reserved-segment list equals `PageAddresses.ReservedFirstSegments`;
  - runs every example in `docs/contracts/page-schema/examples/valid/` (each must pass)
    and `…/invalid/` (each must fail with the `rule` named in its file name).
  The examples are the golden files Principle X asks for, and the editor (#11) can run
  the same files.
- **TypeScript side**: nothing in this feature validates documents in the browser, since
  there's no editor yet. #11 will validate against the same file, with a standards-based
  validator such as `ajv` (MIT), and justify that dependency in its own plan.

**Rationale**:
- Principle VI wants one definition shared by the editor and the renderer, in two
  languages. A JSON Schema file is the only format both can consume without a code
  generator. Embedding the file itself rules out drift on the .NET side.
- Validation is the gate in front of storage, so it must never accept a document the
  contract refuses. A small interpreter of a declared subset is about 300 lines. It is
  fully unit-tested against the golden files, needs no dependency, and fails the build
  if the schema outgrows it.
- Version 1 is small on purpose. Its node types are only `root` and `component-ref`
  (P-10), so the subset covers it with a wide margin.

**Alternatives considered**:
- *JsonSchema.Net (json-everything)*: complete 2020-12 support, but its package licence
  now requires revenue-generating users to pay a monthly maintenance fee. QuickBuilder
  is redistributed to operators who may earn revenue, so the obligation would pass to
  them. That is incompatible with the licence rule in Principle XI. Rejected.
- *Corvus.Json.Validator (Apache-2.0)*: complete and fast. But runtime validation
  compiles schemas with Roslyn, which adds a heavy dependency and startup cost to the
  image. Its build-time source generator avoids that at the price of a generator in the
  build. **It is the recorded fallback** if #11's block schemas outgrow the subset.
- *NJsonSchema*: draft 4 to 7 only, and it brings Newtonsoft.Json. Rejected.
- *C# node-type classes as the definition, with a generated JSON Schema*: that gives two
  artefacts to keep in step, and the editor would validate against a derived file rather
  than the definition.

---

## P-10: Version 1 of the document

**Decision**: the envelope is

```json
{ "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [] } }
```

- **Nodes**: `{ id, type, props, children }`. `id` matches `^[A-Za-z0-9_-]{1,64}$` and is
  unique in the document. `type` is a known node type. `props` is an object matching the
  type's properties. `children` is an array that is present and empty for types without
  children.
- **Node types in v1**:
  - `root`: exactly once, as the envelope's `root`, never elsewhere. `props: {}`. Any
    children allowed.
  - `component-ref`: `props: { componentId: uuid }`, no children. Allowed only in page
    documents, never in component documents (FR-027). The validator knows which kind of
    document it is checking.
- **No raw HTML** (FR-023): no v1 property holds free text. The contract states that
  block types added later must not define properties that carry markup; rich text is
  structured JSON (Principle IX).
- **Evolving the format**: a new block type with its own props is **additive**. It adds a
  `$defs` entry and a `oneOf` branch, and stays in version 1: old documents stay valid,
  and the renderer must handle every type in the current schema. A change that
  invalidates stored documents needs `schemaVersion: 2`, a new schema file next to v1, and
  a forward migration of stored drafts in the same PR (constitution Development
  Workflow). The contract document states this policy.
- **Empty document**: a new page or component starts with the envelope above, containing
  an empty `children` array (FR-015).

**Rationale**: the spec limits this feature to node types that the structure itself needs
(FR-019). The root gives every document one entry point, and `component-ref` is the only
cross-document link.

---

## P-11: Components, references and usage, enforced by the database

**Decision**:
- `pages.components` stores the component aggregate: id, name, and its version (`xmin`).
  The name is unique per site, ignoring case, through `citext` and
  `UNIQUE (site_id, name)`.
- **Tracking usage**: on every page draft save, the validator collects the distinct
  `componentId`s in the document. The handler then replaces that page's rows in
  `pages.component_usages (site_id, page_id, component_id)` **in the same transaction** as
  the draft.
- **Foreign keys**:
  - `(site_id, component_id) → components (site_id, id) ON DELETE RESTRICT`. It makes
    three things true, under any race:
    1. a reference to a component of another site, or to one that doesn't exist, can't
       be stored (FR-026);
    2. a component in use can't be deleted (FR-028);
    3. a delete that races a save referencing the same component can't leave a dangling
       reference.
  - `(site_id, page_id) → pages ON DELETE CASCADE`.
- **Refusals**: the handler checks first and returns `pages.unknown_component` (400) or
  `pages.component_in_use` (409, with `usageCount`). If a race slips past that check, the
  FK violation is mapped to the same codes. An unknown component and another site's
  component give the same response.
- **What a usage is**: the `…/components/{id}/usages` endpoint lists the pages whose
  **drafts** reference the component (FR-028). Published references arrive with #12.
- Component documents can't contain `component-ref` (FR-027, P-10), so there are no cycles
  to detect.

**Rationale**: a check-then-delete in code alone would lose a race with a concurrent
save. The composite foreign key turns the rule into a fact of the database.

**Alternatives considered**:
- *Scan every page draft on delete*: slow, and still racy.
- *A usage counter on the component*: it drifts, and needs its own concurrency.

---

## P-12: Deleting

**Decision**:
- Delete a page with `DELETE …/pages/{id}?version=n`. It is refused with
  `pages.has_children` (409, with `childCount`) while the page has children
  (clarification Q2), and with `pages.home_page` (409) for the home page unless it's the
  site's only page.
- The delete is permanent, and cascades to the page's draft and usage rows. The remaining
  siblings are renumbered.
- Delete a component with `DELETE …/components/{id}?version=n`. It is refused with
  `pages.component_in_use` while used (P-11).
- Each delete publishes an event (P-14).

**Rationale**: as the spec requires. Trash is #21. The version guards against deleting a
page someone just renamed or moved.

---

## P-13: Capabilities

**Decision**: four site-scoped capabilities, following clarification Q3.

| Capability | Designer | Editor | Endpoints |
|---|---|---|---|
| `pages.content.view` | ✓ | ✓ | read the tree, a page, a draft, components and their usages |
| `pages.structure.manage` | ✓ | — | create, rename or reslug, move, make home, delete pages |
| `pages.page_drafts.edit` | ✓ | ✓ | save a page draft |
| `pages.components.manage` | ✓ | — | create, rename and delete components, and save component drafts |

Administrator holds all four everywhere (002). Every endpoint declares exactly one of
them. The tree and page responses carry a `permissions` object computed per request,
which the admin shell uses to hide controls (the same pattern as 009 `SitePermissions`).
002's `authorization.md` §2 table gains the four rows.

**Rationale**: one capability per kind of work keeps a future per-site role configurable
without renaming anything. Names follow `<module>.<noun>.<verb>`.

---

## P-14: Domain events become integration events through the Pages outbox

**Decision**:
- The aggregates record domain events in memory: `PageTree` records `PageCreated`,
  `PageDeleted`, `PageAddressesChanged` and `HomePageChanged`, and `Component` records
  `ComponentCreated` and `ComponentDeleted`.
- Before `SaveChanges`, `PagesOutbox.AddFrom(aggregate)` translates each one into its
  integration event in `Pages.Contracts.Events` and adds it to the module's outbox, in
  the same transaction (001 FR-015).
- `PageAddressesChanged` carries every `(pageId, oldPath, newPath)` of one operation. That
  gives one event per rename or move, not one per page. At 2,000 pages it stays below
  150 KB of JSON.
- Draft saves publish nothing. They change nothing visible, and #12 will publish on
  publish.
- No handler subscribes in this feature. Each event type is registered with
  `EventTypeRegistration<T>` so that dispatch can deserialise it, as Sites does.

**Rationale**: FR-032 asks for events that later features (rendering, navigation,
redirects in #17) can consume without reading Pages' tables. Old paths are included
because redirects are exactly what #17 needs from them.

**Alternatives considered**:
- *One event per page whose address changes*: a subtree move of 200 pages becomes 200
  outbox rows, and consumers lose the fact that they were one operation.
- *A general domain-event dispatcher in `Shared.Kernel`*: no other module needs one yet
  (Principle XI). The aggregate base class stays internal to Pages.

---

## P-15: Reading and writing on the admin surface: one site-scoping helper

**Decision**:
- Every Pages endpoint is on the admin surface, where `ISiteContext` is empty. So each
  handler reads through `PagesDbContext.ForSite<T>(siteId)`. It is the module's
  **single** `AcrossAllSites` call site, with the reason "Admin operation on a site named
  explicitly in the route (001 FR-011b)", and it immediately applies
  `Where(e => e.SiteId == siteId)`.
- The query filters from `SiteFilterCoverageTests` still exist on every entity. They
  protect any public read added by #12.
- The site's existence and visibility are checked first through `ISiteDirectory` and
  capability declarations, exactly as in Media.

**Rationale**: Media repeats `AcrossAllSites(...) + SiteId ==` in every handler. Across
Pages' 17 handlers, one helper means one reason to review, one dedicated test
(`PagesSiteScopingTests`), and no handler that can forget the `SiteId` predicate.

---

## P-16: HTTP API shape

**Decision**: `/api/v1/pages/sites/{siteId}/…`, following Media's
`/api/v1/<module>/sites/{siteId}/…`. The full contract is in
[contracts/pages-api.yaml](./contracts/pages-api.yaml).
- **The tree comes back flat**: an array ordered depth-first, parent before children and
  siblings by position. Each item has `id`, `parentId`, `position`, `depth`, `title`,
  `slug`, `path`, `isHome`, `childCount` and `version`, plus `permissions`. A flat,
  ordered array renders as a tree without the client recursing, and it is what the
  isolation probe and tests assert against.
- **Structural actions are separate endpoints** (`PUT` page, `POST move`,
  `POST make-home`, `DELETE`). Each one's validation and error codes are then specific,
  and each maps to one aggregate method.
- **Drafts**: `GET`/`PUT …/pages/{id}/draft` and `…/components/{id}/draft`. The request
  body is `{ document, version }`, and the size limit applies to the whole body
  (1 MiB of document plus a small envelope allowance, 1.1 MiB in total).
- The body limit is a per-action `RequestSizeLimit`, not a global change.

---

## P-17: Admin shell

**Decision**:
- **Pages screen**: `/sites/:siteId/pages` (`PageTreeView.vue`). An ARIA `tree` shows the
  flat list, indented by `depth` and expandable, with each page's title, address and
  home badge. For each page the actions are:
  - **Add child page**;
  - **Rename** (title and slug, with the slug suggested from the title until the user
    edits it);
  - **Move up** and **Move down**;
  - **Move to…** (a dialog to pick the parent and the position);
  - **Make home**;
  - **Delete** (confirmation, refused with the child count).
  Every action is reachable with the keyboard, and there is no drag-only path (FR-033).
- **Components screen**: `/sites/:siteId/components` (`ComponentsView.vue`). It lists
  components with a usage count, and offers create, rename and delete. Delete is refused
  with the usage count, and a panel lists the pages using the component.
- **Permissions**: controls follow `permissions`. An Editor sees both screens read-only
  (clarification Q3).
- **Draft content**: not editable here. The page details panel shows the draft's schema
  version and when and by whom it was last saved. #11 adds the editor.
- **Code**: `api/pages.ts` for the client, `features/pages/*` for the screens, and all
  strings in `i18n/en.json`. Tests use vitest with axe and keyboard checks, like 009's
  site screens. `slugify.spec.ts` covers the Vietnamese, German and Nordic cases from
  P-04.

**Rationale**: the spec wants the structure manageable before the editor exists, without
a pointing device. Buttons plus a move dialog are the smallest accessible design.
Drag-and-drop can come later as an extra, never as the only way.

---

## P-18: Migration and upgrade

**Decision**:
- One migration, `InitialPages`, creates the `pages` schema: the tables in
  [data-model.md](./data-model.md), the outbox tables, `citext` (already installed by
  Sites; `CREATE EXTENSION IF NOT EXISTS` is safe), and the migration history table
  `pages.__EFMigrationsHistory`.
- There is no data step. No pages existed before this feature.
- `UpgradeFrom009Tests` upgrades a snapshot of the 009 release, then asserts three things:
  existing sites, host names, grants and media still work; the `pages` schema exists; and
  a page can be created.
- The automatic migration runner picks the module up through its
  `IModuleDatabaseProvider`, with no edit to the host beyond the one-line
  `AddPagesModule` and the application part.

---

## P-19: Tests that prove the success criteria

| Criterion | Test |
|---|---|
| SC-001 (10 pages in under 5 min) | Manual, timed in quickstart §1. The API path is covered by `PageTreeApiTests`. |
| SC-002 (no duplicate address, no cycle, consistent addresses, including under concurrency) | `PageTreeAggregateTests` (unit, including property-style random operation sequences that assert the invariants after each step), `PageTreeConcurrencyTests` (parallel creates at one address; crossed moves A→B and B→A; exactly one wins) |
| SC-003 (every contract rule refused, valid round-trips) | `PageDocumentValidatorTests` over the golden examples, `PageSchemaContractTests`, `DraftApiTests` (JSON value equality after a round trip) |
| SC-004 (200-descendant move in under 2 s) | `PageTreePerformanceTests` in the smoke project, on the reference compose setup |
| SC-005 (2,000-page tree shown in under 2 s) | The same class times the tree endpoint. `pageTreeView.spec.ts` renders 2,000 rows in jsdom as a sanity check |
| SC-006 (isolation) | `PagesIsolationProbe` in the shared suite, and `PagesSiteScopingTests` |
| SC-007 (doc and schema agree) | `PageSchemaContractTests` and `PageSchemaSubsetTests` |
| SC-008 (keyboard, translation) | `pageTreeView.spec.ts` and `componentsView.spec.ts` (axe, keyboard-only journeys, the i18n lint rule) |

Also in scope:
- **Authorization**: `PagesAuthorizationTests` runs every endpoint for each role, and the
  existing `EndpointAuthorizationCoverageTests` picks up the new endpoints automatically.
- **Logging**: `LoggingTests` gains a Pages case. It asserts that no title, slug or
  document text appears in the logs.
- **Smoke test**: gains create page, create child, save draft and read it back (FR-036).
- **Coverage gate**: `QuickBuilder.Pages` joins the per-package list in `ci.yml`. The
  aggregate, the validator and the draft save are critical paths for #12.

---

## P-20: Performance

**Decision**: meet SC-004 and SC-005 by loading and saving the tree in one round trip
each:
- **Load**: one `AsNoTracking` query for the listing (projection ordered by
  `(parent_id, position)`, assembled depth-first in memory). A tracked load of the
  structural columns only is used for writes.
- **Save**: one `SaveChanges` with EF's batched updates for the changed paths. Moving
  200 descendants means about 200 small `UPDATE`s in 1–2 batches, well under 2 s on the
  reference two cores.

**Measurement** runs in the smoke project, like 009's `SiteResolutionPerformanceTests`:
it seeds 2,000 pages, then times the listing, and a move of a 200-descendant subtree, 20
times each, asserting the p95.

---

## New dependencies

None. No NuGet or npm packages are added. JSON Schema validation is in-house (P-09), and
slug suggestion uses the browser's `String.prototype.normalize`.

## Configuration keys introduced

None. The limits (10 levels, 2,000 pages, 1 MiB, 5,000 nodes, depth 32, 100-character
slugs, 1,024-character addresses) are contract constants, documented in
`docs/contracts/page-schema.md`. They aren't operator settings: a document valid on one
installation must be valid on every other, or site export and import (#20) would break.
