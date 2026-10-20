# Quickstart results: Navigation and menus (024)

**Status**: automated part done 2026-10-20; the manual run on the reference compose setup is
**still to do** (tasks T080).

This records which parts of [quickstart.md](./quickstart.md) the automated suites already
prove, and what a person still has to check by hand. The by-hand parts are the ones no test
can stand in for: timing a Designer (SC-001) and listening with screen readers (SC-006,
exception E-5).

## Automated evidence

| Quickstart section | Proven by | Result |
|---|---|---|
| §1 Build a menu | `ITESTS/Pages/MenuEndpointsTests.cs`, `ADMINTESTS/menus/MenuEditorView.spec.ts`, `MenusView.spec.ts` | pass |
| §2 Place it and publish | `ITESTS/Pages/MenuPublishingTests.cs`, `ITESTS/Rendering/NavigationRenderTests.cs`, `ADMINTESTS/editor/NavigationBlock.spec.ts`, release smoke journey step 9e2 | pass |
| §3 Reorder | `ADMINTESTS/menus/menuOps.spec.ts`, `MenuEditorView.spec.ts` (drag, Alt+arrows, undo) | pass |
| §4 Link integrity (SC-003) | `ITESTS/Rendering/MenuLinkIntegrityTests.cs`, `MenuInvalidationTests.cs`, `MenuReferencesTests.cs` | pass |
| §5 Active state (SC-004) | `UTESTS/Rendering/Navigation/ActiveStateTests.cs`, `NavigationGoldenTests.cs`, `ADMINTESTS/rendering/navScript.spec.ts` | pass |
| §6 Refresh and isolation (SC-002, FR-035) | `MenuInvalidationTests.cs`, `NavigationRenderTests` (two instances), `MenuIsolationProbe`, `MenuPublishingTests.A_navigation_block_naming_another_sites_menu_never_goes_live` | pass |
| §7 Performance (SC-005) | `STESTS/MenuRenderPerformanceTests.cs` on the reference compose setup | pass: miss p50 20.2 ms, **p95 66.1 ms** (budget 200 ms); hit p50 5.3 ms, **p95 11.0 ms** (budget 20 ms); page 114,203 bytes |
| §8 Accessibility (SC-006), automated part | axe in `NavigationBlock.spec.ts`, `MenuEditorView.spec.ts`; keyboard behaviour in `navScript.spec.ts` | pass |
| §9 Upgrade | `ITESTS/Rendering/UpgradeFrom017Tests.cs` | pass: same bodies, same policy, no stored document changed |
| §10 Menu styles (SC-008) | `ITESTS/Rendering/MenuStyleRenderTests.cs`, `NavigationGoldenTests` (looks, overrides), `ADMINTESTS/editor/navStyleCss.spec.ts`, `ADMINTESTS/editor/contrast.spec.ts`, `ADMINTESTS/design/MenuStyleSection.spec.ts`, the `menu-style.json` token-CSS parity fixture | pass |

## Still to do by hand

1. **SC-001**: a Designer builds the 15-entry, three-level menu of §1 and places it, timed with
   a stopwatch (target: under 5 minutes).
2. **§8 with NVDA (Firefox, Windows) and VoiceOver (Safari, macOS)**: on the published menu,
   the `nav` landmark and its name, each disclosure's state, `aria-current="page"`, the
   new-tab hint and the collapsed panel's button; in the menu editor, the Alt+arrow moves and
   their announcements.
3. **§10 in a real browser**: each look in the header and in a collapsed panel, hover and focus by
   pointer and keyboard, visited entries unchanged, and the contrast warnings in Design and in
   the block's panel.
4. **§2 and §5 in a real browser at phone, tablet and desktop widths**, by keyboard and by
   touch, with scripts on and off: every entry can be reached and opened (SC-006), the panel
   collapses at the chosen width, and the current path opens as FR-019a says.

Record each result here with the date, the browser and the reader versions, and anything
that failed with its fix.
