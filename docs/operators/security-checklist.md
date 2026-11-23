# Security checklist

QuickBuilder's defaults are the secure ones, but some protection depends on choices only you
make: how the server is reached, where secrets live, who can read backups. Work through this
list before an installation goes live, and again after changing how it is hosted. Each item
says what to check, how to verify it on the running installation, and where the fix is.

## HTTPS in front of QuickBuilder {#tls}

**Check**: every public and admin address is served over HTTPS by a reverse proxy, and plain
HTTP redirects to HTTPS.

**Verify**: `curl -sI http://<your host name>/` answers `301` or `308` to `https://`, and
`curl -sI https://<your host name>/` answers with a valid certificate.

**Fix**: [Reverse proxy and TLS](reverse-proxy/index.md). QuickBuilder does not terminate TLS
itself.

## The proxy is named, and only the proxy {#trusted-proxies}

**Check**: [`Hosting:KnownProxies`](reference/configuration.md#hosting-knownproxies) or
[`Hosting:KnownNetworks`](reference/configuration.md#hosting-knownnetworks) names your proxy,
and nothing broader than it. A network such as `0.0.0.0/0` would let anyone choose the host
name and client address QuickBuilder sees.

**Verify**: sign in through the proxy, then read the latest sign-in from the security events.
It must show your own public address, not the proxy's:

```bash
docker compose exec -T db psql -U quickbuilder -d quickbuilder -c \
  "SELECT occurred_at_utc, outcome, client_address FROM identity.security_events WHERE event_type = 'sign_in' ORDER BY occurred_at_utc DESC LIMIT 3"
```

**Fix**: set the proxy's address or its network; see
[What a proxy must do](reverse-proxy/index.md#trust-the-proxy).

## Plain HTTP sign-in is off {#plain-http-sign-in}

**Check**: [`Identity:AllowInsecureHttp`](reference/configuration.md#identity-allowinsecurehttp)
is `false` once the port is reachable from other machines. The reference compose file turns
it on because, inside Docker, requests from your own machine do not arrive from loopback; that
is safe only while the port stays bound to `127.0.0.1`.

**Verify**: `docker compose logs app | grep "Identity:AllowInsecureHttp is enabled"` finds
nothing after the latest start.

**Fix**: set `Identity__AllowInsecureHttp: "false"` in `docker-compose.yml` and restart.

## The admin answers only on your host name {#admin-host-names}

**Check**: for an installation that serves client sites,
[`Hosting:AdminHostNames`](reference/configuration.md#hosting-adminhostnames) is set to a host
name only you use, so client domains never show the sign-in page.

**Verify**: `https://<a client's host name>/admin` answers like a missing page, and
`https://<your admin host name>/admin` shows the sign-in page.

**Fix**: set the setting and restart; see
[Troubleshooting](troubleshooting.md#admin-unreachable) if you lock yourself out.

## Secrets stay out of files you share {#secrets}

**Check**: the database password and every setting marked **Secret** in the
[configuration reference](reference/configuration.md) live in `.env` (readable only by you) or
a secret store, never in `docker-compose.yml`, a repository or a ticket. The placeholder
`change-me-before-real-use` appears nowhere.

**Verify**: `grep -r change-me-before-real-use .` in the installation directory finds nothing,
and `ls -l .env` shows `-rw-------`.

**Fix**: [Configure → Secrets](configure.md#secrets). To change the database password, change
it in PostgreSQL first (`ALTER USER quickbuilder PASSWORD '…'`), then in `.env`.

## The database is not reachable from outside {#database-not-exposed}

**Check**: PostgreSQL has no published port. Only the app reaches it, over the Compose network.

**Verify**: `docker compose port db 5432` prints nothing, and `ss -ltn` on the server shows
nothing listening on `5432` on a public address.

**Fix**: remove any `ports:` section from the `db` service.

## Media storage is private {#media-not-exposed}

**Check**: visitors get files only through QuickBuilder (`/_media/…`). The `media-data` volume
is not served by anything else, and an S3 bucket or Azure container is private, with the
application granted only the permissions it needs.

**Verify**: with S3, `curl -sI https://<bucket endpoint>/<a file key>` answers `403`; the
bucket has no public policy or public ACL.

**Fix**: [Media storage → Choosing a provider](operations/media.md#choosing-a-provider) lists
the permissions to grant.

## Backups exist, are protected and restore {#backups}

**Check**: a full backup runs at least daily, is kept off the server, is encrypted at rest,
and has been restored at least once.

**Verify**: the newest backup is less than a day old and passes
[Verify a backup](backup-restore.md#verify-a-backup).

**Fix**: [Back up and restore](backup-restore.md).

## You will hear about updates {#updates}

**Check**: someone watches for new releases and applies patch releases promptly; they carry
security fixes. The image tag in `docker-compose.yml` is an exact version you chose, not
`latest`.

**Verify**: **Settings → About this installation** shows the latest patch of your release line
(compare with the [upgrade notes](upgrade-notes.md)).

**Fix**: [Upgrade](upgrade.md). Watch the project's releases on GitHub to be notified.

## Logs hold no personal data, and stay private {#personal-data-in-logs}

**Check**: QuickBuilder never writes passwords, secrets, form contents or content bodies to
its logs, and public page addresses are kept out of them. Keep it that way: do not raise
`Serilog:MinimumLevel` overrides to `Debug` for `Microsoft` components on a live installation,
and send logs only to a collector you control. Until setup is finished, the logs hold the
setup code.

**Verify**: [`Serilog:MinimumLevel:Default`](reference/configuration.md#serilog-minimumlevel-default)
is `Information` or higher, and no `Override` is set to `Debug` or `Verbose`.

**Fix**: remove the overrides; see [Logging](reference/configuration.md#logging).

## Outbound features are off unless you need them {#outbound-features}

**Check**: QuickBuilder makes no outbound connections by default. Each of these is on only if
you need it: telemetry export
([`Telemetry:OtlpEndpoint`](reference/configuration.md#telemetry-otlpendpoint)), email
([`Email:Smtp:Host`](reference/configuration.md#email-smtp-host)), and a remote storage
provider ([`Storage:Provider`](reference/configuration.md#storage-provider)).

**Verify**: `Telemetry__OtlpEndpoint` is empty or points at your own collector;
`Email__Smtp__Security` is not `None` for a mail server on another machine.

**Fix**: clear the settings you do not use and restart.

## Report a vulnerability {#report-a-vulnerability}

Please do not report security problems in public issues. Report them privately as described
in the project's security policy, `SECURITY.md` at the root of the repository and of every
release's source download.
