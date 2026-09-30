# Research: Publishing and Rendering

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #12

Decisions are numbered **PR-01 … PR-23** and referred to by number from the plan, the data
model and the contracts. Each one gives the decision, why, and what else was considered.

---

## PR-01 Where publishing lives: Pages and Editor publish, a new Rendering module serves

**Decision**:
- **Pages** publishes pages and components. It owns published versions, the live pointer,
  schedules, the dependency graph and the publish log (PR-02 … PR-05, PR-14 … PR-16).
  Publishing is a change to page content, and the constitution requires a rich domain model
  for Pages.
- **Editor** publishes the design (PR-17), because it owns the token set.
- **Rendering**, a new module, owns the renderer, the output cache, the public page
  endpoint, the block stylesheet endpoint and the not-found page (PR-08 … PR-13). It has
  **no tables**. It reads published data through read-only contracts of Pages, Editor,
  Media and Sites (PR-06).

**Rationale**:
- Publish is an atomic change to data one module owns. If it ran in Rendering, it would
  have to write other modules' tables or span modules in one transaction, and Principle I
  forbids both.
- Keeping Rendering free of tables makes the public path read-only by construction
  (Principle VII), and it can be scaled or cached without touching content.

**Alternatives considered**:
- **A new Publishing module holding copies of pages, components and tokens**: this is not
  one of the constitution's named modules. It would duplicate every document through
  events, which means a second copy that can lag behind, and publish would stop being one
  transaction.
- **Rendering owns the versions**: the same cross-module write problem, reversed.

---

## PR-02 Published versions: immutable copies, a live pointer, and a log kept apart

**Decision**: Three kinds of record in the `pages` schema ([data-model.md](./data-model.md)
§1):
- **`published_versions`**: one immutable row per publish, holding a copy of the document
  (`jsonb`), the schema version and, for pages, the title. It also holds the number, the
  kind (`publish`, `scheduled`, `rollback`), `restored_from_number`, when and by whom.
- **`publications`**: one row per page or component that was ever published. It holds the
  live version (null when unpublished), the next number, the draft version the live one was
  taken from, and the page's render **generation** (PR-05).
- **`publish_log`**: one row per action, holding who, when, what and which version, plus a
  label and path taken at that moment. It is not tied to the versions or the subject row,
  so it survives both (clarification Q2 of 2026-09-29, FR-017).

**"Draft has unpublished changes"** (FR-007) is computed without comparing documents:
- **The draft**: the live version records the draft version it was copied from. The draft
  differs when `draft.version ≠ publication.published_from_draft_version`.
- **The title**: for pages, it differs when `page.title ≠ live version title`.
- **After a rollback**: the source draft version is null, so the draft reads as different
  until the next publish.

**Rationale**: versions must be immutable to be worth rolling back to. Separating the log
from the versions is what lets version content be pruned (PR-15) or deleted with a page
while the audit trail stays.

**Alternatives considered**:
- **A `published_document` column beside the draft**: this gives no history, and rollback
  is required.
- **Event-sourced drafts**: far beyond what rollback needs (Principle XI).

---

## PR-03 Atomic publish and concurrent publishes

**Decision**: A publish is one Pages transaction:
1. validate the draft;
2. check that referenced components are published (FR-003);
3. insert the version;
4. move the live pointer and increment the number;
5. rewrite the dependency rows (PR-04);
6. bump generations (PR-05);
7. cancel any pending scheduled publish (FR-019);
8. prune old versions (PR-15);
9. append to the log;
10. write the integration event (PR-19).

**Preconditions and concurrency**:
- **Draft version**: the request carries the draft version the member saw (`draftVersion`).
  A publish of a draft that someone has changed since is refused as
  `pages.out_of_date` (010 FR-021). A member never publishes content they have not seen.
- **Two publishes at once**: the `publications` row carries an `xmin` concurrency token. A
  conflict retries the whole transaction up to 3 times (the same pattern as 010's
  `TreeConcurrencyRetry`), so both publishes succeed one after the other with consecutive
  numbers.
- **Numbers**: a unique index on `(subject_kind, subject_id, number)` makes a repeated
  number impossible even if the retry logic were wrong.

**Rationale**: FR-001's all-or-nothing rule, and the spec's edge case that concurrent
publishes both succeed and numbers never repeat or skip.

**Alternatives considered**:
- **`SELECT … FOR UPDATE`**: equivalent, but it needs raw SQL for something EF's
  concurrency token already does.
- **Refusing the second publish as out of date**: this contradicts the spec, where both
  succeed.

---

## PR-04 The dependency graph: recorded at publish time, in Pages

**Decision**: `pages.live_dependencies` has one row per (live page or component → target):
- **`component`**: a page's `component-ref` nodes.
- **`page`**: page links in Button `link` props and in rich-text link marks.
- **`media`**: Image `media` props.

It is rewritten in the publish (or rollback) transaction from the version's document, and
deleted on unpublish. It describes **live** versions only.

It answers four questions:
1. **Which pages must refresh when X changes**: pages depending on X directly, plus pages
   depending on a component that depends on X. That is two levels, because components do
   not nest (010 FR-027).
2. **Is this component in use by published pages** (FR-004).
3. **Which pages link to a page whose address or published state changed.**
4. **Which pages show an image that was deleted.**

**Rationale**: Principle VIII names a tracked page → component graph. Recording it when a
page is published, rather than when it is rendered, avoids a race. If a render recorded
"page P uses component C" only after reading C, a publish of C landing in between would
find no dependents and leave P stale. The publish transaction knows the complete set
before any visitor can request the version.

**Alternatives considered**:
- **Recording while rendering**: has the race just described.
- **Reusing `component_usages`**: that table tracks drafts (010 FR-028), not live versions.
  Both are needed: drafts block component deletion, and live versions block unpublish and
  drive refreshes.

---

## PR-05 Invalidation by generation: change the key, don't delete the entry

**Decision**: A cached page is keyed by everything its HTML depends on:

```
site:{siteId}:page:{pageId}:g{generation}:d{designNumber}:s{siteVersion}:r{rendererStamp}
```

- **`generation`**: `publications.generation` of the page. It is incremented in the **same
  transaction** as anything that changes the page's output:
  - its own publish, rollback or unpublish;
  - a publish, rollback or unpublish of a component it uses;
  - the publish or unpublish of a page it links to (directly or through a component);
  - an address change of such a page (010's tree operations, in the same Pages
    transaction);
  - the deletion of a media file it shows (PR-11, through an event).
- **`designNumber`**: the site's live design version (PR-17), `0` for the defaults.
- **`siteVersion`**: `SiteDto.Version`, which changes when the default language or other
  site settings change. It is already loaded by site resolution, so it costs nothing.
- **`rendererStamp`**: a hash of the block stylesheet and the renderer assembly version, so
  an upgrade never serves HTML built by the previous release.

On every public request, Rendering reads the page id and generation for the path (one
indexed lookup in Pages) and the live design number (one primary-key lookup in Editor).
It then looks the key up in the cache. Stale entries are never looked up again; they age
out by sliding expiry and size eviction (PR-07).

**Consequences**:
- **Freshness is immediate on every instance**, whatever the cache provider. A publish on
  instance A changes the database, and instance B's next request builds the new key. This
  meets FR-033 and SC-002 without a shared cache, and removes the multi-instance defect
  that exception **E-4** records for this consumer (plan, Complexity Tracking).
- **"Exactly the affected entries"** (FR-032, SC-006): only pages whose generation was
  bumped get a new key. Every other page keeps its key and stays cached.
- **Race-free**: the generation is read before the content. Anything published after that
  read bumps the generation after it, so a render that read older content is stored under
  a key nobody will ask for again.
- **Cost on a cache hit**: site resolution (existing) plus two indexed lookups, about 1 ms
  each on the reference setup. That is well inside the 20 ms budget (SC-003), and it is
  measured (PR-21).

**Alternatives considered**:
- **Delete entries on publish, spread by event**: correct only with a shared cache or a
  cross-instance bus. It would add Redis or a pub/sub channel as a requirement, against
  Principle IV's single-server default, and it has an event-lag window.
- **A time-to-live only**: stale by design; fails SC-005.
- **A per-site generation only**: correct but coarse. Publishing one page would flush the
  whole site, which fails SC-006.

---

## PR-06 The public path is read-only, and the database enforces it

**Decision**: Rendering reads only through reader interfaces in other modules' Contracts
([contracts/readers.md](./contracts/readers.md)):
- `IPublishedContentReader` (Pages);
- `IPublishedDesignReader` (Editor);
- `IPublicMediaReader` (Media);
- `ICurrentSite` (Sites, existing).

Each reader is implemented in its owning module over a **read-only connection**: the
module's connection string with Npgsql `Options = "-c default_transaction_read_only=on"`,
built by a small helper in `Shared.Infrastructure`. A write attempted through it fails in
PostgreSQL with SQLSTATE `25006`.

The reader contexts map only published tables, use `NoTracking`, and override
`SaveChanges` to throw.

- `PublicPathReadOnlyTests` proves the database refuses a write through each reader's
  connection.
- An architecture test proves that Rendering references no module implementation and has
  no `DbContext` of its own.

**Rationale**: Principle VII says the public rendering path MUST NOT have write access to
content tables. A code convention alone would be a promise; this makes it a property of
the database session, using the same credentials, so the operator configures nothing new.

**Alternatives considered**:
- **A separate PostgreSQL role with SELECT grants**: the strongest guarantee, but a second
  credential for every operator, and migrations would have to maintain grants (Principle
  II).
- **Convention and review only**: not enforceable.

---

## PR-07 The output cache: the shared abstraction, bounded, single-flight

**Decision**:
- **Reshape `ICacheProvider`**, which has had no consumer since 009 removed host-name
  caching:
  - `GetOrCreateAsync` takes `CacheEntrySettings(Size, SlidingExpiration,
    AbsoluteExpiration)`.
  - The in-memory provider uses its own `MemoryCache` with a `SizeLimit` from the new key
    **`Cache:MemoryLimitMegabytes`** (default `256`, validated from 16 to 16,384 at
    startup).
  - It adds **single-flight** per key: concurrent misses for one key share one factory
    call, so a burst on an uncached page renders it once per instance.
- **What an entry holds**: `RenderedPage(byte[] Utf8Html, string ETag, string
  StyleHash)`. The size is the byte length. Entries have a 1-hour sliding expiry.
- **HTTP caching** (FR-035):
  - `ETag` is a hash of the cache key, and `Cache-Control: public, no-cache`.
  - Browsers and proxies may keep the page but must revalidate each time. An unchanged
    page answers `304` from the key alone, without rendering or reading the cache, so
    revalidation is as cheap as a hit and never serves stale content.
- **Compression** is left to the operator's reverse proxy, as the deployment docs already
  say for TLS.

**Rationale**: Principle IV requires the cache to be reached through the abstraction.
Bounding memory by size makes a site with thousands of pages safe on the 4 GB reference
machine.

**Alternatives considered**:
- **ASP.NET Core Output Caching middleware**: its invalidation is tag eviction per
  instance, which is the E-4 problem again. It also sits outside the provider abstraction.
- **`Cache-Control: max-age=60`**: faster for repeat visitors, but a visitor could see a
  page up to 60 s stale after a publish, which breaks FR-033.

---

## PR-08 Public routing, address matching and the not-found hand-off

**Decision**:
- **The page endpoint**: `RenderPageController` in Rendering answers
  `GET`/`HEAD {**path:publicPath}` with the lowest route order, on the public surface only.
  - Admin, API and health prefixes never reach it: `RoutePrefixes`, and the route
    constraint `publicPath`, refuse them.
  - Other methods on a public path get `405` with `Allow: GET, HEAD`.
- **Address normalisation**, before lookup:
  - percent-decode once;
  - refuse `.` and `..` segments and control characters;
  - trim one trailing `/` (except the root);
  - lower-case with the invariant culture, because addresses are stored lower-case (010).
  - `/About` and `/about/` serve `/about` directly. Redirects to a canonical address are
    #17.
- **The block stylesheet**: `GET /_assets/blocks.{hash}.css`, served with
  `Cache-Control: public, max-age=31536000, immutable`. A wrong hash gets the not-found
  page. `/_assets` joins `RoutePrefixes.PublicReservedPrefixes`. The slug grammar can't
  produce `_`, so the contract's reserved list needs no entry, and
  `ReservedAddressCoverageTests` confirms that.
- **Not found**: Rendering.Contracts exposes `IPublicNotFoundPage.RenderAsync(SiteDto,
  ct) → PublicResponse`. `PublicResponses.WritePublicNotFoundAsync` in the host writes that
  response. So the same bytes answer:
  - a missing page (from `SiteResolutionMiddleware` and from the page endpoint);
  - an admin path on a client domain (`AdminHostConfinementMiddleware`, 009 S-13).
  The site-not-resolved JSON answer for unknown and disabled hosts is unchanged (FR-030).

**Rationale**: 009 made "missing page" and "admin path on a client domain" byte-identical
on purpose, so the admin surface can't be detected. One writer for both keeps that true
now that the answer is an HTML page.

---

## PR-09 The renderer: one C# renderer per block type, writing to a pooled UTF-8 buffer

**Decision**:
- **One renderer per block type**: `IBlockRenderer { string Type; void Render(NodeContext,
  HtmlWriter); }`, one class per block type in `Rendering/Blocks/<Type>Renderer.cs`.
  `component-ref` is a renderer too: it writes the component's live version in place, or
  nothing if the component has none.
- **Coverage**: `BlockRendererCoverageTests` fails the build if a node type in
  `v1.schema.json` has no renderer, or a renderer names a type the schema lacks. That keeps
  "defined once" true (Principle VI).
- **Defaults**: `BlockDefaults` reads each property's `default` from the embedded
  `v1.schema.json` (011 ED-07), so a missing property renders exactly as the editor shows
  it.
- **Writing**: `HtmlWriter` writes to an `ArrayBufferWriter<byte>`.
  - Text goes through `HtmlEncoder.Default`, attributes through the attribute path, and
    URLs through `UrlEncoder` after scheme validation (PR-11).
  - No API writes raw strings from content: raw output is limited to constant markup inside
    the renderer.
- **Per-node style values**: token-driven values such as `--qb-section-bg` are collected
  into one `<style>` element as `[data-qb="<nodeId>"]{…}` rules, instead of `style`
  attributes. That lets the page's CSP allow exactly that style block by hash (PR-12). Node
  ids match `^[A-Za-z0-9_-]{1,64}$` (010 contract), so they are safe in a selector
  unescaped.
- **Versions**: `RenderDocument` dispatches on `schemaVersion`. Only version 1 exists, and
  `RendererSchemaVersionsTests` fails if the schema folder gains a version the renderer
  doesn't list (FR-029).

**Rationale**: Principle IX (encode for context, allow-list), Principle VI (one definition
per block type), and the SC-003 miss budget. A render is a single pass over at most 5,000
nodes with no reflection and no templating engine.

**Alternatives considered**:
- **Razor views per block**: this brings the MVC view engine and runtime compilation into
  a module that needs neither, and HTML-encoding discipline is harder to audit across
  `.cshtml` files.
- **Server-side rendering of the Vue components (Node)**: violates "operators never run a
  separate Node process" (Technology Constraints).

---

## PR-10 One markup and one stylesheet for the canvas and the published page

**Decision**:
- **The stylesheet**: `src/admin/src/editor/blocks/blocks.css` is embedded into
  `QuickBuilder.Rendering` as a resource (MSBuild `EmbeddedResource` with a link to the
  admin file). It is served at `/_assets/blocks.{sha256-12}.css` (PR-08). There is still
  one file.
- **The page wrapper**: blocks.css gains `.qb-page { container-type: inline-size;
  container-name: page; }`. The published body wrapper carries `class="qb-canvas
  qb-page"`, so the reset layer and container queries apply exactly as in the canvas (page
  schema contract §9, which this feature makes normative).
- **Token CSS**: `TokenCss` in Rendering ports `tokenCss.ts`. Both run the shared fixtures
  `tests/fixtures/rendering/token-css/*.json` (a token set and its expected property map),
  in xUnit and vitest.
- **Markup parity** (SC-008): for each golden document in
  `docs/contracts/page-schema/examples/valid/`, the C# renderer writes
  `tests/fixtures/rendering/golden/<name>.html`, checked in and compared byte for byte by
  `RendererGoldenTests`.
  - `CanvasMarkupParity.spec.ts` mounts the canvas block components for the same documents
    and normalises both trees: it keeps element names, `qb-*` classes, the allow-listed
    attributes and each node's custom properties, and drops editor-only attributes.
  - It asserts they are equal.
  - The documented exceptions are the Embed internals (a placeholder in the canvas, an
    iframe when published) and the Image placeholder for missing media.

**Rationale**: the spec requires the published layout to match the canvas at every width.
Sharing the stylesheet removes one source of drift; the parity test catches the other,
which is markup.

**Alternatives considered**:
- **A second stylesheet for published pages**: drift is certain.
- **Copying blocks.css into the Rendering project in the build**: that makes two files in
  the repository.

---

## PR-11 Rich text, links, images and embeds

**Decision**:
- **Rich text**: an allow-list walk:
  - `paragraph` → `p`, `bulletList` → `ul`, `orderedList` → `ol`, `listItem` → `li`;
  - `hardBreak` → `br`, and `text` is encoded;
  - marks: `bold` → `strong`, `italic` → `em`, `link` → `a` (below).
  - Anything else cannot occur, because the document was validated at publish. The
    renderer still skips unknown nodes rather than failing a page.
- **Links**:
  - **To a page**: the linked page's current address from `IPublishedContentReader`,
    prefixed with `Hosting:BasePath`, written root-relative. If the target is not
    published, or does not exist, the link is written as plain text in a `span` that keeps
    the button's classes (FR-026).
  - **External**: the `href` is re-checked against the contract's scheme pattern, then
    encoded.
  - **New tab**: `target="_blank" rel="noopener noreferrer"` (FR-025).
- **Images**: `IPublicMediaReader` resolves the page's media ids in one call.
  - An available file gets
    `<img src="{basePath}/_media/{id}.{ext}" alt="…" loading="lazy" decoding="async">`.
    A decorative image gets `alt=""`.
  - A missing file is left out (FR-027).
  - A new integration event, `Media.FileDeleted`, is handled in Pages. The handler bumps
    the generation of pages that show the file (PR-04, PR-05); it is idempotent, with its
    own processed-event row, as 009's Identity handler does. Such pages refresh within the
    outbox poll interval (5 s by default).
- **Embeds**: the video id is parsed from the stored address.
  - The player is `<iframe src="https://www.youtube-nocookie.com/embed/{id}">` or
    `https://player.vimeo.com/video/{id}?dnt=1`, with `loading="lazy"`, a `title`, and
    `allow="fullscreen; picture-in-picture"`.
  - `loading="lazy"` is the browser's own deferral: the provider is contacted only when
    the frame nears the viewport, with no script (FR-027, FR-028).

**Rationale**: Principle IX, and zero JavaScript on published pages.

**Alternatives considered**:
- **A click-to-load facade with script**: stronger privacy, but it needs JavaScript and a
  thumbnail fetched from the provider. It is left to a later privacy option.

---

## PR-12 The page shell, headers and zero JavaScript

**Decision**: Each page is:

```html
<!doctype html>
<html lang="{site default language}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{version title}</title>
  <link rel="stylesheet" href="{basePath}/_assets/blocks.{hash}.css">
  <style>{token custom properties on .qb-page}{per-node rules}</style>
</head>
<body class="qb-published">
  <div class="qb-canvas qb-page">{blocks}</div>
</body>
</html>
```

Response headers:
- `Content-Type: text/html; charset=utf-8`;
- `Content-Security-Policy: default-src 'none'; style-src 'self' 'sha256-{hash of the
  style block}'; img-src 'self'; frame-src https://www.youtube-nocookie.com
  https://player.vimeo.com; base-uri 'none'; form-action 'none'; frame-ancestors 'self'`;
- `X-Content-Type-Options: nosniff`;
- `Referrer-Policy: strict-origin-when-cross-origin`;
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`;
- `ETag` and `Cache-Control` (PR-07).

**No script** is emitted, so FR-028 holds with 0 bytes. `PublishedPageJavaScriptBudgetTests`
counts `<script>` bytes and script `src` sizes on rendered golden pages and fails above
100 KB, so a future block that needs script is caught.

**Rationale**: the strictest CSP that the output allows, computed once per render and
cached with the page.

---

## PR-13 The not-found page

**Decision**: A fixed template rendered by Rendering:
- the same shell, with the site's live design tokens and blocks.css;
- no components (clarification Q4 of 2026-09-29);
- a heading and a sentence from the public text catalogue in the site's default language;
- a link to `{basePath}/` only when the home page is live.

The status is `404` and `Cache-Control: no-store`.

- **Caching**: the rendered bytes are cached under `site:{id}:notfound:d{design}:s{site}:
  h{homeLive}:r{stamp}`, so a flood of random addresses costs lookups, not renders.
- **The text catalogue**: `Rendering/Resources/public-texts/{lang}.json`, with one file
  for each of the 40 tags in the Sites module's `SupportedLanguages`, so every site gets the
  message in its default language (FR-030; analysis I2). There are three short strings per
  language.
  - `PublicTextCatalogueTests` requires a file per supported tag, each with exactly
    `en.json`'s keys (SC-009).
  - A tag added to Sites later without a file falls back to English instead of failing.
  - Machine-drafted translations are listed for native review in `quickstart-results.md`.

**Rationale**: FR-030. The page must not reveal whether an unpublished page exists, so it
is the same for every missing address on a site.

---

## PR-14 Scheduling: delayed messages in the Pages outbox

**Decision**:
- **Setting a schedule** (FR-018, FR-019):
  - It captures the draft (document, title, schema version, draft version), validated then.
  - It stores it in `publish_schedules` with a `revision`.
  - It writes an outbox message `Pages.PublishScheduleDue { scheduleId, revision }` whose
    `available_at_utc` is the due time.
  - All of that is in one transaction. This is the "outbox worker" the issue names, and it
    is the product's built-in, database-backed background job mechanism (Principle IV).
- **Running it**:
  - The dispatcher claims the message at its time, within one poll interval (5 s default),
    well inside FR-020's one minute.
  - The Pages handler loads the schedule. If it is not `pending`, or its revision differs,
    the handler does nothing.
  - Otherwise it runs the same domain operation as a manual publish or unpublish, with
    kind `scheduled`, marks the schedule `done`, and logs it. All of that is in the Pages
    transaction the dispatcher already holds.
- **Exactly once**:
  - `FOR UPDATE SKIP LOCKED` means only one instance claims the message.
  - The schedule's state change is the idempotency guard, so a redelivery after a crash
    finds `done`.
  - A stopped application simply leaves the message pending until it starts again.
  - `ScheduledPublishExactlyOnceTests` covers two dispatchers, a restart, and a
    redelivery.
- **Failures** (FR-021): a domain refusal at run time (for example
  `pages.unpublished_components`) marks the schedule `failed` with the reason, logs it, and
  returns normally, so the outbox doesn't retry a decision. Infrastructure errors throw and
  take the outbox retry path.
- **Changes**:
  - Reschedule updates `due_at_utc`, increments `revision`, and writes a new message. The
    old message becomes a no-op.
  - Cancel sets `cancelled`.
  - A manual publish, unpublish or rollback cancels a pending scheduled publish (FR-019).
  - A manual unpublish also cancels a pending scheduled unpublish, which would otherwise
    have nothing to do.
  - A partial unique index allows one pending schedule per subject and action.
- **Times**: the API takes and returns UTC ISO-8601. The admin shell converts from and to
  the member's time zone and names it (spec Assumptions). A time not in the future on the
  server is refused as `pages.schedule_in_past`.

**Rationale**: it reuses the one background mechanism that already guarantees claiming
across instances, retries and restart safety, instead of adding a second scheduler.

**Alternatives considered**:
- **A polling `BackgroundService` over `publish_schedules`**: a second worker with its own
  locking and restart logic, duplicating the outbox.
- **Quartz.NET or Hangfire**: a new dependency and tables for one job type (Principle XI).

---

## PR-15 Version retention and deletion

**Decision**:
- **Retention** (FR-013): after each publish or rollback, the same transaction deletes the
  versions of that subject beyond the 50 newest, never the live one. Log entries stay.
- **Deleting**:
  - Deleting a page or component is refused while it is live
    (`pages.page_published`, FR-006), or for a component, while a live page uses it
    (`pages.component_in_use_by_published_pages`, FR-004).
  - Otherwise the delete removes its versions, publication row and schedules by cascade,
    and appends a `deleted` log entry if it had any publish history. That entry carries the
    last title (or name) and path.
- **Rolling back to a version that references a deleted component** is refused as
  `pages.version_missing_components` (FR-014).

---

## PR-16 The publish log

**Decision**:
- **Storage**: `pages.publish_log` is append-only.
  - Page and component entries are written in the transaction of the action.
  - Design entries come from `Editor.DesignPublished` through a Pages handler (PR-19). That
    handler is idempotent because each entry's id is the event id, under a primary-key
    constraint. They appear within the outbox poll interval.
- **Reading**:
  - `GET …/publish-log` is filtered by subject and by user, newest first, with a cursor. It
    needs the new capability `pages.publish_log.view`, held only by Administrator
    (FR-017).
  - A subject's own history (FR-016) is the log filtered to that subject, joined with the
    retained version numbers so the admin shell knows which entries can be rolled back to.
    It needs `pages.content.view`.
- **Content**: no document content is in the log. It holds labels and paths only.

---

## PR-17 Design publishing in the Editor module

**Decision**: Beside the draft token set (011), Editor gains two tables:
- `editor.design_versions`: immutable copies of the token document, numbered, with kind
  and author;
- `editor.design_publications`: the live number, the next number and the token set version
  it was copied from.

Operations:
- **Publish** (`POST …/design-tokens/publish` with `version`) validates the draft set,
  copies it, moves the live pointer, prunes to 50 versions and writes `Editor.DesignPublished`,
  all in one Editor transaction.
- **Rollback** republishes a version as a new one.
- **Before the first publish**, `IPublishedDesignReader` returns the built-in default set
  with number `0` (FR-010). This matches 011's lazy defaults, and there is no migration
  data.
- **The design screen** shows "unpublished changes" when the token set version differs from
  the one the live version was copied from.

**Rationale**: clarification Q1 (a separate, recorded, rollback-able design publish), and
Principle I: Editor owns the tokens, so it owns their published copies.

---

## PR-18 Authorization

**Decision**: two new capabilities, registered by Pages:

| Capability | Scope | Held by | Used for |
|---|---|---|---|
| `pages.publishing.manage` | Site | Designer (and Administrator everywhere) | Publish, unpublish, rollback and schedule pages and components; publish and roll back the design (Editor references the name from `Pages.Contracts`) |
| `pages.publish_log.view` | Site | Administrator only | The site publish log |

- **Reads**: history and version reads use `pages.content.view`, and design history uses
  `editor.design_tokens.view`.
- **Copy to draft** uses `pages.page_drafts.edit` for pages and `pages.components.manage`
  for components (FR-015).
- **Editors** hold neither new capability (clarification Q3).
- `specs/002-identity-setup/contracts/authorization.md` §2 gains both.
  `AuthorizationDeclarationTests` covers every new action. Not found versus forbidden
  follows 002.

---

## PR-19 Integration events

**New published contracts** (payloads carry ids, numbers and paths only; FR-038, FR-044):

| Event | Published by | When |
|---|---|---|
| `Pages.PagePublished` | Pages | A publish, scheduled publish or rollback of a page (`kind`) |
| `Pages.PageUnpublished` | Pages | A manual or scheduled unpublish |
| `Pages.ComponentPublished` | Pages | A publish, scheduled publish or rollback of a component |
| `Pages.ComponentUnpublished` | Pages | An unpublish of a component |
| `Editor.DesignPublished` | Editor | A design publish or rollback (`kind`) |
| `Media.FileDeleted` | Media | A media file was deleted |

**Module-internal**: `Pages.PublishScheduleDue` (PR-14) is handled only by Pages.

**Subscribers in this feature**:
- Pages handles `Editor.DesignPublished`, writing the log (PR-16).
- Pages handles `Media.FileDeleted`, bumping generations (PR-11).
- Rendering subscribes to nothing, because it needs no events (PR-05).
- #17 (sitemap) will subscribe to the page events.

---

## PR-20 Admin shell

**Decision**:
- **Page tree**:
  - A status badge per page: Draft only, Published, Published with changes, Scheduled,
    Schedule failed.
  - A **Publishing** menu: Publish, Unpublish, Schedule, History, View live page.
  - The tree API gains a `publication` summary per page.
  - Before a slug change or move, the move and rename dialogs count the published pages in
    the affected subtree from tree data, and warn if the count is above zero (FR-011).
  - Delete is disabled with a reason for published pages.
- **Component list**: the same menu, without View live.
- **Editor top bar**: **Publish**, shown to holders of `pages.publishing.manage`. It saves
  pending changes first, then publishes with the saved draft version. It also shows the
  publication state.
- **History panel** (a route per subject):
  - versions and log entries;
  - **Roll back** and **Copy to draft**, each with a confirmation;
  - **View**, which opens the version in the editor's existing read-only mode (011 ED-15)
    at `/sites/:siteId/pages/:pageId/versions/:number` (FR-016).
- **Schedule dialog**: a date and time in the member's time zone, shown with the zone
  name. It warns when a scheduled publish will be cancelled by a manual action (FR-019).
- **Design screen**: **Publish design**, a history of design versions, and **Roll back**.
- **Publish log**: `/sites/:siteId/publish-log`, for Administrators.
- **Baselines**: everything goes through vue-i18n, with keyboard paths and axe tests
  (SC-009).

---

## PR-21 Testing strategy

| Area | Tests |
|---|---|
| **Domain (unit)** | `PublicationTests` (publish, unpublish, rollback, numbering, draft-changed flag). `PublishScheduleTests` (state machine, revision). `DependencyExtractorTests` (component refs, links in buttons and rich text, media). `GenerationBumpTests` (two-level expansion). |
| **Renderer (unit)** | `RendererGoldenTests` (every golden document to checked-in HTML). One test class per block renderer, including encoding cases (`<script>` in text, quotes in attributes, `javascript:` refused). `RichTextRendererTests`. `TokenCssParityTests` (shared fixtures). `BlockRendererCoverageTests`. `RendererSchemaVersionsTests`. `PublishedPageJavaScriptBudgetTests`. `PublicTextCatalogueTests`. |
| **Admin (vitest)** | `CanvasMarkupParity.spec.ts`. `tokenCss.parity.spec.ts`. `PublishMenu.spec.ts`, `HistoryView.spec.ts`, `ScheduleDialog.spec.ts`, `PublishLogView.spec.ts` and `DesignPublish.spec.ts`, with axe, keyboard paths and translation lookup. `addressWarning.spec.ts`. |
| **Integration** | `PublishApiTests`, `ComponentPublishApiTests`, `RollbackApiTests`, `ScheduleApiTests`, `PublishLogApiTests`, `DesignPublishApiTests`, `PublishingAuthorizationTests`. `PublicRenderingTests`: served HTML, 404, case and trailing slash, HEAD, 304, headers, disabled site. `CacheInvalidationTests`: SC-006's 50 pages and 10 users of a component, rollback, address change, link target unpublish, media deletion. `RenderingAcrossInstancesTests`: two `WebApplicationFactory` hosts on one database. `ScheduledPublishExactlyOnceTests`. `PublicPathReadOnlyTests`. `NotFoundParityTests` (missing page and admin path on a client domain are byte-identical). `PublishingIsolationProbe` (joins the isolation suite; FR-037). `UpgradeFrom011Tests`. `LoggingTests` extended. |
| **Architecture** | Rendering has no `DbContext` and references only Contracts. The Rendering and host paths never call write handlers. |
| **Smoke and performance** (reference compose, `docker-compose.perf.yml`) | The journey is extended (FR-043): create a header component and publish it, create a page referencing it and publish it, `GET` it from the site's host name and check the content, unpublish, and check the 404 page. `RenderPerformanceTests` (SC-003): a 200-block page with header and footer, 200 miss renders (each after a republish bumps the generation) and 1,000 hits, p95 under 200 ms and 20 ms, JavaScript bytes 0. `FreshnessTests` (SC-002): time from a component publish to new content on every page. **In CI** (FR-041; analysis I1): a new `smoke` job in `ci.yml`, after `image`, runs the whole smoke project against the compose files, so these budgets, and the earlier features' performance tests, fail the build. `QB_SMOKE_REQUIRE_DOCKER=1` turns a missing Docker into a failure instead of a skip. |

**Coverage**: `QuickBuilder.Rendering` joins the per-package 80% gate. `QuickBuilder.Pages`
and `QuickBuilder.Editor` are already in it.

---

## PR-22 Upgrade and migrations

**Decision**:
- **Pages**: one additive migration, `AddPublishing`, adding `publications`,
  `published_versions`, `live_dependencies`, `publish_schedules` and `publish_log`.
- **Editor**: `AddDesignPublishing`, adding `design_versions` and `design_publications`.
- **Media**: no schema change (the event uses the existing outbox table).
- **Data**: no backfill. Nothing is published before this release, so every site serves
  the not-found page until something is published.
- **Proof**: `UpgradeFrom011Tests` upgrades a 011 snapshot and checks that drafts and
  token sets are byte-identical, that no publication exists, and that a public request
  gets the 404 page.

---

## PR-23 Things deliberately not built

- **Warming the cache after a publish**: the first visitor after a publish pays one render,
  inside the 200 ms budget. Warming would do work for pages nobody requests.
- **Pre-compressed variants**: the proxy compresses.
- **Scheduling the design**: not in the spec. The clarify pass recorded it as low impact.
- **A distributed cache provider**: not needed for correctness (PR-05). It stays a future
  option, for sharing rendered bytes between instances to save CPU.

## Configuration keys

| Key | Default | Purpose |
|---|---|---|
| `Cache:MemoryLimitMegabytes` | `256` | Upper bound on the in-memory cache, which rendered pages dominate (PR-07). Validated 16–16,384 at startup. |

## Resolved unknowns

The Technical Context has no open questions after PR-01 … PR-23.
