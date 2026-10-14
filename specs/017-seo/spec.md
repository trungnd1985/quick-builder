# Feature Specification: SEO, Sitemap and Redirects

**Feature Branch**: `017-seo`

**Created**: 2026-10-13

**Status**: Draft

**Issue**: #17

**Input**: User description: "#17 SEO, sitemap and redirects. Problem / goal: A published site must be indexable and must not break links when content moves. A user can set metadata per page, define metadata templates for collection items, and manage redirects. The site exposes a sitemap and robots.txt. In: meta title/description and Open Graph fields per page, metadata templates with bindings for collection templates, canonical URLs, automatic sitemap.xml per site, robots.txt, 301/302 redirect management with import, automatic redirect suggestion when a slug changes, structured data for common block types. Out: analytics integrations, per-locale sitemaps."

## Context

Features 009 to 016 give each site host names with one primary address, a page tree,
a visual editor, publishing with server-side rendering and exact cache refreshes,
collections whose items are published separately, and collection templates that give
every published item its own address, such as `/blog/hello-world`.

Search engines and link previews still see very little. A published page's document title
is its page title, and an item page's title is the item's name (016 FR-030). Pages have no
description, no preview image for social networks and no canonical address. The site has no
sitemap and no robots.txt. When a page or item changes address, or a template moves, the
old address gives the not-found page. Specs 010, 012 and 016 moved redirects from old
addresses to this feature, and 009 moved redirecting other host names to the primary one
here as well.

This feature adds four things:

- **search metadata** for every page, and metadata templates for collection templates whose
  values come from each item's fields;
- **canonical addresses**;
- a **sitemap and robots.txt** for each site that update when content is published;
- **redirects** that members manage, import, and are offered when an address changes.

Pages and items also gain **structured data** that describes them to search engines.

Constitution Principle VI applies because metadata templates reuse the binding expression
format from 016. Principle VII means search metadata is part of a page's draft and reaches
visitors only on publish, and the sitemap lists only published content. Principle VIII
means the sitemap, robots.txt and redirect responses are cached and refreshed exactly. Principle IX means
every value placed into metadata, structured data and redirect targets is encoded for its
context, and redirect targets are checked against allowed schemes.

## Clarifications

### Session 2026-10-13

- Q: When a member accepts the redirect offer for a template move that changes many item
  addresses, is the result one prefix rule or one redirect per item? → **A: one prefix
  rule** (for example `/blog/` → `/resources/blog/`). Prefix redirects are a second kind of
  redirect that members can also create, import and export (FR-015, FR-033).
- Q: What happens when a visitor arrives on a host name that is not the site's primary
  one? → **A: an Administrator setting per site, "Redirect other host names to the primary
  one"**, on by default for new sites. When on, those requests get a permanent redirect to
  the same address on the primary host name (FR-014).
- Q: Which structured data content types should item pages offer? → **A: Article, Product
  and Event.** Each type is defined once with its properties. More types can be added
  later without changing stored mappings (FR-035).
- Q: Should the product record when each redirect is used, and keep a list of addresses
  that visitors requested but got the not-found page? → **A: each redirect shows a hit
  count and last-used time, collected in memory and saved in the background; no not-found
  list** (FR-021a).
- Q: Should an Editor be able to keep a single collection item out of search results and
  the sitemap, and if so, how? → **A: the template's "Hide from search engines" can be
  bound to a switch field of the collection**; items with it on are not indexed and are
  left out of the sitemap (FR-007a).
- Q: Should a redirect's source address be allowed to include a query string, such as
  `/index.php?id=42`? → **A: yes, for exact redirects only.** The query must match exactly
  in any parameter order, and it is tried before the same path without a query. Prefix
  redirects never take a query (FR-017a).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Setting search and sharing metadata for a page (Priority: P1)

An Editor opens the "Services" page and its "Search and sharing" settings. They write a
meta title, "Web design services in Hanoi", and a two-sentence meta description. The
panel shows how the result could look in a search listing and warns that the description
is longer than search engines usually show. They pick a sharing image from the media
library and leave the sharing title empty, so it uses the meta title. They publish the
page. A link to it pasted into a chat app shows that title, description and image.

**Why this priority**: Metadata is what search engines and link previews show. Without it
every listing shows only the page title and whatever text the engine guesses. On its own
it makes published pages presentable in search results and shared links.

**Independent Test**: For a published page, set every metadata field, leave each optional
field empty in turn, and publish. Confirm that the published page carries exactly the
expected title, description, sharing fields and fallbacks, that the draft changes are not
visible before publish, and that values containing markup characters are shown as text.

**Acceptance Scenarios**:

1. **Given** a member who may save the page's draft,
   **When** they set the meta title, meta description, sharing title, sharing description
   or sharing image,
   **Then** the values are saved in the draft, and the published page is unchanged until
   the page is published.
2. **Given** a published page with metadata,
   **When** a visitor or crawler requests it,
   **Then** the page carries its meta title as its document title, its meta description,
   and sharing title, description, image, type and address for link previews.
3. **Given** a page whose meta title, description or sharing fields are empty,
   **When** it is published,
   **Then** each empty field uses its fallback (FR-005), and no empty description or
   sharing tag is produced.
4. **Given** a meta title or description longer than the recommended length (FR-004),
   **When** the member types it,
   **Then** the panel shows a non-blocking warning and a preview of how it may be
   shortened; values beyond the hard maximum are refused.
5. **Given** a page that should not appear in search results,
   **When** the member turns on "Hide from search engines",
   **Then** after publish the page tells crawlers not to index it and it is left out of
   the sitemap.

---

### User Story 2 - Keeping old links working with redirects (Priority: P1)

The site moved from another system, and old links such as `/about-us.html` appear in
search results and on other sites. A Designer opens Redirects, adds `/about-us.html` → the
"About" page as permanent (301), and imports a file of 300 more rows exported from the old
site. The import reports 297 added, 2 refused because their sources already have
redirects, and 1 refused because it would loop. A temporary (302) redirect sends `/sale` to
`/offers/autumn` during a campaign. A visitor who opens `/about-us.html` lands on `/about`.

**Why this priority**: Broken links lose visitors and search ranking. That is the second
half of the issue's goal, and moving to the product from another site needs it from the start.

**Independent Test**: Create, edit, disable and delete redirects of both kinds. Import a
file with valid, duplicate, looping, malformed and external rows. Request each source
address and confirm the status, target and query handling. Confirm that live content at a
source address wins over its redirect, and that changes reach every instance.

**Acceptance Scenarios**:

1. **Given** a member with the Designer or Administrator role,
   **When** they add a redirect with a source address, a target and a kind (permanent or
   temporary),
   **Then** from the next request on every instance, a request for the source address
   receives a redirect of that kind to the target.
2. **Given** a redirect whose target is a page or an item,
   **When** that page or item later changes address,
   **Then** the redirect follows it to the new address without creating a chain.
3. **Given** a new redirect that would create a loop, or whose source already has a
   redirect,
   **When** it is saved,
   **Then** it is refused with a reason naming the conflicting redirect.
4. **Given** a redirect without a query whose source address is also the address of live
   content (a published page or item page),
   **When** a visitor requests that address,
   **Then** the live content is served, and the redirect list marks the redirect as
   hidden by that content.
5. **Given** a redirect import file,
   **When** a member imports it,
   **Then** every valid row is added, every invalid row is reported with its line number
   and reason, and nothing is half-added for a refused row.
6. **Given** a redirect whose source has no query string, requested with a query string,
   **When** the redirect is followed,
   **Then** the query string is carried to the target unless the target has its own query
   string.
7. **Given** imported redirects from `/product.php?id=7` and `/product.php?id=8` to two
   different pages,
   **When** a visitor requests `/product.php?id=8`, or `/product.php?ref=mail&id=8`
   with an extra parameter,
   **Then** the first request goes to the second page, and the second request is matched
   only by a redirect without a query for `/product.php`, or gets the not-found page.
8. **Given** a site's redirects,
   **When** a member exports them,
   **Then** they receive a file in the same format the import accepts.

---

### User Story 3 - A sitemap and robots.txt that keep themselves current (Priority: P1)

The site owner submits `https://client.example/sitemap.xml` to a search console once.
When an Editor publishes a new blog post, it appears in the sitemap within seconds with its
last-changed date. When a page is unpublished or hidden from search engines, it disappears.
`https://client.example/robots.txt` allows crawling and names the sitemap. During the build
of a new site, the Administrator turns on "Discourage search engines". Robots.txt then asks
crawlers to stay away and every page tells crawlers not to index it, until the
Administrator turns the setting off at launch.

**Why this priority**: The sitemap is how search engines discover item pages, which
nothing else links to until lists are built. Robots.txt controls whether a site is crawled
at all. Both are expected on every public site.

**Independent Test**: On a site with published, draft-only, unpublished, hidden and
scheduled pages, a published template with items in every state, and pages under an
unpublished parent, request the sitemap and robots.txt. Confirm exactly the expected
addresses with correct last-changed dates. Publish, unpublish and hide content, move a
template, and toggle "Discourage search engines". After each change, confirm the update.
Repeat on two instances and on a site with more addresses than one sitemap file may hold.

**Acceptance Scenarios**:

1. **Given** a site,
   **When** its sitemap is requested on any of its host names,
   **Then** it lists the absolute canonical address on the primary host name of every
   published page and every published item page that is indexable (FR-022), each with its
   last-published time.
2. **Given** a sitemap,
   **When** a page or item is published, unpublished, hidden, moved or deleted, or a
   template changes,
   **Then** the sitemap reflects the change from the next request on every instance.
3. **Given** a site with more addresses than one sitemap file may hold,
   **When** the sitemap is requested,
   **Then** it is a sitemap index pointing to numbered sitemap files that together list
   every address once.
4. **Given** a site,
   **When** robots.txt is requested,
   **Then** it allows crawling of the public site, keeps crawlers out of the
   administrative and reserved addresses, and names the sitemap's absolute address.
5. **Given** "Discourage search engines" turned on,
   **When** robots.txt, the sitemap or any page is requested,
   **Then** robots.txt asks all crawlers not to crawl, every page tells crawlers not to
   index it, and the sitemap is empty.
6. **Given** an Administrator who adds custom rules to robots.txt,
   **When** robots.txt is requested,
   **Then** the custom rules appear after the generated ones, and the sitemap line is kept.

---

### User Story 4 - Metadata templates for collection items (Priority: P2)

On the "Blog post" template, the Designer opens "Search and sharing". They bind the meta
title to "{Title} | Acme blog", the meta description to the post's Summary, with the first
words of the Body as fallback, and the sharing image to Cover image. Every one of the 40
post pages now has its own title, description and preview image. When an Editor publishes
an edited Summary, that post's description changes with it.

**Why this priority**: Without templates, every item page would share one description and
one image. It builds on the binding from 016 and on User Story 1.

**Independent Test**: On a template, bind every metadata field to compatible fields,
including combined text, a single-reference value and a fallback. For items whose values
are present, empty and too long, confirm each item page's metadata. Confirm that changing
an item's published data changes only that item's metadata.

**Acceptance Scenarios**:

1. **Given** a template page,
   **When** the member opens the binding control of a metadata field,
   **Then** they are offered the item fields that can fill it, with the same binding and
   formatting choices as block properties (016 FR-014 to FR-019).
2. **Given** bound metadata,
   **When** an item page is served,
   **Then** each field shows the value from the item's published data. A rich text value
   becomes plain text, and a value longer than the hard maximum is shortened on a word
   boundary.
3. **Given** a bound metadata field whose value is empty for an item,
   **When** the item page is served,
   **Then** the field's fallback text is used, then the field's default fallback (FR-005).
4. **Given** a binding in metadata to a field that is removed or retyped,
   **When** the template is opened,
   **Then** the binding is marked as broken, as for blocks (016), and item pages use the
   fallback.
5. **Given** an item page,
   **When** the template's metadata or the item's published data changes,
   **Then** that item page's metadata is refreshed with the rules for item pages (016
   FR-032).
6. **Given** a template whose "Hide from search engines" is bound to a switch field,
   **When** an Editor turns that switch on for an item and publishes it,
   **Then** that item page tells crawlers not to index it and leaves the sitemap, while
   the collection's other item pages stay indexable.

---

### User Story 5 - Being offered a redirect when an address changes (Priority: P2)

The Designer renames the slug of "Our team" from `team` to `people`. Before publishing,
the admin warns that `/team` will stop working and offers "Redirect old address to the new
one (301)", ticked by default. They publish, and `/team` now redirects to `/people`. Later
the Designer moves "Blog" under "Resources". The warning says 40 published post addresses
will change, and offers to redirect them all. An Editor changes a post's slug and is offered
the same for that post.

**Why this priority**: Most broken links come from the product's own address changes.
Specs 010, 012 and 016 defer this to #17. It builds on User Story 2.

**Independent Test**: Change slugs, move pages with descendants, move and un-template
templates, and change item slugs. Accept the offer for some and decline it for others.
Confirm the offered redirects, that accepting creates them with the publish, that declining
leaves the old address on the not-found page, and that earlier redirects to the old address
are updated rather than chained.

**Acceptance Scenarios**:

1. **Given** a change that will alter the live address of a published page, its published
   descendants or published item pages,
   **When** the member who makes it is warned before it goes live (012 FR-011, 016 FR-008),
   **Then** the warning offers to redirect every affected old address to its new one,
   permanent and turned on by default.
2. **Given** the offer accepted,
   **When** the change goes live,
   **Then** redirects for page changes are created in the same change, redirects for item
   changes are in place within 5 seconds (FR-029), and existing redirects whose target was
   an old address now point to the new one.
3. **Given** the offer declined,
   **When** the change goes live,
   **Then** no redirect is created, and the old addresses give the not-found page.
4. **Given** a change that moves a template with many published items,
   **When** the offer is shown,
   **Then** it states how many addresses are affected, and accepting creates one prefix
   redirect from the old pattern's prefix to the new one, which also covers items
   published later.
5. **Given** a page or item that is unpublished (now or by a schedule) or deleted,
   **When** the action is confirmed,
   **Then** the member is offered to redirect its address to a page they choose, not
   ticked by default.
6. **Given** a scheduled item publish that will change the item's slug,
   **When** the member schedules it,
   **Then** their choice about redirects is kept and applied when the schedule runs.

---

### User Story 6 - Canonical addresses and one primary host (Priority: P2)

The site answers on `client.example`, `www.client.example` and an old domain. Search
engines index one address per page. Every page names its canonical address on the primary
host name, and paged lists name their own page. A product page has a near-copy for a
campaign, and its canonical address points back to the original.

**Why this priority**: Duplicate addresses split ranking and confuse search engines.
This is cheap once metadata exists, and it covers the host name question that 009 deferred.

**Independent Test**: Request pages on every host name of a site, with and without
trailing slashes, in other letter cases, with unknown query parameters and with list page
numbers. Confirm each page's canonical address and the redirect behaviour of other host
names. Set a canonical override on one page and confirm it.

**Acceptance Scenarios**:

1. **Given** a published page or item page,
   **When** it is requested on any of the site's host names,
   **Then** it names as canonical its own address on the primary host name, with the
   installation's base path, normalised in letter case and trailing slash, without
   query parameters except list page numbers other than the first.
2. **Given** a page with a canonical override set to another page of the site or to an
   absolute web address,
   **When** it is published,
   **Then** it names that address as canonical, and it is left out of the sitemap.
3. **Given** a site with more than one host name,
   **When** a visitor requests any public address on a host name that is not the primary
   one,
   **Then** with the site's "Redirect other host names to the primary one" setting on, the
   visitor receives a permanent redirect to the same address and query on the primary host
   name; with it off, the page is served and names its canonical address on the primary
   host name.
4. **Given** an address requested with a trailing slash or different letter case,
   **When** it matches a page,
   **Then** the page is served or redirected permanently to its canonical form, so that
   one address per page is indexed.

---

### User Story 7 - Structured data that describes the content (Priority: P3)

A search engine reading the blog post page finds structured data with the post's headline,
image, author and published date, and a breadcrumb trail "Home › Blog › Hello world". The
home page describes the site and its organisation. A page with a video embed describes the
video, and a page with a list of posts describes it as a list of those pages.

**Why this priority**: Structured data can enrich search results, but sites work in search
without it. It builds on the stories above.

**Independent Test**: Publish pages that contain each block type that produces structured
data, item pages of a collection mapped to each supported type, and nested pages. Confirm
each page's structured data against the expected golden output, that it passes the
schema.org vocabulary rules, that unpublished items never appear in it, and that markup in
values is encoded.

**Acceptance Scenarios**:

1. **Given** a published page that is not the home page,
   **When** it is served,
   **Then** it carries a breadcrumb trail of its published ancestors, using each one's
   page title and canonical address.
2. **Given** the site's home page,
   **When** it is served,
   **Then** it carries a description of the website (name, address) and of the
   organisation named in site settings, with its logo when one is set.
3. **Given** a page containing blocks that produce structured data (FR-034),
   **When** it is served,
   **Then** each such block adds its description, built only from the published values
   it shows.
4. **Given** a template whose collection is mapped to a content type (FR-035),
   **When** an item page is served,
   **Then** it carries a description of that type, filled from the mapped fields of the
   item's published data, and leaves out properties whose values are empty.

---

### Edge Cases

- **Redirect source is a reserved address** (the admin, media or `sitemap.xml` /
  `robots.txt` addresses): refused with a reason.
- **Redirect target is an address on another host name of the same site**: allowed as an
  absolute address on that host name. An absolute target on the site's primary host name is
  stored as a site path, so that it follows a change of primary host name.
- **Redirect target uses a scheme other than `http` or `https`**, or is
  protocol-relative: refused, so redirects cannot send visitors to scripts or files.
- **Chains**: a new redirect whose target is the source of another redirect is saved as a
  redirect to the final target. The list shows that it was shortened.
- **Prefix redirect whose prefix holds live content** (a static page `/blog/archive` left
  behind when the template moved): the live content is served (FR-016), and other
  addresses under the prefix still redirect.
- **Template moved back to its old place**: the prefix redirect from the new place is
  offered, and the earlier prefix redirect, whose source is now live again, is turned off
  instead of forming a loop.
- **Loops**, including one created by a page moving onto a redirect's target: refused
  when a member creates them. When a page move would create a loop, the looping redirect is
  turned off as part of the move, and the result of the move names it.
- **A page is created or published at a redirect's source address**: the admin warns that
  the redirect will be hidden. The content is served (FR-016).
- **An offered redirect whose old address now belongs to other live content** (two pages
  swapped slugs): that redirect is not offered.
- **Item page whose slug has unusual characters**: the sitemap and canonical address use
  the item's address encoded as a valid web address.
- **Address of a disabled site or unknown host name**: robots.txt, the sitemap and
  redirects respond as for any other address of that site, with the not-found response
  from 012.
- **The home page is not published**: it is not in the sitemap. The breadcrumb trail of
  other pages starts with the site name, without a link.
- **Pages under an unpublished parent**: a published page keeps answering at its address
  when its parent is not published (012), so it is listed like any other live page.
- **Templates whose collection has thousands of items**: they are all listed, split across
  sitemap files as FR-024 describes.
- **Sharing image deleted from the media library**: the page keeps working without a
  sharing image. The admin marks the field as missing, as for Image blocks (013).
- **Metadata containing quotes, angle brackets or script-like text**: shown as text in
  every context; never breaks out of an attribute or the structured data.
- **Import file with a byte-order mark, different line endings or a header row**: accepted.
  A file in another encoding is refused as a whole with a reason.
- **Import larger than the limits (FR-019)**: refused as a whole before any row is added.
- **Two members editing the same redirect**: the second save is refused as changed by
  someone else, as for other admin records.
- **Rolling back an item to a version with a different slug** (015): treated as an
  address change, with the same redirect offer. A page's address is not part of its
  versions (012), so rolling back or scheduling a page never moves it.

## Requirements *(mandatory)*

### Functional Requirements

**Page metadata**

- **FR-001**: Each page MUST have optional search and sharing settings: meta title, meta
  description, sharing title, sharing description, sharing image (from the site's media
  library), canonical override, "hide from search engines", and "leave out of sitemap".
- **FR-002**: Search and sharing settings MUST be part of the page's draft. They MUST reach
  visitors only when the page is published, MUST be kept in each published version, and
  MUST follow rollback (012). Members who may save the page's draft MUST be able to edit
  them. The canonical override and "hide from search engines" MUST be limited to the
  Designer and Administrator roles.
- **FR-003**: Each published page MUST state the site's default language as its language
  and MUST carry its document title, meta description, sharing title, description, image with its
  dimensions and alternative text, sharing type (`website` for the home page, `article` for
  item pages, otherwise `website`), canonical address, site name, and a
  robots directive when the page must not be indexed.
- **FR-004**: The meta title MUST be at most 200 characters and the meta description at most
  500. The admin MUST warn, without blocking, beyond 60 and 160 characters respectively, and
  MUST show a preview of the search listing.
- **FR-005**: Empty fields MUST fall back as follows: meta title → page title, followed by
  the site's title suffix when one is set; meta description → no description; sharing
  title → meta title; sharing description → meta description; sharing image → the site's
  default sharing image, or none. For item pages, the page title is replaced by the item's
  name, as in 016 FR-030.
- **FR-006**: Each site MUST have search settings: title suffix (for example " | Acme"),
  default sharing image, organisation name and logo for structured data, "Discourage search
  engines", custom robots.txt rules, and "Redirect other host names to the primary one"
  (FR-014). Only Administrators may change "Discourage search engines", the robots.txt
  rules and the host name redirect. Designers and Administrators may change the rest.

**Metadata templates for collection items**

- **FR-007**: On a template page (016), the meta title, meta description, sharing title,
  sharing description and sharing image MUST be able to be bound to the item's fields with
  the binding expression format, scopes, formatting, one reference step and fallbacks of
  016. Each field MUST offer only compatible fields: text, rich text, number, date, option
  and single-reference values for text fields, and image fields for the sharing image.
- **FR-007a**: On a template page, "Hide from search engines" MUST be able to be bound to a
  switch field of the collection instead of being a fixed setting. An item page whose
  published value of that field is on MUST tell crawlers not to index it and MUST be left
  out of the sitemap. An empty value, or a broken binding, counts as off. Changing the
  field's published value MUST refresh that item page and the sitemap.
- **FR-008**: A rich text value bound to a metadata field MUST become plain text. A bound
  value longer than the field's maximum MUST be shortened on a word boundary with an
  ellipsis.
- **FR-009**: Broken metadata bindings (removed or retyped fields) MUST be marked in the
  editor as for block bindings (016), MUST NOT prevent saving or publishing, and MUST make
  the field use its fallback on the site.
- **FR-010**: The binding format stored for metadata MUST be the same versioned contract as
  for blocks (constitution Principle VI). Where metadata sits in the page schema MUST be
  documented in the page schema contract.

**Canonical addresses and host names**

- **FR-011**: The canonical address of a page or item page MUST be absolute, use the site's
  base URL (009: scheme, primary host name and base path), use the page's normalised address,
  and drop query parameters except a list's page parameter when it is not the first page
  (016 FR-027).
- **FR-012**: A canonical override MUST be either a page of the site, which is followed when
  that page's address changes, or an absolute `http`/`https` address. A page with an override
  MUST be left out of the sitemap.
- **FR-013**: A request for a public address whose letter case or trailing slash differs from
  the page's address MUST receive a permanent redirect to the normalised address.
- **FR-014**: Each site MUST have the setting "Redirect other host names to the primary
  one". Only Administrators may change it. It is on by default for new sites and off for
  sites that exist before the upgrade. When it is on, every request for a public address
  (pages, item pages, redirects, `sitemap.xml`, `robots.txt` and the not-found page) on a
  host name that is not the primary one MUST receive a permanent redirect to the same path
  and query on the primary host name. When it is off, those requests are served as on the
  primary host name. Before the setting is turned on, the admin MUST show the primary host
  name and the host names that will redirect to it, and ask for confirmation.

**Redirects**

- **FR-015**: Members with the Designer or Administrator role on a site MUST be able to
  list, search, create, edit, turn off, turn on, delete, import and export the site's
  redirects. Each redirect has a match kind (exact, or prefix: the source and every address
  below it), a source address (a path on the site), a target (a page or
  item of the site, a path on the site, or an absolute `http`/`https` address), a kind
  (permanent 301 or temporary 302), whether it is on, an optional note, who created it and
  when, and its usage (hit count and last-used time, FR-021a). The list MUST be sortable
  by usage so that unused redirects can be found.
- **FR-016**: The public surface MUST match a request in this order: reserved addresses,
  redirects whose source has a query string (FR-017a), live pages, item pages (016 FR-005),
  other redirects, then the not-found page. Live content at
  a redirect's source address MUST win, and the admin MUST mark that redirect as hidden.
- **FR-017**: Source addresses MUST be unique per site and match kind, compared without
  letter case or a trailing slash, and MUST NOT be reserved addresses. The home address
  without a query MUST NOT be a source. A redirect MUST NOT point to its own source or
  create a loop. A target that is another redirect's source MUST be saved as the final
  target.
- **FR-017a**: An exact redirect's source MAY include a query string. Such a source matches
  only a request with exactly the same parameters and values, in any order. Parameter names
  are compared with their letter case, and values are compared after decoding. Its path is
  compared as in FR-017. On a request with a query string, exact redirects with a matching
  query MUST be tried first, then exact redirects without a query, then prefix redirects.
  Prefix redirects MUST NOT have a query. Because a redirect with a query is more specific
  than the page at its path, it MUST be matched before live content at that path (for
  example `/?p=12` on a site whose home page is published). The admin MUST warn when such a
  redirect's path holds live content or the query uses a list's page parameter (016
  FR-027). When a redirect with a query is followed, the request's query is not carried
  to the target.
- **FR-018**: A redirect whose target is a page or item MUST follow that page's or item's
  current live address. If the target has no live address, the redirect MUST answer with
  the not-found page and be marked as broken in the admin.
- **FR-019**: Import MUST accept a UTF-8 comma-separated file with columns for source,
  target, kind, an optional match kind (exact by default) and an optional note, with or without a header row, up to 5,000 rows and
  1 MB. Each row MUST be checked as in FR-017. Valid rows MUST be added, and invalid rows
  MUST be reported with their line number and reason. Export MUST produce the same format.
- **FR-020**: A site MUST be able to hold at least 10,000 redirects without visibly slowing
  public requests (SC-004).
- **FR-021**: Redirect changes MUST take effect from the next request on every instance
  (constitution Principle IV). Every creation, change, deletion and import of a redirect,
  and every change of site search settings, MUST be recorded with who made it and when,
  and the record MUST outlive the redirect. No source address, target or other content is
  logged or kept in that record (Principle IX).
- **FR-021a**: Each redirect MUST show a hit count and last-used time. Uses MUST be counted
  in memory on each instance and saved in the background at least every 5 minutes. Serving
  a redirect MUST NOT write to the database (constitution Principle VII). Counts lost when
  an instance stops are accepted. Nothing about the visitor (address, agent, referrer or
  query) is recorded. The product MUST NOT keep a list of not-found addresses.

**Sitemap and robots.txt**

- **FR-022**: An address is indexable when it is a live page or a live item page, the site
  is enabled and not set to discourage search engines, and the page (or template) is not
  hidden from search engines, not left out of the sitemap, and has no canonical override.
  For an item page, the template's hide setting may come from the item's bound switch field
  (FR-007a).
- **FR-023**: Each site MUST serve `sitemap.xml` at its root (after the installation's base
  path) on all of its host names. It MUST list every indexable address once, as its
  canonical address, with the time its content was last published. The time for an item
  page is the later of the item's and the template's last publish. The sitemap MUST follow
  the published sitemap protocol.
- **FR-024**: When a site has more than 50,000 indexable addresses, `sitemap.xml` MUST be a
  sitemap index pointing to numbered sitemap files of at most 50,000 addresses each.
- **FR-025**: Each site MUST serve `robots.txt` at its root. By default it allows all
  crawlers on the public site, disallows the administrative surface, and names the
  sitemap's absolute address. With "Discourage search engines" on, it disallows everything.
  An Administrator's custom rules, at most 10 KB, MUST appear after the generated rules.
- **FR-026**: The sitemap and robots.txt MUST be cached like pages and refreshed when content
  they list is published, unpublished, moved, hidden or deleted, when a template or
  collection changes, and when site settings or host names change (constitution
  Principle VIII).
- **FR-027**: With "Discourage search engines" on, every page MUST tell crawlers not to index
  or follow it, the sitemap MUST list no addresses, and the admin MUST show a visible
  reminder that the site is hidden from search engines.

**Redirect offers when addresses change**

- **FR-028**: Any change that will alter the live address of a published page, its
  published descendants, or published item pages MUST offer to create permanent redirects
  from each old address to the new one, turned on by default. This covers a slug change, a
  move, a new home page, making a page a template or changing its collection, turning a
  template back into a page, and, for items, a slug change by publish, scheduled publish or
  rollback. A page's address is not versioned (012), so page rollbacks and schedules never
  change it. The offer appears in the warning that 012 FR-011 and 016 FR-008 already
  require.
- **FR-029**: The member's choice MUST be applied when the change goes live, including by a
  schedule. Redirects for page changes MUST be created in the same change, so the old
  address never gives the not-found page. Redirects for item changes MUST be in place
  within 5 seconds of the change going live (the freshness target of SC-005). The redirects
  created MUST be recorded as created by that change. Existing redirects whose target was an
  old address MUST be updated to the new one.
- **FR-030**: The offer MUST be available to the member who makes the change, even when that
  member could not create redirects by hand (an Editor changing an item slug). It covers
  only the addresses that the change moves.
- **FR-031**: An old address that is the address of other live content after the change
  MUST NOT be offered.
- **FR-032**: Unpublishing or deleting a published page or item MUST offer, not ticked by
  default, a redirect from its address to a page the member chooses.
- **FR-033**: For a change that moves a template's pattern (a move of the template or of an
  ancestor, or a slug change of either), the offer MUST create one prefix redirect from the
  old pattern's prefix to the new one, not one redirect per item. A prefix redirect sends an
  address below its source to the target followed by the rest of the address. Exact
  redirects MUST be matched before prefix redirects, and among prefix redirects the longest
  matching source wins. A prefix redirect's target MUST be a site path or a page, never an
  absolute address on another site or an item. Loop and chain checks of FR-017 MUST also
  cover prefix redirects.

**Structured data**

- **FR-034**: Published pages MUST carry structured data in the schema.org vocabulary:
  a breadcrumb trail on every page other than the home page; on the home page, the website
  and, when an organisation name is set, the organisation; for each Embed block showing a
  video, a video description; for each Collection list block, a list of the item pages it
  shows. Each block type that produces structured data defines it once with its
  block definition (constitution Principle VI).
- **FR-035**: A template MUST be able to map its collection to a content type for item
  pages, chosen from Article, Product and Event, or none. Each property of the type is filled
  from a bound field, with the binding rules of FR-007:
  - Article: headline, description, image, author name, date published, date modified.
  - Product: name, description, image, brand, product code, price, currency, availability.
  - Event: name, description, image, start, end, location name, location address.

  The headline or name is required and defaults to the item's name. A Product without a
  price is described without an offer.
- **FR-035a**: Each content type MUST be defined once, with its properties, the field types
  that can fill each one, and how it is written out. Adding a content type later MUST NOT
  change stored mappings of existing types or the output of item pages that use them
  (constitution Principle VI).
- **FR-036**: Structured data MUST be built only from published values that are shown on
  the page or set in metadata, MUST leave out empty properties, and MUST be encoded so that
  no value can end the structured data or inject markup.

**Rendering, caching and isolation**

- **FR-037**: Metadata and structured data MUST be produced on the server in the published
  page (constitution Principle VIII). Pages MUST stay within 012's render budgets. No
  JavaScript MUST be added to published pages.
- **FR-038**: Search settings, redirects and site search settings MUST belong to exactly one
  site, MUST be separated by site as every site-owned record (constitution Principle V), and
  MUST be covered by the site isolation suite.
- **FR-039**: Changes to redirects and site search settings MUST take effect on every
  instance from the next request, without waiting for a cache to expire. Any event handlers
  involved MUST be idempotent.
- **FR-040**: Export and import of a site (constitution Principle III) MUST include page and
  template metadata, site search settings and redirects. This feature defines what an
  export carries (module-surface.md §7). Building export and import is the obligation of
  #20, which does not exist yet.

### Key Entities

- **Search Settings (page)**: part of a page's draft and published versions. It holds meta
  title, meta description, sharing title, sharing description, sharing image reference,
  canonical override (page reference or absolute address), "hide from search engines" and
  "leave out of sitemap". On a template, each text or image field may instead hold a binding
  with a fallback. A template may also map its collection to a structured data type.
- **Site Search Settings**: one per site. It holds the title suffix, default sharing image,
  organisation name and logo, "Discourage search engines", custom robots.txt rules, and
  "Redirect other host names to the primary one".
- **Structured Data Type**: a built-in definition (Article, Product, Event) listing its
  properties and the field types that can fill each. A template's mapping names one type
  and binds its properties to fields.
- **Redirect**: belongs to one site. It holds a match kind (exact or prefix), a source
  address, a target (page, item, site
  path or absolute address), a kind (permanent or temporary), an on/off state, a note, its
  origin (created by hand, imported, or offered by a change), and who created and changed it
  and when. Its derived state is hidden by content, broken, or shortened. Its usage (hit
  count and last-used time) is kept apart from the redirect itself and is approximate.
- **Sitemap**: derived and never edited. It is the set of indexable addresses of a site with
  their last-published times, split into files when large.
- **Structured Data Description**: derived and never stored apart from the page. It is a
  description of a page, block or item page in the schema.org vocabulary.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Editor who has used the page editor can set the meta title, description and
  sharing image of a page and publish it in under 2 minutes, without help.
- **SC-002**: A Designer can import 300 redirects from another system and see which rows
  failed and why in under 3 minutes.
- **SC-003**: Across a test of every address change listed in FR-028, 100% of the old
  addresses of published content reach the new content in one redirect when the offer is
  accepted, with no chains or loops.
- **SC-004**: On the reference environment (two cores, four gigabytes of memory), with
  10,000 redirects on the site, 95% of redirect responses are returned within the cache-hit
  render budget of 012, and page render times stay within 012's budgets.
- **SC-005**: After a page or item is published, unpublished or hidden, the sitemap shows the
  change on every instance within 5 seconds.
- **SC-006**: The sitemap of a site with 100,000 indexable item pages is served complete and
  valid against the sitemap protocol, and each sitemap file is returned within 2 seconds on
  the reference environment.
- **SC-007**: 100% of published pages in the golden-file test produce metadata and
  structured data that pass the sitemap protocol and schema.org vocabulary validation, with
  no unencoded user value.
- **SC-008**: Every metadata, redirect and search setting action in the admin can be
  completed with the keyboard only, and meets the accessibility baseline of 011.

## Scope

### In Scope

- Search and sharing metadata per page, its fallbacks, and the site's search settings.
- Metadata templates on collection templates, using 016's bindings.
- Canonical addresses, letter-case and trailing-slash normalisation, and handling of
  non-primary host names.
- Automatic `sitemap.xml` (with an index for large sites) and `robots.txt` per site.
- Redirect management (permanent and temporary, exact and prefix), import and export.
- A per-site setting that redirects other host names to the primary one.
- Redirect offers on every change that moves or removes a live address.
- Structured data for breadcrumbs, the website and organisation, video embeds, collection
  lists and item pages.

### Out of Scope

- Analytics, tag managers and search console integrations.
- A log or report of not-found addresses (clarified 2026-10-13).
- Per-locale sitemaps, alternate-language links and localised metadata.
- Wildcard, placeholder or regular-expression redirects. Only exact and prefix redirects
  are supported.
- Structured data content types other than Article, Product and Event. More can be added
  later under FR-035a.
- Metadata fields on individual items: an item's metadata comes from its template's
  bindings. A schema author can add dedicated fields, such as "SEO description" or a
  "Hide from search" switch, and bind them (FR-007a).
- Image, news and video sitemaps; pinging search engines when the sitemap changes.
- Free-form code in the page head and custom structured data entered by members.
- Search engine ranking advice or content scoring.

## Assumptions

- **Metadata follows draft and publish** (constitution Principle VII). A metadata change is
  a page change and needs a publish. Redirects and site search settings are site settings,
  not page content, so they take effect when saved.
- **Roles** follow 010 to 016. Editors, as writers, edit page metadata in drafts. Designers
  and Administrators shape the site's addresses, so they manage redirects and canonical
  overrides. Only Administrators may hide the whole site from search engines or edit
  robots.txt, because a mistake there removes the site from search.
- **Query-string redirects** (clarified 2026-10-13) cover old links from systems that put
  content identifiers in the query, such as `/index.php?id=42` or `/?p=12`. They are the one
  case where a redirect is matched before live content, because they match only that exact
  query.
- **Live content wins over redirects without a query**, so such a redirect can never hide
  a published page, and
  stale redirects cannot break a newly published page. Many hosted site builders work the
  other way. This feature chooses safety, and the admin marks hidden redirects.
- **Redirect offers are on by default** for moves and slug changes, because keeping links
  working is the issue's goal. They are off by default for unpublish and delete, because
  removing content is often intentional.
- **Length limits** (60 and 160 characters for warnings) follow common search engine display
  lengths. The hard maximums only prevent abuse.
- **Import format** is a plain comma-separated file because every site builder and
  spreadsheet can produce one. The 5,000-row and 10,000-redirect limits are defaults that
  can be raised later.
- **Sitemap protocol limits** (50,000 addresses per file) are taken from the public sitemap
  protocol.
- **The website description** uses the site's name. An organisation is described only when
  an organisation name is set, so no organisation is invented from the site's name.
- **Upgrade**: existing pages have no search settings, so they use the fallbacks, and the
  document titles of existing pages and item pages do not change. Sites start with no
  redirects, "Discourage search engines" off and no custom robots rules. "Redirect other host
  names to the primary one" starts off for existing sites, so an upgrade does not change
  which host names serve content (constitution Principle III). The upgrade notes tell
  operators about the setting. New sites start with it on.
- **Prefix redirects for template moves** (clarified 2026-10-13) keep the number of redirects
  constant however large the collection is, and also cover items published later under the
  old pattern.

## Dependencies

- Feature 009 (sites and host name resolution): the base URL, primary host name, host names,
  the enabled state, the default language, and the isolation suite.
- Feature 010 (pages, page tree and shared components): page addresses, slugs, moves,
  reserved addresses and the page schema contract.
- Feature 011 (editor core): block definitions, the property panel, the Embed and Image
  blocks, and the accessibility baseline.
- Feature 012 (publishing and rendering): publish, unpublish, schedules, rollback, the
  address change warning, the not-found page, output caching, refresh rules and budgets.
- Feature 013 (media library and variants): sharing and logo images, their dimensions and
  alternative text.
- Features 014 and 015 (collections and items): fields, item slugs, published data and item
  change announcements.
- Feature 016 (collection templates and binding): templates, item addresses, the binding
  expression format, the Collection list block, route matching and the announcements when a
  template's pattern changes.
- The project constitution, in particular Principles III (site export), V (site isolation),
  VI (binding contract), VII (published data only), VIII (caching and budgets), IX (encoding
  and allowed schemes) and X (golden-file tests for metadata and structured data).
- GitHub issue #17, which this feature implements and which the pull request closes.
