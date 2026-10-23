# Contract: The Public Surface After Publishing

**Feature**: 012 (issue #12) | **Extends**: 009 `contracts/site-context.md` §1 |
**Research**: PR-05, PR-07, PR-08, PR-11, PR-12, PR-13

What a visitor's request gets from the Host-header-resolved site. The administrative surface
(`/api`, `/admin`, `/health`) is unchanged, except that an admin path on a client domain now
answers with the site's not-found page (§4).

---

## 1. Request handling order

1. **Host not held by an enabled site** → 009's `sites.not_resolved` JSON 404, byte for
   byte, unchanged (FR-030).
   - **Since 017**: a site set to redirect other host names answers `301` to its primary host
     name for every address except `/_media` and `/_assets` (017 public-seo.md §1).
2. **`/_media/…`** → Media (003), unchanged.
3. **`/_assets/blocks.{hash}.css`** → the block stylesheet (§5).
   - **Since 018**: **`/_forms/{formId}`** → Forms: `POST` takes a form submission; any other
     method → `405`, `Allow: POST` (018 [public-forms.md](../../018-forms-submissions/contracts/public-forms.md) §1).
4. **Any other path**:
   - A method other than `GET` or `HEAD` → `405`, `Allow: GET, HEAD`, empty body.
   - Otherwise the path is normalised (§2) and looked up. A live page gets §3; anything else
     gets the not-found page (§4).
   - **Since 017**: `/robots.txt` and the sitemap files answer first; a redirect whose source
     has a query is matched before content, and other redirects after content, before the
     not-found page. The full order is 017 [public-seo.md](../../017-seo/contracts/public-seo.md) §1.

---

## 2. Address normalisation

| Step | Rule |
|---|---|
| Base path | `Hosting:BasePath` is removed by the host before matching (001). |
| Decoding | Percent-decoded once. A path with `.` or `..` segments, `//`, a backslash, or control characters → not found. |
| Trailing slash | One trailing `/` is ignored, except for the root. |
| Case | Compared lower-case (invariant), because addresses are unique ignoring case (010). |
| Query string | Ignored for matching and caching. It is never echoed into the page. |

`/about?x=1` serves the page at `/about`, with `200`. **Since 017**, `/About` and `/about/`
answer `301` to `/about`, keeping the query, when a page or item page is there; with nothing
there they go on to redirects and the not-found page (017 research SE-06). Each page also names
its canonical address in its head (017 public-seo.md §3).

---

## 3. A published page

**Status**: `200`. For a `GET` with a matching `If-None-Match`: `304`, empty body, the same
`ETag`.

**Headers**:

| Header | Value |
|---|---|
| `Content-Type` | `text/html; charset=utf-8` |
| `ETag` | `"<base64url SHA-256 of the render key, 22 chars>"` (research PR-05) |
| `Cache-Control` | `public, no-cache` |
| `Content-Security-Policy` | `default-src 'none'; style-src 'self' 'sha256-<hash of the page's style block>'; img-src 'self'; frame-src https://www.youtube-nocookie.com https://player.vimeo.com; base-uri 'none'; form-action 'none'; frame-ancestors 'self'` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=()` |

**Since 018**: a page that holds a form adds `connect-src 'self'`, lists the form script's hash
in `script-src` beside any navigation script's, and answers `form-action 'self'` followed by the
origin of every external address its forms redirect to, instead of `form-action 'none'` (018
research FS-10, FS-11). A page re-rendered after a refused submission is answered with
`Cache-Control: no-store` and never enters the cache.

`HEAD` returns the same headers and no body.

**Body structure** (normative; the golden files in `tests/fixtures/rendering/golden/` are
the exact bytes for each valid example document):

```html
<!doctype html>
<html lang="{site.defaultLanguage}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{live version title, HTML-encoded}</title>
<link rel="stylesheet" href="{basePath}/_assets/blocks.{hash}.css">
<style>.qb-page{--qb-…:…;…}[data-qb="{nodeId}"]{--qb-…:…}…</style>
</head>
<body class="qb-published">
<div class="qb-canvas qb-page">{rendered root children}</div>
</body>
</html>
```

**Rules**:
- **No `<script>`, no inline event handlers, no `style` attributes.** A page ships 0 bytes
  of JavaScript (FR-028). *Exception added by 024*: a page that shows a Navigation block
  carries one inline `<script>`, written once after the first `<nav>`, of at most 2 KB. It
  only improves the menu (Escape, closing sibling and outside dropdowns, the collapse width);
  without it the menu stays usable. Its bytes are fixed, and that page's
  `Content-Security-Policy` adds `script-src 'sha256-<hash of the script>'`. Pages without a
  Navigation block carry no script and no `script-src`. See
  [024 public-navigation.md](../../024-navigation/contracts/public-navigation.md).
- **Blocks**: each block element carries `data-qb="{nodeId}"` only when it has per-node
  custom properties. It carries the same `qb-*` classes as the canvas component (page
  schema contract §9, made normative by this feature).
- **Component references**: rendered in place from the component's **live** version, with
  no wrapper element. A reference to a component with no live version renders nothing.
  This can't happen for a page published under FR-003, but the renderer doesn't rely on it.
- **Encoding** (FR-025):
  - text is HTML-encoded, and attributes are attribute-encoded;
  - URLs are checked against the page schema's scheme pattern, then encoded;
  - token values are written only as `var(--qb-…)` references or values from the design
    document, validated by its contract.
- **Links** (FR-026):
  - A page link becomes `{basePath}{target address}` when the target is live, and is
    otherwise rendered without a link:
    - a Button becomes a `span` with the same classes;
    - rich text keeps the text and drops the `a`.
  - External links keep their `href`.
  - "Open in new tab" adds `target="_blank" rel="noopener noreferrer"`.
- **Images** (FR-027):
  - an available file becomes `<img src="{basePath}/_media/{id:N}.{ext}" alt="{alt or
    empty when decorative}" loading="lazy" decoding="async">`;
  - a missing file leaves out the whole image element (and its link).
- **Embeds**: `<iframe src="https://www.youtube-nocookie.com/embed/{videoId}">` or
  `https://player.vimeo.com/video/{videoId}?dnt=1`, with `title="{title}"`,
  `loading="lazy"`, `allow="fullscreen; picture-in-picture"`, `referrerpolicy="strict-origin-when-cross-origin"`,
  inside the aspect-ratio wrapper the canvas uses.

---

## 4. The not-found page

Served for any unmatched path on an enabled site's host, and for `/api` or `/admin` on a
host that is not an admin host name when `Hosting:AdminHostNames` is set (009 S-13). **The
bytes are identical in both cases** (`NotFoundParityTests`).

- **Status and headers**:
  - `404`;
  - `Content-Type: text/html; charset=utf-8` and `Cache-Control: no-store`;
  - the security headers of §3, with that page's own style hash;
  - no `ETag`.
- **Body**:
  - §3's shell, with the site's live design and no components (FR-030);
  - the `notFound.title` text as the `h1` and `notFound.message` as a paragraph, from
    `public-texts/{site.defaultLanguage}.json`, falling back to `en.json`;
  - when the home page is live, a Button-styled link `notFound.home` to `{basePath}/`.
- **Nothing from the request** (path, query or host) is echoed, and nothing distinguishes
  "no page", "unpublished page" and "draft-only page".

---

## 5. The block stylesheet

- **Request**: `GET`/`HEAD /_assets/blocks.{hash}.css`, where `{hash}` is the first 12
  base64url characters of the SHA-256 of the embedded `blocks.css`.
- **Response**:
  - `200`, `text/css; charset=utf-8`;
  - `Cache-Control: public, max-age=31536000, immutable`;
  - `X-Content-Type-Options: nosniff`.
- **Any other name under `/_assets/`** gets §4.
- It is served on every enabled site's host names, and the content is the same for all.

---

## 6. Freshness guarantees

| Change | When visitors on any instance see it |
|---|---|
| Publish, rollback or unpublish of the page | The next request (PR-05) |
| Publish, rollback or unpublish of a component it uses | The next request |
| Design publish or rollback | The next request |
| Publish, unpublish or address change of a page it links to | The next request |
| Its own address change | The next request (new address serves it; old address gets §4) |
| Site default language or other site settings | The next request (`SiteDto.Version` in the key) |
| A media file it shows deleted | Within the outbox poll interval (5 s by default) |
| Site disabled | The next request (009) |

The cache being unavailable or cleared never changes a response, only its latency
(FR-034).
