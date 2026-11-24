# Troubleshooting

Find what you see below. Each entry says what it means and how to fix it, and links to the
page with the full explanation. Most problems show up in two places: the application log
(`docker compose logs app`) and the readiness probe (`/health/ready`); see [Logs and
health](#logs-and-health) at the end.

## Startup

### The container does not start, or keeps restarting {#container-does-not-start}

**What you see**: `docker compose ps` shows `app` as `exited` or `restarting`, or
`docker compose up --wait` fails.

**Why**: the database container is not healthy yet, the image could not be pulled, or the
process stopped on a configuration error before it could serve anything.

**Fix**:

1. `docker compose ps` — is `db` `healthy`? If not, `docker compose logs db`.
2. `docker compose logs --tail 50 app` — the last lines say why it stopped. A message that
   names a setting is a [configuration error](#configuration-error).
3. `docker compose pull` failing means the server cannot reach `ghcr.io`, or the image tag in
   `docker-compose.yml` does not exist; check it against the [upgrade notes](upgrade-notes.md).

### Startup stops with a message naming a setting {#configuration-error}

**What you see**: the log ends with lines such as
`Email:Smtp:From must be the sender's email address …` or
`ConnectionStrings:Default is required.`

**Why**: every setting is checked before QuickBuilder serves anything. A missing, out-of-range
or unusable value stops it rather than letting it run half-configured.

**Fix**: look the named setting up in the [configuration reference](reference/configuration.md)
(search for its name; the environment variable form uses `__` instead of `:`), correct it in
`docker-compose.yml` or `.env`, and run `docker compose up -d`. Several wrong settings are all
listed at once.

### Readiness says `WaitingForDatabase` {#waiting-for-database}

**What you see**: `/health/ready` answers `503` with `"state": "WaitingForDatabase"`.

**Why**: QuickBuilder cannot reach PostgreSQL and is retrying, for up to
[`Database:ConnectionRetryTimeout`](reference/configuration.md#database-connectionretrytimeout).

**Fix**: see [the database cannot be reached](#database-unreachable).

### The database cannot be reached {#database-unreachable}

**What you see**: `WaitingForDatabase`, then `Failed`; the log names the connection error.

**Why**: the `db` container is down, the connection string points at the wrong host, or the
password does not match the one the database volume was created with.

**Fix**:

- `docker compose ps db` must show `healthy`.
- The password in `.env` must be the one used when the `db-data` volume was first created.
  Changing `.env` later does not change the database's password.
- With your own PostgreSQL, check
  [`ConnectionStrings:Default`](reference/configuration.md#connectionstrings-default) and that
  the server accepts connections from the app container.

### Readiness says `Migrating` {#migrating}

**What you see**: `503` with `"state": "Migrating"` for a while after an upgrade.

**Why**: database migrations are being applied. Large installations take longer.

**Fix**: wait. If it stays there longer than
[`Database:MigrationTimeout`](reference/configuration.md#database-migrationtimeout), the log
says which module's migration timed out.

### Readiness says `MigrationsPending` {#migrations-pending}

**What you see**: `503` with `"state": "MigrationsPending"`; the process is up.

**Why**: [`Database:AutoMigrate`](reference/configuration.md#database-automigrate) is `false`
and the migrations of this release have not been applied.

**Fix**: `docker compose run --rm app dotnet QuickBuilder.Host.dll migrate`, then restart the
app. See [Commands](reference/commands.md#migrate).

### Readiness says `Failed` {#failed}

**What you see**: `503` with `"state": "Failed"` and a `detail`.

**Why**: startup could not finish: the database stayed unreachable, a migration failed, or
media storage is unusable. The container stays up so you can read why.

**Fix**: the `detail` and the log say which. Fix the cause and restart the app.

### Readiness says `Incompatible` {#incompatible}

**What you see**: every address answers `503`; readiness says `"state": "Incompatible"`.

**Why**: the database belongs to a different release than the one running. QuickBuilder changed
nothing. See the next two entries, and
[When QuickBuilder refuses to start](upgrade.md#when-quickbuilder-refuses-to-start).

### "Database was migrated by QuickBuilder X; this is Y" {#database-newer}

**Why**: an older release was started on a database a newer one had upgraded.

**Fix**: start release X or newer again, or restore the backup taken before the upgrade. See
[Upgrade](upgrade.md#database-newer).

### "… upgrades from F or newer" {#database-too-old}

**Why**: the database is too old for this release to upgrade directly.

**Fix**: start release F first, then this one. See [Upgrade](upgrade.md#database-too-old).

## Sites and the admin

### A host name shows the not-found page {#host-shows-not-found}

**What you see**: a plain `404` on a site's address.

**Why**, in order of likelihood:

- no site holds that host name, or the site is disabled (both answer the same `404` on
  purpose);
- the reverse proxy does not pass the visitor's host name: it must send `Host`, or
  `X-Forwarded-Host` from a proxy named in
  [`Hosting:KnownProxies`](reference/configuration.md#hosting-knownproxies);
- the page at that address is not published.

**Fix**: check the site's host names under **Sites** in the admin, then the proxy
([Reverse proxy and TLS](reverse-proxy/index.md#checks-and-common-mistakes)). A site's own
not-found page (in the site's design) means the host name works and the page is missing or
unpublished.

### The admin is unreachable on a host name {#admin-unreachable}

**What you see**: `/admin` answers like a missing page.

**Why**: [`Hosting:AdminHostNames`](reference/configuration.md#hosting-adminhostnames) is set,
and the admin only answers on those host names.

**Fix**: use one of those host names. If none of them reaches the server any more, remove the
setting (or correct it) and restart.

### Signing in is refused over HTTP {#sign-in-refused-over-http}

**What you see**: sign-in fails with a message that credentials must be sent over HTTPS.

**Why**: QuickBuilder refuses passwords sent over plain HTTP from other machines. Behind a
reverse proxy it can only see that the visitor used HTTPS when the proxy is trusted.

**Fix**: use the `https://` address, and name the proxy in
[`Hosting:KnownProxies`](reference/configuration.md#hosting-knownproxies) or
[`Hosting:KnownNetworks`](reference/configuration.md#hosting-knownnetworks). Turning on
[`Identity:AllowInsecureHttp`](reference/configuration.md#identity-allowinsecurehttp) is safe
only while the port is bound to `127.0.0.1`.

### Sign-in is throttled for everyone {#sign-in-throttled}

**What you see**: members are told to wait before signing in, although they did nothing wrong.

**Why**: behind an unnamed proxy every request appears to come from the proxy's address, so a
few failed attempts block everyone.

**Fix**: name the proxy (as above). Throttling then counts each visitor's real address. A
blocked address is released after
[`Identity:SignIn:ClientBlockDuration`](reference/configuration.md#identity-signin-clientblockduration).

### Pages show old content {#stale-content}

**What you see**: a change is not visible on the site.

**Why**: QuickBuilder's own cache never serves stale pages; it shows what is **published**.
Either the change is saved as a draft only, or something in front of QuickBuilder (a CDN, a
caching proxy) is keeping an old copy.

**Fix**: publish the page, component or design (drafts are invisible to visitors). Make sure a
CDN or proxy respects `Cache-Control: no-cache` and the `ETag` QuickBuilder sends. See
[Content and publishing](operations/content.md#caching-and-memory).

## Uploads and email

### Uploads or imports are rejected {#upload-rejected}

**What you see**: uploads fail with `413`, or a message that the file is too large or not an
allowed type.

**Why**: the reverse proxy's request size limit is lower than QuickBuilder's, or the file is
larger than [`Media:MaxFileSizeBytes`](reference/configuration.md#media-maxfilesizebytes), or
its type is not in [`Media:AllowedTypes`](reference/configuration.md#media-allowedtypes).
Site archives have their own limit,
[`SiteTransfer:MaxUploadSizeBytes`](reference/configuration.md#sitetransfer-maxuploadsizebytes).

**Fix**: a `413` page that does not look like QuickBuilder's comes from the proxy: raise its
limit (see [Request size limits](reverse-proxy/index.md#request-size-limits)). Otherwise raise
the QuickBuilder setting, within its allowed values.

### Email is not sent {#email-not-sent}

**What you see**: form notifications do not arrive, or a submission shows its notification
as failed.

**Why**: email is off ([`Email:Smtp:Host`](reference/configuration.md#email-smtp-host) empty),
or the mail server refuses or cannot be reached.

**Fix**: as an Administrator, open **Settings** and choose **Send test email**. It shows the
mail server's reply when it refuses (`5xx` permanent, `4xx` temporary). A failed submission
shows the reply too. See [Forms and email](operations/forms.md#setting-up-email).

## Logs and health

**Logs** are JSON lines on standard output:

```bash
docker compose logs --tail 200 app
docker compose logs -f app   # follow
```

Every entry carries a `TraceId`. An error the admin shows includes the same id, so
`docker compose logs app | grep <traceId>` finds what happened. Logs never contain passwords,
secrets, personal data or content.

**Health**: `/health/live` answers `200` whenever the process runs; `/health/ready` answers
`200` only when the installation should receive traffic, and otherwise `503` with a `state`
and `detail`. See [Health endpoints](reference/health.md).

## Asking for help

Collect, before you ask:

- the release (the first log line, `QuickBuilder <version> (<commit>)`);
- the output of `curl -s http://127.0.0.1:8080/health/ready`;
- the log lines around the problem, with their `TraceId`;
- the settings you changed from their defaults, **with secret values removed**.

Never share passwords, connection strings, keys, `.env`, database dumps, site content or
anything that identifies your members or visitors. To report a security problem, follow
[Report a vulnerability](security-checklist.md#report-a-vulnerability) instead of a public
issue.
