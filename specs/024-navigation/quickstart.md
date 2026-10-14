# Quickstart & Validation Guide

**Feature**: `024-navigation` | **Date**: 2026-10-14

How to check, end to end, that menus are built once, shown by the Navigation block on every
page that uses it, kept correct when their targets change, and marked with the visitor's
place. This guide is for validation only. Details are in:
- tables, guards, error codes and the refresh graph: [data-model.md](./data-model.md);
- the menu document: [contracts/menu-document.md](./contracts/menu-document.md);
- the Navigation block: [contracts/navigation-block.md](./contracts/navigation-block.md);
- endpoints: [contracts/menus-api.yaml](./contracts/menus-api.yaml);
- what visitors receive: [contracts/public-navigation.md](./contracts/public-navigation.md);
- readers, events and export: [contracts/module-surface.md](./contracts/module-surface.md);
- decisions, numbered `NV-xx`: [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation, sites, members, cookies and shorthands**: as in 017's quickstart: sites `$S`
  and `$S2`; cookies `$COOKIE` (Administrator), `$DESIGNER`, `$EDITOR`; `H` is
  `-H "X-QuickBuilder-Request: 1"`.
- **Content from 016 and 017**:
  - published pages Home `/`, Services `/services`, Web design `/services/web-design`,
    Hosting `/services/hosting`, Managed hosting `/services/hosting/managed`, About `/about`,
    Careers `/about/careers`, Contact `/contact`, and Blog `/blog`;
  - the "Blog posts" collection `$C`, its template `/blog/{slug}`, and a published post `$POST`;
  - a published header component `$HEADER` shown by every page.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  dotnet test tests/QuickBuilder.SmokeTests --filter Category=Performance   # SC-005
  ```

---

## 1. Build a menu (User Story 1)

1. As `$DESIGNER`, open **Menus** and create "Main". Creating a second menu named "main" is
   refused (`pages.duplicate_menu_name`).
2. Add the entries of the spec's User Story 1, leaving page labels empty. Save the draft.
   Reload: the tree, labels, targets, anchor and new-tab flags are unchanged.
3. Try each refusal and confirm the reason and pointer:
   - a fourth level (the action is not offered, and a crafted request gives `too_deep`);
   - a `url` entry without a label (`label_required`);
   - `javascript:alert(1)` as an address (`entry_invalid`);
   - `#form` as an anchor: the editor strips the `#`; `1form` is refused.
4. As `$EDITOR`, open Menus: the list and the menu are visible, but there are no edit controls,
   and a crafted `PUT …/draft` gives `403`.
5. Duplicate "Main" as "Footer". Its draft is a copy, and it is not published.

## 2. Place it and publish (User Story 2)

1. Open `$HEADER` in the editor and add a **Navigation** block: menu "Main", horizontal, 3
   levels, collapse on, below mobile. The canvas shows the draft menu. At the mobile preview
   width, a "Menu" button replaces the row.
2. Publish `$HEADER`. It is refused with `pages.unpublished_menus` naming "Main" (FR-003a).
3. Publish "Main", then `$HEADER`. Request `/about`:
   - one `<nav aria-label="Main">` with the live entries, Contact linking to `/contact#form`;
   - Status with `target="_blank" rel="noopener noreferrer"` and the hidden "(opens in a new
     tab)";
   - a CSP with `script-src 'sha256-…'` matching the inline script;
   - a page that shows no menu (the not-found page, a page without the header) has 012's CSP
     unchanged.
4. Keyboard only: Tab to "Services"'s expander, press Enter to open, Escape to close. Focus
   returns to the expander. Opening "Company" closes "Services".
5. With scripts disabled: every entry is reachable, and the expanders still open and close.
6. Narrow the window below 480 px: the menu button appears, and the panel starts closed with
   no flash of the open menu. Open it: the submenus leading to the current page are open
   (FR-019a).
7. Try to unpublish or delete "Main": refused with `pages.menu_in_use_by_published_content`,
   showing the counts.

## 3. Reorder (User Story 3)

1. Drag "Contact" above "Company", drag "Hosting" (with its child) to the top level, and drop
   "Careers" under "About". Save, reload, and confirm the order.
2. Hover a drop that would create a fourth level: it is shown as not allowed, and dropping
   changes nothing.
3. Repeat the moves with `Alt+↑`, `Alt+↓`, `Alt+→` and `Alt+←`. A screen reader announces each
   move. `Ctrl/⌘+Z` undoes the last one.
4. Publish "Main". Every page that shows the header has the new order on its next request, and
   no page was republished.

## 4. Link integrity (User Story 4, SC-003)

For each step, request `/about` on two instances with a shared cache.

| Step | Expected on the site | Expected in Menus |
|---|---|---|
| Change Web design's slug to `websites` | link is `/services/websites` | live |
| Move Hosting under About | link follows the move | live |
| Unpublish Careers (warning lists "Main") | Careers gone; About still links | not published |
| Publish Careers again | Careers back | live |
| Delete `$POST` (warning lists "Main") | "Latest post" gone | deleted, listed at the top |
| Unpublish the Blog template | item entries gone | no page for this item |
| Unpublish Services, which has shown children | "Services" shown as text, children linked | not published |

## 5. Active state (User Story 5, SC-004)

Use the menu as built in §1 (before §3 and §4 moved entries), with one more top-level entry
"Blog" targeting the Blog page, and publish it.

| Request | Current (`aria-current="page"`) | Containing | Section |
|---|---|---|---|
| `/` | Home | — | — |
| `/services/hosting/managed` | Managed hosting | Hosting, Services | — |
| a page `/services/hosting/faq` not in the menu | — | Services | Hosting |
| `/blog/<post>` with an entry for the post | Latest post | — | — |
| `/blog/<other post>` | — | — | Blog |
| `/blog?page=2` | Blog | — | — |

In a vertical block, the submenus of the current path are written `open`. In a horizontal
block none are.

## 6. Refresh and isolation (SC-002, FR-035)

1. `MenuInvalidationTests`: every row of research NV-07's table refreshes exactly the pages
   that show the menu, within 5 s on both instances, and no other page changes its ETag.
2. A `PUT …/draft` on `$S` with a `pageId` of `$S2` is refused with `unknown_target`.
   `MenuIsolationProbe` passes.

## 7. Performance (SC-005)

`MenuRenderPerformanceTests`: a page with two 300-entry menus, cache miss and hit, p95 within
012's budgets (200 ms and 20 ms) on the reference setup.

## 8. Accessibility (SC-006)

- The admin specs with axe pass for MenusView, MenuEditorView, MenuEntryDialog and the
  Navigation block controls.
- Manual (until #25, exception E-5): NVDA with Firefox and VoiceOver with Safari on the
  published menu (landmark name, expander states, current page) and on the menu editor's
  keyboard moves.

## 9. Upgrade

`UpgradeFrom017Tests`: a 017 database snapshot upgrades. Pages without a Navigation block
render the same body and CSP, and no stored document changed.
