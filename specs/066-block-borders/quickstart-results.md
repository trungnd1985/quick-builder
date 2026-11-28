# Quickstart Results: Border Styles for All Blocks

**Feature**: 066 | **Quickstart**: [quickstart.md](./quickstart.md) | **Run**: 2026-11-28

What has been shown to work, and what still needs a person in a browser (tasks T049). Automated
results are from this branch; "by hand" items are open until someone walks them.

## Automated

| Quickstart | Shown by | Result |
|---|---|---|
| §1 Contract, parity and golden tests | `PageSchemaContractTests`, `DesignTokenContractTests`, `TokenCssParityTests`, `tokenCss.parity.spec.ts`, `RendererGoldenTests`, `CanvasMarkupParity.spec.ts` | Pass |
| §1 Existing pages unchanged (SC-004) | Script over the 46 regenerated golden files: each differs from `main` only by the five default `--qb-radius-*` properties and the stylesheet name (`blocks.-_crjtLYQnEW.css` → the new hash) | Pass |
| §2 Border group, order, keyboard, publish (US1, SC-002) | `PropertyPanel.spec.ts`, `BorderControl.spec.ts`, `BlockRegistryCoverage.spec.ts`, `tests/e2e/specs/block-borders.spec.ts` (keyboard only, canvas and visitor computed styles) | Pass |
| §2 Undo, copy, duplicate (FR-011) | `PropertyPanel.spec.ts` (one operation), `clipboard.spec.ts` (copy, paste, duplicate) | Pass |
| §3.1–3.6 Text, Button, Image, Divider, Component reference, list card | `borders-every-block`, `borders-component-ref`, `borders-radius` golden files; `CollectionListRenderTests.A_bordered_card_frames_every_listed_item`; `CollectionListBlock.spec.ts` | Pass |
| §4 Corners and clipping (US2) | `borders-radius` golden file and parity; clipping rule in `blocks.css` | Pass (markup); visual check by hand below |
| §5 Corner radius tokens (US3, SC-005) | `DesignTokensView.spec.ts`, `DesignTokensApiTests` (save, 403 for an Editor, every invalid example), `TokenUsageApiTests`, `DesignPublishApiTests` (draft until published), e2e design step | Pass |
| §6 Invalid values (FR-019) | `DraftApiTests` (every invalid example over HTTP), both validators on the contract examples | Pass |
| §7.1–7.2 Export, import, older archive (US4) | `RestoreTests.Borders_and_corner_radii_come_back_unchanged_from_an_archive`, `ArchiveFromPreviousReleaseTests` (0.2 archive) | Pass |
| SC-006 Accessibility | axe in `BorderControl.spec.ts`, `PropertyPanel.spec.ts` and the e2e scans of the editor (Border group open) and Design screens | Pass |
| SC-007 Performance | `BorderRenderPerformanceTests`: 1.84 ms plain, 2.06 ms with 67 borders, per 200-block render | Pass |

## By hand (open)

- [ ] §2.2 SC-001 timing: frame a block in under 30 seconds.
- [ ] §3.7 A bordered, rounded Navigation block's drop-down opens fully outside the bar.
- [ ] §4.1 A rounded card visibly clips a full-width image at its corners; §4.2 a Full radius on
  a square Spacer draws a circle.
- [ ] §7.3 An admin tab left open across the upgrade refuses a bordered page rather than losing
  the border.
- [ ] §8 A screen reader (NVDA or VoiceOver) reads each Border control's label and value.

## Found along the way

- Clicking **Save design** and then **Publish design** at once can report "Someone else changed
  the design tokens": publishing saves first, and that second save races the first. This was
  already the case before this feature. The e2e page object publishes only, which saves on its
  own. Worth its own issue.
