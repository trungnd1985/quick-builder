# Quickstart results: Concurrent editing safety (023)

**Status**: automated part done 2026-10-31; the manual run (task T072) was skipped at the
maintainer's decision on 2026-11-01.

## Automated evidence

| Quickstart section | Proven by | Result |
|---|---|---|
| §1 No overwrite (SC-001) | `ITESTS/Concurrency/OverlappingDraftWritesTests.cs`, `DraftConflictDetailsTests.cs`, `ITESTS/Cms/Items/ItemPublishDoesNotConflictTests.cs`, `ADMINTESTS/editing/ConflictDialog.spec.ts` | pass |
| §2 Presence and the lock (SC-002, SC-003) | `ITESTS/Editor/EditingSessionEndpointsTests.cs`, `LockRaceTests.cs`, `ADMINTESTS/editing/PresenceBanner.spec.ts`, `useEditingSession.spec.ts` | pass |
| §3 Stale locks, takeover, release and expiry (SC-004) | `UTESTS/Editor/EditingRulesTests.cs`, `EditingSessionEndpointsTests.cs`, `ITESTS/Editor/EditingCleanupTests.cs` | pass |
| §4 Save a copy | `ITESTS/Pages/CopyEndpointsTests.cs`, `ITESTS/Cms/Items/CopyItemTests.cs` | pass |
| §5 Recovery | `ADMINTESTS/editor/autosave.spec.ts` | pass |
| §6 Multiple instances (FR-025) | `ITESTS/Concurrency/DraftFencingTests.cs`, `LockRaceTests.cs` (state is in the database only) | pass |
| §7 Accessibility, automated part | axe in the `ADMINTESTS/editing/` specs | pass |
| §8 Upgrade | `ITESTS/Editor/UpgradeFrom019Tests.cs` | pass |

## Not run by hand

Skipped at the maintainer's decision: the two-browser stopwatch timings (SC-003, SC-004),
§6 with two app containers, §7 with a screen reader (exception E-5), the §9 open-time
comparison against `main` (SC-007) and the §10 usability check with five team members
(SC-005). These success criteria are not measured.
