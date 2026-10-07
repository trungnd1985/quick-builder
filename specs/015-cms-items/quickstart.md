# Quickstart & Validation Guide

**Feature**: `015-cms-items` | **Date**: 2026-10-08

How to check, end to end, that items can be written, published, scheduled, found and
deleted safely, and how to verify each success criterion. Validation only:
- tables, rules, events and error codes: [data-model.md](./data-model.md);
- endpoints: [contracts/items-api.yaml](./contracts/items-api.yaml);
- capabilities, events, the media usage reader: [contracts/module-surface.md](./contracts/module-surface.md);
- value formats: [docs/contracts/collection-schema.md](../../docs/contracts/collection-schema.md) §3;
- decisions are numbered `IT-xx` in [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation, sites and members**: as in 014's quickstart: `$S` (`site-a.test`), `$S2`
  (`site-b.test`), cookies `$COOKIE` (Administrator), `$DESIGNER`, `$EDITOR` (granted on
  `$S` only) and `$NOROLE` (a user with no role on `$S`).
- **Shorthands**: `$API=http://127.0.0.1:8080/api/v1/cms/sites`; `H` is
  `-H "X-QuickBuilder-Request: 1"`; `$C` is the "Blog posts" collection id, `$P` the
  "People" collection id.
- **Collections**: "Blog posts" with the eight fields of 014 quickstart §1 (Title, Summary,
  Body, Cover image, Published on, Featured, Reading time, Category), plus Author
  (reference → People) and Related (multi-reference → Blog posts). "People" with Name
  (text). Upload two images to `$S`'s media library and one PDF.
- **Bulk items**: `./qb.ps1 seed-items -Collection blog-posts -Count 10000 -Publish 50`
  (now through the item API; `-Publish` is the percentage to publish).
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  dotnet test tests/QuickBuilder.SmokeTests --filter Category=Performance   # SC-003, IT-12, on the reference setup
  ```

---

## 1. Write a draft (US1, SC-001, SC-002)

1. **SC-001**: as the Editor, with a stopwatch, open **Collections → Blog posts → Items →
   New item**. Type the title "Hello world"; **expect** the slug `hello-world` suggested.
   Fill every field (pick a cover image, a category, an author), **Publish**. **Expect**
   under 3 minutes and the item listed as Published.
2. **Form generation**: **expect** one input per field in schema order, help texts shown,
   required fields marked, the remaining length on Title, the number box refusing "1.5" for
   Reading time (whole), and the Category drop-down listing News, Guides, Releases in order.
3. **Round trip (SC-002)**: reload the item. **Expect** every value exactly as entered;
   `GET $API/$S/collections/$C/items/{id}` returns `draft.values` keyed `f1` … `f10`, the
   option as `o…`, the image as `{ "mediaId": … }`.
4. **Malformed**: `PUT` with `"f7": "seven"` (number), `"f8": "o99"` (unknown option), and
   `"f9"` set to an id of a People item from `$S2`. **Expect** 400 `cms.invalid_item` with
   one `report.problems` entry per field (`malformed`, `unknown_option`,
   `reference_missing`); nothing stored (`revision` unchanged).
5. **Incomplete draft (clarification Q2)**: create an item with Title empty and Summary
   longer than its maximum. **Expect** 201, `report` listing `missing_required` (f1) and
   `too_long` (f2), the item marked "Needs attention" in the list, and the slug suggested
   from the creation time.
6. **Slug taken**: create a second item with slug `HELLO-WORLD`. **Expect** 409
   `cms.item_slug_taken` with `suggestion: "hello-world-2"`; the form showed the problem
   before saving.
7. **Roles**: as `$NOROLE`, call every endpoint. **Expect** 404 everywhere. As the Designer
   and the Editor, **expect** every item action allowed (clarification Q1).

## 2. Publish and unpublish (US2, SC-004)

1. Publish the item from §1.5 as is. **Expect** 409 `cms.item_needs_attention` listing f1
   and f2. Fix both, publish. **Expect** status `published`, version 1, `liveVersion` 1.
2. **SC-004**: change Summary, save. **Expect** status `changed`, `published.values`
   unchanged (compare with the previous response byte for byte). `dotnet test --filter
   ItemDraftAndPublishTests` covers every write kind against `published`.
3. Publish an item whose Author is a draft-only People item. **Expect** 200 with
   `unpublishedReferences` naming it, and the warning shown in the admin before confirming.
4. Make two Blog posts refer to item X through Related, publish both. Unpublish X.
   **Expect** the admin to say 2 published items refer to it before confirming; afterwards
   X has no `published`, keeps its draft and versions, and history has `unpublished`.
5. **Concurrent publishes**: `dotnet test --filter ItemConcurrencyTests`. **Expect** each
   publish its own version, numbered in commit order.

## 3. Find items (US3, SC-003)

1. In the 10,000-item collection: search `héllo` and `HELLO`. **Expect** the same results
   (accents and case ignored), name or slug only (clarification Q5).
2. Filter status Changed + Category Guides + Featured true; sort by Reading time ascending;
   page size 50. **Expect** the total shown, items without a Reading time last, each item on
   exactly one page when paging through, and the address holding every setting (reload and
   share it: same view).
3. Open an item and go back. **Expect** the same page, filters and sort.
4. `?field[f2]=x` (text field) and `?sort=field.f3` (rich text). **Expect** 400
   `cms.invalid_query`.
5. **SC-003**: `dotnet test tests/QuickBuilder.SmokeTests --filter
   ItemListPerformanceTests`. **Expect** p95 under 1 s for list, search, each filter and sort,
   and picker search.

## 4. Reference pickers (US4)

1. In a post, open Author's picker, type "ngu". **Expect** People items matching by name or
   slug, each with its status; pick one; it replaces any earlier choice; Clear removes it.
2. Related: pick three posts, drag the third to the top, remove one; then repeat with the
   keyboard only (Move up/down buttons, Alt+Arrow). **Expect** the saved order to match,
   moves announced, the item itself not selectable.
3. **Expect** draft-only references tagged "Not published".
4. `PUT` Related with 101 ids, and with the item's own id. **Expect** `too_many_references`
   and `reference_self`.

## 5. Delete and duplicate (US5, SC-005)

1. Delete a People item that 6 posts (4 published) name as Author. **Expect** 409
   `cms.item_referenced` listing the 6 with collection, field and draft/published flags.
   Change their authors, delete again. **Expect** 204; the slug is free again.
2. Save a post whose Related lists itself. **Expect** `reference_self`.
3. Delete a published item. **Expect** the confirmation to say it is live.
4. Duplicate "Linen shirt". **Expect** a new draft "Linen shirt (copy)", slug
   `linen-shirt-copy`, the same draft values, no versions or schedules.
5. **SC-005**: `dotnet test --filter ItemReferenceIntegrityTests` (includes a concurrent
   add-reference/delete race and a whole-collection delete whose items refer to each other). **Expect**
   0 dangling references.

## 6. Scheduling (US6, SC-006)

1. Schedule a publish 2 minutes ahead (the dialog names your time zone), then change the
   draft. **Expect** "draft differs from what is scheduled"; at the time, the scheduled
   content (not the later draft) published as version kind `scheduled`, within a minute.
2. Schedule an unpublish 3 minutes ahead. **Expect** it to happen and history to record it.
3. Schedule a publish, then publish by hand. **Expect** a warning first; the schedule
   cancelled and recorded.
4. Schedule a publish, then make an empty field required in the schema builder (no fill-in).
   **Expect** at the time: nothing published, history `scheduleFailed` with
   `cms.item_needs_attention`, the item marked needs attention.
5. Stop the app (`./qb.ps1 stop`) across a scheduled time and start it. **Expect** the
   action once, within a minute of start-up.
6. **SC-006**: `dotnet test --filter ItemScheduledExactlyOnceTests` (two dispatchers,
   restart, redelivery, a schema change running at the due time).

## 7. History and rollback (US7)

1. Publish a post three times with different titles, roll back to version 1. **Expect**
   version 4 (kind `rollback`, restored from 1), `published` equal to version 1, the draft
   unchanged.
2. Delete the People item version 1 referred to (after changing the current references).
   Roll back to version 1. **Expect** 409 `cms.version_not_publishable`
   (`reference_missing`); **Copy to draft** works and the draft then reports it.
3. Publish one item 52 times. **Expect** 50 versions kept, the live one among them.

## 8. Schema changes reach items (IT-07, IT-11, IT-12)

1. With a post's form open, rename the collection (details only) and save the item.
   **Expect** saved. Then rename a field in the builder and save the item. **Expect**
   `cms.schema_changed`; the form reloads, keeps entered values, highlights the field.
2. Remove the Reading time field. **Expect** its values gone from drafts, published values,
   every kept version and pending scheduled content (`dotnet test --filter
   ItemSchemaMigrationTests`); add a required field with a fill-in: **expect** it in drafts,
   published values and schedules, not in versions.
3. Change the title field to another text field. **Expect** list names and search to follow.
4. Start a schema change on 10,000 items and save an item meanwhile. **Expect**
   `cms.items_being_updated`, the form keeping the input.

## 9. Media (IT-15)

1. Open the cover image in the media library. **Expect** its usage to list the posts using
   it (draft, live, history flags).
2. As the Administrator, delete it (confirming in-use). **Expect** within the outbox poll
   interval: those posts "Needs attention" (`media_missing`) and publish refused until the
   image is replaced or cleared.
3. As the Editor, try to delete an image used only by a published post. **Expect**
   `media.in_live_use` (013 rule, unchanged).

## 10. Keyboard and accessibility (SC-007)

Using only the keyboard: create, fill (every field type, both pickers), save, publish,
schedule, filter, sort, page, roll back, delete. **Expect** every action reachable, focus
visible, errors announced and linked from the summary. `npm test -- items` runs axe on the
list, form, pickers, dialogs and history panel: no serious or critical findings.

## 11. Upgrade and isolation

1. `dotnet test --filter UpgradeFrom014Tests`: a 014 snapshot with draft-only and published
   items upgrades; slugs `item-…` replaced from names by the backfill, statuses right,
   published items have version 1, `index_version` current.
2. `dotnet test --filter "CmsSiteFilterTests|Isolation"`: every new table site-filtered;
   `$S2` never sees `$S` items, slugs, references, versions or schedules.
