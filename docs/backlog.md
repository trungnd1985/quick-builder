- Localisation of content (decide the model early: site-per-language vs
  locale-per-site; it affects the page and item schemas)
- Version history for collection items
- Site search on published sites
- Per-site custom CSS/JS and HTML embed blocks, with an explicit security spec
- Extensibility: custom block types and custom field types for developers
  (shapes how blocks are registered in #6, so decide the direction early)
- Content Delivery API (headless read API)
- Webhooks on publish and other events
- Seed/demo content for a fresh installation
- Security hardening pass: CSP for published sites, security headers, review
- Accessibility for the admin UI and for generated HTML
- Admin UI localisation (Vietnamese and English)
- Housekeeping jobs: prune old versions, orphaned media, old logs
- Observability: OpenTelemetry traces and metrics, dashboards
- Dependency updates (Renovate or Dependabot) and code scanning

Deferred by 012 (publishing and rendering; see specs/012-publishing-rendering/plan.md):

- Collection-driven pages, and page → collection dependencies (#16)
- SEO metadata, sitemaps, canonical addresses and redirects (#17); it can subscribe to
  the `Pages.PagePublished` and `Pages.PageUnpublished` events
- CDN integrations and external cache purge
- Draft previews through the renderer (#22)
- A custom not-found page and other error pages
- Bulk publish, approval workflows, side-by-side version comparison, scheduling the design
- Cache warming, pre-compressed responses, a shared cache provider, click-to-load embeds
- Native review of the not-found page's texts in 39 languages
  (specs/012-publishing-rendering/quickstart-results.md)

Deferred by 013 (media library, variants and remote storage; see specs/013-media-library-variants/plan.md):

- Format conversion of JPEG and PNG originals to WebP or AVIF with `Accept` negotiation;
  cropping and focal points
- A built-in provider-to-provider copy (today: a documented copy and the consistency
  check), CDN integrations, signed or expiring URLs
- Trigram search for libraries beyond 10,000 files per site
- Separate storage-provider projects, keeping the cloud SDKs out of
  Shared.Infrastructure's dependency graph (needs a constitution amendment)
- File renaming, tags, quotas, a recycle bin, moving files between sites
- `sizes` arithmetic per column inside multi-column containers
- Using the latest consistency check instead of per-file `HEAD` requests for availability
  in listings on remote storage, if those listings prove slow

Deferred by 014 (collections and the schema builder; see specs/014-cms-collections-schema/plan.md):

- ~~**Exception E-6, for #16**~~: closed by 016. A schema change that rewrites values
  refreshes every live page that lists or binds the collection, item pages included, through
  `Cms.CollectionChanged` (016 research TB-10)
- Unique field values (a "must be unique" setting on text and number fields)
- Full schema snapshots per revision, and restoring a schema or a removed field
- A background migration engine for collections far beyond 10,000 items
- A per-site time zone for date conversions (today: UTC)

Deferred by 015 (collection items; see specs/015-cms-items/plan.md):

- Searching the content of text and rich text fields in the item list (clarification Q5;
  today: name and slug)
- Bulk actions in the item list (publish, unpublish or delete many at once)
- Keyset paging and per-field indexes for collections far beyond 10,000 items
- Autosave and local recovery for the item form

Deferred by 035 (richer rich text; see specs/035-rich-text-formats/plan.md):

- An alternative-text check at publish for the Image block, as rich text images have
- Code blocks; captions, links, sizes and floats on rich text images; table captions, column
  widths and cell alignment; justified text
- Right-to-left support beyond storing alignment as start and end
- Word desktop lists, which arrive as paragraphs because Word sends them as styled paragraphs

## Deferred by 016 (collection templates and data binding)

- Visitor filtering, sorting and search on lists; filters relative to the current time
- Redirects from old item addresses, and SEO metadata templates (#17)
- More than one reference step in a binding; binding style properties; components that read
  the page's item
- One `LATERAL` query per nested list, per-field expression indexes for sort keys, and
  narrowing list refreshes by filter
- Draft preview links for item pages (#22)

## Deferred by 024 (navigation and menus)

- Menus built automatically from the page tree or a collection
- Mega menus, icons, per-entry styles, per-role menus, localised menus
- Anchor names on blocks, and checking that a menu's anchors exist
- Integration events for menu changes (no consumer yet)
- Breadcrumb blocks (017 has the structured data)
- Obligations on later features:
  - **#20 site export and import**: carry, per menu, its id, name, draft, live version number,
    every retained version (document, number, kind, published by and at) and pending
    schedules. On import, remap menu documents' `pageId`, `itemId` and `collectionId`, and
    Navigation blocks' `menuId` in page and component drafts and versions; rebuild
    `menu_targets` and `menu_usages` from the imported documents rather than importing them

## Deferred by 018 (forms and submissions)

- Fields as child blocks with free layout; conditional and calculated fields; multi-step forms
- File uploads; payment fields; confirmation emails to visitors
- CAPTCHA and third-party spam services; webhooks and CRM integrations
- Per-site SMTP servers and sender addresses
- Trigram search for very large submission stores
- Replying from the admin; assigning or tagging submissions
- Obligations on later features:
  - **#20 site export and import**: forms travel inside page and component documents; keep
    node ids (form and field ids), and remap `redirect.pageId` and `consent.linkPageId` as
    other page links are. Submissions, the form registry, spam counters and the submission
    log are **not** exported (018 FR-040)
  - **Site deletion**: sites cannot be deleted yet. The feature that adds it must delete the
    site's rows in every `forms` table
  - **#22 draft previews**: a preview must render forms disabled, as the editor canvas does

## Deferred by 017 (SEO, sitemap and redirects)

- Wildcard or regular-expression redirects; a not-found log; redirect use over time
- Image, news and video sitemaps; per-locale sitemaps and `hreflang`; pinging search engines
- Structured data types beyond Article, Product and Event (additive); `uploadDate` and
  thumbnails for videos other than YouTube
- Per-item metadata fields; `twitter:*` tags
- A previous-slug fallback for the few seconds before an item's redirect exists
- A redirect for the item addresses of a template turned back into a page: a prefix redirect
  keeps the rest of the path, so it would need a "drop the rest" option
- Obligations on later features:
  - **#20 site export and import**: carry `pages.redirects` (remapping `page` and `item`
    target ids) and the site search settings; page metadata already travels in documents
  - **#22 draft previews**: preview responses must write `noindex` and never appear in the
    sitemap

## Deferred by 019 (release pipeline and upgrade safety)

- Image signing (cosign), SBOMs and provenance attestations on release images
- A second registry (Docker Hub) beside GHCR
- A bulk rewrite of stored documents to a new schema version, needed only to retire old
  upgrade steps (019 clarification Q4)
- Row checksums for the release content journey's tables (the upgrade gate counts rows, so a
  deleted row offset by an added one is not caught)
- Waiting for every background rewrite in the upgrade gate, not only the Cms index backfill
- Zero-downtime rolling upgrades and automatic downgrade (out of scope by the spec)
- Driving the editor in a browser during the release gates (exception E-5, until #25)
- Obligations on later features:
  - a feature that stores a new kind of data adds a step to
    `tests/QuickBuilder.SmokeTests/Release/ContentJourney.cs`;
  - a breaking content schema change follows "Raising a schema version" in
    docs/contributing.md (an upgrade step with golden files, never in a patch);
  - every pull request with operator-visible change adds a release-note fragment.
