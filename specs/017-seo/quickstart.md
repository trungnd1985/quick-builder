# Quickstart & Validation Guide

**Feature**: `017-seo` | **Date**: 2026-10-13

How to check, end to end, that published pages carry the right metadata, that the sitemap
and robots.txt stay current, and that redirects keep old links working. This guide is for
validation only. Details are in:
- tables, rules, error codes and cache keys: [data-model.md](./data-model.md);
- metadata in documents: [contracts/seo-metadata.md](./contracts/seo-metadata.md);
- structured data types: [contracts/structured-data.md](./contracts/structured-data.md);
- endpoints: [contracts/seo-api.yaml](./contracts/seo-api.yaml);
- what crawlers and visitors receive: [contracts/public-seo.md](./contracts/public-seo.md);
- readers and events: [contracts/module-surface.md](./contracts/module-surface.md);
- decisions, numbered `SE-xx`: [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation, sites, members, cookies and shorthands**: as in 016's quickstart:
  - sites `$S` (`site-a.test`, with a second host name `www.site-a.test`) and `$S2`;
  - cookies `$COOKIE` (Administrator), `$DESIGNER`, `$EDITOR`;
  - `H` is `-H "X-QuickBuilder-Request: 1"`.
- **Content from 016's quickstart**:
  - the "Blog posts" collection `$C` with 40 published posts;
  - its template under "Blog" (`/blog/{slug}`);
  - a published home page and a published "Blog" page.
- **Add to `$C`**: a switch field "Hide from search" (`$HIDE`).
- **Media**: one image `$IMG` in `$S`'s library, with alternative text.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  dotnet test tests/QuickBuilder.SmokeTests --filter Category=Performance   # SC-004, SC-006
  ```

---

## 1. Page metadata (US1, SC-001)

1. As `$EDITOR`, open "Services" in the editor and choose **Search and sharing**.
   1. Type a 70-character meta title. Expect a warning past 60, not a refusal. Type 201
      characters and expect a refusal.
   2. Set a description, pick `$IMG` as the sharing image, and leave the sharing title
      empty.
   3. Time the task from opening the panel to publishing (SC-001: under 2 minutes).
2. Before publishing, `curl -s https://site-a.test/services` shows the old head. Publish
   it, and the page shows:
   - `<title>`, `description`, `canonical`;
   - `og:title` equal to the meta title (its fallback);
   - `og:image` absolute, with its width, height and alt text;
   - `og:locale` and `og:site_name`.
3. Clear the description and republish. No `description` or `og:description` tag is
   written.
4. Set the title to `"><script>x</script>`, publish, and confirm it appears encoded in
   `<title>`, `content` and the JSON-LD.
5. As `$EDITOR`, try "Hide from search engines". The control is disabled, and a crafted
   draft save gets `403 pages.seo_restricted`. As `$DESIGNER`, it works; after publishing,
   the page has `robots noindex` and is not in the sitemap.
6. Set the site title suffix " | Acme" as `$DESIGNER`. Every page's title ends with it at
   the next request, with no republish (SE-08).

## 2. Redirects (US2, SC-002)

1. As `$DESIGNER`, create `/about-us.html` → the "About" page (301). Then:

   ```bash
   curl -sI https://site-a.test/about-us.html
   ```

   Expect `301`, `Location: /about`, and `Cache-Control: public, max-age=300`.
2. Create `/sale` → `/offers/autumn` (302). Expect `302` with `no-cache`. Request
   `/sale?utm=x` and expect `Location: /offers/autumn?utm=x`.
3. Move "About" under "Company". `/about-us.html` now redirects to `/company/about` in one
   hop (FR-018).
4. Import `fixtures/redirects-300.csv`: 297 valid rows, 2 duplicates of existing sources and
   1 loop. Expect `added: 297` and three refused lines with their codes (SC-002: under
   3 minutes including review). Export it, and confirm it re-imports with every row refused
   as a duplicate.
5. Import a Latin-1 file and expect `400 redirects.import_encoding`, with nothing added.
6. Publish a page at `/sale`. `/sale` now serves the page, and the list marks the redirect
   `hidden`.
7. Try sources `/api/x`, `/sitemap.xml` and `/` (each refused), a target
   `javascript:alert(1)` (refused), and `/a` → `/b` followed by `/b` → `/a` (`409
   redirects.loop`).
8. Query sources (FR-017a): import `/product.php?id=7` → page A and
   `/product.php?id=8` → page B. Then:
   - `?id=8` goes to B;
   - `?ref=mail&id=8` gives not found;
   - `/?p=12` → page C works even though the home page is live;
   - the admin warned when saving `/?p=12`.

## 3. Sitemap and robots.txt (US3, SC-005, SC-006)

1. `curl -s https://site-a.test/sitemap.xml` lists:
   - the home page, the live pages, and the 40 post addresses;
   - not "Services" while it is hidden;
   - nothing under an unpublished parent, and no draft-only post.

   Each `lastmod` matches the publish time. Validate it against the sitemap 0.9 XSD.
2. Publish a new post, unpublish another, and hide a page. Each change shows in the sitemap
   within 5 s on both instances (SC-005).
3. `curl -s https://site-a.test/robots.txt`: it has the default rules and an absolute
   `Sitemap:` line.
4. Add custom rules as Administrator; they follow the generated block. Turn on "Discourage
   search engines":
   - robots disallows `/`;
   - the sitemap is an empty `urlset`;
   - pages carry `noindex` and `X-Robots-Tag`;
   - the admin banner shows on every screen.

   Turn it off again.
5. With `./qb.ps1 seed-items -Collection blog-posts -Count 100000 -Publish 100`:
   - `sitemap.xml` is an index of `sitemap-1.xml` to `sitemap-3.xml`;
   - every address appears exactly once;
   - each file returns within 2 s on a miss on the reference setup (SC-006);
   - `sitemap-4.xml` gives not found.
6. Under a base path (`Hosting:BasePath=/cms`), the sitemap's addresses and the robots
   `Sitemap:` line include `/cms`.

## 4. Metadata templates (US4)

1. On the post template, as `$DESIGNER`:
   - bind the title to `{Title} | Acme blog`;
   - bind the description to Summary, with the fallback "Stories from Acme.";
   - bind the sharing image to Cover image;
   - bind "Hide from search engines" to `$HIDE`;
   - map **Article**: headline Title, image Cover, author Author → Name.

   Publish the template.
2. A post with a long Summary has a description cut at a word with `…`. A post without one
   has the fallback.
3. Turn `$HIDE` on for one post and publish it. That post has `noindex` and leaves the
   sitemap; the other 39 stay.
4. Delete the Summary field. The template marks the binding broken, and item pages use the
   fallback.
5. Republish one post's Summary. Only that post page's metadata changes (its key moves);
   the others stay cached.

## 5. Redirect offers (US5, SC-003)

1. Change the slug of "Our team" from `team` to `people`. The dialog shows "Redirect old
   addresses (301)", ticked. Save it, and `/team` goes to `/people` at once (same
   transaction).
2. Move "Blog" under "Resources" with the offer ticked. The response reports one prefix
   redirect. `/blog/hello-world` goes to `/resources/blog/hello-world`, and a post published
   afterwards is covered too.
3. Move "Blog" back. The new prefix redirect is offered, and the earlier one is turned off
   (it would loop); the response lists it in `turnedOff`.
4. As `$EDITOR`, change a post's slug and publish with the offer ticked. The old address
   redirects within the outbox delay. Repeat with a scheduled publish, and with a rollback to
   a version with the old slug.
5. Untick the offer on another slug change. The old address gives not found.
6. Unpublish a page, choosing "Redirect to: Home". The address goes to `/`.
7. SC-003: run `RedirectOfferTests`; every case reaches its content in one hop.

## 6. Canonical addresses and host names (US6)

1. `https://site-a.test/Blog/` → `301 /blog`. `/blog?page=3` names itself (with `?page=3`)
   as canonical. `/blog?utm=x` names `/blog`.
2. Set a canonical override on a near-copy page to the original. Its canonical tag names the
   original, and it is left out of the sitemap.
3. On `$S` (upgraded, setting off), `https://www.site-a.test/blog` serves the page with a
   canonical on `site-a.test`. As Administrator, turn the redirect on and confirm the dialog.
   Then:
   - `www.site-a.test/blog?x=1` → `301 https://site-a.test/blog?x=1`;
   - `www.site-a.test/_media/…` is still served directly.
4. A new site has the setting on.

## 7. Structured data (US7, SC-007)

1. Run `StructuredDataGoldenTests`. Validate the JSON-LD of a post, the home page, a page
   with a YouTube embed and a page with a list against the schema.org vocabulary (Schema
   Markup Validator, offline copy of the vocabulary). Record any warnings in the results.
2. Rename "Blog" and publish it. Every post page's breadcrumb shows the new name at the next
   request, and unrelated pages stay cached.

## 8. Performance (SC-004)

`dotnet test tests/QuickBuilder.SmokeTests --filter "Category=Performance&FullyQualifiedName~Redirect"`:
- 10,000 redirects;
- p95 for redirect responses under 20 ms;
- page renders still within 012's budgets.

## 9. Isolation, keyboard, logs and upgrade

1. The isolation suite with `SeoIsolationProbe`: redirects, sitemap and settings of `$S2`
   never show on `$S`.
2. Using the keyboard only, with NVDA:
   - the Search and sharing panel;
   - redirect create, import and export;
   - the site settings panels;
   - the offer checkboxes (SC-008).

   axe reports no violations.
3. Grep the logs of a full run for slugs, sources, targets and titles: none.
4. Restore a 016 snapshot, start the new image, and run `UpgradeFrom016Tests`:
   - bodies are identical;
   - titles are unchanged;
   - the host setting is off;
   - `robots.txt` and the sitemap answer.
