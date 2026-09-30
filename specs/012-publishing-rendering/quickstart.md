# Quickstart & Validation Guide

**Feature**: `012-publishing-rendering` | **Date**: 2026-09-29

How to check, end to end, that publishing and rendering work, and how to verify each
success criterion. This guide covers validation only:
- tables, rules and error codes are in [data-model.md](./data-model.md);
- endpoints are in [contracts/publishing-api.yaml](./contracts/publishing-api.yaml);
- what visitors get is in [contracts/public-rendering.md](./contracts/public-rendering.md);
- readers and events are in [contracts/readers.md](./contracts/readers.md);
- decisions are numbered `PR-xx` in [research.md](./research.md).

Record results, timings and browser versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation and sites**: as in 011's quickstart: a site `$S` with host name
  `site-a.test`, a second site `$S2` with `site-b.test`, and cookies `$COOKIE`
  (Administrator), `$DESIGNER` and `$EDITOR`.
- **Name resolution**: both host names resolve to the installation, through `/etc/hosts`
  or `curl --resolve site-a.test:8080:127.0.0.1`.
- **Content** on `$S`, built in the editor: pages **Home** (the home page), **About**,
  **Contact** and **Pricing**; components **Header** (with a Button linking to **Contact**)
  and **Footer**. **About** uses all ten block types, one image, and the Header and Footer.
- **Shorthands**: `$API=http://127.0.0.1:8080/api/v1`. `H` stands for
  `-H "X-QuickBuilder-Request: 1" -H "Content-Type: application/json"`.
  `PUB(host, path)` stands for `curl -si --resolve $host:8080:127.0.0.1 http://$host:8080$path`.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

---

## 1. Publish a page and view it (US1, SC-001)

1. Timer on. In the page tree, open **About**'s **Publishing** menu and choose
   **Publish**. The component check refuses, naming **Header** and **Footer** as
   unpublished.
2. Publish **Header** and **Footer** from the component list, then publish **About** again.
3. Choose **View live page**. Timer off.

**Expected**:
- **Timing**: under 1 minute (SC-001).
- **Status**: the page tree shows **About** as *Published*, with the number, time and your
  name.
- **The live page**: it opens at `http://site-a.test:8080/about` and shows every block,
  styled like the canvas, with Header and Footer.
- `PUB(site-a.test, /about)` → `200`, `text/html`, no `<script>`, and the headers of
  public-rendering.md §3. `PUB(site-a.test, /About/)` → the same body.
- **No scripts needed**: with JavaScript turned off in the browser, the page looks the
  same.
- **Draft stays private**: change the heading in the editor. `PUB` still shows the old
  heading, and the tree shows *Published — changes*. Publish from the editor's top bar, and
  the new heading appears on the next `PUB`.
- **Invalid draft**: `POST $API/pages/sites/$S/pages/{about}/publish` with a stale
  `draftVersion` → `409 pages.out_of_date`.

## 2. Components and design (US2, SC-006)

1. Add a Button "Blog" to **Header** and publish only **Header**. Every published page shows
   it on the next `PUB`; no page was republished.
2. On the Design screen, change the brand colour. `PUB` is unchanged. Press **Publish
   design**, and every page shows the new colour on the next `PUB`.
3. Try to unpublish **Header** → refused, stating how many published pages use it.
4. **Exactness** (automated, `CacheInvalidationTests`): 50 published pages, 10 of which use
   component C. After a request to each, publish C.

**Expected** for step 4: exactly those 10 pages are re-rendered on their next request, and
the 40 others are served from the cache. The test counts renderer invocations (SC-006).

## 3. History and rollback (US3, SC-001)

1. Publish **Pricing** three times with prices 10, 20 and 30.
2. Open **History**. It lists versions 3, 2 and 1, newest first, with who, when and kind.
   **View** version 1 opens it read-only in the editor.
3. Timer on. **Roll back** to version 1 and confirm. Timer off.
4. **Copy to draft** version 2.

**Expected**:
- **Timing**: under 30 seconds from opening History (SC-001).
- **After the rollback**: `PUB(/pricing)` shows 10. History shows version 4 as *Rollback of
  1*. The draft still holds 30, and the tree shows *Published — changes*.
- **After the copy**: the editor shows the version 2 content as the draft, and `PUB` still
  shows 10.
- **Missing component**: delete a component used only by an old version (unpublish or
  remove its uses first). Rollback to that version → `409
  pages.version_missing_components`.

## 4. Unpublish and delete (US4)

1. Unpublish **Pricing** and confirm. `PUB(/pricing)` → `404`, with the not-found page.
2. Publish **Contact**'s child page while **Contact** is unpublished. The child's address is
   served.
3. Try to delete **About** while it is published. **Delete** is disabled with a reason, and
   the API answers `409 pages.page_published`.
4. Unpublish **Pricing**, delete it, and open the **Publish log** as Administrator.

**Expected** for step 4: Pricing's entries are still listed, with the label "Pricing", its
last address and a *deleted* entry. As Designer, the publish log answers `403`.

## 5. Schedules (US5, SC-007)

1. Schedule **Contact**'s publish for two minutes from now. The dialog shows your time zone.
2. Change **Contact**'s draft after scheduling.
3. Wait.

**Expected**:
- **At the time** (within a minute): the content reviewed at scheduling is live, not the
  later draft. History shows *Scheduled publish* by you, and the tree shows *Published —
  changes*.
- **Cancellation**: schedule again, then publish by hand. The dialog warns first; after
  that the schedule is gone and the log shows `schedule_cancelled`
  (`superseded_by_manual_action`).
- **Failure**: schedule a page that references a component, then unpublish that component
  (after unpublishing its other users). At the due time the page is unchanged, and the tree
  shows *Schedule failed — unpublished components*.
- **Restart** (automated, `ScheduledPublishExactlyOnceTests`): due times that pass while the
  host is stopped run once after start. With two dispatchers, each schedule runs once
  (SC-007).
- **Past time**: a time in the past → `400 pages.schedule_in_past`.

## 6. Freshness across instances (US6, SC-002, SC-005)

Run two instances against one database (`docker-compose.perf.yml` with `--scale app=2`
behind its proxy, or `RenderingAcrossInstancesTests`).
1. Request **About** on both instances, so each has it cached.
2. Publish a change through instance 1.
3. Request it from instance 2 at once.

**Expected**:
- **Step 3** shows the new content on the first request (SC-002; research PR-05).
- **Isolation**: across the automated suite, no response on `site-b.test` contains
  `site-a.test`'s content, and no response mixes versions (SC-005,
  `PublishingIsolationProbe`).

## 7. Not-found page (US7)

1. `PUB(site-a.test, /no-such-page)` → `404` HTML in the site's design and language, with a
   link to `/`.
2. Unpublish **Home**; the link to `/` disappears, and `PUB(/)` → `404`.
3. With `Hosting:AdminHostNames` set, `PUB(site-a.test, /admin)`: the body is byte-identical
   to step 1's (`NotFoundParityTests`).
4. `PUB(unknown.test, /)` and a disabled site's host → 009's JSON `sites.not_resolved`,
   unchanged.

## 8. Performance budgets (SC-003, SC-004)

On the reference compose setup (2 vCPU, 4 GB; `docker-compose.perf.yml`):

```bash
dotnet test tests/QuickBuilder.SmokeTests --filter "FullyQualifiedName~RenderPerformanceTests"
```

**Expected**:
- a 200-block page with Header and Footer renders at p95 under 200 ms on a miss, and is
  served at p95 under 20 ms on a hit;
- the page ships 0 bytes of JavaScript.

The test fails the run otherwise (FR-041). Record the numbers.

## 9. Keyboard and translation (SC-009)

With the keyboard only:
- publish, schedule, open history, roll back and cancel a schedule from the page tree and
  the editor;
- publish the design;
- read the publish log.

**Expected**: every action is reachable. The vitest suites (`PublishMenu`, `HistoryView`,
`ScheduleDialog`, `PublishLogView`, `DesignPublish`) pass axe and the translation-lookup
checks, and `PublicTextCatalogueTests` passes.

## 10. Upgrade (FR-042)

`UpgradeFrom011Tests` upgrades a 011 snapshot. Drafts and token sets are unchanged, nothing
is published, and a public request gets the not-found page.

## 11. Smoke test (FR-043)

```bash
dotnet test tests/QuickBuilder.SmokeTests --filter "FullyQualifiedName~ReferenceDeploymentSmokeTests"
```

**Expected**: the journey publishes a Header and a page that uses it, requests the page
from the site's host name and finds its heading and the header's text, unpublishes it, and
gets the 404 page.
