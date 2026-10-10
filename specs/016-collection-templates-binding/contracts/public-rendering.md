# Public Rendering Changes: Item Pages and Lists

Extends [012 contracts/public-rendering.md](../../012-publishing-rendering/contracts/public-rendering.md).
Everything not stated here is unchanged: methods, headers, CSP, `304` handling and the
not-found page.

## 1. Matching an address (FR-005, TB-03)

For a `GET` or `HEAD` on a resolved, enabled site, after the path is normalised (012 §2):

1. **Platform routes** (`/api`, `/admin`, `/health`, `/_media`, `/_assets`): unchanged.
2. **Static page**: a live page whose address equals the path. Template pages never match
   here.
3. **Template**: when the path has at least two segments and its last segment is a valid
   slug (at most 100 characters), the live template whose parent's address is the path
   without its last segment, and the published item of the template's collection whose
   published slug is that segment.
4. **Not found**: 012's not-found page, `404`, the same bytes whatever the reason (no
   template, template not live, item draft-only, unpublished, or of another collection or
   site) (FR-006).

## 2. Query parameters (FR-027, TB-11)

- Only the parameters named by the page's paginated lists (`paging.param`, default `page`)
  are read. Every other parameter is ignored. It changes neither the output nor the cache
  key.
- **Values**:
  - a whole number from 2 to 10,000 selects that page;
  - `1`, a missing value, or any other value means the first page.
- **Past the last page**: the list shows its empty state and a link to its first page. The
  response is `200`.
- **Links**: written with the page's address and every list's current parameter, changing
  only the one list's. Page 1 is written without its parameter.

## 3. Paging markup

```html
<nav class="qb-pager" aria-label="{list layerLabel or 'Pages'}">
  <a rel="prev" href="/blog">Previous</a>
  <a href="/blog">1</a> <a aria-current="page" href="/blog?page=2">2</a> <a href="/blog?page=3">3</a>
  <a rel="next" href="/blog?page=3">Next</a>
</nav>
```

- At most 7 numbered links: the first, the last, and the current page with up to two
  neighbours on each side, with `…` between gaps.
- Labels come from the public texts in the site's default language (012 `PublicTexts`).
- There is no script.

## 4. Item pages

- **Document title**: the item's name, else the template's title (FR-030).
- **ETag and caching**: from `ItemRenderKey` (data-model §6). `Cache-Control: public,
  no-cache`, as for pages.
- **Item links** (`item: "url"`): the live template's pattern with the published slug,
  under the site's base path. When there is no live template, the link renders as plain
  text (012 FR-026).

## 5. Lists

- **Markup**: a list renders as `<ul class="qb-list" style="--qb-cols:N">` with one `<li>`
  per card, then the pager when paging is on. The empty state renders in place of the
  `<ul>`.
- **Styling**: list rules are added to the block stylesheet (`/_assets`), so
  `RendererStamp` changes once with this release.
- **Budget**: at most 200 list queries per render (TB-09). Lists beyond that show their
  empty state, and a warning with ids only is logged.
