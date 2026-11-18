# Research: Draft Preview Links

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #22

Each decision has an id (PV-nn) that the plan, data model, contracts and tasks refer to.
The Technical Context had no open unknowns: the stack is fixed by the constitution. This
file records the design choices the spec leaves to planning.

---

## PV-01 Which module owns preview links

**Decision**: The **Editor** module stores preview links in a new table,
`editor.preview_links`, and serves the admin endpoints that create, list and revoke them.
**Rendering** renders previews. It resolves a link through a new Editor contract,
`IPreviewLinks`, and owns no table.

**Rationale**:
- Editor already handles documents of all three kinds (page, component, item) for editing
  sessions (023 CE-01). It has `EditingDocumentKinds`, `EditingAccess` (who may view or edit
  each kind) and its own schema and outbox. A preview link is the same kind of
  "about a document, for its authors" record.
- Rendering has no `DbContext`, and the public path reads through read-only connections
  (012 PR-06). Giving Rendering a writable context for links would weaken that boundary.
- One table answers the site-wide list (FR-015) with one query.

**Alternatives considered**:
- *A table in Pages and another in Cms*. Rejected: list, revoke, the 20-link limit, the
  site-wide list and the failure counter would all be built twice.
- *A new `Preview` module*. Rejected: the constitution names a fixed set of modules, and
  this need fits Editor (XI).
- *A table owned by Rendering*. Rejected for the reason above.

---

## PV-02 Reading drafts and versions

**Decision**: Two new read-only contracts, implemented by the modules that own the data:

- `Pages.Contracts.Publishing.IPreviewContentReader` returns:
  - a page's draft (document, title, path, save time and author, pending schedule);
  - a retained page version (number, document, title, publish time and author);
  - draft documents of components and menus that have **never** been published;
  - a template page's draft, by collection.
- `Cms.Contracts.Publishing.IPreviewItemReader` returns:
  - an item's draft values as a `PublishedItem` shape (so bindings read it unchanged), with
    save time and author, needs-attention state and pending schedule;
  - a retained item version, in the same shape.

Each module gains a new `PreviewReadDbContext`, built like the published contexts:
- on `ReadOnlyConnection.From`;
- never migrated, and `SaveChanges` throws;
- with the site filter and the 021 `Trash` named filter.

It maps the draft and version tables. The published contexts are not changed and stay
"published data only", so even inside Pages and Cms the public readers cannot reach a draft
(changed after analysis, K1).

The design's draft comes from the existing `IDesignTokenReader`.

**Rationale**:
- The public readers (`IPublishedContentReader`, `IPublishedItemReader`) stay unchanged.
  Principle VII's "the public path reads only published data" stays true by construction,
  because drafts are reachable only through interfaces the public responder never takes
  (an architecture test checks this, PV-21).
- Read-only contexts keep previews unable to write content.
- Trashed pages and items are hidden by the named filter, so "a link stops working while
  its target is in the trash and works again after a restore" (spec edge case) needs no
  code.

**Alternatives considered**:
- *Adding draft methods to the published readers*. Rejected: the public responder would
  then hold an interface that can read drafts.
- *Calling the admin `GetPageDraft` query handlers across modules*. Rejected: handlers are
  internal, and cross-module reads go through contracts (Principle I).

---

## PV-03 What a preview is composed of (clarification Q2)

**Decision**: A pure composition rule in Rendering, `PreviewComposition`, decides each part:

| Part | Shown from |
|---|---|
| The target page | its draft, or the chosen retained version |
| The target item (item preview) | its draft values, or the chosen retained version |
| Template of an item preview | its live version; its draft if it has never been published |
| Sample item of a template page preview | the chosen item's published values; its draft if it has none |
| Shared components | live version; draft if never published |
| Menus | live version; draft if never published |
| Design | live design; the saved draft tokens if the design has never been published (live number 0 and a saved token set) |
| Lists, references, link targets, menu entry targets | published data only, through the existing readers |

Every part shown from a draft is named in the preview bar (FR-020).

**Rationale**: This is exactly clarification Q2, and keeping it in one pure function lets a
unit test check each row.

---

## PV-04 One render path for published pages and previews

**Decision**: Extract the body of `PageResponder.RenderVersionAsync` into a `PageComposer`
that gathers `RenderInputs` from a **content source**:

- `LiveContentSource` wraps today's readers, and the public responder uses it unchanged.
- `PreviewContentSource` applies PV-03.

`PageResponder.Render(inputs, renderer)` stays the single place that turns inputs into
HTML. `RenderInputs` gains one optional member, `Preview` (`PreviewContext`): the bar model
and the address rules of PV-06. When it is null, nothing changes.

**Rationale**:
- "Identical to what publishing the same content would serve" (FR-002, SC-001) holds only
  if both use the same composer, block renderers, encoding and head builder.
- The extraction does not change behaviour. The existing golden-output and
  performance-budget tests are the regression check, and they must pass before any preview
  code lands.

**Golden check (SC-001)**: `PreviewParityTests` renders each golden page and item page
twice, published and as a preview of an identical draft. It then compares the two after
removing the bar element and its style rules, and after mapping preview addresses (PV-06)
back to public ones. They must be byte-identical.

---

## PV-05 Where previews are served

**Decision**: There is a new platform prefix, **`/_preview`**, under the installation's base
path:

- It answers on **every** host name, the admin host names included, like `/health`.
- It never resolves the site from the `Host` header. The site comes from the link or the
  route, and the handler sets `ISiteContext` from it, as `AdminHostConfinementMiddleware`
  already does for its not-found answer.

| Route | Who | What |
|---|---|---|
| `GET /_preview/sites/{siteId}/pages/{pageId}[?version=N][&item={id}]` | signed-in member | page draft, version, or template page with a sample item |
| `GET /_preview/sites/{siteId}/items/{itemId}[?version=N]` | signed-in member | item draft or version |
| `GET /_preview/sites/{siteId}/media/{file}` | signed-in member | media for member previews |
| `GET /_preview/l/{token}` | anyone with the link | the link's target |
| `GET /_preview/l/{token}/media/{file}` | anyone with the link | media the target uses (PV-11) |
| `GET /_preview/assets/{file}` | anyone | the block stylesheet (site-independent) |
| `POST /_preview/form` | anyone | the "previews do not send forms" page (PV-12) |

**Rules**:
- `SiteResolutionMiddleware` skips `/_preview`, as it skips the admin prefixes.
- `/_preview` is **not** added to `RoutePrefixes.IsReserved`. That list decides whether
  `PublicPathRedactor` keeps the request path in logs, and preview paths carry the link
  secret, so they must stay redacted (FR-032). A test asserts both.
- Slugs cannot start with `_` (010 grammar), so no page can claim `/_preview`.
  `PublicPathConstraint` gains the segment anyway, as defence in depth.
- `AdminHostConfinementMiddleware` only confines `/api` and `/admin`, so a link opened on a
  site host name works.
- Member previews need the session cookie, which is host-only, `SameSite=Strict` and
  scoped to the base path. The admin therefore opens member previews on its own origin.
  Opened anywhere else, the request has no session and gets the not-found answer (FR-004).

**Link address**: the full link handed to the member is built at creation as follows:
- `site.BaseUrl + /_preview/l/{token}` when the site has a host name;
- otherwise the admin origin the request came from, after forwarded headers.

Either works because `/_preview` answers on every host (spec edge case "Site without a host
name").

**Alternatives considered**:
- *Serve previews only on the site's host names*. Rejected: sites without a host name could
  not preview, and member previews could not read the admin's host-only session cookie.
- *Serve previews under `/api` or `/admin`*. Rejected: admin security headers and the admin
  CSP would apply. Admin host confinement would also hide links from reviewers whenever
  `Hosting:AdminHostNames` is set.

---

## PV-06 Addresses written inside a preview

**Decision**: `PreviewContext` carries an address policy that `LinkResolver`, the media
renderers, `PageShell` and the pager consult:

- **Links to pages and items** (FR-012): the public, absolute address `site.BaseUrl + path`
  when the target is live and the site has a host name. When the target is not live, no
  `href` is written, as today. When the site has no host name, internal links are written
  without `href`, so a preview never sends a viewer to a draft or to a wrong host.
- **External links**: unchanged.
- **Media**: `/_preview/l/{token}/media/{file}` for link previews and
  `/_preview/sites/{siteId}/media/{file}` for member previews, instead of `/_media/{file}`.
  `srcset` variants are rewritten the same way.
- **Stylesheet**: `/_preview/assets/{file}`.
- **Pager links of lists** stay inside the preview: the preview's own address plus the
  page parameters.
- **Head metadata**: the canonical and `og:url` addresses stay the public address the draft
  will have. They are only read by crawlers, which are told not to index (PV-10).

---

## PV-07 Link secrets

**Decision**:
- The secret is 32 random bytes from `RandomNumberGenerator`, base64url-encoded
  (43 characters).
- The URL holds **only** the secret: no id, site, kind or title (FR-007).
- The database stores its SHA-256 in a unique column, as Identity does for sessions and
  set-password links (002 R-04).
- The secret is returned once, in the create response (FR-014).
- Lookups are by hash, with one indexed read.

**Why not a signed, self-contained token, as the issue suggests**:
- Revocation, last-opened, the creator check and the 20-link limit all need a stored row
  anyway.
- A random secret looked up by hash cannot be forged, and a database read yields no usable
  link.
- Signing would add key management (rotation, multi-instance key sharing) for nothing.

The spec's requirement, "impossible to guess or forge", is met. The issue's word "signed"
describes the goal, not a mechanism.

**Emergency switch (FR-019)**: `Preview:LinksValidFrom`, an optional UTC timestamp. A link
created before it is treated as revoked. The operator sets it to "now" and restarts, and
every existing link stops working. Nothing else changes, and new links work again. It is
validated at startup (ISO 8601, not in the future by more than one day).

---

## PV-08 Checking a link on every request

**Decision**: `IPreviewLinks.ResolveAsync(secret)` runs on every link request. Nothing is
cached, so revocation and expiry take effect at the next request on every instance
(FR-017, SC-006). It makes four checks:

1. **One indexed read by hash.** The row must exist, not be revoked, not have reached
   `expires_at_utc`, and have `created_at_utc >= Preview:LinksValidFrom`.
2. **The creator still holds the target kind's edit capability on the site**, through the
   new `Identity.Contracts.IMemberAccess.HoldsCapabilityAsync(userId, capability, siteId)`.
   It reads grants and user status per call, never cached, and a deactivated user holds
   nothing. This makes clarification 3 ("at once when the creator loses access") exact
   without events.
3. **The target can still be read** through PV-02. A trashed or deleted page or item is
   absent, and a version past retention has no content.
4. **Last opened**: `last_opened_at_utc` is updated with a conditional `UPDATE` only when it
   is more than 60 seconds old, so a reviewer reloading costs at most one write per minute.

Any failure gives the **same** "no longer available" page (FR-010, PV-10).

**Rationale**:
- Previews are low-traffic, so the extra reads are cheap.
- Event-driven revocation (on grant change, deactivation, trash, purge) would need new
  Identity events and would always lag. Checking at read time is simpler and exact.

---

## PV-09 Who may do what

**Decision**: Existing capabilities, plus one new one.

| Action | Capability |
|---|---|
| Member preview of a page, component-using page or template | `pages.content.view` (every site role) |
| Member preview of an item | `cms.collections.view` (every site role) |
| Member preview of a version | same as the draft preview of that kind |
| Create a link; list or revoke a target's links | the kind's edit capability (`EditingAccess.EditCapabilityOf`): `pages.page_drafts.edit` for pages, `cms.items.edit` for items. Administrator, Designer and Editor hold it (clarification 1). |
| List and revoke every link of the site | **new** `editor.preview_links.manage`, site-scoped, granted to no site role, so Administrator only (FR-015) |

**How it is enforced**:
- Admin endpoints declare their capability (`RequireCapability`). The kind-specific check
  happens in the handler, as 023's editing endpoints do.
- Member preview routes are `[AllowAnonymous]` and check access in the handler with
  `IAccessEvaluator`, so every refusal, anonymous included, is the generic 404 (FR-004).
  The admin authorization filter would answer anonymous requests with 401.
- Another site's ids get the same 404 as a missing page (FR-004).

---

## PV-10 Response rules for every preview

**Decision**: These headers are set by one `PreviewResponse` helper, which an architecture
test requires every `/_preview` action to use:

| Header | Value | Requirement |
|---|---|---|
| `Cache-Control` | `no-store, private` | FR-024 |
| `X-Robots-Tag` | `noindex, nofollow` | FR-024 |
| `Referrer-Policy` | `no-referrer` | FR-025 |
| `Content-Security-Policy` | as published pages (012 PR-12), with `form-action 'self'` when the page has a form (PV-12) | |
| `X-Content-Type-Options`, `Permissions-Policy` | as published pages | |
| `ETag` | none; preview requests never get a `304` | |

**In the document**:
- The head gets `<meta name="robots" content="noindex, nofollow">`, replacing the page's own
  robots setting.
- `<meta name="referrer" content="no-referrer">` is added as well.

**The "no longer available" page** carries the same headers and a `404` status. It is one
static document with no site name, title, design or language taken from the link, so every
failure is byte-identical (FR-010). It uses the
existing `PublicTexts` resource, picked by `Accept-Language`, with English as the fallback.

**Sitemap and robots.txt**: they read only published data and never list `/_preview`
(FR-025). `robots.txt` also gains `Disallow: /_preview/` for crawlers that respect it. The
response headers stay the real guarantee.

---

## PV-11 Media in link previews (FR-027)

**Decision**: `GET /_preview/l/{token}/media/{file}`:

1. Resolves the link (PV-08).
2. Checks that the file id is in the target's **referenced media set**: the files the
   composition (PV-03) references through documents, components, menus, bindings, lists and
   head metadata.
3. Streams the file through a new Media contract, `IMediaContent.OpenAsync(address)`. It
   runs Media's existing public-serve logic for the site set in `ISiteContext`, with the
   same parsing, version check and trash filter.

**Caching the referenced set**:
- Computing the set reads the same inputs as a render, without writing HTML.
- To avoid recomputing it for every image of one page, the set is kept for 5 minutes in
  `ICacheProvider` under `preview-media:{siteId}:{linkId}:{stamp}`. The stamp is the draft's
  save time or the version number, so a new save invalidates it.
- The entry holds ids only, never output. It is a separate namespace from render keys
  (`RenderKey`, `ItemRenderKey`), which preview code must not use (FR-023, PV-21).

**Member previews** serve any file of the site, because members can already browse the
whole library (003, 013).

**Alternatives considered**:
- *Point preview images at `/_media` on the site host*. Rejected: it breaks for sites
  without a host name and on the admin host.
- *Signed per-image URLs*. Rejected: keys and expiry for no benefit over the link itself.

---

## PV-12 Forms in previews (FR-026; 018 backlog note)

**Decision**: In a preview, `FormMarkup`:
- writes `action="{base}/_preview/form"` and `method="post"`;
- leaves out the form script (018 FS-10), which would `fetch` `/_forms`.

`POST /_preview/form` ignores the body and answers a small page (PV-10 headers): "This is a
preview. Forms are not sent from previews." The Forms module is never called, so nothing is
stored, counted or notified.

`FormSubmitIsolationTests` posts to the preview endpoint and to a preview page's form, then
checks that `forms.submissions`, the rate windows and the outbox are unchanged.

---

## PV-13 The preview bar (FR-020, FR-021)

**Decision**: When `RenderInputs.Preview` is set, `PageShell` writes a
`<div class="qb-preview" role="region" aria-label="Preview">` as the first child of
`<body>`, outside the page's root element.

**Styles**:
- The bar's rules go into the page's single `<style>` element, so the CSP hash still covers
  exactly one element.
- Every class is prefixed `qb-preview-`, and the colours and font are fixed, independent of
  design tokens.
- The bar sits in normal flow above the page. It does not overlay content and does not
  change any rule inside the page, so the layout at each preview width is unchanged
  (FR-021).

**Hiding it without a script**: a visually hidden checkbox and a `<label>` "Hide bar" use
`#qb-preview-hide:checked ~ .qb-preview-bar { display: none }`. This needs no script and no
CSP change, works with scripts off, and the label is keyboard-operable.

**Content**:
- "Preview — not live", then what is shown:
  - "Draft saved {time} by {name}", or
  - "Version {n}, published {time} by {name}".
- For a template page preview, the sample item.
- Parts shown from a draft (PV-03).
- A pending scheduled publish.
- A needs-attention warning for items (PV-15).
- For link viewers, "This link expires {time}".
- For members, links to the editor or item form and to "Share preview".

**Times**: written in UTC with an explicit "UTC" suffix. A script-free page cannot know the
viewer's time zone.

**Texts**: a new `PreviewTexts` resource in English. Translating it, like the 39-language
`PublicTexts`, is recorded in the backlog.

**Names**: names come from `IUserDirectory` and are encoded like all content.

---

## PV-14 Admin experience

**Decision**:

**Preview button**:
- **Where**: the editor top bar (`EditorTopBar.vue`) and the item publish bar
  (`ItemPublishBar.vue`).
- **Before opening**: it flushes pending autosave. If the save is refused (023 conflict,
  validation), the tab is not opened and the member is told why. FR-003 is met by "save
  first", with the message only on failure.
- **Opening**: `window.open` on the member preview address, on the admin origin.

**Share preview** (`SharePreviewDialog.vue`):
- **Where**: next to Preview, and per version in `HistoryView.vue` and
  `ItemHistoryPanel.vue`.
- **Creating**: label and expiry (1 hour, 1 day, 7 days by default, 30 days, or a custom
  date up to 30 days ahead). The full link is shown once with a copy button.
- **Listing**: active links with label, creator, created, expires and last opened, each
  with Revoke, and Revoke all.
- **Limit**: at 20 active links, creating is refused with "Revoke a link first".

**Site-wide list** (`PreviewLinksView.vue`, under site settings, Administrators only):
- every active link with its target (page title or item name, kind, draft or version),
  creator and expiry;
- Revoke and Revoke all.

**Page tree and item list**: a "Preview" entry in each row menu.

---

## PV-15 Content that fails validation (FR-030)

**Decision**:
- **Page drafts** are run through the existing `IPageDocumentValidator` before composition.
  On failure:
  - a member gets a `422` problem page listing each reason and location;
  - a link viewer gets "This preview is not available right now" (`503`, `Retry-After: 60`,
    PV-10 headers). It works again once the draft is fixed.
- **Items that "need attention"** (015: a required value missing or a constraint unmet)
  are still rendered. Missing values already render as broken bindings, the renderer's
  normal handling of absent data, and the bar warns "This item needs attention: some
  values may not show". Malformed values cannot be saved (015 FR-013), so they never
  reach a preview.

**Why items render**: refusing to preview an item because one field is empty would block
the review that is meant to find such gaps. A page draft that breaks the schema cannot be
rendered faithfully at all.

**Spec alignment**: this narrows FR-030 for items. It is recorded in the plan's spec
alignment list, for `/speckit-analyze`.

---

## PV-16 History entries (FR-016)

**Decision**: Editor writes two integration events to its outbox in the same transaction as
the link change:
- `Editor.PreviewLinkCreated(SiteId, DocumentKind, DocumentId, LinkId, VersionNumber?, ExpiresAtUtc, By)`;
- `Editor.PreviewLinkRevoked(SiteId, DocumentKind, DocumentId, LinkId, By)`.

**Handlers**:
- **Pages** appends a `PublishLogEntry` with action `preview_link_created` or
  `preview_link_revoked` for pages. The page's history view and the Administrator publish
  log show it.
- **Cms** appends an `ItemHistoryEntry` of kind `previewLinkCreated` or
  `previewLinkRevoked`.
- Both are idempotent: the entry id is derived from the event id (UUID v5), so a redelivery
  inserts nothing.

**What is recorded**:
- Only explicit actions: create, revoke and revoke all, which writes one event per link.
- Expiry, creator access loss and the operator switch end links automatically and are not
  history.
- Entries carry ids, the version number and the expiry, never the secret or the label. The
  label is free text a member typed and could name the reviewer.

**Migrations**: Pages adds the two actions to the publish log's action check constraint,
and Cms adds the two kinds to the item history kind check.

---

## PV-17 Limit of 20 active links per target (FR-018)

**Decision**: Creating a link runs in one Editor transaction:
1. It takes `pg_advisory_xact_lock(hashtext('preview:' || site || ':' || kind || ':' || id))`
   through parameterised `SqlQuery`.
2. It counts active links: not revoked, not expired, created after `LinksValidFrom`.
3. It inserts the link.

At 20 active links the request is refused with `409 preview.link_limit`.

**Rationale**: Without the lock, two concurrent creates could both see 19. An advisory lock
is scoped to one target and released at commit, with no extra table.

---

## PV-18 Slowing down guessing (FR-013)

**Decision**: Failed link lookups (unknown hash only; expired or revoked links are not
probes) are counted per client address group (`ClientAddress.Group`, IPv6 /64) in
`editor.preview_link_failures`, in minute and hour windows. This is the pattern of 018's
`RateWindows`, in Editor's own table.

**Limits**: over **20 failures a minute** or **200 an hour**, link requests from that group
get `429` with `Retry-After`, valid links included, until the window ends.

**Storage**: the address is stored only as a SHA-256 of the group and swept hourly. Nothing
is logged.

**Rationale**: A 256-bit secret cannot be found by guessing, so this mostly stops wasted
load and noise. The limits are well above what a reviewer mistyping a link would reach.

---

## PV-19 Cleanup

**Decision**: An hourly hosted service in Editor, `PreviewLinkSweepService`:
- deletes links that expired or were revoked more than 30 days ago;
- deletes failure windows older than one hour.

It uses one conditional `DELETE` per batch, so several instances running it is harmless.
Because the maximum lifetime is 30 days, no link row is older than 60 days.

Links whose target was permanently deleted simply stop resolving (PV-08) and are swept once
expired.

---

## PV-20 Site export and import (spec edge case)

**Decision**:
- Preview links are **not** part of the archive (020): the format is unchanged.
- When an import replaces or merges into a site, Editor's existing `DesignArchivePart` also
  deletes that site's preview links in the import transaction. A link to an id that comes
  back through import can never come alive again.

---

## PV-21 Guard rails (architecture and isolation tests)

- **Public path**: `RenderPageController`, `PageResponder`, `LiveContentSource`, the sitemap
  and robots must not depend on `IPreviewContentReader`, `IPreviewItemReader` or
  `IDesignTokenReader`.
- **Preview code**: `Features/Preview/**` must not reference `RenderKey`, `ItemRenderKey`
  or the render cache helpers. Its only `ICacheProvider` use is the `preview-media:`
  namespace (FR-023).
- **Headers**: every action under `/_preview` returns through `PreviewResponse` (PV-10).
- **Published readers**: `PublishedContentReader`, `PublishedItemReader` and the sitemap
  readers do not depend on either `PreviewReadDbContext` (PV-02).
- **Logging**: `/_preview` is not in `RoutePrefixes.IsReserved`, so the redactor removes its
  path, and a log-capture test checks it.
- **Authorization**: `AuthorizationDeclarationTests` cover the new admin endpoints, and the
  isolation suite probes them with another site's ids.

---

## PV-22 Performance (SC-003)

**Budget**: a preview is a cache-miss render (012's 200 ms p95 budget) plus at most four
indexed reads:
- the link;
- the creator's grants;
- the draft or version;
- `last_opened`, at most once a minute.

**Test**: `PreviewBudgetTests` in the existing performance suite asserts **p95 under 500 ms**
on the reference setup for a 50-block page with lists. That is well within the spec's
1-second goal, and leaves room for slow CI hosts.

**Image check**: the referenced-media set (PV-11) is computed at most once per draft save
per instance in 5 minutes.
