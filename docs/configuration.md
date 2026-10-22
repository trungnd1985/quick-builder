# Configuration

Every setting comes from `appsettings.json` and every one can be overridden by an
environment variable (FR-029). The environment form replaces `:` with `__`, so
`ConnectionStrings:Default` becomes `ConnectionStrings__Default`.

Invalid or missing required configuration stops startup immediately, naming the offending
key (FR-028). The application never starts in a degraded state and then fails confusingly
later.

## Required

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | *(none)* | PostgreSQL connection string. No default exists on purpose — PostgreSQL is the one external dependency and there is no sensible fallback. |

## Database

| Key | Default | Meaning |
|---|---|---|
| `Database:AutoMigrate` | `true` | Apply migrations during startup. Set `false` to apply them yourself with the `migrate` command; readiness then reports `MigrationsPending` until you do, so a half-deployed installation is visible rather than silent. |
| `Database:ConnectionRetryTimeout` | `00:02:00` | How long to keep retrying an unreachable database at startup. Bounded, not infinite: a container that waits forever looks identical to one that is wedged. |
| `Database:ConnectionRetryDelay` | `00:00:02` | Delay between connection attempts. |
| `Database:MigrationTimeout` | `00:05:00` | How long one module may hold its migration advisory lock. |

Applying migrations manually:

```bash
docker compose run --rm app dotnet QuickBuilder.Host.dll migrate
```

## Hosting and reverse proxies

| Key | Default | Meaning |
|---|---|---|
| `Hosting:BasePath` | *(empty)* | Serve under a sub-path, e.g. `/cms`. Empty means the root. |
| `Hosting:KnownProxies:<n>` | *(empty)* | Proxy IP addresses whose forwarded headers are trusted. |
| `Hosting:KnownNetworks:<n>` | *(empty)* | Trusted proxy networks in CIDR form, e.g. `10.0.0.0/8`. |
| `Hosting:AdminHostNames:<n>` | *(empty)* | Host names on which the administrative surface (`/api`, `/admin`) answers, e.g. `admin.example.com`. Empty means every host name. When set, `/api` and `/admin` on any other host name answer exactly like a missing page, so client domains never show the sign-in page. `/health` answers on every host. Entries follow the site host-name rules (no scheme, port, path or wildcard); an invalid entry, or one a site holds, stops startup and names the key. |
| `Hosting:PublicUrl` | *(empty)* | The address people use to reach this installation, including any base path, e.g. `https://cms.example.com`. Must be empty or an absolute `http(s)` URL. Used only to print complete links from `identity recovery-link`; when empty, the command prints the path for you to complete. The app never works out its own address from request headers. |

**Trusting nothing is the default and it matters.** Sites are resolved from the `Host`
header, so a spoofable `X-Forwarded-Host` would let a caller choose which site they are
talking to. If you put a proxy in front, name it.

**Sign-in security depends on this too.** Two identity rules read the client's address and
scheme, and behind a proxy they are only correct when the proxy is named here:

- per-client sign-in throttling blocks an address after repeated failures, and every
  request through an unnamed proxy appears to come from the proxy, so one attacker would
  block everyone;
- the HTTPS rule refuses credentials sent over plain HTTP from other machines, and it
  cannot see that the original request was HTTPS unless it trusts `X-Forwarded-Proto`.

**Several proxies.** Name every hop you run (for example a CDN and the proxy behind it).
Forwarded values are unwound hop by hop from the nearest one, and unwinding stops at the
first address that is not named, so an unnamed hop can never choose the host.

**robots.txt under a base path.** Search engines look for `/robots.txt` at the root of a host
name only. With `Hosting:BasePath` set, forward that one address to `{BasePath}/robots.txt`
at the proxy (see [operations.md](operations.md#robotstxt-and-sitemaps)). Feature 017 (search
engines, sitemaps and redirects) adds no configuration keys: every choice is a site setting.

**Keeping the admin off client domains.** For an installation that hosts client sites, set
`Hosting:AdminHostNames` to a name only you use. If you mistype it and lose the admin, remove
the key (or fix it) and restart: the setting is the only thing that confines the admin.

## Admin

| Key | Default | Meaning |
|---|---|---|
| `Admin:PathPrefix` | `/admin` | Where the admin interface is served. Reserved ahead of the public catch-all. |

The admin requires sign-in. A fresh installation is claimed through the setup wizard with
a one-time code printed in the logs (see [operations](operations.md)). Until setup is
finished, whoever has that code can become the administrator. The reference
`docker-compose.yml` therefore publishes the app on `127.0.0.1`. Finish setup first. If
you then widen the binding, put an HTTPS proxy in front, name it in
`Hosting:KnownProxies`, and set `Identity:AllowInsecureHttp` to `false`.

## Identity

Sign-in, sessions and accounts. Every value is checked at startup, and a value outside its
range stops the app and names the key.

| Key | Default | Range | Meaning |
|---|---|---|---|
| `Identity:Session:IdleTimeout` | `02:00:00` | 5 min – 24 h | A session unused for this long ends. |
| `Identity:Session:AbsoluteLifetime` | `12:00:00` | 5 min – 30 days, and at least `IdleTimeout` | A session ends this long after sign-in, however active. |
| `Identity:SignIn:ClientFailureThreshold` | `5` | 3 – 100 | Failed sign-ins from one address before it is blocked. |
| `Identity:SignIn:ClientBlockDuration` | `00:15:00` | 1 min – 24 h | How long a blocked address waits. |
| `Identity:SignIn:AccountFailureThreshold` | `3` | 1 – 20 | Failed sign-ins on one account before each further attempt must wait. The wait doubles with each failure. |
| `Identity:SignIn:AccountMaxDelay` | `00:01:00` | 1 s – 10 min | The longest an account's wait grows to. There is no hard lockout. |
| `Identity:SignIn:KnownDeviceFailureThreshold` | `5` | 1 – 20 | Failures from a remembered browser before it stops being exempt from the account wait. |
| `Identity:SignIn:KnownDeviceLifetime` | `90.00:00:00` | 1 – 365 days | How long a browser that signed in successfully is remembered. |
| `Identity:Passwords:Pbkdf2Iterations` | `210000` | at least 210,000 | Password hashing cost (current OWASP guidance). Raising it upgrades stored hashes as people sign in. It can never be lowered. If the startup warning says hashing is slow, give the app more CPU rather than lowering this. |
| `Identity:SetPasswordLinks:Lifetime` | `1.00:00:00` | 10 min – 7 days | How long a set-password link stays valid. |
| `Identity:SecurityEvents:RetentionPeriod` | `365.00:00:00` | 1 – 3650 days | How long security events are kept. An hourly sweep removes older ones. |
| `Identity:AllowInsecureHttp` | `false` | | Accept credentials over plain HTTP from other machines. The app logs a warning at every startup while it is on. The reference compose file turns it on because, inside Docker, requests from your own machine do not arrive from loopback. It is safe only while the port stays bound to `127.0.0.1`. |

## Storage

Where uploaded files live. Every value is checked at startup; an unusable one stops the
app before it listens, with a message that names the key.

| Key | Default | Meaning |
|---|---|---|
| `Storage:Provider` | `Local` | `Local` (the file system), `S3` (any S3-compatible service) or `AzureBlob`, case-insensitive. Any other name stops startup and lists the supported names. Only the selected provider is loaded, and only it makes network calls. |
| `Storage:Local:Path` | Image: `/var/lib/quickbuilder/media`. Elsewhere: `{LocalApplicationData}/QuickBuilder/media` | The directory that holds uploaded files. It must be absolute, and it must not be, or be inside, the application's own directory, content root or web root (checked after resolving links). It is created if missing and must be writable. In a container, mount persistent storage here — the reference compose file uses the `media-data` volume. |

### S3-compatible (`Storage:Provider` = `S3`)

AWS S3, Cloudflare R2, MinIO, Ceph and other services that support conditional writes
(`If-None-Match: *`). Startup checks that they do.

| Key | Default | Meaning |
|---|---|---|
| `Storage:S3:Bucket` | — (required) | The bucket. It must exist; QuickBuilder never creates it. |
| `Storage:S3:Region` | — (required) | The region, e.g. `eu-west-1`. Any value (such as `auto`) for services that ignore regions. |
| `Storage:S3:ServiceUrl` | AWS | An absolute `http(s)` URL for any service other than AWS, e.g. `https://<account>.r2.cloudflarestorage.com`. |
| `Storage:S3:ForcePathStyle` | `false` | `true` for most self-hosted services (MinIO, Ceph), which address buckets by path rather than by host name. |
| `Storage:S3:KeyPrefix` | empty | Puts every object under a prefix, e.g. `quickbuilder/`: lower-case letters, digits and `-` in segments, ending in `/`. Lets one bucket serve several installations. |
| `Storage:S3:AccessKeyId` | — | With `SecretAccessKey`, or neither. With neither, the environment's credentials are used: environment variables, web identity (EKS, IRSA) or the instance profile. |
| `Storage:S3:SecretAccessKey` | — | **Secret.** Set it through the environment (`Storage__S3__SecretAccessKey`) or a secret store, never in a committed file. It never appears in messages or logs. |

### Azure Blob (`Storage:Provider` = `AzureBlob`)

Set exactly one of `ConnectionString` and `AccountUrl`.

| Key | Default | Meaning |
|---|---|---|
| `Storage:AzureBlob:Container` | — (required) | The container. It must exist; QuickBuilder never creates it. |
| `Storage:AzureBlob:ConnectionString` | — | An account-key or SAS connection string. **Secret**: set it through the environment (`Storage__AzureBlob__ConnectionString`) or a secret store. Neither it nor the key inside it ever appears in messages or logs. |
| `Storage:AzureBlob:AccountUrl` | — | `https://<account>.blob.core.windows.net`, authenticated with the environment's identity instead of a secret: managed identity, workload identity, the `AZURE_*` environment variables, or a signed-in Azure CLI. Never an interactive sign-in. |
| `Storage:AzureBlob:KeyPrefix` | empty | As for S3. |

### Startup and readiness

With a remote provider, startup writes, re-writes (which must be refused), reads and
deletes a small probe object under `{KeyPrefix}.quickbuilder-probe/`, within 30 seconds.
A failure stops the app before it listens and names the key at fault: credentials
rejected, bucket or container missing, access denied, unreachable, or no conditional
writes.

`/health/ready` includes a storage check (2 seconds for a remote provider): an instance
whose storage stops being writable or readable after startup reports not ready until it
recovers.

## Media

| Key | Default | Range | Meaning |
|---|---|---|---|
| `Media:MaxFileSizeBytes` | `20971520` (20 MiB) | 1,024 – 1,073,741,824 | The largest file an upload may be. The server stops receiving an upload the moment it passes this. |
| `Media:AllowedTypes` | all six: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/avif`, `application/pdf` | a subset of those six | Which types uploads may have, decided from each file's contents. Set `Media__AllowedTypes__0`, `__1`, … to narrow the list; any other type (SVG, for example) stops startup. |
| `Media:OrphanGracePeriod` | `1.00:00:00` (24 h) | 1 hour – 30 days | Stored bytes with no library entry — left by a crash or a failed clean-up — are removed by an hourly sweep once they are this old. |
| `Media:MaxImagePixels` | `100000000` (100 megapixels) | 1,000,000 – 1,000,000,000 | Images with more pixels (width × height) are refused at upload with `media.dimensions_too_large`. Only the image header is read to decide, so an oversized image is never decoded. |
| `Media:Variants:Widths` | `320, 640, 960, 1280, 1920, 2560` | 1–10 widths, ascending, 64 – 4,096 | The widths published pages and the admin offer resized copies at. The smallest is the thumbnail width, produced right after upload; the others are produced the first time they are requested. Set `Media__Variants__Widths__0`, `__1`, … to replace the list; addresses for removed widths stop working and their stored copies are cleaned up. |
| `Media:Variants:MaxConcurrent` | half the processor count, at least 1 | 1 – 64 | How many resized copies one instance produces at the same time. Requests beyond it wait. |
| `Media:Variants:Timeout` | `00:00:10` | 1 – 60 seconds | How long producing one resized copy may take, waiting included. Past it, the original is sent (not cached) and the file stops offering resized copies until it is replaced. |

Every site serves its media files at `/_media/{id}.{ext}` on its own host names. The
`/_media` path is reserved on every site host: if a reverse proxy handled that path
itself, it must stop.

## Outbox

| Key | Default | Meaning |
|---|---|---|
| `Outbox:Enabled` | `true` | Run the background dispatcher. |
| `Outbox:PollInterval` | `00:00:05` | How often an idle dispatcher looks for work. |
| `Outbox:BatchSize` | `50` | Rows claimed per pass. |
| `Outbox:HandlerTimeout` | `00:00:30` | Per-event handler timeout. Handlers run inside the claim transaction, so a slow one holds a row lock and a connection for this long. |
| `Outbox:MaxAttempts` | `5` | Attempts before an event is set aside as failed. |
| `Outbox:RetentionPeriod` | `7.00:00:00` | How long completed events are kept before the sweep removes them. |

Scheduled publishes and unpublishes run through the outbox (012), so `Outbox:Enabled`
must stay `true` on at least one instance for schedules to run, and `PollInterval` bounds
how late one can be.

## Menus

Feature 024 adds no configuration keys. Menu limits (50 menus per site, 300 entries, three
levels) are part of the menu contract, not settings.

## Email

Outgoing email sends form notifications and the test email (018). All keys are optional:
leave `Email:Smtp:Host` empty and the installation sends no email at all, makes no
connection, and shows editors that notifications are off.

| Key | Default | Meaning |
|---|---|---|
| `Email:Smtp:Host` | *(empty: email off)* | The mail server's host name or address. |
| `Email:Smtp:Port` | `587` | 1 to 65535. Usually 587 with `StartTls`, 465 with `SslOnConnect`. |
| `Email:Smtp:Security` | `StartTls` | `None`, `StartTls` (encrypt before anything is sent; the server must offer it) or `SslOnConnect`. The server's certificate must be trusted by the container. |
| `Email:Smtp:UserName` | *(empty)* | Sign-in name. Empty means no sign-in. |
| `Email:Smtp:Password` | *(empty)* | Sign-in password. **A secret**: set it through the environment (`Email__Smtp__Password`) or a mounted secret file, never in a committed file. It is never stored in the database or shown in the admin. |
| `Email:Smtp:From` | — | The sender address, such as `website@example.com`. Required when `Host` is set. |
| `Email:Smtp:FromName` | `QuickBuilder` | The sender name, at most 100 characters. |
| `Email:Smtp:Timeout` | `00:00:30` | How long one attempt may take, from 5 seconds to 5 minutes. |

Startup stops, naming the key, when `Host` is set without a valid `From`, or when a value
is out of range. It also stops when `Security` is `None` and a `UserName` is set for a
mail server on another machine: the password would cross the network unencrypted. On the
same machine (`localhost`, `127.0.0.1`, `::1`) that is allowed.

Links in notifications point at the submission in the admin and are built from
`Hosting:PublicUrl` and `Admin:PathPrefix`, never from a request. With email on and
`Hosting:PublicUrl` empty, emails say "Open Submissions in the admin" without a link, and
startup logs a warning.

## Cache

| Key | Default | Meaning |
|---|---|---|
| `Cache:MemoryLimitMegabytes` | `256` | Upper bound on the in-memory cache of each instance, from 16 to 16,384. Rendered public pages take nearly all of it. When full, the least recently used pages are dropped and rendered again on their next request. A value outside the range stops startup. |

The cache never decides what visitors see: each page's cache key includes version numbers
read from the database on every request, so a publish is visible on every instance at its
next request without any shared cache (012 research PR-05).

## Telemetry

| Key | Default | Meaning |
|---|---|---|
| `Telemetry:ServiceName` | `quickbuilder` | Service name on traces and metrics. |
| `Telemetry:OtlpEndpoint` | *(empty)* | OTLP collector endpoint. **Empty means no exporter is registered and nothing leaves the process.** |

Tracing and metrics are always instrumented — that is where the trace id in the logs comes
from — but nothing is exported unless you name an endpoint. An offline installation
behaves identically to a connected one.

## Logging

| Key | Default | Meaning |
|---|---|---|
| `Serilog:MinimumLevel:Default` | `Information` | Global minimum level. |
| `Serilog:MinimumLevel:Override:<Namespace>` | see `appsettings.json` | Per-component level, e.g. `Serilog__MinimumLevel__Override__QuickBuilder=Debug`. |

Logs are structured JSON on stdout. Every entry carries a `TraceId`, which is the same
value returned in the `traceId` of an error response — so "it failed, here is the id" is
enough to find the entry.

Logs never contain credentials, secrets, personal data or content bodies.
