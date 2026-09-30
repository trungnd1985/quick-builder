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
