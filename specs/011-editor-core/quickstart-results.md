# Quickstart Results: Editor Core

**Feature**: `011-editor-core` | **Date**: 2026-09-28 | **Guide**: [quickstart.md](./quickstart.md)

**Status: automated checks done; the by-hand browser run (T119) is still to do.** The
implementation session had no browser to drive, so every step below that needs one is listed
as *pending* rather than claimed.

## Automated results

Counts updated on 2026-09-29 after the width convergence (T121–T128). The smoke test,
the performance test, coverage and the build sizes are from 2026-09-28 and were not rerun:
that change touched no server endpoint, query or image build step.

| Check | Result |
|---|---|
| .NET unit tests | 690 passed |
| .NET architecture tests | 25 passed |
| .NET integration tests (Testcontainers PostgreSQL) | 461 passed, 3 skipped (Unix file-mode tests, skipped on Windows as before this feature) |
| Admin tests (vitest, jsdom, axe) | 364 passed across 29 files |
| `dotnet format --verify-no-changes` | clean |
| `eslint --max-warnings 0`, `vue-tsc -b` | clean |
| Editor module line coverage (integration tests alone) | 92.1% (gate: 80%) |
| Release smoke test on the built image (`ReferenceDeploymentSmokeTests`) | passed, including step 9d: the editor route is served, a Section with a Heading saves and reads back, and a new site reads the default tokens |
| `EditorPerformanceTests` on the reference compose setup | passed (below) |

### Performance (reference compose setup, from the test process)

| Measure | p95 | Budget |
|---|---|---|
| Open a 500-block page: draft, tokens and components fetched together (SC-005, server side) | 6 ms | 1 s |
| Count the blocks using a token over 2,000 page drafts, 21,769 matching blocks (ED-16) | 52 ms | 1 s |

Seeding the 2,000-page site with drafts took 55 s.

### Admin build

| Chunk | Size | gzip |
|---|---|---|
| `index` (the admin shell) | 274.9 kB | 89.4 kB |
| `EditorView` (lazy; the only chunk containing TipTap/ProseMirror) | 395.7 kB | 119.3 kB |
| `DesignTokensView` (lazy) | 11.7 kB | 4.1 kB |

The shell's main chunk grew by about 11 kB (the sign-out clearing of editor browser state);
TipTap stays out of it, as research ED-03 required.

### Where each quickstart section is covered automatically

| Quickstart § | Covered by |
|---|---|
| §1 Build a page by dragging | `EditorView.spec.ts` (palette drop, refused drop, keyboard insert of every type, typing, autosave, stored draft validates) |
| §2 Edit properties | `PropertyPanel.spec.ts`, `toContractRichText.spec.ts`, `PageDocumentValidatorTests` |
| §3 Undo, autosave, conflicts, recovery | `ops.property.spec.ts` (8 seeds, 50–200 changes), `autosave.spec.ts`, `recovery.spec.ts`, `EditorView.spec.ts` (conflict → copy → Reload latest → paste; recovery offer) |
| §4 Layer view | `LayerView.spec.ts` |
| §5 Preview widths | `EditorView.spec.ts` (widths, per-member memory, storage failure) |
| §6 Design tokens | `DesignTokensView.spec.ts`, `designTokens.spec.ts`, `DesignTokensApiTests`, `EditorAuthorizationTests`, `TokenUsageApiTests` |
| §7 Shared components | `EditorView.spec.ts` (component editor, reference shown read-only, palette insert at page level only) |
| §8 Performance | `EditorPerformanceTests` (server side, above); the render-path claim by `EditorView.spec.ts` (only the edited path re-renders) |
| §9 Keyboard, translation, accessibility | `EditorView.spec.ts`, `LayerView.spec.ts`, `PropertyPanel.spec.ts`, `DesignTokensView.spec.ts` (axe); vue-i18n lint |
| §10 Contracts, upgrade, smoke | `PageSchemaContractTests`, `DesignTokenContractTests`, `UpgradeFrom010Tests`, smoke test |
| SC-008 No third-party requests | `NoThirdPartyRequests.spec.ts` (every block, every font stack) |

## Pending: the by-hand browser run (T119)

To be run on the reference compose setup in current Chrome or Edge and in Firefox, and
recorded here with browser versions:

- [ ] **SC-001**: time to build the §1 page by hand (budget 10 minutes).
- [ ] **SC-004**: saves complete within 3 s of the last change for 95% of saves (§3).
- [ ] **SC-005 (browser)**: select, change a property and drop on `page-500.json` each paint
  in under 100 ms; the editor opens in under 3 s (§8). The `performance.measure` marks the
  guide mentions were not added; use the Performance panel's interaction timings instead.
- [ ] Real drag and drop with a pointer, including the drop indicator (jsdom has no layout,
  so the tests drive the drop logic, not the pointer geometry).
- [ ] Rich text in a real browser: typing, the toolbar, pasting a web page (§2).
- [ ] Preview scaling when the editor area is narrower than the chosen width (§5).
- [ ] The Network panel shows no request outside the installation (§2, §6).
- [ ] Offline, then back online; and a closed tab offered back on reopening (§3).
