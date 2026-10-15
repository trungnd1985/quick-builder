# Public Rendering: the Navigation block

How a published page renders a menu. Additions to
`specs/012-publishing-rendering/contracts/public-rendering.md` (a new §, "Navigation", and a
CSP note). Decisions: research NV-09 to NV-15.

## 1. Inputs

For each `navigation` node in the page or its components:
1. the menu's **live** document and published name (`GetLiveMenusAsync`, `LiveMenu`). If it is absent, the block writes
   nothing. Publishing makes this impossible for live pages (FR-003a), so this is a guard;
2. page targets through `GetLinkTargetsAsync` (`Path`, `IsLive`, `LiveTitle`);
3. item targets through `IPublishedItemReader.GetAsync` and `GetLiveTemplatePatternsAsync`.

All three are batched across every Navigation block and every other link on the page.

## 2. Resolving entries (FR-022 to FR-024, NV-10)

| Target | Link written | Label when the entry has none |
|---|---|---|
| `page`, live | `{base}{path}` + `#{anchor}` if any | live title |
| `page`, a template page (016 TB-02: never live as a link target) | none | as not live |
| `page`, not live or missing | none | live title, or nothing (the entry is pruned unless it keeps children) |
| `item`, published, live template | `{base}{pattern with slug}` | published name |
| `item`, otherwise | none | published name, or nothing |
| `url` | `href`, scheme-checked (`LinkResolver`) | — (label required) |
| `anchor` | `#{anchor}` | — (label required) |
| `none` | none | — (label required) |

Pruning, depth first: `hidden` → drop with children; levels beyond `levels` → drop (their
ancestors keep the active-state marks); no link and no shown children → drop; no link with
shown children → label without a link. Nothing left → no output at all.

## 3. Active state (FR-027 to FR-030, NV-11)

Given the current page id (or item id) and the current path, without query or fragment:
1. **current**: entries targeting it; their link carries `aria-current="page"`;
2. **containing**: ancestors of current entries; class `qb-nav__item--containing`;
3. if nothing is current, **section**: the page entry, other than the home page, whose live
   path is the longest segment-wise prefix of the current path; class
   `qb-nav__item--section`; its ancestors are containing;
4. `url`, `anchor` and `none` entries are never current. The home page entry is current on
   `/` only.

Active entries use the block's `activeColor`.

## 4. HTML

```html
<nav class="qb-nav qb-nav--horizontal qb-nav--align-start qb-nav--collapse-mobile"
     aria-label="Main" data-qb-nav="n1" style="--qb-nav-color:…;--qb-nav-active:…">
  <details class="qb-nav__panel" open>
    <summary class="qb-nav__toggle">Menu</summary>
    <ul class="qb-nav__list qb-nav__list--l1">
      <li class="qb-nav__item"><a class="qb-nav__link" href="/" aria-current="page">Home</a></li>
      <li class="qb-nav__item qb-nav__item--containing" data-qb-current-path>
        <a class="qb-nav__link" href="/services">Services</a>
        <details class="qb-nav__sub" name="qb-n1-l1">
          <summary class="qb-nav__expander"><span class="qb-nav__hidden">Show submenu: Services</span></summary>
          <ul class="qb-nav__list qb-nav__list--l2">…</ul>
        </details>
      </li>
      <li class="qb-nav__item">
        <details class="qb-nav__sub" name="qb-n1-l1">
          <summary class="qb-nav__expander qb-nav__expander--label">Company</summary>
          <ul class="qb-nav__list qb-nav__list--l2">…</ul>
        </details>
      </li>
      <li class="qb-nav__item"><a class="qb-nav__link" href="https://status.example" target="_blank" rel="noopener noreferrer">Status<span class="qb-nav__hidden"> (opens in a new tab)</span></a></li>
    </ul>
  </details>
</nav>
```

- The `qb-nav__panel` wrapper exists only with `collapse.enabled`, and is always written
  `open`.
- `data-qb-nav` is a per-page counter (`n1`, `n2`, …), so `name` groups never join two
  blocks.
- In a **vertical** block, `<details class="qb-nav__sub">` carries `open` when its entry is
  current, containing or section (FR-019a). In a **horizontal** block it never does. Its
  current-path entries carry `data-qb-current-path` for the script.
- Every label, `aria-label`, address and button text is encoded for its context
  (`HtmlWriter`, Principle IX). Visually hidden texts come from `PublicTexts` in the site's
  default language.

## 5. Stylesheet

Added to `blocks.css`, which the editor canvas and published pages share (011 ED-09):
- horizontal: a flex row; `.qb-nav__sub > ul` is an absolutely positioned dropdown; level 3 opens
  beside level 2;
- vertical: a block list; submenus open inline, indented;
- `.qb-nav__toggle` hidden by default. Inside `@container page (max-width: 480px)` for
  `--collapse-mobile`, or `(max-width: 767px)` for `--collapse-tablet`, it is shown, and the
  list stacks vertically with inline submenus;
- focus styles visible on links and summaries; hit areas at least 44 × 44 CSS px at mobile
  width;
- `prefers-reduced-motion` respected (no transition).

## 6. Script and CSP (NV-14)

- When the page has at least one rendered Navigation block, one inline
  `<script>` (the `NavScript` constant, at most 2 KB) is written right after the first
  `<nav class="qb-nav">`.
- The page's `Content-Security-Policy` then adds `script-src 'sha256-{NavScript.Hash}'`. Pages
  without a Navigation block keep 012's policy unchanged, with no `script-src`, so
  `default-src 'none'` forbids every script.
- Behaviour: Escape closes the innermost open `qb-nav__sub` (else the open panel when
  collapsed) and focuses its `summary`; opening a `qb-nav__sub` closes its `name` siblings
  where the browser does not; a click outside a horizontal block closes its open dropdowns;
  a `ResizeObserver` on each collapsible block closes the panel below its width and opens it
  above; opening a collapsed horizontal panel opens its `data-qb-current-path` submenus.
- No network access, no storage, no content read, no globals beyond one IIFE.

## 7. Caching

Unchanged keys. The menu is part of the page's render and its cache entry. Pages refresh
through their generation (data-model §3, research NV-07). The ETag still derives from the key,
so a menu change gives every page that shows it a new ETag.
