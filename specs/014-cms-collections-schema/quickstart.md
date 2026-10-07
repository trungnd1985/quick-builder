# Quickstart & Validation Guide

**Feature**: `014-cms-collections-schema` | **Date**: 2026-10-07

How to check, end to end, that collections and the schema builder work, and how to verify
each success criterion. This guide covers validation only:
- tables, rules, events and error codes are in [data-model.md](./data-model.md);
- endpoints are in [contracts/cms-api.yaml](./contracts/cms-api.yaml);
- the schema and value formats, change rules and limits are in
  [contracts/collection-schema.md](./contracts/collection-schema.md);
- projects, capabilities and events are in
  [contracts/module-surface.md](./contracts/module-surface.md);
- decisions are numbered `CS-xx` in [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation and sites**: as in 013's quickstart: site `$S` (`site-a.test`) and site
  `$S2` (`site-b.test`), with cookies `$COOKIE` (Administrator), `$DESIGNER` and `$EDITOR`.
  The Designer and the Editor are granted on `$S` only.
- **Shorthands**: `$API=http://127.0.0.1:8080/api/v1/cms/sites`. `H` stands for
  `-H "X-QuickBuilder-Request: 1"`.
- **Items**: there is no item editor until #15. Fill a collection with
  `./qb.ps1 seed-items -Collection <slug> -Count <n> [-Site name]` (CS-19). It fills about
  70 % of optional fields with values of the right type.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  dotnet test tests/QuickBuilder.SmokeTests --filter Category=Performance   # SC-003, on the reference setup
  ```

---

## 1. Define a collection (US1, SC-001, SC-006)

1. **SC-001**: as the Designer, with a stopwatch, open **Collections → New collection**.
   Name it "Blog posts" and check that the address name `blog-posts` is suggested. Add Title
   (text), Summary (text, several lines), Body (rich text), Cover image (image), Published
   on (date), Featured (boolean), Reading time (number, whole) and Category (option: News,
   Guides, Releases), then save. **Expect** under 5 minutes, and Title is shown as the field
   that names items.
2. Reload. **Expect** the same fields in the same order, with ids `f1` … `f8` in
   `GET $API/$S/collections/{id}`, revision 2 (1 was the creation), and options `o1` … `o3`.
3. Add an Author reference to a second collection "People", and a self-reference "Related"
   (multi-reference to Blog posts). **Expect** both saved.
4. **Invalid proposals**: `PUT` with two fields keyed `summary`, a key `slug`, an
   option field with no options, `min` 10 and `max` 5, and a reference to a `$S2`
   collection. **Expect** 400 with `rule` `duplicate_key`, `reserved_key`, a schema
   violation at `/fields/n/options`, `min_above_max`, and `cms.unknown_target_collection`
   respectively, each with a pointer to the field.
5. **Roles**: as the Editor, open the builder. **Expect** it read-only, and `PUT`, `POST`
   and `DELETE` answer 403. With a user that has no role on `$S`, **expect** 404 on every
   endpoint.
6. **SC-006**: `dotnet test --filter CollectionSchemaGoldenTests` and
   `npm test -- collectionSchema`. **Expect** both to pass on every file in
   `docs/contracts/collection-schema/examples/`, with the same verdict, rule and pointer.

## 2. Rename and reorder (US2, SC-002)

1. `./qb.ps1 seed-items -Collection blog-posts -Count 300`.
2. Export the item data:
   `psql … -c "select id, draft, published from cms.items order by id" > before.txt`.
3. In the builder:
   - rename Summary to Excerpt, with key `excerpt`;
   - move Cover image above Body with the keyboard (Alt+↑);
   - rename the collection to "Articles" with slug `articles`;
   - relabel option "News" to "Company news".
   Save. **Expect** no preview dialog (FR-030), and revision +1.
4. Export again to `after.txt`. **Expect** `diff before.txt after.txt` to be empty, and every
   field id to be unchanged.
5. Add a new field with key `summary`. **Expect** id `f11` or later (never `f2`), and no item
   gains a value.

## 3. Add and remove after items exist (US3)

1. Add an optional Subtitle. **Expect** no item changes.
2. Add a required "Language" option (English, Vietnamese) with fill-in English. **Expect** a
   preview with `filled: 300`. After saving, every item's draft (and published, where
   present) has the English option id.
3. Turn Featured into required without a fill-in. **Expect** it saved. Booleans are never
   missing, so the preview shows nothing to confirm.
4. Remove Reading time. **Expect** a preview counting the items that hold a value, examples
   by title, "cannot be undone", and confirmation required. Confirm. **Expect** the key
   `f7` gone from every item.
5. Try to remove Title. **Expect** 400 `invalid_title_field` until another single-line
   text field is designated.

## 4. Type and settings changes (US4, SC-004)

1. Seed a text field "Minutes" with SQL so it holds `"5"`, `"12.5"`, `"about 7"` and `""`.
   Change it to number (whole). **Expect** in the preview: converted 1, partly lost 1
   (12.5 rounded to 13), cleared 2. Confirm, then check the stored values match exactly.
2. Author reference → multi-reference. **Expect** a lossless change, no confirmation, and
   each value becomes a list of one.
3. Cover image → boolean. **Expect** 400 `cms.type_change_not_allowed`. Do the same on a
   field with no values. **Expect** it allowed.
4. Lower a text field's `maxLength` below some values. **Expect** `nowInvalid` counted and
   values unchanged.
5. Remove the option "Releases". **Expect** a lossy change counting the items that chose it;
   after saving, those values are gone.
6. **Stale preview**: open the preview for a lossy change. In another terminal, change one
   affected item's value with SQL so the counts change, then confirm. **Expect**
   `cms.preview_outdated` and the dialog showing the new counts.
7. **SC-004**: `dotnet test --filter "FieldConversionTests|SchemaMigrationTests"`. **Expect**
   every FR-026 pair covered and the stored outcome equal to the confirmed preview in every
   case.

## 5. Delete a collection (US5)

1. Delete "People" while Blog posts' Author field targets it. **Expect** 409
   `cms.collection_referenced` naming Blog posts and Author, shown in the dialog.
2. Remove the Author field, then delete People by typing its name. **Expect** 204, its
   items gone, and its slug free for a new collection.
3. Delete Blog posts, which references itself. **Expect** it deleted.

## 6. History and concurrency (US6, FR-037, SC-005)

1. Open the history panel. **Expect** one entry per save, newest first, with person, time
   and counts, and no item values (check the `summary` column in the database).
2. **Conflict**: open the builder in two browsers, save in the first, then save in the
   second. **Expect** the conflict banner offering to reload (`cms.revision_conflict`).
3. **Busy**: seed 10,000 items and start a field removal. While it runs, save again from a
   second session. **Expect** `cms.collection_busy`. `ItemWriteGateTests` shows an item
   write refused with `cms.items_being_updated` and readers seeing either before or after.
4. **SC-005**: `dotnet test --filter InterruptedSchemaChangeTests`. **Expect** the schema and
   every item unchanged after a fault injected mid-change. By hand: start a change on
   10,000 items and run `docker compose restart app` mid-way. **Expect** the old revision
   and unchanged items after restart.

## 7. Performance (SC-003)

On the reference compose setup (2 vCPU, 4 GB), run
`dotnet test tests/QuickBuilder.SmokeTests --filter CollectionSchemaPerformanceTests`. It
seeds 10,000 items and times a preview and a type change that affects every item.
**Expect** the preview under 5 s and the change under 30 s. Record both.

## 8. Keyboard and accessibility (SC-007)

1. Do §1 step 1 with the keyboard only: create, add fields, edit options, reorder with
   Alt+↑/↓, open the preview, confirm, delete. **Expect** each step reachable and announced.
2. `npm test -- cms`. **Expect** the axe checks in each `tests/cms/*.spec.ts` to report no
   serious or critical issues.

## 9. Upgrade and isolation

1. `dotnet test --filter "UpgradeFrom013Tests|CmsIsolationProbe|CmsSiteFilterTests"`.
   **Expect** a 013 database to gain an empty `cms` schema with nothing else changed, and no
   cross-site read or write.
2. Upgrade a real 013 compose installation by replacing the image. **Expect** **Collections**
   to appear empty on every site, and pages and media unchanged.
