# Quickstart & Validation Guide

**Feature**: `013-media-library-variants` | **Date**: 2026-10-01

How to check, end to end, that the media library, image variants and remote storage
providers work, and how to verify each success criterion. This guide covers validation
only:
- tables, rules and error codes are in [data-model.md](./data-model.md);
- endpoints are in [contracts/media-api.yaml](./contracts/media-api.yaml);
- addresses and image markup are in [contracts/public-media.md](./contracts/public-media.md);
- providers and settings are in [contracts/storage-providers.md](./contracts/storage-providers.md);
- readers and events are in [contracts/readers.md](./contracts/readers.md);
- decisions are numbered `MV-xx` in [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation and sites**: as in 012's quickstart: site `$S` with host name
  `site-a.test`, site `$S2` with `site-b.test`, and cookies `$COOKIE` (Administrator),
  `$DESIGNER` and `$EDITOR`, the Editor granted on `$S` only.
- **Content** on `$S`:
  - a published page **About** with an Image block;
  - a published component **Header** with an Image (the logo);
  - a draft-only page **Draft** with an Image.
- **Fixtures** (`tests/fixtures/media/`):
  - `photo-6000.jpg`: 6,000 × 4,000, EXIF orientation 6, GPS tags;
  - `logo.png` (with alpha) and `logo-v2.png`;
  - `anim.gif`, `anim.webp`;
  - `huge-pixels.png`: 12,000 × 12,000, small file;
  - `brochure-été.pdf`;
  - `photo.avif`.
- **Shorthands**: `$API=http://127.0.0.1:8080/api/v1`. `H` stands for
  `-H "X-QuickBuilder-Request: 1"`. `PUB(host, path)` stands for
  `curl -si --resolve $host:8080:127.0.0.1 http://$host:8080$path`.
- **Remote storage for §6**: `docker compose -f docker-compose.yml -f
  docker-compose.storage.yml up -d minio azurite`. This is a new dev-only compose overlay
  that creates the bucket `qb-media` and the container `qb-media`.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration, provider suites (Docker)
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

---

## 1. Folders, search and alternative text (US1, SC-001, SC-002)

1. As `$EDITOR`, open **Media** on `$S`. Create **Team photos**, then **2026** inside it,
   then rename **2026** to **2026 Q4**.
2. Upload 12 photos into **Team photos**, choosing the folder before uploading. Select all
   12 and move them to **2026 Q4**. Open one photo's address before and after the move.
3. Try to create a second **team PHOTOS** at the top level, and try to delete
   **Team photos**.
4. Upload `brochure-été.pdf`. Search `ETE`, then `broch`, then `%`.
5. Open a photo and set its alternative text to "Anna Nguyen at her desk". Search `anna`.
6. Run `MediaPerformanceTests` (smoke project, Performance category). It seeds 10,000
   rows, as 003's does, and now also times the last page sorted by name and a search.

**Expected**:
- **Moving**: the address is the same before and after (FR-003).
- **Name clash**: the duplicate is refused as `media.folder_name_taken`. Deleting
  **Team photos** is refused as `media.folder_not_empty`.
- **Search**: `ETE` and `broch` find the PDF; `%` finds nothing. `anna` finds the photo
  wherever it is filed.
- **Speed**: every page and search at 10,000 files loads in under 1 s (SC-001). Record
  times from the browser's network panel.
- **Usability**: a colleague finds a named file among 1,000 and places it on a page in
  under 30 s (SC-002). Record 3 attempts.

---

## 2. Correctly sized images (US2, SC-003, SC-004, SC-005, SC-006)

1. As `$DESIGNER`, put `photo-6000.jpg` in **About**'s Image block (full width) and publish.
2. Run `PUB(site-a.test, /about)` and read the `<img>`.
3. Open **About** in Chrome device mode at 375 px and 2× density, then at 768 px, then at
   1,280 px. For each, note the image request and the transferred bytes.
4. Request `PUB(site-a.test, /_media/{id}-v1-w700.jpg)` (a width that is not configured)
   and `…-v1-w2560.jpg` on `site-b.test`.
5. Download `…-w640.jpg` and inspect it with `exiftool`.
6. Run `VariantProductionExactlyOnceTests` and `VariantPerformanceTests`.

**Expected**:
- **Markup**: the `<img>` has `width="4000" height="6000"` (orientation applied),
  `srcset` with widths 320–2,560 and no original, and `sizes` from the Section's content
  width (public-media.md §2).
- **Bytes**: at 375 px and 2× density, a variant of about 750–960 px is fetched, and the
  bytes are at least 80 % below the original. No width fetches the original (SC-003).
- **Refusals**: both requests in step 4 give `404 media.not_found` (FR-024).
- **Variant file**: upright, with no EXIF, GPS, XMP or ICC data (FR-023).
- **Speed and exactly-once**:
  - a cached variant begins arriving in under 100 ms at p95 with 50 concurrent requests;
  - a first-time variant of the 20 MiB fixture is ready within 3 s (SC-004);
  - 100 concurrent first requests over two instances produce it exactly once (SC-005).
- **Layout**: Lighthouse reports a CLS contribution of 0 from images on **About**
  (SC-006).
- **Animated files**: `anim.gif` and `anim.webp` on a page render with the original and no
  `srcset`.
- **Pixel limit**: uploading `huge-pixels.png` is refused as `media.dimensions_too_large`.

---

## 3. The picker (US4)

1. In the editor, add an Image block to **Draft** and open the picker. Go into
   **Team photos → 2026 Q4**, choose the photo with alternative text, and check the
   block's alternative text. Edit the block's text, then reopen the photo in the library.
2. Close the editor, reopen it, and open the picker again.
3. From the picker, upload `logo.png` into the current folder and choose it.
4. Repeat step 1 with the keyboard only, with a screen reader on.

**Expected**:
- **Alternative text**: the block's text is prefilled from the library, and editing it
  does not change the library (FR-009).
- **Last folder**: the picker reopens in **2026 Q4** (FR-010).
- **Upload**: the file appears in that folder and can be chosen immediately.
- **Accessibility**: every action is reachable and announced (SC-013).

---

## 4. Usage and delete (US5, SC-007, SC-008, SC-015)

1. Use one file in **Draft** (draft only), **About** (published) and **Header**
   (published). Open the file in the library.
2. As `$EDITOR` (who uploaded it), choose **Delete**.
3. As `$DESIGNER`, choose **Delete**, cancel at the warning, then try the same through the
   API: `curl -X DELETE $H -b $DESIGNER $API/media/sites/$S/files/$F`.
4. Remove the image from **Draft** and save. Within 5 s, reopen the usage list.
5. Delete a file that nothing uses.

**Expected**:
- **Usage list**: **Draft** (draft), **About** (live) and **Header** (live, "on N pages"),
  each linked (FR-014).
- **Editor**: refused with the Designer or Administrator message, and the API gives
  `403 media.in_live_use` (SC-015).
- **Designer**: the warning names the three places. The API without `confirmInUse` gives
  `409 media.in_use` with the summary.
- **After the draft change**: usage shows two places (SC-008).
- **Unused file**: only the ordinary confirmation (SC-007).
- **Confirmed delete of the live file**: **About** renders without the image at the next
  request.

---

## 5. Replace (US6, SC-009)

1. Note the address `A1` of the **Header** logo. As `$DESIGNER`, replace it with
   `logo-v2.png`.
2. Run `PUB(site-a.test, /about)` and `PUB(site-a.test, A1)`. Run the same on a second
   instance (§6).
3. As `$EDITOR`, try to replace the same file, then a file of their own that no live page
   uses.
4. Try to replace an image with `brochure-été.pdf`.
5. Interrupt a replacement mid-upload (with `curl --limit-rate` and Ctrl-C).

**Expected**:
- **After the replacement**: the file keeps its id, name, folder, alternative text,
  uploader and usage, shows *Replaced by … at …*, and has the address `…-v2.png`
  (FR-017, FR-018).
- **Addresses**: **About** shows the new logo at the next request on both instances, and
  `A1` gives `404` (SC-009).
- **Editor**: the live-used file is refused as `media.in_live_use`; their own unused file
  is replaced.
- **Kind**: the PDF is refused as `media.kind_mismatch`.
- **Interrupted**: the file is unchanged and still served (FR-020).

---

## 6. Remote providers and multiple instances (US3, SC-010, SC-011, SC-014)

1. **S3 (MinIO)**: set `Storage__Provider=S3`, `Storage__S3__ServiceUrl=http://minio:9000`,
   `Storage__S3__Region=us-east-1`, `Storage__S3__Bucket=qb-media`,
   `Storage__S3__ForcePathStyle=true` and the keys. Start two app instances
   (`docker-compose.storage.yml` defines `app2` next to `app`, sharing the database).
2. Upload through instance 1 (`app`, port 8080), then list, preview, publish and view
   through instance 2 (`app2`, port 8081).
3. Misconfigurations, one at a time:
   - a wrong secret;
   - bucket `nope`;
   - no `Region`;
   - `Storage__Provider=Dropbox`.
   A service without conditional writes is covered by `S3StartupValidationTests` only
   (it uses an in-process proxy that drops the header).
4. **Azure (Azurite)**: `Storage__Provider=AzureBlob`, the Azurite connection string and
   `Container=qb-media`. Repeat step 2 and a missing container.
5. **Moving providers**:
   - with the Local provider, upload 20 files;
   - `rclone copy --exclude ".incoming/**" --exclude ".probe-*" <media-dir> minio:qb-media`,
     deliberately leaving out one site's folder;
   - switch to S3 and start;
   - open **Settings → Storage**; run `dotnet QuickBuilder.Host.dll media check-storage`;
   - copy the rest and run the check again.
6. Stop MinIO while the app runs. Call `/health/ready` and try an upload. Start MinIO again.

**Expected**:
- **Two instances**: everything works through both. Conformance passes 100 % for S3 and
  Azure (SC-010).
- **Misconfigurations**: every one stops start-up with a message naming the key, and no
  message or log line contains the secret (SC-011, `grep` the logs for the secret).
- **Consistency check**: the first check (in the background, after readiness) lists
  exactly the missing site's files; the CLI exits `3`. After the full copy it reports 0
  missing, exits `0`, and every file serves at its old address (SC-014).
- **Outage**: readiness is unhealthy, uploads are refused as `media.storage_unavailable`,
  and both recover when MinIO is back (US3 scenario 5).

---

## 7. Keyboard and translation (SC-013)

Complete these with the keyboard only, with the translation-key checker on (002):
- the library;
- the folder tree;
- search;
- the alternative-text editor;
- the picker;
- the usage list;
- replace and delete;
- **Settings → Storage**.

**Expected**: no untranslated key, and axe is clean in every vitest spec.

---

## 8. Upgrade (SC-012, FR-045)

1. Restore the 012 snapshot fixture (with files, drafts and published pages using images)
   and start this build.
2. Run `UpgradeFrom012Tests`.

**Expected**:
- **Files**: every file is at the top level with no alternative text, and its old address
  serves identical bytes.
- **Backfill**: dimensions and thumbnails are recorded within minutes, and live pages then
  show `width`, `height` and `srcset`.
- **Usage**: matches the documents (`MediaReferenceBackfillTests`).

---

## 9. Smoke test (FR-043)

```bash
dotnet test tests/QuickBuilder.SmokeTests --filter "Category=Smoke"
```

**Expected**: the reference journey now uploads a JPEG, places it on the published page,
reads the `srcset` and fetches one variant from the site's host name with `200` and
`image/jpeg`.
