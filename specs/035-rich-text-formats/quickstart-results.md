# Quickstart Results: 035 Richer Rich Text Formats

**Date**: 2026-10-09 | **Branch**: `035-rich-text-formats` | **Guide**: [quickstart.md](./quickstart.md)

What has been verified, and how:
- **Automated** means a named suite checks the section's expectations. The suites run against
  PostgreSQL (integration), in the admin's browser-free test environment (vitest with axe), or
  against the checked-in golden files (both sides).
- **By hand** items need a person in a browser, and are still open (T101).

| § | Section | Automated by | Status |
|---|---|---|---|
| 1 | Headings and quotes (US1) | `RichTextRendererTests`, golden `rich-text-headings-quotes`, `toContractRichText.spec.ts`, `RichTextToolbar.spec.ts`, `ItemField.spec.ts` | Passed |
| 1 | SC-002: format an article in under 5 minutes | — | **By hand, open** |
| 2 | Inline styles, undo, shortcuts (US2) | `RichTextToolbar.spec.ts`, `RichTextInput.spec.ts`, `EditorView.spec.ts`, golden `rich-text-inline-marks` | Passed |
| 3 | Images (US3, FR-014, FR-018) | `PublishRichTextImageTests`, `RichTextMediaUsageTests`, `contractImage.spec.ts`, golden `rich-text-image*`, item goldens | Passed |
| 4 | Paste (US4, FR-012, FR-026, SC-004) | `paste.spec.ts` (6 samples), `pasteUpload.spec.ts` | Passed. The samples are written to match each source's clipboard HTML; capturing real payloads is **by hand, open** |
| 5 | Tables (US5) | `tables.spec.ts`, `RichTextRendererTests`, golden `rich-text-table`, `rich-text-limits` | Passed. The screen reader check is **by hand, open** |
| 6 | Alignment (US6) | `RichTextRendererTests`, `RichTextToolbar.spec.ts`, golden `rich-text-align` | Passed |
| 7 | Limits and performance (SC-006) | `RichTextRulesTests`, `richTextRules.spec.ts`, `tables.spec.ts`, `RichTextRenderPerformanceTests`, `richTextPerformance.spec.ts` | Passed. Typing latency in a browser is **by hand, open** |
| 8 | Allowed formats per field (FR-022 to FR-025) | `ItemRichTextFormatsTests`, `FieldChangeClassificationTests`, `SchemaDiffTests`, `FieldEditor.spec.ts`, `RichTextInput.spec.ts`, `schemaChanges.spec.ts`, collection-schema goldens | Passed |
| 9 | Schema conversion (FR-019) | `FieldConversionTests` | Passed |
| 10 | Outdated admin tab (FR-020) | `AdminOutdatedTests`, `ConflictBanner.spec.ts`, `ItemFormView.spec.ts`, `requestHeaders.spec.ts` | Passed. A real tab across a deploy is **by hand, open** |
| 11 | Upgrade (SC-001) | `UpgradeFrom015Tests`; the existing golden pages are unchanged except for the stylesheet's hashed name | Passed |
| 12 | Accessibility (SC-003) | axe in every toolbar, image and table spec; `renderedRichTextA11y.spec.ts` over every published rich text page | Passed. The keyboard-only walk is **by hand, open** |

## Other checks

- **Hostile content** (SC-005): `RichTextRendererTests.Hostile_content_…` checks every tag and
  attribute against the allow-list; the invalid golden examples run on both sides (SC-007).
- **Logs** (FR-021): `LoggingTests.Rich_text_journeys_never_log_text_alt_text_or_file_names`.

## Full run (Release, 2026-10-09)

| Suite | Result |
|---|---|
| Unit | 1,466 passed |
| Architecture | 33 passed |
| Integration | 926 passed, 3 skipped (all three predate 035) |
| Smoke | 10 of 11 passed. `MediaPerformanceTests` fails on 003's SC-008 upload interference (p95 4 ms idle, 26 ms during uploads, limit 2×). This is the known failure tracked in issue #28: it has failed on this workstation since 003, CI does not run smoke tests, and 035 changes no media or upload code |
| Admin | 801 passed; lint, type check and build clean |
| Formatting | `dotnet format --verify-no-changes` clean |

## Coverage

Overall line coverage is 94.9%. Each package in CI's gate must reach 80%:

| Package | Line coverage |
|---|---|
| QuickBuilder.Pages | 97.7% |
| QuickBuilder.Cms | 96.2% |
| QuickBuilder.Sites | 95.7% |
| QuickBuilder.Editor | 95.2% |
| QuickBuilder.Pages.Contracts (not gated) | 95.2% |
| QuickBuilder.Media | 93.1% |
| QuickBuilder.Host | 93.1% |
| QuickBuilder.Shared.Kernel | 89.2% |
| QuickBuilder.Rendering | 88.6% |
| QuickBuilder.Shared.Infrastructure | 88.6% |

## Performance

| Measure | Target | Result |
|---|---|---|
| A page with the limit rich text renders (50 images, 20 tables, one 200 × 20, 20,000 characters) | 5 ms | about 4 ms |
| Normalising the limit rich text in the admin (every change) | 30 ms | about 2 ms |

Both were measured on the development machine.
