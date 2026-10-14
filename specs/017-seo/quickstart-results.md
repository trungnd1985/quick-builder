# Quickstart Results: SEO, Sitemap and Redirects

**Task**: T114
**Date**: 2026-10-14 (automated part); the manual run is pending
**Guide**: [quickstart.md](./quickstart.md)

The manual run on the reference compose setup has not been done yet. It needs a person: the
stopwatch timings, the keyboard-only pass with NVDA, and the schema.org validator. The rows
below marked **Pending** are for that run. Everything the guide checks by request and response
is also covered by the automated tests listed here, which pass.

## Success criteria

| Criterion | Target | Result | Status |
|---|---|---|---|
| SC-001: set a page's metadata and see the head | under 2 minutes, by hand | | Pending |
| SC-002: import 300 redirects with 3 bad lines | under 1 minute, by hand | `RedirectImportTests`: 297 added, 3 refused with codes | Automated pass; timing pending |
| SC-003: every address change reaches the new content in one hop | 100 % | `RedirectOfferTests` (12), `ItemRedirectOfferTests` (6) | Pass |
| SC-004: 10,000 redirects, redirect p95 under 20 ms, page budgets kept | reference setup | `SeoPerformanceTests` | Pending (needs the perf compose run) |
| SC-005: sitemap shows a change within 5 s on every instance | 5 s | `SitemapFreshnessTests` (second instance included); item redirect measured at under 1 s | Pass |
| SC-006: 100,000 item pages, each file under 2 s on a miss, 50 ms on a hit | reference setup | `SeoPerformanceTests` | Pending (needs the perf compose run) |
| SC-007: the head and structured data validate | schema.org validator, by hand | `HeadMetadataGoldenTests`, `StructuredDataGoldenTests`, `StructuredDataRenderTests` | Automated pass; validator pending |
| SC-008: every new control works from the keyboard, with a screen reader | all, with NVDA | axe passes on every new panel and dialog (`SeoPanel`, `SeoTemplate`, `StructuredDataControl`, `RedirectOffer`, `SearchSettingsPanel`, `SearchVisibilityPanel`, redirects specs) | Automated pass; NVDA pending |

## Quickstart sections and their automated cover

| Section | Tests |
|---|---|
| 1. Page metadata | `PageMetadataRenderTests`, `SeoDraftPermissionTests`, `SeoPanel.spec.ts` |
| 2. Redirects | `RedirectEndpointsTests`, `RedirectMatchingTests`, `RedirectImportTests`, `RedirectUsageTests`, `RedirectAuditTests`, `redirects.spec.ts` |
| 3. Sitemap and robots.txt | `SitemapTests`, `SitemapFreshnessTests`, `RobotsTests`, `SearchVisibilityPanel.spec.ts` |
| 4. Metadata templates | `SeoTemplateRenderTests`, `BindingRulesGoldenTests` (`seo-*`), `SeoTemplate.spec.ts` |
| 5. Redirect offers | `RedirectOfferTests`, `ItemRedirectOfferTests`, `OfferedRedirectsTests`, `RedirectOffer.spec.ts` |
| 6. Canonical addresses and host names | `AddressNormalisationTests`, `HostRedirectTests`, `CanonicalAddressTests` |
| 7. Structured data | `StructuredDataRenderTests`, `StructuredDataGoldenTests`, `JsonLdWriterTests`, `StructuredDataTypesContractTests`, `structuredDataTypes.spec.ts` |
| 8. Performance | `SeoPerformanceTests` (pending run) |
| 9. Isolation, keyboard, logs and upgrade | `SiteIsolationSuite` with `SeoIsolationProbe`, `SeoContentNotLoggedTests`, `UpgradeFrom016Tests` |

## Deviations found while implementing

- **Turning a template back into a page offers no redirect for its items.** A prefix redirect
  keeps the rest of the path, so "old prefix → parent page" would send `/blog/x` back to
  `/blog/x`. Recorded in data-model §3.4 and the backlog.
- **Pages under an unpublished parent are listed in the sitemap**: they stay live (012), which
  corrected the spec's edge case.
