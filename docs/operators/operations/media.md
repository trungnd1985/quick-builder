# Media storage

Where uploaded files live, how they are cleaned up, and how to run on S3-compatible or Azure
Blob storage. Backing media up is covered in [Back up and restore](../backup-restore.md).

## Where files live

Uploaded images and PDFs are stored under `Storage:Local:Path`:

- **In the image**: `/var/lib/quickbuilder/media`, which the reference
  `docker-compose.yml` mounts as the named volume `media-data`.
- **Outside a container**: `{LocalApplicationData}/QuickBuilder/media` unless you set the
  path.

The database records which files exist; the storage location holds their bytes. Files are
laid out as `{site id}/media/{file id}`, so everything belonging to one site is one
directory.

**If you run the image without the reference compose file**, mount persistent storage at
`/var/lib/quickbuilder/media`. The image declares it as a volume, but a plain
`docker run` creates an anonymous volume that a recreated container does not reattach —
the files then appear lost.


## Moving an installation

Copy the database and the whole storage directory, then point the new installation at
both (`ConnectionStrings:Default`, `Storage:Local:Path`). Keep the directory's contents
exactly as they are: the layout is how files are found. With S3 or Azure Blob, point the
new installation at the same bucket or container, or at a copy of it (see "Moving media
between providers").

## Deleted files

Deletion is permanent. A deleted file stops being served on the next request, and its
bytes are removed straight away (or by the next sweep if that fails). Copies already held
by visitors' browsers or by a proxy or CDN in front of the installation may keep showing
until they expire: public files are sent with a one-year cache lifetime because an
address never changes content.

A file that pages or components use is deleted only after a second confirmation, and one
that a live page shows only by a Designer or Administrator. Pages that showed a deleted
image show nothing in its place from their next request.

## Replacing files

**Replace** in a file's details keeps the file — its id, name, folder, alternative text and
every page that shows it — and gives it new contents. The new contents get a new address
(`{id}-v2.png`, then `-v3`, …); the previous address is not found from that moment, so a
browser or proxy cache can never serve old contents under the new address. Live pages
show the new contents at their next request. As for delete, a file a live page shows can
be replaced only by a Designer or Administrator.

## Resized images

Published pages load images at the size they are shown, from copies at the widths in
`Media:Variants:Widths` (default 320, 640, 960, 1280, 1920 and 2560 pixels, never wider
than the original). The thumbnail is made right after upload; every other width is made
the first time a visitor needs it — that visitor may wait up to about 3 seconds — and is
served from storage afterwards. If a copy cannot be made (`Media:Variants:Timeout`, 10 s
by default), the original is served instead and the image offers no copies.

**Storage**: copies live next to the originals, under `{site}/media-variants/`, and
typically add 30–60 % to the bytes of the images they belong to. They need no backup: lost
copies are made again on request. GIFs, PDFs and images too small for any width have none.

## Clean-up

Hourly jobs keep storage tidy, and none ever touches a file that has a library entry:

- **Orphan sweep**: removes bytes with no library entry once they are older than
  `Media:OrphanGracePeriod`, and logs how many per site. It also removes resized copies
  nobody can request any more — of a deleted file, of a replaced version, at a width no
  longer configured — once they are an hour old.
- **Incomplete uploads**: removes temporary `.part` files older than one hour from each
  site's `.incoming` directory (Local), or aborts multipart uploads older than an hour
  (S3).

## Choosing a provider

- **Local** (the default): one instance, or several mounting the same directory. Simplest,
  and nothing leaves the machine.
- **S3** or **AzureBlob**: several instances without a shared file system, and storage
  managed by your cloud. See [configuration](../reference/configuration.md#storage) for the keys.

With a remote provider:

- Give the application only what it needs:
  - **S3**: `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` and `s3:AbortMultipartUpload` on
    `arn:aws:s3:::<bucket>/<KeyPrefix>*`, and `s3:ListBucket` and
    `s3:ListBucketMultipartUploads` on the bucket, limited to the prefix.
  - **Azure**: the **Storage Blob Data Contributor** role on the container, or a SAS with
    read, add, create, write, delete and list permissions.
- **S3**: add a lifecycle rule that aborts incomplete multipart uploads after one day. The
  application aborts its own failed uploads and runs an hourly clean-up for those older
  than an hour, but a rule covers an instance that never comes back. (MinIO lists pending
  uploads only by exact name, so there the clean-up relies on MinIO's own expiry, 24 hours
  by default.)
- **Azure**: nothing to add. Blocks of an unfinished upload are never visible and expire
  on their own.
- Keep the bucket or container private. Visitors are always served through the
  application, never directly from storage.
- Secrets go in environment variables or a secret store, never in a committed file.

**Outbound traffic.** Only the selected provider makes calls: to the configured service
URL or AWS's S3 endpoint for the region (S3), or to the account URL (Azure), plus the
environment's identity endpoint when `AccountUrl` is used. The Local provider makes none.

## More than one instance

Several instances behind a load balancer serve correct sites and up-to-date pages: site
resolution caches nothing (009), and each instance's page cache is keyed by numbers read
from the database on every request, so a publish reaches every instance at its next
request without a shared cache (012; see "Publishing and rendering").

With **S3 or Azure Blob**, media is shared too: a file uploaded, replaced or deleted
through one instance is served, or refused, by every other on its next request. Resized
images are produced once across all instances and stored next to the originals. No shared
cache is needed. With **Local**, every instance must mount the same directory.

If the bucket or container becomes unreachable, every instance reports not ready on
`/health/ready`, and uploads are refused with `media.storage_unavailable`, until it
returns.

**Backups** with a remote provider: back up the bucket or container together with the
database, as for a local volume. Versioning on the bucket or soft delete on the container
gives you a window to recover a mistaken deletion.

## Moving media between providers

Media is moved by copying, not by the application. Every provider uses the same object
names (`{site}/media/{file}`, `{site}/media/{file}-v{n}` and
`{site}/media-variants/{file}-v{n}-w{width}`, after any `KeyPrefix`), so a plain copy
works:

1. Optionally stop uploads (or the instances) so nothing changes during the copy.
2. Copy every object, keeping its name:
   - Local to S3: `aws s3 sync /var/lib/quickbuilder/media s3://<bucket>/<KeyPrefix> --exclude "*/.incoming/*" --exclude ".probe-*"`
     (or `mc mirror`, `rclone copy`);
   - Local to Azure: `azcopy copy "/var/lib/quickbuilder/media/*" "https://<account>.blob.core.windows.net/<container>/<KeyPrefix>" --recursive --exclude-path ".incoming"`;
   - between clouds: `rclone copy`.

   Leave out each provider's private names: `.incoming/` directories and `.probe-*` files
   on Local, `.quickbuilder-probe/` on S3 and Azure. The `media-variants/` objects may be
   left out too: they are produced again on request.
3. Set `Storage:Provider` and the new provider's keys, and start the application.
4. Read the consistency check (below). It runs on its own after startup.

## Storage consistency check

The check compares every site's library with what the provider holds and lists the files
whose current original is missing. Resized images are never reported. It runs:

- once in the background after every start, never delaying readiness, and once across
  instances that start together;
- from **Storage** in the admin menu (Administrators), which shows the provider, the
  latest run, and up to 1,000 missing files linked to their libraries (the count is
  always exact);
- from the command line:

  ```sh
  docker compose exec app dotnet QuickBuilder.Host.dll media check-storage
  ```

  It prints counts and up to 100 `site/file` ids. Exit codes: `0` nothing missing, `3`
  some missing, `1` the check could not run (another is running, or storage failed).

Only one check runs at a time across instances. Logs carry counts per site, never file
names. A missing file is shown as unavailable in its library: copy it again, or re-upload
it.
