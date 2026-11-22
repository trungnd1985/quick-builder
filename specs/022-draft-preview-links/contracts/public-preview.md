# Contract: The `/_preview` Surface

The preview responses, as HTTP. This adds to 012's `public-rendering.md` and does not
change it. Research: PV-05, PV-06, PV-08, PV-10 to PV-13, PV-15, PV-18.

## 1. Prefix and site

- **Prefix**: `/_preview`, under `Hosting:BasePath`. It answers on **every** host name. It is
  not site-resolved from `Host`, and admin host confinement does not apply.
- **Site**: the site comes from the link (§3) or the route (§2). The handler sets
  `ISiteContext` from it before any read.
- **Logs**: the path is never logged (`PublicPathRedactor`), because it may carry a secret.
- **Methods**: only GET and HEAD, except `POST /_preview/form`. Anything else gets `405`.

## 2. Member previews (signed-in members)

| Request | Answer |
|---|---|
| `GET /_preview/sites/{siteId}/pages/{pageId}` | the page draft (§4) |
| `…/pages/{pageId}?version={n}` | that retained version |
| `…/pages/{pageId}?item={itemId}` | a template page with that sample item |
| `GET /_preview/sites/{siteId}/items/{itemId}[?version={n}]` | the item draft or version through its template |
| `GET /_preview/sites/{siteId}/media/{file}` | any current file of the site, as `/_media` serves it |

**Access**:
- **Signing in**: needs the session cookie. Without it, or as a non-member of the site, the
  answer is `404` with the generic not-found body. Nothing reveals whether the id exists
  (FR-004).
- **Capabilities**: `pages.content.view` for pages, and `cms.collections.view` for items.
- **Where it is checked**: in the handler, not by the admin authorization filter, so an
  anonymous request gets the same 404 as a non-member.

**Errors**:
- `404` `preview.version_not_retained`;
- `409` `preview.no_template`;
- `422` `preview.invalid_draft`, with the reasons and locations (PV-15).

## 3. Link previews (anyone holding a link)

| Request | Answer |
|---|---|
| `GET /_preview/l/{secret}` | the link's target (§4) |
| `GET /_preview/l/{secret}?{list page parameters}` | the same target, another page of its lists (PV-06) |
| `GET /_preview/l/{secret}/media/{file}` | the file, only when the target references it (PV-11); else `404` |

**Validation**: on every request, uncached (PV-08). The link must:
- be found by hash;
- not be revoked or expired;
- be created after `Preview:LinksValidFrom`;
- have a creator who still holds the target kind's edit capability;
- have a target that can still be read (not trashed, deleted, or out of retention).

**Answers**:
- On any failure: **"This preview link is no longer available"**, `404`, byte-identical
  whatever the reason (FR-010).
- When the page draft fails the schema: **"This preview is not available right now"**,
  `503` with `Retry-After: 60` (PV-15).
- After more than 20 unknown-link failures a minute (or 200 an hour) from the same address
  group: `429` with `Retry-After`, until the window ends (PV-18).

## 4. The preview document

The page is rendered by the same composer and renderer as a published page (PV-04), from
the parts in PV-03, with these differences only:

1. **Bar**: the preview bar is the first element of `<body>`, outside the page root
   (PV-13).
2. **Head**:
   - `<meta name="robots" content="noindex, nofollow">` replaces the page's robots setting;
   - `<meta name="referrer" content="no-referrer">` is added.
3. **Addresses** (PV-06):
   - links to pages and items go to the public absolute address, or carry no `href`;
   - media goes to the preview media route of §2 or §3;
   - the stylesheet goes to `/_preview/assets/{file}`;
   - pager links stay in the preview.
4. **Forms** (PV-12):
   - each form posts to `/_preview/form`;
   - the form script is not included.
   - `POST /_preview/form` answers `200` with "This is a preview. Forms are not sent from
     previews." Nothing is stored or sent.

## 5. Headers on every `/_preview` response

```http
Cache-Control: no-store, private
X-Robots-Tag: noindex, nofollow
Referrer-Policy: no-referrer
X-Content-Type-Options: nosniff
Permissions-Policy: camera=(), microphone=(), geolocation=()
Content-Security-Policy: <as 012 PR-12 for the rendered page; default-src 'none' for the fixed pages>
```

**Never sent**: `ETag`, `Last-Modified` (except on media) or `304`.

**Media responses** carry the same `Cache-Control`, `X-Robots-Tag` and `Referrer-Policy`,
and otherwise follow `/_media`.

## 6. What never happens

- No preview response is read from or written to the render cache, and no public cache entry
  is discarded or refreshed by a preview (FR-023).
- `/_preview` never appears in the sitemap. `robots.txt` gains `Disallow: /_preview/`
  (PV-10).
- Public addresses never serve draft data (FR-022). An unpublished page's public address
  still answers 012's not-found page.
