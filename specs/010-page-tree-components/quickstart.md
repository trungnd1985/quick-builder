# Quickstart & Validation Guide

**Feature**: `010-page-tree-components` | **Date**: 2026-09-27

How to check, end to end, that the page tree, drafts and shared components work, and how
to verify each success criterion. This guide covers validation only:
- tables and invariants are in [data-model.md](./data-model.md);
- endpoints are in [contracts/pages-api.yaml](./contracts/pages-api.yaml);
- the document format is in [contracts/page-schema.md](./contracts/page-schema.md);
- decisions are numbered `P-xx` in [research.md](./research.md).

---

## Prerequisites

- **Installation**: set up (002 quickstart §1), with an Administrator session cookie in
  `$COOKIE`.
- **Site**: one site from 009, with its id in `$S`. A second site, `$S2`, is needed for §6.
- **Users**: a Designer and an Editor on `$S` (002 grants), with cookies in `$DESIGNER`
  and `$EDITOR`.
- **Requests**: state-changing calls need `-H "X-QuickBuilder-Request: 1"`. Examples use
  `$API=http://127.0.0.1:8080/api/v1/pages/sites/$S`, and `H` stands for
  `-H "X-QuickBuilder-Request: 1" -H "Content-Type: application/json"`.

---

## 1. Build a structure (SC-001, US1)

In the admin shell, open the site and choose **Pages**. With a timer running:
1. Add **Home**, and tick *Home page*. It shows as `/`.
2. Add the top-level pages **About**, **Services** and **Contact**.
3. Under Services, add **Web design** and **Hosting**. Under Hosting, add **Plans** and
   **FAQ**. Under About, add **Team** and **FAQ**.

**Expected**:
- **Timing**: finished in under 5 minutes.
- **Addresses**: `/services/hosting/plans`, `/about/faq` and `/services/hosting/faq`
  all exist. The same slug under different parents is accepted (clarification Q1).
- **Slug suggestion**: typing the title "Dịch vụ & Giá" suggests `dich-vu-gia`.

Refusals to try:

```bash
curl -s -b "$COOKIE" $H -d '{"title":"x","slug":"About"}' $API/pages      # 400 pages.invalid_slug
curl -s -b "$COOKIE" $H -d '{"title":"x","slug":"about"}' $API/pages      # 409 pages.duplicate_address
curl -s -b "$COOKIE" $H -d '{"title":"x","slug":"admin"}' $API/pages      # 409 pages.reserved_address
curl -s -b "$COOKIE" $H -d "{\"title\":\"x\",\"slug\":\"about\",\"parentId\":\"$HOME_ID\"}" $API/pages
                                                                          # 409 pages.duplicate_address (/about)
curl -s -b "$COOKIE" $H -d '{"title":"x","slug":"y","isHome":true}' $API/pages   # 409 pages.home_page_exists
```

**Automated**: `PageTreeApiTests` and `slugify.spec.ts`.

---

## 2. Rename, reorder and move (US2, SC-002, SC-004)

1. Change the slug of **Services** to `what-we-do`. The tree at once shows
   `/what-we-do/hosting/plans`.
2. Select **Contact** and press **Move up** twice with the keyboard. It now comes before
   What we do.
3. **Move to…** for Hosting, choosing parent *About*, position *after Team*. It
   succeeds. Hosting, Plans and FAQ now have the addresses `/about/hosting`,
   `/about/hosting/plans` and `/about/hosting/faq`.
4. Create a page `/what-we-do/faq`. Then move **About → FAQ** (`/about/faq`) under What
   we do. It is refused with **409 `pages.duplicate_address`**, naming `/what-we-do/faq`,
   and the tree is unchanged.
5. Try to move **About** under **Team**, its own child:
   **409 `pages.move_into_descendant`**.

**Stale edit**: open the tree in two tabs, rename **Contact** in one, then rename it in
the other. The second tab says the page changed and offers to reload
(`pages.out_of_date`). Reordering *another* page in the first tab does **not** make the
second tab's rename of Contact stale (P-05).

**Automated**:
- `PageTreeAggregateTests`: the invariants hold after random operation sequences.
- `PageTreeConcurrencyTests`: two parallel creates at one address give exactly one
  success, and crossed moves (A under B, B under A) give exactly one success and no cycle.
- `PageTreePerformanceTests` (smoke): moving a 200-descendant subtree takes under 2 s at
  p95.

---

## 3. Drafts and the page schema contract (US3, SC-003, SC-007)

```bash
P=<Hosting's id>
D=$(curl -s -b "$COOKIE" $API/pages/$P/draft)            # empty root, schemaVersion 1, version n
curl -s -b "$COOKIE" $H -X PUT $API/pages/$P/draft \
  -d "{\"version\":$(jq .version <<<"$D"),\"document\":$(cat docs/contracts/page-schema/examples/valid/empty.json)}"
                                                         # 200, new version
```

Send each file in `docs/contracts/page-schema/examples/invalid/`. Each one gives
**400**, with the `rule` from its file name and a `pointer`, and a `GET` of the draft
afterwards shows it unchanged. Then:
- Resend with the old `version`: **409 `pages.out_of_date`**.
- Send a 1.2 MiB body: **413 `pages.document_too_large`**.
- Round-trip `valid/header-and-footer.json` (after §4 creates the components). The
  document that comes back equals what was sent as a JSON value; key order may differ
  (P-08).

**Contract drift**: edit `docs/contracts/page-schema.md` to drop `component-ref` from the
node-type table, and run `dotnet test --filter PageSchemaContractTests`. It fails, naming
the missing type.

**Nothing public changes**: `curl -H "Host: <site host>" http://127.0.0.1:8080/hosting`
answers exactly as before this feature. Rendering is #12.

**Automated**: `PageDocumentValidatorTests`, `PageSchemaContractTests`,
`PageSchemaSubsetTests` and `DraftApiTests`.

---

## 4. Shared components (US4)

1. Go to **Components → New**, and create **Header** and **Footer**. Creating **header**
   gives 409 `pages.duplicate_component_name`.
2. Save `valid/header-and-footer.json`, with the two ids filled in, as the drafts of Home
   and About. Header then shows **Used by 2 pages**, and the panel lists `/` and
   `/about`.
3. **Delete Header**: refused with **409 `pages.component_in_use`**, `usageCount: 2`.
4. Save Home's and About's drafts without the Header reference. Deleting it then succeeds.
5. Save a page draft that references a component id from `$S2`:
   **400 `pages.unknown_component`**. A random UUID gives the same response.
6. Save a component draft containing a `component-ref`: **400**, with rule
   `component_ref_not_allowed`.
7. Rename **Footer** to **Site footer**. Pages that reference it are unaffected.

**Automated**: `ComponentApiTests`, and `ComponentUsageRaceTests` (a delete racing a
save that references the component: never both succeed).

---

## 5. Delete and roles (US5, clarifications Q2 and Q3)

- Delete **Hosting** while it has Plans and FAQ: **409 `pages.has_children`**,
  `childCount: 2`. Delete Plans, then FAQ, then Hosting: each gives 204, and the siblings
  close up.
- Delete **Home** while other pages exist: **409 `pages.home_page`**.
- **As the Editor** (`$EDITOR`):
  - the tree and components screens are read-only;
  - `PUT …/pages/$P/draft` gives 200;
  - `POST …/pages`, `…/move`, `…/make-home`, `DELETE …/pages/…`,
    `POST …/components` and `PUT …/components/…/draft` each give
    **403 `identity.forbidden`**.
- **As the Designer**, every action succeeds.

**Automated**: `PagesAuthorizationTests` covers every endpoint for every role. The
existing `EndpointAuthorizationCoverageTests` fails if an endpoint has no declaration.

---

## 6. Isolation (SC-006)

With `$S2` populated the same way, every request to `$API/...` that carries a page or
component id from `$S2` gives **404** (`pages.not_found` or
`pages.component_not_found`). Lists on `$S` never contain `$S2`'s ids.

**Automated**: `PagesIsolationProbe` in `SiteIsolationSuite`. `IsolationCoverageTests`
fails if any endpoint with `{pageId}` or `{componentId}` is left out of the probe.
`PagesSiteScopingTests` covers the single `ForSite` helper (P-15).

---

## 7. Upgrade, smoke and performance (FR-035, FR-036, SC-005, SC-008)

- `UpgradeFrom009Tests`: a 009 snapshot upgrades, everything from 009 still works, and a
  page can be created.
- **Smoke test** (`ReferenceDeploymentSmokeTests`): it now creates a page and a child
  page, saves a valid draft and reads it back, against the built image.
- `PageTreePerformanceTests`: with 2,000 pages seeded, the tree endpoint answers in under
  2 s at p95. In a browser, the Pages screen shows all 2,000 rows within 2 s.
- **Accessibility**: `pageTreeView.spec.ts` and `componentsView.spec.ts` run axe and a
  keyboard-only journey: create, rename, move up, move to, make home and delete, all
  without a pointer. The i18n lint rule fails on any untranslated string.
