# Research: SEO, Sitemap and Redirects

**Feature**: `017-seo` | **Date**: 2026-10-13 | **Spec**: [spec.md](./spec.md)

Decisions are numbered `SE-xx`. Each has a decision, a rationale and the alternatives
considered. The spec's six clarifications (2026-10-13) are inputs, not open questions.

---

## SE-01 No new module: metadata in Pages, site settings in Sites, output in Rendering

**Decision**:
- **Pages** owns what a page says about itself and where addresses go:
  - search and sharing metadata live in the page document (SE-02), so they are drafted,
    published, versioned, rolled back and scheduled by the existing 010–012 code;
  - **redirects** live in a new `pages.redirects` table (SE-09). Address changes happen in
    Pages, so the redirects a member accepts are written in the same transaction as the move
    or slug change;
  - the sitemap generation (SE-15) and redirect usage (SE-12) are also kept here.
- **Sites** owns site-wide search settings, as columns on `sites.sites` (SE-08). The row's
  `xmin` is already the `SiteVersion` in every page's cache key, so a title suffix or
  "Discourage search engines" change refreshes every page with no new invalidation code.
- **Cms** accepts the redirect choice on item publish, schedule, rollback, unpublish and
  delete, and announces it (SE-11). It gains one read for the sitemap (SE-14).
- **Rendering** writes the head metadata and structured data, answers redirects, and serves
  `sitemap.xml` and `robots.txt`. It still owns no tables.

**Rationale**: every rule already has an owner. Addresses belong to the page tree (010).
Draft and published copies of metadata come with page versions (012). Site-wide settings
belong to the site (009). Redirects created from a page move must commit with the move, and
no transaction may span modules (Principle I).

**Alternatives considered**:
- **A new `Seo` module**: the constitution's module list is closed, so this needs an
  amendment. Worse, redirects offered by a page move would then need an event, and the old
  address would give the not-found page until the event was handled.
- **Metadata in a side table keyed by page**: two things to publish atomically. Rollback,
  copy-to-draft and schedules would all have to learn about it.
- **Site search settings in a Pages table**: they would not move `SiteVersion`, so every
  page would need a separate key part and an invalidation path.

---

## SE-02 Page metadata: an optional `seo` prop on the document root

**Decision**:
- The `root` node gains one optional prop, `seo` (page schema v1, additive, page-schema.md
  §6):

  ```text
  seo = {
    "title"?: string ≤200,  "description"?: string ≤500,
    "ogTitle"?: string ≤200, "ogDescription"?: string ≤500,
    "ogImage"?: { "mediaId": uuid },
    "canonical"?: link (page-schema §2a: { type: "page", pageId } | { type: "url", href }),
    "noindex"?: boolean, "noSitemap"?: boolean,
    "bind"?: { title|description|ogTitle|ogDescription|ogImage|noindex: binding },
    "fallback"?: { title|description|ogTitle|ogDescription: string ≤500 },
    "structuredData"?: { "type": "Article"|"Product"|"Event", "bind"?: { <property>: binding } }
  }
  ```

- **New rules** in page-schema.md §3, numbered after 016's 21:
  - 22 `seo_in_component`: a component document's root has no `seo`;
  - 23 `seo_bind_outside_template`: `seo.bind` and `seo.structuredData` are allowed in any
    page document, but on a page that is not a template they are **broken**, as a `"page"`
    scope is (binding-expression.md §3), not refused. This keeps a template that is turned
    back into a page valid;
  - 24: a `url` canonical is absolute `http` or `https` without user info;
  - 25: metadata texts hold no control characters, line breaks included.

  Rules 24 and 25 are schema patterns, so they fail as `props_invalid` like every other prop
  (decided at implementation: no code is needed).
- Lengths are hard maximums. The editor warns beyond 60 and 160 characters (FR-004).

**Rationale**: the root already holds the page as a whole. One prop keeps every metadata
change inside the publish, rollback and schedule paths (Principle VII) and in the page's
render generation (Principle VIII). The rules are additive, so every stored document stays
valid and renders the same body.

**Alternatives considered**:
- **Columns on `pages.pages`**: not versioned. A metadata change would reach visitors
  without a publish, breaking Principle VII.
- **A new `seo` node type under `root`**: the node grammar is for content, and `rootChild`
  placement rules would need an exception.

---

## SE-03 Who may change which metadata

**Decision**:
- `seo` is part of the draft, saved with `pages.page_drafts.edit` (Designer, Editor).
- `seo.canonical` and `seo.noindex`, and their bindings, need `pages.structure.manage`
  (Designer; Administrator always). `SavePageDraft` compares them with the current draft. A
  member without the capability who changes them gets `403 pages.seo_restricted` naming the
  field. Unchanged values pass, so Editors can keep saving pages whose canonical a Designer
  set.
- Site search settings (SE-08): two new capabilities.
  - `sites.search.manage` (Designer): title suffix, default sharing image, organisation
    name and logo;
  - `sites.search_visibility.manage` (Administrator only): "Discourage search engines",
    custom robots rules, and "Redirect other host names to the primary one".
- Redirects: `pages.redirects.manage` (Designer).
- Redirect offers (SE-11) need only the capability of the change they come with (FR-030).

**Rationale**: matches 010's "Designer builds, Editor writes", and the spec's rule that a
mistake removing the site from search is Administrator-only.

**Alternatives considered**: a separate `seo` capability for Editors. It would add a role
choice nobody asked for (Principle XI).

---

## SE-04 Metadata templates reuse the 016 binding format

**Decision**:
- `seo.bind` keys are metadata fields, and each value is a 016 `binding` (binding-expression
  §2), with scope `"page"` (the default on a template). The `empty` choice is always
  `"fallback"` (`"hide"` is refused by the schema).
- **Compatibility** (added as rows to binding-expression.md §4):

  | Field | Accepts |
  |---|---|
  | `seo.title`, `seo.description`, `seo.ogTitle`, `seo.ogDescription` | everything `heading.text` accepts, plus rich text (as plain text) and text templates |
  | `seo.ogImage` | image |
  | `seo.noindex` | boolean |

- **Value rules** (FR-008):
  - rich text becomes plain text: block texts joined with spaces, marks dropped;
  - whitespace runs collapse to one space;
  - over the field's maximum, the text is cut at the last space before the limit minus one,
    and `…` is appended. A single word longer than the limit is cut at the limit.
- **Fallbacks** (FR-005, User Story 4 scenario 3): empty or broken binding → the field's
  `seo.fallback` text → the field's default fallback (SE-05).
- **Hide one item** (FR-007a, clarification 4): `seo.bind.noindex` names a boolean field.
  A true published value hides the item page; empty, false or broken counts as off.
- **Resolution** reuses Rendering's `BindingResolver` and `ValueFormatter`. The admin reuses
  `editor/bindings/` for the canvas preview and the shared golden examples.

**Rationale**: one binding contract (Principle VI). Metadata is text, so no new value types
are needed.

**Alternatives considered**: a separate mini-template language for titles (`{Title} |
Blog`): it would duplicate text templates and need its own contract.

---

## SE-05 The head: tags, fallbacks and encoding

**Decision**: `PageShell` gains a `HeadMetadata` record. `HeadBuilder` (Rendering) fills it
from the live document's `seo`, the resolved bindings, the `SiteDto` search settings, the
media files and the canonical address. In order:

```html
<title>…</title>
<meta name="description" content="…">                      <!-- only when non-empty -->
<link rel="canonical" href="https://…">
<meta name="robots" content="noindex, nofollow">            <!-- only when not indexable -->
<meta property="og:type" content="website|article">
<meta property="og:title" content="…">
<meta property="og:description" content="…">              <!-- only when non-empty -->
<meta property="og:url" content="https://… (canonical)">
<meta property="og:site_name" content="…">
<meta property="og:locale" content="en_GB">                <!-- from the default language -->
<meta property="og:image" content="https://…">             <!-- with :width, :height, :alt when known -->
<script type="application/ld+json">…</script>              <!-- SE-18, only when non-empty -->
```

- **Fallbacks** (FR-005): title → live version title (item pages: item name, else template
  title, 016 FR-030) + `title_suffix`; description → none; `og:title` → title;
  `og:description` → description; `og:image` → site default image → none.
- **Image**: absolute, built from `SiteDto.BaseUrl` and `PublicMediaFile.PublicPath`. The
  largest variant no wider than 1200 px is used when it exists, else the original.
  `PublicMediaFile` gains an optional `AltText` (additive, Media.Contracts) for
  `og:image:alt`.
- **Robots meta** is written when the page is not indexable for a reason the page controls
  (`noindex`, or a bound hide switch) or the site discourages search engines (SE-17).
  `noSitemap` and `canonical` do not write it.
- **Encoding**: every value goes through `HtmlWriter.Attribute`. `og:locale` maps the BCP 47
  tag (`pt-BR` → `pt_BR`; a bare language gets no region). The title keeps its existing
  `Text` encoding.

**Rationale**: the tags search engines and link preview services read, and nothing else.
Twitter/X falls back to Open Graph, so `twitter:*` tags are not written (Principle XI).

**Alternatives considered**: writing `og:image` from the original always. Originals can be
tens of megabytes, and 013 already sized variants for this.

---

## SE-06 Canonical addresses and address normalisation

**Decision**:
- **Canonical address** (FR-011): `SiteDto.BaseUrl` + the page's lower-case path (the home
  page is `/`), plus `?<param>=<n>` for each paginated list on the page that is past its
  first page, in parameter-name order. For an item page, the item address. For a canonical
  override, the target page's current live address, or the absolute `url` as written.
- **Override and the sitemap** (FR-012): a page with `seo.canonical` is not indexable for
  the sitemap (SE-14). A `page` target that is not live is ignored, and the page names
  itself.
- **Normalisation redirect** (FR-013): `PathNormaliser` already maps a request to the
  lower-case, slash-trimmed address. `RenderPageHandler` now compares the raw path with the
  normalised one. When they differ and the normalised address is live content (page or item
  page), it answers `301` to the normalised address with the request's query. When nothing
  is there, matching continues with the normalised address (redirects, then not found).
- The home page `/` and the base path root are never redirected for a trailing slash.

**Rationale**: one indexed address per page, at the cost of one comparison per request. A
redirect only when content exists keeps the not-found page byte-identical for every miss
(016 FR-006).

**Alternatives considered**: canonical tags only, no normalisation redirect. Both forms
would stay crawlable and split links. The spec requires the redirect.

---

## SE-07 Other host names: an Administrator setting, enforced at site resolution

**Decision**:
- `sites.sites` gains `redirect_other_hosts boolean not null`. The migration fills `false`
  for existing sites. `CreateSite` sets `true` (clarification 2).
- `SiteDto` gains `RedirectOtherHostNames`.
- `SiteResolutionMiddleware` (Host) checks it right after resolving the site. When it is on
  and the request's normalised host is not `PrimaryHostName`, it answers `301` to
  `BaseUrl + path + query`, with `Cache-Control: public, max-age=300`. This applies to every
  public request **except** `/_media` and `/_assets`. Those stay served on every host name
  so that images in old emails and embeds keep working. They carry no search value.
- Turning the setting on goes through `UpdateSearchVisibility` (SE-08). The admin dialog
  lists the primary and the other host names and asks for confirmation (FR-014).

**Rationale**: the decision belongs to site resolution, which already runs once per public
request with the site loaded. No extra query is needed.

**Alternatives considered**:
- **Rendering answering the redirect**: it would miss `sitemap.xml`, `robots.txt` and
  requests for Media, and duplicate the host check in each controller.
- **Defaulting existing sites to on**: an upgrade would change which host names serve
  content, against Principle III's no-surprise upgrades.

---

## SE-08 Site search settings: columns on `sites.sites`

**Decision**:
- **Columns**:
  - `search_title_suffix text null` (≤60);
  - `search_default_image uuid null`;
  - `search_org_name text null` (≤200);
  - `search_org_logo uuid null`;
  - `search_discouraged boolean not null default false`;
  - `search_robots_rules text null` (≤10 KB);
  - `redirect_other_hosts` (SE-07).
- **Endpoints** (two, with different capabilities, SE-03):
  - `PUT …/sites/{id}/search-settings` with `sites.search.manage`;
  - `PUT …/sites/{id}/search-visibility` with `sites.search_visibility.manage`.

  Both take the site `Version` and refuse a stale one, as `UpdateSettings` does (009 S-06).
- **Media ids are not checked by Sites**. Sites references no other module, and the public
  media reader already returns only the site's own files, so another site's id or a deleted
  file simply shows no image (SE-05). The admin shows a missing-file mark (spec edge case).
- **Robots rules** are text, trimmed, with lines of at most 500 characters and no control
  characters other than line breaks, and stored with `\n` line ends.

**Rationale**: `xmin` moves with the row, so every page, `robots.txt` and the sitemap get new
keys on the next request on every instance (Principle VIII), with no event.

**Alternatives considered**: a `sites.search_settings` table. It would not move
`SiteVersion`, so another key part and its tests would be needed.

---

## SE-09 Redirects: one table in Pages, normalised sources, typed targets

**Decision**: `pages.redirects` (data-model §1.1).
- **Source**:
  - `source_path`: normalised like `PathNormaliser`, lower-case, without a trailing slash,
    and at most 1,024 characters;
  - `source_query`: null, or a **canonical query**: pairs sorted by name (ordinal, case
    kept) then by value, values percent-decoded then re-encoded with RFC 3986 unreserved
    characters kept, and `&`-joined. A query is at most 512 characters and allowed only for
    `match = exact` (FR-017a).
- **Match**: `exact` or `prefix`. A prefix source `P` matches addresses that start with
  `P + "/"`, never `P` itself (SE-11 template moves).
- **Target**:
  - `target_kind` is one of `page` (`target_page_id`), `item` (`target_collection_id`,
    `target_item_id`), `path` (`target_path` on the site, with an optional query) or `url`
    (absolute `http`/`https`, ≤2,048 characters, no credentials);
  - `prefix` redirects take `page` or `path` only (FR-033);
  - a `path` target equal to a page's current address at write time is stored as `page`, so
    it follows moves (FR-018);
  - a `url` target on the site's primary host name and base path is stored as `path` (then
    as `page` by the rule above), so it follows a change of primary host name. A `url` on
    another host name of the site stays absolute (spec edge case).
- **Status**: `status_code` `301` or `302`, an `enabled` flag, a `note` (≤500), `origin`
  (`manual`, `import`, `offer`), and `created_by/at` and `updated_by/at`. `version`
  (`xmin`) is the concurrency token.
- **Uniqueness**: the index `(site_id, match_kind, source_path, coalesce(source_query,''))`
  (FR-017).
- **Refused sources**: reserved first segments (`api`, `admin`, `health`), `/_media`,
  `/_assets`, `/robots.txt`, `/sitemap.xml`, `/sitemap-<n>.xml`, and `/` without a query.
- **Limit**: 10,000 redirects per site (`redirects.limit_reached`).
- **Chains and loops** (FR-017), checked on every write:
  1. resolve the target to a path (page → its current address; item → its address, if
     live);
  2. if that path is the source of an enabled exact redirect without a query, replace the
     target with that redirect's target (`shortened: true` in the response), at most 10
     steps;
  3. refuse with `redirects.loop` when a step returns to the source, or when a prefix
     source would match its own target.

**Rationale**: typed targets make "follows the page" and "broken" (FR-018) a property of the
data, not of string matching. Canonical queries make `?a=1&b=2` and `?b=2&a=1` one source
(FR-017a).

**Alternatives considered**:
- **Regular-expression sources**: out of scope (clarification 1), and expensive to match per
  request.
- **Storing every redirect as a path**: redirects would break on the next move and need
  rewriting.

---

## SE-10 Matching on the public path

**Decision**: `PageResponder` matches in this order (FR-016):
1. reserved routes (unchanged; they never reach the catch-all);
2. **query redirects**: only when the request has a query. One indexed lookup on
   `(site_id, 'exact', path, canonical query)` among enabled rows;
3. the live page (unchanged), then the normalisation redirect (SE-06);
4. the item page (016, unchanged);
5. **exact redirects** without a query: one indexed lookup;
6. **prefix redirects**: one lookup with `source_path = ANY(@ancestors)` (at most 10
   ancestors of the path, PageAddresses.MaxDepth), ordered by length descending, `LIMIT 1`;
7. the not-found page.

Steps 5 and 6 are one query (`IRedirectReader.FindAsync`), run only when no content matched.
Step 2 runs only for requests with a query, before the page lookup.

**Response**:
- **Status and headers**: `301` or `302`, an empty body, and `Location`.
  - **`Location`**: a site target uses the request's base path and the target's current
    address; an `item` target uses the live template pattern and the item's slug; a `url`
    target is written as stored. A prefix redirect adds the rest of the path after its
    source.
  - **Query**: carried over when the source has no query and the target has none (US2
    scenario 6). It is never carried from a query source (FR-017a).
  - **`Cache-Control`**: `public, max-age=300` for 301 and `no-cache` for 302. Browsers cache
    301s indefinitely without a header, and a member must be able to change their mind.
- **Broken target**: a page or item target that is not live gives the not-found page
  (FR-018). The admin marks the redirect broken (SE-13).
- **Usage**: every redirect answered is counted in memory (SE-12).
- **Read path**: `IRedirectReader` runs on Pages' existing read-only `PublishedReadDbContext`
  (012 PR-06). Nothing is cached, so a change takes effect at the next request on every
  instance (FR-021), as host names do (009 S-01).

**Rationale**: content first protects published pages (Assumptions). Query redirects first is
the clarified exception (clarification 5). A miss costs one indexed query, and only misses
pay it.

**Alternatives considered**:
- **An in-memory redirect table per instance**: it needs cross-instance invalidation
  (Principle IV) for a lookup that already takes well under a millisecond.
- **Redirects before content**: rejected in the spec.

---

## SE-11 Redirect offers: a flag on the change, applied in its transaction

**Decision**:
- **Pages requests** gain an optional `redirectOldAddresses: boolean`. When omitted it is
  `false`, so older API clients see no change. The admin sends `true` by default, with a
  visible checkbox. It is added to `UpdatePage` (slug), `MovePage`, `MakeHomePage` and
  `SetPageTemplate` (set, remove).
- **In the `PageTree` transaction**, after the change:
  1. **Live pages**: for every changed live address (the `PageAddressesChanged` changes whose
     page is live), an exact redirect old path → `page` target.
  2. **Template patterns**: for every live template whose pattern changed, one prefix
     redirect from the old pattern's prefix (the pattern without `/{slug}`) to the new prefix
     as a `path` target (clarification 1). Turning a template back into a page offers one
     prefix redirect from the old prefix to the **parent page** (`page` target). Changing a
     template's collection keeps its pattern, so nothing is offered.
  3. Skip old addresses that now belong to live content (FR-031).
  4. Retarget existing `path` redirects whose target equals an old address to the new
     target (FR-029). Turn off any redirect this would loop, and report it as `turned_off`
     in the response.
- **Page unpublish and delete** gain an optional `redirectTo: { pageId }` (off by default,
  FR-032). It is applied in the same transaction.
- **Scheduled page unpublish**: `SetScheduleRequest` gains an optional `redirectToPageId`,
  stored on `pages.publish_schedules.redirect_to_page_id`. `RunDueScheduleHandler` applies
  it in the unpublish's transaction when the page was live.
- **Warning before content hides a redirect**: page create, slug change, move, home change,
  template removal and publish responses carry `redirect_hidden` (data-model §4) when the
  page's live address is the source of an enabled redirect without a query. It is one
  indexed probe after the change.
- **Addresses are not versioned** in 012: a page's address changes when its slug or position
  is saved, not when a version is published or rolled back. So page offers come with tree
  changes only, and a scheduled publish or a rollback of a page never moves it. The spec's
  "rollback" and "scheduled" cases (FR-028, User Story 5 scenario 6) apply to **items**,
  whose slug is published data.
- **Item offers (Cms)**:
  - `PublishItem`, `RollbackItem` and `ScheduleItem` (publish) gain
    `redirectOldAddress: boolean`, stored on the schedule row so that it applies when the
    schedule runs;
  - `UnpublishItem` and `DeleteItem` gain `redirectTo: { pageId }`;
  - when the published slug changes (or the item stops being live) and the member asked for
    a redirect, Cms writes **`Cms.ItemRedirectRequested`** in the same transaction:
    `siteId`, `collectionId`, `itemId`, `oldSlug`, `reason` (`moved`, `removed`),
    `targetPageId`, `actorUserId`;
  - Pages handles it: when the collection has a live template, it adds an exact redirect
    from `pattern(oldSlug)` → `item` target (`moved`) or → `page` target (`removed`), with
    the same checks as above. The unique index makes a redelivery a no-op.
- **Payload exception**: `oldSlug` is the one slug in an event. It is an address, sent only
  when a member asked for the redirect, and it is never logged, the same exception 012 made
  for `path`.

**Rationale**: page redirects commit with the change, so there is no window in which the old
address gives the not-found page. Item redirects cross from Cms to Pages, so they go through
the outbox (Principle I). The window is the outbox delay, a few seconds. FR-029 allows up to
5 seconds for items (decided 2026-10-13 in analysis, I1).

**Alternatives considered**:
- **Cms keeping old slugs** and Rendering looking them up on a miss: invisible to members,
  so they could not edit, export or remove these redirects, against FR-015 and US5.
- **The admin calling the redirects endpoint after the change**: not atomic, and Editors
  lack `pages.redirects.manage` (FR-030).

---

## SE-12 Redirect usage: counted in memory, saved in the background

**Decision** (clarification 3, FR-021a):
- `pages.redirect_usage(redirect_id pk → redirects on delete cascade, site_id, hits bigint,
  last_used_at timestamptz)`.
- `IRedirectHits.Record(Guid siteId, Guid redirectId)` (Pages.Contracts) is a singleton
  that adds to a `ConcurrentDictionary` with an increment and the latest time. Rendering
  calls it after answering.
- `RedirectUsageFlusher` (a Pages `BackgroundService`) swaps the dictionary every 60
  seconds and on shutdown. It upserts `hits = hits + @n` and
  `last_used_at = greatest(last_used_at, @t)` in one batched command per site through
  Pages' writable `DbContext`. Rows of redirects deleted in between are ignored.
- Nothing about the visitor is kept. Counts lost in a crash are accepted.

**Rationale**: the request path never writes (Principle VII). Increments from several
instances add up.

**Alternatives considered**:
- **Writing on each hit**: a write per redirect, and the public path would need a writable
  connection.
- **Outbox messages per hit**: unbounded rows for a statistic.

---

## SE-13 Redirect management, import and export

**Decision**:
- **Endpoints** (contracts/seo-api.yaml), all `pages.redirects.manage`:
  - list: search on source and target text, filter by state, and sort by source, usage,
    last used or updated, paged 50 at a time;
  - get, create, update (with `version`), delete;
  - turn on or off through update;
  - import and export.
- **Derived states** in responses:
  - `hidden`: a live page or item page answers the source (computed per page of results
    with one `IPublishedContentReader` probe per row, at most 50);
  - `broken`: the page or item target is not live;
  - `shortened`: on a create or update response only.
- **Warnings** on create and update (FR-017a), never refusals:
  - `source_holds_content`: a query source whose path is a live page or item page;
  - `source_uses_page_param`: one of its parameters is named like a paginated list's
    parameter on that page (`LivePageRef.Params` or `LiveTemplateRef.Params`).
- **Import**: `POST …/redirects/import` with a `text/csv` body of at most 1 MiB. A UTF-8 BOM
  is accepted. Any other encoding (invalid UTF-8) refuses the whole file.
  - The parser is in-house, RFC 4180, about 80 lines. It needs no dependency (Principle XI).
  - Columns are `source,target,kind[,match][,note]`, with an optional header row detected
    by its first cell `source`. `kind` is `301`, `302`, `permanent` or `temporary`, and
    `match` is `exact` (default) or `prefix`. A row whose path target is a page's current
    address becomes a `page` target.
  - At most 5,000 rows. Every row is checked (SE-09), including against earlier rows of the
    file. Valid rows are inserted in one transaction, and the response lists each refused
    line with its reason code.
  - The site limit is checked for the whole file first; beyond it, the whole file is refused
    with `redirects.limit_reached`.
- **Export**: `GET …/redirects/export` returns the same columns with a header row, `page` and
  `item` targets written as their current address. A cell starting with `=`, `+`, `-`, `@`,
  a tab or a carriage return gets a leading `'` (spreadsheet formula injection).

**Rationale**: a plain CSV is what other site builders export and spreadsheets edit
(Assumptions).

**Alternatives considered**: a JSON import, which spreadsheets cannot produce; or adding a
CSV library, which needs a justification for about 80 lines of code.

---

## SE-13a Audit of redirect and site search changes

**Decision** (FR-021; decided 2026-10-13 in analysis, C4):
- **Redirects**: `pages.redirect_changes` (data-model §1.7) gets one row per redirect action
  (created, changed, turned on, turned off, deleted, imported, offered, retargeted), written
  in the same transaction.
  - It holds ids, the action, the origin, the actor and the time, with no source, target or
    note. It has no foreign key, so it outlives the redirect.
  - An import writes one row per added redirect.
  - The redirect detail response shows the last 20 rows as `history`.
- **Site search settings and visibility**: `sites.sites.search_updated_by` with the existing
  `UpdatedAtUtc`.
- No pruning in this feature. The rows are small, and housekeeping is a backlog item.

**Rationale**: queryable, survives log rotation, and holds no content (Principle IX).

**Alternatives considered**:
- **Log lines only**: lost with rotation.
- **Relaxing FR-021**: deletions would leave no trace.

---

## SE-14 Sitemap content and files

**Decision**:
- **Routes** (Rendering): `GET /sitemap.xml` and `GET /sitemap-{n}.xml`, after the base path,
  on every host name of the site. The slug grammar has no `.`, so no page can claim them.
  They are added to `PageAddresses` documentation as platform paths.
- **Entries**:
  - **Pages**: a new `ISitemapReader.GetPagesAsync` (Pages.Contracts, read-only) returns
    every live, non-template page with its path, `LiveSinceUtc`, and the flags read from
    the live document (`seo.noindex`, `seo.noSitemap`, `seo.canonical` present). It also
    returns every live template with its pattern, `LiveSinceUtc`, collection, flags, and
    the field id bound to `seo.bind.noindex` (if any). This is one query over at most 2,000
    pages per site.
  - **Items**: a new `IPublishedItemReader.ListAddressesAsync(siteId, collectionId,
    hideFieldId?, afterId?, limit)` returns published slugs, `LastPublishedAtUtc` and the
    hide flag, keyset-paged by id 10,000 at a time.
  - **Indexable** (FR-022): the site is not discouraged; the page or template is not
    `noindex` or `noSitemap` and has no canonical; and the item's hide value is not true.
  - **Last modified**: a page's `LiveSinceUtc`; an item page's later of the item's last
    publish and the template's `LiveSinceUtc` (FR-023), written in W3C date-time UTC
    format.
- **Order**: pages by path, then each template's items by item id. This order is stable for
  one generation, so file boundaries are stable too.
- **Files** (FR-024): up to 50,000 entries makes one `urlset`. Beyond that, `sitemap.xml` is
  a `sitemapindex` of `sitemap-1.xml` … `sitemap-N.xml`. A number beyond N, or any number
  when there is one file, gives the not-found page.
- **Writing**: `XmlWriter` (UTF-8, escaped), protocol namespace
  `http://www.sitemaps.org/schemas/sitemap/0.9`, absolute canonical addresses (SE-06), and
  `lastmod` only.
- **Discouraged site** (FR-027): an empty `urlset`.
- **Build**: on a cache miss, the whole set is built once and each file is cached under the
  generation key (SE-15), sized for the cache's accounting. 100,000 entries are about 12 MB
  and are built in one pass.

**Rationale**: the sitemap protocol's limits, with the sitemap derived only from published
data (Principle VII).

**Alternatives considered**:
- **Computing one file on demand with offsets**: each file would need counts of everything
  before it, and the work repeats per file.
- **A stored sitemap table**: a second copy of published state that needs its own
  consistency.

---

## SE-15 Sitemap freshness: one generation per site

**Decision**:
- `pages.sitemap_state(site_id pk, generation bigint)`, read by
  `ISitemapReader.GetGenerationAsync` (one indexed read per sitemap request).
- It is bumped in the same transaction as each Pages change that alters the set or its
  dates:
  - publish, scheduled publish, rollback and unpublish of a page;
  - slug change, move or home page change of a live page or its live descendants;
  - template set, removed or changed collection;
  - page delete.
- It is also bumped by Pages' existing idempotent handlers of `Cms.ItemPublished`,
  `Cms.ItemUnpublished`, `Cms.ItemDeleted` (when `wasPublished`), `Cms.CollectionChanged`
  (`itemsRewritten`) and `Cms.CollectionDeleted`. They already run in Pages' transaction with
  `processed_events`.
- **Sitemap cache key**: `(site, generation, SiteVersion, basePath, file, renderer stamp)`.
  `robots.txt` key: `(site, SiteVersion, basePath)`. Both have a strong `ETag` and
  `Cache-Control: public, no-cache`, like pages.

**Rationale**: the same "key first, content after" pattern as pages (012 PR-05). An item
event already reaches Pages within the outbox delay (SC-005: 5 s).

**Alternatives considered**: deriving a key from `max(live_since)` and item stamps on every
request. That reads two modules per sitemap request and misses unpublishes.

---

## SE-16 robots.txt

**Decision**: `GET /robots.txt` (Rendering), after the base path, `text/plain; charset=utf-8`:

```text
User-agent: *
Disallow: /api/
Disallow: /admin/
Allow: /

Sitemap: https://client.example/sitemap.xml

# Custom rules
<the Administrator's rules, verbatim>
```

- With "Discourage search engines" on, the generated block is `User-agent: *` and
  `Disallow: /`. The custom rules and the `Sitemap` line are still written. The sitemap is
  empty.
- **Base path**: crawlers read `robots.txt` only at the host root. With a base path, the file
  is served at `<base>/robots.txt`, and `docs/operations.md` says how to expose it at the
  root through the proxy.

**Rationale**: FR-025 is literal. The generated part comes first so that custom rules cannot
remove the sitemap line.

---

## SE-17 Discourage search engines

**Decision**: `search_discouraged` on the site (SE-08). Effects:
- `robots.txt` disallows everything (SE-16);
- every page and item page writes `<meta name="robots" content="noindex, nofollow">`, and
  every public HTML, sitemap and robots response carries `X-Robots-Tag: noindex, nofollow`;
- the sitemap is empty;
- the admin shell shows `SearchHiddenBanner` on every screen of the site, for every member
  (FR-027).

Changing it moves `SiteVersion`, so every cached page is replaced on its next request.

---

## SE-18 Structured data: one JSON-LD graph per page, built while rendering

**Decision**:
- **Output**: one `<script type="application/ld+json">` element in the head with an
  `@graph`, written only when there is something to describe. It is a data block that the
  browser never executes. The page CSP (012 PR-12: `default-src 'none'`, no `script-src`)
  is unchanged, and no JavaScript ships (FR-037).
- **Encoding** (FR-036): `System.Text.Json` with the default encoder escapes `<`, `>`, `&`,
  `'`, `+`, U+2028 and U+2029, so no value can end the element. `JsonLdWriterTests` holds
  this with hostile values.
- **Contributions**:
  - **Breadcrumbs**: on every page but the home page, a `BreadcrumbList` of live ancestors
    (title and canonical address), then the page itself. On an item page, the template's
    ancestors, then the item's name. When the home page is not live, the first crumb is the
    site name without an `item` link. Read through a new
    `IPublishedContentReader.GetLiveAncestorsAsync(siteId, pageId)` (one query on the path
    prefixes).
  - **Home page**: `WebSite` (`name` = site name, `url` = BaseUrl), and `Organization`
    (`name`, `url`, `logo`) when `search_org_name` is set.
  - **Embed block** showing a video: `VideoObject` with `name` (the embed's `title`, else the
    page title), `embedUrl`, and `thumbnailUrl` for YouTube
    (`https://i.ytimg.com/vi/<id>/hqdefault.jpg`, built from the id, with no request made).
    `uploadDate` is not known offline and is left out. The vocabulary is satisfied, but rich
    result eligibility is not promised.
  - **Collection list block** whose collection has a live template: an `ItemList` of the
    shown items' page addresses, with positions.
  - **Item page** with `seo.structuredData`: the mapped type (SE-19).
- **Where it is defined**: each contributing renderer adds to `RenderContext.StructuredData`
  as it renders, from the values it shows (FR-034). Block types are still defined once.
- **Ancestor titles**: breadcrumbs make a page depend on its ancestors' live titles. When a
  publish, rollback or unpublish changes a page's live title or live state, `GenerationBump`
  bumps its live descendants (for the home page, every live page of the site) in the same
  transaction. A change to a page whose title stays the same bumps nothing extra.

**Rationale**: JSON-LD is the format search engines prefer. Building it during the render
uses exactly the published values shown, and no second pass reads the document.

**Alternatives considered**:
- **Microdata attributes in the body HTML**: every block renderer and golden file would
  change, and it mixes data into markup.
- **No breadcrumb dependency**: stale crumbs after a parent's rename, which breaks
  Principle VIII's exact refresh.

---

## SE-19 Content types for item pages: built-in definitions, extensible

**Decision** (clarification 3, FR-035, FR-035a):
- `docs/contracts/structured-data.md` defines each type once: its properties, the field
  types that can fill each, and its JSON-LD shape. `structured-data/types.json` holds the
  same data for both the admin control and the renderer. `StructuredDataTypesContractTests`
  and `structuredDataTypes.spec.ts` hold both sides to it.

  | Type | Property · accepts |
  |---|---|
  | `Article` | `headline` text (default: the item's name) · `description` text, rich text · `image` image · `authorName` text, reference (name) · `datePublished` date, `firstPublishedAt` (default) · `dateModified` date, `lastPublishedAt` (default) |
  | `Product` | `name` text (default: the item's name) · `description` · `image` · `brand` text, option, reference · `sku` text · `price` number · `priceCurrency` text, option (ISO 4217, three capital letters, else the offer is left out) · `availability` boolean (`InStock`/`OutOfStock`) |
  | `Event` | `name` (default: the item's name) · `description` · `image` · `startDate` date (required, else the type is left out) · `endDate` date · `locationName` text · `locationAddress` text |

- `url` is always the item page's canonical address. Empty properties are left out. A
  Product without a price is written without `offers`.
- **Adding a type later** adds an entry to the contract and the JSON file (minor version of
  that contract), with no change to existing mappings or output (FR-035a).

**Rationale**: a fixed, validated property set per type keeps the output valid, which a free
mapping cannot promise (spec clarification).

---

## SE-20 Rendering inputs, dependencies and cache keys

**Decision**:
- `RenderInputs` gains `Seo` (the parsed `seo`, or null), `Site` (the `SiteDto`: base URL,
  name, search settings), `Ancestors` and `OgImage`. The cache keys are unchanged in shape:
  - page metadata is in the document, so it moves `Generation`;
  - site settings move `SiteVersion`;
  - item values move the render stamp (016).
- **`DependencyExtractor`** (Pages) adds:
  - `seo.ogImage` media (so 013's `Media.FileChanged` refresh covers it);
  - `seo.canonical` page targets (link-target dependency, so a canonical follows a move);
  - the ancestors (SE-18).
- **Site-wide images**: the default sharing image and the logo are not page dependencies.
  Pages' existing `Media.FileChanged` and `Media.FileDeleted` handlers read the site's
  settings through `ISiteDirectory`. When the file is one of them, they bump every live
  page of the site (rare, bounded by 2,000 pages).
- **Renderer stamp**: the new head moves the build stamp, so every cached page renders once
  after the upgrade (as in 016).

---

## SE-21 Admin

**Decision**:
- **Editor**: a "Search and sharing" button in `EditorTopBar` opens `SeoPanel` (a side
  drawer) bound to `root.props.seo` through the editor's ops, so undo, redo, autosave and
  conflict handling apply unchanged.
  - **Fields**: length counters with the 60 and 160 warnings, a search preview and a sharing
    preview, and the media picker.
  - **Canonical**: a page picker or URL. **Hide and leave out**: switches.
  - **Templates**: each field gets the existing `BindingControl` (scope `page`), a fallback
    text, and a `StructuredDataControl` for the type and its property bindings.
  - **Restricted fields** are disabled for members without `pages.structure.manage`, with a
    reason.
- **Redirects**: `features/seo/RedirectsView.vue` (list, search, sort by usage, states),
  `RedirectDialog.vue`, `RedirectImportDialog.vue` (file, then a per-line report) and
  export. Route `/sites/:id/redirects`, in the site navigation.
- **Site settings**: `features/sites/SearchSettingsPanel.vue` (suffix, image, organisation)
  and `SearchVisibilityPanel.vue` (discourage, robots rules, host redirect with its
  confirmation).
- **Shell**: `components/SearchHiddenBanner.vue` when the site is discouraged.
- **Offers**:
  - `MoveDialog`, `PageFormDialog` (slug), `TemplateDialog` and the home page action show
    "Redirect old addresses (301)", ticked when the change moves live addresses;
  - unpublish and delete confirmations offer "Redirect this address to…" with a page picker;
  - the item form does the same for slug changes on publish, schedule and rollback, and for
    unpublish and delete.
- Every control is reachable and operable by keyboard, and axe-checked (SC-008).

---

## SE-22 Integration events

| Event | Change |
|---|---|
| **+** `Cms.ItemRedirectRequested` | SE-11. Pages subscribes. |
| `Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted`, `CollectionChanged`, `CollectionDeleted` | Pages' existing handlers also bump the sitemap generation. |
| `Media.FileChanged`, `Media.FileDeleted` | Pages' existing handlers also check the site's default sharing image and logo (SE-20). |

No event is added for redirect or site-setting changes. Redirects are read from the database
on every miss, and site settings move `SiteVersion`, so every instance sees a change at its
next request without a message (FR-039, Principle IV).

---

## SE-23 Upgrade and migrations

**Decision**:
- **Pages `AddSeo`**: `+ redirects`, `+ redirect_usage`, `+ redirect_changes`,
  `+ sitemap_state`, `~ publish_schedules.redirect_to_page_id`. It fills one `sitemap_state`
  row per site that has pages.
- **Sites `AddSearchSettings`**: the columns of SE-08 and `search_updated_by`, defaulting to
  null or false. `redirect_other_hosts` is `false` for existing rows.
- **Cms `AddScheduleRedirect`**: `item_schedules.redirect_old_address boolean not null
  default false` and `item_schedules.redirect_to_page_id uuid null`.
- **Page schema v1**: additive (SE-02). No stored document is rewritten.
- **`UpgradeFrom016Tests`** run on a 016 snapshot. They check that:
  - every page body renders identically;
  - titles are unchanged (no suffix set);
  - the head gains only the canonical address and the Open Graph tags;
  - existing sites keep serving every host name;
  - `robots.txt` and `sitemap.xml` answer.
- **Site export (#20)**: page metadata travels in page documents. `#20` must also carry the
  redirects (`page`, `item` and `path` targets remapped to the new ids) and the site search
  settings. This is recorded as an obligation in `docs/backlog.md` and 020's future plan
  input (FR-040).

---

## SE-24 Testing strategy

| Suite | Proves |
|---|---|
| `SeoSchemaGoldenTests` + `seoSchema.spec.ts` | rules 22–25 and the binding compatibility rows, on shared golden documents (both validators) |
| `HeadMetadataGoldenTests` | every field, fallback, encoding of hostile values, `og:locale` mapping, image choice (FR-003, FR-005, SC-007) |
| `JsonLdWriterTests`, `StructuredDataGoldenTests` | escaping; breadcrumbs, website, organisation, video, item list, Article/Product/Event (SC-007) |
| `StructuredDataTypesContractTests` + admin spec | one definition of the types on both sides (FR-035a) |
| `SeoTemplateRenderTests` | bound metadata, plain-text conversion, cutting, fallbacks, broken bindings, hide switch (US4) |
| `SeoDraftPermissionTests` | Editors cannot change canonical or hide; unchanged values pass (FR-002) |
| `RedirectRulesTests` (unit) | source normalisation, canonical queries, chains, loops, prefix matching |
| `RedirectEndpointsTests` | CRUD, concurrency, limits, states, authorization (FR-015 to FR-019) |
| `RedirectImportTests` | BOM, header detection, quoting, bad encoding, duplicates, loops, line reports, limits, export round trip, formula guard |
| `RedirectMatchingTests` | the FR-016 order end to end, query carry rules, 301/302 headers, broken targets, normalisation redirect |
| `RedirectOfferTests` | every FR-028 change with the flag on and off; FR-031 skip; retargeting; loops turned off; Editor item offer (FR-030); scheduled item publish |
| `RedirectUsageTests` | counting, flush, two instances adding up, deleted redirect, no DB write on the request path |
| `HostRedirectTests` | setting off and on, media and assets excluded, new versus upgraded sites |
| `SitemapTests` | content, indexability, dates, ordering, index at 50,001, numbered files, discouraged site, base path (US3) |
| `SitemapFreshnessTests` | every bump source, two instances (SC-005) |
| `RobotsTests` | default, custom rules, discouraged, base path |
| `BreadcrumbInvalidationTests` | an ancestor's title change refreshes descendants; unrelated pages stay cached |
| `SeoIsolationProbe` (isolation suite) | redirects, sitemap and settings never cross sites (FR-038) |
| `UpgradeFrom016Tests` | SE-23 |
| `SitemapPerformanceTests`, `RedirectPerformanceTests` (smoke, `Category=Performance`) | SC-004, SC-006 on the reference setup |
| admin specs with axe | panels, dialogs, import report, keyboard only (SC-008) |

---

## SE-25 Performance

- **Redirect miss path**: one indexed query (`IRedirectReader`), plus one more only for query
  requests. Measured with 10,000 redirects (SC-004).
- **Pages and item pages**:
  - one more read on a cache miss: ancestors;
  - head writing is microseconds;
  - the cache-hit path is unchanged except for the one string comparison of SE-06.
- **Sitemap**: one build per generation. 100,000 items take about 10 keyset reads of 10,000
  rows. The target is under 2 s per file on a miss and under 50 ms on a hit (SC-006).
- **Busy sites**: every item publish bumps the sitemap generation, so a site publishing
  constantly rebuilds its sitemap on the next sitemap request. This is accepted: crawlers
  fetch sitemaps rarely, and a rebuild happens only when one is requested.

---

## SE-26 Dependencies

None. `XmlWriter`, `System.Text.Json` and the in-house CSV parser cover every format. The admin
uses existing components (media picker, page picker, `BindingControl`).

---

## Things deliberately not built

- Wildcard or regular-expression redirects (clarification 1).
- A not-found log (clarification 3).
- Per-item metadata fields (spec Out of Scope). A switch field bound to hide covers the one
  per-item need.
- Image, news and video sitemaps; pinging search engines.
- `twitter:*` tags; `hreflang`; per-locale sitemaps.
- A cache for redirect lookups.

## Configuration keys

None. The flush interval (60 s), the sitemap file size (50,000) and the redirect limits are
constants: the protocol fixes one, and the others are spec defaults.

## Resolved unknowns

| Unknown | Resolution |
|---|---|
| Where redirects live | Pages (SE-01, SE-09) |
| How item slug changes reach redirects | `Cms.ItemRedirectRequested` (SE-11) |
| Whether page addresses change at publish | No: at tree changes (SE-11) |
| How site settings refresh pages | `SiteVersion` (SE-08) |
| Whether JSON-LD conflicts with the CSP or the JS budget | No (SE-18) |
| How the sitemap stays exact | A per-site generation (SE-15) |
| Counting usage without public writes | In-memory counters plus a background flush (SE-12) |
