# Quickstart: Validating Draft Preview Links

How to prove feature 022 works, end to end. It points at the contracts and research rather
than repeating them. The automated suites are listed in [plan.md](./plan.md) (Testing).
Record results in `quickstart-results.md`, as earlier features do.

## 0. Prerequisites

- Docker and the .NET 10 SDK, and Node for the admin tests.
- Run everything:

  ```text
  dotnet test                                    # unit, architecture, integration (Docker)
  dotnet test --filter "FullyQualifiedName~Preview"
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

- Run the app for the manual checks with `./qb.ps1 start` (or `./qb.ps1 dev`), then sign in
  to `/admin`.
- Create site S with host name `s.localhost`, and site T **without** a host name. On S, add
  Administrator A, Designer D and Editor E. Also have a private browser window with no
  session (the "reviewer").
- On S, set up:
  - a published header component;
  - a published "Pricing" page using it, with a Collection list of a "Plans" collection
    (three published items) and a Form block;
  - a "Plans" template page, published.

## 1. Member preview matches the published page (Story 1, SC-001)

1. As E, change Pricing's heading in the editor and press **Preview** at once, without
   waiting for autosave.
   - A new tab opens with the new heading. Header, footer, design, the list of plans and
     the browser tab title are all present.
   - The bar says "Draft saved … by E".
2. Open `http://s.localhost/pricing`. It still shows the old heading.
3. Open a plan item's form, change a value and press **Preview**. The item page renders
   through the template with the draft value.
4. Create a new page "Launch" that was never published and preview it. It renders, and
   `http://s.localhost/launch` answers not found.

**Automated**: `PreviewParityTests` (golden pages and item pages, PV-04),
`MemberPreviewTests`.

## 2. Share a link with a reviewer (Story 2, SC-002, SC-005)

1. As E, on Pricing choose **Share preview**, keep 7 days, label "Client" and **Create
   link**. Copy it, and time the steps from the editor to a copied link (under 30
   seconds).
2. Open the link in the reviewer window. The draft shows, and the bar says "This link
   expires …" with no editor links.
3. Save another heading change and reload the reviewer window. It shows the new draft
   (clarification Q1).
4. Click a link in the page to another page. It opens the public address on `s.localhost`.
5. Change one character of the secret, use a link of site S's other page, and try
   `/_preview/l/` with nothing. Each answers the same "no longer available" page; compare
   the response bodies byte for byte.
6. On site T (no host name), create a link. It is built on the admin origin and works.

**Automated**: `PreviewLinkTests`, `PreviewLinkTamperingTests` (at least 1,000 altered,
cross-site and cross-target secrets, SC-005), `PreviewLinkAddressTests`.

## 3. Out of search engines, caches and forms (Story 3, SC-004)

1. Check the headers of a preview response (`curl -i`): `Cache-Control: no-store, private`,
   `X-Robots-Tag: noindex, nofollow`, `Referrer-Policy: no-referrer`, no `ETag`. The head
   has `<meta name="robots" content="noindex, nofollow">` (public-preview.md §5).
2. Request `http://s.localhost/pricing` twice. The second response is a cache hit
   (`qb.render.cache` metric) with the published content. The cache holds no `/_preview`
   output.
3. Submit the form in the reviewer window. The page says "Forms are not sent from
   previews", and Forms → Submissions is unchanged.
4. `http://s.localhost/sitemap.xml` has no `/_preview` address, and `robots.txt` has
   `Disallow: /_preview/`.
5. In the app's log output, search for the secret. There are no matches.

**Automated**: `PreviewResponseRulesTests`, `PreviewCacheIsolationTests`,
`FormSubmitIsolationTests`, `PreviewLogRedactionTests`; architecture
`PreviewBoundaryTests` (PV-21).

## 4. Revoke and see who has access (Story 4, SC-006)

1. As E, open **Share preview** on Pricing. "Client" shows "last opened" (from §2). Revoke
   it, and the reviewer's reload shows "no longer available".
2. Start a second instance (`docker-compose.gate.yml` profile with two app containers).
   Create a link, open it through instance 2, revoke it through instance 1, and reload
   through instance 2 within 5 seconds. It is gone.
3. As A, open Site settings → **Preview links**. Every active link of S is listed. Revoke
   all.
4. As E, create a link. As A, remove E's role on S. The link stops working at once. Give the
   role back, and the link works again until it expires.
5. Open Pricing's history. It shows "Preview link created" and "Preview link revoked" with
   who and when, and no link or label.

**Automated**: `PreviewLinkRevocationTests` (two instances), `PreviewLinkCreatorAccessTests`,
`PreviewLinkHistoryTests`, `PreviewAuthorizationTests`.

## 5. Past versions (Story 5, SC-007)

1. As D, publish Pricing three times with different headings. In its history, **Preview**
   version 1. The bar names "Version 1, published … by D", and today's header is shown.
2. Share version 1. The link shows version 1 even after more draft saves.
3. Roll back to version 1. The public page matches the version 1 preview (compare without
   the bar).

**Automated**: `VersionPreviewTests`, including retention: a link to a version past the
50-version limit answers "no longer available".

## 6. Edge cases

| Case | Check |
|---|---|
| Never-published header (Q2) | On a fresh site, preview a page using an unpublished component. It shows from its draft, and the bar names it. |
| Item without a template | **Preview** is disabled with the reason, and the API answers `preview.no_template`. |
| Invalid page draft | Store an invalid draft through a test fixture. Members get the 422 reasons, and a link gets "not available right now" (503). |
| Item that needs attention | It renders, and the bar warns (PV-15). |
| Trash | Trash Pricing. Its link answers "no longer available". Restore it, and the link works again. |
| 20 links | The 21st create is refused, and the dialog explains. |
| Guessing | 21 unknown secrets in a minute from one address give `429`. |
| Emergency | Set `Preview__LinksValidFrom` to now and restart. Every existing link is dead, and new ones work. |
| Import | Import an archive into S. S has no preview links afterwards. |

## 7. Performance (SC-003)

`PreviewBudgetTests` in the performance suite (`docker-compose.perf.yml`): p95 under 500 ms
for a 50-block page with two lists, on the reference setup.

## 8. Accessibility (manual, E-5)

With the keyboard and a screen reader:
- operate **Share preview**: create, copy, revoke and revoke all;
- operate the site's **Preview links** view;
- in a preview, reach and use "Hide bar";
- at desktop, tablet and mobile widths, compare a preview with the bar hidden against the
  published page: the layout is the same (FR-021).

Record the results in `quickstart-results.md`.
