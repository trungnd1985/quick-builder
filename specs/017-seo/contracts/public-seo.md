# Public Surface: SEO, Sitemap and Redirects

What visitors and crawlers receive. It extends 012 `contracts/public-rendering.md` and 016
`contracts/public-rendering.md`, and becomes their §6–§10 on merge. Decisions:
[research.md](../research.md).

## 1. Order of answers

For a request on a resolved, enabled site:

1. **Other host name** (SE-07): when the site's `redirectOtherHostNames` is on and the host is
   not the primary, `301` to `BaseUrl + path + query`, for everything except `/_media/…` and
   `/_assets/…`.
2. **Platform routes**: `/_media`, `/_assets`, `/robots.txt`, `/sitemap.xml`,
   `/sitemap-<n>.xml`.
3. **Query redirect** (requests with a query only): an enabled exact redirect whose source
   path and canonical query match.
4. **Live page** at the normalised path. When the raw path differs only by letter case or a
   trailing slash, `301` to the normalised path with the query (SE-06).
5. **Item page** (016), with the same normalisation redirect.
6. **Exact redirect** without a query.
7. **Prefix redirect**, longest source first.
8. **Not-found page** (012), the same bytes whatever the reason.

Methods other than GET and HEAD keep their `405` (012).

## 2. Redirect responses

| | 301 | 302 |
|---|---|---|
| Body | empty | empty |
| `Location` | absolute for `url` targets; `<base path><address>` otherwise | same |
| `Cache-Control` | `public, max-age=300` | `no-cache` |
| Query | carried when neither the source nor the target has one | same |

- **Prefix redirects** keep the rest of the path: with `/blog` → `/resources/blog`, the
  address `/blog/hello` goes to `/resources/blog/hello`.
- **Targets**: a `page` or `item` target that is not live gives the not-found page.
- **Normalisation and host redirects** are `301` with the same headers.

## 3. Head of a published page or item page

Written in this order, each value attribute-encoded. Tags marked † are written only when
they have a value. See research SE-05 for the fallbacks.

```html
<title>{title}</title>
<meta name="description" content="{description}">              †
<link rel="canonical" href="{canonical}">
<meta name="robots" content="noindex">                        † hidden page; "noindex, nofollow" on a discouraged site
<meta property="og:type" content="website|article">
<meta property="og:title" content="{og title}">
<meta property="og:description" content="{og description}">    †
<meta property="og:url" content="{canonical}">
<meta property="og:site_name" content="{site name}">
<meta property="og:locale" content="{xx_YY}">
<meta property="og:image" content="{absolute image}">          †
<meta property="og:image:width" content="{w}">                 †
<meta property="og:image:height" content="{h}">                †
<meta property="og:image:alt" content="{alt}">                 †
<script type="application/ld+json">{graph}</script>            †
```

- **`og:type`**: `article` on item pages, `website` elsewhere.
- **Discouraged site**: every public response also carries `X-Robots-Tag: noindex, nofollow`.
- **JSON-LD**: a data block, never executed. Its text is JSON with `<`, `>`, `&`, `'`, `+`,
  U+2028 and U+2029 escaped as `\uXXXX`. The CSP is unchanged.

## 4. Structured data graph

```json
{ "@context": "https://schema.org", "@graph": [
  { "@type": "BreadcrumbList", "itemListElement": [
      { "@type": "ListItem", "position": 1, "name": "Home", "item": "https://client.example/" },
      { "@type": "ListItem", "position": 2, "name": "Blog", "item": "https://client.example/blog" },
      { "@type": "ListItem", "position": 3, "name": "Hello world" } ] },
  { "@type": "Article", "headline": "Hello world", "image": "https://client.example/_media/…",
    "author": { "@type": "Person", "name": "Lan" }, "datePublished": "2026-10-01T08:00:00Z",
    "url": "https://client.example/blog/hello-world" } ] }
```

| Node | Written on |
|---|---|
| `BreadcrumbList` | every page and item page except the home page; the last crumb has no `item` |
| `WebSite` (+ `Organization` when named) | the home page |
| `VideoObject` | each Embed block showing a video |
| `ItemList` | each Collection list block whose collection has a live template |
| `Article` / `Product` / `Event` | item pages of a template with `seo.structuredData` |

Types and properties: [structured-data.md](./structured-data.md).

## 5. `sitemap.xml` and numbered files

- **Up to 50,000 addresses**:

  ```xml
  <?xml version="1.0" encoding="UTF-8"?>
  <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
    <url><loc>https://client.example/</loc><lastmod>2026-10-12T09:30:00Z</lastmod></url>
  </urlset>
  ```

- **More**: `sitemap.xml` is a `<sitemapindex>` of `<sitemap><loc>…/sitemap-1.xml</loc>
  <lastmod>…</lastmod></sitemap>`, and each `sitemap-<n>.xml` is a `urlset` of at most
  50,000 addresses.
- **Headers**: `Content-Type: application/xml; charset=utf-8`, strong `ETag`,
  `Cache-Control: public, no-cache`, and `304` on a matching `If-None-Match`.
- **Unknown file numbers**, and `sitemap-1.xml` on a site with a single file, give the
  not-found page.
- **Contents**: only indexable addresses (data-model §3.1), as canonical absolute addresses.
  On a discouraged site the `urlset` is empty.

## 6. `robots.txt`

`text/plain; charset=utf-8`, with `ETag` and `Cache-Control: public, no-cache`.

```text
User-agent: *
Disallow: /api/
Disallow: /admin/
Allow: /

Sitemap: {BaseUrl}/sitemap.xml
```

- **Discouraged site**: the first block becomes `User-agent: *` / `Disallow: /`.
- **Custom rules**: the Administrator's rules follow after a blank line and a `# Custom rules`
  comment.
- **Base path**: the file is served at `<base>/robots.txt` (SE-16).
