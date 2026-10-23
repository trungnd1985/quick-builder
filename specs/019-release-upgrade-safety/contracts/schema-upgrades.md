# Contract: Content Schema Upgrades

**Feature**: 019 · **Requirements**: FR-020 – FR-026 · **Research**: RL-19 · Amends
[page-schema.md](../../../docs/contracts/page-schema.md) §6,
[binding-expression.md](../../../docs/contracts/binding-expression.md) (versioning paragraph)
and [docs/contracts/README.md](../../../docs/contracts/README.md).

## 1. Contracts with a version

| Contract | Stored where | Version field | Chain owner | Current |
|---|---|---|---|---|
| page-schema (pages, components) | `pages` drafts, published versions | `schemaVersion` + `schema_version` column | Pages | 1 |
| binding-expression (inside page documents) | same | `bindingVersion` | Pages (run inside the page chain, after it) | 1 |
| menu | `pages` menu drafts, published menus | `schemaVersion` + column | Pages | 1 |
| design-tokens | `editor` token sets, publications | `schemaVersion` + column | Editor | 1 |
| collection-schema (schemas and item values) | `cms` collections, items | `schemaVersion` + column | Cms | 1 |

Structured data types are a catalogue, not stored documents, and have no chain.

## 2. Step and chain (Shared.Infrastructure `Schemas/`)

```text
interface ISchemaUpgradeStep
  int From { get; }                         // upgrades From → From + 1
  JsonObject Upgrade(JsonObject document)   // pure; must set schemaVersion = From + 1

sealed class SchemaUpgradeChain(string contract, int current, IEnumerable<ISchemaUpgradeStep> steps)
  SchemaRead Read(JsonElement document, int storedVersion)

SchemaRead = Current(JsonElement)                       storedVersion == current
           | Upgraded(JsonElement, int fromVersion)     storedVersion <  current, steps applied
           | TooNew(int storedVersion)                  storedVersion >  current
           | Failed(int storedVersion, string reason)   a step threw, or the result failed the
                                                        contract's validator
```

- The constructor throws when steps are missing or duplicated for `1 … current-1`.
- `Read` never writes and never logs content. The caller logs `schema_upgrade_failed` /
  `schema_too_new` with `{contract, subjectKind, subjectId, storedVersion}` only.
- After `Upgraded`, the owning module validates the result with its current validator; a
  failure becomes `Failed`.

## 3. Read sites

Every place an owning module hands a stored document to anyone else goes through its chain:

| Module | Read paths |
|---|---|
| Pages | `PublishedContentReader` (live pages, components, menus → Rendering), `PageReads` and `ComponentReads` (draft and version reads → admin), menu draft reads, the publish and rollback paths (`PublishingWrites` reads the source content) |
| Editor | design draft reads, `PublishedDesignReader` |
| Cms | `PublishedItemReader` (schemas and item values to Rendering), `CollectionSchemaReader` (schemas to Pages), `CollectionReads.ReadAsync` and `ItemReads.ReadAsync` (the admin) |

Results:

| Result | Rendering | Admin API | Save / publish |
|---|---|---|---|
| Current, Upgraded | renders | returns the current-version document with `schemaVersion = current` | stores at current (FR-022) |
| TooNew | the document is handed over empty, so its page renders no content (012 behaviour); others unaffected | `409 document_too_new` problem, read-only | refused with `409 document_too_new` |
| Failed | as TooNew | `422 document_upgrade_failed` problem | refused until the draft is replaced |

Exceptions:
- **Design tokens**: a published design that is TooNew or Failed falls back to the default
  tokens for rendering, as a site with no published design does, and is logged. A site-wide
  document must not make every page of the site fail.
- **Collections and items**: Rendering leaves out a collection schema or an item whose values
  cannot be used (its lists and item page show nothing of it). `ItemReads.ReadValues` is not a
  read site: it checks request input, which clients always send at the current version.
- **Why empty, not the stored text**: the page renderer reads the version inside the document,
  and an old document that failed to upgrade would still say it is an old, renderable one.

Published history rows are never rewritten (FR-023a). Rolling back to one publishes its
upgraded document at the current version.

`PageRenderer.SupportedSchemaVersions` and `MenuModel.SupportedSchemaVersions` become
`{ current }` — Rendering only ever receives current documents — and their guard tests stay.

## 4. Golden files

```text
docs/contracts/<contract>/upgrades/v<N>-to-v<N+1>/
  <name>.in.json     a document at version N
  <name>.out.json    the expected document at N+1
```

`SchemaUpgradeGoldenTests` runs each step on every `.in.json` and compares with `.out.json`
(JSON equality, property order ignored), then validates `.out.json` against `vN+1.schema.json`.
`SchemaUpgradeCoverageTests` fails when a contract's `current > 1` and any step or golden
folder for `1 … current-1` is missing, or a folder has no pair.

## 5. Raising a version (procedure for future features)

In one pull request (replaces page-schema.md §6 item 3):

1. `vN.schema.json` beside the older files; contract document with "Changes from vN-1".
2. `CurrentVersion` raised; validators accept `N`.
3. An `ISchemaUpgradeStep` with `From = N-1`, registered in the owning module's chain, with
   golden files (§4).
4. A migration relaxing the column's `CHECK (schema_version = N-1)` to
   `schema_version BETWEEN 1 AND N` where such a check exists: `pages.page_drafts`,
   `pages.component_drafts`, `pages.menu_drafts`, `editor.design_token_sets`,
   `cms.collections`. Published version rows (`pages.published_versions`,
   `editor.design_versions`) and item values carry no such check.
5. The admin's TypeScript validator accepts only `N` (the API never sends older versions).
6. A release-note fragment (the tool also lists the raise under "Content schema versions").
7. Never in a patch release (FR-034a).

## 6. Tests in this feature

| Test | Proves |
|---|---|
| `SchemaUpgradeChainTests` (unit) | ordering, missing/duplicate steps, Current/Upgraded/TooNew/Failed, purity (input unchanged) with a test-only three-version contract |
| `SchemaUpgradeCoverageTests`, `SchemaUpgradeGoldenTests` (unit) | §4 rules over `docs/contracts/`; pass today with every contract at 1 |
| `SchemaReadSiteTests` (integration) | a page, component and menu in `pages.published_versions`, a design in `editor.design_versions` and an item's values stored at `current + 1` (no check constraint there): that page fails safely (the design falls back to defaults), others render, the admin answers 409 and refuses to save, the row is unchanged; drafts, token sets and collections (held to version 1 by check constraints) through a test-registered failing chain: 422 and a `schema_upgrade_failed` log with no content; published history rows keep their `schema_version` after a republish |
| `SchemaReadCoverageTests` (architecture) | the read-path types of §3 depend on their module's chain (no read path bypasses it) |
