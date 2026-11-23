# Search engines, sitemaps and redirects

Each published site describes itself to search engines and keeps old links working. Nothing
here needs configuration.

## robots.txt and sitemaps

- Each site answers `/robots.txt`, `/sitemap.xml` and, past 50,000 addresses, numbered files
  `/sitemap-1.xml`, `/sitemap-2.xml` and so on, listed by `/sitemap.xml` as an index.
- **Under a base path** (`Hosting:BasePath`, such as `/cms`), these answer at
  `/cms/robots.txt`. Crawlers only ever read `/robots.txt` at the root of a host name, so
  the reverse proxy must forward `/robots.txt` to `/cms/robots.txt`. The sitemap addresses
  inside it already carry the base path.
- The sitemap is rebuilt on the first request after anything it lists changes (a publish, an
  unpublish, a hidden page, a moved template), on every instance. Builds happen only when
  requested; a busy site that publishes items all day rebuilds on each crawler visit.
- **Discourage search engines** (a site setting for Administrators) makes `robots.txt`
  disallow everything, marks every page and file `noindex, nofollow`, and empties the
  sitemap. Every admin screen of that site says so until it is turned off.

## Host names

- **Redirect other host names to the primary one** is a site setting for Administrators. It
  is on for sites created after the upgrade and off for existing ones. When it is on, every
  public address on another host name answers `301` to the same address on the primary
  host name. Media files (`/_media`) and the block stylesheet (`/_assets`) keep answering on
  every host name, so images in old emails still load.
- Before turning it on, make sure the primary host name reaches the site. The admin asks for
  confirmation and lists every host name that will redirect.

## Address normalisation

- `/Blog/`, `/BLOG` and `/blog/` now answer `301` to `/blog` when a page or item page is
  there, keeping the query. Before 017 they served the page directly. An address with
  nothing behind it goes on to the redirects and then the not-found page, unchanged. The home
  page and the base path root are never redirected for a trailing slash.

## Redirects

- Each site holds at most 10,000 redirects, managed under the site's **Redirects** page and
  imported or exported as CSV (1 MiB, 5,000 rows per file).
- A redirect answers only when no live page or item page holds its address, except a redirect
  whose source has a query, which is checked first.
- **Use counts** ("used N times, last on …") are counted in memory on each instance and saved
  about once a minute. A restart loses at most the last minute's counts; nothing else depends
  on them.
- When an item's slug changes and the member keeps the old address, its redirect is created
  by a background message within a few seconds. Until then the old address gives the
  not-found page. Page redirects are created with the change itself.
- Every change to a redirect, and to a site's search settings, is recorded with who made it
  and when (ids only, never the addresses).
