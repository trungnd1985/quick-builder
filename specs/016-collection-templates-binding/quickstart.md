# Quickstart & Validation Guide

**Feature**: `016-collection-templates-binding` | **Date**: 2026-10-10

How to check, end to end, that templates give items their own pages, that bindings and lists
show the right published data, and that pages refresh exactly when they should. This guide
is for validation only. Details are in:
- tables, rules, error codes, cache keys: [data-model.md](./data-model.md);
- binding format: [contracts/binding-expression.md](./contracts/binding-expression.md);
- endpoints: [contracts/templates-api.yaml](./contracts/templates-api.yaml);
- address matching and paging: [contracts/public-rendering.md](./contracts/public-rendering.md);
- readers and events: [contracts/module-surface.md](./contracts/module-surface.md);
- decisions, numbered `TB-xx`: [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation, sites, members, cookies and shorthands**: as in 015's quickstart:
  - sites `$S` (`site-a.test`) and `$S2` (`site-b.test`);
  - cookies `$COOKIE` (Administrator), `$DESIGNER`, `$EDITOR` and `$NOROLE`;
  - `H` is `-H "X-QuickBuilder-Request: 1"`.
- **Collections from 015**:
  - "Blog posts" (`$C`): Title, Summary, Body, Cover image, Published on, Featured,
    Reading time, Category, Author (reference → People), Related (multi-reference → Blog
    posts);
  - "People" (`$P`): Name.
- **Items**:
  - `./qb.ps1 seed-items -Collection blog-posts -Count 40 -Publish 100`;
  - then, by hand: one draft-only post `draft-post`, one unpublished post `gone-post`, and
    three People, each the Author of some posts.
- **Pages**: a published page "Blog" at `/blog` and a published home page.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  dotnet test tests/QuickBuilder.SmokeTests --filter Category=Performance   # SC-004, on the reference setup
  ```

---

## 1. Make a template and open items (US1, SC-001)

1. **SC-001**: as the Designer, with a stopwatch:
   - in the page tree, create "Post" under "Blog";
   - choose **Use as template…** and pick "Blog posts". **Expect** the impact to show
     `/blog/{slug}` and "40 published items will get an address", and the tree to show the
     pattern with a template badge;
   - open the editor and pick the sample item "Hello world". Bind the Heading to Title, the
     Image to Cover image and a Rich text block to Body (§2);
   - publish, then open `https://site-a.test/blog/hello-world`.

   **Expect** under 5 minutes, and the post's published title, cover and body.
2. **Every item**: request three other published slugs. **Expect** `200` and each post's own
   values.
3. **Misses (FR-006)**: request `/blog/draft-post`, `/blog/gone-post`, `/blog/no-such-post`,
   a People slug under `/blog/`, `/blog/{slug}` literally, and `/blog/hello-world` on `$S2`'s
   host. **Expect** `404` with bytes identical to `/no-such-page`.
4. **Static first (FR-005)**: publish a page "Archive" under "Blog" (`/blog/archive`), and
   give a post the slug `archive` and publish it. **Expect** `/blog/archive` to serve the
   Archive page, and the item form to show the address as "hidden by a page" (FR-007).
5. **Rules**:
   - a second template under "Blog" → `409 pages.template_parent_taken`;
   - a second template for "Blog posts" elsewhere → `409 pages.collection_has_template`;
   - a top-level template → `400 pages.template_needs_parent`;
   - creating a child under "Post" → `409 pages.template_has_children`;
   - as the Editor, `PUT …/template` → `403`;
   - as `$NOROLE` → `404`.
6. **Template not live**: unpublish "Post". **Expect** every `/blog/<slug>` to give `404`.
   Publish it again.

## 2. Bind properties (US2, SC-003, SC-008)

1. **Compatible fields only**: select the Heading's Text binding control. **Expect** Title,
   Summary, Category, Published on, Reading time, Featured, Author → Name, the slug and the
   publish times. **Expect** no Body, Cover image or Related.
2. **Text template**: on a Text block, type `By `, insert Author → Name, type `, `, insert
   Published on with the "Long" date format. **Expect** "By Lan Nguyen, 10 October 2026" in
   the canvas and on the site.
3. **Formats**: Reading time with 1 decimal; Featured with yes "Featured" and no "".
   **Expect** "7.0" and "Featured" or nothing.
4. **Empty values**: bind a Text to Summary with "Hide this block when empty", and clear
   Summary on one post and publish it. **Expect** the block to be absent from that post's
   page. With "Show fallback", **expect** the block's own text.
5. **Unpublished reference**: unpublish a post's Author. **Expect** the byline to fall back
   or hide on that post's page within 5 seconds (§3).
6. **Links**: bind a Button's link to "Item page" via Author; give People no template.
   **Expect** the button rendered as plain text (contract §4). Bind another Button to a file
   field. **Expect** a link to the file's media address.
7. **Stored by id**: `GET` the page draft. **Expect** `bind.text.value.field = "f1"`, no
   display names, and `bindingVersion: 1`.
8. **Golden tests**: `BindingRulesGoldenTests`, `bindingRules.spec.ts` and
   `BindingRenderGoldenTests` pass.
9. **SC-008**: complete steps 1–4 with the keyboard only (Tab, arrows, Enter, Delete on
   chips). **Expect** the vitest axe checks of `BindingControl` and the list controls to
   pass, and a screen reader to announce each chip and the scope groups (manual, E-5).

## 3. Freshness and exact refresh (US3, SC-005, SC-006)

1. **Setup**: warm the cache by requesting all 40 post pages, the home page (with a "Latest
   posts" list, §4) and "Blog".
2. **Publish one post**: change its title and publish. **Expect** within 5 seconds:
   - its page and the home page list show the new title;
   - `ItemPageInvalidationTests` reports exactly these pages re-rendered: that post, the
     pages listing "Blog posts", and the pages listing a collection that references "Blog
     posts" (SC-006).
3. **Unpublish, roll back, schedule, delete**: **expect** each, including a scheduled
   publish two minutes ahead, to behave like step 2. An unpublished or deleted post gives
   `404` and leaves every list.
4. **Template publish**: change the template and publish. **Expect** every post page to show
   it.
5. **Schema change (closes E-6)**: rename Summary (nothing changes on the site), then remove
   it. **Expect**:
   - the builder to warn "bound in 2 pages";
   - after applying, every page that showed Summary falls back or hides within 5 seconds;
   - no `5xx`.
6. **Two instances**: repeat steps 2 and 5 with `docker-compose.dev.yml` scaled to two app
   instances and Redis. **Expect** the same results on both (SC-005).

## 4. Collection list (US4, SC-002)

1. **SC-002**: as the Designer, with a stopwatch:
   - on the home page, add a Collection list of "Blog posts" with Featured `eq true`, sort
     Published on `desc`, and limit 6;
   - design the card (Image → Cover image, Heading → Title, Text → Summary, Button →
     Item page);
   - write the empty state.

   **Expect** under 3 minutes, and six cards in the canvas that match the site after
   publishing.
2. **Operators**: `CollectionListRenderTests` covers every operator and type in contract
   §5. By hand, check `contains` on Title ignoring accents, `lt` on Reading time, and
   `notEmpty` on Cover image.
3. **Empty state**: filter on a category nobody uses. **Expect** the empty-state content.
4. **Broken filter**: delete the option used in a filter. **Expect** the editor to mark the
   filter broken, and the site to show the empty state, never all items.

## 5. Paging (US5)

1. **Setup**: on "Blog", add a list of all posts with paging (page size 10, parameter
   `page`), and a second list of People with paging (page size 2, parameter `people`).
2. **Pages**: follow Next to `/blog?page=4`. **Expect**:
   - posts 31–40;
   - `aria-current` on 4;
   - the People list still on its first page;
   - page 2 of People keeping `page=4` in its links.
3. **Out of range**:
   - `/blog?page=99` → the posts list shows its empty state and a link to page 1, the
     People list is normal, and the status is `200`;
   - `/blog?page=abc` and `/blog?page=1` → the first page.
4. **No cache growth**: request `/blog?utm=1`, `/blog?x=2` and `/blog?page=2&y=3`. **Expect**
   the same ETags as `/blog` and `/blog?page=2`.
5. **No script**: with scripts turned off, everything in steps 2–3 works.

## 6. Nested scopes (US6)

1. **Related posts**: on the post template, add "More in this category": a list of Blog
   posts with Category `eq` (page item's Category) and slug `ne` current, limit 3. **Expect**
   each post page to list three other posts of its category.
2. **Multi-reference source**: add a list whose source is the page item's Related field.
   **Expect** the referenced published posts in stored order. A draft-only related post is
   left out.
3. **List in a list**: on "Team", add a list of People. In its card, add a list of Blog
   posts with Author `eq` (outer card item), sorted newest first, limit 2. **Expect** each
   person with their two latest posts. Inside the inner card, bind a Text to the outer
   scope's Name. **Expect** the person's name.
4. **Depth and counts**: try a third level (`400 pages.invalid_document` with
   `list_too_deep`), a 21st list (`too_many_lists`) and two lists with parameter `page`
   (`duplicate_page_param`). The editor refuses each before saving.
5. **Components**: a header component with a "Latest posts" list works. Binding a Text in
   the component with scope `"page"` is refused (`page_scope_not_allowed`).

## 7. Safe changes (US7, SC-007)

1. **Move**: move "Blog" under "Resources". **Expect** the warning "40 published items will
   change address". After the move, `/resources/blog/hello-world` is `200` and
   `/blog/hello-world` is `404`.
2. **Slug change**: change and publish a post's slug. **Expect** the new address `200`, the
   old one `404`, and the home list linking to the new one.
3. **Retype**: change Reading time from number to text (014). **Expect** bindings that need
   a number marked broken, and the site falling back, not failing.
4. **Delete the collection**: try to delete "Blog posts". **Expect** `409
   cms.collection_used_by_pages` listing "Post" (draft, live), the home page and "Team".
   Remove those uses and the delete succeeds.
5. **Back to a page**: turn "Post" back into a page. **Expect**:
   - the impact "40 published items will lose their address";
   - page-scope bindings marked broken;
   - the page saving and publishing at `/resources/blog/post`.
6. **SC-007**: `BindingFailureTests` passes with zero `5xx` across all its scenarios.

## 8. Performance (SC-004)

On the reference setup, run `ItemPagePerformanceTests`:
- an item page of 200 blocks with a list of 20 cards;
- a page with a paginated list of 100 over a 10,000-item collection (`seed-items -Count
  10000 -Publish 100`).

**Expect** p95 under 200 ms on a miss and under 20 ms on a hit for both. Record the numbers
and the query counts per render.

## 9. Isolation, logs and upgrade

1. **Isolation (FR-037)**: the isolation suite with `TemplateIsolationProbe` passes. By
   hand, a list on `$S2` whose `collectionId` is `$S`'s "Blog posts" is refused as
   `unknown_collection`.
2. **Logs**: during §3–§7, the logs hold no item values, names or slugs. Grep for
   "Hello world" and for slugs; **expect** no match.
3. **Upgrade (TB-18)**: `UpgradeFrom035Tests` passes. Every pre-existing page renders the
   same body; only the ETag changes.
4. **Smoke**: the release smoke journey (make a template, publish an item, open it) passes on
   the built image.
