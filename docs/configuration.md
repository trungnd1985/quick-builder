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

**Trusting nothing is the default and it matters.** Sites are resolved from the `Host`
header, so a spoofable `X-Forwarded-Host` would let a caller choose which site they are
talking to. If you put a proxy in front, name it.

## Admin

| Key | Default | Meaning |
|---|---|---|
| `Admin:PathPrefix` | `/admin` | Where the admin interface is served. Reserved ahead of the public catch-all. |

⚠️ **This feature ships no authentication.** The reference `docker-compose.yml` publishes
the app on `127.0.0.1` for that reason. Do not widen that binding until authentication
ships (issue #2).

## Outbox

| Key | Default | Meaning |
|---|---|---|
| `Outbox:Enabled` | `true` | Run the background dispatcher. |
| `Outbox:PollInterval` | `00:00:05` | How often an idle dispatcher looks for work. |
| `Outbox:BatchSize` | `50` | Rows claimed per pass. |
| `Outbox:HandlerTimeout` | `00:00:30` | Per-event handler timeout. Handlers run inside the claim transaction, so a slow one holds a row lock and a connection for this long. |
| `Outbox:MaxAttempts` | `5` | Attempts before an event is set aside as failed. |
| `Outbox:RetentionPeriod` | `7.00:00:00` | How long completed events are kept before the sweep removes them. |

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
