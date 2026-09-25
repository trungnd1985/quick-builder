# Quickstart & Validation Guide

**Feature**: `003-media-storage` | **Date**: 2026-09-25

This guide explains how to check that the media library works end to end and how to
verify each success criterion. It covers validation only. The table is defined in
[data-model.md](./data-model.md), endpoints in
[contracts/media-api.yaml](./contracts/media-api.yaml), and the provider rules in
[contracts/storage-provider.md](./contracts/storage-provider.md). Design decisions are
numbered `M-xx` in [research.md](./research.md).

---

## Prerequisites

- **Tools**: the same as for 002: a container runtime, plus the .NET 10 SDK and Node 22+
  for developers.
- **Starting point**: an installation that has completed setup (002 quickstart §1), with
  one site that has the host name `site-a.test` and one with `site-b.test`.
- **Host names**: the `curl` examples pass the `Host` header explicitly, so no DNS or
  hosts-file changes are needed.

In the examples, `$COOKIE` is a session cookie of a signed-in user and `$A` is site A's
id. Every state-changing call needs `-H "X-QuickBuilder-Request: 1"` (002 R-11).

---

## 1. Upload and view with no configuration (SC-001, US1, US4 #1)

```bash
docker compose down -v && docker compose up -d
docker compose exec app sh -c 'echo $Storage__Local__Path'   # /var/lib/quickbuilder/media
docker volume ls | grep media-data                            # the named volume exists
```

1. In the admin shell, open **Sites → site A → Media**. The upload area shows the privacy
   notice: files are public at once, and embedded details such as location are published
   (FR-027).
2. Drop a JPEG. It shows progress, then appears first in the list with a preview, its
   name, type, size, time and your name.
3. Choose **Copy address** and open the address in a new tab. The image loads from
   `site-a.test`.

**Timing**: from signing in to seeing the image at its public address should take under
1 minute.

**The same through the API**:

```bash
curl -i -b "$COOKIE" -H "X-QuickBuilder-Request: 1" \
     -H "Content-Type: image/jpeg" -H "X-QuickBuilder-File-Name: team%20photo.jpg" \
     --data-binary @team-photo.jpg \
     http://127.0.0.1:8080/api/v1/media/sites/$A/files              # 201, body has publicPath
curl -I -H "Host: site-a.test" http://127.0.0.1:8080/_media/<id>.jpg   # 200, headers per §4
```

**Automated equivalent**: the smoke test uploads an image to a site and fetches it from
the site's public address (FR-032).

---

## 2. Validation refuses what it should (SC-002, US1 #2–#5)

| Try | Expect |
|---|---|
| `echo '<script>alert(1)</script>' > x.jpg`, uploaded as `image/jpeg` | `415 media.unsupported_type` |
| A real PNG named `logo.jpg` | `400 media.type_mismatch` |
| A real PNG declared as `image/gif` | `400 media.type_mismatch` |
| A real PNG declared as `application/octet-stream` and named `logo.png` | `201`: undeclared counts as neutral (M-08) |
| Any SVG | `415 media.unsupported_type` |
| An empty file | `400 media.empty` |
| A 21 MiB file, and the same file sent chunked (`-H "Transfer-Encoding: chunked"`) | `413 media.too_large` both times. The server stops reading at the limit. |
| Five files dropped at once, one of them invalid | Four appear in the library. One row shows its reason, and the screen reader announces each result. |

After each refusal, the library is unchanged and nothing new is under
`/var/lib/quickbuilder/media/<site>/media/` (`docker compose exec app ls …`).

**Automated checks**: `UploadValidationTests` runs the generated set of at least 50
crafted files (M-20). Each case asserts the exact status and `errorCode`, and that no
row and no stored object was created. `FileTypeDetectorTests` unit-tests each signature,
including truncated and near-miss headers.

---

## 3. Site isolation (SC-003, US2 #2–#3, US1 #7)

```bash
curl -i -H "Host: site-b.test" http://127.0.0.1:8080/_media/<site-A id>.jpg    # 404 media.not_found
curl -i -H "Host: site-a.test" http://127.0.0.1:8080/_media/<random id>.jpg    # 404, same body apart from traceId
curl -i -H "Host: site-a.test" http://127.0.0.1:8080/_media/<site-A id>.png    # wrong extension: 404, same body
curl -i -H "Host: unknown.test" http://127.0.0.1:8080/_media/<site-A id>.jpg   # 404 sites.not_resolved (001)
curl -i -H "Host: site-a.test" "http://127.0.0.1:8080/_media/..%2F..%2Fetc%2Fpasswd"  # 404
```

As an Editor on site A only, `GET /api/v1/media/sites/<B>/files` returns `404
sites.not_found`, identical to a site that doesn't exist.

**Automated checks**: `MediaSiteIsolationTests` runs every cross combination of host and
file for two sites and compares the bodies byte for byte, ignoring `traceId`. The
cross-site sweep read has its own `AcrossAllSites` test (001 FR-013a).

---

## 4. Files are served inert (SC-004, US2 #4–#5)

```bash
curl -sI -H "Host: site-a.test" http://127.0.0.1:8080/_media/<jpeg id>.jpg
```

The response must include:
- `Content-Type: image/jpeg`;
- `X-Content-Type-Options: nosniff`;
- `Content-Security-Policy: default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox`;
- `Content-Disposition: inline; filename*=UTF-8''…`;
- `Cache-Control: public, max-age=31536000, immutable`;
- a strong `ETag` and `Accept-Ranges: bytes`.

A PDF has the same headers except the CSP.

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Host: site-a.test" -H "Range: bytes=0-99" .../_media/<pdf id>.pdf   # 206
curl -s -o /dev/null -w "%{http_code}\n" -H "Host: site-a.test" -H 'If-None-Match: "<id>"' .../_media/<id>.jpg   # 304
```

**Manual browser check**: repeat in current Chromium and Firefox, because this checks
real browser behaviour.
1. Upload a GIF that is also valid JavaScript and HTML (a GIF/JS polyglot), and a PDF
   that contains an `app.alert` and a script reading `document.cookie`.
2. Open each public address directly. The GIF displays as an image or as broken, and never
   runs script. The PDF opens in the browser's viewer. Neither can read the site's
   cookies: check the DevTools console of the site's origin for errors and for `alert`
   calls.

**Automated checks**: `MediaServingHeadersTests` asserts the header set for every
accepted file in the crafted-file set, on both the public address and the preview (M-12).

---

## 5. Browse, copy and delete (SC-007, SC-009a, US3)

1. Upload 60 files. The list shows 50 and a **Load more** button. Pressing it with the
   keyboard adds 10, focus moves to the first new row, and no file is duplicated or
   missing. Upload another file mid-way and page again: it doesn't disturb the order.
2. As an Editor, the **Delete** button appears only on your own uploads. Calling the
   delete endpoint on another user's file returns `403 identity.forbidden` (US3 #5).
3. As a Designer, delete any file and confirm. It leaves the list, and its public address
   answers `404` on the very next request.
4. Delete a file's bytes by hand with `docker compose exec app rm …`. The library marks
   it as unavailable, and its address answers `404`.

**Automated checks**:
- `DeleteFileTests` covers the capability matrix and the two-instance "next
  request" check, using 002's two-host fixture.
- `ListFilesTests` covers keyset paging under concurrent inserts.
- `MediaPerformanceTests` (smoke, Performance category) seeds 10,000 rows and loads the first, a middle
  and the last page within 1 s each (SC-009a).

---

## 6. Operator: configuration, persistence and upgrades (SC-005, SC-010, US4)

```bash
docker compose up -d --build app            # replace the image and keep the volumes
# every file listed before is still listed and serves identical bytes (compare sha256sum)

docker compose run --rm -e Storage__Provider=S3 app                            # fails: names Storage:Provider and lists "Local"
docker compose run --rm -e Storage__Local__Path=relative/path app              # fails: must be absolute
docker compose run --rm -e Storage__Local__Path=/app/media app                 # fails: inside the application directory
docker compose run --rm -e Storage__Local__Path=/proc/forbidden app            # fails: cannot be created or written
docker compose run --rm -e Media__AllowedTypes__0=image/svg+xml app            # fails: not an allowed type
```

Each failure exits before listening and prints one message naming the key.

**Automated checks**:
- `StorageStartupValidationTests` covers every M-05 case.
- `UpgradeFrom002Tests` restores a 002 snapshot and migrates it. Sites, accounts and
  grants are unchanged, and every library is empty.
- The smoke test runs on a rebuilt image with the named volume in place (SC-005).

---

## 7. Interrupted uploads and clean-up (SC-006, FR-011, FR-012)

1. Start a 20 MiB upload and kill it partway: stop `curl` or run
   `docker compose restart app`. Nothing new appears in the library. A `.part` file
   may exist under `/var/lib/quickbuilder/media/<site>/.incoming/`, and it is gone within an hour of the next start.
2. **Automated**: `MediaOrphanSweepTests` injects failures:
   - storage throws mid-write;
   - the database insert fails after the write;
   - the purge after deletion fails.

   It then advances the `TimeProvider` past `Media:OrphanGracePeriod` and runs one
   sweep. The test asserts:
   - zero partial files were ever listed or served;
   - every leftover object is gone;
   - no object that has a row was touched;
   - an object younger than the grace period survives.

---

## 8. Storage conformance (SC-011, FR-024)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~Storage"
```

`LocalFileStorageConformanceTests` must pass every case in
[contracts/storage-provider.md §5](./contracts/storage-provider.md#5-conformance-suite).

---

## 9. Performance (SC-008, SC-009, SC-009a)

```bash
docker compose -f docker-compose.yml -f docker-compose.perf.yml up -d --build   # 2 CPU / 4 GB (from 002)
dotnet test tests/QuickBuilder.SmokeTests --filter "Category=Performance"
```

| Test | Pass when |
|---|---|
| `MediaUploadPerformanceTests` | A 10 MB upload completes in ≤ 5 s. With 10 uploads of 20 MiB in flight, the p95 of `GET /api/v1/sites` is ≤ 2× its idle p95. |
| `MediaServingPerformanceTests` | With 50 concurrent GETs of a 1 MB image, the p95 time to first byte is ≤ 100 ms |
| `MediaLibraryPagingTests` | With 10,000 files, the first, a middle and the last page each load in ≤ 1 s |

---

## 10. Logs, privacy and offline operation (SC-012, SC-013, FR-030, FR-031)

- **Log canaries**: every test upload uses a name containing a canary marker
  (`zz-canary-<guid>`). 002's log scanner fails the run if any marker appears in log
  output.
- **Offline**: the egress check from 002 (FR-035) runs the upload, serve and sweep steps
  and must record zero outbound connections.
- **Keyboard only**: complete upload, load more, copy address and delete without a
  pointer. `features/media/*.spec.ts` runs axe and keyboard checks, and the i18n
  coverage test finds no hard-coded strings.
