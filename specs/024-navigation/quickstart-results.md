# Quickstart results: Navigation and menus (024)

**Status**: automated part done 2026-10-20; manual run done 2026-10-21 (tasks T080), except the
screen-reader check, left open under exception E-5.

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

## Done by hand

| Check | Date | Result |
|---|---|---|
| **SC-001**: build the 15-entry menu of §1 and place it (target: under 5 minutes) | 2026-10-21 | **Pass.** Built and placed with 15 entries; it ran normally and was very quick, well under the target by the tester's report. No exact stopwatch time was recorded. |
| **§10 menu styles in a real browser**: each look, hover and focus by pointer and keyboard, visited entries, contrast warnings | 2026-10-21 | **Pass**, by the tester's report. |
| **§2 and §5 in a real browser** (Chrome or Edge device mode): phone and tablet widths, keyboard only, JavaScript off, current page highlighted and its submenu open | 2026-10-21 | **Pass**, all five steps, by the tester's report. |
| **§8 with NVDA (Firefox) and VoiceOver (Safari)** | 2026-10-21 | **Not run**: the tester does not use these browsers or screen readers. Left open under exception E-5 (manual screen-reader checks until #25); the automated axe checks and keyboard specs in §8 pass. |

## Still to do by hand

1. **§8 with NVDA and VoiceOver**: not run (see above); needs someone with those browsers
   and screen readers, or #25's automated replacement.

Record each result here with the date, the browser and the reader versions, and anything
that failed with its fix.
