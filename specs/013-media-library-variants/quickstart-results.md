# Quickstart results: Media library, variants and remote storage (#13)

## Status

The automated verification (T111) and the measurements below were run on 2026-10-05 on a
Windows 11 workstation with Docker Desktop 28.5.1. **The hand-run of quickstart.md §1–§9
(T110) is still to be done**: it needs a browser for the keyboard and translation pass (§7),
the Lighthouse layout-shift figure (SC-006), the usability timing (SC-002) and the browser
versions. Until then, the success criteria below are backed by the automated tests named.

## Automated verification (T111), 2026-10-05

| Check | Result |
|---|---|
| `dotnet format --verify-no-changes` | clean |
| Unit tests | 1,002 passed |
| Architecture tests | 29 passed |
| Integration tests | 767 run: 763 passed, 3 skipped (the skips predate 013), 1 failed and fixed (below) |
| Admin: `npm run lint`, `type-check`, `test`, `build` | clean; 462 tests passed |
| Line coverage, merged | 94.48% |
| `QuickBuilder.Media` | 93.09% (gate 80%) |
| `QuickBuilder.Pages` | 97.68% |
| `QuickBuilder.Rendering` | 87.49% |
| `QuickBuilder.Shared.Infrastructure` | 88.56% |

**Found and fixed during the run**: two folder moves that together form a cycle could both
commit. `FolderWrites` read the tree's row version at save time, after the other change had
committed, so the optimistic check never fired. It now reads the tree row with the folders,
before validation. `MediaFolderApiTests.A_change_validated_before_another_committed_is_refused_as_a_conflict`
reproduces the race deterministically: it fails on the old code and passes now.

**S3 test service (2026-10-06)**: MinIO no longer publishes container images, so a CI runner
could not pull it. The S3 tests and `docker-compose.storage.yml` now use LocalStack 4.9, chosen
by probing three services with the provider's own SDK settings:

| Service | Second create-only PUT | Second multipart completion | Pending uploads by prefix |
|---|---|---|---|
| LocalStack 4.9 | 412 | 412 | listed |
| MinIO RELEASE.2025-09-07 | 412 | 412 | not listed (exact name only) |
| S3Mock 4.9.1 | 412 | 304, not 412 | listed |

LocalStack does not check credentials, so the "wrong secret" start-up case answers as AWS does
from an in-process stand-in (403 `SignatureDoesNotMatch`). The second instance in
`docker-compose.storage.yml` also migrates automatically now: with `Database:AutoMigrate=false`
it never reported ready.

## Success criteria

| Criterion | Result | Evidence |
|---|---|---|
| SC-001 (≤ 1 s, 10,000 files) | Met: last page by name p95 8 ms, search p95 9 ms, any page ≤ 12 ms | `MediaPerformanceTests`, reference limits (2 CPUs, 4 GB) |
| SC-002 (≤ 30 s usability) | Not measured | T110 |
| SC-003 (≥ 80 % fewer bytes) | Met: a 4,000-pixel photo-like JPEG (9.1 MB) at 375 px 2× gets the 960 variant, 44.8 KB (99.5 % fewer); 1,280: 98.9 %; 1,920: 96.3 %. The original is never in `srcset`. | VariantEncoder settings on a generated image; `MediaRendererTests` |
| SC-004 (cached ≤ 100 ms p95; first ≤ 3 s) | Met: stored variant first byte p50 10 ms, p95 34 ms (50 at once, 1,000 requests); first request per width 259–1,232 ms (20 MiB JPEG) and 1,272–2,105 ms (8.2 MB AVIF) | `MediaPerformanceTests` (variants), reference limits |
| SC-005 (exactly once, two instances) | Met | `VariantProductionExactlyOnceTests` |
| SC-006 (no layout shift) | `width` and `height` are rendered on every image with dimensions; Lighthouse figure not measured | `MediaRendererTests`, golden files; T110 |
| SC-007 (in-use warning) | Met | `DeleteInUseTests`, `DeleteDialog.spec.ts` |
| SC-008 (usage matches content) | Met, in the same transaction as each change | `FileUsageApiTests`, `MediaUsageReaderTests`, `MediaReferenceBackfillTests` |
| SC-009 (no old bytes at the new address) | Met | `ReplaceFileApiTests`, `MediaAcrossInstancesTests` |
| SC-010 (conformance, two instances) | Met: 29 of 29 conformance cases on LocalStack and on Azurite; the media journeys across two instances on LocalStack | `RemoteFileStorageConformanceTests`, `MediaAcrossInstancesTests` |
| SC-011 (misconfiguration, no secrets) | Met: 10 S3 and 9 Azure cases, plus readiness failures | `S3StartupValidationTests`, `AzureStartupValidationTests`, `SecretRedactionTests` |
| SC-012 (upgrade) | Met | `UpgradeFrom012Tests` |
| SC-013 (keyboard, translation) | Covered for the picker, delete, replace and storage dialogs by their component tests; the full keyboard pass is T110 | `MediaControl.spec.ts`, `DeleteDialog.spec.ts`, `ReplaceDialog.spec.ts` |
| SC-014 (consistency check) | Met: exactly the files left out, none falsely; 0 after the full copy | `StorageCheckTests` |
| SC-015 (Editors and live files) | Met | `DeleteInUseTests`, `ReplaceFileApiTests` |

## Known miss carried over from 003

SC-008 of **003** (other admin requests within 2× of idle during ten 20 MiB uploads) is still
missed on this workstation: p95 went from 4 ms idle to 22 ms during the uploads. It was
recorded at 27–28 ms against 5–6 ms before 013, so 013 did not make it worse. The
performance job reports it without failing the build.
