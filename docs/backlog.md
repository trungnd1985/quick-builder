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

- **Exception E-6, for #16**: schema changes rewrite published item data at once, and since
  015 also the versions kept for rollback and scheduled content. Before items are rendered,
  decide how a schema change reaches live pages (invalidate on `Cms.CollectionChanged`, or
  require republishing the affected pages)
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
