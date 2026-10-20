# Operations

Running an installation: claiming it the first time, getting back in when nobody can sign
in, and what the application keeps. Every setting mentioned here is described in
[configuration](configuration.md).

## First-run setup

A fresh installation has no accounts and no default password. Until someone claims it,
every admin API call answers `428 Precondition Required`, and the admin interface shows
only the setup wizard.

To claim it you need the **setup code**, which the application prints in its logs at every
start while setup is pending:

```bash
docker compose logs app | grep "SETUP CODE"
```

The code looks like `ABCD-EFGH-JKMN`. Open `http://127.0.0.1:8080/admin`, enter the code,
and create the first administrator. Setup succeeds exactly once. After that the code stops
working and is no longer printed.

The code is in the logs because the only people who can read them are the people who run
the installation. That also makes the logs sensitive until setup is done:

- **Finish setup before you turn on log forwarding.** A collector that ships logs
  elsewhere would ship the code too, and whoever reads it first can claim the installation.
- **Finish setup before you widen the port binding.** The reference compose file publishes
  the app on `127.0.0.1` only, so nobody else can reach the wizard.

If you have access to the container but not the logs, `identity setup-code` prints the
same code (see below).

## Getting back in

These commands run inside the application container and are not reachable over the
network. They use the same configuration and database as the running app.

```bash
docker compose exec app dotnet QuickBuilder.Host.dll identity <command>
```

Add `-T` after `exec` when you run them from a script.

### `identity setup-code`

Prints the setup code while the installation is unclaimed.

| Result | Exit code |
|---|---|
| `Setup code: ABCD-EFGH-JKMN` | 0 |
| `Setup is complete; there is no setup code.` | 2 |
| `The database has pending migrations. Run the migrate command first.` | 3 |

### `identity recovery-link --email <address>`

Use this when no administrator can sign in, for example after a forgotten password. It
issues a set-password link for an existing, **active administrator**, and prints it:

```text
Set-password link (expires 2026-09-25T10:00:00Z):
https://cms.example.com/admin/set-password#token=…
```

If `Hosting:PublicUrl` is not set, the command prints only the path
(`/admin/set-password#token=…`). Put the address you use for the admin in front of it, for
example `http://127.0.0.1:8080`.

Open the link, choose a new password, and sign in. The link:

- works once, and expires after `Identity:SetPasswordLinks:Lifetime` (24 hours by default);
- replaces any earlier link for that account;
- signs the account out everywhere when it is used.

| Result | Exit code |
|---|---|
| The link | 0 |
| `Setup is not complete. Use 'identity setup-code' instead.` | 2 |
| Pending migrations | 3 |
| `No active administrator has that email address.` | 4 |
| Missing `--email`, or an unknown subcommand | 64 |

The link is printed on standard output only and never written to the logs. Treat it like
a password: whoever opens it first can set the administrator's password. Both commands
are recorded as security events (`operator_recovery_used`), including refused attempts.

To recover an account that is not an administrator, sign in as an administrator and issue
a link from **Users**.

## Passwords

The same rules apply wherever a password is chosen: the setup wizard, a set-password
link, and changing your own password.

- **Length**: 12 to 128 characters. Every character counts, spaces included, and a
  password is never trimmed or silently shortened: a longer one is refused, not cut.
- **Common passwords**: a password on the bundled list of common and breached passwords
  is refused, whatever its letter case. The list ships with the application, so the check
  works offline.
- **Not your email address or name**: a password equal to either is refused.
- **No composition rules**: there is no requirement for digits, symbols or mixed case.
  A few unrelated words make a strong password that is easy to remember.

Passwords are stored only as salted PBKDF2 hashes. The cost is
`Identity:Passwords:Pbkdf2Iterations`; see [configuration](configuration.md).

## Security events

Sign-ins, sign-outs, account changes, set-password links, the commands above, and site
changes that alter what the public can reach (`site_host_name_added`,
`site_host_name_removed`, `site_disabled`, `site_enabled`) are recorded in the
`identity.security_events` table. The table is append-only: the
application never changes or deletes an event, except for the retention sweep.

An hourly sweep deletes events older than `Identity:SecurityEvents:RetentionPeriod`
(365 days by default). The same sweep removes:

- sign-in throttle records that have been quiet for 24 hours;
- sessions that ended more than `Identity:Session:AbsoluteLifetime` ago;
- set-password links that expired more than 30 days ago;
- remembered browsers that were revoked or not used within
  `Identity:SignIn:KnownDeviceLifetime`.

If you need events for longer, for an audit for example, export them before the sweep
reaches them, or raise the retention period (up to 3650 days).

## Media storage

### Where files live

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

### Back up and restore

Back up the media volume **together with the database**, ideally at the same moment. To
restore, restore both. If they are restored from different moments:

- a library entry whose bytes are missing is shown as **unavailable**, and its public
  address answers `404`;
- bytes with no library entry are removed automatically by the orphan sweep once they are
  older than `Media:OrphanGracePeriod` (24 hours by default).

Nothing breaks either way, but files uploaded or deleted between the two snapshots are
inconsistent until you re-upload or delete them.

### Moving an installation

Copy the database and the whole storage directory, then point the new installation at
both (`ConnectionStrings:Default`, `Storage:Local:Path`). Keep the directory's contents
exactly as they are: the layout is how files are found. With S3 or Azure Blob, point the
new installation at the same bucket or container, or at a copy of it (see "Moving media
between providers").

### Deleted files

Deletion is permanent. A deleted file stops being served on the next request, and its
bytes are removed straight away (or by the next sweep if that fails). Copies already held
by visitors' browsers or by a proxy or CDN in front of the installation may keep showing
until they expire: public files are sent with a one-year cache lifetime because an
address never changes content.

A file that pages or components use is deleted only after a second confirmation, and one
that a live page shows only by a Designer or Administrator. Pages that showed a deleted
image show nothing in its place from their next request.

### Replacing files

**Replace** in a file's details keeps the file — its id, name, folder, alternative text and
every page that shows it — and gives it new contents. The new contents get a new address
(`{id}-v2.png`, then `-v3`, …); the previous address is not found from that moment, so a
browser or proxy cache can never serve old contents under the new address. Live pages
show the new contents at their next request. As for delete, a file a live page shows can
be replaced only by a Designer or Administrator.

### Resized images

Published pages load images at the size they are shown, from copies at the widths in
`Media:Variants:Widths` (default 320, 640, 960, 1280, 1920 and 2560 pixels, never wider
than the original). The thumbnail is made right after upload; every other width is made
the first time a visitor needs it — that visitor may wait up to about 3 seconds — and is
served from storage afterwards. If a copy cannot be made (`Media:Variants:Timeout`, 10 s
by default), the original is served instead and the image offers no copies.

**Storage**: copies live next to the originals, under `{site}/media-variants/`, and
typically add 30–60 % to the bytes of the images they belong to. They need no backup: lost
copies are made again on request. GIFs, PDFs and images too small for any width have none.

### Clean-up

Hourly jobs keep storage tidy, and none ever touches a file that has a library entry:

- **Orphan sweep**: removes bytes with no library entry once they are older than
  `Media:OrphanGracePeriod`, and logs how many per site. It also removes resized copies
  nobody can request any more — of a deleted file, of a replaced version, at a width no
  longer configured — once they are an hour old.
- **Incomplete uploads**: removes temporary `.part` files older than one hour from each
  site's `.incoming` directory (Local), or aborts multipart uploads older than an hour
  (S3).

### Choosing a provider

- **Local** (the default): one instance, or several mounting the same directory. Simplest,
  and nothing leaves the machine.
- **S3** or **AzureBlob**: several instances without a shared file system, and storage
  managed by your cloud. See [configuration](configuration.md#storage) for the keys.

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

### More than one instance

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

### Moving media between providers

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

### Storage consistency check

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

### Upgrading from feature 002

- The image now declares `VOLUME /var/lib/quickbuilder/media`, and the reference compose
  file mounts `media-data` there. If you use your own compose file or orchestration, add a
  persistent mount at that path before uploading anything.
- Add the media volume to your backups, alongside the database.
- `/_media/` on every site host is now served by the application.
- Every site starts with an empty media library. Nothing else changes.

### Upgrading from feature 012

- Two migrations run automatically: `AddLibraryAndVariants` (Media) and
  `AddMediaReferences` (Pages, which records which pages and components use which files).
  No file, address or page changes.
- Existing files keep their addresses and appear at the top level of each library, without
  alternative text.
- **Background work after the upgrade**, in batches of 50, one site at a time, without
  delaying readiness:
  - image dimensions and thumbnails are filled in over the first minutes; live pages pick
    up `width`, `height` and `srcset` as each image is processed;
  - search ignores letter case straight away, and accents once the search-text backfill has
    run (a few seconds per site).
- Deleting a file that pages use now needs a second confirmation; API clients pass
  `confirmInUse=true`. Editors can no longer delete or replace a file that a live page
  shows; a Designer or Administrator must.
- Published images now load resized copies. The first visitor to need each size may wait
  up to about 3 seconds. Storage use grows by roughly 30–60 % of image bytes as copies
  accumulate (see "Resized images").
- Several instances are now supported with `Storage:Provider` `S3` or `AzureBlob`. To move
  existing files, follow "Moving media between providers", then check **Storage** in the
  admin menu.
- The image is about 25 MB larger (libvips, for image dimensions and resizing).
- New command: `media check-storage`.

## Sites and host names

### How a request finds its site

Each public request is matched to a site by the host name the visitor used: the `Host`
header, or `X-Forwarded-Host` when it comes from a proxy named in `Hosting:KnownProxies`
or `Hosting:KnownNetworks` (see [configuration](configuration.md#hosting-and-reverse-proxies)).
Matching ignores letter case, a trailing dot and the port. Internationalised names are
stored in their ASCII (`xn--…`) form and shown readably in the admin; either spelling can
be typed.

Every host name belongs to at most one site, disabled sites included. A site keeps at least
one host name, and one of them is **primary**: the site's base URL is
`{scheme}://{primary}{Hosting:BasePath}`. The base URL never carries a port, so on a
non-standard public port (typical only of local testing) it will not open as written.
Nothing serves requests from the base URL yet.

Changes take effect at the next request, on every instance: nothing caches host names.

### What visitors see

- **A host name no site holds** and **a disabled site's host name** get the same `404`,
  byte for byte. Disabling a site does not reveal that it exists.
- **A path a site does not have** gets the site's not-found page: a `404` HTML page in the
  site's design and language (012). An address whose page is unpublished or never
  published gets exactly the same page.
- **With `Hosting:AdminHostNames` set**, `/api` and `/admin` on any other host get that same
  not-found page, byte for byte.

Neither response, and no log line, contains the host name that was asked for.

### Disabling a site

Disabling takes a site off the public surface at once and keeps everything else: content,
settings, grants and host names, which stay reserved. Site members keep working on it in
the admin. Enabling brings it back exactly as it was. Sites cannot be deleted yet.

### TLS and DNS

Certificates and DNS are yours: point each host name at your reverse proxy and terminate
TLS there. The app only needs to see the host name the visitor used, through the `Host`
header or a trusted proxy's `X-Forwarded-Host`.

### Audit

Adding or removing a host name, and disabling or enabling a site, are recorded as security
events (see above) with who did it. They appear within a few seconds, once the outbox
delivers them (`Outbox:PollInterval`).

### Upgrading from feature 003

- Every existing site becomes **enabled**, uses **HTTPS** and **English**, and takes its
  earliest host name as **primary**. Nothing that was reachable stops being reachable.
- Host-name matching also accepts internationalised names and trailing dots now. An
  internationalised name added from now on is stored in its ASCII form.
- With several trusted proxies, the forwarded host from the outermost trusted hop is now
  used. Before, only the nearest hop was.
- Unmatched public paths now answer with a fixed `404` body (`public.not_found`).
- **Recommended**: set `Hosting:AdminHostNames`, so client domains stop showing the admin
  sign-in page.

## Pages

Each site has a tree of pages and a set of shared components (a header, a footer), managed
under **Sites → a site → Pages** and **Shared components** in the admin shell. Designers
build the structure and the components; editors can see them and save page drafts.

### Drafts and what visitors see

Visitors see only what has been published (see "Publishing and rendering"). Saving a
draft, renaming or adding a page changes nothing public until someone publishes. Moving or
renaming a *published* page is the exception: it changes that page's address at once, and
the admin shell warns first.

### Addresses the platform keeps

No page address can begin with `/api`, `/admin` or `/health`: the platform answers there
before any page could. `/_media` is safe by construction, because slugs cannot contain
`_`. The list is part of the page schema contract
([docs/contracts/page-schema.md](contracts/page-schema.md) §5).

### Limits

A site holds at most 2,000 pages, nested at most 10 levels deep. A page's content document
is at most 1 MiB and 5,000 elements. These are part of the page schema contract, not
settings: a site valid on one installation is valid on every other, which export and
import rely on.

### Deleting

Deleting a page or a component is permanent; there is no trash yet (#21). A page with
child pages, the home page while other pages exist, and a component that pages still use
cannot be deleted — the admin shell says why.

### Upgrading from feature 009

- The upgrade adds a `pages` schema with its own migration history. Nothing else changes.
- Sites have no pages after the upgrade. Start each with its home page.
- No configuration changes are needed.

## Editor and design

Pages and shared components are built in the visual editor: **Pages → a page → Edit
content**, and **Shared components → Edit content**. Each site's colours, fonts and spacing
are under **Sites → a site → Design**. Designers edit components and the design; editors
build page content.

### Drafts and the design

The editor saves drafts only. Visitors see a page's changes when it is published, and
design changes when the design is published (**Design → Publish design**), not when they
are saved.

### Nothing leaves the installation

The editor makes no request to any other host. Video embeds show a placeholder naming
YouTube or Vimeo rather than loading the player, and fonts are system font stacks, so no
font is downloaded. It works on an installation with no internet access.

### What the browser keeps

- **Unsaved changes.** While changes wait to be saved, a copy is kept in the browser's
  IndexedDB, per user and per page, so a crash or a closed tab loses nothing: the next visit
  offers them back. The copy goes as soon as the changes are saved, when the offer is
  declined, and when the user signs out.
- **Copied blocks** are kept in the browser's local storage, so they survive a reload and
  paste into other pages of the same site. Signing out clears them.
- **The preview width** each user last chose.

On a shared computer, users should sign out rather than close the window: a session that
simply expires keeps that user's copies (only that user is ever offered them).

### Two people on one page

A save made after someone else's is refused, never overwritten. The editor shows the
refused version read-only so blocks can be copied, and **Reload latest** brings in the
saved one.

### Upgrading from feature 010

- The upgrade adds an `editor` schema with its own migration history. It writes no data:
  every existing site starts with the default design tokens.
- Stored drafts are unchanged and stay valid; they open in the editor as they are.
- No configuration changes are needed.

## Publishing and rendering

Visitors get server-rendered HTML of what has been published: pages, the shared components
they show, and the site's design. Designers and Administrators publish; Editors prepare
drafts.

### Publishing

- **Pages and components** are published from their publishing menu in the page tree, the
  component list and the editor. A page can only be published once the components it
  shows are; a component that published pages show cannot be unpublished or deleted.
- **The design** is published on its own, from **Design → Publish design**. Until the first
  design publish, pages use the default design.
- **Unpublishing** takes a page off the site from the next request. A published page
  cannot be deleted until it is unpublished.
- **History**: every page and component keeps its 50 most recent versions, which can be
  viewed, rolled back to (published again as a new version) or copied into the draft. The
  design keeps 50 versions too.
- **The publish log** (**Sites → a site → Publish log**, Administrators only) lists every
  publishing action on the site, including those on pages and components that have since
  been deleted.

### Schedules

A publish of a page or component, or an unpublish of a page, can be scheduled. A scheduled
publish captures the draft when it is set, so what goes live is what was reviewed. The
schedule is a delayed message in the Pages outbox and runs within one outbox poll interval
(`Outbox:PollInterval`, 5 s by default) of its time, exactly once, on whichever instance
claims it. A stopped installation runs overdue schedules when it starts again. A schedule
that cannot run, for example because a component it needs was unpublished in the meantime,
is marked failed and logged, and the admin shell shows it until someone acts.

A manual publish, unpublish or rollback cancels the matching pending schedule; the admin
shell says so before it does.

### Caching and memory

Rendered pages are cached in each instance's memory, up to `Cache:MemoryLimitMegabytes`
(default `256`, from 16 to 16,384). The least recently used pages go first when it is full,
and a page not requested for an hour is dropped. A typical page is tens of kilobytes, so the
default holds several thousand. Size the container's memory limit for this cache on top of
the application's own needs.

The cache never serves stale content: each page's key includes numbers that every relevant
change moves on, in the same transaction as the change, and they are read from the database
on every request. So a publish is visible on every instance at its next request, with no
shared cache and nothing to purge (plan exception E-4 closed). Visitors' browsers get an
`ETag` and `Cache-Control: public, no-cache`, so they revalidate each time and get a `304`
while nothing has changed. A CDN in front must respect those headers.

If the cache fails, pages are rendered without it: slower, never wrong.

### Public paths

- `/_assets/blocks.{hash}.css` is the block stylesheet, served on every site's host names
  and cached by browsers for a year; a new release changes its name.
- `/_media/` serves media files, as before.
- Any other path on a site's host is a page address, or the site's not-found page. Only
  `GET` and `HEAD` are answered; other methods get `405`.

### The not-found page

Missing addresses get an HTML page in the site's design, with a heading and a sentence in
the site's default language (all 40 supported languages; English where a language has no
text) and a link home while the home page is published. It is never cached by browsers
(`Cache-Control: no-store`) and contains nothing from the request. Unknown and disabled host
names still get the JSON `404` of feature 009.

### Database connections

The public path reads through read-only connections (`default_transaction_read_only=on`),
so a bug there cannot write. That adds one connection pool per reading module — Pages,
Editor and Media — to the one each module already has. With Npgsql's default of up to 100
connections per pool, set PostgreSQL's `max_connections` for the pools you expect under
load per instance, times the number of instances; in practice a few dozen per instance
suffice.

### Upgrading from feature 011

- Two migrations run automatically: `AddPublishing` (Pages) and `AddDesignPublishing`
  (Editor). They add tables and change no existing data; drafts and design tokens stay
  exactly as they were.
- Nothing is published after the upgrade, so every site keeps answering "not found" until a
  Designer or Administrator publishes. Publish shared components (header, footer) before
  the pages that use them, then the pages, then **Publish design** to take the site's
  design live; until then pages use the default design.
- Missing pages on a site's host names now get the HTML not-found page instead of a JSON
  body. Unknown host names still get the JSON answer.
- `/_assets/` is a new platform path on every site.
- New setting: `Cache:MemoryLimitMegabytes` (default `256`). Allow for it in the
  container's memory limit.
- Several instances behind a load balancer now serve up-to-date pages without a shared
  cache. Media still needs shared storage.

## Collections

Each site can define collections: structured, repeatable content such as blog posts or
products, managed under **Sites → a site → Collections**. Administrators and designers
define a collection's fields; editors can see them. Every site role, editors included,
writes, publishes and schedules a collection's **items** (feature 015), from the
collection's **Items** link. Pages show items through templates and collection lists (016,
below).

### Changing fields after items exist

- **Renaming, re-keying and reordering** fields never touch stored items. Items are stored
  by field id, which never changes.
- **Removing a field or an option, and changes that convert values** (text to number, rich
  text to text, fewer decimal places, …) show a preview first. It says how many items are
  affected and how. Values removed or shortened this way are **deleted for good**: there is
  no undo. Back up the database before a large restructuring.
- A change applies to items' **draft and published** values at once. While it runs, edits
  to that collection's items are refused with "try again shortly"; reading is never blocked.
- Conversions of dates use **UTC**, because sites have no time zone yet.
- A change that removes or converts values also applies it to the **published versions kept
  for rollback** and to content captured for a **scheduled publish**, so a removed value is
  gone from everywhere.

### Large collections and reverse proxies

A change that rewrites many items runs inside one request (about 30 seconds at 10,000
items on the reference setup). Give the admin path a proxy timeout of at least **60
seconds**. If the connection drops, the change rolls back completely and can simply be
saved again.

### Deleting

Deleting a collection deletes its fields, history and every item, permanently; there is no
trash yet (#21). A collection that another collection's reference field points at cannot
be deleted until that field is removed or changed.

### Items

- **Drafts and publishing**: an item's draft can be saved however incomplete; publishing
  needs every required value and every value within its field's settings. Each publish is a
  version; the last **50** are kept, and any of them can be published again or copied into the
  draft.
- **Who may publish**: unlike pages, **editors may publish, unpublish and schedule items**.
- **Schedules** run through the same built-in background jobs as page schedules, within a
  minute of their time, once, also after a restart. A scheduled publish whose content no
  longer meets the fields' rules when its time comes does not publish; the item's history
  says why and the item is marked as needing attention.
- **Deleting an item** others refer to is refused, listing them; there is no trash yet (#21).
- **Media**: a file used by a **published item** counts as in live use, so only designers and
  administrators can delete or replace it, as for files on published pages.

### Rich text

- **Formats**: rich text, in Rich Text blocks and in items' rich text fields, holds headings
  (levels 2 to 4), quotes, bulleted and numbered lists, bold, italic, underline,
  strikethrough, inline code, links, images from the media library, simple tables and
  alignment. It is always stored as structured data, never as HTML.
- **Images need alternative text** before a page, component or item is published, or must
  be marked decorative. Drafts can be saved without it; publishing (manual or scheduled)
  names the images that need it.
- **Pasting**: content pasted from a word processor or a web page keeps its headings, lists,
  tables and text styles; colours, fonts and anything else are dropped. Images that exist
  only as web addresses are left out, with a notice; pasted image files (screenshots) are
  uploaded to the media library's top folder.
- **Limits** per rich text: 20,000 characters, 50 images and 20 tables; a table holds at most
  200 rows and 20 columns.
- **Collections**: a rich text field can limit its formats (headings and quotes, images,
  tables). Turning a group off keeps existing content and marks the items that use it as
  needing attention.
- **Media**: images in rich text count as uses of their file, like Image blocks and image
  fields.

### Upgrading from feature 015 (richer rich text)

- **Reload every open admin tab after upgrading.** A tab opened before the upgrade cannot save
  over content that uses the new formats; it says it is out of date and asks to be reloaded.
- No migration: stored content is unchanged and is published exactly as before.
- No configuration changes are needed.

### Upgrading from feature 014

- The upgrade adds item columns and tables to the `cms` schema. Items stored before it (only
  test or demo data) get a slug and, if published, a version 1. Shortly after start-up, a
  background step gives them names, search text and slugs built from their names.
- No configuration changes are needed.

### Upgrading from feature 013

- The upgrade adds a `cms` schema with its own migration history. Nothing else changes.
- Sites have no collections after the upgrade.
- No configuration changes are needed.

## Templates and collection lists

Feature 016 puts collection items on the site.

### Item pages and addresses

- A designer makes a page the **template** of a collection, under **Pages → a page → Use as
  template**. Every **published** item then has a page at the template's parent address
  followed by its slug: a template under `/blog` answers `/blog/{slug}`. A collection has
  at most one template, and a parent holds at most one.
- Addresses are matched in this order: the platform's own paths (`/_media`, `/_assets`,
  `/api`, `/admin`), then a live page at that address, then a live template with a
  published item of that slug, then the site's not-found page. So a page created at
  `/blog/archive` hides the item whose slug is `archive`; the admin warns when that
  happens.
- An item's page exists while the item is published and the template is live. Changing an
  item's slug, moving the template or an ancestor, or unpublishing the template changes or
  removes item addresses; the admin says how many. Old addresses are not redirected: they
  answer with the not-found page (redirects come with #17).

### Lists, paging and caching

- A **collection list** block shows published items with filters, a sort and a limit, or
  page by page with `?page=N` (each list on a page has its own parameter name). Paging
  links are plain links: they work without JavaScript and can be shared.
- Only a page's own list parameters, with page numbers from 2 to 10,000, become part of the
  cache key; every other query parameter is ignored, so tracking parameters cannot fill the
  page cache.
- Pages refresh exactly when what they show changes: publishing an item re-renders its own
  page and the pages that list its collection, or a collection that refers to it; a
  collection's schema change re-renders every page that shows the collection, item pages
  included. Re-rendering happens at the next request.
- **Over-refresh on busy collections**: a template that holds a list of its own collection
  ("related posts") re-renders every item page whenever any item of that collection is
  published. This is correct, and each page re-renders only when next visited, but on a site
  that publishes often it lowers the cache hit rate. Lists on the home page of a busy
  collection behave the same way.
- One page render makes at most 200 list queries; lists beyond that show their empty state,
  and a warning naming the page and list ids is logged.

### Deleting collections and changing fields

- A collection that a page or component uses, in its draft, live version or a scheduled
  publish, **cannot be deleted**. The refusal lists them (up to 50) and says where each uses
  it; remove the lists, bindings or template first.
- A schema change's preview says how many pages and components read each field it removes or
  retypes. After the change, live pages show the new data at their next request, and a
  binding to a removed field shows the block's own value (or hides the block, if the
  designer chose that).
- Nothing a collection change does can make a page fail: broken bindings fall back, broken
  filters match nothing, and the page still answers.

### Upgrading to templates and lists (016)

- Three migrations run at start-up: Pages `AddTemplates`, Cms `AddRenderStamp` and Cms
  `AddPublishedText`. They add columns and one table; nothing existing is rewritten.
- Shortly after start-up, a background step folds each published item's text for list
  filters. Until it finishes, a list filter that searches text misses items not yet folded.
- The block stylesheet changes, so every cached page renders once more after the upgrade;
  existing pages render exactly as before.
- **Log entries written while serving the public site no longer carry `RequestPath`.** A
  public address is content (a page's address, or an item's slug), so it is kept out of the
  logs; follow a public request by its `TraceId`, and by the page and site ids an entry carries.
  Entries for `/api`, `/admin` and `/health` keep the path.
- No configuration changes are needed.

## Search engines, sitemaps and redirects

Feature 017 makes each published site describe itself to search engines and keep old links
working. Nothing here needs configuration.

### robots.txt and sitemaps

- Each site answers `/robots.txt`, `/sitemap.xml` and, past 50,000 addresses, numbered files
  `/sitemap-1.xml`, `/sitemap-2.xml` and so on, listed by `/sitemap.xml` as an index.
- **Under a base path** (`Hosting:BasePath`, such as `/cms`), these answer at
  `/cms/robots.txt`. Crawlers only ever read `/robots.txt` at the root of a host name, so
  the reverse proxy must forward `/robots.txt` to `/cms/robots.txt`. The sitemap addresses
  inside it already carry the base path.
- The sitemap is rebuilt on the first request after anything it lists changes (a publish, an
  unpublish, a hidden page, a moved template), on every instance. Builds happen only when
  requested; a busy site that publishes items all day rebuilds on each crawler visit.
- **Discourage search engines** (a site setting for Administrators) makes `robots.txt`
  disallow everything, marks every page and file `noindex, nofollow`, and empties the
  sitemap. Every admin screen of that site says so until it is turned off.

### Host names

- **Redirect other host names to the primary one** is a site setting for Administrators. It
  is on for sites created after the upgrade and off for existing ones. When it is on, every
  public address on another host name answers `301` to the same address on the primary
  host name. Media files (`/_media`) and the block stylesheet (`/_assets`) keep answering on
  every host name, so images in old emails still load.
- Before turning it on, make sure the primary host name reaches the site. The admin asks for
  confirmation and lists every host name that will redirect.

### Address normalisation

- `/Blog/`, `/BLOG` and `/blog/` now answer `301` to `/blog` when a page or item page is
  there, keeping the query. Before 017 they served the page directly. An address with
  nothing behind it goes on to the redirects and then the not-found page, unchanged. The home
  page and the base path root are never redirected for a trailing slash.

### Redirects

- Each site holds at most 10,000 redirects, managed under the site's **Redirects** page and
  imported or exported as CSV (1 MiB, 5,000 rows per file).
- A redirect answers only when no live page or item page holds its address, except a redirect
  whose source has a query, which is checked first.
- **Use counts** ("used N times, last on …") are counted in memory on each instance and saved
  about once a minute. A restart loses at most the last minute's counts; nothing else depends
  on them.
- When an item's slug changes and the member keeps the old address, its redirect is created
  by a background message within a few seconds. Until then the old address gives the
  not-found page. Page redirects are created with the change itself.
- Every change to a redirect, and to a site's search settings, is recorded with who made it
  and when (ids only, never the addresses).

### Upgrading to SEO, sitemaps and redirects (017)

- Three migrations run at start-up: Pages `AddSeo`, Sites `AddSearchSettings` and Cms
  `AddScheduleRedirect`. They add tables and columns; nothing existing is rewritten.
- Every cached page renders once more after the upgrade: the head now carries the canonical
  address, the Open Graph tags and, where there is something to describe, structured data.
  Page bodies are unchanged.
- Existing sites keep serving every host name (`redirect_other_hosts` is off); turn it on per
  site when ready.
- Addresses in other letter cases or with a trailing slash now redirect (see above).
- No configuration changes are needed.

## Navigation and menus

Designers build each site's menus under **Menus** (up to 50 per site, 300 entries over three
levels each) and show them with the Navigation block. Menus are published like components:
a menu publish refreshes exactly the pages that show it, directly or through a component, and
a page, item or template change refreshes the pages whose menus point at it.

- **Content-Security-Policy**: a page that shows a Navigation block carries one small inline
  script, admitted by its hash: that page's policy adds `script-src 'sha256-…'`. Other pages
  carry no script and no `script-src`. A reverse proxy or CDN that rewrites or replaces the
  policy must keep that `script-src` on those pages, or menus lose Escape and collapse
  handling. They stay usable either way: every submenu is a native disclosure, and the
  small-screen panel is written open.
- **Menu styles**: the site-wide look and colours of menus are set in **Design**, under
  Menus, and reach visitors when the design is published. Each Navigation block can override
  any of them. Sites that set no menu style render exactly as before it existed. Colours that
  are hard to read are warned about in the admin, never refused.
- Menu names, labels and addresses are content: they never appear in logs or in the menu
  audit, which records ids and actions only.

### Upgrading to navigation and menus (024)

- One migration runs at start-up: Pages `AddMenus`. It adds tables and lets the publishing
  tables accept the `menu` subject kind; nothing existing is rewritten.
- Every cached page renders once more after the upgrade: the block stylesheet changes. Page
  bodies are unchanged, and pages without a Navigation block keep the same policy.
- Existing navigation built from buttons keeps working.
- No configuration changes are needed.

## Upgrading from the foundation release (feature 001)

Feature 001 had no authentication. After the upgrade:

- The installation starts **unclaimed**, even though its sites are kept. Claim it with the
  setup code as described above. Nothing is lost, and the first administrator sees every
  existing site.
- **Scripts that called the API anonymously stop working.** Every admin endpoint now
  needs a signed-in session, and every state-changing call needs the
  `X-QuickBuilder-Request: 1` header. Scripts must sign in through
  `POST /api/v1/identity/sign-in` and send the `qb_session` cookie on later calls.
- Public site traffic (`GET /` on a site's host name) and the health probes are
  unaffected.
